using RustyRiders.Game.Content;

namespace RustyRiders.Game.Levels.Kit;

/// <summary>
/// Runner strips along the floor from each doorway into the middle of its room, of the kit's
/// <see cref="KitSurface.Runner"/> material: <see cref="Width"/> wide, laid <see cref="Lift"/> above the floor (crossing
/// strips each a little higher, so none flickers through another), meeting in a square of the same width at the room's
/// middle when that is open floor (<see cref="KitRunners"/>).
/// </summary>
internal sealed record RunnerDefinition(float Width, float Lift)
{
    internal void Validate(string path, PlanRules plan)
    {
        Authored.Positive(path, "runners.width", Width);
        Authored.Require(Width <= plan.DoorWidth, path, "runners.width", $"must fit through a doorway: at most {plan.DoorWidth} m.");
        Authored.Positive(path, "runners.lift", Lift);
    }
}

/// <summary>
/// Ribs across room ceilings, springing from opposite pilasters (<see cref="CeilingRibs"/>): <see cref="Width"/> wide and
/// <see cref="Depth"/> deep under the ceiling in the middle of their span, curving down <see cref="Haunch"/> more to the
/// walls, drawn with <see cref="Segments"/> pieces (round at <see cref="Pointed"/> 0, blending to a Gothic pointed rib
/// toward 1). With <see cref="CofferDepth"/> above zero, sculpted stone ceiling beams flank each rib to frame recessed
/// ceiling bays. They take <see cref="Surface"/>; with <see cref="Stones"/>, they are built of those voussoirs in it.
/// </summary>
internal sealed record RibDefinition(float Width, float Depth, float Haunch, int Segments, KitSurface Surface = KitSurface.Trim,
    VoussoirDefinition? Stones = null, float Pointed = 0, float CofferDepth = 0, KitSurface CofferSurface = KitSurface.Wall)
{
    internal void Validate(string path, PilasterDefinition? pilasters)
    {
        Authored.Require(pilasters is not null, path, "ribs", "ribs spring from pilasters: the kit needs pilasters.");
        Authored.Positive(path, "ribs.width", Width);
        Authored.Positive(path, "ribs.depth", Depth);
        Authored.AtLeast(path, "ribs.haunch", Haunch, 0);
        Authored.Require(Segments >= 2, path, "ribs.segments", "must be at least 2.");
        Authored.Within(path, "ribs.pointed", Pointed, 0, 1);
        Authored.AtLeast(path, "ribs.cofferDepth", CofferDepth, 0);
        Stones?.Validate(path, "ribs.stones", Width / 2);
    }
}

/// <summary>
/// Blind arches or recessed alcoves on room walls between neighbouring pilasters (<see cref="BlindArches"/>): a bay from
/// <see cref="Sill"/> up to an arched head springing at <see cref="Spring"/> (pointed by <see cref="Pointed"/>), as wide
/// as the gap between the pilasters less <see cref="Margin"/> each side, framed by a <see cref="Ring"/> with a sill
/// shelf under it. When <see cref="Recess"/> is above zero and solid wall lies behind the bay, the wall opens into a
/// real recessed alcove <see cref="Recess"/> metres deep with thick reveal jambs, arched soffit, <see cref="Panel"/> back
/// wall, and a deep stone interior shelf projecting <see cref="ShelfOut"/> into the room.
/// </summary>
internal sealed record BlindArchDefinition(float Sill, float Spring, float Margin, float TopClearance, int Segments, KitSurface Panel,
    ArchRingDefinition Ring, float Recess = 0, float ShelfOut = 0, float ShelfThickness = .22f,
    KitSurface ShelfSurface = KitSurface.Wall, float Pointed = 0)
{
    internal void Validate(string path, PilasterDefinition? pilasters)
    {
        Authored.Require(pilasters is not null, path, "blindArches", "blind arches stand between pilasters: the kit needs pilasters.");
        Authored.AtLeast(path, "blindArches.sill", Sill, 0);
        Authored.Require(Spring > Sill + 1, path, "blindArches.spring", "must be at least a metre above the sill.");
        Authored.AtLeast(path, "blindArches.margin", Margin, 0);
        Authored.AtLeast(path, "blindArches.topClearance", TopClearance, 0);
        Authored.Require(Segments >= 2, path, "blindArches.segments", "must be at least 2.");
        Authored.AtLeast(path, "blindArches.recess", Recess, 0);
        Authored.AtLeast(path, "blindArches.shelfOut", ShelfOut, 0);
        Authored.Positive(path, "blindArches.shelfThickness", ShelfThickness);
        Authored.Within(path, "blindArches.pointed", Pointed, 0, 1);
        Authored.Positive(path, "blindArches.ring.width", Ring.Width);
        Authored.Positive(path, "blindArches.ring.depth", Ring.Depth);
        Ring.Stones?.Validate(path, "blindArches.ring.stones", Ring.Depth);
        Authored.Require(pilasters is null || pilasters.Spacing - pilasters.Width - 2 * Margin - 2 * Ring.Width > 1, path, "blindArches.margin",
            "leaves no bay a metre wide between the pilasters.");
    }
}

