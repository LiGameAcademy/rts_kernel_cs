namespace Rts.Kernel.Navigation;

internal static class GroupPlacementPlanner
{
    internal const int AdjustmentCells = 8;

    internal static IReadOnlyList<PlacementGoal> Plan(NavigationState navigation, IReadOnlyList<PlacementMember> members,
        GroupMoveRequest request, double heading, IReadOnlyList<PlacementBody> externalBodies)
    {
        var spacing = Math.Max(navigation.Grid.CellSize,
            2 * members.Max(member => member.Definition.Radius) + navigation.Grid.CellSize * 0.1);
        var ideals = FormationLayout.Create(request.Formation, members.Count, spacing, request.Goal, heading);
        var anchorIndex = request.LeaderId.IsNone ? 0 : members.ToList().FindIndex(member => member.Id == request.LeaderId);
        if (anchorIndex < 0) anchorIndex = 0;
        var slots = FormationLayout.Match(members.Select(member => member.Start).ToArray(), ideals, anchorIndex);
        return PlanAssigned(navigation, members, ideals, slots, externalBodies);
    }

    internal static IReadOnlyList<PlacementGoal> PlanAssigned(NavigationState navigation,
        IReadOnlyList<PlacementMember> members, IReadOnlyList<SimVector2> ideals, IReadOnlyList<int> slots,
        IReadOnlyList<PlacementBody>? externalBodies = null, IReadOnlyList<IReadOnlyList<GridCell>>? preparedCandidates = null)
    {
        externalBodies ??= [];
        var reachable = new NavigationComponents(navigation);
        var candidates = preparedCandidates?.ToArray() ?? new IReadOnlyList<GridCell>?[ideals.Count];
        var failed = new HashSet<EntityId>();
        var result = new List<PlacementGoal>();
        // Failed members retain their old orders. Re-plan remaining placements against their unmoved bodies.
        while (true)
        {
            var before = failed.Count;
            var occupied = externalBodies.Concat(members.Where(member => failed.Contains(member.Id))
                .SelectMany(member => member.RetainedPositions.Select(position => new PlacementBody(position, member.Definition.Radius)))).ToList();
            var maximumRadius = Math.Max(members.Max(member => member.Definition.Radius),
                occupied.Count == 0 ? 0 : occupied.Max(body => body.Radius));
            var bodyIndex = new PlacementOccupancy(navigation, maximumRadius);
            foreach (var body in occupied) bodyIndex.Add(body);
            result.Clear();
            foreach (var index in Enumerable.Range(0, members.Count).OrderBy(i => slots[i]))
            {
                var member = members[index];
                var slot = slots[index];
                SimVector2? goal = null;
                if (!failed.Contains(member.Id) && navigation.TryWorldToCell(member.Start, out var start))
                    foreach (var cell in candidates[slot] ??= Candidates(navigation, ideals[slot]))
                    {
                        if (!reachable.Connected(start, cell, member.Clearance)) continue;
                        var center = navigation.CellCenter(cell);
                        if (bodyIndex.Overlaps(center, member.Definition.Radius)) continue;
                        goal = center;
                        bodyIndex.Add(new PlacementBody(center, member.Definition.Radius));
                        break;
                    }
                if (goal is null) failed.Add(member.Id);
                result.Add(new PlacementGoal(member, slot, goal, goal is not null && goal != ideals[slot]));
            }
            if (before == failed.Count) return result.OrderBy(item => item.Member.Id).ToArray();
        }
    }

    internal static bool Overlaps(SimVector2 center, double radius, PlacementBody body)
    {
        var dx = center.X - body.Position.X;
        var dy = center.Y - body.Position.Y;
        var separation = radius + body.Radius;
        if (center == body.Position) return true;
        if (double.IsPositiveInfinity(separation)) return true;
        var scale = Math.Max(separation, Math.Max(Math.Abs(dx), Math.Abs(dy)));
        if (double.IsPositiveInfinity(scale)) return false;
        return (dx / scale) * (dx / scale) + (dy / scale) * (dy / scale)
            < (separation / scale) * (separation / scale);
    }

    internal static IReadOnlyList<GridCell> Candidates(NavigationState navigation, SimVector2 ideal)
    {
        var grid = navigation.Grid;
        var rawX = Math.Floor((ideal.X - grid.Origin.X) / grid.CellSize);
        var rawY = Math.Floor((ideal.Y - grid.Origin.Y) / grid.CellSize);
        if (!double.IsFinite(rawX) || !double.IsFinite(rawY) || rawX < -AdjustmentCells
            || rawY < -AdjustmentCells || rawX >= (long)grid.Width + AdjustmentCells
            || rawY >= (long)grid.Height + AdjustmentCells) return [];
        var x = (int)rawX;
        var y = (int)rawY;
        var capacity = (int)((Math.Min(grid.Width - 1L, (long)x + AdjustmentCells) - Math.Max(0, x - AdjustmentCells) + 1)
            * (Math.Min(grid.Height - 1L, (long)y + AdjustmentCells) - Math.Max(0, y - AdjustmentCells) + 1));
        var candidates = new List<(GridCell Element, (double Distance, int Y, int X) Priority)>(capacity);
        for (var cy = Math.Max(0, y - AdjustmentCells); cy <= Math.Min(grid.Height - 1L, (long)y + AdjustmentCells); cy++)
        for (var cx = Math.Max(0, x - AdjustmentCells); cx <= Math.Min(grid.Width - 1L, (long)x + AdjustmentCells); cx++)
        {
            var cell = new GridCell(cx, cy);
            var point = navigation.CellCenter(cell);
            var dx = point.X - ideal.X;
            var dy = point.Y - ideal.Y;
            var distance = dx * dx + dy * dy;
            if (!double.IsFinite(distance)) throw new ArgumentException("Placement distance overflow.");
            candidates.Add((cell, (distance, cy, cx)));
        }
        return new PlacementCandidates(candidates);
    }
}
