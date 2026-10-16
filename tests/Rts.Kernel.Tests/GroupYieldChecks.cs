using Rts.Kernel;
using Rts.Kernel.Navigation;
using System.Text.Json;

internal static class GroupYieldChecks
{
    internal static readonly MovementDefinition Footman = new(1, 270, 31,
        new MotionParameters(TurnRate: SimScalar.Parse("0.6")));
    internal static readonly PathingGrid Grid = new(128, 128, 32, default, new byte[128 * 128]);
    internal static readonly TerrainHeights Heights = new(129, 129, 32, default, new SimScalar[129 * 129]);

    internal static void Run(Action<bool, string> check)
    {
        foreach (var direction in new[] { new FixedVector2(-800, 0), new FixedVector2(0, -800), new FixedVector2(-600, -600) })
            ExitSettledParty(direction, check);
        ExitSettledParty(new(-800, 0), check, hostCommand: true);
        GroupYieldBoundaryChecks.Run(check);
    }

    internal static RtsMatch Create(Action<bool, string> check)
    {
        var match = new RtsMatch(MatchConfig.Default, 7, Grid, Heights, [Footman]);
        for (var index = 0; index < 24; index++)
            match.SubmitCommand(CommandEnvelope.Spawn(1, 0, index,
                new(1200 + index % 5 * 96, 1800 + index / 5 * 96), 1));
        match.Step(); match.DrainEvents();
        match.SubmitCommand(CommandEnvelope.MoveGroup(2, 0, 24,
            new(match.Entities.Select(entity => entity.Id).ToArray(), new(2608, 2064))));
        var completed = 0;
        for (var frame = 0; frame < 600; frame++)
        {
            match.Step();
            var events = match.DrainEvents();
            completed += events.Count(item => item.Kind == MatchEventKind.MoveCompleted);
            check(!events.Any(item => item.Kind == MatchEventKind.MoveFailed), "dense party setup has no failed members");
            if (completed == 24) break;
        }
        check(completed == 24, "24 party members finish setup");
        check(match.CaptureSnapshot().Cooperation!.Members.Count == 24, "completed party retains cooperation permission");
        return match;
    }

    private static void ExitSettledParty(FixedVector2 direction, Action<bool, string> check, bool hostCommand = false)
    {
        var match = Create(check);
        var centre = match.Entities.Aggregate(FixedVector2.Zero, (sum, entity) => sum + entity.Position) / 24;
        var exiting = match.Entities.OrderBy(entity => (entity.Position - centre).Length).ThenBy(entity => entity.Id).First();
        var goal = exiting.Position + direction;
        if (hostCommand)
            match.SubmitCommand(CommandEnvelope.MoveGroup(match.Frame + 1, 0, 25, new([exiting.Id], goal)));
        else
            match.SubmitCommand(CommandEnvelope.MoveTo(match.Frame + 1, 0, 25, exiting.Id, goal, Footman.Speed, 1, Footman.Motion));
        var completed = 0;
        var yielded = new HashSet<ulong>();
        RtsMatch? restored = null;
        for (var frame = 0; frame < 600; frame++)
        {
            var before = match.Entities.ToArray();
            match.Step();
            var events = match.DrainEvents();
            if (restored is not null)
            {
                restored.Step();
                check(match.ComputeStateHash() == restored.ComputeStateHash(), "yield JSON resume state matches");
                check(JsonSerializer.Serialize(events) == JsonSerializer.Serialize(restored.DrainEvents()), "yield JSON resume events match");
                restored = null;
            }
            foreach (var task in match.CaptureSnapshot().Cooperation!.Yields) yielded.Add(task.EntityId);
            var after = match.Entities.ToArray();
            foreach (var entity in after)
                check((entity.Position - before.Single(item => item.Id == entity.Id).Position).Length <= 10,
                    "yielding uses normal speed, not teleporting");
            for (var i = 0; i < after.Length; i++)
            for (var j = i + 1; j < after.Length; j++)
                check(!CrowdGeometry.Overlap(after[i].Position, after[i].Position, 31,
                    after[j].Position, after[j].Position, 31), "yielding bodies remain separated");
            check(!events.Any(item => item.Kind == MatchEventKind.MoveFailed), "internal reverse exit does not time out");
            completed += events.Count(item => item.Kind == MatchEventKind.MoveCompleted && item.EntityId == exiting.Id);
            if (frame % 7 == 0)
                restored = RtsMatch.Restore(SnapshotJson.Deserialize(SnapshotJson.Serialize(match.CaptureSnapshot())), Grid, Heights, [Footman]);
            if (completed == 0) continue;
            var endpoint = match.Entities.Single(item => item.Id == exiting.Id).Position;
            check(hostCommand ? (endpoint - goal).Length <= 156 + 32 : endpoint == goal, "interior member reaches reverse goal envelope");
            check(yielded.Count >= 2, "exit exercises a multi-member yielding chain");
            var stationary = match.Entities.ToArray();
            for (var idle = 0; idle < 90; idle++) match.Step();
            check(stationary.SequenceEqual(match.Entities), "helpers do not return to slots or oscillate after exit");
            Console.WriteLine($"Dense exit {direction} (host={hostCommand}): completed in {frame + 1} frames, {yielded.Count} helpers");
            break;
        }
        check(completed == 1, "internal reverse exit completes once within deadline");
    }
}
