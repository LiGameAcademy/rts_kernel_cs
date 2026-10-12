using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class OrderSnapshotChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var grid = new PathingGrid(8, 4, 10, default, new byte[32]);
        var id = new EntityId(1);
        var match = new RtsMatch(MatchConfig.Default, 7, grid);
        match.SubmitCommand(CommandEnvelope.Spawn(1, 0, 0, new SimVector2(5, 5)));
        match.Step();
        match.SubmitCommand(CommandEnvelope.MoveTo(2, 0, 1, id, new SimVector2(65, 5), 30));
        match.SubmitCommand(CommandEnvelope.MoveTo(2, 0, 2, id, new SimVector2(25, 5), 30, mode: OrderMode.Append));
        match.Step();
        var saved = match.CaptureSnapshot();
        var queue = saved.Orders!.Single();
        void Invalid(MatchSnapshot snapshot, string label)
        {
            try { RtsMatch.Restore(snapshot, grid); }
            catch (InvalidDataException) { check(true, label); return; }
            check(false, label);
        }
        Invalid(saved with { FormatVersion = 4 }, "v4 snapshots explicitly rejected after order format change");
        Invalid(saved with { Orders = null }, "orders collection required for v5");
        Invalid(saved with { Orders = Array.Empty<UnitOrderQueueSnapshot>() }, "active path cannot omit its order intent");
        Invalid(saved with { Orders = new[] { queue, queue } }, "duplicate order entity rejected");
        Invalid(saved with { Orders = new[] { queue with { EntityId = 99 } } }, "orders cannot reference missing entity");
        Invalid(saved with { Orders = new[] { queue with { Current = null } } }, "active path cannot have waiting order state");
        Invalid(saved with { Orders = new[] { queue with { Current = new UnitOrderIntent(UnitOrderKind.Stop, OrderSource.Player) } } },
            "stop cannot retain pending goals or active path");
        Invalid(saved with { Orders = new[] { queue with { Pending = null! } } }, "null pending queue rejected");
        Invalid(saved with { Orders = new[] { queue with { Current = queue.Current! with { Source = (OrderSource)99 } } } },
            "unknown active source rejected");
        Invalid(saved with { Orders = new[] { queue with { Pending = new[] { queue.Pending[0] with { Kind = UnitOrderKind.Stop } } } } },
            "stop cannot be appended as a pending move");
        Invalid(saved with { Orders = new[] { queue with { Pending = new[] { queue.Pending[0] with { Source = (OrderSource)99 } } } } },
            "unknown pending source rejected");
        Invalid(saved with { Orders = new[] { queue with { Pending = new UnitOrderIntent[] { null! } } } }, "null pending intent rejected");
        Invalid(saved with { Orders = new[] { queue with { Pending = Enumerable.Repeat(queue.Pending[0], 65).ToArray() } } },
            "oversized restored queue rejected");
        Invalid(saved with { Orders = new[] { queue with { Current = queue.Current! with
            { Move = queue.Current.Move! with { Goal = new SimVector2(25, 5) } } } } }, "active request and path must agree");
        MatchSnapshot WithPending(UnitOrderIntent intent) =>
            saved with { Orders = new[] { queue with { Pending = new[] { intent } } } };
        var pendingIntent = queue.Pending[0];
        Invalid(WithPending(pendingIntent with { Move = pendingIntent.Move! with { Goal = new SimVector2(-5, 5) } }),
            "restored queued goal must be inside map");
        Invalid(WithPending(pendingIntent with { Move = pendingIntent.Move! with { Speed = double.NaN } }),
            "restored queued speed must be finite");
        Invalid(WithPending(pendingIntent with { Move = pendingIntent.Move! with { Motion = new MotionParameters() } }),
            "pending slope intent requires height identity even before it has a path");
        var changedSource = saved with { Orders = new[] { queue with { Current = queue.Current! with { Source = OrderSource.PlayerAi } } } };
        check(SnapshotDiff.FindFirst(saved, changedSource)?.Path == "orders[0].current.source", "current source difference is located");
        var changedGoal = WithPending(pendingIntent with { Move = pendingIntent.Move! with { Goal = new SimVector2(45, 5) } });
        check(SnapshotDiff.FindFirst(saved, changedGoal)?.Path == "orders[0].pending[0].move", "pending goal difference is located");
        var changedOrder = saved with { Orders = new[] { queue with { Pending = Array.Empty<UnitOrderIntent>() } } };
        check(SnapshotDiff.FindFirst(saved, changedOrder)?.Path == "orders[0].pending.count", "pending length difference is located");
        check(RtsMatch.Restore(changedSource, grid).ComputeStateHash() != match.ComputeStateHash(), "order source participates in state hash");
        check(RtsMatch.Restore(changedGoal, grid).ComputeStateHash() != match.ComputeStateHash(), "queued goal participates in state hash");
        try
        {
            ((IList<UnitOrderIntent>)match.ReadUnitOrders()[0].Pending).Clear();
            check(false, "pending view is read-only");
        }
        catch (NotSupportedException) { check(true, "pending view is read-only"); }

        var pendingCommand = CommandEnvelope.MoveTo(5, 0, 3, id, new SimVector2(45, 5), 30, mode: OrderMode.Append);
        match.SubmitCommand(pendingCommand);
        var waiting = match.CaptureSnapshot();
        var next = RtsMatch.Restore(SnapshotJson.Deserialize(SnapshotJson.Serialize(waiting)), grid);
        var changedCommands = waiting.PendingCommands.ToArray();
        changedCommands[0] = changedCommands[0] with { Command = pendingCommand with { Mode = OrderMode.Replace } };
        check(SnapshotDiff.FindFirst(waiting, waiting with { PendingCommands = changedCommands })?.Path == "pendingCommands[0].mode",
            "future command append mode difference is located");
        changedCommands[0] = changedCommands[0] with { Command = pendingCommand with { Source = OrderSource.PlayerAi } };
        check(SnapshotDiff.FindFirst(waiting, waiting with { PendingCommands = changedCommands })?.Path == "pendingCommands[0].source",
            "future command source difference is located");
        changedCommands[0] = changedCommands[0] with { Command = pendingCommand with { Mode = (OrderMode)99 } };
        Invalid(waiting with { PendingCommands = changedCommands }, "invalid future append mode rejected");
        for (var i = 0; i < 10; i++)
        {
            match.Step();
            next.Step();
            check(match.ComputeStateHash() == next.ComputeStateHash(), "future append command preserves restored FIFO state each frame");
        }
        check(!match.SubmitCommand(pendingCommand with { ExecuteFrame = 20, Mode = (OrderMode)99 }).Accepted, "unknown append mode rejected on submit");
        check(!match.SubmitCommand(pendingCommand with { ExecuteFrame = 20, Source = (OrderSource)99 }).Accepted, "unknown source rejected on submit");
        check(!match.SubmitCommand(CommandEnvelope.Stop(20, 0, 10, id) with { Mode = OrderMode.Append }).Accepted, "append stop unsupported at boundary");
        check(!match.SubmitCommand(CommandEnvelope.Spawn(20, 0, 11, default) with { Source = OrderSource.UnitAi }).Accepted,
            "movement metadata cannot leak into unrelated commands");
        match.SubmitCommand(CommandEnvelope.Stop(match.Frame + 1, 0, 12, id));
        match.Step();
        var stop = match.CaptureSnapshot();
        var entity = stop.Entities[0];
        Invalid(stop with { Entities = new[] { entity with { VelocityX = 1 } } }, "persistent stop snapshot cannot contain nonzero velocity");
        var empty = stop with { Orders = new[] { stop.Orders![0] with { Current = null } } };
        Invalid(empty, "empty order queue cannot be serialized as pending activation");
    }
}
