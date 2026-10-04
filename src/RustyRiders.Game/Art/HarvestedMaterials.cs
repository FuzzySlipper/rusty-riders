using System.Numerics;
using System.Text.Json.Nodes;
using Rusty.Engine;

namespace RustyRiders.Game.Art;

/// <summary>
/// Engine materials for new geometry made from the converted old-game materials. A Unity material (Assets-relative
/// .mat path) is found on a converted GLB of the given prefabs (the converter records it as
/// <c>extras.unityMaterial</c>); that glTF material's embedded base colour and normal textures are admitted and
/// opened, and its base colour, emission and the level palette's recolour carry over. Each Unity material is built
/// once; one that no prefab uses, or whose textures fail, is recorded as a problem.
/// </summary>
internal sealed class HarvestedMaterials : IDisposable
{
    private readonly IEngineContext engine;
    private readonly ConvertedArt art;
    private readonly MaterialRecolor? recolor;
    private readonly Dictionary<string, Material> materials = [];
    private readonly List<RenderResource> textures = [];
    private readonly List<string> problems = [];

    internal HarvestedMaterials(IEngineContext engine, ConvertedArt art, MaterialRecolor? recolor)
    {
        this.engine = engine;
        this.art = art;
        this.recolor = recolor;
    }

    internal IReadOnlyList<string> Problems => problems;

    /// <summary>
    /// The material made from a Unity material used by one of the prefabs. One that cannot be made is recorded as a
    /// problem and drawn plain grey.
    /// </summary>
    internal Material Get(string unityMaterial, IEnumerable<string> prefabs, HarvestedLook look)
    {
        if (materials.TryGetValue(unityMaterial, out Material? cached)) return cached;
        Material? made = null;
        string? glb = prefabs.SelectMany(prefab => art.Placements(prefab)?.Placements ?? [])
            .FirstOrDefault(row => row.Drawn && row.Materials?.Contains(unityMaterial) == true)?.Glb;
        if (glb is null) problems.Add($"no converted GLB uses {unityMaterial}");
        else
        {
            try
            {
                made = Make(art.PathOf(glb), unityMaterial, look);
            }
            catch (EngineCallException error)
            {
                problems.Add($"{unityMaterial}: {error.Message}");
            }
        }
        made ??= engine.Graphics.CreateMaterial(new MaterialRequest(new Color(.5f, .5f, .5f, 1), default, look.Roughness,
            new Color(1, 1, 1, 1), Vector3.Zero, 0, false));
        materials[unityMaterial] = made;
        return made;
    }

    /// <summary>Releases the materials, then their textures; meshes drawn with them must already be released.</summary>
    public void Dispose()
    {
        foreach (Material material in materials.Values) material.Dispose();
        materials.Clear();
        foreach (RenderResource texture in textures) texture.Dispose();
        textures.Clear();
    }

    private Material? Make(string glbPath, string unityMaterial, HarvestedLook look)
    {
        JsonNode document = art.ReadJson(glbPath);
        JsonNode? material = (document["materials"] as JsonArray ?? [])
            .FirstOrDefault(node => node?["extras"]?["unityMaterial"]?.GetValue<string>() == unityMaterial);
        if (material is null)
        {
            problems.Add($"{glbPath} does not record {unityMaterial}");
            return null;
        }
        JsonNode? pbr = material["pbrMetallicRoughness"];
        RenderResourceReference albedo = Texture(document, glbPath, pbr?["baseColorTexture"]?["index"], TextureColorSpace.Srgb);
        RenderResourceReference normal = Texture(document, glbPath, material["normalTexture"]?["index"], TextureColorSpace.Linear);
        Color color = recolor?.BaseColor(material) ?? ColorOf(pbr?["baseColorFactor"], new Color(1, 1, 1, 1));
        Color emissive = ColorOf(material["emissiveFactor"], new Color(0, 0, 0, 1));
        return engine.Graphics.CreateMaterial(new MaterialRequest(color, albedo, look.Roughness, new Color(1, 1, 1, 1),
            new Vector3(emissive.R, emissive.G, emissive.B), 1, false, MaterialAlphaMode.Opaque, .5f, 0,
            normal, look.NormalScale, look.TriplanarSharpness));
    }

    /// <summary>An embedded image of the GLB, admitted as content and opened as a repeating texture; none for no index.</summary>
    private RenderResourceReference Texture(JsonNode document, string glbPath, JsonNode? textureIndex, TextureColorSpace space)
    {
        if (textureIndex is null) return default;
        JsonNode image = document["images"]![document["textures"]![textureIndex.GetValue<int>()]!["source"]!.GetValue<int>()]!;
        JsonNode view = document["bufferViews"]![image["bufferView"]!.GetValue<int>()]!;
        ReadOnlyMemory<byte> bytes = art.ReadBinary(glbPath, view["byteOffset"]?.GetValue<ulong>() ?? 0, view["byteLength"]!.GetValue<uint>());
        string extension = image["mimeType"]?.GetValue<string>() == "image/jpeg" ? "jpg" : "png";
        using ContentReference content = engine.Content.AdmitReference(new ContentAdmissionRequest(
            $"{glbPath}.{image["name"]?.GetValue<string>() ?? "image"}.{extension}", bytes, ReadOnlyMemory<ContentSourceFile>.Empty));
        RenderResource texture = engine.Graphics.OpenResourceFromContent(
            new RenderResourceContentRequest(content, TextureFilter.Linear, TextureWrap.Repeat, space)).Handle;
        textures.Add(texture);
        return new RenderResourceReference(texture);
    }

    private static Color ColorOf(JsonNode? factor, Color fallback) => factor is JsonArray values
        ? new Color(values[0]!.GetValue<float>(), values[1]!.GetValue<float>(), values[2]!.GetValue<float>(),
            values.Count > 3 ? values[3]!.GetValue<float>() : 1)
        : fallback;
}

/// <summary>How a harvested material is drawn on new geometry: triplanar sharpness (0 for the mesh's uv), normal tilt, roughness.</summary>
internal sealed record HarvestedLook(float TriplanarSharpness, float NormalScale, float Roughness);
