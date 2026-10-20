using System.Numerics;
using System.Text.Json.Serialization;
using Rusty.Engine;
using RustyRiders.Game.Actions;
using RustyRiders.Game.Content;

namespace RustyRiders.Game.Held;

/// <summary>
/// How held looks move through their actions: named poses and, per action, keys that ease the look from pose to pose
/// at moments of the action's phases. The Engine plays each action's keys as one tween timeline over the held model
/// (its curves, at the display's rate, on world time, so a held world holds the swing too); the product starts it when
/// the action starts and settles it back to rest when the action is cut short. Adapted from rusty-hotel's
/// <c>HeldMotionCatalog</c> (docs/reuse.md).
/// </summary>
/// <param name="SettleSeconds">How long a cut-short action takes to ease back to rest.</param>
/// <param name="SettleEase">The curve it eases back on.</param>
internal sealed record HeldMotionCatalog(float SettleSeconds, TweenEasingKind SettleEase, Dictionary<string, HeldMotion> Motions)
{
    internal const string Path = "held/motion.json";
    /// <summary>Every motion has a pose of this name: where the look rests between actions.</summary>
    internal const string Rest = "rest";
    /// <summary>A motion's track for actions with no track of their own.</summary>
    internal const string DefaultTrack = "default";

    internal static HeldMotionCatalog Load(IEngineContext engine)
    {
        HeldMotionCatalog catalog = Authored.Read(engine, Path, HeldJson.Default.HeldMotionCatalog);
        Authored.Positive(Path, "settleSeconds", catalog.SettleSeconds);
        Ease(Path, "settleEase", catalog.SettleEase);
        foreach (var (id, motion) in catalog.Motions)
        {
            string at = $"motions.{id}";
            Authored.Require(motion.Poses.TryGetValue(Rest, out HeldPose? rest) && rest.Position.All(v => v == 0) && rest.Rotation.All(v => v == 0),
                Path, $"{at}.poses.{Rest}", "is required and must be no change ([0, 0, 0] both): a look rests where its stance seats it.");
            if (motion.Loops is { } loops)
                foreach (var (name, loop) in loops.Named)
                {
                    string field = $"{at}.loops.{name}";
                    Authored.Positive(Path, $"{field}.cycle", loop.Cycle);
                    Authored.AtLeast(Path, $"{field}.scale", loop.Scale, 0);
                    Authored.Require(loop.Keys.Length >= 2, Path, $"{field}.keys", "needs at least two keys around the cycle.");
                    for (int i = 0; i < loop.Keys.Length; i++)
                    {
                        Authored.Within(Path, $"{field}.keys[{i}].at", loop.Keys[i].At, 0, 1);
                        Authored.Require(i == 0 || loop.Keys[i].At > loop.Keys[i - 1].At, Path, $"{field}.keys[{i}].at", "keys go around the cycle in order.");
                        Authored.Point(Path, $"{field}.keys[{i}].position", loop.Keys[i].Position);
                        Authored.Point(Path, $"{field}.keys[{i}].rotation", loop.Keys[i].Rotation);
                    }
                }
            foreach (var (name, transition) in new[] { ("equip", motion.Equip), ("unequip", motion.Unequip) })
            {
                if (transition is null) continue;
                string field = $"{at}.{name}";
                Authored.Positive(Path, $"{field}.seconds", transition.Seconds);
                Authored.Require(transition.Keys.Length >= 1, Path, $"{field}.keys", "needs at least one key.");
                for (int i = 0; i < transition.Keys.Length; i++)
                {
                    TransitionKey key = transition.Keys[i];
                    Authored.Require(motion.Poses.ContainsKey(key.Pose), Path, $"{field}.keys[{i}].pose", $"unknown pose '{key.Pose}'.");
                    Authored.Within(Path, $"{field}.keys[{i}].at", key.At, 0, 1);
                    Authored.Require(i == 0 || key.At > transition.Keys[i - 1].At, Path, $"{field}.keys[{i}].at", "keys fall in time order.");
                    Ease(Path, $"{field}.keys[{i}].ease", key.Ease);
                }
            }
            foreach (var (name, pose) in motion.Poses)
            {
                Authored.Point(Path, $"{at}.poses.{name}.position", pose.Position);
                Authored.Point(Path, $"{at}.poses.{name}.rotation", pose.Rotation);
            }
            Authored.Require(motion.Tracks.Count > 0, Path, $"{at}.tracks", "needs at least one track.");
            foreach (var (track, keys) in motion.Tracks)
                for (int i = 0; i < keys.Length; i++)
                {
                    string key = $"{at}.tracks.{track}[{i}]";
                    Authored.Require(motion.Poses.ContainsKey(keys[i].Pose), Path, $"{key}.pose", $"unknown pose '{keys[i].Pose}'.");
                    Authored.Within(Path, $"{key}.at", keys[i].At, 0, 1);
                    Authored.Within(Path, $"{key}.grip", keys[i].Grip, -1, 1);
                    if (keys[i].Shoulder is { } shoulder) Authored.Point(Path, $"{key}.shoulder", shoulder);
                    if (keys[i].Elbow is { } elbow) Authored.Point(Path, $"{key}.elbow", elbow);
                    Authored.Within(Path, $"{key}.offGrip", keys[i].OffGrip, -1, 1);
                    if (keys[i].OffElbow is { } offElbow) Authored.Point(Path, $"{key}.offElbow", offElbow);
                    Ease(Path, $"{key}.ease", keys[i].Ease);
                }
        }
        return catalog;
    }

