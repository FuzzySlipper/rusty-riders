using System.Numerics;
using System.Text.Json.Serialization;
using Rusty.Engine;
using Rusty.Engine.FirstPerson;
using RustyRiders.Game.Content;

namespace RustyRiders.Game.Held;

/// <summary>
/// How what the hands hold is drawn in first person: each held look (a weapon item names one), the stances looks sit
/// in at rest (<see cref="StanceCatalog"/>) and how looks move through actions (<see cref="HeldMotionCatalog"/>).
/// Presentation only; what an item does is its actions. Adapted from rusty-hotel's <c>HeldCatalog</c> for two hands that
/// each hold their own weapon (docs/reuse.md).
/// </summary>
internal sealed record HeldCatalog(HeldLookDefinition[] Looks)
{
    [JsonIgnore] internal HeldMotionCatalog Motion { get; init; } = null!;
    [JsonIgnore] internal StanceCatalog Stances { get; init; } = null!;
    /// <summary>The first-person arms that hold the looks (<see cref="ArmsDefinition.Path"/>).</summary>
    [JsonIgnore] internal ArmsDefinition Arms { get; init; } = null!;
    /// <summary>What a hand feels as its action lands (<see cref="HeldFeedback.Path"/>).</summary>
    [JsonIgnore] internal HeldFeedback Feedback { get; init; } = null!;
    /// <summary>How the weapons sway between and under actions (<see cref="SwayFile.Path"/>).</summary>
    [JsonIgnore] internal SwayTuning Sway { get; init; } = null!;
    /// <summary>The arms models the arms can be drawn by, by profile id (<see cref="ArmRigs.Directory"/>).</summary>
    [JsonIgnore] internal Dictionary<string, ArmRig> Rigs { get; init; } = null!;
    /// <summary>The looks the arms play baked clips for, per rig (<see cref="ArmClipCatalog.Path"/>).</summary>
    [JsonIgnore] internal ArmClipCatalog Clips { get; init; } = null!;

    internal const string Path = "held/looks.json";
    /// <summary>Where looks' solved grips live (<see cref="HeldLookDefinition.SolvedGrips"/>); any file there reloads in place.</summary>
    internal const string GripsDirectory = "held/grips/";

    /// <summary>The content files the held presentation reads, reloaded in place under <c>rusty dev</c> (RustyEngineLiveContent).</summary>
    internal static readonly string[] Files = [Path, StanceCatalog.Path, HeldMotionCatalog.Path, ArmsDefinition.Path, HeldFeedback.Path, SwayFile.Path, ArmClipCatalog.Path, .. ArmRigs.Ids.Select(ArmRigs.PathOf)];

    internal static HeldCatalog Load(IEngineContext engine)
    {
        ArmsDefinition arms = ArmsDefinition.Load(engine);
        HeldMotionCatalog motion = HeldMotionCatalog.Load(engine);
        Dictionary<string, ArmRig> rigs = ArmRigs.Load(engine, arms);
        HeldCatalog catalog = Authored.Read(engine, Path, HeldJson.Default.HeldCatalog) with
        {
            Motion = motion, Stances = StanceCatalog.Load(engine), Arms = arms, Rigs = rigs,
            Feedback = HeldFeedback.Load(engine), Sway = SwayFile.Load(engine), Clips = ArmClipCatalog.Load(engine, rigs, motion)
        };
        catalog.Validate();
        foreach (HeldLookDefinition look in catalog.Looks)
        {
            catalog.Seat(look);
            foreach (var (rig, path) in look.SolvedGrips ?? [])
                look.Solved[rig] = Authored.Read(engine, path, SolvedGripJson.Default.SolvedGrip);
        }
        return catalog;
    }

    internal HeldLookDefinition? Look(string id) => Looks.FirstOrDefault(l => l.Id == id);

    /// <summary>Fails a reference to a look that does not exist.</summary>
    internal void Require(string path, string field, string id) =>
        Authored.Require(Look(id) is not null, path, field, $"unknown held look '{id}'; see content/{Path}.");

    /// <summary>
    /// Seats a look on its stance: its grip on the stance's grip and its length and up side along the stance, after the
    /// look's own nudge. Its motion's poses move it from there. Changing a stance seats every look in it alike.
    /// </summary>
    internal void Seat(HeldLookDefinition look)
    {
        look.Seated = Stances.Stances[look.Stance].Seat(look.Adjust.Grip, look.Adjust.Turn);
    }

