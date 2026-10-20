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
/// started as the action starts and eased back to rest when it is cut short; the arms reaching both hands' grips. Also
/// the developer motion viewer, which shows any look in a hand, holds a key's pose, plays an action at a speed or shows
/// a moment of it. Adapted from rusty-hotel's <c>CombatView</c> held presentation (docs/reuse.md); each hand's tween
/// state is a <see cref="HeldHand"/> and the arms an <see cref="ArmsSlot"/>.
/// </summary>
internal sealed class HeldView : IDisposable
{
    internal const int MainHand = 0, OffHand = 1;
    private const string OldArtRoot = "old-art";
    // How far around a grip its inspection view frames (metres).
    private const float GripFrame = .35f;

    private readonly IEngineContext engine;
    private readonly HeldCatalog held;
    private readonly ConvertedArt art;
    private readonly List<Appearance> boxes = [];
    // Each look's appearance and its opened model (none when it is drawn as its box).
    private readonly Dictionary<string, (Appearance Appearance, ArtMesh? Mesh)> looks = [];
    private readonly HeldHand[] hands;
    private readonly ArmsSlot arms;
    private readonly List<(int Hand, FeedbackKind Kind)> felt = [];
    private (int Hand, string Look, HeldPose? Pose)? viewing;
    private (ActionDefinition Action, int Key)? viewingKey;

    /// <param name="firstObject">The published object id of the main hand's look; the off hand's and the arms' two are the next three.</param>
    internal HeldView(IEngineContext engine, HeldCatalog held, ulong firstObject)
    {
        this.engine = engine;
        this.held = held;
        art = new ConvertedArt(engine, OldArtRoot);
        hands = [new(engine, firstObject), new(engine, firstObject + 1)];
        arms = new ArmsSlot(engine, held, firstObject + 2);
        foreach (HeldLookDefinition look in held.Looks)
            looks[look.Id] = look.Model is { } model && art.Model(model.Glb) is { } mesh ? (mesh.Appearance, mesh) : (Box(look.Box), null);
    }

    internal HeldCatalog Catalog => held;

    /// <summary>Converted models that could not be opened (their looks are drawn as boxes) and arms rigs that did not open.</summary>
    internal IReadOnlyList<string> Problems => [.. art.Problems, .. arms.Problems];

    /// <summary>How many feedback punches each hand has had since its motion last started (ended ones are counted until sampled).</summary>
    internal (int Main, int Off) Felt => (hands[MainHand].Layers.Count, hands[OffHand].Layers.Count);

    /// <summary>The rig profile drawing the arms now.</summary>
    internal string Rig => arms.Drawn.Id;

    /// <summary>The arms as drawn now: wrist shortfall, arm lengths and the measured hand frames.</summary>
    internal ArmsView DrawnArms => arms.Drawn;

    /// <summary>How far each wrist was drawn from its grip (see <see cref="ArmsView.Shortfall"/>).</summary>
    internal (float Main, float Off) ArmsShortfall => arms.Drawn.Shortfall;

