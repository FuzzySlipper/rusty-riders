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
    private void Trim(MeshBuilder mesh, MeshBuilder collision, TrimDefinition trim, Dictionary<(int X, int Z), float> ground)
    {
        bool ceiling = trim.Anchor == TrimAnchor.Ceiling;
        foreach (List<(Vector2 Point, float Height)> loop in OpenGround.Walls(ceiling ? plan.Heights(trim.Areas) : ground))
        {
            bool[] keep = loop.Select(p => ceiling || trim.Areas.Contains(plan.Rules.Area(p.Height))).ToArray();
            if (keep.All(k => k))
            {
                Sweep(mesh, collision, loop, trim, Enumerable.Range(0, loop.Count).ToList(), closed: true);
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
                    Sweep(mesh, collision, loop, trim, stretch, closed: false);
                    stretch = [];
                }
            }
            if (stretch.Count > 0) Sweep(mesh, collision, loop, trim, stretch, closed: false);
        }
    }

    /// <summary>
    /// A trim's profile swept along consecutive runs of an outline: each profile point offset into the room along the
    /// corner's mitre and up from the anchor (the floor, or the run's ceiling). Each run gets its own vertices, so
    /// corners stay crisp. An open stretch ends square at its first and last corner, each end capped. U is metres along
    /// the wall and V metres along the profile from the wall, over its surface's repeat.
    /// </summary>
    private void Sweep(MeshBuilder mesh, MeshBuilder collision, List<(Vector2 Point, float Height)> loop, TrimDefinition trim, List<int> runs, bool closed)
    {
        int n = loop.Count;
        Vector2 Point(int i) => plan.Metres(loop[i % n].Point);
        Vector2 RunInward(int run) => Inward(Point(run), Point(run + 1));
        Vector2 Mitre(Vector2 before, Vector2 after) => (before + after) / (1 + Vector2.Dot(before, after));
        int slot = LevelKit.Slot(trim.Surface);
        float metres = recipe.Metres(trim.Surface);
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
            if (!closed && k == 0) Cap(a, inward, -Vector2.Normalize(b - a), baseY);
            if (!closed && k == runs.Count - 1) Cap(b, inward, Vector2.Normalize(b - a), baseY);
            along += length;
        }

        // The profile's cross-section at an open end: a fan from the wall's foot (or head) through the profile points.
        void Cap(Vector2 corner, Vector2 inward, Vector2 outward, float baseY)
        {
            Vector3 facing = OpenGround.Gltf(outward, 0);
            Vector3 At(Vector2 q) => OpenGround.Gltf(corner + inward * q.X, baseY + q.Y);
            Vector2 UV(Vector2 q) => q / metres;
            uint foot = mesh.Vertex(At(Vector2.Zero), Vector2.Zero);
            for (int p = 0; p + 1 < trim.Profile.Length; p++)
            {
                Vector2 q0 = Profile(trim, p), q1 = Profile(trim, p + 1);
                mesh.Triangle(slot, foot, mesh.Vertex(At(q0), UV(q0)), mesh.Vertex(At(q1), UV(q1)), facing);
                if (trim.Collides)
                    collision.Triangle(0, collision.Vertex(At(Vector2.Zero), default), collision.Vertex(At(q0), default), collision.Vertex(At(q1), default), facing);
            }
        }
    }
}
