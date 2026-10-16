namespace Rts.Kernel.Navigation;

/// <summary>World-space straight travel, conservatively checked through adjacent clearance cells.</summary>
internal static class StraightMovePath
{
    internal static IReadOnlyList<FixedVector2>? TryFind(NavigationState navigation,
        FixedVector2 start, FixedVector2 goal, int clearance)
    {
        if (!navigation.TryWorldToCell(start, out var previous) || !navigation.CanOccupy(previous, clearance))
            return null;
        var length = SquaredDistance.Between(start, goal).Length;
        var stride = navigation.Grid.CellSize / 2;
        var count = checked((int)((length.Raw + stride.Raw - 1) / stride.Raw));
        var points = new List<FixedVector2>(count + 1) { start };
        // Half-cell samples cannot skip an intervening cell. Diagonal transitions check
        // both side cells too, so corner cutting is rejected rather than interpolated through.
        for (var i = 1; i <= count; i++)
        {
            var fraction = SimScalar.FromRatio(i, count);
            var point = i == count ? goal : new FixedVector2(
                start.X + (goal.X - start.X) * fraction,
                start.Y + (goal.Y - start.Y) * fraction);
            if (!navigation.TryWorldToCell(point, out var cell)
                || !navigation.CanTraverse(previous, cell, clearance)) return null;
            points.Add(point);
            previous = cell;
        }
        return points.AsReadOnly();
    }
}
