using System.Text.Json;
using Rts.Kernel.Navigation;

internal static class NavigationCacheChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var grid = new PathingGrid(9, 9, 32, default, new byte[81]);
        var live = new NavigationState(grid);
        var other = new NavigationState(grid);
        var from = new GridCell(4, 1);
        var to = new GridCell(4, 7);
        var wall = new GridArea(0, 4, 9, 1);
        check(live.Components.Connected(from, to, 0), "warm connectivity is open");
        check(live.TrySetObstacle(1, wall), "add wall");
        check(!live.Components.Connected(from, to, 0), "wall invalidates warm connectivity");
        CompareRestored(live, grid, check);
        check(other.Components.Connected(from, to, 0), "cache belongs to one navigation state");
        check(!live.TrySetObstacle(1, new GridArea(0, 4, 10, 1)), "invalid replacement rejected");
        check(!live.Components.Connected(from, to, 0), "invalid replacement preserves blocked state");
        check(live.TrySetObstacle(2, wall), "overlapping wall");
        check(live.RemoveObstacle(1), "remove one overlapping obstacle");
        check(!live.Components.Connected(from, to, 0), "remaining occupancy still blocks");
        CompareRestored(live, grid, check);
        check(live.TrySetObstacle(2, new GridArea(0, 4, 3, 1)), "replace wall with open passage");
        check(live.Components.Connected(from, to, 0), "replacement invalidates disconnected labels");
        CompareRestored(live, grid, check);
        check(live.TrySetObstacle(3, new GridArea(5, 4, 4, 1)), "narrow passage");
        check(live.Components.Connected(from, to, 0), "point can cross two-cell gap");
        check(!live.Components.Connected(from, to, 1), "clearance-specific labels stay separate");
        CompareRestored(live, grid, check);
        check(live.RemoveObstacle(2) && live.RemoveObstacle(3), "remove walls");
        check(live.Components.Connected(from, to, 1), "removal opens warmed clearance map");
        for (var clearance = 0; clearance < 10; clearance++)
            live.Components.Connected(from, to, clearance);
        check(live.Components.Connected(from, to, 0), "bounded cache eviction preserves query result");
        CompareRestored(live, grid, check);
    }

    private static void CompareRestored(NavigationState live, PathingGrid grid, Action<bool, string> check)
    {
        var json = JsonSerializer.Serialize(live.CaptureSnapshot());
        var restored = NavigationState.Restore(grid, JsonSerializer.Deserialize<NavigationSnapshot>(json)!);
        for (var clearance = 0; clearance <= 2; clearance++)
        for (var a = 0; a < 81; a++)
        for (var b = 0; b < 81; b++)
        {
            var from = new GridCell(a % 9, a / 9);
            var to = new GridCell(b % 9, b / 9);
            check(live.Components.Connected(from, to, clearance) == restored.Components.Connected(from, to, clearance),
                "warm and JSON-restored cold connectivity agree for every endpoint pair");
        }
        check(json == JsonSerializer.Serialize(live.CaptureSnapshot()), "warming cache does not alter snapshot");
        live.Components.Clear();
        check(json == JsonSerializer.Serialize(live.CaptureSnapshot()), "evicting cache does not alter snapshot");
    }
}
