namespace Rts.Kernel.Navigation;

internal static class GroupPlacementPlanner
{
    internal const int AdjustmentCells = 8;

    internal static SimScalar CalculateSpacing(NavigationState navigation, IReadOnlyList<PlacementMember> members) =>
        SimMath.Max(navigation.Grid.CellSize,
            2 * members.Max(member => member.Definition.Radius) + navigation.Grid.CellSize / 10);

    internal static IReadOnlyList<PlacementGoal> Plan(NavigationState navigation, IReadOnlyList<PlacementMember> members,
        GroupMoveRequest request, SimScalar heading, IReadOnlyList<PlacementBody> externalBodies)
    {
        var spacing = CalculateSpacing(navigation, members);
        var ideals = FormationLayout.Create(request.Formation, members.Count, spacing, request.Goal, heading);
        var fallbackAnchor = members.MinBy(member => member.Id)!.Id;
        var anchorId = request.LeaderId.IsNone ? fallbackAnchor : request.LeaderId;
        var anchorIndex = members.ToList().FindIndex(member => member.Id == anchorId);
        if (anchorIndex < 0) anchorIndex = members.ToList().FindIndex(member => member.Id == fallbackAnchor);
        var slots = members.Count <= SmallGroupAssignment.MaximumMembers
            ? SmallGroupAssignment.Assign(members, ideals, heading, anchorIndex, !request.LeaderId.IsNone)
            : FormationLayout.Match(members.Select(member => member.Start).ToArray(), ideals, anchorIndex);
        return PlanAssigned(navigation, members, ideals, slots, externalBodies, preserveAnchor: !request.LeaderId.IsNone);
    }

    internal static IReadOnlyList<PlacementGoal> PlanAssigned(NavigationState navigation,
        IReadOnlyList<PlacementMember> members, IReadOnlyList<FixedVector2> ideals, IReadOnlyList<int> slots,
        IReadOnlyList<PlacementBody>? externalBodies = null, IReadOnlyList<IReadOnlyList<GridCell>>? preparedCandidates = null, bool preserveAnchor = true)
    {
        externalBodies ??= [];
        var reachable = navigation.Components;
        var candidates = preparedCandidates?.ToArray() ?? new IReadOnlyList<GridCell>?[ideals.Count];
        var failed = new HashSet<EntityId>();
        var result = new List<PlacementGoal>();
        // Failed members retain their old orders. Re-plan remaining placements against their unmoved bodies.
        while (true)
        {
            var before = failed.Count;
            var occupied = externalBodies.Concat(members.Where(member => failed.Contains(member.Id))
                .SelectMany(member => member.RetainedPositions.Select(position => new PlacementBody(position, member.Definition.Radius)))).ToList();
            var maximumRadius = SimMath.Max(members.Max(member => member.Definition.Radius),
                occupied.Count == 0 ? 0 : occupied.Max(body => body.Radius));
            var bodyIndex = new PlacementOccupancy(navigation, maximumRadius);
            foreach (var body in occupied) bodyIndex.Add(body);
            result.Clear();
            foreach (var index in Enumerable.Range(0, members.Count).OrderBy(i => slots[i]))
            {
                var member = members[index];
                var slot = slots[index];
                FixedVector2? goal = null;
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
            if (before == failed.Count)
            {
                if (members.Count <= 24 && failed.Count == 0
                    && members.All(member => member.Definition == members[0].Definition))
                {
                    var positions = result.Select(item => item.Member).ToArray();
                    var goals = result.Select(item => item.Goal!.Value).ToArray();
                    var assigned = SmallGroupAssignment.Assign(positions, goals, 0, 0, preserveAnchor);
                    var reordered = result.Select((item, index) => item with
                        { Slot = result[assigned[index]].Slot, Goal = goals[assigned[index]],
                            Adjusted = result[assigned[index]].Adjusted }).ToList();
                    if (reordered.All(item => navigation.TryWorldToCell(item.Member.Start, out var from)
                        && navigation.TryWorldToCell(item.Goal!.Value, out var to)
                        && reachable.Connected(from, to, item.Member.Clearance))) result = reordered;
                }
                return result.OrderBy(item => item.Member.Id).ToArray();
            }
        }
    }

    internal static bool Overlaps(FixedVector2 center, SimScalar radius, PlacementBody body)
    {
        var separation = radius + body.Radius;
        return SquaredDistance.Between(center, body.Position).Raw < (Int128)separation.Raw * separation.Raw;
    }

    internal static IReadOnlyList<GridCell> Candidates(NavigationState navigation, FixedVector2 ideal)
    {
        var grid = navigation.Grid;
        var rawX = FixedGridMath.FloorIndex(ideal.X, grid.Origin.X, grid.CellSize);
        var rawY = FixedGridMath.FloorIndex(ideal.Y, grid.Origin.Y, grid.CellSize);
        if (rawX < -AdjustmentCells
            || rawY < -AdjustmentCells || rawX >= (long)grid.Width + AdjustmentCells
            || rawY >= (long)grid.Height + AdjustmentCells) return [];
        var x = rawX;
        var y = rawY;
        var capacity = (int)((Math.Min(grid.Width - 1L, (long)x + AdjustmentCells) - Math.Max(0, x - AdjustmentCells) + 1)
            * (Math.Min(grid.Height - 1L, (long)y + AdjustmentCells) - Math.Max(0, y - AdjustmentCells) + 1));
        var candidates = new List<(GridCell Element, (Int128 Distance, int Y, int X) Priority)>(capacity);
        for (var cy = Math.Max(0, y - AdjustmentCells); cy <= Math.Min(grid.Height - 1L, (long)y + AdjustmentCells); cy++)
        for (var cx = Math.Max(0, x - AdjustmentCells); cx <= Math.Min(grid.Width - 1L, (long)x + AdjustmentCells); cx++)
        {
            var cell = new GridCell(checked((int)cx), checked((int)cy));
            var point = navigation.CellCenter(cell);
            var distance = SquaredDistance.Between(point, ideal).Raw;
            candidates.Add((cell, (distance, checked((int)cy), checked((int)cx))));
        }
        return new PlacementCandidates(candidates);
    }
}
