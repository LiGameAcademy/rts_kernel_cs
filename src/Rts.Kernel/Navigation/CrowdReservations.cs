namespace Rts.Kernel.Navigation;

internal sealed record CrowdBody(EntityId Id, double Radius, IReadOnlyList<SimVector2> Trace);

/// <summary>Derived, frame-local swept-space reservations. Unprocessed bodies hold their current positions.</summary>
internal sealed class CrowdReservations(double bucketSize)
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

    internal bool Safe(EntityId id, double radius, IReadOnlyList<SimVector2> trace)
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
    internal bool Safe(EntityId id, double radius, SimVector2 from, SimVector2 to)
    {
        Query(Bounds(Math.Min(from.X, to.X), Math.Min(from.Y, to.Y),
            Math.Max(from.X, to.X), Math.Max(from.Y, to.Y), radius));
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

    private (int, int, int, int)? Bounds(IReadOnlyList<SimVector2> trace, double radius)
    {
        var minX = trace[0].X; var maxX = minX;
        var minY = trace[0].Y; var maxY = minY;
        for (var i = 1; i < trace.Count; i++)
        {
            minX = Math.Min(minX, trace[i].X); maxX = Math.Max(maxX, trace[i].X);
            minY = Math.Min(minY, trace[i].Y); maxY = Math.Max(maxY, trace[i].Y);
        }
        return Bounds(minX, minY, maxX, maxY, radius);
    }

    private (int, int, int, int)? Bounds(double minX, double minY, double maxX, double maxY, double radius)
    {
        var left = Math.Floor((minX - radius) / bucketSize);
        var top = Math.Floor((minY - radius) / bucketSize);
        var right = Math.Floor((maxX + radius) / bucketSize);
        var bottom = Math.Floor((maxY + radius) / bucketSize);
        if (!double.IsFinite(left + top + right + bottom) || left <= int.MinValue || top <= int.MinValue
            || right >= int.MaxValue || bottom >= int.MaxValue || (right - left + 1) * (bottom - top + 1) > 4096)
            return null;
        return ((int)left, (int)top, (int)right, (int)bottom);
    }
}
