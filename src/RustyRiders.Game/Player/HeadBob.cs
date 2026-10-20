using System.Text.Json.Serialization;
using Rusty.Engine;
using Rusty.Engine.FirstPerson;
using RustyRiders.Game.Content;

namespace RustyRiders.Game.Player;

/// <summary>How the walker's view bobs (content/player/bob.json), as the Engine's <see cref="HeadBobTuning"/>.</summary>
internal static class HeadBobFile
{
    internal const string Path = "player/bob.json";

    internal static HeadBobTuning Load(IEngineContext engine)
    {
        HeadBobTuning tuning = Authored.Read(engine, Path, HeadBobJson.Default.HeadBobTuning);
        Authored.Require(Path, "", tuning.Problems());
        return tuning;
    }
}

// Authored: missing constructor values, nulls in non-nullable fields and unknown members are errors, not defaults.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(HeadBobTuning))]
internal sealed partial class HeadBobJson : JsonSerializerContext;
