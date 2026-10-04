using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Implicit;
using RustyRiders.Game.Art;

namespace RustyRiders.Game.Levels;

/// <summary>
/// Generated level shells: the old level data as measurements, rebuilt as one Engine implicit field instead of
/// stamped tile meshes. Each placed tile prefab's 9x9 walk grid, turned to its tile, marks open ground; open
/// ground is merged into rectangles, each an air box as tall as its room or corridor, smoothly blended and
/// displaced by seeded waves, and carved out of rock above a gently displaced floor. The floor and walls draw
/// with harvested old materials, triplanar over the field's texture units (one unit is
/// <see cref="ShellDefinition.TextureMetres"/>, because triplanar planes repeat once per mesh unit).
/// <para>
/// The field is extracted one of two ways (<see cref="ShellDefinition.Extraction"/>): <c>implicit</c> meshes the
/// whole level at once with adaptive dual contouring (over a cube as wide as the level's longest side), then
/// partitions it into one drawn section per tile cell and collides with the whole; <c>sampled</c> rasterizes the
/// field onto a lattice over just the level's box and meshes it in blocks, which meet without seams, each drawn and
/// colliding on its own. Either way the level is one field, so pieces never seam.
/// </para>
/// </summary>
internal sealed class LevelShells : IGeneratedLevel
{
    internal const string ImplicitExtraction = "implicit";
    internal const string SampledExtraction = "sampled";
    // Rock and floor reach far past any extraction domain (the implicit cube grows to the level's longest side).
    private const float Far = 10_000;
    private const float AirBelowFloor = 3; // air boxes reach under the floor so the floor alone shapes it

    private readonly List<(MeshResource Mesh, Appearance Appearance)> sections = [];
    private readonly List<MeshResource> collision = [];
    private MeshResource? whole;

    internal LevelShells(IEngineContext engine, ShellDefinition shell, float cellSize, IReadOnlyList<(PlannedTile Tile, string[] Walkable)> tiles,
        Material walls, Material floor, int seed)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp(); // load-cost evidence only, never simulation time
        Placement = new Transform(Vector3.Zero, Quaternion.Identity, new Vector3(shell.TextureMetres));
        float step = cellSize / WalkCells.PerTile;
        List<WalkRectangle> rectangles = WalkCells.Merge(WalkCells.Open(tiles, shell));

        using ImplicitRecipe recipe = new(engine.ImplicitSurfaces);
        List<ImplicitNode> boxes = rectangles.Select(r => recipe.Box(
            Gltf(r.X1 + .5f, -AirBelowFloor, r.Z0 - .5f, step), Gltf(r.X0 - .5f, r.Height, r.Z1 + .5f, step))).ToList();
        ImplicitNode air = Reduce(boxes, (a, b) => recipe.Blend(a, b, shell.BlendRadius));
        air = recipe.DisplaceWaves(air, shell.WallNoise.Frequencies, shell.WallNoise.Amplitude, shell.WallNoise.Octaves,
            shell.WallNoise.Lacunarity, shell.WallNoise.Gain, (ulong)seed);
        ImplicitNode rock = recipe.Subtract(recipe.Box(new Vector3(-Far), new Vector3(Far)), air);
        ImplicitNode ground = recipe.DisplaceWaves(recipe.Box(new Vector3(-Far), new Vector3(Far, 0, Far)),
            shell.FloorNoise.Frequencies, shell.FloorNoise.Amplitude, shell.FloorNoise.Octaves, shell.FloorNoise.Lacunarity,
            shell.FloorNoise.Gain, (ulong)seed + 1);
        ImplicitNode floorBand = recipe.Box(new Vector3(-Far), new Vector3(Far, shell.FloorMaterialTop, Far));

