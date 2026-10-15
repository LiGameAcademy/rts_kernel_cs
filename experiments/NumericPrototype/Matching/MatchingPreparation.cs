using NumericPrototype.Movement;

namespace NumericPrototype.Matching;

internal sealed record MatchingPreparationSnapshot(int Format, int Steps);
internal enum MatchingPreparationStage { Costs, Identity, RowMinimum, ColumnMinimum, Seed, Complete }

/// <summary>Owns bounded preparation. Inputs and costs are borrowed from its frozen group owner.</summary>
internal sealed class MatchingPreparation : IDisposable
{
    private readonly FixedPoint[] _starts;
    private readonly FixedPoint[] _goals;
    private readonly Int128[] _costs;
    private readonly Int128[] _u;
    private readonly Int128[] _v;
    private readonly int[] _occupant;
    private readonly MatchingIdentityStream _hash;
    private readonly int _n;
    private int _offset;
    private int _steps;
    private string? _identity;
    internal MatchingPreparationStage Stage { get; private set; }
    internal bool Complete => Stage == MatchingPreparationStage.Complete;

    internal MatchingPreparation(FixedPoint[] starts, FixedPoint[] goals, Int128[] costs)
    {
        if (starts.Length is < 1 or > 500 || starts.Length != goals.Length || costs.Length != starts.Length * starts.Length)
            throw new ArgumentException("Invalid preparation dimensions.");
        _starts = starts;
        _goals = goals;
        _costs = costs;
        _n = starts.Length;
        _u = new Int128[_n + 1];
        _v = new Int128[_n + 1];
        _occupant = new int[_n + 1];
        _hash = new(_n);
    }

    // Decimal encoding is charged eight units per value. Other cell inspections cost one.
    // These are deterministic work weights, never elapsed-time decisions.
    internal int Advance(int budget)
    {
        if (budget < 8) throw new ArgumentOutOfRangeException(nameof(budget));
        var spent = 0;
        while (!Complete)
        {
            var charge = Stage == MatchingPreparationStage.Identity ? 8 : 1;
            if (budget - spent < charge) break;
            ProcessOne();
            spent += charge;
        }
        return spent;
    }

    private void ProcessOne()
    {
        var row = _offset / _n;
        var column = _offset % _n;
        switch (Stage)
        {
            case MatchingPreparationStage.Costs:
                var start = _starts[row];
                var goal = _goals[column];
                var cost = WideSquaredDistance.Between(new(start.X), new(start.Y), new(goal.X), new(goal.Y)).Raw;
                if (cost > WideMatchingWork.MaximumFormationCost)
                    throw new ArgumentOutOfRangeException(nameof(cost), "Formation cost bound exceeded.");
                _costs[_offset] = cost;
                break;
            case MatchingPreparationStage.Identity:
                _hash.Append(_costs[_offset], _offset);
                break;
            case MatchingPreparationStage.RowMinimum:
                if (column == 0 || _costs[_offset] < _u[row + 1]) _u[row + 1] = _costs[_offset];
                break;
            case MatchingPreparationStage.ColumnMinimum:
                var reduced = checked(_costs[_offset] - _u[row + 1]);
                if (row == 0 || reduced < _v[column + 1]) _v[column + 1] = reduced;
                break;
            case MatchingPreparationStage.Seed:
                if (_occupant[column + 1] == 0 && checked(_costs[_offset] - _u[row + 1] - _v[column + 1]) == 0)
                {
                    _occupant[column + 1] = row + 1;
                    _offset = (row + 1) * _n - 1;
                }
                break;
            default:
                throw new InvalidOperationException("Preparation is already complete.");
        }
        _steps++;
        _offset++;
        if (_offset < _costs.Length) return;
        if (Stage == MatchingPreparationStage.Identity)
        {
            _identity = _hash.Finish();
            _hash.Dispose();
        }
        Stage++;
        _offset = 0;
    }

    internal WideMatchingWork CreateWork()
    {
        if (!Complete) throw new InvalidOperationException("Matching preparation is pending.");
        // Costs remain read-only in both owners; mutable dual/seed arrays are copied by the matcher.
        return new WideMatchingWork(_n, _costs, _identity ?? throw new InvalidOperationException("Missing prepared identity."), _u, _v, _occupant);
    }

    internal MatchingPreparationSnapshot Capture() => new(1, _steps);

    // Reference restoration intentionally replays derived preparation outside the measured tick.
    internal void Restore(MatchingPreparationSnapshot saved)
    {
        if (saved.Format != 1 || saved.Steps < 0 || saved.Steps > 5 * _n * _n || _steps != 0)
            throw new InvalidDataException("Invalid matching preparation snapshot.");
        while (_steps < saved.Steps && !Complete) ProcessOne();
        if (_steps != saved.Steps) throw new InvalidDataException("Preparation cursor exceeds reachable work.");
    }

    public void Dispose() => _hash.Dispose();
}
