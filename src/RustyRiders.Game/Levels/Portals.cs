using System.Numerics;
using Rusty.Engine;
using RustyRiders.Game.Content;

namespace RustyRiders.Game.Levels;

/// <summary>
/// The level's portals and point markers: each portal stands as the authored portal machine GLB housing a double-sided
/// WGSL rift shader disc inside its hollow ring aperture, colour-coded by role (entry, destination world, or return)
/// and animated on Engine presentation time so it freezes while the world holds. Each portal also carries a soft
/// colour-matched point light created when the level is shown. Cache and resident spots show as small cubes when
/// markers are on.
/// </summary>
internal sealed class Portals : IDisposable
{
    private const ulong FirstObjectId = 2_000_000;
    private const int ObjectsPerPortal = 64;
    private const float MarkerLift = .5f;
    private const int DiscSegments = 24;

    private static ulong nextLightId = 2_000_000;

    private readonly PortalLook look;
    private readonly RenderResource? machineResource;
    private readonly Appearance? machineAppearance;
    private readonly float machineScale = 1;
    private readonly RenderResource? shaderResource;
    private readonly List<Material> materials = [];
    private readonly List<MeshResource> meshes = [];
    private readonly List<Appearance> appearances = [];
    private readonly List<AppearanceFact> facts = [];
    private readonly List<PortalLightSpec> lightSpecs = [];
    private readonly List<(ulong Id, Light Light, PortalLightSpec Spec)> lights = [];
    private readonly List<string> problems = [];
    private double lastAnimatedSeconds = double.NaN;

    internal Portals(IEngineContext engine, PointRules rules, LevelPoints points)
    {
        look = rules.Portal;

        try
        {
            machineResource = engine.Animation.OpenAnimatedMesh(new AnimatedMeshResourceRequest(look.Model));
            AnimatedMeshInfo info = engine.Animation.ReadMeshInfo(machineResource);
            machineAppearance = engine.Animation.CreateAnimatedMeshAppearance(new AnimatedMeshAppearanceRequest(machineResource));
            float rawHeight = info.BoundsMax.Y - info.BoundsMin.Y;
            if (rawHeight > 1e-3f) machineScale = look.Height / rawHeight;
        }
        catch (EngineCallException error)
        {
            problems.Add($"portal machine {look.Model}: {error.Message}");
            machineAppearance?.Dispose();
            machineAppearance = null;
            machineResource?.Dispose();
            machineResource = null;
        }

        RenderResourceReference shaderRef = default;
        try
        {
            shaderResource = engine.Graphics.OpenResource(new RenderResourceRequest(look.Shader)).Handle;
            shaderRef = new RenderResourceReference(shaderResource);
        }
        catch (EngineCallException error)
        {
            problems.Add($"portal shader {look.Shader}: {error.Message}");
            shaderResource?.Dispose();
            shaderResource = null;
        }

        Dictionary<Color, Appearance> discs = [];
        Appearance MakeDisc(float[] rgb)
        {
            Color color = Authored.Color(rgb);
            if (discs.TryGetValue(color, out Appearance? cached)) return cached;
            MaterialRequest request = new(color, default, 1, new Color(1, 1, 1, 1),
                new Vector3(color.R, color.G, color.B), look.Glow, false, MaterialAlphaMode.Blend, .5f, 0);
            if (shaderResource is not null)
            {
                request = request with
                {
                    Shader = new MaterialShader(shaderRef,
                        new Vector4(color.R, color.G, color.B, look.Glow),
                        new Vector4(look.SwirlSpeed, look.Pulse, look.PulseHz, look.InkStrength),
                        Vector4.Zero, Vector4.Zero),
                };
            }
            Material material = engine.Graphics.CreateMaterial(request);
            materials.Add(material);
            MeshResource mesh = BuildDisc(engine, material, look.ApertureRadius);
            meshes.Add(mesh);
            Appearance made = engine.Graphics.CreateMeshAppearance(mesh);
            appearances.Add(made);
            return discs[color] = made;
        }

        int portalIndex = 0;
        void AddPortal(Vector3 feet, float yaw, float[] rgb)
        {
            ulong id = FirstObjectId + (ulong)(portalIndex * ObjectsPerPortal);
            Quaternion turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
            if (machineAppearance is not null)
                facts.Add(Fact(id++, new Transform(feet, turn, new Vector3(machineScale)), machineAppearance));
            Vector3 centre = feet + Vector3.UnitY * look.ApertureCenterY;
            facts.Add(Fact(id++, new Transform(centre, turn, Vector3.One), MakeDisc(rgb)));
            if (look.LightIntensity > 0)
            {
                Vector3 rgbVec = Authored.Vector(rgb);
                // Blend slightly toward warm-white so deeply saturated rift hues still illuminate the bronze/slate ring cleanly.
                Vector3 lightColor = Vector3.Lerp(rgbVec, Vector3.One, .22f);
                Vector3 forward = Vector3.Transform(Vector3.UnitZ, turn);
                float sideIntensity = look.LightIntensity * .68f;
                // Offset front and back of the ring plane so the glowing rift illuminates both faces of the brass calipers and plinth.
                foreach (float side in new[] { 0.90f, -0.90f })
                {
                    lightSpecs.Add(new PortalLightSpec(new LightDescriptor(LightKind.Point, lightColor, sideIntensity, true,
                        centre + forward * side, -Vector3.UnitY, true, look.LightRange, look.LightDecay, 0, 0,
                        LightShadowIntent.Disabled, 0, 1, false), portalIndex * 1.7f));
                }
            }
            portalIndex++;
        }

        AddPortal(points.Entry, points.EntryYawRadians, rules.EntryColor);
        foreach (RiftPoint rift in points.Rifts)
            AddPortal(rift.Feet, rift.YawRadians, rift.Destination?.RiftColor ?? rules.ReturnColor);

        if (!rules.Markers.Show) return;
        Dictionary<Color, Appearance> markerCubes = [];
        Appearance MakeMarker(float[] rgb)
        {
            Color color = Authored.Color(rgb);
            if (markerCubes.TryGetValue(color, out Appearance? cached)) return cached;
            Appearance made = engine.Graphics.CreatePrimitive(new PrimitiveAppearanceRequest(PrimitiveGeometry.Cube, false, color));
            appearances.Add(made);
            return markerCubes[color] = made;
        }
        ulong markerId = FirstObjectId + (ulong)(portalIndex * ObjectsPerPortal);
        Vector3 size = new(rules.Markers.Size);
        foreach (Vector3 cache in points.Caches)
            facts.Add(Fact(markerId++, new Transform(cache + Vector3.UnitY * MarkerLift, Quaternion.Identity, size), MakeMarker(rules.Markers.CacheColor)));
        foreach (Vector3 resident in points.Residents)
            facts.Add(Fact(markerId++, new Transform(resident + Vector3.UnitY * MarkerLift, Quaternion.Identity, size), MakeMarker(rules.Markers.ResidentColor)));
    }

