using Microsoft.Diagnostics.Tracing;

/// <summary>Absolute QPC is required to join external Stopwatch intervals, not just ETL-relative times.</summary>
internal static class TraceClock
{
    // TraceEvent labels this public API "Not Obsolete but Discouraged" for normal analysis.
    // This explicit clock-join boundary needs the original counter; no private-field reflection.
#pragma warning disable CS0618
    internal static long Qpc(TraceEvent traceEvent) => traceEvent.TimeStampQPC;
#pragma warning restore CS0618
}
