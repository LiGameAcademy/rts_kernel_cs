namespace NumericPrototype.Movement;

internal static class IntegerPathfinder
{
    // Same octile constant as the current 1.4142135 metric, represented as an exact integer ratio.
    private const long Straight = 10000000;
    private const long Diagonal = 14142135;
    internal static FixedPoint[] Find(MovementWorld world, FixedPoint start, FixedPoint goal, long radius = 0)
    {
        var a = world.Cell(start);
        var b = world.Cell(goal);
        if (radius < 0 || radius > 4 * world.CellSize) throw new ArgumentOutOfRangeException(nameof(radius));
        var numerator = Math.Max(0, 2 * radius - world.CellSize);
        var clearance = checked((int)((numerator + 2 * world.CellSize - 1) / (2 * world.CellSize)));
        bool CanOccupy(int x, int y)
        {
            for (var cy = y - clearance; cy <= y + clearance; cy++)
            for (var cx = x - clearance; cx <= x + clearance; cx++)
                if (!world.Walkable(cx, cy)) return false;
            return true;
        }
        if (!CanOccupy(a.X, a.Y) || !CanOccupy(b.X, b.Y)) return [];
        var count = checked(world.Width * world.Height);
        var costs = new long?[count]; // No numeric Infinity sentinel.
        var parents = Enumerable.Repeat(-1, count).ToArray();
        var closed = new bool[count];
        var startIndex = a.Y * world.Width + a.X;
        var goalIndex = b.Y * world.Width + b.X;
        long Heuristic(int x, int y)
        {
            var dx = Math.Abs(x - b.X);
            var dy = Math.Abs(y - b.Y);
            return checked(Math.Max(dx, dy) * Straight + Math.Min(dx, dy) * (Diagonal - Straight));
        }
        var open = new PriorityQueue<(int Index, long Cost), (long Score, int Index)>();
        costs[startIndex] = 0;
        open.Enqueue((startIndex, 0), (Heuristic(a.X, a.Y), startIndex));
        while (open.TryDequeue(out var entry, out _))
        {
            var i = entry.Index;
            if (closed[i] || costs[i] != entry.Cost) continue;
            closed[i] = true;
            if (i == goalIndex)
            {
                var path = new List<FixedPoint>();
                for (var index = i; index != startIndex; index = parents[index])
                    path.Add(world.Center(index % world.Width, index / world.Width));
                path.Reverse();
                if (path.Count == 0) path.Add(goal);
                else path[^1] = goal;
                return path.ToArray();
            }
            var x = i % world.Width;
            var y = i / world.Width;
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0 || !CanOccupy(x + dx, y + dy)) continue;
                var diagonal = dx != 0 && dy != 0;
                if (diagonal && (!CanOccupy(x + dx, y) || !CanOccupy(x, y + dy))) continue;
                var next = (y + dy) * world.Width + x + dx;
                var candidate = checked(entry.Cost + (diagonal ? Diagonal : Straight));
                if (closed[next] || costs[next] is { } previous && candidate >= previous) continue;
                costs[next] = candidate;
                parents[next] = i;
                open.Enqueue((next, candidate), (checked(candidate + Heuristic(x + dx, y + dy)), next));
            }
        }
        return [];
    }
}
