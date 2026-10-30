namespace Rts.Kernel.Navigation;

internal sealed record CrowdBody(EntityId Id, SimScalar Radius, IReadOnlyList<FixedVector2> Trace,
    IReadOnlyList<SimScalar>? Times = null)
{
    internal bool HasMotion => Trace.Any(point => point != Trace[0]);
    internal static CrowdBody Stationary(EntityId id, SimScalar radius, FixedVector2 point) =>
        new(id, radius, new[] { point, point }, new SimScalar[] { 0, 1 });
}

/// <summary>Derived frame trajectories. Local path searches retain conservative swept-space queries.</summary>
internal sealed class CrowdReservations(SimScalar bucketSize)
{
    private readonly Dictionary<EntityId, CrowdBody> _bodies = [];
    private readonly Dictionary<(int X, int Y), HashSet<EntityId>> _buckets = [];
    private readonly HashSet<EntityId> _global = [];
    // Queries are synchronous and never nested. Reuse only derived scratch storage.
    private readonly HashSet<EntityId> _query = [];

    internal void Put(CrowdBody body)
    {
        _bodies[body.Id] = body;
        var bounds = Bounds(body.Trace, body.Radius);
        if (bounds is null) { _global.Add(body.Id); return; }
        var (left, top, right, bottom) = bounds.Value;
        for (var y = top; y <= bottom; y++)
            for (var x = left; x <= right; x++)
            {
                if (!_buckets.TryGetValue((x, y), out var owners)) _buckets.Add((x, y), owners = []);
                owners.Add(body.Id);
            }
    }

    internal bool Safe(EntityId id, SimScalar radius, IReadOnlyList<FixedVector2> trace)
    {
        Query(Bounds(trace, radius));
        foreach (var other in _query)
        {
            if (other == id) continue;
            var body = _bodies[other];
            for (var i = 1; i < trace.Count; i++)
                for (var j = 1; j < body.Trace.Count; j++)
                    if (CrowdGeometry.Overlap(trace[i - 1], trace[i], radius,
                        body.Trace[j - 1], body.Trace[j], body.Radius)) return false;
        }
        return true;
    }

    // Local A* performs thousands of point/edge queries; avoid allocating a two-point array for each.
    internal bool Safe(EntityId id, SimScalar radius, FixedVector2 from, FixedVector2 to)
    {
        Query(Bounds(SimMath.Min(from.X, to.X), SimMath.Min(from.Y, to.Y),
            SimMath.Max(from.X, to.X), SimMath.Max(from.Y, to.Y), radius));
        foreach (var other in _query)
        {
            if (other == id) continue;
            var body = _bodies[other];
            for (var j = 1; j < body.Trace.Count; j++)
                if (CrowdGeometry.Overlap(from, to, radius,
                    body.Trace[j - 1], body.Trace[j], body.Radius)) return false;
        }
        return true;
    }

    internal CrowdBody Read(EntityId id) => _bodies[id];

    // Recovery needs every actual contact, not the smallest ID used by the resolver.
    // Reuse the same spatial buckets and timed predicate; this query does not alter reservations.
    internal EntityId[] MotionConflicts(CrowdBody body)
    {
        Query(Bounds(body.Trace, body.Radius));
        return _query.Where(other => other != body.Id && TimedCrowdGeometry.Overlap(body, _bodies[other]))
            .Order().ToArray();
    }

    internal EntityId? FirstMotionConflict(CrowdBody body)
    {
        Query(Bounds(body.Trace, body.Radius));
        EntityId? first = null;
        foreach (var other in _query)
        {
            if (other == body.Id || (first is not null && other.CompareTo(first.Value) >= 0)) continue;
            if (TimedCrowdGeometry.Overlap(body, _bodies[other])) first = other;
        }
        return first;
    }

    private void Query((int Left, int Top, int Right, int Bottom)? bounds)
    {
        _query.Clear();
        _query.UnionWith(_global);
        if (bounds is null) { _query.UnionWith(_bodies.Keys); return; }
        var (left, top, right, bottom) = bounds.Value;
        for (var y = top; y <= bottom; y++)
            for (var x = left; x <= right; x++)
                if (_buckets.TryGetValue((x, y), out var found)) _query.UnionWith(found);
    }

    private (int, int, int, int)? Bounds(IReadOnlyList<FixedVector2> trace, SimScalar radius)
    {
        var minX = trace[0].X; var maxX = minX;
        var minY = trace[0].Y; var maxY = minY;
        for (var i = 1; i < trace.Count; i++)
        {
            minX = SimMath.Min(minX, trace[i].X); maxX = SimMath.Max(maxX, trace[i].X);
            minY = SimMath.Min(minY, trace[i].Y); maxY = SimMath.Max(maxY, trace[i].Y);
        }
        return Bounds(minX, minY, maxX, maxY, radius);
    }

    private (int, int, int, int)? Bounds(SimScalar minX, SimScalar minY, SimScalar maxX, SimScalar maxY, SimScalar radius)
    {
        var left = FixedGridMath.FloorIndex(minX - radius, SimScalar.Zero, bucketSize);
        var top = FixedGridMath.FloorIndex(minY - radius, SimScalar.Zero, bucketSize);
        var right = FixedGridMath.FloorIndex(maxX + radius, SimScalar.Zero, bucketSize);
        var bottom = FixedGridMath.FloorIndex(maxY + radius, SimScalar.Zero, bucketSize);
        if (left <= int.MinValue || top <= int.MinValue
            || right >= int.MaxValue || bottom >= int.MaxValue || ((Int128)right - left + 1) * ((Int128)bottom - top + 1) > 4096)
            return null;
        return ((int)left, (int)top, (int)right, (int)bottom);
    }
}
