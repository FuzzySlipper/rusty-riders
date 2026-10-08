using Rusty.Engine.Mechanics;
using RustyRiders.Game.Combat;
using RustyRiders.Game.Mechanics;

namespace RustyRiders.Game.Items;

/// <summary>
/// The player's carried things, the one owner for them across a run's levels: weapons (which the hands hold), worn
/// armour (a body piece and two accessories, whose additions are stat sources), carried consumables and spare armour
/// (stacks, each kind taking one of the kit's slots), and the run's haul of supplies. Taking ammunition refills its
/// track and taking a supply adds to the haul at once. Starts from the kit; a fall empties it back to the kit.
/// </summary>
internal sealed class Inventory
{
    private const int Accessories = 2;
    private readonly ItemCatalog items;
    private readonly Kit kit;
    private readonly ActorStats stats;
    private readonly List<ItemDefinition> weapons = [];
    private readonly List<(ItemDefinition Item, int Count)> stacks = [];
    private ItemDefinition? body;
    private readonly ItemDefinition?[] accessories = new ItemDefinition?[Accessories];

    internal Inventory(ItemCatalog items, Kit kit, ActorStats stats)
    {
        this.items = items;
        this.kit = kit;
        this.stats = stats;
        Reset();
    }

    internal IReadOnlyList<ItemDefinition> Weapons => weapons;
    internal IReadOnlyList<(ItemDefinition Item, int Count)> Stacks => stacks;
    internal IEnumerable<ItemDefinition> Worn => [.. (body is null ? [] : new[] { body }), .. accessories.OfType<ItemDefinition>()];
    internal int Haul { get; private set; }
    /// <summary>Slots in use: one per carried weapon, per consumable stack and per spare armour stack.</summary>
    internal int SlotsUsed => weapons.Count + stacks.Count;
    internal int Slots => kit.Slots;
    /// <summary>The consumable the use-item control uses: the first carried.</summary>
    internal (ItemDefinition Item, int Count)? Ready => stacks.Where(s => s.Item.Kind == ItemKind.Consumable).Select(s => ((ItemDefinition, int)?)s).FirstOrDefault();

    /// <summary>The kit as it starts, nothing worn and no haul.</summary>
    internal void Reset()
    {
        weapons.Clear();
        stacks.Clear();
        body = null;
        Array.Clear(accessories);
        Haul = 0;
        foreach (string id in kit.Carried) weapons.Add(items.Item(id)!);
        foreach (KitStack stack in kit.Items) Take(items.Item(stack.Item)!, stack.Count);
        stats.SetEquipmentSources(Sources());
    }

    /// <summary>
    /// Takes <paramref name="count"/> of an item. Returns false, taking nothing, when it needs a slot and none is free
    /// (a weapon already carried is not taken twice).
    /// </summary>
    internal bool Take(ItemDefinition item, int count)
    {
        switch (item.Kind)
        {
            case ItemKind.Ammo:
                stats.Track(item.Ammo!.Track).Restore(item.Ammo.Amount * count);
                return true;
            case ItemKind.Supply:
                Haul += item.Supply!.Value * count;
                return true;
            case ItemKind.Weapon:
                if (weapons.Contains(item) || SlotsUsed >= Slots) return false;
                weapons.Add(item);
                return true;
            case ItemKind.Armour when Wear(item):
                stats.SetEquipmentSources(Sources());
                if (count == 1) return true;
                count--;
                break;
        }
        int at = stacks.FindIndex(s => s.Item == item);
        if (at >= 0) stacks[at] = (item, stacks[at].Count + count);
        else if (SlotsUsed < Slots) stacks.Add((item, count));
        else return false;
        return true;
    }

    /// <summary>Uses one of the ready consumable: restores its track and applies its effects. Returns it, or null when none is carried.</summary>
    internal ItemDefinition? Use(MechanicsDefinition mechanics) => Ready is { } ready ? UseStack(mechanics, ready) : null;

