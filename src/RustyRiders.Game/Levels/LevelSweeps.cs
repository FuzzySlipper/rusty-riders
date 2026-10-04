using System.Numerics;
using Rusty.Engine;

namespace RustyRiders.Game.Levels;

/// <summary>
/// Swept level geometry: the same open ground as <see cref="LevelShells"/>, built as ordinary UV-mapped meshes
/// instead of an extracted field. Each outline of the open ground is traced, its corners rounded, and a wall swept
/// along it with U running along the wall (so a trim sheet flows round corners without seams) and V up it,
/// optionally bulged by seeded waves that fade out at the floor and ceiling. Ceilings cover the merged open
/// rectangles at their height, with step faces where a room's ceiling meets a lower corridor's, over one floor
/// plane. One mesh holds it all: drawn in sections per tile cell, colliding whole.
/// </summary>
internal sealed class LevelSweeps : IGeneratedLevel
{
    private const int WallSlot = 0, FloorSlot = 1, CeilingSlot = 2;
    private const int CornerSegments = 4;

    private readonly List<(MeshResource Mesh, Appearance Appearance)> sections = [];
    private readonly MeshResource whole;

    internal LevelSweeps(IEngineContext engine, ShellDefinition shell, float cellSize, IReadOnlyList<(PlannedTile Tile, string[] Walkable)> tiles,
        Material walls, Material floor, float floorMetres, int seed)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp(); // load-cost evidence only, never simulation time
        SweepDefinition sweep = shell.Sweep;
        float step = cellSize / WalkCells.PerTile;
        Dictionary<(int X, int Z), float> open = WalkCells.Open(tiles, shell);
        List<WalkRectangle> rectangles = WalkCells.Merge(open);
        MeshBuilder mesh = new();

        Bulge bulge = new(sweep.Bulge, sweep.BulgeWavelength, seed);
        List<List<(Vector2 Point, float Height)>> loops = Outlines(open);
        foreach (List<(Vector2 Point, float Height)> loop in loops)
            SweepWall(mesh, Round(loop, sweep.CornerRadius / step), step, sweep, bulge);
        foreach (WalkRectangle r in rectangles)
            AddCeiling(mesh, r, step, sweep);
        AddSteps(mesh, open, step);
        Vector3 min = AddFloor(mesh, open, step, floorMetres);

