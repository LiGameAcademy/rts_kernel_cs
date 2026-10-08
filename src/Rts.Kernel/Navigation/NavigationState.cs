namespace Rts.Kernel.Navigation;

/// <summary>Half-open rectangular footprint in grid cells.</summary>
public readonly record struct GridArea(int X, int Y, int Width, int Height)
{
    internal bool IsValid => X >= 0 && Y >= 0 && Width > 0 && Height > 0;
}

public sealed record NavigationObstacle(ulong Id, GridArea Area);
public sealed record ObstacleCommand(ulong Id, GridArea? Area = null);
public sealed record NavigationSnapshot(string MapHash, IReadOnlyList<NavigationObstacle> Obstacles, string? HeightHash = null);

/// <summary>Mutable occupancy belongs to one match; static grid data may be shared.</summary>
internal sealed class NavigationState
{
    private readonly PathingGrid _grid;
    internal PathingGrid Grid => _grid;
    internal TerrainHeights? Terrain { get; }
    private readonly SortedDictionary<ulong, GridArea> _obstacles = [];
    private readonly int[] _occupancy;
    private readonly Dictionary<int, byte[]> _clearanceCache = [];

    internal NavigationState(PathingGrid grid, TerrainHeights? terrain = null)
    {
        _grid = grid;
        Terrain = terrain;
        var end = new SimVector2(grid.Origin.X + grid.Width * grid.CellSize,
            grid.Origin.Y + grid.Height * grid.CellSize);
        if (terrain is not null && (!terrain.TrySample(grid.Origin, out _) || !terrain.TrySample(end, out _)))
            throw new ArgumentException("Height field must cover the entire navigation grid.");
        _occupancy = new int[checked(grid.Width * grid.Height)];
    }

    internal PathResult FindPath(GridCell start, GridCell goal, int clearanceCells, int maxExpandedNodes) =>
        GridPathfinder.FindPathCore(_grid, start, goal, maxExpandedNodes, clearanceCells,
            IsWalkable);

    private bool IsWalkable(GridCell cell) => _grid.IsWalkable(cell)
        && _occupancy[cell.Y * _grid.Width + cell.X] == 0;

    internal bool TryWorldToCell(SimVector2 position, out GridCell cell) => _grid.TryWorldToCell(position, out cell);
    internal SimVector2 CellCenter(GridCell cell) => new(
        _grid.Origin.X + (cell.X + 0.5) * _grid.CellSize,
        _grid.Origin.Y + (cell.Y + 0.5) * _grid.CellSize);
    internal bool CanOccupy(GridCell cell, int clearance)
    {
        if (clearance < 0 || cell.X < 0 || cell.Y < 0 || cell.X >= _grid.Width || cell.Y >= _grid.Height
            || clearance >= Math.Max(_grid.Width, _grid.Height)) return false;
        if (!_clearanceCache.TryGetValue(clearance, out var cache))
        {
            if (_clearanceCache.Count == 8) _clearanceCache.Clear();
            cache = new byte[_occupancy.Length];
            _clearanceCache.Add(clearance, cache);
        }
        var index = cell.Y * _grid.Width + cell.X;
        if (cache[index] == 0)
            cache[index] = GridPathfinder.HasClearance(_grid, cell, clearance, IsWalkable) ? (byte)1 : (byte)2;
        return cache[index] == 1;
    }
    internal bool CanTraverse(GridCell from, GridCell to, int clearance) =>
        Math.Abs(from.X - to.X) <= 1 && Math.Abs(from.Y - to.Y) <= 1
        && CanOccupy(from, clearance) && CanOccupy(to, clearance)
        && (from.X == to.X || from.Y == to.Y ||
            (CanOccupy(new GridCell(from.X, to.Y), clearance) && CanOccupy(new GridCell(to.X, from.Y), clearance)));

    internal bool TrySetObstacle(ulong id, GridArea area)
    {
        // Validate the entire replacement before removing the previous footprint.
        if (id == 0 || !area.IsValid || (long)area.X + area.Width > _grid.Width
            || (long)area.Y + area.Height > _grid.Height) return false;
        if (_obstacles.TryGetValue(id, out var previous)) Apply(previous, -1);
        _obstacles[id] = area;
        _clearanceCache.Clear();
        Apply(area, 1);
        return true;
    }

    internal bool RemoveObstacle(ulong id)
    {
        if (!_obstacles.Remove(id, out var area)) return false;
        Apply(area, -1);
        _clearanceCache.Clear();
        return true;
    }

    internal NavigationSnapshot CaptureSnapshot() => new(_grid.ContentHash,
        Array.AsReadOnly(_obstacles.Select(pair => new NavigationObstacle(pair.Key, pair.Value)).ToArray()), Terrain?.ContentHash);

    internal static NavigationState Restore(PathingGrid grid, NavigationSnapshot snapshot, TerrainHeights? terrain = null)
    {
        ValidateSnapshot(snapshot);
        if (snapshot.MapHash != grid.ContentHash || snapshot.HeightHash != terrain?.ContentHash)
            throw new InvalidDataException("Navigation snapshot requires the identical static pathing grid.");
        var state = new NavigationState(grid, terrain);
        foreach (var obstacle in snapshot.Obstacles)
            if (!state.TrySetObstacle(obstacle.Id, obstacle.Area))
                throw new InvalidDataException("Obstacle footprint lies outside the static grid.");
        return state;
    }

    internal static void ValidateSnapshot(NavigationSnapshot snapshot)
    {
        if (snapshot.MapHash is null || snapshot.MapHash.Length != 64
            || snapshot.MapHash.Any(c => !((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
            || (snapshot.HeightHash is not null && (snapshot.HeightHash.Length != 64
                || snapshot.HeightHash.Any(c => !((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))))
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
