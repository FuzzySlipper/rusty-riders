using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.FirstPerson;
using RustyRiders.Game.Actions;

namespace RustyRiders.Game.Held;

/// <summary>
/// One hand's held look and the motion playing on it: the look it shows, the tween last started on its object (the
/// timeline it plays, so where it shows the look can be sampled; the look it was started for; the action start it plays,
/// 0 for a settle or the viewer; and that action), the feedback punches layered over it, and the time scale and speed
/// its keys are read at. Split from <see cref="HeldView"/>.
/// </summary>
internal sealed class HeldHand(IEngineContext engine, ulong objectId)
{
    /// <summary>The look the hand shows, and the one it holds (they differ while it swaps).</summary>
    internal string Look = "", Wanted = "";
    /// <summary>Where the hand is in swapping what it holds.</summary>
    internal Swap Swap;
    /// <summary>The hand's sway this update, as a pose from rest.</summary>
    internal HeldPose Sway = HeldPose.Rest;
    internal (TweenHandle Handle, TweenStartRequest Request, string Look, ulong Started)? Tween;
    internal ActionDefinition? Action;
    /// <summary>The last action start of this hand's user that its motion has followed.</summary>
    internal ulong StartedSeen;
    /// <summary>Feedback punches layered over the motion.</summary>
    internal readonly HeldLayers Layers = new(engine);
    /// <summary>The action's time scale and the viewer's playback speed, to read the keys at the tween's elapsed time.</summary>
    internal float Scale = 1, Speed = 1;

    /// <summary>The published object this hand's look is drawn on.</summary>
    internal ulong Object => objectId;

    /// <summary>Starts this hand's motion tween, replacing its tweens; remembers it so where it shows the look can be sampled.</summary>
    internal TweenHandle Begin(TweenSegment[] segments, TweenClock clock, float elapsed = 0, ActionDefinition? action = null, ulong started = 0,
        float scale = 1, float speed = 1)
    {
        // A replacing start ends the hand's tweens, its feedback layers among them.
        Layers.Ended();
        (Scale, Speed) = (scale, speed);
        TweenStartRequest request = new(objectId, segments) { Clock = clock };
        TweenHandle handle = engine.Tween.Start(request with { ElapsedSeconds = elapsed }).Tween;
        Tween = (handle, request, Look, started);
        Action = action;
        return handle;
    }

    internal void Cancel()
    {
        if (Tween is { } playing) engine.Tween.Control(new TweenControlRequest(playing.Handle, TweenControl.Cancel));
        Layers.Cancel();
        Tween = null;
        Action = null;
    }

    /// <summary>
    /// The offset this hand's motion and its feedback layers show now (from its rest placement): translations add and the
    /// layers' turns follow the motion's in the model's frame, as the Engine composes them.
    /// </summary>
    internal (Vector3 Move, Quaternion Turn) Offset()
    {
        (Vector3 layerMove, Quaternion layerTurn) = Layers.Offset();
        if (Tween is not { } playing || playing.Look != Look) return (layerMove, layerTurn);
        TweenReadout readout = engine.Tween.Read(playing.Handle);
        if (readout.State == TweenState.Ended) return (layerMove, layerTurn);
        TweenSample sample = engine.Tween.Sample(playing.Request, readout.ElapsedSeconds);
        return (sample.Translation + layerMove, Quaternion.Normalize(sample.Rotation * layerTurn));
    }

    /// <summary>Seconds of the authored action this hand's motion has reached (time scale and speed undone), or null when none plays.</summary>
    internal float? ActionTime()
    {
        if (Tween is not { } playing || Action is null) return null;
        TweenReadout readout = engine.Tween.Read(playing.Handle);
        return readout.State == TweenState.Ended ? null : (float)readout.ElapsedSeconds * Speed / Scale;
    }

    internal bool Running => Tween is { } playing && engine.Tween.Read(playing.Handle).State != TweenState.Ended;

    /// <summary>Seconds the hand's tween has played, or null when none plays.</summary>
    internal double? Elapsed => Tween is { } playing && engine.Tween.Read(playing.Handle) is { State: not TweenState.Ended } readout ? readout.ElapsedSeconds : null;
}
