using System.Text.Json;
using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class ResumablePathChecks
{
    internal static void Run(Action<bool, string> check)
    {
        // Compare the incremental engine against the existing independent Dijkstra-tested query.
        for (var mask = 0; mask < 64; mask++)
        {
            var flags = Enumerable.Range(0, 25)
                .Select(i => (byte)(i is > 5 and < 12 && (mask & (1 << (i - 6))) != 0 ? 2 : 0)).ToArray();
            var grid = new PathingGrid(5, 5, 1, default, flags);
            var expected = GridPathfinder.FindPath(grid, new(0, 0), new(4, 4));
            var job = new GridSearchJob(grid, new(0, 0), new(4, 4), 0, grid.IsWalkable);
            var steps = 0;
            while (!job.IsComplete)
            {
                var spent = job.Advance(1);
                check(spent == 1, "every incremental path step has exactly one charged operation");
                if (++steps > 30000) throw new InvalidOperationException("Incremental path failed to terminate.");
                if (steps % 17 == 0)
                {
                    var json = JsonSerializer.Serialize(job.Capture());
                    job = GridSearchJob.Restore(grid, JsonSerializer.Deserialize<GridSearchSnapshot>(json)!, grid.IsWalkable);
                    check(JsonSerializer.Serialize(job.Capture()) == json, "search snapshot is detached and canonical");
                }
            }
            var actual = job.Result!;
            check(actual.Status == expected.Status && actual.Cost == expected.Cost,
                $"sliced search preserves exact reachable cost, layout {mask}");
            check(actual.Cells.SequenceEqual(expected.Cells), "slice size and restoration preserve stable tie path");
            check(job.Advance(5) == 0, "completed search consumes no more budget");
        }
        var probeGrid = new PathingGrid(9, 9, 1, default, new byte[81]);
        var probeJob = new GridSearchJob(probeGrid, new(2, 2), new(6, 6), 2, probeGrid.IsWalkable);
        probeJob.Advance(2);
        var validProbe = probeJob.Capture();
        var corruptProbe = validProbe with { Cursor = validProbe.Cursor with { ProbeX = int.MaxValue } };
        try { GridSearchJob.Restore(probeGrid, corruptProbe, probeGrid.IsWalkable); check(false, "corrupt probe rejected before map access"); }
        catch (InvalidDataException) { check(true, "corrupt probe rejected before map access"); }
        var finished = new GridSearchJob(probeGrid, new(1, 1), new(7, 7), 0, probeGrid.IsWalkable);
        while (!finished.IsComplete) finished.Advance(31);
        var good = finished.Capture();
        var badNodes = good.Nodes.ToArray();
        var child = Array.FindIndex(badNodes, node => node.Parent >= 0);
        badNodes[child] = badNodes[child] with { Cost = long.MaxValue };
        try { GridSearchJob.Restore(probeGrid, good with { Nodes = badNodes }, probeGrid.IsWalkable); check(false, "invalid parent costs rejected"); }
        catch (InvalidDataException) { check(true, "invalid parent costs rejected"); }
        var detached = finished.Capture();
        check(detached.Nodes is not SearchNode[] && detached.Path is not GridCell[], "snapshot collections do not expose mutable arrays");
        var open = new PathingGrid(1000, 1, 1, default, new byte[1000]);
        var longJob = new GridSearchJob(open, default, new(999, 0), 0, open.IsWalkable);
        while (!longJob.IsComplete) check(longJob.Advance(7) <= 7, "long path preparation and tracing stay within budget");
        check(longJob.Result!.Cells.Count == 1000, "long path reconstructs every cell");
        var blocked = new PathingGrid(7, 7, 1, default,
            Enumerable.Range(0, 49).Select(i => (byte)(i == 24 ? 2 : 0)).ToArray());
        var clearance = new GridSearchJob(blocked, new(1, 1), new(5, 5), 1, blocked.IsWalkable);
        while (!clearance.IsComplete) clearance.Advance(1);
        check(clearance.Result!.Status == GridPathfinder.FindPath(blocked, new(1, 1), new(5, 5), clearanceCells: 1).Status,
            "footprint and diagonal checks include clearance without synchronous probes");
        var invalid = new GridSearchJob(open, new(-1, 0), new(10, 0), 0, open.IsWalkable);
        while (!invalid.IsComplete) invalid.Advance(1);
        check(invalid.Result!.Status == PathStatus.InvalidEndpoint, "invalid endpoints remain distinct from pending work");
        try { longJob.Advance(0); check(false, "zero work budget rejected"); }
        catch (ArgumentOutOfRangeException) { check(true, "zero work budget rejected"); }
    }
}
