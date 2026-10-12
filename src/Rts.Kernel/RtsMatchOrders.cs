using Rts.Kernel.Navigation;

namespace Rts.Kernel;

public sealed partial class RtsMatch
{
    public const int MaximumPendingOrders = UnitOrderBook.MaximumPending;
    private readonly UnitOrderBook _orders = new();

    public IReadOnlyList<UnitOrderQueueSnapshot> ReadUnitOrders() => _orders.ReadQueues();
    public UnitOrderIntent? ReadCurrentOrder(EntityId id) => _orders.ReadCurrent(id);
    public int GetPendingOrderCount(EntityId id) => _orders.PendingCount(id);
    private bool BlocksUnitAi(EntityId id) => _orders.HasProtectedIntent(id) || _planning.HasProtectedMember(id);

    private enum MoveSubmission { Command, PlannedGroup }

    private bool ExecuteMove(CommandEnvelope command, GroupSlot? group = null) =>
        ExecuteMoveCore(command, group, MoveSubmission.Command);

    private bool ExecutePlannedGroupMove(CommandEnvelope command, GroupSlot group) =>
        ExecuteMoveCore(command, group, MoveSubmission.PlannedGroup);

    private bool ExecuteMoveCore(CommandEnvelope command, GroupSlot? group, MoveSubmission submission)
    {
        if (!_entities.TryGet(command.EntityId, out var entity) || entity.OwnerId != command.PlayerId)
        {
            AddEvent(MatchEventKind.CommandRejected, command.EntityId, "entity_missing_or_not_owned");
            return false;
        }
        if (command.Source == OrderSource.UnitAi && BlocksUnitAi(command.EntityId))
        {
            AddEvent(MatchEventKind.CommandRejected, command.EntityId, "player_order_active");
            return false;
        }
        var request = command.Move!;
        if (entity.MovementDefinitionId != 0 && (!_movementDefinitions.TryGet(entity.MovementDefinitionId, out var definition)
            || request.Speed != definition!.Speed || request.Motion != definition.Motion
            || request.ClearanceCells != Clearance(definition)))
        {
            AddEvent(MatchEventKind.CommandRejected, entity.Id, "move_does_not_match_definition");
            return false;
        }
        if (entity.MovementDefinitionId != 0 && !_navigation!.CanStandAt(request.Goal, ReadMovementDefinition(entity.Id)!.Radius))
        {
            AddEvent(MatchEventKind.CommandRejected, entity.Id, "move_body_goal_unavailable");
            return false;
        }
        if (request.Motion is { ScaleSlopeSpeed: true } && _navigation?.Terrain is null)
        {
            AddEvent(MatchEventKind.CommandRejected, command.EntityId, "motion_requires_height_field");
            return false;
        }
        var intent = new UnitOrderIntent(UnitOrderKind.Move, command.Source, request, group);
        if (command.Mode == OrderMode.Append && _orders.CanAppend(entity.Id))
        {
            if (_navigation is null || !_navigation.TryWorldToCell(request.Goal, out var goal)
                || !_navigation.CanOccupy(goal, request.ClearanceCells))
                AddEvent(MatchEventKind.CommandRejected, entity.Id, "move_goal_unavailable");
            else if (_orders.TryAppend(entity.Id, intent)) return true;
            else AddEvent(MatchEventKind.CommandRejected, entity.Id, "order_queue_full");
            return false; // No path is frozen for a future order; current movement stays intact.
        }
        if (!TryPlanMove(entity.Position, request, out var order, group is not null))
        {
            AddEvent(MatchEventKind.CommandRejected, command.EntityId, "move_path_unavailable");
            return false; // Invalid replacement preserves both current and pending orders.
        }
        if (command.Mode == OrderMode.Replace && submission == MoveSubmission.Command) CancelGroupPlans(entity.Id, command.Source);
        _orders.ReplaceMove(entity.Id, intent, order!);
        _entities.Update(entity with { Velocity = SimVector2.Zero });
        return true;
    }

    private void ExecuteStop(CommandEnvelope command)
    {
        if (!_entities.TryGet(command.EntityId, out var entity) || entity.OwnerId != command.PlayerId)
            AddEvent(MatchEventKind.CommandRejected, command.EntityId, "entity_missing_or_not_owned");
        else if (command.Source == OrderSource.UnitAi && BlocksUnitAi(entity.Id))
            AddEvent(MatchEventKind.CommandRejected, entity.Id, "player_order_active");
        else
        {
            CancelGroupPlans(entity.Id, command.Source);
            _orders.Stop(entity.Id, command.Source);
            _entities.Update(entity with { Velocity = SimVector2.Zero });
        }
    }

    private void ActivateQueuedMoves()
    {
        foreach (var id in _orders.WaitingIds())
        {
            var intent = _orders.PeekWaiting(id);
            var entity = _entities.Get(id);
            if (TryPlanMove(entity.Position, intent.Move!, out var move, intent.Group is not null))
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
