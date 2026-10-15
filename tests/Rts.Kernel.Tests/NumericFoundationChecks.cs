using System.Numerics;
using System.Text.Json;
using Rts.Kernel;
using Rts.Kernel.Numerics;

internal static class NumericFoundationChecks
{
    internal static void Run(Action<bool, string> check)
    {
        void Throws<T>(Action action, string message) where T : Exception
        {
            try { action(); check(false, message); }
            catch (T) { check(true, message); }
        }
        long[] rawValues = [long.MinValue, long.MinValue + 1, -SimScalar.Scale - 1, -1, 0, 1,
            SimScalar.Scale - 1, SimScalar.Scale, long.MaxValue - 1, long.MaxValue];
        foreach (var raw in rawValues)
        {
            var value = SimScalar.FromRaw(raw);
            check(SimScalar.Parse(value.ToString()) == value, "exact decimal text round trips every endpoint raw bit");
            check(JsonSerializer.Deserialize<SimScalar>(JsonSerializer.Serialize(value)) == value,
                "JSON raw integer serialization preserves signed 64-bit endpoints");
        }
        check(SimScalar.Parse("0.000000000116415321826934814453125").Raw == 1
            && SimScalar.Parse("-0.000000000116415321826934814453125").Raw == -1, "input half ties round away from zero");
        check(SimScalar.FromRatio(-1, 3).Raw == -1431655765 && SimScalar.FromRaw(-1).FloorToInt() == -1,
            "arithmetic truncation and negative grid floor remain distinct");
        check((SimScalar.FromRaw(-1) * SimScalar.FromRatio(1, 2)).Raw == 0, "multiplication truncates negative fractions toward zero");
        Throws<OverflowException>(() => _ = SimScalar.MaxValue + SimScalar.FromRaw(1), "addition overflow fails");
        Throws<OverflowException>(() => _ = -SimScalar.MinValue, "negative minimum overflow fails");
        Throws<OverflowException>(() => SimScalar.Parse("2147483648"), "input overflow is rejected before rounding");
        Throws<DivideByZeroException>(() => _ = SimScalar.One / SimScalar.Zero, "division by zero fails");
        Throws<ArgumentOutOfRangeException>(() => SimMath.Sqrt(-1), "negative square root fails");
        Throws<FormatException>(() => SimScalar.Parse("NaN"), "non-JSON numeric text fails");
        Throws<JsonException>(() => JsonSerializer.Deserialize<SimScalar>("1.5"), "fractional raw snapshot value fails");
        check(SimMath.NormalizeRadians(SimMath.Pi) == -SimMath.Pi
            && SimMath.NormalizeRadians(-SimMath.Pi) == -SimMath.Pi, "angle normalization has one exact half-open period");
        check(SimMath.Sin(SimScalar.One + SimMath.Tau) == SimMath.Sin(SimScalar.One),
            "trigonometric inputs share the exact project period");
        var wrapped = SimMath.NormalizeRadians(SimScalar.MinValue);
        check(wrapped >= -SimMath.Pi && wrapped < SimMath.Pi, "extreme negative angle normalizes without overflow");
        Throws<ArgumentException>(() => SimMath.Atan2(0, 0), "undefined heading is an explicit input failure");
        check(SquaredDistance.FromRaw(Int128.MaxValue).ToString().Contains("raw-squared"),
            "diagnosing a wide value never narrows it through scalar length");
        check(JsonSerializer.Serialize(SimScalar.One) == "4294967296", "scalar JSON uses raw integer units explicitly");
        Throws<JsonException>(() => JsonSerializer.Deserialize<SimScalar>("{}"), "missing raw numeric token is rejected");
        foreach (var x in rawValues)
        foreach (var y in rawValues)
        {
            if (x == 0 && y == 0) continue;
            var angle = SimMath.Atan2(SimScalar.FromRaw(y), SimScalar.FromRaw(x));
            var reference = Math.Atan2(y, x);
            var error = Math.Abs(angle.ToPresentationDouble() - reference);
            error = Math.Min(error, Math.Abs(error - 2 * Math.PI));
            check(error < 0.0000001 && angle >= -SimMath.Pi && angle < SimMath.Pi,
                "full-range and sub-unit headings retain direction within the pinned approximation tolerance");
        }
        var rng = new DeterministicRng(85);
        for (var index = 0; index < 1024; index++)
        {
            var a = unchecked((long)rng.NextUInt64());
            var b = unchecked((long)rng.NextUInt64());
            Verify((BigInteger)a * b / SimScalar.Scale, () => SimScalar.FromRaw(a) * SimScalar.FromRaw(b));
            if (b != 0) Verify((BigInteger)a * SimScalar.Scale / b, () => SimScalar.FromRaw(a) / SimScalar.FromRaw(b));
            var limit = 16384L * SimScalar.Scale;
            var p = FixedVector2.FromRaw((long)(rng.NextUInt64() % (ulong)limit), (long)(rng.NextUInt64() % (ulong)limit));
            var q = FixedVector2.FromRaw((long)(rng.NextUInt64() % (ulong)limit), (long)(rng.NextUInt64() % (ulong)limit));
            var dx = (BigInteger)p.X.Raw - q.X.Raw;
            var dy = (BigInteger)p.Y.Raw - q.Y.Raw;
            var expected = dx * dx + dy * dy;
            var distance = SquaredDistance.Between(p, q);
            var root = (BigInteger)distance.Length.Raw;
            check((BigInteger)distance.Raw == expected && root * root <= expected && (root + 1) * (root + 1) > expected,
                "wide distance and floor root agree with independent arbitrary-precision arithmetic");
        }
        check(new FixedVector2(3, 4).Length == (SimScalar)5, "vector length retains distance units");
        Throws<OverflowException>(() => SquaredDistance.Between(FixedVector2.FromRaw(long.MinValue, 0),
            FixedVector2.FromRaw(long.MaxValue, 0)), "full Int64 separation beyond signed Int128 bound fails");
        Throws<OverflowException>(() => _ = SquaredDistance.FromRaw(Int128.MaxValue) + SquaredDistance.FromRaw(1),
            "wide cumulative cost overflow fails");
        Throws<OverflowException>(() => _ = SquaredDistance.FromRaw(Int128.MaxValue).Length,
            "wide root is not silently narrowed into an insufficient scalar");
        foreach (var value in new[] { SimScalar.Zero, SimScalar.FromRaw(1), SimScalar.One, SimScalar.MaxValue })
        {
            var root = (BigInteger)SimMath.Sqrt(value).Raw;
            var square = (BigInteger)value.Raw * SimScalar.Scale;
            check(root * root <= square && (root + 1) * (root + 1) > square,
                "full-range scalar square root is the exact floor, including the previous failing endpoint");
        }
        void Verify(BigInteger expected, Func<SimScalar> operation)
        {
            if (expected < long.MinValue || expected > long.MaxValue)
                Throws<OverflowException>(() => operation(), "random arithmetic overflow agrees with exact reference");
            else check(operation().Raw == (long)expected, "random arithmetic agrees with exact reference truncation");
        }
    }
}
