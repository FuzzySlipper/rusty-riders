using System.Numerics;
using RustyRiders.Game.Mechanics;
using Rusty.Engine;

namespace RustyRiders.Game.Actions;

/// <summary>Someone who uses actions and is hit by them: the player, an enemy or a training dummy.</summary>
internal interface IActionActor
{
    ActorStats Stats { get; }
    ulong Entity { get; }
    Vector3 Eye { get; }
    Vector3 Position { get; }
    SpatialEntityCollider Hitbox { get; }
    bool Alive { get; }
    /// <summary>The hit contributions this actor brings, from what it wears and the effects it bears.</summary>
    IEnumerable<ActiveContribution> Contributions { get; }
    /// <summary>Effects this actor's hits put on what they strike, beyond the action's own (a weapon's).</summary>
    IEnumerable<string> HitEffects { get; }
}

/// <summary>
/// What one landed action did to one target, or where it ended when it hit none (<see cref="Surface"/>: against a
/// surface); <see cref="TurnedAside"/> when a hit contribution prevented it.
/// </summary>
internal sealed record ActionImpact(ActionDefinition Action, IActionActor User, IActionActor? Target, Vector3 End, int Damage, bool Defeated,
    bool Surface = false, bool TurnedAside = false);

/// <summary>
/// A projectile in flight: who shot it, its action, where it is, its heading, how far it may still go, and the bodies it
/// may hit (those the action could hit when it was loosed: an enemy's shot passes its own kind by).
/// </summary>
internal sealed class Projectile(ActionDefinition action, IActionActor user, Vector3 position, Vector3 heading, IReadOnlySet<ulong> eligible)
{
    internal IReadOnlySet<ulong> Eligible { get; } = eligible;
    internal ActionDefinition Action { get; } = action;
    internal IActionActor User { get; } = user;
    internal Vector3 Position { get; set; } = position;
    internal Vector3 Heading { get; } = heading;
    internal float Travelled { get; set; }
}

/// <summary>
/// Lands actions with Engine spatial queries in the scene's session and applies what they deal: damage packets scaled
/// by the user's stats, then the outgoing contributions of the user and the incoming contributions of the target, then
/// the target's resistance and wards (<see cref="ActorStats.TakeDamage"/>), then the action's effects. Adapted from
/// rusty-hotel's (itself from rusty-dagger's <c>CombatResolution</c>; docs/reuse.md): participants' live stats and
/// contributions, one ordered path. Projectiles fly on the world seconds passed to <see cref="Step"/>.
/// </summary>
internal sealed class ActionResolution(IEngineContext engine, SpatialSession session, MechanicsDefinition mechanics)
{
    // Every query sees every collider; bodies are offered explicitly as the entities each query may hit.
    private static readonly SpatialQueryFilter Everything = new(0, uint.MaxValue);
    private readonly List<Projectile> projectiles = [];
    // A sweep meeting a surface this close to its start began in contact with it.
    private const double StartContact = .05;

    internal IReadOnlyList<Projectile> Projectiles => projectiles;

    /// <summary>Lands an action whose windup has ended, against the bodies it may hit. A projectile takes flight instead.</summary>
    internal ActionImpact[] Land(ActionDefinition action, IActionActor user, Vector3 aim, IReadOnlyList<IActionActor> targets)
    {
        foreach (string effect in action.SelfEffects) user.Stats.Effects.Apply(mechanics.Effect(effect)!, $"action.{action.Id}");
        IActionActor[] living = targets.Where(t => t.Alive && t.Entity != user.Entity).ToArray();
        ActionDelivery d = action.Delivery;
        switch (d.Kind)
        {
            case DeliveryKind.Self:
                return [new(action, user, null, user.Eye, 0, false)];
            case DeliveryKind.Hitscan:
                return [First(action, user, Cast(user, aim, d.Range, living), user.Eye + aim * d.Range, living)];
            case DeliveryKind.Melee:
            {
                SpatialHit hit = engine.Spatial.CastCapsule(new(session, user.Eye, 0, d.Width / 2, aim * d.Range, 0, Everything,
                    living.Select(t => t.Hitbox).ToArray(), new[] { user.Entity }));
                // A user hugging a wall starts the sweep in it; then the swing is judged along its line alone.
                if (hit.Present && hit.Kind != SpatialHitKind.Entity && (hit.StartSolid || hit.Distance < StartContact))
                    hit = Cast(user, aim, d.Range, living);
                return [First(action, user, hit, user.Eye + aim * d.Range, living)];
            }
            case DeliveryKind.Projectile:
                projectiles.Add(new(action, user, user.Eye, aim, living.Select(t => t.Entity).ToHashSet()));
                return [];
            default:
                return Area(action, user, user.Eye + aim * d.Range, living);
        }
    }

    /// <summary>Moves every projectile by admitted seconds, casting each step's segment; returns what they hit.</summary>
    internal ActionImpact[] Step(float seconds, IReadOnlyList<IActionActor> bodies)
    {
        List<ActionImpact> impacts = [];
        foreach (Projectile shot in projectiles.ToArray())
        {
            float travel = Math.Min(shot.Action.Delivery.Speed * seconds, shot.Action.Delivery.Range - shot.Travelled);
            Vector3 next = shot.Position + shot.Heading * travel;
            IActionActor[] living = bodies.Where(t => t.Alive && t.Entity != shot.User.Entity && shot.Eligible.Contains(t.Entity)).ToArray();
            SpatialHit hit = engine.Spatial.CastSegment(new(session, shot.Position, next, Everything,
                living.Select(t => t.Hitbox).ToArray(), new[] { shot.User.Entity }, ReadOnlyMemory<SpatialEntityCollider>.Empty));
            shot.Position = next;
            shot.Travelled += travel;
            if (hit.Present) { impacts.Add(First(shot.Action, shot.User, hit, hit.Point, living)); projectiles.Remove(shot); }
            else if (shot.Travelled >= shot.Action.Delivery.Range - 1e-4f) { impacts.Add(new(shot.Action, shot.User, null, next, 0, false)); projectiles.Remove(shot); }
        }
        return impacts.ToArray();
    }

