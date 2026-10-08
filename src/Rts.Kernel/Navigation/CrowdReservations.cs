namespace Rts.Kernel.Navigation;

internal sealed record CrowdBody(EntityId Id, double Radius, IReadOnlyList<SimVector2> Trace);

/// <summary>Derived, frame-local swept-space reservations. Unprocessed bodies hold their current positions.</summary>
internal sealed class CrowdReservations(double bucketSize)
{
    private readonly Dictionary<EntityId, CrowdBody> _bodies = [];
    private readonly Dictionary<(int X, int Y), HashSet<EntityId>> _buckets = [];
    private readonly HashSet<EntityId> _global = [];

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
        var bounds = Bounds(trace, radius);
        var owners = new HashSet<EntityId>(_global);
        if (bounds is null) owners.UnionWith(_bodies.Keys);
        else
        {
            var (left, top, right, bottom) = bounds.Value;
            for (var y = top; y <= bottom; y++)
            for (var x = left; x <= right; x++)
                if (_buckets.TryGetValue((x, y), out var found)) owners.UnionWith(found);
        }
        foreach (var other in owners)
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

    private (int, int, int, int)? Bounds(IReadOnlyList<SimVector2> trace, double radius)
    {
        var left = Math.Floor((trace.Min(p => p.X) - radius) / bucketSize);
        var top = Math.Floor((trace.Min(p => p.Y) - radius) / bucketSize);
        var right = Math.Floor((trace.Max(p => p.X) + radius) / bucketSize);
        var bottom = Math.Floor((trace.Max(p => p.Y) + radius) / bucketSize);
        if (!double.IsFinite(left + top + right + bottom) || left <= int.MinValue || top <= int.MinValue
            || right >= int.MaxValue || bottom >= int.MaxValue || (right - left + 1) * (bottom - top + 1) > 4096)
            return null;
        return ((int)left, (int)top, (int)right, (int)bottom);
    }
}
