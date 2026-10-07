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
        match.SubmitCommand(CommandEnvelope.Spawn(1, 0, 0, new SimVector2(15, 15)));
        match.Step();
        match.SubmitCommand(CommandEnvelope.MoveTo(2, 0, 1, new EntityId(1), new SimVector2(65, 15), 45));
        for (var i = 0; i < 25; i++) match.Step();
        Console.WriteLine($"navigation frame={match.Frame} hash={match.ComputeStateHash()}");
    }
}
