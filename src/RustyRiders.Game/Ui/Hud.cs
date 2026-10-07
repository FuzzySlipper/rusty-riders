using System.Text;
using Rusty.Engine;
using RustyRiders.Game.Combat;
using RustyRiders.Game.Content;
using RustyRiders.Game.Enemies;
using RustyRiders.Game.Mechanics;
using RustyRiders.Game.Player;
using RustyRiders.Game.Time;

namespace RustyRiders.Game.Ui;

/// <summary>The DOM panel's facts: the scene's status and problems, what is under the walker, where it is, and the world's time.</summary>
internal static class Hud
{
    private const int ProblemsShown = 4;

    internal static UiValue Create(IWalkScene scene, Walker walker, TimeFlow time, PlayerVitals vitals, MechanicsMessages messages, PlayerCombat combat, EnemyDirector enemies)
    {
        string problems = scene.Problems.Count == 0 ? ""
            : string.Join("\n", scene.Problems.Take(ProblemsShown))
              + (scene.Problems.Count > ProblemsShown ? $"\n… and {scene.Problems.Count - ProblemsShown} more" : "");
        (string hands, string supplies, string action, string notice) = combat.Hud();
        (string chase, string hostiles) = enemies.Hud();
        (string Key, string Text)[] fields =
        [
            ("status", scene.Status),
            ("problems", problems),
            ("exhibit", scene.Describe(walker.Position)),
            ("position", FormattableString.Invariant($"{(walker.Flying ? "flying" : "walking")} · {walker.Feet.X:0.0}, {walker.Feet.Y:0.0}, {walker.Feet.Z:0.0}")),
            ("time", time.State.Describe(time.Tuning.HeldRate)),
            ("worldTime", FormattableString.Invariant($"{time.WorldSeconds:0.0} s")),
            ("health", FormattableString.Invariant($"{vitals.Health.ValueInt} / {vitals.Health.MaximumValue:0}")),
            ("healthShare", FormattableString.Invariant($"{vitals.Health.Value / Math.Max(1, vitals.Health.MaximumValue):0.###}")),
            ("effects", string.Join("  ", vitals.Stats.Effects.Active.Select(e => Effect(e, messages)))),
            ("hands", hands),
            ("supplies", supplies),
            ("action", action),
            ("notice", notice),
            ("chase", chase),
            ("hostiles", hostiles),
        ];
        List<byte> utf8 = [];
        List<StructuredValueNode> nodes = [new(StructuredValueKind.Object, 0, 0, 0, 0, 0, 0, 0, (uint)fields.Length)];
        foreach ((string key, string text) in fields)
        {
            byte[] keyBytes = Encoding.UTF8.GetBytes(key), textBytes = Encoding.UTF8.GetBytes(text);
            uint keyOffset = (uint)utf8.Count;
            utf8.AddRange(keyBytes);
            uint textOffset = (uint)utf8.Count;
            utf8.AddRange(textBytes);
            nodes.Add(new StructuredValueNode(StructuredValueKind.String, 0, 0, keyOffset, (uint)keyBytes.Length, textOffset, (uint)textBytes.Length, 0, 0));
        }
        return new UiValue(nodes.ToArray(), Enumerable.Range(1, fields.Length).Select(index => (uint)index).ToArray(), 0, utf8.ToArray());
    }

    /// <summary>An effect's mark and name with its stacks, time left and any ward left, from the authored templates.</summary>
    private static string Effect(LiveEffect effect, MechanicsMessages text)
    {
        string line = $"{effect.Definition.Mark} {effect.Definition.Name}";
        if (effect.Stacks > 1) line += " " + Template.Fill(text.EffectStacks, ("stacks", effect.Stacks));
        if (effect.Definition.Ward is not null) line += " " + Template.Fill(text.EffectWard, ("ward", effect.WardLeft));
        return line + " " + Template.Fill(text.EffectSeconds, ("seconds", MathF.Ceiling(effect.Remaining)));
    }
}
