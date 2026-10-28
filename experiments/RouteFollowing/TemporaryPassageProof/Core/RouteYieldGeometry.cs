namespace Rts.Movement.Prototype;

/// <summary>Search-local fast paths; final movement safety keeps its original predicates.</summary>
internal static class RouteYieldGeometry
{
    private static readonly Int128 GoalSpacingRaw = 64L * SimScalar.Scale;
    private static readonly Int128 GoalSpacingSquared = GoalSpacingRaw * GoalSpacingRaw;

    // floor(sqrt(squared)) < integer R iff squared < R*R. Exact64 remains legal.
    internal static bool GoalOverlap(FixedVector2 a, FixedVector2 b) =>
        SquaredDistance.Between(a, b).Raw < GoalSpacingSquared;

    internal static bool Overlap(FixedVector2 a, FixedVector2 b, SimScalar radius,
        FixedVector2 c, FixedVector2 d, SimScalar otherRadius)
    {
        var sum = (Int128)radius.Raw + otherRadius.Raw;
        // Strict separation only. Touching boxes retain the exact capsule predicate.
        // Wide additions avoid overflowing at either end of admitted coordinates.
        if (sum > 0 && (
            (Int128)Math.Max(a.X.Raw, b.X.Raw) + sum < Math.Min(c.X.Raw, d.X.Raw)
            || (Int128)Math.Max(c.X.Raw, d.X.Raw) + sum < Math.Min(a.X.Raw, b.X.Raw)
            || (Int128)Math.Max(a.Y.Raw, b.Y.Raw) + sum < Math.Min(c.Y.Raw, d.Y.Raw)
            || (Int128)Math.Max(c.Y.Raw, d.Y.Raw) + sum < Math.Min(a.Y.Raw, b.Y.Raw)))
            return false;
        return CrowdGeometry.Overlap(a, b, radius, c, d, otherRadius);
    }
}
