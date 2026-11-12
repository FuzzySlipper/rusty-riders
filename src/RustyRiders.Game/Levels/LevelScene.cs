using System.Numerics;
using Rusty.Engine;
using RustyRiders.Game.Art;
using RustyRiders.Game.Levels.Kit;
using RustyRiders.Game.Player;

namespace RustyRiders.Game.Levels;

/// <summary>
/// Builds a generated level. The tiles build stamps each planned cell with one of its tile kind's tile prefabs
/// (converted old-game art), turned by the planned rotation, colliding as the ground plus a box over each blocked cell
/// of the prefab's 9x9 walkability grid (the old combat grid). The shells and sweeps builds replace the tiles' shell
/// meshes with geometry generated from those walk grids; the kit build replaces the tiles with a world's parametric kit
/// over the <see cref="RoomPlan"/>, which every build measures and can show as an overlay.
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
    private IGeneratedLevel? generated;
    private PlanOverlay? overlay;
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
        KitRecipe? kit = null;
        if (settings.Build == LevelSettings.KitBuild)
        {
            if ((kit = KitRecipe.Load(engine, data.Tileset.Id)) is null) problems.Add($"no content/levels/kits/{data.Tileset.Id}.json: stamped the tiles instead");
        }
        else if (settings.Build != LevelSettings.TilesBuild && (shell = ShellDefinition.Load(engine, data.Tileset.Id)) is null)
        {
            problems.Add($"no content/levels/shells/{data.Tileset.Id}.json: stamped the tiles instead");
        }
        Session = engine.Spatial.CreateSession(new SpatialSessionConfig(CollisionVoxelSize, CollisionChunkSize, VoxelSurfaceMode.GreedyCubes));
        LevelPlan plan = LevelGenerator.Generate(data, layout, seed);
        problems.AddRange(plan.Notes);
        Random variants = new(seed);

        CollisionBuilder collision = new();
        Dictionary<string, ulong> collisionAssets = [];
        List<(PlannedTile Tile, string[] Walkable)> walkGrids = [];
        ulong nextPlacement = FirstPlacementObjectId, nextTile = FirstTileObjectId;
        Func<PlacementRow, bool>? keep = kit is not null ? row => kit.KeepsProp(row.Model)
            : shell is not null ? row => shell.KeepsProp(row.Model) : null;
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
            if (kit is not null) continue;
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
        Plan = new RoomPlan(plan, rules.Plan, cellSize);
        if (kit is not null && BuildKit(kit, data.Tileset, collision) is { } kitSummary)
        {
            built = kitSummary;
        }
        else if (shell is not null && walkGrids.Count > 0 && BuildShells(settings.Build, settings.FloorTexture, shell, data.Tileset, walkGrids, seed, collision) is { } summary)
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
        ShowPlan = rules.Plan.Overlay.Show;
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
    /// <summary>The level as rooms, corridors and doorways in metres, which the kit build stands on.</summary>
    internal RoomPlan Plan { get; }

    /// <summary>Whether the room plan's overlay is drawn (developer view; starts as content/levels/plan.json says).</summary>
    internal bool ShowPlan
    {
        get => overlay is not null && showPlan;
        set
        {
            showPlan = value;
            if (value) overlay ??= new PlanOverlay(engine, Plan);
        }
    }
    private bool showPlan;
    /// <summary>The layouts a rift can lead to (the non-special ones).</summary>
    internal string[] TravelLayouts { get; }

    /// <summary>The rift the walker's feet stand in, if any.</summary>
    internal RiftPoint? RiftAt(Vector3 feet) => Portals.Entered(Points, portalLook, feet);

    /// <summary>Moves the level's world-time presentation (the portals) to <paramref name="seconds"/>.</summary>
    public void Animate(double seconds) => worldSeconds = seconds;

    /// <summary>The rift nearest a position, if one is close enough for its label to show.</summary>
    internal RiftPoint? RiftNear(Vector3 position) => Points.Rifts.Where(rift => Vector3.Distance(rift.Feet, position) <= RiftLabelMetres)
        .MinBy(rift => Vector3.Distance(rift.Feet, position));

    public string Describe(Vector3 position)
    {
        (int x, int z) = ((int)MathF.Round(-position.X / cellSize), (int)MathF.Round(position.Z / cellSize));
        return tiles.TryGetValue((x, z), out var placed)
            ? $"{placed.Tile.Kind} ({x}, {z}) turned {placed.Tile.Rotation * 90}° · {System.IO.Path.GetFileName(placed.Prefab)}"
            : "";
    }

    public IEnumerable<AppearanceFact> Facts => ShowPlan
        ? [.. facts, .. portals.Facts(worldSeconds), .. overlay!.Facts]
        : [.. facts, .. portals.Facts(worldSeconds)];

    public void Dispose()
    {
        engine.Graphics.PublishSnapshot([]);
        generated?.Dispose();
        overlay?.Dispose();
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
        SurfaceTexture? floorTexture = null;
        if (floorTextureId is not null && (floorTexture = shell.FloorTextures.FirstOrDefault(t => t.Id == floorTextureId)) is null)
            problems.Add($"{tileset.Id} shells have no floor texture '{floorTextureId}'");
        Material walls = materials.Get(shell.WallMaterial, prefabs, look, shell.WallTexture?.Replacement),
            floor = materials.Get(shell.FloorMaterial, prefabs, look, floorTexture?.Replacement);
        try
        {
            generated = swept ? new LevelSweeps(engine, shell, cellSize, walkGrids, walls, floor,
                    shell.WallTexture?.Metres ?? shell.Sweep.WallMetres, floorTexture?.Metres ?? shell.Sweep.FloorMetres, seed)
                : new LevelShells(engine, shell, cellSize, walkGrids, walls, floor, seed);
        }
        catch (EngineCallException error)
        {
            problems.Add($"shells: {error.Message}");
            return null;
        }
        Place(generated, collision);
        return floorTexture is null ? generated.Summary : $"{generated.Summary} · floor {floorTexture.Id}";
    }

    /// <summary>
    /// The world's parametric kit over the room plan, in place of the tiles: its wall, floor, ceiling and trim materials
    /// harvested from the converted art (so the palette tints them). Returns the build's summary, or null (a recorded
    /// problem) when generation fails.
    /// </summary>
    private string? BuildKit(KitRecipe kit, TilesetDefinition tileset, CollisionBuilder collision)
    {
        HarvestedLook look = new(0, kit.NormalScale, kit.Roughness);
        string[] prefabs = tileset.Tiles.Values.SelectMany(variants => variants).Distinct().ToArray();
        Material[] slots = [materials.Get(kit.WallMaterial, prefabs, look), materials.Get(kit.FloorMaterial, prefabs, look),
            materials.Get(kit.CeilingMaterial, prefabs, look), materials.Get(kit.TrimMaterial, prefabs, look)];
        try
        {
            generated = new LevelKit(engine, Plan, kit, slots, cellSize);
        }
        catch (EngineCallException error)
        {
            problems.Add($"kit: {error.Message}");
            return null;
        }
        Place(generated, collision);
        return generated.Summary;
    }

    /// <summary>A generated build's sections drawn where it is placed, and its collision meshes as their own assets.</summary>
    private void Place(IGeneratedLevel built, CollisionBuilder collision)
    {
        ulong id = FirstShellSectionObjectId;
        foreach (Appearance section in built.Sections)
            facts.Add(new AppearanceFact(id++, false, 0, built.Placement, section, true, RenderLayer.Scene));
        ulong collisionId = FirstShellCollisionId;
        foreach (MeshResource mesh in built.Collision) collision.AddMesh(collisionId++, mesh, built.Placement);
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
}
