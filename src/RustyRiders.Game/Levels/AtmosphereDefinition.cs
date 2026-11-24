using System.Numerics;
using System.Text.Json.Serialization;
using Rusty.Engine;
using RustyRiders.Game.Content;

namespace RustyRiders.Game.Levels;

/// <summary>Tone mapping operators authored in atmosphere definitions.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AtmosphereToneOperator>))]
internal enum AtmosphereToneOperator { None, Neutral, AcesFilmic, Rolloff }

/// <summary>Distance fog modes authored in atmosphere definitions.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AtmosphereFogMode>))]
internal enum AtmosphereFogMode { Off, Linear, Exponential, ExponentialSquared }

/// <summary>
/// A scene's authored world lighting, atmosphere and finishing: hemisphere fill, optional directional key, tone mapping,
/// colour grading, bloom, distance fog, screen-space ambient occlusion, optional full-screen image effect (e.g. comic ink
/// outlines) and optional clear/background colour matching the fog. Used both as the default in
/// <c>content/levels/atmosphere.json</c> and as a kit recipe's <c>atmosphere</c> override.
/// </summary>
internal sealed record AtmosphereDefinition(
    HemisphereLightDefinition Hemisphere,
    DirectionalLightDefinition? Key = null,
    ToneMappingDefinition? ToneMapping = null,
    ColorGradingDefinition? ColorGrading = null,
    BloomDefinition? Bloom = null,
    FogDefinition? Fog = null,
    AmbientOcclusionDefinition? AmbientOcclusion = null,
    ImageEffectDefinition? ImageEffect = null,
    float[]? BackgroundColor = null)
{
    internal const string DefaultPath = "levels/atmosphere.json";

    internal static AtmosphereDefinition LoadDefault(IEngineContext engine)
    {
        AtmosphereDefinition atmosphere = Authored.Read(engine, DefaultPath, AtmosphereJson.Default.AtmosphereDefinition);
        atmosphere.Validate(DefaultPath, "");
        return atmosphere;
    }

    internal void Validate(string path, string prefix)
    {
        string Field(string name) => string.IsNullOrEmpty(prefix) ? name : $"{prefix}.{name}";
        Hemisphere.Validate(path, Field("hemisphere"));
        Key?.Validate(path, Field("key"));
        ToneMapping?.Validate(path, Field("toneMapping"));
        ColorGrading?.Validate(path, Field("colorGrading"));
        Bloom?.Validate(path, Field("bloom"));
        Fog?.Validate(path, Field("fog"));
        AmbientOcclusion?.Validate(path, Field("ambientOcclusion"));
        ImageEffect?.Validate(path, Field("imageEffect"));
        if (BackgroundColor is not null) Authored.Colour(path, Field("backgroundColor"), BackgroundColor);
    }
}

/// <summary>World hemisphere fill: linear RGB sky colour from above, ground colour from below, scaled by <see cref="Intensity"/>.</summary>
internal sealed record HemisphereLightDefinition(float[] SkyColor, float[] GroundColor, float Intensity)
{
    internal void Validate(string path, string field)
    {
        Authored.Colour(path, $"{field}.skyColor", SkyColor);
        Authored.Colour(path, $"{field}.groundColor", GroundColor);
        Authored.AtLeast(path, $"{field}.intensity", Intensity, 0);
    }
}

/// <summary>Optional directional world light (linear RGB, shining along <see cref="Direction"/>).</summary>
internal sealed record DirectionalLightDefinition(float[] Color, float Intensity, float[] Direction, bool Shadows = false)
{
    internal void Validate(string path, string field)
    {
        Authored.Colour(path, $"{field}.color", Color);
        Authored.AtLeast(path, $"{field}.intensity", Intensity, 0);
        Authored.Require(Direction.Length == 3 && Direction.All(float.IsFinite), path, $"{field}.direction", "must be three numbers [x, y, z].");
        Authored.Require(Authored.Vector(Direction).LengthSquared() > 1e-6f, path, $"{field}.direction", "must not be zero.");
    }
}

/// <summary>Tone mapping operator and linear exposure multiplier.</summary>
internal sealed record ToneMappingDefinition(AtmosphereToneOperator Operator, float Exposure = 1)
{
    internal void Validate(string path, string field) => Authored.Positive(path, $"{field}.exposure", Exposure);

