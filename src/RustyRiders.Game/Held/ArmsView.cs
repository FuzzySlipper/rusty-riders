using System.Numerics;
using Rusty.Engine;
using RustyRiders.Game.Content;

namespace RustyRiders.Game.Held;

/// <summary>
/// The rider's arms in first person, drawn by one rig profile (<see cref="ArmRig"/>): its model on the viewmodel layer,
/// placed so its right upper arm sits at the arms' shoulder, whose hands reach their weapons' grips by the Engine's
/// two-bone IK each update over the rig's clip, each hand turned with its weapon's look by the arms' hold and opening or
/// squeezing as the motion keys say. Only the weapons move by authored motion; the arms follow, elbows bending toward
/// their poles, the left mirroring the right. Nothing here names a rig's joints or frames: each hand's frame and digit
/// axes are measured from the drawn model once, so any rig with two arms and five digits a hand can hold. Adapted from
/// rusty-hotel's <c>ArmsView</c> (docs/reuse.md).
/// </summary>
internal sealed class ArmsView : IDisposable
{
    private readonly IEngineContext engine;
    private readonly ArmsDefinition arms;
    private readonly ArmRig rig;
    private readonly RenderResource model;
    private readonly Appearance appearance;
    private readonly ulong objectId;
    private readonly Side main, off;
    private AnimationInstance? instance;
    // Where the right upper arm was last drawn (model space), which places the model so it sits at the shoulder.
    private Vector3? upperArm;

    internal ArmsView(IEngineContext engine, ArmsDefinition arms, ArmRig rig, string rigId, ulong objectId)
    {
        this.engine = engine; this.arms = arms; this.rig = rig; this.objectId = objectId;
        Id = rigId;
        using ContentReference content = engine.Content.OpenReference(new(rig.Model));
        model = engine.Animation.OpenAnimatedMeshFromContent(new(content));
        try
        {
            string path = ArmRig.PathOf(rigId);
            AnimationJointInfo[] joints = engine.Animation.ReadJoints(model).ToArray();
            string[] clips = [.. engine.Animation.ReadClips(model).ToArray().Select(c => c.Id)];
            Authored.Require(clips.Contains(rig.Clip), path, "clip", $"the arms model '{rig.Model}' has no clip '{rig.Clip}'. Its clips: {string.Join(", ", clips)}.");
            uint Joint(string name, string field)
            {
                int at = Array.FindIndex(joints, j => j.Id == name);
                Authored.Require(at >= 0, path, field, $"the arms model '{rig.Model}' has no joint '{name}'.");
                return (uint)at;
            }
            Side Arm(ArmJoints chain, string field, bool left) => new(left,
                Joint(chain.Upper, $"{field}.upper"), Joint(chain.Lower, $"{field}.lower"), Joint(chain.Hand, $"{field}.hand"),
                [.. chain.Fingers.Select((f, i) => f.Select(n => Joint(n, $"{field}.fingers[{i}]")).ToArray())],
                [.. chain.Thumb.Select(n => Joint(n, $"{field}.thumb"))]);
            main = Arm(rig.Main, "main", false);
            off = Arm(rig.Off, "off", true);
            appearance = engine.Animation.CreateAnimatedMeshAppearance(new(model));
        }
        catch { model.Dispose(); throw; }
    }

    /// <summary>Each hand's measured turn from its canonical frame to its joint's (degrees about X, Y, Z), once measured; null before.</summary>
    internal (Vector3 Main, Vector3 Off)? HandFrames => main.Measured ? (Frames.Degrees(main.Frame), Frames.Degrees(off.Frame)) : null;

    /// <summary>The rig profile drawing these arms.</summary>
    internal string Id { get; }

    /// <summary>The published object these arms are drawn on.</summary>
    internal ulong Object => objectId;

    /// <summary>Whether, once replaced, a snapshot hiding these arms has been published (they are released after it).</summary>
    internal bool Published { get; set; }

    /// <summary>
    /// How far each wrist was drawn from its target, as the Engine's solve reports it (metres): more than a centimetre
    /// or so is a grip out of the arm's reach.
    /// </summary>
    internal (float Main, float Off) Shortfall { get; private set; }

    /// <summary>Each arm's length from the upper arm joint through the elbow to the hand joint as last drawn (metres), once measured.</summary>
    internal (float Main, float Off) ArmLength { get; private set; }

