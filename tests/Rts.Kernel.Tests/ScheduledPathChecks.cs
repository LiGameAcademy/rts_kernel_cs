using System.Text.Json;
using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class ScheduledPathChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var grid = new PathingGrid(80, 8, 1, default, new byte[640]);
        var navigation = new NavigationState(grid);
        var queue = new MovePlanningQueue();
        UnitOrderIntent Intent(FixedVector2 goal) => new(UnitOrderKind.Move, OrderSource.Player, new(goal, 4));
        queue.Enqueue(new(1), 0, 1, PathPurpose.Replace, OrderMode.Replace, Intent(new(79, 6)), new(1, 1));
        queue.Enqueue(new(2), 0, 1, PathPurpose.Replace, OrderMode.Replace, Intent(new(3, 1)), new(2, 1));
        var results = new List<PlannedMove>();
        for (var frame = 0; queue.Count > 0; frame++)
        {
            var advance = queue.Advance(navigation, 11);
            check(advance.Spent <= 11 && advance.Spent > 0, "whole path pipeline respects shared frame work");
            results.AddRange(advance.Completed);
            var json = JsonSerializer.Serialize(queue.Capture());
            queue = MovePlanningQueue.Restore(JsonSerializer.Deserialize<MovePlanningSnapshot>(json)!, navigation);
            check(json == JsonSerializer.Serialize(queue.Capture()), "round-robin cursor and conversion restore canonically");
            if (frame > 30000) throw new InvalidOperationException("Planning queue failed to drain.");
        }
        check(results[0].Request.EntityId == new EntityId(2), "short newer request finishes while old long request is still advancing");
        check(results.All(item => item.Order is not null && item.Order.Waypoints[^1] == item.Request.Intent.Move!.Goal),
            "world path conversion finishes at the exact requested goal");
        queue.Enqueue(new(3), 1, 2, PathPurpose.Replace, OrderMode.Replace, Intent(new(60, 4)), new(1, 1));
        queue.Advance(navigation, 11);
        queue.Cancel(new(3));
        check(queue.Advance(navigation, 100).Completed.Count == 0, "canceled search never returns an old result");
        queue.Enqueue(new(4), 1, 2, PathPurpose.Refresh, OrderMode.Replace, Intent(new(60, 4)), new(1, 1));
        queue.Advance(navigation, 9);
        navigation.TrySetObstacle(1, new(20, 0, 1, 8));
        queue.InvalidateMap();
        PlannedMove? blocked = null;
        while (queue.Count > 0)
        {
            var next = queue.Advance(navigation, 103);
            blocked = next.Completed.SingleOrDefault() ?? blocked;
        }
        check(blocked is { Order: null, Status: PathStatus.Unreachable }, "map invalidation restarts work instead of committing a stale path");
        var corrupt = queue.Capture() with { NextId = 0 };
        try { MovePlanningQueue.Restore(corrupt, navigation); check(false, "invalid scheduler identity rejected"); }
        catch (InvalidDataException) { check(true, "invalid scheduler identity rejected"); }
        check(queue.NextId == 5, "request identity survives canceled and restarted work");
    }
}
