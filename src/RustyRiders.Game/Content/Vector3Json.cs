using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RustyRiders.Game.Content;

/// <summary>
/// An authored point, direction or turn as JSON: exactly three finite numbers, [x, y, z]. Domains whose records carry
/// <see cref="Vector3"/> (the Engine's first-person values among them) name it in their JSON context.
/// </summary>
internal sealed class Vector3Json : JsonConverter<Vector3>
{
    public override Vector3 Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("must be [x, y, z] with three finite numbers.");
        Span<float> xyz = stackalloc float[3];
        int count = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.Number || count == 3 || !reader.TryGetSingle(out float value) || !float.IsFinite(value))
                throw new JsonException("must be [x, y, z] with three finite numbers.");
            xyz[count++] = value;
        }
        return count == 3 ? new Vector3(xyz[0], xyz[1], xyz[2]) : throw new JsonException($"must be [x, y, z] with three finite numbers; found {count}.");
    }

    public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.X);
        writer.WriteNumberValue(value.Y);
        writer.WriteNumberValue(value.Z);
        writer.WriteEndArray();
    }
}
