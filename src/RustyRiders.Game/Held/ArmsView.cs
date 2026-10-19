using System.Numerics;
using System.Text.Json.Serialization;
using Rusty.Engine;
using RustyRiders.Game.Content;

namespace RustyRiders.Game.Held;

/// <summary>
/// The rider's arms in first person: one rigged model on the viewmodel layer with its right shoulder at
/// <see cref="ArmsDefinition.Shoulder"/>, whose hands reach their weapons' grips by the Engine's two-bone IK each update,
/// over the clip that closes the hands, each hand opening or squeezing from it as its weapon's motion keys say. Only
/// the weapons move by authored motion; the arms follow, elbows bending toward their poles. The left arm holds its
/// weapon as the right holds its own, mirrored. Adapted from rusty-hotel's <c>ArmsView</c> (docs/reuse.md).
/// </summary>
internal sealed class ArmsView : IDisposable
{
    private readonly IEngineContext engine;
    private readonly ArmsDefinition arms;
    private readonly RenderResource model;
    private readonly Appearance appearance;
    private readonly ulong objectId;
    private readonly (uint Upper, uint Lower, uint Hand) main, off;
    // Each hand's finger joints (a row per finger, base to tip) and thumb joints, for closing it on what it holds.
    private readonly (uint[][] Fingers, uint[] Thumb) mainDigits, offDigits;
    private AnimationInstance? instance;

    /// <summary>
    /// How far each wrist was drawn from its target, as the Engine's solve reports it (metres): more than a centimetre
    /// or so is a grip out of the arm's reach.
    /// </summary>
    internal (float Main, float Off) Shortfall { get; private set; }

    internal ArmsView(IEngineContext engine, ArmsDefinition arms, ulong objectId)
    {
        this.engine = engine; this.arms = arms; this.objectId = objectId;
        using ContentReference content = engine.Content.OpenReference(new(arms.Model));
        model = engine.Animation.OpenAnimatedMeshFromContent(new(content));
        try
        {
            ReadOnlySpan<AnimationJointInfo> joints = engine.Animation.ReadJoints(model).Span;
            string[] clips = [.. engine.Animation.ReadClips(model).ToArray().Select(c => c.Id)];
            Authored.Require(clips.Contains(arms.Clip), ArmsDefinition.Path, "clip", $"the arms model '{arms.Model}' has no clip '{arms.Clip}'. Its clips: {string.Join(", ", clips)}.");
            main = Chain(joints, arms.Main, "main");
            off = Chain(joints, arms.Off, "off");
            mainDigits = Digits(joints, arms.Main, "main");
            offDigits = Digits(joints, arms.Off, "off");
            appearance = engine.Animation.CreateAnimatedMeshAppearance(new(model));
        }
        catch { model.Dispose(); throw; }
    }

    /// <summary>The arms' fact for the snapshot: at the shoulder in camera space.</summary>
    internal AppearanceFact Fact(bool shown) =>
        new(objectId, false, 0, new(Authored.Vector(arms.Shoulder), Quaternion.Identity, Vector3.One), appearance, shown, RenderLayer.Viewmodel);

    /// <summary>
    /// Once the arms are in the published snapshot, reaches each hand for its weapon's grip (camera space) with the hand
    /// turned with the weapon's look, and closes each hand as given (-1 open, 0 the grip clip, 1 squeezed). The off
    /// hand's grip and turn are already mirrored; its hold is the main hand's, mirrored.
    /// </summary>
    internal void Reach((Vector3 Grip, Quaternion Look, float Closure) mainHand, (Vector3 Grip, Quaternion Look, float Closure) offHand)
    {
        if (instance is null)
        {
            instance = engine.Animation.CreateInstance(new(appearance, objectId));
            // The hands hold by the grip clip; each closes or opens from it by joint overrides, and IK places the arms.
            engine.Animation.SetPlayback(new(instance, AnimationPlaybackKind.Play, arms.Clip, AnimationLoopMode.Repeat, 1, 1, true, 0, true, 0));
        }
        Vector3 shoulder = Authored.Vector(arms.Shoulder);
        (Vector3 mainWrist, Quaternion mainTurn) = Wrist(mainHand.Grip, mainHand.Look, false);
        (Vector3 offWrist, Quaternion offTurn) = Wrist(offHand.Grip, offHand.Look, true);
        List<JointOverride> overrides = [JointOverride.Orient(main.Hand, PoseSpace.Model, mainTurn), JointOverride.Orient(off.Hand, PoseSpace.Model, offTurn)];
        overrides.AddRange(Close(mainDigits, mainHand.Closure, false).Concat(Close(offDigits, offHand.Closure, true))
            .Select(t => JointOverride.AddLocalRotation(t.Joint, t.Turn)));
        if (engine.Animation.ReadJointPose(instance) is { Reported: true } drawn && drawn.IkChains.Length == 2)
            Shortfall = (drawn.IkChains.Span[0].Shortfall, drawn.IkChains.Span[1].Shortfall);
        engine.Animation.SetPose(new(instance,
            new TwoBoneIk[]
            {
                new(main.Upper, main.Lower, main.Hand, PoseSpace.Model, mainWrist - shoulder, Authored.Vector(arms.Pole) - shoulder, 1),
                new(off.Upper, off.Lower, off.Hand, PoseSpace.Model, offWrist - shoulder, Frames.Mirror(Authored.Vector(arms.Pole)) - shoulder, 1),
            },
            overrides.ToArray(), true));
    }

