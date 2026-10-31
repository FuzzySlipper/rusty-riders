using System.Text.Json;
using Rusty.Engine.Debugging;
using Rusty.Engine.FirstPerson;
using RustyRiders.Game.Actions;
using RustyRiders.Game.Combat;
using RustyRiders.Game.Held;

namespace RustyRiders.Game.Developer;

/// <summary>
/// Developer motion viewer in the Engine console: show any held look in a hand, step its action's keys (each held
/// exactly), play the action at any speed in real time, show any moment of it, try pose changes, and nudge a look or
/// move a stance live. Edits change only the loaded content; each prints the JSON to paste into content/held/ (or use
/// the workbench's Save). Adapted from rusty-hotel's <c>HeldMotionDebugCommands</c> (docs/reuse.md).
/// </summary>
/// <param name="publish">Republishes the scene, so a held pose shows at once.</param>
/// <param name="uses">Each look's actions, from the weapons that show it.</param>
internal sealed class HeldMotionDebugCommands(PlayerCombat combat, Action publish, IReadOnlyDictionary<string, string[]> uses) : IDebugCommandModule
{
    private string? look;
    private ActionDefinition? action;
    private int hand = HeldView.MainHand;
    private int key = -1;

    private HeldCatalog Held => combat.View.Catalog;
    private HeldView View => combat.View;

    /// <summary>The look the viewer shows, the hand it shows it in, the action whose keys it steps, and the key it holds (-1: none).</summary>
    internal (string? Look, int Hand, ActionDefinition? Action, int Key) Viewing => (look, hand, action, key);

    internal IReadOnlyDictionary<string, string[]> Uses => uses;

    private HeldMotion Motion => Held.Motion.Motions[Held.Look(look!)!.Motion];
    private MotionKey[] Keys => Motion.Track(action!.Id)!;

    [DebugCommand("riders.dev.motion", Description = "Developer motion viewer: show a held look (e.g. rift-blade) at rest in the viewer's hand (main at first) with one of its actions (e.g. blade-slash), and list that action's keys.")]
    public DebugCommandResult Show(string heldLook, string actionId) => Show(heldLook, actionId, hand == HeldView.OffHand ? "off" : "main");

    [DebugCommand("riders.dev.motion.hand", Description = "Developer motion viewer: show the shown look in the main or the off hand (mirrored).")]
    public DebugCommandResult Hand(string inHand) =>
        look is null ? Failure("Choose a look first: riders.dev.motion <look> <action>.") : Show(look, action!.Id, inHand);

    /// <summary>Shows a look with one of its actions in a hand (main or off).</summary>
    internal DebugCommandResult Show(string heldLook, string actionId, string inHand)
    {
        if (Held.Look(heldLook) is null) return Failure($"Unknown look '{heldLook}'. Looks: {string.Join(", ", Held.Looks.Select(l => l.Id))}.");
        if (combat.Action(actionId) is not { } chosen) return Failure($"Unknown action '{actionId}'.");
        if (inHand is not ("main" or "off")) return Failure("The hand is main or off.");
        if (Held.Motion.Motions[Held.Look(heldLook)!.Motion].Track(chosen.Id) is null)
            return Failure($"Motion '{Held.Look(heldLook)!.Motion}' has no track for '{actionId}' and no default.");
        (look, action, hand, key) = (heldLook, chosen, inHand == "off" ? HeldView.OffHand : HeldView.MainHand, -1);
        View.View(hand, look);
        publish();
        return DebugCommandResult.Success(Describe());
    }

    [DebugCommand("riders.dev.motion.key", Description = "Developer motion viewer: hold the shown look exactly in key n's pose (0 is the first key; -1 is rest).")]
    public DebugCommandResult Key(int n)
    {
        if (look is null) return Failure("Choose a look first: riders.dev.motion <look> <action>.");
        if (n < -1 || n >= Keys.Length) return Failure($"Keys run from 0 to {Keys.Length - 1} (-1 is rest).");
        key = n;
        View.HoldKey(action!, n);
        publish();
        return DebugCommandResult.Success(n < 0 ? "rest" : Line(n));
    }

