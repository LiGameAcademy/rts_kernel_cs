using System.Text.Json.Serialization;

namespace Rts.Kernel.Numerics;

/// <summary>Signed 32.32 state value. Arithmetic truncates toward zero and throws on overflow.</summary>
[JsonConverter(typeof(SimScalarJsonConverter))]
public readonly record struct SimScalar : IComparable<SimScalar>
{
    public const long Scale = 1L << 32;
    public long Raw { get; }
    private SimScalar(long raw) => Raw = raw;
    public static SimScalar Zero => default;
    public static SimScalar One => FromRaw(Scale);
    public static SimScalar MinValue => FromRaw(long.MinValue);
    public static SimScalar MaxValue => FromRaw(long.MaxValue);
    public static SimScalar FromRaw(long raw) => new(raw);
    public static SimScalar FromInteger(int value) => new(checked((long)value * Scale));
    public static SimScalar FromRatio(long numerator, long denominator)
    {
        if (denominator == 0) throw new DivideByZeroException();
        return new(checked((long)((Int128)numerator * Scale / denominator)));
    }
    public static SimScalar Parse(string text) => ScalarText.Parse(text);
    public int FloorToInt()
    {
        var quotient = Raw / Scale;
        if (Raw < 0 && Raw % Scale != 0) quotient--;
        return checked((int)quotient);
    }
    /// <summary>Presentation boundary only. Do not feed rounded host values back into rules.</summary>
    public double ToPresentationDouble() => Raw / (double)Scale;
    public override string ToString() => ScalarText.Format(Raw);
    public int CompareTo(SimScalar other) => Raw.CompareTo(other.Raw);
    public static implicit operator SimScalar(int value) => FromInteger(value);
    public static SimScalar operator +(SimScalar a, SimScalar b) => new(checked(a.Raw + b.Raw));
    public static SimScalar operator -(SimScalar a, SimScalar b) => new(checked(a.Raw - b.Raw));
    public static SimScalar operator -(SimScalar value) => new(checked(-value.Raw));
    public static SimScalar operator *(SimScalar a, SimScalar b) => new(checked((long)((Int128)a.Raw * b.Raw / Scale)));
    public static SimScalar operator /(SimScalar a, SimScalar b)
    {
        if (b.Raw == 0) throw new DivideByZeroException();
        return new(checked((long)((Int128)a.Raw * Scale / b.Raw)));
    }
    public static bool operator <(SimScalar a, SimScalar b) => a.Raw < b.Raw;
    public static bool operator >(SimScalar a, SimScalar b) => a.Raw > b.Raw;
    public static bool operator <=(SimScalar a, SimScalar b) => a.Raw <= b.Raw;
    public static bool operator >=(SimScalar a, SimScalar b) => a.Raw >= b.Raw;
}
