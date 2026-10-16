namespace Rts.Kernel.Navigation;

/// <summary>Short legal sidesteps let a body leave a crowded cell before grid detouring is possible.</summary>
internal static class LocalSidestep
{
    internal static MoveOrder? Find(NavigationState navigation,
        CrowdReservations reservations, EntityState entity, MovementDefinition definition,
        MoveOrder order, int tickRate)
    {
        if (!navigation.TryWorldToCell(entity.Position, out var start)) return null;
        var target = order.Waypoints[^1];
        if (target == entity.Position && order.NextWaypoint + 1 < order.Waypoints.Count)
            target = order.Waypoints[order.NextWaypoint + 1];
        var heading = SimMath.Atan2(target.Y - entity.Position.Y, target.X - entity.Position.X);
        foreach (var degrees in new[] { 30, -30, 60, -60, 90, -90 })
        {
            var angle = heading + SimMath.Pi * degrees / 180;
            var reach = navigation.Grid.CellSize / 2;
            var point = new FixedVector2(entity.Position.X + SimMath.Cos(angle) * reach,
                entity.Position.Y + SimMath.Sin(angle) * reach);
            if (!navigation.TryWorldToCell(point, out var cell)
                || !navigation.CanTraverse(start, cell, order.Request.ClearanceCells)) continue;
            var reconnect = StraightMovePath.TryFind(navigation, point, target, order.Request.ClearanceCells);
            if (reconnect is null) continue;
            var points = new List<FixedVector2> { entity.Position };
            points.AddRange(reconnect);
            var alternative = new MoveOrder(order.Request, points.AsReadOnly(), WaitFrames: order.WaitFrames,
                RetryAfterFrame: order.RetryAfterFrame);
            var step = MovementStepper.Compute(entity, alternative, tickRate, navigation.Terrain);
            if (step.Failure is null && step.Position != entity.Position
                && reservations.FirstMotionConflict(new(entity.Id, definition.Radius, step.Trace, step.Times)) is null)
                return alternative;
        }
        return null;
    }
}
