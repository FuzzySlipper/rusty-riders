using System.Text.Json.Serialization;
using Rusty.Engine;
using RustyRiders.Game.Content;

namespace RustyRiders.Game.Gallery;

/// <summary>The authored gallery: which converted placement files to show, in rows, and how to walk them.</summary>
internal sealed record GalleryDefinition(string ArtRoot, float ExhibitGap, float RowGap, float GroundMargin,
    float[] Spawn, float SpawnYawDegrees, float[] GroundColor, float[] BackgroundColor, GalleryRow[] Rows)
{
    internal const string Path = "gallery.json";

    internal static GalleryDefinition Load(IEngineContext engine) => Authored.Read(engine, Path, GalleryJson.Default.GalleryDefinition);
}

internal sealed record GalleryRow(string Label, string[] Exhibits);

// Authored: missing constructor values, nulls in non-nullable fields and unknown members are errors, not defaults.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(GalleryDefinition))]
internal sealed partial class GalleryJson : JsonSerializerContext;
