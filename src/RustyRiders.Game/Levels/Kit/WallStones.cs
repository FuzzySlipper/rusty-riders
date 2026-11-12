using System.Numerics;

namespace RustyRiders.Game.Levels.Kit;

/// <summary>
/// Walls of real stones (<see cref="WallStonesDefinition"/>). A stretch of wall is laid in courses of random height;
/// each course is a row of stones of the recipe's kinds, nudged so no joint sits right over one in the course below.
/// Each stone is a box out from the wall to its relief, its front edges bevelled, with the joints between stones
/// showing the wall behind. Layouts come from a <see cref="Random"/> seeded by the level and the stretch, so a level
/// always lays the same stones.
/// </summary>
internal static class WallStones
{
    private const float JointClearance = .25f; // metres a joint keeps from the joints of the course below

    /// <summary>The stones on every outline run, between its area's floor trims and ceiling trims.</summary>
    internal static int Runs(MeshBuilder mesh, RoomPlan plan, KitRecipe recipe, WallStonesDefinition stones,
        List<List<(Vector2 Point, float Height)>> loops, int seed)
    {
        int laid = 0, run = 0;
        foreach (List<(Vector2 Point, float Height)> loop in loops)
            for (int i = 0; i < loop.Count; i++, run++)
            {
                Vector2 a = plan.Metres(loop[i].Point), b = plan.Metres(loop[(i + 1) % loop.Count].Point);
                PlanArea area = plan.Rules.Area(loop[i].Height);
                Vector2 along = Vector2.Normalize(b - a);
                Random random = new(unchecked(seed * 7919 + run * 104729));
                laid += Lay(mesh, recipe, stones, random, a, along, new Vector2(-along.Y, along.X), 0, Vector2.Distance(a, b),
                    Bottom(recipe, area), loop[i].Height - TopGap(recipe, area));
            }
        return laid;
    }

    /// <summary>How high the floor trims along an area's walls stand: where its stones start.</summary>
    internal static float Bottom(KitRecipe recipe, PlanArea area) => recipe.Trims
        .Where(t => t.Anchor == TrimAnchor.Floor && t.Areas.Contains(area)).Select(t => t.Profile.Max(p => p[1])).DefaultIfEmpty(0).Max();

    /// <summary>How far the ceiling trims round an area hang down: where its stones stop under the ceiling.</summary>
    internal static float TopGap(KitRecipe recipe, PlanArea area) => recipe.Trims
        .Where(t => t.Anchor == TrimAnchor.Ceiling && t.Areas.Contains(area)).Select(t => -t.Profile.Min(p => p[1])).DefaultIfEmpty(0).Max();

    /// <summary>
    /// Lays the wall from <paramref name="x0"/> to <paramref name="x1"/> metres along <paramref name="along"/> from
    /// <paramref name="origin"/> (on the wall line, Unity metres) and from <paramref name="y0"/> to <paramref name="y1"/>
    /// up, standing out toward <paramref name="inward"/>. A stone whose rectangle <paramref name="skip"/> refuses (an
    /// arch in the way) is left out, its place kept. Returns how many stones were laid.
    /// </summary>
    internal static int Lay(MeshBuilder mesh, KitRecipe recipe, WallStonesDefinition stones, Random random, Vector2 origin, Vector2 along,
        Vector2 inward, float x0, float x1, float y0, float y1, Func<float, float, float, float, bool>? skip = null)
    {
        if (x1 - x0 < stones.Joint * 2 || y1 - y0 < stones.Joint * 2) return 0;
        int laid = 0;
        List<float> below = [];
        float y = y0, total = stones.Stones.Sum(s => s.Weight);
        while (y < y1 - 1e-3f)
        {
            float h = Between(random, stones.MinCourse, stones.MaxCourse);
            if (y1 - (y + h) < stones.MinCourse * .6f) h = y1 - y; // no sliver of a course at the top
            List<float> joints = [];
            float x = x0;
            while (x < x1 - 1e-3f)
            {
                StoneKind kind = Pick(random, stones.Stones, total);
                float length = Between(random, kind.MinLength, kind.MaxLength), end = x + length;
                if (below.Any(j => MathF.Abs(j - end) < JointClearance)) end += JointClearance * 2; // break the joint
                if (x1 - end < stones.Shortest * .6f) end = x1; // no sliver of a stone at the end
                if (end < x1) joints.Add(end);
                float relief = Between(random, stones.MinRelief, stones.MaxRelief) + (random.NextDouble() < stones.PushedChance ? stones.Pushed : 0);
                Vector2 patch = new((float)random.NextDouble(), (float)random.NextDouble());
                float g = stones.Joint / 2;
                if (skip?.Invoke(x, end, y, y + h) != true)
                {
                    Stone(mesh, recipe, stones, origin, along, inward, x + g, end - g, y + g, y + h - g, relief, patch);
                    laid++;
                }
                x = end;
            }
            below = joints;
            y += h;
        }
        return laid;
    }

