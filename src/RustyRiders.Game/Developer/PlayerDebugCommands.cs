using System.Globalization;
using System.Text;
using Rusty.Engine.Debugging;
using RustyRiders.Game.Mechanics;
using RustyRiders.Game.Player;

namespace RustyRiders.Game.Developer;

/// <summary>Live-debug commands for the player's stats: read them with their sources, and developer overrides that hurt or affect.</summary>
internal sealed class PlayerDebugCommands(PlayerVitals vitals, Walker walker, Action publish) : IDebugCommandModule
{
    [DebugCommand("riders.player.stats", Description = "Read the player's attributes, derived stats (with where each value came from), tracks, resistances and effects.")]
    public DebugCommandResult Stats()
    {
        ActorStats stats = vitals.Stats;
        MechanicsDefinition m = stats.Mechanics;
        StringBuilder text = new();
        foreach (AttributeDefinition a in m.Attributes) text.AppendLine(Line(a.Name, stats.Stat(a.Id).Value));
        foreach (DerivedStatDefinition d in m.Derived)
        {
            var evaluation = stats.Explain(d.Id);
            text.AppendLine(Line(d.Name, evaluation.Value) + FormattableString.Invariant($"  (base {evaluation.Base:0.##}, +{evaluation.AfterAdditions - evaluation.Base:0.##}, ×→{evaluation.AfterScaling:0.##}; {evaluation.Decisions.Count} decisions)"));
        }
        foreach (TrackDefinition t in m.Tracks)
            text.AppendLine(FormattableString.Invariant($"{t.Name}: {stats.Track(t.Id).ValueInt} / {stats.Track(t.Id).MaximumValue:0}"));
        foreach (DamageKindDefinition k in m.DamageKinds) text.AppendLine(Line($"{k.Name} resistance", stats.Resistance(k.Id)));
        foreach (LiveEffect e in stats.Effects.Active)
            text.AppendLine(FormattableString.Invariant($"{e.Definition.Name} ×{e.Stacks}: {e.Remaining:0.0} s left"));
        return DebugCommandResult.Success(text.ToString().TrimEnd());
    }

    [DebugCommand("riders.dev.damage", Description = "Developer override: deal <amount> damage of <kind> to the player, after resistances and wards.")]
    public DebugCommandResult Damage(int amount, string kind)
    {
        if (vitals.Stats.Mechanics.DamageKind(kind) is null)
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments,
                $"Unknown damage kind '{kind}'. Kinds: {string.Join(", ", vitals.Stats.Mechanics.DamageKinds.Select(k => k.Id))}.");
        if (amount <= 0) return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, "Amount must be positive.");
        int taken = vitals.Stats.TakeDamage(new DamagePacket(amount, kind), ActorStats.HealthTrack);
        publish();
        return DebugCommandResult.Success($"Took {taken} of {amount} {kind}; health {vitals.Health.ValueInt}.");
    }

    [DebugCommand("riders.dev.effect", Description = "Developer override: apply effect <id> to the player, as if from 3 m in front of them (so a knockback drives them back).")]
    public DebugCommandResult Effect(string id)
    {
        if (vitals.Stats.Mechanics.Effect(id) is not { } effect)
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments,
                $"Unknown effect '{id}'. Effects: {string.Join(", ", vitals.Stats.Mechanics.Effects.Select(e => e.Id))}.");
        float yaw = walker.LookState.YawRadians;
        System.Numerics.Vector3 ahead = walker.Feet + new System.Numerics.Vector3(MathF.Sin(yaw), 0, -MathF.Cos(yaw)) * 3;
        LiveEffect? applied = vitals.Stats.Effects.Apply(effect, "developer", ahead);
        publish();
        return applied is null ? DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, $"{effect.Name} is already at its instance limit.")
            : DebugCommandResult.Success($"{effect.Name} ×{applied.Stacks} for {applied.Remaining.ToString("0.#", CultureInfo.InvariantCulture)} s.");
    }

    private static string Line(string name, double value) => FormattableString.Invariant($"{name}: {value:0.##}");
}
