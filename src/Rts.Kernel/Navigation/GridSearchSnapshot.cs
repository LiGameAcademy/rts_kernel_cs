namespace Rts.Kernel.Navigation;

internal enum GridSearchPhase { Start, Goal, Footprint, Open, Neighbor, Trace, Emit, Complete }
internal readonly record struct SearchNode(int Index, long Cost, int Parent, bool Closed);
internal readonly record struct SearchEntry(int Index, long Cost, long Score);

/// <summary>All cursors that affect work scheduling; no delegates or derived caches are serialized.</summary>
internal record struct GridSearchCursor
{
    public GridSearchPhase Phase { get; set; }
    public int Current { get; set; }
    public int Neighbor { get; set; }
    public int Candidate { get; set; }
    public long CandidateCost { get; set; }
    public GridCell Probe { get; set; }
    public int ProbeX { get; set; }
    public int ProbeY { get; set; }
    public int ProbeMode { get; set; }
    public int ProbePart { get; set; }
    public int Trace { get; set; }
    public int Emit { get; set; }
    public int Expanded { get; set; }
    public PathStatus? Status { get; set; }
}

internal sealed record GridSearchSnapshot(string MapHash, GridCell Start, GridCell Goal, int Clearance,
    GridSearchCursor Cursor, IReadOnlyList<SearchNode> Nodes, IReadOnlyList<SearchEntry> Open,
    IReadOnlyList<GridCell> ReversePath, IReadOnlyList<GridCell> Path);
