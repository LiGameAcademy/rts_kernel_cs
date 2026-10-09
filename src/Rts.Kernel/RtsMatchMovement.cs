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
            if (order.Request.Motion is not null)
            {
                AdvanceMotion(entity, order, seconds);
                continue;
            }
            var position = entity.Position;
            var remaining = order.Request.Speed * seconds;
            var next = order.NextWaypoint;
            while (next < order.Waypoints.Count)
            {
                var target = order.Waypoints[next];
                var dx = target.X - position.X;
                var dy = target.Y - position.Y;
                var distance = Math.Sqrt(dx * dx + dy * dy);
                if (!double.IsFinite(distance))
                {
                    FinishMove(pair.Key, position, MatchEventKind.MoveFailed, "invalid_move_geometry");
                    break;
                }
                if (distance <= remaining)
                {
                    position = target;
                    remaining -= distance;
                    next++;
                    continue;
                }
                if (remaining > 0) position = new SimVector2(position.X + dx / distance * remaining,
                    position.Y + dy / distance * remaining);
                break;
            }
            if (!_orders.IsMoving(pair.Key)) continue;
            if (next == order.Waypoints.Count)
                FinishMove(pair.Key, position, MatchEventKind.MoveCompleted, string.Empty);
            else
            {
                _orders.UpdatePath(pair.Key, order with { NextWaypoint = next });
                _entities[pair.Key] = entity with { Position = position, Velocity = new SimVector2(
                    (position.X - entity.Position.X) / seconds, (position.Y - entity.Position.Y) / seconds) };
            }
        }
        AdvanceCrowd(seconds);
        _pathsDirty = false;
    }

    private void FinishMove(EntityId id, SimVector2 position, MatchEventKind kind, string detail)
    {
        _orders.Complete(id);
        _entities[id] = _entities[id] with { Position = position, Velocity = SimVector2.Zero };
        AddEvent(kind, id, detail);
    }

}
