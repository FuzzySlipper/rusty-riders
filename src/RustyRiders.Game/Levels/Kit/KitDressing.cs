using System.Numerics;
using Rusty.Engine;

namespace RustyRiders.Game.Levels.Kit;

/// <summary>One placed prop: its converted model, where it stands (glTF space), and its collision box (null when it does not collide).</summary>
internal sealed record DressingPlacement(string Model, Transform Pose, Vector3? Box);

/// <summary>
/// The old game's props placed over the room plan by the kit's typed <see cref="DressingRule"/>s, so they stand where
/// the new rooms are, not where the old tile prefabs put them. Choices (which model, which places are dressed, rubble's
/// scatter) come from the level's seed, so a level always dresses the same way.
/// </summary>
internal static class KitDressing
{
    private const float WallGap = .02f; // keeps a prop's back off the wall face

    internal static List<DressingPlacement> Place(RoomPlan plan, KitRecipe recipe, int seed)
    {
        List<DressingPlacement> placed = [];
        if (recipe.Dressing is not { } dressing) return placed;
        for (int i = 0; i < dressing.Length; i++)
        {
            DressingDefinition part = dressing[i];
            Random random = new(unchecked(seed * 7919 + i * 104729)); // fixed mix: HashCode.Combine differs per process
            switch (part.Rule)
            {
                case DressingRule.HallColumns: HallColumns(plan, part, random, placed); break;
                case DressingRule.RoomCorners: RoomCorners(plan, part, random, placed); break;
                case DressingRule.WallNiches: WallNiches(plan, recipe, part, random, placed); break;
                default: Rubble(plan, recipe, part, random, placed); break;
            }
        }
        return placed;
    }

    /// <summary>A column at each level-cell corner whose four cells are all one room, where the room's family has columns (any room without families).</summary>
    private static void HallColumns(RoomPlan plan, DressingDefinition part, Random random, List<DressingPlacement> placed)
    {
        foreach (((int x, int z), int node) in plan.RoomOf)
        {
            bool Same(int dx, int dz) => plan.RoomOf.TryGetValue((x + dx, z + dz), out int other) && other == node;
            if (!(Same(1, 0) && Same(0, 1) && Same(1, 1)) || random.NextDouble() >= part.Chance) continue;
            if (plan.Family(node) is { Columns: false }) continue;
            Vector2 corner = new((x + .5f) * plan.CellSize, (z + .5f) * plan.CellSize);
            Add(part, random, placed, corner, Vector2.UnitY);
        }
    }

    /// <summary>A pier in each corner of a room's outline that turns into the room, set against both walls.</summary>
    private static void RoomCorners(RoomPlan plan, DressingDefinition part, Random random, List<DressingPlacement> placed)
    {
        foreach (List<(Vector2 Point, float Height)> loop in OpenGround.Walls(plan.Heights([PlanArea.Room])))
        {
            for (int i = 0; i < loop.Count; i++)
            {
                Vector2 before = plan.Metres(loop[(i + loop.Count - 1) % loop.Count].Point), corner = plan.Metres(loop[i].Point),
                    after = plan.Metres(loop[(i + 1) % loop.Count].Point);
                Vector2 inA = Inward(before, corner), inB = Inward(corner, after);
                // Walked with the room on the left, a corner of the room turns left: the second run's inward is the first's direction.
                // Square corners only: a diagonal cut's shallow turns take no pier.
                if (Vector2.Dot(inB, Vector2.Normalize(corner - before)) > -.95f || random.NextDouble() >= part.Chance) continue;
                Vector2 at = corner + (inA + inB) * (part.Footprint / 2 + WallGap);
                if (Opening(plan, at, part.Footprint)) continue;
                Add(part, random, placed, at, inA); // square to the walls, so it sits in the corner
            }
        }
    }

    /// <summary>Props against room walls at each place midway between pilaster places, where nothing is open behind.</summary>
    private static void WallNiches(RoomPlan plan, KitRecipe recipe, DressingDefinition part, Random random, List<DressingPlacement> placed)
    {
        float clearance = recipe.Pilasters?.Clearance ?? part.Spacing / 2;
        foreach ((Vector2 a, Vector2 b, Vector2 inward) in Runs(plan, [PlanArea.Room]))
        {
            float length = Vector2.Distance(a, b), span = length - 2 * clearance;
            if (span < part.Spacing) continue;
            int places = (int)MathF.Floor(span / part.Spacing);
            float start = clearance + (span - (places - 1) * part.Spacing) / 2 - part.Spacing / 2;
            Vector2 along = Vector2.Normalize(b - a);
            for (int k = 0; k < places; k++)
            {
                float t = start + (k + .5f) * part.Spacing;
                if (t < clearance || t > length - clearance || random.NextDouble() >= part.Chance) continue;
                Vector2 wall = a + along * t;
                if (Behind(plan, wall, along, inward, part.Footprint + 2 * (recipe.DoorArch?.Ring?.Width ?? 0))) continue;
                Add(part, random, placed, wall + inward * (part.Depth / 2 + WallGap), inward); // in the wall's face, above any ledge by its lift
            }
        }
    }

