using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace NumericPrototype.Matching;

internal static class MatchingInputIdentity
{
    // Preserve the existing canonical decimal text exactly, without allocating a giant joined string.
    internal static string Compute(int count, IReadOnlyList<Int128> costs)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes("wide-hungarian-v1;" + count.ToString(CultureInfo.InvariantCulture) + ";"));
        Span<byte> bytes = stackalloc byte[8192];
        Span<char> digits = stackalloc char[40];
        var used = 0;
        for (var index = 0; index < costs.Count; index++)
        {
            if (used + 41 > bytes.Length)
            {
                hash.AppendData(bytes[..used]);
                used = 0;
            }
            if (index > 0) bytes[used++] = (byte)';';
            if (!costs[index].TryFormat(digits, out var length, provider: CultureInfo.InvariantCulture))
                throw new InvalidOperationException("Int128 decimal buffer too small.");
            for (var digit = 0; digit < length; digit++) bytes[used++] = checked((byte)digits[digit]);
        }
        hash.AppendData(bytes[..used]);
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
