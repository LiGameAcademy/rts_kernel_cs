namespace Rts.Movement.Prototype;

/// <summary>Explicit corridor bends are distinct from quantized straight-travel samples.</summary>
internal static class RouteReference
{
    internal static IReadOnlyList<int> FindCorners(IReadOnlyList<FixedVector2> points)
    {
        var corners = new List<int>();
        for (var i = 1; i + 1 < points.Count; i++)
        {
            var incoming = points[i] - points[i - 1];
            var outgoing = points[i + 1] - points[i];
            if ((Int128)incoming.X.Raw * outgoing.Y.Raw != (Int128)incoming.Y.Raw * outgoing.X.Raw
                || (Int128)incoming.X.Raw * outgoing.X.Raw + (Int128)incoming.Y.Raw * outgoing.Y.Raw < 0)
                corners.Add(i);
        }
        return corners.ToArray();
    }

    internal static SimScalar Project(FixedVector2 position, FixedVector2 from, FixedVector2 to)
    {
        var delta = to - from;
        var length = delta.Length;
        if (length == 0) return 0;
        var relative = position - from;
        var dot = (Int128)relative.X.Raw * delta.X.Raw + (Int128)relative.Y.Raw * delta.Y.Raw;
        return SimScalar.FromRaw(checked((long)(dot / length.Raw)));
    }

    internal static int Start(RouteTask route, int end)
    {
        var start = 0;
        foreach (var corner in route.Corners)
        {
            if (corner >= end) break;
            start = corner;
        }
        return start;
    }

    internal static int Next(RouteTask route, int end) =>
        route.Corners.FirstOrDefault(corner => corner > end, route.Points.Count - 1);

    internal static SquaredDistance Distance(RouteTask route, int end, FixedVector2 position)
    {
        var from = route.Points[Start(route, end)];
        var to = route.Points[end];
        var length = (to - from).Length;
        if (length == 0) return SquaredDistance.Between(from, position);
        var projection = SimMath.Clamp(Project(position, from, to), 0, length);
        return SquaredDistance.Between(from + (to - from) * (projection / length), position);
    }

    internal static int Nearest(RouteTask route, FixedVector2 position)
    {
        var best = Next(route, 0);
        var distance = Distance(route, best, position);
        foreach (var end in route.Corners.Append(route.Points.Count - 1))
        {
            var candidate = Distance(route, end, position);
            if (candidate.CompareTo(distance) < 0) { best = end; distance = candidate; }
        }
        return best;
    }
}
