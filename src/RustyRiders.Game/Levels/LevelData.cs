using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rusty.Engine;
using RustyRiders.Game.Content;
using RustyRiders.Game.Art;

namespace RustyRiders.Game.Levels;

/// <summary>
/// The old game's procedural-level data, translated by scripts/extract-level-data.py into content/levels/.
/// Grid conventions are the old game's (Unity space): x east, z north, rotation r = r * 90 degrees clockwise
/// seen from above, 3x3 / 9x9 grids row-major with row 0 at north and column 0 at west.
/// </summary>
internal sealed record LevelData(GeneratorSettings Generator, TileKindTemplate[] TileKinds, ChunkDefinition[] Chunks,
    LayoutDefinition[] Layouts, TilesetDefinition Tileset)
{
    internal static LevelData Load(IEngineContext engine, string tileset) => new(
        Authored.Read(engine, "levels/generator.json", LevelJson.Default.GeneratorSettings),
        Authored.Read(engine, "levels/tile-kinds.json", LevelJson.Default.TileKindTemplateArray),
        Authored.Read(engine, "levels/chunks.json", LevelJson.Default.ChunkDefinitionArray),
        Authored.Read(engine, "levels/layouts.json", LevelJson.Default.LayoutDefinitionArray),
        Authored.Read(engine, $"levels/tilesets/{tileset}.json", LevelJson.Default.TilesetDefinition));
}

/// <summary>
/// Which layout, tileset and seed to stamp, from content/level.json. Without a palette the seed picks one. The build
/// is <c>tiles</c> (the old tile art), <c>shells</c> (an extracted implicit field) or <c>sweeps</c> (swept UV-mapped
/// meshes); both generated builds keep the tile art's props and follow <see cref="ShellDefinition"/>, whose floor
/// texture <see cref="FloorTexture"/> may replace by id.
/// </summary>
internal sealed record LevelSettings(string ArtRoot, string Tileset, string Layout, int Seed, float WallHeight,
    float[] BackgroundColor, string? Palette = null, string Build = LevelSettings.TilesBuild, string? FloorTexture = null)
{
    internal const string TilesBuild = "tiles";
    internal const string ShellsBuild = "shells";
    internal const string SweepsBuild = "sweeps";

    /// <summary>The build after this one, as B cycles them.</summary>
    internal string NextBuild => Build switch { TilesBuild => ShellsBuild, ShellsBuild => SweepsBuild, _ => TilesBuild };

    internal const string Path = "level.json";

    internal static LevelSettings Load(IEngineContext engine) => Authored.Read(engine, Path, LevelJson.Default.LevelSettings);
}

