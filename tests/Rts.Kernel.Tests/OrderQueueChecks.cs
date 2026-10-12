using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class OrderQueueChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var grid = new PathingGrid(8, 4, 10, default, new byte[32]);
        var id = new EntityId(1);
        RtsMatch Create()
        {
            var match = new RtsMatch(MatchConfig.Default, 7, grid);
            match.SubmitCommand(CommandEnvelope.Spawn(1, 0, 0, new SimVector2(5, 5)));
            match.Step();
            match.DrainEvents();
            return match;
        }
        CommandEnvelope Move(long frame, long sequence, double x, OrderMode mode = OrderMode.Replace,
            OrderSource source = OrderSource.Player, double speed = 300) =>
            CommandEnvelope.MoveTo(frame, 0, sequence, id, new SimVector2(x, 5), speed, mode: mode, source: source);
        var queued = Create();
        queued.SubmitCommand(Move(2, 1, 25));
        queued.SubmitCommand(Move(2, 2, 45, OrderMode.Append));
        queued.SubmitCommand(Move(2, 3, 65, OrderMode.Append));
        queued.Step();
        check(queued.Entities.Single().Position == new SimVector2(15, 5)
            && queued.ReadUnitOrders().Single().Pending.Count == 2, "append keeps current goal and does not multiply frame movement");
        var savedView = queued.ReadUnitOrders();
        var restored = RtsMatch.Restore(SnapshotJson.Deserialize(SnapshotJson.Serialize(queued.CaptureSnapshot())), grid);
        queued.Step();
        restored.Step();
        check(queued.Entities.Single().Position == new SimVector2(25, 5)
            && queued.ReadUnitOrders().Single().Current is null, "arrival keeps pending intents until next frame");
        check(savedView.Single().Current!.Move!.Goal.X == 25 && savedView.Single().Pending.Count == 2,
            "captured order views retain copied state after simulation advances");
        var handoff = RtsMatch.Restore(SnapshotJson.Deserialize(SnapshotJson.Serialize(queued.CaptureSnapshot())), grid);
        for (var i = 0; i < 5; i++)
        {
            queued.Step();
            restored.Step();
            handoff.Step();
            check(queued.ComputeStateHash() == restored.ComputeStateHash() && queued.ComputeStateHash() == handoff.ComputeStateHash(),
                "active and handoff snapshots continue identically");
        }
        check(queued.Entities.Single().Position == new SimVector2(65, 5) && queued.ReadUnitOrders().Count == 0,
            "FIFO goals arrive precisely and remove finished queue");
        check(queued.DrainEvents().Select(item => item.Frame).SequenceEqual(new long[] { 3, 5, 7 }),
            "each queued goal completes once on its own frame");

        var invalid = Create();
        invalid.SubmitCommand(Move(2, 1, 65, speed: 30));
        invalid.SubmitCommand(Move(2, 2, 25, OrderMode.Append));
        invalid.Step();
        invalid.SubmitCommand(Move(3, 3, -5));
        invalid.SubmitCommand(Move(3, 4, -5, OrderMode.Append));
        invalid.Step();
        check(invalid.ReadUnitOrders().Single().Current!.Move!.Goal.X == 65
            && invalid.ReadUnitOrders().Single().Pending.Single().Move!.Goal.X == 25,
            "invalid replace and append preserve current and pending orders");
        check(invalid.DrainEvents().Count == 2, "each invalid queued request reports rejection");
        invalid.SubmitCommand(Move(4, 5, 45));
        invalid.Step();
        check(invalid.ReadUnitOrders().Single().Pending.Count == 0
            && invalid.ReadUnitOrders().Single().Current!.Move!.Goal.X == 45, "valid replacement clears all pending orders");

        var stopped = Create();
        stopped.SubmitCommand(Move(2, 1, 65));
        stopped.SubmitCommand(Move(2, 2, 25, OrderMode.Append));
        stopped.Step();
        stopped.SubmitCommand(CommandEnvelope.Stop(3, 0, 3, id));
        stopped.SubmitCommand(Move(3, 4, 45, source: OrderSource.UnitAi));
        stopped.Step();
        check(stopped.Entities.Single().Position == new SimVector2(15, 5) && !stopped.IsMoving(id)
            && stopped.ReadUnitOrders().Single().Current!.Kind == UnitOrderKind.Stop
            && stopped.ReadUnitOrders().Single().Pending.Count == 0, "stop cancels queue and blocks same-frame unit AI");
        var stoppedCopy = RtsMatch.Restore(SnapshotJson.Deserialize(SnapshotJson.Serialize(stopped.CaptureSnapshot())), grid);
        for (var i = 0; i < 5; i++)
        {
            var aiMove = Move(stopped.Frame + 1, 10 + i, 45, source: OrderSource.UnitAi);
            stopped.SubmitCommand(aiMove);
            stoppedCopy.SubmitCommand(aiMove);
            stopped.Step();
            stoppedCopy.Step();
            check(stopped.ComputeStateHash() == stoppedCopy.ComputeStateHash() && !stopped.IsMoving(id),
                "persistent stop survives restore and rejects later unit AI");
        }
        stopped.SubmitCommand(Move(stopped.Frame + 1, 20, 25, OrderMode.Append));
        stopped.Step();
        check(stopped.Entities.Single().Position.X == 25 && stopped.ReadUnitOrders().Count == 0,
            "player append on stopped entity replaces stop and starts immediately");

        var priority = Create();
        priority.SubmitCommand(Move(2, 1, 65, source: OrderSource.UnitAi, speed: 30));
        priority.Step();
        check(priority.ReadUnitOrders().Single().Current!.Source == OrderSource.UnitAi, "idle unit accepts unit AI move");
        priority.SubmitCommand(Move(3, 2, 25, source: OrderSource.PlayerAi, speed: 30));
        priority.SubmitCommand(Move(3, 3, 45, source: OrderSource.UnitAi));
        priority.SubmitCommand(CommandEnvelope.Stop(3, 0, 4, id, OrderSource.UnitAi));
        priority.Step();
        check(priority.ReadUnitOrders().Single().Current!.Source == OrderSource.PlayerAi && priority.IsMoving(id)
            && priority.DrainEvents().Count == 2, "player AI uses player authority and unit AI cannot replace or stop it");
        priority.SubmitCommand(Move(4, 5, 65) with { PlayerId = 1 });
        priority.Step();
        check(priority.ReadUnitOrders().Single().Current!.Source == OrderSource.PlayerAi
            && priority.DrainEvents().Single().Detail == "entity_missing_or_not_owned", "order source cannot bypass entity ownership");
        var pendingPlayer = Create();
        pendingPlayer.SubmitCommand(Move(2, 1, 65, source: OrderSource.UnitAi, speed: 30));
        pendingPlayer.SubmitCommand(Move(2, 2, 25, OrderMode.Append));
        pendingPlayer.SubmitCommand(Move(2, 3, 45, source: OrderSource.UnitAi));
        pendingPlayer.Step();
        check(pendingPlayer.ReadUnitOrders().Single().Pending.Count == 1 && pendingPlayer.DrainEvents().Count == 1,
            "pending player intent prevents unit AI replacing its own active order");

        var failure = Create();
        failure.SubmitCommand(Move(2, 1, 25));
        failure.SubmitCommand(Move(2, 2, 45, OrderMode.Append));
        failure.SubmitCommand(Move(2, 3, 65, OrderMode.Append));
        failure.Step();
        failure.SubmitCommand(CommandEnvelope.SetObstacle(3, 0, 4, 1, new GridArea(4, 0, 1, 1)));
        failure.Step();
        var blockedCopy = RtsMatch.Restore(SnapshotJson.Deserialize(SnapshotJson.Serialize(failure.CaptureSnapshot())), grid);
        failure.Step();
        blockedCopy.Step();
        check(failure.ReadUnitOrders().Single().Pending.Count == 1 && !failure.IsMoving(id)
            && failure.Entities.Single().Velocity == SimVector2.Zero, "failed queued goal retains next intent without using frame movement");
        check(failure.DrainEvents().Any(item => item.Kind == MatchEventKind.MoveFailed && item.Frame == 4),
            "blocked queued goal reports deterministic failure event");
        for (var i = 0; i < 12; i++)
        {
            failure.Step();
            blockedCopy.Step();
            check(failure.ComputeStateHash() == blockedCopy.ComputeStateHash(), "blocked pending goals restore and resume consistently");
        }
        check(failure.Entities.Single().Position == new SimVector2(65, 5) && !failure.IsMoving(id),
            "remaining queued goal starts from current location and detours around new obstacle");

        var reordered = Create();
        reordered.SubmitCommand(Move(2, 3, 65, OrderMode.Append));
        reordered.SubmitCommand(Move(2, 1, 25));
        reordered.SubmitCommand(Move(2, 2, 45, OrderMode.Append));
        reordered.Step();
        check(reordered.ReadUnitOrders().Single().Pending.Select(order => order.Move!.Goal.X).SequenceEqual(new double[] { 45, 65 }),
            "same-frame intent order follows sequence, not submission order");
        var ties = Create();
        ties.SubmitCommand(Move(2, 1, 25));
        ties.SubmitCommand(Move(2, 1, 45, OrderMode.Append));
        var tieCopy = RtsMatch.Restore(SnapshotJson.Deserialize(SnapshotJson.Serialize(ties.CaptureSnapshot())), grid);
        ties.Step();
        tieCopy.Step();
        check(ties.ComputeStateHash() == tieCopy.ComputeStateHash() && ties.ReadUnitOrders().Single().Pending.Count == 1,
            "equal command sequences preserve arrival order through pending-command restore");
        var activeFailure = Create();
        activeFailure.SubmitCommand(Move(2, 1, 65, speed: 30));
        activeFailure.SubmitCommand(CommandEnvelope.MoveTo(2, 0, 2, id, new SimVector2(5, 35), 300, mode: OrderMode.Append));
        activeFailure.Step();
        activeFailure.SubmitCommand(CommandEnvelope.SetObstacle(3, 0, 3, 1, new GridArea(1, 0, 1, 4)));
        activeFailure.Step();
        check(!activeFailure.IsMoving(id) && activeFailure.GetPendingOrderCount(id) == 1
            && activeFailure.DrainEvents().Single().Kind == MatchEventKind.MoveFailed,
            "active path failure retains later queued intent");
        var activeFailureCopy = RtsMatch.Restore(SnapshotJson.Deserialize(SnapshotJson.Serialize(activeFailure.CaptureSnapshot())), grid);
        for (var i = 0; i < 6; i++)
        {
            activeFailure.Step();
            activeFailureCopy.Step();
            check(activeFailure.ComputeStateHash() == activeFailureCopy.ComputeStateHash(), "active failure handoff restores consistently");
        }
        check(activeFailure.Entities.Single().Position == new SimVector2(5, 35), "queue resumes in reachable area after active path fails");

        var full = Create();
        full.SubmitCommand(Move(2, 0, 65, speed: 1));
        for (var i = 0; i <= RtsMatch.MaximumPendingOrders; i++) full.SubmitCommand(Move(2, i + 1, 65, OrderMode.Append));
        full.Step();
        check(full.ReadUnitOrders().Single().Pending.Count == RtsMatch.MaximumPendingOrders
            && full.DrainEvents().Single().Detail == "order_queue_full", "full queue rejects only new intent without altering existing orders");
        var instant = Create();
        instant.SubmitCommand(Move(2, 1, 5));
        instant.SubmitCommand(Move(2, 2, 5, OrderMode.Append));
        instant.SubmitCommand(Move(2, 3, 5, OrderMode.Append));
        instant.Step();
        instant.Step();
        check(instant.ReadUnitOrders().Single().Pending.Count == 1, "zero-distance queued goals activate at most once per frame");
        instant.Step();
        check(instant.ReadUnitOrders().Count == 0 && instant.DrainEvents().Count == 3, "zero-distance goals finish without duplicate completion");
    }
}
