using System.Text.Json.Serialization;
using Rusty.Engine;
using Rusty.Engine.FirstPerson;
using RustyRiders.Game.Content;

namespace RustyRiders.Game.Held;

/// <summary>What a hand feels as its action lands, layered over whatever motion it plays.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FeedbackKind>))]
internal enum FeedbackKind { Struck, Blocked, Fired }

/// <summary>
/// The held weapons' feedback (content/held/feedback.json): an Engine <see cref="HeldPunch"/> for each way an action
/// lands — a blow that strikes a body, a blow stopped by a surface, a shot or cast fired — layered over the hand's motion
/// by <see cref="HeldLayers"/>, so it adds to a baked swing instead of replacing it.
/// </summary>
internal sealed record HeldFeedback(HeldPunch Struck, HeldPunch Blocked, HeldPunch Fired)
{
    internal const string Path = "held/feedback.json";

    internal HeldPunch Punch(FeedbackKind kind) => kind switch
    {
        FeedbackKind.Struck => Struck,
        FeedbackKind.Blocked => Blocked,
        _ => Fired
    };

    internal static HeldFeedback Load(IEngineContext engine)
    {
        HeldFeedback feedback = Authored.Read(engine, Path, HeldJson.Default.HeldFeedback);
        foreach (FeedbackKind kind in Enum.GetValues<FeedbackKind>()) Authored.Require(Path, Field(kind), feedback.Punch(kind).Problems());
        return feedback;
    }

    /// <summary>Takes a reloaded file's values in place (live controls hold these records).</summary>
    internal void Take(HeldFeedback fresh)
    {
        foreach (FeedbackKind kind in Enum.GetValues<FeedbackKind>())
        {
            (HeldPunch live, HeldPunch file) = (Punch(kind), fresh.Punch(kind));
            (live.Move, live.Turn, live.Seconds, live.Frequency, live.Decay) = (file.Move, file.Turn, file.Seconds, file.Frequency, file.Decay);
        }
    }

    internal static string Field(FeedbackKind kind) => char.ToLowerInvariant(kind.ToString()[0]) + kind.ToString()[1..];
}
