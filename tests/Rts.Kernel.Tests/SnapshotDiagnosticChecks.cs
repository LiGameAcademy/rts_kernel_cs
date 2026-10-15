using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class SnapshotDiagnosticChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var grid = new PathingGrid(4, 4, 10, default, new byte[16]);
        var match = new RtsMatch(MatchConfig.Default, 1, grid);
        match.SubmitCommand(CommandEnvelope.SetObstacle(2, 0, 0, 1, new GridArea(1, 1, 1, 1)));
        match.SubmitCommand(CommandEnvelope.MoveTo(2, 0, 1, new EntityId(1), new FixedVector2(15, 15), 10));
        match.SubmitCommand(CommandEnvelope.MoveGroup(2, 0, 2,
            new GroupMoveRequest(new[] { new EntityId(1) }, new FixedVector2(25, 25))));
        match.SubmitCommand(CommandEnvelope.Spawn(2, 0, 3, default, 1));
        var saved = match.CaptureSnapshot();

        void Expect(int index, Func<CommandEnvelope, CommandEnvelope> change, string path)
        {
            var pending = saved.PendingCommands.ToArray();
            pending[index] = pending[index] with { Command = change(pending[index].Command) };
            var changed = saved with { PendingCommands = pending };
            check(SnapshotDiff.FindFirst(saved, changed)?.Path == path, "command diagnostic: " + path);
            check(SnapshotDiff.FindFirst(changed, saved)?.Path == path, "reverse command diagnostic: " + path);
        }

        Expect(0, c => c with { Sequence = 7 }, "pendingCommands[0].sequence");
        Expect(0, c => c with { Obstacle = null }, "pendingCommands[0].command.obstacle.present");
        Expect(0, c => c with { Obstacle = c.Obstacle! with { Id = 3 } }, "pendingCommands[0].command.obstacle.id");
        Expect(0, c => c with { Obstacle = c.Obstacle! with { Area = new GridArea(2, 1, 1, 1) } },
            "pendingCommands[0].command.obstacle.area");
        Expect(1, c => c with { Move = c.Move! with { Speed = 20 } }, "pendingCommands[1].command.move");
        Expect(2, c => c with { Group = c.Group! with { Goal = new FixedVector2(35, 35) } }, "pendingCommands[2].group");
        Expect(3, c => c with { MovementDefinitionId = 2 }, "pendingCommands[3].movementDefinitionId");

        var commands = saved.PendingCommands.ToArray();
        commands[0] = commands[0] with { Command = commands[0].Command with { Obstacle = null } };
        var navigation = saved.Navigation! with { MapHash = new string('a', 64) };
        check(SnapshotDiff.FindFirst(saved, saved with { PendingCommands = commands, Navigation = navigation })?.Path
            == "navigation.mapHash", "navigation state still precedes obstacle payload diagnostics");
        commands[0] = commands[0] with { Command = commands[0].Command with { Sequence = 8 } };
        check(SnapshotDiff.FindFirst(saved, saved with { PendingCommands = commands, Navigation = navigation })?.Path
            == "pendingCommands[0].sequence", "command metadata still precedes navigation diagnostics");
        commands = saved.PendingCommands.ToArray();
        commands[2] = commands[2] with { Command = commands[2].Command with { Group = null } };
        check(SnapshotDiff.FindFirst(saved, saved with { PendingCommands = commands, NextGroupId = 2 })?.Path
            == "nextGroupId", "group state still precedes group command payload diagnostics");
        check(SnapshotDiff.FindFirst(saved, RtsMatch.Restore(saved, grid).CaptureSnapshot()) is null,
            "independently restored command collections compare by value");
    }
}
