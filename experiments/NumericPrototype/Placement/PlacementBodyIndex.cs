using NumericPrototype.Movement;

namespace NumericPrototype.Placement;

/// <summary>Per-pass broad phase. Exact overlap still uses the placement rule.</summary>
internal sealed class PlacementBodyIndex(MovementWorld world)
{
    private readonly Dictionary<(int X, int Y), List<PlacementBody>> _buckets = [];

    internal void Add(PlacementBody body)
    {
        var cell = world.Cell(body.Position);
        var key = (Bucket(cell.X), Bucket(cell.Y));
        if (!_buckets.TryGetValue(key, out var bodies))
        {
            bodies = [];
            _buckets.Add(key, bodies);
        }
        bodies.Add(body);
    }

    internal bool Overlaps(FixedPoint point, long radius)
    {
        var cell = world.Cell(point);
        // Both radii are validated <= four cells. Centers farther than eight
        // cells cannot overlap; the inclusive range also covers boundary rounding.
        for (var y = Bucket(cell.Y - 8); y <= Bucket(cell.Y + 8); y++)
        for (var x = Bucket(cell.X - 8); x <= Bucket(cell.X + 8); x++)
        {
            if (!_buckets.TryGetValue((x, y), out var bodies)) continue;
            foreach (var body in bodies)
                if (FixedPlacement.Overlaps(point, radius, body)) return true;
        }
        return false;
    }

    private static int Bucket(int cell) => cell >= 0 ? cell / 8 : (cell + 1) / 8 - 1;
}
