using Rts.Kernel.Navigation;

namespace Rts.Kernel;

/// <summary>
/// Advances ground-unit crowd motion for one match. Owns arrival-neighbor cache only;
/// entities, orders, events and navigation remain in their modules.
/// </summary>
internal sealed class CrowdStepper
{
    public const int CrowdBlockedSeconds = 3;
    public const int LocalReplansPerFrame = 16;
    internal const int LocalRetryFrames = 6;

    private readonly MatchEntities _entities;
    private readonly MatchEventBuffer _events;
    private readonly UnitOrderBook _orders;
    private readonly MovementDefinitions _movementDefinitions;
    private readonly GroupYieldCoordinator _yield;
    private readonly Dictionary<ulong, ArrivalNeighbors> _arrivalNeighbors = [];

    internal CrowdStepper(MatchEntities entities, MatchEventBuffer events, UnitOrderBook orders,
        MovementDefinitions movementDefinitions)
    {
        _entities = entities;
        _events = events;
        _orders = orders;
        _movementDefinitions = movementDefinitions;
        _yield = new(orders, entities, events, movementDefinitions);
    }

    internal void ValidateGroundBodies()
    {
        var bodies = _entities.Live.Where(entity => entity.MovementDefinitionId != 0).ToArray();
        for (var i = 0; i < bodies.Length; i++)
            for (var j = i + 1; j < bodies.Length; j++)
                if (CrowdGeometry.Overlap(bodies[i].Position, bodies[i].Position, ReadDefinition(bodies[i].Id)!.Radius,
                    bodies[j].Position, bodies[j].Position, ReadDefinition(bodies[j].Id)!.Radius))
                    throw new InvalidDataException("Ground unit bodies overlap in snapshot.");
    }

