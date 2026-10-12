using Rts.Kernel;

internal static class MatchCommandQueueChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var match = new RtsMatch(MatchConfig.Default, 7);
        var commands = new[]
        {
            CommandEnvelope.Spawn(2, 2, 0, new SimVector2(40, 0)),
            CommandEnvelope.Spawn(1, 1, 5, new SimVector2(30, 0)),
            CommandEnvelope.Spawn(1, 0, 9, new SimVector2(20, 0)),
            CommandEnvelope.Spawn(1, 0, 1, new SimVector2(10, 0)),
            CommandEnvelope.Spawn(1, 0, 1, new SimVector2(11, 0)),
        };
        foreach (var command in commands)
            check(match.SubmitCommand(command).Accepted, "sorting fixture accepted");

        var beforeRejection = match.ComputeStateHash();
        var pastAndMalformed = CommandEnvelope.Spawn(0, -1, -1, default);
        check(match.SubmitCommand(pastAndMalformed).Error == "execute_frame_must_be_in_the_future",
            "frame rejection takes precedence over malformed structure");
        check(!match.SubmitCommand(CommandEnvelope.Spawn(1, -1, 0, default)).Accepted,
            "future malformed command rejected");
        check(match.ComputeStateHash() == beforeRejection, "rejections do not consume arrivals or alter pending commands");

        var snapshot = match.CaptureSnapshot();
        check(snapshot.PendingCommands.Select(item => item.ArrivalOrder).SequenceEqual(new long[] { 3, 4, 2, 1, 0 }),
            "snapshot orders by frame, player, sequence, then arrival");
        check(snapshot.NextArrivalOrder == 5, "only accepted commands consume arrival order");
        var restored = RtsMatch.Restore(SnapshotJson.Deserialize(SnapshotJson.Serialize(snapshot)));
        var later = CommandEnvelope.Spawn(2, 2, 0, new SimVector2(41, 0));
        check(match.SubmitCommand(later).Accepted && restored.SubmitCommand(later).Accepted,
            "restored queue accepts new commands");
        check(restored.CaptureSnapshot().PendingCommands.Single(item => item.Command == later).ArrivalOrder == 5,
            "restored arrival counter continues after saved arrivals");
        check(snapshot.PendingCommands.Count == 5, "captured pending list is detached from later submissions");

        for (var frame = 1; frame <= 3; frame++)
        {
            match.Step();
            restored.Step();
            var events = match.DrainEvents();
            check(events.SequenceEqual(restored.DrainEvents()), "restored sorted queue produces identical events");
            check(match.ComputeStateHash() == restored.ComputeStateHash(), "sorted queue restores per-frame state");
            if (frame == 1)
            {
                check(match.ReadEntities().Select(entity => entity.Position.X).SequenceEqual(new double[] { 10, 11, 20, 30 }),
                    "execution sorts player and sequence with stable arrival ties");
                check(match.CaptureSnapshot().PendingCommands.Count == 2, "future commands remain pending");
            }
            else if (frame == 2)
            {
                check(match.ReadEntities().Select(entity => entity.Position.X).SequenceEqual(new double[] { 10, 11, 20, 30, 40, 41 }),
                    "new submission follows restored same-frame ties");
                check(match.CaptureSnapshot().PendingCommands.Count == 0, "executed commands leave the queue");
            }
            else
            {
                check(events.Count == 0, "commands execute only once");
            }
        }
    }
}
