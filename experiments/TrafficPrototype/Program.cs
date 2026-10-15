using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rts.TrafficPrototype;

var checks = 0;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new InvalidOperationException(message);
}
var observations = MotionChecks.Run(Check);
var canonical = JsonSerializer.Serialize(observations);
var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
Console.WriteLine(JsonSerializer.Serialize(new { scope = "linear-frame-reference-only", checks, digest, observations,
    productionChanged = false, bigIntegerReference = true, resolvesTrafficDeadlocks = false }));
