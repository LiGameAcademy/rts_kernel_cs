using Rts.Kernel.Navigation;

namespace Rts.Kernel;

public sealed partial class RtsMatch
{
    private readonly SortedDictionary<EntityId, MoveOrder> _moveOrders = [];
    private bool _pathsDirty;

    public bool IsMoving(EntityId id) => _moveOrders.ContainsKey(id);
    public IReadOnlyList<MoveOrderSnapshot> ReadMoveOrders() => Array.AsReadOnly(_moveOrders.Select(pair =>
        new MoveOrderSnapshot(pair.Key.Value, pair.Value.Request,
            Array.AsReadOnly(pair.Value.Waypoints.ToArray()), pair.Value.NextWaypoint)).ToArray());

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
            if (!_moveOrders.TryGetValue(pair.Key, out var order))
            {
                _entities[pair.Key] = entity with { Position = entity.Position + entity.Velocity * seconds };
                continue;
            }
            if (_pathsDirty)
            {
                if (!TryPlanMove(entity.Position, order.Request, out var replanned, _unitOrders[entity.Id].Current?.Group is not null))
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
            if (!_moveOrders.ContainsKey(pair.Key)) continue;
            if (next == order.Waypoints.Count)
                FinishMove(pair.Key, position, MatchEventKind.MoveCompleted, string.Empty);
            else
            {
                _moveOrders[pair.Key] = order with { NextWaypoint = next };
                _entities[pair.Key] = entity with { Position = position, Velocity = new SimVector2(
                    (position.X - entity.Position.X) / seconds, (position.Y - entity.Position.Y) / seconds) };
            }
        }
        _pathsDirty = false;
    }

    private void FinishMove(EntityId id, SimVector2 position, MatchEventKind kind, string detail)
    {
        _moveOrders.Remove(id);
        CompleteCurrentOrder(id);
        _entities[id] = _entities[id] with { Position = position, Velocity = SimVector2.Zero };
        AddEvent(kind, id, detail);
    }

    private void RestoreMoveOrders(MatchSnapshot snapshot)
    {
        foreach (var item in snapshot.Moves!)
        {
            if (item.Request.Motion is { ScaleSlopeSpeed: true } && _navigation?.Terrain is null)
                throw new InvalidDataException("Slope motion requires a height field.");
            var entity = _entities[new EntityId(item.EntityId)];
            var cells = new List<GridCell>();
            foreach (var waypoint in item.Waypoints)
            {
                if (!_navigation!.TryWorldToCell(waypoint, out var cell)
                    || !_navigation.CanOccupy(cell, item.Request.ClearanceCells))
                    throw new InvalidDataException("Move waypoint is outside valid navigation space.");
                if (cells.Count > 0 && !_navigation.CanTraverse(cells[^1], cell, item.Request.ClearanceCells))
                    throw new InvalidDataException("Move waypoints cannot skip cells or cross blocked corners.");
                cells.Add(cell);
            }
            if (!_navigation!.TryWorldToCell(entity.Position, out var current)
                || !_navigation.CanTraverse(current, cells[item.NextWaypoint], item.Request.ClearanceCells)
                || (item.NextWaypoint == 0 && current != cells[0]))
                throw new InvalidDataException("Entity position does not match its current move segment.");
            if (item.NextWaypoint > 0)
            {
                var from = item.Waypoints[item.NextWaypoint - 1];
                var to = item.Waypoints[item.NextWaypoint];
                var dx = to.X - from.X;
                var dy = to.Y - from.Y;
                var px = entity.Position.X - from.X;
                var py = entity.Position.Y - from.Y;
                var lengthSquared = dx * dx + dy * dy;
                var dot = px * dx + py * dy;
                if (Math.Abs(px * dy - py * dx) > 1e-7 * Math.Max(1, lengthSquared)
                    || dot < -1e-7 || dot > lengthSquared + 1e-7)
                    throw new InvalidDataException("Entity position lies outside its saved move segment.");
            }
            _moveOrders.Add(entity.Id, new MoveOrder(item.Request,
                Array.AsReadOnly(item.Waypoints.ToArray()), item.NextWaypoint));
        }
    }
}
