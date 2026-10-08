using Rusty.Engine;
using Rusty.Engine.Debugging;
using Rusty.Engine.Input;
using RustyRiders.Game.Developer;
using RustyRiders.Game.Levels;
using RustyRiders.Game.Player;
using RustyRiders.Game.Run;
using RustyRiders.Game.Time;
using RustyRiders.Game.Ui;

namespace RustyRiders.Game;

/// <summary>
/// The product entry: lifecycle, and each update's composition of the walker's controls, the scenes, play in the current
/// level, gameplay time and what is published.
/// </summary>
public sealed class RustyRidersProduct : IEngineProduct, IDebugCommandModuleSource
{
    private const string UiStreamId = "rusty-riders";
    private const string UiContract = "rusty.riders.gallery";

    private readonly IEngineContext engine;
    private readonly Scenes scenes;
    private readonly Walker walker;
    private readonly TimeFlow time;
    private readonly Play play;
    private readonly UiStream hud;
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
        scenes = new Scenes(engine, LevelRules.Load(engine));
        WalkerTuning walking = WalkerTuning.Load(engine);
        walker = new Walker(engine, walking, scenes.Current);
        play = new Play(engine, walker, walking.Radius);
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
        PhysicalInputState physical = walker.Input.Physical;
        if (physical.Pressed(KeyboardControl.KeyH))
        {
            Restart();
            return ProductUpdateResult.None;
        }
        if (DeveloperKeys(physical)) EnterScene();
        if (physical.Pressed(KeyboardControl.KeyF)) walker.ToggleFlight();
        jumpPending |= frame.JumpPressed;
        float actionSeconds = play.Act(physical, scenes.ShowingLevel);
        bool fell = false;
        for (uint step = 0; step < update.Facts.AdmittedStepCount && !fell; step++)
        {
            fell = play.Step(delta, frame, jumpPending);
            jumpPending = false;
        }
        if (fell || play.Vitals.Defeated) Defeated(); // a hit can land while the world holds, as well as in a step
        play.Settle();
        if (update.Facts.AdmittedStepCount > 0) scenes.Current.Animate(time.WorldSeconds);
        if (scenes.Level is { } level && level.RiftAt(walker.Feet) is { } rift)
        {
            Travel(level, rift);
            Publish();
            return ProductUpdateResult.None;
        }
        sampleTime = time.WorldSeconds;
        time.Choose(new TimeDemand(!scenes.ShowingLevel || walker.Flying, frame.Movement.Length(), frame.SprintHeld && !frame.CrouchHeld,
            walker.Airborne || jumpPending, physical.Pressed(KeyboardControl.KeyT), actionSeconds));
        play.Combat.Present(update.Facts.HostElapsedSeconds);
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
        play.ResetPlayer();
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
        registrar.Register(new LevelDebugCommands(() => scenes.Current, walker, EnterLevel));
        registrar.Register(new PlayerDebugCommands(play.Vitals, walker, Publish));
        registrar.Register(new CombatDebugCommands(play.Combat, walker));
        registrar.Register(new EnemyDebugCommands(play.Enemies, walker));
        registrar.Register(new ItemDebugCommands(play.Inventory, play.Pickups, play.Loot, play.Items, walker));
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        walker.Dispose();
        play.Dispose();
        scenes.Dispose();
        hud.Dispose();
    }

    /// <summary>G toggles the gallery; in a level N takes the next seed, B the next build and V the next floor texture.</summary>
    private bool DeveloperKeys(PhysicalInputState physical)
    {
        if (physical.Pressed(KeyboardControl.KeyG)) scenes.ToggleGallery();
        else if (!scenes.ShowingLevel) return false;
        else if (physical.Pressed(KeyboardControl.KeyN)) scenes.NextSeed();
        else if (physical.Pressed(KeyboardControl.KeyB)) scenes.NextBuild();
        else if (physical.Pressed(KeyboardControl.KeyV)) scenes.NextFloorTexture();
        else return false;
        return true;
    }

    /// <summary>Builds and enters a level (developer override); returns why it could not, or null.</summary>
    private string? EnterLevel(string tileset, string layout, int seed)
    {
        try
        {
            scenes.GoTo(tileset, layout, seed);
        }
        catch (Exception error) when (error is InvalidOperationException or EngineCallException)
        {
            return error.Message;
        }
        EnterScene();
        Publish();
        return null;
    }

    /// <summary>Play takes up the current scene and the walker stands at its spawn.</summary>
    private void EnterScene()
    {
        play.Enter(scenes.Current, scenes.Settings.Tileset, scenes.Seed, 0, 1);
        walker.Enter(scenes.Current);
        jumpPending = false;
    }

    /// <summary>
    /// The player has fallen. The run loop will decide what that costs; for now they stand up whole at the level's arrival
    /// and the world holds.
    /// </summary>
    private void Defeated()
    {
        play.ResetPlayer();
        EnterScene();
        time.Hold();
    }

    /// <summary>Goes through a rift: a level of its destination world, on a layout and seed drawn from this level's seed.</summary>
    private void Travel(LevelScene from, RiftPoint rift)
    {
        Random random = new(unchecked(scenes.Seed * 31 + rift.Index + 1));
        scenes.GoTo(rift.Destination.Tileset, from.TravelLayouts[random.Next(from.TravelLayouts.Length)], random.Next());
        EnterScene();
    }

    private void Publish()
    {
        engine.Graphics.PublishSnapshot([.. scenes.Current.Facts, .. play.Facts()]);
        walker.Publish(sampleTime);
        engine.Ui.PublishProjection(new UiProjection(hud, ++uiSequence, Hud.Create(scenes.Current, walker, time, play.Vitals,
            play.MechanicsText, play.Combat, play.Enemies, play.Inventory, play.Pickups)));
    }
}
