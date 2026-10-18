using Rts.Kernel.Navigation;

namespace Rts.Kernel;

/// <summary>
/// Applies accepted command rules for one match. Owns no durable state; mutates injected modules only.
/// </summary>
internal sealed class MatchCommandExecutor
{
    private readonly MatchEntities _entities;
    private readonly MatchEventBuffer _events;
    private readonly UnitOrderBook _orders;
    private readonly GroupPlanningQueue _planning;
    private readonly MovementDefinitions _movementDefinitions;

    internal MatchCommandExecutor(MatchEntities entities, MatchEventBuffer events, UnitOrderBook orders,
        GroupPlanningQueue planning, MovementDefinitions movementDefinitions)
    {
        _entities = entities;
        _events = events;
        _orders = orders;
        _planning = planning;
        _movementDefinitions = movementDefinitions;
    }

    internal enum MoveSubmission { Command, PlannedGroup }

    internal void Execute(long frame, CommandEnvelope command, NavigationState? navigation, ref bool pathsDirty)
    {
        switch (command.Kind)
        {
            case CommandKind.SetObstacle:
                if (navigation is null || !navigation.TrySetObstacle(command.Obstacle!.Id, command.Obstacle.Area!.Value))
                    Publish(frame, MatchEventKind.CommandRejected, EntityId.None, "invalid_obstacle_or_navigation_disabled");
                else pathsDirty = true;
                break;
            case CommandKind.RemoveObstacle:
                if (navigation is null || !navigation.RemoveObstacle(command.Obstacle!.Id))
                    Publish(frame, MatchEventKind.CommandRejected, EntityId.None, "obstacle_missing_or_navigation_disabled");
                else pathsDirty = true;
                break;
            case CommandKind.MoveTo:
                ExecuteMove(frame, command, navigation);
                break;
            case CommandKind.SpawnEntity:
                ExecuteSpawn(frame, command, navigation);
                break;
            case CommandKind.SetVelocity:
                ExecuteSetVelocity(frame, command, navigation);
                break;
            case CommandKind.Stop:
                ExecuteStop(frame, command);
                break;
            default:
                Publish(frame, MatchEventKind.CommandRejected, command.EntityId, "unknown_command");
                break;
        }
    }

    internal bool ExecuteMove(long frame, CommandEnvelope command, NavigationState? navigation, GroupSlot? group = null) =>
        ExecuteMoveCore(frame, command, navigation, group, MoveSubmission.Command);

    internal bool ExecutePlannedGroupMove(long frame, CommandEnvelope command, NavigationState? navigation, GroupSlot group) =>
        ExecuteMoveCore(frame, command, navigation, group, MoveSubmission.PlannedGroup);

    internal sealed record MovePreparation(EntityId EntityId, UnitOrderIntent Intent,
        MoveOrder? Path, bool Append, bool CancelPlans);

    private bool ExecuteMoveCore(long frame, CommandEnvelope command, NavigationState? navigation, GroupSlot? group,
        MoveSubmission submission)
    {
        var error = PrepareMove(command, navigation, group, submission, out var prepared);
        if (error is not null)
        {
            Publish(frame, MatchEventKind.CommandRejected, command.EntityId, error);
            return false;
        }
        CommitMove(frame, prepared!);
        return true;
    }

    // No orders, events or group-plan ownership change until the prepared move is committed.
    internal string? PrepareMove(CommandEnvelope command, NavigationState? navigation, GroupSlot? group,
        MoveSubmission submission, out MovePreparation? prepared)
    {
        prepared = null;
        if (!_entities.TryGet(command.EntityId, out var entity) || entity.OwnerId != command.PlayerId)
            return "entity_missing_or_not_owned";
        if (command.Source == OrderSource.UnitAi && BlocksUnitAi(command.EntityId))
            return "player_order_active";
        var request = command.Move!;
        if (entity.MovementDefinitionId != 0 && (!_movementDefinitions.TryGet(entity.MovementDefinitionId, out var definition)
            || request.Speed != definition!.Speed || request.Motion != definition.Motion
            || request.ClearanceCells != MovementDefinitions.ClearanceCells(definition, navigation!.Grid.CellSize)))
            return "move_does_not_match_definition";
        if (entity.MovementDefinitionId != 0
            && !navigation!.CanStandAt(request.Goal, ReadDefinition(entity.Id)!.Radius))
            return "move_body_goal_unavailable";
        if (request.Motion is { ScaleSlopeSpeed: true } && navigation?.Terrain is null)
            return "motion_requires_height_field";
        var intent = new UnitOrderIntent(UnitOrderKind.Move, command.Source, request, group);
        var append = command.Mode == OrderMode.Append && _orders.CanAppend(entity.Id);
        MoveOrder? path = null;
        if (append)
        {
            if (navigation is null || !navigation.TryWorldToCell(request.Goal, out var goal)
                || !navigation.CanOccupy(goal, request.ClearanceCells)) return "move_goal_unavailable";
            if (_orders.PendingCount(entity.Id) >= UnitOrderBook.MaximumPending) return "order_queue_full";
        }
        else if (!MovePathPlanner.TryPlan(navigation, entity.Position, request, out path,
                     group is not null, group?.SoftLanding == true)) return "move_path_unavailable";
        prepared = new(entity.Id, intent, path, append,
            !append && command.Mode == OrderMode.Replace && submission == MoveSubmission.Command);
        return null;
    }

