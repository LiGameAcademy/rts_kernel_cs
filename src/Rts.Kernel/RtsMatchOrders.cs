using Rts.Kernel.Navigation;

namespace Rts.Kernel;

public sealed partial class RtsMatch
{
    public const int MaximumPendingOrders = UnitOrderBook.MaximumPending;

    public IReadOnlyList<UnitOrderQueueSnapshot> ReadUnitOrders() => _orders.ReadQueues();
    public UnitOrderIntent? ReadCurrentOrder(EntityId id) => _orders.ReadCurrent(id);
    public int GetPendingOrderCount(EntityId id) => _orders.PendingCount(id);
    private bool BlocksUnitAi(EntityId id) => _orders.HasProtectedIntent(id) || _planning.HasProtectedMember(id);

    private void ActivateQueuedMoves()
    {
        foreach (var id in _orders.WaitingIds())
        {
            var intent = _orders.PeekWaiting(id);
            var entity = _entities.Get(id);
            if (MovePathPlanner.TryPlan(_navigation, entity.Position, intent.Move!, out var move, intent.Group is not null))
            {
                _orders.ActivateWaiting(id, move!);
                _entities.Update(entity with { Velocity = SimVector2.Zero });
            }
            else
            {
                _orders.ActivateWaiting(id, null);
                AddEvent(MatchEventKind.MoveFailed, id, "queued_move_path_unavailable");
            }
            // At most one activation per entity per frame, including failed queued goals.
        }
    }
}
