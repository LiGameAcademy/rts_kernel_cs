namespace Rts.Kernel.Navigation;

/// <summary>Builds world waypoint paths from grid navigation without owning match state.</summary>
internal static class MovePathPlanner
{
    internal static bool TryPlan(NavigationState? navigation, FixedVector2 position, MoveRequest request,
        out MoveOrder? order, bool preferDirect = false)
    {
        order = null;
        if (navigation is null || !navigation.TryWorldToCell(position, out var start)
            || !navigation.TryWorldToCell(request.Goal, out var goal)) return false;
        var cells = preferDirect ? DirectGridPath.TryFind(navigation, start, goal, request.ClearanceCells) : null;
        if (cells is null)
        {
            var path = navigation.FindPath(start, goal, request.ClearanceCells, int.MaxValue);
            if (path.Status != PathStatus.Found) return false;
            cells = path.Cells;
        }
        var points = new List<FixedVector2> { position };
        if (cells.Count > 1)
            foreach (var cell in cells)
            {
                var center = navigation.CellCenter(cell);
                if (center != points[^1]) points.Add(center);
            }
        if (points.Count == 0 || points[^1] != request.Goal) points.Add(request.Goal);
        if (points.Any(point => !SimulationDomain.Contains(point.X) || !SimulationDomain.Contains(point.Y))) return false;
        order = new MoveOrder(request, points.AsReadOnly());
        return true;
    }
}