    /// <summary>The arms' fact for the snapshot: the model placed so its right upper arm sits at the shoulder, facing -Z.</summary>
    internal AppearanceFact Fact(bool shown) => new(objectId, false, 0, Placement(), appearance, shown, RenderLayer.Viewmodel);

    /// <summary>
    /// Once the arms are in the published snapshot, reaches each hand for its weapon's grip (camera space) with the hand
    /// turned with the weapon's look, and closes each hand as given (-1 open, 0 its grip, 1 squeezed). The off hand's
    /// grip and turn are already mirrored; its hold is the main hand's, mirrored.
    /// </summary>
    internal void Reach((Vector3 Grip, Quaternion Look, float Closure) mainHand, (Vector3 Grip, Quaternion Look, float Closure) offHand)
    {
        if (instance is null)
        {
            instance = engine.Animation.CreateInstance(new(appearance, objectId));
            engine.Animation.SetPlayback(new(instance, AnimationPlaybackKind.Play, rig.Clip, AnimationLoopMode.Repeat, 1, 1, true, 0, true, 0));
        }
        if (engine.Animation.ReadJointPose(instance) is { Reported: true } drawn)
        {
            JointPose[] joints = drawn.Joints.ToArray();
            upperArm = joints[main.Upper].Model.Translation;
            if (!main.Measured) main.Measure(joints, rig.Digits);
            if (!off.Measured) off.Measure(joints, rig.Digits);
            ArmLength = (main.Length(joints) * rig.Scale, off.Length(joints) * rig.Scale);
            if (drawn.IkChains.Length == 2) Shortfall = (drawn.IkChains.Span[0].Shortfall * rig.Scale, drawn.IkChains.Span[1].Shortfall * rig.Scale);
        }
        Transform placed = Placement();
        Quaternion unturn = Quaternion.Inverse(placed.Rotation);
        // Camera space into the model's own space, where the IK targets and hand turns are given.
        Vector3 Into(Vector3 point) => Vector3.Transform(point - placed.Translation, unturn) / rig.Scale;
        List<TwoBoneIk> chains = [];
        List<JointOverride> overrides = [];
        foreach (var (side, hand, pole) in new[] { (main, mainHand, Authored.Vector(arms.Pole)), (off, offHand, Frames.Mirror(Authored.Vector(arms.Pole))) })
        {
            (Vector3 wrist, Quaternion turn) = Wrist(hand.Grip, hand.Look, side.Left);
            chains.Add(new(side.Upper, side.Lower, side.Hand, PoseSpace.Model, Into(wrist), Into(pole), 1));
            if (!side.Measured) continue;
            overrides.Add(JointOverride.Orient(side.Hand, PoseSpace.Model, Quaternion.Normalize(unturn * turn * side.Frame)));
            overrides.AddRange(side.Close(rig, arms.Hands, hand.Closure).Select(t => JointOverride.AddLocalRotation(t.Joint, t.Turn)));
        }
        engine.Animation.SetPose(new(instance, chains.ToArray(), overrides.ToArray(), true));
    }

    /// <summary>
    /// Where a hand's wrist goes to hold a grip (camera space), and how the hand's canonical frame turns: with the look,
    /// by the arms' hold, with the palm, not the wrist, on the grip. The left hand holds as the right does, mirrored.
    /// </summary>
    internal (Vector3 Wrist, Quaternion Turn) Wrist(Vector3 grip, Quaternion look, bool left)
    {
        Quaternion hold = Frames.Turn(arms.Hold);
        Vector3 palm = Authored.Vector(rig.Palm);
        if (left) (hold, palm) = (Frames.Mirror(hold), Frames.Mirror(palm));
        Quaternion turn = Quaternion.Normalize(look * hold);
        return (grip - Vector3.Transform(palm, turn), turn);
    }

    public void Dispose()
    {
        instance?.Dispose();
        appearance.Dispose();
        model.Dispose();
    }

    // The model turned and scaled into camera space, its right upper arm (as last drawn) at the arms' shoulder.
    private Transform Placement()
    {
        Quaternion turn = Frames.Turn(rig.Turn);
        Vector3 at = Authored.Vector(arms.Shoulder) - Vector3.Transform((upperArm ?? Vector3.Zero) * rig.Scale, turn);
        return new(at, turn, new Vector3(rig.Scale));
    }

