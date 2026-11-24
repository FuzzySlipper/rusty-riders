using System.Numerics;

namespace RustyRiders.Game.Levels.Kit;

/// <summary>
/// The kit's own columns: hall columns where <see cref="KitDressing.HallColumnPlaces"/> stands them and corner piers
/// where <see cref="KitDressing.RoomCornerPlaces"/> does, each a battered stone plinth, a dressed shaft, optional
/// sweeping Gothic arch brackets, and a capital up to the room's ceiling, square to the room's walls. A stone shaft
/// is stacked of stones round a core (<see cref="WallStones.Loop"/>). Each collides as an upright box as wide as its
/// widest foot/head footprint.
/// </summary>
internal static class KitColumns
{
    internal static (int Hall, int Corners) Build(MeshBuilder mesh, MeshBuilder collision, RoomPlan plan, KitRecipe recipe, ColumnsDefinition columns,
        int seed)
    {
        Dictionary<(int X, int Z), float> rooms = plan.Heights([PlanArea.Room]);
        int hall = 0, corners = 0;
        WallStonesDefinition? stonework = columns.Stonework ?? recipe.WallStones;
        if (columns.Hall is { } h)
            foreach (Vector2 at in KitDressing.HallColumnPlaces(plan))
                if (rooms.TryGetValue(plan.Square(at), out float top))
                    Column(mesh, collision, recipe, h, stonework, at, Vector2.UnitY, top, isCorner: false, seed + 1_000_033 * ++hall);
        if (columns.Corners is { } c)
            foreach ((Vector2 at, Vector2 front) in KitDressing.RoomCornerPlaces(plan, c.Footprint))
                if (rooms.TryGetValue(plan.Square(at), out float top))
                    Column(mesh, collision, recipe, c, stonework, at, front, top, isCorner: true, seed + 2_000_029 * ++corners);
        return (hall, corners);
    }

    private static void Column(MeshBuilder mesh, MeshBuilder collision, KitRecipe recipe, ColumnDefinition column, WallStonesDefinition? stonework,
        Vector2 at, Vector2 front, float top, bool isCorner, int seed)
    {
        Vector2 side = new(-front.Y, front.X);
        float shaftBottom = column.PlinthHeight, shaftTop = top - column.CapitalHeight;
        KitSurface plinthSurface = column.PlinthSurface ?? column.Surface;

        // Plinth: optionally battered stone pedestal topped by a crisp projecting collar slab.
        if (column.PlinthHeight > 0)
        {
            if (column.PlinthBatter > 0)
            {
                float collarH = MathF.Min(.24f, column.PlinthHeight * .22f);
                float bodyTop = column.PlinthHeight - collarH;
                Frustum(mesh, recipe, plinthSurface, at, front, side, 0, bodyTop, column.PlinthWidth / 2 + column.PlinthBatter, column.PlinthWidth / 2, topCap: false, bottomCap: false);
                Block(mesh, recipe, plinthSurface, at, front, side, bodyTop, column.PlinthHeight, column.PlinthWidth / 2 + .06f, topCap: true, bottomCap: true);
            }
            else
                Block(mesh, recipe, plinthSurface, at, front, side, 0, column.PlinthHeight, column.PlinthWidth / 2, topCap: true, bottomCap: false);
        }

        // Shaft: dressed stonework or monolithic square shaft in column.Surface.
        if (column.Stones && stonework is { } stones)
        {
            float core = column.Width / 2 - (stones.MinRelief + stones.MaxRelief) / 2;
            Block(mesh, recipe, stones.Backing, at, front, side, shaftBottom, shaftTop, core, topCap: false, bottomCap: false);
            Vector2[] square = [at + (front - side) * core, at + (-front - side) * core, at + (-front + side) * core, at + (front + side) * core];
            StoneRun[] runs = square.Select((p, i) => new StoneRun(p, square[(i + 1) % 4], shaftBottom, shaftTop)).ToArray(); // outside on the left
            WallStones.Loop(mesh, recipe, stones, column.Surface, runs, seed);
        }
        else Block(mesh, recipe, column.Surface, at, front, side, shaftBottom, shaftTop, column.Width / 2, topCap: false, bottomCap: false);

        // Sweeping Gothic arch brackets flaring outward from the upper shaft all the way up to the ceiling/capital.
        if (column.BracketReach > 0 && column.BracketDrop > 0 && top - shaftBottom > 1.0f)
        {
            float drop = MathF.Min(column.BracketDrop, (top - shaftBottom) * .68f);
            float halfThick = MathF.Min(column.Width * .34f, .32f);
            Vector2[] dirs = isCorner ? [front, side] : [front, -front, side, -side];
            foreach (Vector2 dir in dirs)
            {
                Vector2 perp = new(-dir.Y, dir.X);
                ArchBracket(mesh, recipe, column.Surface, at + dir * (column.Width / 2), dir, perp, halfThick, top, drop, column.BracketReach, column.BracketSegments);
            }
        }

        // Capital abacus meeting the ceiling.
        Block(mesh, recipe, column.Surface, at, front, side, shaftTop, top, column.CapitalWidth / 2, topCap: false, bottomCap: true);
        KitGeometry.Box(collision, OpenGround.Gltf(at, 0), new Vector3(column.Footprint, top, column.Footprint));
    }

