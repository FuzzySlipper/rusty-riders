using System.Numerics;
using Rusty.Engine;
using RustyRiders.Game.Content;

namespace RustyRiders.Game.Levels.Kit;

/// <summary>
/// The kit's wall sconces: one either side of each doorway on the room's side, beyond any door arch ring, wherever
/// the wall there is a straight room wall. Each is a sculpted wrought-iron torch sconce (wall plate, bracket arm,
/// diagonal scroll brace, tapered vertical handle, flared octagonal brazier bowl and cage prongs), a multi-tone
/// stylized comic flame (bright gold-cream inner heart wrapped in asymmetric fiery-orange outer tongues), a rising
/// additive ember particle emitter, and a subtly flickering warm point light. Doorways may also carry a downward floor
/// spotlight pool and reveal fill.
/// </summary>
internal static class Sconces
{
    private const float PlateWidth = .16f;

    internal static (List<KitLight> Lights, List<PresentationParticleDescriptor> Embers) Build(MeshBuilder mesh, RoomPlan plan, KitRecipe recipe,
        SconceDefinition sconce, int seed)
    {
        List<KitLight> lights = [];
        List<PresentationParticleDescriptor> embers = [];
        float reach = plan.Rules.DoorWidth / 2 + (recipe.DoorArch?.Ring?.Width ?? 0) + sconce.Gap + PlateWidth / 2;
        float probe = plan.Rules.GridMetres / 2;
        bool Room(Vector2 at) => plan.Open.TryGetValue(plan.Square(at), out PlanArea area) && area == PlanArea.Room;
        int sconceIndex = 0;
        foreach (PlanDoorway doorway in plan.Doorways)
        {
            Vector2 into = doorway.IntoRoom, across = new(-into.Y, into.X), face = doorway.Centre + into * plan.Rules.WallMetres;
            foreach (float side in (float[])[-1, 1])
            {
                Vector2 wall = face + across * (side * reach);
                if (!Room(wall + into * probe) || plan.Open.ContainsKey(plan.Square(wall - into * probe))) continue;
                Random random = new(unchecked(seed * 7919 + 6_000_019 + sconceIndex * 104729));
                IronTorch(mesh, sconce, wall, into, across);
                Vector3 cupTop = OpenGround.Gltf(wall + into * sconce.BracketDepth, sconce.Height + .06f);
                ComicFlame(mesh, cupTop, sconce.FlameSize, random);
                Vector3 lightPos = OpenGround.Gltf(wall + into * (sconce.BracketDepth + .35f), sconce.Height + sconce.FlameSize * .75f);
                float phase = (float)(random.NextDouble() * MathF.Tau);
                lights.Add(new KitLight(new LightDescriptor(LightKind.Point, Authored.Vector(sconce.LightColor), sconce.Intensity, true, lightPos, -Vector3.UnitY,
                    true, sconce.Range, sconce.Decay, 0, 0, sconce.Shadows ? LightShadowIntent.Requested : LightShadowIntent.Disabled,
                    sconce.ShadowResolution, 1, sconce.ShadowSoft), sconce.Flicker, phase));
                if (sconce.EmberRate > 0 && sconce.EmberMax > 0)
                    embers.Add(EmberEmitter(cupTop + Vector3.UnitY * (sconce.FlameSize * .55f), sconce.EmberRate, sconce.EmberMax, (ulong)(seed * 997 + sconceIndex + 1)));
                sconceIndex++;
            }
            if (sconce.Pool is { } pool)
            {
                Vector3 origin = OpenGround.Gltf(face + into * pool.Forward, pool.Height);
                Vector3 target = OpenGround.Gltf(face + into * pool.AimForward, 0);
                Vector3 dir = Vector3.Normalize(target - origin);
                lights.Add(new KitLight(new LightDescriptor(LightKind.Spot, Authored.Vector(pool.Color), pool.Intensity, true, origin, dir,
                    true, pool.Range, pool.Decay, pool.OuterDegrees * MathF.PI / 180f, pool.Penumbra,
                    pool.Shadows ? LightShadowIntent.Requested : LightShadowIntent.Disabled, pool.ShadowResolution, 2, false)));
                if (pool.RevealIntensity > 0)
                {
                    Vector3 reveal = OpenGround.Gltf(doorway.Centre + into * (plan.Rules.WallMetres * .55f), plan.Rules.DoorHeight + .35f);
                    lights.Add(new KitLight(new LightDescriptor(LightKind.Point, Authored.Vector(pool.Color), pool.RevealIntensity, true, reveal, -Vector3.UnitY,
                        true, pool.RevealRange, pool.Decay, 0, 0, LightShadowIntent.Disabled)));
                }
            }
        }
        if (sconce.Corridors is { } corridors)
            AddCorridorSconces(mesh, plan, sconce, corridors, seed, lights, embers, ref sconceIndex);
        return (lights, embers);
    }

