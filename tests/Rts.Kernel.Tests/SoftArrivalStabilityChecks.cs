using Rts.Kernel;
using Rts.Kernel.Navigation;
using System.Text.Json;

internal static class SoftArrivalStabilityChecks
{
    internal static void Run(Action<bool, string> check)
    {
        CheckNearbyGoals(check);
        var match = OpenGroundPartyChecks.Create();
        match.SubmitCommand(CommandEnvelope.MoveGroup(2, 0, 25,
            new(match.Entities.Select(entity => entity.Id).ToArray(), new(1490, 2390))));
        var completed = new HashSet<EntityId>();
        var previousMotion = new Dictionary<EntityId, FixedVector2>();
        var preferences = new Dictionary<EntityId, FixedVector2>();
        var reversals = 0;
        var elapsed = 0;
        for (; elapsed < 240 && completed.Count < 24; elapsed++)
        {
            var before = match.Entities.ToDictionary(entity => entity.Id, entity => entity.Position);
            RtsMatch? restored = null;
            if (elapsed % 17 == 0)
                restored = RtsMatch.Restore(SnapshotJson.Deserialize(SnapshotJson.Serialize(match.CaptureSnapshot())),
                    GroupYieldChecks.Grid, GroupYieldChecks.Heights, [GroupYieldChecks.Footman]);
            match.Step();
            var events = match.DrainEvents();
            if (restored is not null)
            {
                restored.Step();
                check(match.ComputeStateHash() == restored.ComputeStateHash(), "soft arrival JSON continuation state");
                check(JsonSerializer.Serialize(events) == JsonSerializer.Serialize(restored.DrainEvents()),
                    "soft arrival JSON continuation events");
            }
            check(!events.Any(item => item.Kind is MatchEventKind.MoveFailed or MatchEventKind.CommandRejected),
                "soft arrival does not trade oscillation for failure");
            foreach (var item in events.Where(item => item.Kind == MatchEventKind.GroupMoveAssigned))
                preferences[item.EntityId] = item.Group!.Goal!.Value;
            foreach (var item in events.Where(item => item.Kind == MatchEventKind.MoveCompleted))
                check(completed.Add(item.EntityId), "each soft order completes once");
            var bodies = match.Entities.ToArray();
            foreach (var entity in bodies)
            {
                var delta = entity.Position - before[entity.Id];
                check(delta.Length <= 10, "soft arrival retains speed and has no teleport");
                if (previousMotion.TryGetValue(entity.Id, out var previous)
                    && (Int128)delta.X.Raw * previous.X.Raw + (Int128)delta.Y.Raw * previous.Y.Raw < 0)
                    reversals++;
                if (delta != FixedVector2.Zero) previousMotion[entity.Id] = delta;
            }
            for (var i = 0; i < bodies.Length; i++)
            for (var j = i + 1; j < bodies.Length; j++)
                check(!CrowdGeometry.Overlap(bodies[i].Position, bodies[i].Position, 31,
                    bodies[j].Position, bodies[j].Position, 31), "soft arrival keeps bodies separate");
        }
        // Original ring/goal regression: 306 frames, 43 reversals. Allow modest
        // legitimate turns, but reject the sustained retry/backtracking sequence.
        check(completed.Count == 24, "24 soft arrivals settle within 240 frames");
        check(reversals <= 24, "soft arrival avoids repeated terminal backtracking");
        foreach (var entity in match.Entities)
            check(SquaredDistance.Between(entity.Position, preferences[entity.Id]).Length <= 156,
                "soft arrival keeps the existing preference envelope");
        var settled = match.Entities.ToArray();
        for (var idle = 0; idle < 90; idle++)
        {
            match.Step();
            check(settled.SequenceEqual(match.Entities), "soft arrival remains still after completion");
            check(match.DrainEvents().Count == 0, "settled orders do not complete twice");
        }
        Console.WriteLine($"Soft arrival regression: 24/24 in {elapsed} frames, {reversals} reversals, 90 stable frames");
    }
    private static void CheckNearbyGoals(Action<bool, string> check)
    {
        // Keep the surrounding directions: optimizing one destination must not
        // turn a previously successful open-ground move into a blocked failure.
        for (var index = 0; index < 32; index++)
        {
            var match = OpenGroundPartyChecks.Create();
            var goal = new FixedVector2(1300 + index % 8 * 190, 1400 + index / 8 * 330);
            match.SubmitCommand(CommandEnvelope.MoveGroup(2, 0, 25,
                new(match.Entities.Select(entity => entity.Id).ToArray(), goal)));
            var completed = 0;
            for (var frame = 0; frame < 450 && completed < 24; frame++)
            {
                match.Step();
                var events = match.DrainEvents();
                check(!events.Any(item => item.Kind is MatchEventKind.MoveFailed or MatchEventKind.CommandRejected),
                    $"nearby soft goal {goal}: no new failure");
                completed += events.Count(item => item.Kind == MatchEventKind.MoveCompleted);
            }
            check(completed == 24, $"nearby soft goal {goal}: all24 arrive");
        }
    }
}
