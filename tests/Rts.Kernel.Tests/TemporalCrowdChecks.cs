using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class TemporalCrowdChecks
{
    internal static void Run(Action<bool, string> check)
    {
        TimedTrajectoryChecks.Run(check);
        var grid = new PathingGrid(128, 128, 32, default, new byte[128 * 128]);
        var terrain = new TerrainHeights(129, 129, 32, default, new SimScalar[129 * 129]);
        var definition = new MovementDefinition(1, 270, 31, new MotionParameters(TurnRate: SimScalar.FromRatio(3, 5)));
        foreach (var reverseIds in new[] { false, true })
        {
            var match = new RtsMatch(MatchConfig.Default, 7, grid, terrain, [definition]);
            var starts = reverseIds ? new[] { new FixedVector2(2128, 2064), new FixedVector2(2064, 2064) }
                : new[] { new FixedVector2(2064, 2064), new FixedVector2(2128, 2064) };
            for (var i = 0; i < 2; i++) match.SubmitCommand(CommandEnvelope.Spawn(1, 0, i, starts[i], 1));
            match.Step();
            match.DrainEvents();
            foreach (var entity in match.Entities)
                match.SubmitCommand(CommandEnvelope.MoveTo(2, 0, (long)entity.Id.Value + 2, entity.Id,
                    new(entity.Position.X + 992, entity.Position.Y), 270, 1, definition.Motion));
            var restored = RtsMatch.Restore(match.CaptureSnapshot(), grid, terrain, [definition]);
            for (var frame = 1; frame <= 30; frame++)
            {
                match.Step();
                restored.Step();
                check(match.ComputeStateHash() == restored.ComputeStateHash(), "same-time motion restores deterministically");
                foreach (var entity in match.Entities)
                {
                    var start = starts[(int)entity.Id.Value - 1];
                    check(entity.Position.Y == start.Y, "parallel footmen do not detour sideways");
                    check(SimMath.Abs(entity.Position.X - start.X - frame * 9) < SimScalar.FromRatio(1, 1000),
                        "parallel footmen retain full forward progress in either ID order");
                }
            }
        }
    }
}
