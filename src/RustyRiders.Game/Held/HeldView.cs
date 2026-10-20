using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using RustyRiders.Game.Actions;
using RustyRiders.Game.Art;
using RustyRiders.Game.Content;

namespace RustyRiders.Game.Held;

/// <summary>
/// What the two hands hold, drawn on the viewmodel layer: each hand's look published at rest where its stance seats it
/// (the off hand mirrored), and moved through an action by an Engine tween timeline built from the look's motion,
/// started as the action starts and eased back to rest when it is cut short. Also the developer motion viewer, which
/// shows any look in a hand, holds a key's pose, plays an action at a speed or shows a moment of it. Adapted from
/// rusty-hotel's <c>CombatView</c> held presentation (docs/reuse.md).
/// </summary>
internal sealed class HeldView : IDisposable
{
    internal const int MainHand = 0, OffHand = 1;
    private const string OldArtRoot = "old-art";

    private readonly IEngineContext engine;
    private readonly HeldCatalog held;
    private readonly ulong firstObject;
    private readonly ConvertedArt art;
    private readonly List<Appearance> boxes = [];
    // Each look's appearance and its opened model (none when it is drawn as its box).
    private readonly Dictionary<string, (Appearance Appearance, ArtMesh? Mesh)> looks = [];
    private readonly Hand[] hands = [new(), new()];
    private ArmsView arms;
    // Arms replaced by another rig: released once a snapshot without them is published (an object keeps one
    // animation instance, so each rig is drawn on its own object, alternating between two).
    private ArmsView? retired;
    private readonly List<string> armsProblems = [];
    /// <summary>The rig profile that draws the arms when none other is chosen or opens: the generated arms, kept in git.</summary>
    internal const string FallbackRig = "generated";
    private ulong startedSeen;
    private (int Hand, string Look, HeldPose? Pose)? viewing;
    private (ActionDefinition Action, int Key)? viewingKey;

    /// <param name="firstObject">The published object id of the main hand's look; the off hand's and the arms' two are the next three.</param>
    internal HeldView(IEngineContext engine, HeldCatalog held, ulong firstObject)
    {
        this.engine = engine;
        this.held = held;
        this.firstObject = firstObject;
        art = new ConvertedArt(engine, OldArtRoot);
        arms = Arms(held.Arms.Rig, firstObject + 2) ?? Arms(FallbackRig, firstObject + 2)
            ?? throw new InvalidOperationException($"The fallback arms did not open: {string.Join("; ", armsProblems)}");
        foreach (HeldLookDefinition look in held.Looks)
            looks[look.Id] = look.Model is { } model && art.Model(model.Glb) is { } mesh ? (mesh.Appearance, mesh) : (Box(look.Box), null);
    }

    /// <summary>Converted models that could not be opened (their looks are drawn as boxes).</summary>
    internal IReadOnlyList<string> Problems => [.. art.Problems, .. armsProblems];

    /// <summary>The rig profile drawing the arms now.</summary>
    internal string Rig => arms.Id;

    /// <summary>The arms as drawn now: wrist shortfall, arm lengths and the measured hand frames.</summary>
    internal ArmsView DrawnArms => arms;

    /// <summary>
    /// Developer arms comparison: draws the arms by another rig profile, with the same hold; returns why it could not,
    /// or null. The caller publishes, after which the previous arms are released.
    /// </summary>
    internal string? ShowRig(string id)
    {
        if (!held.Rigs.ContainsKey(id)) return $"Unknown rig '{id}'. Rigs: {string.Join(", ", held.Rigs.Keys)}.";
        if (id == arms.Id) return null;
        int problems = armsProblems.Count;
        if (Arms(id, arms.Object == firstObject + 2 ? firstObject + 3 : firstObject + 2) is not { } next)
            return armsProblems[problems];
        retired?.Dispose();
        (retired, arms) = (arms, next);
        held.Arms.Rig = id;
        return null;
    }

