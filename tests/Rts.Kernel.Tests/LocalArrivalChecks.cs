using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class LocalArrivalChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var grid = new PathingGrid(64, 64, 10, SimVector2.Zero, new byte[4096]);
        var definitions = new[] { new MovementDefinition(1, 60, 4) };
        RtsMatch Create(SimVector2[] starts, SimVector2[] goals)
        {
            var match = new RtsMatch(MatchConfig.Default, 7, grid, movementDefinitions: definitions);
            for (var i = 0; i < starts.Length; i++)
                match.SubmitCommand(CommandEnvelope.Spawn(1, 0, i, starts[i], 1));
            match.Step(); match.DrainEvents();
            // Isolate assigned final corridors from the independent formation layout/matching tests.
            var moves = new List<MoveOrderSnapshot>();
            var queues = new List<UnitOrderQueueSnapshot>();
            for (var i = 0; i < starts.Length; i++)
            {
                var request = new MoveRequest(goals[i], 60);
                var path = new List<SimVector2> { starts[i] };
                var position = starts[i];
                while (position != goals[i])
                {
                    position = new(position.X + Math.Sign(goals[i].X - position.X) * 10,
                        position.Y + Math.Sign(goals[i].Y - position.Y) * 10);
                    path.Add(position);
                }
                moves.Add(new((ulong)i + 1, request, path, 1));
                var group = new GroupSlot(1, i, FormationKind.Rectangle, new(325, 325), 0, true);
                queues.Add(new((ulong)i + 1, new(UnitOrderKind.Move, OrderSource.Player, request, group), []));
            }
            return RtsMatch.Restore(match.CaptureSnapshot() with
                { NextGroupId = 2, Moves = moves, Orders = queues }, grid, movementDefinitions: definitions);
        }
        var distant = Create([new(65, 65), new(605, 35)], [new(305, 65), new(605, 65)]);
        for (var i = 0; i < 20; i++) distant.Step();
        check(distant.IsMoving(new(1)) && !distant.IsMoving(new(2)),
            "distant higher slot completes while earlier member is still moving");

        var adjacent = Create([new(65, 65), new(75, 115)], [new(85, 65), new(75, 85)]);
        adjacent.Step();
        check(adjacent.Entities.Single(e => e.Id == new EntityId(2)).Position.Y < 115,
            "nearby nonconflicting final corridors advance together");

        foreach (var priorGoal in new[] { new SimVector2(85, 65), new SimVector2(105, 65) })
        {
            var conflict = Create([new(65, 65), new(75, 35)], [priorGoal, new(75, 65)]);
            conflict.Step();
            check(conflict.Entities.Single(e => e.Id == new EntityId(2)).Position == new SimVector2(75, 35)
                && conflict.ReadMoveOrders().Single(o => o.EntityId == 2).WaitFrames == 0,
                "conflicting final corridor yields without charging physical timeout");
            var restored = RtsMatch.Restore(conflict.CaptureSnapshot(), grid, movementDefinitions: definitions);
            for (var i = 0; i < 40; i++)
            {
                conflict.Step(); restored.Step();
                check(conflict.ComputeStateHash() == restored.ComputeStateHash(),
                    "cached local neighbors rebuild identical decisions after restore");
            }
            check(conflict.Entities.All(e => !conflict.IsMoving(e.Id))
                && conflict.Entities.Single(e => e.Id == new EntityId(2)).Position == new SimVector2(75, 65),
                "local conflict releases after earlier corridor clears");
            check(!conflict.DrainEvents().Any(e => e.Kind == MatchEventKind.MoveFailed),
                "local corridor ordering does not strand either assigned member");
        }
        foreach (var replace in new[] { false, true })
        {
            var interrupted = Create([new(65, 65), new(75, 35)], [new(85, 65), new(75, 65)]);
            interrupted.Step();
            var earlier = interrupted.Entities.Single(e => e.Id == new EntityId(1)).Position;
            interrupted.SubmitCommand(replace
                ? CommandEnvelope.MoveTo(interrupted.Frame + 1, 0, 1, new(1), new(65, 5), 60)
                : CommandEnvelope.Stop(interrupted.Frame + 1, 0, 1, new(1)));
            interrupted.Step();
            check(interrupted.Entities.Single(e => e.Id == new EntityId(2)).Position.Y > 35,
                "Stop or replacement releases only the affected local dependency");
            if (!replace)
                check(interrupted.Entities.Single(e => e.Id == new EntityId(1)).Position == earlier
                    && interrupted.ReadCurrentOrder(new(1))!.Kind == UnitOrderKind.Stop,
                    "local assembly never pushes the stopped earlier member");
        }
    }
}
