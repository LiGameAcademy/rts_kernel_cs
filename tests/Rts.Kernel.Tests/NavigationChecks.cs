using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class NavigationChecks
{
    internal static void Run(Action<bool, string> check)
    {
        void Invalid(Action action, string message)
        {
            try { action(); }
            catch (InvalidDataException) { check(true, message); return; }
            check(false, message);
        }
        var start = new GridCell(3, 1);
        var goal = new GridCell(3, 5);
        var flags = new byte[49];
        for (var x = 0; x < 7; x++) if (x != 3) flags[3 * 7 + x] = 2;
        var narrow = new PathingGrid(7, 7, 32, default, flags);
        check(GridPathfinder.FindPath(narrow, start, goal).Status == PathStatus.Found, "point crosses narrow passage");
        check(GridPathfinder.FindPath(narrow, start, goal, clearanceCells: 1).Status == PathStatus.Unreachable,
            "large unit cannot cross one-cell passage");
        flags[3 * 7 + 2] = flags[3 * 7 + 4] = 0;
        var wide = new PathingGrid(7, 7, 32, default, flags);
        check(GridPathfinder.FindPath(wide, start, goal, clearanceCells: 1).Status == PathStatus.Found,
            "large unit crosses three-cell passage");
        check(GridPathfinder.FindPath(wide, new GridCell(0, 0), goal, clearanceCells: 1).Status == PathStatus.InvalidEndpoint,
            "clearance cannot leave map");
        check(GridPathfinder.FindPath(wide, start, goal, clearanceCells: int.MaxValue).Status == PathStatus.InvalidEndpoint,
            "oversized clearance has no overflow");
        try { GridPathfinder.FindPath(wide, start, goal, clearanceCells: -1); check(false, "negative clearance"); }
        catch (ArgumentOutOfRangeException) { check(true, "negative clearance rejected"); }

        var grid = new PathingGrid(7, 7, 32, default, new byte[49]);
        var match = new RtsMatch(MatchConfig.Default, 7, grid);
        var other = new RtsMatch(MatchConfig.Default, 7, grid);
        var wall = new GridArea(0, 3, 7, 1);
        check(match.SubmitCommand(CommandEnvelope.SetObstacle(1, 0, 0, 10, wall)).Accepted, "obstacle queued");
        check(match.FindPath(start, goal).Status == PathStatus.Found, "future edit not visible early");
        match.Step();
        other.Step();
        check(match.FindPath(start, goal).Status == PathStatus.Unreachable, "wall applied at target frame");
        check(other.FindPath(start, goal).Status == PathStatus.Found, "second match not affected");
        check(grid.IsWalkable(new GridCell(3, 3)), "shared template remains unchanged");
        check(match.ComputeStateHash() != other.ComputeStateHash(), "obstacles affect state hash");
        match.SubmitCommand(CommandEnvelope.SetObstacle(2, 0, 1, 20, wall));
        match.Step();
        match.SubmitCommand(CommandEnvelope.RemoveObstacle(3, 0, 2, 10));
        match.Step();
        check(match.FindPath(start, goal).Status == PathStatus.Unreachable, "overlap survives one owner removal");
        match.SubmitCommand(CommandEnvelope.SetObstacle(4, 0, 3, 20, new GridArea(6, 6, 2, 1)));
        match.Step();
        check(match.FindPath(start, goal).Status == PathStatus.Unreachable, "invalid replacement leaves old footprint");
        check(match.DrainEvents().Single().Kind == MatchEventKind.CommandRejected, "out of map edit reported");
        check(!match.SubmitCommand(CommandEnvelope.SetObstacle(5, 0, 4, 0, wall)).Accepted, "zero obstacle id rejected");
        check(!match.SubmitCommand(CommandEnvelope.SetObstacle(5, 0, 4, 21, new GridArea(0, 0, 0, 1))).Accepted,
            "empty footprint rejected before queuing");
        match.SubmitCommand(CommandEnvelope.SetObstacle(5, 0, 4, 20, new GridArea(0, 3, 1, 1)));
        match.Step();
        check(match.FindPath(start, goal, clearanceCells: 1).Status == PathStatus.Found, "valid replacement frees old wall");
        match.SubmitCommand(CommandEnvelope.RemoveObstacle(7, 0, 5, 20));
        match.SubmitCommand(CommandEnvelope.SetObstacle(8, 0, 6, 30, wall));
        var snapshot = SnapshotJson.Deserialize(SnapshotJson.Serialize(match.CaptureSnapshot()));
        var restored = RtsMatch.Restore(snapshot, grid);
        check(match.ComputeStateHash() == restored.ComputeStateHash(), "navigation snapshot JSON round trip");
        check(match.FindPath(start, goal).Cells.SequenceEqual(restored.FindPath(start, goal).Cells), "restored path identical");
        Invalid(() => RtsMatch.Restore(snapshot), "navigation requires static map");
        var changedFlags = new byte[49]; changedFlags[0] = 2;
        Invalid(() => RtsMatch.Restore(snapshot, new PathingGrid(7, 7, 32, default, changedFlags)), "different map rejected");
        Invalid(() => RtsMatch.Restore(snapshot, new PathingGrid(7, 7, 64, default, new byte[49])), "different spacing rejected");
        Invalid(() => RtsMatch.Restore(snapshot, new PathingGrid(7, 7, 32, new SimVector2(1, 0), new byte[49])),
            "different origin rejected");
        Invalid(() => RtsMatch.Restore(snapshot with { FormatVersion = 1 }, grid), "old snapshot format rejected");
        var duplicate = snapshot.Navigation! with { Obstacles = new[] { snapshot.Navigation.Obstacles[0], snapshot.Navigation.Obstacles[0] } };
        Invalid(() => RtsMatch.Restore(snapshot with { Navigation = duplicate }, grid), "duplicate obstacle ids rejected");
        var unsorted = snapshot.Navigation with { Obstacles = new[] {
            new NavigationObstacle(30, wall), new NavigationObstacle(20, wall) } };
        Invalid(() => RtsMatch.Restore(snapshot with { Navigation = unsorted }, grid), "noncanonical obstacle order rejected");
        var invalidPending = snapshot.PendingCommands.ToArray();
        invalidPending[0] = invalidPending[0] with { Command = invalidPending[0].Command with { Obstacle = new ObstacleCommand(0) } };
        Invalid(() => RtsMatch.Restore(snapshot with { PendingCommands = invalidPending }, grid), "malformed pending payload rejected");
        check(!match.SubmitCommand(CommandEnvelope.SetObstacle(6, 0, 99, 1, wall)
            with { Velocity = new SimVector2(double.NaN, 0) }).Accepted, "unused obstacle fields cannot carry invalid state");
        var outside = snapshot.Navigation with { Obstacles = new[] { new NavigationObstacle(20, new GridArea(int.MaxValue, 0, 1, 1)) } };
        Invalid(() => RtsMatch.Restore(snapshot with { Navigation = outside }, grid), "out of map snapshot rejected without overflow");
        check(match.ComputeStateHash() == restored.ComputeStateHash(), "failed restore leaves live match unchanged");
        var changed = snapshot.Navigation with { Obstacles = new[] { new NavigationObstacle(20, new GridArea(1, 3, 1, 1)) } };
        check(SnapshotDiff.FindFirst(snapshot, snapshot with { Navigation = changed })?.Path == "navigation.obstacles[0].area.x",
            "obstacle diff identifies field");
        var pending = snapshot.PendingCommands.ToArray();
        pending[0] = pending[0] with { Command = pending[0].Command with { Obstacle = new ObstacleCommand(21) } };
        check(SnapshotDiff.FindFirst(snapshot, snapshot with { PendingCommands = pending })?.Path == "pendingCommands[0].command.obstacle.id",
            "pending obstacle payload diff identifies field");
        for (var i = 0; i < 2; i++)
        {
            match.Step(); restored.Step();
            check(match.ComputeStateHash() == restored.ComputeStateHash(), "restored pending edit executes identically");
        }
        check(match.FindPath(start, goal).Status == PathStatus.Found, "last overlap removed completely");
        match.Step(); restored.Step();
        check(match.ComputeStateHash() == restored.ComputeStateHash()
            && match.FindPath(start, goal).Status == PathStatus.Unreachable, "pending set footprint restored and executed");
        match.SubmitCommand(CommandEnvelope.RemoveObstacle(match.Frame + 1, 0, 6, 999)); match.Step();
        check(match.DrainEvents().Single().Kind == MatchEventKind.CommandRejected, "missing obstacle removal reported");

        // Same-frame ordering is player/sequence order, even when submission order differs.
        var a = new RtsMatch(MatchConfig.Default, 9, grid);
        var b = new RtsMatch(MatchConfig.Default, 9, grid);
        a.SubmitCommand(CommandEnvelope.SetObstacle(1, 0, 1, 1, wall));
        a.SubmitCommand(CommandEnvelope.RemoveObstacle(1, 0, 2, 1));
        b.SubmitCommand(CommandEnvelope.RemoveObstacle(1, 0, 2, 1));
        b.SubmitCommand(CommandEnvelope.SetObstacle(1, 0, 1, 1, wall));
        a.Step(); b.Step();
        check(a.FindPath(start, goal).Cells.SequenceEqual(b.FindPath(start, goal).Cells), "same-frame sequence controls result");
        check(a.CaptureSnapshot().Navigation!.Obstacles.Count == 0 && b.CaptureSnapshot().Navigation!.Obstacles.Count == 0,
            "same-frame set then remove completes");
        check(a.ComputeStateHash() == b.ComputeStateHash(), "same-frame order produces identical final hash");
        check(grid.ContentHash == new PathingGrid(7, 7, 32, default, new byte[49]).ContentHash,
            "identical grid values have same identity");
        // Compare combined dynamic occupancy against an independently rasterized static grid.
        var dynamic = new RtsMatch(MatchConfig.Default, 17, grid);
        var footprints = new Dictionary<ulong, GridArea>();
        for (var step = 0; step < 40; step++)
        {
            var id = (ulong)(step % 5 + 1);
            var area = new GridArea(step % 5, step * 3 % 5, 2, 2);
            if (step % 3 == 0 && footprints.Remove(id))
                dynamic.SubmitCommand(CommandEnvelope.RemoveObstacle(step + 1, 0, step, id));
            else
            {
                footprints[id] = area;
                dynamic.SubmitCommand(CommandEnvelope.SetObstacle(step + 1, 0, step, id, area));
            }
            dynamic.Step();
            var raster = new byte[49];
            foreach (var footprint in footprints.Values)
                for (var y = footprint.Y; y < footprint.Y + footprint.Height; y++)
                for (var x = footprint.X; x < footprint.X + footprint.Width; x++) raster[y * 7 + x] = 2;
            var referenceGrid = new PathingGrid(7, 7, 32, default, raster);
            for (var clearance = 0; clearance <= 1; clearance++)
            {
                var expected = GridPathfinder.FindPath(referenceGrid, start, goal, clearanceCells: clearance);
                var actual = dynamic.FindPath(start, goal, clearance);
                check(actual.Status == expected.Status && actual.Cells.SequenceEqual(expected.Cells),
                    "dynamic overlapping edits match static raster reference");
            }
        }
        var withoutMap = new RtsMatch(MatchConfig.Default, 0);
        withoutMap.SubmitCommand(CommandEnvelope.SetObstacle(1, 0, 0, 1, wall)); withoutMap.Step();
        check(withoutMap.DrainEvents().Single().Kind == MatchEventKind.CommandRejected, "mapless obstacle command rejected at execution");
        Invalid(() => RtsMatch.Restore(withoutMap.CaptureSnapshot(), grid), "restore cannot silently add navigation");
    }
}
