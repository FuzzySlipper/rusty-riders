using System.Text.Json.Serialization;
using Rusty.Engine;
using RustyRiders.Game.Content;

namespace RustyRiders.Game.Levels.Kit;

/// <summary>What a piece of the room plan's open ground is: a room, a corridor, or a doorway through a room's wall.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PlanArea>))]
internal enum PlanArea { Room, Corridor, Doorway }

/// <summary>Which of a kit's materials a piece takes.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<KitSurface>))]
internal enum KitSurface { Wall, Floor, Ceiling, Trim, Runner }

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

    /// <summary>
    /// The area whose ceiling this is: corridors and doorways have their own heights, and every room is taller than a
    /// corridor (rooms of a kit's families vary).
    /// </summary>
    internal PlanArea Area(float height) => height == CorridorHeight ? PlanArea.Corridor : height == DoorHeight ? PlanArea.Doorway : PlanArea.Room;
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
/// ceilings and trims take (Assets-relative .mat paths, as the converted GLBs name them), metres
/// per texture repeat on each, how they are drawn, and its typed parts: trims swept along the outlines and pilasters
/// set against room walls, an arch over every doorway, and <see cref="Dressing"/>: the old game's props placed over the
/// plan by typed rules. Its details (KitDetailDefinitions.cs) build toward the world's concept scene:
/// <see cref="Runners"/> on the floor, <see cref="Ribs"/> across ceilings, <see cref="BlindArches"/> on walls and
/// <see cref="Sconces"/> lighting the doorways, its own <see cref="Columns"/>, <see cref="WallStones"/> laid on its walls and
/// the <see cref="Voussoirs"/> its arches and ribs may be built of. Converted models of each tile prefab whose file names start with one of <see cref="Props"/>
/// stay where the tile prefab put them.
/// </summary>
internal sealed record KitRecipe(string WallMaterial, string FloorMaterial, string CeilingMaterial, string TrimMaterial,
    float WallMetres, float FloorMetres, float CeilingMetres, float TrimMetres, float NormalScale, float Roughness,
    TrimDefinition[] Trims, PilasterDefinition? Pilasters, string[] Props, ArchDefinition? DoorArch = null,
    DressingDefinition[]? Dressing = null, RoomFamily[]? Rooms = null, KitTextures? Textures = null, RunnerDefinition? Runners = null,
    RibDefinition? Ribs = null, BlindArchDefinition? BlindArches = null, SconceDefinition? Sconces = null, ColumnsDefinition? Columns = null,
    WallStonesDefinition? WallStones = null, VoussoirDefinition? Voussoirs = null)
{
    internal static KitRecipe? Load(IEngineContext engine, string tileset, PlanRules plan, float cellSize)
    {
        string path = $"levels/kits/{tileset}.json";
        if (Authored.ReadOptional(engine, path, LevelKitJson.Default.KitRecipe) is not { } recipe) return null;
        recipe.DoorArch?.Validate(path, plan);
        foreach (KitSurface surface in Enum.GetValues<KitSurface>())
            if (recipe.Textures?.For(surface) is { } texture) Authored.Positive(path, $"textures.{surface}.metres", texture.Metres);
        for (int i = 0; i < (recipe.Rooms?.Length ?? 0); i++) recipe.Rooms![i].Validate(path, $"rooms[{i}]", plan, cellSize);
        for (int i = 0; i < (recipe.Dressing?.Length ?? 0); i++) recipe.Dressing![i].Validate(path, $"dressing[{i}]");
        Authored.Positive(path, "wallMetres", recipe.WallMetres);
        Authored.Positive(path, "floorMetres", recipe.FloorMetres);
        Authored.Positive(path, "ceilingMetres", recipe.CeilingMetres);
        Authored.Positive(path, "trimMetres", recipe.TrimMetres);
        for (int i = 0; i < recipe.Trims.Length; i++) recipe.Trims[i].Validate(path, $"trims[{i}]");
        recipe.Pilasters?.Validate(path);
        recipe.Runners?.Validate(path, plan);
        Authored.Require(recipe.Runners is null || recipe.Textures?.Runner is not null, path, "textures.runner", "runners need a runner texture.");
        recipe.Ribs?.Validate(path, recipe.Pilasters);
        recipe.BlindArches?.Validate(path, recipe.Pilasters);
        recipe.Sconces?.Validate(path, plan);
        recipe.Columns?.Validate(path);
        recipe.WallStones?.Validate(path);
        recipe.Voussoirs?.Validate(path);
        foreach ((string field, bool stones, float face) in (IEnumerable<(string, bool, float)>)[
            ("ribs.stones", recipe.Ribs?.Stones ?? false, (recipe.Ribs?.Width ?? 0) / 2),
            ("doorArch.ring.stones", recipe.DoorArch?.Ring?.Stones ?? false, recipe.DoorArch?.Ring?.Depth ?? 0),
            ("blindArches.ring.stones", recipe.BlindArches?.Ring.Stones ?? false, recipe.BlindArches?.Ring.Depth ?? 0)])
            if (stones)
            {
                Authored.Require(recipe.Voussoirs is not null, path, field, "needs the kit's voussoirs.");
                recipe.Voussoirs!.Fits(path, field, face);
            }
        return recipe;
    }

    /// <summary>The metres per texture repeat of a surface: its generated texture's, or its old material's.</summary>
    internal float Metres(KitSurface surface) => Textures?.For(surface)?.Metres ?? surface switch
    {
        KitSurface.Wall => WallMetres,
        KitSurface.Floor => FloorMetres,
        KitSurface.Ceiling => CeilingMetres,
        KitSurface.Runner => FloorMetres,
        _ => TrimMetres,
    };

    internal bool KeepsProp(string? model) => model is not null
        && Props.Any(prefix => System.IO.Path.GetFileName(model).StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// A moulding or ledge swept along walls: from the floor, along the walls of the plan's <see cref="Areas"/> and ending
/// square, with its end capped, where a wall of another area begins (so a ledge stops at a doorway's jamb); or from the
/// ceiling, around the outline of its one area (so a cornice runs on over a doorway). <see cref="Profile"/> is
/// [out from the wall, up from the anchor] points in metres, running from the wall face to the anchor (a cornice's
/// heights are negative). It takes the kit's <see cref="Surface"/> material, and <see cref="Collides"/> when it should
/// stop the walker (a ledge, not a thin moulding).
/// </summary>
internal sealed record TrimDefinition(string Id, TrimAnchor Anchor, PlanArea[] Areas, float[][] Profile, KitSurface Surface = KitSurface.Trim,
    bool Collides = false)
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

/// <summary>
/// Generated textures in place of a surface's old material texture (content-root paths, repeating every
/// <see cref="SurfaceTexture.Metres"/>). A colour texture is drawn as painted, without the old material's colour.
/// </summary>
internal sealed record KitTextures(SurfaceTexture? Wall = null, SurfaceTexture? Floor = null, SurfaceTexture? Ceiling = null, SurfaceTexture? Trim = null,
    SurfaceTexture? Runner = null)
{
    internal SurfaceTexture? For(KitSurface surface) => surface switch
    {
        KitSurface.Wall => Wall,
        KitSurface.Floor => Floor,
        KitSurface.Ceiling => Ceiling,
        KitSurface.Runner => Runner,
        _ => Trim,
    };
}

/// <summary>
/// An arch over every doorway. The doorway's straight sides stand to the plan's door height, the springing line, and
/// its head rises <see cref="Rise"/> above that in a half-ellipse across the door's width (a round arch when the rise is
/// half the width), drawn with <see cref="Segments"/> straight pieces. The arched soffit through the wall takes
/// <see cref="Surface"/>. An optional <see cref="Ring"/> of voussoirs stands out round the arch on the room side.
/// </summary>
internal sealed record ArchDefinition(float Rise, int Segments, KitSurface Surface, ArchRingDefinition? Ring = null)
{
    internal void Validate(string path, PlanRules plan)
    {
        Authored.Positive(path, "doorArch.rise", Rise);
        Authored.Require(Segments >= 2, path, "doorArch.segments", "must be at least 2.");
        Authored.Require(plan.DoorHeight + Rise + (Ring?.Width ?? 0) < plan.CorridorHeight, path, "doorArch.rise",
            $"the arch (from the plan's door height, {plan.DoorHeight} m, with its ring) must stay under the corridor ceiling, {plan.CorridorHeight} m.");
        if (Ring is { } ring)
        {
            Authored.Positive(path, "doorArch.ring.width", ring.Width);
            Authored.Positive(path, "doorArch.ring.depth", ring.Depth);
        }
    }
}

/// <summary>
/// A band <see cref="Width"/> wide round an arch, standing <see cref="Depth"/> out from the room's wall; with
/// <see cref="Stones"/> and the kit's voussoirs, built of voussoirs, otherwise one smooth band.
/// </summary>
internal sealed record ArchRingDefinition(float Width, float Depth, KitSurface Surface, bool Stones = false);

/// <summary>
/// A kind of room a world's kit builds. Each room of a level takes one of the families whose size range fits its cells,
/// picked by the level's seed in proportion to <see cref="Weight"/>. A family sets the room's ceiling
/// <see cref="Height"/>, cuts each corner the room turns at diagonally by <see cref="Chamfer"/> metres along both walls
/// (beyond the wall band), and says whether the room's hall <see cref="Columns"/> stand.
/// </summary>
internal sealed record RoomFamily(string Id, float Weight, float Height, float Chamfer = 0, bool Columns = false, int MinCells = 1,
    int MaxCells = int.MaxValue)
{
    internal void Validate(string path, string field, PlanRules plan, float cellSize)
    {
        Authored.Positive(path, $"{field}.weight", Weight);
        Authored.Require(Height > plan.CorridorHeight, path, $"{field}.height", $"rooms must be taller than corridors ({plan.CorridorHeight} m).");
        Authored.AtLeast(path, $"{field}.chamfer", Chamfer, 0);
        // A chamfer stops short of a doorway, which is centred on the cell's side.
        float reach = (cellSize - plan.DoorWidth) / 2 - plan.WallMetres;
        Authored.Require(Chamfer < reach, path, $"{field}.chamfer", $"must stay clear of doorways: under {reach} m.");
        Authored.Require(MinCells >= 1 && MaxCells >= MinCells, path, $"{field}.minCells", "must be at least 1 and no more than maxCells.");
    }
}

/// <summary>Where a dressing rule puts its props on the room plan.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DressingRule>))]
internal enum DressingRule
{
    /// <summary>A free-standing column at every level-cell corner with the same room on all four sides.</summary>
    HallColumns,
    /// <summary>A pier in every corner of a room, against both walls.</summary>
    RoomCorners,
    /// <summary>Props against room walls, midway between pilaster places, kept clear of openings.</summary>
    WallNiches,
    /// <summary>Clusters of small props scattered at the foot of room and corridor walls.</summary>
    Rubble,
}

/// <summary>
/// Props from the old game placed over the plan by a typed <see cref="Rule"/>, each picking one of <see cref="Models"/>
/// (converted GLBs under the art root) by the level's seed. <see cref="Footprint"/> and <see cref="Height"/> are the props'
/// size in metres, used to set them against walls and, when they <see cref="Collides"/>, as a box the walker meets.
/// <see cref="Turn"/> (degrees) turns a model so its front faces into the room; <see cref="Lift"/> raises it (a niche sits
/// on the ledge). <see cref="Chance"/> is how likely each place is to be dressed; <see cref="Spacing"/> spaces wall props;
/// <see cref="Count"/> is the most props in a rubble cluster. A wall prop stands <see cref="Depth"/> deep against the wall.
/// </summary>
internal sealed record DressingDefinition(string Id, DressingRule Rule, string[] Models, float Footprint, float Height, bool Collides = false,
    float Turn = 0, float Lift = 0, float Chance = 1, float Spacing = 0, int Count = 1, float Depth = 0)
{
    internal void Validate(string path, string field)
    {
        Authored.Require(Models.Length > 0, path, $"{field}.models", "must name at least one model.");
        Authored.Positive(path, $"{field}.footprint", Footprint);
        Authored.Positive(path, $"{field}.height", Height);
        Authored.Within(path, $"{field}.chance", Chance, 0, 1);
        Authored.AtLeast(path, $"{field}.lift", Lift, 0);
        Authored.Require(Rule != DressingRule.WallNiches || (Spacing > 0 && Depth > 0), path, $"{field}.spacing", "wall props need a spacing and a depth.");
        Authored.Require(Rule != DressingRule.Rubble || Count >= 1, path, $"{field}.count", "a rubble cluster has at least one prop.");
    }
}

// Authored: missing constructor values, nulls in non-nullable fields and unknown members are errors, not defaults.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(PlanRules))]
[JsonSerializable(typeof(KitRecipe))]
internal sealed partial class LevelKitJson : JsonSerializerContext;
