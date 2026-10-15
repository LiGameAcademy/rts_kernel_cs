namespace Rts.Kernel.Numerics;

/// <summary>Compare two products without narrowing 128-bit factors or allocating big integers.</summary>
internal static class WideProduct
{
    internal static int Compare(UInt128 a, UInt128 b, UInt128 c, UInt128 d)
    {
        var left = Multiply(a, b);
        var right = Multiply(c, d);
        var high = left.High.CompareTo(right.High);
        return high != 0 ? high : left.Low.CompareTo(right.Low);
    }

    private static (UInt128 High, UInt128 Low) Multiply(UInt128 a, UInt128 b)
    {
        var a0 = (ulong)a;
        var a1 = (ulong)(a >> 64);
        var b0 = (ulong)b;
        var b1 = (ulong)(b >> 64);
        var low = (UInt128)a0 * b0;
        var cross1 = (UInt128)a0 * b1;
        var cross2 = (UInt128)a1 * b0;
        var carry = (low >> 64) + (ulong)cross1 + (ulong)cross2;
        var high = checked((UInt128)a1 * b1 + (cross1 >> 64) + (cross2 >> 64) + (carry >> 64));
        return (high, ((UInt128)(ulong)carry << 64) | (ulong)low);
    }
}
