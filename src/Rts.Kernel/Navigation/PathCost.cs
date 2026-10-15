namespace Rts.Kernel.Navigation;

/// <summary>Octile cell-path weight, not world distance: 10^7 units per orthogonal step.</summary>
public readonly record struct PathCost : IComparable<PathCost>
{
    public const long UnitsPerStep = 10000000;
    internal const long DiagonalUnits = 14142135;
    public long Units { get; }
    private PathCost(long units) => Units = units;
    public static PathCost FromUnits(long units) => units < 0
        ? throw new ArgumentOutOfRangeException(nameof(units)) : new(units);
    public int CompareTo(PathCost other) => Units.CompareTo(other.Units);
    public double ToPresentationDouble() => Units / (double)UnitsPerStep;
}
