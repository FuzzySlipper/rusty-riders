using System.Numerics;
using System.Text.Json.Serialization;
using Rusty.Engine;
using RustyRiders.Game.Content;

namespace RustyRiders.Game.Held;

/// <summary>
/// How the held weapons sway (content/held/sway.json): how quickly the sway eases from standing to walking to sprinting
/// (<see cref="BlendSeconds"/>), how much of it stays under a hand's own action (<see cref="Acting"/>, 0 to 1), and how
/// far a single update's move may be and still count as walking (a teleport or a level change is not a stride).
/// </summary>
internal sealed record SwayTuning(float BlendSeconds, float Acting, float LongestStride)
{
    internal const string Path = "held/sway.json";

    public float BlendSeconds { get; set; } = BlendSeconds;
    public float Acting { get; set; } = Acting;

    internal static SwayTuning Load(IEngineContext engine)
    {
        SwayTuning tuning = Authored.Read(engine, Path, HeldJson.Default.SwayTuning);
        Authored.Positive(Path, "blendSeconds", tuning.BlendSeconds);
        Authored.Within(Path, "acting", tuning.Acting, 0, 1);
        Authored.Positive(Path, "longestStride", tuning.LongestStride);
        return tuning;
    }

    internal void Take(SwayTuning fresh) => (BlendSeconds, Acting) = (fresh.BlendSeconds, fresh.Acting);
}

/// <summary>
/// The held weapons' sway as presentation over the rider's movement: a standing loop on time, walking and sprinting loops
/// on distance walked (so the bob keeps step at any speed), blended by speed and eased between, faded in the air, and
/// turned down under each hand's own action. Each hand's sway is an offset from rest, as a pose is, published with the
/// look, so actions, punches and the arms' reach compose over it. It moves in real time: the rider still breathes while
/// the world holds.
/// </summary>
internal sealed class HeldSway(SwayTuning tuning)
{
    private double standing;
    private float walked;
    private Vector3? lastFeet;
    private (float Idle, float Walk, float Sprint) weight = (1, 0, 0);
    private readonly float[] acting = [0, 0];

    /// <summary>
    /// Advances by an update's host seconds with the rider's feet and horizontal speed, whether they are on the ground,
    /// the walking and sprinting speeds, and whether each hand's action is playing.
    /// </summary>
    internal void Advance(double hostSeconds, Vector3 feet, float speed, bool grounded, float walkSpeed, float sprintSpeed, bool mainActing, bool offActing)
    {
        standing += hostSeconds;
        Vector3 flat = feet with { Y = 0 };
        if (lastFeet is { } last && grounded)
        {
            float stride = Vector3.Distance(flat, last);
            if (stride <= tuning.LongestStride) walked += stride;
        }
        lastFeet = flat;
        float moving = grounded ? Math.Clamp(speed / MathF.Max(.01f, walkSpeed), 0, 1) : 0;
        float sprinting = grounded ? Math.Clamp((speed - walkSpeed) / MathF.Max(.01f, sprintSpeed - walkSpeed), 0, 1) : 0;
        float ease = 1 - MathF.Exp(-(float)hostSeconds / tuning.BlendSeconds);
        weight = (Toward(weight.Idle, 1 - moving), Toward(weight.Walk, moving * (1 - sprinting)), Toward(weight.Sprint, moving * sprinting));
        acting[HeldView.MainHand] = Toward(acting[HeldView.MainHand], mainActing ? 1 : 0);
        acting[HeldView.OffHand] = Toward(acting[HeldView.OffHand], offActing ? 1 : 0);
        float Toward(float from, float to) => from + (to - from) * ease;
    }

    /// <summary>A hand's sway now, as a pose from rest, from its look's motion's loops (none sways it not at all).</summary>
    internal HeldPose Pose(HeldLoops? loops, int hand)
    {
        (Vector3 move, Vector3 turn) = (Vector3.Zero, Vector3.Zero);
        void Add(HeldLoop? loop, float amount, double along)
        {
            if (loop is null || amount <= 1e-3f) return;
            (Vector3 m, Vector3 t) = loop.At((float)(along / loop.Cycle));
            (move, turn) = (move + m * amount, turn + t * amount);
        }
        Add(loops?.Idle, weight.Idle, standing);
        Add(loops?.Walk, weight.Walk, walked);
        Add(loops?.Sprint, weight.Sprint, walked);
        float keep = 1 - acting[hand] * (1 - tuning.Acting);
        (move, turn) = (move * keep, turn * keep);
        return new HeldPose([move.X, move.Y, move.Z], [turn.X, turn.Y, turn.Z]);
    }

    /// <summary>A level change or a restart: the next move is not a stride.</summary>
    internal void Moved() => lastFeet = null;
}

/// <summary>Where a hand is in swapping what it holds: putting the old look away, about to draw the new one, or drawing it.</summary>
internal enum Swap { None, PuttingAway, DrawNext, Drawing }
