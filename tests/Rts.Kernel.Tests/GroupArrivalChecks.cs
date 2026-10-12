using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class GroupArrivalChecks
{
    internal static void Run(Action<bool, string> check)
    {
        LocalArrivalChecks.Run(check);
        var grid = new PathingGrid(64, 64, 10, SimVector2.Zero, new byte[4096]);
        var definitions = new[] { new MovementDefinition(1, 60, 4), new MovementDefinition(2, 20, 6) };
        RtsMatch Create(int count)
        {
            var match = new RtsMatch(MatchConfig.Default, 7, grid, movementDefinitions: definitions);
            for (var i = 0; i < count; i++)
                match.SubmitCommand(CommandEnvelope.Spawn(1, 0, i, new(35 + i % 10 * 20, 35 + i / 10 * 20),
                    i == 0 ? 2UL : 1UL));
            match.Step(); match.DrainEvents();
            match.SubmitCommand(CommandEnvelope.MoveGroup(2, 0, count,
                new(match.Entities.Select(e => e.Id).ToArray(), new(325, 325), FormationKind.Rectangle)));
            match.Step(); return match;
        }
        var match = Create(12);
        var assignments = match.DrainEvents().Where(e => e.Kind == MatchEventKind.GroupMoveAssigned).ToArray();
        var completed = new List<int>();
        for (var i = 0; i < 1800 && match.ReadMoveOrders().Count > 0; i++)
        {
            match.Step();
            foreach (var result in match.DrainEvents())
            {
                if (result.Kind == MatchEventKind.MoveCompleted)
                    completed.Add(assignments.Single(e => e.EntityId == result.EntityId).Group!.Slot);
                check(result.Kind != MatchEventKind.MoveFailed, "compact arrival sequencing does not strand a member");
            }
            if (i == 200)
            {
                var resumed = RtsMatch.Restore(SnapshotJson.Deserialize(SnapshotJson.Serialize(match.CaptureSnapshot())),
                    grid, movementDefinitions: definitions);
                match.Step(); resumed.Step();
                check(match.ComputeStateHash() == resumed.ComputeStateHash(), "assembly yield restores without presentation state");
            }
        }
        check(completed.Count == 12, "local approach sequencing completes all mixed-speed members");
        check(match.Entities.All(e => e.Position == assignments.Single(a => a.EntityId == e.Id).Group!.Goal),
            "twelve-member compact formation reaches every assigned goal");

        var stopped = Create(6);
        var goals = stopped.DrainEvents().Where(e => e.Kind == MatchEventKind.GroupMoveAssigned).ToArray();
        var anchor = goals.Single(e => e.Group!.Slot == 0).EntityId;
        stopped.SubmitCommand(CommandEnvelope.Stop(stopped.Frame + 1, 0, 20, anchor));
        for (var i = 0; i < 1200; i++) stopped.Step();
        check(stopped.Entities.Where(e => e.Id != anchor).All(e => e.Position == goals.Single(a => a.EntityId == e.Id).Group!.Goal),
            "player Stop releases earlier assembly slot without moving stopped member");
        check(stopped.ReadCurrentOrder(anchor)!.Kind == UnitOrderKind.Stop, "released assembly leader retains Stop intent");
    }
}
