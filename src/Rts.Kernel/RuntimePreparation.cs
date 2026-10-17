using Rts.Kernel.Navigation;

namespace Rts.Kernel;

/// <summary>Once-per-process runtime work in isolated matches, never in the caller's authoritative world.</summary>
internal static class RuntimePreparation
{
    private static readonly Lazy<bool> Ready = new(Initialize);

    internal static void EnsureReady() => _ = Ready.Value;

    private static bool Initialize()
    {
        // Fixed work, independent of wall clock, rendering or the real match's state.
        // Representative world occupancy, with at most 24 members in each command.
        // Two directions cover assignment, arrival, local detours, stop, queue and JSON restore paths.
        foreach (var reverse in new[] { false, true }) RunScenario(reverse);
        return true;
    }

    private static void RunScenario(bool reverse)
    {
        var grid = new PathingGrid(256, 256, 32, default, new byte[256 * 256]);
        var heights = new TerrainHeights(257, 257, 32, default, new SimScalar[257 * 257]);
        var definition = new MovementDefinition(1, 270, 31, new MotionParameters(TurnRate: SimScalar.Parse("0.6")));
        var match = new RtsMatch(MatchConfig.Default, 7, grid, heights, [definition]);
        const int count = 300;
        for (var i = 0; i < count; i++)
        {
            var party = i / 24;
            var member = i % 24;
            Submit(match, CommandEnvelope.Spawn(1, 0, i,
                new(800 + party % 5 * 1300 + member % 5 * 96, 800 + party / 5 * 1300 + member / 5 * 96), 1));
        }
        match.Step();
        match.DrainEvents();
        var ids = match.Entities.Select(entity => entity.Id).ToArray();
        for (var party = 0; party < (count + 23) / 24; party++)
            Submit(match, CommandEnvelope.MoveGroup(2, 0, count + party,
                new(ids.Skip(party * 24).Take(24).ToArray(),
                    new((reverse ? 896 : 1568) + party % 5 * 1300, 992 + party / 5 * 1300))));
        Advance(match, 240);
        var restored = RtsMatch.Restore(SnapshotJson.Deserialize(SnapshotJson.Serialize(match.CaptureSnapshot())),
            grid, heights, [definition]);
        var exiting = restored.Entities.First().Id;
        Submit(restored, CommandEnvelope.MoveGroup(restored.Frame + 1, 0, 400,
            new([exiting], new(800, 992))));
        Submit(restored, CommandEnvelope.MoveGroup(restored.Frame + 1, 0, 401,
            new([exiting], new(1200, 1200)), OrderMode.Append));
        Advance(restored, 120);
        Submit(restored, CommandEnvelope.Stop(restored.Frame + 1, 0, 402, exiting));
        Advance(restored, 2);
    }

    private static void Advance(RtsMatch match, int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            match.Step();
            match.DrainEvents();
        }
    }

    private static void Submit(RtsMatch match, CommandEnvelope command)
    {
        if (!match.SubmitCommand(command).Accepted)
            throw new InvalidOperationException("Runtime preparation fixture command was rejected.");
    }
}
