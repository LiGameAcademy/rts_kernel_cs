namespace Rts.Kernel.Navigation;

/// <summary>Half-open rectangular footprint in grid cells.</summary>
public readonly record struct GridArea(int X, int Y, int Width, int Height)
{
    internal bool IsValid => X >= 0 && Y >= 0 && Width > 0 && Height > 0;
}

public sealed record NavigationObstacle(ulong Id, GridArea Area);
public sealed record ObstacleCommand(ulong Id, GridArea? Area = null);
public sealed record NavigationSnapshot(string MapHash, IReadOnlyList<NavigationObstacle> Obstacles);

/// <summary>Mutable occupancy belongs to one match; static grid data may be shared.</summary>
internal sealed class NavigationState
{
    private readonly PathingGrid _grid;
    private readonly SortedDictionary<ulong, GridArea> _obstacles = [];
    private readonly int[] _occupancy;

    internal NavigationState(PathingGrid grid)
    {
        _grid = grid;
        _occupancy = new int[checked(grid.Width * grid.Height)];
    }

    internal PathResult FindPath(GridCell start, GridCell goal, int clearanceCells, int maxExpandedNodes) =>
        GridPathfinder.FindPathCore(_grid, start, goal, maxExpandedNodes, clearanceCells,
            cell => _grid.IsWalkable(cell) && _occupancy[cell.Y * _grid.Width + cell.X] == 0);

    internal bool TrySetObstacle(ulong id, GridArea area)
    {
        // Validate the entire replacement before removing the previous footprint.
        if (id == 0 || !area.IsValid || (long)area.X + area.Width > _grid.Width
            || (long)area.Y + area.Height > _grid.Height) return false;
        if (_obstacles.TryGetValue(id, out var previous)) Apply(previous, -1);
        _obstacles[id] = area;
        Apply(area, 1);
        return true;
    }

    internal bool RemoveObstacle(ulong id)
    {
        if (!_obstacles.Remove(id, out var area)) return false;
        Apply(area, -1);
        return true;
    }

    internal NavigationSnapshot CaptureSnapshot() => new(_grid.ContentHash,
        Array.AsReadOnly(_obstacles.Select(pair => new NavigationObstacle(pair.Key, pair.Value)).ToArray()));

    internal static NavigationState Restore(PathingGrid grid, NavigationSnapshot snapshot)
    {
        ValidateSnapshot(snapshot);
        if (snapshot.MapHash != grid.ContentHash)
            throw new InvalidDataException("Navigation snapshot requires the identical static pathing grid.");
        var state = new NavigationState(grid);
        foreach (var obstacle in snapshot.Obstacles)
            if (!state.TrySetObstacle(obstacle.Id, obstacle.Area))
                throw new InvalidDataException("Obstacle footprint lies outside the static grid.");
        return state;
    }

    internal static void ValidateSnapshot(NavigationSnapshot snapshot)
    {
        if (snapshot.MapHash is null || snapshot.MapHash.Length != 64
            || snapshot.MapHash.Any(c => !((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
            || snapshot.Obstacles is null)
            throw new InvalidDataException("Invalid navigation snapshot identity or collection.");
        ulong previousId = 0;
        foreach (var obstacle in snapshot.Obstacles)
        {
            if (obstacle is null || obstacle.Id <= previousId || !obstacle.Area.IsValid)
                throw new InvalidDataException("Obstacle snapshots must have valid footprints and ascending unique IDs.");
            previousId = obstacle.Id;
        }
    }

    private void Apply(GridArea area, int delta)
    {
        for (var y = area.Y; y < area.Y + area.Height; y++)
        for (var x = area.X; x < area.X + area.Width; x++)
            _occupancy[y * _grid.Width + x] += delta;
    }
}
