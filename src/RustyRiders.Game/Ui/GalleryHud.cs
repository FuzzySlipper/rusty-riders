using System.Text;
using Rusty.Engine;
using RustyRiders.Game.Gallery;
using RustyRiders.Game.Player;

namespace RustyRiders.Game.Ui;

/// <summary>The DOM panel's facts: what loaded, what is missing, and the exhibit nearest the walker.</summary>
internal static class GalleryHud
{
    private const int ProblemsShown = 4;

    internal static UiValue Create(GalleryScene scene, Walker walker)
    {
        string status = scene.Exhibits.Count == 0
            ? "No converted art found. Run scripts/import-old-art.sh, then restart."
            : $"{scene.Exhibits.Count} exhibits · {scene.PlacementCount} placements · {scene.MeshCount} meshes";
        string problems = scene.Problems.Count == 0 ? ""
            : string.Join("\n", scene.Problems.Take(ProblemsShown))
              + (scene.Problems.Count > ProblemsShown ? $"\n… and {scene.Problems.Count - ProblemsShown} more" : "");
        (string Key, string Text)[] fields =
        [
            ("status", status),
            ("problems", problems),
            ("exhibit", scene.Nearest(walker.Position)?.Label ?? ""),
            ("position", FormattableString.Invariant($"{(walker.Flying ? "flying" : "walking")} · {walker.Feet.X:0.0}, {walker.Feet.Y:0.0}, {walker.Feet.Z:0.0}")),
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
}
