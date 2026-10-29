namespace Rts.Movement.Prototype;

/// <summary>Owns bounded same-task cooperation; goals remain owned by RoutePlacements.</summary>
internal static class RouteYielding
{
    internal static bool Eligible(Walker body, ulong task) => body.Task == task
        && body.Phase == WalkPhase.Settled && body.Pending.Count == 0;

    internal static bool CanParticipate(Walker body, ulong task) => body.Task == task
        && body.Phase is WalkPhase.Settled or WalkPhase.Waiting && body.Pending.Count == 0;

    internal static bool TryStart(RouteWorld world, Walker requester)
    {
        var work = 0;
        return TryStart(world, requester, ref work);
    }

    internal static bool TryStart(RouteWorld world, Walker requester, ref int work)
    {
        if (work >= 2048) return false;
        var observation = new RouteYieldObservation(world);
        return TryStart(world, requester, observation, allowWaiting: false, ref work)
            || work < 2048 && TryStart(world, requester, observation, allowWaiting: true, ref work)
            || work < 2048 && RouteTemporaryYield.TryStart(world, requester, observation, ref work);
    }

    private static bool TryStart(RouteWorld world, Walker requester, RouteYieldObservation observation, bool allowWaiting, ref int work)
    {
        if (requester.Recoveries >= RouteSettings.RecoveryLimit || requester.Blocker is not { } id
            || world.Routes[requester.Task].Yield is not null) return false;
        var root = world.Bodies[id];
        bool Available(Walker body) => body.Id != requester.Id
            && (Eligible(body, requester.Task) || allowWaiting && CanParticipate(body, requester.Task));
        if (!Available(root)) return false;
        var (_, followTarget, _) = RouteNavigation.Follow(world, requester);
        var passage = RouteRequesterPassage.Predict(world, requester, followTarget, reachTarget: false);
        if (passage.Trace[^1] == requester.Position) return false;
        var candidates = RoutePlanning.Candidates(world, root.Preference, 156).ToArray();
        foreach (var point in candidates)
            if (RoutePlacements.TryBeginYield(world, requester, observation, passage, [root.Id], [point])) return true;
        var claims = observation.Claims;
        var secondaryCandidates = new Dictionary<ulong, FixedVector2[]>();
        foreach (var point in candidates)
        {
            if (!world.Navigation.CanStandAt(point, 31) || (point - root.Position).Length < 1) continue;
            // Only the concrete body/claim obstructing this candidate may be the second participant.
            var obstacles = world.Bodies.Values.Where(body => body.Id != root.Id
                && (RouteYieldGeometry.Overlap(root.Position, point, 31, body.Position, body.Position, 31)
                    || RouteYieldGeometry.GoalOverlap(body.Position, point))).Select(body => body.Id)
                .Concat(claims.Where(claim => claim.Task == root.Task && claim.Member != root.Id
                    && RouteYieldGeometry.GoalOverlap(claim.Point, point)).Select(claim => claim.Member)).Distinct().ToArray();
            if (obstacles.Length != 1 || !Available(world.Bodies[obstacles[0]])) continue;
            var second = world.Bodies[obstacles[0]];
            if (!secondaryCandidates.TryGetValue(second.Id, out var alternatives))
                secondaryCandidates.Add(second.Id, alternatives = RoutePlanning.Candidates(world, second.Preference, 156).ToArray());
            foreach (var alternative in alternatives)
            {
                // A local proof has a fixed cap; this is not unbounded joint traffic search.
                if (++work > 2048) return false;
                if (RoutePlacements.TryBeginYield(world, requester, observation, passage, [second.Id, root.Id], [alternative, point])) return true;
            }
        }
        return false;
    }

    internal static void Reconcile(RouteWorld world)
    {
        foreach (var source in world.Routes.Values.Where(route => route.Yield is not null).ToArray())
        {
            var plan = source.Yield!;
            var valid = world.Bodies.TryGetValue(plan.Requester, out var requester)
                && requester.Task == source.Id
                && (requester.Phase is WalkPhase.Following or WalkPhase.Waiting
                    || requester.Phase == WalkPhase.Settled && requester.Pending.Count == 0)
                && plan.MapRevision == world.Revision && world.Frame - plan.StartedAt < 90
                && plan.Members.All(id => world.Bodies[id].Task == source.Id && world.Bodies[id].Pending.Count == 0);
            if (!valid)
            {
                Cancel(world, source);
                continue;
            }
            if (plan.Temporary is not null)
            {
                RouteTemporaryYield.Reconcile(world, source);
                continue;
            }
            var active = world.Bodies[plan.Members[plan.Next]];
            if (active.Phase != WalkPhase.Settled || active.Position != active.Endpoint) continue;
            var next = plan.Next + 1;
            if (next == plan.Members.Count)
            {
                world.Routes[source.Id] = source with { Yield = null };
                world.Publish(plan.Requester, "yield_finished");
            }
            else
            {
                var body = world.Bodies[plan.Members[next]];
                world.Bodies[body.Id] = body with { Phase = WalkPhase.Yielding };
                world.Routes[source.Id] = source with { Yield = plan with { Next = next } };
                world.Publish(body.Id, "yield_leg_started");
            }
        }
    }

    private static void Cancel(RouteWorld world, RouteTask task)
    {
        foreach (var id in task.Yield!.Members)
        {
            var body = world.Bodies[id];
            if (body.Task != task.Id) continue; // Stop/new orders already own this body.
            world.Bodies[id] = body with { Phase = body.Position == body.Endpoint ? WalkPhase.Settled : WalkPhase.Following,
                Connection = null, Blocker = null, Velocity = default, Recovery = null, BlockingContacts = null };
        }
        world.Routes[task.Id] = task with { Yield = null };
        world.Publish(task.Yield.Requester, "yield_cancelled");
    }
}
