namespace Rts.Kernel.Navigation;

internal enum PathPurpose { Replace, Queued, Refresh, Group }
internal sealed record ScheduledMove(ulong Id, EntityId EntityId, long ControlVersion, long RequestedFrame,
    PathPurpose Purpose, OrderMode Mode, UnitOrderIntent Intent, FixedVector2 Origin);
internal sealed record PendingMoveSnapshot(ScheduledMove Request, long MapEpoch, WorldPathSnapshot Work);
internal sealed record MovePlanningSnapshot(ulong NextId, long MapEpoch, IReadOnlyList<PendingMoveSnapshot> Pending);
internal sealed record PlannedMove(ScheduledMove Request, MoveOrder? Order, PathStatus Status);
internal sealed record PathPlanningAdvance(int Spent, IReadOnlyList<PlannedMove> Completed);

/// <summary>Per-match round-robin path work. Owns identities, cancellation and lazy map invalidation.</summary>
internal sealed class MovePlanningQueue
{
    internal const int Quantum = 64;
    internal const int Capacity = 1024;
    private sealed record Pending(ScheduledMove Request, long MapEpoch, WorldPathJob Work);
    private readonly Queue<Pending> _pending = new();
    internal ulong NextId { get; private set; } = 1;
    internal long MapEpoch { get; private set; }
    internal int Count => _pending.Count;

    internal ulong Enqueue(EntityId entityId, long controlVersion, long frame, PathPurpose purpose,
        OrderMode mode, UnitOrderIntent intent, FixedVector2 origin)
    {
        if (entityId.IsNone || controlVersion < 0 || frame < 0 || !Enum.IsDefined(purpose)
            || !Enum.IsDefined(mode) || intent is not { IsValid: true, Kind: UnitOrderKind.Move }
            || !SimulationDomain.Contains(origin)) throw new ArgumentException("Invalid scheduled move request.");
        if (Count >= Capacity || NextId == ulong.MaxValue)
            throw new InvalidOperationException("Movement planning queue is full or its identity is exhausted.");
        var request = new ScheduledMove(NextId++, entityId, controlVersion, frame, purpose, mode, intent, origin);
        _pending.Enqueue(new(request, MapEpoch, new(origin, intent.Move!)));
        return request.Id;
    }

    internal bool Contains(EntityId entityId) => _pending.Any(item => item.Request.EntityId == entityId);
    internal bool HasProtectedIntent(EntityId entityId) => _pending.Any(item => item.Request.EntityId == entityId
        && item.Request.Intent.Source != OrderSource.UnitAi);

    internal void Cancel(EntityId entityId)
    {
        var count = Count;
        for (var i = 0; i < count; i++)
        {
            var item = _pending.Dequeue();
            if (item.Request.EntityId != entityId) _pending.Enqueue(item);
        }
    }

    internal void InvalidateMap()
    {
        if (MapEpoch == long.MaxValue) throw new InvalidOperationException("Navigation revision exhausted.");
        MapEpoch++;
    }

    internal PathPlanningAdvance Advance(NavigationState navigation, int budget)
    {
        if (budget <= 0) throw new ArgumentOutOfRangeException(nameof(budget));
        var completed = new List<PlannedMove>();
        var spent = 0;
        while (spent < budget && _pending.TryDequeue(out var item))
        {
            if (item.MapEpoch != MapEpoch)
                item = item with { MapEpoch = MapEpoch, Work = new(item.Request.Origin, item.Request.Intent.Move!) };
            var cost = item.Work.Advance(navigation, Math.Min(Quantum, budget - spent));
            spent += cost;
            if (item.Work.IsComplete)
                completed.Add(new(item.Request, item.Work.Order, item.Work.Status!.Value));
            else _pending.Enqueue(item);
        }
        return new(spent, completed.AsReadOnly());
    }

    internal MovePlanningSnapshot Capture() => new(NextId, MapEpoch,
        Array.AsReadOnly(_pending.Select(item => new PendingMoveSnapshot(item.Request, item.MapEpoch, item.Work.Capture())).ToArray()));

    internal static MovePlanningQueue Restore(MovePlanningSnapshot saved, NavigationState navigation)
    {
        if (saved is null || saved.NextId == 0 || saved.MapEpoch < 0 || saved.Pending is null || saved.Pending.Count > Capacity)
            throw new InvalidDataException("Invalid move planning queue snapshot.");
        var queue = new MovePlanningQueue { NextId = saved.NextId, MapEpoch = saved.MapEpoch };
        var identities = new HashSet<ulong>();
        foreach (var item in saved.Pending)
        {
            var request = item?.Request;
            if (item is null || request is null || request.Id == 0 || request.Id >= saved.NextId
                || !identities.Add(request.Id) || request.EntityId.IsNone || request.ControlVersion < 0
                || request.RequestedFrame < 0 || !Enum.IsDefined(request.Purpose) || !Enum.IsDefined(request.Mode)
                || request.Intent is not { IsValid: true, Kind: UnitOrderKind.Move }
                || item.MapEpoch < 0 || item.MapEpoch > saved.MapEpoch || item.Work is null
                || item.Work.Origin != request.Origin || item.Work.Request != request.Intent.Move
                || item.Work.Phase == WorldPathPhase.Complete)
                throw new InvalidDataException("Invalid pending move request identity or work state.");
            queue._pending.Enqueue(new(request, item.MapEpoch, WorldPathJob.Restore(item.Work, navigation)));
        }
        return queue;
    }
}