    internal IReadOnlyList<string> Problems => problems;

    /// <summary>The portals and markers as published appearance facts.</summary>
    internal IEnumerable<AppearanceFact> Facts => facts;

    /// <summary>Creates each portal's colour-matched aperture light once the level becomes the active scene.</summary>
    internal void Light(IEngineContext engine)
    {
        if (lights.Count > 0) return;
        foreach (PortalLightSpec spec in lightSpecs)
        {
            ulong id = nextLightId++;
            lights.Add((id, engine.Graphics.CreateLight(new LightRequest(id, false, 0, spec.Descriptor)), spec));
        }
    }

    /// <summary>Pulses the portal aperture lights gently on Engine-admitted world time; holds still while the world holds.</summary>
    internal void Animate(IEngineContext engine, double worldSeconds)
    {
        if (lights.Count == 0 || look.Pulse <= 0 || look.PulseHz <= 0 || worldSeconds == lastAnimatedSeconds) return;
        lastAnimatedSeconds = worldSeconds;
        float t = (float)(worldSeconds * look.PulseHz * Math.Tau);
        foreach ((ulong id, Light light, PortalLightSpec spec) in lights)
        {
            float intensity = MathF.Max(.1f, spec.Descriptor.Intensity * (1 + look.Pulse * MathF.Sin(t + spec.Phase)));
            engine.Graphics.UpdateLight(new LightUpdateRequest(light, new LightRequest(id, false, 0, spec.Descriptor with { Intensity = intensity })));
        }
    }

    /// <summary>The rift whose trigger the feet stand in: within its radius across and not far above or below.</summary>
    internal static RiftPoint? Entered(LevelPoints points, PortalLook look, Vector3 feet) => points.Rifts.FirstOrDefault(rift =>
        Vector2.Distance(new Vector2(rift.Feet.X, rift.Feet.Z), new Vector2(feet.X, feet.Z)) <= look.TriggerRadius
        && MathF.Abs(rift.Feet.Y - feet.Y) <= look.Height);

    public void Dispose()
    {
        foreach ((_, Light light, _) in lights) light.Dispose();
        lights.Clear();
        foreach (Appearance appearance in appearances) appearance.Dispose();
        appearances.Clear();
        machineAppearance?.Dispose();
        foreach (MeshResource mesh in meshes) mesh.Dispose();
        meshes.Clear();
        foreach (Material material in materials) material.Dispose();
        materials.Clear();
        shaderResource?.Dispose();
        machineResource?.Dispose();
    }

    private static MeshResource BuildDisc(IEngineContext engine, Material material, float radius)
    {
        MeshBuilder builder = new(1);
        AddFace(builder, radius, Vector3.UnitZ, 0.004f);
        AddFace(builder, radius, -Vector3.UnitZ, -0.004f);
        return builder.Create(engine, [material]);

        static void AddFace(MeshBuilder mesh, float r, Vector3 normal, float zOffset)
        {
            uint centre = mesh.Vertex(new Vector3(0, 0, zOffset), new Vector2(.5f, .5f));
            uint[] ring = new uint[DiscSegments];
            for (int i = 0; i < DiscSegments; i++)
            {
                float a = i * MathF.Tau / DiscSegments;
                float cx = MathF.Cos(a), cy = MathF.Sin(a);
                ring[i] = mesh.Vertex(new Vector3(cx * r, cy * r, zOffset), new Vector2(.5f + cx * .5f, .5f + cy * .5f));
            }
            for (int i = 0; i < DiscSegments; i++)
                mesh.Triangle(0, centre, ring[i], ring[(i + 1) % DiscSegments], normal);
        }
    }

    private static AppearanceFact Fact(ulong id, Transform transform, Appearance appearance) =>
        new(id, false, 0, transform, appearance, true, RenderLayer.Scene);

    private sealed record PortalLightSpec(LightDescriptor Descriptor, float Phase);
}