    [DebugCommand("riders.dev.motion.next", Description = "Developer motion viewer: hold the next key's pose (after the last, rest).")]
    public DebugCommandResult Next() => look is null ? Key(-1) : Key(key + 1 >= Keys.Length ? -1 : key + 1);

    [DebugCommand("riders.dev.motion.prev", Description = "Developer motion viewer: hold the previous key's pose (before the first, rest).")]
    public DebugCommandResult Previous() => look is null ? Key(-1) : Key(key - 1 < -1 ? Keys.Length - 1 : key - 1);

    [DebugCommand("riders.dev.motion.play", Description = "Developer motion viewer: play the action's motion from rest at a speed (1 is as in play, 0.25 a quarter), in real time.")]
    public DebugCommandResult Play(float speed)
    {
        if (look is null) return Failure("Choose a look first: riders.dev.motion <look> <action>.");
        if (!(speed > 0.01f && speed <= 4)) return Failure("Speed runs from 0.01 to 4.");
        key = -1;
        View.Hold(null);
        publish();
        View.Play(action!, speed);
        return DebugCommandResult.Success($"Playing {action!.Id} on {look} over {action.Duration / speed:0.##} s.");
    }

    [DebugCommand("riders.dev.motion.at", Description = "Developer motion viewer: show the action's motion paused at a moment, in seconds from its start (curves and all, as the Engine plays them).")]
    public DebugCommandResult At(float seconds)
    {
        if (look is null) return Failure("Choose a look first: riders.dev.motion <look> <action>.");
        ActionTiming t = action!.Timing;
        if (!(seconds >= 0 && seconds <= action.Duration)) return Failure($"The action runs from 0 to {action.Duration:0.###} s.");
        key = -1;
        View.Hold(null);
        publish();
        View.Play(action, 1, seconds);
        string phase = seconds < t.Windup ? "windup" : seconds < t.Windup + t.Commit ? "commit" : "recovery";
        return DebugCommandResult.Success($"{action.Id} at {seconds:0.###} s ({phase}; the strike is at {t.Windup:0.###} s).");
    }

    [DebugCommand("riders.dev.motion.pose", Description = "Developer motion viewer: set a pose of the shown look's motion (its grip moved from the stance by x y z metres, the look turned about it by x y z degrees) in the loaded content, hold it, and print its JSON.")]
    public DebugCommandResult Pose(string name, float x, float y, float z, float rx, float ry, float rz)
    {
        if (look is null) return Failure("Choose a look first: riders.dev.motion <look> <action>.");
        if (!Motion.Poses.TryGetValue(name, out HeldPose? pose))
            return Failure($"Motion '{Held.Look(look)!.Motion}' has no pose '{name}'. Poses: {string.Join(", ", Motion.Poses.Keys)}.");
        (pose.Position, pose.Rotation) = (new(x, y, z), new(rx, ry, rz));
        View.Hold(pose);
        publish();
        return DebugCommandResult.Success($"\"{name}\": {JsonSerializer.Serialize(pose, HeldJson.Default.HeldPose)}  (in {HeldMotionCatalog.Path} motions.{Held.Look(look)!.Motion}.poses)");
    }

    [DebugCommand("riders.dev.held.nudge", Description = "Developer held tuning: nudge the shown look from its stance (grip moved x y z metres, turned x y z degrees) in the loaded content, and print its JSON.")]
    public DebugCommandResult Nudge(float x, float y, float z, float rx, float ry, float rz)
    {
        if (look is null) return Failure("Choose a look first: riders.dev.motion <look> <action>.");
        HeldLookDefinition shown = Held.Look(look)!;
        (shown.Adjust.Grip, shown.Adjust.Turn) = (new(x, y, z), new(rx, ry, rz));
        Held.Seat(shown);
        publish();
        return DebugCommandResult.Success($"\"nudge\": {{\"grip\": [{x}, {y}, {z}], \"turn\": [{rx}, {ry}, {rz}]}}  (in {HeldCatalog.Path}, look '{look}')");
    }

