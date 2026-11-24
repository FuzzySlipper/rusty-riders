using System.Numerics;

namespace RustyRiders.Game.Levels.Kit;

/// <summary>The kit's trims: mouldings and ledges swept along the plan's walls (see <see cref="TrimDefinition"/>).</summary>
internal sealed partial class KitGeometry
{
    /// <summary>
    /// A trim swept along walls. A ceiling trim goes round the whole outline of its one area. A floor trim runs along
    /// the walls of its areas only (an outline run's area is told by its height), each stretch ending square with its
    /// profile capped where a wall of another area begins.
    /// </summary>
    private void Trim(MeshBuilder mesh, MeshBuilder collision, TrimDefinition trim, Dictionary<(int X, int Z), float> ground, int seed)
    {
        bool ceiling = trim.Anchor == TrimAnchor.Ceiling;
        int loopIndex = 0;
        foreach (List<(Vector2 Point, float Height)> loop in OpenGround.Walls(ceiling ? plan.Heights(trim.Areas) : ground))
        {
            bool[] keep = loop.Select(p => ceiling || trim.Areas.Contains(plan.Rules.Area(p.Height))).ToArray();
            if (keep.All(k => k))
            {
                Sweep(mesh, collision, loop, trim, Enumerable.Range(0, loop.Count).ToList(), closed: true, seed, loopIndex++);
                continue;
            }
            int first = Array.IndexOf(keep, false);
            List<int> stretch = [];
            for (int s = 1; s <= loop.Count; s++)
            {
                int i = (first + s) % loop.Count;
                if (keep[i]) stretch.Add(i);
                else if (stretch.Count > 0)
                {
                    Sweep(mesh, collision, loop, trim, stretch, closed: false, seed, loopIndex++);
                    stretch = [];
                }
            }
            if (stretch.Count > 0) Sweep(mesh, collision, loop, trim, stretch, closed: false, seed, loopIndex++);
        }
    }

    /// <summary>
    /// A trim's profile swept along consecutive runs of an outline: each profile point offset into the room along the
    /// corner's mitre and up from the anchor (the floor, or the run's ceiling). Each run gets its own vertices, so
    /// corners stay crisp. An open stretch ends square at its first and last corner, each end capped. U is metres along
    /// the wall and V metres along the profile from the wall, over its surface's repeat. When <see cref="TrimDefinition.Stones"/>
    /// is set on a squared floor ledge, the swept profile draws as the dark backing core (keeping its collision) and a
    /// matching single course of curb stones covers its front and top faces with aligned vertical joints.
    /// </summary>
    private void Sweep(MeshBuilder mesh, MeshBuilder collision, List<(Vector2 Point, float Height)> loop, TrimDefinition trim, List<int> runs,
        bool closed, int seed, int stretchId)
    {
        int n = loop.Count;
        Vector2 Point(int i) => plan.Metres(loop[i % n].Point);
        Vector2 RunInward(int run) => Inward(Point(run), Point(run + 1));
        Vector2 Mitre(Vector2 before, Vector2 after) => (before + after) / (1 + Vector2.Dot(before, after));
        WallStonesDefinition? stones = trim.Stones ? recipe.WallStones : null;
        KitSurface sweepSurface = stones is not null ? stones.Backing : trim.Surface;
        int slot = LevelKit.Slot(sweepSurface);
        float metres = recipe.Metres(sweepSurface);
        float[] arc = new float[trim.Profile.Length];
        for (int p = 1; p < arc.Length; p++)
            arc[p] = arc[p - 1] + Vector2.Distance(Profile(trim, p - 1), Profile(trim, p));
        float along = 0;
        for (int k = 0; k < runs.Count; k++)
        {
            int run = runs[k];
            Vector2 inward = RunInward(run);
            Vector2 startMitre = closed || k > 0 ? Mitre(RunInward(runs[(k + runs.Count - 1) % runs.Count]), inward) : inward;
            Vector2 endMitre = closed || k < runs.Count - 1 ? Mitre(inward, RunInward(runs[(k + 1) % runs.Count])) : inward;
            float baseY = trim.Anchor == TrimAnchor.Floor ? 0 : loop[run].Height;
            Vector2 a = Point(run), b = Point(run + 1);
            float length = Vector2.Distance(a, b);
            for (int p = 0; p + 1 < trim.Profile.Length; p++)
            {
                Vector2 q0 = Profile(trim, p), q1 = Profile(trim, p + 1);
                Vector3 At(Vector2 corner, Vector2 mitre, Vector2 q) => OpenGround.Gltf(corner + mitre * q.X, baseY + q.Y);
                Vector2 normal = new(-(q1.Y - q0.Y), q1.X - q0.X); // perpendicular in (out, up); flipped away from the wall foot
                if (Vector2.Dot(normal, (q0 + q1) / 2) < 0) normal = -normal;
                Vector3 facing = OpenGround.Gltf(inward * normal.X, 0) + Vector3.UnitY * normal.Y;
                float u0 = along / metres, u1 = (along + length) / metres;
                float v0 = arc[p] / metres, v1 = arc[p + 1] / metres;
                Vector3 p00 = At(a, startMitre, q0), p10 = At(b, endMitre, q0), p11 = At(b, endMitre, q1), p01 = At(a, startMitre, q1);
                mesh.Face(slot, p00, p10, p11, p01, new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1), facing);
                if (trim.Collides) collision.Face(0, p00, p10, p11, p01, default, default, default, default, facing);
            }
            if (stones is not null)
                LedgeStones(mesh, trim, stones, a, b, inward, startMitre, endMitre, new Random(unchecked(seed * 7919 + stretchId * 104729 + k * 997)));
            if (!closed && k == 0) Cap(a, inward, -Vector2.Normalize(b - a), baseY);
            if (!closed && k == runs.Count - 1) Cap(b, inward, Vector2.Normalize(b - a), baseY);
            along += length;
        }

