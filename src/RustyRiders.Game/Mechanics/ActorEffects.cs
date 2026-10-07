using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using EngineEffectDefinition = Rusty.Engine.Mechanics.EffectDefinition;

namespace RustyRiders.Game.Mechanics;

/// <summary>Why a live effect left its bearer.</summary>
internal enum EffectEnd { Expired, Spent, Replaced, Cleared }

/// <summary>One effect on one actor: the Engine entry's identity with the product's remaining time, tick and ward.</summary>
internal sealed class LiveEffect(EffectDefinition definition, EffectInstanceId instance, string source)
{
    internal EffectDefinition Definition { get; } = definition;
    internal EffectInstanceId Instance { get; } = instance;
    /// <summary>Who applied it ("item.medkit", "enemy.3/claw"); an independent effect keeps one per source.</summary>
    internal string Source { get; set; } = source;
    internal int Stacks { get; set; }
    /// <summary>World seconds left before it expires.</summary>
    internal float Remaining { get; set; }
    /// <summary>World seconds since its last tick, for an effect that ticks.</summary>
    internal float SinceTick { get; set; }
    /// <summary>Damage a ward can still absorb.</summary>
    internal int WardLeft { get; set; }
    /// <summary>Where it came from (the user, or an area's centre): what a knockback drives away from.</summary>
    internal System.Numerics.Vector3 Origin { get; set; }
    /// <summary>The Engine stat sources its stacks activated; they leave the stats when it ends.</summary>
    internal StatSource[] Sources { get; set; } = [];
}

/// <summary>
/// The effects one actor bears, over an Engine <see cref="EffectsComponent"/> that owns stacking and source provenance.
/// Adapted from rusty-hotel's (itself from rusty-dagger's <c>ActiveEffectLifecycle</c>; docs/reuse.md): every Engine
/// admission and removal goes through one path that keeps the product state and the stat sources in step. Durations and
/// ticks advance only by the world seconds passed to <see cref="Advance"/>, so a held world holds every effect where it is.
/// </summary>
internal sealed class ActorEffects
{
    // World seconds accumulate in float steps; a tick or expiry this close to its moment is due.
    private const float Due = 1e-4f;
    private readonly ActorStats stats;
    private readonly EntityId? owner;
    private readonly Dictionary<string, EngineEffectDefinition> engineDefinitions;
    private readonly Dictionary<EffectInstanceId, LiveEffect> live = [];
    private EffectsComponent component;
    private ulong next;

    internal ActorEffects(MechanicsDefinition mechanics, ActorStats stats, EntityId? owner)
    {
        this.stats = stats;
        this.owner = owner;
        component = new(owner);
        engineDefinitions = mechanics.Effects.ToDictionary(e => e.Id, e => new EngineEffectDefinition(EffectDefinitionId.Parse(e.Id),
            StackingGroupId.Parse(e.Group), e.Stacking switch
            {
                EffectStacking.Independent => EffectStackingPolicy.IndependentByProvenance,
                EffectStacking.Refresh => EffectStackingPolicy.Refresh,
                _ => EffectStackingPolicy.Replace
            }, checked((ushort)e.MaximumInstances), checked((ushort)e.MaximumStacks),
            e.ContributesStats ? [SourceDefinitionId.Parse($"effect.{e.Id}")] : []), StringComparer.Ordinal);
    }

    /// <summary>Active effects in admission order.</summary>
    internal IReadOnlyList<LiveEffect> Active => live.Values.OrderBy(e => e.Instance.Value, StringComparer.Ordinal).ToArray();
    /// <summary>Whether a stun holds its bearer: no movement and no action.</summary>
    internal bool Stunned => live.Values.Any(e => e.Definition.Stun is not null);
    /// <summary>A knockback in force, if any: the most recent one moves its bearer.</summary>
    internal LiveEffect? Knockback => Active.LastOrDefault(e => e.Definition.Knockback is not null);
    /// <summary>The hit contributions guards in force bring; a defeating one ends its effect when used.</summary>
    internal IEnumerable<ActiveContribution> HitContributions => Active.Where(e => e.Definition.Guard is not null)
        .Select(e => new ActiveContribution(e.Definition.Guard!, () => End(e, EffectEnd.Spent)));

    /// <summary>The Engine stat sources every active effect holds, for the stats to evaluate.</summary>
    internal IEnumerable<StatSource> Sources => live.Values.SelectMany(e => e.Sources);

    /// <summary>
    /// Applies an effect from a source under its stacking rule. Returns the live effect, or null when an independent
    /// effect's group already holds as many instances as it allows.
    /// </summary>
    internal LiveEffect? Apply(EffectDefinition definition, string source, System.Numerics.Vector3 origin = default)
    {
        LiveEffect? applied = ApplyByRule(definition, source);
        if (applied is not null) applied.Origin = origin;
        return applied;
    }

