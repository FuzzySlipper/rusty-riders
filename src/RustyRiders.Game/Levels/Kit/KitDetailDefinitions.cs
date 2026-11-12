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
/// walls in a half-ellipse drawn with <see cref="Segments"/> pieces. They take <see cref="Surface"/>.
/// </summary>
internal sealed record RibDefinition(float Width, float Depth, float Haunch, int Segments, KitSurface Surface = KitSurface.Trim)
{
    internal void Validate(string path, PilasterDefinition? pilasters)
    {
        Authored.Require(pilasters is not null, path, "ribs", "ribs spring from pilasters: the kit needs pilasters.");
        Authored.Positive(path, "ribs.width", Width);
        Authored.Positive(path, "ribs.depth", Depth);
        Authored.AtLeast(path, "ribs.haunch", Haunch, 0);
        Authored.Require(Segments >= 2, path, "ribs.segments", "must be at least 2.");
    }
}

/// <summary>
/// Blind arches on room walls between neighbouring pilasters (<see cref="BlindArches"/>): a panel of the
/// <see cref="Panel"/> material from <see cref="Sill"/> up to a round head springing at <see cref="Spring"/>, the bay as
/// wide as the gap between the pilasters less <see cref="Margin"/> each side, framed by a <see cref="Ring"/> standing out
/// from the wall, with a sill under it. A room too low for the bay, keeping <see cref="TopClearance"/> under its ceiling,
/// springs it lower; one too low for a bay a metre tall takes none. The head is drawn with <see cref="Segments"/> pieces.
/// </summary>
internal sealed record BlindArchDefinition(float Sill, float Spring, float Margin, float TopClearance, int Segments, KitSurface Panel,
    ArchRingDefinition Ring)
{
    internal void Validate(string path, PilasterDefinition? pilasters)
    {
        Authored.Require(pilasters is not null, path, "blindArches", "blind arches stand between pilasters: the kit needs pilasters.");
        Authored.AtLeast(path, "blindArches.sill", Sill, 0);
        Authored.Require(Spring > Sill + 1, path, "blindArches.spring", "must be at least a metre above the sill.");
        Authored.AtLeast(path, "blindArches.margin", Margin, 0);
        Authored.AtLeast(path, "blindArches.topClearance", TopClearance, 0);
        Authored.Require(Segments >= 2, path, "blindArches.segments", "must be at least 2.");
        Authored.Positive(path, "blindArches.ring.width", Ring.Width);
        Authored.Positive(path, "blindArches.ring.depth", Ring.Depth);
        Authored.Require(pilasters is null || pilasters.Spacing - pilasters.Width - 2 * Margin - 2 * Ring.Width > 1, path, "blindArches.margin",
            "leaves no bay a metre wide between the pilasters.");
    }
}

/// <summary>
/// Wall sconces either side of each doorway on the room's side (<see cref="Sconces"/>): a bracket of the
/// <see cref="Bracket"/> material standing <see cref="BracketDepth"/> out from the wall at <see cref="Height"/>,
/// <see cref="Gap"/> beyond the door arch's ring, holding a flame <see cref="FlameSize"/> across that glows
/// <see cref="FlameColor"/> at <see cref="FlameEmission"/>, and a point light of <see cref="LightColor"/> (linear RGB) at
/// <see cref="Intensity"/> reaching <see cref="Range"/> metres.
/// </summary>
internal sealed record SconceDefinition(float Height, float Gap, float BracketDepth, float FlameSize, float[] FlameColor, float FlameEmission,
    float[] LightColor, float Intensity, float Range, KitSurface Bracket = KitSurface.Ceiling)
{
    internal void Validate(string path, PlanRules plan)
    {
        Authored.Require(Height > 0 && Height < plan.CorridorHeight, path, "sconces.height", $"must be above the floor and under {plan.CorridorHeight} m.");
        Authored.AtLeast(path, "sconces.gap", Gap, 0);
        Authored.Positive(path, "sconces.bracketDepth", BracketDepth);
        Authored.Positive(path, "sconces.flameSize", FlameSize);
        Authored.Colour(path, "sconces.flameColor", FlameColor);
        Authored.AtLeast(path, "sconces.flameEmission", FlameEmission, 0);
        Authored.Colour(path, "sconces.lightColor", LightColor);
        Authored.Positive(path, "sconces.intensity", Intensity);
        Authored.Positive(path, "sconces.range", Range);
    }
}

