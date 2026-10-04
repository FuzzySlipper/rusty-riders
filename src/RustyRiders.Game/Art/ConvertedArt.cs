using System.Numerics;
using Rusty.Engine;

namespace RustyRiders.Game.Art;

/// <summary>
/// The converted old-game art under content/&lt;root&gt;/ (scripts/import-old-art.sh): placement files read once and
/// each GLB opened once, with one appearance shared by every placement of it. Missing files and refused GLBs are
/// recorded as problems instead of stopping the scene. With a <see cref="MaterialRecolor"/>, each appearance gets
/// Engine material factor overrides for the slots whose Unity materials it recolours; the GLB opens once either way.
/// </summary>
internal sealed class ConvertedArt : IDisposable
{
    private readonly IEngineContext engine;
    private readonly string root;
    private readonly MaterialRecolor? recolor;
    private readonly Dictionary<string, PlacementFile?> placements = [];
    private readonly Dictionary<string, ArtMesh?> meshes = [];
    private readonly List<string> problems = [];

    internal ConvertedArt(IEngineContext engine, string root, MaterialRecolor? recolor = null)
    {
        this.engine = engine;
        this.root = root;
        this.recolor = recolor;
    }

    internal IReadOnlyList<string> Problems => problems;
    internal int MeshCount => meshes.Values.Count(mesh => mesh is not null);

    /// <summary>The placement file of an old-game prefab (its Assets-relative path without extension), or null.</summary>
    internal PlacementFile? Placements(string prefab)
    {
        if (placements.TryGetValue(prefab, out PlacementFile? cached)) return cached;
        string path = $"{root}/placements/{prefab}.placements.json";
        PlacementFile? file = null;
        try
        {
            file = PlacementFile.Load(engine, path);
        }
        catch (EngineCallException)
        {
            problems.Add($"{prefab}: no placement file at content/{path}");
        }
        placements[prefab] = file;
        return file;
    }

    /// <summary>The drawn pieces of a prefab: each LOD0 GLB that opened, with its transform inside the prefab.</summary>
    internal List<(ArtMesh Mesh, Transform Pose)> Pieces(string prefab)
    {
        List<(ArtMesh, Transform)> pieces = [];
        foreach (PlacementRow row in Placements(prefab)?.Placements.Where(row => row.Drawn) ?? [])
        {
            if (Mesh(row.Glb!) is { } mesh) pieces.Add((mesh, row.Transform()));
        }
        return pieces;
    }

    public void Dispose()
    {
        foreach (ArtMesh? mesh in meshes.Values)
        {
            mesh?.Appearance.Dispose();
            mesh?.Resource.Dispose();
        }
        meshes.Clear();
    }

    private ArtMesh? Mesh(string glb)
    {
        if (meshes.TryGetValue(glb, out ArtMesh? cached)) return cached;
        ArtMesh? loaded = null;
        try
        {
            string path = $"{root}/{glb}";
            RenderResource resource = engine.Animation.OpenAnimatedMesh(new AnimatedMeshResourceRequest(path));
            AnimatedMeshInfo info = engine.Animation.ReadMeshInfo(resource);
            Appearance appearance = engine.Animation.CreateAnimatedMeshAppearance(new AnimatedMeshAppearanceRequest(resource));
            try
            {
                if (recolor is not null && recolor.Factors(ReadJson(path)) is { Length: > 0 } factors)
                    engine.Animation.UpdateAnimatedMeshMaterialFactors(new AnimatedMeshMaterialFactorsRequest(appearance, factors));
            }
            catch (EngineCallException)
            {
                appearance.Dispose();
                resource.Dispose();
                throw;
            }
            loaded = new ArtMesh(resource, appearance, info.BoundsMin, info.BoundsMax);
        }
        catch (EngineCallException error)
        {
            problems.Add($"{glb}: {error.Message}");
        }
        meshes[glb] = loaded;
        return loaded;
    }

    /// <summary>A GLB's JSON chunk, read without its binary chunk.</summary>
    private System.Text.Json.Nodes.JsonNode ReadJson(string path)
    {
        using ContentReference source = engine.Content.OpenReference(new ContentOpenRequest(path));
        ReadOnlyMemory<byte> header = engine.Content.ReadBytes(new ContentReadBytesRequest(source, 0, MaterialRecolor.HeaderPrefixLength));
        return MaterialRecolor.ReadJson(engine.Content.ReadBytes(new ContentReadBytesRequest(source, 0,
            (uint)MaterialRecolor.JsonPrefixLength(header.Span))).Span);
    }
}

internal sealed record ArtMesh(RenderResource Resource, Appearance Appearance, Vector3 BoundsMin, Vector3 BoundsMax);
