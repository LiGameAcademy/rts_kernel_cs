using Rts.Kernel.Navigation;

namespace Rts.Kernel;

/// <summary>Recent party membership is cooperation permission, never ownership of a completed order.</summary>
public sealed record CooperationMemberSnapshot(ulong EntityId, ulong GroupId, ulong AssistanceGroupId,
    FixedVector2 Anchor, bool Settled);

public sealed record YieldMoveSnapshot(ulong EntityId, ulong RequesterId, MoveOrderSnapshot Path, bool ResumeMove);
public sealed record CooperationSnapshot(IReadOnlyList<CooperationMemberSnapshot> Members,
    IReadOnlyList<YieldMoveSnapshot> Yields);

/// <summary>Owns bounded party cooperation and temporary paths independently of player queues.</summary>
internal sealed class GroupCooperation
{
    private readonly SortedDictionary<EntityId, CooperationMemberSnapshot> _members = [];
    private readonly SortedDictionary<EntityId, YieldMoveSnapshot> _yields = [];

    internal IEnumerable<EntityId> YieldingIds => _yields.Keys;
    internal bool IsYielding(EntityId id) => _yields.ContainsKey(id);
    internal bool IsSettled(EntityId id) => _members.TryGetValue(id, out var member) && member.Settled;
    internal CooperationMemberSnapshot? Read(EntityId id) => _members.GetValueOrDefault(id);
    internal YieldMoveSnapshot ReadYield(EntityId id) => _yields[id];
    internal MoveOrder ReadPath(EntityId id)
    {
        var path = _yields[id].Path;
        return new(path.Request, path.Waypoints, path.NextWaypoint, path.WaitFrames, path.RetryAfterFrame);
    }

    internal void AcceptMove(EntityId id, UnitOrderIntent intent, FixedVector2 position)
    {
        var previous = Read(id);
        _yields.Remove(id);
        var group = intent.Group is { SoftLanding: true } slot ? slot.GroupId : 0;
        // One new order may ask the previous party to open an exit. A further replacement
        // cannot keep renewing a detached exit permission indefinitely.
        var assistance = intent.Group is null || intent.Group.MemberCount == 1 ? previous?.GroupId ?? 0 : 0;
        if (group == 0 && assistance == 0) _members.Remove(id);
        else _members[id] = new(id.Value, group, assistance, position, false);
    }

    internal void Forget(EntityId id)
    {
        _members.Remove(id);
        _yields.Remove(id);
    }

    internal void Settle(EntityId id, FixedVector2 position)
    {
        var member = _members[id];
        _members[id] = member with { Settled = true, AssistanceGroupId = 0, Anchor = position };
    }

    internal void Complete(EntityId id, bool success, FixedVector2 position)
    {
        if (!_members.TryGetValue(id, out var member)) return;
        if (!success || member.GroupId == 0) _members.Remove(id);
        else _members[id] = member with { Settled = false, AssistanceGroupId = 0, Anchor = position };
    }

    internal bool CanHelp(EntityId helper, EntityId requester, UnitOrderBook orders)
    {
        if (helper == requester || HasRequests(helper) || IsYielding(helper) || !_members.TryGetValue(helper, out var member)
            || !_members.TryGetValue(requester, out var asking) || member.GroupId == 0) return false;
        if (member.GroupId != asking.GroupId && member.GroupId != asking.AssistanceGroupId) return false;
        // A freely travelling peer is already vacating space; do not interrupt it.
        if (orders.TryReadPath(helper, out var path) && path.WaitFrames == 0) return false;
        var current = orders.ReadCurrent(helper);
        return current is null || (current.Group?.GroupId == member.GroupId && current.Kind == UnitOrderKind.Move);
    }

    internal bool HasRequests(EntityId requester) => _yields.Values.Any(task => task.RequesterId == requester.Value);

    internal void Start(EntityId id, EntityId requester, MoveOrder path, bool resumeMove)
    {
        _yields.Add(id, new(id.Value, requester.Value, CapturePath(id, path), resumeMove));
    }

    internal void Update(EntityId id, MoveOrder path) =>
        _yields[id] = _yields[id] with { Path = CapturePath(id, path) };

    internal YieldMoveSnapshot End(EntityId id)
    {
        var task = _yields[id];
        _yields.Remove(id);
        return task;
    }

    internal CooperationSnapshot Capture() => new(Array.AsReadOnly(_members.Values.ToArray()),
        Array.AsReadOnly(_yields.Values.Select(task => task with
        {
            Path = task.Path with { Waypoints = Array.AsReadOnly(task.Path.Waypoints.ToArray()) }
        }).ToArray()));

    internal void Restore(CooperationSnapshot snapshot)
    {
        foreach (var member in snapshot.Members) _members.Add(new(member.EntityId), member);
        foreach (var task in snapshot.Yields)
            _yields.Add(new(task.EntityId), task with
            {
                Path = task.Path with { Waypoints = Array.AsReadOnly(task.Path.Waypoints.ToArray()) }
            });
    }

    private static MoveOrderSnapshot CapturePath(EntityId id, MoveOrder path) =>
        new(id.Value, path.Request, path.Waypoints, path.NextWaypoint, path.WaitFrames, path.RetryAfterFrame);
}
