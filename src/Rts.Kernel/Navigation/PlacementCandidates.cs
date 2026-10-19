using System.Collections;

namespace Rts.Kernel.Navigation;

/// <summary>Merges sorted grid rows, retaining only the prefix actually visited by placement.</summary>
internal sealed class PlacementCandidates : IReadOnlyList<GridCell>
{
    private readonly NavigationState _navigation;
    private readonly FixedVector2 _ideal;
    private readonly int _left, _right, _top, _bottom;
    private int[]? _columns;
    private PriorityQueue<(int Y, int Column), (Int128 Distance, int Y, int X)>? _pending;
    private readonly List<GridCell> _ordered = [];
    public int Count { get; }

    internal PlacementCandidates(NavigationState navigation, FixedVector2 ideal, int left, int right, int top, int bottom)
    {
        _navigation = navigation;
        _ideal = ideal;
        _left = left;
        _right = right;
        _top = top;
        _bottom = bottom;
        Count = checked((right - left + 1) * (bottom - top + 1));
    }

    private GridCell FirstCandidate()
    {
        // Grid centers are uniformly spaced, including odd raw cell widths.
        // The nearest point on each axis gives the first distance/Y/X key;
        // ties choose the smaller index, just like the full merge.
        var firstCenter = _navigation.CellCenter(new(_left, _top));
        return new(NearestIndex(_ideal.X, firstCenter.X, _left, _right),
            NearestIndex(_ideal.Y, firstCenter.Y, _top, _bottom));
    }

    private int NearestIndex(SimScalar coordinate, SimScalar firstCenter, int first, int last)
    {
        var spacing = _navigation.Grid.CellSize;
        var lower = (int)Math.Clamp(first + FixedGridMath.FloorIndex(coordinate, firstCenter, spacing), first, last);
        var upper = Math.Min(lower + 1, last);
        var lowerCenter = firstCenter + (lower - first) * spacing;
        var upperCenter = firstCenter + (upper - first) * spacing;
        return Int128.Abs((Int128)coordinate.Raw - lowerCenter.Raw)
            <= Int128.Abs((Int128)coordinate.Raw - upperCenter.Raw) ? lower : upper;
    }

    private void InitializeFrontier()
    {
        // Every row has the same horizontal ordering. Sort at most 17 columns once,
        // then merge rows by the original full distance/Y/X key without materializing the rectangle.
        _columns = Enumerable.Range(_left, _right - _left + 1)
            .OrderBy(x => SquaredDistance.Between(new(_navigation.CellCenter(new(x, _top)).X, _ideal.Y), _ideal).Raw)
            .ThenBy(x => x).ToArray();
        _pending = new(_bottom - _top + 1);
        for (var y = _top; y <= _bottom; y++) Enqueue(y, 0);
        // FirstCandidate is already cached. Consume it exactly once from the
        // original merge so every later access continues the same sequence.
        TakeNext();
    }

    public GridCell this[int index]
    {
        get
        {
            if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
            if (_ordered.Count == 0) _ordered.Add(FirstCandidate());
            if (index > 0 && _pending is null) InitializeFrontier();
            while (_ordered.Count <= index)
                _ordered.Add(TakeNext());
            return _ordered[index];
        }
    }

    private GridCell TakeNext()
    {
        var (y, column) = _pending!.Dequeue();
        var cell = new GridCell(_columns![column], y);
        if (column + 1 < _columns.Length) Enqueue(y, column + 1);
        return cell;
    }

    private void Enqueue(int y, int column)
    {
        var x = _columns![column];
        var distance = SquaredDistance.Between(_navigation.CellCenter(new(x, y)), _ideal).Raw;
        _pending!.Enqueue((y, column), (distance, y, x));
    }

    public IEnumerator<GridCell> GetEnumerator()
    {
        for (var i = 0; i < Count; i++) yield return this[i];
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
