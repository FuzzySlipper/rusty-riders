using Rusty.Engine;
using Rusty.Engine.Debugging;
using Rusty.Engine.Input;
using RustyRiders.Game.Combat;
using RustyRiders.Game.Content;
using RustyRiders.Game.Developer;
using RustyRiders.Game.Enemies;
using RustyRiders.Game.Items;
using RustyRiders.Game.Gallery;
using RustyRiders.Game.Levels;
using RustyRiders.Game.Mechanics;
using RustyRiders.Game.Player;
using RustyRiders.Game.Time;
using RustyRiders.Game.Ui;

namespace RustyRiders.Game;

public sealed class RustyRidersProduct : IEngineProduct, IDebugCommandModuleSource
{
    private const string UiStreamId = "rusty-riders";
    private const string UiContract = "rusty.riders.gallery";

    private readonly IEngineContext engine;
    private LevelSettings levelSettings;
    private readonly Walker walker;
    private readonly TimeFlow time;
    private readonly LevelRules levelRules;
    private readonly PlayerVitals vitals;
    private readonly MechanicsMessages mechanicsText;
    private readonly PlayerCombat combat;
    private readonly EnemyDirector enemies;
    private readonly Inventory inventory;
    private readonly Pickups pickups;
    private readonly MechanicsDefinition mechanics;
    private readonly LootTables loot;
    private readonly ItemCatalog itemCatalog;
    private readonly UiStream hud;
    private IWalkScene scene;
    private bool showingLevel = true;
    private int levelSeed;
    private ulong uiSequence;
    private double sampleTime;
    private bool started;
    private bool paused;
    private bool disposed;
    private bool jumpPending;

    public RustyRidersProduct(ProductCreateContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        engine = context.Engine;
        levelSettings = LevelSettings.Load(engine);
        levelSeed = levelSettings.Seed;
        levelRules = LevelRules.Load(engine);
        mechanics = MechanicsDefinition.Load(engine);
        mechanicsText = mechanics.Text;
        vitals = new PlayerVitals(mechanics, ActorStatFile.Load(engine, PlayerVitals.Path, mechanics));
        scene = BuildScene();
        WalkerTuning walking = WalkerTuning.Load(engine);
        walker = new Walker(engine, walking, scene);
        CombatDefinition combatDefinition = CombatDefinition.Load(engine, mechanics);
        itemCatalog = combatDefinition.Items;
        inventory = new Inventory(combatDefinition.Items, combatDefinition.Kit, vitals.Stats);
        combat = new PlayerCombat(engine, combatDefinition, mechanics, vitals, walker, walking.Radius, inventory);
        loot = LootTables.Load(engine, combatDefinition.Items);
        PickupTuning pickupTuning = Authored.Read(engine, PickupTuning.Path, PickupJson.Default.PickupTuning);
        pickupTuning.Validate();
        PickupMessages pickupText = Authored.Read(engine, PickupMessages.Path, PickupJson.Default.PickupMessages);
        pickupText.Validate();
        pickups = new Pickups(engine, combatDefinition.Items, loot, pickupTuning, pickupText);
        enemies = new EnemyDirector(engine, EnemyCatalog.Load(engine, mechanics, combatDefinition.Actions), combatDefinition.Actions, mechanics);
        EnterScene();
        time = new TimeFlow(engine, TimeTuning.Load(engine));
        hud = engine.Ui.OpenStream(new UiStreamRequest(UiStreamId, UiContract));
    }

    public void Start()
    {
        if (disposed) return;
        started = true;
        paused = false;
        Publish();
    }

