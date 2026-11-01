using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.FirstPerson;
using RustyRiders.Game.Actions;
using RustyRiders.Game.Content;
using RustyRiders.Game.Held;
using RustyRiders.Game.Items;
using RustyRiders.Game.Mechanics;
using RustyRiders.Game.Player;

namespace RustyRiders.Game.Combat;

/// <summary>
/// The player's fighting: one weapon at a time in the right hand, taken from those the inventory carries, running its
/// action through the shared pipeline, costs (charge, ammunition, rounds in a magazine) and reloads, training dummies to hit, and what it all looks like (held
/// weapons through <see cref="HeldView"/>, projectiles, impact bursts) and says on the HUD. A use returns the world seconds it
/// costs; the caller buys them from gameplay time. Hits resolve in the current scene's spatial session.
/// </summary>
internal sealed class PlayerCombat : IDisposable
{
    private const ulong FirstDummyEntity = 10_000;
    // The two held looks and the arms are the first objects; projectiles and dummies follow.
    private const ulong FirstObjectId = 3_000_000;

    private readonly IEngineContext engine;
    private readonly CombatDefinition definition;
    private readonly MechanicsDefinition mechanics;
    private readonly PlayerVitals vitals;
    private readonly Walker walker;
    private readonly PlayerActor actor;
    // The weapon's action timing: its action in progress, its aim and its cooldowns.
    private readonly ActionUser user = new();
    private ItemDefinition weapon = null!;
    // The weapon's action moments since the developer readout last took them: its landing and its events' cues, each
    // with its action and the seconds into it.
    private readonly List<(string Action, string Moment, float At)> moments = [];
    private readonly Inventory inventory;
    private readonly Dictionary<string, int> loaded = [];
    private readonly List<TrainingDummy> dummies = [];
    private readonly List<Appearance> appearances = [];
    private readonly HeldView held;
    private readonly Appearance projectile, dummy;
    private ActionResolution? resolution;
    private ulong nextDummy = FirstDummyEntity, bursts;
    private string notice = "";
    private double noticeLeft;

    internal PlayerCombat(IEngineContext engine, CombatDefinition definition, MechanicsDefinition mechanics, PlayerVitals vitals, Walker walker,
        float radius, Inventory inventory)
    {
        this.inventory = inventory;
        this.engine = engine;
        this.definition = definition;
        this.mechanics = mechanics;
        this.vitals = vitals;
        this.walker = walker;
        actor = new PlayerActor(walker, vitals, radius);
        held = new HeldView(engine, definition.Held, FirstObjectId);
        projectile = Primitive(PrimitiveGeometry.Sphere, [1f, .7f, .35f]);
        dummy = Primitive(PrimitiveGeometry.Cube, definition.Dummy.Color);
        Reset();
    }

    internal IActionActor Actor => actor;
    internal IReadOnlyList<TrainingDummy> Dummies => dummies;
    /// <summary>Whether the weapon's action is under way (what needs the hands free waits: taking, using, swapping).</summary>
    internal bool Busy => user.Busy;
    internal HeldView View => held;
    internal ActionDefinition? Action(string id) => definition.Actions.Action(id);

    /// <summary>The scene's session is where hits now resolve; shots in flight and dummies stay behind in the old one.</summary>
    internal void Enter(SpatialSession session)
    {
        resolution = new ActionResolution(engine, session, mechanics);
        dummies.Clear();
        user.Interrupt();
        held.Reset();
    }

    /// <summary>The kit as it starts: its weapon in hand and every magazine full.</summary>
    internal void Reset()
    {
        user.Reset();
        held.Reset();
        resolution?.Clear();
        weapon = definition.Items.Item(definition.Kit.Hand)!;
        loaded.Clear();
        notice = "";
    }

    /// <summary>Whether the weapon in hand takes both hands: the off hand holds it too (otherwise it holds nothing).</summary>
    internal bool TwoHanded => weapon.Weapon!.TwoHanded;

    /// <summary>Takes carried weapon <paramref name="index"/> in hand, putting away the one held.</summary>
    internal void Select(int index)
    {
        if (index < 0 || index >= inventory.Weapons.Count || Busy) return;
        weapon = inventory.Weapons[index];
    }

