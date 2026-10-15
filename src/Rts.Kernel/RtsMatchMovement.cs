using Rts.Kernel.Navigation;

namespace Rts.Kernel;

public sealed partial class RtsMatch
{
    private bool _pathsDirty;

    public bool IsMoving(EntityId id) => _orders.IsMoving(id);
    public IReadOnlyList<MoveOrderSnapshot> ReadMoveOrders() => _orders.ReadPaths();
    public IReadOnlyList<MovementStatusView> ReadMovementStatuses() => _orders.ReadMovementStatuses();

    private void AdvanceMovement(int tickRate)
    {
        foreach (var entity in _entities.ReadAll())
        {
            if (entity.MovementDefinitionId != 0) continue;
            if (!_orders.TryReadPath(entity.Id, out var order))
            {
                var next = entity.Position + entity.Velocity / tickRate;
                if (SimulationDomain.Contains(next)) _entities.Update(entity with { Position = next });
                else
                {
                    _entities.Update(entity with { Velocity = FixedVector2.Zero });
                    AddEvent(MatchEventKind.MoveFailed, entity.Id, "world_bounds");
                }
                continue;
            }
            if (_pathsDirty)
            {
                if (!MovePathPlanner.TryPlan(_navigation, entity.Position, order.Request, out var replanned,
                    ReadCurrentOrder(entity.Id)?.Group is not null))
                {
                    FinishMove(entity.Id, entity.Position, MatchEventKind.MoveFailed, "path_blocked");
                    continue;
                }
                order = replanned!;
            }
            var step = MovementStepper.ComputeDiagnostic(entity, order, tickRate, _navigation?.Terrain);
            CommitDiagnosticMovement(entity, order, step, tickRate);
        }
        AdvanceCrowd(tickRate);
        _pathsDirty = false;
    }

    private void CommitDiagnosticMovement(EntityState entity, MoveOrder order, MovementStep step, int tickRate)
    {
        _entities.Update(entity with
        {
            Position = step.Position,
            Facing = step.Facing,
            Velocity = new FixedVector2((step.Position.X - entity.Position.X) * tickRate,
                (step.Position.Y - entity.Position.Y) * tickRate)
        });
        if (step.Failure is not null)
            FinishMove(entity.Id, step.Position, MatchEventKind.MoveFailed, step.Failure);
        else if (step.NextWaypoint == order.Waypoints.Count)
            FinishMove(entity.Id, step.Position, MatchEventKind.MoveCompleted, string.Empty);
        else
            _orders.UpdatePath(entity.Id, order with { NextWaypoint = step.NextWaypoint });
    }

    private void FinishMove(EntityId id, FixedVector2 position, MatchEventKind kind, string detail)
    {
        _orders.Complete(id);
        _entities.Update(_entities.Get(id) with { Position = position, Velocity = FixedVector2.Zero });
        AddEvent(kind, id, detail);
    }

}
