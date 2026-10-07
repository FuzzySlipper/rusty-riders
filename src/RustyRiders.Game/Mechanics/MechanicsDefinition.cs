using System.Text.Json.Serialization;
using RustyRiders.Game.Content;
using Rusty.Engine;

namespace RustyRiders.Game.Mechanics;

/// <summary>
/// The game's stat vocabulary: the attributes every actor has, the stats derived from them, the resource tracks those
/// stats bound, the kinds of damage with the resistance each actor holds against it, and the effects any actor can
/// bear. One vocabulary for the player and every enemy; an actor's own numbers are its <see cref="ActorStatBlock"/>.
/// </summary>
internal sealed record MechanicsDefinition(AttributeDefinition[] Attributes, DerivedStatDefinition[] Derived, TrackDefinition[] Tracks,
    DamageKindDefinition[] DamageKinds, EffectDefinition[] Effects, MechanicsMessages Text)
{
    internal const string StatsPath = "mechanics/stats.json";
    internal const string DamagePath = "mechanics/damage.json";

    internal static MechanicsDefinition Load(IEngineContext engine)
    {
        StatVocabulary stats = Authored.Read(engine, StatsPath, MechanicsJson.Default.StatVocabulary);
        DamageVocabulary damage = Authored.Read(engine, DamagePath, MechanicsJson.Default.DamageVocabulary);
        EffectCatalog effects = Authored.Read(engine, EffectDefinition.Path, MechanicsJson.Default.EffectCatalog);
        MechanicsMessages text = Authored.Read(engine, MechanicsMessages.Path, MechanicsJson.Default.MechanicsMessages);
        text.Validate();
        MechanicsDefinition definition = new(stats.Attributes, stats.Derived, stats.Tracks, damage.Kinds, effects.Effects, text);
        definition.Validate();
        return definition;
    }

    internal DamageKindDefinition? DamageKind(string id) => DamageKinds.FirstOrDefault(k => k.Id == id);
    internal EffectDefinition? Effect(string id) => Effects.FirstOrDefault(e => e.Id == id);
    internal bool HasStat(string id) => Attributes.Any(a => a.Id == id) || Derived.Any(d => d.Id == id);

    /// <summary>Checks a list of effect ids authored elsewhere, naming the file and field of an unknown one.</summary>
    internal void RequireEffects(string path, string field, string[] ids)
    {
        for (int i = 0; i < ids.Length; i++)
            Authored.Require(Effect(ids[i]) is not null, path, $"{field}[{i}]", $"unknown effect '{ids[i]}'; see content/{EffectDefinition.Path}.");
    }

    private void Validate()
    {
        Unique(StatsPath, "attributes", Attributes.Select(a => a.Id));
        Unique(StatsPath, "derived", Derived.Select(d => d.Id));
        Unique(StatsPath, "tracks", Tracks.Select(t => t.Id));
        Unique(DamagePath, "kinds", DamageKinds.Select(k => k.Id));
        for (int i = 0; i < Attributes.Length; i++)
            Authored.Require(Attributes[i].Maximum >= Attributes[i].Minimum, StatsPath, $"attributes[{i}].maximum", "must be at least the minimum.");
        for (int i = 0; i < Derived.Length; i++)
        {
            DerivedStatDefinition d = Derived[i];
            Authored.Require(d.Maximum >= d.Minimum, StatsPath, $"derived[{i}].maximum", "must be at least the minimum.");
            Authored.Within(StatsPath, $"derived[{i}].base", d.Base, d.Minimum, d.Maximum);
            Authored.AtLeast(StatsPath, $"derived[{i}].quantum", d.Quantum, 0);
            for (int f = 0; f < d.From.Length; f++)
            {
                Authored.Require(Attributes.Any(a => a.Id == d.From[f].Attribute), StatsPath, $"derived[{i}].from[{f}].attribute",
                    $"unknown attribute '{d.From[f].Attribute}'.");
                Authored.Finite(StatsPath, $"derived[{i}].from[{f}].perPoint", d.From[f].PerPoint);
            }
        }
        for (int i = 0; i < Tracks.Length; i++)
        {
            Authored.Require(Derived.Any(d => d.Id == Tracks[i].Maximum), StatsPath, $"tracks[{i}].maximum",
                $"names '{Tracks[i].Maximum}', which is not a derived stat.");
            Authored.AtLeast(StatsPath, $"tracks[{i}].regeneration", Tracks[i].Regeneration, 0);
        }
        for (int i = 0; i < DamageKinds.Length; i++)
        {
            DamageKindDefinition k = DamageKinds[i];
            Authored.Within(DamagePath, $"kinds[{i}].minimumResistance", k.MinimumResistance, -10, 0);
            Authored.Within(DamagePath, $"kinds[{i}].maximumResistance", k.MaximumResistance, 0, 1);
        }
        Authored.Require(Derived.Any(d => d.Id == ActorStats.PaceStat), StatsPath, "derived",
            $"needs the '{ActorStats.PaceStat}' stat that movement is scaled by.");
        Authored.Require(Derived.Any(d => d.Id == ActorStats.ActionTimeStat), StatsPath, "derived",
            $"needs the '{ActorStats.ActionTimeStat}' stat that action phases are scaled by.");
        Unique(EffectDefinition.Path, "effects", Effects.Select(e => e.Id));
        for (int i = 0; i < Effects.Length; i++) Effects[i].Validate($"effects[{i}]", this, ActorStats.PaceStat);
        // One group, one stacking rule: the Engine compares a new application with the group's entries by it.
        foreach (var group in Effects.GroupBy(e => e.Group))
            Authored.Require(group.Select(e => (e.Stacking, e.MaximumInstances)).Distinct().Count() == 1, EffectDefinition.Path, "effects",
                $"group '{group.Key}' mixes stacking rules.");
    }

    /// <summary>Checks an actor's stat block against the vocabulary, naming the file and field that is wrong.</summary>
    internal void Validate(string path, string field, ActorStatBlock block)
    {
        foreach (AttributeDefinition a in Attributes)
        {
            Authored.Require(block.Attributes.TryGetValue(a.Id, out float value), path, $"{field}.attributes.{a.Id}", "is missing.");
            Authored.Within(path, $"{field}.attributes.{a.Id}", value, a.Minimum, a.Maximum);
        }
        foreach (string id in block.Attributes.Keys)
            Authored.Require(Attributes.Any(a => a.Id == id), path, $"{field}.attributes.{id}", "is not an attribute.");
        foreach (var (id, value) in block.Bases)
        {
            DerivedStatDefinition? d = Derived.FirstOrDefault(s => s.Id == id);
            Authored.Require(d is not null, path, $"{field}.bases.{id}", "is not a derived stat.");
            Authored.Within(path, $"{field}.bases.{id}", value, d!.Minimum, d.Maximum);
        }
        foreach (var (id, value) in block.Resistances)
        {
            DamageKindDefinition? k = DamageKind(id);
            Authored.Require(k is not null, path, $"{field}.resistances.{id}", "is not a damage kind.");
            Authored.Within(path, $"{field}.resistances.{id}", value, k!.MinimumResistance, k.MaximumResistance);
        }
        foreach (var (id, value) in block.Initial)
        {
            Authored.Require(Tracks.Any(t => t.Id == id), path, $"{field}.initial.{id}", "is not a track.");
            Authored.AtLeast(path, $"{field}.initial.{id}", value, 0);
        }
    }

    private static void Unique(string path, string field, IEnumerable<string> ids)
    {
        string? repeated = ids.GroupBy(id => id).FirstOrDefault(g => g.Count() > 1)?.Key;
        Authored.Require(repeated is null, path, field, $"id '{repeated}' appears more than once.");
    }
}