    /// <summary>
    /// Where a hand's wrist goes to hold a grip (camera space), and how the hand turns: with the look, by the arms' hold,
    /// with the palm, not the wrist, on the grip. The left hand's joint frames mirror the right's, so its hold is the
    /// right's mirrored.
    /// </summary>
    internal (Vector3 Wrist, Quaternion Turn) Wrist(Vector3 grip, Quaternion look, bool offHand)
    {
        Quaternion hold = Frames.Turn(arms.Hand.Rotation);
        Vector3 palm = Authored.Vector(arms.Hand.Palm);
        if (offHand) (hold, palm) = (Frames.Mirror(hold), Frames.Mirror(palm));
        Quaternion turn = Quaternion.Normalize(look * hold);
        return (grip - Vector3.Transform(palm, turn), turn);
    }

    public void Dispose()
    {
        instance?.Dispose();
        appearance.Dispose();
        model.Dispose();
    }

    private (uint, uint, uint) Chain(ReadOnlySpan<AnimationJointInfo> joints, ArmChain chain, string field) =>
        (Joint(joints, chain.Upper, $"{field}.upper"), Joint(joints, chain.Lower, $"{field}.lower"), Joint(joints, chain.Hand, $"{field}.hand"));

    // A hand's digit joints: the chain's digit prefix, then each finger's name (or the thumb's) and 1 to 3.
    private (uint[][], uint[]) Digits(ReadOnlySpan<AnimationJointInfo> joints, ArmChain chain, string field)
    {
        AnimationJointInfo[] all = joints.ToArray();
        uint[] Joints(string digit) => [.. Enumerable.Range(1, 3).Select(i => Joint(all, $"{chain.Digits}{digit}{i}", $"{field}.digits"))];
        return ([.. arms.Hands.Fingers.Select(Joints)], Joints(arms.Hands.Thumb));
    }

    private uint Joint(ReadOnlySpan<AnimationJointInfo> joints, string name, string field)
    {
        for (int i = 0; i < joints.Length; i++) if (joints[i].Id == name) return (uint)i;
        Authored.Require(false, ArmsDefinition.Path, field, $"the arms model '{arms.Model}' has no joint '{name}'.");
        return 0;
    }

    // A hand's digits as rotations added to the grip clip's finger and thumb joints: toward Close at 1 and toward Open at
    // -1. Fingers curl about their joints' X (negative toward the palm); a thumb also swings about Z, mirrored on the left.
    private IEnumerable<(uint Joint, Quaternion Turn)> Close((uint[][] Fingers, uint[] Thumb) digits, float closure, bool mirrored)
    {
        if (closure == 0) yield break;
        DigitBend bend = closure > 0 ? arms.Hands.Close : arms.Hands.Open;
        float weight = MathF.Abs(closure), side = mirrored ? -1 : 1;
        const float Radians = MathF.PI / 180;
        foreach (uint[] finger in digits.Fingers)
            for (int i = 0; i < 3; i++)
                yield return (finger[i], Quaternion.CreateFromAxisAngle(Vector3.UnitX, -bend.Fingers[i] * weight * Radians));
        for (int i = 0; i < 3; i++)
            yield return (digits.Thumb[i], Quaternion.Normalize(
                Quaternion.CreateFromAxisAngle(Vector3.UnitZ, bend.Thumb[i][1] * weight * side * Radians)
                * Quaternion.CreateFromAxisAngle(Vector3.UnitX, -bend.Thumb[i][0] * weight * Radians)));
    }
}

