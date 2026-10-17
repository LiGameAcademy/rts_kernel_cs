using Rts.Kernel.Navigation;

namespace Rts.Kernel;

public sealed partial class RtsMatch
{
    private readonly MovementDefinitions _movementDefinitions;

    public MovementDefinition? ReadMovementDefinition(EntityId id) => _entities.TryGet(id, out var entity)
        && _movementDefinitions.TryGet(entity.MovementDefinitionId, out var definition) ? definition : null;

    private int Clearance(MovementDefinition definition) =>
        MovementDefinitions.ClearanceCells(definition, _navigation!.Grid.CellSize);

    private void ExecuteGroupMove(CommandEnvelope command)
    {
        var request = command.Group!;
        if (_navigation is null || !_navigation.TryWorldToCell(request.Goal, out _))
        {
            foreach (var id in request.EntityIds) AddEvent(MatchEventKind.CommandRejected, id, "group_goal_outside_navigation");
            return;
        }
        if (!_planning.TryAllocateId(out var groupId))
        {
            foreach (var id in request.EntityIds) AddEvent(MatchEventKind.CommandRejected, id, "group_id_exhausted");
            return;
        }
        var members = PrepareGroupMembers(command, groupId);
        if (members.Count == 0) return;
        var anchorMember = members.FirstOrDefault(member => member.Id == request.LeaderId) ?? members[0];
        var mean = MeanStart(members);
        var dx = request.Goal.X - mean.X;
        var dy = request.Goal.Y - mean.Y;
        var heading = request.Heading ?? (dx == 0 && dy == 0 ? _entities.Get(anchorMember.Id).Facing : SimMath.Atan2(dy, dx));
        heading = MotionRules.Normalize(heading);
        IReadOnlyList<PlacementGoal> goals;
        try
        {
            if (members.Count > ImmediateGroupMembers)
            {
                if (_planning.Count >= MaximumGroupPlans)
                {
                    foreach (var member in members)
                        AddGroupEvent(MatchEventKind.CommandRejected, member.Id, "group_planning_queue_full", new(groupId, -1, null, false));
                    return;
                }
                var spacing = GroupPlacementPlanner.CalculateSpacing(_navigation, members);
                var job = new GroupPlanningJob(groupId, command, members, heading, spacing);
                if (command.Mode == OrderMode.Replace)
                    foreach (var member in members)
                        _commandExecutor.CancelGroupPlans(Frame, member.Id, command.Source);
                _planning.Enqueue(job);
                return;
            }
            goals = GroupPlacementPlanner.Plan(_navigation, members, request, heading, GroupBodies(members));
        }
        catch (ArgumentException)
        {
            foreach (var member in members)
                AddGroupEvent(MatchEventKind.CommandRejected, member.Id, "invalid_group_geometry", new(groupId, -1, null, false));
            return;
        }
        ApplyGroupGoals(command, groupId, heading, goals);
    }

    private static FixedVector2 MeanStart(IReadOnlyList<PlacementMember> members)
    {
        Int128 x = 0;
        Int128 y = 0;
        foreach (var member in members)
        {
            x += member.Start.X.Raw;
            y += member.Start.Y.Raw;
        }
        return FixedVector2.FromRaw(checked((long)(x / members.Count)), checked((long)(y / members.Count)));
    }

    private IReadOnlyList<PlacementMember> PrepareGroupMembers(CommandEnvelope command, ulong groupId)
    {
        var members = new List<PlacementMember>();
        foreach (var id in command.Group!.EntityIds)
        {
            string? error = null;
            if (!_entities.TryGet(id, out var entity) || entity.OwnerId != command.PlayerId)
                error = "entity_missing_or_not_owned";
            else if (command.Source == OrderSource.UnitAi && BlocksUnitAi(id)) error = "player_order_active";
            else if (command.Mode == OrderMode.Append && GetPendingOrderCount(id) >= MaximumPendingOrders)
                error = "order_queue_full";
            else if (!_movementDefinitions.TryGet(entity.MovementDefinitionId, out var definition))
                error = "movement_definition_missing";
            else if (definition!.Motion is { ScaleSlopeSpeed: true } && _navigation?.Terrain is null)
                error = "motion_requires_height_field";
            else
            {
                var retained = new List<FixedVector2> { entity.Position };
                retained.AddRange(_orders.RetainedGoals(id));
                members.Add(new PlacementMember(id, _orders.PlacementStart(entity, command.Mode), definition, Clearance(definition), retained));
            }
            if (error is not null) AddGroupEvent(MatchEventKind.CommandRejected, id, error, new(groupId, -1, null, false));
        }
        return members;
    }

