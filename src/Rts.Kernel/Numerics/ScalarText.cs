using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace Rts.Kernel.Numerics;

/// <summary>Exact JSON-number text boundary: exact rational conversion, nearest/ties away from zero.</summary>
internal static class ScalarText
{
    internal static string Format(long raw)
    {
        var magnitude = BigInteger.Abs(new BigInteger(raw));
        var integer = magnitude / SimScalar.Scale;
        var fraction = magnitude % SimScalar.Scale;
        var sign = raw < 0 ? "-" : "";
        if (fraction.IsZero) return sign + integer.ToString(CultureInfo.InvariantCulture);
        var digits = (fraction * BigInteger.Pow(5, 32)).ToString(CultureInfo.InvariantCulture).PadLeft(32, '0').TrimEnd('0');
        return sign + integer.ToString(CultureInfo.InvariantCulture) + "." + digits;
    }

    internal const int MaximumTextLength = 256;
    internal const int MaximumExponent = 256;

    internal static SimScalar Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        text = text.Trim();
        if (text.Length == 0 || text.Length > MaximumTextLength)
            throw new FormatException("Numeric text length is outside the numeric boundary limit.");
        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Number)
                throw new FormatException("A JSON numeric literal is required.");
        }
        catch (JsonException error)
        {
            throw new FormatException("Invalid JSON numeric literal.", error);
        }

        var exponentIndex = text.IndexOfAny(['e', 'E']);
        var exponent = 0;
        if (exponentIndex >= 0)
        {
            if (!int.TryParse(text[(exponentIndex + 1)..], NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out exponent)
                || exponent < -MaximumExponent || exponent > MaximumExponent)
                throw new FormatException("Exponent is outside the numeric boundary limit.");
            text = text[..exponentIndex];
        }
        var pointIndex = text.IndexOf('.');
        var fractionDigits = pointIndex < 0 ? 0 : text.Length - pointIndex - 1;
        var numerator = BigInteger.Parse(text.Replace(".", ""), CultureInfo.InvariantCulture)
            * SimScalar.Scale;
        var decimalPower = exponent - fractionDigits;
        var denominator = BigInteger.One;
        if (decimalPower >= 0) numerator *= BigInteger.Pow(10, decimalPower);
        else denominator = BigInteger.Pow(10, -decimalPower);
        if (numerator < (BigInteger)long.MinValue * denominator
            || numerator > (BigInteger)long.MaxValue * denominator)
            throw new OverflowException("Input lies outside the 32.32 representable range.");
        var raw = BigInteger.DivRem(numerator, denominator, out var remainder);
        if (BigInteger.Abs(remainder) * 2 >= denominator) raw += numerator.Sign;
        return SimScalar.FromRaw(checked((long)raw));
    }
}
