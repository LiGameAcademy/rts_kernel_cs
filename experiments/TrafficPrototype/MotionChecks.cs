using System.Security.Cryptography;
using System.Text;
using Rts.Kernel;
using Rts.Kernel.Numerics;

namespace Rts.TrafficPrototype;

internal static class MotionChecks
{
    internal static IReadOnlyList<object> Run(Action<bool, string> check)
    {
        var observations = new List<object>();
        var convoy = new[] { Move(1, 15, 15, 21, 15, 4), Move(2, 25, 15, 31, 15, 4) };
        var candidate = FrameMotionResolver.Resolve(convoy);
        var production = ProductionReference.ConvoyFirstFrame();
        check(candidate.Motions.All(item => !item.HeldForConflict), "same-speed convoy has no temporal conflict");
        check(candidate.Motions[0].End == new FixedVector2(21, 15), "candidate follows original straight path");
        check(production[0] != candidate.Motions[0].End, "current production demonstrates the conservative detour");
        observations.Add(new { scenario = "convoy", production = production.Select(Raw), candidate = Observe(candidate) });

        var differentTimes = FrameMotionResolver.Resolve([Move(1, -18, 0, 6, 0, 1), Move(2, 0, -6, 0, 18, 1)]);
        check(differentTimes.Motions.All(item => !item.HeldForConflict), "spatial crossing at different times can be safe");
        var together = FrameMotionResolver.Resolve([Move(1, -6, 0, 6, 0, 1), Move(2, 0, -6, 0, 6, 1)]);
        check(together.Motions.Count(item => item.HeldForConflict) == 1, "simultaneous crossing requires arbitration");
        var older = FrameMotionResolver.Resolve([Move(1, -6, 0, 6, 0, 1), Move(2, 0, -6, 0, 6, 1) with { WaitAge = 3 }]);
        check(older.Motions[0].HeldForConflict && !older.Motions[1].HeldForConflict, "older wait wins a movable conflict");
        observations.Add(new { scenario = "time_crossing", differentTimes = Observe(differentTimes), simultaneous = Observe(together) });

        foreach (var count in new[] { 2, 12, 64 })
        {
            var chain = Enumerable.Range(0, count).Select(i => Move((ulong)i + 1, 15 + 10 * i, 15, 21 + 10 * i, 15, 4)).ToArray();
            var free = FrameMotionResolver.Resolve(chain);
            check(free.Motions.All(item => !item.HeldForConflict), "entire same-speed chain moves without false blocking");
            chain[^1] = chain[^1] with { DesiredEnd = chain[^1].Start, Stopped = true };
            var stopped = FrameMotionResolver.Resolve(chain);
            check(stopped.Motions.Take(count - 1).All(item => item.HeldForConflict), "Stop propagates to every dependent proposal");
            check(stopped.Motions[^1].End == chain[^1].Start && !stopped.Motions[^1].HeldForConflict, "Stop body is never moved");
            check(stopped.Passes <= count + 1, "reference cancellation has a finite combinatorial bound");
            check(Observe(stopped) == Observe(FrameMotionResolver.Resolve(chain.Reverse().ToArray())), "input iteration order does not affect resolution");
            AssertSafe(stopped, check);
            observations.Add(new { scenario = "chain", count, free = Observe(free), stopped = Observe(stopped), stopped.Passes, stopped.PairChecks });
        }
        var swap = FrameMotionResolver.Resolve([Move(1, 0, 0, 10, 0, 4), Move(2, 10, 0, 0, 0, 4)]);
        check(swap.Motions.All(item => item.HeldForConflict), "two-phase safety alone does not solve an exchange deadlock");
        observations.Add(new { scenario = "swap_remains_blocked", state = Observe(swap) });
        CheckBoundaries(check);
        observations.Add(new { scenario = "random_resolutions", digest = RandomSafety(check) });
        OneDimensionalReference(check);
        return observations.AsReadOnly();
    }