    /// <summary>Uses one of the consumable stack at <paramref name="index"/>; null when that is no consumable.</summary>
    internal ItemDefinition? UseAt(MechanicsDefinition mechanics, int index) =>
        index >= 0 && index < stacks.Count && stacks[index].Item.Kind == ItemKind.Consumable ? UseStack(mechanics, stacks[index]) : null;

    /// <summary>
    /// Puts on the spare armour at stack <paramref name="index"/>, in its slot: what was worn there goes back to the
    /// stacks (an accessory replaces the first accessory when both are worn). Returns false when that is no armour.
    /// </summary>
    internal bool Wear(int index)
    {
        if (index < 0 || index >= stacks.Count || stacks[index].Item.Armour is not { } armour) return false;
        ItemDefinition item = stacks[index].Item;
        ItemDefinition? removed;
        if (armour.Slot == WearSlot.Body)
        {
            removed = body;
            body = item;
        }
        else
        {
            if (accessories.Contains(item)) return false;
            int slot = Array.IndexOf(accessories, null);
            if (slot < 0) slot = 0;
            removed = accessories[slot];
            accessories[slot] = item;
        }
        Remove(index);
        if (removed is not null) Stack(removed, 1);
        stats.SetEquipmentSources(Sources());
        return true;
    }

    /// <summary>Takes off worn piece <paramref name="index"/> (in <see cref="Worn"/> order) into the stacks, if a slot is free.</summary>
    internal bool TakeOff(int index)
    {
        ItemDefinition? item = Worn.ElementAtOrDefault(index);
        if (item is null || (SlotsUsed >= Slots && stacks.All(s => s.Item != item))) return false;
        if (item == body) body = null;
        else accessories[Array.IndexOf(accessories, item)] = null;
        Stack(item, 1);
        stats.SetEquipmentSources(Sources());
        return true;
    }

    private ItemDefinition UseStack(MechanicsDefinition mechanics, (ItemDefinition Item, int Count) ready)
    {
        ConsumableDefinition use = ready.Item.Consumable!;
        if (use.Restore is { } restore) stats.Track(restore.Track).Restore(restore.Amount);
        foreach (string effect in use.Effects ?? []) stats.Effects.Apply(mechanics.Effect(effect)!, $"item.{ready.Item.Id}");
        Remove(stacks.FindIndex(s => s.Item == ready.Item));
        return ready.Item;
    }

    // One fewer of stack index; the last leaves its slot.
    private void Remove(int index)
    {
        (ItemDefinition item, int count) = stacks[index];
        if (count > 1) stacks[index] = (item, count - 1);
        else stacks.RemoveAt(index);
    }

    private void Stack(ItemDefinition item, int count)
    {
        int at = stacks.FindIndex(s => s.Item == item);
        if (at >= 0) stacks[at] = (item, stacks[at].Count + count);
        else stacks.Add((item, count));
    }

    /// <summary>The Engine stat sources the worn armour holds, one per piece and slot.</summary>
    internal StatSource[] Sources() => Worn.Select((item, index) => new StatSource(
        new IntrinsicSourceIdentity(null, SourceInstanceId.Parse($"worn.{index}.{item.Id}")), SourceDefinitionId.Parse($"item.{item.Id}"), 0,
        item.Armour!.Adds.Select(add => new StatContributionDefinition(StatId.Parse(add.Stat),
            StackingGroupId.Parse($"worn.{index}.{add.Stat}"), MechanicsStackingPolicy.Sum, new StatContribution.Add(add.Amount))).ToArray())).ToArray();

    // Puts armour on in its slot when the slot is free.
    private bool Wear(ItemDefinition item)
    {
        if (item.Armour!.Slot == WearSlot.Body)
        {
            if (body is not null) return false;
            body = item;
            return true;
        }
        int free = Array.IndexOf(accessories, null);
        if (free < 0 || accessories.Contains(item)) return false;
        accessories[free] = item;
        return true;
    }
}
