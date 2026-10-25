namespace Rts.Movement.Prototype;

/// <summary>Derived execution leases for the remaining legs of one bounded yield plan.</summary>
internal sealed class RouteYieldCorridors
{
    private sealed record Corridor(ulong Task, ulong Member, IReadOnlyList<ulong> Participants,
        FixedVector2 From, FixedVector2 To);
    private readonly Corridor[] _corridors;

    internal RouteYieldCorridors(RouteWorld world)
    {
        _corridors = world.Routes.Values.Where(route => route.Yield is not null)
            .SelectMany(route => route.Yield!.Members.Skip(route.Yield.Next).Select(id =>
                new Corridor(route.Id, id, route.Yield.Members, world.Bodies[id].Position, world.Bodies[id].Endpoint)))
            .ToArray();
    }

    internal ulong? FirstConflict(Walker body, IReadOnlyList<FixedVector2> trace)
    {
        foreach (var corridor in _corridors)
        {
            if (corridor.Task != body.Task || corridor.Participants.Contains(body.Id)) continue;
            if (!Overlaps(corridor, trace)) continue;
            if (CrowdGeometry.Overlap(corridor.From, corridor.To, 31, body.Position, body.Position, 31)
                && Exits(corridor, trace)) continue;
            return corridor.Member;
        }
        return null;
    }

    private static bool Overlaps(Corridor corridor, IReadOnlyList<FixedVector2> trace)
    {
        for (var i = 1; i < trace.Count; i++)
            if (CrowdGeometry.Overlap(corridor.From, corridor.To, 31, trace[i - 1], trace[i], 31)) return true;
        return false;
    }

    private static bool Exits(Corridor corridor, IReadOnlyList<FixedVector2> trace)
    {
        // A snapshot may already contain an occupant. Only outward motion is allowed:
        // distance to this convex segment cannot decrease along the supporting normal.
        var normal = Normal(trace[0], corridor.From, corridor.To);
        if (normal == default) normal = Normal(trace[^1], corridor.From, corridor.To);
        if (normal == default) return false;
        Int128 Projection(FixedVector2 point) => normal.X * ((Int128)point.X.Raw - trace[0].X.Raw)
            + normal.Y * ((Int128)point.Y.Raw - trace[0].Y.Raw);
        return trace.All(point => Projection(point) >= 0) && Projection(trace[^1]) > 0;
    }

    private static (Int128 X, Int128 Y) Normal(FixedVector2 point, FixedVector2 from, FixedVector2 to)
    {
        var dx = (Int128)to.X.Raw - from.X.Raw;
        var dy = (Int128)to.Y.Raw - from.Y.Raw;
        var px = (Int128)point.X.Raw - from.X.Raw;
        var py = (Int128)point.Y.Raw - from.Y.Raw;
        var length = dx * dx + dy * dy;
        var projection = px * dx + py * dy;
        if (projection <= 0 || length == 0) return (px, py);
        if (projection >= length) return (px - dx, py - dy);
        var side = (dx * py - dy * px).CompareTo(0);
        return (-dy * side, dx * side);
    }
}
