namespace Rts.Kernel.Navigation;

/// <summary>Builds world waypoint paths from grid navigation without owning match state.</summary>
internal static class MovePathPlanner
{
    internal static bool TryPlan(NavigationState? navigation, SimVector2 position, MoveRequest request,
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
        var points = cells.Count == 1 ? new List<SimVector2>()
            : cells.Select(navigation.CellCenter).ToList();
        if (points.Count == 0 || points[^1] != request.Goal) points.Add(request.Goal);
        if (points.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y))) return false;
        order = new MoveOrder(request, points.AsReadOnly());
        return true;
    }
}
