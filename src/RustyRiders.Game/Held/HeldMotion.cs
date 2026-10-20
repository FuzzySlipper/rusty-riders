using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.FirstPerson;
using RustyRiders.Game.Actions;
using RustyRiders.Game.Content;

namespace RustyRiders.Game.Held;

/// <summary>
/// How held looks move (content/held/motion.json): the Engine's <see cref="HeldMotion"/> families by id, played as
/// Engine tween timelines (<see cref="HeldTimelines"/>) on world time, so a held world holds the swing too, and how a
/// cut-short action settles back to rest. An action's keys are timed through its authored phases.
/// </summary>
/// <param name="SettleSeconds">How long a cut-short action takes to ease back to rest.</param>
/// <param name="SettleEase">The curve it eases back on.</param>
internal sealed record HeldMotionCatalog(float SettleSeconds, TweenEasingKind SettleEase, Dictionary<string, HeldMotion> Motions)
{
    internal const string Path = "held/motion.json";

    internal static HeldMotionCatalog Load(IEngineContext engine)
    {
        HeldMotionCatalog catalog = Authored.Read(engine, Path, HeldJson.Default.HeldMotionCatalog);
        Authored.Positive(Path, "settleSeconds", catalog.SettleSeconds);
        Authored.Require(HeldEases.IsNamed(catalog.SettleEase), Path, "settleEase", "must be a named curve (as a motion key's ease).");
        foreach (var (id, motion) in catalog.Motions) Authored.Require(Path, $"motions.{id}", motion.Problems());
        return catalog;
    }

    /// <summary>An action's phases, as its keys are timed through them.</summary>
    internal static PhaseTiming Timing(ActionDefinition action) => new(action.Timing.Windup, action.Timing.Commit, action.Timing.Recovery);

    /// <summary>Checks, for every action a look's items use, that the look's motion has keys for it in time order.</summary>
    internal void Validate(string path, IEnumerable<(string Field, HeldLookDefinition Look, ActionDefinition Action)> uses)
    {
        foreach (var (field, look, action) in uses)
        {
            HeldMotion motion = Motions[look.Motion];
            Authored.Require(motion.Track(action.Id) is not null, Path, $"motions.{look.Motion}.tracks",
                $"has no track for '{action.Id}' ({path} {field}) and no '{HeldMotion.DefaultTrack}' track.");
            Authored.Require(Order(motion, action) is "", Path, $"motions.{look.Motion}.tracks", Order(motion, action));
        }
    }

    /// <summary>Where the keys of an action fall out of time order (empty when they are in order).</summary>
    internal static string Order(HeldMotion motion, ActionDefinition action) => motion.OrderProblem(action.Id, Timing(action)) ?? "";

    /// <summary>
    /// The tween timeline that moves a held model through an action (see <see cref="HeldTimelines.Action"/>), its phases
    /// scaled by the user's action time and played <paramref name="speed"/> times as fast (a viewer's slow motion).
    /// </summary>
    internal TweenSegment[] Timeline(HeldMotion motion, ActionDefinition action, HeldPlacement place,
        float scale = 1, float speed = 1, (Vector3 Move, Quaternion Turn)? from = null) =>
        HeldTimelines.Action(motion, motion.Track(action.Id)!, Timing(action), place, SettleEase, scale, speed, from);

    /// <summary>The settle back to rest from the offset a cut-short action left the model at.</summary>
    internal TweenSegment[] Settle((Vector3 Move, Quaternion Turn) from) => HeldTimelines.Settle(from, SettleSeconds, SettleEase);
}
