using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class OrdersSample
{
    internal static void Run()
    {
        var grid = new PathingGrid(8, 4, 10, default, new byte[32]);
        var match = new RtsMatch(MatchConfig.Default, 7, grid);
        var id = new EntityId(1);
        var motion = new MotionParameters(ScaleSlopeSpeed: false);
        match.SubmitCommand(CommandEnvelope.Spawn(1, 0, 0, new SimVector2(5, 5)));
        match.Step();
        match.SubmitCommand(CommandEnvelope.MoveTo(2, 0, 1, id, new SimVector2(65, 5), 30, motion: motion));
        match.SubmitCommand(CommandEnvelope.MoveTo(2, 0, 2, id, new SimVector2(25, 5), 30,
            motion: motion, mode: OrderMode.Append));
        for (var i = 0; i < 25; i++) match.Step();
        Console.WriteLine($"orders frame={match.Frame} hash={match.ComputeStateHash()}");
    }
}
