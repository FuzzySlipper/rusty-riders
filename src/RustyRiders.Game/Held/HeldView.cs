using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using Rusty.Engine.FirstPerson;
using RustyRiders.Game.Actions;
using RustyRiders.Game.Art;
using RustyRiders.Game.Content;

namespace RustyRiders.Game.Held;

/// <summary>
/// What the two hands hold, drawn on the viewmodel layer: each hand's look published at rest where its stance seats it
/// (the off hand mirrored), and moved through an action by an Engine tween timeline built from the look's motion,
/// started as the action starts and eased back to rest when it is cut short; the arms reaching both hands' grips. Also
/// the developer motion viewer, which shows any look in a hand, holds a key's pose, plays an action at a speed or shows
/// a moment of it. Adapted from rusty-hotel's <c>CombatView</c> held presentation (docs/reuse.md); each hand's tween
/// state is a <see cref="HeldHand"/> and the arms an <see cref="ArmsSlot"/>.
/// </summary>
internal sealed class HeldView : IDisposable
{
    internal const int MainHand = 0, OffHand = 1;
    private const string ArtRoot = "art";
    // How far around a grip its inspection view frames (metres).
    private const float GripFrame = .35f;
    // The overlay set the developer grip check publishes its landmark pairs in.
    internal const string LandmarkSet = "held.landmarks";

    private readonly IEngineContext engine;
    private readonly HeldCatalog held;
    private readonly ConvertedArt art;
    private readonly List<Appearance> boxes = [];
    // Each look's appearance and its opened model (none when it is drawn as its box).
    private readonly Dictionary<string, (Appearance Appearance, ArtMesh? Mesh)> looks = [];
    private readonly HeldHand[] hands;
    private readonly ArmsSlot arms;
    private readonly GripLandmarks grips;
    private readonly List<(int Hand, FeedbackKind Kind)> felt = [];
    private readonly HeldSway sway;
    // A put-away tween holds its last pose this much past its end, so the old look never shows at rest before the swap.
    private const float PutAwayHold = .25f;
    private (int Hand, string Look, HeldPose? Pose)? viewing;
    // The main hand's look is held in both hands.
    private bool twoHanded;
    private (ActionDefinition Action, int Key)? viewingKey;
    private readonly ArmsLead lead = new();
    // Where the main hand holds its look this update while the arms are clip-led (it rides the hand), or null.
    private (Vector3 Grip, Quaternion Look)? ridden;

    /// <param name="firstObject">The published object id of the main hand's look; the off hand's and the arms' two are the next three.</param>
    internal HeldView(IEngineContext engine, HeldCatalog held, ulong firstObject)
    {
        this.engine = engine;
        this.held = held;
        art = new ConvertedArt(engine, ArtRoot);
        hands = [new(engine, firstObject), new(engine, firstObject + 1)];
        arms = new ArmsSlot(engine, held, firstObject + 2);
        grips = new GripLandmarks(engine, LandmarkSet);
        sway = new HeldSway(held.Sway);
        foreach (HeldLookDefinition look in held.Looks)
            looks[look.Id] = look.Model is { } model && art.Model(model.Glb) is { } mesh ? (mesh.Appearance, mesh) : (Box(look.Box), null);
    }

    internal HeldCatalog Catalog => held;

    /// <summary>Converted models that could not be opened (their looks are drawn as boxes) and arms rigs that did not open.</summary>
    internal IReadOnlyList<string> Problems => [.. art.Problems, .. arms.Problems];

    /// <summary>How many feedback punches each hand has had since its motion last started (ended ones are counted until sampled).</summary>
    internal (int Main, int Off) Felt => (hands[MainHand].Layers.Count, hands[OffHand].Layers.Count);

    /// <summary>Each hand's sway now (its grip's move, metres) and where it is in swapping what it holds.</summary>
    internal ((Vector3 Sway, Swap Swap) Main, (Vector3 Sway, Swap Swap) Off) Swaying =>
        ((hands[MainHand].Sway.Position, hands[MainHand].Swap), (hands[OffHand].Sway.Position, hands[OffHand].Swap));

