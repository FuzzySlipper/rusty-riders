namespace RustyRiders.Game.Levels;

/// <summary>Grid directions in the old game's order: forward (+z), right (+x), back (-z), left (-x).</summary>
internal static class GridDirection
{
    internal const int Count = 4;
    private static readonly string[] Names = ["forward", "right", "back", "left"];
    private static readonly (int X, int Z)[] Steps = [(0, 1), (1, 0), (0, -1), (-1, 0)];

    internal static int Parse(string name) => Array.IndexOf(Names, name) is var index and >= 0
        ? index : throw new InvalidOperationException($"Unknown direction '{name}'.");

    internal static (int X, int Z) Step(int direction) => Steps[direction];
    internal static int Opposite(int direction) => (direction + 2) % Count;
    internal static int Rotate(int direction, int quarterTurns) => ((direction + quarterTurns) % Count + Count) % Count;

    /// <summary>A chunk-local cell turned clockwise (seen from above) by quarter turns, as Unity's Y rotation does.</summary>
    internal static (int X, int Z) Rotate((int X, int Z) cell, int quarterTurns) => (((quarterTurns % Count) + Count) % Count) switch
    {
        0 => cell,
        1 => (cell.Z, -cell.X),
        2 => (-cell.X, -cell.Z),
        _ => (-cell.Z, cell.X),
    };
}

/// <summary>The 18 tile kinds of the old SimpleTiles enum, in order.</summary>
internal enum TileKind
{
    Start, Goal, Road, Curve, Forked, Cross, RoadStop, RoomGateA, RoomGateB, RoomGateC, RoomGateD,
    RoomWall, RoomCornerA, RoomCornerB, RoomCornerC, RoomFlat, Warp, Door,
}

internal sealed record PlannedTile(int X, int Z, TileKind Kind, int Rotation);

/// <summary>A layout node's room: its tags, its room cells and its chunk's objective cells (cell, objective name).</summary>
internal sealed record PlannedRoom(int Node, string[] Tags, IReadOnlyList<(int X, int Z)> Cells, IReadOnlyList<((int X, int Z) Cell, string Objective)> Objectives)
{
    internal bool Has(string tag) => Tags.Contains(tag);
}

internal sealed record LevelPlan(IReadOnlyList<PlannedTile> Tiles, IReadOnlyList<PlannedRoom> Rooms, (int X, int Z) Spawn, int SpawnFacing,
    IReadOnlyList<string> Notes);

/// <summary>
/// The old ProceduralLevelBuilder, reduced to its geometry: one chunk per layout node (rotated so its doors face the
/// node's links), one-cell corridors greedily walked between door cells, then each cell's walls and corners choose a
/// tile kind and a rotation that fits that kind's 3x3 walkability template. Deliberate differences from the old code:
/// links are read from both of their nodes (the old code read only the owner's side), tag tests match any flag
/// (the old exact-equality test missed combined tags), and doors are not forced onto locked links (their door art
/// does not convert yet).
/// </summary>
internal sealed class LevelGenerator
{
    private const int RoomType = 0;
    private const int CorridorType = 99;
    private const double DirectionChangeShare = .7; // the old builder only wavered over the first 70% of a corridor

    // 3x3 template slots: the edge slot for each direction and the corner slot for NW, NE, SE, SW.
    private static readonly int[] EdgeSlot = [1, 5, 7, 3];
    private static readonly int[] CornerSlot = [0, 2, 8, 6];
    // Each corner's two walls, and its diagonal step.
    private static readonly (int A, int B)[] CornerWalls = [(3, 0), (0, 1), (1, 2), (2, 3)];
    private static readonly (int X, int Z)[] CornerSteps = [(-1, 1), (1, 1), (1, -1), (-1, -1)];

    private readonly LevelData data;
    private readonly Random random;
    private readonly Dictionary<(int X, int Z), Cell> cells = [];
    private readonly List<string> notes = [];
    private readonly List<PlannedRoom> rooms = [];

    private LevelGenerator(LevelData data, int seed)
    {
        this.data = data;
        random = new Random(seed);
    }

    internal static LevelPlan Generate(LevelData data, LayoutDefinition layout, int seed) => new LevelGenerator(data, seed).Run(layout);

