namespace Rts.Kernel.Navigation;

/// <summary>Reject corrupt search state at the persistence boundary, before executing work.</summary>
internal static class GridSearchValidation
{
    internal static void Validate(PathingGrid grid, GridSearchSnapshot saved)
    {
        if (saved is null || saved.MapHash != grid.ContentHash || saved.Clearance < 0
            || !Enum.IsDefined(saved.Cursor.Phase) || saved.Nodes is null || saved.Open is null
            || saved.ReversePath is null || saved.Path is null || saved.Cursor.Expanded < 0
            || saved.Cursor.Neighbor is < 0 or > 9 || saved.Cursor.ProbeMode is < 0 or > 2
            || saved.Cursor.ProbePart is < 0 or > 2 || saved.Cursor.CandidateCost < 0)
            throw new InvalidDataException("Invalid incremental path snapshot.");
        var nodes = ValidateNodes(grid, saved);
        ValidateFrontier(grid, saved, nodes);
        ValidateCursor(grid, saved, nodes);
        ValidateReconstruction(grid, saved, nodes);
    }

    private static Dictionary<int, SearchNode> ValidateNodes(PathingGrid grid, GridSearchSnapshot saved)
    {
        var count = checked(grid.Width * grid.Height);
        var nodes = new Dictionary<int, SearchNode>();
        var previous = -1;
        foreach (var node in saved.Nodes)
        {
            if (node.Index <= previous || node.Index >= count || node.Cost < 0
                || node.Parent < -1 || node.Parent >= count || node.Parent == node.Index)
                throw new InvalidDataException("Invalid or noncanonical search node.");
            nodes.Add(node.Index, node);
            previous = node.Index;
        }
        foreach (var node in saved.Nodes)
        {
            if (node.Parent == -1)
            {
                if (!grid.Contains(saved.Start) || node.Index != Index(grid, saved.Start) || node.Cost != 0)
                    throw new InvalidDataException("Only the start cell may be a search root.");
            }
            else if (!nodes.TryGetValue(node.Parent, out var parent) || !parent.Closed
                || !Adjacent(grid, node.Index, parent.Index)
                || parent.Cost > long.MaxValue - PathCost.DiagonalUnits
                || node.Cost != parent.Cost + EdgeCost(grid, node.Index, parent.Index))
                throw new InvalidDataException("Path parent and exact edge cost disagree.");
        }
        if (nodes.Values.Count(node => node.Closed) != saved.Cursor.Expanded)
            throw new InvalidDataException("Search expansion count disagrees with closed nodes.");
        return nodes;
    }

    private static void ValidateFrontier(PathingGrid grid, GridSearchSnapshot saved,
        IReadOnlyDictionary<int, SearchNode> nodes)
    {
        (long Score, int Index, long Cost)? previous = null;
        foreach (var entry in saved.Open)
        {
            if (!grid.Contains(saved.Goal) || !nodes.TryGetValue(entry.Index, out var node)
                || entry.Cost < node.Cost || entry.Cost > long.MaxValue - 31000000000000000L)
                throw new InvalidDataException("Invalid search frontier.");
            var dx = Math.Abs(entry.Index % grid.Width - saved.Goal.X);
            var dy = Math.Abs(entry.Index / grid.Width - saved.Goal.Y);
            var heuristic = Math.Max(dx, dy) * PathCost.UnitsPerStep
                + (PathCost.DiagonalUnits - PathCost.UnitsPerStep) * Math.Min(dx, dy);
            var key = (entry.Score, entry.Index, entry.Cost);
            if (entry.Score != entry.Cost + heuristic || (previous is { } last && key.CompareTo(last) <= 0))
                throw new InvalidDataException("Frontier must have canonical scores and order.");
            previous = key;
        }
    }

