using System.Numerics;
using Rusty.Engine;

namespace RustyRiders.Game.Levels.Kit;

/// <summary>
/// A level built from a world's parametric kit over the room plan. It takes the plan's open ground and builds:
/// - walls along every outline, as tall as the area they face;
/// - a lintel or soffit face wherever a taller area meets a lower one (over doorways, at corridor ends);
/// - ceilings at each area's height over a floor;
/// - the kit's trims swept along the outlines of their areas;
/// - pilasters against the walls.
///
/// Texture coordinates run in world metres (walls by metres along their outline and up), so adjoining pieces keep one
/// texture phase. One mesh is drawn in level-cell sections. A second mesh, without the trims, is the collision.
/// Adapted from rusty-hotel's KitBuilder (Den doc `reuse`): rooms from a plan with openings, lintels over them and
/// profiled mouldings, with world-metre UVs.
/// </summary>
internal sealed class LevelKit : IGeneratedLevel
{
    internal const int WallSlot = 0, FloorSlot = 1, CeilingSlot = 2, TrimSlot = 3, SlotCount = 4;
    private static readonly PlanArea[] AllAreas = [PlanArea.Room, PlanArea.Corridor, PlanArea.Doorway];

    private readonly List<(MeshResource Mesh, Appearance Appearance)> sections = [];
    private readonly MeshResource drawn, solid;
    private readonly RoomPlan plan;
    private readonly KitRecipe recipe;
    private int pilasters;

    /// <param name="materials">The wall, floor, ceiling and trim materials, in slot order.</param>
    internal LevelKit(IEngineContext engine, RoomPlan plan, KitRecipe recipe, Material[] materials, float cellSize)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp(); // load-cost evidence only, never simulation time
        this.plan = plan;
        this.recipe = recipe;
        MeshBuilder mesh = new(SlotCount), collision = new(1);
        Dictionary<(int X, int Z), float> ground = plan.Heights(AllAreas);
        List<List<(Vector2 Point, float Height)>> loops = OpenGround.Outlines(ground);
        foreach (List<(Vector2 Point, float Height)> loop in loops) Walls(mesh, collision, loop);
        Steps(mesh, collision, ground);
        foreach (WalkRectangle r in WalkCells.Merge(ground)) Flat(mesh, collision, CeilingSlot, r, r.Height, recipe.CeilingMetres, -Vector3.UnitY);
        foreach (WalkRectangle r in WalkCells.Merge(ground.ToDictionary(p => p.Key, _ => 0f))) Flat(mesh, collision, FloorSlot, r, 0, recipe.FloorMetres, Vector3.UnitY);
        foreach (TrimDefinition trim in recipe.Trims)
            foreach (List<(Vector2 Point, float Height)> loop in OpenGround.Outlines(plan.Heights(trim.Areas))) Sweep(mesh, loop, trim);
        if (recipe.Pilasters is { } pilaster)
            foreach (List<(Vector2 Point, float Height)> loop in OpenGround.Outlines(plan.Heights(pilaster.Areas))) Pilasters(mesh, collision, loop, pilaster, ground);

