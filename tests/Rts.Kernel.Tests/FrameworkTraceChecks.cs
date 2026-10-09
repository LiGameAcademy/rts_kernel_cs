using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class FrameworkTraceChecks
{
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
                64 => "261ca76fc688de67a6906fb2206041d3582a7d4607b6f0219a330bb9d2f58f74",
                65 => "63502e6b3de8ce55a3c8dd5cc05fe7529e154fc13279fcc38105d6fd5630ff15",
                _ => "5f8618b1b92e8128475d5fbfe9b15ef7a043cf97b0e067cf2c7d7fa43f93fbe1",
            };
            check(digest == expected, $"group {count} per-frame hashes/events agree with cc09efb baseline");
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
            == "2bf86c5f392c75f1d81fe12c9654079ec6b4cc942f281c29ab017c3efe74c052",
            "diagnostic/production motion hashes/events agree with cc09efb baseline");
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
