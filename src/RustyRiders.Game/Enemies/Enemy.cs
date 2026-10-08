using System.Numerics;
using Rusty.Engine;
using RustyRiders.Game.Actions;
using RustyRiders.Game.Combat;
using RustyRiders.Game.Mechanics;

namespace RustyRiders.Game.Enemies;

/// <summary>Why an enemy is in the level: waiting in a room, or come through the entry portal after the player.</summary>
internal enum EnemyRole { Resident, Chaser }

/// <summary>
/// One enemy's live state: its kind, stats and action timing, its body (a capsule stepped by the Engine character
/// controller), the way it faces, whether it knows where the player is, and the next waypoint of its route.
/// </summary>
internal sealed class Enemy(ulong entity, EnemyKind kind, MechanicsDefinition mechanics, Vector3 feet, EnemyRole role) : IActionActor, INamed
{
    private readonly Vector3 half = new(kind.Look.Size[0] / 2, kind.Look.Size[1] / 2, kind.Look.Size[2] / 2);

    internal EnemyKind Kind { get; } = kind;
    internal EnemyRole Role { get; } = role;
    internal ActionUser User { get; } = new();
    /// <summary>The capsule's centre, as the character controller moves it.</summary>
    internal Vector3 Centre { get; set; } = feet + Vector3.UnitY * (kind.Look.Size[1] / 2);
    internal CharacterMotion Motion { get; set; }
    internal ulong Sequence { get; set; }
    /// <summary>The way it faces, as a walker yaw (0 faces -Z, positive turns right).</summary>
    internal float Yaw { get; set; }
    internal bool Aware { get; set; } = role == EnemyRole.Chaser;
    internal Vector3? Waypoint { get; set; }
    internal float RepathIn { get; set; }
    /// <summary>How its last route query ended, for inspection.</summary>
    internal NavigationPathOutcome Route { get; set; }
    internal int LastHealth { get; set; } = int.MaxValue;
    /// <summary>World seconds left of the flinch after it was hurt (its hit clip plays meanwhile).</summary>
    internal float HurtFor { get; set; }
    internal Vector3 Feet => Centre - Vector3.UnitY * half.Y;

    public string Name => Kind.Name;
    public ActorStats Stats { get; } = new(mechanics, kind.Stats, null);
    public ulong Entity { get; } = entity;
    public Vector3 Position => Centre;
    public Vector3 Eye => Feet + Vector3.UnitY * (half.Y * 1.7f);
    public SpatialEntityCollider Hitbox => new(Entity, Centre - half, Centre + half, CombatGroups.Target, uint.MaxValue, true, false, false);
    public bool Alive => Stats.Track(ActorStats.HealthTrack).ValueInt > 0;
    public IEnumerable<ActiveContribution> Contributions => Stats.Effects.HitContributions;
    public IEnumerable<string> HitEffects => [];
    internal Transform Transform => new(Centre, Quaternion.CreateFromAxisAngle(Vector3.UnitY, -Yaw), half * 2);
}
