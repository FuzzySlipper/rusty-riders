using Rusty.Engine;
using RustyRiders.Game.Art;
using RustyRiders.Game.Levels.Kit;

namespace RustyRiders.Game.Levels;

/// <summary>The generated builds a level may stand on in place of its tiles: shells, sweeps or the parametric kit.</summary>
internal sealed partial class LevelScene
{
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
    /// harvested from the converted art, and its dressing of old props placed over the plan. Returns the build's summary, or null (a recorded
    /// problem) when generation fails.
    /// </summary>
    private string? BuildKit(KitRecipe kit, TilesetDefinition tileset, CollisionBuilder collision, int seed, ref ulong nextPlacement)
    {
        List<DressingPlacement> dressing = KitDressing.Place(Plan, kit, seed);
        HarvestedLook look = new(0, kit.NormalScale, kit.Roughness);
        string[] prefabs = tileset.Tiles.Values.SelectMany(variants => variants).Distinct().ToArray();
        Material[] slots = [materials.Get(kit.WallMaterial, prefabs, look), materials.Get(kit.FloorMaterial, prefabs, look),
            materials.Get(kit.CeilingMaterial, prefabs, look), materials.Get(kit.TrimMaterial, prefabs, look)];
        try
        {
            generated = new LevelKit(engine, Plan, kit, slots, cellSize, dressing);
        }
        catch (EngineCallException error)
        {
            problems.Add($"kit: {error.Message}");
            return null;
        }
        Place(generated, collision);
        int dressed = 0;
        foreach (DressingPlacement prop in dressing)
        {
            if (art.Model(prop.Model) is not { } model) continue; // listed among the art's problems
            facts.Add(new AppearanceFact(nextPlacement++, false, 0, prop.Pose, model.Appearance, true, RenderLayer.Scene));
            dressed++;
        }
        return $"{generated.Summary}, {dressed} props";
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
}