    // The arms drawn by a rig profile on an object, or null with the reason recorded when its model does not open
    // (an old-game model not imported here).
    private ArmsView? Arms(string id, ulong objectId)
    {
        try
        {
            return new ArmsView(engine, held.Arms, held.Rigs[id], id, objectId);
        }
        catch (Exception error) when (error is EngineCallException or InvalidOperationException)
        {
            armsProblems.Add($"arms rig {id}: {error.Message}");
            return null;
        }
    }

    internal HeldCatalog Catalog => held;

    /// <summary>
    /// The hands' looks as published (camera space, viewmodel layer): each at rest, or in the pose the motion viewer
    /// holds; an action's motion plays over them as the Engine tween.
    /// </summary>
    internal IEnumerable<AppearanceFact> Facts(string mainLook, string offLook, bool visible)
    {
        hands[MainHand].Look = viewing is { Hand: MainHand } v ? v.Look : mainLook;
        hands[OffHand].Look = viewing is { Hand: OffHand } w ? w.Look : offLook;
        for (int h = 0; h < hands.Length; h++)
        {
            HeldLookDefinition look = held.Look(hands[h].Look)!;
            HeldPose pose = viewing is { } view && view.Hand == h && Viewed() is { } shown ? shown : Rest(look);
            (Vector3 at, Quaternion turn) = Drawn(look, pose, h == OffHand);
            yield return new AppearanceFact(firstObject + (ulong)h, false, 0, new Transform(at, turn, Fit(look).Scale),
                looks[look.Id].Appearance, visible, RenderLayer.Viewmodel);
        }
        // The body leans with a blow: the arms move by the acting hand's motion (only one hand acts at a time).
        arms.Lean = Keyed(MainHand).Lean + Keyed(OffHand).Lean;
        arms.Aim(Grip(MainHand), Grip(OffHand));
        yield return arms.Fact(visible);
        // Replaced arms are published hidden once, then left out; they are released after that snapshot.
        if (retired is { Published: false }) yield return retired.Fact(false);
    }

    /// <summary>How far each wrist was drawn from its grip (see <see cref="ArmsView.Shortfall"/>).</summary>
    internal (float Main, float Off) ArmsShortfall => arms.Shortfall;

    /// <summary>
    /// Once the looks are in the published snapshot: starts the acting hand's motion as its action starts (that far
    /// along already), and settles a hand back to rest when its action is cut short or its look changes.
    /// </summary>
    internal void Animate(ActionUser user, int actingHand)
    {
        for (int h = 0; h < hands.Length; h++)
        {
            Hand hand = hands[h];
            if (hand.Tween is not { } playing) continue;
            // Only an action's own motion is cut short (a settle and the viewer's playback start no action: 0).
            bool cutShort = playing.Started != 0 && playing.Started == user.Started && (user.Current is null || h != actingHand);
            if (playing.Look != hand.Look) Cancel(hand);
            else if (cutShort && Running(playing.Handle)) Begin(h, held.Motion.Settle(Offset(hand)), TweenClock.World);
        }
        if (viewing is null && user.Started != startedSeen)
        {
            startedSeen = user.Started;
            if (user.Current is { } action)
            {
                Hand acting = hands[actingHand];
                HeldLookDefinition look = held.Look(acting.Look)!;
                TweenSegment[] timeline = held.Motion.Timeline(held.Motion.Motions[look.Motion], action, pose => Drawn(look, pose, actingHand == OffHand),
                    user.Scale, from: Offset(acting));
                Begin(actingHand, timeline, TweenClock.World, user.Elapsed, action, user.Started, user.Scale);
            }
        }
        Reach();
    }

    /// <summary>Reaches the arms for both hands' grips where their looks are drawn now, each hand closed as its motion says.</summary>
    internal void Reach()
    {
        if (retired is not null && !retired.Published)
            retired.Published = true;
        else if (retired is not null)
        {
            retired.Dispose();
            retired = null;
        }
        (Vector3 mainGrip, Quaternion mainLook) = Grip(MainHand);
        (Vector3 offGrip, Quaternion offLook) = Grip(OffHand);
        arms.Reach((mainGrip, mainLook, Keyed(MainHand).Grip), (offGrip, offLook, Keyed(OffHand).Grip));
    }

