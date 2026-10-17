internal sealed record InterruptSpan(long Start, long End, int Cpu, string Kind);

/// <summary>Union only the interruption portions on the CPU while our thread was scheduled there.</summary>
internal static class InterruptOverlap
{
    internal static long Ticks(IReadOnlyList<ThreadSegment> segments, IReadOnlyList<InterruptSpan> interrupts)
    {
        long total = 0;
        foreach (var segment in segments.Where(s => s.State == "running" && s.Cause is not null))
        {
            var clipped = interrupts.Where(i => i.Cpu == segment.Cause!.Cpu && i.End > segment.Start && i.Start < segment.End)
                .Select(i => (Start: Math.Max(segment.Start, i.Start), End: Math.Min(segment.End, i.End)))
                .OrderBy(i => i.Start);
            long? start = null;
            long end = 0;
            foreach (var interval in clipped)
            {
                if (start is null) { start = interval.Start; end = interval.End; }
                else if (interval.Start <= end) end = Math.Max(end, interval.End);
                else { total += end - start.Value; start = interval.Start; end = interval.End; }
            }
            if (start is not null) total += end - start.Value;
        }
        return total;
    }

    internal static void Check()
    {
        var transition = new ThreadTransition(100, "switch-in", "running", 1, "", 0, 0, "", null, new(null, []));
        ThreadSegment[] segments = [new(100, 200, "running", transition), new(200, 300, "waiting", null)];
        InterruptSpan[] spans = [new(80, 120, 1, "DPC"), new(110, 140, 1, "ISR"),
            new(100, 180, 2, "DPC"), new(220, 240, 1, "ISR")];
        if (Ticks(segments, spans) != 40 || Ticks(segments, []) != 0)
            throw new InvalidOperationException("Interrupt union double-counted nested/different-CPU/off-CPU work.");
        Console.WriteLine("Interrupt overlap self-check PASS: clip, nested union, other CPUs and waiting excluded.");
    }
}
