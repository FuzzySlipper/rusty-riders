using System.Numerics;
using Rusty.Engine;
using RustyRiders.Game.Actions;
using RustyRiders.Game.Content;
using RustyRiders.Game.Held;
using RustyRiders.Game.Items;
using RustyRiders.Game.Mechanics;
using RustyRiders.Game.Player;

namespace RustyRiders.Game.Combat;

/// <summary>
/// The player's fighting: two hands holding weapons the inventory carries, each running its own action through the shared
/// pipeline (a hand is busy only with its own action, so one can strike while the other recovers), costs
/// (charge, ammunition, rounds in a magazine) and reloads, training dummies to hit, and what it all looks like (held
/// weapons through <see cref="HeldView"/>, projectiles, impact bursts) and says on the HUD. A use returns the world seconds it
/// costs; the caller buys them from gameplay time. Hits resolve in the current scene's spatial session.
/// </summary>
internal sealed class PlayerCombat : IDisposable
{
    internal const int MainHand = HeldView.MainHand, OffHand = HeldView.OffHand;
    private const ulong FirstDummyEntity = 10_000;
    // The two held looks and the arms are the first objects; projectiles and dummies follow.
    private const ulong FirstObjectId = 3_000_000;

    private readonly IEngineContext engine;
    private readonly CombatDefinition definition;
    private readonly MechanicsDefinition mechanics;
    private readonly PlayerVitals vitals;
    private readonly Walker walker;
    private readonly PlayerActor actor;
    // Each hand's action timing: its action in progress, its aim and its cooldowns.
    private readonly ActionUser[] users = [new(), new()];
    private readonly Hand[] hands = new Hand[2];
    private readonly Inventory inventory;
    private readonly Dictionary<string, int> loaded = [];
    private readonly List<TrainingDummy> dummies = [];
    private readonly List<Appearance> appearances = [];
    private readonly HeldView held;
    private readonly Appearance projectile, dummy;
    private ActionResolution? resolution;
    // The hand whose action began last (the HUD's progress follows it).
    private int latestHand = MainHand;
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
    /// <summary>Whether either hand's action is under way (what needs both hands free waits: taking, using, swapping).</summary>
    internal bool Busy => users.Any(u => u.Busy);
    internal HeldView View => held;
    internal ActionDefinition? Action(string id) => definition.Actions.Action(id);

    /// <summary>The scene's session is where hits now resolve; shots in flight and dummies stay behind in the old one.</summary>
    internal void Enter(SpatialSession session)
    {
        resolution = new ActionResolution(engine, session, mechanics);
        dummies.Clear();
        foreach (ActionUser user in users) user.Interrupt();
        held.Reset();
    }

    /// <summary>The kit as it starts: its weapons in hand and every magazine full.</summary>
    internal void Reset()
    {
        foreach (ActionUser user in users) user.Reset();
        held.Reset();
        resolution?.Clear();
        hands[MainHand] = new Hand(definition.Items.Item(definition.Kit.MainHand)!);
        hands[OffHand] = new Hand(definition.Items.Item(definition.Kit.OffHand)!);
        loaded.Clear();
        notice = "";
    }

    /// <summary>
    /// Whether the main hand's weapon takes both hands: the off hand holds it too, and the off hand's own weapon is stowed
    /// (kept, hidden and unused) until the main hand holds a one-handed weapon again.
    /// </summary>
    internal bool TwoHanded => hands[MainHand].Item.Weapon!.TwoHanded;

    /// <summary>Takes carried weapon <paramref name="index"/> into the main hand (swapping hands if it is in the off hand).</summary>
    internal void Select(int index)
    {
        if (index < 0 || index >= inventory.Weapons.Count || Busy) return;
        ItemDefinition item = inventory.Weapons[index];
        if (hands[OffHand].Item == item) hands[OffHand] = hands[MainHand];
        hands[MainHand] = new Hand(item);
    }

