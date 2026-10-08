using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rusty.Engine.Debugging;
using RustyRiders.Game.Levels;
using RustyRiders.Game.Player;

namespace RustyRiders.Game.Developer;

/// <summary>
/// Live-debug commands for the current level (Engine command catalog; `rusty dev --live-debug`): read its gameplay
/// points, and developer overrides that stand the walker somewhere for inspection and playtests.
/// </summary>
internal sealed class LevelDebugCommands(Func<IWalkScene> scene, Walker walker, Func<string, string, int, string?> buildLevel) : IDebugCommandModule
{
    private const float ApproachMetres = 4;

    [DebugCommand("riders.level.inspect", Description = "Read the current level's status, entry, arrival, rifts (with destinations), caches, resident spots and navigation.")]
    public DebugCommandResult Inspect()
    {
        if (scene() is not LevelScene level) return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, "Not in a level.");
        LevelPoints points = level.Points;
        return DebugCommandResult.Success(JsonSerializer.Serialize(new LevelInspection(level.Status, Xyz(points.Entry), Xyz(points.Arrival),
            points.Rifts.Select(r => new RiftInspection(r.Index, Xyz(r.Feet), r.Destination?.Tileset ?? "", r.Destination?.Name ?? "return")).ToArray(),
            points.Caches.Select(Xyz).ToArray(), points.Residents.Select(Xyz).ToArray(), level.Problems.ToArray()),
            DebugJson.Default.LevelInspection));
    }

    [DebugCommand("riders.dev.level", Description = "Developer override: build and enter a level of a tileset, layout and seed (layout ids contain spaces: quote them).")]
    public DebugCommandResult Level(string tileset, string layout, int seed) =>
        buildLevel(tileset, layout, seed) is { } refusal
            ? DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, refusal)
            : Inspect();

    [DebugCommand("riders.level.route", Description = "Route from the arrival point to rift <index>: outcome, cells visited, and the nearest point reached when there is no path.")]
    public DebugCommandResult Route(int index)
    {
        if (scene() is not LevelScene level) return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, "Not in a level.");
        if (index < 0 || index >= level.Points.Rifts.Count)
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, $"Rifts are 0 to {level.Points.Rifts.Count - 1}.");
        Rusty.Engine.NavigationStepResult route = level.Navigation.Route(level.Points.Arrival, level.Points.Rifts[index].Feet);
        return DebugCommandResult.Success(FormattableString.Invariant(
            $"{route.Outcome}: visited {route.Visited}, path {route.Path.Length} cells, nearest {(route.NearestPresent ? $"{route.Nearest.X:0.0}, {route.Nearest.Y:0.0}, {route.Nearest.Z:0.0}" : "none")}"));
    }

    [DebugCommand("riders.dev.rift", Description = "Developer override: stand a few metres in front of rift <index>, facing it, so walking forward enters it.")]
    public DebugCommandResult GoToRift(int index)
    {
        if (scene() is not LevelScene level) return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, "Not in a level.");
        if (index < 0 || index >= level.Points.Rifts.Count)
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, $"Rifts are 0 to {level.Points.Rifts.Count - 1}.");
        RiftPoint rift = level.Points.Rifts[index];
        Vector3 face = new(MathF.Sin(rift.YawRadians), 0, MathF.Cos(rift.YawRadians));
        Vector3 stand = level.Navigation.Nearest(rift.Feet + face * ApproachMetres) ?? rift.Feet + face * ApproachMetres;
        walker.Place(stand, Walker.YawDegreesToward(stand, rift.Feet));
        return DebugCommandResult.Success($"Standing at {Xyz(stand)[0]:0.0}, {Xyz(stand)[2]:0.0}, facing rift {index} to {rift.Destination?.Name ?? "return"}.");
    }

    [DebugCommand("riders.dev.goto", Description = "Developer override: stand at floor point (x, z) facing a yaw in degrees (0 faces -Z, 90 faces +X), for repeatable captures.")]
    public DebugCommandResult GoTo(float x, float z, float yaw)
    {
        if (!float.IsFinite(x) || !float.IsFinite(z) || !float.IsFinite(yaw))
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, "The point and yaw must be finite numbers.");
        Vector3 point = new(x, 0, z);
        Vector3 stand = scene() is LevelScene level ? level.Navigation.Nearest(point) ?? point : point;
        walker.Place(stand, yaw);
        return DebugCommandResult.Success(FormattableString.Invariant($"Standing at {stand.X:0.0}, {stand.Y:0.0}, {stand.Z:0.0}."));
    }

    [DebugCommand("riders.dev.travel", Description = "Developer override: stand in rift <index>, so the next update goes through it.")]
    public DebugCommandResult Travel(int index)
    {
        if (scene() is not LevelScene level) return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, "Not in a level.");
        if (index < 0 || index >= level.Points.Rifts.Count)
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, $"Rifts are 0 to {level.Points.Rifts.Count - 1}.");
        RiftPoint rift = level.Points.Rifts[index];
        walker.Place(rift.Feet, walker.LookState.YawRadians * 180 / MathF.PI);
        return DebugCommandResult.Success($"Standing in rift {index} to {rift.Destination?.Name ?? "return"}.");
    }

    [DebugCommand("riders.dev.entry", Description = "Developer override: stand at the arrival point in front of the entry portal, facing into the level.")]
    public DebugCommandResult GoToEntry()
    {
        if (scene() is not LevelScene level) return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, "Not in a level.");
        walker.Place(level.Points.Arrival, level.SpawnYawDegrees);
        return DebugCommandResult.Success("Standing at the arrival point.");
    }

    private static float[] Xyz(Vector3 v) => [v.X, v.Y, v.Z];
}

internal sealed record RiftInspection(int Index, float[] Feet, string Tileset, string Name);

internal sealed record LevelInspection(string Status, float[] Entry, float[] Arrival, RiftInspection[] Rifts, float[][] Caches,
    float[][] Residents, string[] Problems);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LevelInspection))]
internal sealed partial class DebugJson : JsonSerializerContext;
