namespace Rts.Kernel.Navigation;

/// <summary>Shortest octile cell route on clear ground; blocked routes fall back to A*.</summary>
internal static class DirectGridPath
{
    internal static IReadOnlyList<GridCell>? TryFind(NavigationState navigation, GridCell start, GridCell goal, int clearance)
    {
        if (!navigation.CanOccupy(start, clearance) || !navigation.CanOccupy(goal, clearance)) return null;
        var cells = new List<GridCell> { start };
        var dx = Math.Abs(goal.X - start.X);
        var dy = Math.Abs(goal.Y - start.Y);
        var sx = Math.Sign(goal.X - start.X);
        var sy = Math.Sign(goal.Y - start.Y);
        long error = (long)dx - dy;
        var current = start;
        while (current != goal)
        {
            var twice = error * 2;
            var x = current.X;
            var y = current.Y;
            if (twice > -dy) { error -= dy; x += sx; }
            if (twice < dx) { error += dx; y += sy; }
            var next = new GridCell(x, y);
            if (!navigation.CanTraverse(current, next, clearance)) return null;
            cells.Add(next);
            current = next;
        }
        return cells.AsReadOnly();
    }
}
