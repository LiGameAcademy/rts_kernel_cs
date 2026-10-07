using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class MovementChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var flags = new byte[48];
        for (var y = 0; y < 4; y++) flags[y * 8 + 3] = 2;
        var grid = new PathingGrid(8, 6, 10, default, flags);
        var id = new EntityId(1);
        var start = new SimVector2(15, 15);
        var goal = new SimVector2(65, 15);
        RtsMatch Create()
        {
            var match = new RtsMatch(MatchConfig.Default, 7, grid);
            match.SubmitCommand(CommandEnvelope.Spawn(1, 0, 0, start)); match.Step(); match.DrainEvents();
            return match;
        }
        void Invalid(Action action, string label)
        {
            try { action(); }
            catch (InvalidDataException) { check(true, label); return; }
            check(false, label);
        }
        var match = Create();
        check(!match.SubmitCommand(CommandEnvelope.MoveTo(2, 0, 1, id, goal, double.NaN)).Accepted, "nonfinite move speed rejected");
        check(!match.SubmitCommand(CommandEnvelope.MoveTo(2, 0, 1, id, goal, 0)).Accepted, "zero move speed rejected");
        check(!match.SubmitCommand(CommandEnvelope.MoveTo(2, 0, 1, id, goal, 45, -1)).Accepted, "negative move clearance rejected");
        match.SubmitCommand(CommandEnvelope.MoveTo(2, 1, 1, id, goal, 45)); match.Step();
        check(!match.IsMoving(id) && match.Entities.Single().Position == start, "other player cannot move entity");
        match.DrainEvents();
        match.SubmitCommand(CommandEnvelope.MoveTo(3, 0, 2, id, goal, 45));
        check(match.Entities.Single().Position == start, "future move does not advance early");
        match.Step();
        check(match.IsMoving(id), "valid goal starts path following");
        var saved = SnapshotJson.Deserialize(SnapshotJson.Serialize(match.CaptureSnapshot()));
        var restored = RtsMatch.Restore(saved, grid);
        Invalid(() => RtsMatch.Restore(saved with { FormatVersion = 2 }, grid), "v2 snapshots explicitly rejected");
        var corrupted = saved.Moves!.ToArray();
        corrupted[0] = corrupted[0] with { NextWaypoint = corrupted[0].Waypoints.Count };
        Invalid(() => RtsMatch.Restore(saved with { Moves = corrupted }, grid), "invalid waypoint index rejected");
        corrupted = saved.Moves!.ToArray();
        var changedPoints = corrupted[0].Waypoints.ToArray(); changedPoints[1] = new SimVector2(35, 15);
        corrupted[0] = corrupted[0] with { Waypoints = changedPoints };
        Invalid(() => RtsMatch.Restore(saved with { Moves = corrupted }, grid), "blocked waypoint rejected");
        check(SnapshotDiff.FindFirst(saved, saved with { Moves = corrupted })?.Path.StartsWith("moves[0].waypoints") == true,
            "move path differences located");
        for (var i = 0; i < 110; i++)
        {
            match.Step(); restored.Step();
            check(match.ComputeStateHash() == restored.ComputeStateHash(), "moving snapshot continuation matches each frame");
            var entity = match.Entities.Single();
            check(grid.TryWorldToCell(entity.Position, out var cell) && grid.IsWalkable(cell), "path never crosses static wall");
            if (match.IsMoving(id))
            {
                var roundTrip = RtsMatch.Restore(SnapshotJson.Deserialize(SnapshotJson.Serialize(match.CaptureSnapshot())), grid);
                check(roundTrip.ComputeStateHash() == match.ComputeStateHash(), "midsegment snapshot round trip");
            }
        }
        check(match.Entities.Single().Position == goal && !match.IsMoving(id), "arrives at exact goal without overshoot");
        check(match.DrainEvents().Single().Kind == MatchEventKind.MoveCompleted, "one arrival event emitted");
        check(match.Entities.Single().Velocity == SimVector2.Zero, "arrival clears velocity");
        var stopped = Create();
        stopped.SubmitCommand(CommandEnvelope.MoveTo(2, 0, 1, id, goal, 45)); stopped.Step();
        stopped.SubmitCommand(CommandEnvelope.Stop(3, 0, 2, id)); stopped.Step();
        var stopPosition = stopped.Entities.Single().Position;
        stopped.Step();
        check(stopped.Entities.Single().Position == stopPosition && !stopped.IsMoving(id), "stop cancels order before integration");
        stopped.SubmitCommand(CommandEnvelope.MoveTo(5, 0, 3, id, new SimVector2(17, 17), 1000)); stopped.Step();
        check(stopped.Entities.Single().Position == new SimVector2(17, 17), "same cell arbitrary goal stops precisely");
        var replaced = Create();
        replaced.SubmitCommand(CommandEnvelope.MoveTo(2, 0, 1, id, goal, 45)); replaced.Step();
        replaced.SubmitCommand(CommandEnvelope.MoveTo(3, 0, 2, id, new SimVector2(35, 15), 45)); replaced.Step();
        check(replaced.ReadMoveOrders().Single().Request.Goal == goal, "invalid replacement preserves old goal");
        replaced.SubmitCommand(CommandEnvelope.MoveTo(4, 0, 3, id, new SimVector2(15, 55), 45)); replaced.Step();
        check(replaced.ReadMoveOrders().Single().Request.Goal == new SimVector2(15, 55), "valid replacement changes goal");
        var dynamic = Create();
        dynamic.SubmitCommand(CommandEnvelope.MoveTo(2, 0, 1, id, goal, 45)); dynamic.Step();
        dynamic.SubmitCommand(CommandEnvelope.SetObstacle(3, 0, 2, 1, new GridArea(0, 4, 8, 2))); dynamic.Step();
        check(!dynamic.IsMoving(id) && dynamic.Entities.Single().Velocity == SimVector2.Zero, "new full wall stops unreachable order");
        check(dynamic.DrainEvents().Single().Kind == MatchEventKind.MoveFailed, "blocked replan reports failure");
        var detour = Create();
        detour.SubmitCommand(CommandEnvelope.MoveTo(2, 0, 1, id, goal, 45)); detour.Step();
        detour.SubmitCommand(CommandEnvelope.SetObstacle(3, 0, 2, 1, new GridArea(2, 3, 1, 1))); detour.Step();
        check(detour.IsMoving(id), "partial new obstacle replans reachable route");
        var pendingMove = Create();
        pendingMove.SubmitCommand(CommandEnvelope.MoveTo(3, 0, 1, id, goal, 45));
        var pendingRestored = RtsMatch.Restore(SnapshotJson.Deserialize(SnapshotJson.Serialize(pendingMove.CaptureSnapshot())), grid);
        for (var i = 0; i < 8; i++)
        {
            pendingMove.Step(); pendingRestored.Step();
            check(pendingMove.ComputeStateHash() == pendingRestored.ComputeStateHash(), "pending move command restored");
        }
        var noGrid = new RtsMatch(MatchConfig.Default, 1);
        noGrid.SubmitCommand(CommandEnvelope.Spawn(1, 0, 0, start)); noGrid.Step(); noGrid.DrainEvents();
        noGrid.SubmitCommand(CommandEnvelope.MoveTo(2, 0, 1, id, goal, 45)); noGrid.Step();
        check(noGrid.DrainEvents().Single().Kind == MatchEventKind.CommandRejected, "mapless move explicitly rejected");
        var velocity = Create();
        velocity.SubmitCommand(CommandEnvelope.SetVelocity(2, 0, 1, id, new SimVector2(100, 0))); velocity.Step();
        check(velocity.Entities.Single().Position == start, "navigation cannot bypass collisions through velocity command");
    }
}
