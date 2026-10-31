using System.Numerics;
using System.Text.Json.Serialization;
using Rusty.Engine;
using Rusty.Engine.FirstPerson;
using RustyRiders.Game.Actions;
using RustyRiders.Game.Content;

namespace RustyRiders.Game.Held;

/// <summary>
/// Which looks the arms play clips for (content/held/clips.json): per arm rig (a clip set is on one skeleton), per motion
/// family, the idle loop the arms play at rest and the Engine <see cref="ArmClip"/> each action plays, its phases marked
/// in the clip. A look whose motion has clips on the drawing rig is clip-led there (the look rides the main hand); an
/// action without a clip, and every other rig, stays weapon-led.
/// </summary>
internal sealed record ArmClipCatalog(Dictionary<string, RigClips> Rigs)
{
    internal const string Path = "held/clips.json";

    internal static ArmClipCatalog Load(IEngineContext engine, IReadOnlyDictionary<string, ArmRig> rigs, HeldMotionCatalog motion)
    {
        ArmClipCatalog catalog = Authored.Read(engine, Path, ArmClipJson.Default.ArmClipCatalog);
        foreach (var (rig, set) in catalog.Rigs)
        {
            Authored.Require(rigs.ContainsKey(rig), Path, $"rigs.{rig}", $"unknown rig; profiles: {string.Join(", ", rigs.Keys)}.");
            foreach (var (id, clips) in set.Motions)
            {
                string at = $"rigs.{rig}.motions.{id}";
                Authored.Require(motion.Motions.ContainsKey(id), Path, at, $"unknown motion; see content/{HeldMotionCatalog.Path}.");
                Authored.Positive(Path, $"{at}.idle.seconds", clips.Idle.Seconds);
                foreach (var (action, clip) in clips.Actions) Authored.Require(Path, $"{at}.actions.{action}", clip.Problems());
            }
        }
        return catalog;
    }

    /// <summary>The clips a motion plays on a rig, or null where it is weapon-led.</summary>
    internal MotionClips? For(string rig, string motion) => Rigs.GetValueOrDefault(rig)?.Motions.GetValueOrDefault(motion);

    /// <summary>How far a rig's arms are carried to frame its clips (zero for a rig without clips).</summary>
    internal Vector3 Carry(string rig) => Rigs.GetValueOrDefault(rig)?.Carry ?? Vector3.Zero;

    /// <summary>Takes a reloaded file's clips in place.</summary>
    internal void Take(ArmClipCatalog fresh)
    {
        Rigs.Clear();
        foreach (var (rig, set) in fresh.Rigs) Rigs[rig] = set;
    }
}

/// <summary>
/// One rig's clip-led motion families, by motion id, and where its clips are framed: how far (camera-space metres) the
/// rig's arms are carried from the shared shoulder (content/held/arms.json) to where the clips were authored to be
/// seen. The carry holds while the rig draws, clip-led or not; weapon-led hands keep their grips under it.
/// </summary>
internal sealed record RigClips(Dictionary<string, MotionClips> Motions)
{
    public Vector3 Carry { get; init; }
}

/// <summary>A motion family's clips on one rig: its idle loop and each action's clip, by action id.</summary>
internal sealed record MotionClips(IdleClip Idle, Dictionary<string, ArmClip> Actions);

/// <summary>A clip the arms loop at rest, once every <see cref="Seconds"/> of world time.</summary>
internal sealed record IdleClip(string Clip, float Seconds);

/// <summary>
/// What clip the arms play now (Engine <see cref="FirstPersonArms.Lead"/>) and the world seconds since they were last
/// led: the main hand's action fitted to its scaled phases, or the idle loop, on the world time the actions run on.
/// </summary>
internal sealed class ArmsLead
{
    private float since;
    private double idle;

    /// <summary>World seconds admitted (a held world holds the arms' clips as it holds the actions).</summary>
    internal void Step(float seconds)
    {
        since += seconds;
        idle += seconds;
    }

    /// <summary>The seconds since the last lead, taken.</summary>
    internal float Take()
    {
        float seconds = since;
        since = 0;
        return seconds;
    }

    /// <summary>The clip for a hand's action, its idle loop at rest, or null (weapon-led) for an action without one.</summary>
    internal ArmClipFrame? Frame(MotionClips? clips, ActionUser user)
    {
        if (clips is null) return null;
        if (user.Current is not { } action) return new(clips.Idle.Clip, (float)(idle / clips.Idle.Seconds % 1));
        if (!clips.Actions.TryGetValue(action.Id, out ArmClip? clip)) return null;
        PhaseTiming timing = HeldMotionCatalog.Timing(action);
        return clip.At(new(timing.Windup * user.Scale, timing.Commit * user.Scale, timing.Recovery * user.Scale), user.Elapsed);
    }

    /// <summary>Whether an action plays a clip on these clips (its motion is then not tweened).</summary>
    internal static bool Plays(MotionClips? clips, ActionDefinition action) => clips?.Actions.ContainsKey(action.Id) ?? false;
}

// Authored: missing constructor values, nulls in non-nullable fields and unknown members are errors, not defaults.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true, Converters = [typeof(Vector3Json)],
    RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(ArmClipCatalog))]
internal sealed partial class ArmClipJson : JsonSerializerContext;
