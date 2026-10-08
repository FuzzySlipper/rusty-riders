using Rusty.Engine;
using RustyRiders.Game.Combat;
using RustyRiders.Game.Content;
using RustyRiders.Game.Mechanics;
using RustyRiders.Game.Player;
using RustyRiders.Game.Run;
using RustyRiders.Game.Time;

namespace RustyRiders.Game.Ui;

/// <summary>
/// The DOM companion's facts, one projection per update: the scene's status, the walker's place, the world's time,
/// the player's vitals, effects and hands, the run and the chase, the latest notice and prompt, and the inventory and
/// character sheet the screens show. The UI draws them and holds no state of its own.
/// </summary>
internal static class Hud
{
    private const int ProblemsShown = 4;

    internal static UiValue Create(IWalkScene scene, Walker walker, TimeFlow time, Play play, Expedition run, UiFonts fonts)
    {
        UiValueWriter w = new();
        string problems = scene.Problems.Count == 0 ? ""
            : string.Join("\n", scene.Problems.Take(ProblemsShown))
              + (scene.Problems.Count > ProblemsShown ? $"\n… and {scene.Problems.Count - ProblemsShown} more" : "");
        (string _, string _, string action, string notice) = play.Combat.Hud();
        (string chase, string hostiles) = play.Enemies.Hud();
        (string depth, string bank, string? rift) = run.Hud(walker.Feet);
        PlayerVitals vitals = play.Vitals;
        List<uint> fields =
        [
            w.Text("titleFont", fonts.TitleUrl),
            w.Text("status", scene.Status),
            w.Text("problems", problems),
            w.Text("exhibit", rift ?? scene.Describe(walker.Position)),
            w.Text("position", FormattableString.Invariant($"{(walker.Flying ? "flying" : "walking")} · {walker.Feet.X:0.0}, {walker.Feet.Y:0.0}, {walker.Feet.Z:0.0}")),
            w.Text("time", time.State.Describe(time.Tuning.HeldRate)),
            w.Flag("held", time.State.Rate <= time.Tuning.HeldRate && time.State.AdvanceSeconds <= 0),
            w.Text("worldTime", FormattableString.Invariant($"{time.WorldSeconds:0.0} s")),
            w.Number("health", vitals.Health.ValueInt),
            w.Number("healthMax", vitals.Health.MaximumValue),
            Tracks(w, vitals.Stats),
            w.Array("effects", vitals.Stats.Effects.Active.Select(e => w.Object("", w.Text("mark", e.Definition.Mark),
                w.Text("text", Effect(e, play.MechanicsText)))).ToArray()),
            Hands(w, play.Combat),
            w.Text("action", action),
            w.Number("actionProgress", play.Combat.Acting?.Progress ?? 0),
            w.Text("notice", notice),
            w.Text("prompt", play.Pickups.Prompt(walker.Feet)),
            w.Text("chase", chase),
            w.Text("hostiles", hostiles),
            w.Text("depth", depth),
            w.Text("bank", bank),
            w.Text("haul", Template.Fill(play.Pickups.Text.Haul, ("value", play.Inventory.Haul))),
            w.Text("ready", play.Inventory.Ready is { } ready
                ? Template.Fill(play.Pickups.Text.Consumable, ("item", ready.Item.Name), ("count", ready.Count)) : ""),
            w.Text("readyIcon", play.Inventory.Ready?.Item.Icon ?? ""),
            w.Text("summary", run.Summary),
            InventoryFacts.Write(w, play.Inventory, play.Combat),
            SheetFacts.Write(w, vitals.Stats),
        ];
        return w.Finish([.. fields]);
    }

    // The tracks other than health (charge, ammunition), with their maximums.
    private static uint Tracks(UiValueWriter w, ActorStats stats) => w.Array("tracks", stats.Mechanics.Tracks
        .Where(t => t.Id != ActorStats.HealthTrack)
        .Select(t => w.Object("", w.Text("name", t.Name), w.Number("value", stats.Track(t.Id).ValueInt), w.Number("max", stats.Track(t.Id).MaximumValue)))
        .ToArray());

    // Each hand's weapon, its load and whether its action is under way.
    private static uint Hands(UiValueWriter w, PlayerCombat combat) => w.Array("hands", new[] { PlayerCombat.MainHand, PlayerCombat.OffHand }
        .Select(hand =>
        {
            (Items.ItemDefinition item, int loaded, int size) = combat.Held(hand);
            return w.Object("", w.Text("name", item.Name), w.Text("icon", item.Icon), w.Number("loaded", loaded), w.Number("size", size),
                w.Flag("acting", combat.Acting?.Hand == hand));
        }).ToArray());

    /// <summary>An effect's name with its stacks, time left and any ward left, from the authored templates.</summary>
    private static string Effect(LiveEffect effect, MechanicsMessages text)
    {
        string line = effect.Definition.Name;
        if (effect.Stacks > 1) line += " " + Template.Fill(text.EffectStacks, ("stacks", effect.Stacks));
        if (effect.Definition.Ward is not null) line += " " + Template.Fill(text.EffectWard, ("ward", effect.WardLeft));
        return line + " " + Template.Fill(text.EffectSeconds, ("seconds", MathF.Ceiling(effect.Remaining)));
    }
}
