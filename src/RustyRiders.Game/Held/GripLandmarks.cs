using System.Numerics;
using System.Text.Json.Serialization;
using Rusty.Engine;
using RustyRiders.Game.Content;

namespace RustyRiders.Game.Held;

/// <summary>
/// Points on a look (look space, as its grips) that a correct hold puts parts of the hand on, each optional: the trigger
/// (the main index finger's pad), the back strap's top under the hammer (the main hand's web), the butt (the main
/// little finger) and the fore-end (the off palm, on a look held in both hands). <see cref="GripLandmarks"/> measures
/// the drawn hands against them. From rusty-hotel's <c>HeldLandmarks</c> (docs/reuse.md).
/// </summary>
internal sealed record HeldLandmarks(float[]? Trigger = null, float[]? BackstrapTop = null, float[]? Butt = null, float[]? ForeEnd = null)
{
    public float[]? Trigger { get; set; } = Trigger;
    public float[]? BackstrapTop { get; set; } = BackstrapTop;
    public float[]? Butt { get; set; } = Butt;
    public float[]? ForeEnd { get; set; } = ForeEnd;

    /// <summary>The landmarks the look gives, by their field name.</summary>
    [JsonIgnore] internal IEnumerable<(string Field, float[] Point)> Named =>
        new[] { ("trigger", Trigger), ("backstrapTop", BackstrapTop), ("butt", Butt), ("foreEnd", ForeEnd) }
            .Where(p => p.Item2 is not null).Select(p => (p.Item1, p.Item2!));

    internal void Validate(string path, string field, bool offGrip)
    {
        foreach (var (name, point) in Named) Authored.Point(path, $"{field}.{name}", point);
        Authored.Require(ForeEnd is null || offGrip, path, $"{field}.foreEnd", "is where the off hand holds: the look needs an off grip.");
    }

    /// <summary>Takes a reloaded file's landmarks: a point on both edited in place (live controls hold it), one added or removed taken as is.</summary>
    internal void Take(HeldLandmarks fresh)
    {
        static float[]? Point(float[]? live, float[]? file)
        {
            if (live is null || file is null) return file;
            file.CopyTo(live, 0);
            return live;
        }
        (Trigger, BackstrapTop, Butt, ForeEnd) = (Point(Trigger, fresh.Trigger), Point(BackstrapTop, fresh.BackstrapTop), Point(Butt, fresh.Butt), Point(ForeEnd, fresh.ForeEnd));
    }
}

/// <summary>
/// Where the grip check finds a hand's own points and how near a look's landmarks they must come (content/held/arms.json
/// <c>landmarks</c>): a pad past a digit's last joint by <see cref="Pad"/> of the bone before it; the web
/// <see cref="Web"/> of the way from the thumb's base to the index finger's; the palm's centre <see cref="Palm"/> of the
/// way from the wrist to the middle finger's base and <see cref="PalmDepth"/> metres out of the palm; then the
/// tolerances, metres from the trigger, back strap, butt and fore-end, and degrees between a palm's normal and what it
/// should face.
/// </summary>
internal sealed record HandLandmarks
{
    public required float Pad { get; set; }
    public required float Web { get; set; }
    public required float Palm { get; set; }
    public required float PalmDepth { get; set; }
    public required float Trigger { get; set; }
    public required float Backstrap { get; set; }
    public required float Butt { get; set; }
    public required float ForeEnd { get; set; }
    public required float Facing { get; set; }

    internal void Validate(string path, string field)
    {
        foreach (var (name, value) in new[] { ("pad", Pad), ("web", Web), ("palm", Palm), ("trigger", Trigger), ("backstrap", Backstrap),
            ("butt", Butt), ("foreEnd", ForeEnd), ("facing", Facing) })
            Authored.Positive(path, $"{field}.{name}", value);
    }

    internal void Take(HandLandmarks fresh) =>
        (Pad, Web, Palm, PalmDepth, Trigger, Backstrap, Butt, ForeEnd, Facing) =
        (fresh.Pad, fresh.Web, fresh.Palm, fresh.PalmDepth, fresh.Trigger, fresh.Backstrap, fresh.Butt, fresh.ForeEnd, fresh.Facing);
}

/// <summary>
/// The developer grip check: which of the drawn hands' own points must meet which of the main hand's look's landmarks
/// (<see cref="HeldLandmarks"/>), published as Engine landmark pairs in the overlay set <see cref="Set"/>. The Engine
/// measures them on the poses it draws (<c>engine.inspect.landmarks held.landmarks</c>, or
/// <c>riders.dev.hands.contact</c>), green within tolerance and red outside while inspection shows the set. Adapted
/// from rusty-hotel's <c>GripLandmarks</c> (docs/reuse.md).
/// </summary>
internal sealed class GripLandmarks(IEngineContext engine)
{
    internal const string Set = "held.landmarks";

    // What the published pairs were made from; they are published again only when it changes.
    private string published = "";

