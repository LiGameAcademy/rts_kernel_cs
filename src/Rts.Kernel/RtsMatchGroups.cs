using Rts.Kernel.Navigation;

namespace Rts.Kernel;

public sealed partial class RtsMatch
{
    private readonly MovementDefinitions _movementDefinitions;
    private ulong _nextGroupId = 1;

    public MovementDefinition? ReadMovementDefinition(EntityId id) => _entities.TryGetValue(id, out var entity)
        && _movementDefinitions.TryGet(entity.MovementDefinitionId, out var definition) ? definition : null;

    private int Clearance(MovementDefinition definition)
    {
        var value = Math.Max(0, Math.Ceiling(definition.Radius / _navigation!.Grid.CellSize - 0.5));
        return value > int.MaxValue ? int.MaxValue : (int)value;
    }

    private SimVector2 PlacementStart(EntityState entity, OrderMode mode)
    {
        if (mode == OrderMode.Append && _unitOrders.TryGetValue(entity.Id, out var queue))
            return queue.Pending.LastOrDefault()?.Move?.Goal ?? queue.Current?.Move?.Goal ?? entity.Position;
        return entity.Position;
    }

    private void ExecuteGroupMove(CommandEnvelope command)
    {
        var request = command.Group!;
        if (_navigation is null || !_navigation.TryWorldToCell(request.Goal, out _))
        {
            foreach (var id in request.EntityIds) AddEvent(MatchEventKind.CommandRejected, id, "group_goal_outside_navigation");
            return;
        }
        if (_nextGroupId == ulong.MaxValue)
        {
            foreach (var id in request.EntityIds) AddEvent(MatchEventKind.CommandRejected, id, "group_id_exhausted");
            return;
        }
        var groupId = _nextGroupId++;
        var members = new List<PlacementMember>();
        foreach (var id in request.EntityIds)
        {
            string? error = null;
            if (!_entities.TryGetValue(id, out var entity) || entity.OwnerId != command.PlayerId)
                error = "entity_missing_or_not_owned";
            else if (command.Source == OrderSource.UnitAi && BlocksUnitAi(id)) error = "player_order_active";
            else if (command.Mode == OrderMode.Append && GetPendingOrderCount(id) >= MaximumPendingOrders)
                error = "order_queue_full";
            else if (!_movementDefinitions.TryGet(entity.MovementDefinitionId, out var definition))
                error = "movement_definition_missing";
            else if (definition!.Motion is { ScaleSlopeSpeed: true } && _navigation.Terrain is null)
                error = "motion_requires_height_field";
            else
            {
                var retained = new List<SimVector2> { entity.Position };
                if (_unitOrders.TryGetValue(id, out var retainedQueue))
                    retained.AddRange(retainedQueue.Pending.Prepend(retainedQueue.Current)
                        .Where(intent => intent?.Move is not null).Select(intent => intent!.Move!.Goal));
                members.Add(new PlacementMember(id, PlacementStart(entity, command.Mode), definition, Clearance(definition), retained));
            }
            if (error is not null) AddGroupEvent(MatchEventKind.CommandRejected, id, error, new(groupId, -1, null, false));
        }
        if (members.Count == 0) return;
        var activeIds = members.Select(member => member.Id).ToHashSet();
        var occupied = new List<PlacementBody>();
        foreach (var entity in _entities.Values.Where(entity => !activeIds.Contains(entity.Id)))
        {
            var radius = ReadMovementDefinition(entity.Id)?.Radius ?? 0;
            if (!IsMoving(entity.Id)) occupied.Add(new PlacementBody(entity.Position, radius));
            if (_unitOrders.TryGetValue(entity.Id, out var queue))
                foreach (var intent in queue.Pending.Prepend(queue.Current).Where(intent => intent?.Move is not null))
                    occupied.Add(new PlacementBody(intent!.Move!.Goal, radius));
        }
        var anchorMember = members.FirstOrDefault(member => member.Id == request.LeaderId) ?? members[0];
        var mean = new SimVector2(members.Sum(member => member.Start.X / members.Count),
            members.Sum(member => member.Start.Y / members.Count));
        var dx = request.Goal.X - mean.X;
        var dy = request.Goal.Y - mean.Y;
        var heading = request.Heading ?? (dx == 0 && dy == 0 ? _entities[anchorMember.Id].Facing : Math.Atan2(dy, dx));
        heading = MotionRules.Normalize(heading);
        IReadOnlyList<PlacementGoal> goals;
        try { goals = GroupPlacementPlanner.Plan(_navigation, members, request, heading, occupied); }
        catch (ArgumentException)
        {
            foreach (var member in members)
                AddGroupEvent(MatchEventKind.CommandRejected, member.Id, "invalid_group_geometry", new(groupId, -1, null, false));
            return;
        }
        foreach (var item in goals)
        {
            var outcome = new GroupMoveOutcome(groupId, item.Slot, item.Goal, item.Adjusted);
            if (item.Goal is null)
            {
                AddGroupEvent(MatchEventKind.CommandRejected, item.Member.Id, "group_slot_unavailable", outcome);
                continue;
            }
            var definition = item.Member.Definition;
            var move = CommandEnvelope.MoveTo(command.ExecuteFrame, command.PlayerId, command.Sequence, item.Member.Id,
                item.Goal.Value, definition.Speed, item.Member.Clearance, definition.Motion, command.Mode, command.Source);
            var slot = new GroupSlot(groupId, item.Slot, request.Formation, request.Goal, heading, item.Adjusted);
            if (ExecuteMove(move, slot)) AddGroupEvent(MatchEventKind.GroupMoveAssigned, item.Member.Id, string.Empty, outcome);
        }
    }

    private void AddGroupEvent(MatchEventKind kind, EntityId id, string detail, GroupMoveOutcome outcome) =>
        _events.Add(new MatchEvent(Frame, _nextEventSequence++, kind, id, detail, outcome));

    private void ValidateRestoredGroups(MatchSnapshot snapshot)
    {
        if (snapshot.MovementHash != _movementDefinitions.ContentHash)
            throw new InvalidDataException("Movement definitions do not match snapshot content.");
        foreach (var entity in _entities.Values)
            if (entity.MovementDefinitionId != 0 && !_movementDefinitions.TryGet(entity.MovementDefinitionId, out _))
                throw new InvalidDataException("Entity movement definition is missing.");
        foreach (var queue in snapshot.Orders!)
        foreach (var intent in queue.Pending.Prepend(queue.Current))
        {
            if (intent?.Move is null) continue;
            var definition = ReadMovementDefinition(new EntityId(queue.EntityId));
            if (definition is null && intent.Group is null) continue;
            if (definition is null || intent.Move!.Speed != definition.Speed || intent.Move.Motion != definition.Motion
                || intent.Move.ClearanceCells != Clearance(definition))
                throw new InvalidDataException("Group move does not match authoritative movement definition.");
        }
    }
}
