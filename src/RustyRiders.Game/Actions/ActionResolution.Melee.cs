using System.Numerics;
using Rusty.Engine;

namespace RustyRiders.Game.Actions;

/// <summary>Melee blows, each found the way its <see cref="MeleeHit"/> says; sweeps turn across the commit in <see cref="Step"/>.</summary>
internal sealed partial class ActionResolution
{
    // A thrust meeting a surface this close to its start began in contact with it.
    private const double StartContact = .05;
    private readonly List<MeleeSweep> sweeps = [];

    private ActionImpact[] Melee(ActionDefinition action, IActionActor user, Vector3 aim, IActionActor[] living, float commit)
    {
        ActionDelivery d = action.Delivery;
        Vector3 reach = user.Eye + aim * d.Range;
        switch (d.Melee.Kind)
        {
            case MeleeHitKind.Ray:
                return [First(action, user, Cast(user, aim, d.Range, living), reach, living)];
            case MeleeHitKind.Sphere:
                return Sphere(action, user, reach, living);
            case MeleeHitKind.Sweep:
                sweeps.Add(new(action, user, aim, commit, living.Select(t => t.Entity).ToHashSet()));
                return [];
            default:
            {
                SpatialHit hit = engine.Spatial.CastCapsule(new(session, user.Eye, 0, d.Width / 2, aim * d.Range, 0, Everything,
                    living.Select(t => t.Hitbox).ToArray(), new[] { user.Entity }));
                // A user hugging a wall starts the thrust in it; then the blow is judged along its line alone.
                if (hit.Present && hit.Kind != SpatialHitKind.Entity && (hit.StartSolid || hit.Distance < StartContact))
                    hit = Cast(user, aim, d.Range, living);
                return [First(action, user, hit, reach, living)];
            }
        }
    }

    // The nearest bodies (up to the limit) the sphere overlaps with a clear line from the eye.
    private ActionImpact[] Sphere(ActionDefinition action, IActionActor user, Vector3 centre, IActionActor[] living)
    {
        SpatialContact[] contacts = engine.Spatial.OverlapContacts(SpatialContactQueryRequest.Overlap(session,
            SpatialShape.Sphere(action.Delivery.Radius), SpatialPose.At(centre), living.Select(t => t.Hitbox).ToArray(),
            ignoredEntities: new[] { user.Entity }, filter: Everything, excludeWorld: true)).Contacts.ToArray();
        IActionActor[] struck = contacts.Where(c => c.Kind == SpatialHitKind.Entity)
            .Select(c => living.FirstOrDefault(t => t.Entity == c.Entity)).OfType<IActionActor>()
            .Where(t => Clear(user.Eye, t.Eye)).OrderBy(t => Vector3.Distance(user.Eye, t.Eye)).Take(action.Delivery.Melee.Limit).ToArray();
        if (struck.Length == 0) return [new(action, user, null, centre, 0, false)];
        return struck.Select(target =>
        {
            int? taken = Hit(action, user, target);
            return new ActionImpact(action, user, target, target.Eye, taken ?? 0, !target.Alive, TurnedAside: taken is null);
        }).ToArray();
    }

    // Turns each sweep's blade on by admitted seconds: the bodies it passes are struck as it reaches them, a surface
    // met first stops it, and one that ends having met nothing misses where it ended.
    private List<ActionImpact> Turn(float seconds, IReadOnlyList<IActionActor> bodies)
    {
        List<ActionImpact> impacts = [];
        foreach (MeleeSweep sweep in sweeps.ToArray())
        {
            ActionDefinition action = sweep.Action;
            ActionDelivery d = action.Delivery;
            MeleeHit hit = d.Melee;
            float from = sweep.Progress;
            sweep.Elapsed += seconds;
            float to = sweep.Progress;
            Vector3 eye = sweep.User.Eye;
            IActionActor[] living = bodies.Where(t => t.Alive && t.Entity != sweep.User.Entity && sweep.Eligible.Contains(t.Entity)
                && !sweep.Struck.Contains(t.Entity)).ToArray();
            SpatialContact[] contacts = engine.Spatial.SweepContacts(SpatialContactQueryRequest.Sweep(session,
                SpatialShape.Capsule(Vector3.Zero, -Vector3.UnitZ * d.Range, d.Width / 2),
                new SpatialPose(eye, Bearing.Toward(sweep.Aim, hit.From!, hit.To!, from)),
                new SpatialPose(eye, Bearing.Toward(sweep.Aim, hit.From!, hit.To!, to)),
                living.Select(t => t.Hitbox).ToArray(), ignoredEntities: new[] { sweep.User.Entity }, filter: Everything)).Contacts.ToArray();
            bool over = false;
            foreach (SpatialContact contact in contacts)
            {
                if (contact.Kind == SpatialHitKind.Entity)
                {
                    if (living.FirstOrDefault(t => t.Entity == contact.Entity) is not { } target || !sweep.Struck.Add(target.Entity)) continue;
                    int? taken = Hit(action, sweep.User, target);
                    impacts.Add(new(action, sweep.User, target, contact.Point, taken ?? 0, !target.Alive, TurnedAside: taken is null));
                    if (over = sweep.Struck.Count >= hit.Limit) break;
                }
                // A blade that starts a step inside a surface (its user hugging a wall) passes through it.
                else if (!contact.StartSolid)
                {
                    if (sweep.Struck.Count == 0) impacts.Add(new(action, sweep.User, null, contact.Point, 0, false, Surface: true));
                    over = true;
                    break;
                }
            }
            if (!over && to >= 1)
            {
                over = true;
                if (sweep.Struck.Count == 0)
                    impacts.Add(new(action, sweep.User, null, eye + Vector3.Transform(-Vector3.UnitZ * d.Range, Bearing.Toward(sweep.Aim, hit.From!, hit.To!, 1)), 0, false));
            }
            if (over) sweeps.Remove(sweep);
        }
        return impacts;
    }

    /// <summary>A melee sweep turning across its commit: who swings, its aim, how long it turns, and whom it has struck.</summary>
    private sealed class MeleeSweep(ActionDefinition action, IActionActor user, Vector3 aim, float seconds, IReadOnlySet<ulong> eligible)
    {
        internal ActionDefinition Action { get; } = action;
        internal IActionActor User { get; } = user;
        internal Vector3 Aim { get; } = aim;
        internal IReadOnlySet<ulong> Eligible { get; } = eligible;
        internal HashSet<ulong> Struck { get; } = [];
        internal float Elapsed { get; set; }
        internal float Progress => seconds <= 0 ? 1 : Math.Min(1, Elapsed / seconds);
    }
}
