using System.Numerics;

namespace RustyRiders.Game.Levels.Kit;

/// <summary>
/// A pilaster's place on a wall run (Unity metres): its middle on the wall line, the run's direction and inward
/// (open-side) normal, the run's ceiling, and which run and which place along it it is.
/// </summary>
internal readonly record struct PilasterPlace(Vector2 Centre, Vector2 Along, Vector2 Inward, float Height, int Run, int Index);

/// <summary>
/// The kit's ceiling ribs. Each springs from a room pilaster on a wall facing +x or +z and crosses the room straight
/// to the far wall, so a rectangular room's opposite pilasters carry one rib between them and its two families of ribs
/// cross as a grid. A rib's underside is a half-ellipse: <see cref="RibDefinition.Depth"/> under the ceiling at the
/// middle of the span, <see cref="RibDefinition.Haunch"/> lower at the walls. Ribs running along z sit a little
/// shallower than those along x, so where they cross no two faces share a plane. A rib is smooth, or built of
/// voussoirs (<see cref="Voussoirs"/>) round a core. They do not collide.
/// </summary>
internal static class CeilingRibs
{
    private const float CrossOffset = .06f; // how much shallower the z ribs are
    private const float Shortest = 4;       // metres of span below which a rib is left out

    /// <param name="voussoirs">Counts the voussoirs of ribs built of stones.</param>
    internal static int Build(MeshBuilder mesh, RoomPlan plan, KitRecipe recipe, RibDefinition rib, IEnumerable<PilasterPlace> places, int seed,
        ref int voussoirs)
    {
        Dictionary<(int X, int Z), float> rooms = plan.Heights([PlanArea.Room]);
        float step = plan.Rules.GridMetres;
        VoussoirDefinition? stones = rib.Stones ? recipe.Voussoirs : null;
        int built = 0;
        foreach (PilasterPlace place in places)
        {
            bool alongX = place.Inward.X > .99f, alongZ = place.Inward.Y > .99f;
            if (!alongX && !alongZ) continue;
            float span = 0;
            while (rooms.TryGetValue(plan.Square(place.Centre + place.Inward * (span + step / 2)), out float h) && h == place.Height) span += step;
            if (span < Shortest) continue;
            float top = place.Height, depth = rib.Depth - (alongZ ? CrossOffset : 0);
            // The underside: a half-ellipse span/2 across and Haunch high, sampled evenly round it so the haunches stay smooth.
            (float S, float Y)[] under = Enumerable.Range(0, rib.Segments + 1).Select(k =>
            {
                float angle = MathF.PI * k / rib.Segments;
                return (span / 2 * (1 - MathF.Cos(angle)), top - depth - rib.Haunch * (1 - MathF.Sin(angle)));
            }).ToArray();
            Vector3 At(float s, float y) => OpenGround.Gltf(place.Centre + place.Inward * s, y);
            if (stones is null)
            {
                Solid(mesh, recipe, LevelKit.Slot(rib.Surface), recipe.Metres(rib.Surface), place, under, top, rib.Width / 2);
                built++;
                continue;
            }
            // Voussoirs round a band depth thick, out from the underside along the ellipse's normal, over a narrower core
            // of their backing up to the ceiling, which their joints show.
            float a = span / 2, b = rib.Haunch;
            Vector2[] normals = under.Select(p => Vector2.Normalize(new Vector2((p.S - a) / (a * a), (p.Y - (top - depth - b)) / (b * b)))).ToArray();
            (float S, float Y) Off((float S, float Y) p, Vector2 normal, float by) => (p.S + normal.X * by, p.Y + normal.Y * by);
            VoussoirBand band = new(under.Select(p => At(p.S, p.Y)).ToArray(),
                under.Select((p, k) => Off(p, normals[k], depth)).Select(p => At(p.S, p.Y)).ToArray(), depth);
            float inset = stones.Joint;
            Solid(mesh, recipe, LevelKit.Slot(stones.Backing), recipe.Metres(stones.Backing), place,
                under.Select((p, k) => Off(p, normals[k], inset)).ToArray(), top, rib.Width / 2 - inset);
            voussoirs += Voussoirs.Lay(mesh, recipe, stones, rib.Surface, band, OpenGround.Gltf(place.Along, 0), rib.Width / 2, rib.Width / 2,
                new Random(unchecked(seed * 7919 + 5_000_011 + built * 104729)));
            built++;
        }
        return built;
    }

    /// <summary>A smooth rib: its underside along <paramref name="under"/> and its two flanks up to the ceiling, <paramref name="half"/> either side.</summary>
    private static void Solid(MeshBuilder mesh, KitRecipe recipe, int slot, float metres, PilasterPlace place, (float S, float Y)[] under, float top, float half)
    {
        Vector2 side = place.Along * half;
        Vector3 At(float s, Vector2 offset, float y) => OpenGround.Gltf(place.Centre + place.Inward * s + offset, y);
        Vector3 down = -Vector3.UnitY, left = OpenGround.Gltf(-place.Along, 0);
        for (int k = 0; k + 1 < under.Length; k++)
        {
            ((float s0, float y0), (float s1, float y1)) = (under[k], under[k + 1]);
            float u0 = s0 / metres, u1 = s1 / metres;
            mesh.Face(slot, At(s0, -side, y0), At(s1, -side, y1), At(s1, side, y1), At(s0, side, y0),
                new Vector2(u0, 0), new Vector2(u1, 0), new Vector2(u1, 2 * half / metres), new Vector2(u0, 2 * half / metres), down);
            foreach ((Vector2 offset, Vector3 facing) in new[] { (-side, left), (side, -left) })
                mesh.Face(slot, At(s0, offset, y0), At(s1, offset, y1), At(s1, offset, top), At(s0, offset, top),
                    new Vector2(u0, -y0 / metres), new Vector2(u1, -y1 / metres), new Vector2(u1, -top / metres), new Vector2(u0, -top / metres), facing);
        }
    }
}
