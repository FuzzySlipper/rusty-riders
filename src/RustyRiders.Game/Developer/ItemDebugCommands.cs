using System.Text;
using Rusty.Engine.Debugging;
using RustyRiders.Game.Items;

namespace RustyRiders.Game.Developer;

/// <summary>Live-debug commands for items: read the inventory and what lies about, give items, and sample loot tables.</summary>
internal sealed class ItemDebugCommands(Inventory inventory, Pickups pickups, LootTables loot, ItemCatalog items, Player.Walker walker) : IDebugCommandModule
{
    [DebugCommand("riders.player.inventory", Description = "Read the carried weapons, worn armour, item stacks, slots used and the run's haul.")]
    public DebugCommandResult Inventory() => DebugCommandResult.Success(string.Join("\n",
        $"weapons: {string.Join(", ", inventory.Weapons.Select(w => w.Name))}",
        $"worn: {string.Join(", ", inventory.Worn.Select(w => w.Name))}",
        $"stacks: {string.Join(", ", inventory.Stacks.Select(s => $"{s.Item.Name} ×{s.Count}"))}",
        $"slots: {inventory.SlotsUsed}/{inventory.Slots}  haul: {inventory.Haul}"));

    [DebugCommand("riders.items.inspect", Description = "Read the current level's caches and the items lying about.")]
    public DebugCommandResult Inspect() => DebugCommandResult.Success(string.Join("\n", pickups.Describe()) is { Length: > 0 } text ? text : "Nothing.");

    [DebugCommand("riders.dev.give", Description = "Developer override: give the player <count> of item <id>, as if picked up.")]
    public DebugCommandResult Give(string id, int count)
    {
        if (count < 1) return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, "Count must be at least 1.");
        if (items.Item(id) is not { } item) return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, $"Unknown item '{id}'.");
        return inventory.Take(item, count) ? DebugCommandResult.Success($"Took {item.Name} ×{count}.")
            : DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, "No room.");
    }

    [DebugCommand("riders.dev.drop", Description = "Developer override: lay <count> of item <id> on the ground <metres> in front of the player.")]
    public DebugCommandResult Drop(string id, int count, float metres)
    {
        if (items.Item(id) is not { } item || count < 1 || !float.IsFinite(metres))
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, "Name a known item, a count of at least 1 and a distance.");
        System.Numerics.Vector3 ahead = walker.Forward with { Y = 0 };
        pickups.Place(item, count, walker.Feet + System.Numerics.Vector3.Normalize(ahead) * metres);
        return DebugCommandResult.Success($"{item.Name} ×{count} lies {metres} m ahead.");
    }

    [DebugCommand("riders.loot.sample", Description = "Roll a world's cache table <times> times from seed 1 at a reward multiplier and list how often each item comes up.")]
    public DebugCommandResult Sample(string tileset, int times, float multiplier)
    {
        if (times is < 1 or > 100_000 || !float.IsFinite(multiplier) || multiplier <= 0)
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, "Times is 1 to 100000; the multiplier is positive.");
        LootTable table = loot.Cache(tileset);
        Random random = new(1);
        Dictionary<string, (int Rolls, int Count)> seen = [];
        for (int i = 0; i < times; i++)
            foreach ((string item, int count) in loot.Roll(table, random, multiplier))
                seen[item] = (seen.GetValueOrDefault(item).Rolls + 1, seen.GetValueOrDefault(item).Count + count);
        StringBuilder text = new($"{table.Id} × {times} at ×{multiplier}:");
        foreach (var (item, (rolls, count)) in seen.OrderByDescending(s => s.Value.Rolls))
            text.Append(FormattableString.Invariant($"\n{item}: in {100.0 * rolls / times:0.#}% of caches, {count / (double)times:0.##} per cache"));
        return DebugCommandResult.Success(text.ToString());
    }
}
