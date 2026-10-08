using Rts.Kernel.Navigation;

namespace Rts.Kernel;

public sealed partial class RtsMatch
{
    private bool YieldAtGroupGoal(EntityState entity, MoveOrder order, MovementStep proposal)
    {
        var group = ReadCurrentOrder(entity.Id)?.Group;
        if (group is null || group.Slot == 0) return false;
        var radius = ReadMovementDefinition(entity.Id)!.Radius;
        var threshold = radius * 4 + _navigation!.Grid.CellSize * 2;
        var dx = proposal.Position.X - order.Request.Goal.X;
        var dy = proposal.Position.Y - order.Request.Goal.Y;
        if (Math.Sqrt(dx * dx + dy * dy) > threshold) return false;
        foreach (var other in _moveOrders)
        {
            if (other.Key == entity.Id) continue;
            var prior = ReadCurrentOrder(other.Key)?.Group;
            if (prior is not null && prior.GroupId == group.GroupId && prior.Slot < group.Slot) return true;
        }
        return false;
    }
}
