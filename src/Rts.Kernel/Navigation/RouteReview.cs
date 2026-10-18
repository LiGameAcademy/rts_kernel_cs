namespace Rts.Kernel.Navigation;

/// <summary>Durable progress of a soft-move detour; temporary yielding never inherits it.</summary>
public sealed record RouteReviewState(long NextFrame, SimScalar BestDistance, long ProgressFrame,
    bool Recovering = false, FixedVector2 WindowStart = default, long WindowFrame = 0,
    SimScalar Travelled = default, bool Oscillating = false);

/// <summary>Rechecks bounded local detours without changing the player's intent or moving bodies.</summary>
internal static class RouteReview
{
    internal const int IntervalFrames = 15;
    internal const int ReviewsPerFrame = 2;

    internal static MoveOrder Begin(long frame, EntityState entity, MoveOrder source, MoveOrder detour) =>
        detour with { Review = source.Review ?? new(frame + 1 + (long)(entity.Id.Value % IntervalFrames),
            (entity.Position - source.Request.Goal).Length, frame, WindowStart: entity.Position, WindowFrame: frame) };

    internal static MoveOrder Advance(long frame, int tickRate, EntityState entity, MovementDefinition definition,
        MoveOrder order, NavigationState navigation, CrowdReservations reservations)
    {
        var minimumProgress = navigation.Grid.CellSize / 4;
        var directGoal = StraightMovePath.TryFind(navigation, entity.Position, order.Request.Goal, order.Request.ClearanceCells);
        var state = Observe(frame, tickRate, entity, definition, order, navigation, directGoal is not null);
        var stalled = directGoal is not null && frame - state.ProgressFrame >= tickRate * 3 / 2;
        var result = order with { Review = state };
        var endpoint = order.Waypoints[^1];
        // A legal crowd detour may temporarily move away from the preference.
        // Do not restart it while its complete remaining corridor is still free.
        var unsafeStall = stalled && !reservations.Safe(entity.Id, definition.Radius, Remaining(entity.Position, order));
        if (unsafeStall)
        {
            var replacement = SoftGoalPath.Find(navigation, reservations, entity, definition, order);
            if (replacement is not null && (replacement.Waypoints[^1] != endpoint
                || Length(entity.Position, replacement) + minimumProgress < Length(entity.Position, order)))
                return replacement with { Review = state };
        }
        var direct = endpoint == order.Request.Goal ? directGoal
            : StraightMovePath.TryFind(navigation, entity.Position, endpoint, order.Request.ClearanceCells);
        if (direct is not null && (entity.Position - endpoint).Length + minimumProgress < Length(entity.Position, order)
            && reservations.Safe(entity.Id, definition.Radius, direct))
            return new(order.Request, direct, WaitFrames: order.WaitFrames,
                RetryAfterFrame: order.RetryAfterFrame, Review: state);
        if (unsafeStall)
        {
            // Drop the stale sidestep reconnect before asking peers to open a corridor.
            // Motion still goes through the regular same-frame collision resolver.
            return new(order.Request, directGoal!, WaitFrames: order.WaitFrames,
                RetryAfterFrame: 0, Review: state with { Recovering = true });
        }
        return result;
    }

    private static RouteReviewState Observe(long frame, int tickRate, EntityState entity,
        MovementDefinition definition, MoveOrder order, NavigationState navigation, bool terrainClear)
    {
        var prior = order.Review!;
        var distance = (entity.Position - order.Request.Goal).Length;
        // A necessary terrain detour may increase Euclidean distance. Only diagnose stalled
        // crowd travel where terrain itself allows the preferred approach.
        var progressed = distance + navigation.Grid.CellSize / 4 < prior.BestDistance;
        var state = prior with { NextFrame = frame + IntervalFrames };
        if (progressed || !terrainClear)
            state = state with { BestDistance = distance, ProgressFrame = frame, Recovering = false,
                WindowStart = entity.Position, WindowFrame = frame, Travelled = 0, Oscillating = false };
        if (frame - state.WindowFrame >= tickRate * 3)
        {
            var cycling = terrainClear && !progressed && state.Travelled >= definition.Radius
                && (entity.Position - state.WindowStart).Length <= navigation.Grid.CellSize;
            state = state with { WindowStart = entity.Position, WindowFrame = frame,
                Travelled = 0, Oscillating = state.Oscillating || cycling };
        }
        return state;
    }

    internal static RouteReviewState? RecordMotion(RouteReviewState? state, SimScalar distance) =>
        state is null || distance == 0 ? state
            : state with { Travelled = SimMath.Min(SimulationDomain.CoordinateLimit * 3, state.Travelled + distance) };

    private static IReadOnlyList<FixedVector2> Remaining(FixedVector2 position, MoveOrder order)
    {
        var points = new List<FixedVector2> { position };
        points.AddRange(order.Waypoints.Skip(order.NextWaypoint));
        return points;
    }

    private static SimScalar Length(FixedVector2 position, MoveOrder order)
    {
        var result = SimScalar.Zero;
        for (var index = order.NextWaypoint; index < order.Waypoints.Count; index++)
        {
            result += (order.Waypoints[index] - position).Length;
            position = order.Waypoints[index];
        }
        return result;
    }
}
