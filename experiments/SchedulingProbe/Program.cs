using System.Text.Json;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;

if (args.SequenceEqual(new[] { "--metadata-fixture-handshake" })) { MetadataCalibration.RunFixture(handshake: true); return; }
if (args.Length == 4 && args[0] == "--providers")
{
    Environment.ExitCode = ProviderSnapshot.Write(int.Parse(args[1]), args[2], args[3]) ? 0 : 1;
    return;
}
if (args.SequenceEqual(new[] { "--metadata-fixture" })) { MetadataCalibration.RunFixture(); return; }
if (args.Length == 4 && args[0] == "--calibrate")
{
    Environment.ExitCode = MetadataCalibration.Check(args[1], args[2], args[3]) ? 0 : 1;
    return;
}
if (args.SequenceEqual(new[] { "--self-test" })) { SchedulingChecks.Run(); ProviderSnapshotChecks.Run(); InterruptOverlap.Check(); CodeVersion.Check(); return; }
if (args.Length == 4 && args[0] == "--matrix-evidence") { MatrixEvidence.Export(args[1], args[2], args[3]); return; }
if (args.Length != 3) throw new ArgumentException("Usage: SchedulingProbe trace.etl|trace.etlx probe.log output.json");
var frames = File.ReadLines(args[1]).Where(line => line.StartsWith("frame_sample="))
    .Select(line => JsonDocument.Parse(line[13..]).RootElement.Clone())
    .Where(frame => frame.GetProperty("phase").GetString() == "command"
        || (frame.GetProperty("over_budget").GetBoolean() && frame.GetProperty("phase").GetString() != "spawn"))
    .Select(frame => new FrameWindow(frame)).ToArray();
if (frames.Length < 10) throw new InvalidDataException("Missing command/restore frame records.");
if (frames.Select(f => (f.ProcessId, f.ThreadId, f.Frequency)).Distinct().Count() != 1)
    throw new InvalidDataException("Expected a single native probe thread and clock.");
if (frames.Any(f => f.ProcessId <= 0 || f.ThreadId <= 0 || f.Frequency <= 0 || f.Start >= f.End))
    throw new InvalidDataException("Invalid native thread or QPC interval.");
