namespace Rts.Kernel.Numerics;

internal static class FixedGridMath
{
    internal static long FloorIndex(SimScalar position, SimScalar origin, SimScalar spacing)
    {
        var numerator = (Int128)position.Raw - origin.Raw;
        var quotient = numerator / spacing.Raw;
        if (numerator < 0 && numerator % spacing.Raw != 0) quotient--;
        return checked((long)quotient);
    }
    internal static int BucketCells(SimScalar radius, int multiplier, SimScalar cellSize, int extra = 0)
    {
        var numerator = (Int128)radius.Raw * multiplier;
        var ceiling = (numerator + cellSize.Raw - 1) / cellSize.Raw + extra;
        return (int)Int128.Clamp(ceiling, 1, int.MaxValue);
    }
}