    private LiveEffect? ApplyByRule(EffectDefinition definition, string source)
    {
        LiveEffect? present = live.Values.FirstOrDefault(e => e.Definition.Group == definition.Group &&
            (definition.Stacking != EffectStacking.Independent || e.Source == source));
        EngineEffectDefinition entry = engineDefinitions[definition.Id];
        switch (definition.Stacking)
        {
            case EffectStacking.Independent when present is not null && present.Definition == definition:
            {
                present.Remaining = definition.Duration;
                present.WardLeft = Absorbs(definition, present.Stacks);
                return present;
            }
            case EffectStacking.Refresh when present is not null && present.Definition == definition:
            {
                int stacks = Math.Min(definition.MaximumStacks, present.Stacks + 1);
                LiveEffect refreshed = Admit(component.Refresh(present.Instance, Provenance(source), checked((ushort)stacks)),
                    definition, source, stacks, definition.Duration, present.SinceTick, Absorbs(definition, stacks));
                return refreshed;
            }
            case EffectStacking.Replace:
                return Admit(component.Replace(entry, NewInstance(), Provenance(source), 1),
                    definition, source, 1, definition.Duration, 0, Absorbs(definition, 1));
        }
        // Another effect sharing the group from this source gives way; otherwise this is a first application.
        if (present is not null) End(present, EffectEnd.Replaced);
        if (definition.Stacking == EffectStacking.Independent &&
            live.Values.Count(e => e.Definition.Group == definition.Group) >= definition.MaximumInstances) return null;
        return Admit(component.Apply(entry, NewInstance(), Provenance(source), 1), definition, source, 1, definition.Duration, 0,
            Absorbs(definition, 1));
    }

    /// <summary>
    /// Advances every effect by world seconds: over-time effects tick on their interval, then effects whose time
    /// is up expire. Returns whether any track changed.
    /// </summary>
    internal bool Advance(float seconds)
    {
        if (seconds <= 0) return false;
        bool changed = false;
        foreach (LiveEffect effect in Active)
        {
            if (!live.ContainsKey(effect.Instance)) continue;
            float interval = effect.Definition.Interval;
            if (interval > 0)
            {
                effect.SinceTick += Math.Min(seconds, effect.Remaining);
                while (effect.SinceTick >= interval - Due && live.ContainsKey(effect.Instance))
                {
                    effect.SinceTick = Math.Max(0, effect.SinceTick - interval);
                    changed |= Tick(effect);
                }
            }
            if (!live.ContainsKey(effect.Instance)) continue;
            effect.Remaining -= seconds;
            if (effect.Remaining <= Due) End(effect, EffectEnd.Expired);
        }
        return changed;
    }

    /// <summary>Lets wards against this kind take what they can of a hit; returns what is left to land.</summary>
    internal int Absorb(string damageKind, int amount)
    {
        foreach (LiveEffect ward in Active.Where(e => e.Definition.Ward is { } w && w.DamageKinds.Contains(damageKind)))
        {
            if (amount <= 0) break;
            int taken = Math.Min(ward.WardLeft, amount);
            ward.WardLeft -= taken;
            amount -= taken;
            if (ward.WardLeft == 0) End(ward, EffectEnd.Spent);
        }
        return amount;
    }

    internal void End(LiveEffect effect, EffectEnd reason) => Settle(reason == EffectEnd.Expired
        ? component.Expire(effect.Instance) : component.Remove(effect.Instance), null);

    /// <summary>Removes every effect: a reset actor bears none.</summary>
    internal void Clear()
    {
        component = new(owner);
        live.Clear();
        next = 0;
        stats.ApplyEffectSources();
    }

    private bool Tick(LiveEffect effect)
    {
        if (effect.Definition.Restore is { } restore)
            return stats.Track(restore.Track).Restore(restore.Amount * effect.Stacks) > 0;
        if (effect.Definition.Damage is { } damage)
            return stats.TakeDamage(new(damage.Amount * effect.Stacks, damage.DamageKind), ActorStats.HealthTrack) > 0;
        return false;
    }

    // One path for every Engine receipt: entries it removed leave with their sources, and its current entry is recorded
    // with the sources its stacks activated.
    private LiveEffect Admit(EffectMutationReceipt receipt, EffectDefinition definition, string source, int stacks,
        float remaining, float sinceTick, int wardLeft)
    {
        LiveEffect current = new(definition, receipt.Current!.Instance, source)
        {
            Stacks = stacks, Remaining = remaining, SinceTick = sinceTick, WardLeft = wardLeft,
            Sources = receipt.ActivatedSources.Select(a => new StatSource(a.Identity, a.Definition, 0, Contributions(definition))).ToArray()
        };
        Settle(receipt, current);
        return current;
    }

    private void Settle(EffectMutationReceipt receipt, LiveEffect? current)
    {
        foreach (ActiveEffect removed in receipt.Removed) live.Remove(removed.Instance);
        if (current is not null) live[current.Instance] = current;
        stats.ApplyEffectSources();
    }

    private static StatContributionDefinition[] Contributions(EffectDefinition definition)
    {
        if (definition.Stat is { } stat)
            return [new(StatId.Parse(stat.Stat), StackingGroupId.Parse($"effect.{definition.Group}.{stat.Stat}"),
                MechanicsStackingPolicy.Sum, new StatContribution.Add(stat.Amount))];
        // Slows do not compound: the strongest one sets the pace.
        if (definition.Slow is { } slow)
            return [new(StatId.Parse(ActorStats.PaceStat), StackingGroupId.Parse("effect.slow"), MechanicsStackingPolicy.Lowest,
                new StatContribution.Multiply(slow.Factor))];
        return [];
    }

    private static int Absorbs(EffectDefinition definition, int stacks) => (definition.Ward?.Absorb ?? 0) * stacks;
    private MechanicsSourceIdentity Provenance(string source) => new IntrinsicSourceIdentity(owner, SourceInstanceId.Parse($"applied.{source}"));
    // Zero-padded so the Engine's ordinal order is admission order.
    private EffectInstanceId NewInstance() => EffectInstanceId.Parse($"effect-{++next:D8}");
}