    /// <summary>Staggered non-shadowed wall torches along straight corridor runs so passages carry warm/cool pools.</summary>
    private static void AddCorridorSconces(MeshBuilder mesh, RoomPlan plan, SconceDefinition sconce, CorridorSconceDefinition corridors,
        int seed, List<KitLight> lights, List<PresentationParticleDescriptor> embers, ref int sconceIndex)
    {
        float probe = plan.Rules.GridMetres / 2;
        float doorClear = plan.Rules.DoorWidth / 2 + 2.5f;
        Dictionary<(int X, int Z), float> ground = plan.Heights([PlanArea.Room, PlanArea.Corridor, PlanArea.Doorway]);
        foreach (List<(Vector2 Point, float Height)> loop in OpenGround.Walls(ground))
        {
            for (int i = 0; i < loop.Count; i++)
            {
                if (plan.Rules.Area(loop[i].Height) != PlanArea.Corridor) continue;
                Vector2 a = plan.Metres(loop[i].Point), b = plan.Metres(loop[(i + 1) % loop.Count].Point);
                float length = Vector2.Distance(a, b);
                Vector2 along = Vector2.Normalize(b - a), into = new(-along.Y, along.X);
                // Stagger opposite corridor walls by half a spacing so short passages get one torch and long ones alternate sides.
                bool staggered = into.X + into.Y < 0;
                float lead = corridors.Clearance + (staggered ? corridors.Spacing * .5f : 0);
                float span = length - lead - corridors.Clearance;
                if (span < 0) continue;
                int count = (int)MathF.Floor(span / corridors.Spacing) + 1;
                float start = lead + (span - (count - 1) * corridors.Spacing) / 2;
                for (int k = 0; k < count; k++)
                {
                    Vector2 wall = a + along * (start + k * corridors.Spacing);
                    if (!plan.Open.TryGetValue(plan.Square(wall + into * probe), out PlanArea area) || area != PlanArea.Corridor) continue;
                    if (plan.Open.ContainsKey(plan.Square(wall - into * probe))) continue;
                    if (plan.Doorways.Any(d => Vector2.Distance(wall, d.Centre) < doorClear)) continue;
                    Random random = new(unchecked(seed * 7919 + 6_500_023 + sconceIndex * 104729));
                    IronTorch(mesh, sconce, wall, into, along);
                    float flameSize = sconce.FlameSize * corridors.FlameScale;
                    Vector3 cupTop = OpenGround.Gltf(wall + into * sconce.BracketDepth, sconce.Height + .06f);
                    ComicFlame(mesh, cupTop, flameSize, random);
                    Vector3 lightPos = OpenGround.Gltf(wall + into * (sconce.BracketDepth + .35f), sconce.Height + flameSize * .75f);
                    float phase = (float)(random.NextDouble() * MathF.Tau);
                    lights.Add(new KitLight(new LightDescriptor(LightKind.Point, Authored.Vector(sconce.LightColor), corridors.Intensity, true,
                        lightPos, -Vector3.UnitY, true, corridors.Range, corridors.Decay, 0, 0, LightShadowIntent.Disabled), sconce.Flicker, phase));
                    if (corridors.EmberRate > 0 && corridors.EmberMax > 0)
                        embers.Add(EmberEmitter(cupTop + Vector3.UnitY * (flameSize * .55f), corridors.EmberRate, corridors.EmberMax, (ulong)(seed * 997 + sconceIndex + 1)));
                    sconceIndex++;
                }
            }
        }
    }

