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
/// shallower than those along x, so where they cross no two faces share a plane. They do not collide.
/// </summary>
internal static class CeilingRibs
{
    private const float CrossOffset = .06f; // how much shallower the z ribs are
    private const float Shortest = 4;       // metres of span below which a rib is left out

    internal static int Build(MeshBuilder mesh, RoomPlan plan, KitRecipe recipe, RibDefinition rib, IEnumerable<PilasterPlace> places)
    {
        Dictionary<(int X, int Z), float> rooms = plan.Heights([PlanArea.Room]);
        float step = plan.Rules.GridMetres, metres = recipe.Metres(rib.Surface), half = rib.Width / 2;
        int slot = LevelKit.Slot(rib.Surface), built = 0;
        foreach (PilasterPlace place in places)
        {
            bool alongX = place.Inward.X > .99f, alongZ = place.Inward.Y > .99f;
            if (!alongX && !alongZ) continue;
            float span = 0;
            while (rooms.TryGetValue(plan.Square(place.Centre + place.Inward * (span + step / 2)), out float h) && h == place.Height) span += step;
            if (span < Shortest) continue;
            float top = place.Height, depth = rib.Depth - (alongZ ? CrossOffset : 0);
            Vector2 side = place.Along * half;
            float Under(float s)
            {
                float x = 2 * s / span - 1;
                return top - depth - rib.Haunch * (1 - MathF.Sqrt(MathF.Max(0, 1 - x * x)));
            }
            Vector3 At(float s, Vector2 offset, float y) => OpenGround.Gltf(place.Centre + place.Inward * s + offset, y);
            Vector3 down = -Vector3.UnitY, left = OpenGround.Gltf(-place.Along, 0);
            for (int k = 0; k < rib.Segments; k++)
            {
                float s0 = span * k / rib.Segments, s1 = span * (k + 1) / rib.Segments, y0 = Under(s0), y1 = Under(s1);
                float u0 = s0 / metres, u1 = s1 / metres;
                mesh.Face(slot, At(s0, -side, y0), At(s1, -side, y1), At(s1, side, y1), At(s0, side, y0),
                    new Vector2(u0, 0), new Vector2(u1, 0), new Vector2(u1, rib.Width / metres), new Vector2(u0, rib.Width / metres), down);
                foreach ((Vector2 offset, Vector3 facing) in new[] { (-side, left), (side, -left) })
                    mesh.Face(slot, At(s0, offset, y0), At(s1, offset, y1), At(s1, offset, top), At(s0, offset, top),
                        new Vector2(u0, -y0 / metres), new Vector2(u1, -y1 / metres), new Vector2(u1, -top / metres), new Vector2(u0, -top / metres), facing);
            }
            built++;
        }
        return built;
    }
}
