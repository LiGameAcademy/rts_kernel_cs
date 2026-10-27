namespace Rts.Movement.Prototype;

/// <summary>Bounded temporary clearance without replacing task-owned final goals.</summary>
internal static class RouteTemporaryYield
{
    internal static bool TryStart(RouteWorld world, Walker requester, ref int work)
    {
        if (requester.Recoveries >= RouteSettings.RecoveryLimit || requester.Blocker is not { } rootId
            || world.Routes[requester.Task].Yield is not null) return false;
        var root = world.Bodies[rootId];
        if (!RouteYielding.CanParticipate(root, requester.Task)) return false;
        var (_, followTarget, _) = RouteNavigation.Follow(world, requester);
        var requested = RouteMotionCandidates.Compute(world, requester, followTarget, RouteSettings.Speed);
        if (!Blocked(requester.Position, requested.Position, root.Position)) return false;
        var clearance = Clearance(world, requester, followTarget);
        var candidates = RoutePlanning.Candidates(world, root.Position, 156).ToArray();
        foreach (var target in candidates)
            if (TryAdmit(world, requester, followTarget, clearance, [root.Id], [target])) return true;
        // Existing occupied body centres are semantic hand-off positions, not extra angular samples.
        // Moving the one obstructing body first can expose a narrow passage between packed neighbours.
        var helpers = world.Bodies.Values.Where(body => body.Id != requester.Id && body.Id != root.Id
            && RouteYielding.CanParticipate(body, requester.Task) && (body.Position - root.Position).Length <= 156).ToArray();
        foreach (var target in candidates.Concat(helpers.Select(body => body.Position)).Distinct())
        {
            var obstacles = world.Bodies.Values.Where(body => body.Id != root.Id
                && (Blocked(root.Position, target, body.Position) || (body.Position - target).Length < 64)).ToArray();
            if (obstacles.Length != 1 || !helpers.Any(body => body.Id == obstacles[0].Id)) continue;
            var helper = obstacles[0];
            foreach (var alternative in RoutePlanning.Candidates(world, helper.Position, 156))
            {
                if (++work > 2048) return false;
                if (TryAdmit(world, requester, followTarget, clearance, [helper.Id, root.Id], [alternative, target])) return true;
            }
        }
        return false;
    }

    private static bool TryAdmit(RouteWorld world, Walker requester, FixedVector2 followTarget, IReadOnlyList<FixedVector2> clearance,
        ulong[] members, FixedVector2[] targets)
    {
        if (targets.Any(target => clearance.Zip(clearance.Skip(1)).Any(edge => Blocked(edge.First, edge.Second, target)))) return false;
        var positions = world.Bodies.Values.ToDictionary(body => body.Id, body => body.Position);
        for (var index = 0; index < members.Length; index++)
        {
            var body = world.Bodies[members[index]];
            var target = targets[index];
            if ((target - body.Position).Length is var distance && (distance < 1 || distance > 156)
                || !world.Navigation.CanStandAt(target, 31)
                || StraightMovePath.TryFind(world.Navigation, body.Position, target, 1) is null
                || positions.Any(other => other.Key != body.Id
                    && ((other.Value - target).Length < 64 || Blocked(body.Position, target, other.Value)))) return false;
            positions[body.Id] = target;
        }
        var task = world.Routes[requester.Task];
        var origins = members.Select(id => world.Bodies[id].Position).ToArray();
        var plan = new RouteYieldPlan(requester.Id, members, 0, world.Revision, world.Frame,
            new(targets, origins, requester.Position, followTarget));
        foreach (var id in members)
        {
            var body = world.Bodies[id];
            // Final reservation, preference and progress remain the member's original task state.
            world.Bodies[id] = body with { Phase = id == members[0] ? WalkPhase.Yielding : WalkPhase.YieldHolding,
                Blocker = null, Passage = null, Connection = null, Velocity = default, Recovery = null, BlockingContacts = [] };
        }
        world.Routes[task.Id] = task with { Yield = plan };
        world.Bodies[requester.Id] = requester with { Recovery = null, RetryAtWait = requester.WaitTicks + 90,
            Recoveries = requester.Recoveries + 1 };
        world.Publish(requester.Id, "yield_started", string.Join(",", members));
        world.Publish(requester.Id, "temporary_passage_started");
        return true;
    }

