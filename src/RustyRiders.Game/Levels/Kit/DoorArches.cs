using System.Numerics;

namespace RustyRiders.Game.Levels.Kit;

/// <summary>
/// The kit's arch over each of the plan's doorways, in place of the flat lintel. The doorway's straight sides (the
/// outline's walls) stand to the plan's door height, the springing line. Above it the kit builds:
/// - the arched soffit through the wall band, from the corridor's face to the room's;
/// - the room-side and corridor-side wall faces between the arch and each side's ceiling, over the door's width;
/// - the optional ring standing out from the room-side face (clear of any wall stones), smooth or laid in voussoirs;
/// - with wall stones, stones laid on both faces above the arch, clear of it and its ring.
///
/// Walls take world-metre UVs along the wall's axis and up, as the kit's step faces do; the soffit and the ring run in
/// metres round the arch.
/// </summary>
internal static class DoorArches
{
    private const float StripOff = .01f; // metres the strip behind a ring's voussoirs stands off the wall

    /// <summary>
    /// Points across an arch head from left springing (-<paramref name="half"/>, <paramref name="spring"/>) to right
    /// springing (+<paramref name="half"/>, <paramref name="spring"/>), rising <paramref name="rise"/> at the crown.
    /// <paramref name="pointed"/> 0 gives a half-ellipse; positive values blend smoothly into a two-centred Gothic arch.
    /// </summary>
    internal static (float S, float Y)[] Curve(float half, float spring, float rise, int segments, float pointed = 0)
    {
        (float S, float Y)[] pts = new (float, float)[segments + 1];
        float e = Math.Clamp(pointed, 0, 1) * half * .85f;
        float r = half + e;
        float maxAngle = MathF.Acos(e / r);
        float peakY = MathF.Sqrt(MathF.Max(1e-6f, r * r - e * e));
        for (int k = 0; k <= segments; k++)
        {
            float u = k / (float)segments;
            if (e <= 1e-5f)
            {
                float angle = MathF.PI * (1 - u);
                pts[k] = (half * MathF.Cos(angle), spring + rise * MathF.Sin(angle));
            }
            else if (u <= .5f)
            {
                float a = u * 2 * maxAngle;
                pts[k] = (e - r * MathF.Cos(a), spring + rise * (r * MathF.Sin(a) / peakY));
            }
            else
            {
                float a = (1 - u) * 2 * maxAngle;
                pts[k] = (-e + r * MathF.Cos(a), spring + rise * (r * MathF.Sin(a) / peakY));
            }
        }
        return pts;
    }

    /// <summary>Height of the arch curve above <paramref name="spring"/> at horizontal offset <paramref name="s"/>.</summary>
    internal static float HeightAt(float s, float half, float rise, float pointed = 0)
    {
        float ax = MathF.Abs(s);
        if (ax >= half) return 0;
        float e = Math.Clamp(pointed, 0, 1) * half * .85f;
        if (e <= 1e-5f) return rise * MathF.Sqrt(MathF.Max(0, 1 - (ax / half) * (ax / half)));
        float r = half + e;
        float peakY = MathF.Sqrt(MathF.Max(1e-6f, r * r - e * e));
        float dx = ax + e;
        return rise * MathF.Sqrt(MathF.Max(0, r * r - dx * dx)) / peakY;
    }

    /// <summary>Point <paramref name="k"/> of a ring's outer edge: the head's curve grown by <paramref name="width"/>.</summary>
    private static (float S, float Y)[] OuterCurve(int segments, float half, float spring, float rise, float width, float pointed) =>
        Curve(half + width, spring, rise + width, segments, pointed);