    // Curves that need parameters (bezier, steps, spring) are not authored here.
    private static void Ease(string path, string field, TweenEasingKind ease) =>
        Authored.Require(Named.Contains(ease), path, field,
            "must be a named curve (Linear, or Quad, Cubic, Quart, Quint, Sine, Expo, Circ, Back, Elastic or Bounce with In, Out or InOut).");

    /// <summary>The curves keys may use: every named one (those needing parameters are left out).</summary>
    internal static readonly TweenEasingKind[] Named = [.. Enum.GetValues<TweenEasingKind>()
        .Where(e => e is not (TweenEasingKind.CubicBezier or TweenEasingKind.Steps or TweenEasingKind.Spring))];

    /// <summary>Checks, for every action a look's items use, that the look's motion has keys for it in time order.</summary>
    internal void Validate(string path, IEnumerable<(string Field, HeldLookDefinition Look, ActionDefinition Action)> uses)
    {
        foreach (var (field, look, action) in uses)
        {
            HeldMotion motion = Motions[look.Motion];
            Authored.Require(motion.Track(action.Id) is not null, Path, $"motions.{look.Motion}.tracks",
                $"has no track for '{action.Id}' ({path} {field}) and no '{DefaultTrack}' track.");
            Authored.Require(Order(motion, action) is "", Path, $"motions.{look.Motion}.tracks", Order(motion, action));
        }
    }

    /// <summary>Where the keys of an action fall out of time order (empty when they are in order).</summary>
    internal static string Order(HeldMotion motion, ActionDefinition action)
    {
        if (motion.Track(action.Id) is not { } keys) return "";
        float[] times = [.. keys.Select(k => k.Time(action.Timing))];
        for (int i = 1; i < times.Length; i++)
            if (times[i] <= times[i - 1])
                return $"keys for '{action.Id}' must fall in time order through its phases: key {i} ({keys[i].Pose}, {times[i]:0.###} s) follows key {i - 1} ({times[i - 1]:0.###} s).";
        return "";
    }

    /// <summary>
    /// The tween timeline that moves a held model through an action: from rest, through each key's pose by its curve,
    /// back to rest by the end of recovery. <paramref name="place"/> says where the drawn model is, and how it is turned,
    /// in a pose (camera space); values are offsets from rest, a translation in camera space and a rotation in the
    /// model's own frame, as the Engine applies them. The action's phases are scaled by <paramref name="scale"/> (the
    /// user's action time) and played <paramref name="speed"/> times as fast (a viewer's slow motion). It begins at
    /// <paramref name="from"/>, the offset shown when it starts, so it carries on without a jump.
    /// </summary>
    internal TweenSegment[] Timeline(HeldMotion motion, ActionDefinition action, Func<HeldPose, (Vector3 At, Quaternion Turn)> place,
        float scale = 1, float speed = 1, (Vector3 Move, Quaternion Turn)? from = null)
    {
        (Vector3 restAt, Quaternion restTurn) = place(motion.Poses[Rest]);
        (Vector3 Move, Quaternion Turn) Offset(HeldPose pose)
        {
            (Vector3 at, Quaternion turn) = place(pose);
            return (at - restAt, Quaternion.Normalize(Quaternion.Inverse(restTurn) * turn));
        }
        List<TweenSegment> segments = [];
        (Vector3 Move, Quaternion Turn) last = from ?? (Vector3.Zero, Quaternion.Identity);
        float start = 0;
        void Ease((Vector3 Move, Quaternion Turn) to, float time, TweenEasingKind ease)
        {
            if (time - start <= 0) return;
            float at = start / speed, seconds = (time - start) / speed;
            segments.Add(TweenSegment.Move(last.Move, to.Move, seconds, ease).At(at));
            segments.Add(TweenSegment.Rotate(last.Turn, to.Turn, seconds, ease).At(at));
            last = to;
            start = time;
        }
        foreach (MotionKey key in motion.Track(action.Id)!) Ease(Offset(motion.Poses[key.Pose]), key.Time(action.Timing) * scale, key.Ease);
        // Every timeline ends at rest, where the model is published, when the action ends.
        Ease((Vector3.Zero, Quaternion.Identity), action.Duration * scale, SettleEase);
        return [.. segments];
    }

