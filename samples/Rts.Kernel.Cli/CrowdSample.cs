using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class CrowdSample
{
    internal static void Run()
    {
        var grid = new PathingGrid(32, 32, 10, SimVector2.Zero, new byte[1024]);
        var match = new RtsMatch(MatchConfig.Default, 7, grid,
            movementDefinitions: new[] { new MovementDefinition(1, 60, 4) });
        match.SubmitCommand(CommandEnvelope.Spawn(1, 0, 0, new(35, 155), 1));
        match.SubmitCommand(CommandEnvelope.Spawn(1, 0, 1, new(155, 155), 1)); match.Step();
        match.SubmitCommand(CommandEnvelope.Stop(2, 0, 2, new(2)));
        match.SubmitCommand(CommandEnvelope.MoveTo(2, 0, 3, new(1), new(275, 155), 60));
        for (var i = 0; i < 65; i++) match.Step();
        Console.WriteLine($"frame={match.Frame} hash={match.ComputeStateHash()}");
    }
}
