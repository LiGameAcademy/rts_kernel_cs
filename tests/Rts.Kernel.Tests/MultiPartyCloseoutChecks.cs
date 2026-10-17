using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
using System.Text.Json;
using Rts.Kernel;
using Rts.Kernel.Navigation;

/// <summary>Release acceptance probe: independent small parties share one populated match.</summary>
internal static class MultiPartyCloseoutChecks
{
    internal static readonly MovementDefinition Footman = new(1, 270, 31,
        new MotionParameters(TurnRate: SimScalar.Parse("0.6")));
    internal static readonly PathingGrid Grid = new(256, 256, 32, default, new byte[256 * 256]);
    internal static readonly TerrainHeights Heights = new(257, 257, 32, default, new SimScalar[257 * 257]);

    internal static void Run(Action<bool, string> check, bool prepared = false)
    {
        foreach (var count in new[] { 300, 500 })
        for (var run = 0; run < 3; run++) Measure(count, run, check, prepared);
    }

    internal static void QueueSpawns(RtsMatch match, int count)
    {
        for (var index = 0; index < count; index++)
        {
            var party = index / 24;
            var member = index % 24;
            match.SubmitCommand(CommandEnvelope.Spawn(1, 0, index,
                new(800 + party % 5 * 1300 + member % 5 * 96,
                    800 + party / 5 * 1300 + member / 5 * 96), 1));
        }
    }

    internal static void QueueMoves(RtsMatch match, int count)
    {
        var ids = match.Entities.Select(entity => entity.Id).ToArray();
        var parties = (count + 23) / 24;
        for (var party = 0; party < parties; party++)
            match.SubmitCommand(CommandEnvelope.MoveGroup(match.Frame + 1, 0, count + party,
                new(ids.Skip(party * 24).Take(24).ToArray(),
                    new(1568 + party % 5 * 1300, 992 + party / 5 * 1300))));

    }

    private static void Measure(int count, int run, Action<bool, string> check, bool prepared)
    {
        var match = new RtsMatch(MatchConfig.Default, 7, Grid, Heights, [Footman]);
        if (prepared) PreparationObserver.Run(match, check, $"fresh-{count}-{run}");
        QueueSpawns(match, count);
        var spawnLog = new FrameSpikeLog(1);
        var spawnCounters = FrameSpikeLog.ReadCounters();
        var spawnBytes = GC.GetAllocatedBytesForCurrentThread();
        var spawnWatch = Stopwatch.StartNew();
        match.Step();
        spawnWatch.Stop();
        var spawnAfter = FrameSpikeLog.ReadCounters();
        spawnLog.Record(-1, spawnWatch.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - spawnBytes, spawnCounters, spawnAfter);
        check(match.Entities.Count == count, "multi-party fixture spawns all bodies");
        match.DrainEvents();
        QueueMoves(match, count);

        using var trajectory = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var spikeLog = new FrameSpikeLog(450);
        var samples = new List<double>(450);
        var allocated = 0L;
        var completed = new HashSet<EntityId>();
        var failed = 0;
        var assigned = 0;
        var commandMs = 0.0;
        var restoreChecks = 0;
        RtsMatch? resumed = null;
        var replayRemaining = 0;
        for (var frame = 0; frame < 450 && completed.Count + failed < count; frame++)
        {
            // Include command execution/planning in the first measured tick.
            var countersBefore = FrameSpikeLog.ReadCounters();
            var before = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            match.Step();
            watch.Stop();
            var countersAfter = FrameSpikeLog.ReadCounters();
            var frameBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            spikeLog.Record(frame, watch.Elapsed.TotalMilliseconds, frameBytes, countersBefore, countersAfter);
            allocated += frameBytes;
            samples.Add(watch.Elapsed.TotalMilliseconds);
            if (frame == 0) commandMs = watch.Elapsed.TotalMilliseconds;
            var events = match.DrainEvents();
            trajectory.AppendData(Encoding.UTF8.GetBytes(match.ComputeStateHash() + "\n" + JsonSerializer.Serialize(events) + "\n"));
            check(!events.Any(item => item.Kind == MatchEventKind.CommandRejected), "all small-party commands accepted");
            assigned += events.Count(item => item.Kind == MatchEventKind.GroupMoveAssigned);
            failed += events.Count(item => item.Kind == MatchEventKind.MoveFailed);
            foreach (var item in events.Where(item => item.Kind == MatchEventKind.MoveCompleted))
                check(completed.Add(item.EntityId), "multi-party completion is exactly once");

            // Replay the restored world against the uninterrupted live world.
            // Hashing/serialization and replay are outside tick measurements.
            if (resumed is not null)
            {
                resumed.Step();
                check(match.ComputeStateHash() == resumed.ComputeStateHash(), "populated live/JSON replay state");
                check(JsonSerializer.Serialize(events) == JsonSerializer.Serialize(resumed.DrainEvents()),
                    "populated live/JSON replay events");
                restoreChecks++;
                if (--replayRemaining == 0) resumed = null;
            }
            if (frame is 0 or 30)
            {
                resumed = RtsMatch.Restore(SnapshotJson.Deserialize(SnapshotJson.Serialize(match.CaptureSnapshot())),
                    Grid, Heights, [Footman]);
                check(resumed.ComputeStateHash() == match.ComputeStateHash(), "populated match JSON restores raw state");
                replayRemaining = 12;
            }
        }
        var bodies = match.Entities.ToArray();
        for (var i = 0; i < bodies.Length; i++)
        for (var j = i + 1; j < bodies.Length; j++)
            check(!CrowdGeometry.Overlap(bodies[i].Position, bodies[i].Position, 31,
                bodies[j].Position, bodies[j].Position, 31), "multi-party final bodies remain separate");
        check(assigned == count && completed.Count == count && failed == 0, "all small parties finish without failure");
        var idle = match.Entities.ToArray();
        for (var frame = 0; frame < 90; frame++) match.Step();
        check(idle.SequenceEqual(match.Entities), "all parties remain settled");
        spawnLog.Report(count, run);
        spikeLog.Report(count, run);
        samples.Sort();
        var p95 = samples[(int)Math.Ceiling(samples.Count * 0.95) - 1];
        var parties = (count + 23) / 24;
        var maximumMs = Math.Max(samples[^1], spawnWatch.Elapsed.TotalMilliseconds);
        var remaining = match.ReadUnitOrders().Count(queue => queue.Current?.Kind == UnitOrderKind.Move);
        Console.WriteLine($"multi_party count={count} parties={parties} run={run} ticks={samples.Count} command_ms={commandMs:F3}"
            + $" mean_ms={samples.Average():F3} p95_ms={p95:F3} max_ms={maximumMs:F3} first_step_ms={spawnWatch.Elapsed.TotalMilliseconds:F3} prepared={prepared} alloc_kb_tick={allocated / 1024.0 / samples.Count:F1}"
            + $" assigned={assigned} completed={completed.Count} failed={failed} remaining={remaining} replay_ticks={restoreChecks}"
            + $" trace={Convert.ToHexString(trajectory.GetHashAndReset())} hash={match.ComputeStateHash()} budget33={(maximumMs <= 1000.0 / 30 ? "PASS" : "OVER")}");
    }
}