    /// <summary>
    /// Takes the values of <paramref name="fresh"/> from the files in <paramref name="changed"/> into this catalog in
    /// place (unchanged files keep their live, possibly unsaved, values), so the views holding it show them; then seats
    /// every look again. Returns what it could not take: changes to what exists or how it is drawn (looks, models,
    /// boxes, motions or stances added or removed, the settle), which need the product restarted.
    /// </summary>
    internal string[] Refresh(HeldCatalog fresh, IReadOnlySet<string> changed)
    {
        List<string> needRestart = [];
        void Same(bool same, string what) { if (!same) needRestart.Add(what); }
        if (changed.Contains(Path))
        {
            Same(Looks.Select(Drawn).SequenceEqual(fresh.Looks.Select(Drawn)), $"{Path} looks (ids, models, boxes, motions or stances)");
            foreach (HeldLookDefinition look in Looks)
                if (fresh.Look(look.Id) is { } now)
                {
                    (look.Adjust.Grip, look.Adjust.Turn) = (now.Adjust.Grip, now.Adjust.Turn);
                    // Whether a look has a hand hold or an off grip is how it is drawn (a restart); their values follow the file.
                    look.Grips.Main = now.Grips.Main;
                    if (look.Grips.Hand is not null && now.Grips.Hand is not null) look.Grips.Hand = now.Grips.Hand;
                    if (look.Grips.Off is not null && now.Grips.Off is not null) (look.Grips.Off, look.Grips.OffHand) = (now.Grips.Off, now.Grips.OffHand);
                    if (look.Shape is { } shape && now.Shape is { } shaped) shape.Take(shaped);
                    else look.Shape = now.Shape;
                    // Landmarks follow the file: points edited in place, a point or the whole set added or removed taken too.
                    if (look.Landmarks is { } marks && now.Landmarks is { } newer) marks.Take(newer);
                    else look.Landmarks = now.Landmarks;
                    look.Solved = now.Solved;
                    if (look.Model is { } model && now.Model is { } fresher)
                    {
                        (model.Rotation, model.Length) = (fresher.Rotation, fresher.Length);
                    }
                }
        }
        // A solved grip file changes no other value.
        if (!changed.Contains(Path) && changed.Any(c => c.StartsWith(GripsDirectory, StringComparison.Ordinal)))
            foreach (HeldLookDefinition look in Looks)
                if (fresh.Look(look.Id) is { } now) look.Solved = now.Solved;
        if (changed.Contains(StanceCatalog.Path))
        {
            Same(Stances.Stances.Keys.Order().SequenceEqual(fresh.Stances.Stances.Keys.Order()), $"{StanceCatalog.Path} stance ids");
            foreach (var (id, stance) in fresh.Stances.Stances)
                if (Stances.Stances.TryGetValue(id, out HeldStance? live))
                {
                    (live.Grip, live.Toward, live.Up) = (stance.Grip, stance.Toward, stance.Up);
                }
        }
        if (changed.Contains(HeldMotionCatalog.Path))
        {
            Same(Motion.SettleSeconds == fresh.Motion.SettleSeconds && Motion.SettleEase == fresh.Motion.SettleEase,
                $"{HeldMotionCatalog.Path} settleSeconds or settleEase");
            Same(Motion.Motions.Keys.Order().SequenceEqual(fresh.Motion.Motions.Keys.Order()), $"{HeldMotionCatalog.Path} motion ids");
            foreach (var (id, motion) in fresh.Motion.Motions)
            {
                if (!Motion.Motions.TryGetValue(id, out HeldMotion? live)) continue;
                // Poses keep their identity (the motion viewer may hold one); tracks follow the file exactly.
                foreach (var (name, pose) in motion.Poses)
                    if (live.Poses.TryGetValue(name, out HeldPose? held))
                    {
                        (held.Position, held.Rotation) = (pose.Position, pose.Rotation);
                    }
                    else live.Poses[name] = pose;
                foreach (string gone in live.Poses.Keys.Except(motion.Poses.Keys).ToArray()) live.Poses.Remove(gone);
                live.Tracks.Clear();
                foreach (var (track, keys) in motion.Tracks) live.Tracks[track] = keys;
                // Loops and transitions follow the file (the poses dictionary, shared, keeps its identity).
                Motion.Motions[id] = live with { Loops = motion.Loops, Equip = motion.Equip, Unequip = motion.Unequip };
            }
        }
        if (changed.Contains(ArmsDefinition.Path)) Arms.Take(fresh.Arms);
        if (changed.Contains(HeldFeedback.Path)) Feedback.Take(fresh.Feedback);
        if (changed.Contains(SwayFile.Path)) Sway.Take(fresh.Sway);
        if (changed.Contains(ArmClipCatalog.Path)) Clips.Take(fresh.Clips);
        foreach (var (id, rig) in Rigs)
        {
            if (!changed.Contains(ArmRigs.PathOf(id))) continue;
            Same(rig.SameDrawing(fresh.Rigs[id]), $"{ArmRigs.PathOf(id)} model, clip, joints or digit frames");
            rig.Take(fresh.Rigs[id]);
        }
        foreach (HeldLookDefinition look in Looks) Seat(look);
        return [.. needRestart];
    }