    /// <summary>Ends every motion (a level or the kit changes).</summary>
    internal void Reset()
    {
        foreach (Hand hand in hands) Cancel(hand);
        viewing = null;
        viewingKey = null;
    }

    /// <summary>The look a hand shows now: the motion viewer's, or what it holds.</summary>
    internal string Shown(int hand) => hands[hand].Look;

    /// <summary>The hand and look the motion viewer shows, if it is on.</summary>
    internal (int Hand, string Look)? Viewing => viewing is { } v ? (v.Hand, v.Look) : null;

    /// <summary>Developer motion viewer: shows a look in a hand in place of what it holds, at rest; null returns to play.</summary>
    internal void View(int hand, string? look)
    {
        foreach (Hand each in hands) Cancel(each);
        viewing = look is null ? null : (hand, look, null);
        viewingKey = null;
    }

    /// <summary>Developer motion viewer: holds the shown look at one of an action's keys, as the key is now; -1 holds rest.</summary>
    internal void HoldKey(ActionDefinition action, int key)
    {
        if (viewing is not { } view) return;
        Cancel(hands[view.Hand]);
        viewing = view with { Pose = null };
        viewingKey = key < 0 ? null : (action, key);
    }

    /// <summary>Developer motion viewer: holds the shown look in a pose exactly (no tween), or at rest for null.</summary>
    internal void Hold(HeldPose? pose)
    {
        if (viewing is not { } view) return;
        Cancel(hands[view.Hand]);
        viewing = view with { Pose = pose };
        viewingKey = null;
    }

    /// <summary>
    /// Developer motion viewer: plays an action's motion on the shown look from rest at a speed, in real time (the
    /// world holds while the player stands still), or with <paramref name="at"/>, shows it paused at that moment.
    /// The look must already be published at rest.
    /// </summary>
    internal void Play(ActionDefinition action, float speed, float? at = null)
    {
        if (viewing is not { } view) return;
        viewing = view with { Pose = null };
        viewingKey = null;
        HeldLookDefinition look = held.Look(view.Look)!;
        TweenSegment[] timeline = held.Motion.Timeline(held.Motion.Motions[look.Motion], action, pose => Drawn(look, pose, view.Hand == OffHand), speed: speed);
        TweenHandle handle = Begin(view.Hand, timeline, TweenClock.Realtime, action: action, speed: speed);
        if (at is not { } seconds) return;
        engine.Tween.Control(new TweenControlRequest(handle, TweenControl.Pause));
        engine.Tween.Control(TweenControlRequest.Seek(handle, seconds));
    }

    /// <summary>
    /// Where a hand's look is drawn now (camera space): the viewer's held pose, or rest under the offset its playing
    /// motion shows where the Engine reports it has got to.
    /// </summary>
    internal (Vector3 At, Quaternion Turn) Now(int hand)
    {
        HeldLookDefinition look = held.Look(hands[hand].Look)!;
        if (viewing is { } view && view.Hand == hand && Viewed() is { } pose) return Drawn(look, pose, hand == OffHand);
        (Vector3 at, Quaternion turn) = Drawn(look, Rest(look), hand == OffHand);
        (Vector3 move, Quaternion offset) = Offset(hands[hand]);
        return (at + move, Quaternion.Normalize(turn * offset));
    }

    /// <summary>
    /// Each hand's grip where its look is drawn now, as Engine inspection targets (camera space, framed from the side
    /// along the look's turn): <c>main-grip</c> and <c>off-grip</c>, which the developer shell's views frame.
    /// </summary>
    internal IReadOnlyList<InspectionTarget> Targets()
    {
        List<InspectionTarget> targets = [];
        foreach (var (hand, id) in new[] { (MainHand, "main-grip"), (OffHand, "off-grip") })
        {
            (Vector3 grip, Quaternion lookTurn) = Grip(hand);
            targets.Add(new(id, OverlayPlace.Camera, grip) { Rotation = lookTurn, Radius = GripFrame, Framing = "side" });
        }
        return targets;
    }

