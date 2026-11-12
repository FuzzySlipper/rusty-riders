using System.Text.Json.Serialization;
using Rusty.Engine;
using RustyRiders.Game.Content;

namespace RustyRiders.Game.Levels.Kit;

/// <summary>What a piece of the room plan's open ground is: a room, a corridor, or a doorway through a room's wall.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PlanArea>))]
internal enum PlanArea { Room, Corridor, Doorway }

/// <summary>Where a trim's profile hangs: from the floor up, or from the ceiling down.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TrimAnchor>))]
internal enum TrimAnchor { Floor, Ceiling }

/// <summary>
/// How the room plan is measured out (content/levels/plan.json), in metres. The plan lays the level's rooms, corridors
/// and doorways onto a grid of <see cref="GridMetres"/> squares, a whole number of them to a level cell. A room keeps a
/// wall band <see cref="WallMetres"/> deep along each edge it does not share with itself. A corridor is
/// <see cref="CorridorWidth"/> wide down the middle of its cells, and a doorway is <see cref="DoorWidth"/> wide through
/// the room's wall band. Each area has its own ceiling, and they must differ, so a builder can tell them apart by height.
/// </summary>
internal sealed record PlanRules(float GridMetres, float WallMetres, float CorridorWidth, float DoorWidth, float RoomHeight,
    float CorridorHeight, float DoorHeight, PlanOverlayLook Overlay)
{
    internal const string Path = "levels/plan.json";

    internal static PlanRules Load(IEngineContext engine)
    {
        PlanRules rules = Authored.Read(engine, Path, LevelKitJson.Default.PlanRules);
        Authored.Positive(Path, "gridMetres", rules.GridMetres);
        Authored.Positive(Path, "wallMetres", rules.WallMetres);
        Authored.Positive(Path, "corridorWidth", rules.CorridorWidth);
        Authored.Positive(Path, "doorWidth", rules.DoorWidth);
        Authored.Require(rules.DoorWidth <= rules.CorridorWidth, Path, "doorWidth", "must not be wider than corridorWidth.");
        Authored.Require(rules.RoomHeight > rules.CorridorHeight && rules.CorridorHeight > rules.DoorHeight && rules.DoorHeight > 0,
            Path, "roomHeight", "rooms must be taller than corridors, and corridors taller than doorways.");
        rules.Overlay.Validate();
        return rules;
    }

    internal float Height(PlanArea area) => area switch
    {
        PlanArea.Room => RoomHeight,
        PlanArea.Corridor => CorridorHeight,
        _ => DoorHeight,
    };
}

/// <summary>The plan overlay: the plan's open ground drawn flat and unlit, coloured by area, <see cref="Lift"/> above the floor.</summary>
internal sealed record PlanOverlayLook(bool Show, float Lift, float[] RoomColor, float[] CorridorColor, float[] DoorwayColor)
{
    internal void Validate()
    {
        Authored.AtLeast(PlanRules.Path, "overlay.lift", Lift, 0);
        Authored.Colour(PlanRules.Path, "overlay.roomColor", RoomColor);
        Authored.Colour(PlanRules.Path, "overlay.corridorColor", CorridorColor);
        Authored.Colour(PlanRules.Path, "overlay.doorwayColor", DoorwayColor);
    }

    internal float[] Color(PlanArea area) => area switch
    {
        PlanArea.Room => RoomColor,
        PlanArea.Corridor => CorridorColor,
        _ => DoorwayColor,
    };
}

/// <summary>
/// A world's parametric kit (content/levels/kits/&lt;tileset&gt;.json): the old Unity materials its walls, floors,
/// ceilings and trims take (Assets-relative .mat paths, as palettes name them, so a level's palette tints them), metres
/// per texture repeat on each, how they are drawn, and its typed parts: trims swept along the outlines and pilasters
/// set against room walls. Converted models of each tile prefab whose file names start with one of <see cref="Props"/>
/// stay as placed dressing.
/// </summary>
internal sealed record KitRecipe(string WallMaterial, string FloorMaterial, string CeilingMaterial, string TrimMaterial,
    float WallMetres, float FloorMetres, float CeilingMetres, float TrimMetres, float NormalScale, float Roughness,
    TrimDefinition[] Trims, PilasterDefinition? Pilasters, string[] Props)
{
    internal static KitRecipe? Load(IEngineContext engine, string tileset)
    {
        string path = $"levels/kits/{tileset}.json";
        if (Authored.ReadOptional(engine, path, LevelKitJson.Default.KitRecipe) is not { } recipe) return null;
        Authored.Positive(path, "wallMetres", recipe.WallMetres);
        Authored.Positive(path, "floorMetres", recipe.FloorMetres);
        Authored.Positive(path, "ceilingMetres", recipe.CeilingMetres);
        Authored.Positive(path, "trimMetres", recipe.TrimMetres);
        for (int i = 0; i < recipe.Trims.Length; i++) recipe.Trims[i].Validate(path, $"trims[{i}]");
        recipe.Pilasters?.Validate(path);
        return recipe;
    }

    internal bool KeepsProp(string? model) => model is not null
        && Props.Any(prefix => System.IO.Path.GetFileName(model).StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// A moulding swept along the outline of the plan's <see cref="Areas"/>: a skirting from the floor, or a cornice from
/// the ceiling. <see cref="Profile"/> is [out from the wall, up from the anchor] points in metres, running from the wall
/// face to the anchor (a cornice's heights are negative).
/// </summary>
internal sealed record TrimDefinition(string Id, TrimAnchor Anchor, PlanArea[] Areas, float[][] Profile)
{
    internal void Validate(string path, string field)
    {
        Authored.Require(Areas.Length > 0, path, $"{field}.areas", "must name at least one area.");
        Authored.Require(Anchor == TrimAnchor.Floor || Areas.Length == 1, path, $"{field}.areas", "a ceiling trim follows one area's outline, at one height.");
        Authored.Require(Profile.Length >= 2 && Profile.All(point => point.Length == 2 && point.All(float.IsFinite)), path, $"{field}.profile",
            "must be at least two [out, up] points.");
        Authored.Require(Profile[0][0] == 0 && Profile[^1][1] == 0, path, $"{field}.profile", "must start on the wall face (out 0) and end on its anchor (up 0).");
    }
}

/// <summary>
/// Flat pilasters against the walls of the plan's <see cref="Areas"/>: <see cref="Width"/> wide, standing
/// <see cref="Depth"/> out from the wall, floor to ceiling, about every <see cref="Spacing"/> metres along a wall and no
/// nearer than <see cref="Clearance"/> to its ends; none stands in front of an opening.
/// </summary>
internal sealed record PilasterDefinition(PlanArea[] Areas, float Spacing, float Width, float Depth, float Clearance)
{
    internal void Validate(string path)
    {
        Authored.Require(Areas.Length > 0, path, "pilasters.areas", "must name at least one area.");
        Authored.Positive(path, "pilasters.spacing", Spacing);
        Authored.Positive(path, "pilasters.width", Width);
        Authored.Positive(path, "pilasters.depth", Depth);
        Authored.AtLeast(path, "pilasters.clearance", Clearance, Width / 2);
    }
}

// Authored: missing constructor values, nulls in non-nullable fields and unknown members are errors, not defaults.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(PlanRules))]
[JsonSerializable(typeof(KitRecipe))]
internal sealed partial class LevelKitJson : JsonSerializerContext;
