using System.Numerics;
using Rusty.Engine;

namespace RustyRiders.Game.Levels.Kit;

/// <summary>
/// The geometry of a level built from a world's parametric kit over the room plan, made without the Engine so it can be
/// built off the update (<see cref="LevelKit"/> makes it into meshes). It takes the plan's open ground and builds:
/// - walls along every outline, as tall as the area they face;
/// - a lintel or soffit face wherever a taller area meets a lower one (over doorways, at corridor ends);
/// - ceilings at each area's height over a floor;
/// - the kit's trims swept along the walls of their areas (a ledge may collide);
/// - pilasters against the walls;
/// - an arch over each doorway, when the recipe has one (<see cref="DoorArches"/>);
/// - the recipe's details: runners (<see cref="KitRunners"/>), ceiling ribs (<see cref="CeilingRibs"/>), blind arches
///   (<see cref="BlindArches"/>), sconces (<see cref="Sconces"/>), whose point lights the kit owns, and columns
///   (<see cref="KitColumns"/>).
///
/// Texture coordinates run in world metres (walls by metres along their outline and up), so adjoining pieces keep one
/// texture phase. The drawn geometry is cut into bands of level-cell columns (<see cref="Bands"/>), each one mesh, so no
/// one update makes the whole level; stones whose faces the shader carves are banded apart. A second mesh, without the
/// thin trims, is the collision. Adapted from rusty-hotel's KitBuilder (Den doc `reuse`): rooms from a plan with
/// openings, lintels over them and profiled mouldings, with world-metre UVs.
/// </summary>
internal sealed partial class KitGeometry
{
    private const float SconceClearance = .5f; // wall kept for a sconce's bracket beside a doorway
    // About this many triangles a band: a deferred create still copies its streams in the update (about 70 ms a million).
    private const int BandTriangles = 40_000;
    private static readonly PlanArea[] AllAreas = [PlanArea.Room, PlanArea.Corridor, PlanArea.Doorway];

    private readonly RoomPlan plan;
    private readonly KitRecipe recipe;
    private int pilasters;

