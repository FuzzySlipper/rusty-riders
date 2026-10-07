using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Rusty.Engine;

namespace RustyRiders.Game.Content;

/// <summary>
/// Reads one authored content file through Engine Content into its domain's typed record; every error names the
/// file. Each domain declares its own JSON context (strict for authored files: missing constructor values, nulls in
/// non-nullable fields and unknown members fail) and validates what parses but contradicts another definition with
/// <see cref="Require"/> and the helpers below.
/// </summary>
internal static class Authored
{
    internal static T Read<T>(IEngineContext engine, string path, JsonTypeInfo<T> type) where T : class =>
        Parse(Bytes(engine, path), path, type);

    /// <summary>An optional file: null when the content root has no such file; a present file must parse.</summary>
    internal static T? ReadOptional<T>(IEngineContext engine, string path, JsonTypeInfo<T> type) where T : class
    {
        ReadOnlyMemory<byte> bytes;
        try
        {
            bytes = Bytes(engine, path);
        }
        catch (EngineCallException)
        {
            return null;
        }
        return Parse(bytes, path, type);
    }

    /// <summary>A whole content-root file; a missing file throws <see cref="EngineCallException"/>.</summary>
    internal static ReadOnlyMemory<byte> Bytes(IEngineContext engine, string path)
    {
        using ContentReference content = engine.Content.OpenReference(new ContentOpenRequest(path));
        ulong length = engine.Content.ReadReferenceInfo(content).Span[0].ByteLength;
        return engine.Content.ReadBytes(new ContentReadBytesRequest(content, 0, checked((uint)length)));
    }

    private static T Parse<T>(ReadOnlyMemory<byte> bytes, string path, JsonTypeInfo<T> type) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize(bytes.Span, type) ?? throw new JsonException("The file holds no value.");
        }
        catch (JsonException error)
        {
            throw new InvalidOperationException($"content/{path}: {error.Message}", error);
        }
    }

    /// <summary>Fails authored data that parses but contradicts another definition.</summary>
    internal static void Require(bool condition, string path, string field, string problem)
    {
        if (!condition) throw new InvalidOperationException($"content/{path} {field}: {problem}");
    }

    internal static void Positive(string path, string field, float value) =>
        Require(float.IsFinite(value) && value > 0, path, field, $"must be a positive number; found {value}.");

    internal static void AtLeast(string path, string field, float value, float minimum) =>
        Require(float.IsFinite(value) && value >= minimum, path, field, $"must be at least {minimum}; found {value}.");

    internal static void Within(string path, string field, float value, float minimum, float maximum) =>
        Require(float.IsFinite(value) && value >= minimum && value <= maximum, path, field,
            $"must be between {minimum} and {maximum}; found {value}.");

    internal static void Finite(string path, string field, float value) =>
        Require(float.IsFinite(value), path, field, $"must be a finite number; found {value}.");

    /// <summary>A world position or direction: exactly three finite components.</summary>
    internal static void Point(string path, string field, float[] value) =>
        Require(value.Length == 3 && value.All(float.IsFinite), path, field,
            $"must be [x, y, z] with three finite numbers; found {value.Length} component(s).");

    /// <summary>A linear RGB colour: three finite, non-negative components.</summary>
    internal static void Colour(string path, string field, float[] value) =>
        Require(value.Length == 3 && value.All(c => float.IsFinite(c) && c >= 0), path, field,
            "must be [r, g, b] with three non-negative numbers.");

    internal static Vector3 Vector(float[] xyz) => xyz.Length == 3
        ? new Vector3(xyz[0], xyz[1], xyz[2])
        : throw new InvalidOperationException("Coordinates must have three components.");

    internal static Color Color(float[] rgb) => rgb.Length == 3
        ? new Color(rgb[0], rgb[1], rgb[2], 1)
        : throw new InvalidOperationException("Colours must have three components.");
}
