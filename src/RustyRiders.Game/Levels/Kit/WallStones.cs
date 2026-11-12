using System.Numerics;

namespace RustyRiders.Game.Levels.Kit;

/// <summary>
/// Walls of real stones (<see cref="WallStonesDefinition"/>). A stretch of wall is laid in courses of random height;
/// each course is a row of stones of the recipe's kinds, nudged so no joint sits right over one in the course below.
/// Each stone is its kind's irregular outline (chipped corners, nicked edges) run out from the wall to its relief,
/// bevelled in to a pillowed or hewn face, with the joints between stones showing the wall behind. Layouts come from a <see cref="Random"/> seeded by the level and the stretch, so a level
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
                    Stone(mesh, recipe, stones, kind, random, origin, along, inward, x + g, end - g, y + g, y + h - g, relief, patch);
                    laid++;
                }
                x = end;
            }
            below = joints;
            y += h;
        }
        return laid;
    }

    /// <summary>
    /// One stone: its outline (the rectangle with chipped corners and nicked edges, <see cref="Outline"/>) runs straight
    /// out from the wall, then bevels in to its face, which bulges to a point over the middle and stands a little
    /// unevenly round its edge; a hewn face is flat facets, any other smooth.
    /// </summary>
    private static void Stone(MeshBuilder mesh, KitRecipe recipe, WallStonesDefinition stones, StoneKind kind, Random random, Vector2 origin,
        Vector2 along, Vector2 inward, float x0, float x1, float y0, float y1, float relief, Vector2 patch)
    {
        int slot = LevelKit.Slot(stones.Surface);
        float metres = recipe.Metres(stones.Surface);
        float b = MathF.Min(stones.Bevel, MathF.Min(relief / 2, MathF.Min((x1 - x0) / 4, (y1 - y0) / 4)));
        float edge = relief - b; // where the sides meet the bevel
        Vector3 At(Vector2 p, float d) => OpenGround.Gltf(origin + along * p.X + inward * d, p.Y);
        Vector2 offset = patch * metres; // each stone shows its own patch of the texture
        Vector2 Uv(Vector2 p) => new((offset.X + p.X) / metres, -(offset.Y + p.Y) / metres);
        Vector3 out_ = OpenGround.Gltf(inward, 0);
        Vector3 Across(Vector2 local) => OpenGround.Gltf(along * local.X, local.Y); // a wall-plane direction in glTF

        List<Vector2> rim = Outline(kind, random, x0, x1, y0, y1);
        Vector2 middle = rim.Aggregate(Vector2.Zero, (sum, p) => sum + p) / rim.Count;
        Vector2[] face = rim.Select(p => p + Vector2.Normalize(middle - p) * b * 1.4f).ToArray();
        float[] lift = rim.Select(_ => relief + Between(random, -kind.Facet, kind.Facet)).ToArray();
        int n = rim.Count;
        for (int i = 0; i < n; i++)
        {
            Vector2 p = rim[i], q = rim[(i + 1) % n], d = q - p;
            Vector3 outward = Across(new Vector2(d.Y, -d.X)); // the outline runs anticlockwise seen from the room
            float l0 = (p - rim[0]).Length(), l1 = l0 + d.Length();
            mesh.Face(slot, At(p, 0), At(q, 0), At(q, edge), At(p, edge),
                new Vector2((offset.X + l0) / metres, 0), new Vector2((offset.X + l1) / metres, 0),
                new Vector2((offset.X + l1) / metres, edge / metres), new Vector2((offset.X + l0) / metres, edge / metres), outward);
            if (b > 0)
                mesh.Face(slot, At(p, edge), At(q, edge), At(face[(i + 1) % n], lift[(i + 1) % n]), At(face[i], lift[i]),
                    Uv(p), Uv(q), Uv(face[(i + 1) % n]), Uv(face[i]), outward + out_);
        }
        // The face: a fan from its peak, bulged by the kind's pillow; a hewn face's peak sits off the middle, so its facets differ.
        Vector2 peak = kind.Hewn ? middle + new Vector2(Between(random, -.25f, .25f) * (x1 - x0), Between(random, -.25f, .25f) * (y1 - y0)) : middle;
        Vector3 top = At(peak, relief + kind.Pillow);
        if (kind.Hewn)
            for (int i = 0; i < n; i++)
                mesh.Triangle(slot, mesh.Vertex(top, Uv(peak)), mesh.Vertex(At(face[i], lift[i]), Uv(face[i])),
                    mesh.Vertex(At(face[(i + 1) % n], lift[(i + 1) % n]), Uv(face[(i + 1) % n])), out_);
        else
        {
            uint hub = mesh.Vertex(top, Uv(middle));
            uint[] ring = Enumerable.Range(0, n).Select(i => mesh.Vertex(At(face[i], lift[i]), Uv(face[i]))).ToArray();
            for (int i = 0; i < n; i++) mesh.Triangle(slot, hub, ring[i], ring[(i + 1) % n], out_);
        }
    }

    /// <summary>
    /// A stone's outline, anticlockwise from its bottom-left as seen from the room: each corner of the rectangle chipped
    /// off by chance, and each edge by chance nicked with a shallow notch. Chips and nicks stay within a third of the
    /// stone's smaller side.
    /// </summary>
    private static List<Vector2> Outline(StoneKind kind, Random random, float x0, float x1, float y0, float y1)
    {
        Vector2[] corners = [new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1)];
        float most = MathF.Min(kind.Chip, MathF.Min(x1 - x0, y1 - y0) / 3);
        List<Vector2> rim = [];
        for (int c = 0; c < 4; c++)
        {
            Vector2 corner = corners[c], before = corners[(c + 3) % 4], after = corners[(c + 1) % 4];
            if (most > 0 && random.NextDouble() < kind.ChipChance)
            {
                rim.Add(corner + Vector2.Normalize(before - corner) * Between(random, most * .3f, most));
                rim.Add(corner + Vector2.Normalize(after - corner) * Between(random, most * .3f, most));
            }
            else rim.Add(corner);
            if (most > 0 && random.NextDouble() < kind.Nicks)
            {
                // A nick partway along the edge to the next corner, cut into the stone.
                Vector2 edge = after - corner, dir = Vector2.Normalize(edge), into = new(-dir.Y, dir.X);
                float length = edge.Length(), width = Between(random, most * .4f, most), at = Between(random, .25f, .75f) * length;
                if (width < length / 3)
                {
                    rim.Add(corner + dir * (at - width / 2));
                    rim.Add(corner + dir * at + into * Between(random, most * .2f, most * .5f));
                    rim.Add(corner + dir * (at + width / 2));
                }
            }
        }
        return rim;
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
