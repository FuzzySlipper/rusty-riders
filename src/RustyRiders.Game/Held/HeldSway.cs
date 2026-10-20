using Rusty.Engine;
using Rusty.Engine.FirstPerson;
using RustyRiders.Game.Content;

namespace RustyRiders.Game.Held;

/// <summary>How the held weapons sway (content/held/sway.json), as the Engine's <see cref="SwayTuning"/>.</summary>
internal static class SwayFile
{
    internal const string Path = "held/sway.json";

    internal static SwayTuning Load(IEngineContext engine)
    {
        SwayTuning tuning = Authored.Read(engine, Path, HeldJson.Default.SwayTuning);
        Authored.Require(Path, "", tuning.Problems());
        return tuning;
    }

    /// <summary>Takes a reloaded file's values in place.</summary>
    internal static void Take(this SwayTuning live, SwayTuning fresh) =>
        (live.BlendSeconds, live.Acting, live.LongestStride) = (fresh.BlendSeconds, fresh.Acting, fresh.LongestStride);
}

/// <summary>Where a hand is in swapping what it holds: putting the old look away, about to draw the new one, or drawing it.</summary>
internal enum Swap { None, PuttingAway, DrawNext, Drawing }
