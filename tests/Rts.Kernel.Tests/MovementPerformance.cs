using System.Diagnostics;
using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class MovementPerformance
{
    internal static void Run(bool longRun)
    {
        foreach (var count in new[] { 300, 500 })
        for (var run = 0; run < (longRun ? 1 : 3); run++)
        {
            var definitions = new[] { new MovementDefinition(1, 60, 4), new MovementDefinition(2, 30, 9) };
            var grid = new PathingGrid(64, 64, 10, SimVector2.Zero, new byte[4096]);
            var match = new RtsMatch(MatchConfig.Default, 7, grid, movementDefinitions: definitions);
            for (var i = 0; i < count; i++)
                match.SubmitCommand(CommandEnvelope.Spawn(1, 0, i, new(35 + i % 20 * 20, 35 + i / 20 * 20),
                    i % 3 == 0 ? 2UL : 1UL));
            match.Step(); match.DrainEvents();
            match.SubmitCommand(CommandEnvelope.MoveGroup(2, 0, 1,
                new(match.Entities.Select(e => e.Id).ToArray(), new(325, 325))));
            var planning = new List<double>();
            var planningBytes = 0L;
            do
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                var watch = Stopwatch.StartNew(); match.Step(); watch.Stop();
                planning.Add(watch.Elapsed.TotalMilliseconds);
                planningBytes += GC.GetAllocatedBytesForCurrentThread() - before;
            } while (match.ReadGroupPlans().Count > 0 && planning.Count < 500);
            var outcomes = match.DrainEvents().Where(e => e.Kind == MatchEventKind.GroupMoveAssigned).ToArray();
            var placement = System.Text.Json.JsonSerializer.Serialize(outcomes.Select(e => new { e.EntityId, e.Group }));
            var placementHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(placement))).ToLowerInvariant();
            var goals = outcomes.ToDictionary(e => e.EntityId, e => e.Group!.Goal!.Value);
            Console.WriteLine($"plan count={count} run={run} frames={planning.Count} total_ms={planning.Sum():F3} max_ms={planning.Max():F3} alloc_kb={planningBytes / 1024.0:F1} assigned={goals.Count} placement_hash={placementHash}");
            if (!longRun && run == 1) MeasureReads(match, count);
            var samples = new List<double>();
            var bytes = 0L; var failed = 0; var completed = 0;
            for (var frame = 0; frame < (longRun ? 1800 : 180); frame++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                var watch = Stopwatch.StartNew(); match.Step(); watch.Stop();
                samples.Add(watch.Elapsed.TotalMilliseconds);
                bytes += GC.GetAllocatedBytesForCurrentThread() - before;
                foreach (var e in match.DrainEvents())
                {
                    if (e.Kind == MatchEventKind.MoveFailed) failed++;
                    if (e.Kind == MatchEventKind.MoveCompleted) completed++;
                }
                if (longRun && frame % 300 == 299)
                {
                    var moves = match.ReadMoveOrders();
                    var ordered = match.ReadUnitOrders().Where(q => q.Current?.Group is not null)
                        .OrderBy(q => q.Current!.Group!.Slot).ToArray();
                    var first = ordered.FirstOrDefault();
                    Console.WriteLine($"progress count={count} frame={frame + 1} failed={failed} completed={completed} remaining={moves.Count} physical_wait={moves.Count(o => o.WaitFrames > 0)} first_slot={first?.Current?.Group?.Slot} first_id={first?.EntityId}");
                }
            }
            samples.Sort();
            Console.WriteLine($"move count={count} run={run} frames={samples.Count} mean_ms={samples.Average():F3} p95_ms={samples[(int)(samples.Count * .95) - 1]:F3} max_ms={samples[^1]:F3} alloc_kb_frame={bytes / 1024.0 / samples.Count:F1} failed={failed} completed={completed} remaining={match.ReadMoveOrders().Count} hash={match.ComputeStateHash()}");
        }
    }

    private static void MeasureReads(RtsMatch match, int count)
    {
        foreach (var kind in new[] { "paths", "status" })
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            for (var read = 0; read < 30; read++)
            {
                if (kind == "paths") _ = match.ReadMoveOrders();
                else _ = match.ReadMovementStatuses();
            }
            watch.Stop();
            var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Console.WriteLine($"read count={count} kind={kind} reads=30 mean_ms={watch.Elapsed.TotalMilliseconds / 30:F3} alloc_kb_read={bytes / 1024.0 / 30:F1}");
        }
    }
}