/// <summary>
/// Wall sconces either side of each doorway on the room's side (<see cref="Sconces"/>): a bracket of the
/// <see cref="Bracket"/> material standing <see cref="BracketDepth"/> out from the wall at <see cref="Height"/>,
/// <see cref="Gap"/> beyond the door arch's ring, holding a flame <see cref="FlameSize"/> across that glows
/// <see cref="FlameColor"/> at <see cref="FlameEmission"/>, and a point light of <see cref="LightColor"/> (linear RGB) at
/// <see cref="Intensity"/> reaching <see cref="Range"/> metres with falloff <see cref="Decay"/>. An optional
/// <see cref="Pool"/> casts a crisp warm spotlight onto the floor and runner in front of the doorway and lights the
/// doorway reveal.
/// </summary>
internal sealed record SconceDefinition(float Height, float Gap, float BracketDepth, float FlameSize, float[] FlameColor, float FlameEmission,
    float[] LightColor, float Intensity, float Range, KitSurface Bracket = KitSurface.Ceiling, float Decay = 2, bool Shadows = false,
    uint ShadowResolution = 512, bool ShadowSoft = false, DoorPoolLightDefinition? Pool = null,
    float[]? FlameCoreColor = null, float FlameCoreEmission = 3.2f, float EmberRate = 10, uint EmberMax = 16, float Flicker = .08f,
    CorridorSconceDefinition? Corridors = null)
{
    internal void Validate(string path, PlanRules plan)
    {
        Authored.Require(Height > 0 && Height < plan.CorridorHeight, path, "sconces.height", $"must be above the floor and under {plan.CorridorHeight} m.");
        Authored.AtLeast(path, "sconces.gap", Gap, 0);
        Authored.Positive(path, "sconces.bracketDepth", BracketDepth);
        Authored.Positive(path, "sconces.flameSize", FlameSize);
        Authored.Colour(path, "sconces.flameColor", FlameColor);
        Authored.AtLeast(path, "sconces.flameEmission", FlameEmission, 0);
        if (FlameCoreColor is not null) Authored.Colour(path, "sconces.flameCoreColor", FlameCoreColor);
        Authored.AtLeast(path, "sconces.flameCoreEmission", FlameCoreEmission, 0);
        Authored.AtLeast(path, "sconces.emberRate", EmberRate, 0);
        Authored.Within(path, "sconces.flicker", Flicker, 0, .4f);
        Authored.Colour(path, "sconces.lightColor", LightColor);
        Authored.Positive(path, "sconces.intensity", Intensity);
        Authored.Positive(path, "sconces.range", Range);
        Authored.AtLeast(path, "sconces.decay", Decay, 0);
        Pool?.Validate(path, "sconces.pool");
        Corridors?.Validate(path);
    }
}

