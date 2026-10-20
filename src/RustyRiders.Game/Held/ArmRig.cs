using System.Numerics;
using System.Text.Json.Serialization;
using Rusty.Engine;
using RustyRiders.Game.Content;

namespace RustyRiders.Game.Held;

/// <summary>
/// The first-person arms, apart from any one arms model (content/held/arms.json): which rig profile draws them, where
/// the right shoulder sits in camera space and the point its elbow bends toward (the left's are mirrored), how a hand
/// holds a look (<see cref="Hold"/>, a turn in the hand's canonical frame, so it suits every rig), and how far a hand
/// closes or opens from its grip. The hand's canonical frame: +Y along the fingers, +Z out of the back of the hand, and
/// +X toward the little finger on the right hand (toward the thumb on the left). <see cref="Landmarks"/> says where
/// the developer grip check finds a hand's points and how near a look's landmarks they must come.
/// </summary>
internal sealed record ArmsDefinition(float[] Shoulder, float[] Pole, float[] Hold, ArmDigits Hands, HandLandmarks Landmarks)
{
    internal const string Path = "held/arms.json";

    /// <summary>The rig profile that draws the arms (content/held/rigs/&lt;rig&gt;.json); developers switch it live.</summary>
    public required string Rig { get; set; }

    internal static ArmsDefinition Load(IEngineContext engine)
    {
        ArmsDefinition arms = Authored.Read(engine, Path, HeldJson.Default.ArmsDefinition);
        foreach (var (field, bend) in new[] { ("close", arms.Hands.Close), ("open", arms.Hands.Open) }) bend.Validate(Path, $"hands.{field}");
        Authored.Point(Path, "shoulder", arms.Shoulder);
        Authored.Point(Path, "pole", arms.Pole);
        Authored.Point(Path, "hold", arms.Hold);
        arms.Landmarks.Validate(Path, "landmarks");
        return arms;
    }

    /// <summary>Takes a reloaded file's values in place (live controls hold these arrays).</summary>
    internal void Take(ArmsDefinition fresh)
    {
        Rig = fresh.Rig;
        fresh.Shoulder.CopyTo(Shoulder, 0);
        fresh.Pole.CopyTo(Pole, 0);
        fresh.Hold.CopyTo(Hold, 0);
        Hands.Close.Take(fresh.Hands.Close);
        Hands.Open.Take(fresh.Hands.Open);
        Landmarks.Take(fresh.Landmarks);
    }
}

/// <summary>How far each finger joint (base to tip, alike for every finger) and each thumb joint ([curl, swing]) bend, in degrees, at full closure (<see cref="Close"/>) and fully open (<see cref="Open"/>).</summary>
internal sealed record ArmDigits(DigitBend Close, DigitBend Open);

/// <summary>Degrees added to each finger joint's curl (base to tip, the same for every finger; positive toward the palm) and each thumb joint's [curl, swing].</summary>
internal sealed record DigitBend(float[] Fingers, float[][] Thumb)
{
    internal void Validate(string path, string field)
    {
        Authored.Require(Fingers.Length == 3, path, $"{field}.fingers", "gives three finger joints' curl, in degrees.");
        Authored.Require(Thumb.Length == 3 && Thumb.All(j => j.Length == 2), path, $"{field}.thumb", "gives three thumb joints' [curl, swing], in degrees.");
    }

    internal void Take(DigitBend fresh)
    {
        fresh.Fingers.CopyTo(Fingers, 0);
        for (int i = 0; i < 3; i++) fresh.Thumb[i].CopyTo(Thumb[i], 0);
    }
}

/// <summary>
/// One arms model and how to drive it (content/held/rigs/&lt;id&gt;.json): the GLB and the clip played under the IK; how
/// the model is turned and scaled so it faces -Z in metres (it is placed so its right upper arm joint sits at the arms'
/// shoulder); each arm's joints; how the digits turn (<see cref="Digits"/>); the palm's offset from the hand joint in
/// the hand's canonical frame (right hand; the left mirrors it); and the curl added to the clip's hands so they hold
/// (<see cref="Grip"/>). A rig's hand frames and digit axes are measured from the drawn model, so the arms' hold and
/// the motion keys' closure suit every rig.
/// </summary>
internal sealed record ArmRig(string Model, string Clip, float[] Turn, ArmJoints Main, ArmJoints Off, DigitFrames Digits, float[] Palm, DigitBend Grip)
{
    /// <summary>The model's uniform scale into metres (1 for a model already in metres); tuned live.</summary>
    public float Scale { get; set; } = 1;

    internal const string Directory = "held/rigs";

    internal static string PathOf(string id) => $"{Directory}/{id}.json";