    /// <summary>Developer arms comparison: draws the arms by another rig profile (see <see cref="ArmsSlot.Show"/>).</summary>
    internal string? ShowRig(string id) => arms.Show(id);

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
            yield return new AppearanceFact(hands[h].Object, false, 0, new Transform(at, turn, Fit(look).Scale),
                looks[look.Id].Appearance, visible, RenderLayer.Viewmodel);
        }
        // The body leans with a blow: the arms move by both hands' motions together (two blows at once lean as their sum;
        // the arms' own reach then keeps each hand on its grip).
        arms.Drawn.Lean = Keyed(MainHand).Lean + Keyed(OffHand).Lean;
        arms.Drawn.Aim(Aim(MainHand), Aim(OffHand));
        yield return arms.Drawn.Fact(visible);
        if (arms.Retired is { } retired) yield return retired;
    }

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
            if (viewing is not null || user.Started == hand.StartedSeen) continue;
            hand.StartedSeen = user.Started;
            if (user.Current is not { } action) continue;
            HeldLookDefinition look = held.Look(hand.Look)!;
            bool offHand = h == OffHand;
            TweenSegment[] timeline = held.Motion.Timeline(held.Motion.Motions[look.Motion], action, pose => Drawn(look, pose, offHand),
                user.Scale, from: hand.Offset());
            hand.Begin(timeline, TweenClock.World, user.Elapsed, action, user.Started, user.Scale);
        }
        Punch();
        Reach();
    }

    /// <summary>Reaches the arms for both hands' grips where their looks are drawn now, each hand closed as its motion says.</summary>
    internal void Reach()
    {
        arms.Published();
        arms.Drawn.Reach(Aim(MainHand), Aim(OffHand));
    }

    /// <summary>
    /// A hand feels its action land: a punch layered over its motion. Started once its motion for this update has
    /// started (an action can start and land in one update, and the motion's start would end an earlier punch).
    /// </summary>
    internal void Feel(int hand, FeedbackKind kind) => felt.Add((hand, kind));

    /// <summary>Ends every motion (a level or the kit changes).</summary>
    internal void Reset()
    {
        foreach (HeldHand hand in hands) hand.Cancel();
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
        foreach (HeldHand each in hands) each.Cancel();
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
        (Vector3 at, Quaternion turn) = Drawn(look, Rest(look), hand == OffHand);
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
            (Vector3 grip, Quaternion lookTurn) = Grip(hand);
            targets.Add(new(id, OverlayPlace.Camera, grip) { Rotation = lookTurn, Radius = GripFrame, Framing = "side" });
        }
        return targets;
    }

    public void Dispose()
    {
        arms.Dispose();
        art.Dispose();
        foreach (Appearance box in boxes) box.Dispose();
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

    // How closed a hand is now (-1 open, 0 its grip, 1 squeezed), how far its motion leans the arms and where its
    // elbow bends toward (camera space, mirrored for the off hand): the viewer's held key, or the playing action's keys
    // where the Engine reports its tween has got to, eased by each key's curve as the Engine eases them.
    private (float Grip, Vector3 Lean, Vector3 Elbow) Keyed(int index)
    {
        HeldHand hand = hands[index];
        Vector3 Side(Vector3 point) => index == OffHand ? Frames.Mirror(point) : point;
        Vector3 pole = Side(Authored.Vector(held.Arms.Pole));
        Vector3 Lean(MotionKey key) => key.Shoulder is { } lean ? Side(Authored.Vector(lean)) : Vector3.Zero;
        Vector3 Elbow(MotionKey key) => key.Elbow is { } elbow ? Side(Authored.Vector(elbow)) : pole;
        if (viewing is { } view && view.Hand == index && viewingKey is { } key)
            return Keys(view.Look, key.Action) is { } keys && key.Key < keys.Length ? (keys[key.Key].Grip, Lean(keys[key.Key]), Elbow(keys[key.Key])) : (0, Vector3.Zero, pole);
        if (hand.ActionTime() is not { } time || hand.Action is not { } action || Keys(hand.Look, action) is not { } track) return (0, Vector3.Zero, pole);
        (float grip, Vector3 lean, Vector3 elbow, float start) from = (0, Vector3.Zero, pole, 0);
        foreach ((float at, float grip, Vector3 lean, Vector3 elbow, TweenEasingKind curve) in track
            .Select(k => (k.Time(action.Timing), k.Grip, Lean(k), Elbow(k), k.Ease))
            .Append((action.Duration, 0f, Vector3.Zero, pole, held.Motion.SettleEase)))
        {
            if (time <= at)
            {
                float t = at - from.start <= 0 ? 1 : engine.Tween.Evaluate(curve, Math.Clamp((time - from.start) / (at - from.start), 0, 1));
                return (from.grip + (grip - from.grip) * t, Vector3.Lerp(from.lean, lean, t), Vector3.Lerp(from.elbow, elbow, t));
            }
            from = (grip, lean, elbow, at);
        }
        return (0, Vector3.Zero, pole);
    }

    // What a hand reaches for now: its look's grip and turn, the look's own hold, and its motion's closure and elbow.
    private HandAim Aim(int index)
    {
        (Vector3 grip, Quaternion look) = Grip(index);
        (float closure, _, Vector3 elbow) = Keyed(index);
        return new(grip, look, held.Look(hands[index].Look)!.Grips.Hand, closure, elbow);
    }

    private MotionKey[]? Keys(string look, ActionDefinition action) => held.Motion.Motions[held.Look(look)!.Motion].Track(action.Id);

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
}
