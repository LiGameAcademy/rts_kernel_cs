using Rts.Kernel.Navigation;

namespace Rts.Kernel;

public sealed partial class RtsMatch
{
    private bool _pathsDirty;

    public bool IsMoving(EntityId id) => _orders.IsMoving(id);
    public IReadOnlyList<MoveOrderSnapshot> ReadMoveOrders() => _orders.ReadPaths();
    public IReadOnlyList<MovementStatusView> ReadMovementStatuses() => _orders.ReadMovementStatuses();

    private bool TryPlanMove(SimVector2 position, MoveRequest request, out MoveOrder? order, bool preferDirect = false)
    {
        order = null;
        if (_navigation is null || !_navigation.TryWorldToCell(position, out var start)
            || !_navigation.TryWorldToCell(request.Goal, out var goal)) return false;
        var cells = preferDirect ? DirectGridPath.TryFind(_navigation, start, goal, request.ClearanceCells) : null;
        if (cells is null)
        {
            var path = _navigation.FindPath(start, goal, request.ClearanceCells, int.MaxValue);
            if (path.Status != PathStatus.Found) return false;
            cells = path.Cells;
        }
        var points = cells.Count == 1 ? new List<SimVector2>()
            : cells.Select(_navigation.CellCenter).ToList();
        if (points.Count == 0 || points[^1] != request.Goal) points.Add(request.Goal);
        if (points.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y))) return false;
        order = new MoveOrder(request, points.AsReadOnly());
        return true;
    }

    private void AdvanceMovement(double seconds)
    {
        foreach (var pair in _entities.ToArray())
        {
            var entity = pair.Value;
            if (entity.MovementDefinitionId != 0) continue;
            if (!_orders.TryReadPath(pair.Key, out var order))
            {
                _entities[pair.Key] = entity with { Position = entity.Position + entity.Velocity * seconds };
                continue;
            }
            if (_pathsDirty)
            {
                if (!TryPlanMove(entity.Position, order.Request, out var replanned, ReadCurrentOrder(entity.Id)?.Group is not null))
                {
                    FinishMove(pair.Key, entity.Position, MatchEventKind.MoveFailed, "path_blocked");
                    continue;
                }
                order = replanned!;
            }
            var step = MovementStepper.ComputeDiagnostic(entity, order, seconds, _navigation?.Terrain);
            CommitDiagnosticMovement(entity, order, step, seconds);
        }
        AdvanceCrowd(seconds);
        _pathsDirty = false;
    }

    private void CommitDiagnosticMovement(EntityState entity, MoveOrder order, MovementStep step, double seconds)
    {
        _entities[entity.Id] = entity with
        {
            Position = step.Position,
            Facing = step.Facing,
            Velocity = new SimVector2((step.Position.X - entity.Position.X) / seconds,
                (step.Position.Y - entity.Position.Y) / seconds)
        };
        if (step.Failure is not null)
            FinishMove(entity.Id, step.Position, MatchEventKind.MoveFailed, step.Failure);
        else if (step.NextWaypoint == order.Waypoints.Count)
            FinishMove(entity.Id, step.Position, MatchEventKind.MoveCompleted, string.Empty);
        else
            _orders.UpdatePath(entity.Id, order with { NextWaypoint = step.NextWaypoint });
    }

    private void FinishMove(EntityId id, SimVector2 position, MatchEventKind kind, string detail)
    {
        _orders.Complete(id);
        _entities[id] = _entities[id] with { Position = position, Velocity = SimVector2.Zero };
        AddEvent(kind, id, detail);
    }

}
