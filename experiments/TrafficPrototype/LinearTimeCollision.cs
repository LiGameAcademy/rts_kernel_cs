using System.Numerics;
using Rts.Kernel.Numerics;

namespace Rts.TrafficPrototype;

/// <summary>Exact oracle, not a proposed production hot path. Contact is allowed.</summary>
internal static class LinearTimeCollision
{
    internal static bool Overlaps(FixedVector2 a0, FixedVector2 a1, SimScalar ra,
        FixedVector2 b0, FixedVector2 b1, SimScalar rb)
    {
        // Relative position d(t)=d0+t*v. All arithmetic retains raw32.32 without division.
        var dx = (BigInteger)a0.X.Raw - b0.X.Raw;
        var dy = (BigInteger)a0.Y.Raw - b0.Y.Raw;
        var vx = ((BigInteger)a1.X.Raw - a0.X.Raw) - ((BigInteger)b1.X.Raw - b0.X.Raw);
        var vy = ((BigInteger)a1.Y.Raw - a0.Y.Raw) - ((BigInteger)b1.Y.Raw - b0.Y.Raw);
        var radius = (BigInteger)ra.Raw + rb.Raw;
        var margin = dx * dx + dy * dy - radius * radius;
        if (margin < 0) return true;
        var speedSquared = vx * vx + vy * vy;
        var projection = -(dx * vx + dy * vy);
        if (speedSquared == 0 || projection <= 0) return false;
        if (projection >= speedSquared)
        {
            var endX = dx + vx;
            var endY = dy + vy;
            return endX * endX + endY * endY < radius * radius;
        }
        return margin * speedSquared < projection * projection;
    }
}