    internal void CommitMove(long frame, MovePreparation prepared)
    {
        if (prepared.Append)
        {
            if (!_orders.TryAppend(prepared.EntityId, prepared.Intent))
                throw new InvalidOperationException("Prepared move queue capacity changed before commit.");
            return;
        }
        if (prepared.CancelPlans) CancelGroupPlans(frame, prepared.EntityId, prepared.Intent.Source);
        _orders.ReplaceMove(prepared.EntityId, prepared.Intent, prepared.Path!);
        _entities.Update(_entities.Get(prepared.EntityId) with { Velocity = FixedVector2.Zero });
    }

    private void ExecuteStop(long frame, CommandEnvelope command)
    {
        if (!_entities.TryGet(command.EntityId, out var entity) || entity.OwnerId != command.PlayerId)
            Publish(frame, MatchEventKind.CommandRejected, command.EntityId, "entity_missing_or_not_owned");
        else if (command.Source == OrderSource.UnitAi && BlocksUnitAi(entity.Id))
            Publish(frame, MatchEventKind.CommandRejected, entity.Id, "player_order_active");
        else
        {
            CancelGroupPlans(frame, entity.Id, command.Source);
            _orders.Stop(entity.Id, command.Source);
            _entities.Update(entity with { Velocity = FixedVector2.Zero });
        }
    }

    private void ExecuteSpawn(long frame, CommandEnvelope command, NavigationState? navigation)
    {
        if (command.MovementDefinitionId != 0 && !_movementDefinitions.TryGet(command.MovementDefinitionId, out _))
        {
            Publish(frame, MatchEventKind.CommandRejected, EntityId.None, "movement_definition_missing");
            return;
        }
        if (!CanSpawnGround(command.Position, command.MovementDefinitionId, navigation))
        {
            Publish(frame, MatchEventKind.CommandRejected, EntityId.None, "spawn_ground_unavailable");
            return;
        }
        var spawned = _entities.Spawn(command.PlayerId, command.Position, command.MovementDefinitionId);
        Publish(frame, MatchEventKind.EntitySpawned, spawned.Id, string.Empty);
    }

    private void ExecuteSetVelocity(long frame, CommandEnvelope command, NavigationState? navigation)
    {
        if (!_entities.TryGet(command.EntityId, out var entity) || entity.OwnerId != command.PlayerId)
        {
            Publish(frame, MatchEventKind.CommandRejected, command.EntityId, "entity_missing_or_not_owned");
            return;
        }

        if (navigation is not null)
        {
            Publish(frame, MatchEventKind.CommandRejected, command.EntityId, "use_move_to_with_navigation");
            return;
        }
        _orders.Clear(entity.Id);
        _entities.Update(entity with { Velocity = command.Velocity });
    }

    internal bool CanSpawnGround(FixedVector2 position, ulong movementDefinitionId, NavigationState? navigation)
    {
        if (!SimulationDomain.Contains(position)) return false;
        if (movementDefinitionId == 0) return true;
        var definition = _movementDefinitions.TryGet(movementDefinitionId, out var found) ? found : null;
        if (definition is null || navigation is null || !navigation.TryWorldToCell(position, out var cell)
            || !navigation.CanOccupy(cell, MovementDefinitions.ClearanceCells(definition, navigation.Grid.CellSize))
            || !navigation.CanStandAt(position, definition.Radius))
            return false;
        return !_entities.Live.Where(entity => entity.MovementDefinitionId != 0).Any(entity =>
            CrowdGeometry.Overlap(position, position, definition.Radius,
                entity.Position, entity.Position, ReadDefinition(entity.Id)!.Radius));
    }

    internal void CancelGroupPlans(long frame, EntityId id, OrderSource source)
    {
        foreach (var outcome in _planning.Cancel(id, source))
            _events.Publish(frame, MatchEventKind.CommandRejected, id, "group_plan_superseded", outcome);
    }

    private bool BlocksUnitAi(EntityId id) =>
        _orders.HasProtectedIntent(id) || _planning.HasProtectedMember(id);

    private MovementDefinition? ReadDefinition(EntityId id) =>
        _entities.TryGet(id, out var entity) && _movementDefinitions.TryGet(entity.MovementDefinitionId, out var definition)
            ? definition : null;

    private void Publish(long frame, MatchEventKind kind, EntityId entityId, string detail) =>
        _events.Publish(frame, kind, entityId, detail);
}