    internal void Advance(long frame, int tickRate, NavigationState navigation, ref bool pathsDirty)
    {
        _yield.Prepare(frame, navigation);
        var bodies = _entities.Live.Where(entity => entity.MovementDefinitionId != 0).ToArray();
        if (bodies.Length == 0) return;
        var maximumRadius = bodies.Max(entity => ReadDefinition(entity.Id)!.Radius);
        var reservations = new CrowdReservations(SimMath.Max(navigation.Grid.CellSize, maximumRadius * 2));
        foreach (var body in bodies)
            reservations.Put(CrowdBody.Stationary(body.Id, ReadDefinition(body.Id)!.Radius, body.Position));
        if (!pathsDirty) ReviewRoutes(frame, tickRate, navigation, bodies, reservations);
        RefreshArrivalNeighbors(navigation);
        var moving = bodies.Where(entity => _orders.IsMoving(entity.Id) || _orders.Cooperation.IsYielding(entity.Id))
            .OrderByDescending(entity => _orders.Cooperation.IsYielding(entity.Id))
            .ThenByDescending(entity => ReadPath(entity.Id).WaitFrames).ThenBy(entity => entity.Id).ToArray();
        // Every proposal observes frame-start positions; none may assume an earlier unit already moved.
        var proposals = Prepare(frame, tickRate, navigation, pathsDirty, moving, reservations);
        var held = CrowdMotionResolver.Resolve(reservations, proposals.Select(item => item.Entity.Id).ToArray());
        var retries = LocalReplansPerFrame;
        var yieldRequests = new List<(EntityId Requester, EntityId Blocker)>();
        var remainingYieldPlans = 2;
        var helpingProgress = proposals.Where(proposal => _orders.Cooperation.IsYielding(proposal.Entity.Id)
            && !held.Contains(proposal.Entity.Id) && proposal.Step.Position != proposal.Entity.Position)
            .Select(proposal => _orders.Cooperation.ReadYield(proposal.Entity.Id).RequesterId).ToHashSet();
        foreach (var proposal in proposals)
        {
            var entity = proposal.Entity;
            var order = proposal.Order;
            var step = proposal.Step;
            var blocked = held.Contains(entity.Id);
            var yielding = _orders.Cooperation.IsYielding(entity.Id);
            EntityId? blocker = null;
            if (yielding)
            {
                CommitYield(frame, tickRate, navigation, proposal, blocked);
                continue;
            }
            if (blocked && frame >= order.RetryAfterFrame && retries > 0)
            {
                retries--;
                blocker = reservations.FirstMotionConflict(new(entity.Id, proposal.Definition.Radius, step.Trace, step.Times));
                order = order with { RetryAfterFrame = frame + LocalRetryFrames };
                var waitingForHelp = _orders.Cooperation.HasRequests(entity.Id);
                if (!waitingForHelp && (_orders.Cooperation.Read(entity.Id)?.AssistanceGroupId > 0 || order.Review?.Recovering == true))
                    waitingForHelp = TryQueueYield(entity, order, blocker, bodies, navigation, yieldRequests, ref remainingYieldPlans);
                // Once a valid opening is being prepared, sidestepping would change the
                // requested corridor and make helpers chase a moving target.
                if (!waitingForHelp)
                {
                    if (TryAlternative(frame, entity, proposal.Definition, order, tickRate, navigation, reservations,
                            out var alternativeOrder, out var alternativeStep))
                    {
                        order = alternativeOrder!;
                        step = alternativeStep!;
                        blocked = false;
                    }
                }
            }
            if (blocked)
            {
                TryQueueYield(entity, order, blocker, bodies, navigation, yieldRequests, ref remainingYieldPlans);
                var wait = helpingProgress.Contains(entity.Id.Value) ? 0 : order.WaitFrames + 1;
                _entities.Update(entity with { Velocity = FixedVector2.Zero, Facing = step.Facing });
                if (wait >= CrowdBlockedSeconds * tickRate)
                    FinishMove(frame, entity.Id, entity.Position, MatchEventKind.MoveFailed, "crowd_blocked_timeout");
                else
                    _orders.UpdatePath(entity.Id, order with { WaitFrames = wait });
                continue;
            }
            _entities.Update(entity with
            {
                Position = step.Position,
                Facing = step.Facing,
                Velocity = new((step.Position.X - entity.Position.X) * tickRate,
                    (step.Position.Y - entity.Position.Y) * tickRate)
            });
            if (step.NextWaypoint == order.Waypoints.Count)
                _yield.Arrive(frame, entity.Id);
            else
                _orders.UpdatePath(entity.Id, order with { NextWaypoint = step.NextWaypoint, WaitFrames = 0,
                    Review = RouteReview.RecordMotion(order.Review, (step.Position - entity.Position).Length) });
        }

        // Request planning happens after every normal proposal is committed. No frame-start
        // proposal can overwrite a newly installed temporary path.
        foreach (var (requester, blocker) in yieldRequests.Take(2))
            if (_orders.IsMoving(requester))
                _yield.Request(requester, blocker,
                    _entities.Live.Where(entity => entity.MovementDefinitionId != 0).ToArray(), navigation);
        _yield.Prepare(frame, navigation);
    }

    private void ReviewRoutes(long frame, int tickRate, NavigationState navigation,
        IReadOnlyList<EntityState> bodies, CrowdReservations reservations)
    {
        var due = bodies.Where(entity => _orders.TryReadPath(entity.Id, out var path)
                && path.Review is { } review && frame >= review.NextFrame
                && !_orders.Cooperation.IsYielding(entity.Id) && !_orders.Cooperation.HasRequests(entity.Id))
            .OrderBy(entity => _orders.ReadPath(entity.Id).Review!.NextFrame).ThenBy(entity => entity.Id)
            .Take(RouteReview.ReviewsPerFrame).ToArray();
        foreach (var entity in due)
            _orders.UpdatePath(entity.Id, RouteReview.Advance(frame, tickRate, entity, ReadDefinition(entity.Id)!,
                _orders.ReadPath(entity.Id), navigation, reservations));
    }