    /// <summary>
    /// Publishes the pairs for <paramref name="look"/> in the main hand (drawn on <paramref name="held"/>, its model
    /// fitted into look space by <paramref name="fit"/>), against the drawn arms' main hand and, when
    /// <paramref name="offHolds"/>, the off hand on its fore-end; or removes them when the look has no landmarks.
    /// </summary>
    internal void Publish(HeldLookDefinition look, ulong held, Transform fit, ArmsView arms, bool offHolds, HandLandmarks tuning)
    {
        HeldLandmarks? marks = look.Landmarks;
        string now = marks is null ? "" : string.Join("|", look.Id, held, arms.Object, offHolds, fit.Translation, fit.Rotation, fit.Scale, tuning,
            string.Join(";", marks.Named.Select(m => $"{m.Field}:{string.Join(",", m.Point)}")));
        if (now == published) return;
        published = now;
        engine.Graphics.PublishOverlaySet(new(Set, Array.Empty<OverlayItem>(), marks is null ? [] : Pairs(look, marks, held, fit, arms, offHolds, tuning)));
    }

    private static LandmarkPair[] Pairs(HeldLookDefinition look, HeldLandmarks marks, ulong held, Transform fit, ArmsView arms, bool offHolds, HandLandmarks tuning)
    {
        // Look space into the drawn model's own: the fit's move, turn and scale undone. Offsets read along look space.
        Quaternion unturn = Quaternion.Inverse(fit.Rotation);
        Vector3 Model(float[] point) => Vector3.Transform(Authored.Vector(point) - fit.Translation, unturn) / fit.Scale;
        Vector3 Direction(Vector3 along) => Vector3.Normalize(Vector3.Transform(along, unturn) / fit.Scale);
        OverlayPlace place = OverlayPlace.Model(held);
        Landmark At(float[] point, Vector3 axis = default) => Landmark.At(place, Model(point), axis, unturn);
        HandRig main = arms.Hand(false);
        List<LandmarkPair> pairs = [];
        if (marks.Trigger is { } trigger)
            pairs.Add(LandmarkPair.Distance("trigger", main.Pad(Digit.Index, tuning.Pad), At(trigger), tuning.Trigger));
        if (marks.BackstrapTop is { } top)
            pairs.Add(LandmarkPair.Distance("backstrap", main.Web(tuning.Web), At(top), tuning.Backstrap));
        if (marks.Butt is { } butt)
            pairs.Add(LandmarkPair.Distance("butt", main.Joint(Digit.Pinky, 1, default), At(butt), tuning.Butt));
        // The handle runs through the main grip from the back strap's top toward the butt (down the look without a
        // butt); the palm wraps it, facing the nearest point of that line.
        if (marks.BackstrapTop is { } from)
        {
            Vector3 along = marks.Butt is { } toward ? Authored.Vector(toward) - Authored.Vector(from) : -Vector3.UnitY;
            if (along.LengthSquared() > 1e-10f)
                pairs.Add(LandmarkPair.Facing("main-facing", main.Palm(tuning.Palm, tuning.PalmDepth), At(look.Grips.Main, Direction(along)), LandmarkAim.Line, tuning.Facing, 0));
        }
        if (offHolds && marks.ForeEnd is { } foreEnd)
        {
            Landmark palm = arms.Hand(true).Palm(tuning.Palm, tuning.PalmDepth);
            pairs.Add(LandmarkPair.Distance("fore-end", palm, At(foreEnd), tuning.ForeEnd));
            pairs.Add(LandmarkPair.Facing("off-facing", palm, At(foreEnd), LandmarkAim.Point, tuning.Facing, 0));
        }
        return [.. pairs];
    }

    /// <summary>The pairs as the Engine last measured them on the drawn hands.</summary>
    internal LandmarkReadResult Read() => engine.Graphics.ReadLandmarks(Set);

    // What each pair's ends are, in a report's words: the hand's point and the look's landmark.
    private static readonly Dictionary<string, (string Hand, string Landmark)> Words = new(StringComparer.Ordinal)
    {
        ["trigger"] = ("main index pad", "trigger"),
        ["backstrap"] = ("main web", "back strap's top"),
        ["butt"] = ("main little finger", "butt"),
        ["main-facing"] = ("main", "the handle"),
        ["fore-end"] = ("off palm", "fore-end"),
        ["off-facing"] = ("off", "the fore-end"),
    };

    /// <summary>
    /// A report line for one pair as the Engine measured it: within or off, the distance (or a palm's facing angle) and
    /// its tolerance, and where the hand's point lies from the landmark along the look's own axes.
    /// </summary>
    internal static string Line(LandmarkMeasure pair)
    {
        (string hand, string landmark) = Words.TryGetValue(pair.Name, out var words) ? words : (pair.Name, "its landmark");
        if (!pair.Drawn) return $"--  {hand}: not drawn";
        string mark = pair.Within ? "ok " : "OFF";
        if (pair.Aim != LandmarkAim.None)
            return $"{mark} {hand} palm faces {landmark} {(pair.HasAngle ? $"{pair.Angle:0}°" : "(no angle)")} off (within {pair.AngleTolerance:0})";
        static string Part(float v, string plus, string minus) => $"{MathF.Abs(v) * 1000:0} {(v >= 0 ? plus : minus)}";
        Vector3 d = pair.Offset;
        return $"{mark} {hand} {pair.Distance * 1000:0.0} mm from the {landmark} (within {pair.Tolerance * 1000:0}): " +
            $"{Part(d.Y, "above", "below")}, {Part(d.Z, "behind", "ahead")}, {Part(d.X, "right", "left")}";
    }
}
