using System.Text.Json.Serialization;
using Rusty.Engine;
using RustyRiders.Game.Actions;
using RustyRiders.Game.Content;
using RustyRiders.Game.Mechanics;

namespace RustyRiders.Game.Items;

/// <summary>What an item is; each kind has its own typed settings on the definition.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ItemKind>))]
internal enum ItemKind { Weapon, Armour, Consumable, Ammo, Supply }

/// <summary>Where a worn item goes.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<WearSlot>))]
internal enum WearSlot { Body, Accessory }

/// <summary>
/// One reusable item (content/items/items.json): its name, its kind and exactly that kind's settings, and the colour
/// it shows as on the ground. A weapon (melee, gun or spell focus) is an item that grants a hand its actions; input
/// names a hand, never a weapon. Armour is worn for stat and resistance contributions; a consumable is carried and used;
/// ammunition refills a track when walked over; a supply is the run's haul, counted by value. <see cref="Icon"/> names
/// its picture in the DOM UI's art (src/ui/art/icons/&lt;icon&gt;.png).
/// </summary>
internal sealed record ItemDefinition(string Id, string Name, ItemKind Kind, float[] Color, string Icon, WeaponDefinition? Weapon = null,
    ArmourDefinition? Armour = null, ConsumableDefinition? Consumable = null, AmmoDefinition? Ammo = null, SupplyDefinition? Supply = null)
{
    /// <summary>Whether walking over it takes it (ammunition and supplies); other items are taken with the use control.</summary>
    internal bool WalkOver => Kind is ItemKind.Ammo or ItemKind.Supply;
}

/// <summary>A worn item's slot and what it adds to the wearer's stats (attributes, derived stats, or <c>resistance.&lt;kind&gt;</c>).</summary>
internal sealed record ArmourDefinition(WearSlot Slot, StatAddition[] Adds);
internal sealed record StatAddition(string Stat, float Amount);

/// <summary>What using a consumable does: restores a track, and/or puts effects on the user. <see cref="Seconds"/> is its world-time cost.</summary>
internal sealed record ConsumableDefinition(float Seconds, TrackRestore? Restore = null, string[]? Effects = null);
internal sealed record TrackRestore(string Track, int Amount);

/// <summary>Ammunition: walking over it restores <see cref="Amount"/> points of a track.</summary>
internal sealed record AmmoDefinition(string Track, int Amount);

/// <summary>A supply: what it is worth to the run's haul.</summary>
internal sealed record SupplyDefinition(int Value);

/// <summary>
/// What a weapon in a hand does: the action it uses, the action that reloads it and its magazine (guns), and how it is
/// drawn in the hand.
/// </summary>
internal sealed record WeaponDefinition(string Action, HeldLook Look, string? Reload = null, MagazineDefinition? Magazine = null);

/// <summary>A magazine of <see cref="Size"/> rounds, refilled from a track (the ammunition carried) by the reload action.</summary>
internal sealed record MagazineDefinition(int Size, string Track);

/// <summary>A held weapon's placeholder: a box of a size and colour, offset in the hand, with its muzzle (or tip) where effects start.</summary>
internal sealed record HeldLook(float[] Size, float[] Color, float[] Offset, float[] Muzzle);

internal sealed record ItemCatalog(ItemDefinition[] Items)
{
    internal const string Path = "items/items.json";

