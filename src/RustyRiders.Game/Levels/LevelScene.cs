using System.Numerics;
using Rusty.Engine;
using RustyRiders.Game.Art;
using RustyRiders.Game.Player;

namespace RustyRiders.Game.Levels;

/// <summary>
/// Stamps a generated level: each planned cell places one of its tile kind's tile prefabs (converted old-game art)
/// at the cell, turned by the planned rotation. Collision is the ground plus a box over each blocked cell of the
/// tile prefab's 9x9 walkability grid (the old combat grid), so walls stop the walker where the old game's did.
/// Engine navigation is derived from that collision, and the level's gameplay points (<see cref="LevelPoints"/>) stand
/// on it: the entry portal the walker arrives in front of, the rifts out, caches and resident spots.
/// </summary>
internal sealed class LevelScene : IWalkScene
{
    private const int CollisionChunkSize = 16;
    private const double CollisionVoxelSize = 0.5;
    private const ulong GroundObjectId = 1;
    private const ulong FirstTileObjectId = 1000;
    private const ulong FirstPlacementObjectId = 100_000;
    private const ulong FirstShellCollisionId = 900_000; // each shell collision mesh's asset and instance
    private const ulong FirstShellSectionObjectId = 10_000;
    private const int WalkGrid = 9;
    private const float SpawnLift = .1f;
    private const float RiftLabelMetres = 8;

    private readonly IEngineContext engine;
    private readonly ConvertedArt art;
    private readonly HarvestedMaterials materials;
    private IGeneratedLevel? shells;
    private readonly Portals portals;
    private readonly PortalLook portalLook;
    private double worldSeconds;
    private readonly List<AppearanceFact> facts = [];
    private readonly List<string> problems = [];
    private readonly Dictionary<(int X, int Z), (PlannedTile Tile, string Prefab)> tiles = [];
    private readonly float cellSize;

    internal LevelScene(IEngineContext engine, LevelSettings settings, LevelData data, LayoutDefinition layout, int seed, LevelRules rules)
    {
        this.engine = engine;
        PaletteDefinition? palette = ChoosePalette(settings, data.Tileset, seed);
        MaterialRecolor? recolor = palette?.Recolor();
        art = new ConvertedArt(engine, settings.ArtRoot, recolor);
        materials = new HarvestedMaterials(engine, art, recolor);
        cellSize = data.Generator.CellSize;
        ShellDefinition? shell = null;
        if (settings.Build != LevelSettings.TilesBuild && (shell = ShellDefinition.Load(engine, data.Tileset.Id)) is null)
            problems.Add($"no content/levels/shells/{data.Tileset.Id}.json: stamped the tiles instead");
        Session = engine.Spatial.CreateSession(new SpatialSessionConfig(CollisionVoxelSize, CollisionChunkSize, VoxelSurfaceMode.GreedyCubes));
        LevelPlan plan = LevelGenerator.Generate(data, layout, seed);
        problems.AddRange(plan.Notes);
        Random variants = new(seed);

        CollisionBuilder collision = new();
        Dictionary<string, ulong> collisionAssets = [];
        List<(PlannedTile Tile, string[] Walkable)> walkGrids = [];
        ulong nextPlacement = FirstPlacementObjectId, nextTile = FirstTileObjectId;
        Func<PlacementRow, bool>? keep = shell is null ? null : row => shell.KeepsProp(row.Model);
        foreach (PlannedTile tile in plan.Tiles)
        {
            string[] prefabs = data.Tileset.Tiles.GetValueOrDefault(tile.Kind.ToString(), []);
            if (prefabs.Length == 0)
            {
                problems.Add($"{data.Tileset.Id} has no {tile.Kind} tile");
                continue;
            }
            string prefab = prefabs[variants.Next(prefabs.Length)];
            tiles[(tile.X, tile.Z)] = (tile, prefab);
            Transform cell = CellTransform(tile);
            foreach ((ArtMesh mesh, Transform pose) in art.Pieces(prefab, keep))
            {
                facts.Add(new AppearanceFact(nextPlacement++, false, 0, Compose(cell, pose), mesh.Appearance, true, RenderLayer.Scene));
            }
            string[] walkable = data.Tileset.Walkability.GetValueOrDefault(prefab) ?? Upsample(data.TileKinds[(int)tile.Kind].Walkable);
            if (shell is not null)
            {
                walkGrids.Add((tile, walkable));
                continue;
            }
            if (!collisionAssets.TryGetValue(prefab, out ulong asset))
                collisionAssets[prefab] = asset = collision.AddBlockedCells(walkable, cellSize, settings.WallHeight);
            collision.Place(nextTile++, asset, cell);
        }
        string built = "tiles";
        float spawnLift = SpawnLift;
        (Vector3 floorMin, Vector3 floorMax) = Extent(plan);
        if (shell is not null && walkGrids.Count > 0 && BuildShells(settings.Build, settings.FloorTexture, shell, data.Tileset, walkGrids, seed, collision) is { } summary)
        {
            built = summary;
            spawnLift += shell.FloorNoise.Amplitude;
        }
        else
        {
            collision.AddGround(GroundObjectId, floorMin, floorMax);
        }
        engine.Spatial.ReplaceCollision(collision.Request(Session));
        Navigation = new LevelNavigation(engine, Session, rules.Navigation, floorMin, floorMax);
        Points = LevelPoints.Place(plan, rules.Points, rules.Worlds, data.Tileset.Id, seed, cell => CellCenter(cell.X, cell.Z), Navigation);
        problems.AddRange(Points.Problems);
        portalLook = rules.Points.Portal;
        portals = new Portals(engine, rules.Points, Points);
        Playable = Points.Rifts.Count >= rules.Points.MinimumRifts;
        TravelLayouts = data.Layouts.Where(l => !l.Special).Select(l => l.Id).ToArray();
        engine.CameraView.SetBackgroundColor(new SetBackgroundColorRequest(new Color(settings.BackgroundColor[0],
            settings.BackgroundColor[1], settings.BackgroundColor[2], 1)));

        SpawnFeet = Points.Arrival + Vector3.UnitY * spawnLift;
        SpawnYawDegrees = GltfYawDegrees(plan.SpawnFacing);
        Status = $"Level: {data.Tileset.Id} · {layout.Id} · seed {seed} · palette {palette?.Id ?? "none"} · {plan.Tiles.Count} tiles · {art.MeshCount} meshes · {built}"
            + $"\n{Points.Rifts.Count} rifts · {Points.Caches.Count} caches · {Points.Residents.Count} resident spots · {Navigation.Summary}";
    }