        whole = mesh.Create(engine, [walls, floor, walls]);
        using MeshPartition partition = engine.Graphics.PartitionMesh(new MeshPartitionRequest(whole, min, new Vector3(cellSize)));
        uint parts = engine.Graphics.ReadMeshPartition(partition).PartCount;
        for (uint index = 0; index < parts; index++)
        {
            MeshResource section = engine.Graphics.TakeMeshPartitionPart(new MeshPartitionPartRequest(partition, index));
            sections.Add((section, engine.Graphics.CreateMeshAppearance(section)));
        }
        double seconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalSeconds;
        Summary = FormattableString.Invariant(
            $"swept: {loops.Count} outlines, {rectangles.Count} ceilings, {mesh.VertexCount} vertices, {mesh.TriangleCount} triangles, {sections.Count} sections, {seconds:0.000} s");
    }

    public Transform Placement { get; } = new(Vector3.Zero, Quaternion.Identity, Vector3.One);
    public string Summary { get; }
    public IEnumerable<Appearance> Sections => sections.Select(section => section.Appearance);
    public IReadOnlyList<MeshResource> Collision => [whole];

    public void Dispose()
    {
        foreach ((MeshResource mesh, Appearance appearance) in sections)
        {
            appearance.Dispose();
            mesh.Dispose();
        }
        sections.Clear();
        whole.Dispose();
    }

    /// <summary>
    /// The open ground's outlines as closed loops of cell corners (cell units, corner (i, j) at (i - ½, j - ½)), each
    /// walked with open ground on its left. Each corner carries the ceiling height of the open cell along the edge
    /// that leaves it; straight runs of one height are merged.
    /// </summary>
    private static List<List<(Vector2 Point, float Height)>> Outlines(Dictionary<(int X, int Z), float> open)
    {
        Dictionary<(int X, int Z), List<((int X, int Z) End, float Height)>> outgoing = [];
        void Edge((int, int) from, (int, int) to, float height)
        {
            if (!outgoing.TryGetValue(from, out var list)) outgoing[from] = list = [];
            list.Add((to, height));
        }
        foreach (((int x, int z), float h) in open)
        {
            if (!open.ContainsKey((x, z - 1))) Edge((x, z), (x + 1, z), h);
            if (!open.ContainsKey((x + 1, z))) Edge((x + 1, z), (x + 1, z + 1), h);
            if (!open.ContainsKey((x, z + 1))) Edge((x + 1, z + 1), (x, z + 1), h);
            if (!open.ContainsKey((x - 1, z))) Edge((x, z + 1), (x, z), h);
        }

        List<List<(Vector2, float)>> loops = [];
        while (outgoing.FirstOrDefault(pair => pair.Value.Count > 0) is { Value: not null } first)
        {
            List<((int X, int Z) Corner, float Height)> chain = [];
            (int X, int Z) start = first.Key, at = start;
            (int X, int Z) heading = (0, 0); // none yet: any edge may start the loop
            do
            {
                List<((int X, int Z) End, float Height)> choices = outgoing[at];
                // Where two cells touch only at this corner, keep turning left so each loop stays one outline.
                int pick = 0;
                for (int i = 1; i < choices.Count; i++)
                    if (Turn(heading, Direction(at, choices[i].End)) > Turn(heading, Direction(at, choices[pick].End))) pick = i;
                ((int X, int Z) end, float height) = choices[pick];
                choices.RemoveAt(pick);
                chain.Add((at, height));
                heading = Direction(at, end);
                at = end;
            }
            while (at != start);

            List<(Vector2, float)> loop = [];
            for (int i = 0; i < chain.Count; i++)
            {
                ((int X, int Z) corner, float height) = chain[i];
                ((int X, int Z) previous, float previousHeight) = chain[(i + chain.Count - 1) % chain.Count];
                (int X, int Z) next = chain[(i + 1) % chain.Count].Corner;
                bool straight = Direction(previous, corner) == Direction(corner, next) && previousHeight == height;
                if (!straight) loop.Add((new Vector2(corner.X - .5f, corner.Z - .5f), height));
            }
            loops.Add(loop);
        }
        return loops;
    }

    private static (int X, int Z) Direction((int X, int Z) from, (int X, int Z) to) => (Math.Sign(to.X - from.X), Math.Sign(to.Z - from.Z));

    /// <summary>1 for a left turn (seen from above, x east and z north), 0 straight, -1 right.</summary>
    private static int Turn((int X, int Z) a, (int X, int Z) b) => Math.Sign(a.X * b.Z - a.Z * b.X);

    /// <summary>
    /// Each corner of a loop replaced by a quadratic arc of up to <paramref name="radius"/> cells (half of either
    /// neighbouring run at most). Arcs always cut into the open side, over ground that still has floor and ceiling.
    /// </summary>
    private static List<(Vector2 Point, float Height)> Round(List<(Vector2 Point, float Height)> loop, float radius)
    {
        List<(Vector2 Point, float Height)> rounded = [];
        for (int i = 0; i < loop.Count; i++)
        {
            Vector2 previous = loop[(i + loop.Count - 1) % loop.Count].Point, corner = loop[i].Point, next = loop[(i + 1) % loop.Count].Point;
            float heightIn = loop[(i + loop.Count - 1) % loop.Count].Height, heightOut = loop[i].Height;
            float r = MathF.Min(radius, MathF.Min(Vector2.Distance(previous, corner), Vector2.Distance(corner, next)) / 2);
            if (r <= 0)
            {
                rounded.Add((corner, heightOut));
                continue;
            }
            Vector2 entry = corner + Vector2.Normalize(previous - corner) * r, exit = corner + Vector2.Normalize(next - corner) * r;
            float arcHeight = MathF.Max(heightIn, heightOut);
            for (int s = 0; s <= CornerSegments; s++)
            {
                float t = s / (float)CornerSegments;
                Vector2 point = (1 - t) * (1 - t) * entry + 2 * (1 - t) * t * corner + t * t * exit;
                rounded.Add((point, s == CornerSegments ? heightOut : arcHeight));
            }
        }
        // Arcs meeting mid-run leave coincident points; drop them (keeping the later height) so every run has a direction.
        List<(Vector2 Point, float Height)> distinct = [];
        foreach ((Vector2 point, float height) in rounded)
        {
            if (distinct.Count > 0 && Vector2.DistanceSquared(distinct[^1].Point, point) < 1e-6f) distinct[^1] = (point, height);
            else distinct.Add((point, height));
        }
        if (distinct.Count > 1 && Vector2.DistanceSquared(distinct[0].Point, distinct[^1].Point) < 1e-6f) distinct.RemoveAt(distinct.Count - 1);
        return distinct;
    }

    /// <summary>
    /// A wall along a closed loop: columns at most <see cref="SweepDefinition.ColumnMetres"/> apart, each as tall as
    /// the higher run beside it (any excess hides above the lower ceiling). U is metres along the loop, V metres up,
    /// both over <see cref="SweepDefinition.WallMetres"/>.
    /// </summary>
    private static void SweepWall(MeshBuilder mesh, List<(Vector2 Point, float Height)> loop, float step, SweepDefinition sweep, Bulge bulge)
    {
        List<(Vector2 Point, Vector2 Outward, float Height, float Along)> columns = [];
        float along = 0;
        for (int i = 0; i <= loop.Count; i++)
        {
            (Vector2 a, float height) = loop[i % loop.Count];
            Vector2 b = loop[(i + 1) % loop.Count].Point;
            Vector2 previous = loop[(i + loop.Count - 1) % loop.Count].Point;
            float heightBefore = loop[(i + loop.Count - 1) % loop.Count].Height;
            Vector2 sum = Outward(previous, a) + Outward(a, b);
            Vector2 outward = sum.LengthSquared() > 1e-6f ? Vector2.Normalize(sum) : Outward(a, b);
            columns.Add((a * step, outward, MathF.Max(height, heightBefore), along));
            if (i == loop.Count) break;
            float length = Vector2.Distance(a, b) * step;
            int pieces = Math.Max(1, (int)MathF.Ceiling(length / sweep.ColumnMetres));
            for (int p = 1; p < pieces; p++)
                columns.Add((Vector2.Lerp(a, b, p / (float)pieces) * step, Outward(a, b), height, along + length * p / pieces));
            along += length;
        }
        float tallest = columns.Max(column => column.Height);
        int rows = Math.Max(2, (int)MathF.Ceiling(tallest / sweep.ColumnMetres));
        uint[,] grid = new uint[columns.Count, rows + 1];
        for (int c = 0; c < columns.Count; c++)
        {
            (Vector2 point, Vector2 outward, float height, float u) = columns[c];
            for (int row = 0; row <= rows; row++)
            {
                float y = height * row / rows;
                float push = bulge.At(u, y) * MathF.Sin(MathF.PI * y / height);
                Vector2 at = point + outward * push;
                grid[c, row] = mesh.Vertex(Gltf(at, y), new Vector2(u / sweep.WallMetres, -y / sweep.WallMetres));
            }
        }
        for (int c = 0; c + 1 < columns.Count; c++)
        {
            Vector3 inward = -Gltf(columns[c].Outward + columns[c + 1].Outward, 0);
            for (int row = 0; row < rows; row++)
                mesh.Quad(WallSlot, grid[c, row], grid[c + 1, row], grid[c + 1, row + 1], grid[c, row + 1], inward);
        }
    }

    /// <summary>The right-hand (rock-side) unit normal of a run walked with open ground on its left.</summary>
    private static Vector2 Outward(Vector2 from, Vector2 to)
    {
        Vector2 along = Vector2.Normalize(to - from);
        return new Vector2(along.Y, -along.X);
    }

    private static void AddCeiling(MeshBuilder mesh, WalkRectangle r, float step, SweepDefinition sweep)
    {
        Vector2 min = new((r.X0 - .5f) * step, (r.Z0 - .5f) * step), max = new((r.X1 + .5f) * step, (r.Z1 + .5f) * step);
        uint Corner(float x, float z) => mesh.Vertex(Gltf(new Vector2(x, z), r.Height), new Vector2(x, z) / sweep.WallMetres);
        mesh.Quad(CeilingSlot, Corner(min.X, min.Y), Corner(max.X, min.Y), Corner(max.X, max.Y), Corner(min.X, max.Y), -Vector3.UnitY);
    }

    /// <summary>A vertical face wherever a taller open cell meets a lower one, from the lower ceiling up, facing the taller cell.</summary>
    private static void AddSteps(MeshBuilder mesh, Dictionary<(int X, int Z), float> open, float step)
    {
        (int X, int Z)[] sides = [(0, -1), (1, 0), (0, 1), (-1, 0)];
        foreach (((int x, int z), float high) in open)
        {
            foreach ((int dx, int dz) in sides)
            {
                if (!open.TryGetValue((x + dx, z + dz), out float low) || low >= high) continue;
                Vector2 centre = new(x + dx * .5f, z + dz * .5f), across = new(-dz * .5f, dx * .5f);
                Vector2 a = (centre - across) * step, b = (centre + across) * step;
                uint V(Vector2 p, float y) => mesh.Vertex(Gltf(p, y), new Vector2(0, 0));
                mesh.Quad(CeilingSlot, V(a, low), V(b, low), V(b, high), V(a, high), Gltf(new Vector2(-dx, -dz), 0));
            }
        }
    }

    /// <summary>
    /// One floor plane under the whole open ground and a margin, so rounded or bulged walls never leave a gap; returns
    /// its least corner, where tile-cell sections start.
    /// </summary>
    private static Vector3 AddFloor(MeshBuilder mesh, Dictionary<(int X, int Z), float> open, float step, float metres)
    {
        const int Margin = 2;
        Vector2 min = new((open.Keys.Min(c => c.X) - Margin) * step, (open.Keys.Min(c => c.Z) - Margin) * step);
        Vector2 max = new((open.Keys.Max(c => c.X) + Margin) * step, (open.Keys.Max(c => c.Z) + Margin) * step);
        uint Corner(float x, float z) => mesh.Vertex(Gltf(new Vector2(x, z), 0), new Vector2(x, z) / metres);
        mesh.Quad(FloorSlot, Corner(min.X, min.Y), Corner(max.X, min.Y), Corner(max.X, max.Y), Corner(min.X, max.Y), Vector3.UnitY);
        return Vector3.Min(Gltf(min, 0), Gltf(max, 0));
    }

    /// <summary>A Unity-space point (metres on the ground plane) and height in glTF space: Unity is the X mirror.</summary>
    private static Vector3 Gltf(Vector2 point, float y) => new(-point.X, y, point.Y);

    /// <summary>Seeded smooth wall relief in metres, ±<c>amplitude</c>, over metres along the wall and up it.</summary>
    private sealed class Bulge(float amplitude, float wavelength, int seed)
    {
        private readonly (float U, float Y, float Phase)[] waves = Waves(seed);

        internal float At(float along, float y)
        {
            if (amplitude == 0) return 0;
            float sum = 0;
            for (int i = 0; i < waves.Length; i++)
            {
                float scale = MathF.Tau / wavelength * (1 + i * .9f);
                sum += MathF.Sin((along * waves[i].U + y * waves[i].Y) * scale + waves[i].Phase) / (1 + i);
            }
            return amplitude * sum / 1.83f; // 1 + 1/2 + 1/3: the waves' largest sum
        }

        private static (float, float, float)[] Waves(int seed)
        {
            Random random = new(seed);
            return Enumerable.Range(0, 3).Select(_ =>
            {
                float angle = (float)(random.NextDouble() * MathF.PI);
                return (MathF.Cos(angle), MathF.Sin(angle) * .6f, (float)(random.NextDouble() * MathF.Tau));
            }).ToArray();
        }
    }

    /// <summary>Positions, smooth normals, uvs and per-material triangle lists for one retained mesh.</summary>
    private sealed class MeshBuilder
    {
        private readonly List<Vector3> positions = [];
        private readonly List<Vector3> normals = [];
        private readonly List<Vector2> uvs = [];
        private readonly List<uint>[] slots = [[], [], []];

        internal int VertexCount => positions.Count;
        internal int TriangleCount => slots.Sum(slot => slot.Count) / 3;

        internal uint Vertex(Vector3 position, Vector2 uv)
        {
            positions.Add(position);
            normals.Add(Vector3.Zero);
            uvs.Add(uv);
            return (uint)positions.Count - 1;
        }

        /// <summary>Two triangles wound to face <paramref name="facing"/>; their normals accumulate on the corners.</summary>
        internal void Quad(int slot, uint a, uint b, uint c, uint d, Vector3 facing)
        {
            Triangle(slot, a, b, c, facing);
            Triangle(slot, a, c, d, facing);
        }

        internal MeshResource Create(IEngineContext engine, Material[] materials)
        {
            List<uint> indices = [];
            List<MeshGroup> groups = [];
            for (int slot = 0; slot < slots.Length; slot++)
            {
                if (slots[slot].Count == 0) continue;
                groups.Add(new MeshGroup((uint)slot, (uint)indices.Count, (uint)slots[slot].Count));
                indices.AddRange(slots[slot]);
            }
            Vector3[] unit = normals.Select(n => n == Vector3.Zero ? Vector3.UnitY : Vector3.Normalize(n)).ToArray();
            MeshMaterialBinding[] bindings = materials.Select((material, slot) => new MeshMaterialBinding((uint)slot, material)).ToArray();
            return engine.Graphics.CreateMeshResource(new MeshResourceCreateRequest(positions.ToArray(), unit, uvs.ToArray(),
                indices.ToArray(), groups.ToArray(), bindings));
        }

        private void Triangle(int slot, uint a, uint b, uint c, Vector3 facing)
        {
            Vector3 normal = Vector3.Cross(positions[(int)b] - positions[(int)a], positions[(int)c] - positions[(int)a]);
            if (normal.LengthSquared() < 1e-12f) return;
            if (Vector3.Dot(normal, facing) < 0)
            {
                (b, c) = (c, b);
                normal = -normal;
            }
            slots[slot].AddRange([a, b, c]);
            foreach (uint corner in (uint[])[a, b, c]) normals[(int)corner] += normal;
        }
    }
}