    private LevelPlan Run(LayoutDefinition layout)
    {
        Dictionary<int, LayoutNode> nodes = layout.Nodes.ToDictionary(node => node.Id);
        Dictionary<int, int?[]> links = nodes.Keys.ToDictionary(id => id, _ => new int?[GridDirection.Count]);
        HashSet<(int, int)> locked = [];
        foreach (LayoutNode node in layout.Nodes)
        {
            foreach (LayoutConnection connection in node.Connections)
            {
                if (!nodes.ContainsKey(connection.Target))
                {
                    notes.Add($"node {node.Id} links to missing node {connection.Target}");
                    continue;
                }
                int direction = GridDirection.Parse(connection.Dir);
                links[node.Id][direction] = connection.Target;
                links[connection.Target][GridDirection.Opposite(direction)] ??= node.Id;
                if (connection.Locked) locked.Add((Math.Min(node.Id, connection.Target), Math.Max(node.Id, connection.Target)));
            }
        }

        Dictionary<int, List<(int X, int Z)>[]> doorZones = [];
        (int X, int Z)? spawn = null;
        int spawnFacing = 0;
        foreach (LayoutNode node in layout.Nodes)
        {
            if (Fit(node, links[node.Id]) is not { } fit)
            {
                notes.Add($"node {node.Id}: no chunk fits its links; left empty");
                continue;
            }
            (ChunkDefinition chunk, int rotation) = fit;
            (int X, int Z) pivot = (node.X * data.Generator.SectorSize, node.Z * data.Generator.SectorSize);
            List<(int X, int Z)>[] zones = Enumerable.Range(0, GridDirection.Count).Select(_ => new List<(int X, int Z)>()).ToArray();
            List<(int X, int Z)> roomCells = [];
            List<((int X, int Z) Cell, string Objective)> objectives = [];
            foreach (ChunkCell cell in chunk.Cells)
            {
                (int x, int z) = GridDirection.Rotate((cell.X, cell.Z), rotation);
                (int X, int Z) world = (pivot.X + x, pivot.Z + z);
                if (cell.Door is { } door)
                {
                    zones[GridDirection.Rotate(GridDirection.Parse(door), rotation)].Add(world);
                    continue;
                }
                cells[world] = new Cell(world, RoomType);
                roomCells.Add(world);
                if (cell.Objective is { } objective) objectives.Add((world, objective));
                if (node.Has("start") && (cell.Objective == "primary" || spawn is null)) spawn = world;
            }
            doorZones[node.Id] = zones;
            rooms.Add(new PlannedRoom(node.Id, node.Tags, roomCells, objectives));
            if (node.Has("start")) spawnFacing = Array.FindIndex(links[node.Id], link => link is not null) is var facing and >= 0 ? facing : 0;
        }

        foreach ((int a, int?[] neighbors) in links)
        {
            for (int direction = 0; direction < GridDirection.Count; direction++)
            {
                if (neighbors[direction] is not { } b || b < a) continue; // each link once
                if (!doorZones.TryGetValue(a, out var zonesA) || !doorZones.TryGetValue(b, out var zonesB)) continue;
                List<(int X, int Z)> from = zonesA[direction], to = zonesB[GridDirection.Opposite(direction)];
                if (from.Count == 0 || to.Count == 0)
                {
                    notes.Add($"link {a}-{b}: a chunk has no door on that side; not connected");
                    continue;
                }
                (int X, int Z) start = from[random.Next(from.Count)];
                (int X, int Z) end = to.MinBy(cell => Manhattan(cell, start));
                AddDoor(start, direction);
                AddDoor(end, GridDirection.Opposite(direction));
                BuildCorridor(Offset(start, direction), Offset(end, GridDirection.Opposite(direction)));
                if (locked.Contains((a, b))) notes.Add($"link {a}-{b} is locked; doors are not placed yet");
            }
        }

        SetBorders();
        SetCorners();
        List<PlannedTile> tiles = [];
        foreach (Cell cell in cells.Values)
        {
            TileKind kind = FindTile(cell);
            tiles.Add(new PlannedTile(cell.Position.X, cell.Position.Z, kind, FindRotation(cell, kind)));
        }
        if (tiles.Count == 0) throw new InvalidOperationException($"Layout {layout.Id} produced no cells.");
        if (spawn is null) notes.Add("no start node was built; spawning at the first cell");
        return new LevelPlan(tiles, rooms, spawn ?? (tiles[0].X, tiles[0].Z), spawnFacing, notes);
    }

