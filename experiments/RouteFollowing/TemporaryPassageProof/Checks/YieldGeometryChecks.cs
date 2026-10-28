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
        Console.WriteLine("yield_geometry_assertions=" + assertions);
    }
}
