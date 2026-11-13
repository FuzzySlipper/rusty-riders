using System.Numerics;

namespace RustyRiders.Game.Levels.Kit;

/// <summary>
/// The kit's own columns: hall columns where <see cref="KitDressing.HallColumnPlaces"/> stands them and corner piers
/// where <see cref="KitDressing.RoomCornerPlaces"/> does, each a plinth, a square shaft and a capital up to the room's
/// ceiling, square to the room's walls. A stone shaft is stacked of stones round a core (<see cref="WallStones.Loop"/>).
/// Each collides as an upright box as wide as its widest part.
/// </summary>
internal static class KitColumns
{
    internal static (int Hall, int Corners) Build(MeshBuilder mesh, MeshBuilder collision, RoomPlan plan, KitRecipe recipe, ColumnsDefinition columns,
        int seed)
    {
        Dictionary<(int X, int Z), float> rooms = plan.Heights([PlanArea.Room]);
        int hall = 0, corners = 0;
        if (columns.Hall is { } h)
            foreach (Vector2 at in KitDressing.HallColumnPlaces(plan))
                if (rooms.TryGetValue(plan.Square(at), out float top)) Column(mesh, collision, recipe, h, at, Vector2.UnitY, top, seed + 1_000_033 * ++hall);
        if (columns.Corners is { } c)
            foreach ((Vector2 at, Vector2 front) in KitDressing.RoomCornerPlaces(plan, c.Footprint))
                if (rooms.TryGetValue(plan.Square(at), out float top)) Column(mesh, collision, recipe, c, at, front, top, seed + 2_000_029 * ++corners);
        return (hall, corners);
    }

    private static void Column(MeshBuilder mesh, MeshBuilder collision, KitRecipe recipe, ColumnDefinition column, Vector2 at, Vector2 front, float top,
        int seed)
    {
        int slot = LevelKit.Slot(column.Surface);
        float metres = recipe.Metres(column.Surface);
        Vector2 side = new(-front.Y, front.X);
        float shaftBottom = column.PlinthHeight, shaftTop = top - column.CapitalHeight;
        Block(0, column.PlinthHeight, column.PlinthWidth / 2, top: true, bottom: false);
        if (column.Stones && recipe.WallStones is { } stones)
        {
            // The core sits back by a middling relief, so the stones bring the shaft out to about its width.
            float core = column.Width / 2 - (stones.MinRelief + stones.MaxRelief) / 2;
            slot = LevelKit.Slot(stones.Backing);
            Block(shaftBottom, shaftTop, core, top: false, bottom: false);
            slot = LevelKit.Slot(column.Surface);
            Vector2[] square = [at + (front - side) * core, at + (-front - side) * core, at + (-front + side) * core, at + (front + side) * core];
            StoneRun[] runs = square.Select((p, i) => new StoneRun(p, square[(i + 1) % 4], shaftBottom, shaftTop)).ToArray(); // outside on the left
            WallStones.Loop(mesh, recipe, stones, column.Surface, runs, seed);
        }
        else Block(shaftBottom, shaftTop, column.Width / 2, top: false, bottom: false);
        Block(shaftTop, top, column.CapitalWidth / 2, top: false, bottom: true);
        KitGeometry.Box(collision, OpenGround.Gltf(at, 0), new Vector3(column.Footprint, top, column.Footprint));

        // A square block between two heights: its four sides, and its top or underside where it steps in or out.
        void Block(float y0, float y1, float half, bool top, bool bottom)
        {
            if (y1 <= y0) return;
            Vector2[] corners = [at + (-front - side) * half, at + (front - side) * half, at + (front + side) * half, at + (-front + side) * half];
            float u = 2 * half / metres;
            for (int i = 0; i < 4; i++)
            {
                Vector2 a = corners[i], b = corners[(i + 1) % 4];
                Vector3 outward = OpenGround.Gltf((a + b) / 2 - at, 0);
                mesh.Face(slot, OpenGround.Gltf(a, y0), OpenGround.Gltf(b, y0), OpenGround.Gltf(b, y1), OpenGround.Gltf(a, y1),
                    new Vector2(0, -y0 / metres), new Vector2(u, -y0 / metres), new Vector2(u, -y1 / metres), new Vector2(0, -y1 / metres), outward);
            }
            Vector2[] uv = [Vector2.Zero, new(u, 0), new(u, u), new(0, u)];
            if (top) Cap(y1, Vector3.UnitY);
            if (bottom) Cap(y0, -Vector3.UnitY);

            void Cap(float y, Vector3 facing) => mesh.Face(slot, OpenGround.Gltf(corners[0], y), OpenGround.Gltf(corners[1], y),
                OpenGround.Gltf(corners[2], y), OpenGround.Gltf(corners[3], y), uv[0], uv[1], uv[2], uv[3], facing);
        }
    }
}
