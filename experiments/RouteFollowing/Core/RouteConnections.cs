namespace Rts.Movement.Prototype;

/// <summary>Bounded connection to a current/forward reference point, never to an old endpoint.</summary>
internal static class RouteConnections
{
    internal static RouteConnection? Find(RouteWorld world, Walker walker, FixedVector2 target)
    {
        var route = world.Routes[walker.Task];
        var navigation = world.Navigation;
        if (!navigation.TryWorldToCell(walker.Position, out var start)) return null;
        var others = world.Bodies.Values.Where(body => body.Id != walker.Id).ToArray();
        bool Clear(FixedVector2 from, FixedVector2 to) => others.All(body =>
            !CrowdGeometry.Overlap(from, to, 31, body.Position, body.Position, 31));
        bool Cell(GridCell cell) => navigation.CanOccupy(cell, 1)
            && (cell == start || Clear(navigation.CellCenter(cell), navigation.CellCenter(cell)));
        bool Edge(GridCell a, GridCell b) => navigation.CanTraverse(a, b, 1)
            && Clear(a == start ? walker.Position : navigation.CellCenter(a), navigation.CellCenter(b));
        var candidates = new List<(FixedVector2 Point, int Cursor)> { (target, walker.Cursor) };
        for (var i = route.Points.Count - 1; i >= walker.Cursor; i--)
            foreach (var fraction in new SimScalar[] { 1, SimScalar.FromRatio(1, 2), 0 })
                candidates.Add((route.Points[i] + walker.Offset * fraction, i));
        var left = Math.Max(0, start.X - 8);
        var top = Math.Max(0, start.Y - 8);
        var area = new GridArea(left, top, Math.Min(world.Width - left, start.X + 9 - left),
            Math.Min(world.Height - top, start.Y + 9 - top));
        var remaining = 256;
        foreach (var (point, cursor) in candidates.Distinct())
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
                return new(point, cursor, route.Version, points.ToArray());
            }
            if (remaining <= 0) break;
        }
        return null;
    }
}