    /// <summary>The rig profile drawing the arms now.</summary>
    internal string Rig => arms.Id;

    /// <summary>The look a hand holds, and whether it holds it by a solved grip on the drawing rig.</summary>
    internal string Holding(int hand)
    {
        if (hand == OffHand && twoHanded) return $"{hands[MainHand].Look}, both hands";
        return held.Look(hands[hand].Look) is { } look && look.Solved.ContainsKey(Rig) ? $"{look.Id}, solved grip" : hands[hand].Look;
    }

    /// <summary>The arms as drawn now: wrist shortfall, arm lengths and the measured hand frames.</summary>
    internal FirstPersonArms DrawnArms => arms.Drawn;

    /// <summary>How far each wrist was drawn from its grip (see <see cref="FirstPersonArms.Shortfall"/>).</summary>
    internal (float Main, float Off) ArmsShortfall => arms.Drawn.Shortfall;

    /// <summary>Developer arms comparison: draws the arms by another rig profile (see <see cref="ArmsSlot.Show"/>).</summary>
    internal string? ShowRig(string id) => arms.Show(id);

    /// <summary>
    /// The hands' looks as published (camera space, viewmodel layer): each at rest, or in the pose the motion viewer
    /// holds; an action's motion plays over them as the Engine tween.
    /// </summary>
    /// <param name="twoHanded">The main hand's look is held in both hands: the off hand's own look is hidden and the off hand holds the main look's off grip.</param>
    /// <param name="users">Each hand's actions; the main hand's leads clip-led arms.</param>
    internal IEnumerable<AppearanceFact> Facts(string mainLook, string offLook, bool twoHanded, bool visible, IReadOnlyList<ActionUser> users)
    {
        // The motion viewer shows a look with an off grip in both hands when it shows it in the main hand.
        // Each hand shows what it holds once a swap has drawn it (see Swaps); the viewer shows its look at once.
        foreach (var (hand, wanted) in new[] { (hands[MainHand], mainLook), (hands[OffHand], offLook) })
        {
            hand.Wanted = wanted;
            if (hand.Look == "") hand.Look = wanted;
        }
        if (viewing is { } viewed) hands[viewed.Hand].Look = viewed.Look;
        // A look held in both hands takes the off hand once it is drawn.
        this.twoHanded = viewing is { } shownLook ? shownLook.Hand == MainHand && held.Look(shownLook.Look)!.Grips.Off is not null
            : twoHanded && hands[MainHand].Look == mainLook;
        Lead(users[MainHand]);
        for (int h = 0; h < hands.Length; h++)
        {
            HeldLookDefinition look = held.Look(hands[h].Look)!;
            HeldPose pose = viewing is { } view && view.Hand == h && Viewed() is { } shown ? shown : Placed(h);
            (Vector3 at, Quaternion turn) = h == MainHand && ridden is { } hold ? Held(look, hold) : Drawn(look, pose, h == OffHand);
            bool drawn = !(h == OffHand && this.twoHanded) && hands[h].Swap != Swap.DrawNext;
            yield return new AppearanceFact(hands[h].Object, false, 0, new Transform(at, turn, Fit(look).Scale),
                looks[look.Id].Appearance, visible && drawn, RenderLayer.Viewmodel);
        }
        // The body leans with a blow: the arms move by both hands' motions together (two blows at once lean as their sum;
        // the arms' own reach then keeps each hand on its grip).
        arms.Drawn.Lean = Keyed(MainHand).Lean + Keyed(OffHand).Lean;
        // A clip-led main hand reaches for nothing; while the arms fade it reaches the blend.
        if (arms.Drawn.Clip.Led < 1) arms.Drawn.Aim(Aim(MainHand), Aim(OffHand));
        yield return arms.Drawn.Fact(visible);
        if (arms.Retired is { } retired) yield return retired;
    }

    /// <summary>World seconds admitted, on which clip-led arms play and fade.</summary>
    internal void Step(float seconds) => lead.Step(seconds);