    // One arm: its IK chain, its digit joints (index, middle, ring and little fingers, then the thumb), and what is
    // measured from the drawn model: the turn from the hand's canonical frame to its joint's, and each digit joint's
    // curl and swing axes in its own frame.
    private sealed class Side(bool left, uint upper, uint lower, uint hand, uint[][] fingers, uint[] thumb)
    {
        internal bool Left => left;
        internal uint Upper => upper;
        internal uint Lower => lower;
        internal uint Hand => hand;
        internal bool Measured { get; private set; }
        internal Quaternion Frame { get; private set; } = Quaternion.Identity;
        private readonly Dictionary<uint, (Vector3 Curl, Vector3 Swing)> axes = [];

        internal float Length(JointPose[] joints) =>
            Vector3.Distance(joints[upper].Model.Translation, joints[lower].Model.Translation)
            + Vector3.Distance(joints[lower].Model.Translation, joints[hand].Model.Translation);

        // The knuckles are fixed to the hand, so where they sit in its frame gives the canonical frame whatever the
        // hand's turn; each digit's axes are taken in the joint's own frame, so they hold however the digit has turned.
        internal void Measure(JointPose[] joints, DigitFrames frames)
        {
            Transform wrist = joints[hand].Model;
            Quaternion unturn = Quaternion.Inverse(wrist.Rotation);
            Vector3 Local(uint joint) => Vector3.Transform(joints[joint].Model.Translation - wrist.Translation, unturn);
            Frame = HandFrame.Measure(Local(fingers[0][0]), Local(fingers[1][0]), Local(fingers[3][0]), left);
            Quaternion canonical = Quaternion.Normalize(wrist.Rotation * Quaternion.Inverse(Frame));
            Vector3 x = Vector3.Transform(Vector3.UnitX, canonical), z = Vector3.Transform(Vector3.UnitZ, canonical);
            foreach (uint[] finger in fingers)
                foreach (uint joint in finger)
                    axes[joint] = frames == DigitFrames.Native ? (Vector3.UnitX, Vector3.UnitZ)
                        : (Vector3.Transform(x, Quaternion.Inverse(joints[joint].Model.Rotation)), Vector3.Zero);
            for (int i = 0; i < 3; i++)
            {
                if (frames == DigitFrames.Native) { axes[thumb[i]] = (Vector3.UnitX, Vector3.UnitZ); continue; }
                // A thumb curls across the palm: about the axis square to its own length and the palm's normal (-Z),
                // signed so a positive turn of a negative curl angle brings it toward the palm, as a finger's does.
                Quaternion own = joints[thumb[i]].Model.Rotation;
                Vector3 along = Vector3.Normalize(joints[i < 2 ? thumb[i + 1] : thumb[i]].Model.Translation - joints[i < 2 ? thumb[i] : thumb[i - 1]].Model.Translation);
                Vector3 curl = Vector3.Normalize(Vector3.Cross(-z, along));
                axes[thumb[i]] = (Vector3.Transform(curl, Quaternion.Inverse(own)), Vector3.Transform(z, Quaternion.Inverse(own)));
            }
            Measured = true;
        }

        // Each digit joint's turn over the clip: the rig's grip, then the closure toward the arms' close at 1 or open
        // at -1. Curls are negative turns about the curl axis (toward the palm); a thumb swings about its swing axis,
        // mirrored on the left.
        internal IEnumerable<(uint Joint, Quaternion Turn)> Close(ArmRig rig, ArmDigits digits, float closure)
        {
            DigitBend bend = closure > 0 ? digits.Close : digits.Open;
            float weight = MathF.Abs(closure), side = left ? -1 : 1;
            const float Radians = MathF.PI / 180;
            foreach (uint[] finger in fingers)
                for (int i = 0; i < 3; i++)
                {
                    float curl = rig.Grip.Fingers[i] + bend.Fingers[i] * weight;
                    if (curl != 0) yield return (finger[i], Quaternion.CreateFromAxisAngle(axes[finger[i]].Curl, -curl * Radians));
                }
            for (int i = 0; i < 3; i++)
            {
                float curl = rig.Grip.Thumb[i][0] + bend.Thumb[i][0] * weight, swing = rig.Grip.Thumb[i][1] + bend.Thumb[i][1] * weight;
                if (curl == 0 && swing == 0) continue;
                (Vector3 curlAxis, Vector3 swingAxis) = axes[thumb[i]];
                yield return (thumb[i], Quaternion.Normalize(
                    Quaternion.CreateFromAxisAngle(swingAxis, swing * side * Radians) * Quaternion.CreateFromAxisAngle(curlAxis, -curl * Radians)));
            }
        }
    }
}