/// <summary>Sparse staggered non-shadowed wall sconces placed along straight corridor walls.</summary>
internal sealed record CorridorSconceDefinition(float Spacing, float Clearance, float Intensity, float Range, float Decay = 1.8f,
    float FlameScale = .85f, float EmberRate = 7, uint EmberMax = 10)
{
    internal void Validate(string path)
    {
        Authored.Positive(path, "sconces.corridors.spacing", Spacing);
        Authored.AtLeast(path, "sconces.corridors.clearance", Clearance, 0);
        Authored.Positive(path, "sconces.corridors.intensity", Intensity);
        Authored.Positive(path, "sconces.corridors.range", Range);
        Authored.AtLeast(path, "sconces.corridors.decay", Decay, 0);
        Authored.Positive(path, "sconces.corridors.flameScale", FlameScale);
        Authored.AtLeast(path, "sconces.corridors.emberRate", EmberRate, 0);
    }
}

/// <summary>
/// Procedural kit-native dressing (<see cref="KitProps"/>): palette-matched angular rubble chunks, recessed alcove
/// storytelling vignettes (votive candles/vessels, draped banners with leaned spears, reliquaries), and sparse
/// doorway threshold relics, built directly into the kit's banded meshes.
/// </summary>
internal sealed record KitPropsDefinition(RubblePropsDefinition? Rubble = null, AlcovePropsDefinition? Alcoves = null,
    ThresholdPropsDefinition? Thresholds = null)
{
    internal void Validate(string path)
    {
        Rubble?.Validate(path);
        Alcoves?.Validate(path);
        Thresholds?.Validate(path);
    }
}

internal sealed record RubblePropsDefinition(float Chance, int MaxCluster, float MinSize, float MaxSize)
{
    internal void Validate(string path)
    {
        Authored.Within(path, "kitProps.rubble.chance", Chance, 0, 1);
        Authored.Require(MaxCluster >= 1, path, "kitProps.rubble.maxCluster", "must be at least 1.");
        Authored.Positive(path, "kitProps.rubble.minSize", MinSize);
        Authored.AtLeast(path, "kitProps.rubble.maxSize", MaxSize, MinSize);
    }
}

internal sealed record AlcovePropsDefinition(float Chance, float CandleLightIntensity = 0, float CandleLightRange = 3.5f)
{
    internal void Validate(string path)
    {
        Authored.Within(path, "kitProps.alcoves.chance", Chance, 0, 1);
        Authored.AtLeast(path, "kitProps.alcoves.candleLightIntensity", CandleLightIntensity, 0);
        Authored.Positive(path, "kitProps.alcoves.candleLightRange", CandleLightRange);
    }
}

internal sealed record ThresholdPropsDefinition(float Chance)
{
    internal void Validate(string path) => Authored.Within(path, "kitProps.thresholds.chance", Chance, 0, 1);
}

/// <summary>
/// A doorway's warm floor pool and reveal fill (<see cref="Sconces"/>): a downward-angled spot light throwing a crisp
/// warm pool onto the floor and runner in front of the doorway, plus an optional warm fill inside the doorway's thick
/// arch reveal.
/// </summary>
internal sealed record DoorPoolLightDefinition(float[] Color, float Intensity, float Range, float Decay, float Height, float Forward,
    float AimForward, float OuterDegrees, float Penumbra, bool Shadows = true, uint ShadowResolution = 512, float RevealIntensity = 0,
    float RevealRange = 7)
{
    internal void Validate(string path, string field)
    {
        Authored.Colour(path, $"{field}.color", Color);
        Authored.Positive(path, $"{field}.intensity", Intensity);
        Authored.Positive(path, $"{field}.range", Range);
        Authored.AtLeast(path, $"{field}.decay", Decay, 0);
        Authored.Positive(path, $"{field}.height", Height);
        Authored.Within(path, $"{field}.outerDegrees", OuterDegrees, 5, 85);
        Authored.Within(path, $"{field}.penumbra", Penumbra, 0, 1);
        Authored.AtLeast(path, $"{field}.revealIntensity", RevealIntensity, 0);
        Authored.Positive(path, $"{field}.revealRange", RevealRange);
    }
}

