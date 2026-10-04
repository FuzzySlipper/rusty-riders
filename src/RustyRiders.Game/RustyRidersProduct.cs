using Rusty.Engine;
using Rusty.Engine.Input;
using RustyRiders.Game.Gallery;
using RustyRiders.Game.Levels;
using RustyRiders.Game.Player;
using RustyRiders.Game.Ui;

namespace RustyRiders.Game;

public sealed class RustyRidersProduct : IEngineProduct
{
    private const string UiStreamId = "rusty-riders";
    private const string UiContract = "rusty.riders.gallery";

    private readonly IEngineContext engine;
    private LevelSettings levelSettings;
    private readonly Walker walker;
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
        scene = BuildScene();
        walker = new Walker(engine, WalkerTuning.Load(engine), scene);
        hud = engine.Ui.OpenStream(new UiStreamRequest(UiStreamId, UiContract));
    }

    public void Start()
    {
        if (disposed) return;
        started = true;
        paused = false;
        scene.Publish();
        Publish();
    }

    public ProductUpdateResult Update(ProductUpdate update)
    {
        if (!started || paused || disposed) return ProductUpdateResult.None;
        foreach (ProductInputEvent input in update.Input)
        {
            if (input.Kind == InputEventKind.Clear) jumpPending = false;
        }
        float delta = (float)update.Facts.FixedDeltaSeconds;
        FpsInputFrame frame = walker.ReadInput(update.Input, delta * update.Facts.AdmittedStepCount);
        if (walker.Input.Physical.Pressed(KeyboardControl.KeyR))
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
        for (uint step = 0; step < update.Facts.AdmittedStepCount; step++)
        {
            walker.Step(frame, jumpPending, delta);
            jumpPending = false;
        }
        sampleTime = (update.Facts.SimulationStep + update.Facts.AdmittedStepCount) * update.Facts.FixedDeltaSeconds;
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
        walker.Reset();
        jumpPending = false;
        started = true;
        paused = false;
        Publish();
    }

    public void Shutdown() => Dispose();

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        walker.Dispose();
        scene.Dispose();
        hud.Dispose();
    }

    /// <summary>Replaces the current scene (its art, facts and collision) and moves the walker to the new spawn.</summary>
    private void SwitchScene(bool level)
    {
        scene.Dispose();
        showingLevel = level;
        scene = BuildScene();
        scene.Publish();
        walker.Enter(scene);
        jumpPending = false;
    }

    private IWalkScene BuildScene()
    {
        if (!showingLevel) return new GalleryScene(engine, GalleryDefinition.Load(engine));
        LevelData data = LevelData.Load(engine, levelSettings.Tileset);
        LayoutDefinition layout = data.Layouts.FirstOrDefault(layout => layout.Id == levelSettings.Layout)
            ?? throw new InvalidOperationException($"content/levels/layouts.json has no layout '{levelSettings.Layout}'.");
        return new LevelScene(engine, levelSettings, data, layout, levelSeed);
    }

    /// <summary>The tileset's next shell floor texture after the current one, then none (its own) again.</summary>
    private string? NextFloorTexture()
    {
        string[] ids = ShellDefinition.Load(engine, levelSettings.Tileset)?.FloorTextures.Select(t => t.Id).ToArray() ?? [];
        int next = Array.IndexOf(ids, levelSettings.FloorTexture) + 1;
        return next < ids.Length ? ids[next] : null;
    }

    private void Publish()
    {
        walker.Publish(sampleTime);
        engine.Ui.PublishProjection(new UiProjection(hud, ++uiSequence, Hud.Create(scene, walker)));
    }
}
