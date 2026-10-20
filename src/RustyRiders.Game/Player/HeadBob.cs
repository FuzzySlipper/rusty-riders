using System.Numerics;
using System.Text.Json.Serialization;
using Rusty.Engine;
using RustyRiders.Game.Content;

namespace RustyRiders.Game.Player;

/// <summary>
/// How the walker's view bobs (content/player/bob.json): a gait for walking and one for sprinting, blended by speed and
/// eased in and out over <see cref="BlendSeconds"/>, and a dip on landing. Presentation only.
/// </summary>
internal sealed record HeadBobTuning(BobGait Walk, BobGait Sprint, float BlendSeconds, BobLanding Landing)
{
    internal const string Path = "player/bob.json";

    internal static HeadBobTuning Load(IEngineContext engine)
    {
        HeadBobTuning tuning = Authored.Read(engine, Path, HeadBobJson.Default.HeadBobTuning);
        Gait("walk", tuning.Walk);
        Gait("sprint", tuning.Sprint);
        Authored.Positive(Path, "blendSeconds", tuning.BlendSeconds);
        Authored.AtLeast(Path, "landing.metresPerSpeed", tuning.Landing.MetresPerSpeed, 0);
        Authored.AtLeast(Path, "landing.deepest", tuning.Landing.Deepest, 0);
        Authored.AtLeast(Path, "landing.softest", tuning.Landing.Softest, 0);
        Authored.Positive(Path, "landing.seconds", tuning.Landing.Seconds);
        Authored.Within(Path, "landing.down", tuning.Landing.Down, .01f, .99f);
        return tuning;

        static void Gait(string field, BobGait gait)
        {
            Authored.Positive(Path, $"{field}.stepMetres", gait.StepMetres);
            Authored.AtLeast(Path, $"{field}.rise", gait.Rise, 0);
            Authored.AtLeast(Path, $"{field}.sway", gait.Sway, 0);
            Authored.AtLeast(Path, $"{field}.rollDegrees", gait.RollDegrees, 0);
        }
    }
}

/// <summary>
/// A gait's bob: the ground one step covers (metres), how far the eye drops at each footfall (<see cref="Rise"/>,
/// metres below mid-stance), how far it sways over the planted foot (<see cref="Sway"/>, metres sideways) and how far
/// the view rolls with it (degrees).
/// </summary>
internal sealed record BobGait(float StepMetres, float Rise, float Sway, float RollDegrees);

/// <summary>
/// The dip on landing: <see cref="MetresPerSpeed"/> of drop for each m/s of fall past <see cref="Softest"/> (a hop does not
/// dip), at most <see cref="Deepest"/>, down over the first <see cref="Down"/> share of <see cref="Seconds"/> and back up.
/// </summary>
internal sealed record BobLanding(float MetresPerSpeed, float Softest, float Deepest, float Seconds, float Down);

/// <summary>
/// The walker's head bob: an offset and roll for the camera only (actions aim from the steady eye). The step keeps pace
/// with the ground walked, so it stays in step at any speed; its size eases with speed and fades in the air; a landing
/// dips the view by how fast the body fell. It moves in real time, as the held weapons' sway does.
/// </summary>
internal sealed class HeadBob(HeadBobTuning tuning)
{
    private float phase;
    private (float Walk, float Sprint) weight;
    private double sinceLanding = double.MaxValue;
    private float landingDepth;

    /// <summary>
    /// Advances by an update's host seconds with the ground walked since the last, the walker's horizontal speed, whether
    /// it is on the ground, its walking and sprinting speeds, and the fall speed of a landing in this update.
    /// </summary>
    internal void Advance(double hostSeconds, float walked, float speed, bool grounded, float walkSpeed, float sprintSpeed, float? landedFalling)
    {
        float moving = grounded ? Math.Clamp(speed / MathF.Max(.01f, walkSpeed), 0, 1) : 0;
        float sprinting = grounded ? Math.Clamp((speed - walkSpeed) / MathF.Max(.01f, sprintSpeed - walkSpeed), 0, 1) : 0;
        float ease = 1 - MathF.Exp(-(float)hostSeconds / tuning.BlendSeconds);
        weight = (weight.Walk + (moving * (1 - sprinting) - weight.Walk) * ease, weight.Sprint + (moving * sprinting - weight.Sprint) * ease);
        float step = Blend(g => g.StepMetres, (1 - sprinting, sprinting)); // the gait's step even before its bob grows
        if (grounded) phase = (phase + walked / step * MathF.PI) % (2 * MathF.PI);
        sinceLanding += hostSeconds;
        if (landedFalling is { } falling)
        {
            BobLanding landing = tuning.Landing;
            float depth = Math.Min(landing.Deepest, MathF.Max(0, falling - landing.Softest) * landing.MetresPerSpeed);
            if (depth > 0) (landingDepth, sinceLanding) = (depth, 0);
            phase = 0; // a landing is a footfall: the next step starts here
        }
    }

    /// <summary>The bob now: the eye's offset (right, up, along the view's own axes) and roll (degrees, positive to the right).</summary>
    internal (float Right, float Up, float RollDegrees) Now()
    {
        float side = MathF.Sin(phase);
        float up = (MathF.Abs(side) - 1) * Blend(g => g.Rise, weight);
        BobLanding landing = tuning.Landing;
        float t = (float)(sinceLanding / landing.Seconds);
        if (t < 1)
            up -= landingDepth * (t < landing.Down
                ? MathF.Sin(t / landing.Down * MathF.PI / 2)
                : 0.5f + 0.5f * MathF.Cos((t - landing.Down) / (1 - landing.Down) * MathF.PI));
        return (side * Blend(g => g.Sway, weight), up, side * Blend(g => g.RollDegrees, weight));
    }

    /// <summary>A level change or a restart: the view stands still.</summary>
    internal void Reset() => (phase, weight, sinceLanding) = (0, (0, 0), double.MaxValue);

    private float Blend(Func<BobGait, float> of, (float Walk, float Sprint) by) => of(tuning.Walk) * by.Walk + of(tuning.Sprint) * by.Sprint;
}

// Authored: missing constructor values, nulls in non-nullable fields and unknown members are errors, not defaults.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(HeadBobTuning))]
internal sealed partial class HeadBobJson : JsonSerializerContext;
