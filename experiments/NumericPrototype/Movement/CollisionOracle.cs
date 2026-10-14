using System.Numerics;

namespace NumericPrototype.Movement;

// Exact, deliberately slow reference predicates. BigInteger products avoid hiding a 256-bit geometry need.
// This is an experimental collision oracle, NOT a proposed production broadphase/physics implementation.
internal static class CollisionOracle
{
    internal static bool Clear(MovementWorld world, FixedPoint from, FixedPoint to, long radius,
        FixedPoint? blocker = null, long blockerRadius = 0)
    {
        var a = world.Cell(from);
        var b = world.Cell(to);
        if (!world.Walkable(a.X, a.Y) || !world.Walkable(b.X, b.Y)) return false;
        if (blocker is { } body && PointNearSegment(body, from, to, checked(radius + blockerRadius))) return false;
        foreach (var box in world.BlockedBoxes(from, to, radius))
        {
            var corners = new[] { new FixedPoint(box.Left, box.Top), new(box.Right, box.Top),
                new FixedPoint(box.Right, box.Bottom), new(box.Left, box.Bottom) };
            FixedPoint Closest(FixedPoint p) => new(Math.Clamp(p.X, box.Left, box.Right), Math.Clamp(p.Y, box.Top, box.Bottom));
            var closestA = from - Closest(from);
            var closestB = to - Closest(to);
            var squaredRadius = (BigInteger)radius * radius;
            if ((BigInteger)closestA.LengthSquared < squaredRadius || (BigInteger)closestB.LengthSquared < squaredRadius) return false;
            for (var i = 0; i < 4; i++)
                if (Intersects(from, to, corners[i], corners[(i + 1) % 4])
                    || PointNearSegment(corners[i], from, to, radius)) return false;
        }
        return true; // Exact touching is allowed, overlap is not.
    }
    internal static bool PointNearSegment(FixedPoint point, FixedPoint from, FixedPoint to, long radius)
    {
        var delta = to - from;
        var relative = point - from;
        var length = (BigInteger)delta.LengthSquared;
        var projection = (BigInteger)relative.X * delta.X + (BigInteger)relative.Y * delta.Y;
        var squaredRadius = (BigInteger)radius * radius;
        if (length == 0 || projection <= 0) return (BigInteger)relative.LengthSquared < squaredRadius;
        if (projection >= length) return (BigInteger)(point - to).LengthSquared < squaredRadius;
        var cross = (BigInteger)relative.X * delta.Y - (BigInteger)relative.Y * delta.X;
        return cross * cross < squaredRadius * length;
    }
    private static bool Intersects(FixedPoint a, FixedPoint b, FixedPoint c, FixedPoint d)
    {
        static int Side(FixedPoint p, FixedPoint q, FixedPoint r) =>
            ((BigInteger)(q.X - p.X) * (r.Y - p.Y) - (BigInteger)(q.Y - p.Y) * (r.X - p.X)).Sign;
        return Math.Max(Math.Min(a.X, b.X), Math.Min(c.X, d.X)) <= Math.Min(Math.Max(a.X, b.X), Math.Max(c.X, d.X))
            && Math.Max(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)) <= Math.Min(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y))
            && Side(a, b, c) * Side(a, b, d) <= 0 && Side(c, d, a) * Side(c, d, b) <= 0;
    }
}
