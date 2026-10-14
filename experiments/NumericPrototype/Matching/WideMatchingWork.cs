using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NumericPrototype.Matching;

internal sealed record MatchingSnapshot(int Format, string Identity, int Batches, int Row,
    int Column, bool Searching, string[] U, string[] V, int[] Occupant,
    int[] Previous, string?[] Minimum, bool[] Used);

/// <summary>Isolated Hungarian prototype. Costs and signed potentials have scale 2^64.</summary>
internal sealed class WideMatchingWork
{
    internal const int MaximumMembers = 500;
    internal static readonly Int128 MaximumCost =
        2 * (Int128)(16384L * PrototypeScalar.Scale) * (16384L * PrototypeScalar.Scale);
    private readonly Int128[] _costs;
    private readonly int _n;
    private readonly string _identity;
    private readonly Int128[] _u;
    private readonly Int128[] _v;
    private readonly int[] _occupant;
    private readonly int[] _previous;
    private readonly Int128?[] _minimum;
    private readonly bool[] _used;
    private int _row = 1;
    private int _column;
    private bool _searching;
    private int _batches;

    internal WideMatchingWork(int count, IReadOnlyList<Int128> costs)
    {
        if (count < 1 || count > MaximumMembers || costs.Count != count * count)
            throw new ArgumentException("Invalid matching dimensions.");
        if (costs.Any(cost => cost < 0 || cost > MaximumCost))
            throw new ArgumentOutOfRangeException(nameof(costs), "Experimental cost bound exceeded.");
        _n = count;
        _costs = costs.ToArray();
        _identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            "wide-hungarian-v1;" + count + ";" + string.Join(";", _costs.Select(Text)))));
        _u = new Int128[count + 1];
        _v = new Int128[count + 1];
        _occupant = new int[count + 1];
        _previous = new int[count + 1];
        _minimum = new Int128?[count + 1];
        _used = new bool[count + 1];
        for (var row = 1; row <= count; row++)
            _u[row] = _costs.Skip((row - 1) * count).Take(count).Min();
        for (var column = 1; column <= count; column++)
            _v[column] = Enumerable.Range(1, count)
                .Select(row => _costs[(row - 1) * count + column - 1] - _u[row]).Min();
        // Same deterministic zero-reduced-cost seed as the current production algorithm.
        for (var row = 1; row <= count; row++)
            for (var column = 1; column <= count; column++)
                if (_occupant[column] == 0 && Reduced(row, column) == 0)
                {
                    _occupant[column] = row;
                    break;
                }
    }

    internal bool Complete => _row > _n;
    private Int128 Reduced(int row, int column) =>
        checked(_costs[(row - 1) * _n + column - 1] - _u[row] - _v[column]);

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
                Array.Clear(_minimum);
                Array.Clear(_used);
                _searching = true;
            }
            _used[_column] = true;
            var activeRow = _occupant[_column];
            Int128? delta = null;
            var nextColumn = 0;
            for (var column = 1; column <= _n; column++)
            {
                if (_used[column]) continue;
                var reduced = Reduced(activeRow, column);
                if (_minimum[column] is null || reduced < _minimum[column])
                {
                    _minimum[column] = reduced;
                    _previous[column] = _column;
                }
                if (delta is null || _minimum[column] < delta)
                {
                    delta = _minimum[column];
                    nextColumn = column;
                }
            }
            if (delta is null || delta < 0) throw new InvalidOperationException("Invalid feasible dual.");
            for (var column = 0; column <= _n; column++)
            {
                if (_used[column])
                {
                    _u[_occupant[column]] = checked(_u[_occupant[column]] + delta.Value);
                    _v[column] = checked(_v[column] - delta.Value);
                }
                else if (_minimum[column] is Int128 minimum)
                    _minimum[column] = checked(minimum - delta.Value);
            }
            inspected = checked(inspected + _n);
            _batches++;
            _column = nextColumn;
            if (_occupant[_column] != 0) continue;
            do
            {
                var previous = _previous[_column];
                _occupant[_column] = _occupant[previous];
                _column = previous;
            } while (_column != 0);
            _row++;
            _searching = false;
        }
        return inspected;
    }

    internal int[] Result()
    {
        if (!Complete) throw new InvalidOperationException("Matching is pending.");
        var result = new int[_n];
        for (var column = 1; column <= _n; column++) result[_occupant[column] - 1] = column - 1;
        return result;
    }

    internal MatchingSnapshot Capture() => new(1, _identity, _batches, _row, _column, _searching,
        _u.Select(Text).ToArray(), _v.Select(Text).ToArray(), _occupant.ToArray(),
        _previous.ToArray(), _minimum.Select(value => value.HasValue ? Text(value.Value) : null).ToArray(),
        _used.ToArray());

    // Deliberately expensive reference restore: reconstruct reachable state before returning an owner.
    // This validates every field without inventing a second incomplete structural validator.
    internal static WideMatchingWork Restore(int count, IReadOnlyList<Int128> costs, MatchingSnapshot saved)
    {
        if (count < 1 || count > MaximumMembers) throw new InvalidDataException("Invalid matching dimensions.");
        if (saved is null || saved.Format != 1 || saved.Batches < 0 || saved.Batches > count * count)
            throw new InvalidDataException("Invalid experimental matching snapshot.");
        var candidate = new WideMatchingWork(count, costs);
        if (saved.Identity != candidate._identity) throw new InvalidDataException("Matching input differs.");
        while (candidate._batches < saved.Batches && !candidate.Complete) candidate.Advance(1);
        // Advance may skip seeded rows at zero cost, including the final completion transition.
        if (!saved.Searching && saved.Row > candidate._row) candidate.Advance(1);
        if (JsonSerializer.Serialize(candidate.Capture()) != JsonSerializer.Serialize(saved))
            throw new InvalidDataException("Matching snapshot is not reachable under this contract.");
        return candidate;
    }

    private static string Text(Int128 value) => value.ToString(CultureInfo.InvariantCulture);
}
