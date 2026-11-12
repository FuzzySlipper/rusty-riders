using System.Numerics;
using RustyRiders.Game.Content;

namespace RustyRiders.Game.Levels.Kit;

/// <summary>A room of the plan: its layout node and tags, its level cells, and its footprint's bounds in Unity metres.</summary>
internal sealed record PlanRoom(int Node, string[] Tags, int Cells, Vector2 Min, Vector2 Max);

/// <summary>A doorway of the plan: the middle of its opening on the room's wall line, and the way into the room, in Unity metres.</summary>
internal sealed record PlanDoorway(Vector2 Centre, Vector2 IntoRoom);

/// <summary>
/// The level as rooms, corridors and doorways measured out in metres, from the generated plan's cells rather than the
/// old tile prefabs' walk grids. Each level cell is cut into <see cref="Fine"/>-by-<see cref="Fine"/> squares of
/// <see cref="PlanRules.GridMetres"/> (Unity space: x east, z north; fine square (i, j) of level cell (X, Z) has
/// i = X × Fine + k). A room fills its cells but for a wall band along every edge, and corner, it does not share with
/// itself. A corridor runs down the middle of its cells, joining the next corridor cell and any doorway. A doorway cuts
/// through the room's wall band where the generator put one. Generated builds take <see cref="Open"/> as their ground.
/// </summary>
internal sealed class RoomPlan
{
    private readonly Dictionary<(int X, int Z), PlanArea> open = [];
    private readonly List<PlanDoorway> doorways = [];
    private readonly Dictionary<(int X, int Z), int> roomOf = [];
    private readonly float cellSize;

    internal RoomPlan(LevelPlan plan, PlanRules rules, float cellSize)
    {
        this.cellSize = cellSize;
        Rules = rules;
        Fine = (int)MathF.Round(cellSize / rules.GridMetres);
        Authored.Require(MathF.Abs(Fine * rules.GridMetres - cellSize) < 1e-3f, PlanRules.Path, "gridMetres", $"must divide the {cellSize} m cell evenly.");
        int band = Squares(rules.WallMetres, "wallMetres"), corridor = Squares(rules.CorridorWidth, "corridorWidth"), door = Squares(rules.DoorWidth, "doorWidth");
        Authored.Require(2 * band < Fine, PlanRules.Path, "wallMetres", "must leave room inside a cell.");
        Authored.Require((Fine - corridor) % 2 == 0 && (Fine - door) % 2 == 0, PlanRules.Path, "corridorWidth",
            "corridors and doorways must centre on the grid: the cell's squares less theirs must be even.");

        foreach (PlannedRoom room in plan.Rooms)
            foreach ((int X, int Z) cell in room.Cells) roomOf[cell] = room.Node;
        foreach (((int x, int z), int node) in roomOf) AddRoomCell(x, z, node, roomOf, band);

        HashSet<(int X, int Z)> corridors = [.. plan.Corridors];
        Dictionary<(int X, int Z), List<int>> doorSides = [];
        foreach (PlannedDoorway doorway in plan.Doorways)
        {
            if (!doorSides.TryGetValue(doorway.Corridor, out List<int>? sides)) doorSides[doorway.Corridor] = sides = [];
            sides.Add(doorway.Direction);
        }
        foreach ((int x, int z) in corridors)
        {
            List<int> arms = Enumerable.Range(0, GridDirection.Count)
                .Where(d => corridors.Contains(Offset((x, z), d)) || (doorSides.TryGetValue((x, z), out List<int>? sides) && sides.Contains(d))).ToList();
            AddCorridorCell(x, z, arms, corridor);
        }
        foreach (PlannedDoorway doorway in plan.Doorways)
        {
            AddDoorway(Offset(doorway.Corridor, doorway.Direction), GridDirection.Opposite(doorway.Direction), door, band);
            (int dx, int dz) = GridDirection.Step(doorway.Direction);
            Vector2 into = new(dx, dz);
            doorways.Add(new PlanDoorway(new Vector2(doorway.Corridor.X, doorway.Corridor.Z) * cellSize + into * cellSize / 2, into));
        }

        Rooms = plan.Rooms.Select(room => new PlanRoom(room.Node, room.Tags, room.Cells.Count,
            new Vector2(room.Cells.Min(c => c.X) * cellSize - cellSize / 2, room.Cells.Min(c => c.Z) * cellSize - cellSize / 2),
            new Vector2(room.Cells.Max(c => c.X) * cellSize + cellSize / 2, room.Cells.Max(c => c.Z) * cellSize + cellSize / 2))).ToArray();
        CorridorCells = corridors.Count;
    }

