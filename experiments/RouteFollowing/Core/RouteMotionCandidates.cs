namespace Rts.Movement.Prototype;

internal sealed record MotionProposal(Walker Body, FixedVector2 Target, MovementStep Step);

/// <summary>Frame-local alternatives never replace a member's navigation reference or endpoint.</summary>
internal static class RouteMotionCandidates
{
    internal static PassageState? RetainPassage(RouteWorld world, Walker body)
    {
        if (body.Passage is not { } passage
            || !world.Bodies.TryGetValue(passage.Blocker, out var other)) return null;
        // One conflict-free tick is not a completed encounter. Release beyond the
        // bodies' contact range plus one existing pathing cell, without a timer.
        var reach = RouteSettings.Radius * 2 + world.Navigation.Grid.CellSize;
        return SquaredDistance.Between(body.Position, other.Position).Raw <= (Int128)reach.Raw * reach.Raw
            ? passage : null;
    }

    internal static MovementStep Compute(RouteWorld world, Walker body, FixedVector2 target, SimScalar speed)
    {
        if (StraightMovePath.TryFind(world.Navigation, body.Position, target, 1) is null)
            return new(body.Position, body.Facing, 0, [body.Position, body.Position], Times: [0, 1]);
        return MovementStepper.Compute(new(new(body.Id), 0, body.Position, body.Velocity, body.Facing),
            new(new(target, speed, 1, RouteSettings.Motion), new FixedVector2[] { body.Position, target }), 30, null);
    }

    internal static bool TryAlternative(RouteWorld world, MotionProposal proposal, ulong blocker,
        CrowdReservations reservations, RouteYieldCorridors corridors, out MovementStep step, out PassageState? passage)
    {
        var body = proposal.Body;
        passage = body.Passage?.Blocker == blocker ? body.Passage : null;
        step = Compute(world, body, proposal.Target, RouteSettings.Speed / 2);
        if (Safe(body, step, reservations, corridors)) return true;
        var heading = SimMath.Atan2(proposal.Target.Y - body.Position.Y, proposal.Target.X - body.Position.X);
        // The first safe side persists for this blocker. With no history, prefer right-hand passage.
        var sides = passage is not null ? new[] { passage.Side } : new[] { -1, 1 };
        foreach (var side in sides)
        foreach (var degrees in new[] { 30, 60, 90 })
        {
            var angle = heading + SimMath.Pi * (side * degrees) / 180;
            var reach = world.Navigation.Grid.CellSize / 2;
            var target = body.Position + new FixedVector2(SimMath.Cos(angle) * reach, SimMath.Sin(angle) * reach);
            step = Compute(world, body, target, RouteSettings.Speed);
            if (!Safe(body, step, reservations, corridors)) continue;
            passage = new(blocker, side);
            return true;
        }
        return false;
    }

    private static bool Safe(Walker body, MovementStep step, CrowdReservations reservations, RouteYieldCorridors corridors) =>
        step.Failure is null && step.Position != body.Position
        && corridors.FirstConflict(body, step.Trace) is null
        && reservations.FirstMotionConflict(new(new(body.Id), 31, step.Trace, step.Times)) is null;
}
