using System.Text.Json;

internal static class SchedulingChecks
{
    internal static void Run()
    {
        if (FrameWindow.AfterSwitchOut(System.Diagnostics.ThreadState.Wait) != "waiting"
            || FrameWindow.AfterSwitchOut(System.Diagnostics.ThreadState.Ready) != "ready"
            || FrameWindow.AfterSwitchOut(System.Diagnostics.ThreadState.Unknown) != "unknown")
            throw new InvalidOperationException("Native switch-out states mapped incorrectly.");
        using var document = JsonDocument.Parse("""
            {"process_id":1,"native_thread_id":2,"qpc_frequency":1000,"qpc_start":1000,"qpc_end":1100}
            """);
        var frame = new FrameWindow(document.RootElement);
        ThreadTransition At(long qpc, string state) => new(qpc, "test", state, 0, "", 0, 0, "", null, new(null, []));
        JsonElement Read(ThreadTransition[] events, int lost = 0) =>
            JsonSerializer.SerializeToElement(frame.Summarize(events, lost));
        void Check(JsonElement result, string property, double expected)
        {
            if (Math.Abs(result.GetProperty(property).GetDouble() - expected) > 0.000001)
                throw new InvalidOperationException($"Scheduling interval check failed: {property}");
        }
        var result = Read([At(900, "running"), At(1020, "waiting"), At(1050, "ready"), At(1080, "running"), At(1200, "waiting")]);
        Check(result, "scheduled_running_ms", 40);
        Check(result, "waiting_ms", 30);
        Check(result, "ready_ms", 30);
        Check(result, "unknown_ms", 0);
        Check(result, "longest_off_cpu_ms", 60);
        result = Read([At(990, "ready"), At(1020, "running")]);
        Check(result, "ready_ms", 20);
        Check(result, "scheduled_running_ms", 80);
        result = Read([]);
        Check(result, "unknown_ms", 100);
        if (result.GetProperty("state_coverage_complete").GetBoolean()) throw new InvalidOperationException("Missing events treated as known.");
        result = Read([At(900, "running")], lost: 1);
        if (result.GetProperty("state_coverage_complete").GetBoolean()) throw new InvalidOperationException("Lost events treated as complete.");
        Console.WriteLine("Scheduling interval self-checks PASS: clipped running/ready/waiting, missing and lost events.");
    }
}
