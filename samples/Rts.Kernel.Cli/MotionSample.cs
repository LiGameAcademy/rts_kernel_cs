using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class MotionSample
{
    internal static void Run()
    {
        var flags = new byte[48];
        for (var y = 0; y < 4; y++) flags[y * 8 + 3] = 2;
        var grid = new PathingGrid(8, 6, 10, default, flags);
        var heights = Enumerable.Range(0, 63).Select(i => (i % 9) * 10.0).ToArray();
        var terrain = new TerrainHeights(9, 7, 10, default, heights);
        var match = new RtsMatch(MatchConfig.Default, 7, grid, terrain);
        match.SubmitCommand(CommandEnvelope.Spawn(1, 0, 0, new SimVector2(15, 15)));
        match.Step();
        match.SubmitCommand(CommandEnvelope.MoveTo(2, 0, 1, new EntityId(1), new SimVector2(65, 15), 45,
            motion: new MotionParameters()));
        for (var i = 0; i < 25; i++) match.Step();
        Console.WriteLine($"motion frame={match.Frame} hash={match.ComputeStateHash()}");
    }
}