    public ProductUpdateResult Update(ProductUpdate update)
    {
        if (!started || paused || disposed) return ProductUpdateResult.None;
        foreach (ProductInputEvent input in update.Input)
        {
            if (input.Kind == InputEventKind.Clear) jumpPending = false;
        }
        time.Observe(update.Facts);
        float delta = (float)update.Facts.FixedDeltaSeconds;
        // Look and controls in host time, so they stay live while the world holds; the body per admitted step.
        FpsInputFrame frame = walker.ReadInput(update.Input, (float)update.Facts.HostElapsedSeconds);
        if (walker.Input.Physical.Pressed(KeyboardControl.KeyH))
        {
            Restart();
            return ProductUpdateResult.None;
        }
        if (walker.Input.Physical.Pressed(KeyboardControl.KeyG)) SwitchScene(!showingLevel);
        else if (walker.Input.Physical.Pressed(KeyboardControl.KeyN) && showingLevel)
        {
            levelSeed++;
            SwitchScene(true);
        }
        else if (walker.Input.Physical.Pressed(KeyboardControl.KeyB) && showingLevel)
        {
            levelSettings = levelSettings with { Build = levelSettings.NextBuild };
            SwitchScene(true);
        }
        else if (walker.Input.Physical.Pressed(KeyboardControl.KeyV) && showingLevel)
        {
            levelSettings = levelSettings with { FloorTexture = NextFloorTexture() };
            SwitchScene(true);
        }
        if (walker.Input.Physical.Pressed(KeyboardControl.KeyF)) walker.ToggleFlight();
        jumpPending |= frame.JumpPressed;
        float actionSeconds = Act();
        for (uint step = 0; step < update.Facts.AdmittedStepCount; step++)
        {
            vitals.Step(delta);
            enemies.Step(delta, combat.Actor);
            combat.Step(delta, enemies.Living);
            walker.Step(frame, jumpPending, delta, vitals.MovementScale, vitals.Knockback(walker.Feet));
            jumpPending = false;
            if (vitals.Defeated) break;
        }
        if (vitals.Defeated) Defeated(); // a hit can land while the world holds, as well as in a step
        foreach (Enemy fallen in enemies.TakeFallen()) pickups.Drop(fallen.Feet);
        if (pickups.WalkOver(walker.Feet, inventory) is { } took) combat.Announce(took);
        if (update.Facts.AdmittedStepCount > 0) scene.Animate(time.WorldSeconds);
        if (scene is LevelScene level && level.RiftAt(walker.Feet) is { } rift)
        {
            Travel(level, rift);
            Publish();
            return ProductUpdateResult.None;
        }
        sampleTime = time.WorldSeconds;
        time.Choose(new TimeDemand(!showingLevel || walker.Flying, frame.Movement.Length(), frame.SprintHeld && !frame.CrouchHeld,
            walker.Airborne || jumpPending, walker.Input.Physical.Pressed(KeyboardControl.KeyT), actionSeconds));
        combat.Present(update.Facts.HostElapsedSeconds);
        Publish();
        return ProductUpdateResult.None;
    }

    public void Pause()
    {
        if (!started || disposed) return;
        paused = true;
        walker.Input.Physical.Clear();
        jumpPending = false;
    }

    public void Resume()
    {
        if (started && !disposed) paused = false;
    }

    public void Restart()
    {
        if (disposed) return;
        vitals.Reset();
        inventory.Reset();
        combat.Reset();
        walker.Reset();
        jumpPending = false;
        time.Hold(); // a restart returns the Engine to realtime
        started = true;
        paused = false;
        Publish();
    }

    public void Shutdown() => Dispose();

    public void RegisterDebugCommands(IDebugCommandModuleRegistrar registrar)
    {
        registrar.Register(new LevelDebugCommands(() => scene, walker, EnterLevel));
        registrar.Register(new PlayerDebugCommands(vitals, walker, Publish));
        registrar.Register(new CombatDebugCommands(combat, walker));
        registrar.Register(new EnemyDebugCommands(enemies, walker));
        registrar.Register(new ItemDebugCommands(inventory, pickups, loot, itemCatalog, walker));
    }

