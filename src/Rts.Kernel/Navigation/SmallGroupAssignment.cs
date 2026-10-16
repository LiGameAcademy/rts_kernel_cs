namespace Rts.Kernel.Navigation;

/// <summary>
/// Bounded small-party assignment: forward members choose nearby free slots first.
/// Shape and relative placement are preferences; world placement still checks navigation and bodies.
/// </summary>
internal static class SmallGroupAssignment
{
    internal const int MaximumMembers = 24;

    internal static IReadOnlyList<int> Assign(IReadOnlyList<PlacementMember> members,
        IReadOnlyList<FixedVector2> ideals, SimScalar heading, int anchorIndex)
    {
        if (members.Count is < 1 or > MaximumMembers || ideals.Count != members.Count
            || anchorIndex < 0 || anchorIndex >= members.Count)
            throw new ArgumentException("Invalid small-party assignment.");
        var result = new int[members.Count];
        var occupied = new bool[members.Count];
        result[anchorIndex] = 0;
        occupied[0] = true;
        var cos = SimMath.Cos(heading).Raw;
        var sin = SimMath.Sin(heading).Raw;
        // Wide dot products preserve ordering without rounding projections back to fixed scalars.
        Int128 Forward(FixedVector2 point) => (Int128)point.X.Raw * cos + (Int128)point.Y.Raw * sin;
        Int128 Lateral(FixedVector2 point) => -(Int128)point.X.Raw * sin + (Int128)point.Y.Raw * cos;
        var ordered = Enumerable.Range(0, members.Count).Where(index => index != anchorIndex)
            .OrderByDescending(index => Forward(members[index].Start))
            .ThenBy(index => Lateral(members[index].Start)).ThenBy(index => members[index].Id);
        foreach (var index in ordered)
        {
            var chosen = -1;
            Int128 best = Int128.MaxValue;
            for (var slot = 1; slot < ideals.Count; slot++)
            {
                if (occupied[slot]) continue;
                var distance = SquaredDistance.Between(members[index].Start, ideals[slot]).Raw;
                // Slot order is the stable tie breaker; no total-cost search follows this choice.
                if (distance >= best) continue;
                best = distance;
                chosen = slot;
            }
            result[index] = chosen;
            occupied[chosen] = true;
        }
        return Array.AsReadOnly(result);
    }
}