    private static PresentationParticleDescriptor EmberEmitter(Vector3 origin, float rate, uint maxParticles, ulong seed) => new()
    {
        SignalId = "torch-ember",
        Visible = true,
        Anchor = new PresentationAnchor { Kind = PresentationAnchorKind.World, Position = origin },
        Visual = PresentationParticleVisual.Cube,
        Blend = PresentationParticleBlendMode.Additive,
        SizeMode = PresentationParticleSizeMode.World,
        RatePerSecond = rate,
        BurstCount = 0,
        MaxParticles = maxParticles,
        LifetimeMinSeconds = .65f,
        LifetimeMaxSeconds = 1.45f,
        VelocityMin = new Vector3(-.24f, .55f, -.24f),
        VelocityMax = new Vector3(.24f, 1.35f, .24f),
        Acceleration = new Vector3(0, .35f, 0),
        SizeCurve = new PresentationParticleScalarKey[]
        {
            new() { Age = 0, Value = .048f },
            new() { Age = .45f, Value = .034f },
            new() { Age = 1, Value = 0 },
        },
        ColorCurve = new PresentationParticleColorKey[]
        {
            new() { Age = 0, Color = new Color(1, .86f, .32f, 1) },
            new() { Age = .45f, Color = new Color(1, .38f, .06f, .95f) },
            new() { Age = 1, Color = new Color(.85f, .14f, .02f, 0) },
        },
        Seed = seed,
    };

    /// <summary>
    /// Sculpted wrought-iron wall torch: chamfered wall escutcheon plate, horizontal arm + diagonal lower brace,
    /// tapered octagonal vertical shaft with finial, flared octagonal brazier bowl, rim collar and 4 cage prongs.
    /// </summary>
    private static void IronTorch(MeshBuilder mesh, SconceDefinition sconce, Vector2 wall, Vector2 into, Vector2 across)
    {
        int slot = LevelKit.IronSlot;
        float top = sconce.Height, d = sconce.BracketDepth;
        Vector3 At(float s, float t, float y) => OpenGround.Gltf(wall + across * s + into * t, y);

        // Wall mounting backplate
        Box(mesh, slot, At(0, .02f, top - .24f), across, into, .08f, .025f, .22f);
        // Horizontal bracket arm out from wall to torch shaft
        Box(mesh, slot, At(0, d * .5f, top - .16f), across, into, .03f, d * .5f, .03f);
        // Diagonal brace strut from lower backplate up to torch collar
        Strut(mesh, slot, At(0, .03f, top - .42f), At(0, d - .04f, top - .17f), .024f);
        // Vertical tapered iron handle/shaft descending to a pointed finial
        Vector3 shaftCenter = OpenGround.Gltf(wall + into * d, 0);
        TaperRing(mesh, slot, shaftCenter, top - .74f, top - .64f, .012f, .032f, 6, capBottom: true);
        TaperRing(mesh, slot, shaftCenter, top - .64f, top - .10f, .032f, .052f, 8);
        // Decorative iron collar ring below brazier bowl
        TaperRing(mesh, slot, shaftCenter, top - .12f, top - .07f, .072f, .072f, 8, capBottom: true, capTop: true);
        // Flared octagonal brazier bowl + heavy upper rim
        TaperRing(mesh, slot, shaftCenter, top - .08f, top + .07f, .062f, .155f, 8, capTop: true);
        TaperRing(mesh, slot, shaftCenter, top + .04f, top + .09f, .165f, .168f, 8, capBottom: true, capTop: true);
        // Four upward-splayed iron cage prongs cradling the flame
        for (int i = 0; i < 4; i++)
        {
            float a = MathF.PI * .25f + i * MathF.PI * .5f;
            Vector3 dir = new(MathF.Cos(a), 0, MathF.Sin(a));
            Vector3 p0 = shaftCenter + new Vector3(0, top + .06f, 0) + dir * .14f;
            Vector3 p1 = shaftCenter + new Vector3(0, top + .20f, 0) + dir * .19f;
            Vector3 p2 = shaftCenter + new Vector3(0, top + .27f, 0) + dir * .16f;
            Strut(mesh, slot, p0, p1, .015f);
            Strut(mesh, slot, p1, p2, .011f);
        }
    }

