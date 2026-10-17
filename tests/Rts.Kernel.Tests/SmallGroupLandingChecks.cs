using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class SmallGroupLandingChecks
{
    internal static void Run(Action<bool, string> check)
    {
        SmallGroupAssignmentCostChecks.Run(check);
        var definition = new MovementDefinition(1, 30, 2);
        PlacementMember Member(ulong id, int x, int y) => new(new(id), new(x, y), definition, 0, []);
        var members = new[] { Member(1, 0, 1000), Member(2, 0, 0), Member(3, 2, 0) };
        var ideals = new FixedVector2[] { new(0, 1000), new(1, 0), new(100, 0) };
        var local = SmallGroupAssignment.Assign(members, ideals, 0, 0);
        check(local[0] == 0 && local[1] == 1 && local[2] == 2,
            "bounded pair exchange removes the avoidable crossing while preserving explicit anchor");
        var shuffled = new[] { members[2], members[0], members[1] };
        var shuffledResult = SmallGroupAssignment.Assign(shuffled, ideals, 0, 1);
        check(members.Select((member, i) => (member.Id, Slot: local[i])).OrderBy(pair => pair.Id)
            .SequenceEqual(shuffled.Select((member, i) => (member.Id, Slot: shuffledResult[i])).OrderBy(pair => pair.Id)),
            "selection order cannot change local slot ownership");
        var grid = new PathingGrid(48, 48, 10, default, new byte[2304]);
        var map = new NavigationState(grid);
        var starts = Enumerable.Range(0, 24).Select(i => Member((ulong)i + 1, 35 + i % 6 * 20, 35 + i / 6 * 20)).ToArray();
        foreach (var formation in Enum.GetValues<FormationKind>())
        {
            var request = new GroupMoveRequest(starts.Select(member => member.Id).ToArray(), new(325, 325), formation);
            var placed = GroupPlacementPlanner.Plan(map, starts, request, 0, [new(new(325, 325), 8)]);
            check(placed.Count == 24 && placed.All(item => item.Goal is not null), $"{formation}: all24 find locally valid goals");
            check(placed.Select(item => item.Slot).Distinct().Count() == 24, $"{formation}: local assignment remains bijective");
            check(placed.All(item => !GroupPlacementPlanner.Overlaps(item.Goal!.Value, definition.Radius, new(new(325, 325), 8))),
                $"{formation}: occupied anchor adjusts instead of demanding the exact shape");
            var safe = true;
            for (var i = 0; i < placed.Count; i++)
            for (var j = i + 1; j < placed.Count; j++)
                safe &= !GroupPlacementPlanner.Overlaps(placed[i].Goal!.Value, definition.Radius,
                    new(placed[j].Goal!.Value, definition.Radius));
            check(safe, $"{formation}: accepted goals retain body separation");
            var again = GroupPlacementPlanner.Plan(map, starts.Reverse().ToArray(), request, 0, [new(new(325, 325), 8)]);
            check(placed.SequenceEqual(again), $"{formation}: local adjustment is stable under input reversal");
        }
        var led = new GroupMoveRequest(starts.Select(member => member.Id).ToArray(), new(325, 325),
            LeaderId: starts[8].Id);
        var ledGoals = GroupPlacementPlanner.Plan(map, starts, led, 0, []);
        check(ledGoals.Single(item => item.Member.Id == starts[8].Id).Slot == 0,
            "explicit leader retains anchor through both assignment passes");
        var match = new RtsMatch(MatchConfig.Default, 97, grid, movementDefinitions: [definition]);
        foreach (var member in starts)
            match.SubmitCommand(CommandEnvelope.Spawn(1, 0, (long)member.Id.Value, member.Start, 1));
        match.Step(); match.DrainEvents();
        match.SubmitCommand(CommandEnvelope.MoveGroup(2, 0, 25,
            new(starts.Select(member => member.Id).ToArray(), new(325, 325))));
        match.Step();
        check(match.DrainEvents().Count(item => item.Kind == MatchEventKind.GroupMoveAssigned) == 24,
            "formal24-unit command uses local assignment and publishes every outcome");
        var resumed = RtsMatch.Restore(SnapshotJson.Deserialize(SnapshotJson.Serialize(match.CaptureSnapshot())), grid,
            movementDefinitions: [definition]);
        for (var i = 0; i < 8; i++)
        {
            match.Step(); resumed.Step();
            check(match.ComputeStateHash() == resumed.ComputeStateHash(), "local goals survive existing v11 recovery unchanged");
        }
    }
}
