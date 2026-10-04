using System.Numerics;
using Rusty.Engine;

namespace RustyRiders.Game.Art;

/// <summary>
/// The converted old-game art under content/&lt;root&gt;/ (scripts/import-old-art.sh): placement files read once and
/// each GLB opened once, with one appearance shared by every placement of it. Missing files and refused GLBs are
/// recorded as problems instead of stopping the scene. With a <see cref="MaterialRecolor"/>, a GLB whose materials it
/// names is read, recoloured and admitted from memory; every other GLB still opens from its content path.
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
            RenderResource resource = Open(glb);
            AnimatedMeshInfo info = engine.Animation.ReadMeshInfo(resource);
            loaded = new ArtMesh(resource, engine.Animation.CreateAnimatedMeshAppearance(new AnimatedMeshAppearanceRequest(resource)),
                info.BoundsMin, info.BoundsMax);
        }
        catch (EngineCallException error)
        {
            problems.Add($"{glb}: {error.Message}");
        }
        meshes[glb] = loaded;
        return loaded;
    }

    private RenderResource Open(string glb)
    {
        string path = $"{root}/{glb}";
        if (recolor is null) return engine.Animation.OpenAnimatedMesh(new AnimatedMeshResourceRequest(path));
        byte[]? recoloured;
        using (ContentReference source = engine.Content.OpenReference(new ContentOpenRequest(path)))
        {
            ReadOnlyMemory<byte> header = engine.Content.ReadBytes(new ContentReadBytesRequest(source, 0, MaterialRecolor.HeaderPrefixLength));
            ReadOnlyMemory<byte> prefix = engine.Content.ReadBytes(new ContentReadBytesRequest(source, 0,
                (uint)MaterialRecolor.JsonPrefixLength(header.Span)));
            recoloured = recolor.Touches(MaterialRecolor.ReadJson(prefix.Span).Document)
                ? recolor.Apply(engine.Content.ReadBytes(new ContentReadBytesRequest(source, 0,
                    checked((uint)ContentFiles.Length(engine, source)))).Span)
                : null;
        }
        if (recoloured is null) return engine.Animation.OpenAnimatedMesh(new AnimatedMeshResourceRequest(path));
        using ContentReference admitted = engine.Content.AdmitReference(new ContentAdmissionRequest(
            $"recolor/{recolor.Id}/{glb}", recoloured, ReadOnlyMemory<ContentSourceFile>.Empty));
        return engine.Animation.OpenAnimatedMeshFromContent(new AnimationContentRequest(admitted));
    }
}

internal sealed record ArtMesh(RenderResource Resource, Appearance Appearance, Vector3 BoundsMin, Vector3 BoundsMax);