    // Clip-led arms (Engine FirstPersonArms.Lead): the main hand's action clip, or its look's idle loop, where the drawing
    // rig has clips for the look's motion; then where the look rides the hand. The arms are carried to the rig's clip
    // framing, and by the hand's sway while clip-led.
    private void Lead(ActionUser user)
    {
        ArmClipFrame? frame = lead.Frame(MainClips(), user);
        arms.Drawn.Lead(frame, lead.Take());
        arms.Drawn.Carry = held.Clips.Carry(Rig) + (frame is null ? Vector3.Zero : hands[MainHand].Sway.Position);
        ridden = null;
        if (arms.Drawn.Clip.Led > 0) ridden = arms.Drawn.Ride(Aim(MainHand));
    }

    // The clips the main hand's look plays on the drawing rig, or null where it is weapon-led (and in the motion viewer).
    private MotionClips? MainClips() =>
        viewing is null && held.Look(hands[MainHand].Look) is { } look ? held.Clips.For(Rig, look.Motion) : null;

    /// <summary>
    /// Once the looks are in the published snapshot: starts a hand's motion as its action starts (that far along
    /// already), settles a hand back to rest when its action is cut short or its look changes, starts the punches felt,
    /// and reaches the arms.
    /// </summary>
    internal void Animate(IReadOnlyList<ActionUser> users)
    {
        for (int h = 0; h < hands.Length; h++)
        {
            HeldHand hand = hands[h];
            ActionUser user = users[h];
            if (hand.Tween is { } playing)
            {
                // Only an action's own motion is cut short (a settle and the viewer's playback start no action: 0).
                bool cutShort = playing.Started != 0 && playing.Started == user.Started && user.Current is null;
                if (playing.Look != hand.Look) hand.Cancel();
                else if (cutShort && hand.Running) hand.Begin(held.Motion.Settle(hand.Offset()), TweenClock.World);
            }
            if (viewing is not null) continue;
            // A clip-led main hand's look rides the hand: no tween moves it, and it swaps what it holds at once.
            if (h == MainHand && MainClips() is { } clips && (user.Current is not { } current || ArmsLead.Plays(clips, current)))
            {
                hand.Cancel();
                (hand.Look, hand.Swap, hand.StartedSeen) = (hand.Wanted, Swap.None, user.Started);
                continue;
            }
            if (user.Started == hand.StartedSeen || user.Current is not { } action)
            {
                hand.StartedSeen = user.Started;
                if (!hand.Running || hand.Action is null) Swaps(hand, h == OffHand);
                continue;
            }
            hand.StartedSeen = user.Started;
            // An action begun mid-swap shows the weapon it is made with at once.
            (hand.Look, hand.Swap) = (hand.Wanted, Swap.None);
            HeldLookDefinition look = held.Look(hand.Look)!;
            bool offHand = h == OffHand;
            TweenSegment[] timeline = held.Motion.Timeline(held.Motion.Motions[look.Motion], action, pose => Drawn(look, pose, offHand),
                user.Scale, from: hand.Offset());
            hand.Begin(timeline, TweenClock.World, user.Elapsed, action, user.Started, user.Scale);
        }
        Punch();
        Reach();
    }

    /// <summary>
    /// Advances the weapons' sway by an update's host seconds over the rider's movement (feet, horizontal speed, ground,
    /// walking and sprinting speeds), turned down under each hand's own action; the viewer shows its look without sway.
    /// </summary>
    internal void Present(double hostSeconds, Vector3 feet, float speed, bool grounded, (float Walk, float Sprint) speeds)
    {
        sway.Advance(hostSeconds, feet, speed, grounded, speeds.Walk, speeds.Sprint,
            hands[MainHand].Action is not null && hands[MainHand].Running, hands[OffHand].Action is not null && hands[OffHand].Running);
        for (int h = 0; h < hands.Length; h++)
            hands[h].Sway = sway.Pose(hands[h].Look == "" ? null : held.Motion.Motions[held.Look(hands[h].Look)!.Motion].Loops, h == OffHand);
    }

