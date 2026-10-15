using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class PathfindingChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var snapshot = new RtsMatch(MatchConfig.Default, 86).CaptureSnapshot();
        try { RtsMatch.Restore(snapshot with { FormatVersion = 9 }); check(false, "floating-score snapshot is rejected"); }
        catch (InvalidDataException) { check(true, "floating-score snapshot is rejected"); }
        var grid = new PathingGrid(4, 4, 32, default, new byte[16]);
        var start = new GridCell(0, 0);
        var goal = new GridCell(3, 3);
        var result = GridPathfinder.FindPath(grid, start, goal);
        check(result.Status == PathStatus.Found && result.Cells.Count == 4, "open diagonal path");
        check(result.Cost == PathCost.FromUnits(3 * 14142135L), "Octile cost exactly preserves the existing decimal metric");
        check(GridPathfinder.FindPath(grid, start, start).Cost == PathCost.FromUnits(0), "same cell has a real zero cost");
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

        var budgetFailure = GridPathfinder.FindPath(grid, start, goal, 1);
        check(budgetFailure.Cost is null, "failed search has no numeric cost rather than an infinity sentinel");
        var tieMap = new PathingGrid(5, 3, 1, default,
            Enumerable.Range(0, 15).Select(i => (byte)(i == 7 ? 2 : 0)).ToArray());
        var tiePath = GridPathfinder.FindPath(tieMap, new GridCell(0, 1), new GridCell(4, 1));
        check(tiePath.Cells.SequenceEqual(new[] { new GridCell(0, 1), new GridCell(1, 0),
            new GridCell(2, 0), new GridCell(3, 0), new GridCell(4, 1) }), "equal scores prefer stable row-major cell indices");
        var longMap = new PathingGrid(8192, 1, 1, default, new byte[8192]);
        var longPath = GridPathfinder.FindPath(longMap, default, new GridCell(8191, 0));
        check(longPath.Cost?.Units == 8191L * 10000000 && longPath.Cells.Count == 8192,
            "long accumulation exceeds Int32 without rounding or overflowing");
        check(PathCost.FromUnits(14142135).ToPresentationDouble() == 1.4142135,
            "presentation conversion preserves the original displayed unit");
        try { PathCost.FromUnits(-1); check(false, "negative path cost rejected"); }
        catch (ArgumentOutOfRangeException) { check(true, "negative path cost rejected"); }

        // Exhaust every 3x3 obstacle layout; independent Dijkstra validates reachable costs.
        for (var mask = 0; mask < 512; mask++)
        {
            var obstacles = Enumerable.Range(0, 9).Select(i => (byte)((mask & (1 << i)) == 0 ? 0 : 2)).ToArray();
            var map = new PathingGrid(3, 3, 1, default, obstacles);
            // All endpoint pairs, including blocked/same-cell pairs, have an exact integer oracle.
            for (var from = 0; from < 9; from++)
            for (var to = 0; to < 9; to++)
            {
                var a = new GridCell(from % 3, from / 3);
                var b = new GridCell(to % 3, to / 3);
                var actual = GridPathfinder.FindPath(map, a, b);
                var optimal = ReferenceCost(map, a, b);
                check(optimal is null ? actual.Status != PathStatus.Found && actual.Cost is null
                    : actual.Status == PathStatus.Found && actual.Cost?.Units == optimal,
                    $"all-pair integer shortest path: mask {mask}, {from}->{to}");
            }
            var end = new GridCell(2, 2);
            var path = GridPathfinder.FindPath(map, start, end);
            var reference = ReferenceCost(map, start, end);
            check(reference is null ? path.Status != PathStatus.Found && path.Cost is null
                : path.Status == PathStatus.Found && path.Cost?.Units == reference, $"exact reference cost mask {mask}");
            if (path.Status != PathStatus.Found) continue;
            check(path.Cells[0] == start && path.Cells[^1] == end, "path endpoints");
            long actualCost = 0;
            for (var i = 1; i < path.Cells.Count; i++)
            {
                var a = path.Cells[i - 1];
                var b = path.Cells[i];
                var diagonal = a.X != b.X && a.Y != b.Y;
                check(map.IsWalkable(b) && Math.Abs(a.X - b.X) <= 1 && Math.Abs(a.Y - b.Y) <= 1
                    && a != b && (!diagonal || (map.IsWalkable(new GridCell(a.X, b.Y))
                    && map.IsWalkable(new GridCell(b.X, a.Y)))), "path obeys adjacency and corner rules");
                actualCost += diagonal ? 14142135L : 10000000L;
            }
            check(actualCost == path.Cost?.Units, "reported integer cost equals every summed edge exactly");
            GridPathfinder.FindPath(grid, goal, start);
            check(GridPathfinder.FindPath(map, start, end).Cells.SequenceEqual(path.Cells), "interleaved query stable");
        }
    }

    private static long? ReferenceCost(PathingGrid grid, GridCell start, GridCell goal)
    {
        if (!grid.IsWalkable(start) || !grid.IsWalkable(goal)) return null;
        var costs = new long?[grid.Width * grid.Height];
        var visited = new bool[costs.Length];
        costs[start.Y * grid.Width + start.X] = 0;
        while (true)
        {
            var index = -1;
            for (var i = 0; i < costs.Length; i++)
                if (!visited[i] && costs[i] is not null && (index < 0 || costs[i] < costs[index])) index = i;
            if (index < 0) return null;
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
                var candidate = costs[index]!.Value + (dx == 1 && dy == 1 ? 14142135L : 10000000L);
                if (costs[next] is null || candidate < costs[next]) costs[next] = candidate;
            }
        }
    }
}