    internal static ItemCatalog Load(IEngineContext engine, MechanicsDefinition mechanics, ActionCatalog actions)
    {
        ItemCatalog catalog = Authored.Read(engine, Path, ItemJson.Default.ItemCatalog);
        string? repeated = catalog.Items.GroupBy(i => i.Id).FirstOrDefault(g => g.Count() > 1)?.Key;
        Authored.Require(repeated is null, Path, "items", $"id '{repeated}' appears more than once.");
        for (int i = 0; i < catalog.Items.Length; i++)
        {
            ItemDefinition item = catalog.Items[i];
            string at = $"items[{i}]";
            Template.Plain(Path, ($"{at}.name", item.Name));
            Authored.Colour(Path, $"{at}.color", item.Color);
            Authored.Require(item.Icon.Length > 0 && item.Icon.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'), Path, $"{at}.icon",
                "names an icon file (letters, digits and dashes).");
            object?[] settings = [item.Weapon, item.Armour, item.Consumable, item.Ammo, item.Supply];
            Authored.Require(settings.Count(s => s is not null) == 1 && settings[(int)item.Kind] is not null, Path, at,
                $"a {item.Kind} sets {item.Kind.ToString().ToLowerInvariant()} and no other kind's settings.");
            ValidateKind(item, at, mechanics);
            if (item.Weapon is not { } weapon) continue;
            actions.Require(Path, $"{at}.weapon.action", weapon.Action);
            if (weapon.Reload is { } reload)
            {
                actions.Require(Path, $"{at}.weapon.reload", reload);
                Authored.Require(actions.Action(reload)!.Reloads, Path, $"{at}.weapon.reload", $"'{reload}' does not reload.");
            }
            Authored.Require((weapon.Reload is null) == (weapon.Magazine is null), Path, $"{at}.weapon", "a magazine and a reload come together.");
            if (weapon.Magazine is { } magazine)
            {
                Authored.Require(magazine.Size >= 1, Path, $"{at}.weapon.magazine.size", "must be at least 1.");
                Authored.Require(mechanics.Tracks.Any(t => t.Id == magazine.Track), Path, $"{at}.weapon.magazine.track", $"unknown track '{magazine.Track}'.");
            }
            Authored.Point(Path, $"{at}.weapon.look.size", weapon.Look.Size);
            Authored.Colour(Path, $"{at}.weapon.look.color", weapon.Look.Color);
            Authored.Point(Path, $"{at}.weapon.look.offset", weapon.Look.Offset);
            Authored.Point(Path, $"{at}.weapon.look.muzzle", weapon.Look.Muzzle);
        }
        return catalog;
    }

    private static void ValidateKind(ItemDefinition item, string at, MechanicsDefinition mechanics)
    {
        bool Track(string id) => mechanics.Tracks.Any(t => t.Id == id);
        if (item.Armour is { } armour)
            for (int a = 0; a < armour.Adds.Length; a++)
            {
                string stat = armour.Adds[a].Stat;
                bool resistance = stat.StartsWith("resistance.", StringComparison.Ordinal) && mechanics.DamageKind(stat["resistance.".Length..]) is not null;
                Authored.Require(resistance || mechanics.HasStat(stat), Path, $"{at}.armour.adds[{a}].stat", $"'{stat}' is not a stat or resistance.");
                Authored.Finite(Path, $"{at}.armour.adds[{a}].amount", armour.Adds[a].Amount);
            }
        if (item.Consumable is { } consumable)
        {
            Authored.AtLeast(Path, $"{at}.consumable.seconds", consumable.Seconds, 0);
            Authored.Require(consumable.Restore is not null || consumable.Effects is { Length: > 0 }, Path, $"{at}.consumable", "restores a track or applies effects.");
            if (consumable.Restore is { } restore)
            {
                Authored.Require(Track(restore.Track), Path, $"{at}.consumable.restore.track", $"unknown track '{restore.Track}'.");
                Authored.Require(restore.Amount > 0, Path, $"{at}.consumable.restore.amount", "must be positive.");
            }
            if (consumable.Effects is { } effects) mechanics.RequireEffects(Path, $"{at}.consumable.effects", effects);
        }
        if (item.Ammo is { } ammo)
        {
            Authored.Require(Track(ammo.Track), Path, $"{at}.ammo.track", $"unknown track '{ammo.Track}'.");
            Authored.Require(ammo.Amount > 0, Path, $"{at}.ammo.amount", "must be positive.");
        }
        if (item.Supply is { } supply) Authored.Require(supply.Value > 0, Path, $"{at}.supply.value", "must be positive.");
    }

    internal ItemDefinition? Item(string id) => Items.FirstOrDefault(i => i.Id == id);

    /// <summary>Checks item ids authored elsewhere, naming the file and field of an unknown one.</summary>
    internal void Require(string path, string field, params string[] ids)
    {
        for (int i = 0; i < ids.Length; i++)
            Authored.Require(Item(ids[i]) is not null, path, $"{field}[{i}]", $"unknown item '{ids[i]}'; see content/{Path}.");
    }
}

// Authored: missing constructor values, nulls in non-nullable fields and unknown members are errors, not defaults.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(ItemCatalog))]
internal sealed partial class ItemJson : JsonSerializerContext;