    // A hand swapping what it holds: puts the look it shows away (its motion's unequip, in real time), shows the one it
    // holds hidden for an update, then draws it (its equip) from where the draw starts. A motion without a transition
    // swaps at once; a change of mind while drawing puts the new look away again.
    private void Swaps(HeldHand hand, bool offHand)
    {
        HeldMotion motion = held.Motion.Motions[held.Look(hand.Look)!.Motion];
        HeldLookDefinition look = held.Look(hand.Look)!;
        switch (hand.Swap)
        {
            case Swap.None when hand.Look != hand.Wanted:
                if (motion.Unequip is { } away)
                {
                    hand.Begin(HeldTimelines.Transition(motion, away, false, pose => Drawn(look, pose, offHand), hand.Offset(), PutAwayHold),
                        TweenClock.Realtime);
                    hand.Swap = Swap.PuttingAway;
                }
                else (hand.Look, hand.Swap) = (hand.Wanted, Swap.DrawNext);
                break;
            case Swap.PuttingAway when (hand.Elapsed ?? double.MaxValue) >= (motion.Unequip?.Seconds ?? 0):
                (hand.Look, hand.Swap) = (hand.Wanted, Swap.DrawNext);
                break;
            case Swap.DrawNext:
                if (motion.Equip is { } draw)
                {
                    hand.Begin(HeldTimelines.Transition(motion, draw, true, pose => Drawn(look, pose, offHand)), TweenClock.Realtime);
                    hand.Swap = Swap.Drawing;
                }
                else hand.Swap = Swap.None;
                break;
            case Swap.Drawing when hand.Look != hand.Wanted || !hand.Running:
                hand.Swap = Swap.None;
                break;
        }
    }

    /// <summary>Reaches the arms for both hands' grips where their looks are drawn now, each hand closed as its motion says.</summary>
    internal void Reach()
    {
        arms.Published();
        arms.Drawn.Reach(Aim(MainHand), Aim(OffHand));
        if (held.Look(hands[MainHand].Look) is { } shown)
            grips.Publish(shown.Landmarks, shown.Grips.Main, hands[MainHand].Object, Fit(shown), arms.Drawn, twoHanded, held.Arms.Landmarks);
        if (held.Look(hands[MainHand].Look) is { } look) PublishHaft(look);
    }

    // Every main digit joint against the look's main grip point, its offset along the look's axes (riders.dev.hands.haft).
    private string haftKey = "";
    internal LandmarkReadResult Haft => engine.Graphics.ReadLandmarks("held.haft");
    private void PublishHaft(HeldLookDefinition shown)
    {
        Transform fit = Fit(shown);
        string key = string.Join("|", shown.Id, hands[MainHand].Object, arms.Drawn.Object, fit.Translation, fit.Rotation, fit.Scale, shown.Grips.Main);
        if (key == haftKey) return;
        haftKey = key;
        Quaternion unturn = Quaternion.Inverse(fit.Rotation);
        Vector3 point = Vector3.Transform(shown.Grips.Main - fit.Translation, unturn) / fit.Scale;
        Landmark at = Landmark.At(OverlayPlace.Model(hands[MainHand].Object), point, default, unturn);
        HandRig main = arms.Drawn.Hand(false);
        List<LandmarkPair> pairs = [];
        foreach (Digit digit in new[] { Digit.Thumb, Digit.Index, Digit.Middle, Digit.Ring, Digit.Pinky })
            for (int j = 0; j < 3; j++) pairs.Add(LandmarkPair.Distance($"{digit}{j + 1}", main.Joint(digit, j), at, 1));
        engine.Graphics.PublishOverlaySet(new("held.haft", Array.Empty<OverlayItem>(), [.. pairs]));
    }

    /// <summary>
    /// The developer grip check (<see cref="GripLandmarks"/>): the main hand's look and its landmark pairs as the Engine
    /// last measured them on the drawn hands.
    /// </summary>
    internal (string Look, LandmarkReadResult Measured) Grips => (hands[MainHand].Look, grips.Read());

