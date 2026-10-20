using Rusty.Engine;
using Rusty.Engine.FirstPerson;
using RustyRiders.Game.Content;

namespace RustyRiders.Game.Held;

/// <summary>
/// Where held things sit in first person at rest, by kind, for the main (right) hand (content/held/stances.json): a
/// blade low at the right edge and rising, a gun nearer the centre and converging on the crosshair. Each is an Engine
/// <see cref="HeldStance"/>; a look names its stance and is seated on it, so every look of a kind sits alike, and the
/// off (left) hand holds its look mirrored.
/// </summary>
internal sealed record StanceCatalog(Dictionary<string, HeldStance> Stances)
{
    internal const string Path = "held/stances.json";

    internal static StanceCatalog Load(IEngineContext engine)
    {
        StanceCatalog catalog = Authored.Read(engine, Path, HeldJson.Default.StanceCatalog);
        Authored.Require(catalog.Stances.Count > 0, Path, "stances", "needs at least one stance.");
        foreach (var (id, stance) in catalog.Stances) Authored.Require(Path, $"stances.{id}", stance.Problems());
        return catalog;
    }
}