    internal ToneMappingRequest Request => new(Operator switch
    {
        AtmosphereToneOperator.None => ToneMappingOperator.None,
        AtmosphereToneOperator.Neutral => ToneMappingOperator.Neutral,
        AtmosphereToneOperator.AcesFilmic => ToneMappingOperator.AcesFilmic,
        _ => ToneMappingOperator.Rolloff,
    }, Exposure);
}

/// <summary>Colour grading controls (each in [-1, 1]; 0 leaves colour unchanged).</summary>
internal sealed record ColorGradingDefinition(float Temperature = 0, float Tint = 0, float Contrast = 0, float Saturation = 0)
{
    internal void Validate(string path, string field)
    {
        Authored.Within(path, $"{field}.temperature", Temperature, -1, 1);
        Authored.Within(path, $"{field}.tint", Tint, -1, 1);
        Authored.Within(path, $"{field}.contrast", Contrast, -1, 1);
        Authored.Within(path, $"{field}.saturation", Saturation, -1, 1);
    }

    internal ColorGradingRequest Request => new(Temperature, Tint, Contrast, Saturation);
}

/// <summary>Bloom threshold and added glow intensity (0 turns bloom off).</summary>
internal sealed record BloomDefinition(float Threshold = 1, float Intensity = 0)
{
    internal void Validate(string path, string field)
    {
        Authored.AtLeast(path, $"{field}.threshold", Threshold, 0);
        Authored.AtLeast(path, $"{field}.intensity", Intensity, 0);
    }

    internal BloomRequest Request => new(Threshold, Intensity);
}

/// <summary>Distance fog: linear (<see cref="Start"/> to <see cref="End"/> metres) or exponential (<see cref="Density"/>).</summary>
internal sealed record FogDefinition(AtmosphereFogMode Mode, float[] Color, float Start = 0, float End = 100, float Density = 0)
{
    internal void Validate(string path, string field)
    {
        Authored.Colour(path, $"{field}.color", Color);
        Authored.AtLeast(path, $"{field}.start", Start, 0);
        Authored.AtLeast(path, $"{field}.end", End, Start);
        Authored.AtLeast(path, $"{field}.density", Density, 0);
    }

    internal FogRequest Request => new(Mode switch
    {
        AtmosphereFogMode.Linear => FogMode.Linear,
        AtmosphereFogMode.Exponential => FogMode.Exponential,
        AtmosphereFogMode.ExponentialSquared => FogMode.ExponentialSquared,
        _ => FogMode.Off,
    }, Authored.Color(Color), Start, End, Density);
}

/// <summary>Screen-space ambient occlusion strength and world-unit radius.</summary>
internal sealed record AmbientOcclusionDefinition(float Strength, float Radius)
{
    internal void Validate(string path, string field)
    {
        Authored.AtLeast(path, $"{field}.strength", Strength, 0);
        Authored.Positive(path, $"{field}.radius", Radius);
    }
}

/// <summary>
/// Full-screen product image effect (e.g. comic ink edge outlines): <see cref="Shader"/> is a content-relative
/// <c>.wgsl</c> path, followed by silhouette depth sensitivity, structural chroma sensitivity, maximum ink strength,
/// edge fade start/end depth, and ink colour (linear RGB).
/// </summary>
internal sealed record ImageEffectDefinition(string Shader, float DepthThreshold = 0.045f, float ColorThreshold = 0.22f,
    float Strength = 0.85f, float FadeEnd = 0.996f, float[]? InkColor = null)
{
    internal void Validate(string path, string field)
    {
        Authored.Require(!string.IsNullOrWhiteSpace(Shader), path, $"{field}.shader", "must name a shader path.");
        Authored.Positive(path, $"{field}.depthThreshold", DepthThreshold);
        Authored.Positive(path, $"{field}.colorThreshold", ColorThreshold);
        Authored.Within(path, $"{field}.strength", Strength, 0, 1);
        Authored.Within(path, $"{field}.fadeEnd", FadeEnd, 0, 1);
        if (InkColor is not null) Authored.Colour(path, $"{field}.inkColor", InkColor);
    }

    internal Vector4 Params0 => new(DepthThreshold, ColorThreshold, Strength, FadeEnd);
    internal Vector4 Params1 => InkColor is { } c ? new(c[0], c[1], c[2], 1) : new(0.015f, 0.012f, 0.025f, 1);
}

// Authored: missing constructor values, nulls in non-nullable fields and unknown members are errors, not defaults.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(AtmosphereDefinition))]
internal sealed partial class AtmosphereJson : JsonSerializerContext;