    public SpatialSession Session { get; }
    public Vector3 SpawnFeet { get; }
    public float SpawnYawDegrees { get; }
    public string Status { get; }
    public IReadOnlyList<string> Problems => [.. art.Problems, .. materials.Problems, .. problems];
    internal LevelNavigation Navigation { get; }
    /// <summary>Whether the arrival can walk to at least the minimum number of rifts; a level that cannot is rejected.</summary>
    internal bool Playable { get; }
    internal LevelPoints Points { get; }
    /// <summary>The layouts a rift can lead to (the non-special ones).</summary>
    internal string[] TravelLayouts { get; }

    /// <summary>The rift the walker's feet stand in, if any.</summary>
    internal RiftPoint? RiftAt(Vector3 feet) => Portals.Entered(Points, portalLook, feet);

    /// <summary>Moves the level's world-time presentation (the portals) to <paramref name="seconds"/> and republishes it.</summary>
    public void Animate(double seconds)
    {
        worldSeconds = seconds;
        Publish();
    }

    public string Describe(Vector3 position)
    {
        RiftPoint? near = Points.Rifts.MinBy(rift => Vector3.Distance(rift.Feet, position));
        if (near is not null && Vector3.Distance(near.Feet, position) <= RiftLabelMetres) return $"Rift → {near.Destination.Name}";
        (int x, int z) = ((int)MathF.Round(-position.X / cellSize), (int)MathF.Round(position.Z / cellSize));
        return tiles.TryGetValue((x, z), out var placed)
            ? $"{placed.Tile.Kind} ({x}, {z}) turned {placed.Tile.Rotation * 90}° · {System.IO.Path.GetFileName(placed.Prefab)}"
            : "";
    }

    public void Publish() => engine.Graphics.PublishSnapshot([.. facts, .. portals.Facts(worldSeconds)]);

    public void Dispose()
    {
        engine.Graphics.PublishSnapshot([]);
        shells?.Dispose();
        portals.Dispose();
        materials.Dispose();
        art.Dispose();
        Session.Dispose();
    }

