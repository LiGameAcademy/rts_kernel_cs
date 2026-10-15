namespace Rts.Kernel.Navigation;

/// <summary>
/// Sparse deterministic A* with bounded steps for footprint checks, queue pops, neighbors and reconstruction.
/// The caller owns map revision/cancellation. Advance never allocates or clears a map-sized array.
/// </summary>
internal sealed class GridSearchJob
{
    private readonly PathingGrid _grid;
    private readonly Func<GridCell, bool> _walkable;
    private readonly GridCell _start;
    private readonly GridCell _goal;
    private readonly int _clearance;
    private readonly Dictionary<int, SearchNode> _nodes = [];
    private readonly PriorityQueue<SearchEntry, (long Score, int Index)> _open = new();
    private readonly List<GridCell> _reverse = [];
    private readonly List<GridCell> _path = [];
    private GridSearchCursor _cursor;

    internal GridSearchJob(PathingGrid grid, GridCell start, GridCell goal, int clearance,
        Func<GridCell, bool> walkable)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(walkable);
        if (clearance < 0) throw new ArgumentOutOfRangeException(nameof(clearance));
        _grid = grid;
        _walkable = walkable;
        _start = start;
        _goal = goal;
        _clearance = clearance;
    }

    internal bool IsComplete => _cursor.Phase == GridSearchPhase.Complete;
    internal PathResult? Result => !IsComplete ? null : new(_cursor.Status!.Value,
        _path.AsReadOnly(), _cursor.Status == PathStatus.Found
            ? PathCost.FromUnits(_nodes[Index(_goal)].Cost) : null, _cursor.Expanded);

    internal int Advance(int budget)
    {
        if (budget <= 0) throw new ArgumentOutOfRangeException(nameof(budget));
        var spent = 0;
        while (spent < budget && !IsComplete)
        {
            spent++;
            switch (_cursor.Phase)
            {
                case GridSearchPhase.Start: BeginProbe(_start, 0, 0); break;
                case GridSearchPhase.Goal: BeginProbe(_goal, 1, 0); break;
                case GridSearchPhase.Footprint: AdvanceProbe(); break;
                case GridSearchPhase.Open: OpenNode(); break;
                case GridSearchPhase.Neighbor: AdvanceNeighbor(); break;
                case GridSearchPhase.Trace: TraceParent(); break;
                case GridSearchPhase.Emit: EmitCell(); break;
                default: throw new InvalidOperationException("Unknown path search phase.");
            }
        }
        return spent;
    }

    private void BeginProbe(GridCell cell, int mode, int part)
    {
        _cursor.ProbeMode = mode;
        _cursor.ProbePart = part;
        if ((long)cell.X - _clearance < 0 || (long)cell.Y - _clearance < 0
            || (long)cell.X + _clearance >= _grid.Width || (long)cell.Y + _clearance >= _grid.Height)
        {
            RejectProbe();
            return;
        }
        _cursor.Probe = cell;
        _cursor.ProbeX = cell.X - _clearance;
        _cursor.ProbeY = cell.Y - _clearance;
        _cursor.Phase = GridSearchPhase.Footprint;
    }

    private void AdvanceProbe()
    {
        if (!_walkable(new(_cursor.ProbeX, _cursor.ProbeY)))
        {
            RejectProbe();
            return;
        }
        if (_cursor.ProbeX < _cursor.Probe.X + _clearance) { _cursor.ProbeX++; return; }
        _cursor.ProbeX = _cursor.Probe.X - _clearance;
        if (_cursor.ProbeY < _cursor.Probe.Y + _clearance) { _cursor.ProbeY++; return; }
        if (_cursor.ProbeMode == 0) { _cursor.Phase = GridSearchPhase.Goal; return; }
        if (_cursor.ProbeMode == 1)
        {
            var index = Index(_start);
            _nodes.Add(index, new(index, 0, -1, false));
            Enqueue(index, 0);
            _cursor.Phase = GridSearchPhase.Open;
            return;
        }
        var from = Cell(_cursor.Current);
        var to = Cell(_cursor.Candidate);
        if (from.X != to.X && from.Y != to.Y && _cursor.ProbePart < 2)
        {
            var part = _cursor.ProbePart + 1;
            BeginProbe(part == 1 ? new(from.X, to.Y) : new(to.X, from.Y), 2, part);
            return;
        }
        if (!_nodes.TryGetValue(_cursor.Candidate, out var known)
            || (!known.Closed && _cursor.CandidateCost < known.Cost))
        {
            _nodes[_cursor.Candidate] = new(_cursor.Candidate, _cursor.CandidateCost, _cursor.Current, false);
            Enqueue(_cursor.Candidate, _cursor.CandidateCost);
        }
        _cursor.Phase = GridSearchPhase.Neighbor;
    }

    private void RejectProbe()
    {
        if (_cursor.ProbeMode < 2) Finish(PathStatus.InvalidEndpoint);
        else _cursor.Phase = GridSearchPhase.Neighbor;
    }

    private void OpenNode()
    {
        if (!_open.TryDequeue(out var entry, out _)) { Finish(PathStatus.Unreachable); return; }
        var node = _nodes[entry.Index];
        if (node.Closed || node.Cost != entry.Cost) return;
        _nodes[entry.Index] = node with { Closed = true };
        _cursor.Expanded++;
        _cursor.Current = entry.Index;
        if (entry.Index == Index(_goal))
        {
            _cursor.Trace = entry.Index;
            _cursor.Phase = GridSearchPhase.Trace;
        }
        else
        {
            _cursor.Neighbor = 0;
            _cursor.Phase = GridSearchPhase.Neighbor;
        }
    }

    private void AdvanceNeighbor()
    {
        if (_cursor.Neighbor == 9) { _cursor.Phase = GridSearchPhase.Open; return; }
        var ordinal = _cursor.Neighbor++;
        if (ordinal == 4) return;
        var from = Cell(_cursor.Current);
        var dx = ordinal % 3 - 1;
        var dy = ordinal / 3 - 1;
        var next = new GridCell(from.X + dx, from.Y + dy);
        if (next.X < 0 || next.Y < 0 || next.X >= _grid.Width || next.Y >= _grid.Height) return;
        _cursor.Candidate = Index(next);
        _cursor.CandidateCost = checked(_nodes[_cursor.Current].Cost
            + (dx != 0 && dy != 0 ? PathCost.DiagonalUnits : PathCost.UnitsPerStep));
        if (_nodes.TryGetValue(_cursor.Candidate, out var known)
            && (known.Closed || known.Cost <= _cursor.CandidateCost)) return;
        BeginProbe(next, 2, 0);
    }

    private void TraceParent()
    {
        _reverse.Add(Cell(_cursor.Trace));
        _cursor.Trace = _nodes[_cursor.Trace].Parent;
        if (_cursor.Trace >= 0) return;
        _cursor.Emit = _reverse.Count - 1;
        _cursor.Phase = GridSearchPhase.Emit;
    }

    private void EmitCell()
    {
        _path.Add(_reverse[_cursor.Emit--]);
        if (_cursor.Emit < 0) Finish(PathStatus.Found);
    }

    private void Finish(PathStatus status)
    {
        _cursor.Status = status;
        _cursor.Phase = GridSearchPhase.Complete;
    }

    private void Enqueue(int index, long cost)
    {
        var cell = Cell(index);
        var dx = Math.Abs(cell.X - _goal.X);
        var dy = Math.Abs(cell.Y - _goal.Y);
        var heuristic = checked(Math.Max(dx, dy) * PathCost.UnitsPerStep
            + (PathCost.DiagonalUnits - PathCost.UnitsPerStep) * Math.Min(dx, dy));
        var entry = new SearchEntry(index, cost, checked(cost + heuristic));
        _open.Enqueue(entry, (entry.Score, entry.Index));
    }

    internal GridSearchSnapshot Capture() => new(_grid.ContentHash, _start, _goal, _clearance, _cursor,
        Array.AsReadOnly(_nodes.Values.OrderBy(node => node.Index).ToArray()),
        Array.AsReadOnly(_open.UnorderedItems.Select(item => item.Element)
            .OrderBy(entry => entry.Score).ThenBy(entry => entry.Index).ThenBy(entry => entry.Cost).ToArray()),
        Array.AsReadOnly(_reverse.ToArray()), Array.AsReadOnly(_path.ToArray()));

    internal static GridSearchJob Restore(PathingGrid grid, GridSearchSnapshot saved, Func<GridCell, bool> walkable)
    {
        GridSearchValidation.Validate(grid, saved);
        var job = new GridSearchJob(grid, saved.Start, saved.Goal, saved.Clearance, walkable) { _cursor = saved.Cursor };
        foreach (var node in saved.Nodes) job._nodes.Add(node.Index, node);
        foreach (var entry in saved.Open) job._open.Enqueue(entry, (entry.Score, entry.Index));
        job._reverse.AddRange(saved.ReversePath);
        job._path.AddRange(saved.Path);
        return job;
    }

    private int Index(GridCell cell) => checked(cell.Y * _grid.Width + cell.X);
    private GridCell Cell(int index) => new(index % _grid.Width, index / _grid.Width);
}