    /// <param name="dressing">The props placed over the plan; those with a box collide.</param>
    /// <param name="seed">The level's seed, which lays the wall stones.</param>
    internal KitGeometry(RoomPlan plan, KitRecipe recipe, float cellSize, IReadOnlyList<DressingPlacement> dressing, int seed)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp(); // load-cost evidence only, never simulation time
        this.plan = plan;
        this.recipe = recipe;
        MeshBuilder mesh = new(LevelKit.SlotCount), collision = new(1);
        Dictionary<(int X, int Z), float> ground = plan.Heights(AllAreas);
        List<List<(Vector2 Point, float Height)>> loops = OpenGround.Walls(ground);
        // Plan pilasters and recessed alcove bays before laying walls and wall stones so recessed alcoves cut cleanly into the wall.
        List<PilasterPlace> places = recipe.Pilasters is { } pilaster ? Places(pilaster, ground) : [];
        List<BlindArchBay> blindBays = recipe.BlindArches is { } blindArch
            ? BlindArches.Plan(plan, blindArch, recipe.Pilasters!, places.Where(p => plan.Rules.Area(p.Height) == PlanArea.Room), (l, r, i) => Behind(l, r, i, ground), ground)
            : [];
        foreach (List<(Vector2 Point, float Height)> loop in loops) Walls(mesh, collision, loop, blindBays);
        int stones = recipe.WallStones is { } wallStones ? WallStones.Outlines(mesh, plan, recipe, wallStones, loops, seed, blindBays) : 0;
        // An arched doorway takes no flat lintel or ceiling: DoorArches builds its head.
        bool arched = recipe.DoorArch is not null;
        Steps(mesh, collision, ground, arched);
        foreach (WalkRectangle r in WalkCells.Merge(ground).Where(r => !arched || r.Height != plan.Rules.DoorHeight))
            Flat(mesh, collision, LevelKit.CeilingSlot, r, r.Height, recipe.Metres(KitSurface.Ceiling), -Vector3.UnitY);
        int voussoirs = 0;
        int arches = recipe.DoorArch is { } arch ? DoorArches.Build(mesh, collision, plan, recipe, arch, seed, ref stones, ref voussoirs) : 0;
        int diagonals = loops.Sum(loop => loop.Where((p, i) => p.Point.X != loop[(i + 1) % loop.Count].Point.X
            && p.Point.Y != loop[(i + 1) % loop.Count].Point.Y).Count());
        foreach (WalkRectangle r in WalkCells.Merge(ground.ToDictionary(p => p.Key, _ => 0f))) Flat(mesh, collision, LevelKit.FloorSlot, r, 0, recipe.Metres(KitSurface.Floor), Vector3.UnitY);
        foreach (TrimDefinition trim in recipe.Trims) Trim(mesh, collision, trim, ground, seed);
        foreach (PilasterPlace place in places) Pilaster(mesh, collision, place, recipe.Pilasters!);
        List<string> details = recipe.WallStones is null ? [] : [$"{stones} wall stones"];
        if (recipe.Runners is { } runners) details.Add($"{KitRunners.Build(mesh, plan, recipe, runners)} runners");
        if (recipe.Ribs is { } ribs)
            details.Add($"{CeilingRibs.Build(mesh, plan, recipe, ribs, places.Where(p => plan.Rules.Area(p.Height) == PlanArea.Room), seed, ref voussoirs)} ribs");
        if (recipe.BlindArches is { } blind)
            details.Add($"{BlindArches.Build(mesh, recipe, blind, blindBays, seed, ref voussoirs)} blind arches");
        if (recipe.Ribs?.Stones is not null || recipe.DoorArch?.Ring?.Stones is not null || recipe.BlindArches?.Ring.Stones is not null) details.Add($"{voussoirs} voussoirs");
        if (recipe.Columns is { } columns)
        {
            (int hall, int corners) = KitColumns.Build(mesh, collision, plan, recipe, columns, seed);
            details.Add($"{hall} columns, {corners} corner piers");
        }
        Sconces = recipe.Sconces is { } sconce ? Kit.Sconces.Build(mesh, plan, recipe, sconce) : [];
        if (recipe.Sconces is not null) details.Add($"{Sconces.Count} sconces");

        foreach (DressingPlacement prop in dressing)
            if (prop.Box is { } box) Box(collision, prop.Pose.Translation, box);

