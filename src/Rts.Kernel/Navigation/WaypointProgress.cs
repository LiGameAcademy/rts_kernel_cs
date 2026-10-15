namespace Rts.Kernel.Navigation;

/// <summary>Project progress onto the original path segment; each coordinate rounds at most one raw unit.</summary>
internal static class WaypointProgress
{
    internal static FixedVector2 Advance(MoveOrder order, int next, FixedVector2 position, SimScalar distance)
    {
        var origin = next > 0 ? order.Waypoints[next - 1] : position;
        var target = order.Waypoints[next];
        var dx = (Int128)target.X.Raw - origin.X.Raw;
        var dy = (Int128)target.Y.Raw - origin.Y.Raw;
        var length = SquaredDistance.Between(origin, target).Length.Raw;
        if (length == 0) return target;
        var projection = checked(((Int128)position.X.Raw - origin.X.Raw) * dx
            + ((Int128)position.Y.Raw - origin.Y.Raw) * dy);
        var current = Int128.Clamp(projection / length, 0, length);
        var progress = Int128.Min(length, current + distance.Raw);
        return FixedVector2.FromRaw(checked(origin.X.Raw + (long)(dx * progress / length)),
            checked(origin.Y.Raw + (long)(dy * progress / length)));
    }

    internal static bool Contains(FixedVector2 origin, FixedVector2 target, FixedVector2 position)
    {
        var dx = (Int128)target.X.Raw - origin.X.Raw;
        var dy = (Int128)target.Y.Raw - origin.Y.Raw;
        var px = (Int128)position.X.Raw - origin.X.Raw;
        var py = (Int128)position.Y.Raw - origin.Y.Raw;
        var lengthSquared = checked(dx * dx + dy * dy);
        if (lengthSquared == 0) return position == origin;
        var dot = checked(px * dx + py * dy);
        var cross = checked(px * dy - py * dx);
        // One raw rounding error per axis; two raw units also cover reconstructed progress endpoints.
        var tolerance = checked((Int128.Abs(dx) + Int128.Abs(dy)) * 2);
        return Int128.Abs(cross) <= tolerance && dot >= -tolerance && dot <= lengthSquared + tolerance;
    }
}