    // A hand's grip where its look is drawn now (camera space) and the look's turn there.
    private (Vector3 Grip, Quaternion Look) Grip(int hand)
    {
        HeldLookDefinition look = held.Look(hands[hand].Look)!;
        Transform fit = Fit(look);
        (Vector3 at, Quaternion turn) = Now(hand);
        // The drawn model is the look turned by its fit, so the look's own turn is the drawn turn less the fit's.
        Quaternion lookTurn = Quaternion.Normalize(turn * Quaternion.Inverse(fit.Rotation));
        return (at + Vector3.Transform(Authored.Vector(look.Grips.Main) - fit.Translation, lookTurn), lookTurn);
    }

    // How closed a hand is now (-1 open, 0 its grip, 1 squeezed) and how far its motion leans the arms (camera space,
    // mirrored for the off hand): the viewer's held key, or the playing action's keys where the Engine reports its
    // tween has got to, eased by each key's curve as the Engine eases them.
    private (float Grip, Vector3 Lean) Keyed(int index)
    {
        Hand hand = hands[index];
        Vector3 Lean(MotionKey key) => key.Shoulder is { } lean ? (index == OffHand ? Frames.Mirror(Authored.Vector(lean)) : Authored.Vector(lean)) : Vector3.Zero;
        if (viewing is { } view && view.Hand == index && viewingKey is { } key)
            return Keys(view.Look, key.Action) is { } keys && key.Key < keys.Length ? (keys[key.Key].Grip, Lean(keys[key.Key])) : (0, Vector3.Zero);
        if (hand.Tween is not { } playing || hand.Action is not { } action || Keys(hand.Look, action) is not { } track) return (0, Vector3.Zero);
        TweenReadout readout = engine.Tween.Read(playing.Handle);
        if (readout.State == TweenState.Ended) return (0, Vector3.Zero);
        float time = (float)readout.ElapsedSeconds * hand.Speed / hand.Scale;
        (float grip, Vector3 lean, float start) from = (0, Vector3.Zero, 0);
        foreach ((float at, float grip, Vector3 lean, TweenEasingKind curve) in track.Select(k => (k.Time(action.Timing), k.Grip, Lean(k), k.Ease))
            .Append((action.Duration, 0f, Vector3.Zero, held.Motion.SettleEase)))
        {
            if (time <= at)
            {
                float t = at - from.start <= 0 ? 1 : engine.Tween.Evaluate(curve, Math.Clamp((time - from.start) / (at - from.start), 0, 1));
                return (from.grip + (grip - from.grip) * t, Vector3.Lerp(from.lean, lean, t));
            }
            from = (grip, lean, at);
        }
        return (0, Vector3.Zero);
    }

    private MotionKey[]? Keys(string look, ActionDefinition action) => held.Motion.Motions[held.Look(look)!.Motion].Track(action.Id);

    // How far around a grip its inspection view frames (metres).
    private const float GripFrame = .35f;

    public void Dispose()
    {
        arms.Dispose();
        retired?.Dispose();
        art.Dispose();
        foreach (Appearance box in boxes) box.Dispose();
    }

    private HeldPose Rest(HeldLookDefinition look) => held.Motion.Motions[look.Motion].Poses[HeldMotionCatalog.Rest];

    // The pose the viewer holds: the held key's, as its track says now (rest once the key is gone), or the pose it was given.
    private HeldPose? Viewed()
    {
        if (viewing is not { } view) return null;
        if (viewingKey is not { } key) return view.Pose;
        HeldMotion motion = held.Motion.Motions[held.Look(view.Look)!.Motion];
        return motion.Track(key.Action.Id) is { } track && key.Key < track.Length ? motion.Poses.GetValueOrDefault(track[key.Key].Pose) : null;
    }

