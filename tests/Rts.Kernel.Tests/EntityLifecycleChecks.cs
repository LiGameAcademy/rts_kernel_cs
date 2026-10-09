using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class EntityLifecycleChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var grid = new PathingGrid(20, 20, 10, default, new byte[400]);
        var definitions = new[] { new MovementDefinition(1, 30, 1) };
        var match = new RtsMatch(MatchConfig.Default, 7, grid, movementDefinitions: definitions);
        match.SubmitCommand(CommandEnvelope.Spawn(1, 0, 0, new SimVector2(15, 15), 99));
        match.SubmitCommand(CommandEnvelope.Spawn(1, 0, 1, new SimVector2(15, 15), 1));
        match.SubmitCommand(CommandEnvelope.Spawn(1, 0, 2, new SimVector2(15, 15), 1));
        match.SubmitCommand(CommandEnvelope.Spawn(1, 0, 3, new SimVector2(35, 15), 1));
        match.Step();
        var spawned = match.DrainEvents();
        check(spawned.Select(item => item.Kind).SequenceEqual(new[]
        {
            MatchEventKind.CommandRejected, MatchEventKind.EntitySpawned,
            MatchEventKind.CommandRejected, MatchEventKind.EntitySpawned,
        }), "definition and occupied-ground rejections remain between accepted spawns");
        check(spawned.Where(item => item.Kind == MatchEventKind.CommandRejected).Select(item => item.Detail)
            .SequenceEqual(new[] { "movement_definition_missing", "spawn_ground_unavailable" }),
            "spawn validation keeps specific rejection reasons");
        check(match.ReadEntities().Select(item => item.Id.Value).SequenceEqual(new[] { 1UL, 2UL }),
            "rejected spawns do not consume entity IDs");
        check(match.CaptureSnapshot().NextEntityId == 3, "next ID belongs to accepted spawns only");
        match.SubmitCommand(CommandEnvelope.MoveTo(2, 0, 0, new EntityId(1), new SimVector2(15, 115), 30));
        match.SubmitCommand(CommandEnvelope.Spawn(2, 0, 1, new SimVector2(65, 15), 1));
        match.Step();
        match.DrainEvents();
        VerifyCaptureAndRestore(match, grid, definitions, check);
    }

    private static void VerifyCaptureAndRestore(RtsMatch match, PathingGrid grid,
        MovementDefinition[] definitions, Action<bool, string> check)
    {
        var snapshot = match.CaptureSnapshot();
        var serialized = SnapshotJson.Serialize(snapshot);
        foreach (var entity in match.ReadEntities())
        {
            var expected = new EntitySnapshot(entity.Id.Value, entity.OwnerId, entity.Position.X, entity.Position.Y,
                entity.Velocity.X, entity.Velocity.Y, entity.Facing, entity.MovementDefinitionId);
            check(snapshot.Entities.Single(item => item.Id == entity.Id.Value) == expected,
                "entity capture retains identity, owner, position, velocity, facing and movement definition");
        }
        var reversed = snapshot.Entities.Reverse().ToArray();
        var restored = RtsMatch.Restore(snapshot with { Entities = reversed }, grid, movementDefinitions: definitions);
        check(match.ComputeStateHash() == restored.ComputeStateHash(), "restore canonicalizes shuffled entity snapshots");
        check(restored.ReadEntities().Select(item => item.Id.Value).SequenceEqual(new[] { 1UL, 2UL, 3UL }),
            "restored entity enumeration remains sorted by ID");
        reversed[0] = reversed[0] with { OwnerId = 9, PositionX = 999 };
        check(match.ComputeStateHash() == restored.ComputeStateHash(), "restored states do not alias caller snapshot entries");
        var spawn = CommandEnvelope.Spawn(3, 0, 0, new SimVector2(95, 15), 1);
        match.SubmitCommand(spawn);
        restored.SubmitCommand(spawn);
        match.Step();
        restored.Step();
        var events = match.DrainEvents();
        check(events.Any(item => item.Kind == MatchEventKind.EntitySpawned && item.EntityId == new EntityId(4)),
            "restored identity counter continues at the saved next ID");
        check(restored.CaptureSnapshot().NextEntityId == 5, "restored spawn advances its own identity counter");
        check(events.SequenceEqual(restored.DrainEvents()), "restored entity generation preserves event order and IDs");
        check(match.ComputeStateHash() == restored.ComputeStateHash(), "restored entity updates preserve state hashes");
        check(SnapshotJson.Serialize(snapshot) == serialized, "saved entity values survive later state replacement and spawn");
        var independent = new RtsMatch(MatchConfig.Default, 7);
        independent.SubmitCommand(CommandEnvelope.Spawn(1, 0, 0, default));
        independent.Step();
        check(independent.ReadEntities().Single().Id == new EntityId(1), "entity identity allocation is per match");
        var restoredBefore = restored.ComputeStateHash();
        match.SubmitCommand(CommandEnvelope.Stop(4, 0, 0, new EntityId(1)));
        match.Step();
        check(restored.ComputeStateHash() == restoredBefore, "updates to the original cannot mutate the restored roster");
    }
}
