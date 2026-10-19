using System.Numerics;
using System.Text.Json.Serialization;
using Rusty.Engine;
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

    internal const string Path = "held/looks.json";

    /// <summary>The content files the held presentation reads, reloaded in place under <c>rusty dev</c> (RustyEngineLiveContent).</summary>
    internal static readonly string[] Files = [Path, StanceCatalog.Path, HeldMotionCatalog.Path];

    internal static HeldCatalog Load(IEngineContext engine)
    {
        HeldCatalog catalog = Authored.Read(engine, Path, HeldJson.Default.HeldCatalog) with
        {
            Motion = HeldMotionCatalog.Load(engine), Stances = StanceCatalog.Load(engine)
        };
        catalog.Validate();
        foreach (HeldLookDefinition look in catalog.Looks) catalog.Seat(look);
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
        HeldStance stance = Stances.Stances[look.Stance];
        look.Seated = (Authored.Vector(stance.Grip) + Authored.Vector(look.Adjust.Grip),
            Quaternion.Normalize(Frames.Turn(look.Adjust.Turn) * stance.Turn()));
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
                    now.Adjust.Grip.CopyTo(look.Adjust.Grip, 0);
                    now.Adjust.Turn.CopyTo(look.Adjust.Turn, 0);
                    now.Grips.Main.CopyTo(look.Grips.Main, 0);
                    if (look.Model is { } model && now.Model is { } fresher)
                    {
                        fresher.Rotation.CopyTo(model.Rotation, 0);
                        model.Length = fresher.Length;
                    }
                }
        }
        if (changed.Contains(StanceCatalog.Path))
        {
            Same(Stances.Stances.Keys.Order().SequenceEqual(fresh.Stances.Stances.Keys.Order()), $"{StanceCatalog.Path} stance ids");
            foreach (var (id, stance) in fresh.Stances.Stances)
                if (Stances.Stances.TryGetValue(id, out HeldStance? live))
                {
                    stance.Grip.CopyTo(live.Grip, 0);
                    stance.Toward.CopyTo(live.Toward, 0);
                    stance.Up.CopyTo(live.Up, 0);
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
                        pose.Position.CopyTo(held.Position, 0);
                        pose.Rotation.CopyTo(held.Rotation, 0);
                    }
                    else live.Poses[name] = pose;
                foreach (string gone in live.Poses.Keys.Except(motion.Poses.Keys).ToArray()) live.Poses.Remove(gone);
                live.Tracks.Clear();
                foreach (var (track, keys) in motion.Tracks) live.Tracks[track] = keys;
            }
        }
        foreach (HeldLookDefinition look in Looks) Seat(look);
        return [.. needRestart];
    }

    // What a look draws and what it refers to, beyond the placement values a reload takes.
    private static string Drawn(HeldLookDefinition look) => string.Join("|", look.Id, look.Motion, look.Stance,
        string.Join(",", look.Box.Size), string.Join(",", look.Box.Color),
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
            Authored.Point(Path, $"{at}.grips.main", look.Grips.Main);
            Authored.Point(Path, $"{at}.box.size", look.Box.Size);
            Authored.Colour(Path, $"{at}.box.color", look.Box.Color);
            Authored.Point(Path, $"{at}.nudge.grip", look.Adjust.Grip);
            Authored.Point(Path, $"{at}.nudge.turn", look.Adjust.Turn);
            if (look.Model is { } model)
            {
                Authored.Require(model.Glb.EndsWith(".glb", StringComparison.Ordinal), Path, $"{at}.model.glb", "names a .glb file.");
                Authored.Positive(Path, $"{at}.model.length", model.Length);
                Authored.Point(Path, $"{at}.model.rotation", model.Rotation);
            }
        }
    }
}

/// <summary>
/// One held look, named for the weapons that show it. It is drawn in its own look space, metres with its length along
/// -Z and its up side +Y: its converted <see cref="Model"/> fitted there when it opens, else its <see cref="Box"/>. It
/// moves by <see cref="Motion"/>, sits in <see cref="Stance"/> at rest with an optional <see cref="Nudge"/> from it, and
/// is held at <see cref="Grips"/> (look space).
/// </summary>
internal sealed record HeldLookDefinition(string Id, string Motion, string Stance, HeldGrips Grips, HeldBox Box,
    HeldModel? Model = null, HeldNudge? Nudge = null)
{
    /// <summary>Where its stance seats it (camera space, main hand): its grip's place and its turn, set by <see cref="HeldCatalog.Seat"/>.</summary>
    [JsonIgnore] internal (Vector3 Grip, Quaternion Turn) Seated { get; set; }

    /// <summary>This look's nudge from its stance (none when it has none).</summary>
    [JsonIgnore] internal HeldNudge Adjust { get; } = Nudge ?? new([0, 0, 0], [0, 0, 0]);

    /// <summary>
    /// Where the look is and how it is turned in <paramref name="pose"/> (camera space), its grip moved and turned as
    /// the pose says from where its stance seats it; in the off hand, the main hand's placement mirrored.
    /// </summary>
    internal (Vector3 At, Quaternion Turn) Place(HeldPose pose, bool offHand)
    {
        Quaternion turn = Quaternion.Normalize(Frames.Turn(pose.Rotation) * Seated.Turn);
        Vector3 grip = Seated.Grip + Authored.Vector(pose.Position);
        Vector3 at = grip - Vector3.Transform(Authored.Vector(Grips.Main), turn);
        return offHand ? (Frames.Mirror(at), Frames.Mirror(turn)) : (at, turn);
    }
}

/// <summary>
/// A look's converted model (a GLB under content/old-art) fitted into look space: centred on its bounds, scaled so its
/// longest side is <see cref="Length"/> metres, and turned by <see cref="Rotation"/> (degrees about X, Y, Z) so its
/// length runs along -Z with its up side +Y.
/// </summary>
internal sealed record HeldModel(string Glb, float Length, float[] Rotation)
{
    /// <summary>The fitted length; tuned live and taken by a reload in place.</summary>
    public float Length { get; set; } = Length;
}

/// <summary>The box a look is drawn as when it has no model or its model did not open: its size (look space) and colour.</summary>
internal sealed record HeldBox(float[] Size, float[] Color);

/// <summary>Where the hand holds a look, in look space.</summary>
internal sealed record HeldGrips(float[] Main);

/// <summary>
/// A look's correction to its stance, judged by eye: its grip moved by <see cref="Grip"/> (camera space, metres) and the
/// look turned further by <see cref="Turn"/> (degrees about camera X, Y, Z) about that grip.
/// </summary>
internal sealed record HeldNudge(float[] Grip, float[] Turn);

// Authored: missing constructor values, nulls in non-nullable fields and unknown members are errors, not defaults.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true,
    RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(HeldCatalog))]
[JsonSerializable(typeof(StanceCatalog))]
[JsonSerializable(typeof(HeldMotionCatalog))]
[JsonSerializable(typeof(HeldPose))]
internal sealed partial class HeldJson : JsonSerializerContext;