    /// <summary>
    /// The generated shells in place of the tiles' shell meshes: drawn by sections, colliding as one mesh. Returns
    /// what was built for the status, or null (a recorded problem) when generation fails, leaving only the props.
    /// </summary>
    private string? BuildShells(string build, string? floorTextureId, ShellDefinition shell, TilesetDefinition tileset, List<(PlannedTile Tile, string[] Walkable)> walkGrids,
        int seed, CollisionBuilder collision)
    {
        bool swept = build == LevelSettings.SweepsBuild;
        HarvestedLook look = new(swept ? 0 : shell.TriplanarSharpness, shell.NormalScale, shell.Roughness);
        string[] prefabs = tileset.Tiles.Values.SelectMany(variants => variants).Distinct().ToArray();
        FloorTexture? floorTexture = null;
        if (floorTextureId is not null && (floorTexture = shell.FloorTextures.FirstOrDefault(t => t.Id == floorTextureId)) is null)
            problems.Add($"{tileset.Id} shells have no floor texture '{floorTextureId}'");
        Material walls = materials.Get(shell.WallMaterial, prefabs, look),
            floor = materials.Get(shell.FloorMaterial, prefabs, look, floorTexture?.Replacement);
        try
        {
            shells = swept ? new LevelSweeps(engine, shell, cellSize, walkGrids, walls, floor, floorTexture?.Metres ?? shell.Sweep.FloorMetres, seed)
                : new LevelShells(engine, shell, cellSize, walkGrids, walls, floor, seed);
        }
        catch (EngineCallException error)
        {
            problems.Add($"shells: {error.Message}");
            return null;
        }
        ulong id = FirstShellSectionObjectId;
        foreach (Appearance section in shells.Sections)
            facts.Add(new AppearanceFact(id++, false, 0, shells.Placement, section, true, RenderLayer.Scene));
        ulong collisionId = FirstShellCollisionId;
        foreach (MeshResource mesh in shells.Collision) collision.AddMesh(collisionId++, mesh, shells.Placement);
        return floorTexture is null ? shells.Summary : $"{shells.Summary} · floor {floorTexture.Id}";
    }

    /// <summary>The named palette, or (as the old LevelFx did) one of the tileset's picked by the level's seed.</summary>
    private PaletteDefinition? ChoosePalette(LevelSettings settings, TilesetDefinition tileset, int seed)
    {
        if (settings.Palette is { } id)
        {
            PaletteDefinition? named = tileset.Palettes.FirstOrDefault(palette => palette.Id == id);
            if (named is null) problems.Add($"{tileset.Id} has no palette '{id}'");
            return named;
        }
        return tileset.Palettes.Length == 0 ? null : tileset.Palettes[new Random(seed).Next(tileset.Palettes.Length)];
    }

    /// <summary>The old game's cell placement, in glTF space: Unity is the X mirror, so x flips and the turn reverses.</summary>
    private Transform CellTransform(PlannedTile tile) => new(CellCenter(tile.X, tile.Z),
        Quaternion.CreateFromAxisAngle(Vector3.UnitY, -tile.Rotation * MathF.PI / 2), Vector3.One);

    private Vector3 CellCenter(int x, int z) => new(-x * cellSize, 0, z * cellSize);

    /// <summary>The walker's yaw for a grid direction (positive yaw turns right; 0 faces -Z, so +Z is 180).</summary>
    private static float GltfYawDegrees(int direction) => direction switch { 0 => 180, 1 => 270, 2 => 0, _ => 90 };

    private static Transform Compose(Transform parent, Transform child) => new(
        parent.Translation + Vector3.Transform(child.Translation * parent.Scale, parent.Rotation),
        parent.Rotation * child.Rotation, parent.Scale * child.Scale);

    /// <summary>A 3x3 tile-kind template scaled up to the 9x9 walk grid, for tiles without their own grid.</summary>
    private static string[] Upsample(string[] template) =>
        Enumerable.Range(0, WalkGrid).Select(row => string.Concat(Enumerable.Range(0, WalkGrid)
            .Select(column => template[row / 3][column / 3]))).ToArray();

    private (Vector3 Min, Vector3 Max) Extent(LevelPlan plan)
    {
        float half = cellSize / 2;
        Vector3 min = new(float.MaxValue, 0, float.MaxValue), max = new(float.MinValue, 0, float.MinValue);
        foreach (PlannedTile tile in plan.Tiles)
        {
            Vector3 center = CellCenter(tile.X, tile.Z);
            min = Vector3.Min(min, center - new Vector3(half, 0, half));
            max = Vector3.Max(max, center + new Vector3(half, 0, half));
        }
        return (min, max);
    }