/// <summary>
/// The first-person arms (content/held/arms.json): their model (facing -Z, the right shoulder at its origin and the left
/// beside it), where the right shoulder sits in camera space (at half the model's shoulder width right of centre, the
/// left sits mirrored), the clip that poses the hands as they hold, how a hand closes or opens from it, each arm's joint
/// chain, the right elbow's pole (camera space; the left's is mirrored), and how a hand holds a look (<see cref="Hand"/>).
/// </summary>
internal sealed record ArmsDefinition(string Model, string Clip, ArmDigits Hands, float[] Shoulder, float[] Pole, ArmChain Main, ArmChain Off, ArmHand Hand)
{
    internal const string Path = "held/arms.json";

    internal static ArmsDefinition Load(IEngineContext engine)
    {
        ArmsDefinition arms = Authored.Read(engine, Path, HeldJson.Default.ArmsDefinition);
        Authored.Require(arms.Model.EndsWith(".glb", StringComparison.Ordinal), Path, "model", "must be a GLB content path.");
        Authored.Require(arms.Hands.Fingers.Length > 0, Path, "hands.fingers", "names the fingers that close.");
        foreach (var (field, bend) in new[] { ("close", arms.Hands.Close), ("open", arms.Hands.Open) })
        {
            Authored.Require(bend.Fingers.Length == 3, Path, $"hands.{field}.fingers", "gives three finger joints' curl, in degrees.");
            Authored.Require(bend.Thumb.Length == 3 && bend.Thumb.All(j => j.Length == 2), Path, $"hands.{field}.thumb",
                "gives three thumb joints' [curl, swing], in degrees.");
        }
        Authored.Point(Path, "shoulder", arms.Shoulder);
        Authored.Point(Path, "pole", arms.Pole);
        Authored.Point(Path, "hand.rotation", arms.Hand.Rotation);
        Authored.Point(Path, "hand.palm", arms.Hand.Palm);
        return arms;
    }

    /// <summary>Takes a reloaded file's placement values in place (live controls hold these arrays).</summary>
    internal void Take(ArmsDefinition fresh)
    {
        fresh.Shoulder.CopyTo(Shoulder, 0);
        fresh.Pole.CopyTo(Pole, 0);
        fresh.Hand.Rotation.CopyTo(Hand.Rotation, 0);
        fresh.Hand.Palm.CopyTo(Hand.Palm, 0);
        fresh.Hands.Close.Fingers.CopyTo(Hands.Close.Fingers, 0);
        fresh.Hands.Open.Fingers.CopyTo(Hands.Open.Fingers, 0);
        for (int i = 0; i < 3; i++)
        {
            fresh.Hands.Close.Thumb[i].CopyTo(Hands.Close.Thumb[i], 0);
            fresh.Hands.Open.Thumb[i].CopyTo(Hands.Open.Thumb[i], 0);
        }
    }

    /// <summary>What a reload cannot take in place: the model, its clip or its joints.</summary>
    internal bool SameRig(ArmsDefinition fresh) => Model == fresh.Model && Clip == fresh.Clip && Main == fresh.Main && Off == fresh.Off
        && Hands.Fingers.SequenceEqual(fresh.Hands.Fingers) && Hands.Thumb == fresh.Hands.Thumb;
}

/// <summary>
/// How a hand holds a look: turned by <see cref="Rotation"/> (degrees about X, Y, Z) from the look's own turn, with its
/// palm <see cref="Palm"/> from the wrist (in that turned frame, metres), so the palm, not the wrist, is on the grip.
/// </summary>
internal sealed record ArmHand(float[] Rotation, float[] Palm);

/// <summary>An arm's joints from shoulder to hand and the prefix of its digit joints (<c>R_</c> names <c>R_Index1</c>).</summary>
internal sealed record ArmChain(string Upper, string Lower, string Hand, string Digits);

/// <summary>
/// The hands' digits (finger names and the thumb's, each with joints 1 to 3) and how far they bend from the grip clip
/// fully squeezed (<see cref="Close"/>, closure 1) and fully open (<see cref="Open"/>, closure -1); closure in between scales them.
/// </summary>
internal sealed record ArmDigits(string[] Fingers, string Thumb, DigitBend Close, DigitBend Open);

/// <summary>Degrees added to each finger joint's curl (base to tip, the same for every finger) and each thumb joint's [curl, swing].</summary>
internal sealed record DigitBend(float[] Fingers, float[][] Thumb);
