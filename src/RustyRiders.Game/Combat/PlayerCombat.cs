using System.Numerics;
using Rusty.Engine;
using RustyRiders.Game.Actions;
using RustyRiders.Game.Content;
using RustyRiders.Game.Items;
using RustyRiders.Game.Mechanics;
using RustyRiders.Game.Player;

namespace RustyRiders.Game.Combat;

/// <summary>
/// The player's fighting: two hands holding carried weapons, one action at a time through the shared pipeline, costs
/// (charge, ammunition, rounds in a magazine) and reloads, training dummies to hit, and what it all looks like (held
/// weapons on the viewmodel layer, projectiles, impact bursts) and says on the HUD. A use returns the world seconds it
/// costs; the caller buys them from gameplay time. Hits resolve in the current scene's spatial session.
/// </summary>
internal sealed class PlayerCombat : IDisposable
{
    internal const int MainHand = 0, OffHand = 1;
    private const ulong FirstDummyEntity = 10_000;
    private const ulong FirstObjectId = 3_000_000;

    private readonly IEngineContext engine;
    private readonly CombatDefinition definition;
    private readonly MechanicsDefinition mechanics;
    private readonly PlayerVitals vitals;
    private readonly Walker walker;
    private readonly PlayerActor actor;
    private readonly ActionUser user = new();
    private readonly Hand[] hands = new Hand[2];
    private readonly ItemDefinition[] carried;
    private readonly Dictionary<string, int> loaded = [];
    private readonly List<TrainingDummy> dummies = [];
    private readonly List<Appearance> appearances = [];
    private readonly Dictionary<string, Appearance> held = [];
    private readonly Appearance projectile, dummy;
    private ActionResolution? resolution;
    private int actingHand = MainHand;
    private ulong nextDummy = FirstDummyEntity, bursts;
    private string notice = "";
    private double noticeLeft;

    internal PlayerCombat(IEngineContext engine, CombatDefinition definition, MechanicsDefinition mechanics, PlayerVitals vitals, Walker walker, float radius)
    {
        this.engine = engine;
        this.definition = definition;
        this.mechanics = mechanics;
        this.vitals = vitals;
        this.walker = walker;
        actor = new PlayerActor(walker, vitals, radius);
        carried = definition.Kit.Carried.Select(id => definition.Items.Item(id)!).ToArray();
        foreach (ItemDefinition item in carried)
            held[item.Id] = Primitive(PrimitiveGeometry.Cube, item.Weapon!.Look.Color);
        projectile = Primitive(PrimitiveGeometry.Sphere, [1f, .7f, .35f]);
        dummy = Primitive(PrimitiveGeometry.Cube, definition.Dummy.Color);
        Reset();
    }

    internal IActionActor Actor => actor;
    internal IReadOnlyList<TrainingDummy> Dummies => dummies;
    internal bool Busy => user.Busy;

    /// <summary>The scene's session is where hits now resolve; shots in flight and dummies stay behind in the old one.</summary>
    internal void Enter(SpatialSession session)
    {
        resolution = new ActionResolution(engine, session, mechanics);
        dummies.Clear();
        user.Interrupt();
    }

    /// <summary>The kit as it starts: its weapons in hand and every magazine full.</summary>
    internal void Reset()
    {
        user.Reset();
        resolution?.Clear();
        hands[MainHand] = new Hand(definition.Items.Item(definition.Kit.MainHand)!);
        hands[OffHand] = new Hand(definition.Items.Item(definition.Kit.OffHand)!);
        foreach (ItemDefinition item in carried)
            if (item.Weapon!.Magazine is { } magazine) loaded[item.Id] = magazine.Size;
        notice = "";
    }

    /// <summary>Takes carried weapon <paramref name="index"/> into the main hand (swapping hands if it is in the off hand).</summary>
    internal void Select(int index)
    {
        if (index < 0 || index >= carried.Length || user.Busy) return;
        ItemDefinition item = carried[index];
        if (hands[OffHand].Item == item) hands[OffHand] = hands[MainHand];
        hands[MainHand] = new Hand(item);
    }

