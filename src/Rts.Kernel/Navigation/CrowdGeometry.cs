namespace Rts.Kernel.Navigation;

/// <summary>Capsule separation includes entire swept segments, preventing high-speed crossing and swaps.</summary>
internal static class CrowdGeometry
{
    internal static bool Overlap(SimVector2 a, SimVector2 b, double radius,
        SimVector2 c, SimVector2 d, double otherRadius)
    {
        var bx = b.X - a.X; var by = b.Y - a.Y;
        var cx = c.X - a.X; var cy = c.Y - a.Y;
        var dx = d.X - a.X; var dy = d.Y - a.Y;
        var sum = radius + otherRadius;
        var scale = Math.Max(sum, Math.Max(Math.Abs(bx), Math.Max(Math.Abs(by),
            Math.Max(Math.Abs(cx), Math.Max(Math.Abs(cy), Math.Max(Math.Abs(dx), Math.Abs(dy)))))));
        if (!double.IsFinite(scale)) return true;
        if (scale == 0) return true;
        var p = new SimVector2(bx / scale, by / scale);
        var q = new SimVector2(cx / scale, cy / scale);
        var t = new SimVector2(dx / scale, dy / scale);
        var distance = Intersects(SimVector2.Zero, p, q, t) ? 0 :
            Math.Min(Math.Min(PointSegment(SimVector2.Zero, q, t), PointSegment(p, q, t)),
                Math.Min(PointSegment(q, SimVector2.Zero, p), PointSegment(t, SimVector2.Zero, p)));
        var required = sum / scale;
        return distance < required * required * (1 - 1e-12);
    }

    private static double PointSegment(SimVector2 p, SimVector2 a, SimVector2 b)
    {
        var dx = b.X - a.X; var dy = b.Y - a.Y;
        var denominator = dx * dx + dy * dy;
        var fraction = denominator == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / denominator, 0, 1);
        var x = p.X - a.X - fraction * dx; var y = p.Y - a.Y - fraction * dy;
        return x * x + y * y;
    }

    private static bool Intersects(SimVector2 a, SimVector2 b, SimVector2 c, SimVector2 d)
    {
        static double Side(SimVector2 p, SimVector2 q, SimVector2 r) =>
            (q.X - p.X) * (r.Y - p.Y) - (q.Y - p.Y) * (r.X - p.X);
        return Math.Max(Math.Min(a.X, b.X), Math.Min(c.X, d.X)) <= Math.Min(Math.Max(a.X, b.X), Math.Max(c.X, d.X))
            && Math.Max(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)) <= Math.Min(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y))
            && Side(a, b, c) * Side(a, b, d) <= 0 && Side(c, d, a) * Side(c, d, b) <= 0;
    }
}
