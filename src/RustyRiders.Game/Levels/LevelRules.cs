using System.Text.Json.Serialization;
using Rusty.Engine;
using RustyRiders.Game.Content;

namespace RustyRiders.Game.Levels;

/// <summary>
/// The authored rules that turn a stamped level into a play space: where its points go (points.json), how its
/// navigation is derived (navigation.json) and which worlds a rift can lead to (worlds.json). Loaded once.
/// </summary>
internal sealed record LevelRules(PointRules Points, NavigationTuning Navigation, World[] Worlds)
{
    internal static LevelRules Load(IEngineContext engine)
    {
        PointRules points = Authored.Read(engine, PointRules.Path, LevelRulesJson.Default.PointRules);
        points.Validate();
        NavigationTuning navigation = Authored.Read(engine, NavigationTuning.Path, LevelRulesJson.Default.NavigationTuning);
        navigation.Validate();
        World[] worlds = Authored.Read(engine, WorldCatalog.Path, LevelRulesJson.Default.WorldCatalog).Worlds;
        for (int i = 0; i < worlds.Length; i++) Authored.Colour(WorldCatalog.Path, $"worlds[{i}].riftColor", worlds[i].RiftColor);
        Authored.Require(worlds.Length > 0, WorldCatalog.Path, "worlds", "must name at least one world.");
        Authored.Require(worlds.Select(w => w.Tileset).Distinct().Count() == worlds.Length, WorldCatalog.Path, "worlds", "each tileset is one world.");
        return new LevelRules(points, navigation, worlds);
    }

    internal World? World(string tileset) => Worlds.FirstOrDefault(world => world.Tileset == tileset);
}

/// <summary>
/// Which layout nodes hold the level's points. Rifts go to the primary objective of rooms tagged with
/// <see cref="RiftTags"/>, in that order of preference (never the start room), up to <see cref="MaximumRifts"/>; when
/// fewer than <see cref="MinimumRifts"/> fit, other rooms' objectives make up the rest. Caches take the objectives named
/// in <see cref="CacheObjectives"/> and the primary objective of rooms tagged <see cref="CacheTags"/>. Residents wait in
/// rooms tagged <see cref="ResidentTags"/>. The player arrives <see cref="ArrivalMetres"/> in front of the entry portal.
/// A point the arrival cannot walk to moves to the nearest floor it can, up to <see cref="ReachSlackMetres"/> away, or is
/// left out. A level whose arrival reaches fewer than the minimum rifts is rejected, and the next seed is tried, up to
/// <see cref="LevelAttempts"/> levels in all.
/// </summary>
internal sealed record PointRules(string[] RiftTags, int MinimumRifts, int MaximumRifts, string[] CacheTags, string[] CacheObjectives,
    string[] ResidentTags, float ArrivalMetres, float ReachSlackMetres, int LevelAttempts, PortalLook Portal, float[] EntryColor, float[] ReturnColor,
    MarkerLook Markers)
{
    internal const string Path = "levels/points.json";

    internal void Validate()
    {
        Authored.Require(MinimumRifts >= 1 && MaximumRifts >= MinimumRifts, Path, "minimumRifts", "must be at least 1 and no more than maximumRifts.");
        Authored.AtLeast(Path, "arrivalMetres", ArrivalMetres, 0);
        Authored.AtLeast(Path, "reachSlackMetres", ReachSlackMetres, 0);
        Authored.Require(LevelAttempts >= 1, Path, "levelAttempts", "must be at least 1.");
        Authored.Colour(Path, "entryColor", EntryColor);
        Authored.Colour(Path, "returnColor", ReturnColor);
        Portal.Validate();
        Markers.Validate();
    }
}

/// <summary>A portal's placeholder look in metres and hertz: a pulsing disc with cubes orbiting its rim.</summary>
internal sealed record PortalLook(float Width, float Height, float Depth, float Lift, float Pulse, float PulseHz, int Orbiters,
    float OrbiterSize, float OrbitHz, float TriggerRadius)
{
    internal void Validate()
    {
        const string path = PointRules.Path;
        Authored.Positive(path, "portal.width", Width);
        Authored.Positive(path, "portal.height", Height);
        Authored.Positive(path, "portal.depth", Depth);
        Authored.AtLeast(path, "portal.lift", Lift, 0);
        Authored.Within(path, "portal.pulse", Pulse, 0, 1);
        Authored.AtLeast(path, "portal.pulseHz", PulseHz, 0);
        Authored.Require(Orbiters >= 0, path, "portal.orbiters", "must not be negative.");
        Authored.Positive(path, "portal.orbiterSize", OrbiterSize);
        Authored.AtLeast(path, "portal.orbitHz", OrbitHz, 0);
        Authored.Positive(path, "portal.triggerRadius", TriggerRadius);
    }
}

/// <summary>Placeholder markers for cache and resident spots until items and enemies draw their own.</summary>
internal sealed record MarkerLook(bool Show, float Size, float[] CacheColor, float[] ResidentColor)
{
    internal void Validate()
    {
        Authored.Positive(PointRules.Path, "markers.size", Size);
        Authored.Colour(PointRules.Path, "markers.cacheColor", CacheColor);
        Authored.Colour(PointRules.Path, "markers.residentColor", ResidentColor);
    }
}

/// <summary>
/// How a level's Engine navigation is derived from its collision: the grid, the body it is walked with (enemies), how
/// far a query point may be from a support, the vertical band sampled around the floor, and a query's search budget.
/// </summary>
internal sealed record NavigationTuning(float CellSize, uint MaximumCells, bool DiagonalNeighbors, float AgentRadius, float AgentHeight,
    float MaximumStepHeight, float SnapAcrossMetres, float BelowFloorMetres, float AboveFloorMetres, uint QueryVisitedCells)
{
    internal const string Path = "levels/navigation.json";

    internal void Validate()
    {
        Authored.Positive(Path, "cellSize", CellSize);
        Authored.Require(MaximumCells > 0, Path, "maximumCells", "must be positive.");
        Authored.Within(Path, "agentRadius", AgentRadius, float.Epsilon, CellSize / 2);
        Authored.Positive(Path, "agentHeight", AgentHeight);
        Authored.AtLeast(Path, "maximumStepHeight", MaximumStepHeight, 0);
        Authored.AtLeast(Path, "snapAcrossMetres", SnapAcrossMetres, 0);
        Authored.Positive(Path, "belowFloorMetres", BelowFloorMetres);
        Authored.Positive(Path, "aboveFloorMetres", AboveFloorMetres);
        Authored.Require(QueryVisitedCells > 0, Path, "queryVisitedCells", "must be positive.");
    }
}

/// <summary>A tileset as a world a rift leads to: its name and the colour its rifts show.</summary>
internal sealed record World(string Tileset, string Name, float[] RiftColor);

internal sealed record WorldCatalog(World[] Worlds)
{
    internal const string Path = "levels/worlds.json";
}

// Authored: missing constructor values, nulls in non-nullable fields and unknown members are errors, not defaults.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(PointRules))]
[JsonSerializable(typeof(NavigationTuning))]
[JsonSerializable(typeof(WorldCatalog))]
internal sealed partial class LevelRulesJson : JsonSerializerContext;
