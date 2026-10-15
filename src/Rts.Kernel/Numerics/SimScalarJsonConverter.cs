using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rts.Kernel.Numerics;

// Scalar JSON is a raw Int64 token. Human-readable decimal units enter through SimScalar.Parse.
internal sealed class SimScalarJsonConverter : JsonConverter<SimScalar>
{
    public SimScalarJsonConverter() { }
    public override SimScalar Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt64(out var raw))
            throw new JsonException("A raw signed 64-bit integer is required.");
        return SimScalar.FromRaw(raw);
    }
    public override void Write(Utf8JsonWriter writer, SimScalar value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value.Raw);
}