    /// <summary>Whether <paramref name="item"/> is the weapon in hand.</summary>
    internal bool Holds(ItemDefinition item) => weapon == item;

    /// <summary>The weapon in hand and, for a gun, its rounds loaded and magazine size.</summary>
    internal (ItemDefinition Item, int Loaded, int Size) Held => (weapon, Loaded(weapon), weapon.Weapon!.Magazine?.Size ?? 0);

    /// <summary>The weapon's action under way: its phase label and how far through it is (0 to 1), or none.</summary>
    internal (string Label, float Progress)? Acting => user.Current is { } current
        ? (user.Phase == ActionPhase.Windup ? current.WindupLabel : current.CommitLabel, user.PhaseProgress) : null;

    /// <summary>Uses the weapon in hand. Returns the world seconds the action costs, or null when it could not start.</summary>
    internal float? Use()
    {
        ActionDefinition action = definition.Actions.Action(weapon.Weapon!.Action)!;
        if (user.Readiness(action) != ActionRefusal.None) return null;
        if (action.Cost.Rounds > Loaded(weapon))
            return Reload() ?? Notice(definition.Text.Empty);
        foreach ((string track, int amount) in action.Cost.Tracks)
            if (vitals.Stats.Track(track).ValueInt < amount)
                return Notice(Template.Fill(definition.Text.Short, ("track", mechanics.Tracks.First(t => t.Id == track).Name)));
        foreach ((string track, int amount) in action.Cost.Tracks) vitals.Stats.Track(track).Spend(amount);
        if (action.Cost.Rounds > 0) loaded[weapon.Id] -= action.Cost.Rounds;
        return Begin(action);
    }

    /// <summary>Reloads the gun in hand from its ammunition. Returns the world seconds it costs, or null when there is nothing to do.</summary>
    internal float? Reload()
    {
        if (weapon.Weapon is not { Reload: { } reload, Magazine: { } magazine } || user.Busy) return null;
        if (Loaded(weapon) >= magazine.Size || vitals.Stats.Track(magazine.Track).ValueInt <= 0) return null;
        return Begin(definition.Actions.Action(reload)!);
    }

    /// <summary>A training dummy standing at <paramref name="feet"/> (developer setups).</summary>
    internal TrainingDummy AddDummy(Vector3 feet)
    {
        TrainingDummy added = new(nextDummy++, definition.Dummy, mechanics, feet);
        dummies.Add(added);
        return added;
    }

    /// <summary>
    /// Advances by world seconds: the weapon's action in progress (landing it when its windup ends), shots in flight, and
    /// the dummies' effects. <paramref name="others"/> are the other bodies the player's actions may hit (enemies).
    /// </summary>
    internal void Step(float seconds, IReadOnlyList<IActionActor> others)
    {
        held.Step(seconds);
        if (resolution is null) return;
        IActionActor[] targets = [.. dummies.Where(d => d.Alive), .. others];
        ActionDefinition? acting = user.Current;
        ActionDefinition? landed = user.Step(seconds);
        foreach (ActionEvent cue in user.Cued) Moment(acting!, cue.Cue.ToString(), cue.Time(acting!.Timing, user.Scale));
        if (landed is not null)
        {
            Moment(landed, "Landing", landed.Timing.Windup * user.Scale);
            if (landed.Reloads) Refill(weapon);
            else Feedback(resolution.Land(landed, actor, user.Aim, targets, user.Scale), landedNow: true);
        }
        Feedback(resolution.Step(seconds, [.. targets, actor]), landedNow: false);
        foreach (TrainingDummy target in dummies) target.Stats.Effects.Advance(seconds);
        dummies.RemoveAll(d => !d.Alive);
    }

    /// <summary>The weapon's action moments (its landing and its events' cues) since this was last read, taken.</summary>
    internal IReadOnlyList<(string Action, string Moment, float At)> TakeMoments()
    {
        var taken = moments.ToArray();
        moments.Clear();
        return taken;
    }

    // A moment the weapon's action passed, kept for the developer readout (the last few only).
    private void Moment(ActionDefinition action, string moment, float at)
    {
        if (moments.Count >= 32) moments.RemoveAt(0);
        moments.Add((action.Id, moment, at));
    }