    /// <summary>
    /// The tween timeline of a look being drawn or put away, as offsets from rest (<paramref name="place"/> as for an
    /// action): drawing starts at its first key's pose and ends at rest; putting away starts from <paramref name="from"/>
    /// (the offset shown) and ends at its last key's pose, where the look is swapped, held there <paramref name="hold"/>
    /// seconds past its end so the look never shows at rest before the swap.
    /// </summary>
    internal static TweenSegment[] Timeline(HeldMotion motion, HeldTransition transition, bool drawing, Func<HeldPose, (Vector3 At, Quaternion Turn)> place,
        (Vector3 Move, Quaternion Turn) from = default, float hold = 0)
    {
        (Vector3 restAt, Quaternion restTurn) = place(motion.Poses[Rest]);
        (Vector3 Move, Quaternion Turn) Offset(HeldPose pose)
        {
            (Vector3 at, Quaternion turn) = place(pose);
            return (at - restAt, Quaternion.Normalize(Quaternion.Inverse(restTurn) * turn));
        }
        List<TweenSegment> segments = [];
        (Vector3 Move, Quaternion Turn) last = drawing ? Offset(motion.Poses[transition.Keys[0].Pose])
            : from.Turn == default ? (Vector3.Zero, Quaternion.Identity) : from;
        float start = 0;
        void Ease((Vector3 Move, Quaternion Turn) to, float time, TweenEasingKind ease)
        {
            if (time - start <= 0) { last = to; return; }
            segments.Add(TweenSegment.Move(last.Move, to.Move, time - start, ease).At(start));
            segments.Add(TweenSegment.Rotate(last.Turn, to.Turn, time - start, ease).At(start));
            (last, start) = (to, time);
        }
        foreach (TransitionKey key in transition.Keys) Ease(Offset(motion.Poses[key.Pose]), key.At * transition.Seconds, key.Ease);
        if (drawing) Ease((Vector3.Zero, Quaternion.Identity), transition.Seconds, transition.Keys[^1].Ease);
        else Ease(last, transition.Seconds + hold, TweenEasingKind.Linear);
        // A timeline whose keys all sit at its start still holds its pose for its length.
        if (segments.Count == 0)
        {
            segments.Add(TweenSegment.Move(last.Move, last.Move, transition.Seconds, TweenEasingKind.Linear));
            segments.Add(TweenSegment.Rotate(last.Turn, last.Turn, transition.Seconds, TweenEasingKind.Linear));
        }
        return [.. segments];
    }

    /// <summary>The settle back to rest from <paramref name="from"/>, the offset a cut-short action left the model at.</summary>
    internal TweenSegment[] Settle((Vector3 Move, Quaternion Turn) from) =>
    [
        TweenSegment.Move(from.Move, Vector3.Zero, SettleSeconds, SettleEase),
        TweenSegment.Rotate(from.Turn, Quaternion.Identity, SettleSeconds, SettleEase)
    ];
}

/// <summary>One family of held motion: its poses and its tracks by action id (with a default for the rest).</summary>
/// <summary>
/// One family of held motion: its poses, its tracks by action id (with a default for the rest), the loops it sways by
/// between and under actions (<see cref="Loops"/>), and how a look of it is drawn and put away (<see cref="Equip"/>,
/// <see cref="Unequip"/>).
/// </summary>
internal sealed record HeldMotion(Dictionary<string, HeldPose> Poses, Dictionary<string, MotionKey[]> Tracks, HeldLoops? Loops = null,
    HeldTransition? Equip = null, HeldTransition? Unequip = null)
{
    internal MotionKey[]? Track(string action) =>
        Tracks.GetValueOrDefault(action) ?? Tracks.GetValueOrDefault(HeldMotionCatalog.DefaultTrack);
}

/// <summary>
/// A pose of a held look, from where its stance seats it: its grip moved by <see cref="Position"/> (camera space,
/// metres right, up, back) and the look turned further by <see cref="Rotation"/> (degrees about camera X, Y, Z) about
/// that grip. The rest pose is no change: the look as its stance holds it.
/// </summary>
internal sealed record HeldPose(float[] Position, float[] Rotation);

