namespace Rts.Kernel.Navigation;

internal static class LocalDetour
{
    internal const int RadiusCells = 8;
    internal const int NodeBudget = 256;

    internal static MoveOrder? Find(NavigationState navigation, CrowdReservations reservations,
        EntityState entity, MovementDefinition definition, MoveOrder order)
    {
        if (!navigation.TryWorldToCell(entity.Position, out var start)) return null;
        var rejoin = order.NextWaypoint;
        for (var i = order.NextWaypoint; i < Math.Min(order.Waypoints.Count, order.NextWaypoint + 32); i++)
        {
            if (!navigation.TryWorldToCell(order.Waypoints[i], out var cell)
                || Math.Abs(cell.X - start.X) > RadiusCells || Math.Abs(cell.Y - start.Y) > RadiusCells) break;
            rejoin = i;
        }
        var left = Math.Max(0, start.X - RadiusCells);
        var top = Math.Max(0, start.Y - RadiusCells);
        var area = new GridArea(left, top, Math.Min(navigation.Grid.Width - left, start.X + RadiusCells - left + 1),
            Math.Min(navigation.Grid.Height - top, start.Y + RadiusCells - top + 1));
        var remainingNodes = NodeBudget;
        for (var targetIndex = rejoin; targetIndex >= order.NextWaypoint; targetIndex--)
        {
            navigation.TryWorldToCell(order.Waypoints[targetIndex], out var goal);
            if (goal == start) continue;
            bool CanUse(GridCell cell)
            {
                if (Math.Abs((long)cell.X - start.X) > RadiusCells || Math.Abs((long)cell.Y - start.Y) > RadiusCells
                    || !navigation.CanOccupy(cell, order.Request.ClearanceCells)) return false;
                var point = navigation.CellCenter(cell);
                return cell == start || reservations.Safe(entity.Id, definition.Radius, new[] { point, point });
            }
            bool Edge(GridCell from, GridCell to) =>
                navigation.CanTraverse(from, to, order.Request.ClearanceCells) && reservations.Safe(entity.Id, definition.Radius, new[] { from == start ? entity.Position : navigation.CellCenter(from), navigation.CellCenter(to) });
            var path = GridPathfinder.FindPathCore(navigation.Grid, start, goal, remainingNodes, 0, CanUse, Edge, area);
            remainingNodes -= path.ExpandedNodes;
            if (remainingNodes <= 0 && path.Status != PathStatus.Found) return null;
            if (path.Status != PathStatus.Found) continue;
            var points = new List<SimVector2> { entity.Position };
            points.AddRange(path.Cells.Skip(1).Select(navigation.CellCenter));
            if (points[^1] != order.Waypoints[targetIndex]) points.Add(order.Waypoints[targetIndex]);
            points.AddRange(order.Waypoints.Skip(targetIndex + 1));
            return new MoveOrder(order.Request, points.AsReadOnly(), WaitFrames: order.WaitFrames,
                RetryAfterFrame: order.RetryAfterFrame);
        }
        return null;
    }
}
