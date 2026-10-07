using System.Text.Json.Serialization;
using Rusty.Engine;
using RustyRiders.Game.Content;

namespace RustyRiders.Game.Time;

/// <summary>
/// How fast the world runs for what the player does, from content/time.json, as fractions of realtime.
/// <see cref="HeldRate"/> is the one value for "standing still": 0 freezes the world; a small positive crawl is the
/// global fallback if a true freeze ever misbehaves. Walking and sprinting at full input run at their rates (partial
/// stick input scales between the held rate and those); a body in the air or about to jump runs at least at
/// <see cref="AirborneRate"/> so jumps and falls finish. Waiting (the wait control) lets <see cref="WaitSeconds"/>
/// of world time pass while the player stands still.
/// </summary>
internal sealed record TimeTuning(double HeldRate, double WalkRate, double SprintRate, double AirborneRate, double WaitSeconds)
{
    internal const string Path = "time.json";

    internal static TimeTuning Load(IEngineContext engine)
    {
        TimeTuning tuning = Authored.Read(engine, Path, TimeJson.Default.TimeTuning);
        Authored.Within(Path, "heldRate", (float)tuning.HeldRate, 0, 1);
        Authored.Within(Path, "walkRate", (float)tuning.WalkRate, (float)tuning.HeldRate, 1);
        Authored.Within(Path, "sprintRate", (float)tuning.SprintRate, (float)tuning.HeldRate, 1);
        Authored.Within(Path, "airborneRate", (float)tuning.AirborneRate, (float)tuning.HeldRate, 1);
        Authored.Positive(Path, "waitSeconds", (float)tuning.WaitSeconds);
        return tuning;
    }
}

// Authored: missing constructor values, nulls in non-nullable fields and unknown members are errors, not defaults.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(TimeTuning))]
internal sealed partial class TimeJson : JsonSerializerContext;
