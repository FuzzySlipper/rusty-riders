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
/// It is made on the update from a <see cref="LevelBlueprint"/>; a kit level made in the background draws its sections
/// once <see cref="Prepare"/> has made them, and is shown (<see cref="Show"/>) only then.
/// </summary>
internal sealed partial class LevelScene : IWalkScene
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
    private Portals? portals; // with the points, once navigation is installed
    private readonly PortalLook portalLook;
    private Action? placePoints; // until navigation is installed (Settle)
    private double worldSeconds;
    private readonly List<AppearanceFact> facts = [];
    private readonly List<string> problems = [];
    private readonly Dictionary<(int X, int Z), (PlannedTile Tile, string Prefab)> tiles = [];
    private readonly float cellSize;
    private readonly Color background;
    private readonly string level;
    private string found = "";
    private Func<string> built = () => "tiles";
    private bool shown;

    /// <param name="deferred">Whether a kit's drawn sections are made later, by <see cref="Prepare"/>, rather than now.</param>
    internal LevelScene(IEngineContext engine, LevelSettings settings, LevelData data, LayoutDefinition layout, int seed, LevelRules rules,
        LevelBlueprint blueprint, bool deferred)
    {
        this.engine = engine;
        MaterialRecolor? recolor = rules.MaterialColors.Recolor();
        art = new ConvertedArt(engine, settings.ArtRoot, recolor);
        materials = new HarvestedMaterials(engine, art, recolor);
        cellSize = data.Generator.CellSize;
        ShellDefinition? shell = null;
        KitRecipe? kit = blueprint.Kit;
        if (settings.Build == LevelSettings.KitBuild)
        {
            if (kit is null) problems.Add($"no content/levels/kits/{data.Tileset.Id}.json: stamped the tiles instead");
        }
        else if (settings.Build != LevelSettings.TilesBuild && (shell = ShellDefinition.Load(engine, data.Tileset.Id)) is null)
        {
            problems.Add($"no content/levels/shells/{data.Tileset.Id}.json: stamped the tiles instead");
        }
        Session = engine.Spatial.CreateSession(new SpatialSessionConfig(CollisionVoxelSize, CollisionChunkSize, VoxelSurfaceMode.GreedyCubes));
        LevelPlan plan = blueprint.Plan;
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
        float spawnLift = SpawnLift;
        (Vector3 floorMin, Vector3 floorMax) = Extent(plan);
        Plan = blueprint.Rooms;
        if (kit is not null && blueprint.Geometry is { } geometry && BuildKit(kit, geometry, blueprint.Dressing, data.Tileset, collision, ref nextPlacement, deferred) is { } kitSummary)
        {
            built = kitSummary;
        }
        else if (shell is not null && walkGrids.Count > 0 && BuildShells(settings.Build, settings.FloorTexture, shell, data.Tileset, walkGrids, seed, collision) is { } summary)
        {
            built = () => summary;
            spawnLift += shell.FloorNoise.Amplitude;
        }
        else
        {
            collision.AddGround(GroundObjectId, floorMin, floorMax);
        }
        engine.Spatial.ReplaceCollision(collision.Request(Session));
        Navigation = new LevelNavigation(engine, Session, rules.Navigation, floorMin, floorMax, deferred);
        portalLook = rules.Points.Portal;
        placePoints = () =>
        {
            Points = LevelPoints.Place(plan, rules.Points, rules.Worlds, data.Tileset.Id, seed, cell => CellCenter(cell.X, cell.Z), Navigation);
            problems.AddRange(Points.Problems);
            portals = new Portals(engine, rules.Points, Points);
            Playable = Points.Rifts.Count >= rules.Points.MinimumRifts;
            SpawnFeet = Points.Arrival + Vector3.UnitY * spawnLift;
            found = $"\n{Points.Rifts.Count} rifts · {Points.Caches.Count} caches · {Points.Residents.Count} resident spots · {Navigation.Summary}";
        };
        if (!deferred) Settle();
        ShowPlan = rules.Plan.Overlay.Show;
        TravelLayouts = data.Layouts.Where(l => !l.Special).Select(l => l.Id).ToArray();
        background = new Color(settings.BackgroundColor[0], settings.BackgroundColor[1], settings.BackgroundColor[2], 1);

        SpawnYawDegrees = GltfYawDegrees(plan.SpawnFacing);
        level = $"Level: {data.Tileset.Id} · {layout.Id} · seed {seed} · {plan.Tiles.Count} tiles · {art.MeshCount} meshes · ";
    }

    public SpatialSession Session { get; }
    public Vector3 SpawnFeet { get; private set; }
    public float SpawnYawDegrees { get; }
    public string Status => level + built() + found;
    public IReadOnlyList<string> Problems => [.. art.Problems, .. materials.Problems, .. problems];
    internal LevelNavigation Navigation { get; }
    /// <summary>Whether the arrival can walk to at least the minimum number of rifts; a level that cannot is rejected.</summary>
    internal bool Playable { get; private set; }
    /// <summary>The gameplay points, placed on the navigation once it is installed (<see cref="Settle"/>).</summary>
    internal LevelPoints Points { get; private set; } = null!;
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
        ? [.. facts, .. SectionFacts, .. portals?.Facts(worldSeconds) ?? [], .. overlay!.Facts]
        : [.. facts, .. SectionFacts, .. portals?.Facts(worldSeconds) ?? []];

    /// <summary>
    /// Places the level's points once its navigation is installed (a level built in the background derives it on the
    /// Engine's workers); true once they are, and with them <see cref="Playable"/> and the spawn.
    /// </summary>
    internal bool Settle()
    {
        if (placePoints is null) return true;
        if (!Navigation.Installed()) return false;
        placePoints();
        placePoints = null;
        return true;
    }

    /// <summary>
    /// One update's share of making a kit level's sections in the background (<see cref="LevelKit.Prepare"/>); true once
    /// the renderer has drawn them, and at once for any other level.
    /// </summary>
    internal bool Prepare(double budgetSeconds) => generated is not LevelKit kit || kit.Prepare(engine, budgetSeconds);

    /// <summary>
    /// The level becomes the one shown: its background colour is set and a kit's lights created (its facts are published by
    /// the product).
    /// </summary>
    internal void Show()
    {
        shown = true;
        engine.CameraView.SetBackgroundColor(new SetBackgroundColorRequest(background));
        (generated as LevelKit)?.Light(engine);
    }

    public void Dispose()
    {
        if (shown) engine.Graphics.PublishSnapshot([]); // its appearances leave the published set before they are disposed
        generated?.Dispose();
        overlay?.Dispose();
        portals?.Dispose();
        materials.Dispose();
        art.Dispose();
        Session.Dispose();
    }

    /// <summary>A generated build's sections, drawn where it is placed (a kit made in the background has them once made).</summary>
    private IEnumerable<AppearanceFact> SectionFacts => generated is null ? []
        : generated.Sections.Select((section, i) => new AppearanceFact(FirstShellSectionObjectId + (ulong)i, false, 0, generated.Placement, section, true, RenderLayer.Scene));

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
