using Rts.Kernel;
using Rts.Kernel.Navigation;
using System.Text.Json;

internal static class OpenGroundPartyChecks
{
    private static readonly MovementDefinition Footman = new(1, 270, 31,
        new MotionParameters(TurnRate: SimScalar.Parse("0.6")));
    private static readonly PathingGrid Grid = new(128, 128, 32, default, new byte[128 * 128]);
    private static readonly TerrainHeights Heights = new(129, 129, 32, default, new SimScalar[129 * 129]);

    internal static void Run(Action<bool, string> check)
    {
        foreach (var (name, goal, formation) in new[]
        {
            ("east", new FixedVector2(3056, 2064), FormationKind.Compact),
            ("north", new FixedVector2(2064, 3056), FormationKind.Compact),
            ("west", new FixedVector2(1072, 2064), FormationKind.Compact),
            ("diagonal", new FixedVector2(2800, 2800), FormationKind.Compact),
            ("rectangle", new FixedVector2(3056, 2064), FormationKind.Rectangle)
        })
        {
            var match = Create();
            match.SubmitCommand(CommandEnvelope.MoveGroup(2, 0, 25,
                new(match.Entities.Select(entity => entity.Id).ToArray(), goal, formation)));
            RtsMatch? resumed = null;
            var completed = 0;
            var failed = 0;
            var maximumDeviation = SimScalar.Zero;
            var preferences = new Dictionary<EntityId, FixedVector2>();
            for (var frame = 0; frame < 450; frame++)
            {
                var before = match.Entities.ToArray();
                match.Step();
                var events = match.DrainEvents();
                if (resumed is not null)
                {
                    resumed.Step();
                    check(match.ComputeStateHash() == resumed.ComputeStateHash(), $"{name}: live/JSON recovery state");
                    check(JsonSerializer.Serialize(events) == JsonSerializer.Serialize(resumed.DrainEvents()),
                        $"{name}: live/JSON recovery events");
                    resumed = null;
                }
                foreach (var assigned in events.Where(item => item.Kind == MatchEventKind.GroupMoveAssigned))
                    preferences[assigned.EntityId] = assigned.Group!.Goal!.Value;
                completed += events.Count(item => item.Kind == MatchEventKind.MoveCompleted);
                failed += events.Count(item => item.Kind == MatchEventKind.MoveFailed);
                check(!events.Any(item => item.Kind == MatchEventKind.CommandRejected), $"{name}: command accepted");
                var after = match.Entities.ToArray();
                for (var i = 0; i < after.Length; i++)
                for (var j = i + 1; j < after.Length; j++)
                    check(!CrowdGeometry.Overlap(after[i].Position, after[i].Position, Footman.Radius,
                        after[j].Position, after[j].Position, Footman.Radius), $"{name}: bodies remain separate");
                foreach (var entity in after)
                    check(SquaredDistance.Between(before.Single(item => item.Id == entity.Id).Position, entity.Position).Length
                        <= 10, $"{name}: no teleport or speed increase");
                if (frame % 17 == 0 && match.ReadMoveOrders().Count > 0)
                {
                    var saved = SnapshotJson.Deserialize(SnapshotJson.Serialize(match.CaptureSnapshot()));
                    resumed = RtsMatch.Restore(saved, Grid, Heights, [Footman]);
                }
                if (match.ReadMoveOrders().Count != 0) continue;
                foreach (var entity in match.Entities)
                    maximumDeviation = SimMath.Max(maximumDeviation,
                        SquaredDistance.Between(entity.Position, preferences[entity.Id]).Length);
                check(completed == 24 && failed == 0, $"{name}: all24 complete without blockage failures");
                check(maximumDeviation <= SoftGoalPath.Tolerance(Footman.Radius, Grid.CellSize),
                    $"{name}: completion stays in the original preference envelope");
                var settled = match.Entities.ToArray();
                for (var idle = 0; idle < 60; idle++) match.Step();
                check(settled.SequenceEqual(match.Entities), $"{name}: no movement after completion");
                Console.WriteLine($"Open ground {name}: 24/24 at {frame + 1} frames; maximum offset {maximumDeviation}");
                break;
            }
            check(completed == 24 && failed == 0, $"{name}: bounded completion deadline");
        }
        StopAndQueue(check);
        PathBoundaries(check);
    }

