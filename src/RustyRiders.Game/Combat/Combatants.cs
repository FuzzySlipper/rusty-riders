using System.Numerics;
using Rusty.Engine;
using RustyRiders.Game.Actions;
using RustyRiders.Game.Mechanics;
using RustyRiders.Game.Player;

namespace RustyRiders.Game.Combat;

/// <summary>Collision groups for action queries' bodies.</summary>
internal static class CombatGroups
{
    internal const uint Player = 1, Target = 2;
    internal const ulong PlayerEntity = 1;
}

/// <summary>The player as an action user and target: the walker's body with the vitals' stats.</summary>
internal sealed class PlayerActor(Walker walker, PlayerVitals vitals, float radius) : IActionActor
{
    public ActorStats Stats => vitals.Stats;
    public ulong Entity => CombatGroups.PlayerEntity;
    public Vector3 Eye => walker.EyePosition;
    public Vector3 Position => walker.Position;
    public SpatialEntityCollider Hitbox => new(Entity, walker.Feet - new Vector3(radius, 0, radius),
        walker.Feet + new Vector3(radius, walker.Height, radius), CombatGroups.Player, uint.MaxValue, true, false, false);
    public bool Alive => !vitals.Defeated;
    public IEnumerable<ActiveContribution> Contributions => vitals.Stats.Effects.HitContributions;
    public IEnumerable<string> HitEffects => [];
}

/// <summary>A still training target with stats, standing where it was put.</summary>
internal sealed class TrainingDummy(ulong entity, DummyDefinition definition, MechanicsDefinition mechanics, Vector3 feet) : IActionActor, INamed
{
    private readonly Vector3 half = new(definition.Size[0] / 2, definition.Size[1] / 2, definition.Size[2] / 2);

    public string Name => definition.Name;
    internal Vector3 Feet { get; } = feet;
    public ActorStats Stats { get; } = new(mechanics, definition.Stats, null);
    public ulong Entity { get; } = entity;
    public Vector3 Position => Feet + Vector3.UnitY * half.Y;
    public Vector3 Eye => Feet + Vector3.UnitY * (half.Y * 1.8f);
    public SpatialEntityCollider Hitbox => new(Entity, Position - half, Position + half, CombatGroups.Target, uint.MaxValue, true, false, false);
    public bool Alive => Stats.Track(ActorStats.HealthTrack).ValueInt > 0;
    public IEnumerable<ActiveContribution> Contributions => Stats.Effects.HitContributions;
    public IEnumerable<string> HitEffects => [];
    internal Transform Transform => new(Position, Quaternion.Identity, half * 2);
}
