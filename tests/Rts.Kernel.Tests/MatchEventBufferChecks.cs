using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class MatchEventBufferChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var grid = new PathingGrid(20, 20, 10, default, new byte[400]);
        var definitions = new[] { new MovementDefinition(1, 10, 1) };
        var match = CreateMatchWithBufferedEvents(grid, definitions);
        var id = new EntityId(1);

        var saved = match.CaptureSnapshot();
        var restored = RtsMatch.Restore(SnapshotJson.Deserialize(SnapshotJson.Serialize(saved)), grid,
            movementDefinitions: definitions);
        check(saved.NextEventSequence == 5, "ordinary and group events share one counter");
        check(restored.DrainEvents().Count == 0, "restore does not replay undrained pre-snapshot events");
        check(match.ComputeStateHash() == restored.ComputeStateHash(), "undrained events remain outside v9 state");
        var beforeDrain = match.ComputeStateHash();
        var drained = match.DrainEvents();
        check(drained.Select(item => item.Sequence).SequenceEqual(new long[] { 0, 1, 2, 3, 4 }),
            "ordinary and group events have continuous publication order");
        check(drained.Select(item => item.Frame).SequenceEqual(new long[] { 1, 1, 2, 2, 2 }),
            "buffer preserves the frame of publication across undrained steps");
        check(drained.Select(item => item.Kind).SequenceEqual(new[]
        {
            MatchEventKind.EntitySpawned, MatchEventKind.EntitySpawned, MatchEventKind.CommandRejected,
            MatchEventKind.GroupMoveAssigned, MatchEventKind.CommandRejected,
        }), "group assignment stays between surrounding ordinary events");
        check(drained[3].Group is { GroupId: 1, Slot: 0, Goal: not null }, "group payload is preserved");
        check(drained.Where(item => item.Kind == MatchEventKind.CommandRejected)
            .All(item => item.Detail == "use_move_to_with_navigation" && item.Group is null),
            "ordinary event details remain separate from group payloads");
        check(match.DrainEvents().Count == 0, "drain consumes events exactly once");
        check(match.ComputeStateHash() == beforeDrain, "draining does not reset the counter or alter state");

        var later = CommandEnvelope.SetVelocity(3, 0, 0, id, new SimVector2(3, 0));
        match.SubmitCommand(later);
        restored.SubmitCommand(later);
        match.Step();
        restored.Step();
        var next = match.DrainEvents();
        check(next.Count == 1 && next[0].Sequence == 5 && next[0].Frame == 3,
            "publication continues after drain with the current simulation frame");
        check(next.SequenceEqual(restored.DrainEvents()), "restored event counter continues identically");
        check(match.ComputeStateHash() == restored.ComputeStateHash(), "event ownership restores per-frame state");
        check(drained.Count == 5 && drained[4].Sequence == 4, "retained drain result is detached from future events");

        var independent = new RtsMatch(MatchConfig.Default, 7);
        independent.SubmitCommand(CommandEnvelope.Spawn(1, 0, 0, default));
        independent.Step();
        check(independent.DrainEvents().Single().Sequence == 0, "different matches own independent event counters");
        check(match.CaptureSnapshot().NextEventSequence == 6, "other match cannot change this event counter");
    }

    private static RtsMatch CreateMatchWithBufferedEvents(PathingGrid grid, MovementDefinition[] definitions)
    {
        var match = new RtsMatch(MatchConfig.Default, 7, grid, movementDefinitions: definitions);
        match.SubmitCommand(CommandEnvelope.Spawn(1, 0, 0, new SimVector2(15, 15), 1));
        match.SubmitCommand(CommandEnvelope.Spawn(1, 0, 1, new SimVector2(45, 15), 1));
        match.Step();
        var id = new EntityId(1);
        match.SubmitCommand(CommandEnvelope.SetVelocity(2, 0, 0, id, new SimVector2(1, 0)));
        match.SubmitCommand(CommandEnvelope.MoveGroup(2, 0, 1,
            new GroupMoveRequest(new[] { id }, new SimVector2(105, 105))));
        match.SubmitCommand(CommandEnvelope.SetVelocity(2, 0, 2, id, new SimVector2(2, 0)));
        match.Step();

        return match;
    }
}
