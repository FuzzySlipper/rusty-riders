using System.Text.Json.Serialization;
using Rusty.Engine;
using RustyRiders.Game.Actions;
using RustyRiders.Game.Content;
using RustyRiders.Game.Held;
using RustyRiders.Game.Items;
using RustyRiders.Game.Mechanics;

namespace RustyRiders.Game.Combat;

/// <summary>
/// Everything combat composes from content: the action and item catalogs, the player's starting kit, how held weapons
/// look and move (<see cref="HeldCatalog"/>), how hits are presented, the combat HUD text, and the training dummy.
/// </summary>
internal sealed record CombatDefinition(ActionCatalog Actions, ItemCatalog Items, Kit Kit, HeldCatalog Held, CombatPresentation Presentation,
    CombatMessages Text, DummyDefinition Dummy)
{
    internal static CombatDefinition Load(IEngineContext engine, MechanicsDefinition mechanics)
    {
        ActionCatalog actions = ActionCatalog.Load(engine, mechanics);
        ItemCatalog items = ItemCatalog.Load(engine, mechanics, actions);
        HeldCatalog held = HeldCatalog.Load(engine);
        ValidateHeld(held, items, actions);
        Kit kit = Authored.Read(engine, Kit.Path, CombatJson.Default.Kit);
        items.Require(Kit.Path, "carried", kit.Carried);
        Authored.Require(kit.Carried.All(id => items.Item(id)!.Weapon is not null), Kit.Path, "carried", "carried lists weapons; other starting items go in items.");
        Authored.Require(kit.Carried.Contains(kit.MainHand) && kit.Carried.Contains(kit.OffHand) && kit.MainHand != kit.OffHand,
            Kit.Path, "mainHand", "both hands hold different carried weapons.");
        items.Require(Kit.Path, "items", kit.Items.Select(s => s.Item).ToArray());
        Authored.Require(kit.Items.All(s => s.Count >= 1), Kit.Path, "items", "counts are at least 1.");
        Authored.Require(kit.Slots >= kit.Carried.Length + kit.Items.Length, Kit.Path, "slots", "must hold the kit's weapons and items.");
        CombatPresentation presentation = Authored.Read(engine, CombatPresentation.Path, CombatJson.Default.CombatPresentation);
        presentation.Validate();
        CombatMessages text = Authored.Read(engine, CombatMessages.Path, CombatJson.Default.CombatMessages);
        text.Validate();
        DummyDefinition dummy = Authored.Read(engine, DummyDefinition.Path, CombatJson.Default.DummyDefinition);
        mechanics.Validate(DummyDefinition.Path, "stats", dummy.Stats);
        Authored.Point(DummyDefinition.Path, "size", dummy.Size);
        Authored.Colour(DummyDefinition.Path, "color", dummy.Color);
        return new(actions, items, kit, held, presentation, text, dummy);
    }

    /// <summary>Every weapon names a held look whose motion has keys, in time order, for each action the weapon uses.</summary>
    internal static void ValidateHeld(HeldCatalog held, ItemCatalog items, ActionCatalog actions)
    {
        List<(string, HeldLookDefinition, ActionDefinition)> uses = [];
        for (int i = 0; i < items.Items.Length; i++)
        {
            if (items.Items[i].Weapon is not { } weapon) continue;
            string at = $"items[{i}].weapon";
            held.Require(ItemCatalog.Path, $"{at}.look", weapon.Look);
            HeldLookDefinition look = held.Look(weapon.Look)!;
            uses.Add(($"{at}.action", look, actions.Action(weapon.Action)!));
            if (weapon.Reload is { } reload) uses.Add(($"{at}.reload", look, actions.Action(reload)!));
        }
        held.Motion.Validate(ItemCatalog.Path, uses);
    }
}

/// <summary>
/// The player's starting weapons and which two are in hand, the other items they start with, and how many slots they can
/// carry (content/player/kit.json).
/// </summary>
internal sealed record Kit(string[] Carried, string MainHand, string OffHand, KitStack[] Items, int Slots)
{
    internal const string Path = "player/kit.json";
}

internal sealed record KitStack(string Item, int Count);

/// <summary>
/// Hits on screen (content/combat/presentation.json): the size of a projectile, an impact burst's particles, size and
/// life, and how long a hit notice shows (host seconds). Held weapons are content/held/.
/// </summary>
internal sealed record CombatPresentation(float ProjectileSize, int ImpactBurst, float ImpactSize, float ImpactSeconds, float NoticeSeconds)
{
    internal const string Path = "combat/presentation.json";

    internal void Validate()
    {
        Authored.Positive(Path, "projectileSize", ProjectileSize);
        Authored.Require(ImpactBurst is >= 1 and <= 256, Path, "impactBurst", "must be 1 to 256 particles.");
        Authored.Positive(Path, "impactSize", ImpactSize);
        Authored.Positive(Path, "impactSeconds", ImpactSeconds);
        Authored.Positive(Path, "noticeSeconds", NoticeSeconds);
    }
}

/// <summary>The combat HUD's text (content/combat/messages.json), as templates.</summary>
internal sealed record CombatMessages(string Hit, string Defeated, string TurnedAside, string Missed, string Empty, string Short,
    string Loaded, string Carried)
{
    internal const string Path = "combat/messages.json";

    internal void Validate()
    {
        Template.Check(Path, "hit", Hit, "target", "damage");
        Template.Check(Path, "defeated", Defeated, "target");
        Template.Check(Path, "turnedAside", TurnedAside, "target");
        Template.Check(Path, "missed", Missed);
        Template.Check(Path, "empty", Empty);
        Template.Check(Path, "short", Short, "track");
        Template.Check(Path, "loaded", Loaded, "loaded", "size");
        Template.Check(Path, "carried", Carried, "name", "value");
    }
}

/// <summary>A training dummy: a still target with stats, drawn as a box (content/combat/dummy.json).</summary>
internal sealed record DummyDefinition(string Name, float[] Size, float[] Color, ActorStatBlock Stats)
{
    internal const string Path = "combat/dummy.json";
}

// Authored: missing constructor values, nulls in non-nullable fields and unknown members are errors, not defaults.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(Kit))]
[JsonSerializable(typeof(CombatPresentation))]
[JsonSerializable(typeof(CombatMessages))]
[JsonSerializable(typeof(DummyDefinition))]
internal sealed partial class CombatJson : JsonSerializerContext;