    private static void ValidateCursor(PathingGrid grid, GridSearchSnapshot saved,
        IReadOnlyDictionary<int, SearchNode> nodes)
    {
        var cursor = saved.Cursor;
        if ((cursor.Phase == GridSearchPhase.Complete) != (cursor.Status is not null)
            || (cursor.Status is not null && cursor.Status is not (PathStatus.Found or PathStatus.Unreachable or PathStatus.InvalidEndpoint))
            || (cursor.Phase == GridSearchPhase.Neighbor && !nodes.ContainsKey(cursor.Current))
            || (cursor.Phase == GridSearchPhase.Trace && !nodes.ContainsKey(cursor.Trace))
            || (cursor.Phase == GridSearchPhase.Emit && (cursor.Emit < 0 || cursor.Emit >= saved.ReversePath.Count)))
            throw new InvalidDataException("Search phase and cursors disagree.");
        if (cursor.Phase is GridSearchPhase.Start or GridSearchPhase.Goal || cursor.Status == PathStatus.InvalidEndpoint)
            if (nodes.Count != 0 || saved.Open.Count != 0 || saved.ReversePath.Count != 0 || saved.Path.Count != 0)
                throw new InvalidDataException("Search work exists before endpoint validation.");
        if (cursor.Phase != GridSearchPhase.Footprint) return;
        if (cursor.ProbeMode < 2 && (nodes.Count != 0 || cursor.ProbePart != 0))
            throw new InvalidDataException("Endpoint probe cannot contain active search work.");
        if (cursor.ProbeMode == 2)
        {
            if (!nodes.TryGetValue(cursor.Current, out var current) || !current.Closed
                || !Adjacent(grid, cursor.Current, cursor.Candidate)
                || current.Cost > long.MaxValue - PathCost.DiagonalUnits
                || cursor.CandidateCost != current.Cost + EdgeCost(grid, cursor.Current, cursor.Candidate))
                throw new InvalidDataException("Candidate probe disagrees with its search node.");
            var from = new GridCell(cursor.Current % grid.Width, cursor.Current / grid.Width);
            var to = new GridCell(cursor.Candidate % grid.Width, cursor.Candidate / grid.Width);
            var expected = cursor.ProbePart switch { 0 => to, 1 => new(from.X, to.Y), _ => new(to.X, from.Y) };
            if (cursor.Probe != expected || (cursor.ProbePart > 0 && (from.X == to.X || from.Y == to.Y)))
                throw new InvalidDataException("Footprint probe does not match its candidate or diagonal guard.");
        }
        if ((long)cursor.Probe.X - saved.Clearance < 0 || (long)cursor.Probe.Y - saved.Clearance < 0
            || (long)cursor.Probe.X + saved.Clearance >= grid.Width
            || (long)cursor.Probe.Y + saved.Clearance >= grid.Height
            || Math.Abs((long)cursor.ProbeX - cursor.Probe.X) > saved.Clearance
            || Math.Abs((long)cursor.ProbeY - cursor.Probe.Y) > saved.Clearance
            || (cursor.ProbeMode == 0 && cursor.Probe != saved.Start)
            || (cursor.ProbeMode == 1 && cursor.Probe != saved.Goal)
            || (cursor.ProbeMode == 2 && (!nodes.ContainsKey(cursor.Current)
                || cursor.Candidate < 0 || cursor.Candidate >= (long)grid.Width * grid.Height)))
            throw new InvalidDataException("Invalid in-progress footprint probe.");
    }

    private static void ValidateReconstruction(PathingGrid grid, GridSearchSnapshot saved,
        IReadOnlyDictionary<int, SearchNode> nodes)
    {
        foreach (var cell in saved.ReversePath.Concat(saved.Path))
            if (!grid.Contains(cell)) throw new InvalidDataException("Search reconstruction contains an invalid cell.");
        for (var i = 0; i < saved.ReversePath.Count; i++)
        {
            var cell = saved.ReversePath[i];
            if (!nodes.TryGetValue(Index(grid, cell), out var node) || !node.Closed
                || (i == 0 && cell != saved.Goal)
                || (i > 0 && nodes[Index(grid, saved.ReversePath[i - 1])].Parent != node.Index))
                throw new InvalidDataException("Invalid reverse parent chain.");
        }
        if (saved.Path.Count > saved.ReversePath.Count)
            throw new InvalidDataException("Emitted path exceeds reconstructed parents.");
        for (var i = 0; i < saved.Path.Count; i++)
            if (saved.Path[i] != saved.ReversePath[saved.ReversePath.Count - 1 - i])
                throw new InvalidDataException("Emitted path disagrees with parent chain.");
        var cursor = saved.Cursor;
        if (cursor.Phase is GridSearchPhase.Emit || cursor.Status == PathStatus.Found)
        {
            if (saved.ReversePath.Count == 0 || saved.ReversePath[^1] != saved.Start
                || saved.Path.Count + (cursor.Status == PathStatus.Found ? 0 : cursor.Emit + 1) != saved.ReversePath.Count)
                throw new InvalidDataException("Incomplete or inconsistent path emission.");
        }
        else if (saved.Path.Count > 0) throw new InvalidDataException("Path cells emitted before reconstruction.");
        if (cursor.Phase == GridSearchPhase.Trace && saved.ReversePath.Count > 0
            && nodes[Index(grid, saved.ReversePath[^1])].Parent != cursor.Trace)
            throw new InvalidDataException("Trace cursor disagrees with reconstructed tail.");
    }

    private static int Index(PathingGrid grid, GridCell cell) => checked(cell.Y * grid.Width + cell.X);
    private static bool Adjacent(PathingGrid grid, int a, int b) => a != b
        && Math.Abs(a % grid.Width - b % grid.Width) <= 1 && Math.Abs(a / grid.Width - b / grid.Width) <= 1;
    private static long EdgeCost(PathingGrid grid, int a, int b) => a % grid.Width != b % grid.Width
        && a / grid.Width != b / grid.Width ? PathCost.DiagonalUnits : PathCost.UnitsPerStep;
}
