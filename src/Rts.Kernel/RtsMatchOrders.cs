using Rts.Kernel.Navigation;

namespace Rts.Kernel;

public sealed partial class RtsMatch
{
    public const int MaximumPendingOrders = 64;
    private readonly SortedDictionary<EntityId, UnitOrderQueue> _unitOrders = [];

    public IReadOnlyList<UnitOrderQueueSnapshot> ReadUnitOrders() => Array.AsReadOnly(_unitOrders.Select(pair =>
        new UnitOrderQueueSnapshot(pair.Key.Value, pair.Value.Current,
            Array.AsReadOnly(pair.Value.Pending.ToArray()))).ToArray());

    public UnitOrderIntent? ReadCurrentOrder(EntityId id) =>
        _unitOrders.TryGetValue(id, out var queue) ? queue.Current : null;

    public int GetPendingOrderCount(EntityId id) =>
        _unitOrders.TryGetValue(id, out var queue) ? queue.Pending.Count : 0;

    private bool BlocksUnitAi(EntityId id) => _unitOrders.TryGetValue(id, out var queue)
        && ((queue.Current is not null && queue.Current.Source != OrderSource.UnitAi)
            || queue.Pending.Any(order => order.Source != OrderSource.UnitAi));

    private void ExecuteMove(CommandEnvelope command)
    {
        if (!_entities.TryGetValue(command.EntityId, out var entity) || entity.OwnerId != command.PlayerId)
        {
            AddEvent(MatchEventKind.CommandRejected, command.EntityId, "entity_missing_or_not_owned");
            return;
        }
        if (command.Source == OrderSource.UnitAi && BlocksUnitAi(command.EntityId))
        {
            AddEvent(MatchEventKind.CommandRejected, command.EntityId, "player_order_active");
            return;
        }
        var request = command.Move!;
        if (request.Motion is { ScaleSlopeSpeed: true } && _navigation?.Terrain is null)
        {
            AddEvent(MatchEventKind.CommandRejected, command.EntityId, "motion_requires_height_field");
            return;
        }
        var intent = new UnitOrderIntent(UnitOrderKind.Move, command.Source, request);
        if (command.Mode == OrderMode.Append && _unitOrders.TryGetValue(entity.Id, out var queue)
            && (queue.Current?.Kind == UnitOrderKind.Move || queue.Pending.Count > 0))
        {
            if (_navigation is null || !_navigation.TryWorldToCell(request.Goal, out var goal)
                || !_navigation.CanOccupy(goal, request.ClearanceCells))
                AddEvent(MatchEventKind.CommandRejected, entity.Id, "move_goal_unavailable");
            else if (queue.Pending.Count >= MaximumPendingOrders)
                AddEvent(MatchEventKind.CommandRejected, entity.Id, "order_queue_full");
            else
                queue.Pending.Enqueue(intent);
            return; // No path is frozen for a future order; current movement stays intact.
        }
        if (!TryPlanMove(entity.Position, request, out var order))
        {
            AddEvent(MatchEventKind.CommandRejected, command.EntityId, "move_path_unavailable");
            return; // Invalid replacement preserves both current and pending orders.
        }
        _moveOrders[entity.Id] = order!;
        _unitOrders[entity.Id] = new UnitOrderQueue(intent);
        _entities[entity.Id] = entity with { Velocity = SimVector2.Zero };
    }

    private void ExecuteStop(CommandEnvelope command)
    {
        if (!_entities.TryGetValue(command.EntityId, out var entity) || entity.OwnerId != command.PlayerId)
            AddEvent(MatchEventKind.CommandRejected, command.EntityId, "entity_missing_or_not_owned");
        else if (command.Source == OrderSource.UnitAi && BlocksUnitAi(entity.Id))
            AddEvent(MatchEventKind.CommandRejected, entity.Id, "player_order_active");
        else
        {
            _moveOrders.Remove(entity.Id);
            _unitOrders[entity.Id] = new UnitOrderQueue(new UnitOrderIntent(UnitOrderKind.Stop, command.Source));
            _entities[entity.Id] = entity with { Velocity = SimVector2.Zero };
        }
    }

    private void ActivateQueuedMoves()
    {
        foreach (var pair in _unitOrders.ToArray())
        {
            var queue = pair.Value;
            if (queue.Current is not null) continue;
            var intent = queue.Pending.Dequeue();
            var entity = _entities[pair.Key];
            if (TryPlanMove(entity.Position, intent.Move!, out var move))
            {
                queue.Current = intent;
                _moveOrders[entity.Id] = move!;
                _entities[entity.Id] = entity with { Velocity = SimVector2.Zero };
            }
            else
            {
                AddEvent(MatchEventKind.MoveFailed, entity.Id, "queued_move_path_unavailable");
                if (queue.Pending.Count == 0) _unitOrders.Remove(entity.Id);
            }
            // At most one activation per entity per frame, including failed queued goals.
        }
    }

    private void CompleteCurrentOrder(EntityId id)
    {
        var queue = _unitOrders[id];
        queue.Current = null;
        if (queue.Pending.Count == 0) _unitOrders.Remove(id);
    }

    private void RestoreUnitOrders(MatchSnapshot snapshot)
    {
        foreach (var saved in snapshot.Orders!)
        {
            foreach (var intent in saved.Pending)
                if (!_navigation!.TryWorldToCell(intent.Move!.Goal, out _))
                    throw new InvalidDataException("Queued goal lies outside the navigation grid.");
            var queue = new UnitOrderQueue(saved.Current);
            foreach (var intent in saved.Pending) queue.Pending.Enqueue(intent);
            _unitOrders.Add(new EntityId(saved.EntityId), queue);
        }
    }
}
