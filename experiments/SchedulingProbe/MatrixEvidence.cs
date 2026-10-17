using System.Text.Json;
using Microsoft.Diagnostics.Tracing;

/// <summary>Raw ETL evidence for the fixed matrix. No retroactive rundown mapping or inferred net frame time.</summary>
internal static class MatrixEvidence
{
    internal static void Export(string etl, string log, string output)
    {
        if (File.Exists(output)) throw new IOException("Refusing to overwrite matrix evidence.");
        var frames = File.ReadLines(log).Where(l => l.StartsWith("frame_sample="))
            .Select(l => JsonDocument.Parse(l[13..]).RootElement.Clone())
            .Where(f => f.GetProperty("phase").GetString() == "command" || f.GetProperty("over_budget").GetBoolean())
            .Select(f => new FrameWindow(f)).ToArray();
        var target = frames[0];
        var transitions = new List<ThreadTransition>();
        var samples = new List<(long Qpc, ulong Ip)>();
        var interrupts = new List<InterruptSpan>();
        var methods = new List<CodeVersion>();
        var latest = new Dictionary<ulong, CodeVersion>();
        var runtimeEvents = 0;
        using var source = new ETWTraceEventSource(etl);
        source.Clr.All += e => { if (e.ProcessID == target.ProcessId) runtimeEvents++; };
        source.Clr.MethodLoadVerbose += e =>
        {
            if (e.ProcessID != target.ProcessId || e.MethodSize <= 0) return;
            var qpc = TraceClock.Qpc(e);
            if (latest.TryGetValue(e.MethodStartAddress, out var previous)) previous.CloseAt(qpc);
            var method = new CodeVersion(qpc, e.MethodStartAddress, e.MethodSize,
                e.MethodNamespace + "." + e.MethodName, e.MethodSignature, e.MethodID, e.ReJITID, e.OptimizationTier.ToString());
            latest[e.MethodStartAddress] = method;
            methods.Add(method);
        };
        source.Clr.MethodUnloadVerbose += e =>
        {
            if (e.ProcessID == target.ProcessId && latest.TryGetValue(e.MethodStartAddress, out var method)
                && method.MethodId == e.MethodID && method.ReJitId == e.ReJITID)
                method.CloseAt(TraceClock.Qpc(e));
        };
        source.Kernel.ThreadCSwitch += e =>
        {
            var qpc = TraceClock.Qpc(e);
            if (e.OldThreadID == target.ThreadId)
                transitions.Add(new(qpc, "switch-out", FrameWindow.AfterSwitchOut(e.OldThreadState), e.ProcessorNumber,
                    e.OldThreadWaitReason.ToString(), 0, e.NewThreadID, "", null, new(null, [])));
            if (e.NewThreadID == target.ThreadId)
                transitions.Add(new(qpc, "switch-in", "running", e.ProcessorNumber, "", 0, e.OldThreadID, "", null, new(null, [])));
        };
        source.Kernel.DispatcherReadyThread += e =>
        {
            if (e.AwakenedThreadID == target.ThreadId)
                transitions.Add(new(TraceClock.Qpc(e), "ready", "ready", e.ProcessorNumber, e.AdjustReason.ToString(),
                    0, e.ThreadID, "", null, new(null, [])));
        };
        source.Kernel.PerfInfoSample += e =>
        {
            if (e.ThreadID == target.ThreadId && frames.Any(f => TraceClock.Qpc(e) >= f.Start && TraceClock.Qpc(e) <= f.End))
                samples.Add((TraceClock.Qpc(e), e.InstructionPointer));
        };
        void Interrupt(TraceEvent e, double elapsedMs, string kind)
        {
            if (!double.IsFinite(elapsedMs) || elapsedMs < 0) throw new InvalidDataException("Invalid interrupt interval.");
            var end = TraceClock.Qpc(e);
            interrupts.Add(new(end - checked((long)Math.Round(elapsedMs * target.Frequency / 1000)), end, e.ProcessorNumber, kind));
        }
        source.Kernel.PerfInfoDPC += e => Interrupt(e, e.ElapsedTimeMSec, "DPC");
        source.Kernel.PerfInfoISR += e => Interrupt(e, e.ElapsedTimeMSec, "ISR");
        source.Process();
        transitions = transitions.OrderBy(t => t.Qpc).ToList();
        var results = frames.Select(frame =>
        {
            var segments = frame.ReadSegments(transitions);
            var hits = samples.Where(s => s.Qpc >= frame.Start && s.Qpc <= frame.End).Select(s => new {
                qpc = s.Qpc, instruction_pointer = s.Ip,
                method = methods.LastOrDefault(m => m.Covers(s.Qpc, s.Ip))
            }).ToArray();
            return new { timing = frame.Summarize(transitions, source.EventsLost), cpu_samples = hits,
                interrupt_union_ms = InterruptOverlap.Ticks(segments, interrupts) * 1000.0 / frame.Frequency,
                dpc_ms = InterruptOverlap.Ticks(segments, interrupts.Where(i => i.Kind == "DPC").ToArray()) * 1000.0 / frame.Frequency,
                isr_ms = InterruptOverlap.Ticks(segments, interrupts.Where(i => i.Kind == "ISR").ToArray()) * 1000.0 / frame.Frequency };
        }).ToArray();
        var kernelMethods = methods.Where(m => m.Name.StartsWith("Rts.Kernel.", StringComparison.Ordinal)).ToArray();
        File.WriteAllText(output, JsonSerializer.Serialize(new {
            process_id = target.ProcessId, native_thread_id = target.ThreadId, runtime_events = runtimeEvents,
            method_loads = methods.Count, kernel_method_versions = kernelMethods, events_lost = source.EventsLost,
            interrupts = interrupts.Count, frames = results,
            notes = "Only runtime MethodLoad intervals map earlier samples; no rundown backfill. "
                + "MethodId/ReJitId/addresses are process local. Same tier/size does not prove identical machine code. "
                + "Interrupts unioned only on scheduled target CPU; never subtract from the wall-clock release gate."
        }, new JsonSerializerOptions { WriteIndented = true }));
        if (runtimeEvents == 0 || kernelMethods.Length == 0 || source.EventsLost != 0)
            throw new InvalidDataException("Matrix CLR metadata or complete event coverage missing; saved evidence remains diagnostic.");
        Console.WriteLine($"Matrix ETL export: {results.Length} frames, {methods.Count} method versions, lost={source.EventsLost}.");
    }
}

