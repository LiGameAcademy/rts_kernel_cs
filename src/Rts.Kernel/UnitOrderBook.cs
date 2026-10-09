using System.Diagnostics.CodeAnalysis;
using Rts.Kernel.Navigation;

namespace Rts.Kernel;

/// <summary>Owns order intents and active paths; lifecycle operations update both together.</summary>
internal sealed class UnitOrderBook
{
    internal const int MaximumPending = 64;
    private readonly SortedDictionary<EntityId, UnitOrderQueue> _queues = [];
    private readonly SortedDictionary<EntityId, MoveOrder> _moves = [];

    private sealed class UnitOrderQueue(UnitOrderIntent? current)
    {
        internal UnitOrderIntent? Current { get; set; } = current;
        internal Queue<UnitOrderIntent> Pending { get; } = new();
    }

    internal IReadOnlyCollection<EntityId> MovingIds => _moves.Keys;
    internal bool IsMoving(EntityId id) => _moves.ContainsKey(id);
    internal MoveOrder ReadPath(EntityId id) => _moves[id];
    internal bool TryReadPath(EntityId id, [NotNullWhen(true)] out MoveOrder? path) => _moves.TryGetValue(id, out path);
    internal UnitOrderIntent? ReadCurrent(EntityId id) => _queues.TryGetValue(id, out var queue) ? queue.Current : null;
    internal int PendingCount(EntityId id) => _queues.TryGetValue(id, out var queue) ? queue.Pending.Count : 0;
    internal bool CanAppend(EntityId id) => _queues.TryGetValue(id, out var queue)
        && (queue.Current?.Kind == UnitOrderKind.Move || queue.Pending.Count > 0);
    internal bool HasProtectedIntent(EntityId id) => _queues.TryGetValue(id, out var queue)
        && ((queue.Current is not null && queue.Current.Source != OrderSource.UnitAi)
            || queue.Pending.Any(order => order.Source != OrderSource.UnitAi));

    internal SimVector2 PlacementStart(EntityState entity, OrderMode mode) =>
        mode == OrderMode.Append && _queues.TryGetValue(entity.Id, out var queue)
            ? queue.Pending.LastOrDefault()?.Move?.Goal ?? queue.Current?.Move?.Goal ?? entity.Position
            : entity.Position;

    internal IEnumerable<SimVector2> RetainedGoals(EntityId id) =>
        _queues.TryGetValue(id, out var queue)
            ? queue.Pending.Prepend(queue.Current).Where(intent => intent?.Move is not null).Select(intent => intent!.Move!.Goal)
            : Enumerable.Empty<SimVector2>();

    internal IEnumerable<(EntityId Id, UnitOrderIntent Intent)> Intents()
    {
        foreach (var pair in _queues)
        foreach (var intent in pair.Value.Pending.Prepend(pair.Value.Current))
            if (intent is not null) yield return (pair.Key, intent);
    }

    internal IReadOnlyList<UnitOrderQueueSnapshot> ReadQueues() => Array.AsReadOnly(_queues.Select(pair =>
        new UnitOrderQueueSnapshot(pair.Key.Value, pair.Value.Current,
            Array.AsReadOnly(pair.Value.Pending.ToArray()))).ToArray());

    internal IReadOnlyList<MoveOrderSnapshot> ReadPaths() => Array.AsReadOnly(_moves.Select(pair =>
        new MoveOrderSnapshot(pair.Key.Value, pair.Value.Request, Array.AsReadOnly(pair.Value.Waypoints.ToArray()),
            pair.Value.NextWaypoint, pair.Value.WaitFrames, pair.Value.RetryAfterFrame)).ToArray());

    internal IReadOnlyList<MovementStatusView> ReadMovementStatuses() => Array.AsReadOnly(_moves.Select(pair =>
        new MovementStatusView(pair.Key.Value, pair.Value.WaitFrames, pair.Value.RetryAfterFrame)).ToArray());

