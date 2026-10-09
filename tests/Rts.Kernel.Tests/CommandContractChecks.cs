using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class CommandContractChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var id = new EntityId(5);
        var spawn = CommandEnvelope.Spawn(5, 0, 0, SimVector2.Zero);
        var invalid = new[]
        {
            spawn with { Kind = (CommandKind)999 },
            CommandEnvelope.Stop(5, 0, 1, EntityId.None),
            CommandEnvelope.SetVelocity(5, 0, 2, EntityId.None, SimVector2.Zero),
            spawn with { EntityId = id },
        };
        foreach (var command in invalid)
        {
            var match = new RtsMatch(MatchConfig.Default, 7);
            var before = match.ComputeStateHash();
            check(!match.SubmitCommand(command).Accepted, $"invalid command shape rejected: {command.Kind}/{command.EntityId}");
            check(match.ComputeStateHash() == before, "rejection does not consume arrival order or alter state");
            check(RtsMatch.Restore(match.CaptureSnapshot()).ComputeStateHash() == before,
                "rejected command leaves a restorable snapshot");

            // Restore must reject the same malformed structure even when injected into a snapshot.
            var injected = match.CaptureSnapshot() with
            {
                NextArrivalOrder = 1,
                PendingCommands = new[] { new QueuedCommandSnapshot(0, command) },
            };
            try
            {
                RtsMatch.Restore(injected);
                check(false, "malformed pending command rejected on restore");
            }
            catch (InvalidDataException)
            {
                check(true, "malformed pending command rejected on restore");
            }
        }

        var legal = new[]
        {
            spawn,
            CommandEnvelope.SetVelocity(5, 0, 1, id, new SimVector2(1, 2)),
            CommandEnvelope.Stop(5, 0, 2, id),
            CommandEnvelope.SetObstacle(5, 0, 3, 1, new GridArea(0, 0, 1, 1)),
            CommandEnvelope.RemoveObstacle(5, 0, 4, 1),
            CommandEnvelope.MoveTo(5, 0, 5, id, new SimVector2(5, 5), 10),
            CommandEnvelope.MoveGroup(5, 0, 6, new GroupMoveRequest(new[] { id }, new SimVector2(5, 5))),
        };
        foreach (var command in legal)
        {
            var match = new RtsMatch(MatchConfig.Default, 7);
            check(match.SubmitCommand(command).Accepted, $"legal pending command accepted: {command.Kind}");
            var restored = RtsMatch.Restore(SnapshotJson.Deserialize(SnapshotJson.Serialize(match.CaptureSnapshot())));
            check(match.ComputeStateHash() == restored.ComputeStateHash(), $"pending command round trip: {command.Kind}");
            // Entity/map existence is an execution concern, not a command-structure constraint.
            for (var frame = 0; frame < 5; frame++)
            {
                match.Step();
                restored.Step();
                check(match.ComputeStateHash() == restored.ComputeStateHash(), "restored pending command executes identically");
                check(match.DrainEvents().SequenceEqual(restored.DrainEvents()), "restored pending command preserves events");
            }
        }
    }
}