    /// <summary>One stone: a box from the wall out to <paramref name="relief"/>, its front face inset by the bevel.</summary>
    private static void Stone(MeshBuilder mesh, KitRecipe recipe, WallStonesDefinition stones, Vector2 origin, Vector2 along, Vector2 inward,
        float x0, float x1, float y0, float y1, float relief, Vector2 patch)
    {
        int slot = LevelKit.Slot(stones.Surface);
        float metres = recipe.Metres(stones.Surface);
        float b = MathF.Min(stones.Bevel, MathF.Min(relief / 2, MathF.Min((x1 - x0) / 4, (y1 - y0) / 4)));
        float edge = relief - b; // where the sides meet the bevel
        Vector3 At(float x, float y, float d) => OpenGround.Gltf(origin + along * x + inward * d, y);
        Vector2 offset = patch * metres; // each stone shows its own patch of the texture
        Vector2 Face(float x, float y) => new((offset.X + x) / metres, -(offset.Y + y) / metres);
        Vector2 Flat(float x, float d) => new((offset.X + x) / metres, (offset.Y + d) / metres);
        Vector2 Upright(float d, float y) => new((offset.X + d) / metres, -(offset.Y + y) / metres);
        Vector3 out_ = OpenGround.Gltf(inward, 0), right = OpenGround.Gltf(along, 0), up = Vector3.UnitY;

        // Front, inset by the bevel.
        (float fx0, float fx1, float fy0, float fy1) = (x0 + b, x1 - b, y0 + b, y1 - b);
        mesh.Face(slot, At(fx0, fy0, relief), At(fx1, fy0, relief), At(fx1, fy1, relief), At(fx0, fy1, relief),
            Face(fx0, fy0), Face(fx1, fy0), Face(fx1, fy1), Face(fx0, fy1), out_);
        if (b > 0)
        {
            mesh.Face(slot, At(fx0, fy1, relief), At(fx1, fy1, relief), At(x1, y1, edge), At(x0, y1, edge),
                Face(fx0, fy1), Face(fx1, fy1), Face(x1, y1), Face(x0, y1), up + out_);
            mesh.Face(slot, At(fx0, fy0, relief), At(fx1, fy0, relief), At(x1, y0, edge), At(x0, y0, edge),
                Face(fx0, fy0), Face(fx1, fy0), Face(x1, y0), Face(x0, y0), -up + out_);
            mesh.Face(slot, At(fx0, fy0, relief), At(fx0, fy1, relief), At(x0, y1, edge), At(x0, y0, edge),
                Face(fx0, fy0), Face(fx0, fy1), Face(x0, y1), Face(x0, y0), -right + out_);
            mesh.Face(slot, At(fx1, fy0, relief), At(fx1, fy1, relief), At(x1, y1, edge), At(x1, y0, edge),
                Face(fx1, fy0), Face(fx1, fy1), Face(x1, y1), Face(x1, y0), right + out_);
        }
        // Sides, from the wall to the bevel.
        mesh.Face(slot, At(x0, y1, 0), At(x1, y1, 0), At(x1, y1, edge), At(x0, y1, edge), Flat(x0, 0), Flat(x1, 0), Flat(x1, edge), Flat(x0, edge), up);
        mesh.Face(slot, At(x0, y0, 0), At(x1, y0, 0), At(x1, y0, edge), At(x0, y0, edge), Flat(x0, 0), Flat(x1, 0), Flat(x1, edge), Flat(x0, edge), -up);
        mesh.Face(slot, At(x0, y0, 0), At(x0, y1, 0), At(x0, y1, edge), At(x0, y0, edge), Upright(0, y0), Upright(0, y1), Upright(edge, y1), Upright(edge, y0), -right);
        mesh.Face(slot, At(x1, y0, 0), At(x1, y1, 0), At(x1, y1, edge), At(x1, y0, edge), Upright(0, y0), Upright(0, y1), Upright(edge, y1), Upright(edge, y0), right);
    }

    private static StoneKind Pick(Random random, StoneKind[] kinds, float total)
    {
        double pick = random.NextDouble() * total;
        foreach (StoneKind kind in kinds)
            if ((pick -= kind.Weight) < 0) return kind;
        return kinds[^1];
    }

    private static float Between(Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);
}
