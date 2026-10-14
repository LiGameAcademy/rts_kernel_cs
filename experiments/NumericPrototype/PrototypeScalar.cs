using FixPointCS;

namespace NumericPrototype;

// Experimental boundary only. Production Rts.Kernel still uses double.
internal readonly record struct PrototypeScalar(long Raw)
{
    internal const long Scale = 1L << 32;
    internal static PrototypeScalar FromInt(int value) => new((long)value * Scale);
    internal static PrototypeScalar FromRatio(long numerator, long denominator)
    {
        if (denominator == 0) throw new DivideByZeroException();
        return new(checked((long)((Int128)numerator * Scale / denominator)));
    }
    internal static PrototypeScalar Add(PrototypeScalar a, PrototypeScalar b) => new(checked(a.Raw + b.Raw));
    internal static PrototypeScalar Multiply(PrototypeScalar a, PrototypeScalar b) =>
        new(checked((long)((Int128)a.Raw * b.Raw / Scale)));
    internal static PrototypeScalar Divide(PrototypeScalar a, PrototypeScalar b)
    {
        if (b.Raw == 0) throw new DivideByZeroException();
        return new(checked((long)((Int128)a.Raw * Scale / b.Raw)));
    }
    internal int FloorToInt()
    {
        var quotient = Raw / Scale;
        if (Raw < 0 && Raw % Scale != 0) quotient--;
        return checked((int)quotient);
    }
    internal static PrototypeScalar Sqrt(PrototypeScalar value)
    {
        if (value.Raw < 0) throw new ArgumentOutOfRangeException(nameof(value));
        return new(Fixed64.SqrtPrecise(value.Raw));
    }
}
