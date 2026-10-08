using RustyRiders.Game.Combat;
using RustyRiders.Game.Items;

namespace RustyRiders.Game.Ui;

/// <summary>
/// The inventory screen's facts: carried weapons (and which hand holds each), worn armour, item stacks with what
/// each can do, slots used and the haul. Indices are what the screen's intents name.
/// </summary>
internal static class InventoryFacts
{
    internal static uint Write(UiValueWriter w, Inventory inventory, PlayerCombat combat)
    {
        uint[] weapons = inventory.Weapons.Select((item, index) => w.Object("", w.Number("index", index), w.Text("name", item.Name),
            w.Text("icon", item.Icon), w.Number("hand", combat.HandOf(item)))).ToArray();
        uint[] worn = inventory.Worn.Select((item, index) => w.Object("", w.Number("index", index), w.Text("name", item.Name),
            w.Text("icon", item.Icon), w.Text("slot", item.Armour!.Slot.ToString()), w.Text("adds", Adds(item)))).ToArray();
        uint[] stacks = inventory.Stacks.Select((stack, index) => w.Object("", w.Number("index", index), w.Text("name", stack.Item.Name),
            w.Text("icon", stack.Item.Icon), w.Number("count", stack.Count), w.Flag("usable", stack.Item.Kind == ItemKind.Consumable),
            w.Flag("wearable", stack.Item.Kind == ItemKind.Armour), w.Text("adds", stack.Item.Armour is null ? "" : Adds(stack.Item)))).ToArray();
        return w.Object("inventory", w.Array("weapons", weapons), w.Array("worn", worn), w.Array("stacks", stacks),
            w.Number("used", inventory.SlotsUsed), w.Number("slots", inventory.Slots), w.Number("haul", inventory.Haul));
    }

    // What a piece of armour adds, as numbers beside stat ids (the screen shows them as written).
    private static string Adds(ItemDefinition item) => string.Join(", ", item.Armour!.Adds.Select(a =>
        FormattableString.Invariant($"{(a.Amount >= 0 ? "+" : "")}{a.Amount:0.##} {a.Stat.Replace("resistance.", "")}")));
}