    private static void CheckBoundaries(Action<bool, string> check)
    {
        check(!LinearTimeCollision.Overlaps(new(0, 0), new(10, 0), 1, new(0, 2), new(10, 2), 1), "exact tangency is allowed");
        check(LinearTimeCollision.Overlaps(new(0, 0), new(10, 0), 1,
            FixedVector2.FromRaw(0, 2 * SimScalar.Scale - 1), FixedVector2.FromRaw(10 * SimScalar.Scale, 2 * SimScalar.Scale - 1), 1), "one raw unit of overlap is detected");
        Reject(check, () => FrameMotionResolver.Resolve([Move(1, 0, 0, 1, 0, 1) with { Stopped = true }]), "displaced Stop is invalid input");
        Reject(check, () => FrameMotionResolver.Resolve([Move(1, 0, 0, 1, 0, 1), Move(1, 10, 0, 11, 0, 1)]), "duplicate identity is invalid");
        Reject(check, () => FrameMotionResolver.Resolve([Move(1, 0, 0, 1, 0, 2), Move(2, 1, 0, 2, 0, 2)]), "invalid initial overlap is rejected");
    }

    private static string RandomSafety(Action<bool, string> check)
    {
        var random = new DeterministicRng(94);
        var trace = new StringBuilder();
        for (var sample = 0; sample < 128; sample++)
        {
            var input = Enumerable.Range(0, 12).Select(i =>
            {
                var x = i % 4 * 10;
                var y = i / 4 * 10;
                return Move((ulong)i + 1, x, y, x + (int)(random.NextUInt64() % 25) - 12,
                    y + (int)(random.NextUInt64() % 25) - 12, 4) with { WaitAge = (int)(random.NextUInt64() % 4) };
            }).ToArray();
            var result = FrameMotionResolver.Resolve(input);
            trace.AppendLine(Observe(result));
            AssertSafe(result, check);
            check(Observe(result) == Observe(FrameMotionResolver.Resolve(input.Reverse().ToArray())), "random proposal order is irrelevant");
        }
        var bytes = Encoding.UTF8.GetBytes(trace.ToString().Replace("\r\n", "\n"));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static void OneDimensionalReference(Action<bool, string> check)
    {
        // Independent signed interval reasoning: initial separation >= 2; linear relative motion
        // overlaps iff the final signed separation becomes < 2, even when it passes through zero.
        for (var gap = 2; gap <= 12; gap++)
        for (var a = -12; a <= 12; a++)
        for (var b = -12; b <= 12; b++)
            check(LinearTimeCollision.Overlaps(new(0, 0), new(a, 0), 1, new(gap, 0), new(gap + b, 0), 1)
                == (gap + b - a < 2), "collinear oracle matches independent signed interval result");
    }

    private static void AssertSafe(MotionResolution result, Action<bool, string> check)
    {
        for (var a = 0; a < result.Motions.Count; a++)
        for (var b = a + 1; b < result.Motions.Count; b++)
        {
            var x = result.Motions[a]; var y = result.Motions[b];
            check(!LinearTimeCollision.Overlaps(x.Start, x.End, x.Radius, y.Start, y.End, y.Radius), "committed motions are safe for the entire frame");
        }
    }

    private static MotionProposal Move(ulong id, int x0, int y0, int x1, int y1, int radius) =>
        new(id, new(x0, y0), new(x1, y1), radius);
    private static object Raw(FixedVector2 point) => new { x = point.X.Raw, y = point.Y.Raw };
    private static string Observe(MotionResolution result) => string.Join(";", result.Motions.Select(item =>
        FormattableString.Invariant($"{item.Id}:{item.End.X.Raw},{item.End.Y.Raw}:{item.HeldForConflict}")));
    private static void Reject(Action<bool, string> check, Action action, string message)
    {
        try { action(); } catch (ArgumentException) { check(true, message); return; }
        check(false, message);
    }
}
