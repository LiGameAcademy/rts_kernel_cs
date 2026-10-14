using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NumericPrototype;

internal static class WideCostChecks
{
    private sealed record SavedCost(string Raw);

    internal static string Run(Action<bool, string> check)
    {
        // Bounding-box corners derived from the observed 512x384, 32-unit grid.
        // This validates a conservative cost bound, not a claim that boundary points are occupiable.
        var cost = WideSquaredDistance.Between(PrototypeScalar.FromInt(-8192), PrototypeScalar.FromInt(-6144),
            PrototypeScalar.FromInt(8192), PrototypeScalar.FromInt(6144));
        var scaleSquared = (Int128)PrototypeScalar.Scale * PrototypeScalar.Scale;
        check(cost.Raw == (Int128)419430400 * scaleSquared, "map diagonal squared keeps full raw precision");
        var sum = default(WideSquaredDistance);
        for (var member = 0; member < 500; member++) sum = WideSquaredDistance.Add(sum, cost);
        check(sum.Raw == (Int128)209715200000 * scaleSquared, "500-map-diagonal upper-bound costs fit Int128");
        var overflowingScalar = PrototypeScalar.FromInt(419430400);
        try
        {
            for (var member = 1; member < 500; member++)
                overflowingScalar = PrototypeScalar.Add(overflowingScalar, PrototypeScalar.FromInt(419430400));
            check(false, "the same cost bound cannot be accumulated in a 32.32 scalar");
        }
        catch (OverflowException) { check(true, "narrow scalar accumulation explicitly fails"); }
        var half = new PrototypeScalar(PrototypeScalar.Scale / 2);
        check(WideSquaredDistance.Between(default, default, half, half).Raw == scaleSquared / 2,
            "squared-distance retains fractional raw-coordinate products");
        var reversed = WideSquaredDistance.Between(PrototypeScalar.FromInt(8192), PrototypeScalar.FromInt(6144),
            PrototypeScalar.FromInt(-8192), PrototypeScalar.FromInt(-6144));
        check(reversed == cost, "distance is symmetric");
        var json = JsonSerializer.Serialize(new SavedCost(sum.ToRawText()));
        var restored = WideSquaredDistance.RestoreRaw(JsonSerializer.Deserialize<SavedCost>(json)!.Raw);
        using var savedDocument = JsonDocument.Parse(json);
        check(restored == sum && savedDocument.RootElement.GetProperty("Raw").ValueKind == JsonValueKind.String,
            "128-bit cost is restored from decimal JSON string");
        try
        {
            WideSquaredDistance.Between(new(long.MinValue), default, new(long.MaxValue), default);
            check(false, "unrestricted 64-bit coordinate extremes do not imply safe 128-bit squares");
        }
        catch (OverflowException) { check(true, "wide squared-distance overflow is explicit"); }
        try
        {
            WideSquaredDistance.RestoreRaw("-1");
            check(false, "negative squared distance is invalid");
        }
        catch (ArgumentOutOfRangeException) { check(true, "negative restored cost rejected"); }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cost.ToRawText() + ";" + json))).ToLowerInvariant();
    }
}