/// <summary>
/// Stylized three-band French-comic cel shading applied in <c>stone-faces.wgsl</c> across all lit kit surfaces: crushes
/// deep hollows into dark shadow (<see cref="ShadowFloor"/>), holds unlit stone at a calm painted midtone
/// (<see cref="MidLevel"/>), and steps cleanly into warm torchlight pools (<see cref="HighlightBoost"/>) across
/// <see cref="Softness"/>, biasing shadows cool by <see cref="CoolShadowTint"/>.
/// </summary>
internal sealed record ComicShadingDefinition(float Strength, float ShadowCutoff, float HighlightCutoff, float Softness,
    float ShadowFloor, float MidLevel, float HighlightBoost, float CoolShadowTint)
{
    internal void Validate(string path)
    {
        Authored.Within(path, "shading.strength", Strength, 0, 1);
        Authored.AtLeast(path, "shading.shadowCutoff", ShadowCutoff, 0);
        Authored.Require(HighlightCutoff > ShadowCutoff, path, "shading.highlightCutoff", "must be above shadowCutoff.");
        Authored.Positive(path, "shading.softness", Softness);
        Authored.AtLeast(path, "shading.shadowFloor", ShadowFloor, 0);
        Authored.AtLeast(path, "shading.midLevel", MidLevel, ShadowFloor);
        Authored.Positive(path, "shading.highlightBoost", HighlightBoost);
        Authored.Within(path, "shading.coolShadowTint", CoolShadowTint, 0, 1);
    }
}

/// <summary>
/// The kit's own columns (<see cref="KitColumns"/>), in place of old props: free-standing <see cref="Hall"/> columns where
/// the HallColumns dressing rule would stand them (rooms whose family has columns), and <see cref="Corners"/> piers in
/// each square room corner. Both run floor to ceiling and collide. Optional <see cref="Stonework"/> overrides the kit's
/// wall stones for column shafts when their courses and relief differ from the walls.
/// </summary>
internal sealed record ColumnsDefinition(ColumnDefinition? Hall = null, ColumnDefinition? Corners = null,
    WallStonesDefinition? Stonework = null)
{
    internal void Validate(string path)
    {
        Hall?.Validate(path, "columns.hall");
        Corners?.Validate(path, "columns.corners");
        Stonework?.Validate(path, "columns.stonework");
    }
}

/// <summary>
/// A square column: a shaft <see cref="Width"/> across on a plinth <see cref="PlinthWidth"/> across and
/// <see cref="PlinthHeight"/> tall, under a capital <see cref="CapitalWidth"/> across and <see cref="CapitalHeight"/> tall
/// that meets the ceiling. The shaft and capital take <see cref="Surface"/>; the plinth takes <see cref="PlinthSurface"/>
/// (defaulting to <see cref="Surface"/>) and batters inward by <see cref="PlinthBatter"/> from foot to collar. With
/// <see cref="BracketReach"/> and <see cref="BracketDrop"/> above zero, sweeping curved arch brackets flare outward from
/// the upper shaft beneath the capital. With <see cref="Stones"/> and stonework, the shaft is stacked of stones round a
/// core of the stones' backing, its corners interlocking.
/// </summary>
internal sealed record ColumnDefinition(float Width, float PlinthWidth, float PlinthHeight, float CapitalWidth, float CapitalHeight,
    KitSurface Surface = KitSurface.Trim, bool Stones = false, KitSurface? PlinthSurface = null, float PlinthBatter = 0,
    float BracketReach = 0, float BracketDrop = 0, int BracketSegments = 6)
{
    internal void Validate(string path, string field)
    {
        Authored.Positive(path, $"{field}.width", Width);
        Authored.AtLeast(path, $"{field}.plinthWidth", PlinthWidth, Width);
        Authored.AtLeast(path, $"{field}.capitalWidth", CapitalWidth, Width);
        Authored.AtLeast(path, $"{field}.plinthHeight", PlinthHeight, 0);
        Authored.AtLeast(path, $"{field}.capitalHeight", CapitalHeight, 0);
        Authored.AtLeast(path, $"{field}.plinthBatter", PlinthBatter, 0);
        Authored.AtLeast(path, $"{field}.bracketReach", BracketReach, 0);
        Authored.AtLeast(path, $"{field}.bracketDrop", BracketDrop, 0);
        Authored.Require(BracketSegments >= 2, path, $"{field}.bracketSegments", "must be at least 2.");
    }

    /// <summary>The widest part at foot/head, which sets how far a corner pier stands from its walls and its collision footprint.</summary>
    internal float Footprint => MathF.Max(PlinthWidth + 2 * PlinthBatter, CapitalWidth);
}

