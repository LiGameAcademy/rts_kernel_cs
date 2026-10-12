using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class GroupSupersessionChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var definitions = new[] { new MovementDefinition(1, 30, 2) };
        var grid = new PathingGrid(64, 64, 10, default, new byte[4096]);
        var id = new EntityId(1);
        RtsMatch Create(OrderSource source)
        {
            var match = new RtsMatch(MatchConfig.Default, 7, grid, movementDefinitions: definitions);
            for (var i = 0; i < 100; i++)
                match.SubmitCommand(CommandEnvelope.Spawn(1, 0, i,
                    new SimVector2(35 + i % 13 * 20, 35 + i / 13 * 20), 1));
            match.Step();
            match.DrainEvents();
            var request = new GroupMoveRequest(match.Entities.Select(entity => entity.Id).ToArray(), new SimVector2(455, 455));
            match.SubmitCommand(CommandEnvelope.MoveGroup(2, 0, 100, request, source: source));
            match.Step();
            check(match.ReadGroupPlans().Count == 1, "supersession fixture keeps planning in progress");
            return match;
        }

        foreach (var source in new[] { OrderSource.UnitAi, OrderSource.Player })
        {
            var match = Create(OrderSource.UnitAi);
            match.SubmitCommand(CommandEnvelope.Stop(3, 0, 101, id, source));
            match.Step();
            check(match.ReadCurrentOrder(id)?.Kind == UnitOrderKind.Stop && !match.IsMoving(id),
                $"{source} Stop survives older UnitAi planning commit");
            check(match.ReadCurrentOrder(new EntityId(2))?.Group is not null,
                "canceling one member preserves other members' commit");
            check(match.DrainEvents().Count(e => e.EntityId == id && e.Detail == "group_plan_superseded") == 1,
                "superseded member reports one cancellation");
        }

        var replaced = Create(OrderSource.UnitAi);
        var replacementGoal = new SimVector2(35, 525);
        replaced.SubmitCommand(CommandEnvelope.MoveTo(3, 0, 101, id, replacementGoal, 30, source: OrderSource.UnitAi));
        replaced.Step();
        check(replaced.ReadCurrentOrder(id)?.Move?.Goal == replacementGoal
            && replaced.ReadCurrentOrder(id)?.Group is null, "same-source replacement survives older group commit");

        foreach (var source in new[] { OrderSource.Player, OrderSource.PlayerAi })
        {
            var protectedMatch = Create(source);
            protectedMatch.SubmitCommand(CommandEnvelope.Stop(3, 0, 101, id, OrderSource.UnitAi));
            protectedMatch.Step();
            check(protectedMatch.ReadCurrentOrder(id)?.Source == source
                && protectedMatch.ReadCurrentOrder(id)?.Group is not null, "UnitAi cannot cancel protected player plan");
            check(protectedMatch.DrainEvents().Any(e => e.EntityId == id && e.Detail == "player_order_active"),
                "protected order rejection remains visible");
        }

        var canceled = Create(OrderSource.UnitAi);
        canceled.SubmitCommand(CommandEnvelope.Stop(3, 0, 101, id, OrderSource.UnitAi));
        canceled.SubmitCommand(CommandEnvelope.Stop(3, 0, 102, id, OrderSource.UnitAi));
        // Capture a pending plan with newer Stop commands queued for the next frame.
        canceled.DrainEvents();
        var snapshot = canceled.CaptureSnapshot();
        var resumed = RtsMatch.Restore(SnapshotJson.Deserialize(SnapshotJson.Serialize(snapshot)), grid,
            movementDefinitions: definitions);
        for (var frame = 0; frame < 12; frame++)
        {
            canceled.Step();
            resumed.Step();
            check(canceled.ComputeStateHash() == resumed.ComputeStateHash(), "supersession restores identical authority");
            var events = canceled.DrainEvents();
            check(SnapshotJsonEvents(events) == SnapshotJsonEvents(resumed.DrainEvents()), "supersession restores event order");
        }
    }

    private static string SnapshotJsonEvents(IReadOnlyList<MatchEvent> events) =>
        System.Text.Json.JsonSerializer.Serialize(events);
}