internal sealed class CodeVersion(long loadQpc, ulong start, int size, string name, string signature, long methodId, long reJitId, string tier)
{
    public long LoadQpc { get; } = loadQpc;
    public long EndQpc { get; private set; } = long.MaxValue;
    public ulong Start { get; } = start;
    public int Size { get; } = size;
    public string Name { get; } = name;
    public string Signature { get; } = signature;
    public long MethodId { get; } = methodId;
    public long ReJitId { get; } = reJitId;
    public string Tier { get; } = tier;
    internal void CloseAt(long qpc) => EndQpc = Math.Min(EndQpc, qpc);
    internal bool Covers(long qpc, ulong ip) => qpc >= LoadQpc && qpc < EndQpc && ip >= Start && ip - Start < (ulong)Size;

    internal static void Check()
    {
        var method = new CodeVersion(10, 1000, 20, "test", "", 1, 0, "test");
        if (method.Covers(9, 1000) || method.Covers(12, 999) || method.Covers(12, 1020) || !method.Covers(12, 1019))
            throw new InvalidOperationException("Method time/address boundary mapped incorrectly.");
        method.CloseAt(20);
        method.CloseAt(30); // A later address reuse must not extend past the original unload.
        if (method.EndQpc != 20 || method.Covers(20, 1000) || !method.Covers(19, 1000))
            throw new InvalidOperationException("Unloaded method range revived by a later load.");
        Console.WriteLine("Method lifetime self-check PASS: load/unload and address bounds, reused address never revives old code.");
    }
}
