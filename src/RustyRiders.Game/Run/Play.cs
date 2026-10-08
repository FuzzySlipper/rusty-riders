using Rusty.Engine;
using Rusty.Engine.Input;
using RustyRiders.Game.Combat;
using RustyRiders.Game.Content;
using RustyRiders.Game.Enemies;
using RustyRiders.Game.Items;
using RustyRiders.Game.Levels;
using RustyRiders.Game.Mechanics;
using RustyRiders.Game.Player;

namespace RustyRiders.Game.Run;

/// <summary>
/// Playing in a level: the player's vitals, inventory and combat, the level's enemies and pickups, and the controls
/// that act on them. Composes those owners for each world step and each update; the run decides which level, at what
/// depth and reward, and what a fall costs.
/// </summary>
internal sealed class Play : IDisposable
{
    private readonly Walker walker;
    private readonly MechanicsDefinition mechanics;

    internal Play(IEngineContext engine, Walker walker, float bodyRadius)
    {
        this.walker = walker;
        mechanics = MechanicsDefinition.Load(engine);
        Vitals = new PlayerVitals(mechanics, ActorStatFile.Load(engine, PlayerVitals.Path, mechanics));
        CombatDefinition combat = CombatDefinition.Load(engine, mechanics);
        Items = combat.Items;
        Inventory = new Inventory(combat.Items, combat.Kit, Vitals.Stats);
        Combat = new PlayerCombat(engine, combat, mechanics, Vitals, walker, bodyRadius, Inventory);
        Loot = LootTables.Load(engine, combat.Items);
        PickupTuning pickupTuning = Authored.Read(engine, PickupTuning.Path, PickupJson.Default.PickupTuning);
        pickupTuning.Validate();
        PickupMessages pickupText = Authored.Read(engine, PickupMessages.Path, PickupJson.Default.PickupMessages);
        pickupText.Validate();
        Pickups = new Pickups(engine, combat.Items, Loot, pickupTuning, pickupText);
        Enemies = new EnemyDirector(engine, EnemyCatalog.Load(engine, mechanics, combat.Actions), combat.Actions, mechanics);
    }

    internal PlayerVitals Vitals { get; }
    internal Inventory Inventory { get; }
    internal PlayerCombat Combat { get; }
    internal EnemyDirector Enemies { get; }
    internal Pickups Pickups { get; }
    internal LootTables Loot { get; }
    internal ItemCatalog Items { get; }
    internal MechanicsMessages MechanicsText => mechanics.Text;

    /// <summary>Takes up a scene: a level's chase and caches by the run's depth and reward; the gallery has neither.</summary>
    internal void Enter(IWalkScene scene, string tileset, int seed, int depth, float rewardMultiplier)
    {
        Combat.Enter(scene.Session);
        if (scene is LevelScene level)
        {
            Enemies.Enter(level, depth, seed);
            Pickups.Enter(level.Points.Caches, tileset, seed, rewardMultiplier);
        }
        else
        {
            Enemies.Leave();
            Pickups.Leave();
        }
    }

    /// <summary>The player as they start a run: whole, with the kit.</summary>
    internal void ResetPlayer()
    {
        Vitals.Reset();
        Inventory.Reset();
        Combat.Reset();
    }

    /// <summary>
    /// The player's controls this update: number keys take a carried weapon into the main hand; E opens or takes, Q uses
    /// the ready consumable, the primary and secondary buttons use the main and off hands, R reloads. Nothing acts in the
    /// gallery, in flight, while stunned or while an action is under way. Returns the world seconds what began now costs.
    /// </summary>
    internal float Act(PhysicalInputState physical, bool inLevel)
    {
        KeyboardControl[] slots = [KeyboardControl.Digit1, KeyboardControl.Digit2, KeyboardControl.Digit3, KeyboardControl.Digit4,
            KeyboardControl.Digit5, KeyboardControl.Digit6, KeyboardControl.Digit7, KeyboardControl.Digit8, KeyboardControl.Digit9];
        for (int i = 0; i < slots.Length; i++)
            if (physical.Pressed(slots[i])) Combat.Select(i);
        if (!inLevel || walker.Flying || Vitals.Stats.Effects.Stunned || Combat.Busy) return 0;
        if (physical.Pressed(KeyboardControl.KeyE) && Pickups.Use(walker.Feet, Inventory) is { } taken)
        {
            Combat.Announce(taken.Notice);
            return taken.Seconds;
        }
        if (physical.Pressed(KeyboardControl.KeyQ) && Inventory.Use(mechanics) is { } used)
        {
            Combat.Announce(Template.Fill(Pickups.Text.Used, ("item", used.Name)));
            return used.Consumable!.Seconds;
        }
        float? cost = physical.Pressed(PointerButton.Primary) ? Combat.Use(PlayerCombat.MainHand)
            : physical.Pressed(PointerButton.Secondary) ? Combat.Use(PlayerCombat.OffHand)
            : physical.Pressed(KeyboardControl.KeyR) ? Combat.Reload(PlayerCombat.MainHand)
            : null;
        return cost ?? 0;
    }

    /// <summary>One world step: effects, enemies, the player's action and shots, then the body. Returns whether the player fell.</summary>
    internal bool Step(float seconds, FpsInputFrame frame, bool jump)
    {
        Vitals.Step(seconds);
        Enemies.Step(seconds, Combat.Actor);
        Combat.Step(seconds, Enemies.Living);
        walker.Step(frame, jump, seconds, Vitals.MovementScale, Vitals.Knockback(walker.Feet));
        return Vitals.Defeated;
    }

    /// <summary>After the update's steps: fallen enemies drop their loot and walk-over items are taken.</summary>
    internal void Settle()
    {
        foreach (Enemy fallen in Enemies.TakeFallen()) Pickups.Drop(fallen.Feet);
        if (Pickups.WalkOver(walker.Feet, Inventory) is { } took) Combat.Announce(took);
    }

    internal IEnumerable<AppearanceFact> Facts() => [.. Pickups.Facts(), .. Enemies.Facts(), .. Combat.Facts()];

    public void Dispose()
    {
        Combat.Dispose();
        Enemies.Dispose();
        Pickups.Dispose();
    }
}
