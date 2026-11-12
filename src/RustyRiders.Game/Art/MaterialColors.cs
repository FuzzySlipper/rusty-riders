using System.Numerics;
using System.Text.Json.Serialization;
using Rusty.Engine;
using RustyRiders.Game.Content;

namespace RustyRiders.Game.Art;

/// <summary>
/// Base colours for converted Unity materials whose colour the import took from the wrong property
/// (content/levels/material-colors.json), applied to every level build. <see cref="MaterialColor.Note"/> says why each
/// is needed. Rift Riders has no level palettes (Patch, 2026-11-12): a texture carries its own colour.
/// </summary>
internal sealed record MaterialColors(MaterialColor[] Materials)
{
    internal const string Path = "levels/material-colors.json";

    internal static MaterialColors Load(IEngineContext engine)
    {
        MaterialColors colors = Authored.Read(engine, Path, MaterialColorsJson.Default.MaterialColors);
        for (int i = 0; i < colors.Materials.Length; i++) Authored.Colour(Path, $"materials[{i}].baseColor", colors.Materials[i].BaseColor);
        return colors;
    }

    /// <summary>The corrections as one recolour of the converted art; none when there are none.</summary>
    internal MaterialRecolor? Recolor()
    {
        if (Materials.Length == 0) return null;
        MaterialRecolor recolor = new("corrections");
        foreach (MaterialColor material in Materials)
            recolor.Set(material.Material, new Vector4(material.BaseColor[0], material.BaseColor[1], material.BaseColor[2], 1), null);
        return recolor;
    }
}

/// <summary>A Unity material (Assets-relative .mat path, as the converted GLBs name it) and the gamma-encoded rgb it should have.</summary>
internal sealed record MaterialColor(string Material, float[] BaseColor, string Note);

// Authored: missing constructor values, nulls in non-nullable fields and unknown members are errors, not defaults.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(MaterialColors))]
internal sealed partial class MaterialColorsJson : JsonSerializerContext;
