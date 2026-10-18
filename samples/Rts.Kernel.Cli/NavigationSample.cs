using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class NavigationSample
{
    internal static void Run()
    {
        var flags = new byte[48];
        for (var y = 0; y < 4; y++) flags[y * 8 + 3] = 2;
        var grid = new PathingGrid(8, 6, 10, default, flags);
        var match = new RtsMatch(MatchConfig.Default, 7, grid);
        var start = new SimVector2(15, 15);
        var target = new SimVector2(65, 15);
        var entityId = new EntityId(1);

        Console.WriteLine("RTS kernel navigation demo (no Godot runtime)");
        Console.WriteLine("Map: 8 x 6 cells, with a wall; commands run automatically.");
        match.SubmitCommand(CommandEnvelope.Spawn(1, 0, 0, start));
        match.Step();
        var initial = match.Entities.Single();
        Console.WriteLine($"Initial position: ({initial.Position.X:F3}, {initial.Position.Y:F3})");
        Console.WriteLine($"Move target:      ({target.X:F3}, {target.Y:F3})");
        match.SubmitCommand(CommandEnvelope.MoveTo(2, 0, 1, entityId, target, 45));
        for (var i = 0; i < 25; i++)
        {
            match.Step();
        }

        var entity = match.Entities.Single();
        Console.WriteLine($"Position after 25 steps: ({entity.Position.X:F3}, {entity.Position.Y:F3})");
        Console.WriteLine($"Movement status: {(match.IsMoving(entityId) ? "still moving" : "stopped")}");
        Console.WriteLine($"navigation frame={match.Frame} hash={match.ComputeStateHash()}");
    }
}
