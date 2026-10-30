namespace Rts.Movement.Prototype;

/// <summary>One terrain route per task; transient member connections cannot become a new route.</summary>
internal static class RoutePlanning
{
    internal static FixedVector2 Centre(IReadOnlyList<Walker> members) =>
        members.Aggregate(FixedVector2.Zero, (sum, body) => sum + body.Position) / members.Count;

    internal static IEnumerable<FixedVector2> Candidates(RouteWorld world, FixedVector2 goal, SimScalar radius)
    {
        if (!world.Navigation.TryWorldToCell(goal, out var cell)) yield break;
        var cells = new List<FixedVector2>();
        var range = (radius / 32).FloorToInt() + 1;
        for (var y = -range; y <= range; y++)
        for (var x = -range; x <= range; x++)
        {
            var point = world.Navigation.CellCenter(new(cell.X + x, cell.Y + y));
            if ((point - goal).Length <= radius) cells.Add(point);
        }
        foreach (var point in cells.OrderBy(point => SquaredDistance.Between(point, goal))
            .ThenBy(point => point.Y).ThenBy(point => point.X)) yield return point;
    }

    internal static bool TryRoute(RouteWorld world, FixedVector2 start, FixedVector2 goal,
        IReadOnlyList<Walker> blockers, out IReadOnlyList<FixedVector2> points, out IReadOnlyList<int> corners)
    {
        points = [];
        corners = [];
        var navigation = world.Navigation;
        if (!navigation.TryWorldToCell(start, out var first) || !navigation.CanOccupy(first, 1))
        {
            var nearest = Candidates(world, start, 256).FirstOrDefault(point => navigation.CanStandAt(point, 31));
            if (nearest == default || !navigation.TryWorldToCell(nearest, out first)) return false;
            start = nearest;
        }
        if (!navigation.TryWorldToCell(goal, out var last)) return false;
        bool BodyClear(FixedVector2 from, FixedVector2 to) => blockers.All(body =>
            !CrowdGeometry.Overlap(from, to, 31, body.Position, body.Position, 31));
        var direct = StraightMovePath.TryFind(navigation, start, goal, 1);
        if (direct is not null && BodyClear(start, goal)) { points = direct.Count == 1 ? new FixedVector2[] { start, goal } : direct; return true; }
        bool Cell(GridCell cell) => navigation.CanOccupy(cell, 1)
            && (cell == first || BodyClear(navigation.CellCenter(cell), navigation.CellCenter(cell)));
        bool Edge(GridCell from, GridCell to) => navigation.CanTraverse(from, to, 1)
            && BodyClear(from == first ? start : navigation.CellCenter(from), navigation.CellCenter(to));
        var path = GridPathfinder.FindPathCore(navigation.Grid, first, last, RouteSettings.SearchNodes, 0, Cell, Edge);
        if (path.Status != PathStatus.Found) return false;
        var route = new List<FixedVector2> { start };
        route.AddRange(path.Cells.Skip(1).Select(navigation.CellCenter));
        if (route[^1] != goal) route.Add(goal);
        points = route.ToArray();
        corners = RouteReference.FindCorners(points);
        return true;
    }

    internal static IReadOnlyList<FixedVector2>? TryDirect(RouteWorld world, FixedVector2 from,
        FixedVector2 to, IReadOnlyList<Walker> blockers)
    {
        var direct = StraightMovePath.TryFind(world.Navigation, from, to, 1);
        if (direct is null || blockers.Any(body => CrowdGeometry.Overlap(from, to, 31, body.Position, body.Position, 31)))
            return null;
        return new FixedVector2[] { from, to };
    }

    internal static (Walker Body, FixedVector2 Target, int Quarter) Follow(RouteWorld world, Walker body, RouteTask route)
        => RouteNavigation.Follow(world, body);
}
