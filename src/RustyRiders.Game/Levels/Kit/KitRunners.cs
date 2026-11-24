using System.Numerics;

namespace RustyRiders.Game.Levels.Kit;

/// <summary>
/// The kit's floor runners: from each doorway's corridor face, through the doorway and straight into its room as far as
/// the room's middle (the middle of its cells), stopping where the floor ahead is not the room's. Where a runner lines up
/// with the middle, it stops short and meets a square hub of runner flush at the middle. Non-meeting strips rise slightly
/// per crossing so crossing strips never share a plane. Across each strip, texture <c>u</c> spans <c>[0, 1]</c> so border
/// curbs stay flush to both edges; along it, <c>v</c> advances in metres. Thin vertical edges ground each slab to the floor.
/// </summary>
internal static class KitRunners
{
    private const float Rise = .004f; // between crossing strips' planes

    internal static int Build(MeshBuilder mesh, RoomPlan plan, KitRecipe recipe, RunnerDefinition runner)
    {
        float half = runner.Width / 2, metres = recipe.Metres(KitSurface.Runner), step = plan.Rules.GridMetres;
        int slot = LevelKit.Slot(KitSurface.Runner), strips = 0, crossings = 0;
        Dictionary<int, Vector2> middles = plan.RoomOf.GroupBy(pair => pair.Value)
            .ToDictionary(room => room.Key, room => new Vector2((float)room.Average(p => p.Key.X), (float)room.Average(p => p.Key.Z)) * plan.CellSize);
        bool Open(Vector2 at) => plan.Open.TryGetValue(plan.Square(at), out PlanArea area) && area != PlanArea.Corridor;
        HashSet<int> hubs = [];
        foreach (PlanDoorway doorway in plan.Doorways)
        {
            if (!plan.RoomOf.TryGetValue(plan.CellAt(doorway.Centre + doorway.IntoRoom * plan.CellSize / 2), out int node)) continue;
            Vector2 into = doorway.IntoRoom, across = new(-into.Y, into.X), middle = middles[node];
            float depth = Vector2.Dot(middle - doorway.Centre, into), side = Vector2.Dot(middle - doorway.Centre, across);
            bool meets = MathF.Abs(side) < half && Open(middle);
            float end = meets ? depth - half : depth, reached = 0;
            while (reached < end && Open(doorway.Centre + into * (reached + step / 2))) reached += step;
            end = MathF.Min(end, reached);
            if (end <= 0) continue;
            float y = meets ? runner.Lift : runner.Lift + Rise * ++crossings;
            Vector2 a = doorway.Centre - across * half, b = doorway.Centre + across * half;
            Vector2 c = b + into * end, d = a + into * end;
            float vEnd = end / metres;
            mesh.Face(slot, OpenGround.Gltf(a, y), OpenGround.Gltf(b, y), OpenGround.Gltf(c, y), OpenGround.Gltf(d, y),
                Vector2.Zero, Vector2.UnitX, new Vector2(1, vEnd), new Vector2(0, vEnd), Vector3.UnitY);
            Skirt(mesh, slot, d, a, y, -across, 0, vEnd);
            Skirt(mesh, slot, b, c, y, across, 1, vEnd);
            if (!meets) Skirt(mesh, slot, c, d, y, into, 0, 1);
            strips++;
            if (meets) hubs.Add(node);
        }
        foreach (int node in hubs)
        {
            Vector2 m = middles[node];
            Vector2[] corners = [m + new Vector2(-half, -half), m + new Vector2(half, -half), m + new Vector2(half, half), m + new Vector2(-half, half)];
            Vector3[] at = corners.Select(c => OpenGround.Gltf(c, runner.Lift)).ToArray();
            mesh.Face(slot, at[0], at[1], at[2], at[3], Vector2.Zero, Vector2.UnitX, Vector2.One, Vector2.UnitY, Vector3.UnitY);
            for (int i = 0; i < 4; i++)
            {
                Vector2 p0 = corners[i], p1 = corners[(i + 1) % 4];
                Vector2 outward = (p0 + p1) / 2 - m;
                Skirt(mesh, slot, p0, p1, runner.Lift, outward, 0, 1);
            }
        }
        return strips;
    }

    private static void Skirt(MeshBuilder mesh, int slot, Vector2 p0, Vector2 p1, float y, Vector2 outward, float uEdge, float vSpan)
    {
        Vector3 facing = OpenGround.Gltf(outward, 0);
        mesh.Face(slot, OpenGround.Gltf(p0, 0), OpenGround.Gltf(p1, 0), OpenGround.Gltf(p1, y), OpenGround.Gltf(p0, y),
            new Vector2(uEdge, 0), new Vector2(uEdge, vSpan), new Vector2(uEdge, vSpan), new Vector2(uEdge, 0), facing);
    }
}