    /// <summary>Counts down the hit notice in host seconds (it is presentation, not world state).</summary>
    internal void Present(double hostSeconds)
    {
        noticeLeft = Math.Max(0, noticeLeft - hostSeconds);
        Vector3 velocity = walker.Motion.ControlledVelocity;
        held.Present(hostSeconds, walker.Feet, new Vector2(velocity.X, velocity.Z).Length(), !walker.Airborne && !walker.Flying, walker.Speeds);
    }

    /// <summary>Held weapons (camera space, viewmodel layer), projectiles and dummies as they stand now.</summary>
    internal IEnumerable<AppearanceFact> Facts()
    {
        CombatPresentation p = definition.Presentation;
        foreach (AppearanceFact fact in held.Facts(weapon.Weapon!.Look, TwoHanded, walker.Flying is false, user))
            yield return fact;
        ulong id = FirstObjectId + 4;
        foreach (Projectile shot in resolution?.Projectiles ?? [])
            yield return new AppearanceFact(id++, false, 0, new Transform(shot.Position, Quaternion.Identity, new Vector3(p.ProjectileSize)),
                projectile, true, RenderLayer.Scene);
        foreach (TrainingDummy target in dummies)
            yield return new AppearanceFact(id++, false, 0, target.Transform, dummy, true, RenderLayer.Scene);
    }

    /// <summary>The weapon in hand and its load, the carried ammunition and charge, the action in progress and the latest hit.</summary>
    internal (string Hand, string Supplies, string Action, string Notice) Hud()
    {
        CombatMessages text = definition.Text;
        string hand = weapon.Weapon!.Magazine is { } magazine
            ? $"{weapon.Name} {Template.Fill(text.Loaded, ("loaded", Loaded(weapon)), ("size", magazine.Size))}"
            : weapon.Name;
        string supplies = string.Join("  ", mechanics.Tracks.Where(t => t.Id != ActorStats.HealthTrack).Select(t =>
            Template.Fill(text.Carried, ("name", t.Name), ("value", vitals.Stats.Track(t.Id).ValueInt))));
        return (hand, supplies, Acting?.Label ?? "", noticeLeft > 0 ? notice : "");
    }

    /// <summary>Each held look's actions, from the weapons that show it (the developer motion viewer's choices).</summary>
    internal IReadOnlyDictionary<string, string[]> LookUses() => definition.Items.Items.Where(i => i.Weapon is not null)
        .GroupBy(i => i.Weapon!.Look)
        .ToDictionary(g => g.Key, g => g.SelectMany(i => new[] { i.Weapon!.Action, i.Weapon.Reload }).OfType<string>().Distinct().ToArray());

    /// <summary>
    /// Under rusty dev an edited held-presentation file (declared live content) arrives here instead of a restart: the
    /// files are read again, validated as at load, and the changed ones' values taken into the catalog the view holds.
    /// Returns what needs a restart, or why the content did not load (the last usable values are kept).
    /// </summary>
    internal string[] ReloadHeld(IReadOnlySet<string> changed)
    {
        try
        {
            HeldCatalog fresh = HeldCatalog.Load(engine);
            CombatDefinition.ValidateHeld(fresh, definition.Items, definition.Actions);
            return definition.Held.Refresh(fresh, changed);
        }
        catch (Exception error) when (error is InvalidOperationException or EngineCallException)
        {
            return [$"not reloaded: {error.Message}"];
        }
    }

    /// <summary>Once the held looks are in the published snapshot, their motion follows the action in progress.</summary>
    internal void Animate() => held.Animate(user);

    /// <summary>Converted weapon models that could not be opened (they fall back to their boxes).</summary>
    internal IReadOnlyList<string> Problems => held.Problems;

    public void Dispose()
    {
        held.Dispose();
        foreach (Appearance appearance in appearances) appearance.Dispose();
    }

    // A gun comes with a full magazine the first time it is held.
    private int Loaded(ItemDefinition item) =>
        loaded.TryGetValue(item.Id, out int rounds) ? rounds : loaded[item.Id] = item.Weapon!.Magazine?.Size ?? 0;

    private float Begin(ActionDefinition action)
    {
        float scale = (float)vitals.Stats.Stat(ActorStats.ActionTimeStat).Value;
        return user.Begin(action, walker.Forward, scale);
    }