/// <summary>A base attribute, bounded for every actor.</summary>
internal sealed record AttributeDefinition(string Id, string Name, float Minimum, float Maximum);

/// <summary>
/// A stat derived from attributes: its base, bounds, the step its value rounds to (zero for none), and how much each
/// point of an attribute adds. An actor's block may set its own base; the attribute contributions are Engine stat
/// sources, so the value explains itself.
/// </summary>
internal sealed record DerivedStatDefinition(string Id, string Name, float Base, float Minimum, float Maximum, float Quantum,
    AttributeScaling[] From);
internal sealed record AttributeScaling(string Attribute, float PerPoint);

/// <summary>A resource pool (health, charge) bounded by a derived stat, recovering <see cref="Regeneration"/> points a world second.</summary>
internal sealed record TrackDefinition(string Id, string Name, string Maximum, float Regeneration);

/// <summary>A kind of damage; each actor holds a resistance to it as a fraction removed (negative is a weakness).</summary>
internal sealed record DamageKindDefinition(string Id, string Name, float MinimumResistance, float MaximumResistance);

/// <summary>
/// One actor's numbers in the vocabulary: every attribute, any derived bases it overrides, its resistances (absent is
/// none), and the tracks' starting values (absent is full).
/// </summary>
internal sealed record ActorStatBlock(Dictionary<string, float> Attributes, Dictionary<string, float> Bases,
    Dictionary<string, float> Resistances, Dictionary<string, float> Initial);

/// <summary>Damage of one kind about to land on an actor, before its resistance.</summary>
internal readonly record struct DamagePacket(int Amount, string Kind);

internal sealed record StatVocabulary(AttributeDefinition[] Attributes, DerivedStatDefinition[] Derived, TrackDefinition[] Tracks);
internal sealed record DamageVocabulary(DamageKindDefinition[] Kinds);

/// <summary>An actor's stat block file: the player's (player/stats.json) and, later, each enemy kind's.</summary>
internal static class ActorStatFile
{
    internal static ActorStatBlock Load(IEngineContext engine, string path, MechanicsDefinition mechanics)
    {
        ActorStatBlock block = Authored.Read(engine, path, MechanicsJson.Default.ActorStatBlock);
        mechanics.Validate(path, "block", block);
        return block;
    }
}

// Authored: missing constructor values, nulls in non-nullable fields and unknown members are errors, not defaults.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(StatVocabulary))]
[JsonSerializable(typeof(DamageVocabulary))]
[JsonSerializable(typeof(EffectCatalog))]
[JsonSerializable(typeof(MechanicsMessages))]
[JsonSerializable(typeof(ActorStatBlock))]
internal sealed partial class MechanicsJson : JsonSerializerContext;
