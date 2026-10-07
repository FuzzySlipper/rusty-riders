using System.Text.Json.Serialization;
using RustyRiders.Game.Content;

namespace RustyRiders.Game.Mechanics;

/// <summary>What an effect does while it lasts; each kind has its own typed settings on the definition.</summary>
internal enum EffectKind { Stat, Restore, Damage, Ward, Stun, Slow, Guard, Knockback }

/// <summary>How a second application of an effect meets one already in its group (the Engine stacking policy).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EffectStacking>))]
internal enum EffectStacking
{
    /// <summary>Each source holds its own instance, up to the group's instance limit; the same source again restarts it.</summary>
    Independent,
    /// <summary>One instance in the group; another application adds a stack, up to the stack limit, and restarts it.</summary>
    Refresh,
    /// <summary>One instance in the group; another application replaces it with a fresh one.</summary>
    Replace,
}

/// <summary>
/// One reusable effect: its name and HUD mark, the stacking group it shares, how applications stack, how long it lasts
/// in world seconds, and exactly one kind's settings. Items, enemies, weapons and spells name it by id.
/// </summary>
internal sealed record EffectDefinition(string Id, string Name, string Mark, string Group, EffectStacking Stacking,
    int MaximumStacks, int MaximumInstances, float Duration,
    StatEffect? Stat = null, RestoreEffect? Restore = null, DamageEffect? Damage = null, WardEffect? Ward = null,
    StunEffect? Stun = null, SlowEffect? Slow = null, DamageContribution? Guard = null, KnockbackEffect? Knockback = null)
{
    internal const string Path = "mechanics/effects.json";

    internal EffectKind Kind => Stat is not null ? EffectKind.Stat : Restore is not null ? EffectKind.Restore
        : Damage is not null ? EffectKind.Damage : Ward is not null ? EffectKind.Ward : Stun is not null ? EffectKind.Stun
        : Slow is not null ? EffectKind.Slow : Guard is not null ? EffectKind.Guard : EffectKind.Knockback;

    /// <summary>The stats this effect contributes to while active, one Engine source per stack.</summary>
    internal bool ContributesStats => Stat is not null || Slow is not null;

    /// <summary>Seconds between an over-time effect's ticks; zero for kinds that do not tick.</summary>
    internal float Interval => Restore?.Interval ?? Damage?.Interval ?? 0;

    internal void Validate(string field, MechanicsDefinition mechanics, string pace)
    {
        Template.Plain(Path, ($"{field}.name", Name), ($"{field}.mark", Mark));
        Authored.Require(Group.Length > 0, Path, $"{field}.group", "is empty.");
        Authored.Within(Path, $"{field}.maximumStacks", MaximumStacks, 1, ushort.MaxValue);
        Authored.Within(Path, $"{field}.maximumInstances", MaximumInstances, 1, ushort.MaxValue);
        Authored.Require(Stacking == EffectStacking.Refresh || MaximumStacks == 1, Path, $"{field}.maximumStacks",
            "only a refresh effect gathers stacks; set it to 1.");
        Authored.Require(Stacking == EffectStacking.Independent || MaximumInstances == 1, Path, $"{field}.maximumInstances",
            "only an independent effect holds several instances; set it to 1.");
        Authored.Positive(Path, $"{field}.duration", Duration);
        int kinds = new object?[] { Stat, Restore, Damage, Ward, Stun, Slow, Guard, Knockback }.Count(k => k is not null);
        Authored.Require(kinds == 1, Path, field, "must set exactly one of stat, restore, damage, ward, stun, slow, guard or knockback.");
        if (Knockback is { } knockback) Authored.Positive(Path, $"{field}.knockback.speed", knockback.Speed);
        if (Guard is { } guard) DamageContribution.Validate([guard], Path, $"{field}.guard", mechanics, fromEffect: true);
        if (Stat is { } stat)
        {
            Authored.Require(mechanics.HasStat(stat.Stat) && stat.Stat != pace, Path, $"{field}.stat.stat",
                $"'{stat.Stat}' is not an attribute or derived stat (pace is changed by a slow).");
            Authored.Finite(Path, $"{field}.stat.amount", stat.Amount);
        }
        if (Restore is { } restore)
        {
            Authored.Require(mechanics.Tracks.Any(t => t.Id == restore.Track), Path, $"{field}.restore.track", $"unknown track '{restore.Track}'.");
            Authored.Positive(Path, $"{field}.restore.amount", restore.Amount);
            Ticks($"{field}.restore.interval", restore.Interval);
        }
        if (Damage is { } damage)
        {
            Authored.Require(mechanics.DamageKind(damage.DamageKind) is not null, Path, $"{field}.damage.damageKind",
                $"unknown damage kind '{damage.DamageKind}'.");
            Authored.Positive(Path, $"{field}.damage.amount", damage.Amount);
            Ticks($"{field}.damage.interval", damage.Interval);
        }
        if (Ward is { } ward)
        {
            Authored.Positive(Path, $"{field}.ward.absorb", ward.Absorb);
            Authored.Require(ward.DamageKinds.Length > 0, Path, $"{field}.ward.damageKinds", "names no damage kind.");
            for (int i = 0; i < ward.DamageKinds.Length; i++)
                Authored.Require(mechanics.DamageKind(ward.DamageKinds[i]) is not null, Path, $"{field}.ward.damageKinds[{i}]",
                    $"unknown damage kind '{ward.DamageKinds[i]}'.");
        }
        if (Slow is { } slow) Authored.Within(Path, $"{field}.slow.factor", slow.Factor, 0, 1);

        void Ticks(string at, float interval)
        {
            Authored.Positive(Path, at, interval);
            Authored.Require(interval <= Duration, Path, at, "is longer than the effect lasts.");
        }
    }
}

/// <summary>Adds <see cref="Amount"/> per stack to an attribute or derived stat.</summary>
internal sealed record StatEffect(string Stat, float Amount);
/// <summary>Restores <see cref="Amount"/> points per stack to a track every <see cref="Interval"/> seconds.</summary>
internal sealed record RestoreEffect(string Track, int Amount, float Interval);
/// <summary>Deals <see cref="Amount"/> damage per stack of one kind every <see cref="Interval"/> seconds, against resistance.</summary>
internal sealed record DamageEffect(string DamageKind, int Amount, float Interval);
/// <summary>Absorbs up to <see cref="Absorb"/> points per stack of the named kinds of damage; spent, it ends.</summary>
internal sealed record WardEffect(int Absorb, string[] DamageKinds);
/// <summary>Stuns its bearer: no movement and no action while it lasts.</summary>
internal sealed record StunEffect;
/// <summary>Multiplies its bearer's pace by <see cref="Factor"/>.</summary>
internal sealed record SlowEffect(float Factor);
/// <summary>Drives its bearer away from where the effect came from at <see cref="Speed"/> metres a second, while it lasts.</summary>
internal sealed record KnockbackEffect(float Speed);

internal sealed record EffectCatalog(EffectDefinition[] Effects);

/// <summary>How an active effect's state reads on the HUD: time left, stacks and a ward's remaining absorption.</summary>
internal sealed record MechanicsMessages(string EffectSeconds, string EffectStacks, string EffectWard)
{
    internal const string Path = "mechanics/messages.json";

    internal void Validate()
    {
        Template.Check(Path, "effectSeconds", EffectSeconds, "seconds");
        Template.Check(Path, "effectStacks", EffectStacks, "stacks");
        Template.Check(Path, "effectWard", EffectWard, "ward");
    }
}
