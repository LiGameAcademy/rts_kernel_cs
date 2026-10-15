namespace Rts.Kernel.Navigation;

/// <summary>Exact capsule predicates on admitted raw coordinates. Touching circles do not overlap.</summary>
internal static class CrowdGeometry
{
    internal static bool Overlap(FixedVector2 a, FixedVector2 b, SimScalar radius,
        FixedVector2 c, FixedVector2 d, SimScalar otherRadius)
    {
        var sum = checked((Int128)radius.Raw + otherRadius.Raw);
        if (sum <= 0) return false;
        if (Intersects(a, b, c, d)) return true;
        var squared = checked((UInt128)(sum * sum));
        return PointSegment(a, c, d, squared)
            || PointSegment(b, c, d, squared)
            || PointSegment(c, a, b, squared)
            || PointSegment(d, a, b, squared);
    }

    private static bool PointSegment(FixedVector2 point, FixedVector2 from, FixedVector2 to,
        UInt128 radiusSquared)
    {
        var dx = (Int128)to.X.Raw - from.X.Raw;
        var dy = (Int128)to.Y.Raw - from.Y.Raw;
        var px = (Int128)point.X.Raw - from.X.Raw;
        var py = (Int128)point.Y.Raw - from.Y.Raw;
        var lengthSquared = checked(dx * dx + dy * dy);
        var projection = checked(px * dx + py * dy);
        int comparison;
        if (projection <= 0 || lengthSquared == 0)
            comparison = checked((UInt128)(px * px + py * py)).CompareTo(radiusSquared);
        else if (projection >= lengthSquared)
        {
            var x = px - dx;
            var y = py - dy;
            comparison = checked((UInt128)(x * x + y * y)).CompareTo(radiusSquared);
        }
        else
        {
            var cross = checked(dx * py - dy * px);
            var magnitude = (UInt128)Int128.Abs(cross);
            // cross^2 < radius^2 * length^2: both sides may require more than128 bits.
            comparison = WideProduct.Compare(magnitude, magnitude, radiusSquared, (UInt128)lengthSquared);
        }
        return comparison < 0;
    }

    private static bool Intersects(FixedVector2 a, FixedVector2 b, FixedVector2 c, FixedVector2 d)
    {
        static int Side(FixedVector2 p, FixedVector2 q, FixedVector2 r) => checked(
            ((Int128)q.X.Raw - p.X.Raw) * ((Int128)r.Y.Raw - p.Y.Raw)
            - ((Int128)q.Y.Raw - p.Y.Raw) * ((Int128)r.X.Raw - p.X.Raw)).CompareTo(0);
        return SimMath.Max(SimMath.Min(a.X, b.X), SimMath.Min(c.X, d.X)) <= SimMath.Min(SimMath.Max(a.X, b.X), SimMath.Max(c.X, d.X))
            && SimMath.Max(SimMath.Min(a.Y, b.Y), SimMath.Min(c.Y, d.Y)) <= SimMath.Min(SimMath.Max(a.Y, b.Y), SimMath.Max(c.Y, d.Y))
            && Side(a, b, c) * Side(a, b, d) <= 0 && Side(c, d, a) * Side(c, d, b) <= 0;
    }
}
