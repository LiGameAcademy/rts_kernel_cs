using Rts.Kernel.Numerics;
using Rts.Movement.Prototype;

/// <summary>Raw boundary equivalence, including values lost by rounded distance comparisons.</summary>
internal static class YieldGeometryChecks
{
    internal static void Run()
    {
        var assertions = 0;
        var radius = 64L * SimScalar.Scale;
        var threshold = (Int128)radius * radius;
        foreach (var squared in new[] { threshold - 1, threshold, threshold + 1 })
        {
            if ((SquaredDistance.FromRaw(squared).Length < 64) != (squared < threshold))
                throw new Exception("Raw squared threshold disagrees with floor root");
            assertions++;
        }
        void Compare(FixedVector2 a, FixedVector2 b)
        {
            if (RouteYieldGeometry.GoalOverlap(a, b) != ((a - b).Length < 64))
                throw new Exception($"Goal-spacing boundary differs: {a}, {b}");
            assertions++;
        }
        foreach (var delta in new long[] { -radius - 1, -radius, -radius + 1, -1, 0, 1, radius - 1, radius, radius + 1 })
        {
            Compare(default, FixedVector2.FromRaw(delta, 0));
            Compare(default, FixedVector2.FromRaw(0, delta));
            Compare(new(-1000000, 1000000), FixedVector2.FromRaw(-1000000L * SimScalar.Scale + delta, 1000000L * SimScalar.Scale));
        }
        Compare(new(-1000000, -1000000), new(1000000, 1000000));
        // Fixed deterministic samples, concentrated around the admission boundary as well as far apart.
        var random = new Random(154);
        for (var i = 0; i < 10000; i++)
        {
            var x = random.NextInt64(-radius * 2, radius * 2);
            var y = random.NextInt64(-radius * 2, radius * 2);
            Compare(new(500, -500), FixedVector2.FromRaw(500L * SimScalar.Scale + x, -500L * SimScalar.Scale + y));
        }
        CapsuleChecks(ref assertions);
        Console.WriteLine("yield_geometry_assertions=" + assertions);
    }

    private static void CapsuleChecks(ref int assertions)
    {
        var count = 0;
        void Check(FixedVector2 a, FixedVector2 b, FixedVector2 c, FixedVector2 d, bool expected)
        {
            if (RouteYieldGeometry.Overlap(a, b, 31, c, d, 31) != expected
                || RouteYieldGeometry.Overlap(c, d, 31, a, b, 31) != expected
                || RouteYieldGeometry.Overlap(b, a, 31, d, c, 31) != expected)
                throw new Exception($"Capsule boundary differs: {a}, {b}, {c}, {d}");
            count += 3;
        }
        var touching = 62L * SimScalar.Scale;
        foreach (var shift in new[] { new FixedVector2(0, 0), new(-1000000, 1000000) })
        {
            foreach (var offset in new long[] { -1, 0, 1 })
            {
                var near = shift + FixedVector2.FromRaw(0, touching + offset);
                Check(shift, shift, near, near, offset < 0);
                Check(shift, shift + new FixedVector2(100, 0), near, near + new FixedVector2(100, 0), offset < 0);
                Check(shift, shift + new FixedVector2(100, 0), near + new FixedVector2(50, 0), near + new FixedVector2(50, 0), offset < 0);
            }
            Check(shift, shift + new FixedVector2(100, 100), shift + new FixedVector2(0, 100), shift + new FixedVector2(100, 0), true);
            Check(shift, shift + new FixedVector2(100, 0), shift + new FixedVector2(1000, 1000), shift + new FixedVector2(1100, 1000), false);
            Check(shift, shift, shift, shift, true);
        }
        if (RouteYieldGeometry.Overlap(default, new(100, 100), 0, new(0, 100), new(100, 0), 0))
            throw new Exception("Zero summed radius must retain exact false result");
        assertions += count + 1;
    }
}
