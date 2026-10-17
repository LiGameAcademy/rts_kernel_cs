using System.Text.Json;

internal sealed class FrameWindow(JsonElement frame)
{
    internal int ProcessId => frame.GetProperty("process_id").GetInt32();
    internal int ThreadId => frame.GetProperty("native_thread_id").GetInt32();
    internal long Frequency => frame.GetProperty("qpc_frequency").GetInt64();
    internal long Start => frame.GetProperty("qpc_start").GetInt64();
    internal long End => frame.GetProperty("qpc_end").GetInt64();

    internal static string AfterSwitchOut(System.Diagnostics.ThreadState state) => state switch
    {
        System.Diagnostics.ThreadState.Ready => "ready",
        System.Diagnostics.ThreadState.Wait => "waiting",
        _ => "unknown"
    };

    internal ThreadSegment[] ReadSegments(IReadOnlyList<ThreadTransition> transitions)
    {
        var segments = new List<ThreadSegment>();
        ThreadTransition? current = null;
        var cursor = Start;
        foreach (var transition in transitions)
        {
            if (transition.Qpc <= Start) { current = transition; continue; }
            if (transition.Qpc >= End) break;
            segments.Add(new(cursor, transition.Qpc, current?.State ?? "unknown", current));
            current = transition;
            cursor = transition.Qpc;
        }
        segments.Add(new(cursor, End, current?.State ?? "unknown", current));
        return segments.ToArray();
    }

    internal object Summarize(IReadOnlyList<ThreadTransition> transitions, int eventsLost, bool truncated = false)
    {
        var segments = ReadSegments(transitions);
        long? offCpuStart = null;
        long longestOffCpu = 0;
        foreach (var segment in segments)
        {
            if (segment.State is "ready" or "waiting")
            {
                offCpuStart ??= segment.Start;
                longestOffCpu = Math.Max(longestOffCpu, segment.End - offCpuStart.Value);
            }
            else offCpuStart = null;
        }
        double Ms(long ticks) => ticks * 1000.0 / Frequency;
        double Total(string state) => Ms(segments.Where(s => s.State == state).Sum(s => s.End - s.Start));
        return new { sample = frame, scheduled_running_ms = Total("running"), ready_ms = Total("ready"),
            waiting_ms = Total("waiting"), unknown_ms = Total("unknown"),
            longest_off_cpu_ms = Ms(longestOffCpu),
            state_coverage_complete = eventsLost == 0 && !truncated && segments.All(s => s.State != "unknown"),
            segments = segments.Select(s => new { start_qpc = s.Start, end_qpc = s.End,
                duration_ms = Ms(s.End - s.Start), state = s.State, cause = s.Cause }) };
    }
}

internal sealed record ThreadTransition(long Qpc, string Kind, string State, int Cpu, string Reason,
    int OtherProcessId, int OtherThreadId, string OtherProcess, int? OtherPriority, StackEvidence Stack);
internal sealed record ThreadSegment(long Start, long End, string State, ThreadTransition? Cause);

internal sealed record StackEvidence(int? ThreadId, string[] Frames);
