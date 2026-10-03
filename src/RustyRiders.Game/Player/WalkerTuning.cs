using System.Text.Json;
using System.Text.Json.Serialization;
using Rusty.Engine;

namespace RustyRiders.Game.Player;

/// <summary>The authored feel of the walker, from content/walker.json.</summary>
internal sealed record WalkerTuning(float Height, float CrouchedHeight, float Radius, float Speed, float SprintSpeed,
    float JumpSpeed, float Gravity, float MaximumStepHeight, float MaximumSlopeDegrees, float PointerRadiansPerUnit,
    float FlySpeed, float FlySprintSpeed, float FieldOfViewDegrees, float FarPlane)
{
    internal const string Path = "walker.json";

    internal static WalkerTuning Load(IEngineContext engine) =>
        JsonSerializer.Deserialize(ContentFiles.Read(engine, Path).Span, WalkerJson.Default.WalkerTuning)
        ?? throw new InvalidOperationException($"{Path} must contain the walker tuning.");
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WalkerTuning))]
internal sealed partial class WalkerJson : JsonSerializerContext;