        // Everything above is in metres; the mesh is extracted in texture units and placed back at TextureMetres.
        float units = 1 / shell.TextureMetres;
        Transform toUnits = new(Vector3.Zero, Quaternion.Identity, new Vector3(units));
        ImplicitNode solid = recipe.Place(recipe.Union(rock, ground), toUnits);
        ImplicitMaterialRegion[] regions = [new ImplicitMaterialRegion(recipe.Place(floorBand, toUnits), floor)];
        ImplicitTextureMapping mapping = ImplicitTextureMapping.MajorAxis(Vector2.One, Vector2.Zero);
        (Vector3 min, Vector3 max) = Extent(tiles, cellSize, shell);
        (uint vertices, uint triangles, double meshSeconds) = (0, 0, 0);
        try
        {
            (vertices, triangles, meshSeconds) = shell.Extraction == SampledExtraction
                ? Sampled(engine, shell, recipe.Field, solid, min * units, max * units, mapping, walls, regions)
                : Implicit(engine, shell, recipe.Field, solid, min * units, max * units, cellSize * units, mapping, walls, regions);
        }
        catch (EngineCallException)
        {
            Dispose(); // a failed extraction releases the sections it already made
            throw;
        }
        double seconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalSeconds;
        Summary = FormattableString.Invariant(
            $"{shell.Extraction} shells: {rectangles.Count} air boxes, {vertices} vertices, {triangles} triangles, {sections.Count} sections, meshing {meshSeconds:0.00} s of {seconds:0.00} s");
    }

    public Transform Placement { get; }
    public string Summary { get; }
    public IEnumerable<Appearance> Sections => sections.Select(section => section.Appearance);
    /// <summary>The meshes that collide, in texture units.</summary>
    public IReadOnlyList<MeshResource> Collision => collision;

    public void Dispose()
    {
        foreach ((MeshResource mesh, Appearance appearance) in sections)
        {
            appearance.Dispose();
            mesh.Dispose();
        }
        sections.Clear();
        whole?.Dispose();
        whole = null;
        collision.Clear();
    }

    /// <summary>The whole level in one adaptive extraction, partitioned into a drawn section per tile cell; it collides whole.</summary>
    private (uint Vertices, uint Triangles, double Seconds) Implicit(IEngineContext engine, ShellDefinition shell, ImplicitField field,
        ImplicitNode solid, Vector3 min, Vector3 max, float cellUnits, ImplicitTextureMapping mapping, Material walls,
        ImplicitMaterialRegion[] regions)
    {
        MeshResource level = whole = engine.ImplicitSurfaces.Generate(new ImplicitGenerateRequest(field, solid, min, max,
            shell.SampleSpacing / shell.TextureMetres, shell.CreaseDegrees, 1, mapping, walls, regions,
            ImplicitMaterialBoundaryMode.Interpolated, 0, shell.MaxVertices, shell.MaxTriangles));
        collision.Add(level);
        ImplicitGenerationReadout readout = engine.ImplicitSurfaces.ReadGeneration(field);
        using MeshPartition partition = engine.Graphics.PartitionMesh(new MeshPartitionRequest(level, min, new Vector3(cellUnits)));
        uint parts = engine.Graphics.ReadMeshPartition(partition).PartCount;
        for (uint index = 0; index < parts; index++)
        {
            MeshResource section = engine.Graphics.TakeMeshPartitionPart(new MeshPartitionPartRequest(partition, index));
            sections.Add((section, engine.Graphics.CreateMeshAppearance(section)));
        }
        return (readout.Vertices, readout.Triangles, readout.GenerationSeconds);
    }

    /// <summary>The field rasterized over the level's box, meshed in seamless blocks that each draw and collide.</summary>
    private (uint Vertices, uint Triangles, double Seconds) Sampled(IEngineContext engine, ShellDefinition shell, ImplicitField field,
        ImplicitNode solid, Vector3 min, Vector3 max, ImplicitTextureMapping mapping, Material walls, ImplicitMaterialRegion[] regions)
    {
        float spacing = shell.SampleSpacing / shell.TextureMetres;
        Vector3 size = (max - min) / spacing;
        using SampledVolume volume = engine.ImplicitSurfaces.CreateSampledVolume(new SampledVolumeCreateRequest(min, spacing,
            (uint)MathF.Ceiling(size.X) + 1, (uint)MathF.Ceiling(size.Y) + 1, (uint)MathF.Ceiling(size.Z) + 1, 1, shell.MaxSamples));
        engine.ImplicitSurfaces.RasterizeSampledVolume(new SampledVolumeRasterizeRequest(volume, field, solid));
        SampledVolumeGenerateRequest request = new(volume, field, 0, shell.CreaseDegrees, 1, walls, regions,
            ImplicitMaterialBoundaryMode.Interpolated, 0, mapping, ReadOnlyMemory<SampledVolumeMaterial>.Empty,
            shell.MaxVertices, shell.MaxTriangles, 0, 0, 0, 0, 0, 0);
        uint blockSamples = (uint)Math.Max(2, MathF.Round(shell.BlockMetres / shell.SampleSpacing));
        SampledVolumeBlocksResult blocks = engine.ImplicitSurfaces.ReadSampledVolumeDirtyBlocks(new SampledVolumeBlockLayout(volume, blockSamples));
        (uint vertices, uint triangles, double seconds) = (0, 0, 0);
        foreach (SampledVolumeBlock block in blocks.Blocks.Span)
        {
            SampledVolumeBlockOptionalMesh mesh = engine.ImplicitSurfaces.GenerateSampledVolumeBlock(request.ForBlock(blockSamples, block));
            seconds += mesh.GenerationSeconds;
            if (mesh.Mesh is null) continue;
            (vertices, triangles) = (vertices + mesh.Vertices, triangles + mesh.Triangles);
            sections.Add((mesh.Mesh, engine.Graphics.CreateMeshAppearance(mesh.Mesh)));
            collision.Add(mesh.Mesh);
        }
        return (vertices, triangles, seconds);
    }

    /// <summary>A Unity walk-cell coordinate (cells, may be fractional) and height in glTF metres: Unity is the X mirror.</summary>
    private static Vector3 Gltf(float x, float y, float z, float step) => new(-x * step, y, z * step);

    /// <summary>A balanced combination, so hundreds of boxes make a shallow tree.</summary>
    private static ImplicitNode Reduce(List<ImplicitNode> nodes, Func<ImplicitNode, ImplicitNode, ImplicitNode> combine)
    {
        while (nodes.Count > 1)
        {
            List<ImplicitNode> next = [];
            for (int index = 0; index + 1 < nodes.Count; index += 2) next.Add(combine(nodes[index], nodes[index + 1]));
            if (nodes.Count % 2 == 1) next.Add(nodes[^1]);
            nodes = next;
        }
        return nodes[0];
    }

    /// <summary>The tiles' extent in glTF metres, from below the floor to above the tallest displaced ceiling.</summary>
    private static (Vector3 Min, Vector3 Max) Extent(IReadOnlyList<(PlannedTile Tile, string[] Walkable)> tiles, float cellSize, ShellDefinition shell)
    {
        float half = cellSize / 2;
        float minX = tiles.Min(t => -t.Tile.X * cellSize) - half, maxX = tiles.Max(t => -t.Tile.X * cellSize) + half;
        float minZ = tiles.Min(t => t.Tile.Z * cellSize) - half, maxZ = tiles.Max(t => t.Tile.Z * cellSize) + half;
        float top = Math.Max(shell.RoomHeight, shell.CorridorHeight) + shell.WallNoise.Amplitude + 1;
        return (new Vector3(minX, -AirBelowFloor - shell.FloorNoise.Amplitude, minZ), new Vector3(maxX, top, maxZ));
    }
}

/// <summary>Level geometry generated from the open ground in place of the tiles' shell meshes.</summary>
internal interface IGeneratedLevel : IDisposable
{
    /// <summary>Where every section and collision mesh is placed.</summary>
    Transform Placement { get; }

    /// <summary>What was built and what it cost, for the status line.</summary>
    string Summary { get; }

    IEnumerable<Appearance> Sections { get; }

    IReadOnlyList<MeshResource> Collision { get; }
}
