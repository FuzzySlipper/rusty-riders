using RustyRiders.Game.Levels.Kit;

namespace RustyRiders.Game.Levels;

/// <summary>
/// What a level is before the Engine holds any of it: its stamped plan, room plan, and for a kit build its dressing and
/// geometry. Made from authored data alone, so it can be made off the update (<see cref="LevelBuild"/>); a
/// <see cref="LevelScene"/> is then made from it on the update.
/// </summary>
internal sealed record LevelBlueprint(LevelPlan Plan, RoomPlan Rooms, KitRecipe? Kit, IReadOnlyList<DressingPlacement> Dressing, KitGeometry? Geometry)
{
    /// <param name="kit">The world's kit recipe for a kit build (loaded on the update), or null.</param>
    internal static LevelBlueprint Make(LevelData data, LayoutDefinition layout, int seed, LevelRules rules, KitRecipe? kit)
    {
        float cellSize = data.Generator.CellSize;
        LevelPlan plan = LevelGenerator.Generate(data, layout, seed);
        RoomPlan rooms = new(plan, rules.Plan, cellSize, kit?.Rooms, seed);
        if (kit is null) return new LevelBlueprint(plan, rooms, null, [], null);
        List<DressingPlacement> dressing = KitDressing.Place(rooms, kit, seed);
        return new LevelBlueprint(plan, rooms, kit, dressing, new KitGeometry(rooms, kit, cellSize, dressing, seed));
    }
}
