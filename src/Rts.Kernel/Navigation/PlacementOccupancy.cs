namespace Rts.Kernel.Navigation;

/// <summary>Derived per-placement body index. Exact overlap geometry remains authoritative.</summary>
internal sealed class PlacementOccupancy(NavigationState navigation, double maximumRadius)
{
    private readonly int _bucketCells = (int)Math.Min(int.MaxValue,
        Math.Max(1, Math.Ceiling(maximumRadius * 2 / navigation.Grid.CellSize)));
    private readonly Dictionary<(int X, int Y), List<PlacementBody>> _buckets = [];
    private readonly List<PlacementBody> _outside = [];

    internal void Add(PlacementBody body)
    {
        if (!navigation.TryWorldToCell(body.Position, out var cell)) { _outside.Add(body); return; }
        var key = (cell.X / _bucketCells, cell.Y / _bucketCells);
        if (!_buckets.TryGetValue(key, out var bucket)) _buckets.Add(key, bucket = []);
        bucket.Add(body);
    }

    internal bool Overlaps(SimVector2 position, double radius)
    {
        if (_outside.Any(body => GroupPlacementPlanner.Overlaps(position, radius, body))) return true;
        navigation.TryWorldToCell(position, out var cell); // Placement candidates are inside the grid.
        var x = cell.X / _bucketCells; var y = cell.Y / _bucketCells;
        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
            if (_buckets.TryGetValue((x + dx, y + dy), out var bucket))
                foreach (var body in bucket)
                    if (GroupPlacementPlanner.Overlaps(position, radius, body)) return true;
        return false;
    }
}
