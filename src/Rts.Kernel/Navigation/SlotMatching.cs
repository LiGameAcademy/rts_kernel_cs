namespace Rts.Kernel.Navigation;

public sealed record SlotMatchingSnapshot(int Row, int Column, bool Searching,
    Int128[] U, Int128[] V, int[] Occupant, int[] Previous, Int128[] Minimum, bool[] Used);

/// <summary>Exact Hungarian assignment; each advance charges a fixed number of inspected columns.</summary>
internal sealed class SlotMatchingWork
{
    // Slot 0 belongs to the anchor. _members maps remaining rows back to entity input indices.
    // Costs use zero-based rows/columns and squared world distance.
    private readonly int[] _members;
    private readonly Int128[] _costs;
    private readonly int _n;
    private readonly int _count;
    private int _row = 1;
    private int _column;
    private bool _searching;
    // Hungarian arrays use indices 1..n; column 0 is the synthetic augmenting-path root.
    // u/v are row/column potentials, occupant maps columns to rows, previous links the search tree.
    private Int128[] _u;
    private Int128[] _v;
    private int[] _occupant;
    private int[] _previous;
    private Int128[] _minimum;
    private bool[] _used;

    internal SlotMatchingWork(IReadOnlyList<FixedVector2> positions, IReadOnlyList<FixedVector2> slots, int anchor)
    {
        _count = positions.Count;
        _members = Enumerable.Range(0, _count).Where(i => i != anchor).ToArray();
        _n = _members.Length;
        _costs = new Int128[_n * _n];
        foreach (var p in positions.Concat(slots))
            if (!SimulationDomain.Contains(p.X) || !SimulationDomain.Contains(p.Y)) throw new ArgumentException("Nonfinite match point.");
        _u = new Int128[_n + 1];
        _v = new Int128[_n + 1];
        _occupant = new int[_n + 1];
        _previous = new int[_n + 1];
        _minimum = new Int128[_n + 1];
        _used = new bool[_n + 1];
        for (var i = 0; i < _n; i++)
        {
            var minimum = Int128.MaxValue;
            for (var j = 0; j < _n; j++)
            {
                var cost = SquaredDistance.Between(positions[_members[i]], slots[j + 1]).Raw;
                _costs[i * _n + j] = cost;
                minimum = Int128.Min(minimum, cost);
            }
            _u[i + 1] = minimum;
        }
        for (var j = 0; j < _n; j++)
        {
            var minimum = Int128.MaxValue;
            for (var i = 0; i < _n; i++)
                minimum = Int128.Min(minimum, _costs[i * _n + j] - _u[i + 1]);
            _v[j + 1] = minimum;
        }
        // Seed exact zero-cost matches in row/column order; tie order is deterministic.
        for (var i = 1; i <= _n; i++)
        {
            for (var j = 1; j <= _n; j++)
            {
                if (_occupant[j] == 0 && _costs[(i - 1) * _n + j - 1] - _u[i] - _v[j] == 0)
                {
                    _occupant[j] = i;
                    break;
                }
            }
        }
    }

    internal bool Complete => _row > _n;
    internal int NextRow => _row;

    internal int Advance(int budget)
    {
        if (budget <= 0) throw new ArgumentOutOfRangeException(nameof(budget));
        var inspected = 0;
        while (!Complete && inspected < budget)
        {
            if (!_searching)
            {
                if (Array.IndexOf(_occupant, _row, 1) >= 0)
                {
                    _row++;
                    continue;
                }
                _occupant[0] = _row;
                _column = 0;
                Array.Fill(_minimum, Int128.MaxValue);
                Array.Clear(_used);
                _searching = true;
            }
            _used[_column] = true;
            var nextColumn = 0;
            var delta = Int128.MaxValue;
            var activeRow = _occupant[_column];
            var offset = (activeRow - 1) * _n;
            var potential = _u[activeRow];
            for (var j = 1; j <= _n; j++)
            {
                if (_used[j]) continue;
                var reduced = checked(_costs[offset + j - 1] - potential - _v[j]);
                if (reduced < _minimum[j])
                {
                    _minimum[j] = reduced;
                    _previous[j] = _column;
                }
                // Strict comparison keeps the first column when costs tie.
                if (_minimum[j] < delta)
                {
                    delta = _minimum[j];
                    nextColumn = j;
                }
            }
            if (delta == Int128.MaxValue) throw new InvalidOperationException("No augmenting column.");
            for (var j = 0; j <= _n; j++)
            {
                if (_used[j])
                {
                    _u[_occupant[j]] = checked(_u[_occupant[j]] + delta);
                    _v[j] = checked(_v[j] - delta);
                }
                else if (_minimum[j] != Int128.MaxValue) _minimum[j] = checked(_minimum[j] - delta);
            }
            // Charge the whole inspected column batch, even if it crosses the frame budget.
            // Resume only between batches: these cursor/potential fields are snapshot authority.
            inspected += _n;
            _column = nextColumn;
            if (_occupant[_column] != 0) continue;
            do
            {
                var next = _previous[_column];
                _occupant[_column] = _occupant[next];
                _column = next;
            } while (_column != 0);
            _row++;
            _searching = false;
        }
        return inspected;
    }