    private (ChunkDefinition Chunk, int Rotation)? Fit(LayoutNode node, int?[] neighbors)
    {
        bool needsPrimary = node.Has("start") || node.Has("exit") || node.Has("spawner");
        bool needsSecondary = node.Has("bonus") || node.Has("key");
        foreach (bool exact in new[] { true, false })
        {
            foreach (ChunkDefinition chunk in Shuffled(data.Chunks))
            {
                if (needsPrimary && !chunk.Cells.Any(cell => cell.Objective == "primary")) continue;
                if (needsSecondary && !chunk.Cells.Any(cell => cell.Objective is "secondary" or "chest")) continue;
                foreach (int rotation in Shuffled([0, 1, 2, 3]))
                {
                    if (Fits(chunk, rotation, neighbors, exact)) return (chunk, rotation);
                }
            }
        }
        return null;
    }

    private static bool Fits(ChunkDefinition chunk, int rotation, int?[] neighbors, bool exact)
    {
        for (int direction = 0; direction < GridDirection.Count; direction++)
        {
            int local = GridDirection.Rotate(direction, -rotation);
            bool hasDoor = chunk.Cells.Any(cell => cell.Door is { } door && GridDirection.Parse(door) == local);
            if (neighbors[direction] is null)
            {
                if (exact && hasDoor) return false;
            }
            else if (!hasDoor)
            {
                return false;
            }
        }
        return true;
    }

    private void AddDoor((int X, int Z) position, int facing)
    {
        if (!cells.TryGetValue(position, out Cell? cell)) cells[position] = cell = new Cell(position, CorridorType);
        cell.Doorway[facing] = true;
        cell.Doorway[GridDirection.Opposite(facing)] = true;
    }

    /// <summary>The old greedy walk: step to the neighbour nearest the target, sometimes the second nearest.</summary>
    private void BuildCorridor((int X, int Z) current, (int X, int Z) target)
    {
        int wavering = (int)(Manhattan(current, target) * DirectionChangeShare);
        AddCorridor(current);
        for (int step = 0; step < data.Generator.MaxCorridorSteps && current != target; step++)
        {
            (int X, int Z)[] options = Enumerable.Range(0, GridDirection.Count).Select(direction => Offset(current, direction))
                .Where(cell => !cells.TryGetValue(cell, out Cell? existing) || existing.Type == CorridorType)
                .OrderBy(cell => Manhattan(cell, target)).ToArray();
            if (options.Length == 0) break;
            bool waver = options.Length > 1 && step < wavering && random.NextDouble() < data.Generator.ChangeCorridorDirectionChance;
            current = options[waver ? 1 : 0];
            AddCorridor(current);
        }
        if (current != target) notes.Add($"corridor to {target} stopped at {current}");
    }

    private void AddCorridor((int X, int Z) position)
    {
        if (!cells.ContainsKey(position)) cells[position] = new Cell(position, CorridorType);
    }

    /// <summary>The old LevelCellMap.SetTiles walls: outside, doorway, between different cell types, or open.</summary>
    private void SetBorders()
    {
        foreach (Cell cell in cells.Values)
        {
            for (int direction = 0; direction < GridDirection.Count; direction++)
            {
                cell.Borders[direction] = !cells.TryGetValue(Offset(cell.Position, direction), out Cell? neighbor) ? Border.Wall
                    : cell.Doorway[direction] || neighbor.Doorway[GridDirection.Opposite(direction)] ? Border.Doorway
                    : neighbor.Type != cell.Type ? Border.Wall
                    : Border.Open;
            }
        }
    }

    /// <summary>The old corners: inner where both walls close, outer where both neighbours wall off a missing diagonal.</summary>
    private void SetCorners()
    {
        foreach (Cell cell in cells.Values)
        {
            for (int corner = 0; corner < GridDirection.Count; corner++)
            {
                (int a, int b) = CornerWalls[corner];
                if (cell.Closed(a) && cell.Closed(b))
                {
                    cell.Corners[corner] = true;
                    continue;
                }
                if (cell.Closed(a) || cell.Closed(b)) continue;
                cells.TryGetValue(Offset(cell.Position, a), out Cell? nearA);
                cells.TryGetValue(Offset(cell.Position, b), out Cell? nearB);
                cells.TryGetValue(Diagonal(cell.Position, corner), out Cell? diagonal);
                cell.Corners[corner] = nearA is not null && nearA.Closed(b) && nearB is not null && nearB.Closed(a)
                    && (diagonal is null || diagonal.Type != cell.Type);
            }
        }
        foreach (Cell cell in cells.Values)
        {
            for (int corner = 0; corner < GridDirection.Count; corner++)
            {
                if (!cells.TryGetValue(Diagonal(cell.Position, corner), out Cell? diagonal) || diagonal.Type != cell.Type) continue;
                (int a, int b) = CornerWalls[corner];
                cells.TryGetValue(Offset(cell.Position, a), out Cell? nearA);
                cells.TryGetValue(Offset(cell.Position, b), out Cell? nearB);
                if ((nearA is null && nearB is null) || (nearA is not null && nearA.Type != cell.Type)
                    || (nearB is not null && nearB.Type != cell.Type)) continue;
                cell.Corners[corner] = false;
                diagonal.Corners[(corner + 2) % GridDirection.Count] = false;
            }
        }
    }