    /// <param name="stones">Counts the wall stones laid over the doorways.</param>
    /// <param name="voussoirs">Counts the voussoirs of their rings.</param>
    internal static int Build(MeshBuilder mesh, MeshBuilder collision, RoomPlan plan, KitRecipe recipe, ArchDefinition arch, int seed, ref int stones,
        ref int voussoirs)
    {
        int index = 0;
        PlanRules rules = plan.Rules;
        float half = rules.DoorWidth / 2, depth = rules.WallMetres, spring = rules.DoorHeight;
        float wallMetres = recipe.Metres(KitSurface.Wall);
        foreach (PlanDoorway doorway in plan.Doorways)
        {
            Vector2 into = doorway.IntoRoom, across = new(-into.Y, into.X);
            bool alongX = MathF.Abs(across.X) > .5f; // which world axis the wall runs along, for its U
            // A point on the doorway: s metres across from its middle, t metres into the room from the corridor's face, y up.
            Vector3 At(float s, float t, float y) => OpenGround.Gltf(doorway.Centre + across * s + into * t, y);
            float WallU(float s) { Vector2 p = doorway.Centre + across * s; return (alongX ? p.X : p.Y) / wallMetres; }

            // The head: points across the door from one springing to the other.
            (float S, float Y)[] head = Curve(half, spring, arch.Rise, arch.Segments, arch.Pointed);

            // The soffit through the wall band, facing down and in toward the arch's middle.
            int soffit = LevelKit.Slot(arch.Surface);
            float soffitMetres = recipe.Metres(arch.Surface), around = 0;
            for (int k = 0; k < arch.Segments; k++)
            {
                (float s0, float y0) = head[k];
                (float s1, float y1) = head[k + 1];
                float length = MathF.Sqrt((s1 - s0) * (s1 - s0) + (y1 - y0) * (y1 - y0));
                Vector2 middle = new((s0 + s1) / 2, (y0 + y1) / 2);
                Vector3 facing = At(0, 0, spring) - At(middle.X, 0, middle.Y);
                Vector3 a = At(s0, 0, y0), b = At(s1, 0, y1), c = At(s1, depth, y1), d = At(s0, depth, y0);
                float u0 = around / soffitMetres, u1 = (around + length) / soffitMetres, v = depth / soffitMetres;
                mesh.Face(soffit, a, b, c, d, new Vector2(u0, 0), new Vector2(u1, 0), new Vector2(u1, v), new Vector2(u0, v), facing);
                collision.Face(0, a, b, c, d, default, default, default, default, facing);
                around += length;
            }

            // The wall faces above the arch: the room's at the band's room side, the corridor's at the cell edge.
            foreach ((float t, float top, float sign) in new[] { (depth, doorway.RoomHeight, 1f), (0f, rules.CorridorHeight, -1f) })
            {
                Vector3 facing = OpenGround.Gltf(into * sign, 0);
                for (int k = 0; k < arch.Segments; k++)
                {
                    (float s0, float y0) = head[k];
                    (float s1, float y1) = head[k + 1];
                    mesh.Face(LevelKit.WallSlot, At(s0, t, y0), At(s1, t, y1), At(s1, t, top), At(s0, t, top),
                        new Vector2(WallU(s0), -y0 / wallMetres), new Vector2(WallU(s1), -y1 / wallMetres),
                        new Vector2(WallU(s1), -top / wallMetres), new Vector2(WallU(s0), -top / wallMetres), facing);
                    collision.Face(0, At(s0, t, y0), At(s1, t, y1), At(s1, t, top), At(s0, t, top), default, default, default, default, facing);
                }
            }

            if (arch.Steps is { } steps) ThresholdSteps(mesh, collision, recipe, steps, half, depth, into, across, At);

            float ringWidth = arch.Ring?.Width ?? 0;
            float proud = recipe.WallStones?.Deepest ?? 0;
            if (arch.Ring is { Stones: { } ringStones } stoneRing)
            {
                // The ring's band on the room's wall face; its joints show a strip of the voussoirs' backing just off it.
                (float S, float Y)[] outer = OuterCurve(head.Length - 1, half, spring, arch.Rise, stoneRing.Width, arch.Pointed);
                VoussoirBand band = new(head.Select(p => At(p.S, depth, p.Y)).ToArray(),
                    outer.Select(p => At(p.S, depth, p.Y)).ToArray(), stoneRing.Width);
                Vector3 toRoom = OpenGround.Gltf(into, 0);
                band.Strip(mesh, LevelKit.Slot(ringStones.Backing), recipe.Metres(ringStones.Backing), toRoom, StripOff, 0);
                voussoirs += Voussoirs.Lay(mesh, recipe, ringStones, stoneRing.Surface, band, toRoom, proud + stoneRing.Depth, 0,
                    new Random(unchecked(seed * 7919 + 3_000_017 + index * 104729)));
            }
            else if (arch.Ring is { } ring) Ring(mesh, recipe, ring, head, OuterCurve(head.Length - 1, half, spring, arch.Rise, ring.Width, arch.Pointed), spring, depth, proud, At);
            if (recipe.WallStones is { } wall)
            {
                // A stone stays clear of the arch (and, on the room side, its ring).
                Func<float, float, float, float, bool> Clear(float a, float b) => (x0, x1, y0, y1) =>
                {
                    float x = Math.Clamp(0, x0, x1);
                    return y0 < spring + HeightAt(x, a, b, arch.Pointed);
                };
                float g = wall.Joint;
                Random random = new(unchecked(seed * 7919 + 1_000_003 + index * 104729));
                stones += WallStones.Lay(mesh, recipe, wall, random, doorway.Centre + into * depth, across, into, -half, half, spring,
                    doorway.RoomHeight - WallStones.TopGap(recipe, PlanArea.Room), Clear(half + ringWidth + g, arch.Rise + ringWidth + g));
                stones += WallStones.Lay(mesh, recipe, wall, random, doorway.Centre, across, -into, -half, half, spring,
                    rules.CorridorHeight - WallStones.TopGap(recipe, PlanArea.Corridor), Clear(half + g, arch.Rise + g));
            }
            index++;
        }
        return plan.Doorways.Count;
    }

