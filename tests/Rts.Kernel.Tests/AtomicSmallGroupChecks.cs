using System.Text.Json;
using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class AtomicSmallGroupChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var flags = new byte[128 * 128];
        for (var y = 0; y < 128; y++) flags[y * 128 + 64] = 2;
        var split = new PathingGrid(128, 128, 32, default, flags);
        var match = new RtsMatch(MatchConfig.Default, 7, split, GroupYieldChecks.Heights, [GroupYieldChecks.Footman]);
        for (var i = 0; i < 24; i++)
            match.SubmitCommand(CommandEnvelope.Spawn(1, 0, i,
                i == 0 ? new(1008, 2064) : new(2416 + i % 5 * 96, 1808 + i / 5 * 96), 1));
        match.Step();
        var ids = match.Entities.Select(entity => entity.Id).ToArray();
        check(ids.Length == 24, "split component fixture has all24 members");
        for (var i = 0; i < ids.Length; i++)
            match.SubmitCommand(CommandEnvelope.Stop(2, 0, 24 + i, ids[i]));
        match.Step(); match.DrainEvents();
        var before = match.CaptureSnapshot();
        match.SubmitCommand(CommandEnvelope.MoveGroup(3, 0, 48, new(ids, new(3200, 2200))));
        match.Step();
        var events = match.DrainEvents();
        check(events.Count(item => item.Kind == MatchEventKind.CommandRejected) == 24
            && !events.Any(item => item.Kind == MatchEventKind.GroupMoveAssigned),
            "one unreachable member rejects the complete small-group command");
        var after = match.CaptureSnapshot();
        check(JsonSerializer.Serialize(before.Orders) == JsonSerializer.Serialize(after.Orders)
            && JsonSerializer.Serialize(before.Moves) == JsonSerializer.Serialize(after.Moves),
            "rejected group preserves every existing order and path");
        check(before.Entities.SequenceEqual(after.Entities), "rejected group cannot move only its reachable subset");

        var treeFlags = new byte[128 * 128];
        for (var y = 42; y < 48; y++)
        for (var x = 85; x < 91; x++) treeFlags[y * 128 + x] = 2;
        var grid = new PathingGrid(128, 128, 32, default, treeFlags);
        var edge = new RtsMatch(MatchConfig.Default, 7, grid, GroupYieldChecks.Heights, [GroupYieldChecks.Footman]);
        for (var i = 0; i < 24; i++)
            edge.SubmitCommand(CommandEnvelope.Spawn(1, 0, i, new(1808 + i % 5 * 96, 1808 + i / 5 * 96), 1));
        edge.Step(); edge.DrainEvents();
        var selected = edge.Entities.Select(entity => entity.Id).ToArray();
        edge.SubmitCommand(CommandEnvelope.MoveGroup(2, 0, 24, new(selected, new(2736, 1456))));
        edge.Step();
        var outcomes = edge.DrainEvents();
        check(outcomes.Count(item => item.Kind == MatchEventKind.GroupMoveAssigned) == 24
            && !outcomes.Any(item => item.Kind == MatchEventKind.CommandRejected),
            "tree-edge goal adjusts locally and accepts every member together");
        var navigation = new NavigationState(grid);
        check(edge.ReadMoveOrders().All(order => navigation.CanStandAt(order.Request.Goal, 31)),
            "every adjusted tree-edge goal fits the complete body");
    }
}
