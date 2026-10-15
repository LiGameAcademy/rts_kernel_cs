using FixPointCS;
using Rts.Kernel.Numerics;

namespace NumericPrototype;

// Experimental boundary only. Production Rts.Kernel still uses double.
internal readonly record struct PrototypeScalar(long Raw)
{
    internal const long Scale = 1L << 32;
    internal static PrototypeScalar FromInt(int value) => new(SimScalar.FromInteger(value).Raw);
    internal static PrototypeScalar FromRatio(long numerator, long denominator)
    {
        if (denominator == 0) throw new DivideByZeroException();
        return new(SimScalar.FromRatio(numerator, denominator).Raw);
    }
    internal static PrototypeScalar Add(PrototypeScalar a, PrototypeScalar b) => new((SimScalar.FromRaw(a.Raw) + SimScalar.FromRaw(b.Raw)).Raw);
    internal static PrototypeScalar Multiply(PrototypeScalar a, PrototypeScalar b) =>
        new((SimScalar.FromRaw(a.Raw) * SimScalar.FromRaw(b.Raw)).Raw);
    internal static PrototypeScalar Divide(PrototypeScalar a, PrototypeScalar b)
    {
        if (b.Raw == 0) throw new DivideByZeroException();
        return new((SimScalar.FromRaw(a.Raw) / SimScalar.FromRaw(b.Raw)).Raw);
    }
    internal int FloorToInt()
    {
        return SimScalar.FromRaw(Raw).FloorToInt();
    }
    internal static PrototypeScalar Sqrt(PrototypeScalar value)
    {
        if (value.Raw < 0) throw new ArgumentOutOfRangeException(nameof(value));
        // Preserve the pinned candidate as an independent reference; production uses an exact wide floor root.
        return new(Fixed64.SqrtPrecise(value.Raw));
    }
}
