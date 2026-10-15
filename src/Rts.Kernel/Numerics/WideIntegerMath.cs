namespace Rts.Kernel.Numerics;

internal static class WideIntegerMath
{
    // Restoring integer square root: bounded to 64 iterations, no floating estimate/BigInteger in the hot path.
    internal static UInt128 FloorRoot(UInt128 value)
    {
        UInt128 bit = (UInt128)1 << 126;
        UInt128 root = 0;
        while (bit > value) bit >>= 2;
        while (bit != 0)
        {
            if (value >= root + bit)
            {
                value -= root + bit;
                root = (root >> 1) + bit;
            }
            else root >>= 1;
            bit >>= 2;
        }
        return root;
    }
}
