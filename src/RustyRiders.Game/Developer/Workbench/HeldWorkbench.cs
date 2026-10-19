using Rusty.Engine;
using Rusty.Engine.Debugging;
using RustyRiders.Game.Combat;
using RustyRiders.Game.Held;

namespace RustyRiders.Game.Developer.Workbench;

/// <summary>
/// The first-person workbench as an Engine tunables page: choosers for the look, the action and the hand the motion
/// viewer shows, the action's keys as the timeline (held, sought and played through the motion viewer's commands), and
/// the values from <see cref="HeldTunables"/>. The Engine's <see cref="TunablesDebugModule"/> serves it, and its panel
/// renders it and saves each control where its source names. Adapted from rusty-hotel's <c>HotelWorkbench</c>
/// (docs/reuse.md).
/// </summary>
internal sealed class HeldWorkbench(IContentSourceService source, PlayerCombat combat, HeldMotionDebugCommands viewer, Action publish)
{
    private static readonly string[] Hands = ["main", "off"];

    internal TunablesDebugModule Module() => new(source, Page, Choose);

    private TunablesPage Page()
    {
        if (viewer.Viewing is not { Look: { } look, Action: { } action, Hand: var hand, Key: var key })
        {
            // Nothing shown yet: the look the main hand holds, or the first that has actions.
            string holding = combat.View.Shown(HeldView.MainHand);
            string first = Uses(holding).Length > 0 ? holding : viewer.Uses.First(u => u.Value.Length > 0).Key;
            viewer.Show(first, Uses(first)[0]);
            return Page();
        }
        HeldCatalog held = combat.View.Catalog;
        HeldMotion motion = held.Motion.Motions[held.Look(look)!.Motion];
        MotionKey[] keys = motion.Track(action.Id) ?? [];
        return new("First-person workbench", HeldTunables.For(held, look, action, key, publish))
        {
            Choosers =
            [
                new("look", "Look", look, [.. held.Looks.Select(l => l.Id).Where(l => Uses(l).Length > 0)]),
                new("action", "Action", action.Id, Uses(look)),
                new("hand", "Hand", Hands[hand], Hands)
            ],
            Timeline = new([.. keys.Select((k, i) => new TunableKey(i, k.Pose, k.Time(action.Timing)))], action.Duration, key),
            Commands = new Dictionary<string, string>
            {
                ["key"] = "riders.dev.motion.key {key}",
                ["at"] = "riders.dev.motion.at {seconds}",
                ["play"] = "riders.dev.motion.play {speed}"
            },
            Notice = HeldMotionCatalog.Order(motion, action)
        };
    }

    // A chooser picks the look (with its first action), the action or the hand; null when it is shown.
    private string? Choose(string chooser, string value)
    {
        var (look, hand, action, _) = viewer.Viewing;
        string inHand = Hands[hand];
        switch (chooser)
        {
            case "look":
                if (Uses(value) is not [var first, ..]) return $"No weapon shows '{value}', so it has no actions to play.";
                return Refusal(viewer.Show(value, first, inHand));
            case "action":
                if (look is null || !Uses(look).Contains(value)) return $"'{value}' is not an action of {look}.";
                return Refusal(viewer.Show(look, value, inHand));
            case "hand":
                if (look is null || action is null) return "Choose a look first.";
                return Refusal(viewer.Show(look, action.Id, value));
            default:
                return $"Unknown chooser '{chooser}'.";
        }
    }

    private static string? Refusal(DebugCommandResult result) => result.Succeeded ? null : result.Message;

    private string[] Uses(string look) => viewer.Uses.GetValueOrDefault(look) ?? [];
}