    /// <summary>Clusters of up to <see cref="DressingDefinition.Count"/> props at the foot of room and corridor walls, each turned at random.</summary>
    private static void Rubble(RoomPlan plan, KitRecipe recipe, DressingDefinition part, Random random, List<DressingPlacement> placed)
    {
        foreach ((Vector2 a, Vector2 b, Vector2 inward) in Runs(plan, [PlanArea.Room, PlanArea.Corridor]))
        {
            float length = Vector2.Distance(a, b);
            if (length < 2 || random.NextDouble() >= part.Chance) continue;
            Vector2 along = Vector2.Normalize(b - a), heap = a + along * (float)(1 + random.NextDouble() * (length - 2));
            int count = 1 + random.Next(part.Count);
            for (int k = 0; k < count; k++)
            {
                Vector2 at = heap + along * (float)((random.NextDouble() - .5) * 2)
                    + inward * (Ledge(recipe) + part.Footprint / 2 + (float)(random.NextDouble() * part.Footprint * 2));
                float angle = (float)(random.NextDouble() * MathF.Tau);
                Add(part, random, placed, at, new Vector2(MathF.Cos(angle), MathF.Sin(angle)));
            }
        }
    }

    /// <summary>A prop facing <paramref name="front"/> (Unity metres), on the floor or lifted, with its box when it collides.</summary>
    private static void Add(DressingDefinition part, Random random, List<DressingPlacement> placed, Vector2 at, Vector2 front)
    {
        Vector3 facing = OpenGround.Gltf(front, 0);
        float yaw = MathF.Atan2(facing.X, facing.Z) + part.Turn * MathF.PI / 180;
        Transform pose = new(OpenGround.Gltf(at, part.Lift), Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw), Vector3.One);
        placed.Add(new DressingPlacement(part.Models[random.Next(part.Models.Length)], pose,
            part.Collides ? new Vector3(part.Footprint, part.Height, part.Footprint) : null));
    }

    /// <summary>Each straight run of the given areas' outlines, in Unity metres, with its inward (open-side) normal.</summary>
    private static IEnumerable<(Vector2 A, Vector2 B, Vector2 Inward)> Runs(RoomPlan plan, PlanArea[] areas)
    {
        // A run's area is told by its height: only runs of the named areas, on the outline of all the open ground.
        Dictionary<(int X, int Z), float> ground = plan.Heights([PlanArea.Room, PlanArea.Corridor, PlanArea.Doorway]);
        foreach (List<(Vector2 Point, float Height)> loop in OpenGround.Walls(ground))
            for (int i = 0; i < loop.Count; i++)
            {
                if (!areas.Contains(plan.Rules.Area(loop[i].Height))) continue;
                Vector2 a = plan.Metres(loop[i].Point), b = plan.Metres(loop[(i + 1) % loop.Count].Point);
                yield return (a, b, Inward(a, b));
            }
    }

    /// <summary>How far a floor trim that collides (a ledge) stands out from the walls.</summary>
    private static float Ledge(KitRecipe recipe) => recipe.Trims.Where(t => t.Anchor == TrimAnchor.Floor && t.Collides)
        .Select(t => t.Profile.Max(p => p[0])).DefaultIfEmpty(0).Max();

    /// <summary>Whether a doorway or other opening lies behind a wall within <paramref name="width"/> of a point on it.</summary>
    private static bool Behind(RoomPlan plan, Vector2 wall, Vector2 along, Vector2 inward, float width)
    {
        float step = plan.Rules.GridMetres;
        for (float s = -width / 2; s <= width / 2; s += step)
            if (plan.Open.ContainsKey(plan.Square(wall + along * s - inward * step / 2))) return true;
        return false;
    }

    /// <summary>Whether a doorway lies within a footprint's reach of a point.</summary>
    private static bool Opening(RoomPlan plan, Vector2 at, float footprint)
    {
        float reach = footprint / 2 + plan.Rules.WallMetres + (plan.Rules.DoorWidth / 2);
        return plan.Doorways.Any(d => Vector2.Distance(d.Centre, at) < reach);
    }

    private static Vector2 Inward(Vector2 from, Vector2 to)
    {
        Vector2 along = Vector2.Normalize(to - from);
        return new Vector2(-along.Y, along.X);
    }
}