var target = frames[0];
var indexedInput = Path.GetExtension(args[0]).Equals(".etlx", StringComparison.OrdinalIgnoreCase);
var etlx = indexedInput ? args[0] : Path.ChangeExtension(args[0], ".etlx");
if (!indexedInput)
{
    if (File.Exists(etlx)) throw new IOException("Index exists; pass its .etlx path explicitly to read it.");
    Console.WriteLine("Indexing ETL with existing TraceEvent; no symbols downloaded.");
    TraceLog.CreateFromEventTraceLogFile(args[0], etlx);
}
using var log = new TraceLog(etlx);
var transitions = new List<ThreadTransition>();
var competitors = new List<object>();
var cpuSamples = new List<object>();
var observedTargetProcess = false;
long minimumQpc = long.MaxValue, maximumQpc = long.MinValue;
double minimumMs = 0, maximumMs = 0;
using var source = log.Events.GetSource();
source.AllEvents += e =>
{
    if (TraceClock.Qpc(e) < minimumQpc) { minimumQpc = TraceClock.Qpc(e); minimumMs = e.TimeStampRelativeMSec; }
    if (TraceClock.Qpc(e) > maximumQpc) { maximumQpc = TraceClock.Qpc(e); maximumMs = e.TimeStampRelativeMSec; }
};
StackEvidence Stack(TraceEvent e, bool blocking = false)
{
    // CSwitch's regular stack belongs to the incoming thread; BlockingStack is the outgoing thread.
    var index = blocking && e is CSwitchTraceData change ? change.BlockingStack() : log.GetCallStackIndexForEvent(e);
    if (index == CallStackIndex.Invalid) return new(null, []);
    var stackThread = log.CallStacks.Thread(index);
    var owner = stackThread?.ThreadID;
    if (blocking && (owner != target.ThreadId || stackThread?.Process.ProcessID != target.ProcessId))
        throw new InvalidDataException("Outgoing stack does not belong to the target thread.");
    var result = new List<string>();
    for (var stack = log.CallStacks[index]; stack is not null && result.Count < 64; stack = stack.Caller)
    {
        var code = stack.CodeAddress;
        result.Add($"{code.ModuleName}!{code.FullMethodName} [0x{code.Address:x}]");
    }
    return new(owner, result.ToArray());
}
source.Kernel.ThreadCSwitch += e =>
{
    var qpc = TraceClock.Qpc(e);
    if ((e.OldThreadID == target.ThreadId && e.OldProcessID == target.ProcessId)
        || (e.NewThreadID == target.ThreadId && e.NewProcessID == target.ProcessId)) observedTargetProcess = true;
    if (e.OldThreadID == target.ThreadId)
        transitions.Add(new(qpc, "switch-out", FrameWindow.AfterSwitchOut(e.OldThreadState),
            e.ProcessorNumber, e.OldThreadWaitReason.ToString(), e.NewProcessID, e.NewThreadID,
            e.NewProcessName, e.NewThreadPriority, Stack(e, blocking: true)));
    if (e.NewThreadID == target.ThreadId)
        transitions.Add(new(qpc, "switch-in", "running", e.ProcessorNumber, "", e.OldProcessID,
            e.OldThreadID, e.OldProcessName, e.OldThreadPriority, Stack(e)));
    if (frames.Any(f => qpc >= f.Start && qpc <= f.End))
        competitors.Add(new { qpc, cpu = e.ProcessorNumber, old_tid = e.OldThreadID, new_tid = e.NewThreadID,
            old_pid = e.OldProcessID, new_pid = e.NewProcessID, old_process = e.OldProcessName,
            new_process = e.NewProcessName, old_priority = e.OldThreadPriority, new_priority = e.NewThreadPriority,
            old_state = e.OldThreadState.ToString(), wait_reason = e.OldThreadWaitReason.ToString() });
};
source.Kernel.DispatcherReadyThread += e =>
{
    if (e.AwakenedThreadID == target.ThreadId)
        transitions.Add(new(TraceClock.Qpc(e), "ready", "ready", e.ProcessorNumber, e.AdjustReason.ToString(),
            e.ProcessID, e.ThreadID, e.ProcessName, null, Stack(e)));
};
source.Kernel.PerfInfoSample += e =>
{
    if (e.ThreadID == target.ThreadId && frames.Any(f => TraceClock.Qpc(e) >= f.Start && TraceClock.Qpc(e) <= f.End))
        cpuSamples.Add(new { qpc = TraceClock.Qpc(e), cpu = e.ProcessorNumber, stack = Stack(e) });
};
source.Process();
if (!observedTargetProcess) throw new InvalidDataException("Target PID/TID was not observed in this trace.");
// Preserve ETL event order for transitions with the same QPC.
transitions = transitions.OrderBy(t => t.Qpc).ToList();
var clockErrorMs = Math.Abs((maximumQpc - minimumQpc) * 1000.0 / target.Frequency - (maximumMs - minimumMs));
if (clockErrorMs > 0.01 || frames.Any(f => f.Start < minimumQpc || f.End > maximumQpc))
    throw new InvalidDataException("ETL and frame QPC clocks do not align or trace does not cover frames.");
var summaries = frames.Select(f => f.Summarize(transitions, log.EventsLost, log.Truncated)).ToArray();
File.WriteAllText(args[2], JsonSerializer.Serialize(new {
    process_id = target.ProcessId, native_thread_id = target.ThreadId, qpc_frequency = target.Frequency,
    trace_events_lost = log.EventsLost, truncated = log.Truncated, qpc_clock_error_ms = clockErrorMs,
    trace_start_qpc = minimumQpc, trace_end_qpc = maximumQpc, target_transitions = transitions.Count, target_process_verified = observedTargetProcess,
    notes = "Scheduled running includes unmeasured DPC/ISR; unresolved symbols remain addresses. "
        + "Intervals are clipped to Step; raw ETL is authoritative; no wall-clock failures discarded.",
    frames = summaries, cpu_samples = cpuSamples, competing_switches = competitors
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Exported {summaries.Length} frames, {transitions.Count} transitions; lost={log.EventsLost}, clock_error_ms={clockErrorMs:F6}");
