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

    internal void Advance(long frame, int tickRate, double seconds, NavigationState navigation, ref bool pathsDirty)
    {
        var bodies = _entities.Live.Where(entity => entity.MovementDefinitionId != 0).ToArray();
        if (bodies.Length == 0) return;
        var maximumRadius = bodies.Max(entity => ReadDefinition(entity.Id)!.Radius);
        var reservations = new CrowdReservations(Math.Max(navigation.Grid.CellSize, maximumRadius * 2));
        foreach (var body in bodies)
            reservations.Put(new(body.Id, ReadDefinition(body.Id)!.Radius, new[] { body.Position, body.Position }));
        RefreshArrivalNeighbors(navigation);
        var retries = LocalReplansPerFrame;
        // Older physical waits go first; entity ID breaks ties. Every body is reserved before proposals.
        var moving = bodies.Where(entity => _orders.IsMoving(entity.Id))
            .OrderByDescending(entity => _orders.ReadPath(entity.Id).WaitFrames).ThenBy(entity => entity.Id).ToArray();
        foreach (var initial in moving)
        {
            var entity = _entities.Get(initial.Id);
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
            var step = MovementStepper.Compute(entity, order, seconds, navigation.Terrain);
            if (step.Failure is not null)
            {
                FinishMove(frame, entity.Id, entity.Position, MatchEventKind.MoveFailed, step.Failure);
                continue;
            }
            if (YieldAtGroupGoal(navigation, entity, order, step))
            {
                _entities.Update(entity with { Velocity = SimVector2.Zero });
                _orders.UpdatePath(entity.Id, order with { WaitFrames = 0 });
                continue; // Assembly sequencing is intentional waiting, not a physical-blockage failure.
            }
            if (!reservations.Safe(entity.Id, definition.Radius, step.Trace))
            {
                if (frame >= order.RetryAfterFrame && retries > 0)
                {
                    retries--;
                    order = order with { RetryAfterFrame = frame + LocalRetryFrames };
                    var detour = LocalDetour.Find(navigation, reservations, entity, definition, order);
                    if (detour is not null)
                    {
                        var alternative = MovementStepper.Compute(entity, detour, seconds, navigation.Terrain);
                        if (alternative.Failure is null && reservations.Safe(entity.Id, definition.Radius, alternative.Trace))
                        {
                            order = detour;
                            step = alternative;
                        }
                    }
                }
                if (!reservations.Safe(entity.Id, definition.Radius, step.Trace))
                {
                    var wait = order.WaitFrames + 1;
                    _entities.Update(entity with { Velocity = SimVector2.Zero, Facing = step.Facing });
                    if (wait >= CrowdBlockedSeconds * tickRate)
                        FinishMove(frame, entity.Id, entity.Position, MatchEventKind.MoveFailed, "crowd_blocked_timeout");
                    else
                        _orders.UpdatePath(entity.Id, order with { WaitFrames = wait });
                    continue;
                }
            }
            // Commit only a safe proposal, then publish its trace for the remaining units this frame.
            _entities.Update(entity with
            {
                Position = step.Position,
                Facing = step.Facing,
                Velocity = new((step.Position.X - entity.Position.X) / seconds,
                    (step.Position.Y - entity.Position.Y) / seconds)
            });
            reservations.Put(new(entity.Id, definition.Radius, step.Trace));
            if (step.NextWaypoint == order.Waypoints.Count)
                FinishMove(frame, entity.Id, step.Position, MatchEventKind.MoveCompleted, string.Empty);
            else
                _orders.UpdatePath(entity.Id, order with { NextWaypoint = step.NextWaypoint, WaitFrames = 0 });
        }
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
        var dx = proposal.Position.X - order.Request.Goal.X;
        var dy = proposal.Position.Y - order.Request.Goal.Y;
        if (Math.Sqrt(dx * dx + dy * dy) > threshold) return false;
        foreach (var other in _arrivalNeighbors[group.GroupId].Prior(entity.Id))
            if (_orders.TryReadPath(other.Id, out var prior)
                && _orders.ReadCurrent(other.Id)?.Group?.GroupId == group.GroupId
                && ArrivalNeighbors.BlocksApproach(order.Request.Goal, radius, _entities.Get(other.Id).Position,
                    other.Radius, prior, navigation.Grid.CellSize)) return true;
        return false;
    }

    private void FinishMove(long frame, EntityId id, SimVector2 position, MatchEventKind kind, string detail)
    {
        _orders.Complete(id);
        _entities.Update(_entities.Get(id) with { Position = position, Velocity = SimVector2.Zero });
        _events.Publish(frame, kind, id, detail);
    }

    private MovementDefinition? ReadDefinition(EntityId id) =>
        _entities.TryGet(id, out var entity) && _movementDefinitions.TryGet(entity.MovementDefinitionId, out var definition)
            ? definition : null;
}
