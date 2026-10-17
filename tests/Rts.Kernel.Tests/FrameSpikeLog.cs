using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;

/// <summary>Probe-only observations. Buffered outside Step; never consulted by simulation.</summary>
internal sealed class FrameSpikeLog(int capacity)
{
    private readonly Sample[] _samples = new Sample[capacity];
    private int _count;

    internal readonly record struct Counters(int Gen0, int Gen1, int Gen2, long PauseTicks, long JitTicks,
        ThreadWorkObservation? ThreadWork);
    private readonly record struct Sample(int Tick, long StartQpc, long EndQpc, long Bytes, Counters Delta)
    {
        internal double ElapsedMs => Stopwatch.GetElapsedTime(StartQpc, EndQpc).TotalMilliseconds;
    }

    // Invoke the same reader before/after Step, so first-call compilation precedes the measured window.
    internal static Counters ReadCounters()
    {
        // Read the native helper before JIT time so its first compilation is not attributed to Step.
        var threadWork = ThreadWorkObservation.Read();
        return new(GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2),
            GC.GetTotalPauseDuration().Ticks, JitInfo.GetCompilationTime(currentThread: true).Ticks, threadWork);
    }

    internal void Record(int tick, long startQpc, long endQpc, long bytes, Counters before, Counters after)
    {
        if (_count == _samples.Length) throw new InvalidOperationException("Spike observation buffer exhausted.");
        if (endQpc < startQpc) throw new ArgumentException("Frame QPC interval reversed.");
        _samples[_count++] = new(tick, startQpc, endQpc, bytes, new(after.Gen0 - before.Gen0, after.Gen1 - before.Gen1,
            after.Gen2 - before.Gen2, after.PauseTicks - before.PauseTicks, after.JitTicks - before.JitTicks,
            ThreadWorkObservation.Between(before.ThreadWork, after.ThreadWork)));
    }

    internal void Report(int count, int run, string scenario = "multi-party")
    {
        Console.WriteLine("frame_observer=" + JsonSerializer.Serialize(new {
            count, run, scenario, runtime = RuntimeInformation.FrameworkDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            process_id = Environment.ProcessId, qpc_frequency = Stopwatch.Frequency,
            clock = Stopwatch.IsHighResolution ? "Stopwatch/QPC on Windows" : "Stopwatch fallback",
            processFirstSample = scenario == "multi-party" && count == 300 && run == 0,
            thread_counters = OperatingSystem.IsWindows() ? "GetThreadTimes + QueryThreadCycleTime" : "unavailable",
            notes = "Step wall clock is the 30Hz gate; counters bracket Step with small observation overhead; "
                + "thread CPU is quantized, raw cycles are not elapsed time; no frames discarded"
        }));
        for (var i = 0; i < _count; i++)
        {
            var sample = _samples[i];
            Console.WriteLine("frame_sample=" + JsonSerializer.Serialize(new {
                count, run, scenario, tick = sample.Tick, phase = sample.Tick == -2 ? "obstacle-edit" : sample.Tick < 0 ? "spawn" : sample.Tick == 0 ? "command" : "move",
                process_id = Environment.ProcessId, native_thread_id = sample.Delta.ThreadWork?.ThreadId,
                qpc_start = sample.StartQpc, qpc_end = sample.EndQpc, qpc_frequency = Stopwatch.Frequency,
                elapsed_ms = sample.ElapsedMs, alloc_bytes = sample.Bytes,
                gc0 = sample.Delta.Gen0, gc1 = sample.Delta.Gen1, gc2 = sample.Delta.Gen2,
                gc_pause_ms = sample.Delta.PauseTicks / (double)TimeSpan.TicksPerMillisecond,
                jit_ms = sample.Delta.JitTicks / (double)TimeSpan.TicksPerMillisecond,
                thread_cpu_ms = sample.Delta.ThreadWork?.CpuTicks / (double)TimeSpan.TicksPerMillisecond,
                cpu_cycles = sample.Delta.ThreadWork?.Cycles,
                over_budget = sample.ElapsedMs > 1000.0 / 30
            }));
        }
    }
}