    internal PlanRules Rules { get; }
    /// <summary>The layout node whose room each room cell belongs to (level cells).</summary>
    internal IReadOnlyDictionary<(int X, int Z), int> RoomOf => roomOf;
    internal float CellSize => cellSize;
    /// <summary>Grid squares to a level cell's side.</summary>
    internal int Fine { get; }
    internal IReadOnlyList<PlanRoom> Rooms { get; }
    internal IReadOnlyList<PlanDoorway> Doorways => doorways;
    internal int CorridorCells { get; }
    internal IReadOnlyDictionary<(int X, int Z), PlanArea> Open => open;

    /// <summary>The open squares of the given areas, each with its area's ceiling height.</summary>
    internal Dictionary<(int X, int Z), float> Heights(IReadOnlyCollection<PlanArea> areas) => open.Where(pair => areas.Contains(pair.Value))
        .ToDictionary(pair => pair.Key, pair => Rules.Height(pair.Value));

    /// <summary>A grid corner (outline units, corner (i, j) at (i − ½, j − ½)) in Unity metres on the ground plane.</summary>
    internal Vector2 Metres(Vector2 corner) => (corner + new Vector2(.5f)) * Rules.GridMetres - new Vector2(cellSize / 2);

    internal string Summary => FormattableString.Invariant(
        $"plan: {Rooms.Count} rooms, {CorridorCells} corridor cells, {doorways.Count} doorways, {open.Count * Rules.GridMetres * Rules.GridMetres:0} m² open");

    private int Squares(float metres, string field)
    {
        int squares = (int)MathF.Round(metres / Rules.GridMetres);
        Authored.Require(squares > 0 && MathF.Abs(squares * Rules.GridMetres - metres) < 1e-3f, PlanRules.Path, field, "must be a whole number of grid squares.");
        return squares;
    }

    /// <summary>A room cell, less its wall band toward each side and corner that is not the same room.</summary>
    private void AddRoomCell(int x, int z, int node, Dictionary<(int X, int Z), int> roomOf, int band)
    {
        bool Same(int dx, int dz) => roomOf.TryGetValue((x + dx, z + dz), out int other) && other == node;
        for (int k = 0; k < Fine; k++)
        {
            int dx = k < band ? -1 : k >= Fine - band ? 1 : 0;
            for (int l = 0; l < Fine; l++)
            {
                int dz = l < band ? -1 : l >= Fine - band ? 1 : 0;
                bool walled = (dx != 0 && !Same(dx, 0)) || (dz != 0 && !Same(0, dz)) || (dx != 0 && dz != 0 && !Same(dx, dz));
                if (!walled) open[(x * Fine + k, z * Fine + l)] = PlanArea.Room;
            }
        }
    }

    /// <summary>A corridor cell: a square of corridor width in its middle, and an arm of that width to each joined side.</summary>
    private void AddCorridorCell(int x, int z, List<int> arms, int width)
    {
        int from = (Fine - width) / 2, to = from + width;
        void Fill(int k0, int k1, int l0, int l1)
        {
            for (int k = k0; k < k1; k++)
                for (int l = l0; l < l1; l++) open.TryAdd((x * Fine + k, z * Fine + l), PlanArea.Corridor);
        }
        Fill(from, to, from, to);
        foreach (int arm in arms)
        {
            switch (arm)
            {
                case 0: Fill(from, to, to, Fine); break; // forward (+z)
                case 1: Fill(to, Fine, from, to); break; // right (+x)
                case 2: Fill(from, to, 0, from); break;  // back (-z)
                default: Fill(0, from, from, to); break; // left (-x)
            }
        }
    }

    /// <summary>A doorway through a room cell's wall band on the side facing <paramref name="toward"/> (the corridor).</summary>
    private void AddDoorway((int X, int Z) room, int toward, int width, int band)
    {
        int from = (Fine - width) / 2, to = from + width;
        for (int across = from; across < to; across++)
        {
            for (int depth = 0; depth < band; depth++)
            {
                (int k, int l) = toward switch
                {
                    0 => (across, Fine - 1 - depth),
                    1 => (Fine - 1 - depth, across),
                    2 => (across, depth),
                    _ => (depth, across),
                };
                open[(room.X * Fine + k, room.Z * Fine + l)] = PlanArea.Doorway;
            }
        }
    }

    private static (int X, int Z) Offset((int X, int Z) cell, int direction)
    {
        (int x, int z) = GridDirection.Step(direction);
        return (cell.X + x, cell.Z + z);
    }
}
