namespace Rts.Kernel.Navigation;

/// <summary>
/// Bounded small-party assignment: forward members choose nearby free slots first.
/// Shape and relative placement are preferences; world placement still checks navigation and bodies.
/// </summary>
internal static class SmallGroupAssignment
{
    internal const int MaximumMembers = 24;

    internal static IReadOnlyList<int> Assign(IReadOnlyList<PlacementMember> members,
        IReadOnlyList<FixedVector2> ideals, SimScalar heading, int anchorIndex, bool preserveAnchor = true)
    {
        if (members.Count is < 1 or > MaximumMembers || ideals.Count != members.Count
            || anchorIndex < 0 || anchorIndex >= members.Count)
            throw new ArgumentException("Invalid small-party assignment.");
        // Compare local offsets, not the long translation to the destination. Otherwise
        // the front row greedily occupies the rear slots and seals the approach.
        var startCenter = Center(members.Select(member => member.Start));
        var goalCenter = Center(ideals);
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
                var distance = SquaredDistance.Between(new(members[index].Start.X - startCenter.X + goalCenter.X,
                    members[index].Start.Y - startCenter.Y + goalCenter.Y), ideals[slot]).Raw;
                // Slot order breaks ties before the bounded pair exchanges below.
                if (distance >= best) continue;
                best = distance;
                chosen = slot;
            }
            result[index] = chosen;
            occupied[chosen] = true;
        }
        Relax(members, ideals, result, anchorIndex, preserveAnchor);
        return Array.AsReadOnly(result);
    }
    private static FixedVector2 Center(IEnumerable<FixedVector2> points)
    {
        Int128 x = 0;
        Int128 y = 0;
        var count = 0;
        foreach (var point in points)
        {
            x += point.X.Raw;
            y += point.Y.Raw;
            count++;
        }
        return FixedVector2.FromRaw(checked((long)(x / count)), checked((long)(y / count)));
    }

    // Cold command probes attribute a tier-transition spike to this bounded hot loop.
    // Optimize only this method; preserve exchange order and fixed-point calculations.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
    private static void Relax(IReadOnlyList<PlacementMember> members, IReadOnlyList<FixedVector2> ideals,
        int[] result, int anchorIndex, bool preserveAnchor)
    {
        // A bounded pair exchange removes avoidable crossings left by greedy selection.
        var exchangeOrder = Enumerable.Range(0, members.Count).OrderBy(i => members[i].Id).ToArray();
        Span<Int128> currentCosts = stackalloc Int128[MaximumMembers];
        Span<bool> knownCosts = stackalloc bool[MaximumMembers];
        knownCosts.Clear();
        for (var pass = 0; pass < members.Count; pass++)
        {
            var changed = false;
            for (var ai = 0; ai < members.Count; ai++)
            for (var bi = ai + 1; bi < members.Count; bi++)
            {
                var a = exchangeOrder[ai];
                var b = exchangeOrder[bi];
                if (preserveAnchor && (a == anchorIndex || b == anchorIndex)) continue;
                // Evaluate only costs visited by the original loop, including overflow checks.
                if (!knownCosts[a])
                {
                    currentCosts[a] = SquaredDistance.Between(members[a].Start, ideals[result[a]]).Raw;
                    knownCosts[a] = true;
                }
                if (!knownCosts[b])
                {
                    currentCosts[b] = SquaredDistance.Between(members[b].Start, ideals[result[b]]).Raw;
                    knownCosts[b] = true;
                }
                var before = currentCosts[a] + currentCosts[b];
                var afterA = SquaredDistance.Between(members[a].Start, ideals[result[b]]).Raw;
                var afterB = SquaredDistance.Between(members[b].Start, ideals[result[a]]).Raw;
                var after = afterA + afterB;
                if (after >= before) continue;
                (result[a], result[b]) = (result[b], result[a]);
                currentCosts[a] = afterA;
                currentCosts[b] = afterB;
                changed = true;
            }
            if (!changed) break;
        }
    }

}
