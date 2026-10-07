using System.Numerics;
using Rusty.Engine;
using RustyRiders.Game.Content;
using RustyRiders.Game.Art;
using RustyRiders.Game.Player;

namespace RustyRiders.Game.Gallery;

/// <summary>
/// Lays the converted old-game prefabs out as walkable rows on a flat ground. Each exhibit keeps its prefab's own
/// arrangement and is moved as a whole so its lowest point rests on the ground. Only the ground collides: the
/// converted meshes are drawn, not collided with.
/// </summary>
internal sealed class GalleryScene : IWalkScene
{
    private const int CollisionChunkSize = 16;
    private const double CollisionVoxelSize = 0.5;
    private const ulong GroundObjectId = 1;
    private const ulong GroundCollisionAsset = 1;
    private const ulong FirstPlacementObjectId = 100;
    private const float GroundThickness = 0.2f;

    private readonly IEngineContext engine;
    private readonly ConvertedArt art;
    private readonly List<AppearanceFact> facts = [];
    private readonly List<string> problems = [];
    private readonly Appearance ground;

    internal GalleryScene(IEngineContext engine, GalleryDefinition definition)
    {
        this.engine = engine;
        Definition = definition;
        art = new ConvertedArt(engine, definition.ArtRoot);
        Session = engine.Spatial.CreateSession(new SpatialSessionConfig(CollisionVoxelSize, CollisionChunkSize, VoxelSurfaceMode.GreedyCubes));
        ulong nextObject = FirstPlacementObjectId;
        float rowZ = 0;
        foreach (GalleryRow row in definition.Rows)
        {
            float cursorX = 0, rowDepth = 0;
            foreach (string exhibit in row.Exhibits)
            {
                List<(ArtMesh Mesh, Transform Pose)> pieces = art.Pieces(exhibit);
                if (pieces.Count == 0)
                {
                    problems.Add($"{exhibit}: nothing drawable");
                    continue;
                }
                (Vector3 min, Vector3 max) = Bounds(pieces);
                Vector3 offset = new(cursorX - min.X, -min.Y, rowZ - min.Z);
                foreach ((ArtMesh mesh, Transform pose) in pieces)
                    facts.Add(new AppearanceFact(nextObject++, false, 0, pose with { Translation = pose.Translation + offset },
                        mesh.Appearance, true, RenderLayer.Scene));
                Exhibits.Add(new Exhibit($"{row.Label} / {System.IO.Path.GetFileName(exhibit)}", min + offset, max + offset));
                cursorX += max.X - min.X + definition.ExhibitGap;
                rowDepth = Math.Max(rowDepth, max.Z - min.Z);
            }
            rowZ += rowDepth + definition.RowGap;
        }
        ground = engine.Graphics.CreatePrimitive(new PrimitiveAppearanceRequest(PrimitiveGeometry.Cube, false,
            Authored.Color(definition.GroundColor)));
        (Vector3 groundMin, Vector3 groundMax) = GroundExtent();
        facts.Add(new AppearanceFact(GroundObjectId, false, 0, new Transform(
            new Vector3((groundMin.X + groundMax.X) / 2, -GroundThickness / 2, (groundMin.Z + groundMax.Z) / 2), Quaternion.Identity,
            new Vector3(groundMax.X - groundMin.X, GroundThickness, groundMax.Z - groundMin.Z)), ground, true, RenderLayer.Scene));
        AdmitGroundCollision(groundMin, groundMax);
        engine.CameraView.SetBackgroundColor(new SetBackgroundColorRequest(Authored.Color(definition.BackgroundColor)));
    }

    internal GalleryDefinition Definition { get; }
    public SpatialSession Session { get; }
    public Vector3 SpawnFeet => Authored.Vector(Definition.Spawn);
    public float SpawnYawDegrees => Definition.SpawnYawDegrees;
    internal List<Exhibit> Exhibits { get; } = [];
    public IReadOnlyList<string> Problems => [.. art.Problems, .. problems];

    public string Status => Exhibits.Count == 0
        ? "Gallery: no converted art found. Run scripts/import-old-art.sh, then restart."
        : $"Gallery: {Exhibits.Count} exhibits · {facts.Count - 1} placements · {art.MeshCount} meshes";

    public string Describe(Vector3 position) => Exhibits.Count == 0 ? ""
        : Exhibits.MinBy(exhibit => Vector2.Distance(new(position.X, position.Z), exhibit.CenterXZ))!.Label;

    public void Animate(double worldSeconds) { }

    public IEnumerable<AppearanceFact> Facts => facts;

    public void Dispose()
    {
        engine.Graphics.PublishSnapshot([]);
        ground.Dispose();
        art.Dispose();
        Session.Dispose();
    }

    private static (Vector3 Min, Vector3 Max) Bounds(List<(ArtMesh Mesh, Transform Pose)> pieces)
    {
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        foreach ((ArtMesh mesh, Transform pose) in pieces)
        {
            Matrix4x4 world = Matrix4x4.CreateScale(pose.Scale) * Matrix4x4.CreateFromQuaternion(pose.Rotation)
                * Matrix4x4.CreateTranslation(pose.Translation);
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 local = new((corner & 1) == 0 ? mesh.BoundsMin.X : mesh.BoundsMax.X,
                    (corner & 2) == 0 ? mesh.BoundsMin.Y : mesh.BoundsMax.Y, (corner & 4) == 0 ? mesh.BoundsMin.Z : mesh.BoundsMax.Z);
                Vector3 point = Vector3.Transform(local, world);
                min = Vector3.Min(min, point);
                max = Vector3.Max(max, point);
            }
        }
        return (min, max);
    }

    private (Vector3 Min, Vector3 Max) GroundExtent()
    {
        Vector3 margin = new(Definition.GroundMargin, 0, Definition.GroundMargin);
        Vector3 spawn = Authored.Vector(Definition.Spawn);
        Vector3 min = Exhibits.Aggregate(spawn, (current, exhibit) => Vector3.Min(current, exhibit.Min));
        Vector3 max = Exhibits.Aggregate(spawn, (current, exhibit) => Vector3.Max(current, exhibit.Max));
        return (min - margin, max + margin);
    }

    private void AdmitGroundCollision(Vector3 min, Vector3 max)
    {
        Vector3[] corners = [new(min.X, 0, min.Z), new(max.X, 0, min.Z), new(max.X, 0, max.Z), new(min.X, 0, max.Z)];
        Triangle[] triangles = [new(0, 2, 1), new(0, 3, 2)]; // wound to face up
        engine.Spatial.ReplaceCollision(new CollisionReplaceRequest(Session,
            new[] { new StaticMeshAsset(GroundCollisionAsset, 0, (uint)corners.Length, 0, (uint)triangles.Length) },
            corners, triangles, new[] { new StaticMeshInstance(GroundObjectId, GroundCollisionAsset, new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One)) }));
    }
}

internal sealed record Exhibit(string Label, Vector3 Min, Vector3 Max)
{
    internal Vector2 CenterXZ => new((Min.X + Max.X) / 2, (Min.Z + Max.Z) / 2);
}
