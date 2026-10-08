using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class GroupMovementChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var definitions = new[] { new MovementDefinition(1, 60, 4), new MovementDefinition(2, 30, 9) };
        var grid = new PathingGrid(64, 64, 10, SimVector2.Zero, new byte[4096]);
        RtsMatch Create(int count = 6)
        {
            var match = new RtsMatch(MatchConfig.Default, 7, grid, movementDefinitions: definitions);
            for (var i = 0; i < count; i++)
                match.SubmitCommand(CommandEnvelope.Spawn(1, 0, i, new SimVector2(35 + i % 20 * 20, 35 + i / 20 * 20),
                    i % 3 == 0 ? 2UL : 1UL));
            match.Step(); match.DrainEvents(); return match;
        }
        GroupMoveRequest Request(RtsMatch match, FormationKind kind = FormationKind.Compact) =>
            new(match.Entities.Select(entity => entity.Id).ToArray(), new SimVector2(325, 325), kind);
        foreach (var kind in Enum.GetValues<FormationKind>())
        {
            var match = Create();
            var request = Request(match, kind);
            var input = request.EntityIds.ToArray();
            check(match.SubmitCommand(CommandEnvelope.MoveGroup(2, 0, 9, request with { EntityIds = input })).Accepted,
                $"{kind}: group accepted");
            Array.Reverse(input);
            var submitted = match.CaptureSnapshot();
            var resumed = RtsMatch.Restore(SnapshotJson.Deserialize(SnapshotJson.Serialize(submitted)), grid,
                movementDefinitions: definitions.Reverse().ToArray());
            check(match.ComputeStateHash() == resumed.ComputeStateHash(), $"{kind}: frozen input and sorted definition identity");
            match.Step(); resumed.Step();
            var assigned = match.DrainEvents().Where(e => e.Kind == MatchEventKind.GroupMoveAssigned).ToArray();
            check(assigned.Length == 6 && assigned.All(e => e.Group is { Goal: not null }), $"{kind}: actual per-unit assigned results");
            var orders = match.ReadUnitOrders();
            check(orders.All(queue => queue.Current!.Move!.Speed == (queue.EntityId % 3 == 1 ? 30 : 60)),
                $"{kind}: authoritative per-unit speed");
            var separated = true;
            for (var i = 0; i < orders.Count; i++)
            for (var j = i + 1; j < orders.Count; j++)
            {
                var a = orders[i].Current!.Move!.Goal; var b = orders[j].Current!.Move!.Goal;
                var radius = match.ReadMovementDefinition(new EntityId(orders[i].EntityId))!.Radius
                    + match.ReadMovementDefinition(new EntityId(orders[j].EntityId))!.Radius;
                separated &= (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) >= radius * radius;
            }
            check(separated, $"{kind}: mixed bodies never overlap at destinations");
            var planned = match.CaptureSnapshot();
            resumed = RtsMatch.Restore(SnapshotJson.Deserialize(SnapshotJson.Serialize(planned)), grid, movementDefinitions: definitions);
            for (var frame = 0; frame < 25; frame++)
            {
                match.Step(); resumed.Step();
                check(match.ComputeStateHash() == resumed.ComputeStateHash(), $"{kind}: in-flight per-frame group recovery {frame}");
            }
            for (var frame = 0; frame < 1000; frame++) match.Step();
            check(match.Entities.All(entity => !match.IsMoving(entity.Id)), $"{kind}: all independently arrive");
            check(match.Entities.All(entity => assigned.Single(e => e.EntityId == entity.Id).Group!.Goal == entity.Position),
                $"{kind}: arrived positions equal assigned formation goals");
        }
        var aMatch = Create(); var bMatch = Create();
        var aRequest = Request(aMatch);
        aMatch.SubmitCommand(CommandEnvelope.MoveGroup(2, 0, 1, aRequest));
        bMatch.SubmitCommand(CommandEnvelope.MoveGroup(2, 0, 1, aRequest with { EntityIds = aRequest.EntityIds.Reverse().ToArray() }));
        aMatch.Step(); bMatch.Step();
        check(aMatch.ComputeStateHash() == bMatch.ComputeStateHash(), "selection order cannot change group result");
        aMatch.SubmitCommand(CommandEnvelope.MoveGroup(3, 0, 2, aRequest with { Goal = new SimVector2(425, 425), Formation = FormationKind.Rectangle }, OrderMode.Append));
        aMatch.Step();
        check(aMatch.ReadUnitOrders().All(queue => queue.Pending.Count == 1 && queue.Pending[0].Group!.GroupId == 2), "append preserves current group and persists second group slots");
        var queued = aMatch.CaptureSnapshot();
        var restored = RtsMatch.Restore(queued, grid, movementDefinitions: definitions);
        for (var i = 0; i < 300; i++) { aMatch.Step(); restored.Step(); }
        check(aMatch.ComputeStateHash() == restored.ComputeStateHash(), "queued group restore reaches same future state");
        var persistent = Create(1);
        persistent.SubmitCommand(CommandEnvelope.Stop(2, 0, 1, new EntityId(1)));
        persistent.SubmitCommand(CommandEnvelope.MoveGroup(2, 0, 2, Request(persistent), source: OrderSource.UnitAi));
        persistent.Step();
        check(persistent.ReadCurrentOrder(new EntityId(1))!.Kind == UnitOrderKind.Stop, "unit AI group cannot replace player Stop");
        var duplicate = aRequest with { EntityIds = new[] { new EntityId(1), new EntityId(1) } };
        var hash = aMatch.ComputeStateHash();
        check(!aMatch.SubmitCommand(CommandEnvelope.MoveGroup(aMatch.Frame + 1, 0, 3, duplicate)).Accepted
            && hash == aMatch.ComputeStateHash(), "duplicate member rejection is atomic");
        foreach (var bad in new[] { definitions.Select(d => d with { Speed = d.Speed + 1 }).ToArray(), Array.Empty<MovementDefinition>() })
        {
            var rejected = false;
            try { RtsMatch.Restore(queued, grid, movementDefinitions: bad); } catch (InvalidDataException) { rejected = true; }
            check(rejected, "different movement definitions reject recovery");
        }
        var fake = queued with { NextGroupId = 1 };
        var invalid = false;
        try { SnapshotValidator.Validate(fake); } catch (InvalidDataException) { invalid = true; }
        check(invalid, "snapshot rejects invalid group counter");
        check(SnapshotDiff.FindFirst(queued, queued with { NextGroupId = 100 })?.Path == "nextGroupId", "group counter field difference exposed");
        var blockFlags = new byte[4096];
        for (var y = 0; y < 64; y++) blockFlags[y * 64 + 30] = 2;
        var disconnectedGrid = new PathingGrid(64, 64, 10, SimVector2.Zero, blockFlags);
        var blocked = new RtsMatch(MatchConfig.Default, 7, disconnectedGrid, movementDefinitions: definitions);
        blocked.SubmitCommand(CommandEnvelope.Spawn(1, 0, 0, new SimVector2(35, 35), 1)); blocked.Step(); blocked.DrainEvents();
        blocked.SubmitCommand(CommandEnvelope.MoveGroup(2, 0, 1, new(new[] { new EntityId(1) }, new SimVector2(505, 505)))); blocked.Step();
        check(blocked.DrainEvents().Any(e => e.Detail == "group_slot_unavailable") && !blocked.IsMoving(new EntityId(1)), "disconnected goal reports failure without teleporting or unbounded search");
        var large = Create(500);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        large.SubmitCommand(CommandEnvelope.MoveGroup(2, 0, 1, Request(large)));
        var frames = 0;
        var maximum = 0.0;
        do
        {
            var frameWatch = System.Diagnostics.Stopwatch.StartNew();
            large.Step();
            maximum = Math.Max(maximum, frameWatch.Elapsed.TotalMilliseconds);
            frames++;
        } while (large.ReadGroupPlans().Count > 0 && frames < 500);
        watch.Stop();
        var outcomes = large.DrainEvents().Where(e => e.Kind == MatchEventKind.GroupMoveAssigned).ToArray();
        check(outcomes.Length == 500, "500 mixed-size units receive reachable distinct endpoints");
        Console.WriteLine($"group_plan_500 elapsed_ms={watch.Elapsed.TotalMilliseconds:F3} frames={frames} max_frame_ms={maximum:F3}");
        var samples = new List<double>();
        var separatedDuringMotion = true;
        var failures = 0;
        for (var i = 0; i < 180; i++)
        {
            var frameWatch = System.Diagnostics.Stopwatch.StartNew();
            large.Step();
            samples.Add(frameWatch.Elapsed.TotalMilliseconds);
            failures += large.DrainEvents().Count(e => e.Kind == MatchEventKind.MoveFailed);
            var entities = large.Entities.ToArray();
            for (var a = 0; a < entities.Length; a++)
            for (var b = a + 1; b < entities.Length; b++)
            {
                var radius = large.ReadMovementDefinition(entities[a].Id)!.Radius + large.ReadMovementDefinition(entities[b].Id)!.Radius;
                var dx = entities[a].Position.X - entities[b].Position.X;
                var dy = entities[a].Position.Y - entities[b].Position.Y;
                separatedDuringMotion &= dx * dx + dy * dy >= radius * radius - 1e-8;
            }
            if (i == 90)
            {
                var resumedLarge = RtsMatch.Restore(large.CaptureSnapshot(), grid, movementDefinitions: definitions);
                resumedLarge.Step(); large.Step();
                check(resumedLarge.ComputeStateHash() == large.ComputeStateHash(), "500-unit crowd restores same next-frame decisions");
            }
        }
        check(separatedDuringMotion, "500-unit continuous movement preserves body separation each frame");
        samples.Sort();
        Console.WriteLine($"crowd_500 frames=180 mean_ms={samples.Average():F3} p95_ms={samples[170]:F3} max_ms={samples[^1]:F3} failures={failures} remaining={large.ReadMoveOrders().Count}");
    }
}