    /// <summary>The old SimpleTileset.FindTile.</summary>
    private static TileKind FindTile(Cell cell)
    {
        int[] open = Enumerable.Range(0, GridDirection.Count).Where(d => !cell.Closed(d)).ToArray();
        int[] corners = Enumerable.Range(0, GridDirection.Count).Where(c => cell.Corners[c]).ToArray();
        int[] walls = Enumerable.Range(0, GridDirection.Count).Where(d => cell.Borders[d] == Border.Wall).ToArray();
        switch (open.Length)
        {
            case 1:
                return TileKind.RoadStop;
            case 2:
                if (GridDirection.Opposite(open[0]) == open[1]) return TileKind.Road;
                return corners.Length > 1 ? TileKind.Curve : TileKind.RoomCornerA;
            case 3:
                if (corners.Length == 0) return TileKind.RoomWall;
                if (corners.Length == 2) return TileKind.Forked;
                // RoomGateB when the single corner sits opposite the wall's left end, as the old switch spelled out.
                return walls.Length > 0 && corners[0] == (walls[0] + 2) % GridDirection.Count ? TileKind.RoomGateB : TileKind.RoomGateC;
        }
        return corners.Length switch
        {
            0 => TileKind.RoomFlat,
            1 => TileKind.RoomCornerB,
            2 => (corners[0] + 2) % GridDirection.Count != corners[1] ? TileKind.RoomGateA : TileKind.RoomCornerC,
            3 => TileKind.RoomGateD,
            _ => TileKind.Cross,
        };
    }

    /// <summary>The old SimpleTileset.FindRotation: the first quarter turn whose template blocks every wall and corner.</summary>
    private int FindRotation(Cell cell, TileKind kind)
    {
        string template = string.Concat(data.TileKinds[(int)kind].Walkable);
        for (int rotation = 0; rotation < GridDirection.Count; rotation++)
        {
            bool valid = true;
            for (int side = 0; side < GridDirection.Count && valid; side++)
            {
                int local = GridDirection.Rotate(side, -rotation);
                if (cell.Corners[side] && template[CornerSlot[local]] == '.') valid = false;
                if (cell.Borders[side] == Border.Wall && template[EdgeSlot[local]] == '.') valid = false;
            }
            if (valid) return rotation;
        }
        notes.Add($"no rotation of {kind} fits cell {cell.Position}");
        return 0;
    }

    private IEnumerable<T> Shuffled<T>(IEnumerable<T> items)
    {
        T[] array = items.ToArray();
        random.Shuffle(array);
        return array;
    }

    private static int Manhattan((int X, int Z) a, (int X, int Z) b) => Math.Abs(a.X - b.X) + Math.Abs(a.Z - b.Z);

    private static (int X, int Z) Offset((int X, int Z) cell, int direction)
    {
        (int x, int z) = GridDirection.Step(direction);
        return (cell.X + x, cell.Z + z);
    }

    private static (int X, int Z) Diagonal((int X, int Z) cell, int corner) => (cell.X + CornerSteps[corner].X, cell.Z + CornerSteps[corner].Z);

    private enum Border { Open, Wall, Doorway }

    private sealed class Cell((int X, int Z) position, int type)
    {
        internal (int X, int Z) Position { get; } = position;
        internal int Type { get; } = type;
        internal bool[] Doorway { get; } = new bool[GridDirection.Count];
        internal Border[] Borders { get; } = new Border[GridDirection.Count];
        internal bool[] Corners { get; } = new bool[GridDirection.Count];

        internal bool Closed(int direction) => Borders[direction] == Border.Wall;
    }
}
