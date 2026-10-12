namespace Rts.Kernel.Navigation;

public enum PathStatus { Found, InvalidEndpoint, Unreachable, NodeLimitReached }

/// <summary>Cell path includes both endpoints. Cost uses the existing game's Octile metric.</summary>
public sealed record PathResult(PathStatus Status, IReadOnlyList<GridCell> Cells, double Cost, int ExpandedNodes);

/// <summary>Synchronous static ground path query. Search state belongs to each call.</summary>
public static class GridPathfinder
{
    private const double DiagonalCost = 1.4142135;

    public static PathResult FindPath(PathingGrid grid, GridCell start, GridCell goal,
        int maxExpandedNodes = int.MaxValue, int clearanceCells = 0)
    {
        ArgumentNullException.ThrowIfNull(grid);
        return FindPathCore(grid, start, goal, maxExpandedNodes, clearanceCells, grid.IsWalkable);
    }

    internal static PathResult FindPathCore(PathingGrid grid, GridCell start, GridCell goal,
        int maxExpandedNodes, int clearanceCells, Func<GridCell, bool> isWalkable,
        Func<GridCell, GridCell, bool>? canTraverse = null, GridArea? searchArea = null)
    {
        if (clearanceCells < 0) throw new ArgumentOutOfRangeException(nameof(clearanceCells));
        var area = searchArea ?? new GridArea(0, 0, grid.Width, grid.Height);
        if (!area.IsValid || (long)area.X + area.Width > grid.Width || (long)area.Y + area.Height > grid.Height)
            throw new ArgumentException("Invalid search area.");
        var count = checked(area.Width * area.Height);
        var occupancyCache = new byte[count];
        bool CanOccupy(GridCell cell)
        {
            if (cell.X < area.X || cell.Y < area.Y || cell.X >= (long)area.X + area.Width
                || cell.Y >= (long)area.Y + area.Height) return false;
            var index = (cell.Y - area.Y) * area.Width + cell.X - area.X;
            if (occupancyCache[index] == 0)
                occupancyCache[index] = HasClearance(grid, cell, clearanceCells, isWalkable) ? (byte)1 : (byte)2;
            return occupancyCache[index] == 1;
        }
        if (maxExpandedNodes <= 0) throw new ArgumentOutOfRangeException(nameof(maxExpandedNodes));
        if (!CanOccupy(start) || !CanOccupy(goal))
            return Failure(PathStatus.InvalidEndpoint, 0);

        var costs = new double[count];
        Array.Fill(costs, double.PositiveInfinity);
        var parents = new int[count];
        Array.Fill(parents, -1);
        var closed = new bool[count];
        var startIndex = (start.Y - area.Y) * area.Width + start.X - area.X;
        var goalIndex = (goal.Y - area.Y) * area.Width + goal.X - area.X;
        // Index explicitly breaks equal-score ties; queue implementation order is irrelevant.
        var open = new PriorityQueue<(int Index, double Cost), (double Score, int Index)>();
        costs[startIndex] = 0;
        open.Enqueue((startIndex, 0), (Heuristic(start, goal), startIndex));
        var expanded = 0;
        while (open.TryDequeue(out var entry, out _))
        {
            var index = entry.Index;
            if (closed[index] || entry.Cost != costs[index]) continue;
            if (expanded == maxExpandedNodes) return Failure(PathStatus.NodeLimitReached, expanded);
            closed[index] = true;
            expanded++;
            if (index == goalIndex)
                return new PathResult(PathStatus.Found, Reconstruct(parents, index, area.Width, area.X, area.Y), costs[index], expanded);
            var cell = new GridCell(index % area.Width + area.X, index / area.Width + area.Y);
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var next = new GridCell(cell.X + dx, cell.Y + dy);
                if (!CanOccupy(next)) continue;
                var diagonal = dx != 0 && dy != 0;
                if (diagonal && canTraverse is null && (!CanOccupy(new GridCell(cell.X, next.Y))
                    || !CanOccupy(new GridCell(next.X, cell.Y)))) continue;
                if (canTraverse is not null && !canTraverse(cell, next)) continue;
                var nextIndex = (next.Y - area.Y) * area.Width + next.X - area.X;
                var candidate = costs[index] + (diagonal ? DiagonalCost : 1);
                if (closed[nextIndex] || candidate >= costs[nextIndex]) continue;
                costs[nextIndex] = candidate;
                parents[nextIndex] = index;
                open.Enqueue((nextIndex, candidate), (candidate + Heuristic(next, goal), nextIndex));
            }
        }
        return Failure(PathStatus.Unreachable, expanded);
    }

    internal static bool HasClearance(PathingGrid grid, GridCell cell, int clearance, Func<GridCell, bool> isWalkable)
    {
        if ((long)cell.X - clearance < 0 || (long)cell.Y - clearance < 0
            || (long)cell.X + clearance >= grid.Width || (long)cell.Y + clearance >= grid.Height) return false;
        for (var y = cell.Y - clearance; y <= cell.Y + clearance; y++)
        for (var x = cell.X - clearance; x <= cell.X + clearance; x++)
            if (!isWalkable(new GridCell(x, y))) return false;
        return true;
    }

    private static double Heuristic(GridCell from, GridCell to)
    {
        var dx = Math.Abs(from.X - to.X);
        var dy = Math.Abs(from.Y - to.Y);
        return Math.Max(dx, dy) + (DiagonalCost - 1) * Math.Min(dx, dy);
    }

    private static IReadOnlyList<GridCell> Reconstruct(int[] parents, int index, int width, int originX, int originY)
    {
        var cells = new List<GridCell>();
        while (index >= 0)
        {
            cells.Add(new GridCell(index % width + originX, index / width + originY));
            index = parents[index];
        }
        cells.Reverse();
        return cells.AsReadOnly();
    }

    private static PathResult Failure(PathStatus status, int expanded) =>
        new(status, Array.Empty<GridCell>(), double.PositiveInfinity, expanded);
}
