namespace Rts.Movement.Prototype;

internal readonly record struct RouteGoalClaim(ulong Task, ulong Member, FixedVector2 Point);

/// <summary>Sole writer of task goals. Reservations derive from active and queued intents, never a second goal store.</summary>
internal static class RoutePlacements
{
    internal static bool IsFixed(Walker body) => body.Phase is WalkPhase.Idle or WalkPhase.Stopped
        or WalkPhase.Settled or WalkPhase.Failed;

    internal static (ulong Id, ulong Task, FixedVector2 Position)[] FixedBodies(RouteWorld world) =>
        world.Bodies.Values.Where(IsFixed).Select(body => (body.Id, body.Task, body.Position)).ToArray();

    internal static Walker Initialise(Walker body, FixedVector2 preference) => body with
        { Preference = preference, Endpoint = preference, PlacementCheckedRevision = -1 };

    internal static RouteGoalClaim[] Claims(IEnumerable<Walker> bodies) => bodies.SelectMany(body =>
        body.Pending.Select(goal => new RouteGoalClaim(goal.Task, body.Id, goal.Preference))
            .Concat(body.Task == 0 ? [] : new[] { new RouteGoalClaim(body.Task, body.Id, body.Endpoint) })).ToArray();

    internal static bool TryAssign(RouteWorld world, Walker[] members, FixedVector2 goal,
        out FixedVector2 anchor, out Dictionary<ulong, FixedVector2> assigned)
    {
        anchor = goal;
        assigned = [];
        var occupied = world.Bodies.Values.Where(body => !members.Any(member => member.Id == body.Id)
            && IsFixed(body)).Select(body => body.Position).ToList();
        bool Free(FixedVector2 point) => world.Navigation.CanStandAt(point, RouteSettings.Radius)
            && occupied.All(other => (other - point).Length >= RouteSettings.Radius * 2 + 2);
        var centre = RoutePlanning.Centre(members);
        if (!world.Navigation.CanStandAt(anchor, RouteSettings.Radius))
        {
            var adjusted = RoutePlanning.Candidates(world, goal, 256).FirstOrDefault(point => Free(point));
            if (adjusted == default) return false;
            anchor = adjusted;
        }
        var slots = FormationLayout.Create(FormationKind.Compact, members.Length, 72, anchor,
            SimMath.Atan2(anchor.Y - centre.Y, anchor.X - centre.X));
        var match = FormationLayout.Match(members.Select(body => body.Position).ToArray(), slots, 0);
        for (var i = 0; i < members.Length; i++)
        {
            var preferred = slots[match[i]];
            var point = Free(preferred) ? preferred : RoutePlanning.Candidates(world, preferred, 256).FirstOrDefault(Free);
            if (point == default || !Free(point)) return false;
            if (!world.Navigation.TryWorldToCell(members[i].Position, out var start)
                || !world.Navigation.TryWorldToCell(point, out var end)
                || !world.Navigation.Components.Connected(start, end, 1)) return false;
            assigned.Add(members[i].Id, point);
            occupied.Add(point);
        }
        return true;
    }