    /// <summary>
    /// Carved stone threshold steps across a doorway opening: stepping up from the room side (with the first tread
    /// extending slightly into the room where the floor runner ends) to a raised stone landing through the wall band,
    /// then stepping back down to the corridor floor.
    /// </summary>
    private static void ThresholdSteps(MeshBuilder mesh, MeshBuilder collision, KitRecipe recipe, DoorThresholdDefinition steps,
        float half, float depth, Vector2 into, Vector2 across, Func<float, float, float, Vector3> at)
    {
        int slot = LevelKit.Slot(steps.Surface);
        float m = recipe.Metres(steps.Surface), w = 2 * half;
        Vector3 toRoom = OpenGround.Gltf(into, 0), toCorridor = -toRoom;
        Vector3 rightFace = OpenGround.Gltf(across, 0), leftFace = -rightFace;
        // Build stacked slabs: each tier i (0..Count-1) spans from corridor step up to room step, narrowing by one tread per tier.
        for (int i = 0; i < steps.Count; i++)
        {
            float y0 = i * steps.Rise, y1 = (i + 1) * steps.Rise;
            float tRoom = depth + steps.Tread - i * steps.Tread;
            float tCorr = i * steps.Tread;
            if (tRoom <= tCorr + .05f) break;
            float len = tRoom - tCorr;
            // Top tread
            mesh.Face(slot, at(-half, tCorr, y1), at(half, tCorr, y1), at(half, tRoom, y1), at(-half, tRoom, y1),
                Vector2.Zero, new Vector2(w / m, 0), new Vector2(w / m, len / m), new Vector2(0, len / m), Vector3.UnitY);
            collision.Face(0, at(-half, tCorr, y1), at(half, tCorr, y1), at(half, tRoom, y1), at(-half, tRoom, y1),
                default, default, default, default, Vector3.UnitY);
            // Room-side riser
            mesh.Face(slot, at(-half, tRoom, y0), at(half, tRoom, y0), at(half, tRoom, y1), at(-half, tRoom, y1),
                Vector2.Zero, new Vector2(w / m, 0), new Vector2(w / m, steps.Rise / m), new Vector2(0, steps.Rise / m), toRoom);
            collision.Face(0, at(-half, tRoom, y0), at(half, tRoom, y0), at(half, tRoom, y1), at(-half, tRoom, y1),
                default, default, default, default, toRoom);
            // Corridor-side riser
            mesh.Face(slot, at(-half, tCorr, y0), at(half, tCorr, y0), at(half, tCorr, y1), at(-half, tCorr, y1),
                Vector2.Zero, new Vector2(w / m, 0), new Vector2(w / m, steps.Rise / m), new Vector2(0, steps.Rise / m), toCorridor);
            collision.Face(0, at(-half, tCorr, y0), at(half, tCorr, y0), at(half, tCorr, y1), at(-half, tCorr, y1),
                default, default, default, default, toCorridor);
            // Exposed side cheeks where step 0 extends past the wall band into the room
            if (tRoom > depth)
            {
                float ext = tRoom - depth;
                foreach ((float s, Vector3 face) in new[] { (-half, leftFace), (half, rightFace) })
                {
                    mesh.Face(slot, at(s, depth, y0), at(s, tRoom, y0), at(s, tRoom, y1), at(s, depth, y1),
                        Vector2.Zero, new Vector2(ext / m, 0), new Vector2(ext / m, steps.Rise / m), new Vector2(0, steps.Rise / m), face);
                    collision.Face(0, at(s, depth, y0), at(s, tRoom, y0), at(s, tRoom, y1), at(s, depth, y1),
                        default, default, default, default, face);
                }
            }
        }
    }

