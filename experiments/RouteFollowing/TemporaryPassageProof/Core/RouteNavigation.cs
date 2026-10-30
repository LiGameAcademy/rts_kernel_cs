namespace Rts.Movement.Prototype;

/// <summary>A member follows its task-owned goal; recovery may route around currently settled bodies.</summary>
internal static class RouteNavigation
{
    internal static RouteTask Reference(Walker body) => body.Navigation is { } reference
        ? new(body.Task, reference.Goal, reference.Points, reference.Version,
            reference.MapRevision, reference.Corners)
        : throw new InvalidOperationException("Member reference has not been prepared.");

    internal static Walker Prepare(RouteWorld world, Walker body, bool rebuild = false, bool allowShared = true, bool avoidSettled = false)
    {
        var prior = body.Navigation;
        if (!rebuild && prior is not null && prior.MapRevision == world.Revision && prior.Goal == body.Endpoint)
            return body;
        var blockers = world.Bodies.Values.Where(other => other.Id != body.Id
            && RoutePlacements.IsFixed(other) && (avoidSettled || other.Task != body.Task || other.Task == 0)).ToArray();
        var version = (prior?.Version ?? 0) + 1;
        IReadOnlyList<FixedVector2> points;
        IReadOnlyList<int> corners;
        var kind = avoidSettled ? MemberReferenceKind.Independent : MemberReferenceKind.Direct;
        if (RoutePlanning.TryDirect(world, body.Position, body.Endpoint, blockers) is { } direct)
        {
            points = direct;
            corners = [];
        }
        else if (allowShared && !avoidSettled && TryShared(world, body, blockers, out points))
        {
            corners = RouteReference.FindCorners(points);
            kind = MemberReferenceKind.SharedSuffix;
        }
        else
        {
            if (!RoutePlanning.TryRoute(world, body.Position, body.Endpoint, blockers, out points, out corners))
                return body with { Navigation = null, Passage = null, Connection = null, Cursor = 0, RouteVersion = 0,
                    Progress = 0, ProgressCursor = 0, ProgressVersion = 0 };
            kind = MemberReferenceKind.Independent;
        }
        return body with
        {
            Navigation = new(body.Endpoint, world.Revision, version, kind, points, corners),
            Cursor = 0, RouteVersion = version, Connection = null, Passage = null,
            // Changing a route cannot manufacture signed physical progress or erase waits.
            Progress = 0, ProgressCursor = 0, ProgressVersion = 0, TransitQuarters = 4
        };
    }

    internal static (Walker Body, FixedVector2 Target, int Quarter) Follow(RouteWorld world, Walker source)
    {
        var body = Prepare(world, source);
        if (body.Navigation is null) return (body, body.Position, 4);
        if (body.Navigation.Kind == MemberReferenceKind.Independent)
        {
            // A completed yield can open a direct leg before the old detour reaches its next corner.
            // This is only a motion proposal: moving peers still go through timed collision resolution.
            var fixedBodies = world.Bodies.Values.Where(other => other.Id != body.Id && RoutePlacements.IsFixed(other)).ToArray();
            if (RoutePlanning.TryDirect(world, body.Position, body.Endpoint, fixedBodies) is not null)
                body = Prepare(world, body, rebuild: true, allowShared: false);
        }
        var selected = SelectTarget(world, body);
        if (StraightMovePath.TryFind(world.Navigation, selected.Body.Position, selected.Target, 1) is not null)
            return (selected.Body, selected.Target, 4);
        // Actual sidesteps may invalidate the entry leg even though map/goal versions still match.
        // Repair once from the actual position; do not repeatedly reconnect the same broken shared suffix.
        body = Prepare(world, selected.Body, rebuild: true, allowShared: false);
        if (body.Navigation is null) return (body, body.Position, 4);
        selected = SelectTarget(world, body);
        return (selected.Body, selected.Target, 4);
    }

    private static (Walker Body, FixedVector2 Target) SelectTarget(RouteWorld world, Walker body)
    {
        var route = Reference(body);
        var cursor = Math.Min(body.Cursor, route.Points.Count - 1);
        while (cursor < route.Points.Count - 1 && body.Position == route.Points[cursor]) cursor++;
        var target = route.Points[cursor];
        var blockers = body.Navigation!.Kind == MemberReferenceKind.Independent
            ? world.Bodies.Values.Where(other => other.Id != body.Id && RoutePlacements.IsFixed(other)).ToArray()
            : [];
        // An independent recovery reference must not shortcut through the bodies it routed around.
        // Read their current positions: departing bodies never become permanent route obstacles.
        for (var i = cursor; i < route.Points.Count; i++)
        {
            if (SquaredDistance.Between(body.Position, route.Points[i]).Raw > (Int128)192 * 192 * SimScalar.Scale * SimScalar.Scale)
                break;
            if (RoutePlanning.TryDirect(world, body.Position, route.Points[i], blockers) is null) continue;
            cursor = i;
            target = route.Points[i];
        }
        return (body with { Cursor = cursor }, target);
    }

    private static bool TryShared(RouteWorld world, Walker body, Walker[] blockers,
        out IReadOnlyList<FixedVector2> points)
    {
        points = [];
        var shared = world.Routes[body.Task];
        if (shared.MapRevision != world.Revision
            || RoutePlanning.TryDirect(world, shared.Points[^1], body.Endpoint, blockers) is null) return false;
        // Reuse only a reachable local suffix. No requirement to reach a fixed centre-line interval.
        for (var i = shared.Points.Count - 1; i >= 0; i--)
        {
            if ((shared.Points[i] - body.Position).Length > 192
                || RoutePlanning.TryDirect(world, body.Position, shared.Points[i], blockers) is null) continue;
            var suffix = new List<FixedVector2> { body.Position };
            suffix.AddRange(shared.Points.Skip(i));
            if (suffix[^1] != body.Endpoint) suffix.Add(body.Endpoint);
            if (suffix.Zip(suffix.Skip(1)).Any(edge => RoutePlanning.TryDirect(world, edge.First, edge.Second, blockers) is null))
                continue;
            points = suffix.ToArray();
            return true;
        }
        return false;
    }
}
