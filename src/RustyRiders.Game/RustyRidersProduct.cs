using Rusty.Engine;
using Rusty.Engine.Input;
using RustyRiders.Game.Gallery;
using RustyRiders.Game.Player;
using RustyRiders.Game.Ui;

namespace RustyRiders.Game;

public sealed class RustyRidersProduct : IEngineProduct
{
    private const string UiStreamId = "rusty-riders";
    private const string UiContract = "rusty.riders.gallery";

    private readonly IEngineContext engine;
    private readonly GalleryScene scene;
    private readonly Walker walker;
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
        scene = new GalleryScene(engine, GalleryDefinition.Load(engine));
        walker = new Walker(engine, scene);
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

    private void Publish()
    {
        walker.Publish(sampleTime);
        engine.Ui.PublishProjection(new UiProjection(hud, ++uiSequence, GalleryHud.Create(scene, walker)));
    }
}