    /// <summary>
    /// The voussoir ring on the room-side face: a band from the arch's edge out to a second curve
    /// <see cref="ArchRingDefinition.Width"/> wider, standing <see cref="ArchRingDefinition.Depth"/> out into the room,
    /// with its outer edge and its underside along the soffit's line.
    /// </summary>
    private static void Ring(MeshBuilder mesh, KitRecipe recipe, ArchRingDefinition ring, (float S, float Y)[] head,
        (float S, float Y)[] outer, float spring, float depth, float proud, Func<float, float, float, System.Numerics.Vector3> at)
    {
        int slot = LevelKit.Slot(ring.Surface);
        float metres = recipe.Metres(ring.Surface), front = depth + proud + ring.Depth, around = 0;
        int segments = head.Length - 1;
        for (int k = 0; k < segments; k++)
        {
            (float s0, float y0) = head[k];
            (float s1, float y1) = head[k + 1];
            ((float o0, float z0), (float o1, float z1)) = (outer[k], outer[k + 1]);
            float length = MathF.Sqrt((s1 - s0) * (s1 - s0) + (y1 - y0) * (y1 - y0));
            float u0 = around / metres, u1 = (around + length) / metres, w = ring.Width / metres, d = (front - depth) / metres;
            Vector3 toRoom = at(0, front, 0) - at(0, depth, 0);
            // Front face, the outer edge, and the inner edge continuing the soffit's line.
            mesh.Face(slot, at(s0, front, y0), at(s1, front, y1), at(o1, front, z1), at(o0, front, z0),
                new Vector2(u0, 0), new Vector2(u1, 0), new Vector2(u1, w), new Vector2(u0, w), toRoom);
            Vector3 outward = at((o0 + o1) / 2, depth, (z0 + z1) / 2) - at(0, depth, spring);
            mesh.Face(slot, at(o0, depth, z0), at(o1, depth, z1), at(o1, front, z1), at(o0, front, z0),
                new Vector2(u0, 0), new Vector2(u1, 0), new Vector2(u1, d), new Vector2(u0, d), outward);
            mesh.Face(slot, at(s0, depth, y0), at(s1, depth, y1), at(s1, front, y1), at(s0, front, y0),
                new Vector2(u0, 0), new Vector2(u1, 0), new Vector2(u1, d), new Vector2(u0, d), -outward);
            around += length;
        }
        // The ring's feet: its bottom ends at the springing, facing down.
        foreach ((float inner, float outS) in new[] { (head[0].S, outer[0].S), (head[^1].S, outer[segments].S) })
            mesh.Face(slot, at(inner, depth, spring), at(outS, depth, spring), at(outS, front, spring), at(inner, front, spring),
                Vector2.Zero, new Vector2(ring.Width / metres, 0), new Vector2(ring.Width / metres, (front - depth) / metres), new Vector2(0, (front - depth) / metres),
                at(0, depth, 0) - at(0, depth, 1));
    }
}