    private bool TryQueueYield(EntityState entity, MoveOrder order, EntityId? blocker,
        IReadOnlyList<EntityState> bodies, NavigationState navigation,
        List<(EntityId Requester, EntityId Blocker)> requests, ref int remainingPlans)
    {
        if (blocker is not { } id || remainingPlans == 0 || _orders.Cooperation.HasRequests(entity.Id)
            || requests.Any(request => request.Requester == entity.Id)
            || !_orders.Cooperation.CanHelp(id, entity.Id, _orders)) return false;
        remainingPlans--;
        if (LocalYieldPlanner.Plan(entity, order, id, bodies, _orders, _movementDefinitions, navigation) is null) return false;
        requests.Add((entity.Id, id));
        return true;
    }

    private bool TryAlternative(long frame, EntityState entity, MovementDefinition definition, MoveOrder order, int tickRate,
        NavigationState navigation, CrowdReservations reservations, out MoveOrder? alternativeOrder,
        out MovementStep? alternativeStep)
    {
        alternativeOrder = null;
        alternativeStep = null;
        var soft = _orders.ReadCurrent(entity.Id)?.Group?.SoftLanding == true;
        var detour = soft ? SoftGoalPath.Find(navigation, reservations, entity, definition, order) : null;
        // Near a soft slot, grid-centre rejoining can undo a legal lateral step.
        // Keep bounded soft endpoints and world-space sidesteps; distant/exact paths retain A*.
        var nearSoftGoal = soft && SquaredDistance.Between(entity.Position, order.Request.Goal).Length
            <= navigation.Grid.CellSize * LocalDetour.RadiusCells;
        if (!nearSoftGoal) detour ??= LocalDetour.Find(navigation, reservations, entity, definition, order);
        if (soft) detour ??= LocalSidestep.Find(navigation, reservations, entity, definition, order, tickRate);
        if (detour is null || (order.Review?.Oscillating == true
            && !reservations.Safe(entity.Id, definition.Radius, detour.Waypoints))) return false;
        var step = MovementStepper.Compute(entity, detour, tickRate, navigation.Terrain);
        var body = new CrowdBody(entity.Id, definition.Radius, step.Trace, step.Times);
        if (step.Failure is not null || reservations.FirstMotionConflict(body) is not null) return false;
        reservations.Put(body);
        alternativeOrder = soft ? RouteReview.Begin(frame, entity, order, detour) : detour;
        alternativeStep = step;
        return true;
    }

    private MoveOrder ReadPath(EntityId id) => _orders.Cooperation.IsYielding(id)
        ? _orders.Cooperation.ReadPath(id) : _orders.ReadPath(id);

    private void CommitYield(long frame, int tickRate, NavigationState navigation, Proposal proposal, bool blocked)
    {
        var entity = proposal.Entity;
        var order = proposal.Order;
        var step = proposal.Step;
        if (blocked)
        {
            var wait = order.WaitFrames + 1;
            _entities.Update(entity with { Velocity = FixedVector2.Zero, Facing = step.Facing });
            if (wait >= CrowdBlockedSeconds * tickRate) _yield.EndYield(frame, entity.Id, navigation);
            else _orders.Cooperation.Update(entity.Id, order with { WaitFrames = wait });
            return;
        }
        _entities.Update(entity with { Position = step.Position, Facing = step.Facing,
            Velocity = (step.Position - entity.Position) * tickRate });
        if (step.NextWaypoint == order.Waypoints.Count) _yield.EndYield(frame, entity.Id, navigation);
        else _orders.Cooperation.Update(entity.Id, order with { NextWaypoint = step.NextWaypoint, WaitFrames = 0 });
    }

    private sealed record Proposal(EntityState Entity, MovementDefinition Definition, MoveOrder Order, MovementStep Step);

