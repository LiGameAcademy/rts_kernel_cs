using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace NumericPrototype;

/// <summary>Experimental JSON-number text boundary: exact rational conversion, nearest/ties away from zero.</summary>
internal static class InputQuantizer
{
    internal const int MaximumTextLength = 256;
    internal const int MaximumExponent = 256;

    internal static PrototypeScalar Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        text = text.Trim();
        if (text.Length == 0 || text.Length > MaximumTextLength)
            throw new FormatException("Numeric text length is outside the experimental limit.");
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
                throw new FormatException("Exponent is outside the experimental limit.");
            text = text[..exponentIndex];
        }
        var pointIndex = text.IndexOf('.');
        var fractionDigits = pointIndex < 0 ? 0 : text.Length - pointIndex - 1;
        var numerator = BigInteger.Parse(text.Replace(".", ""), CultureInfo.InvariantCulture)
            * PrototypeScalar.Scale;
        var decimalPower = exponent - fractionDigits;
        var denominator = BigInteger.One;
        if (decimalPower >= 0) numerator *= BigInteger.Pow(10, decimalPower);
        else denominator = BigInteger.Pow(10, -decimalPower);
        if (numerator < (BigInteger)long.MinValue * denominator
            || numerator > (BigInteger)long.MaxValue * denominator)
            throw new OverflowException("Input lies outside the 32.32 representable range.");
        var raw = BigInteger.DivRem(numerator, denominator, out var remainder);
        if (BigInteger.Abs(remainder) * 2 >= denominator) raw += numerator.Sign;
        return new PrototypeScalar(checked((long)raw));
    }
}
