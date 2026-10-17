using System.Diagnostics;
using System.Text.Json;
using Rts.Kernel;

/// <summary>Timing/identity checks around the explicit preparation API; never drains the caller's events.</summary>
internal static class PreparationObserver
{
    internal static void Run(RtsMatch match, Action<bool, string> check, string scenario)
    {
        var phase = Stopwatch.StartNew();
        var before = SnapshotJson.Serialize(match.CaptureSnapshot());
        var hash = match.ComputeStateHash();
        var bytes = GC.GetAllocatedBytesForCurrentThread();
        var work = Stopwatch.StartNew();
        match.PrepareForPlay();
        work.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
        check(before == SnapshotJson.Serialize(match.CaptureSnapshot()), "preparation preserves complete authoritative snapshot");
        check(hash == match.ComputeStateHash(), "preparation preserves state hash");
        phase.Stop();
        Console.WriteLine("preparation=" + JsonSerializer.Serialize(new {
            scenario, frame = match.Frame, work_ms = work.Elapsed.TotalMilliseconds,
            total_ms = phase.Elapsed.TotalMilliseconds, alloc_bytes = allocated, state_preserved = true,
            notes = "total includes before/after validation; work includes isolated runtime initialization and match cache build"
        }));
    }
}
