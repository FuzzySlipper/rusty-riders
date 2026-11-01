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

    // Turns each sweep's blade on by admitted seconds, one query per stretch of its path the step covers (a corner of the
    // path is not cut): the bodies it passes are struck as it reaches them, a surface met first stops it, and one that
    // ends having met nothing misses where it ended.
    private List<ActionImpact> Turn(float seconds, IReadOnlyList<IActionActor> bodies)
    {
        List<ActionImpact> impacts = [];
        foreach (MeleeSweep sweep in sweeps.ToArray())
        {
            MeleeHit hit = sweep.Action.Delivery.Melee;
            float from = sweep.Progress;
            sweep.Elapsed += seconds;
            float to = sweep.Progress;
            IActionActor[] living = bodies.Where(t => t.Alive && t.Entity != sweep.User.Entity && sweep.Eligible.Contains(t.Entity)
                && !sweep.Struck.Contains(t.Entity)).ToArray();
            bool over = false;
            for (int k = 1; k < hit.Path!.Length && !over; k++)
            {
                float start = Math.Max(from, hit.Knot(k - 1)), end = Math.Min(to, hit.Knot(k));
                if (end > start) over = Cut(sweep, start, end, living, impacts);
            }
            if (!over && to >= 1)
            {
                over = true;
                if (sweep.Struck.Count == 0)
                    impacts.Add(new(sweep.Action, sweep.User, null, Tip(sweep, 1), 0, false));
            }
            if (over) sweeps.Remove(sweep);
        }
        return impacts;
    }

    // Turns a sweep's blade from one point of its path to another; whether the blow is over (its limit struck, or a surface met).
    private bool Cut(MeleeSweep sweep, float from, float to, IActionActor[] living, List<ActionImpact> impacts)
    {
        ActionDefinition action = sweep.Action;
        ActionDelivery d = action.Delivery;
        Vector3 eye = sweep.User.Eye;
        SpatialContact[] contacts = engine.Spatial.SweepContacts(SpatialContactQueryRequest.Sweep(session,
            SpatialShape.Capsule(Vector3.Zero, -Vector3.UnitZ * d.Range, d.Width / 2),
            new SpatialPose(eye, Bearing.Along(sweep.Aim, d.Melee.Path!, from)),
            new SpatialPose(eye, Bearing.Along(sweep.Aim, d.Melee.Path!, to)),
            living.Select(t => t.Hitbox).ToArray(), ignoredEntities: new[] { sweep.User.Entity }, filter: Everything)).Contacts.ToArray();
        foreach (SpatialContact contact in contacts)
        {
            if (contact.Kind == SpatialHitKind.Entity)
            {
                if (living.FirstOrDefault(t => t.Entity == contact.Entity) is not { } target || !sweep.Struck.Add(target.Entity)) continue;
                int? taken = Hit(action, sweep.User, target);
                impacts.Add(new(action, sweep.User, target, contact.Point, taken ?? 0, !target.Alive, TurnedAside: taken is null));
                if (sweep.Struck.Count >= d.Melee.Limit) return true;
            }
            // A blade that starts a step inside a surface (its user hugging a wall) passes through it.
            else if (!contact.StartSolid)
            {
                if (sweep.Struck.Count == 0) impacts.Add(new(action, sweep.User, null, contact.Point, 0, false, Surface: true));
                return true;
            }
        }
        return false;
    }

    // Where a sweep's blade tip is at a point of its path.
    private static Vector3 Tip(MeleeSweep sweep, float t) =>
        sweep.User.Eye + Vector3.Transform(-Vector3.UnitZ * sweep.Action.Delivery.Range, Bearing.Along(sweep.Aim, sweep.Action.Delivery.Melee.Path!, t));

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
