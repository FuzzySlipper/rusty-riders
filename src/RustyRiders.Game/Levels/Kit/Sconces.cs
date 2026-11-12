using System.Numerics;
using Rusty.Engine;
using RustyRiders.Game.Content;

namespace RustyRiders.Game.Levels.Kit;

/// <summary>
/// The kit's wall sconces: one either side of each doorway on the room's side, beyond the door arch's ring, wherever
/// the wall there is a straight room wall (a cut corner may leave none). Each is a square bracket out from the wall, a
/// glowing flame on it (a stretched octahedron of the flame slot) and a warm point light just in front of the flame.
/// The lights are returned for the level to create and dispose.
/// </summary>
internal static class Sconces
{
    private const float BracketSide = .25f; // the bracket's square section, metres

    internal static List<LightDescriptor> Build(MeshBuilder mesh, RoomPlan plan, KitRecipe recipe, SconceDefinition sconce)
    {
        List<LightDescriptor> lights = [];
        float reach = plan.Rules.DoorWidth / 2 + (recipe.DoorArch?.Ring?.Width ?? 0) + sconce.Gap + BracketSide / 2;
        float probe = plan.Rules.GridMetres / 2;
        bool Room(Vector2 at) => plan.Open.TryGetValue(plan.Square(at), out PlanArea area) && area == PlanArea.Room;
        foreach (PlanDoorway doorway in plan.Doorways)
        {
            Vector2 into = doorway.IntoRoom, across = new(-into.Y, into.X), face = doorway.Centre + into * plan.Rules.WallMetres;
            foreach (float side in (float[])[-1, 1])
            {
                Vector2 wall = face + across * (side * reach);
                if (!Room(wall + into * probe) || plan.Open.ContainsKey(plan.Square(wall - into * probe))) continue;
                Bracket(mesh, sconce, wall, into, across);
                Vector3 flame = OpenGround.Gltf(wall + into * sconce.BracketDepth, sconce.Height + sconce.FlameSize * .6f);
                Flame(mesh, flame, sconce.FlameSize);
                Vector3 light = OpenGround.Gltf(wall + into * (sconce.BracketDepth + .3f), sconce.Height + sconce.FlameSize);
                lights.Add(new LightDescriptor(LightKind.Point, Authored.Vector(sconce.LightColor), sconce.Intensity, true, light, -Vector3.UnitY,
                    true, sconce.Range, 2, 0, 0, LightShadowIntent.Disabled));
            }
        }
        return lights;
    }

    /// <summary>A square bracket from the wall out to the flame, top at the sconce's height.</summary>
    private static void Bracket(MeshBuilder mesh, SconceDefinition sconce, Vector2 wall, Vector2 into, Vector2 across)
    {
        int slot = LevelKit.Slot(sconce.Bracket);
        float h = BracketSide / 2, top = sconce.Height, bottom = top - BracketSide, d = sconce.BracketDepth;
        Vector3 At(float s, float t, float y) => OpenGround.Gltf(wall + across * s + into * t, y);
        Vector2 uv0 = Vector2.Zero, uv1 = new(d, 0), uv2 = new(d, BracketSide), uv3 = new(0, BracketSide);
        mesh.Face(slot, At(-h, 0, top), At(h, 0, top), At(h, d, top), At(-h, d, top), uv0, uv1, uv2, uv3, Vector3.UnitY);
        mesh.Face(slot, At(-h, 0, bottom), At(h, 0, bottom), At(h, d, bottom), At(-h, d, bottom), uv0, uv1, uv2, uv3, -Vector3.UnitY);
        foreach (float s in (float[])[-h, h])
            mesh.Face(slot, At(s, 0, bottom), At(s, d, bottom), At(s, d, top), At(s, 0, top), uv0, uv1, uv2, uv3, OpenGround.Gltf(across * MathF.Sign(s), 0));
        mesh.Face(slot, At(-h, d, bottom), At(h, d, bottom), At(h, d, top), At(-h, d, top), uv0, uv1, uv2, uv3, OpenGround.Gltf(into, 0));
    }

    /// <summary>A flame: an octahedron <paramref name="size"/> across, half again as tall, round <paramref name="centre"/> (glTF).</summary>
    private static void Flame(MeshBuilder mesh, Vector3 centre, float size)
    {
        float r = size / 2;
        Vector3 up = centre + new Vector3(0, r * 1.5f, 0), down = centre - new Vector3(0, r, 0);
        Vector3[] ring = [centre + new Vector3(r, 0, 0), centre + new Vector3(0, 0, r), centre + new Vector3(-r, 0, 0), centre + new Vector3(0, 0, -r)];
        for (int i = 0; i < 4; i++)
        {
            Vector3 a = ring[i], b = ring[(i + 1) % 4];
            foreach (Vector3 tip in (Vector3[])[up, down])
            {
                Vector3 facing = (a + b + tip) / 3 - centre;
                mesh.Triangle(LevelKit.FlameSlot, mesh.Vertex(a, Vector2.Zero), mesh.Vertex(b, Vector2.UnitX), mesh.Vertex(tip, Vector2.One), facing);
            }
        }
    }
}
