using Rts.Kernel.Navigation;

namespace Rts.Kernel;

public sealed partial class RtsMatch
{
    private readonly Dictionary<ulong, ArrivalNeighbors> _arrivalNeighbors = [];

    private void RefreshArrivalNeighbors()
    {
        var active = _moveOrders.Keys.Select(id => ReadCurrentOrder(id)?.Group?.GroupId)
            .Where(id => id is not null).Select(id => id!.Value).ToHashSet();
        foreach (var id in _arrivalNeighbors.Keys.Where(id => !active.Contains(id)).ToArray())
            _arrivalNeighbors.Remove(id);
        foreach (var id in active)
        {
            if (_arrivalNeighbors.ContainsKey(id)) continue;
            var members = new List<ArrivalMember>();
            foreach (var pair in _unitOrders)
            foreach (var intent in pair.Value.Pending.Prepend(pair.Value.Current))
                if (intent?.Group?.GroupId == id)
                    members.Add(new(pair.Key, intent.Group.Slot, intent.Move!.Goal,
                        ReadMovementDefinition(pair.Key)!.Radius));
            _arrivalNeighbors.Add(id, new(_navigation!, members));
        }
    }

    private bool YieldAtGroupGoal(EntityState entity, MoveOrder order, MovementStep proposal)
    {
        var group = ReadCurrentOrder(entity.Id)?.Group;
        if (group is null || group.Slot == 0) return false;
        var radius = ReadMovementDefinition(entity.Id)!.Radius;
        var threshold = radius * 4 + _navigation!.Grid.CellSize * 2;
        var dx = proposal.Position.X - order.Request.Goal.X;
        var dy = proposal.Position.Y - order.Request.Goal.Y;
        if (Math.Sqrt(dx * dx + dy * dy) > threshold) return false;
        foreach (var other in _arrivalNeighbors[group.GroupId].Prior(entity.Id))
            if (_moveOrders.TryGetValue(other.Id, out var prior)
                && ReadCurrentOrder(other.Id)?.Group?.GroupId == group.GroupId
                && ArrivalNeighbors.BlocksApproach(order.Request.Goal, radius, _entities[other.Id].Position,
                    other.Radius, prior, _navigation.Grid.CellSize)) return true;
        return false;
    }
}