    internal void Clear() => projectiles.Clear();

    /// <summary>Whether a straight line between two points meets no surface.</summary>
    internal bool Clear(Vector3 from, Vector3 to) => !engine.Spatial.CastSegment(new(session, from, to, Everything,
        ReadOnlyMemory<SpatialEntityCollider>.Empty, ReadOnlyMemory<ulong>.Empty, ReadOnlyMemory<SpatialEntityCollider>.Empty)).Present;

    /// <summary>
    /// Applies one hit in its stages, the user's outgoing contributions before the target's incoming ones at each: a hit
    /// contribution may turn it aside; each packet, scaled by the user's stats, takes the damage stage, then the
    /// target's resistance, then the applying stage, then wards and health once, where a defeating contribution may keep
    /// the target standing. The action's effects follow a hit that was not turned aside. Returns the health taken, or
    /// null when the hit was turned aside.
    /// </summary>
    /// <param name="origin">Where the hit came from (the user, or an area's centre), for effects that push or lure.</param>
    internal int? Hit(ActionDefinition action, IActionActor user, IActionActor target, Vector3? origin = null)
    {
        // Read live at each stage of each packet: a defeating guard spent by one packet is gone for the next.
        IEnumerable<ActiveContribution> At(ContributionStage stage, string kind) =>
            user.Contributions.Where(c => c.Contribution.Applies(stage, ContributionSide.Outgoing, kind))
                .Concat(target.Contributions.Where(c => c.Contribution.Applies(stage, ContributionSide.Incoming, kind))).ToArray();
        if (action.Damage.Any(p => At(ContributionStage.Hit, p.Kind).Any(c => c.Contribution.Prevent))) return null;
        int taken = 0;
        foreach (DamageDefinition packet in action.Damage)
        {
            float amount = (packet.Amount + packet.Scaling.Sum(s => (float)user.Stats.Stat(s.Stat).Value * s.PerPoint))
                * (packet.Power is { } power ? (float)user.Stats.Stat(power).Value : 1);
            foreach (ActiveContribution c in At(ContributionStage.Damage, packet.Kind)) amount = c.Contribution.Change(amount);
            float applying = target.Stats.Resist(new((int)Math.Round(amount, MidpointRounding.AwayFromZero), packet.Kind));
            foreach (ActiveContribution c in At(ContributionStage.Applying, packet.Kind)) applying = c.Contribution.Change(applying);
            ActiveContribution[] defeating = At(ContributionStage.Defeating, packet.Kind).ToArray();
            taken += target.Stats.Apply(applying, packet.Kind, ActorStats.HealthTrack, defeating.Length == 0 ? null : () =>
            {
                // The strongest saving contribution is used, and spent.
                ActiveContribution saving = defeating.MaxBy(c => c.Contribution.Retain)!;
                saving.Spend?.Invoke();
                return saving.Contribution.Retain;
            });
        }
        if (target.Alive)
        {
            foreach (string effect in action.Effects) target.Stats.Effects.Apply(mechanics.Effect(effect)!, $"action.{action.Id}", origin ?? user.Position);
            if (action.Damage.Length > 0)
                foreach (string effect in user.HitEffects) target.Stats.Effects.Apply(mechanics.Effect(effect)!, $"held.{effect}", origin ?? user.Position);
        }
        return taken;
    }

    private SpatialHit Cast(IActionActor user, Vector3 aim, float range, IActionActor[] living) =>
        engine.Spatial.CastRay(new(session, user.Eye, aim, range, Everything, living.Select(t => t.Hitbox).ToArray(),
            new[] { user.Entity }, living.Select(t => t.Hitbox).ToArray()));

    // The body a query met, if any, takes the hit; a surface or nothing ends the action there.
    private ActionImpact First(ActionDefinition action, IActionActor user, SpatialHit hit, Vector3 miss, IActionActor[] living)
    {
        IActionActor? target = hit.Present && hit.Kind == SpatialHitKind.Entity ? living.FirstOrDefault(t => t.Entity == hit.Entity) : null;
        if (target is null) return new(action, user, null, hit.Present ? hit.Point : miss, 0, false, hit.Present);
        int? taken = Hit(action, user, target);
        return new(action, user, target, hit.Point, taken ?? 0, !target.Alive, TurnedAside: taken is null);
    }

    // Every body whose box overlaps the area's box and has a clear line from its centre.
    private ActionImpact[] Area(ActionDefinition action, IActionActor user, Vector3 centre, IActionActor[] living)
    {
        Vector3 reach = new(action.Delivery.Radius);
        List<ActionImpact> impacts = [];
        foreach (IActionActor target in living)
        {
            bool within = engine.Spatial.OverlapAabb(new(session, centre - reach, centre + reach, Vector3.Zero, Everything,
                new[] { target.Hitbox }, new[] { user.Entity })).Present;
            if (!within || Vector3.Distance(target.Position, centre) > action.Delivery.Radius + MathF.Max(target.Hitbox.Max.X - target.Hitbox.Min.X, 0) / 2
                || !Clear(centre, target.Eye)) continue;
            int? taken = Hit(action, user, target, centre);
            impacts.Add(new(action, user, target, target.Eye, taken ?? 0, !target.Alive, TurnedAside: taken is null));
        }
        return impacts.Count == 0 ? [new(action, user, null, centre, 0, false)] : impacts.ToArray();
    }
}
