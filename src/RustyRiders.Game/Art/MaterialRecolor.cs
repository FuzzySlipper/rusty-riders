using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;

namespace RustyRiders.Game.Art;

/// <summary>
/// Unity-side recolouring of converted materials, as the old game did at run time: per Unity material, a new
/// Material.color (the shader's _Color) and optionally a new _EmissionColor. The converter records on each glTF
/// material which Unity material it came from and which Unity property each factor came from
/// (<c>extras.unityMaterial</c>, <c>extras.unityProperties</c>); only factors that came from those properties change.
/// </summary>
internal sealed class MaterialRecolor
{
    private const uint GlbMagic = 0x46546C67, JsonChunk = 0x4E4F534A, GlbVersion = 2;
    private const int HeaderBytes = 12, ChunkHeaderBytes = 8;
    private const string MainColorProperty = "_Color";
    private const string EmissionProperty = "_EmissionColor";
    private const string EmissiveStrength = "KHR_materials_emissive_strength";

    internal const int HeaderPrefixLength = HeaderBytes + ChunkHeaderBytes;

    private readonly Dictionary<string, (Vector4 BaseColor, Vector3? Emissive)> colors = [];

    internal MaterialRecolor(string id) => Id = id;

    internal string Id { get; }

    /// <summary>Recolours a Unity material (Assets-relative .mat path). Colours are Unity's stored, gamma-encoded values.</summary>
    internal void Set(string unityMaterial, Vector4 baseColor, Vector3? emissive) => colors[unityMaterial] = (baseColor, emissive);

    /// <summary>Whether a GLB's JSON chunk names any recoloured material.</summary>
    internal bool Touches(JsonNode document) => document["materials"] is JsonArray materials
        && materials.Any(material => material?["extras"]?["unityMaterial"]?.GetValue<string>() is { } source && colors.ContainsKey(source));

    /// <summary>The GLB with its recoloured factors; the binary chunk (geometry, textures) is copied unchanged.</summary>
    internal byte[] Apply(ReadOnlySpan<byte> glb)
    {
        (JsonNode document, int jsonEnd) = ReadJson(glb);
        foreach (JsonNode? material in document["materials"]!.AsArray())
        {
            if (material?["extras"] is not JsonNode extras || extras["unityMaterial"]?.GetValue<string>() is not { } source
                || !colors.TryGetValue(source, out var color)) continue;
            JsonNode? properties = extras["unityProperties"];
            if (properties?["baseColorFactor"]?.GetValue<string>() == MainColorProperty)
            {
                JsonObject pbr = material["pbrMetallicRoughness"] as JsonObject ?? [];
                material["pbrMetallicRoughness"] = pbr;
                pbr["baseColorFactor"] = new JsonArray(Linear(color.BaseColor.X), Linear(color.BaseColor.Y), Linear(color.BaseColor.Z), color.BaseColor.W);
            }
            if (color.Emissive is { } emissive && properties?["emissiveFactor"]?.GetValue<string>() == EmissionProperty)
            {
                SetEmissive(document, material.AsObject(), new Vector3(Linear(emissive.X), Linear(emissive.Y), Linear(emissive.Z)));
            }
        }
        return Write(glb, document, jsonEnd);
    }

    /// <summary>The JSON chunk of a GLB, from its first bytes (header and JSON chunk).</summary>
    internal static (JsonNode Document, int JsonEnd) ReadJson(ReadOnlySpan<byte> glb)
    {
        if (BinaryPrimitives.ReadUInt32LittleEndian(glb) != GlbMagic
            || BinaryPrimitives.ReadUInt32LittleEndian(glb[(HeaderBytes + 4)..]) != JsonChunk)
            throw new InvalidOperationException("Not a GLB with a leading JSON chunk.");
        int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(glb[HeaderBytes..]);
        int start = HeaderBytes + ChunkHeaderBytes;
        return (JsonNode.Parse(glb.Slice(start, length)) ?? throw new InvalidOperationException("Empty GLB JSON chunk."), start + length);
    }

    /// <summary>Bytes needed to read a GLB's JSON chunk, from its first <see cref="HeaderPrefixLength"/> bytes.</summary>
    internal static int JsonPrefixLength(ReadOnlySpan<byte> header) =>
        HeaderPrefixLength + (int)BinaryPrimitives.ReadUInt32LittleEndian(header[HeaderBytes..]);

    private static void SetEmissive(JsonNode document, JsonObject material, Vector3 emissive)
    {
        float peak = MathF.Max(emissive.X, MathF.Max(emissive.Y, emissive.Z));
        JsonObject extensions = material["extensions"] as JsonObject ?? [];
        material["extensions"] = extensions;
        extensions.Remove(EmissiveStrength);
        if (peak > 1)
        {
            extensions[EmissiveStrength] = new JsonObject { ["emissiveStrength"] = peak };
            emissive /= peak;
            JsonArray used = document["extensionsUsed"] as JsonArray ?? [];
            document.AsObject()["extensionsUsed"] = used;
            if (!used.Any(name => name?.GetValue<string>() == EmissiveStrength)) used.Add(EmissiveStrength);
        }
        if (extensions.Count == 0) material.Remove("extensions");
        material["emissiveFactor"] = new JsonArray(emissive.X, emissive.Y, emissive.Z);
    }

    /// <summary>Unity's gamma-encoded colour channel to linear; HDR channels above 1 follow the same power curve.</summary>
    private static float Linear(float value) => value <= .04045f ? value / 12.92f
        : value <= 1 ? MathF.Pow((value + .055f) / 1.055f, 2.4f)
        : MathF.Pow(value, 2.2f);

    private static byte[] Write(ReadOnlySpan<byte> glb, JsonNode document, int jsonEnd)
    {
        byte[] json = Encoding.UTF8.GetBytes(document.ToJsonString());
        int padded = (json.Length + 3) & ~3;
        ReadOnlySpan<byte> rest = glb[jsonEnd..];
        byte[] output = new byte[HeaderBytes + ChunkHeaderBytes + padded + rest.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(output, GlbMagic);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(4), GlbVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(8), (uint)output.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(HeaderBytes), (uint)padded);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(HeaderBytes + 4), JsonChunk);
        json.CopyTo(output.AsSpan(HeaderBytes + ChunkHeaderBytes));
        output.AsSpan(HeaderBytes + ChunkHeaderBytes + json.Length, padded - json.Length).Fill((byte)' ');
        rest.CopyTo(output.AsSpan(HeaderBytes + ChunkHeaderBytes + padded));
        return output;
    }
}