        Solid = collision;
        CellSize = cellSize;
        Vector3 a = OpenGround.Gltf(plan.Metres(new Vector2(ground.Keys.Min(c => c.X), ground.Keys.Min(c => c.Z))), 0),
            b = OpenGround.Gltf(plan.Metres(new Vector2(ground.Keys.Max(c => c.X) + 1, ground.Keys.Max(c => c.Z) + 1)), 0);
        Min = Vector3.Min(a, b);
        int columnCount = Math.Max(1, (int)MathF.Ceiling((Vector3.Max(a, b).X - Min.X) / cellSize));
        Bands = [.. Band(mesh, columnCount), .. mesh.HasShaded ? Band(mesh.Shaded, columnCount) : []];
        Seconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalSeconds;
        Summary = FormattableString.Invariant(
            $"kit: {loops.Count} outlines, {diagonals} diagonal walls, {arches} arches, {pilasters} pilasters, {string.Concat(details.Select(d => d + ", "))}{mesh.VertexCount} vertices, {mesh.TriangleCount} triangles ({collision.TriangleCount} colliding)");
    }

    /// <summary>The drawn geometry in bands of level-cell columns, then its shaded stones in theirs: each one mesh, cut into sections.</summary>
    internal IReadOnlyList<MeshBuilder> Bands { get; }
    /// <summary>The collision mesh: the drawn geometry without the thin trims, and the props' boxes.</summary>
    internal MeshBuilder Solid { get; }
    internal List<LightDescriptor> Sconces { get; }
    /// <summary>The level's least corner in glTF space, where its section grid starts.</summary>
    internal Vector3 Min { get; }
    internal float CellSize { get; }
    /// <summary>What was built, for the status line.</summary>
    internal string Summary { get; }
    /// <summary>How long the geometry took (load-cost evidence).</summary>
    internal double Seconds { get; }

    /// <summary>A mesh cut into bands of whole level-cell columns, about <see cref="BandTriangles"/> each.</summary>
    private MeshBuilder[] Band(MeshBuilder mesh, int columnCount)
    {
        int count = Math.Clamp((mesh.TriangleCount - (mesh.HasShaded ? mesh.Shaded.TriangleCount : 0) + BandTriangles - 1) / BandTriangles, 1, columnCount);
        int perBand = (columnCount + count - 1) / count;
        count = (columnCount + perBand - 1) / perBand;
        float minX = Min.X, cell = CellSize;
        return mesh.Split(count, p => Math.Clamp((int)MathF.Floor((p.X - minX) / cell) / perBand, 0, count - 1));
    }

    /// <summary>
    /// A wall face along each run of an outline, floor to the run's ceiling, U in metres along the outline. Behind wall
    /// stones it is the stones' backing, seen in their joints. Recessed alcove bays cut clean arched openings in the
    /// drawn wall while keeping collision flat across the run.
    /// </summary>
    private void Walls(MeshBuilder mesh, MeshBuilder collision, List<(Vector2 Point, float Height)> loop, IReadOnlyList<BlindArchBay> bays)
    {
        KitSurface surface = recipe.WallStones?.Backing ?? KitSurface.Wall;
        int slot = LevelKit.Slot(surface);
        float metres = recipe.Metres(surface), along = 0;
        for (int i = 0; i < loop.Count; i++)
        {
            (Vector2 a, float height) = (plan.Metres(loop[i].Point), loop[i].Height);
            Vector2 b = plan.Metres(loop[(i + 1) % loop.Count].Point);
            float length = Vector2.Distance(a, b);
            Vector2 dir = Vector2.Normalize(b - a), in2 = Inward(a, b);
            Vector3 inward = OpenGround.Gltf(in2, 0);
            collision.Face(0, OpenGround.Gltf(a, 0), OpenGround.Gltf(b, 0), OpenGround.Gltf(b, height), OpenGround.Gltf(a, height),
                default, default, default, default, inward);

            // Find any recessed alcove bays hosted on this wall run, ordered along the run.
            List<(float Start, float End, BlindArchBay Bay)> cuts = [];
            foreach (BlindArchBay bay in bays)
            {
                if (bay.Recess <= 0 || Vector2.Dot(bay.Inward, in2) < .95f) continue;
                Vector2 rel = bay.Middle - a;
                if (MathF.Abs(Vector2.Dot(rel, in2)) > .15f) continue;
                float mid = Vector2.Dot(rel, dir);
                if (mid - bay.Radius < -1e-2f || mid + bay.Radius > length + 1e-2f) continue;
                cuts.Add((MathF.Max(0, mid - bay.Radius), MathF.Min(length, mid + bay.Radius), bay));
            }
            cuts.Sort((x, y) => x.Start.CompareTo(y.Start));

            void Quad(float s0, float s1, float y0, float y1, int quadSlot = -1, float quadMetres = 0)
            {
                if (s1 - s0 <= 1e-3f || y1 - y0 <= 1e-3f) return;
                int useSlot = quadSlot >= 0 ? quadSlot : slot;
                float useMetres = quadMetres > 0 ? quadMetres : metres;
                Vector2 p0 = a + dir * s0, p1 = a + dir * s1;
                mesh.Face(useSlot, OpenGround.Gltf(p0, y0), OpenGround.Gltf(p1, y0), OpenGround.Gltf(p1, y1), OpenGround.Gltf(p0, y1),
                    new Vector2((along + s0) / useMetres, -y0 / useMetres), new Vector2((along + s1) / useMetres, -y0 / useMetres),
                    new Vector2((along + s1) / useMetres, -y1 / useMetres), new Vector2((along + s0) / useMetres, -y1 / useMetres), inward);
            }

            float cursor = 0;
            int wallSlot = LevelKit.Slot(KitSurface.Wall);
            float wm = recipe.Metres(KitSurface.Wall);
            foreach ((float start, float end, BlindArchBay bay) in cuts)
            {
                Quad(cursor, start, 0, height);
                // Below the alcove shelf, draw in Wall surface so there is no dark backing slit above the floor ledge.
                Quad(start, end, 0, bay.Sill, wallSlot, wm);
                // Spandrel wall above the arched alcove head up to the ceiling (drawn in Wall surface so curve meets stones cleanly).
                (float S, float Y)[] head = DoorArches.Curve(bay.Radius, bay.Spring, bay.Radius, bay.Segments, bay.Pointed);
                float mid = (start + end) / 2;
                for (int k = 0; k < bay.Segments; k++)
                {
                    float s0 = mid + head[k].S, y0 = head[k].Y, s1 = mid + head[k + 1].S, y1 = head[k + 1].Y;
                    Vector2 p0 = a + dir * s0, p1 = a + dir * s1;
                    mesh.Face(wallSlot, OpenGround.Gltf(p0, y0), OpenGround.Gltf(p1, y1), OpenGround.Gltf(p1, height), OpenGround.Gltf(p0, height),
                        new Vector2((along + s0) / wm, -y0 / wm), new Vector2((along + s1) / wm, -y1 / wm),
                        new Vector2((along + s1) / wm, -height / wm), new Vector2((along + s0) / wm, -height / wm), inward);
                }
                cursor = end;
            }
            Quad(cursor, length, 0, height);
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
                float ua = (dx != 0 ? a.Y : a.X) / recipe.Metres(KitSurface.Wall), ub = (dx != 0 ? b.Y : b.X) / recipe.Metres(KitSurface.Wall);
                Vector3 facing = OpenGround.Gltf(new Vector2(-dx, -dz), 0);
                mesh.Face(LevelKit.WallSlot, OpenGround.Gltf(a, low), OpenGround.Gltf(b, low), OpenGround.Gltf(b, high), OpenGround.Gltf(a, high),
                    new Vector2(ua, -low / recipe.Metres(KitSurface.Wall)), new Vector2(ub, -low / recipe.Metres(KitSurface.Wall)),
                    new Vector2(ub, -high / recipe.Metres(KitSurface.Wall)), new Vector2(ua, -high / recipe.Metres(KitSurface.Wall)), facing);
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
    /// The pilasters' places along each run of the outlines of the pilasters' areas: evenly spaced, kept clear of the
    /// run's ends, and left out wherever the ground behind them is open (a doorway).
    /// </summary>
    private List<PilasterPlace> Places(PilasterDefinition pilaster, Dictionary<(int X, int Z), float> ground)
    {
        List<PilasterPlace> places = [];
        int run = 0;
        foreach (List<(Vector2 Point, float Height)> loop in OpenGround.Walls(plan.Heights(pilaster.Areas)))
        {
            for (int i = 0; i < loop.Count; i++, run++)
            {
                Vector2 a = plan.Metres(loop[i].Point), b = plan.Metres(loop[(i + 1) % loop.Count].Point);
                float span = Vector2.Distance(a, b) - 2 * pilaster.Clearance;
                if (span < 0) continue;
                int count = (int)MathF.Floor(span / pilaster.Spacing) + 1;
                float start = pilaster.Clearance + (span - (count - 1) * pilaster.Spacing) / 2;
                Vector2 along = Vector2.Normalize(b - a), inward = Inward(a, b);
                for (int k = 0; k < count; k++)
                {
                    Vector2 centre = a + along * (start + k * pilaster.Spacing);
                    if (!Behind(centre - along * pilaster.Width / 2, centre + along * pilaster.Width / 2, inward, ground))
                        places.Add(new PilasterPlace(centre, along, inward, loop[i].Height, run, k));
                }
            }
        }
        return places;
    }

    /// <summary>
    /// A pilaster: optionally a battered stone base plinth (<see cref="PilasterDefinition.PlinthHeight"/>) under a sleek
    /// upper terracotta shaft with sweeping curved arch brackets at the top, or a single flat pilaster floor to ceiling.
    /// </summary>
    private void Pilaster(MeshBuilder mesh, MeshBuilder collision, PilasterPlace place, PilasterDefinition pilaster)
    {
        (Vector2 along, Vector2 inward, float height) = (place.Along, place.Inward, place.Height);
        float bw = pilaster.BaseWidth + 2 * pilaster.PlinthBatter, bd = pilaster.BaseDepth + pilaster.PlinthBatter;
        Vector2 colLeft = place.Centre - along * bw / 2, colRight = place.Centre + along * bw / 2;
        Vector2 colLeftF = colLeft + inward * bd, colRightF = colRight + inward * bd;
        // Collision keeps a single clean upright box covering the pilaster's widest footprint.
        collision.Face(0, OpenGround.Gltf(colLeftF, 0), OpenGround.Gltf(colRightF, 0), OpenGround.Gltf(colRightF, height), OpenGround.Gltf(colLeftF, height), default, default, default, default, OpenGround.Gltf(inward, 0));
        collision.Face(0, OpenGround.Gltf(colLeft, 0), OpenGround.Gltf(colLeftF, 0), OpenGround.Gltf(colLeftF, height), OpenGround.Gltf(colLeft, height), default, default, default, default, OpenGround.Gltf(-along, 0));
        collision.Face(0, OpenGround.Gltf(colRightF, 0), OpenGround.Gltf(colRight, 0), OpenGround.Gltf(colRight, height), OpenGround.Gltf(colRightF, height), default, default, default, default, OpenGround.Gltf(along, 0));

        float shaftY0 = MathF.Min(pilaster.PlinthHeight, height * .45f);
        if (shaftY0 > 0)
        {
            float collarH = pilaster.PlinthBatter > 0 ? MathF.Min(.22f, shaftY0 * .22f) : 0;
            float bodyTop = shaftY0 - collarH;
            PilasterBox(mesh, pilaster.PlinthSurface, 0, bodyTop, pilaster.BaseWidth + 2 * pilaster.PlinthBatter, pilaster.BaseDepth + pilaster.PlinthBatter,
                pilaster.BaseWidth, pilaster.BaseDepth, topCap: collarH == 0);
            if (collarH > 0)
                PilasterBox(mesh, pilaster.PlinthSurface, bodyTop, shaftY0, pilaster.BaseWidth + .10f, pilaster.BaseDepth + .06f,
                    pilaster.BaseWidth + .10f, pilaster.BaseDepth + .06f, topCap: true, bottomCap: true);
        }
        // Sleek upper terracotta shaft.
        PilasterBox(mesh, KitSurface.Trim, shaftY0, height, pilaster.Width, pilaster.Depth, pilaster.Width, pilaster.Depth, topCap: false);

        // Sweeping Gothic terracotta arch brackets flaring sideways along the wall and forward into the room.
        if (pilaster.BracketReach > 0 && pilaster.BracketDrop > 0 && height - shaftY0 > 1.2f)
        {
            float drop = MathF.Min(pilaster.BracketDrop, (height - shaftY0) * .6f);
            float sideHalf = MathF.Min(pilaster.Depth * .42f, .32f);
            float fwdHalf = MathF.Min(pilaster.Width * .32f, .32f);
            KitColumns.ArchBracket(mesh, recipe, KitSurface.Trim, place.Centre - along * (pilaster.Width / 2) + inward * (pilaster.Depth / 2), -along, inward, sideHalf, height, drop, pilaster.BracketReach, pilaster.BracketSegments);
            KitColumns.ArchBracket(mesh, recipe, KitSurface.Trim, place.Centre + along * (pilaster.Width / 2) + inward * (pilaster.Depth / 2), along, inward, sideHalf, height, drop, pilaster.BracketReach, pilaster.BracketSegments);
            KitColumns.ArchBracket(mesh, recipe, KitSurface.Trim, place.Centre + inward * pilaster.Depth, inward, along, fwdHalf, height, drop, pilaster.BracketReach * .9f, pilaster.BracketSegments);
        }
        pilasters++;

        void PilasterBox(MeshBuilder m, KitSurface surface, float y0, float y1, float w0, float d0, float w1, float d1, bool topCap, bool bottomCap = false)
        {
            if (y1 <= y0) return;
            int slot = LevelKit.Slot(surface);
            float sm = recipe.Metres(surface);
            Vector2 l0 = place.Centre - along * (w0 / 2), r0 = place.Centre + along * (w0 / 2);
            Vector2 lf0 = l0 + inward * d0, rf0 = r0 + inward * d0;
            Vector2 l1 = place.Centre - along * (w1 / 2), r1 = place.Centre + along * (w1 / 2);
            Vector2 lf1 = l1 + inward * d1, rf1 = r1 + inward * d1;
            Vector3 frontF = OpenGround.Gltf(inward, 0), leftF = OpenGround.Gltf(-along, 0), rightF = OpenGround.Gltf(along, 0);
            m.Face(slot, OpenGround.Gltf(lf0, y0), OpenGround.Gltf(rf0, y0), OpenGround.Gltf(rf1, y1), OpenGround.Gltf(lf1, y1),
                new Vector2(0, -y0 / sm), new Vector2(w0 / sm, -y0 / sm), new Vector2(w1 / sm, -y1 / sm), new Vector2(0, -y1 / sm), frontF);
            m.Face(slot, OpenGround.Gltf(l0, y0), OpenGround.Gltf(lf0, y0), OpenGround.Gltf(lf1, y1), OpenGround.Gltf(l1, y1),
                new Vector2(0, -y0 / sm), new Vector2(d0 / sm, -y0 / sm), new Vector2(d1 / sm, -y1 / sm), new Vector2(0, -y1 / sm), leftF);
            m.Face(slot, OpenGround.Gltf(rf0, y0), OpenGround.Gltf(r0, y0), OpenGround.Gltf(r1, y1), OpenGround.Gltf(rf1, y1),
                new Vector2(0, -y0 / sm), new Vector2(d0 / sm, -y0 / sm), new Vector2(d1 / sm, -y1 / sm), new Vector2(0, -y1 / sm), rightF);
            if (topCap)
                m.Face(slot, OpenGround.Gltf(l1, y1), OpenGround.Gltf(r1, y1), OpenGround.Gltf(rf1, y1), OpenGround.Gltf(lf1, y1),
                    Vector2.Zero, new Vector2(w1 / sm, 0), new Vector2(w1 / sm, d1 / sm), new Vector2(0, d1 / sm), Vector3.UnitY);
            if (bottomCap)
                m.Face(slot, OpenGround.Gltf(l0, y0), OpenGround.Gltf(r0, y0), OpenGround.Gltf(rf0, y0), OpenGround.Gltf(lf0, y0),
                    Vector2.Zero, new Vector2(w0 / sm, 0), new Vector2(w0 / sm, d0 / sm), new Vector2(0, d0 / sm), -Vector3.UnitY);
        }
    }

    /// <summary>
    /// Whether open ground lies just behind the wall anywhere between two points on it, or within a door arch's ring and
    /// its sconces of them (a doorway or other opening).
    /// </summary>
    private bool Behind(Vector2 left, Vector2 right, Vector2 inward, Dictionary<(int X, int Z), float> ground)
    {
        float step = plan.Rules.GridMetres, margin = (recipe.DoorArch?.Ring?.Width ?? 0) + (recipe.Sconces is { } sconce ? sconce.Gap + SconceClearance : 0);
        Vector2 along = Vector2.Normalize(right - left);
        (left, right) = (left - along * margin, right + along * margin);
        int samples = Math.Max(2, (int)MathF.Ceiling(Vector2.Distance(left, right) / step) + 1);
        for (int s = 0; s < samples; s++)
        {
            Vector2 behind = Vector2.Lerp(left, right, s / (float)(samples - 1)) - inward * step / 2;
            if (ground.ContainsKey(plan.Square(behind))) return true;
        }
        return false;
    }

    private static Vector2 Profile(TrimDefinition trim, int index) => new(trim.Profile[index][0], trim.Profile[index][1]);

    /// <summary>The left-hand (open-side) unit normal of a run walked with open ground on its left, in Unity metres.</summary>
    private static Vector2 Inward(Vector2 from, Vector2 to)
    {
        Vector2 along = Vector2.Normalize(to - from);
        return new Vector2(-along.Y, along.X);
    }

    /// <summary>An upright box's four sides (glTF space, standing on its base), for a prop the walker meets.</summary>
    internal static void Box(MeshBuilder collision, Vector3 foot, Vector3 size)
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
}
