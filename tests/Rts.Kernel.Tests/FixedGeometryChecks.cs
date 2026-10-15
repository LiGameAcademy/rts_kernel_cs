using System.Numerics;
using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class FixedGeometryChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var rng = new DeterministicRng(89);
        UInt128 Next() => ((UInt128)rng.NextUInt64() << 64) | rng.NextUInt64();
        for (var i = 0; i < 2048; i++)
        {
            var a = Next(); var b = Next(); var c = Next(); var d = Next();
            var expected = ((BigInteger)a * (BigInteger)b).CompareTo((BigInteger)c * (BigInteger)d);
            check(WideProduct.Compare(a, b, c, d) == expected, "wide product comparison agrees with independent exact arithmetic");
        }
        check(WideProduct.Compare(UInt128.MaxValue, UInt128.MaxValue, UInt128.MaxValue, UInt128.MaxValue) == 0,
            "full-width product carry is exact");
        var limit = SimulationDomain.CoordinateLimit.Raw;
        FixedVector2 Point() => FixedVector2.FromRaw((long)(rng.NextUInt64() % (ulong)(2 * limit)) - limit,
            (long)(rng.NextUInt64() % (ulong)(2 * limit)) - limit);
        for (var i = 0; i < 2048; i++)
        {
            var a = Point(); var b = i % 7 == 0 ? a : Point();
            var c = Point(); var d = i % 11 == 0 ? c : Point();
            var radius = SimScalar.FromRaw((long)(rng.NextUInt64() % (ulong)SimulationDomain.CapabilityLimit.Raw));
            var otherRadius = radius / 2;
            var actual = CrowdGeometry.Overlap(a, b, radius, c, d, otherRadius);
            check(actual == Reference(a, b, radius, c, d, otherRadius), "raw capsule agrees with independent rational-distance oracle");
            check(actual == CrowdGeometry.Overlap(c, d, otherRadius, a, b, radius), "capsule predicate is symmetric");
        }
        var origin = FixedVector2.Zero;
        var end = new FixedVector2(100, 0);
        foreach (var rawOffset in new long[] { -1, 0, 1 })
        {
            var point = FixedVector2.FromRaw(50 * SimScalar.Scale, 8 * SimScalar.Scale + rawOffset);
            check(CrowdGeometry.Overlap(origin, end, 4, point, point, 4) == (rawOffset < 0),
                "one raw unit distinguishes overlap, tangent and separation");
        }
        check(CrowdGeometry.Overlap(new(-1000000, -1000000), new(1000000, 1000000), 1,
            new(-1000000, 1000000), new(1000000, -1000000), 1), "extreme admitted swept crossing uses wide signs without overflow");
        var low = new FixedVector2(-1000000, -1000000);
        var high = new FixedVector2(1000000, 1000000);
        var far = new FixedVector2(1000000, -1000000);
        check(!CrowdGeometry.Overlap(low, high, 1, far, far, 1), "extreme non-intersecting point-segment products remain exact");
    }

    private static bool Reference(FixedVector2 a, FixedVector2 b, SimScalar r,
        FixedVector2 c, FixedVector2 d, SimScalar s)
    {
        BigInteger Side(FixedVector2 p, FixedVector2 q, FixedVector2 t) =>
            ((BigInteger)q.X.Raw - p.X.Raw) * ((BigInteger)t.Y.Raw - p.Y.Raw)
            - ((BigInteger)q.Y.Raw - p.Y.Raw) * ((BigInteger)t.X.Raw - p.X.Raw);
        var intersects = SimMath.Max(SimMath.Min(a.X, b.X), SimMath.Min(c.X, d.X)) <= SimMath.Min(SimMath.Max(a.X, b.X), SimMath.Max(c.X, d.X))
            && SimMath.Max(SimMath.Min(a.Y, b.Y), SimMath.Min(c.Y, d.Y)) <= SimMath.Min(SimMath.Max(a.Y, b.Y), SimMath.Max(c.Y, d.Y))
            && Side(a, b, c).Sign * Side(a, b, d).Sign <= 0 && Side(c, d, a).Sign * Side(c, d, b).Sign <= 0;
        var sum = (BigInteger)r.Raw + s.Raw;
        if (sum <= 0) return false;
        if (intersects) return true;
        bool Close(FixedVector2 p, FixedVector2 x, FixedVector2 y)
        {
            var dx = (BigInteger)y.X.Raw - x.X.Raw;
            var dy = (BigInteger)y.Y.Raw - x.Y.Raw;
            var px = (BigInteger)p.X.Raw - x.X.Raw;
            var py = (BigInteger)p.Y.Raw - x.Y.Raw;
            var denominator = dx * dx + dy * dy;
            var dot = px * dx + py * dy;
            if (denominator == 0 || dot <= 0) return px * px + py * py < sum * sum;
            if (dot >= denominator) return (px - dx) * (px - dx) + (py - dy) * (py - dy) < sum * sum;
            return (px * px + py * py) * denominator - dot * dot < sum * sum * denominator;
        }
        return Close(a, c, d) || Close(b, c, d) || Close(c, a, b) || Close(d, a, b);
    }
}