/// <summary>
/// Walls built of real stones (<see cref="WallStones"/>) instead of a flat face with stones painted on it. Each wall
/// run, and each doorway's head above its arch, is laid in courses <see cref="MinCourse"/> to <see cref="MaxCourse"/>
/// metres tall, each course a row of <see cref="Stones"/> of the kinds' lengths, picked by weight, its joints kept clear
/// of the course below. A stone stands <see cref="MinRelief"/> to <see cref="MaxRelief"/> out from the wall, and one in
/// <see cref="PushedChance"/> stands <see cref="Pushed"/> further; <see cref="Joint"/> metres separate stones, showing
/// the wall behind in the <see cref="Backing"/> material, and the front edges are cut back by <see cref="Bevel"/>.
/// Stones take <see cref="Surface"/>, each showing its own patch of the texture. They run between the floor trims' top
/// and the ceiling trims' foot, chosen by the level's seed; they do not collide.
/// </summary>
internal sealed record WallStonesDefinition(float MinCourse, float MaxCourse, StoneKind[] Stones, float Joint, float MinRelief, float MaxRelief,
    float PushedChance, float Pushed, float Bevel, KitSurface Surface = KitSurface.Wall, KitSurface Backing = KitSurface.Ceiling)
{
    internal void Validate(string path, string field = "wallStones")
    {
        Authored.Positive(path, $"{field}.minCourse", MinCourse);
        Authored.AtLeast(path, $"{field}.maxCourse", MaxCourse, MinCourse);
        Authored.Require(Stones.Length > 0, path, $"{field}.stones", "must name at least one kind of stone.");
        for (int i = 0; i < Stones.Length; i++) Stones[i].Validate(path, $"{field}.stones[{i}]");
        Authored.Positive(path, $"{field}.joint", Joint);
        Authored.Require(Joint < MinCourse / 2, path, $"{field}.joint", "must be well under a course's height.");
        Authored.Positive(path, $"{field}.minRelief", MinRelief);
        Authored.AtLeast(path, $"{field}.maxRelief", MaxRelief, MinRelief);
        Authored.Within(path, $"{field}.pushedChance", PushedChance, 0, 1);
        Authored.AtLeast(path, $"{field}.pushed", Pushed, 0);
        Authored.AtLeast(path, $"{field}.bevel", Bevel, 0);
    }

    /// <summary>The furthest any stone stands out from its wall, which parts set on the wall stand clear of.</summary>
    internal float Deepest => MaxRelief + Pushed + Stones.Max(s => s.Pillow + s.Facet);

    /// <summary>The shortest stone of any kind.</summary>
    internal float Shortest => Stones.Min(s => s.MinLength);
}

/// <summary>
/// A kind of stone a wall is laid from: <see cref="MinLength"/> to <see cref="MaxLength"/> metres long, picked in
/// proportion to <see cref="Weight"/>, and its shape. Each corner is chipped off, with chance <see cref="ChipChance"/>,
/// by up to <see cref="Chip"/> metres along both edges; each edge takes a nick with chance <see cref="Nicks"/>. Its face
/// bulges <see cref="Pillow"/> metres at the middle, and each point round the face stands up to <see cref="Facet"/>
/// metres proud of or below the rest. A <see cref="Hewn"/> face is cut in flat facets; otherwise it is smooth.
/// </summary>
internal sealed record StoneKind(string Id, float Weight, float MinLength, float MaxLength, float ChipChance = 0, float Chip = 0, float Nicks = 0,
    float Pillow = 0, float Facet = 0, bool Hewn = false)
{
    internal void Validate(string path, string field)
    {
        Authored.Positive(path, $"{field}.weight", Weight);
        Authored.Positive(path, $"{field}.minLength", MinLength);
        Authored.AtLeast(path, $"{field}.maxLength", MaxLength, MinLength);
        Authored.Within(path, $"{field}.chipChance", ChipChance, 0, 1);
        Authored.AtLeast(path, $"{field}.chip", Chip, 0);
        Authored.Within(path, $"{field}.nicks", Nicks, 0, 1);
        Authored.AtLeast(path, $"{field}.pillow", Pillow, 0);
        Authored.AtLeast(path, $"{field}.facet", Facet, 0);
    }
}

