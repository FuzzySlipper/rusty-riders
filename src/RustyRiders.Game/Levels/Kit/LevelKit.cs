using System.Numerics;
using Rusty.Engine;

namespace RustyRiders.Game.Levels.Kit;

/// <summary>
/// A level built from a world's parametric kit over the room plan. It takes the plan's open ground and builds:
/// - walls along every outline, as tall as the area they face;
/// - a lintel or soffit face wherever a taller area meets a lower one (over doorways, at corridor ends);
/// - ceilings at each area's height over a floor;
/// - the kit's trims swept along the walls of their areas (a ledge may collide);
/// - pilasters against the walls;
/// - an arch over each doorway, when the recipe has one (<see cref="DoorArches"/>).
///
/// Texture coordinates run in world metres (walls by metres along their outline and up), so adjoining pieces keep one
/// texture phase. One mesh is drawn in level-cell sections. A second mesh, without the thin trims, is the collision.
/// Adapted from rusty-hotel's KitBuilder (Den doc `reuse`): rooms from a plan with openings, lintels over them and
/// profiled mouldings, with world-metre UVs.
/// </summary>
internal sealed partial class LevelKit : IGeneratedLevel
{
    internal const int WallSlot = 0, FloorSlot = 1, CeilingSlot = 2, TrimSlot = 3, SlotCount = 4;
    private static readonly PlanArea[] AllAreas = [PlanArea.Room, PlanArea.Corridor, PlanArea.Doorway];

    private readonly List<(MeshResource Mesh, Appearance Appearance)> sections = [];
    private readonly MeshResource drawn, solid;
    private readonly RoomPlan plan;
    private readonly KitRecipe recipe;
    private int pilasters;

    /// <param name="materials">The wall, floor, ceiling and trim materials, in slot order.</param>
    /// <param name="dressing">The props placed over the plan; those with a box collide.</param>
    internal LevelKit(IEngineContext engine, RoomPlan plan, KitRecipe recipe, Material[] materials, float cellSize, IReadOnlyList<DressingPlacement> dressing)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp(); // load-cost evidence only, never simulation time
        this.plan = plan;
        this.recipe = recipe;
        MeshBuilder mesh = new(SlotCount), collision = new(1);
        Dictionary<(int X, int Z), float> ground = plan.Heights(AllAreas);
        List<List<(Vector2 Point, float Height)>> loops = OpenGround.Outlines(ground);
        foreach (List<(Vector2 Point, float Height)> loop in loops) Walls(mesh, collision, loop);
        // An arched doorway takes no flat lintel or ceiling: DoorArches builds its head.
        bool arched = recipe.DoorArch is not null;
        Steps(mesh, collision, ground, arched);
        foreach (WalkRectangle r in WalkCells.Merge(ground).Where(r => !arched || r.Height != plan.Rules.DoorHeight))
            Flat(mesh, collision, CeilingSlot, r, r.Height, recipe.CeilingMetres, -Vector3.UnitY);
        int arches = recipe.DoorArch is { } arch ? DoorArches.Build(mesh, collision, plan, recipe, arch) : 0;
        foreach (WalkRectangle r in WalkCells.Merge(ground.ToDictionary(p => p.Key, _ => 0f))) Flat(mesh, collision, FloorSlot, r, 0, recipe.FloorMetres, Vector3.UnitY);
        foreach (TrimDefinition trim in recipe.Trims) Trim(mesh, collision, trim, ground);
        if (recipe.Pilasters is { } pilaster)
            foreach (List<(Vector2 Point, float Height)> loop in OpenGround.Outlines(plan.Heights(pilaster.Areas))) Pilasters(mesh, collision, loop, pilaster, ground);