/// <summary>
/// How a tileset's levels are rebuilt as generated shells (content/levels/shells/&lt;tileset&gt;.json): the old
/// materials the floor and walls take (Assets-relative .mat paths, as palettes name them), how they are drawn, the
/// room and corridor ceilings, the smoothing and wave displacement of the walls and floor, extraction detail, and
/// which converted models of each tile prefab stay as props (model file names starting with one of
/// <see cref="Props"/>). <see cref="Extraction"/> is <c>implicit</c> or <c>sampled</c> (see <see cref="LevelShells"/>);
/// <see cref="BlockMetres"/> sizes sampled blocks, and zero limits mean the Engine defaults. Lengths are metres.
/// </summary>
internal sealed record ShellDefinition(string FloorMaterial, string WallMaterial, float TextureMetres, float TriplanarSharpness,
    float NormalScale, float Roughness, float RoomHeight, float CorridorHeight, float FloorMaterialTop, float BlendRadius,
    ShellNoise WallNoise, ShellNoise FloorNoise, string Extraction, float SampleSpacing, float BlockMetres, uint MaxSamples,
    float CreaseDegrees, uint MaxVertices, uint MaxTriangles, string[] Props, SweepDefinition Sweep, FloorTexture[] FloorTextures)
{
    internal static ShellDefinition? Load(IEngineContext engine, string tileset) =>
        Authored.ReadOptional(engine, $"levels/shells/{tileset}.json", LevelJson.Default.ShellDefinition);

    internal bool KeepsProp(string? model) => model is not null
        && Props.Any(prefix => System.IO.Path.GetFileName(model).StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// A floor texture to try in place of the floor material's own (content-root paths), repeating every
/// <see cref="Metres"/>; <see cref="Source"/> says where it came from. <c>level.json</c>'s <c>floorTexture</c>, or V,
/// picks one by id.
/// </summary>
internal sealed record FloorTexture(string Id, string Albedo, string? Normal, float Metres, string Source)
{
    internal Art.ReplacementTextures Replacement => new(Albedo, Normal);
}

/// <summary>
/// The swept build (<see cref="LevelSweeps"/>): metres per texture repeat on walls and ceilings and on the floor,
/// the corner rounding radius, the wall bulge (± metres) and its wavelength, and the wall grid's column and row spacing.
/// </summary>
internal sealed record SweepDefinition(float WallMetres, float FloorMetres, float CornerRadius, float Bulge, float BulgeWavelength,
    float ColumnMetres);

/// <summary>Engine wave displacement: cycles per metre across and up, peak field change, and its octaves.</summary>
internal sealed record ShellNoise(float Frequency, float VerticalFrequency, float Amplitude, uint Octaves, float Lacunarity, float Gain)
{
    internal Vector3 Frequencies => new(Frequency, VerticalFrequency, Frequency);
}

internal sealed record GeneratorSettings(float CellSize, int SectorSize, double ChangeCorridorDirectionChance, int MaxCorridorSteps);

internal sealed record TileKindTemplate(string Kind, string[] Walkable);

internal sealed record ChunkDefinition(string Id, ChunkCell[] Cells);

/// <summary>A chunk cell: a room cell, or a door cell (<see cref="Door"/> = the side it opens away from the room).</summary>
internal sealed record ChunkCell(int X, int Z, string? Door = null, string? Objective = null);

internal sealed record LayoutDefinition(string Id, bool Special, LayoutNode[] Nodes);

internal sealed record LayoutNode(int Id, int X, int Z, string[] Tags, LayoutConnection[] Connections)
{
    internal bool Has(string tag) => Tags.Contains(tag);
}

internal sealed record LayoutConnection(string Dir, int Target, bool Locked = false);

/// <summary>Tile prefabs per tile kind (old-game prefab paths, the names of their placement files) and their 9x9 walkability.</summary>
internal sealed record TilesetDefinition(string Id, Dictionary<string, string[]> Tiles, Dictionary<string, string[]> Walkability,
    PaletteDefinition[] Palettes);

/// <summary>An old PrefabMaterialsColors palette; the old game recoloured a level's materials with one of its tileset's.</summary>
internal sealed record PaletteDefinition(string Id, PaletteEntry[] Entries)
{
    /// <summary>The old PaintLevel: each listed material gets Material.color, and _EmissionColor when its alpha is above 0.</summary>
    internal MaterialRecolor Recolor()
    {
        MaterialRecolor recolor = new(Id);
        foreach (PaletteEntry entry in Entries)
        {
            float[] e = entry.Emissive;
            foreach (string material in entry.Materials)
                recolor.Set(material, new Vector4(entry.BaseColor[0], entry.BaseColor[1], entry.BaseColor[2], entry.BaseColor[3]),
                    e[3] > 0 ? new Vector3(e[0], e[1], e[2]) : null);
        }
        return recolor;
    }
}

/// <summary>
/// Colours as Unity stored them (gamma-encoded rgba) and the Assets-relative materials they recolour.
/// <see cref="MissingMaterials"/> keeps the names the old palette listed that no material has; they recolour nothing.
/// </summary>
internal sealed record PaletteEntry(float[] BaseColor, float[] Emissive, string[] Materials, string[]? MissingMaterials = null);

// Generated by scripts/extract-level-data.py and then authored here.
// Authored: missing constructor values, nulls in non-nullable fields and unknown members are errors, not defaults.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(GeneratorSettings))]
[JsonSerializable(typeof(TileKindTemplate[]))]
[JsonSerializable(typeof(ChunkDefinition[]))]
[JsonSerializable(typeof(LayoutDefinition[]))]
[JsonSerializable(typeof(TilesetDefinition))]
[JsonSerializable(typeof(LevelSettings))]
[JsonSerializable(typeof(ShellDefinition))]
internal sealed partial class LevelJson : JsonSerializerContext;
