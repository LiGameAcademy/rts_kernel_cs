using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class PathfindingChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var grid = new PathingGrid(4, 4, 32, default, new byte[16]);
        var start = new GridCell(0, 0);
        var goal = new GridCell(3, 3);
        var result = GridPathfinder.FindPath(grid, start, goal);
        check(result.Status == PathStatus.Found && result.Cells.Count == 4, "open diagonal path");
        check(Math.Abs(result.Cost - 3 * 1.4142135) < 1e-9, "Octile cost matches game");
        check(GridPathfinder.FindPath(grid, start, start).Cells.SequenceEqual(new[] { start }), "same cell path");
        check(GridPathfinder.FindPath(grid, new GridCell(-1, 0), goal).Status == PathStatus.InvalidEndpoint, "outside endpoint");
        check(GridPathfinder.FindPath(grid, start, goal, 1).Status == PathStatus.NodeLimitReached, "explicit search budget");
        check(GridPathfinder.FindPath(grid, start, start, 1).Status == PathStatus.Found, "goal at budget boundary");
        var flags = new byte[] { 0, 2, 2, 0 };
        var corner = new PathingGrid(2, 2, 1, default, flags);
        check(GridPathfinder.FindPath(corner, start, new GridCell(1, 1)).Status == PathStatus.Unreachable, "cannot cross blocked corner");
        check(GridPathfinder.FindPath(corner, start, new GridCell(1, 0)).Status == PathStatus.InvalidEndpoint, "blocked endpoint");
        try { GridPathfinder.FindPath(grid, start, goal, 0); check(false, "invalid budget must throw"); }
        catch (ArgumentOutOfRangeException) { check(true, "invalid budget rejected"); }

        // Exhaust every 3x3 obstacle layout; independent Dijkstra validates reachable costs.
        for (var mask = 0; mask < 512; mask++)
        {
            var obstacles = Enumerable.Range(0, 9).Select(i => (byte)((mask & (1 << i)) == 0 ? 0 : 2)).ToArray();
            var map = new PathingGrid(3, 3, 1, default, obstacles);
            var end = new GridCell(2, 2);
            var path = GridPathfinder.FindPath(map, start, end);
            var reference = ReferenceCost(map, start, end);
            check(double.IsPositiveInfinity(reference) ? path.Status != PathStatus.Found
                : path.Status == PathStatus.Found && Math.Abs(path.Cost - reference) < 1e-9, $"reference cost mask {mask}");
            if (path.Status != PathStatus.Found) continue;
            check(path.Cells[0] == start && path.Cells[^1] == end, "path endpoints");
            var actualCost = 0.0;
            for (var i = 1; i < path.Cells.Count; i++)
            {
                var a = path.Cells[i - 1];
                var b = path.Cells[i];
                var diagonal = a.X != b.X && a.Y != b.Y;
                check(map.IsWalkable(b) && Math.Abs(a.X - b.X) <= 1 && Math.Abs(a.Y - b.Y) <= 1
                    && a != b && (!diagonal || (map.IsWalkable(new GridCell(a.X, b.Y))
                    && map.IsWalkable(new GridCell(b.X, a.Y)))), "path obeys adjacency and corner rules");
                actualCost += diagonal ? 1.4142135 : 1;
            }
            check(Math.Abs(actualCost - path.Cost) < 1e-9, "reported cost equals path cost");
            GridPathfinder.FindPath(grid, goal, start);
            check(GridPathfinder.FindPath(map, start, end).Cells.SequenceEqual(path.Cells), "interleaved query stable");
        }
    }

    private static double ReferenceCost(PathingGrid grid, GridCell start, GridCell goal)
    {
        if (!grid.IsWalkable(start) || !grid.IsWalkable(goal)) return double.PositiveInfinity;
        var costs = Enumerable.Repeat(double.PositiveInfinity, grid.Width * grid.Height).ToArray();
        var visited = new bool[costs.Length];
        costs[start.Y * grid.Width + start.X] = 0;
        while (true)
        {
            var index = -1;
            for (var i = 0; i < costs.Length; i++)
                if (!visited[i] && double.IsFinite(costs[i]) && (index < 0 || costs[i] < costs[index])) index = i;
            if (index < 0) return double.PositiveInfinity;
            var cell = new GridCell(index % grid.Width, index / grid.Width);
            if (cell == goal) return costs[index];
            visited[index] = true;
            for (var y = 0; y < grid.Height; y++)
            for (var x = 0; x < grid.Width; x++)
            {
                var dx = Math.Abs(x - cell.X);
                var dy = Math.Abs(y - cell.Y);
                if (dx > 1 || dy > 1 || dx + dy == 0 || !grid.IsWalkable(new GridCell(x, y))) continue;
                if (dx == 1 && dy == 1 && (!grid.IsWalkable(new GridCell(x, cell.Y))
                    || !grid.IsWalkable(new GridCell(cell.X, y)))) continue;
                var next = y * grid.Width + x;
                costs[next] = Math.Min(costs[next], costs[index] + (dx == 1 && dy == 1 ? 1.4142135 : 1));
            }
        }
    }
}
