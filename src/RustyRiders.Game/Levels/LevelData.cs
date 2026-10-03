using System.Text.Json;
using System.Text.Json.Serialization;
using Rusty.Engine;

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
        Read(engine, "levels/generator.json", LevelJson.Default.GeneratorSettings),
        Read(engine, "levels/tile-kinds.json", LevelJson.Default.TileKindTemplateArray),
        Read(engine, "levels/chunks.json", LevelJson.Default.ChunkDefinitionArray),
        Read(engine, "levels/layouts.json", LevelJson.Default.LayoutDefinitionArray),
        Read(engine, $"levels/tilesets/{tileset}.json", LevelJson.Default.TilesetDefinition));

    private static T Read<T>(IEngineContext engine, string path, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type) =>
        JsonSerializer.Deserialize(ContentFiles.Read(engine, path).Span, type)
        ?? throw new InvalidOperationException($"content/{path} is empty.");
}

/// <summary>Which layout, tileset and seed to stamp, from content/level.json.</summary>
internal sealed record LevelSettings(string ArtRoot, string Tileset, string Layout, int Seed, float WallHeight,
    float[] BackgroundColor)
{
    internal const string Path = "level.json";

    internal static LevelSettings Load(IEngineContext engine) =>
        JsonSerializer.Deserialize(ContentFiles.Read(engine, Path).Span, LevelJson.Default.LevelSettings)
        ?? throw new InvalidOperationException($"{Path} must contain the level settings.");
}

internal sealed record GeneratorSettings(float CellSize, int SectorSize, double ChangeCorridorDirectionChance, int MaxCorridorSteps);

internal sealed record TileKindTemplate(string Kind, string[] Walkable);

internal sealed record ChunkDefinition(string Id, ChunkCell[] Cells);

/// <summary>A chunk cell: a room cell, or a door cell (<see cref="Door"/> = the side it opens away from the room).</summary>
internal sealed record ChunkCell(int X, int Z, string? Door, string? Objective);

internal sealed record LayoutDefinition(string Id, bool Special, LayoutNode[] Nodes);

internal sealed record LayoutNode(int Id, int X, int Z, string[] Tags, LayoutConnection[] Connections)
{
    internal bool Has(string tag) => Tags.Contains(tag);
}

internal sealed record LayoutConnection(string Dir, int Target, bool Locked);

/// <summary>Tile prefabs per tile kind (old-game prefab paths, the names of their placement files) and their 9x9 walkability.</summary>
internal sealed record TilesetDefinition(string Id, Dictionary<string, string[]> Tiles, Dictionary<string, string[]> Walkability);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GeneratorSettings))]
[JsonSerializable(typeof(TileKindTemplate[]))]
[JsonSerializable(typeof(ChunkDefinition[]))]
[JsonSerializable(typeof(LayoutDefinition[]))]
[JsonSerializable(typeof(TilesetDefinition))]
[JsonSerializable(typeof(LevelSettings))]
internal sealed partial class LevelJson : JsonSerializerContext;
