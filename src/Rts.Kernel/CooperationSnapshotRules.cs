using Rts.Kernel.Navigation;
using System.Text.Json;

namespace Rts.Kernel;

internal static class CooperationSnapshotRules
{
    internal static void Validate(MatchSnapshot snapshot)
    {
        var cooperation = snapshot.Cooperation ?? throw new InvalidDataException("Cooperation state is required.");
        if (cooperation.Members is null || cooperation.Yields is null)
            throw new InvalidDataException("Cooperation collections are required.");
        var entities = snapshot.Entities.ToDictionary(item => item.Id);
        var orders = snapshot.Orders!.ToDictionary(item => item.EntityId);
        var moves = snapshot.Moves!.Select(item => item.EntityId).ToHashSet();
        var members = new Dictionary<ulong, CooperationMemberSnapshot>();
        ulong previous = 0;
        foreach (var member in cooperation.Members)
        {
            if (member is null || member.EntityId <= previous || !entities.TryGetValue(member.EntityId, out var entity)
                || entity.MovementDefinitionId == 0 || member.GroupId >= snapshot.NextGroupId
                || member.AssistanceGroupId >= snapshot.NextGroupId
                || (member.GroupId == 0 && member.AssistanceGroupId == 0)
                || !SimulationDomain.Contains(member.Anchor))
                throw new InvalidDataException("Invalid cooperation member or canonical order.");
            var current = orders.GetValueOrDefault(member.EntityId)?.Current;
            if (current?.Kind == UnitOrderKind.Stop
                || (current is not null && (current.Group?.SoftLanding == true ? current.Group.GroupId : 0) != member.GroupId)
                || (member.Settled && (current?.Group?.SoftLanding != true || moves.Contains(member.EntityId)))
                || (member.AssistanceGroupId != 0 && current?.Kind != UnitOrderKind.Move))
                throw new InvalidDataException("Cooperation permission disagrees with order ownership.");
            previous = member.EntityId;
            members.Add(member.EntityId, member);
        }
        foreach (var queue in orders.Values)
            if (queue.Current?.Group?.SoftLanding == true
                && (!members.TryGetValue(queue.EntityId, out var member) || member.GroupId != queue.Current.Group.GroupId))
                throw new InvalidDataException("Every active soft-party member requires cooperation identity.");
        foreach (var member in members.Values)
        {
            var assistanceOwners = members.Values.Where(peer => peer.GroupId == member.AssistanceGroupId && peer.GroupId != 0)
                .Select(peer => entities[peer.EntityId].OwnerId);
            if (assistanceOwners.Any(owner => owner != entities[member.EntityId].OwnerId))
                throw new InvalidDataException("Previous-party assistance cannot cross ownership.");
        }
        foreach (var group in members.Values.Where(member => member.GroupId != 0).GroupBy(member => member.GroupId))
            if (group.Count() > LocalYieldPlanner.MaximumMembers
                || group.Select(member => entities[member.EntityId].OwnerId).Distinct().Count() != 1)
                throw new InvalidDataException("Cooperation requires a small single-owner party.");
        previous = 0;
        foreach (var task in cooperation.Yields)
        {
            if (task is null || task.EntityId <= previous || task.RequesterId == task.EntityId
                || !members.TryGetValue(task.EntityId, out var helper)
                || !members.TryGetValue(task.RequesterId, out var requester)
                || helper.GroupId == 0 || (helper.GroupId != requester.GroupId && helper.GroupId != requester.AssistanceGroupId)
                || entities[task.EntityId].OwnerId != entities[task.RequesterId].OwnerId
                || moves.Contains(task.EntityId) || !moves.Contains(task.RequesterId)
                || task.Path is null || task.Path.EntityId != task.EntityId
                || (task.ResumeMove && (helper.Settled || orders.GetValueOrDefault(task.EntityId)?.Current?.Kind != UnitOrderKind.Move))
                || (!task.ResumeMove && !helper.Settled && orders.GetValueOrDefault(task.EntityId)?.Current is not null))
                throw new InvalidDataException("Invalid temporary yield ownership.");
            MovementSnapshot.Validate(snapshot with { Moves = [task.Path] }, entities.Keys.ToHashSet());
            if (task.Path.Waypoints[^1] != task.Path.Request.Goal)
                throw new InvalidDataException("Temporary yield must retain its exact endpoint.");
            previous = task.EntityId;
        }
        foreach (var member in members.Values.Where(member => member.Settled))
            if (!cooperation.Yields.Any(task => task.EntityId == member.EntityId)
                && (entities[member.EntityId].VelocityX != 0 || entities[member.EntityId].VelocityY != 0))
                throw new InvalidDataException("Settled member without a yield must be stationary.");
    }

    internal static void Restore(UnitOrderBook orders, MatchSnapshot snapshot,
        IReadOnlyDictionary<EntityId, EntityState> entities, NavigationState? navigation, MovementDefinitions definitions)
    {
        var cooperation = snapshot.Cooperation!;
        foreach (var task in cooperation.Yields)
        {
            var entity = entities[new(task.EntityId)];
            definitions.TryGet(entity.MovementDefinitionId, out var definition);
            var member = cooperation.Members.Single(item => item.EntityId == task.EntityId);
            var anchor = task.ResumeMove ? task.Path.Waypoints[0] : member.Anchor;
            if (task.Path.Request.Speed != definition!.Speed || task.Path.Request.Motion != definition.Motion
                || task.Path.Request.ClearanceCells != MovementDefinitions.ClearanceCells(definition, navigation!.Grid.CellSize)
                || (task.Path.Request.Goal - anchor).Length > SoftGoalPath.Tolerance(definition.Radius, navigation.Grid.CellSize))
                throw new InvalidDataException("Yield path exceeds its definition or bounded displacement.");
            if (member.Settled)
            {
                var preferred = snapshot.Orders!.Single(item => item.EntityId == member.EntityId).Current!.Move!.Goal;
                if ((task.Path.Request.Goal - preferred).Length > SoftGoalPath.Tolerance(definition.Radius, navigation.Grid.CellSize))
                    throw new InvalidDataException("Yield endpoint leaves the settled member's completion envelope.");
            }
        }
        // Reuse the same terrain, cell-adjacency and segment-progress validation as normal motion.
        var validation = new UnitOrderBook();
        validation.RestorePaths(snapshot with { Moves = cooperation.Yields.Select(task => task.Path).ToArray() }, entities, navigation);
        foreach (var member in cooperation.Members.Where(item => item.Settled))
        {
            var entity = entities[new(member.EntityId)];
            definitions.TryGet(entity.MovementDefinitionId, out var definition);
            var request = snapshot.Orders!.Single(item => item.EntityId == member.EntityId).Current!.Move!;
            if ((entity.Position - request.Goal).Length > SoftGoalPath.Tolerance(definition!.Radius, navigation!.Grid.CellSize))
                throw new InvalidDataException("Settled member is outside its soft completion envelope.");
        }
        orders.Cooperation.Restore(cooperation);
    }

    internal static SnapshotDifference? FindFirst(MatchSnapshot expected, MatchSnapshot actual)
    {
        var left = JsonSerializer.Serialize(expected.Cooperation);
        var right = JsonSerializer.Serialize(actual.Cooperation);
        return left == right ? null : new("cooperation", left, right);
    }
}
