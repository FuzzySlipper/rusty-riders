using System.Numerics;
using Rusty.Engine.Mechanics;
using RustyRiders.Game.Mechanics;

namespace RustyRiders.Game.Player;

/// <summary>
/// The player's stats, health and effects (content/player/stats.json over the shared mechanics vocabulary), and what
/// they mean for the body: a stun stops movement, a slow scales it by pace, and a knockback drives it away from where
/// it came from. Effects and regeneration advance only by world seconds.
/// </summary>
internal sealed class PlayerVitals
{
    internal const string Path = "player/stats.json";

    internal PlayerVitals(MechanicsDefinition mechanics, ActorStatBlock block) => Stats = new ActorStats(mechanics, block, null);

    internal ActorStats Stats { get; }
    internal Track Health => Stats.Track(ActorStats.HealthTrack);
    internal bool Defeated => Health.ValueInt <= 0;

    /// <summary>How much of the player's movement input moves the body: none while stunned, else scaled by pace.</summary>
    internal float MovementScale => Stats.Effects.Stunned ? 0 : Stats.Pace;

    /// <summary>Advances effects and regeneration by world seconds.</summary>
    internal void Step(float seconds)
    {
        Stats.Effects.Advance(seconds);
        Stats.Regenerate(seconds);
    }

    /// <summary>The velocity a knockback in force drives the body with, away from its origin across the ground.</summary>
    internal Vector3 Knockback(Vector3 feet) => Stats.Effects.KnockbackVelocity(feet);

    /// <summary>Back to the block's starting values, with no effects.</summary>
    internal void Reset() => Stats.Reset();
}
