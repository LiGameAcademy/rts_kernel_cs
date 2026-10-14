using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FixPointCS;
using NumericPrototype;
using Rts.Kernel;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
if (args.Length == 2 && args[0] == "--survey")
{
    Console.WriteLine(JsonSerializer.Serialize(ContentRangeSurvey.Run(args[1])));
    return;
}
var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    checks++;
}
void Throws<T>(Action action, string message) where T : Exception
{
    try { action(); }
    catch (T) { checks++; return; }
    throw new InvalidOperationException(message);
}
var rng = new DeterministicRng(0);
ulong[] answers = [0xe220a8397b1dcdaf, 0x6e789e6aa1b965f4, 0x06c45d188009454f, 0xf88bb8a8724c81ec];
foreach (var answer in answers) Check(rng.NextUInt64() == answer, "SplitMix64 known-answer vector in checked build");
var clone = new DeterministicRng(rng.State);
for (var i = 0; i < 100; i++) Check(rng.NextUInt64() == clone.NextUInt64(), "RNG restored state");
var one = PrototypeScalar.FromInt(1);
Check(PrototypeScalar.FromRatio(-1, 3).Raw == -1431655765L, "division truncates toward zero");
Check(PrototypeScalar.FromRatio(-1, 3).FloorToInt() == -1, "negative grid floor differs from truncation");
Throws<OverflowException>(() => PrototypeScalar.Add(new(long.MaxValue), one), "addition overflow must fail");
Throws<OverflowException>(() => PrototypeScalar.Multiply(new(long.MaxValue), PrototypeScalar.FromInt(2)), "multiply overflow must fail");
Throws<OverflowException>(() => PrototypeScalar.Divide(new(long.MaxValue), new(1)), "divide overflow must fail");
Throws<DivideByZeroException>(() => PrototypeScalar.Divide(one, default), "zero divisor must fail");
Throws<ArgumentOutOfRangeException>(() => PrototypeScalar.Sqrt(new(-1)), "negative root must fail at boundary");
// Candidate primitive multiplies toward negative infinity; it cannot silently define project arithmetic.
Check(Fixed64.Mul(-1, Fixed64.Half) == -1, "candidate multiplication floors negative fractions");
Check(PrototypeScalar.Multiply(new(-1), new(Fixed64.Half)).Raw == 0, "project arithmetic truncates negative fractions");
var rawTrace = new StringBuilder();
var maxSinError = 0.0;
var maxAtanError = 0.0;
var maxSqrtError = 0.0;
for (var i = -1000; i <= 1000; i++)
{
    var x = PrototypeScalar.FromRatio(i, 100);
    var square = PrototypeScalar.Multiply(x, x);
    var root = PrototypeScalar.Sqrt(square);
    Check(Math.Abs(root.Raw / (double)PrototypeScalar.Scale - Math.Sqrt(square.Raw / (double)PrototypeScalar.Scale))
        <= 2.0 / PrototypeScalar.Scale, "square root of quantized square within two raw units");
    var angle = PrototypeScalar.FromRatio(i, 1000);
    var sine = Fixed64.Sin(angle.Raw);
    var atan = Fixed64.Atan2(x.Raw, Fixed64.One);
    // Floating reference is a diagnostic oracle, never fed back into simulated values.
    maxSinError = Math.Max(maxSinError, Math.Abs(sine / (double)PrototypeScalar.Scale - Math.Sin(i / 1000.0)));
    maxAtanError = Math.Max(maxAtanError, Math.Abs(atan / (double)PrototypeScalar.Scale - Math.Atan2(i / 100.0, 1)));
    maxSqrtError = Math.Max(maxSqrtError, Math.Abs(root.Raw / (double)PrototypeScalar.Scale - Math.Abs(i / 100.0)));
    rawTrace.Append(x.Raw).Append(',').Append(square.Raw).Append(',').Append(root.Raw).Append(',')
        .Append(sine).Append(',').Append(atan).Append(';');
}
Check(maxSinError < 0.00001, "sample sine error bound");
Check(maxAtanError < 0.00001, "sample atan2 error bound");
Check(maxSqrtError < 0.000001, "sample square-root error bound");
var state = new long[] { one.Raw, -1, long.MaxValue, long.MinValue };
Check(JsonSerializer.Deserialize<long[]>(JsonSerializer.Serialize(state))!.SequenceEqual(state), "64-bit raw JSON round trip in .NET");
ContentSurveyChecks.Run(Check);
var inputTraceHash = InputQuantizationChecks.Run(Check);
var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawTrace.ToString()))).ToLowerInvariant();
Console.WriteLine(JsonSerializer.Serialize(new { checks, rawTraceHash = digest, inputTraceHash, maxSinError, maxAtanError, maxSqrtError,
    scope = "scalar and math contract probe; not a migrated RTS simulation" }));
