using System.Diagnostics;
using System.Text.Json;
using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class PreparedBoundaryChecks
{
    internal static void Run(Action<bool, string> check)
    {
        const int count = 300;
        foreach (var scenario in new[] { "restore", "obstacle-add", "obstacle-replace", "obstacle-remove" })
        {
            var original = new RtsMatch(MatchConfig.Default, 7, MultiPartyCloseoutChecks.Grid,
                MultiPartyCloseoutChecks.Heights, [MultiPartyCloseoutChecks.Footman]);
            MultiPartyCloseoutChecks.QueueSpawns(original, count);
            original.Step(); original.DrainEvents();
            if (scenario is "obstacle-replace" or "obstacle-remove")
            {
                original.SubmitCommand(CommandEnvelope.SetObstacle(original.Frame + 1, 0, 900, 1, new(0, 10, 256, 1)));
                original.Step(); original.DrainEvents();
            }
            var restored = Restore(original.CaptureSnapshot());
            var control = Restore(original.CaptureSnapshot());
            var records = new FrameSpikeLog(20);
            var maximumMs = 0.0;
            PreparationObserver.Run(restored, check, scenario + "-loaded");
            if (scenario != "restore")
            {
                CommandEnvelope Edit(RtsMatch match) => scenario == "obstacle-remove"
                    ? CommandEnvelope.RemoveObstacle(match.Frame + 1, 0, 901, 1)
                    : CommandEnvelope.SetObstacle(match.Frame + 1, 0, 901, 1,
                        new(0, scenario == "obstacle-add" ? 10 : 12, 256, 1));
                restored.SubmitCommand(Edit(restored)); control.SubmitCommand(Edit(control));
                maximumMs = Step(restored, records, -2);
                control.Step(); Compare(restored, control, check);
                var status = restored.FindPath(new(3, 5), new(3, 20)).Status;
                check(status == (scenario == "obstacle-remove" ? PathStatus.Found : PathStatus.Unreachable),
                    "obstacle change affects actual reachability before re-preparation");
                PreparationObserver.Run(restored, check, scenario + "-changed");
            }
            MultiPartyCloseoutChecks.QueueMoves(restored, count);
            MultiPartyCloseoutChecks.QueueMoves(control, count);
            var firstMs = Step(restored, records, 0);
            control.Step(); Compare(restored, control, check);
            maximumMs = Math.Max(maximumMs, firstMs);
            for (var frame = 1; frame <= 12; frame++)
            {
                maximumMs = Math.Max(maximumMs, Step(restored, records, frame));
                control.Step(); Compare(restored, control, check);
            }
            records.Report(count, 0, scenario);
            Console.WriteLine($"prepared_boundary scenario={scenario} first_command_ms={firstMs:F3} max_ms={maximumMs:F3}"
                + $" budget33={(maximumMs <= 1000.0 / 30 ? "PASS" : "OVER")}");
        }
    }

    private static double Step(RtsMatch match, FrameSpikeLog records, int tick)
    {
        var counters = FrameSpikeLog.ReadCounters();
        var bytes = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        match.Step();
        var end = Stopwatch.GetTimestamp();
        var after = FrameSpikeLog.ReadCounters();
        records.Record(tick, start, end, GC.GetAllocatedBytesForCurrentThread() - bytes, counters, after);
        return Stopwatch.GetElapsedTime(start, end).TotalMilliseconds;
    }

    private static RtsMatch Restore(MatchSnapshot snapshot) => RtsMatch.Restore(
        SnapshotJson.Deserialize(SnapshotJson.Serialize(snapshot)), MultiPartyCloseoutChecks.Grid,
        MultiPartyCloseoutChecks.Heights, [MultiPartyCloseoutChecks.Footman]);

    private static void Compare(RtsMatch actual, RtsMatch control, Action<bool, string> check)
    {
        check(actual.ComputeStateHash() == control.ComputeStateHash(), "prepared restore/edit continues with identical state");
        var events = actual.DrainEvents();
        check(!events.Any(item => item.Kind is MatchEventKind.CommandRejected or MatchEventKind.MoveFailed),
            "restored/edit command remains accepted and reachable");
        check(JsonSerializer.Serialize(events) == JsonSerializer.Serialize(control.DrainEvents()),
            "prepared restore/edit continues with identical event order");
    }
}