    /// <summary>
    /// A hand feels its action land: a punch layered over its motion. Started once its motion for this update has
    /// started (an action can start and land in one update, and the motion's start would end an earlier punch).
    /// </summary>
    internal void Feel(int hand, FeedbackKind kind) => felt.Add((hand, kind));

    /// <summary>Ends every motion (a level or the kit changes).</summary>
    internal void Reset()
    {
        sway.Moved();
        foreach (HeldHand hand in hands)
        {
            hand.Cancel();
            (hand.Look, hand.Swap) = (hand.Wanted, Swap.None);
        }
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
        foreach (HeldHand each in hands)
        {
            each.Cancel();
            // The viewer shows its look at once; off, each hand shows what it holds again.
            if (look is null) (each.Look, each.Swap) = (each.Wanted, Swap.None);
        }
        viewing = look is null ? null : (hand, look, null);
        viewingKey = null;
    }

    /// <summary>Developer motion viewer: holds the shown look at one of an action's keys, as the key is now; -1 holds rest.</summary>
    internal void HoldKey(ActionDefinition action, int key)
    {
        if (viewing is not { } view) return;
        hands[view.Hand].Cancel();
        viewing = view with { Pose = null };
        viewingKey = key < 0 ? null : (action, key);
    }

    /// <summary>Developer motion viewer: holds the shown look in a pose exactly (no tween), or at rest for null.</summary>
    internal void Hold(HeldPose? pose)
    {
        if (viewing is not { } view) return;
        hands[view.Hand].Cancel();
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
        TweenHandle handle = hands[view.Hand].Begin(timeline, TweenClock.Realtime, action: action, speed: speed);
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
        if (hand == MainHand && ridden is { } hold) return Held(look, hold);
        (Vector3 at, Quaternion turn) = Drawn(look, Placed(hand), hand == OffHand);
        (Vector3 move, Quaternion offset) = hands[hand].Offset();
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
            HandAim aim = Aim(hand);
            targets.Add(new(id, OverlayPlace.Camera, aim.Grip) { Rotation = aim.Look, Radius = GripFrame, Framing = "side" });
        }
        return targets;
    }

    public void Dispose()
    {
        arms.Dispose();
        art.Dispose();
        foreach (Appearance box in boxes) box.Dispose();
    }

    // A hand's grip where its look is drawn now (camera space) and the look's turn there; or another point of the look
    // (look space), such as its off grip.
    private (Vector3 Grip, Quaternion Look) Grip(int hand, Vector3? point = null)
    {
        HeldLookDefinition look = held.Look(hands[hand].Look)!;
        Transform fit = Fit(look);
        (Vector3 at, Quaternion turn) = Now(hand);
        // The drawn model is the look turned by its fit, so the look's own turn is the drawn turn less the fit's.
        Quaternion lookTurn = Quaternion.Normalize(turn * Quaternion.Inverse(fit.Rotation));
        return (at + Vector3.Transform((point ?? look.Grips.Main) - fit.Translation, lookTurn), lookTurn);
    }

    // How closed a hand is now (-1 open, 0 its grip, 1 squeezed), how far its motion leans the arms and where its
    // elbow bends toward (camera space, mirrored for the off hand): the viewer's held key, or the playing action's keys
    // where the Engine reports its tween has got to. The off hand on a look held in both hands reads the main hand's keys
    // for the off hand, as they are.
    private HandKeying Keyed(int index)
    {
        Vector3 Side(Vector3 point) => index == OffHand ? HeldFrames.Mirror(point) : point;
        Vector3 pole = Side(held.Arms.Pole);
        if (index == OffHand && twoHanded)
            return Keyed(MainHand, k => new(k.OffGrip, Vector3.Zero, k.OffElbow ?? pole), pole);
        return Keyed(index, k => new(k.Grip, k.Shoulder is { } lean ? Side(lean) : Vector3.Zero, k.Elbow is { } elbow ? Side(elbow) : pole), pole);
    }

    // A hand's keys read by read, at the viewer's held key or where its playing action has got to; at rest, idle.
    private HandKeying Keyed(int index, Func<MotionKey, HandKeying> read, Vector3 pole)
    {
        HeldHand hand = hands[index];
        HandKeying idle = new(0, Vector3.Zero, pole);
        if (viewing is { } view && view.Hand == index && viewingKey is { } key)
            return Keys(view.Look, key.Action) is { } keys && key.Key < keys.Length ? read(keys[key.Key]) : idle;
        if (hand.ActionTime() is not { } time || hand.Action is not { } action || Keys(hand.Look, action) is not { } track) return idle;
        return HeldTimelines.Keyed(engine, track, HeldMotionCatalog.Timing(action), time, read, idle, held.Motion.SettleEase);
    }

    // What a hand reaches for now: its look's grip and turn, the look's own hold, and its motion's closure and elbow. The
    // off hand on a look held in both hands reaches the main look's off grip, held as the look says (not mirrored).
    private HandAim Aim(int index)
    {
        (float closure, _, Vector3 elbow) = Keyed(index);
        if (index == OffHand && twoHanded)
        {
            HeldLookDefinition main = held.Look(hands[MainHand].Look)!;
            (Vector3 offGrip, Quaternion mainTurn) = Grip(MainHand, main.Grips.Off!);
            return new(offGrip, mainTurn, main.Grips.OffHand, closure, elbow, HoldMirrored: false, main.Shape?.Off);
        }
        (Vector3 grip, Quaternion look) = Grip(index);
        HeldLookDefinition own = held.Look(hands[index].Look)!;
        return new(grip, look, own.Grips.Hand, closure, elbow, Shape: own.Shape?.Main, Solved: index == MainHand || !twoHanded ? own.Solved.GetValueOrDefault(Rig) : null);
    }

    private MotionKey[]? Keys(string look, ActionDefinition action) => held.Motion.Motions[held.Look(look)!.Motion].Track(action.Id);

    // Where a hand's look is published: at rest, swayed (the viewer's look does not sway).
    private HeldPose Placed(int hand) => viewing is { } view && view.Hand == hand ? held.Motion.Motions[held.Look(hands[hand].Look)!.Motion].Poses[HeldMotion.Rest]
        : hands[hand].Sway;

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

    // Where a look's drawn model is when its main grip is held at a point and turn (camera space), as a hand holds it.
    private (Vector3 At, Quaternion Turn) Held(HeldLookDefinition look, (Vector3 Grip, Quaternion Look) hold)
    {
        Transform fit = Fit(look);
        return (hold.Grip - Vector3.Transform(look.Grips.Main - fit.Translation, hold.Look), Quaternion.Normalize(hold.Look * fit.Rotation));
    }

    // Starts the punches felt since the last update over each hand's motion as now drawn.
    private void Punch()
    {
        foreach (var (hand, kind) in felt)
            hands[hand].Layers.Start(hands[hand].Object, held.Feedback.Punch(kind), Now(hand).Turn, hand == OffHand,
                viewing is null ? TweenClock.World : TweenClock.Realtime);
        felt.Clear();
    }

    // How a look is drawn inside look space: its converted model centred on its bounds, scaled so its longest side is
    // the authored length and turned by the authored rotation (read each time, so tuning them shows at once), or its box.
    private Transform Fit(HeldLookDefinition look)
    {
        if (looks[look.Id].Mesh is not { } mesh || look.Model is not { } model)
            return new Transform(Vector3.Zero, Quaternion.Identity, look.Box.Size);
        Vector3 extent = mesh.BoundsMax - mesh.BoundsMin, centre = (mesh.BoundsMax + mesh.BoundsMin) / 2;
        float scale = model.Length / MathF.Max(1e-4f, MathF.Max(extent.X, MathF.Max(extent.Y, extent.Z)));
        Quaternion rotation = HeldFrames.Turn(model.Rotation);
        return new Transform(Vector3.Transform(-centre * scale, rotation), rotation, new Vector3(scale));
    }

    private Appearance Box(HeldBox box)
    {
        Appearance made = engine.Graphics.CreatePrimitive(new PrimitiveAppearanceRequest(PrimitiveGeometry.Cube, false, Authored.Color(box.Color)));
        boxes.Add(made);
        return made;
    }
}
