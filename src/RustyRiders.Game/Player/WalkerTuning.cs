using System.Text.Json.Serialization;
using Rusty.Engine;
using RustyRiders.Game.Content;

namespace RustyRiders.Game.Player;

/// <summary>The authored feel of the walker, from content/walker.json.</summary>
internal sealed record WalkerTuning(float Height, float CrouchedHeight, float Radius, float Speed, float SprintSpeed,
    float JumpSpeed, float Gravity, float MaximumStepHeight, float MaximumSlopeDegrees, float PointerRadiansPerUnit,
    float FlySpeed, float FlySprintSpeed, float FieldOfViewDegrees, float FarPlane)
{
    internal const string Path = "walker.json";

    internal static WalkerTuning Load(IEngineContext engine) => Authored.Read(engine, Path, WalkerJson.Default.WalkerTuning);
}

// Authored: missing constructor values, nulls in non-nullable fields and unknown members are errors, not defaults.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(WalkerTuning))]
internal sealed partial class WalkerJson : JsonSerializerContext;
