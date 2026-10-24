using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rts.Kernel.Combat;

/// <summary>Strict, canonical wire format for the stationary combat module; not a whole-match save.</summary>
public static class CombatSnapshotJson
{
    public const int CurrentFormatVersion = 1;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32
    };

    public static string Serialize(CombatSnapshot snapshot)
    {
        CombatSnapshotRules.ValidateStructure(snapshot);
        return JsonSerializer.Serialize(Freeze(snapshot), Options);
    }

    public static CombatSnapshot Deserialize(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        RejectDuplicateNames(document.RootElement);
        var snapshot = JsonSerializer.Deserialize<CombatSnapshot>(json, Options)
            ?? throw new InvalidDataException("Combat snapshot cannot be null.");
        CombatSnapshotRules.ValidateStructure(snapshot);
        return Freeze(snapshot);
    }

    internal static string Hash(CombatSnapshot snapshot) => Digest(Serialize(snapshot));

    internal static string HashDefinitions(IEnumerable<CombatDefinitionEntry> definitions) =>
        Digest(JsonSerializer.Serialize(definitions.OrderBy(entry => entry.Id).ToArray(), Options));

    private static string Digest(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static CombatSnapshot Freeze(CombatSnapshot snapshot) =>
        snapshot with { Units = Array.AsReadOnly(snapshot.Units.OrderBy(unit => unit.Id).ToArray()) };

    private static void RejectDuplicateNames(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new JsonException($"Duplicate property: {property.Name}.");
                RejectDuplicateNames(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) RejectDuplicateNames(item);
        }
    }
}