    private static void PathBoundaries(Action<bool, string> check)
    {
        var flags = new byte[100];
        flags[4 * 10 + 5] = 2;
        var navigation = new NavigationState(new PathingGrid(10, 10, 10, default, flags));
        check(StraightMovePath.TryFind(navigation, new(45, 45), new(65, 65), 0) is null,
            "straight travel cannot cut the blocked side of a diagonal corner");
        check(StraightMovePath.TryFind(navigation, new(15, 15), new(85, 25), 0) is not null,
            "clear oblique travel does not need a grid-centre staircase");
        var match = Create();
        match.SubmitCommand(CommandEnvelope.MoveGroup(2, 0, 25,
            new(match.Entities.Select(entity => entity.Id).ToArray(), new(3056, 2064))));
        match.Step();
        var saved = match.CaptureSnapshot();
        var moves = saved.Moves!.ToArray();
        var entity = match.Entities.Single(item => item.Id.Value == moves[0].EntityId);
        var illegalGoal = new FixedVector2(moves[0].Request.Goal.X + 512, moves[0].Request.Goal.Y);
        var points = StraightMovePath.TryFind(new NavigationState(Grid), entity.Position, illegalGoal, 1)!;
        moves[0] = moves[0] with { Waypoints = points, NextWaypoint = 0 };
        var rejected = false;
        try { RtsMatch.Restore(saved with { Moves = moves }, Grid, Heights, [Footman]); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected, "snapshot cannot extend a soft path beyond its preferred-goal envelope");
        rejected = false;
        try { RtsMatch.Restore(saved with { FormatVersion = 11 }, Grid, Heights, [Footman]); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected, "v11 snapshot is explicitly rejected after soft endpoint contract change");
    }

    private static void StopAndQueue(Action<bool, string> check)
    {
        var match = Create();
        var ids = match.Entities.Select(entity => entity.Id).ToArray();
        match.SubmitCommand(CommandEnvelope.MoveGroup(2, 0, 25, new(ids, new(3056, 2064))));
        for (var i = 0; i < 30; i++) match.Step();
        var stopped = match.Entities.First();
        match.SubmitCommand(CommandEnvelope.Stop(match.Frame + 1, 0, 26, stopped.Id));
        match.DrainEvents();
        for (var i = 0; i < 600; i++) match.Step();
        check(match.Entities.First().Position == stopped.Position
            && match.ReadCurrentOrder(stopped.Id)?.Kind == UnitOrderKind.Stop, "soft party respects player Stop");
        var queued = new RtsMatch(MatchConfig.Default, 7, Grid, Heights, [Footman]);
        queued.SubmitCommand(CommandEnvelope.Spawn(1, 0, 0, new(2064, 2064), 1));
        queued.SubmitCommand(CommandEnvelope.Spawn(1, 0, 1, new(2064, 2256), 1));
        queued.Step(); queued.DrainEvents();
        queued.SubmitCommand(CommandEnvelope.MoveGroup(2, 0, 2, new([new(1), new(2)], new(3056, 2064))));
        queued.SubmitCommand(CommandEnvelope.MoveTo(3, 0, 3, new(1), new(3408, 2064),
            Footman.Speed, 1, Footman.Motion, OrderMode.Append));
        for (var i = 0; i < 500; i++) queued.Step();
        check(queued.Entities.First().Position == new FixedVector2(3408, 2064),
            "soft completion activates the next exact queued order");
    }

    internal static RtsMatch Create()
    {
        var match = new RtsMatch(MatchConfig.Default, 7, Grid, Heights, [Footman]);
        var points = new List<FixedVector2> { new(2064, 2064) };
        for (var ring = 1; points.Count < 24; ring++)
        {
            for (var x = -ring; x <= ring; x++)
            {
                points.Add(new(2064 + x * 96, 2064 - ring * 96));
                points.Add(new(2064 + x * 96, 2064 + ring * 96));
            }
            for (var y = -ring + 1; y < ring; y++)
            {
                points.Add(new(2064 - ring * 96, 2064 + y * 96));
                points.Add(new(2064 + ring * 96, 2064 + y * 96));
            }
        }
        for (var i = 0; i < 24; i++)
            match.SubmitCommand(CommandEnvelope.Spawn(1, 0, i, points[i], Footman.Id));
        match.Step(); match.DrainEvents();
        return match;
    }
}
