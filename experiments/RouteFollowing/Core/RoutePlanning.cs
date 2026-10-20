namespace Rts.Movement.Prototype;

/// <summary>One terrain route per task; transient member connections cannot become a new route.</summary>
internal static class RoutePlanning
{
    internal static FixedVector2 Centre(IReadOnlyList<Walker> members) =>
        members.Aggregate(FixedVector2.Zero, (sum, body) => sum + body.Position) / members.Count;

    internal static bool TryAssign(RouteWorld world, Walker[] members, FixedVector2 goal,
        out FixedVector2 anchor, out Dictionary<ulong, FixedVector2> assigned)
    {
        anchor = goal;
        assigned = [];
        var occupied = world.Bodies.Values.Where(body => !members.Any(member => member.Id == body.Id)
            && body.Phase is WalkPhase.Stopped or WalkPhase.Idle or WalkPhase.Settled).Select(body => body.Position).ToList();
        bool Free(FixedVector2 point) => world.Navigation.CanStandAt(point, RouteSettings.Radius)
            && occupied.All(other => (other - point).Length >= RouteSettings.Radius * 2 + 2);
        var centre = Centre(members);
        if (!world.Navigation.CanStandAt(anchor, RouteSettings.Radius))
        {
            var adjusted = Candidates(world, goal, 256).FirstOrDefault(point => Free(point));
            if (adjusted == default) return false;
            anchor = adjusted;
        }
        var slots = FormationLayout.Create(FormationKind.Compact, members.Length, 72, anchor,
            SimMath.Atan2(anchor.Y - centre.Y, anchor.X - centre.X));
        var match = FormationLayout.Match(members.Select(body => body.Position).ToArray(), slots, 0);
        for (var i = 0; i < members.Length; i++)
        {
            var preferred = slots[match[i]];
            var point = Free(preferred) ? preferred : Candidates(world, preferred, 256).FirstOrDefault(Free);
            if (point == default || !Free(point)) return false;
            if (!world.Navigation.TryWorldToCell(members[i].Position, out var start)
                || !world.Navigation.TryWorldToCell(point, out var end)
                || !world.Navigation.Components.Connected(start, end, 1)) return false;
            assigned.Add(members[i].Id, point);
            occupied.Add(point);
        }
        return true;
    }

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
        IReadOnlyList<Walker> blockers, out IReadOnlyList<FixedVector2> points)
    {
        points = [];
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
        return true;
    }

    internal static (Walker Body, FixedVector2 Target) Follow(RouteWorld world, Walker body, RouteTask route)
    {
        var sameVersion = body.RouteVersion == route.Version;
        var cursor = sameVersion ? body.Cursor : route.Points.Select((point, index) => (point, index))
            .OrderBy(item => (item.point + body.Offset - body.Position).Length).ThenBy(item => item.index)
            .First().index;
        if (!sameVersion) body = body with { BestRemaining = SimScalar.MaxValue };
        if (cursor < route.Points.Count - 1
            && (body.Position - (route.Points[cursor] + body.Offset)).Length <= 9) cursor++;
        var best = cursor;
        var target = route.Points[Math.Min(cursor, route.Points.Count - 1)];
        // Look ahead on the shared reference, never on the previous connection's bend list.
        for (var i = cursor; i < route.Points.Count; i++)
        {
            var candidate = route.Points[i] + body.Offset;
            foreach (var fraction in new SimScalar[] { 1, SimScalar.FromRatio(3, 4), SimScalar.FromRatio(1, 2),
                SimScalar.FromRatio(1, 4), 0 })
            {
                candidate = route.Points[i] + body.Offset * fraction;
                if ((candidate - body.Position).Length > 192) continue;
                if (StraightMovePath.TryFind(world.Navigation, body.Position, candidate, 1) is null) continue;
                best = i;
                target = candidate;
                break;
            }
            // A reference bend represents a corridor decision, including a temporary body
            // avoided by the latest retry. Static visibility cannot erase that decision.
            if (i > 0 && i + 1 < route.Points.Count)
            {
                var incoming = route.Points[i] - route.Points[i - 1];
                var outgoing = route.Points[i + 1] - route.Points[i];
                if ((Int128)incoming.X.Raw * outgoing.Y.Raw != (Int128)incoming.Y.Raw * outgoing.X.Raw) break;
            }
        }
        if (best == route.Points.Count - 1 || (body.Position - body.Preference).Length <= 200)
        {
            target = body.Endpoint;
            if (!EndpointFree(world, body, target))
            {
                var replacement = Candidates(world, body.Preference, 156)
                    .Where(point => EndpointFree(world, body, point)
                        && StraightMovePath.TryFind(world.Navigation, body.Position, point, 1) is not null)
                    .OrderBy(point => (point - body.Position).Length).FirstOrDefault();
                if (replacement != default) { body = body with { Endpoint = replacement }; target = replacement; }
            }
        }
        body = body with { Cursor = best, RouteVersion = route.Version };
        return (body, target);
    }

    internal static bool EndpointFree(RouteWorld world, Walker body, FixedVector2 point) =>
        world.Navigation.CanStandAt(point, 31) && world.Bodies.Values.All(other => other.Id == body.Id
            || (other.Position - point).Length >= 64);

    internal static SimScalar Remaining(Walker body, RouteTask route)
    {
        var index = Math.Clamp(body.Cursor, 1, route.Points.Count - 1);
        var from = route.Points[index - 1];
        var to = route.Points[index];
        var delta = to - from;
        var length = delta.Length;
        var remaining = SimScalar.Zero;
        for (var i = index + 1; i < route.Points.Count; i++) remaining += (route.Points[i] - route.Points[i - 1]).Length;
        if (length == 0) return remaining;
        var relative = body.Position - from;
        var projection = (relative.X * delta.X + relative.Y * delta.Y) / length;
        return remaining + length - SimMath.Clamp(projection, 0, length);
    }
}
