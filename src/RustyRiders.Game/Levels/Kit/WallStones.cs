using System.Numerics;
using static RustyRiders.Game.Levels.Kit.StoneShape;

namespace RustyRiders.Game.Levels.Kit;

/// <summary>
/// Walls of real stones (<see cref="WallStonesDefinition"/>). A stretch of wall is laid in courses; each course is a
/// row of stones of the recipe's kinds, nudged so no joint sits right over one in the course below. Each stone is a
/// <see cref="StoneShape"/> standing out from the wall to its relief, with the joints between stones showing the wall behind. Outlines and column shafts are laid
/// as loops whose corners interlock (WallStones.Corners.cs); a doorway's head is laid on its own (<see cref="Lay"/>).
/// Layouts come from <see cref="Random"/>s seeded by the level, so a level always lays the same stones.
/// </summary>
internal static partial class WallStones
{
    private const float JointClearance = .25f; // metres a joint keeps from the joints of the course below

    /// <summary>How high the floor trims along an area's walls stand: where its stones start.</summary>
    internal static float Bottom(KitRecipe recipe, PlanArea area) => recipe.Trims
        .Where(t => t.Anchor == TrimAnchor.Floor && t.Areas.Contains(area)).Select(t => t.Profile.Max(p => p[1])).DefaultIfEmpty(0).Max();

    /// <summary>How far the ceiling trims round an area hang down: where its stones stop under the ceiling.</summary>
    internal static float TopGap(KitRecipe recipe, PlanArea area) => recipe.Trims
        .Where(t => t.Anchor == TrimAnchor.Ceiling && t.Areas.Contains(area)).Select(t => -t.Profile.Min(p => p[1])).DefaultIfEmpty(0).Max();

    /// <summary>
    /// Lays the wall from <paramref name="x0"/> to <paramref name="x1"/> metres along <paramref name="along"/> from
    /// <paramref name="origin"/> (on the wall line, Unity metres) and from <paramref name="y0"/> to <paramref name="y1"/>
    /// up, standing out toward <paramref name="inward"/>, in courses of its own. A stone whose rectangle
    /// <paramref name="skip"/> refuses (an arch in the way) is left out, its place kept. Returns how many stones were laid.
    /// </summary>
    internal static int Lay(MeshBuilder mesh, KitRecipe recipe, WallStonesDefinition stones, Random random, Vector2 origin, Vector2 along,
        Vector2 inward, float x0, float x1, float y0, float y1, Func<float, float, float, float, bool>? skip = null)
    {
        if (x1 - x0 < stones.Joint * 2 || y1 - y0 < stones.Joint * 2) return 0;
        int laid = 0;
        List<float> below = [];
        float y = y0;
        while (y < y1 - 1e-3f)
        {
            float h = Between(random, stones.MinCourse, stones.MaxCourse);
            if (y1 - (y + h) < stones.MinCourse * .6f) h = y1 - y; // no sliver of a course at the top
            laid += Row(mesh, recipe, stones, stones.Surface, random, origin, along, inward, x0, x1, y, y + h, ref below, null, null, skip);
            y += h;
        }
        return laid;
    }

    /// <summary>
    /// One course from <paramref name="x0"/> to <paramref name="x1"/>, its joints kept clear of <paramref name="below"/>
    /// (which it replaces with its own). The first and last stones may have their relief set (corner stones).
    /// </summary>
    private static int Row(MeshBuilder mesh, KitRecipe recipe, WallStonesDefinition stones, KitSurface surface, Random random, Vector2 origin,
        Vector2 along, Vector2 inward, float x0, float x1, float y0, float y1, ref List<float> below, float? firstRelief, float? lastRelief,
        Func<float, float, float, float, bool>? skip = null)
    {
        int laid = 0;
        List<float> joints = [];
        float x = x0, total = stones.Stones.Sum(s => s.Weight), g = stones.Joint / 2;
        bool first = true;
        StoneFrame frame = new(p => OpenGround.Gltf(origin + along * p.X, p.Y), OpenGround.Gltf(inward, 0));
        while (x < x1 - 1e-3f)
        {
            StoneKind kind = Pick(random, stones.Stones, total);
            float length = Between(random, kind.MinLength, kind.MaxLength), end = x + length;
            if (below.Any(j => MathF.Abs(j - end) < JointClearance)) end += JointClearance * 2; // break the joint
            if (x1 - end < stones.Shortest * .6f) end = x1; // no sliver of a stone at the end
            if (end < x1) joints.Add(end);
            float relief = Between(random, stones.MinRelief, stones.MaxRelief) + (random.NextDouble() < stones.PushedChance ? stones.Pushed : 0);
            if (first && firstRelief is { } f) relief = f;
            if (end >= x1 && lastRelief is { } l) relief = l;
            Vector2 patch = new((float)random.NextDouble(), (float)random.NextDouble());
            if (skip?.Invoke(x, end, y0, y1) != true && end - x > 2 * g)
            {
                StoneShape.Build(mesh, LevelKit.Slot(surface), recipe.Metres(surface), stones.Bevel, kind, random, frame,
                    Outline(kind, random, x + g, end - g, y0 + g, y1 - g), relief, patch,
                    faces: recipe.StoneFaces, box: recipe.StoneFaces is null ? null : Box(x + g, end - g, y0 + g, y1 - g));
                laid++;
            }
            x = end;
            first = false;
        }
        below = joints;
        return laid;
    }
}
