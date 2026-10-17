using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class CollisionBoundsChecks
{
    internal static void Run(Action<bool, string> check)
    {
        void Compare(CrowdBody a, CrowdBody b)
        {
            var expected = UnfilteredTimedGeometry.Overlap(a, b);
            check(TimedCrowdGeometry.Overlap(a, b) == expected, "bounds preserve unfiltered collision result");
            check(TimedCrowdGeometry.Overlap(b, a) == expected, "bounds preserve collision symmetry");
        }

        // Different segment times force the exact rational path. Include touching and one-raw-unit overlap.
        foreach (var origin in new long[] { -1_000_000 * SimScalar.Scale, 0, 1_000_000 * SimScalar.Scale })
        foreach (var offset in new long[] { -1, 0, 1 })
        {
            var y = 2 * SimScalar.Scale + offset;
            var a = new CrowdBody(new(1), 1,
                [FixedVector2.FromRaw(origin, 0), FixedVector2.FromRaw(origin + 10 * SimScalar.Scale, 0)], [0, 1]);
            var b = new CrowdBody(new(2), 1,
                [FixedVector2.FromRaw(origin, y), FixedVector2.FromRaw(origin + 3 * SimScalar.Scale, y),
                    FixedVector2.FromRaw(origin + 10 * SimScalar.Scale, y)], [0, SimScalar.FromRatio(3, 10), 1]);
            Compare(a, b);
            check(TimedCrowdGeometry.Overlap(a, b) == (offset < 0), "unaligned parallel contact keeps raw precision");
            Compare(a, b with { Times = [0, 0, 1] });
            Compare(a, b with { Times = null });
        }

        // Test-only reproducible sampling covers corners, stationary bodies and quantized-time fallback.
        var random = new Random(114);
        for (var sample = 0; sample < 12_000; sample++)
        {
            CrowdBody Body(ulong id)
            {
                FixedVector2 Point() => new(random.Next(-100, 101), random.Next(-100, 101));
                var first = Point();
                if (sample % 5 == 0 && id == 2) return CrowdBody.Stationary(new(id), random.Next(0, 12), first);
                var times = new SimScalar[] { 0, SimScalar.FromRatio(random.Next(1, 100), 100), 1 };
                if (sample % 7 == 0) times[1] = 0;
                return new(new(id), random.Next(0, 12), [first, Point(), Point()], times);
            }
            Compare(Body(1), Body(2));
        }
    }
}
