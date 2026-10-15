using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rts.Kernel;
using Rts.Kernel.Navigation;
using Rts.Kernel.Numerics;

namespace NumericPrototype;

/// <summary>Production-kernel parity scenario; the CLI and Godot invoke this same source.</summary>
internal static class FixedKernelProbe
{
    internal static string Run()
    {
        var cases = new List<object>();
        foreach (var count in new[] { 64, 65, 500 })
        {
            var grid = new PathingGrid(64, 64, 10, default, new byte[4096]);
            var definitions = new[] { new MovementDefinition(1, 30, 2) };
            var match = new RtsMatch(MatchConfig.Default, 7, grid, movementDefinitions: definitions);
            var ids = Enumerable.Range(1, count).Select(i => new EntityId((ulong)i)).ToArray();
            for (var i = 0; i < count; i++)
                match.SubmitCommand(CommandEnvelope.Spawn(1, 0, i,
                    new FixedVector2(35 + i % 20 * 20, 35 + i / 20 * 20), 1));
            match.SubmitCommand(CommandEnvelope.MoveGroup(2, 0, 1000, new(ids, new(425, 425))));
            match.SubmitCommand(CommandEnvelope.MoveGroup(3, 0, 1001, new(ids, new(325, 425)), OrderMode.Append));
            match.SubmitCommand(CommandEnvelope.Stop(4, 0, 1002, ids[0]));
            match.SubmitCommand(CommandEnvelope.MoveTo(5, 0, 1003, ids[1], new(55, 525), 30));
            cases.Add(Trace("group_" + count, match, 24, grid, null, definitions));
        }
        var motionGrid = new PathingGrid(16, 16, 10, default, new byte[256]);
        var heights = new TerrainHeights(2, 2, 160, default, new SimScalar[] { 0, 20, -10, 30 });
        var motion = new MotionParameters();
        var movingDefinitions = new[] { new MovementDefinition(1, 100, 2, motion) };
        var moving = new RtsMatch(MatchConfig.Default, 11, motionGrid, heights, movingDefinitions);
        for (var i = 0; i < 3; i++)
            moving.SubmitCommand(CommandEnvelope.Spawn(1, 0, i, new(15, 15 + i * 30), i == 2 ? 1UL : 0));
        moving.SubmitCommand(CommandEnvelope.MoveTo(2, 0, 10, new(1), new(125, 115), 100));
        moving.SubmitCommand(CommandEnvelope.MoveTo(2, 0, 11, new(2), new(125, 135), 100, motion: motion));
        moving.SubmitCommand(CommandEnvelope.MoveTo(2, 0, 12, new(3), new(125, 75), 100, motion: motion));
        moving.SubmitCommand(CommandEnvelope.SetObstacle(9, 0, 13, 1, new(6, 3, 1, 8)));
        moving.SubmitCommand(CommandEnvelope.RemoveObstacle(13, 0, 14, 1));
        cases.Add(Trace("motion_slope_obstacles", moving, 60, motionGrid, heights, movingDefinitions));
        return JsonSerializer.Serialize(new {
            SnapshotVersion = SnapshotJson.CurrentFormatVersion,
            ProductionMvid = typeof(RtsMatch).Assembly.ManifestModule.ModuleVersionId.ToString("D"),
            NumericMvid = typeof(SimScalar).Assembly.ManifestModule.ModuleVersionId.ToString("D"),
            Cases = cases,
            Scope = "production fixed-state synthetic parity; not actual-map or full-crowd acceptance"
        });
    }

    private static object Trace(string name, RtsMatch match, int frames, PathingGrid grid,
        TerrainHeights? terrain, IReadOnlyList<MovementDefinition> definitions)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        RtsMatch? resumed = null;
        var restores = 0;
        var eventCount = 0;
        for (var frame = 0; frame < frames; frame++)
        {
            match.Step();
            var hash = match.ComputeStateHash();
            var events = match.DrainEvents();
            var eventJson = JsonSerializer.Serialize(events);
            digest.AppendData(Encoding.UTF8.GetBytes(hash + "\n" + eventJson + "\n"));
            eventCount += events.Count;
            if (resumed is not null)
            {
                resumed.Step();
                if (hash != resumed.ComputeStateHash() || eventJson != JsonSerializer.Serialize(resumed.DrainEvents()))
                    throw new InvalidOperationException($"Fixed production restore differs: {name}/{frame}");
                restores++;
            }
            resumed = RtsMatch.Restore(SnapshotJson.Deserialize(SnapshotJson.Serialize(match.CaptureSnapshot())),
                grid, terrain, definitions);
        }
        return new { Name = name, Frames = frames, RestoredContinuations = restores,
            EventCount = eventCount, TraceHash = Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant(),
            FinalStateHash = match.ComputeStateHash(), RemainingMoves = match.ReadMoveOrders().Count };
    }
}
