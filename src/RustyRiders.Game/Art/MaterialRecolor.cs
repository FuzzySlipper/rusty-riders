using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json.Nodes;
using Rusty.Engine;

namespace RustyRiders.Game.Art;

/// <summary>
/// Unity-side recolouring of converted materials, as the old game did at run time: per Unity material, a new
/// Material.color (the shader's _Color) and optionally a new _EmissionColor. The converter records on each glTF
/// material which Unity material it came from and which Unity property each factor came from
/// (<c>extras.unityMaterial</c>, <c>extras.unityProperties</c>); only factors that came from those properties are
/// overridden, per appearance, through Engine material factors, so the GLB's textures and maps stay as they are.
/// </summary>
internal sealed class MaterialRecolor
{
    private const uint GlbMagic = 0x46546C67, JsonChunk = 0x4E4F534A;
    private const int HeaderBytes = 12, ChunkHeaderBytes = 8;
    private const string MainColorProperty = "_Color";
    private const string EmissionProperty = "_EmissionColor";

    internal const int HeaderPrefixLength = HeaderBytes + ChunkHeaderBytes;
    internal const int ChunkHeaderLength = ChunkHeaderBytes;

    private readonly Dictionary<string, (Vector4 BaseColor, Vector3? Emissive)> colors = [];

    internal MaterialRecolor(string id) => Id = id;

    internal string Id { get; }

    /// <summary>Recolours a Unity material (Assets-relative .mat path). Colours are Unity's stored, gamma-encoded values.</summary>
    internal void Set(string unityMaterial, Vector4 baseColor, Vector3? emissive) => colors[unityMaterial] = (baseColor, emissive);

    /// <summary>
    /// The Engine factor overrides for one GLB, from its JSON chunk. The Engine numbers a GLB's material slots by
    /// the glTF material indices its primitives use, in ascending order; that is how slots are matched here.
    /// </summary>
    internal MeshMaterialFactors[] Factors(JsonNode document)
    {
        if (document["materials"] is not JsonArray materials) return [];
        int[] used = (document["meshes"] as JsonArray ?? [])
            .SelectMany(mesh => mesh?["primitives"] as JsonArray ?? [])
            .Select(primitive => primitive?["material"]?.GetValue<int>())
            .OfType<int>().Where(index => index < materials.Count).Distinct().Order().ToArray();
        List<MeshMaterialFactors> factors = [];
        for (int slot = 0; slot < used.Length; slot++)
        {
            JsonNode? extras = materials[used[slot]]?["extras"];
            if (extras?["unityMaterial"]?.GetValue<string>() is not { } source || !colors.TryGetValue(source, out var color)) continue;
            JsonNode? properties = extras["unityProperties"];
            bool baseColor = properties?["baseColorFactor"]?.GetValue<string>() == MainColorProperty;
            bool emission = color.Emissive is not null && properties?["emissiveFactor"]?.GetValue<string>() == EmissionProperty;
            if (!baseColor && !emission) continue;
            Vector4 linear = new(Linear(color.BaseColor.X), Linear(color.BaseColor.Y), Linear(color.BaseColor.Z), color.BaseColor.W);
            (Vector3 emissive, float strength) = emission ? Emissive(color.Emissive!.Value) : (Vector3.Zero, 0);
            factors.Add(new MeshMaterialFactors((uint)slot, baseColor, new Color(Clamp(linear.X), Clamp(linear.Y),
                Clamp(linear.Z), Clamp(linear.W)), emission, emissive, strength));
        }
        return factors.ToArray();
    }

    /// <summary>
    /// The new linear base colour for one converted glTF material, when this palette recolours its Unity material
    /// and its base colour factor came from _Color (as <see cref="Factors"/> decides per slot); otherwise null.
    /// </summary>
    internal Color? BaseColor(JsonNode material)
    {
        JsonNode? extras = material["extras"];
        if (extras?["unityMaterial"]?.GetValue<string>() is not { } source || !colors.TryGetValue(source, out var color)) return null;
        if (extras["unityProperties"]?["baseColorFactor"]?.GetValue<string>() != MainColorProperty) return null;
        return new Color(Clamp(Linear(color.BaseColor.X)), Clamp(Linear(color.BaseColor.Y)), Clamp(Linear(color.BaseColor.Z)),
            Clamp(color.BaseColor.W));
    }

    /// <summary>The JSON chunk of a GLB, from its first bytes (header and JSON chunk).</summary>
    internal static JsonNode ReadJson(ReadOnlySpan<byte> glb)
    {
        if (BinaryPrimitives.ReadUInt32LittleEndian(glb) != GlbMagic
            || BinaryPrimitives.ReadUInt32LittleEndian(glb[(HeaderBytes + 4)..]) != JsonChunk)
            throw new InvalidOperationException("Not a GLB with a leading JSON chunk.");
        int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(glb[HeaderBytes..]);
        return JsonNode.Parse(glb.Slice(HeaderPrefixLength, length)) ?? throw new InvalidOperationException("Empty GLB JSON chunk.");
    }

    /// <summary>Bytes needed to read a GLB's JSON chunk, from its first <see cref="HeaderPrefixLength"/> bytes.</summary>
    internal static int JsonPrefixLength(ReadOnlySpan<byte> header) =>
        HeaderPrefixLength + (int)BinaryPrimitives.ReadUInt32LittleEndian(header[HeaderBytes..]);

    /// <summary>A linear HDR emission as a 0..1 factor and a strength, the split glTF and the Engine use.</summary>
    private static (Vector3 Factor, float Strength) Emissive(Vector3 gamma)
    {
        Vector3 linear = new(Linear(gamma.X), Linear(gamma.Y), Linear(gamma.Z));
        float peak = MathF.Max(linear.X, MathF.Max(linear.Y, linear.Z));
        return peak > 1 ? (linear / peak, peak) : (linear, 1);
    }

    /// <summary>Unity's gamma-encoded colour channel to linear; HDR channels above 1 follow the same power curve.</summary>
    private static float Linear(float value) => value <= .04045f ? value / 12.92f
        : value <= 1 ? MathF.Pow((value + .055f) / 1.055f, 2.4f)
        : MathF.Pow(value, 2.2f);

    private static float Clamp(float value) => Math.Clamp(value, 0, 1);
}
