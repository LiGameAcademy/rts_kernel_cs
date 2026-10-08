using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class FormationSample
{
    internal static void Run()
    {
        var grid = new PathingGrid(32, 32, 10, SimVector2.Zero, new byte[1024]);
        var match = new RtsMatch(MatchConfig.Default, 7, grid,
            movementDefinitions: new[] { new MovementDefinition(1, 30, 4), new MovementDefinition(2, 20, 6) });
        for (var i = 0; i < 6; i++)
            match.SubmitCommand(CommandEnvelope.Spawn(1, 0, i, new SimVector2(35 + i * 20, 35), i % 2 == 0 ? 1UL : 2UL));
        match.Step();
        match.SubmitCommand(CommandEnvelope.MoveGroup(2, 0, 6,
            new(match.Entities.Select(entity => entity.Id).ToArray(), new SimVector2(225, 225), FormationKind.Rectangle)));
        for (var frame = 0; frame < 25; frame++) match.Step();
        Console.WriteLine($"formation frame={match.Frame} hash={match.ComputeStateHash()}");
    }
}