    [DebugCommand("riders.dev.held.hand", Description = "Developer held tuning: set how the shown look's main hand holds it (grips.hand: the hand's canonical frame turned from the look, x y z degrees; in the off hand, mirrored) in the loaded content, and print its JSON. A look held by a solved grip on the drawing rig ignores it.")]
    public DebugCommandResult Hand(float rx, float ry, float rz) => Hold(false, new(rx, ry, rz));

    [DebugCommand("riders.dev.held.offhand", Description = "Developer held tuning: set how the off hand holds the shown look when it is held in both hands (grips.offHand, x y z degrees, not mirrored) in the loaded content, and print its JSON.")]
    public DebugCommandResult OffHand(float rx, float ry, float rz) => Hold(true, new(rx, ry, rz));

    private DebugCommandResult Hold(bool off, System.Numerics.Vector3 turn)
    {
        if (look is null) return Failure("Choose a look first: riders.dev.motion <look> <action>.");
        HeldLookDefinition shown = Held.Look(look)!;
        if (off && shown.Grips.OffHand is null) return Failure($"'{look}' is not held in both hands.");
        if (!off && shown.Grips.Hand is null) return Failure($"'{look}' has no hold of its own; it is held as content/{ArmsDefinition.Path} holds every look.");
        if (off) shown.Grips.OffHand = turn;
        else shown.Grips.Hand = turn;
        publish();
        return DebugCommandResult.Success(FormattableString.Invariant($"\"{(off ? "offHand" : "hand")}\": [{turn.X}, {turn.Y}, {turn.Z}]  (in {HeldCatalog.Path}, look '{look}' grips)"));
    }

    [DebugCommand("riders.dev.held.fit", Description = "Developer held tuning: turn the shown look's converted model in look space (x y z degrees, so its length runs along -Z and its up side +Y) and set its length in metres, in the loaded content, and print its JSON.")]
    public DebugCommandResult FitModel(float rx, float ry, float rz, float length)
    {
        if (look is null) return Failure("Choose a look first: riders.dev.motion <look> <action>.");
        if (Held.Look(look)!.Model is not { } model) return Failure($"'{look}' has no converted model; it is drawn as its box.");
        if (!(length > 0.01f)) return Failure("The length is positive metres.");
        model.Rotation = new(rx, ry, rz);
        model.Length = length;
        publish();
        return DebugCommandResult.Success($"\"rotation\": [{rx}, {ry}, {rz}], \"length\": {length}  (in {HeldCatalog.Path}, look '{look}' model)");
    }

    [DebugCommand("riders.dev.stance.set", Description = "Developer held tuning: move the shown look's stance (grip, the point its length heads toward, its up side; camera space x y z each) in the loaded content, reseating every look in it, and print its JSON.")]
    public DebugCommandResult Stance(float gx, float gy, float gz, float tx, float ty, float tz, float ux, float uy, float uz)
    {
        if (look is null) return Failure("Choose a look first: riders.dev.motion <look> <action>.");
        string id = Held.Look(look)!.Stance;
        HeldStance stance = Held.Stances.Stances[id];
        (stance.Grip, stance.Toward, stance.Up) = (new(gx, gy, gz), new(tx, ty, tz), new(ux, uy, uz));
        foreach (HeldLookDefinition each in Held.Looks.Where(l => l.Stance == id)) Held.Seat(each);
        publish();
        return DebugCommandResult.Success($"\"{id}\": {{\"grip\": [{gx}, {gy}, {gz}], \"toward\": [{tx}, {ty}, {tz}], \"up\": [{ux}, {uy}, {uz}]}}  (in {StanceCatalog.Path})");
    }

