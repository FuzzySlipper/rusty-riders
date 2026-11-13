using System.Numerics;
using Rusty.Engine;

namespace RustyRiders.Game.Levels;

/// <summary>
/// Positions, smooth normals, uvs and per-material triangle lists for one retained mesh (the generated level builds).
/// Geometry whose vertices carry data for a material's shader (stone faces) goes to its companion, <see cref="Shaded"/>:
/// a mesh with product data is drawn outside the Engine's static mesh pool, so the rest of the level stays in it.
/// </summary>
internal sealed class MeshBuilder
{
    // Growable streams, handed to the Engine as they stand (it copies them), so a large build is not copied again first.
    private Vector3[] positions = new Vector3[1024], normals = new Vector3[1024];
    private Vector2[] uvs = new Vector2[1024];
    private Vector4[]? data; // only in the companion, once a vertex carries data for a shader; the rest read zero
    private int count;
    private readonly List<uint>[] slots;
    private MeshBuilder? shaded;

    /// <summary>A builder with <paramref name="slotCount"/> material slots, numbered from 0.</summary>
    internal MeshBuilder(int slotCount) => slots = Enumerable.Range(0, slotCount).Select(_ => new List<uint>()).ToArray();

    internal int VertexCount => count + (shaded?.count ?? 0);
    internal int TriangleCount => slots.Sum(slot => slot.Count) / 3 + (shaded?.TriangleCount ?? 0);

    /// <summary>The companion builder, with the same slots, for geometry whose vertices carry shader data.</summary>
    internal MeshBuilder Shaded => shaded ??= new MeshBuilder(slots.Length);

    /// <summary>Whether anything was built in <see cref="Shaded"/>.</summary>
    internal bool HasShaded => shaded is { count: > 0 };

    internal uint Vertex(Vector3 position, Vector2 uv)
    {
        if (count == positions.Length)
        {
            Array.Resize(ref positions, count * 2);
            Array.Resize(ref normals, count * 2);
            Array.Resize(ref uvs, count * 2);
            if (data is not null) Array.Resize(ref data, count * 2);
        }
        (positions[count], uvs[count]) = (position, uv);
        if (data is not null) data[count] = Vector4.Zero;
        return (uint)count++;
    }

    /// <summary>A vertex carrying <paramref name="values"/> to its material's shader (stone faces); every other vertex carries zero.</summary>
    internal uint Vertex(Vector3 position, Vector2 uv, Vector4 values)
    {
        data ??= new Vector4[positions.Length];
        uint index = Vertex(position, uv);
        data[index] = values;
        return index;
    }

    /// <summary>Two triangles wound to face <paramref name="facing"/>; their normals accumulate on the corners.</summary>
    internal void Quad(int slot, uint a, uint b, uint c, uint d, Vector3 facing)
    {
        Triangle(slot, a, b, c, facing);
        Triangle(slot, a, c, d, facing);
    }

    /// <summary>The mesh as built so far (not its <see cref="Shaded"/> companion), with its normals made final: nothing is added after.</summary>
    internal MeshResource Create(IEngineContext engine, Material[] materials)
    {
        uint[] indices = new uint[slots.Sum(slot => slot.Count)];
        List<MeshGroup> groups = [];
        int start = 0;
        for (int slot = 0; slot < slots.Length; slot++)
        {
            if (slots[slot].Count == 0) continue;
            groups.Add(new MeshGroup((uint)slot, (uint)start, (uint)slots[slot].Count));
            slots[slot].CopyTo(indices, start);
            start += slots[slot].Count;
        }
        for (int i = 0; i < count; i++) normals[i] = normals[i] == Vector3.Zero ? Vector3.UnitY : Vector3.Normalize(normals[i]); // final: no more triangles accumulate
        MeshMaterialBinding[] bindings = materials.Select((material, slot) => new MeshMaterialBinding((uint)slot, material)).ToArray();
        return engine.Graphics.CreateMeshResource(new MeshResourceCreateRequest(positions.AsMemory(0, count), normals.AsMemory(0, count), uvs.AsMemory(0, count),
            ReadOnlyMemory<Color>.Empty, indices, groups.ToArray(), bindings) { ProductData = data is null ? ReadOnlyMemory<Vector4>.Empty : data.AsMemory(0, count) });
    }

    /// <summary>A quad from four new corners with their UVs, wound to face <paramref name="facing"/>.</summary>
    internal void Face(int slot, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 uvA, Vector2 uvB, Vector2 uvC, Vector2 uvD, Vector3 facing) =>
        Quad(slot, Vertex(a, uvA), Vertex(b, uvB), Vertex(c, uvC), Vertex(d, uvD), facing);

    /// <summary>One triangle wound to face <paramref name="facing"/>; its normal accumulates on its corners.</summary>
    internal void Triangle(int slot, uint a, uint b, uint c, Vector3 facing)
    {
        Vector3 normal = Vector3.Cross(positions[(int)b] - positions[(int)a], positions[(int)c] - positions[(int)a]);
        if (normal.LengthSquared() < 1e-12f) return;
        if (Vector3.Dot(normal, facing) < 0)
        {
            (b, c) = (c, b);
            normal = -normal;
        }
        List<uint> indices = slots[slot];
        indices.Add(a);
        indices.Add(b);
        indices.Add(c);
        normals[(int)a] += normal;
        normals[(int)b] += normal;
        normals[(int)c] += normal;
    }
}
