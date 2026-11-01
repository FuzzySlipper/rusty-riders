using System.Text.Json.Serialization;
using RustyRiders.Game.Content;

namespace RustyRiders.Game.Actions;

/// <summary>What happens at an action's event, besides its landing; each is a cue its owners play.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ActionCue>))]
internal enum ActionCue
{
    /// <summary>A gun throws out a spent case.</summary>
    Eject,
    /// <summary>A magazine comes out of a gun.</summary>
    MagazineOut,
    /// <summary>A magazine goes into a gun.</summary>
    MagazineIn,
    /// <summary>A hammer or bolt is cocked.</summary>
    Cock,
}

/// <summary>
/// A moment within an action besides its landing (which is always the end of its windup): a cue, at a fraction (0 to 1)
/// of one phase, so retiming the action's phases moves it with them, as it moves the clip fitted to them. Place it on
/// the frame where the action's clip shows it.
/// </summary>
internal sealed record ActionEvent(ActionPhase Phase, float At, ActionCue Cue)
{
    internal void Validate(string path, string at)
    {
        Authored.Require(Phase is ActionPhase.Windup or ActionPhase.Commit or ActionPhase.Recovery, path, $"{at}.phase",
            "an event is in the windup, commit or recovery.");
        Authored.Within(path, $"{at}.at", At, 0, 1);
    }

    /// <summary>Seconds from the action's start to this event, its phases scaled by <paramref name="scale"/>.</summary>
    internal float Time(ActionTiming timing, float scale) => scale * Phase switch
    {
        ActionPhase.Windup => At * timing.Windup,
        ActionPhase.Commit => timing.Windup + At * timing.Commit,
        _ => timing.Windup + timing.Commit + At * timing.Recovery,
    };
}
