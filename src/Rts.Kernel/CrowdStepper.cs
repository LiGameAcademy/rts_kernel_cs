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
    private readonly Dictionary<ulong, ArrivalNeighbors> _arrivalNeighbors = [];

    internal CrowdStepper(MatchEntities entities, MatchEventBuffer events, UnitOrderBook orders,
        MovementDefinitions movementDefinitions)
    {
        _entities = entities;
        _events = events;
        _orders = orders;
        _movementDefinitions = movementDefinitions;
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
        var bodies = _entities.Live.Where(entity => entity.MovementDefinitionId != 0).ToArray();
        if (bodies.Length == 0) return;
        var maximumRadius = bodies.Max(entity => ReadDefinition(entity.Id)!.Radius);
        var reservations = new CrowdReservations(SimMath.Max(navigation.Grid.CellSize, maximumRadius * 2));
        foreach (var body in bodies)
            reservations.Put(CrowdBody.Stationary(body.Id, ReadDefinition(body.Id)!.Radius, body.Position));
        RefreshArrivalNeighbors(navigation);
        var moving = bodies.Where(entity => _orders.IsMoving(entity.Id))
            .OrderByDescending(entity => _orders.ReadPath(entity.Id).WaitFrames).ThenBy(entity => entity.Id).ToArray();
        // Every proposal observes frame-start positions; none may assume an earlier unit already moved.
        var proposals = Prepare(frame, tickRate, navigation, pathsDirty, moving, reservations);
        var held = CrowdMotionResolver.Resolve(reservations, proposals.Select(item => item.Entity.Id).ToArray());
        var retries = LocalReplansPerFrame;
        foreach (var proposal in proposals)
        {
            var entity = proposal.Entity;
            var order = proposal.Order;
            var step = proposal.Step;
            var blocked = held.Contains(entity.Id);
            if (blocked && frame >= order.RetryAfterFrame && retries > 0)
            {
                retries--;
                order = order with { RetryAfterFrame = frame + LocalRetryFrames };
                var detour = LocalDetour.Find(navigation, reservations, entity, proposal.Definition, order);
                if (detour is not null)
                {
                    var alternative = MovementStepper.Compute(entity, detour, tickRate, navigation.Terrain);
                    var body = new CrowdBody(entity.Id, proposal.Definition.Radius, alternative.Trace, alternative.Times);
                    if (alternative.Failure is null && reservations.FirstMotionConflict(body) is null)
                    {
                        order = detour;
                        step = alternative;
                        reservations.Put(body);
                        blocked = false;
                    }
                }
            }
            if (blocked)
            {
                var wait = order.WaitFrames + 1;
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
                FinishMove(frame, entity.Id, step.Position, MatchEventKind.MoveCompleted, string.Empty);
            else
                _orders.UpdatePath(entity.Id, order with { NextWaypoint = step.NextWaypoint, WaitFrames = 0 });
        }
    }

    private sealed record Proposal(EntityState Entity, MovementDefinition Definition, MoveOrder Order, MovementStep Step);

    private List<Proposal> Prepare(long frame, int tickRate, NavigationState navigation, bool pathsDirty,
        IReadOnlyList<EntityState> moving, CrowdReservations reservations)
    {
        var proposals = new List<Proposal>(moving.Count);
        foreach (var entity in moving)
        {
            var definition = ReadDefinition(entity.Id)!;
            var order = _orders.ReadPath(entity.Id);
            if (pathsDirty)
            {
                if (!MovePathPlanner.TryPlan(navigation, entity.Position, order.Request, out var replanned, true))
                {
                    FinishMove(frame, entity.Id, entity.Position, MatchEventKind.MoveFailed, "path_blocked");
                    continue;
                }
                order = replanned! with { WaitFrames = order.WaitFrames, RetryAfterFrame = order.RetryAfterFrame };
            }
            var step = MovementStepper.Compute(entity, order, tickRate, navigation.Terrain);
            if (step.Failure is not null)
            {
                FinishMove(frame, entity.Id, entity.Position, MatchEventKind.MoveFailed, step.Failure);
                continue;
            }
            if (YieldAtGroupGoal(navigation, entity, order, step))
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
        _orders.Complete(id);
        _entities.Update(_entities.Get(id) with { Position = position, Velocity = FixedVector2.Zero });
        _events.Publish(frame, kind, id, detail);
    }

    private MovementDefinition? ReadDefinition(EntityId id) =>
        _entities.TryGet(id, out var entity) && _movementDefinitions.TryGet(entity.MovementDefinitionId, out var definition)
            ? definition : null;
}