    internal bool TryAppend(EntityId id, UnitOrderIntent intent)
    {
        var queue = _queues[id];
        if (queue.Pending.Count >= MaximumPending) return false;
        queue.Pending.Enqueue(intent);
        return true;
    }

    internal void ReplaceMove(EntityId id, UnitOrderIntent intent, MoveOrder path)
    {
        _queues[id] = new UnitOrderQueue(intent);
        _moves[id] = path;
    }

    internal void Stop(EntityId id, OrderSource source)
    {
        _moves.Remove(id);
        _queues[id] = new UnitOrderQueue(new UnitOrderIntent(UnitOrderKind.Stop, source));
    }

    internal void Clear(EntityId id)
    {
        _moves.Remove(id);
        _queues.Remove(id);
    }

    internal void Complete(EntityId id)
    {
        _moves.Remove(id);
        var queue = _queues[id];
        queue.Current = null;
        if (queue.Pending.Count == 0) _queues.Remove(id);
    }

    internal void UpdatePath(EntityId id, MoveOrder path) => _moves[id] = path;
    internal IReadOnlyList<EntityId> WaitingIds() => _queues.Where(pair => pair.Value.Current is null)
        .Select(pair => pair.Key).ToArray();
    internal UnitOrderIntent PeekWaiting(EntityId id) => _queues[id].Pending.Peek();

    // Consume exactly one pending intent even when its path cannot be activated.
    internal void ActivateWaiting(EntityId id, MoveOrder? path)
    {
        var queue = _queues[id];
        var intent = queue.Pending.Dequeue();
        if (path is not null)
        {
            queue.Current = intent;
            _moves[id] = path;
        }
        else if (queue.Pending.Count == 0) _queues.Remove(id);
    }

    internal void Restore(MatchSnapshot snapshot, IReadOnlyDictionary<EntityId, EntityState> entities,
        NavigationState? navigation)
    {
        RestorePaths(snapshot, entities, navigation);
        foreach (var saved in snapshot.Orders!)
        {
            foreach (var intent in saved.Pending)
                if (!navigation!.TryWorldToCell(intent.Move!.Goal, out _))
                    throw new InvalidDataException("Queued goal lies outside the navigation grid.");
            var queue = new UnitOrderQueue(saved.Current);
            foreach (var intent in saved.Pending) queue.Pending.Enqueue(intent);
            _queues.Add(new EntityId(saved.EntityId), queue);
        }
    }

    private void RestorePaths(MatchSnapshot snapshot, IReadOnlyDictionary<EntityId, EntityState> entities, NavigationState? navigation)
    {
        foreach (var item in snapshot.Moves!)
        {
            if (item.Request.Motion is { ScaleSlopeSpeed: true } && navigation?.Terrain is null)
                throw new InvalidDataException("Slope motion requires a height field.");
            var entity = entities[new EntityId(item.EntityId)];
            var cells = new List<GridCell>();
            foreach (var waypoint in item.Waypoints)
            {
                if (!navigation!.TryWorldToCell(waypoint, out var cell)
                    || !navigation.CanOccupy(cell, item.Request.ClearanceCells))
                    throw new InvalidDataException("Move waypoint is outside valid navigation space.");
                if (cells.Count > 0 && !navigation.CanTraverse(cells[^1], cell, item.Request.ClearanceCells))
                    throw new InvalidDataException("Move waypoints cannot skip cells or cross blocked corners.");
                cells.Add(cell);
            }
            if (!navigation!.TryWorldToCell(entity.Position, out var current)
                || !navigation.CanTraverse(current, cells[item.NextWaypoint], item.Request.ClearanceCells)
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
            _moves.Add(entity.Id, new MoveOrder(item.Request,
                Array.AsReadOnly(item.Waypoints.ToArray()), item.NextWaypoint, item.WaitFrames, item.RetryAfterFrame));
        }
    }
}