    internal static FixedVector2 Target(RouteWorld world, Walker body)
    {
        var plan = world.Routes[body.Task].Yield;
        return plan?.Temporary is { } temporary ? temporary.Targets[plan.Next] : body.Endpoint;
    }

    internal static IEnumerable<(ulong Member, FixedVector2 Target)> Remaining(RouteWorld world, RouteYieldPlan plan)
    {
        if (plan.Temporary?.Phase == TemporaryYieldPhase.Passing) yield break;
        var order = Enumerable.Range(plan.Next, plan.Members.Count - plan.Next);
        foreach (var index in order)
            yield return (plan.Members[index], plan.Temporary?.Phase == TemporaryYieldPhase.Outward
                ? plan.Temporary.Targets[index] : world.Bodies[plan.Members[index]].Endpoint);
    }

    internal static void Reconcile(RouteWorld world, RouteTask task)
    {
        var plan = task.Yield!;
        var temporary = plan.Temporary!;
        if (temporary.Phase == TemporaryYieldPhase.Passing)
        {
            // Resume the original tasks after real passage, rather than requiring a complete
            // straight return to distant final goals before releasing the temporary plan.
            var requester = world.Bodies[plan.Requester];
            if (requester.Position != requester.Endpoint
                && Advance(temporary.RequesterStart, temporary.RequesterTarget, requester.Position) < 32) return;
            foreach (var id in plan.Members)
            {
                var body = world.Bodies[id];
                world.Bodies[id] = body with { Phase = body.Position == body.Endpoint ? WalkPhase.Settled : WalkPhase.Following,
                    Blocker = null, Passage = null, Recovery = null, BlockingContacts = [], Velocity = default };
            }
            world.Routes[task.Id] = task with { Yield = null };
            world.Publish(plan.Requester, "yield_finished");
            return;
        }
        var active = world.Bodies[plan.Members[plan.Next]];
        if (active.Phase != WalkPhase.YieldHolding || active.Position != Target(world, active)) return;
        var next = plan.Next + 1;
        if (next == plan.Members.Count)
        {
            world.Routes[task.Id] = task with { Yield = plan with {
                Temporary = temporary with { Phase = TemporaryYieldPhase.Passing } } };
            world.Publish(plan.Requester, "temporary_passage_open");
        }
        else
        {
            world.Routes[task.Id] = task with { Yield = plan with { Next = next } };
            var body = world.Bodies[plan.Members[next]];
            world.Bodies[body.Id] = body with { Phase = WalkPhase.Yielding, Passage = null };
        }
    }

    private static IReadOnlyList<FixedVector2> Clearance(RouteWorld world, Walker body, FixedVector2 target)
    {
        var start = body.Position;
        var trace = new List<FixedVector2> { start };
        // Predict only the existing progress threshold, using the real turn/step rule.
        // This is bounded by the unchanged cooperation deadline, not a sampled search.
        for (var tick = 0; tick < 90 && body.Position != target && Advance(start, target, body.Position) < 32; tick++)
        {
            var step = RouteMotionCandidates.Compute(world, body, target, RouteSettings.Speed);
            trace.AddRange(step.Trace.Skip(1));
            if (step.Position == body.Position) break;
            body = body with { Position = step.Position, Facing = step.Facing,
                Velocity = (step.Position - body.Position) * RouteSettings.TickRate };
        }
        return trace;
    }

    private static SimScalar Advance(FixedVector2 start, FixedVector2 target, FixedVector2 point)
    {
        return start == target ? 0 : RouteReference.Project(point, start, target);
    }

    private static bool Blocked(FixedVector2 from, FixedVector2 to, FixedVector2 body) =>
        CrowdGeometry.Overlap(from, to, 31, body, body, 31);
}
