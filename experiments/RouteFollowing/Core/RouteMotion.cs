namespace Rts.Movement.Prototype;

/// <summary>Time-aligned safety arbitration never supplies a new authoritative route.</summary>
internal static class RouteMotion
{
    private sealed record Proposal(Walker Body, MovementStep Step, bool Final, int Quarter);

    internal static void Advance(RouteWorld world)
    {
        RetryOne(world);
        var reservations = new CrowdReservations(64);
        foreach (var body in world.Bodies.Values)
            reservations.Put(CrowdBody.Stationary(new(body.Id), 31, body.Position));
        var proposals = new List<Proposal>();
        foreach (var source in world.Bodies.Values.Where(body => body.Task != 0
            && body.Phase is WalkPhase.Following or WalkPhase.Waiting or WalkPhase.Yielding).ToArray())
        {
            var route = world.Routes[source.Task];
            var cached = source.Navigation is not null && source.Connection is { } connection && connection.Version == source.Navigation.Version
                && connection.Next < connection.Points.Count ? connection : null;
            var (body, target, quarter) = source.Phase == WalkPhase.Yielding ? (source, source.Endpoint, source.TransitQuarters)
                : cached is null ? RoutePlanning.Follow(world, source with { Connection = null }, route)
                : (source, cached.Target, cached.TransitQuarters);
            var direct = cached?.Points ?? StraightMovePath.TryFind(world.Navigation, body.Position, target, 1);
            if (direct is null)
            {
                // A connection is bounded and tied only to this reference point.
                direct = Connection(world, body.Position, target);
            }
            var step = direct is null
                ? new MovementStep(body.Position, body.Facing, 0, [body.Position, body.Position], Times: [0, 1])
                : MovementStepper.Compute(new(new(body.Id), 0, body.Position, body.Velocity, body.Facing),
                    new(new(target, RouteSettings.Speed, 1, RouteSettings.Motion), direct, cached?.Next ?? 0), 30, null);
            var final = target == body.Endpoint;
            proposals.Add(new(body, step, final, quarter));
            reservations.Put(new(new(body.Id), 31, step.Trace, step.Times));
        }
        var held = CrowdMotionResolver.Resolve(reservations, proposals.Select(item => new EntityId(item.Body.Id)).ToArray());
        foreach (var proposal in proposals)
        {
            var body = proposal.Body;
            var step = proposal.Step;
            var quarter = proposal.Quarter;
            var original = new CrowdBody(new(body.Id), 31, step.Trace, step.Times);
            var blocked = held.Contains(new(body.Id));
            // Revalidate an ephemeral hold against current reservations, never use it to choose a detour.
            if (blocked && reservations.FirstMotionConflict(original) is null)
            {
                reservations.Put(original);
                blocked = false;
            }
            var moved = !blocked && step.Position != body.Position;
            var blocker = blocked ? reservations.FirstMotionConflict(original) : null;
            if (blocked && !proposal.Final && RouteContraction.TryStep(world, body, quarter, blocker, reservations,
                out var inwardBody, out var inwardStep))
            {
                body = inwardBody;
                step = inwardStep;
                quarter = body.TransitQuarters;
                blocked = false;
                blocker = null;
                moved = true;
            }
            var updated = body with { Position = blocked ? body.Position : step.Position, Facing = step.Facing,
                TransitQuarters = moved ? quarter : body.TransitQuarters,
                Velocity = moved ? (step.Position - body.Position) * 30 : default,
                Phase = body.Phase == WalkPhase.Yielding ? WalkPhase.Yielding : moved ? WalkPhase.Following : WalkPhase.Waiting, Blocker = blocker?.Value,
                WaitTicks = body.WaitTicks + (moved ? 0 : 1),
                Connection = blocked || step.NextWaypoint >= (body.Connection?.Points.Count ?? 0) ? null
                    : body.Connection! with { Next = step.NextWaypoint } };
            if (body.Phase != WalkPhase.Yielding && body.Navigation is not null) updated = RouteProgress.Measure(body, updated, RouteNavigation.Reference(body));
            if (!blocked && proposal.Final && step.Position == body.Endpoint)
                updated = updated with { Phase = WalkPhase.Settled, Velocity = default, Blocker = null };
            world.Bodies[body.Id] = updated;
        }
    }

    private static IReadOnlyList<FixedVector2>? Connection(RouteWorld world, FixedVector2 from, FixedVector2 to)
    {
        if ((from - to).Length > 256 || !world.Navigation.TryWorldToCell(from, out var start)
            || !world.Navigation.TryWorldToCell(to, out var goal)) return null;
        var left = Math.Max(0, start.X - 8);
        var top = Math.Max(0, start.Y - 8);
        var area = new GridArea(left, top, Math.Min(world.Width - left, start.X + 9 - left),
            Math.Min(world.Height - top, start.Y + 9 - top));
        var path = GridPathfinder.FindPathCore(world.Navigation.Grid, start, goal,
            RouteSettings.ShortConnectionNodes, 0, cell => world.Navigation.CanOccupy(cell, 1),
            (a, b) => world.Navigation.CanTraverse(a, b, 1), area);
        if (path.Status != PathStatus.Found) return null;
        var points = new List<FixedVector2> { from };
        points.AddRange(path.Cells.Skip(1).Select(world.Navigation.CellCenter));
        if (points[^1] != to) points.Add(to);
        return points.ToArray();
    }

    private static void RetryOne(RouteWorld world)
    {
        var waiting = world.Bodies.Values.Where(body => body.Task != 0 && body.Phase is WalkPhase.Waiting or WalkPhase.Following
            && world.Routes[body.Task].Yield is null
            && (body.WaitTicks >= body.RetryAtWait || world.Routes[body.Task].MapRevision != world.Revision))
            .OrderBy(body => body.Id).FirstOrDefault();
        if (waiting is null) return;
        if (RouteYielding.TryStart(world, waiting)) return;
        var attempts = waiting.Recoveries + 1;
        if (attempts > RouteSettings.RecoveryLimit)
        {
            world.Bodies[waiting.Id] = waiting with
            {
                Task = 0, Phase = WalkPhase.Failed, Velocity = default, Blocker = null,
                Navigation = null, Connection = null, TransitQuarters = 4,
                Progress = 0, ProgressCursor = 0, ProgressVersion = 0, PlacementCheckedRevision = -1
            };
            world.Publish(waiting.Id, "failed", "no_member_progress");
            return;
        }
        var prepared = RouteNavigation.Prepare(world, waiting, rebuild: true);
        world.Bodies[waiting.Id] = prepared with
            { Recoveries = attempts, RetryAtWait = waiting.WaitTicks + RouteSettings.WaitBeforeRetry };
        world.Publish(waiting.Id, "retry", prepared.Navigation is null ? "member_route_unavailable" : "member_reference_replaced");
    }
}
