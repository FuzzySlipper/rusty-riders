using System.Text.Json.Serialization;
using Rusty.Engine;
using RustyRiders.Game.Actions;
using RustyRiders.Game.Content;
using RustyRiders.Game.Mechanics;

namespace RustyRiders.Game.Items;

/// <summary>What an item is; each kind has its own typed settings on the definition.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ItemKind>))]
internal enum ItemKind { Weapon }

/// <summary>
/// One reusable item (content/items/items.json): its name, its kind and exactly that kind's settings. A weapon (melee,
/// gun or spell focus) is an item that grants a hand its actions; input names a hand, never a weapon.
/// </summary>
internal sealed record ItemDefinition(string Id, string Name, ItemKind Kind, WeaponDefinition? Weapon = null);

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
            Authored.Require(item.Kind != ItemKind.Weapon || item.Weapon is not null, Path, at, "a weapon sets weapon.");
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