    // Where a look's drawn model is in a pose (camera space): the look placed by the pose, its fit inside look space after.
    private (Vector3 At, Quaternion Turn) Drawn(HeldLookDefinition look, HeldPose pose, bool offHand)
    {
        (Vector3 at, Quaternion turn) = look.Place(pose, offHand);
        Transform fit = Fit(look);
        return (at + Vector3.Transform(fit.Translation, turn), Quaternion.Normalize(turn * fit.Rotation));
    }

    // Starts a hand's motion tween, replacing its tweens; remembers it so where it shows the look can be sampled.
    private TweenHandle Begin(int hand, TweenSegment[] segments, TweenClock clock, float elapsed = 0, ActionDefinition? action = null, ulong started = 0,
        float scale = 1, float speed = 1)
    {
        hands[hand].Scale = scale;
        hands[hand].Speed = speed;
        TweenStartRequest request = new(firstObject + (ulong)hand, segments) { Clock = clock };
        TweenHandle handle = engine.Tween.Start(request with { ElapsedSeconds = elapsed }).Tween;
        hands[hand].Tween = (handle, request, hands[hand].Look, started);
        hands[hand].Action = action;
        return handle;
    }

    private void Cancel(Hand hand)
    {
        if (hand.Tween is { } playing) engine.Tween.Control(new TweenControlRequest(playing.Handle, TweenControl.Cancel));
        hand.Tween = null;
        hand.Action = null;
    }

    // The offset a hand's motion shows now (from its rest placement), or none.
    private (Vector3 Move, Quaternion Turn) Offset(Hand hand)
    {
        if (hand.Tween is not { } playing || playing.Look != hand.Look) return (Vector3.Zero, Quaternion.Identity);
        TweenReadout readout = engine.Tween.Read(playing.Handle);
        if (readout.State == TweenState.Ended) return (Vector3.Zero, Quaternion.Identity);
        TweenSample sample = engine.Tween.Sample(playing.Request, readout.ElapsedSeconds);
        return (sample.Translation, sample.Rotation);
    }

    private bool Running(TweenHandle tween) => engine.Tween.Read(tween).State != TweenState.Ended;

    // How a look is drawn inside look space: its converted model centred on its bounds, scaled so its longest side is
    // the authored length and turned by the authored rotation (read each time, so tuning them shows at once), or its box.
    private Transform Fit(HeldLookDefinition look)
    {
        if (looks[look.Id].Mesh is not { } mesh || look.Model is not { } model)
            return new Transform(Vector3.Zero, Quaternion.Identity, Authored.Vector(look.Box.Size));
        Vector3 extent = mesh.BoundsMax - mesh.BoundsMin, centre = (mesh.BoundsMax + mesh.BoundsMin) / 2;
        float scale = model.Length / MathF.Max(1e-4f, MathF.Max(extent.X, MathF.Max(extent.Y, extent.Z)));
        Quaternion rotation = Frames.Turn(model.Rotation);
        return new Transform(Vector3.Transform(-centre * scale, rotation), rotation, new Vector3(scale));
    }

    private Appearance Box(HeldBox box)
    {
        Appearance made = engine.Graphics.CreatePrimitive(new PrimitiveAppearanceRequest(PrimitiveGeometry.Cube, false, Authored.Color(box.Color)));
        boxes.Add(made);
        return made;
    }
    // A hand's shown look and the motion tween last started on it: the timeline it plays (to sample where it shows the
    // look), the look it was started for, the action start it plays (0 for a settle or the viewer), and that action.
    private sealed class Hand
    {
        internal string Look = "";
        internal (TweenHandle Handle, TweenStartRequest Request, string Look, ulong Started)? Tween;
        internal ActionDefinition? Action;
        // The action's time scale and the viewer's playback speed, to read the keys at the tween's elapsed time.
        internal float Scale = 1, Speed = 1;
    }
}