    /// <summary>Builds and enters a level (developer override); returns why it could not, or null.</summary>
    private string? EnterLevel(string tileset, string layout, int seed)
    {
        LevelSettings previous = levelSettings;
        int previousSeed = levelSeed;
        try
        {
            levelSettings = levelSettings with { Tileset = tileset, Layout = layout, Palette = null };
            levelSeed = seed;
            SwitchScene(true);
            Publish();
            return null;
        }
        catch (Exception error) when (error is InvalidOperationException or EngineCallException)
        {
            levelSettings = previous;
            levelSeed = previousSeed;
            return error.Message;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        walker.Dispose();
        combat.Dispose();
        enemies.Dispose();
        pickups.Dispose();
        scene.Dispose();
        hud.Dispose();
    }

    /// <summary>Replaces the current scene (its art, facts and collision) and moves the walker to the new spawn.</summary>
    private void SwitchScene(bool level)
    {
        bool wasLevel = showingLevel;
        showingLevel = level;
        IWalkScene next;
        try
        {
            next = BuildScene(); // the current scene stays whole until the next one is built
        }
        catch
        {
            showingLevel = wasLevel;
            throw;
        }
        scene.Dispose();
        scene = next;
        EnterScene();
        walker.Enter(scene);
        jumpPending = false;
    }

    private IWalkScene BuildScene()
    {
        if (!showingLevel) return new GalleryScene(engine, GalleryDefinition.Load(engine));
        LevelData data = LevelData.Load(engine, levelSettings.Tileset);
        LayoutDefinition layout = data.Layouts.FirstOrDefault(layout => layout.Id == levelSettings.Layout)
            ?? throw new InvalidOperationException($"content/levels/layouts.json has no layout '{levelSettings.Layout}'.");
        // A level whose arrival cannot reach enough rifts is rejected; the next seed is tried, and the last kept.
        for (int attempt = 1; ; attempt++)
        {
            LevelScene level = new(engine, levelSettings, data, layout, levelSeed, levelRules);
            if (level.Playable || attempt >= levelRules.Points.LevelAttempts) return level;
            level.Dispose();
            levelSeed++;
        }
    }

    /// <summary>
    /// The player has fallen. The run loop will decide what that costs; for now they stand up whole at the level's arrival
    /// and the world holds.
    /// </summary>
    private void Defeated()
    {
        vitals.Reset();
        inventory.Reset();
        combat.Reset();
        walker.Reset();
        EnterScene();
        jumpPending = false;
        time.Hold();
    }

    /// <summary>Goes through a rift: a level of its destination world, on a layout and seed drawn from this level's seed.</summary>
    private void Travel(LevelScene from, RiftPoint rift)
    {
        Random random = new(unchecked(levelSeed * 31 + rift.Index + 1));
        levelSettings = levelSettings with
        {
            Tileset = rift.Destination.Tileset,
            Layout = from.TravelLayouts[random.Next(from.TravelLayouts.Length)],
            Palette = null,
        };
        levelSeed = random.Next();
        SwitchScene(true);
    }

    /// <summary>The tileset's next shell floor texture after the current one, then none (its own) again.</summary>
    private string? NextFloorTexture()
    {
        string[] ids = ShellDefinition.Load(engine, levelSettings.Tileset)?.FloorTextures.Select(t => t.Id).ToArray() ?? [];
        int next = Array.IndexOf(ids, levelSettings.FloorTexture) + 1;
        return next < ids.Length ? ids[next] : null;
    }

    /// <summary>Combat and the enemies take up the current scene: a level's chase starts afresh; the gallery has none.</summary>
    private void EnterScene()
    {
        combat.Enter(scene.Session);
        if (scene is LevelScene level)
        {
            enemies.Enter(level, 0, levelSeed);
            pickups.Enter(level.Points.Caches, levelSettings.Tileset, levelSeed, 1);
        }
        else
        {
            enemies.Leave();
            pickups.Leave();
        }
    }

    /// <summary>
    /// The player's combat controls this update: number keys take a carried weapon into the main hand, the primary and
    /// secondary buttons use the main and off hands, R reloads the main hand. A stunned player cannot act. Returns the
    /// world seconds an action begun now costs.
    /// </summary>
    private float Act()
    {
        PhysicalInputState physical = walker.Input.Physical;
        KeyboardControl[] slots = [KeyboardControl.Digit1, KeyboardControl.Digit2, KeyboardControl.Digit3, KeyboardControl.Digit4,
            KeyboardControl.Digit5, KeyboardControl.Digit6, KeyboardControl.Digit7, KeyboardControl.Digit8, KeyboardControl.Digit9];
        for (int i = 0; i < slots.Length; i++)
            if (physical.Pressed(slots[i])) combat.Select(i);
        if (!showingLevel || walker.Flying || vitals.Stats.Effects.Stunned || combat.Busy) return 0;
        if (physical.Pressed(KeyboardControl.KeyE) && pickups.Use(walker.Feet, inventory) is { } taken)
        {
            combat.Announce(taken.Notice);
            return taken.Seconds;
        }
        if (physical.Pressed(KeyboardControl.KeyQ) && inventory.Use(mechanics) is { } used)
        {
            combat.Announce(Template.Fill(pickups.Text.Used, ("item", used.Name)));
            return used.Consumable!.Seconds;
        }
        float? cost = physical.Pressed(PointerButton.Primary) ? combat.Use(PlayerCombat.MainHand)
            : physical.Pressed(PointerButton.Secondary) ? combat.Use(PlayerCombat.OffHand)
            : physical.Pressed(KeyboardControl.KeyR) ? combat.Reload(PlayerCombat.MainHand)
            : null;
        return cost ?? 0;
    }

    private void Publish()
    {
        engine.Graphics.PublishSnapshot([.. scene.Facts, .. pickups.Facts(), .. enemies.Facts(), .. combat.Facts()]);
        walker.Publish(sampleTime);
        engine.Ui.PublishProjection(new UiProjection(hud, ++uiSequence, Hud.Create(scene, walker, time, vitals, mechanicsText, combat, enemies, inventory, pickups)));
    }
}