    /// <summary>Uses a hand's weapon. Returns the world seconds the action costs, or null when it could not start.</summary>
    internal float? Use(int hand)
    {
        WeaponDefinition weapon = hands[hand].Item.Weapon!;
        ActionDefinition action = definition.Actions.Action(weapon.Action)!;
        if (user.Readiness(action) != ActionRefusal.None) return null;
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
        if (item.Weapon is not { Reload: { } reload, Magazine: { } magazine } || user.Busy) return null;
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
    /// Advances by world seconds: the action in progress (landing it when its windup ends), shots in flight, and the
    /// dummies' effects. <paramref name="others"/> are the other bodies the player's actions may hit (enemies).
    /// </summary>
    internal void Step(float seconds, IReadOnlyList<IActionActor> others)
    {
        if (resolution is null) return;
        IActionActor[] targets = [.. dummies.Where(d => d.Alive), .. others];
        if (user.Step(seconds) is { } landed)
        {
            if (landed.Reloads) Refill(hands[actingHand].Item);
            else Feedback(resolution.Land(landed, actor, user.Aim, targets), true);
        }
        Feedback(resolution.Step(seconds, [.. targets, actor]), false);
        foreach (TrainingDummy target in dummies) target.Stats.Effects.Advance(seconds);
        dummies.RemoveAll(d => !d.Alive);
    }

    /// <summary>Counts down the hit notice in host seconds (it is presentation, not world state).</summary>
    internal void Present(double hostSeconds) => noticeLeft = Math.Max(0, noticeLeft - hostSeconds);

    /// <summary>Held weapons (camera space, viewmodel layer), projectiles and dummies as they stand now.</summary>
    internal IEnumerable<AppearanceFact> Facts()
    {
        ulong id = FirstObjectId;
        CombatPresentation p = definition.Presentation;
        for (int h = 0; h < hands.Length; h++)
        {
            HeldLook look = hands[h].Item.Weapon!.Look;
            Vector3 at = Authored.Vector(h == MainHand ? p.MainHand : p.OffHand) + Authored.Vector(look.Offset);
            if (h == actingHand && user.Current is not null)
                at += user.Phase switch
                {
                    ActionPhase.Windup => Authored.Vector(p.Windup) * user.PhaseProgress,
                    ActionPhase.Commit => Authored.Vector(p.Commit),
                    ActionPhase.Recovery => Authored.Vector(p.Commit) * (1 - user.PhaseProgress) - Vector3.UnitY * p.RecoveryDrop * (1 - user.PhaseProgress),
                    _ => Vector3.Zero,
                };
            yield return new AppearanceFact(id++, false, 0, new Transform(at, Quaternion.Identity, Authored.Vector(look.Size)),
                held[hands[h].Item.Id], walker.Flying is false, RenderLayer.Viewmodel);
        }
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
        string action = user.Current is { } current ? user.Phase == ActionPhase.Windup ? current.WindupLabel : current.CommitLabel : "";
        return ($"{Describe(hands[MainHand])}  ·  {Describe(hands[OffHand])}", supplies, action, noticeLeft > 0 ? notice : "");
    }

    public void Dispose()
    {
        foreach (Appearance appearance in appearances) appearance.Dispose();
    }

    private int Loaded(ItemDefinition item) => loaded.GetValueOrDefault(item.Id);

    private float Begin(ActionDefinition action, int hand)
    {
        actingHand = hand;
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

    private float? Notice(string text)
    {
        notice = text;
        noticeLeft = definition.Presentation.NoticeSeconds;
        return null;
    }

    // A burst where each landed action ended, and a notice of what the player's own actions did.
    private void Feedback(ActionImpact[] impacts, bool landedNow)
    {
        foreach (ActionImpact impact in impacts)
        {
            if (impact.Action.Delivery.Kind != DeliveryKind.Self) Burst(impact.End);
            if (impact.User != actor || (!landedNow && impact.Target is null && !impact.Surface)) continue;
            string name = impact.Target is INamed named ? named.Name : "";
            CombatMessages text = definition.Text;
            Notice(impact.Target is null ? text.Missed
                : impact.TurnedAside ? Template.Fill(text.TurnedAside, ("target", name))
                : impact.Defeated ? Template.Fill(text.Defeated, ("target", name))
                : Template.Fill(text.Hit, ("target", name), ("damage", impact.Damage)));
        }
    }

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
