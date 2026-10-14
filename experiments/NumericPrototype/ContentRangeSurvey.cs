using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace NumericPrototype;

/// <summary>Read-only survey of existing exported data; never bundles source content into the prototype.</summary>
internal static class ContentRangeSurvey
{
    internal sealed record FieldRange(string Field, int Count, int Skipped, string? Minimum, string? Maximum,
        long? MinimumRaw, long? MaximumRaw, decimal MaximumAbsoluteError);
    internal sealed record FileRange(string Source, string Sha256, IReadOnlyList<FieldRange> Fields);

    internal static IReadOnlyList<FileRange> Run(string assetsDirectory)
    {
        var reports = new List<FileRange>();
        Add("slk-exported/Units/UnitData.json", root => Records(root, "records", ["turnRate", "moveHeight", "moveFloor"]));
        Add("slk-exported/Units/UnitBalance.json", root => Records(root, "records", ["spd", "collision"]));
        Add("map-parsed/echoisles/units.json", root => root.GetProperty("units").EnumerateArray()
            .SelectMany(unit => unit.GetProperty("position").EnumerateObject()
                .Select(p => ("position." + p.Name, p.Value))));
        Add("map-parsed/echoisles/pathing.json", root => root.EnumerateObject()
            .Where(p => p.Name is "width" or "height" or "cellSize").Select(p => (p.Name, p.Value))
            .Concat(root.GetProperty("origin").EnumerateObject().Select(p => ("origin." + p.Name, p.Value))));
        Add("map-parsed/echoisles/terrain-heightfield.json", root => root.GetProperty("heights").EnumerateArray()
            .Select(h => ("height", h)));
        return reports.AsReadOnly();

        void Add(string relative, Func<JsonElement, IEnumerable<(string Field, JsonElement Value)>> select)
        {
            var bytes = File.ReadAllBytes(Path.Combine(assetsDirectory, relative));
            using var document = JsonDocument.Parse(bytes);
            var fields = select(document.RootElement).GroupBy(p => p.Field, StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal).Select(group => Measure(group.Key, group.Select(p => p.Value))).ToArray();
            reports.Add(new(relative, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), fields));
        }
    }

    private static IEnumerable<(string Field, JsonElement Value)> Records(JsonElement root, string property, string[] fields)
    {
        foreach (var record in root.GetProperty(property).EnumerateArray())
        foreach (var field in fields)
            yield return (field, record.TryGetProperty(field, out var value) ? value : default);
    }

    private static FieldRange Measure(string field, IEnumerable<JsonElement> values)
    {
        var count = 0;
        var skipped = 0;
        decimal? minimum = null;
        decimal? maximum = null;
        string? minimumText = null;
        string? maximumText = null;
        long? minimumRaw = null;
        long? maximumRaw = null;
        var maximumError = 0m;
        foreach (var value in values)
        {
            if (value.ValueKind != JsonValueKind.Number)
            {
                skipped++;
                continue;
            }
            var text = value.GetRawText();
            var raw = InputQuantizer.Parse(text).Raw; // A numeric out-of-range value aborts; it is never silently omitted.
            var number = decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
            var error = Math.Abs((decimal)raw / PrototypeScalar.Scale - number);
            if (minimum is null || number < minimum) { minimum = number; minimumText = text; }
            if (maximum is null || number > maximum) { maximum = number; maximumText = text; }
            minimumRaw = minimumRaw is null ? raw : Math.Min(minimumRaw.Value, raw);
            maximumRaw = maximumRaw is null ? raw : Math.Max(maximumRaw.Value, raw);
            maximumError = Math.Max(maximumError, error);
            count++;
        }
        return new(field, count, skipped, minimumText, maximumText, minimumRaw, maximumRaw, maximumError);
    }
}
