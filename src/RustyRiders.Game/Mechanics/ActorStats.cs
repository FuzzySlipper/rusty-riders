using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;

namespace RustyRiders.Game.Mechanics;

/// <summary>
/// One actor's live stats: an Engine <see cref="StatsComponent"/> built from the vocabulary and the actor's block.
/// Attributes are base stats; each derived stat carries one intrinsic Engine source holding its attributes'
/// contributions, so <see cref="Explain"/> names where a value came from; tracks share their derived maximum; and a
/// resistance stat per damage kind decides how much of a hit lands. The actor's <see cref="Effects"/> add their own
/// Engine sources beside those, and wards among them take their share of a hit first. One model for the player and
/// every enemy.
/// </summary>
internal sealed class ActorStats
{
    // Resource pools count in whole points.
    private const double WholePoints = 1;
    /// <summary>The track hits land on, and the derived stat movement is scaled by, by vocabulary id.</summary>
    internal const string HealthTrack = "health", PaceStat = "pace";
    /// <summary>The derived stat an actor's action phases are scaled by.</summary>
    internal const string ActionTimeStat = "action-time";
    private readonly MechanicsDefinition mechanics;
    private readonly ActorStatBlock block;
    private readonly EntityId? owner;

    internal ActorStats(MechanicsDefinition mechanics, ActorStatBlock block, EntityId? owner)
    {
        this.mechanics = mechanics;
        this.block = block;
        this.owner = owner;
        foreach (AttributeDefinition a in mechanics.Attributes)
            Stats.AddStat(StatOf(a.Id), new Stat(block.Attributes[a.Id], a.Minimum, a.Maximum));
        foreach (DerivedStatDefinition d in mechanics.Derived)
            Stats.AddStat(StatOf(d.Id), new Stat(BaseOf(d), d.Minimum, d.Maximum, d.Quantum));
        foreach (DamageKindDefinition k in mechanics.DamageKinds)
            Stats.AddStat(ResistanceOf(k.Id), new Stat(block.Resistances.GetValueOrDefault(k.Id), k.MinimumResistance, k.MaximumResistance));
        RefreshDerived();
        foreach (TrackDefinition t in mechanics.Tracks)
        {
            Stat maximum = Stats.GetStat(StatOf(t.Maximum));
            Stats.AddTrack(TrackOf(t.Id), new Track(maximum, InitialOf(t, maximum), quantum: WholePoints));
        }
        Effects = new(mechanics, this, owner);
    }

    internal StatsComponent Stats { get; } = new();
    internal ActorEffects Effects { get; }
    internal MechanicsDefinition Mechanics => mechanics;
    /// <summary>The factor this actor's movement is scaled by.</summary>
    internal float Pace => (float)Stat(PaceStat).Value;

    internal Stat Stat(string id) => Stats.GetStat(StatOf(id));
    internal Track Track(string id) => Stats.GetTrack(TrackOf(id));
    internal double Resistance(string kind) => Stats.GetStat(ResistanceOf(kind)).Value;
    internal StatEvaluation Explain(string id) => Stat(id).Explain();

    // Regeneration owed but not yet a whole point, per track; a fraction of a point, not saved.
    private readonly Dictionary<string, float> owed = new(StringComparer.Ordinal);

    /// <summary>Recovers each regenerating track by world seconds, a whole point at a time.</summary>
    internal void Regenerate(float seconds)
    {
        foreach (TrackDefinition t in mechanics.Tracks)
        {
            if (t.Regeneration <= 0) continue;
            Track track = Track(t.Id);
            if (track.Value >= track.MaximumValue) { owed.Remove(t.Id); continue; }
            float due = owed.GetValueOrDefault(t.Id) + t.Regeneration * seconds;
            int whole = (int)due;
            if (whole > 0) track.Restore(whole);
            owed[t.Id] = due - whole;
        }
    }

    /// <summary>Lands damage on a track after this actor's resistance to its kind; returns what was taken.</summary>
    internal int TakeDamage(DamagePacket packet, string track) => Apply(Resist(packet), packet.Kind, track);

    /// <summary>What a packet comes to after this actor's resistance to its kind.</summary>
    internal float Resist(DamagePacket packet) => Math.Max(0, packet.Amount * (float)(1 - Resistance(packet.Kind)));

