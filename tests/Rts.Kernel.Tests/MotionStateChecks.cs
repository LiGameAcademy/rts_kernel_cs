using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class MotionStateChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var grid = new PathingGrid(2, 2, 10, default, new byte[4]);
        var values = new double[] { 0, 10, 0, 10 };
        var terrain = new TerrainHeights(2, 2, 20, default, values);
        var hash = terrain.ContentHash;
        values[0] = 999;
        check(terrain.ContentHash == hash && terrain.TrySample(default, out var height) && height == 0,
            "height identity and samples do not expose source buffers");
        var match = new RtsMatch(MatchConfig.Default, 7, grid, terrain);
        match.SubmitCommand(CommandEnvelope.Spawn(1, 0, 0, new SimVector2(5, 5)));
        match.Step();
        var snapshot = match.CaptureSnapshot();
        var changed = snapshot with { Entities = new[] { snapshot.Entities[0] with { Facing = 0.75 } } };
        var restored = RtsMatch.Restore(SnapshotJson.Deserialize(SnapshotJson.Serialize(changed)), grid, terrain);
        check(restored.Entities.Single().Facing == 0.75, "authoritative facing round trips in snapshot");
        check(SnapshotDiff.FindFirst(snapshot, changed)?.Path == "entities[0].facing", "facing diff identifies field");
        check(snapshot.Navigation!.HeightHash == terrain.ContentHash, "navigation identifies height field");
        void Invalid(Action action, string label)
        {
            try { action(); }
            catch (ArgumentException) { check(true, label); return; }
            catch (InvalidDataException) { check(true, label); return; }
            check(false, label);
        }
        Invalid(() => RtsMatch.Restore(snapshot, grid), "missing height field rejected");
        var other = new TerrainHeights(2, 2, 20, default, new double[] { 1, 10, 0, 10 });
        Invalid(() => RtsMatch.Restore(snapshot, grid, other), "different height field rejected");
        Invalid(() => RtsMatch.Restore(snapshot with { FormatVersion = 3 }, grid, terrain), "v3 snapshot rejected explicitly");
        Invalid(() => RtsMatch.Restore(changed with { Entities = new[] { changed.Entities[0] with { Facing = double.NaN } } }, grid, terrain),
            "nonfinite facing rejected");
        Invalid(() => RtsMatch.Restore(changed with { Entities = new[] { changed.Entities[0] with { Facing = Math.PI } } }, grid, terrain),
            "noncanonical facing rejected");
        Invalid(() => new RtsMatch(MatchConfig.Default, 7, terrain: terrain), "height field without navigation rejected");
        Invalid(() => new RtsMatch(MatchConfig.Default, 7, grid,
            new TerrainHeights(2, 2, 10, default, new double[4])), "incomplete height coverage rejected");
        check(SnapshotDiff.FindFirst(snapshot, snapshot with { Navigation = snapshot.Navigation with { HeightHash = other.ContentHash } })?.Path
            == "navigation.heightHash", "height identity diff identifies field");
        check(!match.SubmitCommand(CommandEnvelope.MoveTo(2, 0, 1, new EntityId(1), new SimVector2(15, 5), 10,
            motion: new MotionParameters(TurnRate: 0))).Accepted, "invalid unit turn parameters rejected");
    }
}
