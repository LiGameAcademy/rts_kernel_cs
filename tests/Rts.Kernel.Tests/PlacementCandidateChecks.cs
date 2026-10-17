using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class PlacementCandidateChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var random = new Random(117);
        foreach (var size in new[] { (1, 1), (1, 30), (30, 1), (7, 9), (32, 32) })
        foreach (var cellSize in new SimScalar[] { 1, 32, SimScalar.FromRaw(32 * SimScalar.Scale + 1) })
        foreach (var origin in new[] { new FixedVector2(-100, -200), FixedVector2.Zero, new FixedVector2(1000, 2000) })
        {
            var grid = new PathingGrid(size.Item1, size.Item2, cellSize, origin, new byte[size.Item1 * size.Item2]);
            var navigation = new NavigationState(grid);
            var positions = new List<FixedVector2>();
            foreach (var x in new[] { -9, -8, -1, 0, size.Item1 - 1, size.Item1, size.Item1 + 7, size.Item1 + 8 })
            foreach (var y in new[] { -8, 0, size.Item2 - 1, size.Item2 + 8 })
            foreach (var tail in new long[] { -1, 0, 1, cellSize.Raw / 2 })
                positions.Add(FixedVector2.FromRaw(origin.X.Raw + x * cellSize.Raw + tail,
                    origin.Y.Raw + y * cellSize.Raw + tail));
            for (var i = 0; i < 30; i++)
                positions.Add(FixedVector2.FromRaw(origin.X.Raw + random.NextInt64(-9 * cellSize.Raw, (size.Item1 + 9) * cellSize.Raw),
                    origin.Y.Raw + random.NextInt64(-9 * cellSize.Raw, (size.Item2 + 9) * cellSize.Raw)));
            foreach (var ideal in positions)
            {
                var expected = FullCandidates(navigation, ideal);
                var actual = GroupPlacementPlanner.Candidates(navigation, ideal);
                check(actual.Count == expected.Length, "candidate count retains complete clipped window");
                using var first = actual.GetEnumerator();
                if (expected.Length > 0)
                {
                    check(first.MoveNext() && first.Current == expected[0], "nearest candidate agrees");
                    check(actual[expected.Length / 2] == expected[expected.Length / 2], "index can extend consumed prefix");
                }
                check(actual.SequenceEqual(expected), "complete distance/Y/X candidate ordering agrees");
                for (var i = 1; i < expected.Length; i++)
                    check(first.MoveNext() && first.Current == expected[i], "interleaved enumeration retains prefix");
                check(!first.MoveNext(), "enumeration ends at complete count");
                check(actual.SequenceEqual(expected), "retry enumeration retains full ordering");
            }
        }
    }

    // Independent eager reference: original clipped rectangle and full distance/Y/X ordering.
    private static GridCell[] FullCandidates(NavigationState navigation, FixedVector2 ideal)
    {
        var grid = navigation.Grid;
        var x = FixedGridMath.FloorIndex(ideal.X, grid.Origin.X, grid.CellSize);
        var y = FixedGridMath.FloorIndex(ideal.Y, grid.Origin.Y, grid.CellSize);
        if (x < -8 || y < -8 || x >= grid.Width + 8L || y >= grid.Height + 8L) return [];
        var result = new List<GridCell>();
        for (var cy = Math.Max(0, y - 8); cy <= Math.Min(grid.Height - 1L, y + 8); cy++)
        for (var cx = Math.Max(0, x - 8); cx <= Math.Min(grid.Width - 1L, x + 8); cx++)
            result.Add(new((int)cx, (int)cy));
        return result.OrderBy(cell => SquaredDistance.Between(navigation.CellCenter(cell), ideal).Raw)
            .ThenBy(cell => cell.Y).ThenBy(cell => cell.X).ToArray();
    }
}
