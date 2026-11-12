using System.Numerics;
using Rusty.Engine;
using RustyRiders.Game.Content;

namespace RustyRiders.Game.Levels.Kit;

/// <summary>
/// The room plan drawn over a level for developers: the plan's open ground as one mesh of flat, unlit rectangles just
/// above the floor, merged and coloured by area (room, corridor, doorway), so the plan can be judged against whatever
/// build is standing on it.
/// </summary>
internal sealed class PlanOverlay : IDisposable
{
    private const ulong ObjectId = 2_900_000; // after the portals (Portals, 2_000_000); combat starts at 3_000_000
    private const float Inset = .05f; // keeps neighbouring rectangles apart, so the merge shows

    private readonly Material[] materials;
    private readonly MeshResource mesh;
    private readonly Appearance appearance;

    internal PlanOverlay(IEngineContext engine, RoomPlan plan)
    {
        PlanOverlayLook look = plan.Rules.Overlay;
        PlanArea[] areas = Enum.GetValues<PlanArea>();
        materials = areas.Select(area => engine.Graphics.CreateMaterial(new MaterialRequest(Authored.Color(look.Color(area)), default, 1,
            new Color(1, 1, 1, 1), Vector3.Zero, 0, false) { Unlit = true })).ToArray();
        MeshBuilder builder = new(areas.Length);
        for (int slot = 0; slot < areas.Length; slot++)
        {
            Dictionary<(int X, int Z), float> squares = plan.Open.Where(pair => pair.Value == areas[slot]).ToDictionary(pair => pair.Key, _ => 0f);
            foreach (WalkRectangle r in WalkCells.Merge(squares))
            {
                Vector2 min = plan.Metres(new Vector2(r.X0, r.Z0)) + new Vector2(Inset), max = plan.Metres(new Vector2(r.X1 + 1, r.Z1 + 1)) - new Vector2(Inset);
                uint Corner(float x, float z) => builder.Vertex(OpenGround.Gltf(new Vector2(x, z), look.Lift), Vector2.Zero);
                builder.Quad(slot, Corner(min.X, min.Y), Corner(max.X, min.Y), Corner(max.X, max.Y), Corner(min.X, max.Y), Vector3.UnitY);
            }
        }
        mesh = builder.Create(engine, materials);
        appearance = engine.Graphics.CreateMeshAppearance(mesh);
        Facts = [new AppearanceFact(ObjectId, false, 0, new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One), appearance, true, RenderLayer.Scene)];
    }

    internal IReadOnlyList<AppearanceFact> Facts { get; }

    public void Dispose()
    {
        appearance.Dispose();
        mesh.Dispose();
        foreach (Material material in materials) material.Dispose();
    }
}