    /// <summary>Raw static-mesh collision: one asset per tile prefab's blocked cells, one instance per placed tile.</summary>
    private sealed class CollisionBuilder
    {
        private const ulong GroundAsset = 1;
        internal const ulong NoCollision = 0;
        private readonly List<Vector3> vertices = [];
        private readonly List<Triangle> triangles = [];
        private readonly List<StaticMeshAsset> assets = [];
        private readonly List<StaticMeshInstance> instances = [];

        /// <summary>Boxes over the blocked cells (merged along each row), in a tile's glTF-local space.</summary>
        internal ulong AddBlockedCells(string[] walkable, float cellSize, float height)
        {
            int firstVertex = vertices.Count, firstTriangle = triangles.Count;
            float step = cellSize / WalkGrid;
            for (int row = 0; row < WalkGrid; row++)
            {
                for (int column = 0; column < WalkGrid; column++)
                {
                    if (walkable[row][column] != '#') continue;
                    int end = column;
                    while (end + 1 < WalkGrid && walkable[row][end + 1] == '#') end++;
                    // Unity tile-local: column 0 is west (-x), row 0 is north (+z); glTF mirrors x.
                    float westX = -cellSize / 2 + column * step, eastX = -cellSize / 2 + (end + 1) * step;
                    float northZ = cellSize / 2 - row * step, southZ = northZ - step;
                    AddBox(new Vector3(-eastX, 0, southZ), new Vector3(-westX, height, northZ), firstVertex);
                    column = end;
                }
            }
            if (triangles.Count == firstTriangle) return NoCollision;
            ulong id = (ulong)assets.Count + GroundAsset + 1;
            assets.Add(new StaticMeshAsset(id, (uint)firstVertex, (uint)(vertices.Count - firstVertex),
                (uint)firstTriangle, (uint)(triangles.Count - firstTriangle)));
            return id;
        }

        internal void Place(ulong instance, ulong asset, Transform transform)
        {
            if (asset != NoCollision) instances.Add(new StaticMeshInstance(instance, asset, transform));
        }

        /// <summary>A retained mesh as its own collision asset (the Engine copies its geometry), placed once.</summary>
        internal void AddMesh(ulong id, MeshResource mesh, Transform transform)
        {
            assets.Add(new StaticMeshAsset(id, new MeshResourceReference(mesh), 0, 0, 0, 0));
            instances.Add(new StaticMeshInstance(id, id, transform));
        }

        internal void AddGround(ulong instance, Vector3 min, Vector3 max)
        {
            int first = vertices.Count;
            vertices.AddRange([new(min.X, 0, min.Z), new(max.X, 0, min.Z), new(max.X, 0, max.Z), new(min.X, 0, max.Z)]);
            triangles.AddRange([new(0, 2, 1), new(0, 3, 2)]); // asset-local indices, wound to face up
            assets.Add(new StaticMeshAsset(GroundAsset, (uint)first, 4, (uint)triangles.Count - 2, 2));
            instances.Add(new StaticMeshInstance(instance, GroundAsset, new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One)));
        }

        internal CollisionReplaceRequest Request(SpatialSession session) =>
            new(session, assets.ToArray(), vertices.ToArray(), triangles.ToArray(), instances.ToArray());

        /// <summary>An axis-aligned box, faces wound outward. Triangle indices are relative to the asset's first vertex.</summary>
        private void AddBox(Vector3 min, Vector3 max, int assetFirstVertex)
        {
            int assetFirst = vertices.Count - assetFirstVertex;
            for (int corner = 0; corner < 8; corner++)
                vertices.Add(new Vector3((corner & 1) == 0 ? min.X : max.X, (corner & 2) == 0 ? min.Y : max.Y, (corner & 4) == 0 ? min.Z : max.Z));
            int[][] faces = [[0, 2, 3, 1], [4, 5, 7, 6], [0, 1, 5, 4], [2, 6, 7, 3], [0, 4, 6, 2], [1, 3, 7, 5]];
            foreach (int[] face in faces)
            {
                uint a = (uint)(assetFirst + face[0]), b = (uint)(assetFirst + face[1]), c = (uint)(assetFirst + face[2]), d = (uint)(assetFirst + face[3]);
                triangles.Add(new Triangle(a, b, c));
                triangles.Add(new Triangle(a, c, d));
            }
        }
    }
}