    /// <summary>Puts carried weapon <paramref name="index"/> in a hand (the hands swap when it is in the other).</summary>
    internal void Hold(int index, int hand)
    {
        if (index < 0 || index >= inventory.Weapons.Count || Busy || hand is not (MainHand or OffHand)) return;
        ItemDefinition item = inventory.Weapons[index];
        if (item.Weapon!.TwoHanded) hand = MainHand; // held in both hands, it is the main hand's
        int other = 1 - hand;
        if (hands[other].Item == item) hands[other] = hands[hand];
        hands[hand] = new Hand(item);
    }

    /// <summary>Which hand holds carried weapon <paramref name="item"/>, or -1 (a weapon stowed for a two-handed one is in no hand).</summary>
    internal int HandOf(ItemDefinition item) => hands[MainHand].Item == item ? MainHand : hands[OffHand].Item == item && !TwoHanded ? OffHand : -1;

    /// <summary>A hand's weapon (both hands', for a two-handed one) and, for a gun, its rounds loaded and magazine size.</summary>
    internal (ItemDefinition Item, int Loaded, int Size) Held(int hand)
    {
        ItemDefinition item = Holding(hand);
        return (item, Loaded(item), item.Weapon!.Magazine?.Size ?? 0);
    }

    // What a hand holds: its own weapon, or for the off hand under a two-handed weapon, that weapon.
    private ItemDefinition Holding(int hand) => hand == OffHand && TwoHanded ? hands[MainHand].Item : hands[hand].Item;

    /// <summary>A hand's action under way: its phase label and how far through it is (0 to 1), or none.</summary>
    internal (string Label, float Progress)? Acting(int hand) => users[hand] is { Current: { } current } user
        ? (user.Phase == ActionPhase.Windup ? current.WindupLabel : current.CommitLabel, user.PhaseProgress) : null;

    /// <summary>The action under way that began last, with its hand, or none.</summary>
    internal (int Hand, string Label, float Progress)? Latest =>
        Acting(latestHand) is { } latest ? (latestHand, latest.Label, latest.Progress)
        : Acting(1 - latestHand) is { } other ? (1 - latestHand, other.Label, other.Progress) : null;

    /// <summary>Uses a hand's weapon. Returns the world seconds the action costs, or null when it could not start.</summary>
    internal float? Use(int hand)
    {
        if (hand == OffHand && TwoHanded) return null; // it holds the main hand's weapon, which the main hand uses
        WeaponDefinition weapon = hands[hand].Item.Weapon!;
        ActionDefinition action = definition.Actions.Action(weapon.Action)!;
        if (users[hand].Readiness(action) != ActionRefusal.None) return null;
        if (action.Cost.Rounds > Loaded(hands[hand].Item))
            return Reload(hand) ?? Notice(definition.Text.Empty);
        foreach ((string track, int amount) in action.Cost.Tracks)
            if (vitals.Stats.Track(track).ValueInt < amount)
                return Notice(Template.Fill(definition.Text.Short, ("track", mechanics.Tracks.First(t => t.Id == track).Name)));
        foreach ((string track, int amount) in action.Cost.Tracks) vitals.Stats.Track(track).Spend(amount);
        if (action.Cost.Rounds > 0) loaded[hands[hand].Item.Id] -= action.Cost.Rounds;
        return Begin(action, hand);
    }

    /// <summary>Reloads a hand's gun from its ammunition. Returns the world seconds it costs, or null when there is nothing to do.</summary>
    internal float? Reload(int hand)
    {
        ItemDefinition item = hands[hand].Item;
        if (hand == OffHand && TwoHanded) return null;
        if (item.Weapon is not { Reload: { } reload, Magazine: { } magazine } || users[hand].Busy) return null;
        if (Loaded(item) >= magazine.Size || vitals.Stats.Track(magazine.Track).ValueInt <= 0) return null;
        return Begin(definition.Actions.Action(reload)!, hand);
    }

    /// <summary>A training dummy standing at <paramref name="feet"/> (developer setups).</summary>
    internal TrainingDummy AddDummy(Vector3 feet)
    {
        TrainingDummy added = new(nextDummy++, definition.Dummy, mechanics, feet);
        dummies.Add(added);
        return added;
    }

