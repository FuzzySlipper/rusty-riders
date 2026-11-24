using System.Numerics;
using Rusty.Engine;
using RustyRiders.Game.Content;

namespace RustyRiders.Game.Levels;

/// <summary>
/// Owns the active scene's world fill lights, tone mapping, colour grading, bloom, distance fog, ambient occlusion
/// tuning and optional full-screen image effect. Applied when a scene becomes active (<see cref="Scenes"/>), cleanly
/// replacing or clearing prior state so kit atmospheres never bleed into other levels or the gallery.
/// </summary>
internal sealed class SceneAtmosphere : IDisposable
{
    // Logical light ids distinct from LevelKit's sconce ids (which start at 1).
    private static ulong nextLightId = 1_000_000;

    private readonly IEngineContext engine;
    private readonly List<Light> lights = [];
    private readonly Dictionary<string, RenderResource> shaders = [];
    private bool disposed;

    internal SceneAtmosphere(IEngineContext engine) => this.engine = engine;

    internal void Apply(AtmosphereDefinition atmosphere)
    {
        if (disposed) return;
        ClearLights();
        Vector3 sky = Authored.Vector(atmosphere.Hemisphere.SkyColor);
        Vector3 ground = Authored.Vector(atmosphere.Hemisphere.GroundColor);
        lights.Add(engine.Graphics.CreateLight(new LightRequest(nextLightId++, false, 0,
            new LightDescriptor(LightKind.Hemisphere, sky, atmosphere.Hemisphere.Intensity, true, Vector3.Zero, -Vector3.UnitY,
                false, 0, 0, 0, 0, LightShadowIntent.Disabled, 0, 0, false, ground))));

        if (atmosphere.Key is { Intensity: > 0 } key)
        {
            Vector3 direction = Vector3.Normalize(Authored.Vector(key.Direction));
            lights.Add(engine.Graphics.CreateLight(new LightRequest(nextLightId++, false, 0,
                new LightDescriptor(LightKind.Directional, Authored.Vector(key.Color), key.Intensity, true, Vector3.Zero, direction,
                    false, 0, 0, 0, 0, key.Shadows ? LightShadowIntent.Requested : LightShadowIntent.Disabled))));
        }

        if (atmosphere.BackgroundColor is { } bg)
            engine.CameraView.SetBackgroundColor(new SetBackgroundColorRequest(Authored.Color(bg)));

        engine.CameraView.SetToneMapping(atmosphere.ToneMapping?.Request ?? new ToneMappingRequest(ToneMappingOperator.None, 1));
        engine.CameraView.SetColorGrading(atmosphere.ColorGrading?.Request ?? new ColorGradingRequest(0, 0, 0, 0));
        engine.CameraView.SetBloom(atmosphere.Bloom?.Request ?? new BloomRequest(1, 0));
        engine.CameraView.SetFog(atmosphere.Fog?.Request ?? new FogRequest(FogMode.Off, new Color(0, 0, 0, 1), 0, 100, 0));

        if (atmosphere.AmbientOcclusion is { } ao)
        {
            RendererSettingsRequest current = engine.RendererSettings.Read().Requested;
            engine.RendererSettings.Set(current with
            {
                AmbientOcclusion = ao.Strength > 0 ? AmbientOcclusionMode.ScreenSpace : AmbientOcclusionMode.Disabled,
                AmbientOcclusionStrength = ao.Strength,
                AmbientOcclusionRadius = ao.Radius,
            });
        }

        if (atmosphere.ImageEffect is { } effect)
        {
            RenderResourceReference shader = OpenShader(effect.Shader);
            engine.CameraView.SetImageEffect(new ImageEffectRequest(shader, effect.Params0, effect.Params1, Vector4.Zero, Vector4.Zero, default, default));
        }
        else
        {
            engine.CameraView.SetImageEffect(new ImageEffectRequest(default, Vector4.Zero));
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        engine.CameraView.SetImageEffect(new ImageEffectRequest(default, Vector4.Zero));
        ClearLights();
        foreach (RenderResource shader in shaders.Values) shader.Dispose();
        shaders.Clear();
    }

    private void ClearLights()
    {
        foreach (Light light in lights) light.Dispose();
        lights.Clear();
    }

    private RenderResourceReference OpenShader(string path)
    {
        if (!shaders.TryGetValue(path, out RenderResource? resource))
            shaders[path] = resource = engine.Graphics.OpenResource(new RenderResourceRequest(path)).Handle;
        return new RenderResourceReference(resource);
    }
}