    /// <summary>
    /// Stylized two-tone comic flame: bright golden-cream central core tongue nestled inside 5 asymmetric curving
    /// fiery-orange licking outer tongues so the silhouette reads as hand-inked comic fire from every angle.
    /// </summary>
    private static void ComicFlame(MeshBuilder mesh, Vector3 baseCenter, float size, Random random)
    {
        // Bright golden-yellow inner flame heart (compact so fiery-orange outer tongues frame its silhouette cleanly)
        FlameTongue(mesh, LevelKit.FlameCoreSlot, baseCenter + Vector3.UnitY * .02f, size * .18f, size * .62f,
            new Vector3((float)(random.NextDouble() - .5) * .03f, 0, (float)(random.NextDouble() - .5) * .03f), 6);
        // Five asymmetric outer fiery-orange flame tongues licking upward and outward
        for (int i = 0; i < 5; i++)
        {
            float angle = i * MathF.Tau / 5f + (float)(random.NextDouble() * .45f);
            float radial = size * (.13f + (float)random.NextDouble() * .07f);
            Vector3 offset = new(MathF.Cos(angle) * radial, 0, MathF.Sin(angle) * radial);
            float h = size * (.88f + (float)random.NextDouble() * .58f);
            float r = size * (.14f + (float)random.NextDouble() * .06f);
            Vector3 lean = new(MathF.Cos(angle + .45f) * size * .22f, 0, MathF.Sin(angle + .45f) * size * .22f);
            FlameTongue(mesh, LevelKit.FlameSlot, baseCenter + offset, r, h, lean, 5);
        }
    }

    /// <summary>A tapered, curving flame tongue with wide belly and sharp pointed tip.</summary>
    internal static void FlameTongue(MeshBuilder mesh, int slot, Vector3 root, float radius, float height, Vector3 lean, int sides)
    {
        Vector3 c0 = root;
        Vector3 c1 = root + Vector3.UnitY * (height * .32f) + lean * .25f;
        Vector3 c2 = root + Vector3.UnitY * (height * .68f) + lean * .65f;
        Vector3 tip = root + Vector3.UnitY * height + lean;
        float r0 = radius * .65f, r1 = radius, r2 = radius * .48f;
        Vector3[] ring0 = Ring(c0, r0, sides), ring1 = Ring(c1, r1, sides), ring2 = Ring(c2, r2, sides);
        for (int i = 0; i < sides; i++)
        {
            int next = (i + 1) % sides;
            Vector3 f0 = (ring0[i] + ring0[next]) * .5f - c0;
            Vector3 f1 = (ring1[i] + ring1[next]) * .5f - c1;
            Vector3 f2 = (ring2[i] + ring2[next]) * .5f - c2;
            mesh.Face(slot, ring0[i], ring0[next], ring1[next], ring1[i], Vector2.Zero, Vector2.UnitX, Vector2.One, Vector2.UnitY, f0);
            mesh.Face(slot, ring1[i], ring1[next], ring2[next], ring2[i], Vector2.Zero, Vector2.UnitX, Vector2.One, Vector2.UnitY, f1);
            mesh.Triangle(slot, mesh.Vertex(ring2[i], Vector2.Zero), mesh.Vertex(ring2[next], Vector2.UnitX), mesh.Vertex(tip, Vector2.One), f2 + Vector3.UnitY * .3f);
        }
    }

