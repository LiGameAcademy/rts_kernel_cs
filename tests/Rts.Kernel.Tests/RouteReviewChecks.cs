using Rts.Kernel;
using Rts.Kernel.Navigation;
using System.Text.Json;

internal static class RouteReviewChecks
{
    internal static void Run(Action<bool, string> check)
    {
        RouteReviewBoundaryChecks.Run(check);
        ArrivalTimeoutChecks.Run(check);
        RunCase(31, [new(1443, 1425), new(2399, 2335), new(2470, 1415), new(2119, 2823), new(1380, 2995)], check);
        RunCase(10, [new(2337, 2186), new(2711, 1201), new(2935, 2829), new(2653, 2878), new(2066, 2735)], check);
        RunCase(80, [new(2586, 2986), new(2242, 2699), new(1250, 2186), new(1919, 2526), new(2573, 1871)], check);
    }

    private static void RunCase(int interval, FixedVector2[] goals, Action<bool, string> check)
    {
        var match = OpenGroundPartyChecks.Create();
        var ids = match.Entities.Select(entity => entity.Id).ToArray();
        var lastCommand = (goals.Length - 1) * interval;
        var completed = 0;
        var failed = 0;
        var reversals = 0;
        var motion = new Dictionary<EntityId, FixedVector2>();
        var elapsed = 0;
        for (; elapsed < lastCommand + 600 && (elapsed <= lastCommand || completed < 24); elapsed++)
        {
            if (elapsed % interval == 0 && elapsed / interval < goals.Length)
            {
                match.SubmitCommand(CommandEnvelope.MoveGroup(match.Frame + 1, 0, 25 + elapsed / interval,
                    new(ids, goals[elapsed / interval])));
                completed = 0;
                failed = 0;
            }
            var before = match.Entities.ToDictionary(entity => entity.Id, entity => entity.Position);
            RtsMatch? restored = null;
            if (elapsed % 37 == 0)
                restored = RtsMatch.Restore(SnapshotJson.Deserialize(SnapshotJson.Serialize(match.CaptureSnapshot())),
                    GroupYieldChecks.Grid, GroupYieldChecks.Heights, [GroupYieldChecks.Footman]);
            match.Step();
            var events = match.DrainEvents();
            if (restored is not null)
            {
                restored.Step();
                check(match.ComputeStateHash() == restored.ComputeStateHash(), "route review JSON state continuation");
                check(JsonSerializer.Serialize(events) == JsonSerializer.Serialize(restored.DrainEvents()),
                    "route review JSON event continuation");
            }
            completed += events.Count(item => item.Kind == MatchEventKind.MoveCompleted);
            failed += events.Count(item => item.Kind is MatchEventKind.CommandRejected or MatchEventKind.MoveFailed);
            foreach (var entity in match.Entities)
            {
                var delta = entity.Position - before[entity.Id];
                check(delta.Length <= 10, "route review cannot teleport or accelerate bodies");
                if (elapsed > lastCommand + 120 && motion.TryGetValue(entity.Id, out var previous)
                    && (Int128)delta.X.Raw * previous.X.Raw + (Int128)delta.Y.Raw * previous.Y.Raw < 0) reversals++;
                if (delta != FixedVector2.Zero) motion[entity.Id] = delta;
            }
        }
        Console.WriteLine($"route_review interval={interval} complete={completed} failed={failed} active={match.ReadMoveOrders().Count} frames_after_last={elapsed - lastCommand} reversals={reversals}");
        check(completed == 24 && failed == 0, "all24 finish repeated replacement without substituting failure for arrival");
        check(elapsed - lastCommand <= 450 && reversals <= 24, "repeated replacements avoid sustained detours and reversals");
        var settled = match.Entities.ToArray();
        for (var idle = 0; idle < 90; idle++)
        {
            match.Step();
            check(settled.SequenceEqual(match.Entities), "reviewed orders remain still after completion");
            check(match.DrainEvents().Count == 0, "reviewed orders complete once");
        }
    }
}
