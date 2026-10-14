using System.Globalization;

namespace NumericPrototype;

/// <summary>Nonnegative squared-distance/cumulative cost in raw-coordinate squared units (scale 2^64).</summary>
internal readonly record struct WideSquaredDistance
{
    internal Int128 Raw { get; }
    private WideSquaredDistance(Int128 raw)
    {
        if (raw < 0) throw new ArgumentOutOfRangeException(nameof(raw));
        Raw = raw;
    }
    internal static WideSquaredDistance Between(PrototypeScalar ax, PrototypeScalar ay,
        PrototypeScalar bx, PrototypeScalar by)
    {
        var dx = (Int128)bx.Raw - ax.Raw;
        var dy = (Int128)by.Raw - ay.Raw;
        return new(checked(dx * dx + dy * dy));
    }
    internal static WideSquaredDistance Add(WideSquaredDistance left, WideSquaredDistance right) =>
        new(checked(left.Raw + right.Raw));
    internal string ToRawText() => Raw.ToString(CultureInfo.InvariantCulture);
    internal static WideSquaredDistance RestoreRaw(string text) =>
        new(Int128.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture));
}
