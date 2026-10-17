using System.Text.Json;
using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class PreparationStateChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var grid = new PathingGrid(9, 9, 32, default, new byte[81]);
        var definitions = new[] { new MovementDefinition(1, 100, 1) };
        var live = new RtsMatch(MatchConfig.Default, 91, grid, movementDefinitions: definitions);
        var control = new RtsMatch(MatchConfig.Default, 91, grid, movementDefinitions: definitions);
        foreach (var match in new[] { live, control })
            match.SubmitCommand(CommandEnvelope.Spawn(1, 0, 0, new(48, 48), 1));
        Preserve(live, check);
        live.Step(); control.Step();
        Preserve(live, check);
        check(JsonSerializer.Serialize(live.DrainEvents()) == JsonSerializer.Serialize(control.DrainEvents()),
            "preparation preserves undrained events, including their identity and order");
        var id = live.Entities.Single().Id;
        foreach (var match in new[] { live, control })
            match.SubmitCommand(CommandEnvelope.MoveGroup(2, 0, 1, new([id], new(240, 240))));
        Preserve(live, check);
        for (var frame = 0; frame < 20; frame++)
        {
            live.Step(); control.Step();
            Preserve(live, check);
            check(live.ComputeStateHash() == control.ComputeStateHash(), "prepared and unprepared matches retain identical state");
            check(JsonSerializer.Serialize(live.DrainEvents()) == JsonSerializer.Serialize(control.DrainEvents()),
                "prepared and unprepared matches retain identical events");
        }
        var restored = RtsMatch.Restore(SnapshotJson.Deserialize(SnapshotJson.Serialize(live.CaptureSnapshot())),
            grid, movementDefinitions: definitions);
        Preserve(restored, check);
        check(restored.ComputeStateHash() == live.ComputeStateHash(), "restored preparation does not change state");
        var blank = new RtsMatch(MatchConfig.Default, 55);
        Preserve(blank, check);
    }

    private static void Preserve(RtsMatch match, Action<bool, string> check)
    {
        var snapshot = SnapshotJson.Serialize(match.CaptureSnapshot());
        var hash = match.ComputeStateHash();
        match.PrepareForPlay();
        match.PrepareForPlay();
        check(snapshot == SnapshotJson.Serialize(match.CaptureSnapshot()), "repeated preparation preserves all snapshot authority");
        check(hash == match.ComputeStateHash(), "repeated preparation preserves hash/RNG/IDs/orders/frame");
    }
}