    internal static bool TryBeginYield(RouteWorld world, Walker requester, RouteYieldObservation observation,
        ulong[] members, FixedVector2[] goals)
    {
        var task = world.Routes[requester.Task];
        if (requester.Recoveries >= RouteSettings.RecoveryLimit || task.Yield is not null || members.Length is < 1 or > 2 || goals.Length != members.Length
            || members.Distinct().Count() != members.Length || members.Contains(requester.Id) || requester.Blocker != members[^1]
            || members.Any(id => !world.Bodies.TryGetValue(id, out var body) || !RouteYielding.CanParticipate(body, task.Id))) return false;
        var root = world.Bodies[members[^1]];
        if (!RouteYieldGeometry.Overlap(requester.Position, requester.Endpoint, 31, root.Position, root.Position, 31)
            || RouteYieldGeometry.Overlap(requester.Position, requester.Endpoint, 31, goals[^1], goals[^1], 31)) return false;
        var claims = observation.Claims;
        for (var i = 0; i < members.Length; i++)
        {
            var body = world.Bodies[members[i]];
            var goal = goals[i];
            var relocated = i == 0 ? 0 : members[0];
            var replacement = i == 0 ? default : goals[0];
            if ((goal - body.Preference).Length > 156 || observation.Distance(body, goal) < 1
                || !observation.CanStand(goal)
                || claims.Any(claim => claim.Task == task.Id && !members.Contains(claim.Member) && RouteYieldGeometry.GoalOverlap(goal, claim.Point))
                || goals.Take(i).Any(other => RouteYieldGeometry.GoalOverlap(other, goal))
                || observation.BodyAtGoal(body.Id, goal, relocated, replacement)
                || !observation.CanTravel(body, goal)
                || observation.BodyOnLeg(body.Id, body.Position, goal, relocated, replacement)) return false;
        }
        // All reservations are validated before any authoritative goal changes.
        for (var i = 0; i < members.Length; i++)
        {
            var body = world.Bodies[members[i]];
            world.Bodies[body.Id] = body with { Endpoint = goals[i], Connection = null, Blocker = null, Velocity = default,
                Navigation = null, Passage = null, Recovery = null, BlockingContacts = [], Progress = 0, ProgressCursor = 0, ProgressVersion = 0,
                Phase = i == 0 ? WalkPhase.Yielding : WalkPhase.Settled, PlacementCheckedRevision = -1 };
        }
        world.Routes[task.Id] = task with { Yield = new(requester.Id, members.ToArray(), 0, world.Revision, world.Frame) };
        world.Bodies[requester.Id] = requester with { Recovery = null, RetryAtWait = requester.WaitTicks + 90, Recoveries = requester.Recoveries + 1 };
        world.Publish(requester.Id, "yield_started", string.Join(",", members));
        world.Publish(members[0], "yield_leg_started");
        return true;
    }

    internal static void Refresh(RouteWorld world)
    {
        var claims = Claims(world.Bodies.Values);
        var fixedBodies = world.Bodies.Values.Where(IsFixed).ToArray();
        foreach (var source in world.Bodies.Values.Where(body => body.Task != 0
            && body.Phase is WalkPhase.Following or WalkPhase.Waiting).ToArray())
        {
            if (source.PlacementCheckedRevision == world.PlacementRevision) continue;
            var body = source with { PlacementCheckedRevision = world.PlacementRevision };
            var terrainInvalid = !world.Navigation.CanStandAt(body.Endpoint, 31);
            if (terrainInvalid) body = body with { Connection = null };
            bool FixedFree(FixedVector2 point) => fixedBodies.All(other => other.Id == body.Id
                || other.Task == body.Task || (other.Position - point).Length >= 64);
            if (!terrainInvalid && FixedFree(body.Endpoint))
            {
                world.Bodies[body.Id] = body;
                continue;
            }
            bool Free(FixedVector2 point) => world.Navigation.CanStandAt(point, 31) && FixedFree(point)
                && claims.All(claim => claim.Task != body.Task || claim.Member == body.Id || (claim.Point - point).Length >= 64)
                && world.Navigation.TryWorldToCell(body.Position, out var start)
                && world.Navigation.TryWorldToCell(point, out var end)
                && world.Navigation.Components.Connected(start, end, 1);
            var found = false;
            foreach (var point in RoutePlanning.Candidates(world, body.Preference, 156))
            {
                if (!Free(point)) continue;
                // Commit the replacement and its reservation together; remaining
                // members see this claim before choosing another replacement.
                var index = Array.FindIndex(claims, claim => claim.Task == body.Task && claim.Member == body.Id);
                claims[index] = new(body.Task, body.Id, point);
                body = body with { Endpoint = point, Connection = null, Recovery = null, BlockingContacts = null };
                found = true;
                break;
            }
            world.Bodies[body.Id] = body;
            world.Publish(body.Id, found ? "goal_reassigned" : "goal_repair_blocked",
                terrainInvalid ? "terrain_invalid" : "fixed_body_occupied");
        }
    }
}
