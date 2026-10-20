using System.Numerics;
using System.Text.Json.Serialization;
using Rusty.Engine;
using RustyRiders.Game.Content;

namespace RustyRiders.Game.Held;

/// <summary>What a hand feels as its action lands, layered over whatever motion it plays.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FeedbackKind>))]
internal enum FeedbackKind { Struck, Blocked, Fired }

/// <summary>
/// The held weapons' feedback (content/held/feedback.json): a damped punch for each way an action lands — a blow that
/// strikes a body, a blow stopped by a surface, a shot or cast fired — layered over the hand's motion by the Engine's
/// additive tweens, so it adds to a baked swing instead of replacing it.
/// </summary>
internal sealed record HeldFeedback(HeldPunch Struck, HeldPunch Blocked, HeldPunch Fired)
{
    internal const string Path = "held/feedback.json";

    internal HeldPunch Punch(FeedbackKind kind) => kind switch
    {
        FeedbackKind.Struck => Struck,
        FeedbackKind.Blocked => Blocked,
        _ => Fired
    };

    internal static HeldFeedback Load(IEngineContext engine)
    {
        HeldFeedback feedback = Authored.Read(engine, Path, HeldJson.Default.HeldFeedback);
        foreach (FeedbackKind kind in Enum.GetValues<FeedbackKind>())
        {
            HeldPunch punch = feedback.Punch(kind);
            string at = Field(kind);
            Authored.Point(Path, $"{at}.move", punch.Move);
            Authored.Point(Path, $"{at}.turn", punch.Turn);
            Authored.Positive(Path, $"{at}.seconds", punch.Seconds);
            Authored.Positive(Path, $"{at}.frequency", punch.Frequency);
            Authored.Require(HeldMotionCatalog.Named.Contains(punch.Decay), Path, $"{at}.decay", "must be a named curve (as a motion key's ease).");
        }
        return feedback;
    }

    /// <summary>Takes a reloaded file's values in place (live controls hold these records).</summary>
    internal void Take(HeldFeedback fresh)
    {
        foreach (FeedbackKind kind in Enum.GetValues<FeedbackKind>()) Punch(kind).Take(fresh.Punch(kind));
    }

    private static string Field(FeedbackKind kind) => char.ToLowerInvariant(kind.ToString()[0]) + kind.ToString()[1..];
}

/// <summary>
/// A damped punch: the weapon swings toward <see cref="Move"/> (camera space metres, the main hand's; mirrored for the off
/// hand) and turns toward <see cref="Turn"/> (degrees about camera X, Y, Z) and back <see cref="Frequency"/> times over
/// <see cref="Seconds"/>, the swing dying away by <see cref="Decay"/>.
/// </summary>
internal sealed record HeldPunch(float[] Move, float[] Turn, float Seconds, float Frequency, TweenEasingKind Decay)
{
    public float Seconds { get; set; } = Seconds;
    public float Frequency { get; set; } = Frequency;
    public TweenEasingKind Decay { get; set; } = Decay;

    internal void Take(HeldPunch fresh)
    {
        fresh.Move.CopyTo(Move, 0);
        fresh.Turn.CopyTo(Turn, 0);
        (Seconds, Frequency, Decay) = (fresh.Seconds, fresh.Frequency, fresh.Decay);
    }
}

/// <summary>
/// One hand's feedback layered over its motion: each punch an Engine tween started as a layer on the hand's object,
/// kept so where the weapon is drawn can be told (the arms reach for the weapon as drawn, layers and all). A motion
/// started over them (an action, a settle) ends them, as the Engine ends an object's tweens on a replacing start.
/// </summary>
internal sealed class FeedbackLayers(IEngineContext engine)
{
    private readonly List<(TweenHandle Handle, TweenStartRequest Request)> playing = [];

    /// <summary>
    /// Starts a punch on a hand's object, its turn given in camera space and applied in the drawn model's own frame
    /// (<paramref name="model"/>, its turn now), as the Engine applies a rotation channel; on world time in play, in real
    /// time for the motion viewer (the world holds while the player stands still).
    /// </summary>
    internal void Start(ulong objectId, HeldPunch punch, Quaternion model, bool offHand, TweenClock clock)
    {
        Vector3 move = Authored.Vector(punch.Move);
        Quaternion turn = Frames.Turn(punch.Turn);
        if (offHand) (move, turn) = (Frames.Mirror(move), Frames.Mirror(turn));
        Quaternion local = Quaternion.Normalize(Quaternion.Inverse(model) * turn * model);
        TweenSegment[] segments =
        [
            TweenSegment.Punch(TweenChannel.Translation, new Vector4(move, 0), punch.Seconds, punch.Frequency, punch.Decay),
            TweenSegment.Punch(TweenChannel.Rotation, new Vector4(local.X, local.Y, local.Z, local.W), punch.Seconds, punch.Frequency, punch.Decay)
        ];
        TweenStartRequest request = new(objectId, segments) { Clock = clock, Start = TweenStart.Layer };
        playing.Add((engine.Tween.Start(request).Tween, request));
    }

    /// <summary>The offset the running punches add now (translation summed, turns composed in the model's frame).</summary>
    internal (Vector3 Move, Quaternion Turn) Offset()
    {
        playing.RemoveAll(p => engine.Tween.Read(p.Handle).State == TweenState.Ended);
        (Vector3 move, Quaternion turn) = (Vector3.Zero, Quaternion.Identity);
        foreach (var (handle, request) in playing)
        {
            TweenSample sample = engine.Tween.Sample(request, engine.Tween.Read(handle).ElapsedSeconds);
            (move, turn) = (move + sample.Translation, Quaternion.Normalize(turn * sample.Rotation));
        }
        return (move, turn);
    }

    /// <summary>How many punches have run since the hand last started a motion or the view was asked (they end on their own).</summary>
    internal int Count => playing.Count;

    /// <summary>Forgets the punches: a replacing start has ended them.</summary>
    internal void Ended() => playing.Clear();

    /// <summary>Ends the punches.</summary>
    internal void Cancel()
    {
        foreach (var (handle, _) in playing) engine.Tween.Control(new TweenControlRequest(handle, TweenControl.Cancel));
        playing.Clear();
    }
}