        foreach (DressingPlacement prop in dressing)
            if (prop.Box is { } box) Box(collision, prop.Pose.Translation, box);

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
            $"kit: {loops.Count} outlines, {arches} arches, {pilasters} pilasters, {mesh.VertexCount} vertices, {mesh.TriangleCount} triangles ({collision.TriangleCount} colliding), {sections.Count} sections, {seconds:0.000} s");
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
            mesh.Face(WallSlot, OpenGround.Gltf(a, 0), OpenGround.Gltf(b, 0), OpenGround.Gltf(b, height), OpenGround.Gltf(a, height),
                new Vector2(u0, 0), new Vector2(u1, 0), new Vector2(u1, -v), new Vector2(u0, -v), inward);
            collision.Face(0, OpenGround.Gltf(a, 0), OpenGround.Gltf(b, 0), OpenGround.Gltf(b, height), OpenGround.Gltf(a, height),
                default, default, default, default, inward);
            along += length;
        }
    }

    /// <summary>A lintel or soffit face wherever a taller square meets a lower one, from the lower ceiling up, facing the taller.</summary>
    private void Steps(MeshBuilder mesh, MeshBuilder collision, Dictionary<(int X, int Z), float> ground, bool arched)
    {
        (int X, int Z)[] sides = [(0, -1), (1, 0), (0, 1), (-1, 0)];
        foreach (((int x, int z), float high) in ground)
        {
            foreach ((int dx, int dz) in sides)
            {
                if (!ground.TryGetValue((x + dx, z + dz), out float low) || low >= high) continue;
                if (arched && low == plan.Rules.DoorHeight) continue; // the arch's own faces stand here
                Vector2 centre = new(x + dx * .5f, z + dz * .5f), across = new(-dz * .5f, dx * .5f);
                Vector2 a = plan.Metres(centre - across), b = plan.Metres(centre + across);
                float ua = (dx != 0 ? a.Y : a.X) / recipe.WallMetres, ub = (dx != 0 ? b.Y : b.X) / recipe.WallMetres;
                Vector3 facing = OpenGround.Gltf(new Vector2(-dx, -dz), 0);
                mesh.Face(WallSlot, OpenGround.Gltf(a, low), OpenGround.Gltf(b, low), OpenGround.Gltf(b, high), OpenGround.Gltf(a, high),
                    new Vector2(ua, -low / recipe.WallMetres), new Vector2(ub, -low / recipe.WallMetres),
                    new Vector2(ub, -high / recipe.WallMetres), new Vector2(ua, -high / recipe.WallMetres), facing);
                collision.Face(0, OpenGround.Gltf(a, low), OpenGround.Gltf(b, low), OpenGround.Gltf(b, high), OpenGround.Gltf(a, high),
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
        mesh.Face(slot, at[0], at[1], at[2], at[3], uv[0], uv[1], uv[2], uv[3], facing);
        collision.Face(0, at[0], at[1], at[2], at[3], default, default, default, default, facing);
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
                mesh.Face(TrimSlot, OpenGround.Gltf(p, 0), OpenGround.Gltf(q, 0), OpenGround.Gltf(q, height), OpenGround.Gltf(p, height),
                    uvMin, new Vector2(uvMax.X, uvMin.Y), uvMax, new Vector2(uvMin.X, uvMax.Y), facing);
                collision.Face(0, OpenGround.Gltf(p, 0), OpenGround.Gltf(q, 0), OpenGround.Gltf(q, height), OpenGround.Gltf(p, height),
                    default, default, default, default, facing);
            }
        }
    }

    /// <summary>
    /// Whether open ground lies just behind the wall anywhere between two points on it, or within a door arch's ring of
    /// them (a doorway or other opening).
    /// </summary>
    private bool Behind(Vector2 left, Vector2 right, Vector2 inward, Dictionary<(int X, int Z), float> ground)
    {
        float step = plan.Rules.GridMetres, margin = recipe.DoorArch?.Ring?.Width ?? 0;
        Vector2 along = Vector2.Normalize(right - left);
        (left, right) = (left - along * margin, right + along * margin);
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

    /// <summary>An upright box's four sides (glTF space, standing on its base), for a prop the walker meets.</summary>
    private static void Box(MeshBuilder collision, Vector3 foot, Vector3 size)
    {
        Vector3 h = new(size.X / 2, 0, size.Z / 2);
        Vector3[] corners = [foot + new Vector3(-h.X, 0, -h.Z), foot + new Vector3(h.X, 0, -h.Z), foot + new Vector3(h.X, 0, h.Z), foot + new Vector3(-h.X, 0, h.Z)];
        for (int i = 0; i < 4; i++)
        {
            Vector3 a = corners[i], b = corners[(i + 1) % 4], up = Vector3.UnitY * size.Y;
            Vector3 outward = (a + b) / 2 - foot;
            collision.Face(0, a, b, b + up, a + up, default, default, default, default, outward);
        }
    }

    /// <summary>The mesh slot a surface's material is bound to.</summary>
    internal static int Slot(KitSurface surface) => surface switch
    {
        KitSurface.Wall => WallSlot,
        KitSurface.Floor => FloorSlot,
        KitSurface.Ceiling => CeilingSlot,
        _ => TrimSlot,
    };
}