/// <summary>
/// The stones an arch's ring or a rib is built of (its <c>stones</c>): its band is cut into voussoirs of
/// <see cref="Stones"/>' kinds and lengths, picked by weight, laid from each springing toward a keystone at the crown that
/// stands <see cref="Keystone"/> further out and reaches as much past the band's edges (none when 0). <see cref="Joint"/>
/// metres separate them, showing the <see cref="Backing"/> material behind: an open joint for a rib, a hairline for a
/// tight ring. Each stone's face stands <see cref="MinJut"/> to <see cref="MaxJut"/> off the part's own face, so the
/// stones step in and out; its inner edge stands up to <see cref="InnerSag"/> off the band's and its outer edge up to
/// <see cref="OuterSag"/>; its front edges are cut back by <see cref="Bevel"/> (none by default). Seen from further off
/// than a wall, they take simpler kinds than the wall's (few nicks); a tight ring's kinds take no chips, so nothing opens
/// between its stones, and get their variety from bulging, hewn faces and an uneven outer edge.
/// </summary>
internal sealed record VoussoirDefinition(StoneKind[] Stones, float Joint, float MinJut, float MaxJut, float InnerSag, float OuterSag, float Keystone,
    float Bevel = 0, KitSurface Backing = KitSurface.Ceiling)
{
    /// <param name="face">How far the part's face stands from its base, which every stone's face must stay beyond.</param>
    internal void Validate(string path, string field, float face)
    {
        Authored.Require(Stones.Length > 0, path, $"{field}.stones", "must name at least one kind of stone.");
        for (int i = 0; i < Stones.Length; i++) Stones[i].Validate(path, $"{field}.stones[{i}]");
        Authored.Positive(path, $"{field}.joint", Joint);
        Authored.Require(Joint < Shortest / 2, path, $"{field}.joint", "must be well under the shortest stone.");
        Authored.AtLeast(path, $"{field}.bevel", Bevel, 0);
        Authored.AtLeast(path, $"{field}.maxJut", MaxJut, MinJut);
        Authored.Require(face + MinJut > Joint, path, $"{field}.minJut", $"leaves a stone's face behind the part's base: the part stands {face} m out.");
        Authored.AtLeast(path, $"{field}.innerSag", InnerSag, 0);
        Authored.AtLeast(path, $"{field}.outerSag", OuterSag, 0);
        Authored.AtLeast(path, $"{field}.keystone", Keystone, 0);
    }

    /// <summary>The shortest stone of any kind.</summary>
    internal float Shortest => Stones.Min(s => s.MinLength);
}

/// <summary>
/// Stones whose faces the shader draws (<c>stoneFaces</c>): each stone is a plain box of its size and jut, and
/// <see cref="Shader"/> carves its face in place of the mesh: the bevel, the bulge and facets of its kind, its chipped
/// corners and nicked edges, by parallax over <see cref="Steps"/> steps, <see cref="Depth"/> times as deep as the kind
/// says, its hollows darkened by up to <see cref="Cavity"/>. The stones' surfaces take untinted textures: their vertex
/// colours carry each stone's place and shape to the shader. With <see cref="MeshOutlines"/>, a stone keeps its chipped
/// and nicked outline in the mesh and the shader carves only its face.
/// </summary>
internal sealed record StoneFacesDefinition(string Shader, float Depth = 1, int Steps = 24, float Cavity = .35f, bool MeshOutlines = false)
{
    internal void Validate(string path)
    {
        Authored.Positive(path, "stoneFaces.depth", Depth);
        Authored.Require(Steps is >= 4 and <= 64, path, "stoneFaces.steps", "must be 4 to 64.");
        Authored.Within(path, "stoneFaces.cavity", Cavity, 0, 1);
    }
}