    // What a look draws and what it refers to, beyond the placement values a reload takes.
    private static string Drawn(HeldLookDefinition look) => string.Join("|", look.Id, look.Motion, look.Stance, look.Grips.Hand is not null, look.Grips.Off is not null,
        look.Box.Size, string.Join(",", look.Box.Color),
        look.Model?.Glb ?? "");

    private void Validate()
    {
        foreach (IGrouping<string, HeldLookDefinition> repeated in Looks.GroupBy(l => l.Id).Where(g => g.Count() > 1))
            Authored.Require(false, Path, "looks", $"names the '{repeated.Key}' look more than once.");
        for (int i = 0; i < Looks.Length; i++)
        {
            HeldLookDefinition look = Looks[i];
            string at = $"looks[{i}]";
            Authored.Require(Motion.Motions.ContainsKey(look.Motion), Path, $"{at}.motion", $"unknown motion '{look.Motion}' in {HeldMotionCatalog.Path}.");
            Authored.Require(Stances.Stances.ContainsKey(look.Stance), Path, $"{at}.stance", $"unknown stance '{look.Stance}' in {StanceCatalog.Path}.");
            Authored.Require((look.Grips.Off is null) == (look.Grips.OffHand is null), Path, $"{at}.grips.offHand",
                "comes with grips.off: a look held in both hands says where the off hand holds it and how.");
            Authored.Colour(Path, $"{at}.box.color", look.Box.Color);
            if (look.Shape is { } shape) Authored.Require(Path, $"{at}.shape", shape.Problems(offAllowed: look.Grips.Off is not null));
            if (look.Landmarks is { } marks) Authored.Require(Path, $"{at}.landmarks", marks.Problems(look.Grips.Off is not null));
            foreach (var (rig, grip) in look.Solved)
            {
                Authored.Require(Rigs.ContainsKey(rig), Path, $"{at}.solvedGrips.{rig}", $"unknown rig '{rig}'; profiles: {string.Join(", ", ArmRigs.Ids)}.");
                Authored.Require(look.SolvedGrips![rig].StartsWith(GripsDirectory, StringComparison.Ordinal), Path, $"{at}.solvedGrips.{rig}", $"names a file under content/{GripsDirectory}.");
                Authored.Require(look.SolvedGrips[rig], "grip", grip.Problems());
            }
            if (look.Model is { } model)
            {
                Authored.Require(model.Glb.EndsWith(".glb", StringComparison.Ordinal), Path, $"{at}.model.glb", "names a .glb file.");
                Authored.Positive(Path, $"{at}.model.length", model.Length);
            }
        }
    }
}