/// <summary>
/// A key: the look reaches <see cref="Pose"/> at <see cref="At"/> (0 to 1) through <see cref="Phase"/>, easing in by
/// <see cref="Ease"/>, with the holding hand closed to <see cref="Grip"/> (-1 open, 0 its grip, 1 squeezed) and, when
/// given, the body leaning the shoulders by <see cref="Shoulder"/> (camera space metres, the main hand's; mirrored for
/// the off hand), so a lunging blow can reach further than the arms from where they rest, and the point the holding
/// arm's elbow bends toward (<see cref="Elbow"/>, camera space, the main hand's; without it the arms' pole). For a look
/// held in both hands, the off hand on it closes to <see cref="OffGrip"/> and its elbow bends toward
/// <see cref="OffElbow"/> (camera space as is: the off hand holds the main hand's look, so nothing is mirrored).
/// </summary>
internal sealed record MotionKey(string Pose, MotionPhase Phase, float At, TweenEasingKind Ease, float Grip = 0, float[]? Shoulder = null,
    float[]? Elbow = null, float OffGrip = 0, float[]? OffElbow = null)
{
    /// <summary>Seconds from the action's start at its authored timing: windup, then commit (whose start is the strike), then recovery.</summary>
    internal float Time(ActionTiming timing) => Phase switch
    {
        MotionPhase.Windup => timing.Windup * At,
        MotionPhase.Commit => timing.Windup + timing.Commit * At,
        _ => timing.Windup + timing.Commit + timing.Recovery * At
    };
}

/// <summary>The action phase a key is timed through.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MotionPhase>))]
internal enum MotionPhase { Windup, Commit, Recovery }

/// <summary>A motion's sway loops: standing (<see cref="Idle"/>), walking and sprinting, each optional.</summary>
internal sealed record HeldLoops(HeldLoop? Idle = null, HeldLoop? Walk = null, HeldLoop? Sprint = null)
{
    internal IEnumerable<(string Name, HeldLoop Loop)> Named =>
        new[] { ("idle", Idle), ("walk", Walk), ("sprint", Sprint) }.Where(p => p.Item2 is not null).Select(p => (p.Item1, p.Item2!));
}

/// <summary>
/// A sway loop: offsets from rest (a grip move, camera metres, and a turn about it, degrees about camera X, Y, Z) at
/// moments around one cycle, drawn as a smooth closed curve through them and scaled by <see cref="Scale"/>. Its
/// <see cref="Cycle"/> is seconds for standing and metres walked for walking and sprinting, so the bob keeps step.
/// </summary>
internal sealed record HeldLoop(float Cycle, LoopKey[] Keys)
{
    public float Cycle { get; set; } = Cycle;
    public float Scale { get; set; } = 1;

    /// <summary>The loop's offset at a phase (0 to 1, wrapping), through its keys on a closed Catmull-Rom curve.</summary>
    internal (Vector3 Move, Vector3 Turn) At(float phase)
    {
        phase -= MathF.Floor(phase);
        int n = Keys.Length;
        int i = Array.FindLastIndex(Keys, k => k.At <= phase);
        if (i < 0) i = n - 1;
        LoopKey a = Keys[i], b = Keys[(i + 1) % n];
        float span = (b.At - a.At + 1) % 1, into = (phase - a.At + 1) % 1;
        float t = span <= 0 ? 0 : into / span;
        LoopKey before = Keys[(i - 1 + n) % n], after = Keys[(i + 2) % n];
        Vector3 Curve(Func<LoopKey, float[]> value) => CatmullRom(Authored.Vector(value(before)), Authored.Vector(value(a)),
            Authored.Vector(value(b)), Authored.Vector(value(after)), t);
        return (Curve(k => k.Position) * Scale, Curve(k => k.Rotation) * Scale);
    }

    private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t) =>
        0.5f * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (3 * p1 - p0 - 3 * p2 + p3) * t * t * t);
}

/// <summary>A sway loop's key: its moment around the cycle (0 to 1) and its offset from rest.</summary>
internal sealed record LoopKey(float At, float[] Position, float[] Rotation);

/// <summary>
/// How a look is drawn or put away: keys through the motion's poses over <see cref="Seconds"/>, played in real time
/// (a swap shows while the world holds). Drawing ends at rest; putting away ends at its last key, out of the way.
/// </summary>
internal sealed record HeldTransition(float Seconds, TransitionKey[] Keys)
{
    public float Seconds { get; set; } = Seconds;
}

/// <summary>A transition key: the look reaches <see cref="Pose"/> at <see cref="At"/> (0 to 1) of the transition, easing in by <see cref="Ease"/>.</summary>
internal sealed record TransitionKey(string Pose, float At, TweenEasingKind Ease);
