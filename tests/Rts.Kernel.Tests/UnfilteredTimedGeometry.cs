// Frozen pre-broad-phase predicate for differential regression; do not optimize with production.
using System.Numerics;

namespace Rts.Kernel.Navigation;

/// <summary>Piecewise linear bodies compared at the same normalized frame time. Contact is allowed.</summary>
internal static class UnfilteredTimedGeometry
{
    internal static bool Overlap(CrowdBody a, CrowdBody b)
    {
        // A zero-duration quantized segment remains a conservative swept segment, never an unchecked jump.
        if (!HasStrictTimes(a) || !HasStrictTimes(b)) return SpatialOverlap(a, b);
        var i = 1;
        var j = 1;
        while (i < a.Trace.Count && j < b.Trace.Count)
        {
            var start = SimMath.Max(a.Times![i - 1], b.Times![j - 1]);
            var end = SimMath.Min(a.Times[i], b.Times[j]);
            if (start < end && SegmentOverlap(a, i, b, j, start.Raw, end.Raw)) return true;
            var aEnd = a.Times[i];
            var bEnd = b.Times[j];
            if (aEnd <= bEnd) i++;
            if (bEnd <= aEnd) j++;
        }
        return false;
    }

    private static bool HasStrictTimes(CrowdBody body)
    {
        if (body.Times is null || body.Times.Count != body.Trace.Count || body.Times[0] != 0 || body.Times[^1] != 1) return false;
        for (var i = 1; i < body.Times.Count; i++)
            if (body.Times[i] <= body.Times[i - 1]) return false;
        return true;
    }

    private static bool SegmentOverlap(CrowdBody a, int i, CrowdBody b, int j, long start, long end)
    {
        var a0 = a.Trace[i - 1];
        var a1 = a.Trace[i];
        var b0 = b.Trace[j - 1];
        var b1 = b.Trace[j];
        var at = a.Times![i - 1].Raw;
        var ae = a.Times[i].Raw;
        var bt = b.Times![j - 1].Raw;
        var be = b.Times[j].Raw;
        var radius = a.Radius + b.Radius;
        // Aligned segments and static bodies need no rational interpolation or arbitrary-width arithmetic.
        if ((at == bt && ae == be) || (a0 == a1 && start == bt && end == be)
            || (b0 == b1 && start == at && end == ae))
        {
            var from = FixedVector2.FromRaw(a0.X.Raw - b0.X.Raw, a0.Y.Raw - b0.Y.Raw);
            var to = FixedVector2.FromRaw(a1.X.Raw - b1.X.Raw, a1.Y.Raw - b1.Y.Raw);
            return CrowdGeometry.Overlap(from, to, radius, FixedVector2.Zero, FixedVector2.Zero, 0);
        }
        // Keep a common denominator: rounding synchronized endpoints could miss a one-raw-unit collision.
        var da = (BigInteger)(ae - at);
        var db = (BigInteger)(be - bt);
        BigInteger Relative(long x0, long x1, long y0, long y1, long time) =>
            ((BigInteger)x0 - y0) * da * db + ((BigInteger)x1 - x0) * (time - at) * db - ((BigInteger)y1 - y0) * (time - bt) * da;
        var x = Relative(a0.X.Raw, a1.X.Raw, b0.X.Raw, b1.X.Raw, start);
        var y = Relative(a0.Y.Raw, a1.Y.Raw, b0.Y.Raw, b1.Y.Raw, start);
        var vx = Relative(a0.X.Raw, a1.X.Raw, b0.X.Raw, b1.X.Raw, end) - x;
        var vy = Relative(a0.Y.Raw, a1.Y.Raw, b0.Y.Raw, b1.Y.Raw, end) - y;
        var r = (BigInteger)radius.Raw * da * db;
        var margin = x * x + y * y - r * r;
        if (margin < 0) return true;
        var length = vx * vx + vy * vy;
        var projection = -(x * vx + y * vy);
        if (length == 0 || projection <= 0) return false;
        if (projection >= length) return (x + vx) * (x + vx) + (y + vy) * (y + vy) < r * r;
        return margin * length < projection * projection;
    }

    private static bool SpatialOverlap(CrowdBody a, CrowdBody b)
    {
        for (var i = 1; i < a.Trace.Count; i++)
            for (var j = 1; j < b.Trace.Count; j++)
                if (CrowdGeometry.Overlap(a.Trace[i - 1], a.Trace[i], a.Radius, b.Trace[j - 1], b.Trace[j], b.Radius)) return true;
        return false;
    }
}