        drawn = mesh.Create(engine, materials);
        solid = collision.Create(engine, [materials[WallSlot]]);
        Vector3 min = Vector3.Min(OpenGround.Gltf(plan.Metres(new Vector2(ground.Keys.Min(c => c.X), ground.Keys.Min(c => c.Z))), 0),
            OpenGround.Gltf(plan.Metres(new Vector2(ground.Keys.Max(c => c.X) + 1, ground.Keys.Max(c => c.Z) + 1)), 0));
        using MeshPartition partition = engine.Graphics.PartitionMesh(new MeshPartitionRequest(drawn, min, new Vector3(cellSize)));
        uint parts = engine.Graphics.ReadMeshPartition(partition).PartCount;
        for (uint index = 0; index < parts; index++)
        {
            MeshResource section = engine.Graphics.TakeMeshPartitionPart(new MeshPartitionPartRequest(partition, index));
            sections.Add((section, engine.Graphics.CreateMeshAppearance(section)));
        }
        double seconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalSeconds;
        Summary = FormattableString.Invariant(
            $"kit: {loops.Count} outlines, {pilasters} pilasters, {mesh.VertexCount} vertices, {mesh.TriangleCount} triangles ({collision.TriangleCount} colliding), {sections.Count} sections, {seconds:0.000} s");
    }

    public Transform Placement { get; } = new(Vector3.Zero, Quaternion.Identity, Vector3.One);
    public string Summary { get; }
    public IEnumerable<Appearance> Sections => sections.Select(section => section.Appearance);
    public IReadOnlyList<MeshResource> Collision => [solid];

    public void Dispose()
    {
        foreach ((MeshResource mesh, Appearance appearance) in sections)
        {
            appearance.Dispose();
            mesh.Dispose();
        }
        sections.Clear();
        drawn.Dispose();
        solid.Dispose();
    }

    /// <summary>A wall face along each run of an outline, floor to the run's ceiling, U in metres along the outline.</summary>
    private void Walls(MeshBuilder mesh, MeshBuilder collision, List<(Vector2 Point, float Height)> loop)
    {
        float along = 0;
        for (int i = 0; i < loop.Count; i++)
        {
            (Vector2 a, float height) = (plan.Metres(loop[i].Point), loop[i].Height);
            Vector2 b = plan.Metres(loop[(i + 1) % loop.Count].Point);
            float length = Vector2.Distance(a, b);
            Vector3 inward = OpenGround.Gltf(Inward(a, b), 0);
            float u0 = along / recipe.WallMetres, u1 = (along + length) / recipe.WallMetres, v = height / recipe.WallMetres;
            Face(mesh, WallSlot, OpenGround.Gltf(a, 0), OpenGround.Gltf(b, 0), OpenGround.Gltf(b, height), OpenGround.Gltf(a, height),
                new Vector2(u0, 0), new Vector2(u1, 0), new Vector2(u1, -v), new Vector2(u0, -v), inward);
            Face(collision, 0, OpenGround.Gltf(a, 0), OpenGround.Gltf(b, 0), OpenGround.Gltf(b, height), OpenGround.Gltf(a, height),
                default, default, default, default, inward);
            along += length;
        }
    }

    /// <summary>A lintel or soffit face wherever a taller square meets a lower one, from the lower ceiling up, facing the taller.</summary>
    private void Steps(MeshBuilder mesh, MeshBuilder collision, Dictionary<(int X, int Z), float> ground)
    {
        (int X, int Z)[] sides = [(0, -1), (1, 0), (0, 1), (-1, 0)];
        foreach (((int x, int z), float high) in ground)
        {
            foreach ((int dx, int dz) in sides)
            {
                if (!ground.TryGetValue((x + dx, z + dz), out float low) || low >= high) continue;
                Vector2 centre = new(x + dx * .5f, z + dz * .5f), across = new(-dz * .5f, dx * .5f);
                Vector2 a = plan.Metres(centre - across), b = plan.Metres(centre + across);
                float ua = (dx != 0 ? a.Y : a.X) / recipe.WallMetres, ub = (dx != 0 ? b.Y : b.X) / recipe.WallMetres;
                Vector3 facing = OpenGround.Gltf(new Vector2(-dx, -dz), 0);
                Face(mesh, WallSlot, OpenGround.Gltf(a, low), OpenGround.Gltf(b, low), OpenGround.Gltf(b, high), OpenGround.Gltf(a, high),
                    new Vector2(ua, -low / recipe.WallMetres), new Vector2(ub, -low / recipe.WallMetres),
                    new Vector2(ub, -high / recipe.WallMetres), new Vector2(ua, -high / recipe.WallMetres), facing);
                Face(collision, 0, OpenGround.Gltf(a, low), OpenGround.Gltf(b, low), OpenGround.Gltf(b, high), OpenGround.Gltf(a, high),
                    default, default, default, default, facing);
            }
        }
    }

    /// <summary>A floor or ceiling rectangle at <paramref name="y"/>, UV in world metres over <paramref name="metres"/>.</summary>
    private void Flat(MeshBuilder mesh, MeshBuilder collision, int slot, WalkRectangle r, float y, float metres, Vector3 facing)
    {
        Vector2 min = plan.Metres(new Vector2(r.X0, r.Z0)), max = plan.Metres(new Vector2(r.X1 + 1, r.Z1 + 1));
        Vector2[] corners = [min, new(max.X, min.Y), max, new(min.X, max.Y)];
        Vector3[] at = corners.Select(c => OpenGround.Gltf(c, y)).ToArray();
        Vector2[] uv = corners.Select(c => c / metres).ToArray();
        Face(mesh, slot, at[0], at[1], at[2], at[3], uv[0], uv[1], uv[2], uv[3], facing);
        Face(collision, 0, at[0], at[1], at[2], at[3], default, default, default, default, facing);
    }

    /// <summary>
    /// A trim's profile swept around a closed outline: each profile point offset into the room along the corner's mitre
    /// and up from the anchor (the floor, or the run's ceiling). Each run gets its own vertices, so corners stay crisp.
    /// U is metres along the outline, V metres along the profile.
    /// </summary>
    private void Sweep(MeshBuilder mesh, List<(Vector2 Point, float Height)> loop, TrimDefinition trim)
    {
        int n = loop.Count;
        Vector2[] points = loop.Select(p => plan.Metres(p.Point)).ToArray();
        Vector2[] mitre = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            Vector2 before = Inward(points[(i + n - 1) % n], points[i]), after = Inward(points[i], points[(i + 1) % n]);
            mitre[i] = (before + after) / (1 + Vector2.Dot(before, after));
        }
        float[] arc = new float[trim.Profile.Length];
        for (int p = 1; p < arc.Length; p++)
            arc[p] = arc[p - 1] + Vector2.Distance(Profile(trim, p - 1), Profile(trim, p));
        float along = 0;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            float baseY = trim.Anchor == TrimAnchor.Floor ? 0 : loop[i].Height;
            float length = Vector2.Distance(points[i], points[j]);
            Vector2 inward = Inward(points[i], points[j]);
            for (int p = 0; p + 1 < trim.Profile.Length; p++)
            {
                Vector2 q0 = Profile(trim, p), q1 = Profile(trim, p + 1);
                Vector3 At(int corner, Vector2 q) => OpenGround.Gltf(points[corner] + mitre[corner] * q.X, baseY + q.Y);
                Vector2 normal = new(-(q1.Y - q0.Y), q1.X - q0.X); // perpendicular in (out, up); flipped away from the wall foot
                if (Vector2.Dot(normal, (q0 + q1) / 2) < 0) normal = -normal;
                Vector3 facing = OpenGround.Gltf(inward * normal.X, 0) + Vector3.UnitY * normal.Y;
                float u0 = along / recipe.TrimMetres, u1 = (along + length) / recipe.TrimMetres;
                float v0 = arc[p] / recipe.TrimMetres, v1 = arc[p + 1] / recipe.TrimMetres;
                Face(mesh, TrimSlot, At(i, q0), At(j, q0), At(j, q1), At(i, q1),
                    new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1), facing);
            }
            along += length;
        }
    }

    /// <summary>
    /// Pilasters along each run of an outline: evenly spaced, kept clear of the run's ends, and left out wherever the
    /// ground behind them is open (a doorway). Each is a front and two sides, floor to the run's ceiling, and collides.
    /// </summary>
    private void Pilasters(MeshBuilder mesh, MeshBuilder collision, List<(Vector2 Point, float Height)> loop, PilasterDefinition pilaster,
        Dictionary<(int X, int Z), float> ground)
    {
        for (int i = 0; i < loop.Count; i++)
        {
            Vector2 a = plan.Metres(loop[i].Point), b = plan.Metres(loop[(i + 1) % loop.Count].Point);
            float height = loop[i].Height, length = Vector2.Distance(a, b);
            float span = length - 2 * pilaster.Clearance;
            if (span < 0) continue;
            int count = (int)MathF.Floor(span / pilaster.Spacing) + 1;
            float start = pilaster.Clearance + (span - (count - 1) * pilaster.Spacing) / 2;
            Vector2 along = Vector2.Normalize(b - a), inward = Inward(a, b);
            for (int k = 0; k < count; k++)
            {
                Vector2 centre = a + along * (start + k * pilaster.Spacing);
                Vector2 left = centre - along * pilaster.Width / 2, right = centre + along * pilaster.Width / 2;
                if (Behind(left, right, inward, ground)) continue;
                Vector2 leftFront = left + inward * pilaster.Depth, rightFront = right + inward * pilaster.Depth;
                float u0 = 0, u1 = pilaster.Width / recipe.TrimMetres, d = pilaster.Depth / recipe.TrimMetres, v = -height / recipe.TrimMetres;
                Vector3 front = OpenGround.Gltf(inward, 0), toLeft = OpenGround.Gltf(-along, 0), toRight = OpenGround.Gltf(along, 0);
                Box(leftFront, rightFront, front, new Vector2(u0, 0), new Vector2(u1, v));
                Box(left, leftFront, toLeft, new Vector2(0, 0), new Vector2(d, v));
                Box(rightFront, right, toRight, new Vector2(0, 0), new Vector2(d, v));
                pilasters++;
            }

            void Box(Vector2 p, Vector2 q, Vector3 facing, Vector2 uvMin, Vector2 uvMax)
            {
                Face(mesh, TrimSlot, OpenGround.Gltf(p, 0), OpenGround.Gltf(q, 0), OpenGround.Gltf(q, height), OpenGround.Gltf(p, height),
                    uvMin, new Vector2(uvMax.X, uvMin.Y), uvMax, new Vector2(uvMin.X, uvMax.Y), facing);
                Face(collision, 0, OpenGround.Gltf(p, 0), OpenGround.Gltf(q, 0), OpenGround.Gltf(q, height), OpenGround.Gltf(p, height),
                    default, default, default, default, facing);
            }
        }
    }

    /// <summary>Whether open ground lies just behind the wall anywhere between two points on it (a doorway or other opening).</summary>
    private bool Behind(Vector2 left, Vector2 right, Vector2 inward, Dictionary<(int X, int Z), float> ground)
    {
        float step = plan.Rules.GridMetres;
        int samples = Math.Max(2, (int)MathF.Ceiling(Vector2.Distance(left, right) / step) + 1);
        for (int s = 0; s < samples; s++)
        {
            Vector2 behind = Vector2.Lerp(left, right, s / (float)(samples - 1)) - inward * step / 2;
            if (ground.ContainsKey(Square(behind))) return true;
        }
        return false;
    }

    /// <summary>The grid square holding a point in Unity metres (the inverse of <see cref="RoomPlan.Metres"/> at square centres).</summary>
    private (int X, int Z) Square(Vector2 metres)
    {
        Vector2 corner = (metres + new Vector2(plan.Fine * plan.Rules.GridMetres / 2)) / plan.Rules.GridMetres;
        return ((int)MathF.Floor(corner.X), (int)MathF.Floor(corner.Y));
    }

    private static Vector2 Profile(TrimDefinition trim, int index) => new(trim.Profile[index][0], trim.Profile[index][1]);

    /// <summary>The left-hand (open-side) unit normal of a run walked with open ground on its left, in Unity metres.</summary>
    private static Vector2 Inward(Vector2 from, Vector2 to)
    {
        Vector2 along = Vector2.Normalize(to - from);
        return new Vector2(-along.Y, along.X);
    }

    private static void Face(MeshBuilder mesh, int slot, Vector3 a, Vector3 b, Vector3 c, Vector3 d,
        Vector2 uvA, Vector2 uvB, Vector2 uvC, Vector2 uvD, Vector3 facing) =>
        mesh.Quad(slot, mesh.Vertex(a, uvA), mesh.Vertex(b, uvB), mesh.Vertex(c, uvC), mesh.Vertex(d, uvD), facing);
}
