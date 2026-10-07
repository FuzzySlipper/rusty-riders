using System.Numerics;
using Rusty.Engine;
using RustyRiders.Game.Content;

namespace RustyRiders.Game.Levels;

/// <summary>
/// The level's portals and point markers as placeholder primitives: each portal a disc that pulses with cubes orbiting
/// its rim, animated on world time so it stands still while the world holds; the entry portal in the entry colour,
/// each rift in its destination world's colour. Cache and resident spots show as small cubes when markers are on.
/// </summary>
internal sealed class Portals : IDisposable
{
    private const ulong FirstObjectId = 2_000_000;
    private const int ObjectsPerPortal = 64;
    private const float MarkerLift = .5f;

    private readonly PortalLook look;
    private readonly List<Appearance> appearances = [];
    private readonly List<(Vector3 Feet, float Yaw, Appearance Disc, Appearance Orbiter)> portals = [];
    private readonly List<AppearanceFact> markers = [];

    internal Portals(IEngineContext engine, PointRules rules, LevelPoints points)
    {
        look = rules.Portal;
        Dictionary<Color, Appearance> sphere = [], cube = [];
        Appearance Make(Dictionary<Color, Appearance> cache, PrimitiveGeometry geometry, float[] rgb)
        {
            Color color = Authored.Color(rgb);
            if (!cache.TryGetValue(color, out Appearance? made))
            {
                cache[color] = made = engine.Graphics.CreatePrimitive(new PrimitiveAppearanceRequest(geometry, false, color));
                appearances.Add(made);
            }
            return made;
        }

        portals.Add((points.Entry, points.EntryYawRadians, Make(sphere, PrimitiveGeometry.Sphere, rules.EntryColor),
            Make(cube, PrimitiveGeometry.Cube, rules.EntryColor)));
        foreach (RiftPoint rift in points.Rifts)
            portals.Add((rift.Feet, rift.YawRadians, Make(sphere, PrimitiveGeometry.Sphere, rift.Destination.RiftColor),
                Make(cube, PrimitiveGeometry.Cube, rift.Destination.RiftColor)));
        if (!rules.Markers.Show) return;
        ulong id = FirstObjectId + (ulong)(portals.Count * ObjectsPerPortal);
        Vector3 size = new(rules.Markers.Size);
        foreach (Vector3 cache in points.Caches)
            markers.Add(Fact(id++, new Transform(cache + Vector3.UnitY * MarkerLift, Quaternion.Identity, size), Make(cube, PrimitiveGeometry.Cube, rules.Markers.CacheColor)));
        foreach (Vector3 resident in points.Residents)
            markers.Add(Fact(id++, new Transform(resident + Vector3.UnitY * MarkerLift, Quaternion.Identity, size), Make(cube, PrimitiveGeometry.Cube, rules.Markers.ResidentColor)));
    }

    /// <summary>The portals and markers as they stand at <paramref name="worldSeconds"/> of world time.</summary>
    internal IEnumerable<AppearanceFact> Facts(double worldSeconds)
    {
        float pulse = 1 + look.Pulse * MathF.Sin((float)(worldSeconds * look.PulseHz * Math.Tau));
        float orbit = (float)(worldSeconds * look.OrbitHz * Math.Tau);
        Vector3 discScale = new(look.Width * pulse, look.Height * pulse, look.Depth);
        float rimX = look.Width / 2 + look.OrbiterSize, rimY = look.Height / 2 + look.OrbiterSize;
        for (int p = 0; p < portals.Count; p++)
        {
            (Vector3 feet, float yaw, Appearance disc, Appearance orbiter) = portals[p];
            Quaternion turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
            Vector3 centre = feet + Vector3.UnitY * (look.Lift + look.Height / 2);
            ulong id = FirstObjectId + (ulong)(p * ObjectsPerPortal);
            yield return Fact(id++, new Transform(centre, turn, discScale), disc);
            for (int i = 0; i < look.Orbiters; i++)
            {
                float angle = orbit + i * MathF.Tau / look.Orbiters;
                Vector3 local = new(MathF.Cos(angle) * rimX, MathF.Sin(angle) * rimY, 0);
                Quaternion spin = turn * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, angle);
                yield return Fact(id++, new Transform(centre + Vector3.Transform(local, turn), spin, new Vector3(look.OrbiterSize)), orbiter);
            }
        }
        foreach (AppearanceFact marker in markers) yield return marker;
    }

    /// <summary>The rift whose trigger the feet stand in: within its radius across and not far above or below.</summary>
    internal static RiftPoint? Entered(LevelPoints points, PortalLook look, Vector3 feet) => points.Rifts.FirstOrDefault(rift =>
        Vector2.Distance(new Vector2(rift.Feet.X, rift.Feet.Z), new Vector2(feet.X, feet.Z)) <= look.TriggerRadius
        && MathF.Abs(rift.Feet.Y - feet.Y) <= look.Height);

    public void Dispose()
    {
        foreach (Appearance appearance in appearances) appearance.Dispose();
    }

    private static AppearanceFact Fact(ulong id, Transform transform, Appearance appearance) =>
        new(id, false, 0, transform, appearance, true, RenderLayer.Scene);
}