    // Compile this measured hot scan with optimizations to reduce command-time JIT spikes.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
    private IReadOnlyList<PlacementBody> GroupBodies(IReadOnlyList<PlacementMember> members)
    {
        var activeIds = members.Select(member => member.Id).ToHashSet();
        var occupied = new List<PlacementBody>();
        foreach (var entity in _entities.Live.Where(entity => !activeIds.Contains(entity.Id)))
        {
            var radius = ReadMovementDefinition(entity.Id)?.Radius ?? 0;
            if (!IsMoving(entity.Id)) occupied.Add(new PlacementBody(entity.Position, radius));
            foreach (var goal in _orders.RetainedGoals(entity.Id))
                occupied.Add(new PlacementBody(goal, radius));
        }
        return occupied;
    }

    private void ApplyGroupGoals(CommandEnvelope command, ulong groupId, SimScalar heading, IReadOnlyList<PlacementGoal> goals) =>
        SubmitGroupGoals(command, groupId, heading, goals, MatchCommandExecutor.MoveSubmission.Command);

    private void CommitPlannedGroupGoals(CommandEnvelope command, ulong groupId, SimScalar heading, IReadOnlyList<PlacementGoal> goals) =>
        SubmitGroupGoals(command, groupId, heading, goals, MatchCommandExecutor.MoveSubmission.PlannedGroup);

    private void SubmitGroupGoals(CommandEnvelope command, ulong groupId, SimScalar heading,
        IReadOnlyList<PlacementGoal> goals, MatchCommandExecutor.MoveSubmission submission)
    {
        var request = command.Group!;
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
            var slot = new GroupSlot(groupId, item.Slot, request.Formation, request.Goal, heading, item.Adjusted, request.EntityIds.Count <= SmallGroupAssignment.MaximumMembers, request.EntityIds.Count);
            var accepted = submission == MatchCommandExecutor.MoveSubmission.PlannedGroup
                ? _commandExecutor.ExecutePlannedGroupMove(Frame, move, _navigation, slot)
                : _commandExecutor.ExecuteMove(Frame, move, _navigation, slot);
            if (accepted) AddGroupEvent(MatchEventKind.GroupMoveAssigned, item.Member.Id, string.Empty, outcome);
        }
    }

    private void AddGroupEvent(MatchEventKind kind, EntityId id, string detail, GroupMoveOutcome outcome) =>
        _events.Publish(Frame, kind, id, detail, outcome);

    private void ValidateRestoredGroups(MatchSnapshot snapshot)
    {
        if (snapshot.MovementHash != _movementDefinitions.ContentHash)
            throw new InvalidDataException("Movement definitions do not match snapshot content.");
        foreach (var entity in _entities.Live)
            if (entity.MovementDefinitionId != 0 && !_movementDefinitions.TryGet(entity.MovementDefinitionId, out _))
                throw new InvalidDataException("Entity movement definition is missing.");
        foreach (var move in snapshot.Moves!)
        {
            if (move.Waypoints[^1] == move.Request.Goal) continue;
            var id = new EntityId(move.EntityId);
            var definition = ReadMovementDefinition(id);
            if (_orders.ReadCurrent(id)?.Group?.SoftLanding != true || definition is null
                || SquaredDistance.Between(move.Waypoints[^1], move.Request.Goal).Length
                    > SoftGoalPath.Tolerance(definition.Radius, _navigation!.Grid.CellSize))
                throw new InvalidDataException("Soft landing endpoint exceeds its preferred-goal envelope.");
        }
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
