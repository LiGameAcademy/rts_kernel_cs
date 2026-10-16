namespace Rts.Kernel.Navigation;

/// <summary>Searches a bounded free endpoint near the preferred slot when the exact approach is blocked.</summary>
internal static class SoftGoalPath
{
    internal static SimScalar Tolerance(SimScalar radius, SimScalar cellSize) => SimMath.Min(radius * 4 + cellSize, cellSize * LocalDetour.RadiusCells);

    internal static MoveOrder? Find(NavigationState navigation, CrowdReservations reservations,
        EntityState entity, MovementDefinition definition, MoveOrder order)
    {
        var tolerance = Tolerance(definition.Radius, navigation.Grid.CellSize);
        if (SquaredDistance.Between(entity.Position, order.Request.Goal).Length > navigation.Grid.CellSize * LocalDetour.RadiusCells
            || !navigation.TryWorldToCell(entity.Position, out var start)
            || !navigation.TryWorldToCell(order.Request.Goal, out var goal)) return null;
        var span = Math.Min(LocalDetour.RadiusCells, checked((int)(tolerance.Raw / navigation.Grid.CellSize.Raw) + 1));
        var candidates = new List<FixedVector2>();
        var distance = SquaredDistance.Between(entity.Position, order.Request.Goal).Length;
        var fraction = distance <= tolerance ? SimScalar.One : tolerance / distance;
        var nearest = new FixedVector2(order.Request.Goal.X + (entity.Position.X - order.Request.Goal.X) * fraction,
            order.Request.Goal.Y + (entity.Position.Y - order.Request.Goal.Y) * fraction);
        if (navigation.CanStandAt(nearest, definition.Radius)
            && reservations.Safe(entity.Id, definition.Radius, nearest, nearest)) candidates.Add(nearest);
        for (var y = goal.Y - span; y <= goal.Y + span; y++)
        for (var x = goal.X - span; x <= goal.X + span; x++)
        {
            var cell = new GridCell(x, y);
            if (!navigation.CanOccupy(cell, order.Request.ClearanceCells)) continue;
            var point = navigation.CellCenter(cell);
            if (SquaredDistance.Between(point, order.Request.Goal).Length > tolerance
                || !reservations.Safe(entity.Id, definition.Radius, point, point)) continue;
            candidates.Add(point);
        }
        var remaining = LocalDetour.NodeBudget;
        foreach (var point in candidates.OrderBy(point => SquaredDistance.Between(entity.Position, point).Raw)
                     .ThenBy(point => point.Y).ThenBy(point => point.X))
        {
            var direct = StraightMovePath.TryFind(navigation, entity.Position, point, order.Request.ClearanceCells);
            if (direct is not null && reservations.Safe(entity.Id, definition.Radius, direct))
                return new(order.Request, direct, WaitFrames: order.WaitFrames, RetryAfterFrame: order.RetryAfterFrame);
            navigation.TryWorldToCell(point, out var target);
            bool CanUse(GridCell cell) => navigation.CanOccupy(cell, order.Request.ClearanceCells)
                && (cell == start || reservations.Safe(entity.Id, definition.Radius,
                    navigation.CellCenter(cell), navigation.CellCenter(cell)));
            bool Edge(GridCell from, GridCell to) => navigation.CanTraverse(from, to, order.Request.ClearanceCells)
                && reservations.Safe(entity.Id, definition.Radius,
                    from == start ? entity.Position : navigation.CellCenter(from), navigation.CellCenter(to));
            var left = Math.Max(0, start.X - LocalDetour.RadiusCells);
            var top = Math.Max(0, start.Y - LocalDetour.RadiusCells);
            var area = new GridArea(left, top,
                Math.Min(navigation.Grid.Width - left, start.X + LocalDetour.RadiusCells - left + 1),
                Math.Min(navigation.Grid.Height - top, start.Y + LocalDetour.RadiusCells - top + 1));
            if (remaining <= 0) break;
            var path = GridPathfinder.FindPathCore(navigation.Grid, start, target, remaining, 0, CanUse, Edge, area);
            remaining -= path.ExpandedNodes;
            if (path.Status == PathStatus.Found)
            {
                var points = new List<FixedVector2> { entity.Position };
                points.AddRange(path.Cells.Skip(1).Select(navigation.CellCenter));
                if (points[^1] != point)
                {
                    if (!reservations.Safe(entity.Id, definition.Radius, points[^1], point)) continue;
                    points.Add(point);
                }
                return new(order.Request, points.AsReadOnly(), WaitFrames: order.WaitFrames,
                    RetryAfterFrame: order.RetryAfterFrame);
            }
            if (remaining <= 0) break;
        }
        return null;
    }
}