    private List<Proposal> Prepare(long frame, int tickRate, NavigationState navigation, bool pathsDirty,
        IReadOnlyList<EntityState> moving, CrowdReservations reservations)
    {
        var proposals = new List<Proposal>(moving.Count);
        foreach (var entity in moving)
        {
            var definition = ReadDefinition(entity.Id)!;
            var order = ReadPath(entity.Id);
            var yielding = _orders.Cooperation.IsYielding(entity.Id);
            if (pathsDirty && yielding)
            {
                _yield.EndYield(frame, entity.Id, navigation);
                continue;
            }
            if (pathsDirty)
            {
                if (!MovePathPlanner.TryPlan(navigation, entity.Position, order.Request, out var replanned, true, _orders.ReadCurrent(entity.Id)?.Group?.SoftLanding == true))
                {
                    FinishMove(frame, entity.Id, entity.Position, MatchEventKind.MoveFailed, "path_blocked");
                    continue;
                }
                order = replanned! with { WaitFrames = order.WaitFrames, RetryAfterFrame = order.RetryAfterFrame };
            }
            var step = MovementStepper.Compute(entity, order, tickRate, navigation.Terrain);
            if (step.Failure is not null)
            {
                if (yielding) _yield.EndYield(frame, entity.Id, navigation);
                else FinishMove(frame, entity.Id, entity.Position, MatchEventKind.MoveFailed, step.Failure);
                continue;
            }
            if (!yielding && YieldAtGroupGoal(navigation, entity, order, step))
            {
                _entities.Update(entity with { Velocity = FixedVector2.Zero });
                _orders.UpdatePath(entity.Id, order with { WaitFrames = 0 });
                continue;
            }
            proposals.Add(new(entity, definition, order, step));
            reservations.Put(new(entity.Id, definition.Radius, step.Trace, step.Times));
        }
        return proposals;
    }

    private void RefreshArrivalNeighbors(NavigationState navigation)
    {
        var active = _orders.MovingIds.Select(id => _orders.ReadCurrent(id)?.Group?.GroupId)
            .Where(id => id is not null).Select(id => id!.Value).ToHashSet();
        foreach (var id in _arrivalNeighbors.Keys.Where(id => !active.Contains(id)).ToArray())
            _arrivalNeighbors.Remove(id);
        foreach (var id in active)
        {
            if (_arrivalNeighbors.ContainsKey(id)) continue;
            var members = new List<ArrivalMember>();
            foreach (var (entityId, intent) in _orders.Intents())
                if (intent.Group?.GroupId == id)
                    members.Add(new(entityId, intent.Group.Slot, intent.Move!.Goal,
                        ReadDefinition(entityId)!.Radius));
            _arrivalNeighbors.Add(id, new(navigation, members));
        }
    }

    private bool YieldAtGroupGoal(NavigationState navigation, EntityState entity, MoveOrder order, MovementStep proposal)
    {
        var group = _orders.ReadCurrent(entity.Id)?.Group;
        if (group is null || group.Slot == 0) return false;
        if (group.SoftLanding) return false;
        var radius = ReadDefinition(entity.Id)!.Radius;
        var threshold = radius * 4 + navigation.Grid.CellSize * 2;
        if (SquaredDistance.Between(proposal.Position, order.Request.Goal).Length > threshold) return false;
        foreach (var other in _arrivalNeighbors[group.GroupId].Prior(entity.Id))
            if (_orders.TryReadPath(other.Id, out var prior)
                && _orders.ReadCurrent(other.Id)?.Group?.GroupId == group.GroupId
                && ArrivalNeighbors.BlocksApproach(order.Request.Goal, radius, _entities.Get(other.Id).Position,
                    other.Radius, prior, navigation.Grid.CellSize)) return true;
        return false;
    }

    private void FinishMove(long frame, EntityId id, FixedVector2 position, MatchEventKind kind, string detail)
    {
        _entities.Update(_entities.Get(id) with { Position = position, Velocity = FixedVector2.Zero });
        _yield.Finish(frame, id, kind == MatchEventKind.MoveCompleted, detail);
    }

    private MovementDefinition? ReadDefinition(EntityId id) =>
        _entities.TryGet(id, out var entity) && _movementDefinitions.TryGet(entity.MovementDefinitionId, out var definition)
            ? definition : null;
}
