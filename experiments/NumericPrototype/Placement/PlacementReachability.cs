using System.Diagnostics;
using NumericPrototype.Movement;

namespace NumericPrototype.Placement;

/// <summary>Derived immutable-world component labels; owned by the preview session, keyed by integer body clearance.</summary>
internal sealed class PlacementReachability(MovementWorld world)
{
    internal double BuildMs { get; private set; }
    private readonly Dictionary<int, int[]> _labels = [];
    internal bool Connected(FixedPoint from, FixedPoint to, long radius)
    {
        if (radius <= 0 || radius > 4 * world.CellSize) throw new ArgumentOutOfRangeException(nameof(radius));
        var clearance = checked((int)((Math.Max(0, 2 * radius - world.CellSize) + 2 * world.CellSize - 1)
            / (2 * world.CellSize)));
        var a = world.Cell(from);
        var b = world.Cell(to);
        if (!Inside(a.X, a.Y) || !Inside(b.X, b.Y)) return false;
        if (!_labels.TryGetValue(clearance, out var labels))
        {
            var watch = Stopwatch.StartNew();
            labels = Build(clearance);
            BuildMs += watch.Elapsed.TotalMilliseconds;
            _labels.Add(clearance, labels);
        }
        var component = labels[a.Y * world.Width + a.X];
        return component > 0 && component == labels[b.Y * world.Width + b.X];
    }

    private bool Inside(int x, int y) => x >= 0 && y >= 0 && x < world.Width && y < world.Height;
    private int[] Build(int clearance)
    {
        var labels = new int[checked(world.Width * world.Height)];
        for (var y = 0; y < world.Height; y++)
        for (var x = 0; x < world.Width; x++)
        {
            var valid = true;
            for (var cy = y - clearance; cy <= y + clearance && valid; cy++)
            for (var cx = x - clearance; cx <= x + clearance; cx++)
                if (!world.Walkable(cx, cy)) { valid = false; break; }
            if (!valid) labels[y * world.Width + x] = -1;
        }
        var pending = new Queue<(int X, int Y)>();
        var component = 0;
        for (var y = 0; y < world.Height; y++)
        for (var x = 0; x < world.Width; x++)
        {
            if (labels[y * world.Width + x] != 0) continue;
            labels[y * world.Width + x] = ++component;
            pending.Enqueue((x, y));
            while (pending.TryDequeue(out var cell))
                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    var nx = cell.X + dx;
                    var ny = cell.Y + dy;
                    if (dx == 0 && dy == 0 || !Inside(nx, ny) || labels[ny * world.Width + nx] != 0
                        || dx != 0 && dy != 0 && (labels[cell.Y * world.Width + nx] < 0
                            || labels[ny * world.Width + cell.X] < 0)) continue;
                    labels[ny * world.Width + nx] = component;
                    pending.Enqueue((nx, ny));
                }
        }
        return labels;
    }
}
