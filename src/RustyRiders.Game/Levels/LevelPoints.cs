using System.Numerics;
using Rusty.Engine;

namespace RustyRiders.Game.Levels;

/// <summary>A rift out of the level: where it stands, which way its face turns, and the world it leads to.</summary>
internal sealed record RiftPoint(int Index, Vector3 Feet, float YawRadians, World Destination);

/// <summary>
/// A stamped level's gameplay points, standing on its navigation: the entry portal (where the player arrives and the
/// chase comes through), the rifts out, supply caches and where residents wait. Other owners read these; the level
/// scene draws the portals.
/// </summary>
internal sealed record LevelPoints(Vector3 Entry, float EntryYawRadians, Vector3 Arrival, IReadOnlyList<RiftPoint> Rifts,
    IReadOnlyList<Vector3> Caches, IReadOnlyList<Vector3> Residents, IReadOnlyList<string> Problems)
{
    /// <summary>
    /// Places the points from the plan's rooms by <paramref name="rules"/> and snaps each to the navigation; a point with
    /// no walkable support near it, or none the arrival can walk to within the slack, is left out and named in
    /// <see cref="Problems"/>. Rift destinations are the other worlds, shuffled by the seed.
    /// </summary>
    internal static LevelPoints Place(LevelPlan plan, PointRules rules, World[] worlds, string tileset, int seed,
        Func<(int X, int Z), Vector3> cellCentre, LevelNavigation navigation)
    {
        List<string> problems = [];
        Random random = new(seed);
        HashSet<(int X, int Z)> used = [plan.Spawn];
        PlannedRoom? start = plan.Rooms.FirstOrDefault(room => room.Cells.Contains(plan.Spawn));
        PlannedRoom[] others = plan.Rooms.Where(room => room != start && !room.Has("start")).ToArray();

        (int X, int Z) step = GridDirection.Step(plan.SpawnFacing);
        Vector3 facing = Vector3.Normalize(new Vector3(-step.X, 0, step.Z)); // Unity x mirrors to glTF -x
        Vector3 entry = navigation.Nearest(cellCentre(plan.Spawn)) ?? cellCentre(plan.Spawn);
        Vector3 arrival = navigation.Nearest(entry + facing * rules.ArrivalMetres) ?? entry;
        entry = Reachable(entry) ?? entry;

        List<(int X, int Z)> riftCells = [];
        foreach (string tag in rules.RiftTags)
            foreach (PlannedRoom room in others.Where(room => room.Has(tag)))
                if (riftCells.Count < rules.MaximumRifts && Primary(room) is { } cell && used.Add(cell)) riftCells.Add(cell);
        foreach (PlannedRoom room in others.OrderByDescending(room => Distance(Centroid(room), plan.Spawn)))
        {
            if (riftCells.Count >= rules.MinimumRifts) break;
            foreach (((int X, int Z) cell, _) in room.Objectives)
                if (riftCells.Count < rules.MinimumRifts && used.Add(cell)) riftCells.Add(cell);
        }
        if (riftCells.Count < rules.MinimumRifts) problems.Add($"only {riftCells.Count} rift spots for a minimum of {rules.MinimumRifts}");

        World[] destinations = worlds.Where(world => world.Tileset != tileset).OrderBy(_ => random.Next()).ToArray();
        if (destinations.Length == 0) destinations = worlds;
        List<RiftPoint> rifts = [];
        foreach ((int X, int Z) cell in riftCells)
        {
            if (Snap(cellCentre(cell), $"rift at cell {cell}") is not { } feet) continue;
            Vector3 toEntry = entry - feet;
            float yaw = MathF.Atan2(toEntry.X, toEntry.Z); // the disc's face turns toward where the player came in
            rifts.Add(new RiftPoint(rifts.Count, feet, yaw, destinations[rifts.Count % destinations.Length]));
        }

        List<Vector3> caches = [];
        foreach (PlannedRoom room in others)
        {
            IEnumerable<(int X, int Z)> cells = room.Objectives.Where(o => rules.CacheObjectives.Contains(o.Objective)).Select(o => o.Cell);
            if (rules.CacheTags.Any(room.Has) && Primary(room) is { } primary) cells = cells.Append(primary);
            foreach ((int X, int Z) cell in cells)
                if (used.Add(cell) && Snap(cellCentre(cell), $"cache at cell {cell}") is { } feet) caches.Add(feet);
        }

        List<Vector3> residents = [];
        foreach (PlannedRoom room in others.Where(room => rules.ResidentTags.Any(room.Has)))
        {
            Vector2 centroid = Centroid(room);
            (int X, int Z) cell = room.Cells.Where(c => !used.Contains(c)).DefaultIfEmpty(room.Cells[0])
                .MinBy(c => Vector2.DistanceSquared(new Vector2(c.X, c.Z), centroid));
            used.Add(cell);
            if (Snap(cellCentre(cell), $"residents at cell {cell}") is { } feet) residents.Add(feet);
        }

        return new LevelPoints(entry, MathF.Atan2(facing.X, facing.Z), arrival, rifts, caches, residents, problems);

        // A point stands on the nearest support the arrival can walk to, within the slack: a spot among a tile's blocked
        // cells moves out to the open floor beside it. One the arrival cannot reach is left out.
        Vector3? Snap(Vector3 point, string what)
        {
            Vector3? standing = navigation.Nearest(point);
            if (standing is null)
            {
                problems.Add($"{what}: no walkable ground nearby");
                return null;
            }
            Vector3? reached = Reachable(standing.Value);
            if (reached is null) problems.Add($"{what}: cannot be walked to from the entry");
            return reached;
        }

        Vector3? Reachable(Vector3 standing)
        {
            NavigationStepResult route = navigation.Route(arrival, standing);
            if (route.Outcome == NavigationPathOutcome.Reached) return standing;
            return route.NearestPresent && Vector3.Distance(route.Nearest, standing) <= rules.ReachSlackMetres ? route.Nearest : null;
        }
    }

    private static (int X, int Z)? Primary(PlannedRoom room) =>
        room.Objectives.Where(o => o.Objective == "primary").Select(o => ((int X, int Z)?)o.Cell).FirstOrDefault()
        ?? (room.Cells.Count > 0 ? room.Cells[0] : null);

    private static Vector2 Centroid(PlannedRoom room) =>
        room.Cells.Aggregate(Vector2.Zero, (sum, c) => sum + new Vector2(c.X, c.Z)) / Math.Max(1, room.Cells.Count);

    private static float Distance(Vector2 a, (int X, int Z) b) => Vector2.Distance(a, new Vector2(b.X, b.Z));
}
