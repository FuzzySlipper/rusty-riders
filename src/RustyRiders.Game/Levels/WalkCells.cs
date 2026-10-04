namespace RustyRiders.Game.Levels;

/// <summary>
/// The level's open ground at walk-grid resolution: every placed tile prefab's 9x9 walk grid turned to its tile,
/// in Unity cell space (x east, z north, <see cref="PerTile"/> cells per tile, tile (0, 0) centred on cell (0, 0)).
/// Generated builds measure the old level from it.
/// </summary>
internal static class WalkCells
{
    internal const int PerTile = 9;
    private const int Centre = PerTile / 2;

    /// <summary>
    /// Open walk-grid cells across the level (Unity cell space: x east, z north, 9 per tile), each with its ceiling:
    /// the room or the corridor height by its tile's kind.
    /// </summary>
    internal static Dictionary<(int X, int Z), float> Open(IReadOnlyList<(PlannedTile Tile, string[] Walkable)> tiles, ShellDefinition shell)
    {
        Dictionary<(int X, int Z), float> open = [];
        foreach ((PlannedTile tile, string[] walkable) in tiles)
        {
            float height = IsCorridor(tile.Kind) ? shell.CorridorHeight : shell.RoomHeight;
            for (int row = 0; row < PerTile; row++)
            {
                for (int column = 0; column < PerTile; column++)
                {
                    if (walkable[row][column] == '#') continue;
                    (int x, int z) = GridDirection.Rotate((column - Centre, Centre - row), tile.Rotation);
                    (int X, int Z) cell = (tile.X * PerTile + x, tile.Z * PerTile + z);
                    open[cell] = Math.Max(open.GetValueOrDefault(cell), height);
                }
            }
        }
        return open;
    }

    internal static bool IsCorridor(TileKind kind) =>
        kind is TileKind.Road or TileKind.Curve or TileKind.Forked or TileKind.Cross or TileKind.RoadStop or TileKind.Start or TileKind.Goal;

    /// <summary>Greedy rectangles over the open cells, each of one ceiling height: grow east, then north while the run stays open.</summary>
    internal static List<WalkRectangle> Merge(Dictionary<(int X, int Z), float> open)
    {
        HashSet<(int X, int Z)> used = [];
        List<WalkRectangle> rectangles = [];
        foreach ((int X, int Z) start in open.Keys.OrderBy(cell => cell.Z).ThenBy(cell => cell.X))
        {
            if (used.Contains(start)) continue;
            float height = open[start];
            bool Free((int X, int Z) cell) => !used.Contains(cell) && open.TryGetValue(cell, out float h) && h == height;
            int x1 = start.X;
            while (Free((x1 + 1, start.Z))) x1++;
            int z1 = start.Z;
            while (Enumerable.Range(start.X, x1 - start.X + 1).All(x => Free((x, z1 + 1)))) z1++;
            for (int z = start.Z; z <= z1; z++)
                for (int x = start.X; x <= x1; x++) used.Add((x, z));
            rectangles.Add(new WalkRectangle(start.X, start.Z, x1, z1, height));
        }
        return rectangles;
    }
}

/// <summary>Open cells X0..X1 by Z0..Z1 (inclusive) sharing one ceiling height.</summary>
internal sealed record WalkRectangle(int X0, int Z0, int X1, int Z1, float Height);
