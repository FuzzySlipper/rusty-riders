using System.Numerics;
using Rusty.Engine.Debugging;
using RustyRiders.Game.Combat;
using RustyRiders.Game.Mechanics;
using RustyRiders.Game.Player;

namespace RustyRiders.Game.Developer;

/// <summary>Live-debug commands for combat: training dummies to hit and a read of their state.</summary>
internal sealed class CombatDebugCommands(PlayerCombat combat, Walker walker) : IDebugCommandModule
{
    [DebugCommand("riders.dev.dummy", Description = "Developer override: stand a training dummy <metres> in front of the player.")]
    public DebugCommandResult Dummy(float metres)
    {
        if (!float.IsFinite(metres) || metres <= 0) return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, "Metres must be positive.");
        Vector3 forward = walker.Forward with { Y = 0 };
        TrainingDummy dummy = combat.AddDummy(walker.Feet + Vector3.Normalize(forward) * metres);
        return DebugCommandResult.Success(FormattableString.Invariant($"Dummy {dummy.Entity} at {dummy.Feet.X:0.0}, {dummy.Feet.Z:0.0}."));
    }

    [DebugCommand("riders.dev.use", Description = "Developer override: the main or off hand uses its weapon, as a click does (the world still runs on its own time; advance it in held time).")]
    public DebugCommandResult Use(string hand)
    {
        if (hand is not ("main" or "off")) return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, "Hand is main or off.");
        return combat.Use(hand == "main" ? PlayerCombat.MainHand : PlayerCombat.OffHand) is { } seconds
            ? DebugCommandResult.Success(FormattableString.Invariant($"Used; it takes {seconds:0.###} s."))
            : DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, "The hand cannot act now.");
    }

    [DebugCommand("riders.dev.select", Description = "Developer override: take carried weapon <index> (0 first) into the main hand, as its number key does.")]
    public DebugCommandResult Select(int index)
    {
        combat.Select(index);
        return DebugCommandResult.Success("Selected; riders.arms reads the look and how the arms lead.");
    }

    [DebugCommand("riders.combat.dummies", Description = "Read each training dummy's health and effects.")]
    public DebugCommandResult Dummies() => DebugCommandResult.Success(combat.Dummies.Count == 0 ? "No dummies." : string.Join("\n",
        combat.Dummies.Select(d => $"{d.Entity}: health {d.Stats.Track(ActorStats.HealthTrack).ValueInt}"
            + string.Concat(d.Stats.Effects.Active.Select(e => $", {e.Definition.Name} {e.Remaining:0.0}s")))));
}