        // The profile's cross-section at an open end: a fan from the wall's foot (or head) through the profile points.
        void Cap(Vector2 corner, Vector2 inward, Vector2 outward, float baseY)
        {
            int capSlot = LevelKit.Slot(trim.Surface);
            float capMetres = recipe.Metres(trim.Surface);
            Vector3 facing = OpenGround.Gltf(outward, 0);
            Vector3 At(Vector2 q) => OpenGround.Gltf(corner + inward * q.X, baseY + q.Y);
            Vector2 UV(Vector2 q) => q / capMetres;
            uint foot = mesh.Vertex(At(Vector2.Zero), Vector2.Zero);
            for (int p = 0; p + 1 < trim.Profile.Length; p++)
            {
                Vector2 q0 = Profile(trim, p), q1 = Profile(trim, p + 1);
                mesh.Triangle(capSlot, foot, mesh.Vertex(At(q0), UV(q0)), mesh.Vertex(At(q1), UV(q1)), facing);
                if (trim.Collides)
                    collision.Triangle(0, collision.Vertex(At(Vector2.Zero), default), collision.Vertex(At(q0), default), collision.Vertex(At(q1), default), facing);
            }
        }
    }

    /// <summary>
    /// A single course of curb stones over a squared floor ledge run: matching front and top slabs sharing the same
    /// vertical joints, standing slightly proud of the colliding dark backing core.
    /// </summary>
    private void LedgeStones(MeshBuilder mesh, TrimDefinition trim, WallStonesDefinition stones, Vector2 a, Vector2 b, Vector2 inward,
        Vector2 startMitre, Vector2 endMitre, Random random)
    {
        float height = trim.Profile[0][1], depth = trim.Profile[1][0];
        Vector2 along = Vector2.Normalize(b - a);
        // Project the mitred front-edge endpoints onto the run's line so curb stones stay clear of inside corners
        // and meet outside corners cleanly.
        float startX = MathF.Max(0, Vector2.Dot(startMitre * depth, along));
        float endX = Vector2.Distance(a, b) + MathF.Min(0, Vector2.Dot(endMitre * depth, along));
        float g = stones.Joint / 2;
        if (endX - startX <= stones.Joint * 2) return;

        int slot = LevelKit.Slot(trim.Surface);
        float metres = recipe.Metres(trim.Surface);
        float total = stones.Stones.Sum(s => s.Weight);
        Vector2 frontOrigin = a + inward * depth;
        StoneFrame frontFrame = new(p => OpenGround.Gltf(frontOrigin + along * p.X, p.Y), OpenGround.Gltf(inward, 0));
        StoneFrame topFrame = new(p => OpenGround.Gltf(a + along * p.X + inward * p.Y, height), Vector3.UnitY);

        float x = startX;
        while (x < endX - 1e-3f)
        {
            StoneKind kind = StoneShape.Pick(random, stones.Stones, total);
            float end = x + StoneShape.Between(random, kind.MinLength, kind.MaxLength);
            if (endX - end < stones.Shortest * .6f) end = endX;
            float relief = StoneShape.Between(random, stones.MinRelief, stones.MaxRelief);
            float topRelief = MathF.Min(relief, stones.MinRelief + (stones.MaxRelief - stones.MinRelief) * .5f);
            Vector2 patch = new((float)random.NextDouble(), (float)random.NextDouble());
            if (end - x > 2 * g)
            {
                float sx0 = x + g, sx1 = end - g;
                StoneShape.Build(mesh, slot, metres, stones.Bevel, kind, random, frontFrame,
                    StoneShape.Outline(kind, random, sx0, sx1, 0, height), relief, patch,
                    faces: recipe.StoneFaces, box: recipe.StoneFaces is null ? null : StoneShape.Box(sx0, sx1, 0, height));
                // Matching top cap slab flush with the front face and wall, sharing vertical joints.
                StoneKind topKind = kind with { ChipChance = kind.ChipChance * .35f, Nicks = 0, Hewn = false };
                StoneShape.Build(mesh, slot, metres, stones.Bevel * .75f, topKind, random, topFrame,
                    StoneShape.Outline(topKind, random, sx0, sx1, 0, depth + relief), topRelief, patch,
                    faces: recipe.StoneFaces, box: recipe.StoneFaces is null ? null : StoneShape.Box(sx0, sx1, 0, depth + relief));
            }
            x = end;
        }
    }
}
