using Rts.Kernel.Navigation;

namespace Rts.Kernel;

/// <summary>Finds a small, same-party lateral translation closure. It never moves bodies directly.</summary>
internal static class LocalYieldPlanner
{
    internal const int MaximumMembers = 24;

    internal static IReadOnlyList<(EntityId Id, MoveOrder Path)>? Plan(EntityState requester, MoveOrder order,
        EntityId blocker, IReadOnlyList<EntityState> bodies, UnitOrderBook orders,
        MovementDefinitions definitions, NavigationState navigation)
    {
        var cooperation = orders.Cooperation;
        if (!cooperation.CanHelp(blocker, requester.Id, orders) || cooperation.HasRequests(requester.Id)) return null;
        var next = order.Waypoints.Skip(order.NextWaypoint).FirstOrDefault(point => point != requester.Position);
        var direction = next - requester.Position;
        if (direction.Length == 0) return null;
        direction /= direction.Length;
        var normal = new FixedVector2(-direction.Y, direction.X);
        var first = bodies.Single(body => body.Id == blocker);
        definitions.TryGet(requester.MovementDefinitionId, out var requesterDefinition);
        definitions.TryGet(first.MovementDefinitionId, out var firstDefinition);
        var offset = first.Position - requester.Position;
        var side = offset.X * normal.X + offset.Y * normal.Y;
        var preferredSign = side < 0 ? -1 : 1;
        var translations = new List<FixedVector2>(2);
        foreach (var sign in new[] { preferredSign, -preferredSign })
        {
            var clearance = requesterDefinition!.Radius + firstDefinition!.Radius + navigation.Grid.CellSize / 4;
            var distance = SimMath.Max(navigation.Grid.CellSize / 2, clearance - side * sign);
            var translation = normal * (distance * sign);
            translations.Add(translation);
            var result = BuildClosure(requester, blocker, translation, bodies, orders, definitions, navigation);
            if (result is not null) return result;
        }
        if (order.Review?.Recovering != true) return null;
        var group = cooperation.Read(requester.Id)!.GroupId;
        if (group == 0 || bodies.Any(body => body.Id != requester.Id
            && cooperation.Read(body.Id)?.GroupId == group && !cooperation.IsSettled(body.Id))) return null;
        // Settled peers can already be at the outer edge of their goal envelope.
        // A purely lateral opening would push them outside it. Keep that same lateral
        // clearance, but allow a short forward slide for the last arriving member.
        // Earlier arrivals retain their existing plans; concurrent recovery requests
        // must not recruit peers into crossing forward translations.
        for (var cells = 1; cells <= 2; cells++)
        foreach (var translation in translations)
        {
            var result = BuildClosure(requester, blocker,
                translation + direction * (navigation.Grid.CellSize * cells),
                bodies, orders, definitions, navigation);
            if (result is not null) return result;
        }
        return null;
    }

    private static IReadOnlyList<(EntityId Id, MoveOrder Path)>? BuildClosure(EntityState requester,
        EntityId blocker, FixedVector2 translation, IReadOnlyList<EntityState> bodies, UnitOrderBook orders,
        MovementDefinitions definitions, NavigationState navigation)
    {
        var pending = new Queue<EntityId>();
        var selected = new HashSet<EntityId> { blocker };
        var result = new List<(EntityId, MoveOrder)>();
        pending.Enqueue(blocker);
        while (pending.TryDequeue(out var id))
        {
            if (!orders.Cooperation.CanHelp(id, requester.Id, orders)) return null;
            var entity = bodies.Single(body => body.Id == id);
            definitions.TryGet(entity.MovementDefinitionId, out var definition);
            var goal = entity.Position + translation;
            var member = orders.Cooperation.Read(id)!;
            var anchor = orders.IsMoving(id) ? entity.Position : member.Anchor;
            var tolerance = SoftGoalPath.Tolerance(definition!.Radius, navigation.Grid.CellSize);
            if ((goal - anchor).Length > tolerance || !navigation.CanStandAt(goal, definition.Radius)) return null;
            var current = orders.ReadCurrent(id);
            if (member.Settled && current?.Move is { } move && (goal - move.Goal).Length > tolerance) return null;
            var clearance = MovementDefinitions.ClearanceCells(definition, navigation.Grid.CellSize);
            var path = StraightMovePath.TryFind(navigation, entity.Position, goal, clearance);
            if (path is null) return null;
            // Every selected member translates by the same vector. Their mutual spacing is
            // unchanged geometrically; actual speeds/turning are still resolved every frame.
            foreach (var other in bodies)
            {
                if (other.Id == id || selected.Contains(other.Id)) continue;
                definitions.TryGet(other.MovementDefinitionId, out var otherDefinition);
                if (!CrowdGeometry.Overlap(entity.Position, goal, definition.Radius,
                        other.Position, other.Position, otherDefinition!.Radius)) continue;
                if (other.OwnerId != requester.OwnerId || !orders.Cooperation.CanHelp(other.Id, requester.Id, orders)
                    || selected.Count >= MaximumMembers) return null;
                selected.Add(other.Id);
                pending.Enqueue(other.Id);
            }
            result.Add((id, new(new(goal, definition.Speed, clearance, definition.Motion), path)));
        }
        return result.OrderBy(item => item.Item1).ToArray();
    }
}
