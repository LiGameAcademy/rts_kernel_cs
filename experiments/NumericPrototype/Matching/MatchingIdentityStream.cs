using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace NumericPrototype.Matching;

/// <summary>Derived hash state; snapshot restoration replays its deterministic input prefix.</summary>
internal sealed class MatchingIdentityStream : IDisposable
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private readonly byte[] _bytes = new byte[8192];
    private int _used;

    internal MatchingIdentityStream(int count) => _hash.AppendData(Encoding.UTF8.GetBytes(
        "wide-hungarian-v1;" + count.ToString(CultureInfo.InvariantCulture) + ";"));

    internal void Append(Int128 value, int index)
    {
        if (_used + 41 > _bytes.Length) Flush();
        if (index > 0) _bytes[_used++] = (byte)';';
        Span<char> digits = stackalloc char[40];
        if (!value.TryFormat(digits, out var length, provider: CultureInfo.InvariantCulture))
            throw new InvalidOperationException("Int128 decimal buffer too small.");
        for (var digit = 0; digit < length; digit++) _bytes[_used++] = checked((byte)digits[digit]);
    }

    internal string Finish()
    {
        Flush();
        return Convert.ToHexString(_hash.GetHashAndReset());
    }

    private void Flush()
    {
        _hash.AppendData(_bytes.AsSpan(0, _used));
        _used = 0;
    }

    public void Dispose() => _hash.Dispose();
}
