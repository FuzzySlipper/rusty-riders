using System.Numerics;
using Rusty.Engine;

namespace RustyRiders.Game.Levels;

/// <summary>Positions, smooth normals, uvs and per-material triangle lists for one retained mesh (the generated level builds).</summary>
internal sealed class MeshBuilder
{
    private readonly List<Vector3> positions = [];
    private readonly List<Vector3> normals = [];
    private readonly List<Vector2> uvs = [];
    private readonly List<uint>[] slots;

    /// <summary>A builder with <paramref name="slotCount"/> material slots, numbered from 0.</summary>
    internal MeshBuilder(int slotCount) => slots = Enumerable.Range(0, slotCount).Select(_ => new List<uint>()).ToArray();

    internal int VertexCount => positions.Count;
    internal int TriangleCount => slots.Sum(slot => slot.Count) / 3;

    internal uint Vertex(Vector3 position, Vector2 uv)
    {
        positions.Add(position);
        normals.Add(Vector3.Zero);
        uvs.Add(uv);
        return (uint)positions.Count - 1;
    }

    /// <summary>Two triangles wound to face <paramref name="facing"/>; their normals accumulate on the corners.</summary>
    internal void Quad(int slot, uint a, uint b, uint c, uint d, Vector3 facing)
    {
        Triangle(slot, a, b, c, facing);
        Triangle(slot, a, c, d, facing);
    }

    internal MeshResource Create(IEngineContext engine, Material[] materials)
    {
        List<uint> indices = [];
        List<MeshGroup> groups = [];
        for (int slot = 0; slot < slots.Length; slot++)
        {
            if (slots[slot].Count == 0) continue;
            groups.Add(new MeshGroup((uint)slot, (uint)indices.Count, (uint)slots[slot].Count));
            indices.AddRange(slots[slot]);
        }
        Vector3[] unit = normals.Select(n => n == Vector3.Zero ? Vector3.UnitY : Vector3.Normalize(n)).ToArray();
        MeshMaterialBinding[] bindings = materials.Select((material, slot) => new MeshMaterialBinding((uint)slot, material)).ToArray();
        return engine.Graphics.CreateMeshResource(new MeshResourceCreateRequest(positions.ToArray(), unit, uvs.ToArray(),
            indices.ToArray(), groups.ToArray(), bindings));
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
        slots[slot].AddRange([a, b, c]);
        foreach (uint corner in (uint[])[a, b, c]) normals[(int)corner] += normal;
    }
}
