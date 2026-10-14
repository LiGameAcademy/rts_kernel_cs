using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace NumericPrototype;

internal static class InputQuantizationChecks
{
    internal static string Run(Action<bool, string> check)
    {
        var trace = new StringBuilder();
        void Expect(string text, long raw)
        {
            var actual = InputQuantizer.Parse(text).Raw;
            check(actual == raw, "quantized decimal: " + text);
            trace.Append(actual.ToString(CultureInfo.InvariantCulture)).Append(';');
        }
        void Reject<T>(string text) where T : Exception
        {
            try { InputQuantizer.Parse(text); }
            catch (T) { check(true, "rejected numeric input"); return; }
            check(false, "unexpectedly accepted: " + text);
        }
        Expect("0.1", 429496730);
        Expect("-0.1", -429496730);
        Expect(" 1e-1 ", 429496730);
        Expect("-0", 0);
        Expect("0.000000000116415321826934814453125", 1);
        Expect("-0.000000000116415321826934814453125", -1);
        Expect("0.000000000116415321826934814453124", 0);
        Expect("-0.000000000116415321826934814453124", 0);
        Expect("2147483647.99999999976716935634613037109375", long.MaxValue);
        Expect("-2147483648", long.MinValue);
        Expect("1e-256", 0);
        Reject<OverflowException>("2147483647.999999999883584678173065185546875");
        Reject<OverflowException>("-2147483648.000000000116415321826934814453125");
        Reject<OverflowException>("1e256");
        Reject<OverflowException>("2147483647.99999999976716935634613037109376");
        Reject<OverflowException>("-2147483648.00000000000000000000000000000001");
        foreach (var invalid in new[] { "", "NaN", "Infinity", "1,5", "01", "+1", "true", "[]", "1 2", "1e257", "1e-257", new string('1', 257) })
            Reject<FormatException>(invalid);
        var savedCulture = CultureInfo.CurrentCulture;
        try
        {
            foreach (var culture in new[] { "en-US", "fr-FR", "zh-CN" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                Expect("123.125", 528817848320);
                Expect("-1.25E+2", -536870912000);
            }
        }
        finally { CultureInfo.CurrentCulture = savedCulture; }
        for (var i = -1024; i <= 1024; i++)
        {
            var text = ((decimal)i / 1024).ToString(CultureInfo.InvariantCulture);
            Expect(text, (long)i * (1L << 22));
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(trace.ToString()))).ToLowerInvariant();
    }
}
