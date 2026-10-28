namespace Rts.Movement.Prototype;

/// <summary>Search-local fast paths; final movement safety keeps its original predicates.</summary>
internal static class RouteYieldGeometry
{
    private static readonly Int128 GoalSpacingRaw = 64L * SimScalar.Scale;
    private static readonly Int128 GoalSpacingSquared = GoalSpacingRaw * GoalSpacingRaw;

    // floor(sqrt(squared)) < integer R iff squared < R*R. Exact64 remains legal.
    internal static bool GoalOverlap(FixedVector2 a, FixedVector2 b) =>
        SquaredDistance.Between(a, b).Raw < GoalSpacingSquared;
}
