namespace Rts.Kernel.Numerics;

/// <summary>State coordinates/velocity components; distances use a separately scaled wide intermediate.</summary>
public readonly record struct FixedVector2(SimScalar X, SimScalar Y)
{
    public static FixedVector2 Zero => default;
    public static FixedVector2 FromRaw(long x, long y) => new(SimScalar.FromRaw(x), SimScalar.FromRaw(y));
    public override string ToString() => $"({X}, {Y})";
    public SimScalar Length => SquaredDistance.Between(Zero, this).Length;
    public static FixedVector2 operator +(FixedVector2 a, FixedVector2 b) => new(a.X + b.X, a.Y + b.Y);
    public static FixedVector2 operator -(FixedVector2 a, FixedVector2 b) => new(a.X - b.X, a.Y - b.Y);
    public static FixedVector2 operator *(FixedVector2 value, SimScalar scale) => new(value.X * scale, value.Y * scale);
    public static FixedVector2 operator /(FixedVector2 value, SimScalar scale) => new(value.X / scale, value.Y / scale);
}
