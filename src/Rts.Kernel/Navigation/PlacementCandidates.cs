using System.Collections;

namespace Rts.Kernel.Navigation;

/// <summary>Stable nearest-first candidates; only materialize the prefix that placement actually visits.</summary>
internal sealed class PlacementCandidates : IReadOnlyList<GridCell>
{
    private readonly PriorityQueue<GridCell, (double Distance, int Y, int X)> _pending;
    private readonly List<GridCell> _ordered = [];
    public int Count { get; }

    internal PlacementCandidates(List<(GridCell Element, (double Distance, int Y, int X) Priority)> entries)
    {
        Count = entries.Count;
        _pending = new(entries);
    }

    public GridCell this[int index]
    {
        get
        {
            if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
            while (_ordered.Count <= index) _ordered.Add(_pending.Dequeue());
            return _ordered[index];
        }
    }

    public IEnumerator<GridCell> GetEnumerator()
    {
        for (var i = 0; i < Count; i++) yield return this[i];
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