    [DebugCommand("riders.arms", Description = "Read the arms: the rig drawing them, how far each wrist was last drawn from its grip as the Engine IK solve reports it (metres; more than a centimetre is a grip out of reach), each arm's drawn length, and each hand's measured frame.")]
    public DebugCommandResult Arms()
    {
        FirstPersonArms arms = View.DrawnArms;
        var (main, off) = arms.Shortfall;
        var (mainLength, offLength) = arms.ArmLength;
        string frames = arms.HandFrames is { } f ? $"hand frames main [{f.Main.X:0}, {f.Main.Y:0}, {f.Main.Z:0}] off [{f.Off.X:0}, {f.Off.Y:0}, {f.Off.Z:0}] degrees" : "hand frames not measured yet";
        var (mainFelt, offFelt) = View.Felt;
        var (mainSway, offSway) = View.Swaying;
        string Sway((System.Numerics.Vector3 Move, Swap Swap) s) => FormattableString.Invariant($"[{s.Move.X:0.###}, {s.Move.Y:0.###}, {s.Move.Z:0.###}] {s.Swap}");
        return DebugCommandResult.Success($"Rig {View.Rig}. Sway main {Sway(mainSway)}, off {Sway(offSway)}. Feedback punches main {mainFelt}, off {offFelt}. Lean [{arms.Lean.X:0.###}, {arms.Lean.Y:0.###}, {arms.Lean.Z:0.###}] m, reaching in [{arms.Reaching.X:0.###}, {arms.Reaching.Y:0.###}, {arms.Reaching.Z:0.###}] m. Wrist shortfall: main {main:0.###} m, off {off:0.###} m. Arm length main {mainLength:0.###} m, off {offLength:0.###} m; {frames}. {Clip(arms.Clip)}");
    }

