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
