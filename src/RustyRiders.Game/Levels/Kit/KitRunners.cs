using System.Numerics;

namespace RustyRiders.Game.Levels.Kit;

/// <summary>
/// The kit's floor runners: from each doorway's corridor face, through the doorway and straight into its room as far as
/// the room's middle (the middle of its cells), stopping where the floor ahead is not the room's. Where a runner lines up
/// with the middle, it stops short and a square of runner at the middle joins it to the others. Each strip lies a little
/// higher than the one before, so crossing strips never share a plane. Texture runs across the strip and along it in
/// metres.
/// </summary>
internal static class KitRunners
{
    private const float Rise = .004f; // between one strip's plane and the next

    internal static int Build(MeshBuilder mesh, RoomPlan plan, KitRecipe recipe, RunnerDefinition runner)
    {
        float half = runner.Width / 2, metres = recipe.Metres(KitSurface.Runner), step = plan.Rules.GridMetres;
        int slot = LevelKit.Slot(KitSurface.Runner), strips = 0;
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
            float y = runner.Lift + Rise * (strips + 1);
            Vector2 a = doorway.Centre - across * half, b = doorway.Centre + across * half;
            mesh.Face(slot, OpenGround.Gltf(a, y), OpenGround.Gltf(b, y), OpenGround.Gltf(b + into * end, y), OpenGround.Gltf(a + into * end, y),
                Vector2.Zero, new Vector2(runner.Width / metres, 0), new Vector2(runner.Width / metres, end / metres), new Vector2(0, end / metres),
                Vector3.UnitY);
            strips++;
            if (meets) hubs.Add(node);
        }
        foreach (int node in hubs)
        {
            Vector2 m = middles[node];
            Vector2[] corners = [m + new Vector2(-half, -half), m + new Vector2(half, -half), m + new Vector2(half, half), m + new Vector2(-half, half)];
            Vector3[] at = corners.Select(c => OpenGround.Gltf(c, runner.Lift)).ToArray();
            Vector2[] uv = corners.Select(c => (c - m + new Vector2(half)) / metres).ToArray();
            mesh.Face(slot, at[0], at[1], at[2], at[3], uv[0], uv[1], uv[2], uv[3], Vector3.UnitY);
        }
        return strips;
    }
}
