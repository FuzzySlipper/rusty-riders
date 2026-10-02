using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rusty.Engine;

namespace RustyRiders.Game.Gallery;

/// <summary>
/// One asset-pipeline unity-import placement file: where the old game's prefab put each converted GLB, in glTF
/// space. Rows without a GLB (nested prefab markers, built-in meshes) or with an error are not drawn.
/// </summary>
internal sealed record PlacementFile(string Prefab, PlacementRow[] Placements, string[] Notes)
{
    internal static PlacementFile Load(IEngineContext engine, string path) =>
        JsonSerializer.Deserialize(ContentFiles.Read(engine, path).Span, PlacementJson.Default.PlacementFile)
        ?? throw new InvalidOperationException($"{path} must contain a placement file.");
}

internal sealed record PlacementRow(string Kind, string? Glb, string? Error, float[] Matrix,
    float[]? Translation, float[]? Rotation, float[]? Scale)
{
    internal bool Drawn => Glb is not null && Error is null;

    /// <summary>The row's transform; a shearing matrix (no TRS in the file) keeps its closest decomposition.</summary>
    internal Transform Transform()
    {
        if (Translation is { Length: 3 } t && Rotation is { Length: 4 } r && Scale is { Length: 3 } s)
            return new Transform(new Vector3(t[0], t[1], t[2]), new Quaternion(r[0], r[1], r[2], r[3]), new Vector3(s[0], s[1], s[2]));
        float[] m = Matrix; // column-major
        Matrix4x4 matrix = new(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15]);
        return Matrix4x4.Decompose(matrix, out Vector3 scale, out Quaternion rotation, out Vector3 translation)
            ? new Transform(translation, rotation, scale)
            : throw new InvalidOperationException($"Placement of {Glb} has a degenerate matrix.");
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PlacementFile))]
internal sealed partial class PlacementJson : JsonSerializerContext;