/// <summary>
/// The kit's own columns (<see cref="KitColumns"/>), in place of old props: free-standing <see cref="Hall"/> columns where
/// the HallColumns dressing rule would stand them (rooms whose family has columns), and <see cref="Corners"/> piers in
/// each square room corner. Both run floor to ceiling and collide.
/// </summary>
internal sealed record ColumnsDefinition(ColumnDefinition? Hall = null, ColumnDefinition? Corners = null)
{
    internal void Validate(string path)
    {
        Hall?.Validate(path, "columns.hall");
        Corners?.Validate(path, "columns.corners");
    }
}

/// <summary>
/// A square column: a shaft <see cref="Width"/> across on a plinth <see cref="PlinthWidth"/> across and
/// <see cref="PlinthHeight"/> tall, under a capital <see cref="CapitalWidth"/> across and <see cref="CapitalHeight"/> tall
/// that meets the ceiling, all of the kit's <see cref="Surface"/> material.
/// </summary>
internal sealed record ColumnDefinition(float Width, float PlinthWidth, float PlinthHeight, float CapitalWidth, float CapitalHeight,
    KitSurface Surface = KitSurface.Trim)
{
    internal void Validate(string path, string field)
    {
        Authored.Positive(path, $"{field}.width", Width);
        Authored.AtLeast(path, $"{field}.plinthWidth", PlinthWidth, Width);
        Authored.AtLeast(path, $"{field}.capitalWidth", CapitalWidth, Width);
        Authored.AtLeast(path, $"{field}.plinthHeight", PlinthHeight, 0);
        Authored.AtLeast(path, $"{field}.capitalHeight", CapitalHeight, 0);
    }

    /// <summary>The widest part, which sets how far a corner pier stands from its walls.</summary>
    internal float Footprint => MathF.Max(PlinthWidth, CapitalWidth);
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
    internal void Validate(string path)
    {
        Authored.Positive(path, "wallStones.minCourse", MinCourse);
        Authored.AtLeast(path, "wallStones.maxCourse", MaxCourse, MinCourse);
        Authored.Require(Stones.Length > 0, path, "wallStones.stones", "must name at least one kind of stone.");
        for (int i = 0; i < Stones.Length; i++)
        {
            Authored.Positive(path, $"wallStones.stones[{i}].weight", Stones[i].Weight);
            Authored.Positive(path, $"wallStones.stones[{i}].minLength", Stones[i].MinLength);
            Authored.AtLeast(path, $"wallStones.stones[{i}].maxLength", Stones[i].MaxLength, Stones[i].MinLength);
            Authored.Within(path, $"wallStones.stones[{i}].chipChance", Stones[i].ChipChance, 0, 1);
            Authored.AtLeast(path, $"wallStones.stones[{i}].chip", Stones[i].Chip, 0);
            Authored.Within(path, $"wallStones.stones[{i}].nicks", Stones[i].Nicks, 0, 1);
            Authored.AtLeast(path, $"wallStones.stones[{i}].pillow", Stones[i].Pillow, 0);
            Authored.AtLeast(path, $"wallStones.stones[{i}].facet", Stones[i].Facet, 0);
        }
        Authored.Positive(path, "wallStones.joint", Joint);
        Authored.Require(Joint < MinCourse / 2, path, "wallStones.joint", "must be well under a course's height.");
        Authored.Positive(path, "wallStones.minRelief", MinRelief);
        Authored.AtLeast(path, "wallStones.maxRelief", MaxRelief, MinRelief);
        Authored.Within(path, "wallStones.pushedChance", PushedChance, 0, 1);
        Authored.AtLeast(path, "wallStones.pushed", Pushed, 0);
        Authored.AtLeast(path, "wallStones.bevel", Bevel, 0);
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
    float Pillow = 0, float Facet = 0, bool Hewn = false);
