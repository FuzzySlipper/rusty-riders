using System.Text.Json.Serialization;
using Rusty.Engine;
using RustyRiders.Game.Actions;
using RustyRiders.Game.Content;
using RustyRiders.Game.Items;
using RustyRiders.Game.Mechanics;

namespace RustyRiders.Game.Combat;

/// <summary>
/// Everything combat composes from content: the action and item catalogs, the player's starting kit, how hands and
/// hits are presented, the combat HUD text, and the training dummy.
/// </summary>
internal sealed record CombatDefinition(ActionCatalog Actions, ItemCatalog Items, Kit Kit, CombatPresentation Presentation,
    CombatMessages Text, DummyDefinition Dummy)
{
    internal static CombatDefinition Load(IEngineContext engine, MechanicsDefinition mechanics)
    {
        ActionCatalog actions = ActionCatalog.Load(engine, mechanics);
        ItemCatalog items = ItemCatalog.Load(engine, mechanics, actions);
        Kit kit = Authored.Read(engine, Kit.Path, CombatJson.Default.Kit);
        items.Require(Kit.Path, "carried", kit.Carried);
        Authored.Require(kit.Carried.All(id => items.Item(id)!.Weapon is not null), Kit.Path, "carried", "the kit carries weapons only for now.");
        Authored.Require(kit.Carried.Contains(kit.MainHand) && kit.Carried.Contains(kit.OffHand) && kit.MainHand != kit.OffHand,
            Kit.Path, "mainHand", "both hands hold different carried weapons.");
        CombatPresentation presentation = Authored.Read(engine, CombatPresentation.Path, CombatJson.Default.CombatPresentation);
        presentation.Validate();
        CombatMessages text = Authored.Read(engine, CombatMessages.Path, CombatJson.Default.CombatMessages);
        text.Validate();
        DummyDefinition dummy = Authored.Read(engine, DummyDefinition.Path, CombatJson.Default.DummyDefinition);
        mechanics.Validate(DummyDefinition.Path, "stats", dummy.Stats);
        Authored.Point(DummyDefinition.Path, "size", dummy.Size);
        Authored.Colour(DummyDefinition.Path, "color", dummy.Color);
        return new(actions, items, kit, presentation, text, dummy);
    }
}

/// <summary>The player's starting weapons and which two are in hand (content/player/kit.json).</summary>
internal sealed record Kit(string[] Carried, string MainHand, string OffHand)
{
    internal const string Path = "player/kit.json";
}

/// <summary>
/// Hands and hits on screen (content/combat/presentation.json): each hand's rest point in camera space (right, up,
/// back), how far a windup draws it back and a commit drives it, how much it drops in recovery, the size of a projectile,
/// an impact burst's particles, size and life, and how long a hit notice shows (host seconds).
/// </summary>
internal sealed record CombatPresentation(float[] MainHand, float[] OffHand, float[] Windup, float[] Commit, float RecoveryDrop,
    float ProjectileSize, int ImpactBurst, float ImpactSize, float ImpactSeconds, float NoticeSeconds)
{
    internal const string Path = "combat/presentation.json";

    internal void Validate()
    {
        Authored.Point(Path, "mainHand", MainHand);
        Authored.Point(Path, "offHand", OffHand);
        Authored.Point(Path, "windup", Windup);
        Authored.Point(Path, "commit", Commit);
        Authored.AtLeast(Path, "recoveryDrop", RecoveryDrop, 0);
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
