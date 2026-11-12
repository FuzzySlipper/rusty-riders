using System.Numerics;

namespace RustyRiders.Game.Levels.Kit;

/// <summary>
/// The kit's arch over each of the plan's doorways, in place of the flat lintel. The doorway's straight sides (the
/// outline's walls) stand to the plan's door height, the springing line. Above it the kit builds:
/// - the arched soffit through the wall band, from the corridor's face to the room's;
/// - the room-side and corridor-side wall faces between the arch and each side's ceiling, over the door's width;
/// - the optional ring of voussoirs standing out from the room-side face (clear of any wall stones);
/// - with wall stones, stones laid on both faces above the arch, clear of it and its ring.
///
/// Walls take world-metre UVs along the wall's axis and up, as the kit's step faces do; the soffit and the ring run in
/// metres round the arch.
/// </summary>
internal static class DoorArches
{
    /// <param name="stones">Counts the wall stones laid over the doorways.</param>
    internal static int Build(MeshBuilder mesh, MeshBuilder collision, RoomPlan plan, KitRecipe recipe, ArchDefinition arch, int seed, ref int stones)
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
            (float S, float Y)[] head = Enumerable.Range(0, arch.Segments + 1).Select(k =>
            {
                float angle = MathF.PI * (1 - k / (float)arch.Segments);
                return (half * MathF.Cos(angle), spring + arch.Rise * MathF.Sin(angle));
            }).ToArray();

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

            float ringWidth = arch.Ring?.Width ?? 0;
            if (arch.Ring is { } ring) Ring(mesh, recipe, ring, head, half, spring, arch.Rise, depth, recipe.WallStones?.Deepest ?? 0, At);
            if (recipe.WallStones is { } wall)
            {
                // A stone stays clear of the arch (and, on the room side, its ring): none comes nearer its middle than that.
                Func<float, float, float, float, bool> Clear(float a, float b) => (x0, x1, y0, y1) =>
                {
                    float x = Math.Clamp(0, x0, x1), y = Math.Clamp(spring, y0, y1);
                    return (x / a) * (x / a) + ((y - spring) / b) * ((y - spring) / b) < 1;
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
    /// The voussoir ring on the room-side face: a band from the arch's edge out to a second half-ellipse
    /// <see cref="ArchRingDefinition.Width"/> wider, standing <see cref="ArchRingDefinition.Depth"/> out into the room,
    /// with its outer edge and its underside along the soffit's line.
    /// </summary>
    private static void Ring(MeshBuilder mesh, KitRecipe recipe, ArchRingDefinition ring, (float S, float Y)[] head, float half, float spring,
        float rise, float depth, float proud, Func<float, float, float, System.Numerics.Vector3> at)
    {
        int slot = LevelKit.Slot(ring.Surface);
        float metres = recipe.Metres(ring.Surface), front = depth + proud + ring.Depth, around = 0;
        int segments = head.Length - 1;
        (float S, float Y) Outer(int k)
        {
            float angle = MathF.PI * (1 - k / (float)segments);
            return ((half + ring.Width) * MathF.Cos(angle), spring + (rise + ring.Width) * MathF.Sin(angle));
        }
        for (int k = 0; k < segments; k++)
        {
            (float s0, float y0) = head[k];
            (float s1, float y1) = head[k + 1];
            ((float o0, float z0), (float o1, float z1)) = (Outer(k), Outer(k + 1));
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
        foreach ((float inner, float outer) in new[] { (head[0].S, Outer(0).S), (head[^1].S, Outer(segments).S) })
            mesh.Face(slot, at(inner, depth, spring), at(outer, depth, spring), at(outer, front, spring), at(inner, front, spring),
                Vector2.Zero, new Vector2(ring.Width / metres, 0), new Vector2(ring.Width / metres, (front - depth) / metres), new Vector2(0, (front - depth) / metres),
                at(0, depth, 0) - at(0, depth, 1));
    }
}