    /// <summary>
    /// A sweeping concave-underside Gothic arch-corbel bracket springing from <paramref name="origin"/> along
    /// <paramref name="outDir"/>: starting flush at <c>topY - drop</c> and curving out <paramref name="reach"/> metres
    /// to support a capital, beam, or arcade arch at <paramref name="topY"/>.
    /// </summary>
    internal static void ArchBracket(MeshBuilder mesh, KitRecipe recipe, KitSurface surface, Vector2 origin, Vector2 outDir, Vector2 sideDir,
        float halfThick, float topY, float drop, float reach, int segments)
    {
        int slot = LevelKit.Slot(surface);
        float m = recipe.Metres(surface);
        // Concave quarter-ellipse curve starting tangent-vertical at (out=0, y=topY-drop) and sweeping out to (out=reach, y=topY).
        (float Out, float Y)[] pts = new (float, float)[segments + 1];
        for (int k = 0; k <= segments; k++)
        {
            float angle = MathF.PI * .5f * (k / (float)segments);
            pts[k] = (reach * (1 - MathF.Cos(angle)), topY - drop + drop * MathF.Sin(angle));
        }
        Vector3 At(float out_, float s, float y) => OpenGround.Gltf(origin + outDir * out_ + sideDir * s, y);
        Vector3 leftFace = OpenGround.Gltf(-sideDir, 0), rightFace = OpenGround.Gltf(sideDir, 0);
        float around = 0;
        for (int k = 0; k < segments; k++)
        {
            (float o0, float y0) = pts[k];
            (float o1, float y1) = pts[k + 1];
            float segLen = MathF.Sqrt((o1 - o0) * (o1 - o0) + (y1 - y0) * (y1 - y0));
            // Concave underside soffit (facing outward and down).
            Vector2 t2 = new(o1 - o0, y1 - y0);
            Vector3 underFacing = OpenGround.Gltf(outDir * t2.Y, 0) - Vector3.UnitY * t2.X;
            mesh.Face(slot, At(o0, -halfThick, y0), At(o1, -halfThick, y1), At(o1, halfThick, y1), At(o0, halfThick, y0),
                new Vector2(around / m, 0), new Vector2((around + segLen) / m, 0),
                new Vector2((around + segLen) / m, 2 * halfThick / m), new Vector2(around / m, 2 * halfThick / m), underFacing);
            // Spandrel flanks filling from the curve up to topY.
            foreach ((float s, Vector3 face) in new[] { (-halfThick, leftFace), (halfThick, rightFace) })
                mesh.Face(slot, At(o0, s, y0), At(o1, s, y1), At(o1, s, topY), At(o0, s, topY),
                    new Vector2(o0 / m, -y0 / m), new Vector2(o1 / m, -y1 / m), new Vector2(o1 / m, -topY / m), new Vector2(o0 / m, -topY / m), face);
            around += segLen;
        }
        // Stepped abacus cap band along the top of the bracket (subtle horizontal lip at topY).
        float lipH = MathF.Min(.18f, drop * .12f);
        Vector3 tipFace = OpenGround.Gltf(outDir, 0);
        mesh.Face(slot, At(reach, -halfThick, topY - lipH), At(reach, halfThick, topY - lipH), At(reach, halfThick, topY), At(reach, -halfThick, topY),
            Vector2.Zero, new Vector2(2 * halfThick / m, 0), new Vector2(2 * halfThick / m, lipH / m), new Vector2(0, lipH / m), tipFace);
    }

    private static void Block(MeshBuilder mesh, KitRecipe recipe, KitSurface surface, Vector2 at, Vector2 front, Vector2 side,
        float y0, float y1, float half, bool topCap, bool bottomCap) =>
        Frustum(mesh, recipe, surface, at, front, side, y0, y1, half, half, topCap, bottomCap);

    private static void Frustum(MeshBuilder mesh, KitRecipe recipe, KitSurface surface, Vector2 at, Vector2 front, Vector2 side,
        float y0, float y1, float bottomHalf, float topHalf, bool topCap, bool bottomCap)
    {
        if (y1 <= y0) return;
        int slot = LevelKit.Slot(surface);
        float metres = recipe.Metres(surface);
        Vector2[] bot = [at + (-front - side) * bottomHalf, at + (front - side) * bottomHalf, at + (front + side) * bottomHalf, at + (-front + side) * bottomHalf];
        Vector2[] top = [at + (-front - side) * topHalf, at + (front - side) * topHalf, at + (front + side) * topHalf, at + (-front + side) * topHalf];
        float uBot = 2 * bottomHalf / metres, uTop = 2 * topHalf / metres;
        for (int i = 0; i < 4; i++)
        {
            Vector2 a0 = bot[i], b0 = bot[(i + 1) % 4], a1 = top[i], b1 = top[(i + 1) % 4];
            Vector3 outward = OpenGround.Gltf((a0 + b0) / 2 - at, 0);
            float du = (uBot - uTop) / 2;
            mesh.Face(slot, OpenGround.Gltf(a0, y0), OpenGround.Gltf(b0, y0), OpenGround.Gltf(b1, y1), OpenGround.Gltf(a1, y1),
                new Vector2(0, -y0 / metres), new Vector2(uBot, -y0 / metres), new Vector2(uBot - du, -y1 / metres), new Vector2(du, -y1 / metres), outward);
        }
        if (topCap)
            mesh.Face(slot, OpenGround.Gltf(top[0], y1), OpenGround.Gltf(top[1], y1), OpenGround.Gltf(top[2], y1), OpenGround.Gltf(top[3], y1),
                Vector2.Zero, new Vector2(uTop, 0), new Vector2(uTop, uTop), new Vector2(0, uTop), Vector3.UnitY);
        if (bottomCap)
            mesh.Face(slot, OpenGround.Gltf(bot[0], y0), OpenGround.Gltf(bot[1], y0), OpenGround.Gltf(bot[2], y0), OpenGround.Gltf(bot[3], y0),
                Vector2.Zero, new Vector2(uBot, 0), new Vector2(uBot, uBot), new Vector2(0, uBot), -Vector3.UnitY);
    }
}
