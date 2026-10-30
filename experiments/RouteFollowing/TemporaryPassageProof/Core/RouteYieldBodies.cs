namespace Rts.Movement.Prototype;

/// <summary>Conservative point buckets for a single immutable cooperation observation.</summary>
internal sealed class RouteYieldBodies
{
    private const int CellSize = 128;
    private readonly Dictionary<(int X, int Y), List<Walker>> _cells = [];

    internal RouteYieldBodies(IEnumerable<Walker> bodies)
    {
        foreach (var body in bodies)
        {
            var key = (Cell(body.Position.X), Cell(body.Position.Y));
            if (!_cells.TryGetValue(key, out var found)) _cells.Add(key, found = []);
            found.Add(body);
        }
    }

    internal IEnumerable<Walker> Near(FixedVector2 from, FixedVector2 to)
    {
        // Goal spacing is64, larger than the62 body-contact diameter. Inclusive
        // buckets keep touching boundaries for the caller's original exact test.
        var left = Cell(SimMath.Min(from.X, to.X) - 64);
        var right = Cell(SimMath.Max(from.X, to.X) + 64);
        var top = Cell(SimMath.Min(from.Y, to.Y) - 64);
        var bottom = Cell(SimMath.Max(from.Y, to.Y) + 64);
        for (var y = top; y <= bottom; y++)
        for (var x = left; x <= right; x++)
            if (_cells.TryGetValue((x, y), out var found))
                foreach (var body in found) yield return body;
    }

    private static int Cell(SimScalar position) => checked((int)FixedGridMath.FloorIndex(position, SimScalar.Zero, CellSize));
}