    /// <summary>
    /// Advances by world seconds: each hand's action in progress (landing it when its windup ends), shots in flight, and
    /// the dummies' effects. <paramref name="others"/> are the other bodies the player's actions may hit (enemies).
    /// </summary>
    internal void Step(float seconds, IReadOnlyList<IActionActor> others)
    {
        if (resolution is null) return;
        IActionActor[] targets = [.. dummies.Where(d => d.Alive), .. others];
        for (int hand = 0; hand < users.Length; hand++)
        {
            if (users[hand].Step(seconds) is not { } landed) continue;
            if (landed.Reloads) Refill(hands[hand].Item);
            else Feedback(resolution.Land(landed, actor, users[hand].Aim, targets), hand);
        }
        Feedback(resolution.Step(seconds, [.. targets, actor]), null);
        foreach (TrainingDummy target in dummies) target.Stats.Effects.Advance(seconds);
        dummies.RemoveAll(d => !d.Alive);
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
        foreach (AppearanceFact fact in held.Facts(hands[MainHand].Item.Weapon!.Look, hands[OffHand].Item.Weapon!.Look, TwoHanded, walker.Flying is false))
            yield return fact;
        ulong id = FirstObjectId + 4;
        foreach (Projectile shot in resolution?.Projectiles ?? [])
            yield return new AppearanceFact(id++, false, 0, new Transform(shot.Position, Quaternion.Identity, new Vector3(p.ProjectileSize)),
                projectile, true, RenderLayer.Scene);
        foreach (TrainingDummy target in dummies)
            yield return new AppearanceFact(id++, false, 0, target.Transform, dummy, true, RenderLayer.Scene);
    }

    /// <summary>The hands' weapons and loads, the carried ammunition and charge, the action in progress and the latest hit.</summary>
    internal (string Hands, string Supplies, string Action, string Notice) Hud()
    {
        CombatMessages text = definition.Text;
        string Describe(Hand hand) => hand.Item.Weapon!.Magazine is { } magazine
            ? $"{hand.Item.Name} {Template.Fill(text.Loaded, ("loaded", Loaded(hand.Item)), ("size", magazine.Size))}"
            : hand.Item.Name;
        string supplies = string.Join("  ", mechanics.Tracks.Where(t => t.Id != ActorStats.HealthTrack).Select(t =>
            Template.Fill(text.Carried, ("name", t.Name), ("value", vitals.Stats.Track(t.Id).ValueInt))));
        string action = string.Join("  ·  ", new[] { MainHand, OffHand }.Select(Acting).OfType<(string Label, float Progress)>().Select(a => a.Label));
        return ($"{Describe(hands[MainHand])}  ·  {Describe(hands[OffHand])}", supplies, action, noticeLeft > 0 ? notice : "");
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
    internal void Animate() => held.Animate(users);

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

    private float Begin(ActionDefinition action, int hand)
    {
        latestHand = hand;
        float scale = (float)vitals.Stats.Stat(ActorStats.ActionTimeStat).Value;
        return users[hand].Begin(action, walker.Forward, scale);
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

    // A burst where each landed action ended, and a notice of what the player's own actions did.
    // landedBy is the hand whose action landed now (null for shots in flight).
    private void Feedback(ActionImpact[] impacts, int? landedBy)
    {
        bool landedNow = landedBy is not null;
        foreach (ActionImpact impact in impacts)
        {
            if (impact.Action.Delivery.Kind != DeliveryKind.Self) Burst(impact.End);
            if (impact.User == actor && landedBy is { } hand && Felt(impact) is { } kind) held.Feel(hand, kind);
            if (impact.User != actor || (!landedNow && impact.Target is null && !impact.Surface)) continue;
            string name = impact.Target is INamed named ? named.Name : "";
            CombatMessages text = definition.Text;
            Notice(impact.Target is null ? text.Missed
                : impact.TurnedAside ? Template.Fill(text.TurnedAside, ("target", name))
                : impact.Defeated ? Template.Fill(text.Defeated, ("target", name))
                : Template.Fill(text.Hit, ("target", name), ("damage", impact.Damage)));
        }
    }

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

    private sealed record Hand(ItemDefinition Item);
}

/// <summary>An actor with a name the HUD can show (enemies).</summary>
internal interface INamed
{
    string Name { get; }
}
