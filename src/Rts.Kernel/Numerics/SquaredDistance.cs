namespace Rts.Kernel.Numerics;

/// <summary>Nonnegative square/cumulative cost at scale 2^64; never use this type for signed matching potentials.</summary>
public readonly record struct SquaredDistance : IComparable<SquaredDistance>
{
    public Int128 Raw { get; }
    private SquaredDistance(Int128 raw) => Raw = raw;
    public static SquaredDistance FromRaw(Int128 raw) => raw < 0
        ? throw new ArgumentOutOfRangeException(nameof(raw)) : new(raw);
    public static SquaredDistance Between(FixedVector2 a, FixedVector2 b)
    {
        var dx = (Int128)a.X.Raw - b.X.Raw;
        var dy = (Int128)a.Y.Raw - b.Y.Raw;
        return new(checked(dx * dx + dy * dy));
    }
    public SimScalar Length => SimScalar.FromRaw(checked((long)WideIntegerMath.FloorRoot((UInt128)Raw)));
    public override string ToString() => Raw.ToString(System.Globalization.CultureInfo.InvariantCulture) + " raw-squared";
    public int CompareTo(SquaredDistance other) => Raw.CompareTo(other.Raw);
    public static SquaredDistance operator +(SquaredDistance a, SquaredDistance b) => new(checked(a.Raw + b.Raw));

}
