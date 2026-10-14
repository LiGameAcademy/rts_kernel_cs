using FixPointCS;

namespace NumericPrototype.Movement;

internal readonly record struct FixedPoint(long X, long Y)
{
    internal static FixedPoint Parse(string x, string y) => new(InputQuantizer.Parse(x).Raw, InputQuantizer.Parse(y).Raw);
    public static FixedPoint operator +(FixedPoint a, FixedPoint b) => new(checked(a.X + b.X), checked(a.Y + b.Y));
    public static FixedPoint operator -(FixedPoint a, FixedPoint b) => new(checked(a.X - b.X), checked(a.Y - b.Y));
    internal FixedPoint ScaleRatio(long numerator, long denominator) =>
        new(checked((long)((Int128)X * numerator / denominator)), checked((long)((Int128)Y * numerator / denominator)));
    internal Int128 LengthSquared => checked((Int128)X * X + (Int128)Y * Y);
    internal long Length
    {
        get
        {
            var n = checked((UInt128)LengthSquared);
            UInt128 low = 0, high = ulong.MaxValue;
            while (low < high)
            {
                var middle = (low + high + 1) / 2;
                if (middle <= n / middle) low = middle;
                else high = middle - 1;
            }
            return checked((long)low);
        }
    }
}

internal static class FixedMath
{
    internal const long One = PrototypeScalar.Scale;
    private static readonly long MinimumTurnScale = InputQuantizer.Parse("0.12").Raw;
    private static readonly long UphillScale = InputQuantizer.Parse("0.6").Raw;
    private static readonly long DownhillScale = InputQuantizer.Parse("0.85").Raw;
    internal static long Mul(long a, long b) => checked((long)((Int128)a * b / One));
    internal static long Div(long a, long b) => b == 0 ? throw new DivideByZeroException()
        : checked((long)((Int128)a * One / b));
    internal static long Normalize(long angle)
    {
        angle %= Fixed64.Pi2;
        if (angle >= Fixed64.Pi) angle -= Fixed64.Pi2;
        // The pinned library rounds Pi and 2*Pi separately: an odd raw period needs one canonical interval.
        if (angle < -(Fixed64.Pi2 / 2)) angle += Fixed64.Pi2;
        return angle;
    }
    internal static long TurnScale(long facing, long desired)
    {
        var degrees = Mul(Math.Abs(Normalize(desired - facing)), Div(180 * One, Fixed64.Pi));
        var progress = Math.Clamp(Div(degrees - 25 * One, 115 * One), 0, One);
        progress = Mul(Mul(progress, progress), 3 * One - 2 * progress);
        return One + Mul(MinimumTurnScale - One, progress);
    }
    internal static long SlopeScale(long rise, long distance)
    {
        if (distance == 0) return One;
        var angle = Fixed64.Atan2(Math.Abs(rise), distance);
        var progress = Math.Min(Div(angle, Fixed64.Pi / 6), One);
        var limit = rise > 0 ? UphillScale : DownhillScale;
        return One + Mul(limit - One, progress);
    }
}
