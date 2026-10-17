using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Diagnostics.Tracing;

/// <summary>Separate diagnostic process. Never pre-runs or changes the real match fixture.</summary>
internal static class MetadataCalibration
{
    internal static void RunFixture()
    {
        var start = Stopwatch.GetTimestamp();
        var checksum = KnownWork(64_000_000);
        var end = Stopwatch.GetTimestamp();
        Console.WriteLine("metadata_fixture=" + JsonSerializer.Serialize(new {
            process_id = Environment.ProcessId, native_thread_id = GetCurrentThreadId(), qpc_start = start, qpc_end = end,
            qpc_frequency = Stopwatch.Frequency, checksum,
            runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription
        }));
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint GetCurrentThreadId();

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static ulong KnownWork(int iterations)
    {
        ulong value = 7;
        for (var i = 0; i < iterations; i++)
            value = unchecked((value ^ (value >> 13)) * 6364136223846793005UL + 1442695040888963407UL);
        return value;
    }

    internal static bool Check(string etl, string fixtureLog, string output)
    {
        var line = File.ReadLines(fixtureLog).Single(s => s.StartsWith("metadata_fixture="));
        using var fixture = JsonDocument.Parse(line[17..]);
        var pid = fixture.RootElement.GetProperty("process_id").GetInt32();
        var tid = fixture.RootElement.GetProperty("native_thread_id").GetInt32();
        var start = fixture.RootElement.GetProperty("qpc_start").GetInt64();
        var end = fixture.RootElement.GetProperty("qpc_end").GetInt64();
        var methods = new List<MethodRange>();
        var samples = new List<(long Qpc, ulong Ip)>();
        var runtimeEvents = 0;
        var runtimeProviderEvents = 0;
        var allRuntimeProviderEvents = 0;
        using var source = new ETWTraceEventSource(etl);
        source.AllEvents += e =>
        {
            if (e.ProviderGuid == Microsoft.Diagnostics.Tracing.Parsers.ClrTraceEventParser.ProviderGuid)
            {
                allRuntimeProviderEvents++;
                if (e.ProcessID == pid) runtimeProviderEvents++;
            }
        };
        source.Clr.All += e => { if (e.ProcessID == pid) runtimeEvents++; };
        source.Clr.MethodLoadVerbose += e =>
        {
            if (e.ProcessID == pid)
                methods.Add(new(TraceClock.Qpc(e), e.MethodStartAddress, e.MethodSize,
                    e.MethodNamespace + "." + e.MethodName, e.MethodID, e.ReJITID, e.OptimizationTier.ToString()));
        };
        source.Kernel.PerfInfoSample += e =>
        {
            if (e.ThreadID == tid && TraceClock.Qpc(e) >= start && TraceClock.Qpc(e) <= end)
                samples.Add((TraceClock.Qpc(e), e.InstructionPointer));
        };
        source.Process();
        var known = methods.Where(m => m.Name == "MetadataCalibration.KnownWork" && m.Size > 0).ToArray();
        var mapped = samples.Count(s => known.Any(m => s.Qpc >= m.LoadQpc && s.Ip >= m.Start && s.Ip - m.Start < (ulong)m.Size));
        var passed = runtimeEvents > 0 && known.Length > 0 && mapped > 0 && source.EventsLost == 0;
        File.WriteAllText(output, JsonSerializer.Serialize(new {
            passed, process_id = pid, native_thread_id = tid, runtime_events = runtimeEvents,
            runtime_provider_events = runtimeProviderEvents, all_runtime_provider_events = allRuntimeProviderEvents,
            method_loads = methods.Count,
            known_methods = known, cpu_samples = samples.Count, mapped_known_samples = mapped,
            events_lost = source.EventsLost,
            cpu_sample_addresses = samples.Select(s => new { qpc = s.Qpc, instruction_pointer = s.Ip }),
            fixture = fixture.RootElement
        }, new JsonSerializerOptions { WriteIndented = true }));
        if (passed) Console.WriteLine($"Metadata calibration PASS: {known.Length} known versions, {mapped} mapped samples.");
        else Console.Error.WriteLine("Metadata calibration FAIL: method/address evidence missing; A/B/C gate remains closed.");
        return passed;
    }
}

internal sealed record MethodRange(long LoadQpc, ulong Start, int Size, string Name,
    long MethodId, long ReJitId, string OptimizationTier);
