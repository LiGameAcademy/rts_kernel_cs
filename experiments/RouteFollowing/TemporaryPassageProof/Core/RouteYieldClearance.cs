namespace Rts.Movement.Prototype;

/// <summary>Rejects static gathering placements that leave no progress through existing local proposals.</summary>
internal static class RouteYieldClearance
{
    internal static bool AllowsPlacement(RouteWorld world, Walker source, ulong[] members, FixedVector2[] targets,
        RouteYieldObservation? observation = null)
    {
        // A fixed-target, stationary-body proof cannot reject moving peers or terrain detours.
        // Those cases retain the existing timed execution and cooperation rules.
        if (source.Navigation is not { Kind: MemberReferenceKind.Direct }
            || world.Bodies.Values.Any(body => body.Task == source.Task && body.Id != source.Id
                && !members.Contains(body.Id) && body.Phase != WalkPhase.Settled)) return true;
        return observation is null ? CanAdvance(world, source, members, targets)
            : observation.CanAdvance(world, source, members, targets);
    }

    internal static bool CanAdvance(RouteWorld world, Walker source, ulong[] members, FixedVector2[] targets)
        => new RouteYieldTrial(world.Bodies.Values).CanAdvance(world, source, members, targets);

    internal static bool Simulate(RouteWorld world, Walker source, CrowdReservations reservations)
    {
        var corridors = new RouteYieldCorridors(world);
        var (body, target, _) = RouteNavigation.Follow(world, source);
        for (var tick = 0; tick < RouteSettings.WaitBeforeRetry; tick++)
        {
            var step = RouteMotionCandidates.Compute(world, body, target, RouteSettings.Speed);
            var blocker = reservations.FirstMotionConflict(new(new(body.Id), RouteSettings.Radius, step.Trace, step.Times));
            var passage = body.Passage;
            if (blocker is { } id && !RouteMotionCandidates.TryAlternative(world, new(body, target, step), id.Value,
                reservations, corridors, out step, out passage)) return false;
            if (step.Position == body.Position) return false;
            body = body with
            {
                Position = step.Position,
                Facing = step.Facing,
                Velocity = (step.Position - body.Position) * RouteSettings.TickRate,
                Passage = passage
            };
            if (body.Position == source.Endpoint
                || RouteReference.Project(body.Position, source.Position, source.Endpoint) >= RouteSettings.ProgressThreshold)
                return true;
            reservations.Put(CrowdBody.Stationary(new(body.Id), RouteSettings.Radius, body.Position));
        }
        return false;
    }
}