    internal int[] Result()
    {
        if (!Complete) throw new InvalidOperationException("Matching is still pending.");
        var result = new int[_count];
        for (var j = 1; j <= _n; j++) result[_members[_occupant[j] - 1]] = j;
        return result;
    }

    internal SlotMatchingSnapshot Capture() => new(_row, _column, _searching, _u.ToArray(), _v.ToArray(),
        _occupant.ToArray(), _previous.ToArray(), _minimum.ToArray(), _used.ToArray());

    internal void Restore(SlotMatchingSnapshot state)
    {
        Validate(state, _n);
        // Reconstruct from immutable inputs; do not trust forged dual values in a snapshot.
        var batches = 0;
        while ((_row != state.Row || _column != state.Column || _searching != state.Searching)
            && !Complete && _row <= state.Row && batches++ <= _n * _n + _n)
            Advance(1);
        var actual = Capture();
        if (actual.Row != state.Row || actual.Column != state.Column || actual.Searching != state.Searching
            || !actual.U.SequenceEqual(state.U) || !actual.V.SequenceEqual(state.V)
            || !actual.Minimum.SequenceEqual(state.Minimum) || !actual.Used.SequenceEqual(state.Used)
            || !actual.Occupant.SequenceEqual(state.Occupant) || !actual.Previous.SequenceEqual(state.Previous))
            throw new InvalidDataException("Matching snapshot does not match its immutable inputs.");
    }

    internal static void Validate(SlotMatchingSnapshot state, int n)
    {
        if (state is null || state.U is null || state.V is null || state.Occupant is null
            || state.Previous is null || state.Minimum is null || state.Used is null
            || new[] { state.U.Length, state.V.Length, state.Occupant.Length, state.Previous.Length,
                state.Minimum.Length, state.Used.Length }.Any(length => length != n + 1)
            || state.Row < 1 || state.Row > n + 1 || state.Column < 0 || state.Column > n
            || state.U.Concat(state.V).Any(value => value <= -(Int128.One << 120) || value >= (Int128.One << 120))
            || state.Minimum.Any(value => value < 0 || (value != Int128.MaxValue && value >= (Int128.One << 120)))
            || state.Occupant.Any(value => value < 0 || value > n)
            || state.Previous.Any(value => value < 0 || value > n)
            || state.Occupant.Skip(1).Where(value => value != 0).Distinct().Count()
                != state.Occupant.Skip(1).Count(value => value != 0)
            || (state.Searching && (state.Row > n || state.Occupant[state.Column] == 0))
            || (!state.Searching && state.Column != 0)
            || (state.Row == n + 1 && state.Occupant.Skip(1).Any(value => value == 0)))
            throw new InvalidDataException("Invalid incremental slot matching state.");
        if (state.Searching)
            for (var i = 1; i <= n; i++)
            {
                if (!state.Used[i]) continue;
                var current = i;
                var depth = 0;
                while (current != 0 && depth++ <= n)
                {
                    if (!state.Used[current]) throw new InvalidDataException("Matching predecessor is not in the search tree.");
                    current = state.Previous[current];
                }
                if (current != 0) throw new InvalidDataException("Matching predecessor cycle.");
            }
    }
}

internal static class SlotMatching
{
    internal static int[] Assign(IReadOnlyList<FixedVector2> positions, IReadOnlyList<FixedVector2> slots, int anchor)
    {
        var work = new SlotMatchingWork(positions, slots, anchor);
        while (!work.Complete) work.Advance(int.MaxValue);
        return work.Result();
    }
}