    internal static Vector3[] Ring(Vector3 center, float radius, int sides)
    {
        Vector3[] pts = new Vector3[sides];
        for (int i = 0; i < sides; i++)
        {
            float a = i * MathF.Tau / sides;
            pts[i] = center + new Vector3(MathF.Cos(a) * radius, 0, MathF.Sin(a) * radius);
        }
        return pts;
    }

    internal static void TaperRing(MeshBuilder mesh, int slot, Vector3 baseXZ, float y0, float y1, float r0, float r1, int sides,
        bool capBottom = false, bool capTop = false)
    {
        Vector3 c0 = new(baseXZ.X, y0, baseXZ.Z), c1 = new(baseXZ.X, y1, baseXZ.Z);
        Vector3[] bot = Ring(c0, r0, sides), top = Ring(c1, r1, sides);
        for (int i = 0; i < sides; i++)
        {
            int next = (i + 1) % sides;
            Vector3 outward = (bot[i] + bot[next]) * .5f - c0;
            mesh.Face(slot, bot[i], bot[next], top[next], top[i], Vector2.Zero, Vector2.UnitX, Vector2.One, Vector2.UnitY, outward);
            if (capTop) mesh.Triangle(slot, mesh.Vertex(c1, Vector2.Zero), mesh.Vertex(top[i], Vector2.UnitX), mesh.Vertex(top[next], Vector2.One), Vector3.UnitY);
            if (capBottom) mesh.Triangle(slot, mesh.Vertex(c0, Vector2.Zero), mesh.Vertex(bot[next], Vector2.UnitX), mesh.Vertex(bot[i], Vector2.One), -Vector3.UnitY);
        }
    }

    internal static void Strut(MeshBuilder mesh, int slot, Vector3 a, Vector3 b, float half)
    {
        Vector3 axis = b - a;
        if (axis.LengthSquared() < 1e-6f) return;
        axis = Vector3.Normalize(axis);
        Vector3 refUp = MathF.Abs(axis.Y) > .9f ? Vector3.UnitX : Vector3.UnitY;
        Vector3 u = Vector3.Normalize(Vector3.Cross(axis, refUp)) * half;
        Vector3 v = Vector3.Normalize(Vector3.Cross(axis, u)) * half;
        Vector3[] ra = [a - u - v, a + u - v, a + u + v, a - u + v];
        Vector3[] rb = [b - u - v, b + u - v, b + u + v, b - u + v];
        for (int i = 0; i < 4; i++)
        {
            int next = (i + 1) % 4;
            Vector3 outward = (ra[i] + ra[next]) * .5f - a;
            mesh.Face(slot, ra[i], ra[next], rb[next], rb[i], Vector2.Zero, Vector2.UnitX, Vector2.One, Vector2.UnitY, outward);
        }
    }

    private static void Box(MeshBuilder mesh, int slot, Vector3 center, Vector2 across, Vector2 into, float hx, float hd, float hy)
    {
        Vector3 vx = OpenGround.Gltf(across * hx, 0), vd = OpenGround.Gltf(into * hd, 0), vy = Vector3.UnitY * hy;
        Vector3[] b = [center - vx - vd - vy, center + vx - vd - vy, center + vx + vd - vy, center - vx + vd - vy];
        Vector3[] t = [center - vx - vd + vy, center + vx - vd + vy, center + vx + vd + vy, center - vx + vd + vy];
        for (int i = 0; i < 4; i++)
        {
            int next = (i + 1) % 4;
            Vector3 outward = (b[i] + b[next]) * .5f - (center - vy);
            mesh.Face(slot, b[i], b[next], t[next], t[i], Vector2.Zero, Vector2.UnitX, Vector2.One, Vector2.UnitY, outward);
        }
        mesh.Face(slot, t[0], t[1], t[2], t[3], Vector2.Zero, Vector2.UnitX, Vector2.One, Vector2.UnitY, Vector3.UnitY);
        mesh.Face(slot, b[0], b[1], b[2], b[3], Vector2.Zero, Vector2.UnitX, Vector2.One, Vector2.UnitY, -Vector3.UnitY);
    }
}