    private void Refill(ItemDefinition item)
    {
        MagazineDefinition magazine = item.Weapon!.Magazine!;
        Rusty.Engine.Mechanics.Track ammunition = vitals.Stats.Track(magazine.Track);
        int rounds = Math.Min(magazine.Size - Loaded(item), ammunition.ValueInt);
        ammunition.Spend(rounds);
        loaded[item.Id] = Loaded(item) + rounds;
    }

    /// <summary>Shows a notice (an item found or used) in the hit notice's place.</summary>
    internal void Announce(string text) => Notice(text);

    private float? Notice(string text)
    {
        notice = text;
        noticeLeft = definition.Presentation.NoticeSeconds;
        return null;
    }

    // A burst where each landed action ended, and a notice of what the player's own actions did. landedNow is whether
    // the weapon's action landed now (not for shots in flight; a melee sweep's blow, turning across the commit, is still
    // the weapon's action landing).
    private void Feedback(ActionImpact[] impacts, bool landedNow)
    {
        foreach (ActionImpact impact in impacts)
        {
            bool now = landedNow || Swept(impact);
            if (impact.Action.Delivery.Kind != DeliveryKind.Self) Burst(impact.End);
            if (impact.User == actor && now && Felt(impact) is { } kind) held.Feel(HeldView.MainHand, kind);
            if (impact.User != actor || (!now && impact.Target is null && !impact.Surface)) continue;
            string name = impact.Target is INamed named ? named.Name : "";
            CombatMessages text = definition.Text;
            Notice(impact.Target is null ? text.Missed
                : impact.TurnedAside ? Template.Fill(text.TurnedAside, ("target", name))
                : impact.Defeated ? Template.Fill(text.Defeated, ("target", name))
                : Template.Fill(text.Hit, ("target", name), ("damage", impact.Damage)));
        }
    }

    // Whether an impact is a blow of the weapon's melee sweep, still committing.
    private bool Swept(ActionImpact impact) =>
        impact.User == actor && impact.Action.Delivery.Kind == DeliveryKind.Melee && user.Current == impact.Action;

    // What the acting hand feels as its action lands: a shot or cast fires; a blow strikes a body or is stopped by a surface.
    private static FeedbackKind? Felt(ActionImpact impact) => impact.Action.Delivery.Kind switch
    {
        DeliveryKind.Self => null,
        DeliveryKind.Melee => impact.Target is not null ? FeedbackKind.Struck : impact.Surface ? FeedbackKind.Blocked : null,
        _ => FeedbackKind.Fired
    };

    private void Burst(Vector3 at)
    {
        CombatPresentation p = definition.Presentation;
        try
        {
            engine.Presentation.EmitParticles(new PresentationParticleDescriptor
            {
                SignalId = "impact",
                Visible = true,
                Anchor = new PresentationAnchor { Kind = PresentationAnchorKind.World, Position = at },
                Visual = PresentationParticleVisual.Cube,
                BurstCount = (uint)p.ImpactBurst,
                MaxParticles = (uint)p.ImpactBurst,
                LifetimeMinSeconds = p.ImpactSeconds * .6f,
                LifetimeMaxSeconds = p.ImpactSeconds,
                VelocityMin = new Vector3(-2, -1, -2),
                VelocityMax = new Vector3(2, 3, 2),
                SizeCurve = new PresentationParticleScalarKey[] { new() { Age = 0, Value = p.ImpactSize }, new() { Age = 1, Value = 0 } },
                ColorCurve = new PresentationParticleColorKey[]
                {
                    new() { Age = 0, Color = new Color(1, .85f, .5f, 1) }, new() { Age = 1, Color = new Color(1, .4f, .1f, 0) },
                },
                Seed = ++bursts,
            });
        }
        catch (EngineCallException)
        {
            // Over the shared particle budget: the hit still counts, it just shows no burst.
        }
    }

    private Appearance Primitive(PrimitiveGeometry geometry, float[] rgb)
    {
        Appearance made = engine.Graphics.CreatePrimitive(new PrimitiveAppearanceRequest(geometry, false, Authored.Color(rgb)));
        appearances.Add(made);
        return made;
    }
}

/// <summary>An actor with a name the HUD can show (enemies).</summary>
internal interface INamed
{
    string Name { get; }
}
