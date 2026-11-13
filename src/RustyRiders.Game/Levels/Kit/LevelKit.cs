using System.Diagnostics;
using System.Numerics;
using Rusty.Engine;

namespace RustyRiders.Game.Levels.Kit;

/// <summary>
/// A kit level's Engine resources, made from its <see cref="KitGeometry"/>: the sconces' lights (created once the level
/// is shown, <see cref="Light"/>: a light without a parent shows as soon as it exists), the collision mesh, and
/// the drawn sections, each band made one mesh and cut by level cell. Made at once, or in the background: then
/// <see cref="Prepare"/> creates and cuts the bands off the update a few at a time, takes the sections once every cut is
/// ready, and reports them ready once the renderer has drawn them.
/// </summary>
internal sealed class LevelKit : IGeneratedLevel
{
    internal const int WallSlot = 0, FloorSlot = 1, CeilingSlot = 2, TrimSlot = 3, RunnerSlot = 4, FlameSlot = 5, SlotCount = 6;
    // Logical light ids, never reused while the process lives: the next level is built while the last still holds its lights.
    private static ulong nextLightId = 1;

    // Only what making the meshes needs is kept from the geometry: its builders go once their meshes are made.
    private readonly Vector3 origin, cell;
    private readonly string built;
    private readonly double geometrySeconds;
    private readonly Material[] materials;
    private readonly Queue<MeshBuilder> waiting;
    private readonly List<MeshPartition> cutting = [];
    private readonly List<(MeshResource Mesh, Appearance Appearance)> sections = [];
    private readonly List<LightDescriptor> sconces;
    private readonly List<Light> lights = [];
    private readonly MeshResource solid;
    private readonly int bands;
    private ulong? batch;
    private bool ready;
    // Load-cost evidence only, never simulation time: the seconds spent making meshes, the most in one update, and the updates.
    private double makeSeconds, longestUpdate;
    private int updates;

    /// <param name="materials">The wall, floor, ceiling, trim, runner and flame materials, in slot order.</param>
    /// <param name="deferred">Whether the drawn sections are made later, by <see cref="Prepare"/>, rather than now.</param>
    internal LevelKit(IEngineContext engine, KitGeometry geometry, Material[] materials, bool deferred)
    {
        (origin, cell, built, geometrySeconds) = (geometry.Min, new Vector3(geometry.CellSize), geometry.Summary, geometry.Seconds);
        this.materials = materials;
        waiting = new Queue<MeshBuilder>(geometry.Bands);
        bands = waiting.Count;
        sconces = geometry.Sconces;
        solid = geometry.Solid.Create(engine, [materials[WallSlot]]);
        if (deferred) return;
        long started = Stopwatch.GetTimestamp();
        while (waiting.TryDequeue(out MeshBuilder? band))
        {
            using MeshResource whole = band.Create(engine, materials);
            using MeshPartition partition = engine.Graphics.PartitionMesh(new MeshPartitionRequest(whole, origin, cell));
            Take(engine, partition);
        }
        makeSeconds = longestUpdate = Stopwatch.GetElapsedTime(started).TotalSeconds;
        updates = 1;
        ready = true;
    }

    public Transform Placement { get; } = new(Vector3.Zero, Quaternion.Identity, Vector3.One);
    public string Summary => FormattableString.Invariant(
        $"{built}, {bands} meshes, {sections.Count} sections, geometry {geometrySeconds:0.000} s, meshes {makeSeconds:0.000} s over {updates} updates (longest {longestUpdate * 1000:0.0} ms)");
    public IEnumerable<Appearance> Sections => sections.Select(section => section.Appearance);
    public IReadOnlyList<MeshResource> Collision => [solid];

    /// <summary>
    /// One update's share of making the sections in the background: creates and cuts bands until
    /// <paramref name="budgetSeconds"/> is spent (one at least), takes every section once all the cuts are ready, and
    /// returns true once the renderer has drawn them.
    /// </summary>
    internal bool Prepare(IEngineContext engine, double budgetSeconds)
    {
        if (ready) return true;
        long started = Stopwatch.GetTimestamp();
        if (waiting.Count > 0)
        {
            do
            {
                MeshBuilder band = waiting.Dequeue();
                // The whole band is only its partition's source: the cut keeps its own reference.
                using MeshResource whole = band.Create(engine, materials, deferred: true);
                cutting.Add(engine.Graphics.PartitionMesh(new MeshPartitionRequest(whole, origin, cell, Deferred: true)));
            }
            while (waiting.Count > 0 && Stopwatch.GetElapsedTime(started).TotalSeconds < budgetSeconds);
        }
        else if (batch is null && cutting.All(partition => engine.Graphics.ReadMeshPartition(partition).Ready))
        {
            foreach (MeshPartition partition in cutting)
            {
                Take(engine, partition);
                partition.Dispose();
            }
            cutting.Clear();
            batch = engine.Graphics.ReadResourcePreparation().LastBatch;
        }
        else if (batch is { } taken && engine.Graphics.ReadResourcePreparation().CompletedBatch >= taken)
        {
            ready = true;
        }
        double seconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
        makeSeconds += seconds;
        longestUpdate = Math.Max(longestUpdate, seconds);
        updates++;
        return ready;
    }

    /// <summary>Creates the sconces' lights, as the level is shown.</summary>
    internal void Light(IEngineContext engine)
    {
        if (lights.Count > 0) return;
        foreach (LightDescriptor light in sconces) lights.Add(engine.Graphics.CreateLight(new LightRequest(nextLightId++, false, 0, light)));
    }

    public void Dispose()
    {
        foreach (MeshPartition partition in cutting) partition.Dispose();
        cutting.Clear();
        foreach (Light light in lights) light.Dispose();
        lights.Clear();
        foreach ((MeshResource mesh, Appearance appearance) in sections)
        {
            appearance.Dispose();
            mesh.Dispose();
        }
        sections.Clear();
        solid.Dispose();
    }

    /// <summary>The mesh slot a surface's material is bound to.</summary>
    internal static int Slot(KitSurface surface) => surface switch
    {
        KitSurface.Wall => WallSlot,
        KitSurface.Floor => FloorSlot,
        KitSurface.Ceiling => CeilingSlot,
        KitSurface.Runner => RunnerSlot,
        _ => TrimSlot,
    };

    private void Take(IEngineContext engine, MeshPartition partition)
    {
        uint parts = engine.Graphics.ReadMeshPartition(partition).PartCount;
        for (uint index = 0; index < parts; index++)
        {
            MeshResource section = engine.Graphics.TakeMeshPartitionPart(new MeshPartitionPartRequest(partition, index));
            sections.Add((section, engine.Graphics.CreateMeshAppearance(section)));
        }
    }
}