/// <summary>
/// One held look, named for the weapons that show it. It is drawn in its own look space, metres with its length along
/// -Z and its up side +Y: its converted <see cref="Model"/> fitted there when it opens, else its <see cref="Box"/>. It
/// moves by <see cref="Motion"/>, sits in <see cref="Stance"/> at rest with an optional <see cref="Nudge"/> from it, and
/// is held at <see cref="Grips"/> (look space), optionally with its own <see cref="Shape"/> for each hand's digits and
/// the <see cref="Landmarks"/> a correct hold puts the hand on (look space).
/// </summary>
internal sealed record HeldLookDefinition(string Id, string Motion, string Stance, HeldGrips Grips, HeldBox Box,
    HeldModel? Model = null, HeldNudge? Nudge = null, HeldShape? Shape = null, HeldLandmarks? Landmarks = null, Dictionary<string, string>? SolvedGrips = null)
{
    /// <summary>
    /// The main hand's grip solved by contact around this look (asset-pipeline's tools/fps-arms/grip.py output, read
    /// from the files <see cref="SolvedGrips"/> names), by arm rig id: a solve places the look in the hand it was solved
    /// on, so a rig without one holds the look by its grips. A live reload takes them.
    /// </summary>
    [JsonIgnore] internal Dictionary<string, SolvedGrip> Solved { get; set; } = [];

    /// <summary>How each hand's digits sit on this look over the rig's grip (none: the grip as it is); a live reload adds or removes it.</summary>
    public HeldShape? Shape { get; set; } = Shape;

    /// <summary>The points a correct hold puts the hand on; a live reload adds, removes or edits them.</summary>
    public HeldLandmarks? Landmarks { get; set; } = Landmarks;

    /// <summary>Where its stance seats it (camera space, main hand): its grip's place and its turn, set by <see cref="HeldCatalog.Seat"/>.</summary>
    [JsonIgnore] internal (Vector3 Grip, Quaternion Turn) Seated { get; set; }

    /// <summary>This look's nudge from its stance (none when it has none).</summary>
    [JsonIgnore] internal HeldNudge Adjust { get; } = Nudge ?? new(Vector3.Zero, Vector3.Zero);

    /// <summary>
    /// Where the look is and how it is turned in <paramref name="pose"/> (camera space), its grip moved and turned as
    /// the pose says from where its stance seats it; in the off hand, the main hand's placement mirrored.
    /// </summary>
    internal (Vector3 At, Quaternion Turn) Place(HeldPose pose, bool offHand)
    {
        Quaternion turn = Quaternion.Normalize(HeldFrames.Turn(pose.Rotation) * Seated.Turn);
        Vector3 grip = Seated.Grip + pose.Position;
        Vector3 at = grip - Vector3.Transform(Grips.Main, turn);
        return offHand ? (HeldFrames.Mirror(at), HeldFrames.Mirror(turn)) : (at, turn);
    }
}

/// <summary>
/// A look's converted model (a GLB under content/art) fitted into look space: centred on its bounds, scaled so its
/// longest side is <see cref="Length"/> metres, and turned by <see cref="Rotation"/> (degrees about X, Y, Z) so its
/// length runs along -Z with its up side +Y.
/// </summary>
internal sealed record HeldModel(string Glb, float Length, Vector3 Rotation)
{
    public Vector3 Rotation { get; set; } = Rotation;

    /// <summary>The fitted length; tuned live and taken by a reload in place.</summary>
    public float Length { get; set; } = Length;
}

/// <summary>The box a look is drawn as when it has no model or its model did not open: its size (look space) and colour.</summary>
internal sealed record HeldBox(Vector3 Size, float[] Color);

/// <summary>
/// Where the hands hold a look, in look space: the main hand at <see cref="Main"/>, optionally how
/// (<see cref="Hand"/>: degrees about X, Y, Z, the hand's canonical frame turned from the look's; without it the arms'
/// hold applies); and for a look held in both hands, the off hand at <see cref="Off"/> on the same look, held by
/// <see cref="OffHand"/> (the left hand's canonical frame, +X toward its thumb, turned from the look's; given as is, not
/// mirrored). A baked look takes its source's holds.
/// </summary>
internal sealed record HeldGrips(Vector3 Main, Vector3? Hand = null, Vector3? Off = null, Vector3? OffHand = null)
{
    public Vector3 Main { get; set; } = Main;
    public Vector3? Hand { get; set; } = Hand;
    public Vector3? Off { get; set; } = Off;
    public Vector3? OffHand { get; set; } = OffHand;
}

/// <summary>
/// A look's correction to its stance, judged by eye: its grip moved by <see cref="Grip"/> (camera space, metres) and the
/// look turned further by <see cref="Turn"/> (degrees about camera X, Y, Z) about that grip.
/// </summary>
internal sealed record HeldNudge(Vector3 Grip, Vector3 Turn)
{
    public Vector3 Grip { get; set; } = Grip;
    public Vector3 Turn { get; set; } = Turn;
}

// Authored: missing constructor values, nulls in non-nullable fields and unknown members are errors, not defaults.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true, Converters = [typeof(Vector3Json)],
    RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(HeldCatalog))]
[JsonSerializable(typeof(StanceCatalog))]
[JsonSerializable(typeof(HeldMotionCatalog))]
[JsonSerializable(typeof(HeldPose))]
[JsonSerializable(typeof(ArmsDefinition))]
[JsonSerializable(typeof(ArmRig))]
[JsonSerializable(typeof(HeldFeedback))]
[JsonSerializable(typeof(SwayTuning))]
internal sealed partial class HeldJson : JsonSerializerContext;

// A solver's output carries its diagnostics beside the grip.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, Converters = [typeof(Vector3Json)], UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip)]
[JsonSerializable(typeof(SolvedGrip))]
internal sealed partial class SolvedGripJson : JsonSerializerContext;
