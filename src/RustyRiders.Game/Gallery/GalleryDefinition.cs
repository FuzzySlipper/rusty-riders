using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rusty.Engine;

namespace RustyRiders.Game.Gallery;

/// <summary>The authored gallery: which converted placement files to show, in rows, and how to walk them.</summary>
internal sealed record GalleryDefinition(string ArtRoot, float ExhibitGap, float RowGap, float GroundMargin,
    float[] Spawn, float SpawnYawDegrees, float[] GroundColor, float[] BackgroundColor,
    WalkerTuning Walker, GalleryRow[] Rows)
{
    internal const string Path = "gallery.json";

    internal static GalleryDefinition Load(IEngineContext engine) =>
        JsonSerializer.Deserialize(ContentFiles.Read(engine, Path).Span, GalleryJson.Default.GalleryDefinition)
        ?? throw new InvalidOperationException($"{Path} must contain the gallery definition.");

    internal static Vector3 Vector(float[] xyz) => xyz.Length == 3
        ? new Vector3(xyz[0], xyz[1], xyz[2])
        : throw new InvalidOperationException("Gallery coordinates must have three components.");

    internal static Color Color(float[] rgb) => rgb.Length == 3
        ? new Color(rgb[0], rgb[1], rgb[2], 1)
        : throw new InvalidOperationException("Gallery colours must have three components.");
}

internal sealed record GalleryRow(string Label, string[] Exhibits);

internal sealed record WalkerTuning(float Height, float CrouchedHeight, float Radius, float Speed, float SprintSpeed,
    float JumpSpeed, float Gravity, float MaximumStepHeight, float MaximumSlopeDegrees, float PointerRadiansPerUnit,
    float FlySpeed, float FlySprintSpeed, float FieldOfViewDegrees, float FarPlane);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GalleryDefinition))]
internal sealed partial class GalleryJson : JsonSerializerContext;