    /// <summary>
    /// Applies damage already resisted: wards against its kind take their share, then the track, never below empty. When it
    /// would take the last point, <paramref name="defeating"/> may name health to keep. Returns what was taken.
    /// </summary>
    internal int Apply(float amount, string kind, string track, Func<int>? defeating = null)
    {
        int landed = (int)Math.Round(amount, MidpointRounding.AwayFromZero);
        if (landed <= 0) return 0;
        Track health = Track(track);
        landed = Effects.Absorb(kind, landed);
        int applied = Math.Clamp(landed, 0, health.ValueInt);
        if (applied > 0 && applied >= health.ValueInt && defeating?.Invoke() is int keep and > 0)
            applied = Math.Max(0, health.ValueInt - keep);
        health.Spend(applied);
        return applied;
    }

    /// <summary>
    /// Gives every stat the effect sources aimed at it, attributes first, then recomputes the derived stats from the
    /// attributes as they now stand.
    /// </summary>
    internal void ApplyEffectSources()
    {
        // Equipment and effect sources are rebuilt by their owners, never saved.
        foreach (AttributeDefinition a in mechanics.Attributes) Stats.GetStat(StatOf(a.Id)).SetSources(StatOf(a.Id), EffectSources(a.Id));
        foreach (DamageKindDefinition k in mechanics.DamageKinds)
            Stats.GetStat(ResistanceOf(k.Id)).SetSources(ResistanceOf(k.Id), EffectSources(ResistanceStat(k.Id)));
        RefreshDerived();
    }

    /// <summary>Recomputes each derived stat's attribute source from the attributes as they stand, beside its effect sources.</summary>
    internal void RefreshDerived()
    {
        foreach (DerivedStatDefinition d in mechanics.Derived)
        {
            StatContributionDefinition[] contributions = d.From.Select(f => new StatContributionDefinition(StatOf(d.Id),
                StackingGroupId.Parse($"{d.Id}.{f.Attribute}"), MechanicsStackingPolicy.Sum,
                new StatContribution.Add(Stats.GetStat(StatOf(f.Attribute)).Value * f.PerPoint))).ToArray();
            StatSource[] derived = contributions.Length == 0 ? [] :
                [new StatSource(new IntrinsicSourceIdentity(owner, SourceInstanceId.Parse($"derived.{d.Id}")),
                    SourceDefinitionId.Parse($"derived.{d.Id}"), 0, contributions)];
            Stats.GetStat(StatOf(d.Id)).SetSources(StatOf(d.Id), [.. derived, .. EffectSources(d.Id)]);
        }
    }

    private IEnumerable<StatSource> EffectSources(string stat) =>
        (Effects?.Sources ?? []).Concat(equipment).Where(s => s.Contributions.Any(c => c.Stat.Value == stat));

    private StatSource[] equipment = [];

    /// <summary>Replaces the sources worn equipment holds (see <see cref="Items.Inventory.Sources"/>) and re-evaluates.</summary>
    internal void SetEquipmentSources(StatSource[] sources)
    {
        equipment = sources;
        ApplyEffectSources();
    }

    /// <summary>The block's starting values: bases as authored and tracks at their initial points.</summary>
    internal void Reset()
    {
        Effects.Clear();
        foreach (AttributeDefinition a in mechanics.Attributes) Stats.GetStat(StatOf(a.Id)).BaseValue = block.Attributes[a.Id];
        foreach (DerivedStatDefinition d in mechanics.Derived) Stats.GetStat(StatOf(d.Id)).BaseValue = BaseOf(d);
        RefreshDerived();
        foreach (TrackDefinition t in mechanics.Tracks)
        {
            Track track = Track(t.Id);
            track.SetCurrent(InitialOf(t, track.Maximum), true);
        }
    }

    private float BaseOf(DerivedStatDefinition d) => block.Bases.TryGetValue(d.Id, out float authored) ? authored : d.Base;

    private double InitialOf(TrackDefinition t, Stat maximum) => block.Initial.TryGetValue(t.Id, out float initial)
        ? Math.Min(initial, maximum.Value) : maximum.Value;

    private static StatId StatOf(string id) => StatId.Parse(id);
    private static TrackId TrackOf(string id) => TrackId.Parse(id);
    private static StatId ResistanceOf(string kind) => StatId.Parse(ResistanceStat(kind));
    /// <summary>The id of the stat holding an actor's resistance to a damage kind.</summary>
    internal static string ResistanceStat(string kind) => $"resistance.{kind}";
}
