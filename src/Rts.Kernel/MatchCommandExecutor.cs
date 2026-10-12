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

    private bool ExecuteMoveCore(long frame, CommandEnvelope command, NavigationState? navigation, GroupSlot? group,
        MoveSubmission submission)
    {
        if (!_entities.TryGet(command.EntityId, out var entity) || entity.OwnerId != command.PlayerId)
        {
            Publish(frame, MatchEventKind.CommandRejected, command.EntityId, "entity_missing_or_not_owned");
            return false;
        }
        if (command.Source == OrderSource.UnitAi && BlocksUnitAi(command.EntityId))
        {
            Publish(frame, MatchEventKind.CommandRejected, command.EntityId, "player_order_active");
            return false;
        }
        var request = command.Move!;
        if (entity.MovementDefinitionId != 0 && (!_movementDefinitions.TryGet(entity.MovementDefinitionId, out var definition)
            || request.Speed != definition!.Speed || request.Motion != definition.Motion
            || request.ClearanceCells != MovementDefinitions.ClearanceCells(definition, navigation!.Grid.CellSize)))
        {
            Publish(frame, MatchEventKind.CommandRejected, entity.Id, "move_does_not_match_definition");
            return false;
        }
        if (entity.MovementDefinitionId != 0
            && !navigation!.CanStandAt(request.Goal, ReadDefinition(entity.Id)!.Radius))
        {
            Publish(frame, MatchEventKind.CommandRejected, entity.Id, "move_body_goal_unavailable");
            return false;
        }
        if (request.Motion is { ScaleSlopeSpeed: true } && navigation?.Terrain is null)
        {
            Publish(frame, MatchEventKind.CommandRejected, command.EntityId, "motion_requires_height_field");
            return false;
        }
        var intent = new UnitOrderIntent(UnitOrderKind.Move, command.Source, request, group);
        if (command.Mode == OrderMode.Append && _orders.CanAppend(entity.Id))
        {
            if (navigation is null || !navigation.TryWorldToCell(request.Goal, out var goal)
                || !navigation.CanOccupy(goal, request.ClearanceCells))
                Publish(frame, MatchEventKind.CommandRejected, entity.Id, "move_goal_unavailable");
            else if (_orders.TryAppend(entity.Id, intent)) return true;
            else Publish(frame, MatchEventKind.CommandRejected, entity.Id, "order_queue_full");
            return false; // No path is frozen for a future order; current movement stays intact.
        }
        if (!MovePathPlanner.TryPlan(navigation, entity.Position, request, out var order, group is not null))
        {
            Publish(frame, MatchEventKind.CommandRejected, command.EntityId, "move_path_unavailable");
            return false; // Invalid replacement preserves both current and pending orders.
        }
        if (command.Mode == OrderMode.Replace && submission == MoveSubmission.Command)
            CancelGroupPlans(frame, entity.Id, command.Source);
        _orders.ReplaceMove(entity.Id, intent, order!);
        _entities.Update(entity with { Velocity = SimVector2.Zero });
        return true;
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
            _entities.Update(entity with { Velocity = SimVector2.Zero });
        }
    }

    private void ExecuteSpawn(long frame, CommandEnvelope command, NavigationState? navigation)
    {
        if (command.MovementDefinitionId != 0 && !_movementDefinitions.TryGet(command.MovementDefinitionId, out _))
        {
            Publish(frame, MatchEventKind.CommandRejected, EntityId.None, "movement_definition_missing");
            return;
        }
        if (!CanSpawnGround(command, navigation))
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

    private bool CanSpawnGround(CommandEnvelope command, NavigationState? navigation)
    {
        if (command.MovementDefinitionId == 0) return true;
        var definition = _movementDefinitions.TryGet(command.MovementDefinitionId, out var found) ? found : null;
        if (definition is null || navigation is null || !navigation.TryWorldToCell(command.Position, out var cell)
            || !navigation.CanOccupy(cell, MovementDefinitions.ClearanceCells(definition, navigation.Grid.CellSize))
            || !navigation.CanStandAt(command.Position, definition.Radius))
            return false;
        return !_entities.Live.Where(entity => entity.MovementDefinitionId != 0).Any(entity =>
            CrowdGeometry.Overlap(command.Position, command.Position, definition.Radius,
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