    // What clip-led arms sample (content/held/clips.json), or that they are weapon-led.
    private static string Clip(ArmClipReading clip) => clip.Led <= 0 ? "Weapon-led." : FormattableString.Invariant(
        $"Clip-led {clip.Led:0.##}: {clip.Playing?.Clip ?? "none"} at {clip.Playing?.Time ?? 0:0.###} ({clip.Playing?.Phase?.ToString() ?? "loop"}), blend {clip.Blend:0.##}{(clip.From is { } from ? $" over {from} at {clip.FromTime:0.###}" : "")}.");

    [DebugCommand("riders.dev.hands.contact", Description = "Developer grip check: how the drawn hands meet the main hand's look's grip landmarks (content/held/looks.json landmarks), as the Engine measured them on the poses it drew: the main index pad on the trigger, the web under the back strap, the little finger at the butt, the off palm on the fore-end, and each palm facing what it holds. Drawn as the overlay set held.landmarks.")]
    public DebugCommandResult Contact()
    {
        var (shown, read) = View.Grips;
        if (Held.Look(shown)?.Landmarks is null)
            return Failure($"{(shown == "" ? "Nothing" : shown)} is shown in the main hand, with no grip landmarks. Looks with them: {string.Join(", ", Held.Looks.Where(l => l.Landmarks is not null).Select(l => l.Id))}.");
        if (!read.Measured || read.Pairs.Length == 0) return Failure("The grip landmarks are not measured yet: the renderer has not drawn them.");
        List<string> lines = [$"Grip landmarks on {shown} (rig {View.Rig}), as drawn at step {read.Step}:"];
        foreach (Rusty.Engine.LandmarkMeasure pair in read.Pairs.Span) lines.Add("  " + GripLandmarks.Line(pair));
        return DebugCommandResult.Success(string.Join("\n", lines));
    }

    [DebugCommand("riders.dev.hands.haft", Description = "Developer grip check: each main-hand digit joint's distance from the main grip's line along the look's length (look Z), radially and along it (mm), as last drawn. A joint nearer the line than the handle's radius is inside the handle.")]
    public DebugCommandResult Haft()
    {
        Rusty.Engine.LandmarkReadResult read = View.Haft;
        if (!read.Measured) return Failure("not measured yet");
        List<string> lines = [$"Main digit joints from the haft's line (look Z) at step {read.Step}:"];
        foreach (Rusty.Engine.LandmarkMeasure pair in read.Pairs.Span)
        {
            System.Numerics.Vector3 d = pair.Offset;
            lines.Add(FormattableString.Invariant($"  {pair.Name}: radial {MathF.Sqrt(d.X * d.X + d.Y * d.Y) * 1000:0.0} mm, along {d.Z * 1000:0.0} mm"));
        }
        return DebugCommandResult.Success(string.Join("\n", lines));
    }

    [DebugCommand("riders.dev.arms.wrist", Description = "Developer wrist check: each wrist's asked bend on the last reach (flexion, negative is extension; ulnar, negative is radial; roll, positive turns the palm up; degrees), how far the Engine turned it back into the rig's wrist range (Clamped, 0 in range) and how far the elbow swung to help, with the look each hand holds.")]
    public DebugCommandResult Wrist()
    {
        var (main, off) = View.DrawnArms.WristClamp;
        return DebugCommandResult.Success(FormattableString.Invariant(
            $"Wrist range of rig {View.Rig}: {View.DrawnArms.Rig.Wrist}.\n  main ({View.Holding(HeldView.MainHand)}) {main}\n  off ({View.Holding(HeldView.OffHand)}) {off}"));
    }

    [DebugCommand("riders.dev.hands.knuckles", Description = "Developer hand calibration: each drawn hand's knuckle angles in the hand's canonical frame (each finger joint's curl, each thumb joint's [curl, swing], degrees) on the rig's clip alone and as now drawn, against the arms' canonical holding pose (content/held/arms.json hands.pose), and the rig grip that brings this clip to that pose.")]
    public DebugCommandResult Knuckles()
    {
        FirstPersonArms arms = View.DrawnArms;
        HandShape pose = Held.Arms.Hands.Pose;
        List<string> lines = [$"Knuckles on rig {View.Rig} (canonical pose {HandKnuckles.Json(pose)}):"];
        List<string> grips = [];
        foreach (var (name, offHand) in new[] { ("main", false), ("off", true) })
        {
            if (arms.Knuckles(offHand) is not var (clip, drawn)) return Failure("The hands are not measured yet: the arms have not been drawn.");
            lines.Add($"  {name} clip  {HandKnuckles.Json(clip)}");
            lines.Add($"  {name} drawn {HandKnuckles.Json(drawn)}");
            grips.Add($"\"{name}\": {HandKnuckles.Json(HandKnuckles.Difference(pose, clip))}");
        }
        lines.Add($"  to the pose: \"grip\": {{{string.Join(", ", grips)}}}  (in {ArmRigs.PathOf(View.Rig)}; leave out \"off\" when it is the main's)");
        return DebugCommandResult.Success(string.Join("\n", lines));
    }

    [DebugCommand("riders.dev.arms.grip", Description = "Developer hand calibration: set one finger's grip curls (base, middle, tip degrees) on the drawing rig, for the main or off hand (where the rig gives the off hand its own), in the loaded content, and print the hand's JSON.")]
    public DebugCommandResult ArmsGrip(string inHand, string finger, float b, float m, float t)
    {
        string[] fingers = ["index", "middle", "ring", "little"];
        int f = Array.IndexOf(fingers, finger);
        if (f < 0) return Failure($"The finger is {string.Join(", ", fingers)}.");
        if (inHand is not ("main" or "off")) return Failure("The hand is main or off.");
        ArmRig rig = Held.Rigs[View.Rig];
        if ((inHand == "main" ? rig.Grip.Main : rig.Grip.Off) is not { } hand)
            return Failure($"Rig '{View.Rig}' gives no off grip; its off hand takes the main's mirrored.");
        (hand.Fingers[f][0], hand.Fingers[f][1], hand.Fingers[f][2]) = (b, m, t);
        publish();
        return DebugCommandResult.Success($"\"{inHand}\": {HandKnuckles.Json(hand)}  (in {ArmRigs.PathOf(View.Rig)} grip)");
    }

    [DebugCommand("riders.dev.arms.rig", Description = "Developer arms comparison: draw the arms by another rig profile (generated, painted, leather, shared or psx; content/held/rigs) with the same hold, in the loaded content.")]
    public DebugCommandResult Rig(string id)
    {
        if (View.ShowRig(id) is { } refusal) return Failure(refusal);
        publish();
        return DebugCommandResult.Success($"Arms drawn by rig '{id}'. Set \"rig\": \"{id}\" in {ArmsDefinition.Path} to keep it.");
    }

    [DebugCommand("riders.dev.arms.hold", Description = "Developer arms tuning: set how a hand holds a look (its canonical frame's turn from the look, x y z degrees; every rig, the left mirrored) and the drawing rig's palm offset from its hand joint (x y z metres), in the loaded content, and print their JSON.")]
    public DebugCommandResult ArmsHold(float rx, float ry, float rz, float px, float py, float pz)
    {
        Held.Arms.Hold = new(rx, ry, rz);
        Held.Rigs[Held.Arms.Rig].Palm = new(px, py, pz);
        publish();
        return DebugCommandResult.Success($"\"hold\": [{rx}, {ry}, {rz}]  (in {ArmsDefinition.Path}); \"palm\": [{px}, {py}, {pz}]  (in {ArmRigs.PathOf(Held.Arms.Rig)})");
    }

    [DebugCommand("riders.dev.arms.reach", Description = "Developer arms tuning: set where the right shoulder sits and the point the right elbow bends toward (camera space x y z each; the left mirrors them) in the loaded content, and print their JSON.")]
    public DebugCommandResult ArmsReach(float sx, float sy, float sz, float px, float py, float pz)
    {
        ArmsDefinition arms = Held.Arms;
        (arms.Shoulder, arms.Pole) = (new(sx, sy, sz), new(px, py, pz));
        publish();
        return DebugCommandResult.Success($"\"shoulder\": [{sx}, {sy}, {sz}], \"pole\": [{px}, {py}, {pz}]  (in {ArmsDefinition.Path})");
    }

    [DebugCommand("riders.dev.held.feel", Description = "Developer held tuning: the main or off hand feels its action land (struck, blocked or fired): a feedback punch layered over its motion, as content/held/feedback.json says.")]
    public DebugCommandResult Feel(string kind, string inHand)
    {
        if (!Enum.TryParse(kind, true, out FeedbackKind felt)) return Failure($"Feedback is {string.Join(", ", Enum.GetNames<FeedbackKind>()).ToLowerInvariant()}.");
        if (inHand is not ("main" or "off")) return Failure("The hand is main or off.");
        View.Feel(inHand == "off" ? HeldView.OffHand : HeldView.MainHand, felt);
        publish();
        return DebugCommandResult.Success($"The {inHand} hand feels {felt}.");
    }

    [DebugCommand("riders.dev.motion.off", Description = "Developer motion viewer: return the hands to what they hold.")]
    public DebugCommandResult Off()
    {
        (look, action, key) = (null, null, -1);
        View.View(hand, null);
        publish();
        return DebugCommandResult.Success("Motion viewer off.");
    }

    private string Describe()
    {
        ActionTiming t = action!.Timing;
        return $"{look} in the {(hand == HeldView.OffHand ? "off" : "main")} hand · {action.Id} (motion '{Held.Look(look!)!.Motion}'; windup {t.Windup} s, commit {t.Commit} s, recovery {t.Recovery} s; the strike is at {t.Windup} s)\n" +
            string.Join("\n", Enumerable.Range(0, Keys.Length).Select(Line));
    }

    private string Line(int i)
    {
        MotionKey k = Keys[i];
        HeldPose pose = Motion.Poses[k.Pose];
        return $"{i}: {k.Pose} at {k.Phase} {k.At:0.##} = {k.Time(HeldMotionCatalog.Timing(action!)):0.###} s, {k.Ease}, grip {k.Grip}; position [{pose.Position.X}, {pose.Position.Y}, {pose.Position.Z}] rotation [{pose.Rotation.X}, {pose.Rotation.Y}, {pose.Rotation.Z}]";
    }

    private static DebugCommandResult Failure(string message) => DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, message);
}
