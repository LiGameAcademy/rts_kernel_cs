namespace Rts.Movement.Prototype;

/// <summary>Bounded connection to a current/forward reference point, never to an old endpoint.</summary>
internal static class RouteConnections
{
    internal static RouteConnection? Find(RouteWorld world, Walker walker, FixedVector2 target, int targetQuarter)
    {
        var route = world.Routes[walker.Task];
        var navigation = world.Navigation;
        if (!navigation.TryWorldToCell(walker.Position, out var start)) return null;
        var others = world.Bodies.Values.Where(body => body.Id != walker.Id).ToArray();
        bool Clear(FixedVector2 from, FixedVector2 to) => ClearOfBodies(from, to, others);
        bool Cell(GridCell cell) => navigation.CanOccupy(cell, 1)
            && (cell == start || Clear(navigation.CellCenter(cell), navigation.CellCenter(cell)));
        bool Edge(GridCell a, GridCell b) => navigation.CanTraverse(a, b, 1)
            && Clear(a == start ? walker.Position : navigation.CellCenter(a), navigation.CellCenter(b));
        var candidates = new List<(FixedVector2 Point, int Cursor, int Quarter)> { (target, walker.Cursor, targetQuarter) };
        for (var i = route.Points.Count - 1; i >= walker.Cursor; i--)
            foreach (var quarter in new[] { walker.TransitQuarters, Math.Min(2, walker.TransitQuarters), 0 })
                candidates.Add((route.Points[i] + walker.Offset * SimScalar.FromRatio(quarter, 4), i, quarter));
        var left = Math.Max(0, start.X - 8);
        var top = Math.Max(0, start.Y - 8);
        var area = new GridArea(left, top, Math.Min(world.Width - left, start.X + 9 - left),
            Math.Min(world.Height - top, start.Y + 9 - top));
        var remaining = 256;
        foreach (var (point, cursor, quarter) in candidates.DistinctBy(candidate => (candidate.Point, candidate.Cursor)))
        {
            if ((point - walker.Position).Length > 256 || (point - walker.Position).Length < 32
                || !navigation.TryWorldToCell(point, out var goal) || !Cell(goal)
                || !Clear(navigation.CellCenter(goal), point)) continue;
            var path = GridPathfinder.FindPathCore(navigation.Grid, start, goal, remaining, 0, Cell, Edge, area);
            remaining -= path.ExpandedNodes;
            if (path.Status == PathStatus.Found)
            {
                var points = new List<FixedVector2> { walker.Position };
                points.AddRange(path.Cells.Skip(1).Select(navigation.CellCenter));
                if (points[^1] != point) points.Add(point);
                return new(point, cursor, route.Version, points.ToArray(), TransitQuarters: quarter);
            }
            if (remaining <= 0) break;
        }
        return null;
    }

    private static bool ClearOfBodies(FixedVector2 from, FixedVector2 to, Walker[] others)
    {
        // The swept segment box expanded by both radii contains every possible collision.
        // Strict separation only: contact on the box boundary still uses the exact predicate.
        var reach = (Int128)62 * SimScalar.Scale;
        var left = (Int128)Math.Min(from.X.Raw, to.X.Raw) - reach;
        var right = (Int128)Math.Max(from.X.Raw, to.X.Raw) + reach;
        var bottom = (Int128)Math.Min(from.Y.Raw, to.Y.Raw) - reach;
        var top = (Int128)Math.Max(from.Y.Raw, to.Y.Raw) + reach;
        foreach (var body in others)
        {
            var centre = body.Position;
            if (centre.X.Raw < left || centre.X.Raw > right || centre.Y.Raw < bottom || centre.Y.Raw > top)
                continue;
            if (CrowdGeometry.Overlap(from, to, 31, centre, centre, 31)) return false;
        }
        return true;
    }
}
