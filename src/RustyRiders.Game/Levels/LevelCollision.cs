using System.Numerics;
using Rusty.Engine;

namespace RustyRiders.Game.Levels;

/// <summary>
/// A level's raw static-mesh collision: one asset per tile prefab's blocked walk-grid cells, one instance per placed
/// tile; a generated build's meshes as their own assets; and the ground.
/// </summary>
internal sealed class CollisionBuilder
{
    private const ulong GroundAsset = 1;
    internal const ulong NoCollision = 0;
    private readonly List<Vector3> vertices = [];
    private readonly List<Triangle> triangles = [];
    private readonly List<StaticMeshAsset> assets = [];
    private readonly List<StaticMeshInstance> instances = [];

    /// <summary>Boxes over the blocked cells (merged along each row), in a tile's glTF-local space.</summary>
    internal ulong AddBlockedCells(string[] walkable, float cellSize, float height)
    {
        int firstVertex = vertices.Count, firstTriangle = triangles.Count;
        float step = cellSize / WalkCells.PerTile;
        for (int row = 0; row < WalkCells.PerTile; row++)
        {
            for (int column = 0; column < WalkCells.PerTile; column++)
            {
                if (walkable[row][column] != '#') continue;
                int end = column;
                while (end + 1 < WalkCells.PerTile && walkable[row][end + 1] == '#') end++;
                // Unity tile-local: column 0 is west (-x), row 0 is north (+z); glTF mirrors x.
                float westX = -cellSize / 2 + column * step, eastX = -cellSize / 2 + (end + 1) * step;
                float northZ = cellSize / 2 - row * step, southZ = northZ - step;
                AddBox(new Vector3(-eastX, 0, southZ), new Vector3(-westX, height, northZ), firstVertex);
                column = end;
            }
        }
        if (triangles.Count == firstTriangle) return NoCollision;
        ulong id = (ulong)assets.Count + GroundAsset + 1;
        assets.Add(new StaticMeshAsset(id, (uint)firstVertex, (uint)(vertices.Count - firstVertex),
            (uint)firstTriangle, (uint)(triangles.Count - firstTriangle)));
        return id;
    }

    internal void Place(ulong instance, ulong asset, Transform transform)
    {
        if (asset != NoCollision) instances.Add(new StaticMeshInstance(instance, asset, transform));
    }

    /// <summary>A retained mesh as its own collision asset (the Engine copies its geometry), placed once.</summary>
    internal void AddMesh(ulong id, MeshResource mesh, Transform transform)
    {
        assets.Add(new StaticMeshAsset(id, new MeshResourceReference(mesh), 0, 0, 0, 0));
        instances.Add(new StaticMeshInstance(id, id, transform));
    }

    internal void AddGround(ulong instance, Vector3 min, Vector3 max)
    {
        int first = vertices.Count;
        vertices.AddRange([new(min.X, 0, min.Z), new(max.X, 0, min.Z), new(max.X, 0, max.Z), new(min.X, 0, max.Z)]);
        triangles.AddRange([new(0, 2, 1), new(0, 3, 2)]); // asset-local indices, wound to face up
        assets.Add(new StaticMeshAsset(GroundAsset, (uint)first, 4, (uint)triangles.Count - 2, 2));
        instances.Add(new StaticMeshInstance(instance, GroundAsset, new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One)));
    }

    internal CollisionReplaceRequest Request(SpatialSession session) =>
        new(session, assets.ToArray(), vertices.ToArray(), triangles.ToArray(), instances.ToArray());

    /// <summary>An axis-aligned box, faces wound outward. Triangle indices are relative to the asset's first vertex.</summary>
    private void AddBox(Vector3 min, Vector3 max, int assetFirstVertex)
    {
        int assetFirst = vertices.Count - assetFirstVertex;
        for (int corner = 0; corner < 8; corner++)
            vertices.Add(new Vector3((corner & 1) == 0 ? min.X : max.X, (corner & 2) == 0 ? min.Y : max.Y, (corner & 4) == 0 ? min.Z : max.Z));
        int[][] faces = [[0, 2, 3, 1], [4, 5, 7, 6], [0, 1, 5, 4], [2, 6, 7, 3], [0, 4, 6, 2], [1, 3, 7, 5]];
        foreach (int[] face in faces)
        {
            uint a = (uint)(assetFirst + face[0]), b = (uint)(assetFirst + face[1]), c = (uint)(assetFirst + face[2]), d = (uint)(assetFirst + face[3]);
            triangles.Add(new Triangle(a, b, c));
            triangles.Add(new Triangle(a, c, d));
        }
    }
}
