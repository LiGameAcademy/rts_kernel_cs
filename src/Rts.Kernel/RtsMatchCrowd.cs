using Rts.Kernel.Navigation;

namespace Rts.Kernel;

public sealed partial class RtsMatch
{
    public const int CrowdBlockedSeconds = 3;
    public const int LocalReplansPerFrame = 16;
    internal const int LocalRetryFrames = 6;

    private bool CanSpawnGround(CommandEnvelope command)
    {
        if (command.MovementDefinitionId == 0) return true;
        var definition = _movementDefinitions.TryGet(command.MovementDefinitionId, out var found) ? found : null;
        if (definition is null || !_navigation!.TryWorldToCell(command.Position, out var cell)
            || !_navigation.CanOccupy(cell, Clearance(definition)) || !_navigation.CanStandAt(command.Position, definition.Radius))
            return false;
        return !_entities.Live.Where(entity => entity.MovementDefinitionId != 0).Any(entity =>
            CrowdGeometry.Overlap(command.Position, command.Position, definition.Radius,
                entity.Position, entity.Position, ReadMovementDefinition(entity.Id)!.Radius));
    }

    private void ValidateGroundBodies()
    {
        var bodies = _entities.Live.Where(entity => entity.MovementDefinitionId != 0).ToArray();
        for (var i = 0; i < bodies.Length; i++)
        for (var j = i + 1; j < bodies.Length; j++)
            if (CrowdGeometry.Overlap(bodies[i].Position, bodies[i].Position, ReadMovementDefinition(bodies[i].Id)!.Radius,
                bodies[j].Position, bodies[j].Position, ReadMovementDefinition(bodies[j].Id)!.Radius))
                throw new InvalidDataException("Ground unit bodies overlap in snapshot.");
    }

    private void AdvanceCrowd(double seconds)
    {
        var bodies = _entities.Live.Where(entity => entity.MovementDefinitionId != 0).ToArray();
        if (bodies.Length == 0) return;
        var maximumRadius = bodies.Max(entity => ReadMovementDefinition(entity.Id)!.Radius);
        var reservations = new CrowdReservations(Math.Max(_navigation!.Grid.CellSize, maximumRadius * 2));
        foreach (var body in bodies)
            reservations.Put(new(body.Id, ReadMovementDefinition(body.Id)!.Radius, new[] { body.Position, body.Position }));
        RefreshArrivalNeighbors();
        var retries = LocalReplansPerFrame;
        // Older physical waits go first; entity ID breaks ties. Every body is reserved before proposals.
        var moving = bodies.Where(entity => _orders.IsMoving(entity.Id))
            .OrderByDescending(entity => _orders.ReadPath(entity.Id).WaitFrames).ThenBy(entity => entity.Id).ToArray();
        foreach (var initial in moving)
        {
            var entity = _entities.Get(initial.Id);
            var definition = ReadMovementDefinition(entity.Id)!;
            var order = _orders.ReadPath(entity.Id);
            if (_pathsDirty)
            {
                if (!TryPlanMove(entity.Position, order.Request, out var replanned, true))
                {
                    FinishMove(entity.Id, entity.Position, MatchEventKind.MoveFailed, "path_blocked");
                    continue;
                }
                order = replanned! with { WaitFrames = order.WaitFrames, RetryAfterFrame = order.RetryAfterFrame };
            }
            var step = MovementStepper.Compute(entity, order, seconds, _navigation.Terrain);
            if (step.Failure is not null)
            {
                FinishMove(entity.Id, entity.Position, MatchEventKind.MoveFailed, step.Failure);
                continue;
            }
            if (YieldAtGroupGoal(entity, order, step))
            {
                _entities.Update(entity with { Velocity = SimVector2.Zero });
                _orders.UpdatePath(entity.Id, order with { WaitFrames = 0 });
                continue; // Assembly sequencing is intentional waiting, not a physical-blockage failure.
            }
            if (!reservations.Safe(entity.Id, definition.Radius, step.Trace))
            {
                if (Frame >= order.RetryAfterFrame && retries > 0)
                {
                    retries--;
                    order = order with { RetryAfterFrame = Frame + LocalRetryFrames };
                    var detour = LocalDetour.Find(_navigation, reservations, entity, definition, order);
                    if (detour is not null)
                    {
                        var alternative = MovementStepper.Compute(entity, detour, seconds, _navigation.Terrain);
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
                    if (wait >= CrowdBlockedSeconds * Config.TickRate)
                        FinishMove(entity.Id, entity.Position, MatchEventKind.MoveFailed, "crowd_blocked_timeout");
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
                FinishMove(entity.Id, step.Position, MatchEventKind.MoveCompleted, string.Empty);
            else
                _orders.UpdatePath(entity.Id, order with { NextWaypoint = step.NextWaypoint, WaitFrames = 0 });
        }
    }
}
