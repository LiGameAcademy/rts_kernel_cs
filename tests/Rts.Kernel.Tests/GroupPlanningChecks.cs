using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class GroupPlanningChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var definitions = new[] { new MovementDefinition(1, 30, 4) };
        var grid = new PathingGrid(64, 64, 10, SimVector2.Zero, new byte[4096]);
        RtsMatch Create()
        {
            var match = new RtsMatch(MatchConfig.Default, 7, grid, movementDefinitions: definitions);
            for (var i = 0; i < 100; i++)
                match.SubmitCommand(CommandEnvelope.Spawn(1, 0, i, new SimVector2(35 + i % 10 * 20, 35 + i / 10 * 20), 1));
            match.Step(); match.DrainEvents();
            return match;
        }
        GroupMoveRequest Request(RtsMatch match, double x) =>
            new(match.Entities.Select(entity => entity.Id).ToArray(), new SimVector2(x, 425));
        var match = Create();
        match.SubmitCommand(CommandEnvelope.MoveGroup(2, 0, 100, Request(match, 425)));
        match.Step();
        check(match.ReadGroupPlans().Count == 1 && !match.IsMoving(new EntityId(1)), "large plan defers commit without changing old intent");
        var saved = match.CaptureSnapshot();
        var altered = saved.GroupPlans![0].Matching! with { U = new double[1] };
        var bad = saved with { GroupPlans = new[] { saved.GroupPlans[0] with { Matching = altered } } };
        var rejected = false;
        try { RtsMatch.Restore(bad, grid, movementDefinitions: definitions); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "malformed matching work state rejected before recovery");
        var previousVersion = saved with { FormatVersion = 6 };
        rejected = false;
        try { RtsMatch.Restore(previousVersion, grid, movementDefinitions: definitions); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "v6 snapshot rejected after planning progress format change");
        check(SnapshotDiff.FindFirst(saved, saved with { GroupPlans = [] })?.Path == "groupPlans.count", "planning differences are exposed");
        var resumed = RtsMatch.Restore(SnapshotJson.Deserialize(SnapshotJson.Serialize(saved)), grid, movementDefinitions: definitions);
        for (var i = 0; i < 20; i++)
        {
            match.Step(); resumed.Step();
            check(match.ComputeStateHash() == resumed.ComputeStateHash(), $"planning recovery agrees at frame {i}");
            var roundTrip = RtsMatch.Restore(SnapshotJson.Deserialize(SnapshotJson.Serialize(match.CaptureSnapshot())),
                grid, movementDefinitions: definitions);
            check(match.ComputeStateHash() == roundTrip.ComputeStateHash(), $"matching/candidate progress snapshot at frame {i}");
        }
        check(match.ReadGroupPlans().Count == 0 && match.ReadUnitOrders().Count == 100, "plan commits all member goals as one frame");
        var goals = match.ReadUnitOrders().Select(queue => queue.Current!.Group!.Slot).ToArray();
        check(goals.Distinct().Count() == 100, "delayed exact assignment remains bijective");

        var canceled = Create();
        canceled.SubmitCommand(CommandEnvelope.MoveGroup(2, 0, 100, Request(canceled, 425)));
        canceled.SubmitCommand(CommandEnvelope.Stop(2, 0, 101, new EntityId(1)));
        canceled.Step();
        var cancelSaved = canceled.CaptureSnapshot();
        check(cancelSaved.GroupPlans![0].Canceled.SequenceEqual(new[] { 1UL }), "same-frame Stop cancels pending member");
        var cancelResume = RtsMatch.Restore(cancelSaved, grid, movementDefinitions: definitions);
        for (var i = 0; i < 20; i++) { canceled.Step(); cancelResume.Step(); }
        check(canceled.ReadCurrentOrder(new EntityId(1))!.Kind == UnitOrderKind.Stop && !canceled.IsMoving(new EntityId(1)),
            "completed plan cannot overwrite later Stop");
        check(canceled.ComputeStateHash() == cancelResume.ComputeStateHash(), "cancellation restores exactly");

        var player = Create();
        player.SubmitCommand(CommandEnvelope.MoveGroup(2, 0, 100, Request(player, 425)));
        player.SubmitCommand(CommandEnvelope.MoveTo(2, 0, 101, new EntityId(1), new SimVector2(35, 525), 30,
            source: OrderSource.UnitAi));
        player.Step();
        check(player.DrainEvents().Any(e => e.Detail == "player_order_active"), "pending player plan protects against unit AI");
        player.SubmitCommand(CommandEnvelope.MoveTo(3, 0, 102, new EntityId(2), new SimVector2(55, 525), 30));
        player.Step();
        for (var i = 0; i < 20; i++) player.Step();
        check(player.ReadCurrentOrder(new EntityId(2))!.Group is null, "later successful replacement cancels only its planned member");

        var appended = Create();
        appended.SubmitCommand(CommandEnvelope.MoveGroup(2, 0, 100, Request(appended, 425)));
        appended.SubmitCommand(CommandEnvelope.MoveGroup(2, 0, 101, Request(appended, 325), OrderMode.Append));
        appended.Step();
        for (var i = 0; i < 40; i++) appended.Step();
        check(appended.ReadGroupPlans().Count == 0
            && appended.ReadUnitOrders().All(queue => queue.Current?.Group?.GroupId == 1 && queue.Pending.Count == 1
                && queue.Pending[0].Group!.GroupId == 2), "committing replacement preserves later append planning job");
        var immutable = Create();
        immutable.SubmitCommand(CommandEnvelope.MoveGroup(2, 0, 100, Request(immutable, 425)));
        immutable.Step();
        var copy = immutable.CaptureSnapshot();
        var hash = immutable.ComputeStateHash();
        copy.GroupPlans![0].Matching!.U[1] = -100;
        check(immutable.ComputeStateHash() == hash, "captured matching arrays cannot mutate match authority");
    }
}
