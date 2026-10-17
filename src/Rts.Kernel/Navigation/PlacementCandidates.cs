using System.Collections;

namespace Rts.Kernel.Navigation;

/// <summary>Merges sorted grid rows, retaining only the prefix actually visited by placement.</summary>
internal sealed class PlacementCandidates : IReadOnlyList<GridCell>
{
    private readonly NavigationState _navigation;
    private readonly FixedVector2 _ideal;
    private readonly int[] _columns;
    private readonly PriorityQueue<(int Y, int Column), (Int128 Distance, int Y, int X)> _pending;
    private readonly List<GridCell> _ordered = [];
    public int Count { get; }

    internal PlacementCandidates(NavigationState navigation, FixedVector2 ideal, int left, int right, int top, int bottom)
    {
        _navigation = navigation;
        _ideal = ideal;
        // Every row has the same horizontal ordering. Sort at most 17 columns once,
        // then merge rows by the original full distance/Y/X key without materializing the rectangle.
        _columns = Enumerable.Range(left, right - left + 1)
            .OrderBy(x => SquaredDistance.Between(new(navigation.CellCenter(new(x, top)).X, ideal.Y), ideal).Raw)
            .ThenBy(x => x).ToArray();
        Count = checked(_columns.Length * (bottom - top + 1));
        _pending = new(bottom - top + 1);
        for (var y = top; y <= bottom; y++) Enqueue(y, 0);
    }

    public GridCell this[int index]
    {
        get
        {
            if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
            while (_ordered.Count <= index)
            {
                var (y, column) = _pending.Dequeue();
                _ordered.Add(new(_columns[column], y));
                if (column + 1 < _columns.Length) Enqueue(y, column + 1);
            }
            return _ordered[index];
        }
    }

    private void Enqueue(int y, int column)
    {
        var x = _columns[column];
        var distance = SquaredDistance.Between(_navigation.CellCenter(new(x, y)), _ideal).Raw;
        _pending.Enqueue((y, column), (distance, y, x));
    }

    public IEnumerator<GridCell> GetEnumerator()
    {
        for (var i = 0; i < Count; i++) yield return this[i];
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
