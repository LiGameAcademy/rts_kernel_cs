using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class FrameworkTraceChecks
{
    // #86: v10 records integer Octile ties by cell index. Equal-cost old floating
    // paths can differ; independent path optimality checks justify this numeric migration.
    internal static void Run(Action<bool, string> check)
    {
        foreach (var count in new[] { 64, 65, 500 })
        {
            var grid = new PathingGrid(64, 64, 10, default, new byte[4096]);
            var definitions = new[] { new MovementDefinition(1, 30, 2) };
            var match = new RtsMatch(MatchConfig.Default, 7, grid, movementDefinitions: definitions);
            var ids = Enumerable.Range(1, count).Select(i => new EntityId((ulong)i)).ToArray();
            for (var i = 0; i < count; i++)
                match.SubmitCommand(CommandEnvelope.Spawn(1, 0, i,
                    new SimVector2(35 + i % 20 * 20, 35 + i / 20 * 20), 1));
            match.SubmitCommand(CommandEnvelope.MoveGroup(2, 0, 1000,
                new GroupMoveRequest(ids, new SimVector2(425, 425))));
            match.SubmitCommand(CommandEnvelope.MoveGroup(3, 0, 1001,
                new GroupMoveRequest(ids, new SimVector2(325, 425)), OrderMode.Append));
            match.SubmitCommand(CommandEnvelope.Stop(4, 0, 1002, ids[0]));
            match.SubmitCommand(CommandEnvelope.MoveTo(5, 0, 1003, ids[1], new SimVector2(55, 525), 30));
            var digest = Trace(match, 24, grid, null, definitions, check);
            var expected = count switch
            {
                64 => "4c01c7fe2dff40b1f6a51bfade3854baa295f8530d503a93dc549c49854718d0",
                65 => "33fc65b3947c72f39a6fded2b10a592294eaf7c394c3bd7487c16438dc6cb975",
                _ => "a312302c2e69c8cc601f3ac44114402e9276d83101630f5f7ee2e869b25519bc",
            };
            check(digest == expected, $"group {count} per-frame hashes/events agree with v10 integer-score baseline");
        }

        var motionGrid = new PathingGrid(16, 16, 10, default, new byte[256]);
        var heights = new TerrainHeights(2, 2, 160, default, new double[] { 0, 20, -10, 30 });
        var motion = new MotionParameters();
        var definitionsWithMotion = new[] { new MovementDefinition(1, 100, 2, motion) };
        var moving = new RtsMatch(MatchConfig.Default, 11, motionGrid, heights, definitionsWithMotion);
        for (var i = 0; i < 3; i++)
            moving.SubmitCommand(CommandEnvelope.Spawn(1, 0, i, new SimVector2(15, 15 + i * 30), i == 2 ? 1UL : 0));
        moving.SubmitCommand(CommandEnvelope.MoveTo(2, 0, 10, new EntityId(1), new SimVector2(125, 115), 100));
        moving.SubmitCommand(CommandEnvelope.MoveTo(2, 0, 11, new EntityId(2), new SimVector2(125, 135), 100, motion: motion));
        moving.SubmitCommand(CommandEnvelope.MoveTo(2, 0, 12, new EntityId(3), new SimVector2(125, 75), 100, motion: motion));
        moving.SubmitCommand(CommandEnvelope.SetObstacle(9, 0, 13, 1, new GridArea(6, 3, 1, 8)));
        moving.SubmitCommand(CommandEnvelope.RemoveObstacle(13, 0, 14, 1));
        check(Trace(moving, 60, motionGrid, heights, definitionsWithMotion, check)
            == "923036647bab016cc7e72a922be7d3e2927d3d8dac861edab498ee0ca7009948",
            "diagnostic/production motion hashes/events agree with v10 integer-score baseline");
    }

    private static string Trace(RtsMatch match, int frames, PathingGrid grid, TerrainHeights? heights,
        IReadOnlyList<MovementDefinition> definitions, Action<bool, string> check)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        RtsMatch? resumed = null;
        for (var frame = 0; frame < frames; frame++)
        {
            match.Step();
            var hash = match.ComputeStateHash();
            var events = JsonSerializer.Serialize(match.DrainEvents());
            digest.AppendData(Encoding.UTF8.GetBytes(hash + "\n" + events + "\n"));
            if (resumed is not null)
            {
                resumed.Step();
                check(hash == resumed.ComputeStateHash(), "framework trace restores per-frame state");
                check(events == JsonSerializer.Serialize(resumed.DrainEvents()), "framework trace restores event sequence");
            }
            if (frame == 7)
                resumed = RtsMatch.Restore(SnapshotJson.Deserialize(SnapshotJson.Serialize(match.CaptureSnapshot())),
                    grid, heights, definitions);
        }
        return Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();
    }
}