    internal static ArmRig Load(IEngineContext engine, string id)
    {
        string path = PathOf(id);
        ArmRig rig = Authored.Read(engine, path, HeldJson.Default.ArmRig);
        Authored.Require(rig.Model.EndsWith(".glb", StringComparison.Ordinal), path, "model", "must be a GLB content path.");
        Authored.Point(path, "turn", rig.Turn);
        Authored.Positive(path, "scale", rig.Scale);
        Authored.Point(path, "palm", rig.Palm);
        rig.Grip.Validate(path, "grip");
        foreach (var (field, joints) in new[] { ("main", rig.Main), ("off", rig.Off) })
        {
            Authored.Require(joints.Fingers.Length == 4 && joints.Fingers.All(f => f.Length == 3), path, $"{field}.fingers",
                "names the index, middle, ring and little fingers' joints, three each from the knuckle out.");
            Authored.Require(joints.Thumb.Length == 3, path, $"{field}.thumb", "names the thumb's three joints from its base out.");
        }
        return rig;
    }

    /// <summary>Takes a reloaded profile's tuning in place: its turn, scale, palm and grip.</summary>
    internal void Take(ArmRig fresh)
    {
        fresh.Turn.CopyTo(Turn, 0);
        Scale = fresh.Scale;
        fresh.Palm.CopyTo(Palm, 0);
        Grip.Take(fresh.Grip);
    }

    /// <summary>What a reload cannot take in place: the model, its clip, its joints or how its digits turn.</summary>
    internal bool SameDrawing(ArmRig fresh) => Model == fresh.Model && Clip == fresh.Clip && Digits == fresh.Digits
        && Main.Same(fresh.Main) && Off.Same(fresh.Off);
}

/// <summary>
/// One arm's joints: the IK chain from the upper arm to the hand (<see cref="Lower"/> descends from <see cref="Upper"/>
/// and the hand from it, with any twist joints between), and each digit's three joints from its base out: the index,
/// middle, ring and little fingers, then the thumb.
/// </summary>
internal sealed record ArmJoints(string Upper, string Lower, string Hand, string[][] Fingers, string[] Thumb)
{
    internal bool Same(ArmJoints other) => Upper == other.Upper && Lower == other.Lower && Hand == other.Hand
        && Fingers.Zip(other.Fingers).All(p => p.First.SequenceEqual(p.Second)) && Thumb.SequenceEqual(other.Thumb);
}

/// <summary>
/// How a rig's digits turn. <c>Native</c>: each digit joint's own frame is the hand's canonical frame (a finger curls
/// about its X, a thumb also swings about its Z), as the generated arms are rigged. <c>Measured</c>: curl and swing
/// axes are measured from the drawn hand (a finger curls about the knuckle line, a thumb across the palm and swings
/// about the palm's normal), for rigs authored any other way.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<DigitFrames>))]
internal enum DigitFrames { Native, Measured }

/// <summary>Every rig profile in content/held/rigs, by id: those named here, loaded at start.</summary>
internal static class ArmRigs
{
    /// <summary>The profiles the product knows; a new arms model is a new file and its id here.</summary>
    internal static readonly string[] Ids = ["generated", "painted", "leather"];

    internal static Dictionary<string, ArmRig> Load(IEngineContext engine, ArmsDefinition arms)
    {
        Dictionary<string, ArmRig> rigs = Ids.ToDictionary(id => id, id => ArmRig.Load(engine, id));
        Authored.Require(rigs.ContainsKey(arms.Rig), ArmsDefinition.Path, "rig", $"unknown rig '{arms.Rig}'; profiles: {string.Join(", ", Ids)}.");
        return rigs;
    }
}

/// <summary>Turns in the hand's canonical frame.</summary>
internal static class HandFrame
{
    /// <summary>
    /// The turn from a hand joint's own frame to the hand's canonical frame, from where the knuckles sit in the hand
    /// joint's frame: +Y toward the middle knuckle, +X from the index knuckle toward the little one on the right hand
    /// (from the little toward the index on the left), +Z out of the back of the hand.
    /// </summary>
    internal static Quaternion Measure(Vector3 index, Vector3 middle, Vector3 little, bool left)
    {
        Vector3 y = Vector3.Normalize(middle);
        Vector3 across = left ? index - little : little - index;
        Vector3 x = Vector3.Normalize(across - y * Vector3.Dot(across, y));
        Vector3 z = Vector3.Cross(x, y);
        // The canonical axes as columns in the joint's frame; its inverse takes the canonical frame to the joint's.
        Quaternion canonical = Quaternion.CreateFromRotationMatrix(new(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1));
        return Quaternion.Normalize(Quaternion.Inverse(canonical));
    }
}
