using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.FirstPerson;
using RustyRiders.Game.Content;

namespace RustyRiders.Game.Held;

/// <summary>
/// The first-person arms apart from any one arms model (content/held/arms.json): the Engine's <see cref="HeldArms"/>
/// (shoulder, elbow pole, default hold in the hand's canonical frame, the hands' closure and holding pose), which rig
/// profile draws them, and where the developer grip check finds a hand's points and how near a look's landmarks they
/// must come.
/// </summary>
internal sealed record ArmsDefinition(Vector3 Shoulder, Vector3 Pole, Vector3 Hold, ArmDigits Hands, HandLandmarkTuning Landmarks)
    : HeldArms(Shoulder, Pole, Hold, Hands)
{
    internal const string Path = "held/arms.json";

    /// <summary>The rig profile that draws the arms (content/held/rigs/&lt;rig&gt;.json); developers switch it live.</summary>
    public required string Rig { get; set; }

    internal static ArmsDefinition Load(IEngineContext engine)
    {
        ArmsDefinition arms = Authored.Read(engine, Path, HeldJson.Default.ArmsDefinition);
        Authored.Require(Path, "hands", arms.Hands.Problems());
        Authored.Require(Path, "landmarks", arms.Landmarks.Problems());
        return arms;
    }

    /// <summary>Takes a reloaded file's values in place (live controls hold these records).</summary>
    internal void Take(ArmsDefinition fresh)
    {
        (Rig, Shoulder, Pole, Hold) = (fresh.Rig, fresh.Shoulder, fresh.Pole, fresh.Hold);
        Hands.Take(fresh.Hands);
        Landmarks.Take(fresh.Landmarks);
    }
}

/// <summary>Every rig profile in content/held/rigs (Engine <see cref="ArmRig"/>s), by id: those named here, loaded at start.</summary>
internal static class ArmRigs
{
    internal const string Directory = "held/rigs";

    /// <summary>The profiles the product knows; a new arms model is a new file and its id here.</summary>
    internal static readonly string[] Ids = ["generated", "painted", "leather", "shared"];

    internal static string PathOf(string id) => $"{Directory}/{id}.json";

    internal static Dictionary<string, ArmRig> Load(IEngineContext engine, ArmsDefinition arms)
    {
        Dictionary<string, ArmRig> rigs = Ids.ToDictionary(id => id, id =>
        {
            ArmRig rig = Authored.Read(engine, PathOf(id), HeldJson.Default.ArmRig);
            Authored.Require(PathOf(id), "", rig.Problems());
            return rig;
        });
        Authored.Require(rigs.ContainsKey(arms.Rig), ArmsDefinition.Path, "rig", $"unknown rig '{arms.Rig}'; profiles: {string.Join(", ", Ids)}.");
        return rigs;
    }
}
