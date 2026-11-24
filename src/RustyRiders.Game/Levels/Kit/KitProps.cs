using System.Numerics;
using Rusty.Engine;

namespace RustyRiders.Game.Levels.Kit;

/// <summary>
/// Kit-native grounded storytelling dressing: slate-matched angular rubble chunks at the foot of walls and plinths,
/// sparse Direction B alcove vignettes on recessed alcove shelves (votive candles and vessels, draped terracotta cloth
/// with leaned iron spears, iron-banded stone reliquaries), and occasional doorway threshold relics. Built directly into
/// the kit's banded mesh so every piece shares Direction B materials, comic shading, and outlines at zero extra draw calls.
/// </summary>
internal static class KitProps
{
    internal static int Build(MeshBuilder mesh, RoomPlan plan, KitRecipe recipe, KitPropsDefinition props,
        IReadOnlyList<BlindArchBay> bays, List<KitLight> lights, int seed)
    {
        int placed = 0;
        if (props.Rubble is { } rubble)
            placed += BuildRubble(mesh, plan, recipe, rubble, new Random(unchecked(seed * 7919 + 7_000_003)));
        if (props.Alcoves is { } alcoves)
            placed += BuildAlcoves(mesh, recipe, alcoves, bays, lights, new Random(unchecked(seed * 7919 + 8_000_009)));
        if (props.Thresholds is { } thresholds)
            placed += BuildThresholds(mesh, plan, recipe, thresholds, new Random(unchecked(seed * 7919 + 9_000_011)));
        return placed;
    }

    /// <summary>Faceted slate-matched stone chunks clustered sparsely along wall curbs and column plinths.</summary>
    private static int BuildRubble(MeshBuilder mesh, RoomPlan plan, KitRecipe recipe, RubblePropsDefinition rubble, Random random)
    {
        int count = 0;
        float ledgeOut = recipe.Trims.Where(t => t.Anchor == TrimAnchor.Floor && t.Collides)
            .Select(t => t.Profile.Max(p => p[0])).DefaultIfEmpty(0).Max();
        float runnerHalf = (recipe.Runners?.Width ?? 0) / 2 + .65f;
        Dictionary<(int X, int Z), float> ground = plan.Heights([PlanArea.Room, PlanArea.Corridor, PlanArea.Doorway]);

        bool ClearOfDoorAndRunner(Vector2 at)
        {
            foreach (PlanDoorway d in plan.Doorways)
            {
                Vector2 rel = at - d.Centre;
                float along = Vector2.Dot(rel, d.IntoRoom);
                float side = MathF.Abs(Vector2.Dot(rel, new Vector2(-d.IntoRoom.Y, d.IntoRoom.X)));
                if (along >= -.5f && along <= plan.Rules.WallMetres + 1.5f && side < plan.Rules.DoorWidth / 2 + .8f) return false;
                if (along > 0 && along < 12f && side < runnerHalf) return false;
            }
            return true;
        }

        foreach (List<(Vector2 Point, float Height)> loop in OpenGround.Walls(ground))
        {
            for (int i = 0; i < loop.Count; i++)
            {
                PlanArea area = plan.Rules.Area(loop[i].Height);
                if (area == PlanArea.Doorway) continue;
                Vector2 a = plan.Metres(loop[i].Point), b = plan.Metres(loop[(i + 1) % loop.Count].Point);
                float length = Vector2.Distance(a, b);
                if (length < 2.6f || random.NextDouble() >= rubble.Chance) continue;
                Vector2 along = Vector2.Normalize(b - a), inward = new(-along.Y, along.X);
                float t = 1.1f + (float)random.NextDouble() * (length - 2.2f);
                Vector2 clusterCenter = a + along * t + inward * (ledgeOut + .22f);
                if (!ClearOfDoorAndRunner(clusterCenter)) continue;
                int pieces = 2 + random.Next(rubble.MaxCluster);
                for (int k = 0; k < pieces; k++)
                {
                    float spread = k == 0 ? 0 : .15f + (float)random.NextDouble() * .55f;
                    Vector2 at = clusterCenter + along * ((float)(random.NextDouble() - .5) * spread * 2)
                        + inward * ((float)random.NextDouble() * .55f);
                    if (!plan.Open.ContainsKey(plan.Square(at))) continue;
                    float size = k == 0
                        ? rubble.MinSize + (float)random.NextDouble() * (rubble.MaxSize - rubble.MinSize)
                        : rubble.MinSize * (.45f + (float)random.NextDouble() * .65f);
                    int slot = random.NextDouble() < .65 ? LevelKit.WallSlot : LevelKit.FloorSlot;
                    StoneChunk(mesh, slot, OpenGround.Gltf(at, 0), size, random);
                    count++;
                }
            }
        }
        // Also scatter occasional grounded slate boulders beside free-standing hall column plinths.
        if (recipe.Columns?.Hall is { } hallCol)
        {
            float baseHalf = hallCol.Footprint / 2 + .22f;
            foreach (Vector2 col in KitDressing.HallColumnPlaces(plan))
            {
                if (random.NextDouble() >= rubble.Chance) continue;
                float angle = (float)(random.NextDouble() * MathF.Tau);
                Vector2 center = col + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (baseHalf + .18f);
                if (!plan.Open.ContainsKey(plan.Square(center)) || !ClearOfDoorAndRunner(center)) continue;
                int pieces = 2 + random.Next(Math.Max(1, rubble.MaxCluster - 1));
                for (int k = 0; k < pieces; k++)
                {
                    Vector2 at = center + new Vector2((float)(random.NextDouble() - .5) * .6f, (float)(random.NextDouble() - .5) * .6f);
                    if (!plan.Open.ContainsKey(plan.Square(at))) continue;
                    float size = k == 0 ? rubble.MinSize + (float)random.NextDouble() * (rubble.MaxSize - rubble.MinSize) : rubble.MinSize * .6f;
                    StoneChunk(mesh, LevelKit.WallSlot, OpenGround.Gltf(at, 0), size, random);
                    count++;
                }
            }
        }
        return count;
    }

    /// <summary>Direction B storytelling vignettes sitting on recessed alcove shelves.</summary>
    private static int BuildAlcoves(MeshBuilder mesh, KitRecipe recipe, AlcovePropsDefinition alcoves,
        IReadOnlyList<BlindArchBay> bays, List<KitLight> lights, Random random)
    {
        int placed = 0;
        foreach (BlindArchBay bay in bays)
        {
            if (bay.Recess <= 0 || random.NextDouble() >= alcoves.Chance) continue;
            Vector3 At(float s, float out_, float y) => OpenGround.Gltf(bay.Middle + bay.Along * s + bay.Inward * out_, bay.Sill + y);
            int kind = random.Next(3);
            if (kind == 0)
            {
                // Votive candles and carved stone ritual vessels on the shelf
                float baseS = (float)(random.NextDouble() - .5) * bay.Radius * .3f;
                Vessel(mesh, LevelKit.WallSlot, At(baseS - .44f, -bay.Recess * .42f, 0), .16f, .54f);
                Vessel(mesh, LevelKit.TrimSlot, At(baseS - .12f, -bay.Recess * .55f, 0), .13f, .40f);
                for (int c = 0; c < 4; c++)
                {
                    float cs = baseS + .16f + c * .15f + (float)(random.NextDouble() - .5) * .04f;
                    float co = -bay.Recess * (.22f + (float)random.NextDouble() * .26f);
                    float ch = .18f + (float)random.NextDouble() * .20f;
                    Vector3 candleBase = At(cs, co, 0);
                    Sconces.TaperRing(mesh, LevelKit.WallSlot, candleBase, candleBase.Y, candleBase.Y + ch, .048f, .042f, 6, capTop: true);
                    Sconces.FlameTongue(mesh, LevelKit.FlameCoreSlot, candleBase + Vector3.UnitY * ch, .034f, .12f, Vector3.Zero, 4);
                }
                if (alcoves.CandleLightIntensity > 0)
                {
                    Vector3 glowAt = At(baseS + .22f, -bay.Recess * .22f, .46f);
                    lights.Add(new KitLight(new LightDescriptor(LightKind.Point, new Vector3(1f, .56f, .20f), alcoves.CandleLightIntensity, true,
                        glowAt, -Vector3.UnitY, true, alcoves.CandleLightRange, 1.65f, 0, 0, LightShadowIntent.Disabled), .06f, (float)(random.NextDouble() * MathF.Tau)));
                }
            }
            else if (kind == 1)
            {
                // Draped terracotta-crimson cloth/banner over the shelf lip + leaned wrought-iron spear
                DrapedCloth(mesh, recipe, bay, (float)(random.NextDouble() - .5) * bay.Radius * .25f);
                float side = random.NextDouble() < .5 ? -1 : 1;
                Vector3 butt = At(side * bay.Radius * .12f, -bay.Recess * .46f, 0);
                Vector3 tip = At(side * bay.Radius * .70f, -bay.Recess * .32f, MathF.Min(2.55f, (bay.Spring - bay.Sill) * .92f));
                Spear(mesh, butt, tip);
            }
            else
            {
                // Iron-banded stone reliquary chest + scattered stone shards
                Reliquary(mesh, bay, (float)(random.NextDouble() - .5) * bay.Radius * .3f);
                StoneChunk(mesh, LevelKit.WallSlot, At(bay.Radius * .48f, -bay.Recess * .42f, 0), .18f, random);
                StoneChunk(mesh, LevelKit.WallSlot, At(-bay.Radius * .52f, -bay.Recess * .36f, 0), .14f, random);
            }
            placed++;
        }
        return placed;
    }

    /// <summary>Sparse doorway threshold scale anchor tucked beside a room doorway curb (planted relic blade or urns).</summary>
    private static int BuildThresholds(MeshBuilder mesh, RoomPlan plan, KitRecipe recipe, ThresholdPropsDefinition thresholds, Random random)
    {
        int placed = 0;
        float ledgeOut = recipe.Trims.Where(t => t.Anchor == TrimAnchor.Floor && t.Collides)
            .Select(t => t.Profile.Max(p => p[0])).DefaultIfEmpty(0).Max();
        float ledgeHeight = recipe.Trims.Where(t => t.Anchor == TrimAnchor.Floor && t.Areas.Contains(PlanArea.Room))
            .Select(t => t.Profile.Max(p => p[1])).DefaultIfEmpty(0).Max();
        float probe = plan.Rules.GridMetres / 2;
        bool Room(Vector2 at) => plan.Open.TryGetValue(plan.Square(at), out PlanArea area) && area == PlanArea.Room;

        foreach (PlanDoorway doorway in plan.Doorways)
        {
            if (random.NextDouble() >= thresholds.Chance) continue;
            Vector2 into = doorway.IntoRoom, across = new(-into.Y, into.X), face = doorway.Centre + into * plan.Rules.WallMetres;
            float firstSide = random.NextDouble() < .5 ? -1 : 1;
            float offset = plan.Rules.DoorWidth / 2 + 1.45f;
            foreach (float side in (float[])[firstSide, -firstSide])
            {
                Vector2 wallPos = face + across * (side * offset);
                Vector2 floorPos = wallPos + into * (ledgeOut + .28f);
                if (!Room(floorPos) || !Room(floorPos + across * (side * .6f)) || plan.Open.ContainsKey(plan.Square(wallPos - into * probe))) continue;
                if (random.NextDouble() < .68)
                {
                    // Upright forged-iron relic blade planted in a low stone socket with a draped terracotta cloth
                    Vector3 basePos = OpenGround.Gltf(floorPos, 0);
                    StoneChunk(mesh, LevelKit.WallSlot, basePos, .30f, random);
                    RelicBlade(mesh, basePos + Vector3.UnitY * .08f, across, into);
                }
                else
                {
                    // Carved stone & terracotta urns resting on the curb ledge beside the doorway
                    Vector3 onLedge = OpenGround.Gltf(wallPos + into * (ledgeOut * .5f), ledgeHeight);
                    Vessel(mesh, LevelKit.TrimSlot, onLedge, .15f, .50f);
                    Vessel(mesh, LevelKit.WallSlot, onLedge + OpenGround.Gltf(across * (side * .34f), 0), .12f, .36f);
                }
                placed++;
                break;
            }
        }
        return placed;
    }

    /// <summary>An angular faceted stone rubble chunk grounded on the floor.</summary>
    private static void StoneChunk(MeshBuilder mesh, int slot, Vector3 basePos, float size, Random random)
    {
        int sides = 5 + random.Next(3);
        float height = size * (.42f + (float)random.NextDouble() * .35f);
        float yaw = (float)(random.NextDouble() * MathF.Tau);
        Vector3[] bot = new Vector3[sides], top = new Vector3[sides];
        for (int i = 0; i < sides; i++)
        {
            float a = yaw + i * MathF.Tau / sides + (float)(random.NextDouble() - .5) * .28f;
            float rb = size * (.42f + (float)random.NextDouble() * .25f);
            float rt = rb * (.50f + (float)random.NextDouble() * .32f);
            float ty = height * (.75f + (float)random.NextDouble() * .35f);
            bot[i] = basePos + new Vector3(MathF.Cos(a) * rb, -.01f, MathF.Sin(a) * rb);
            top[i] = basePos + new Vector3(MathF.Cos(a + .12f) * rt, ty, MathF.Sin(a + .12f) * rt);
        }
        Vector3 capCenter = basePos + Vector3.UnitY * (height * .95f);
        for (int i = 0; i < sides; i++)
        {
            int next = (i + 1) % sides;
            Vector3 outward = (bot[i] + bot[next]) * .5f - basePos;
            mesh.Face(slot, bot[i], bot[next], top[next], top[i],
                new Vector2(0, 0), new Vector2(size * .5f, 0), new Vector2(size * .5f, height * .5f), new Vector2(0, height * .5f), outward);
            mesh.Triangle(slot, mesh.Vertex(capCenter, Vector2.Zero), mesh.Vertex(top[i], new Vector2(size * .3f, 0)),
                mesh.Vertex(top[next], new Vector2(0, size * .3f)), Vector3.UnitY);
        }
    }

    /// <summary>A tapered octagonal carved stone or terracotta ritual vessel/urn.</summary>
    private static void Vessel(MeshBuilder mesh, int slot, Vector3 basePos, float radius, float height)
    {
        Sconces.TaperRing(mesh, slot, basePos, basePos.Y, basePos.Y + height * .25f, radius * .65f, radius, 8);
        Sconces.TaperRing(mesh, slot, basePos, basePos.Y + height * .25f, basePos.Y + height * .68f, radius, radius * .82f, 8);
        Sconces.TaperRing(mesh, slot, basePos, basePos.Y + height * .68f, basePos.Y + height * .86f, radius * .82f, radius * .55f, 8);
        Sconces.TaperRing(mesh, slot, basePos, basePos.Y + height * .86f, basePos.Y + height, radius * .55f, radius * .70f, 8, capTop: true);
    }

    /// <summary>An angular draped terracotta banner/cloth lying on an alcove shelf and spilling over its front lip.</summary>
    private static void DrapedCloth(MeshBuilder mesh, KitRecipe recipe, BlindArchBay bay, float offsetS)
    {
        int slot = LevelKit.TrimSlot;
        float m = recipe.Metres(KitSurface.Trim);
        float w = MathF.Min(.55f, bay.Radius * .55f);
        Vector3 At(float s, float out_, float y) => OpenGround.Gltf(bay.Middle + bay.Along * (offsetS + s) + bay.Inward * out_, bay.Sill + y);
        Vector3 toRoom = OpenGround.Gltf(bay.Inward, 0);
        // Sculpted folded mound resting inside the alcove
        Vector3 p0 = At(-w, -bay.Recess * .72f, .02f), p1 = At(w * .8f, -bay.Recess * .65f, .02f);
        Vector3 p2 = At(w * .45f, -bay.Recess * .18f, .24f), p3 = At(-w * .65f, -bay.Recess * .28f, .18f);
        Vector3 f0 = At(-w * .8f, .25f, .02f), f1 = At(w * .7f, .25f, .02f);
        Vector3 d0 = At(-w * .68f, .27f, -.44f), d1 = At(w * .55f, .27f, -.36f);
        mesh.Face(slot, p0, p1, p2, p3, Vector2.Zero, new Vector2(w / m, 0), new Vector2(w / m, .4f / m), new Vector2(0, .4f / m), Vector3.UnitY);
        mesh.Face(slot, p3, p2, f1, f0, Vector2.Zero, new Vector2(w / m, 0), new Vector2(w / m, .4f / m), new Vector2(0, .4f / m), Vector3.UnitY + toRoom * .4f);
        // Hanging drape fold over the front edge of the stone shelf
        mesh.Face(slot, f0, f1, d1, d0, Vector2.Zero, new Vector2(w / m, 0), new Vector2(w / m, .45f / m), new Vector2(0, .45f / m), toRoom);
    }

    /// <summary>A slender forged-iron spear leaning diagonally inside a recessed alcove.</summary>
    private static void Spear(MeshBuilder mesh, Vector3 butt, Vector3 tip)
    {
        Vector3 dir = Vector3.Normalize(tip - butt);
        Vector3 collar = tip - dir * .34f;
        Sconces.Strut(mesh, LevelKit.IronSlot, butt, collar, .020f);
        // Diamond-section spearhead
        Vector3 refUp = MathF.Abs(dir.Y) > .9f ? Vector3.UnitX : Vector3.UnitY;
        Vector3 u = Vector3.Normalize(Vector3.Cross(dir, refUp)) * .065f;
        Vector3 v = Vector3.Normalize(Vector3.Cross(dir, u)) * .022f;
        Vector3 mid = collar + dir * .11f;
        Vector3[] ring = [mid - u, mid - v, mid + u, mid + v];
        for (int i = 0; i < 4; i++)
        {
            int next = (i + 1) % 4;
            Vector3 outward = (ring[i] + ring[next]) * .5f - mid;
            mesh.Triangle(LevelKit.IronSlot, mesh.Vertex(collar, Vector2.Zero), mesh.Vertex(ring[i], Vector2.UnitX), mesh.Vertex(ring[next], Vector2.One), outward);
            mesh.Triangle(LevelKit.IronSlot, mesh.Vertex(ring[i], Vector2.Zero), mesh.Vertex(ring[next], Vector2.UnitX), mesh.Vertex(tip, Vector2.One), outward);
        }
    }

    /// <summary>A low stone reliquary box bound with wrought-iron straps sitting on an alcove shelf.</summary>
    private static void Reliquary(MeshBuilder mesh, BlindArchBay bay, float offsetS)
    {
        Vector3 c = OpenGround.Gltf(bay.Middle + bay.Along * offsetS - bay.Inward * (bay.Recess * .52f), bay.Sill + .18f);
        Vector3 vx = OpenGround.Gltf(bay.Along * .38f, 0), vd = OpenGround.Gltf(bay.Inward * .20f, 0), vy = Vector3.UnitY * .18f;
        SolidBox(mesh, LevelKit.WallSlot, c, vx, vd, vy);
        // Two wrought-iron bands wrapping the chest
        foreach (float f in (float[])[-.55f, .55f])
            SolidBox(mesh, LevelKit.IronSlot, c + vx * f, OpenGround.Gltf(bay.Along * .035f, 0), vd * 1.06f, vy * 1.06f);
    }

    /// <summary>An upright forged-iron relic blade with crossguard and draped terracotta cloth beside a doorway.</summary>
    private static void RelicBlade(MeshBuilder mesh, Vector3 foot, Vector2 across, Vector2 into)
    {
        Vector3 pommel = foot + Vector3.UnitY * 1.18f;
        Vector3 guard = foot + Vector3.UnitY * .88f;
        Vector3 ax = OpenGround.Gltf(across, 0), iz = OpenGround.Gltf(into, 0);
        // Blade + grip + crossguard + pommel
        Sconces.Strut(mesh, LevelKit.IronSlot, foot, guard, .036f);
        Sconces.Strut(mesh, LevelKit.IronSlot, guard, pommel, .020f);
        Sconces.Strut(mesh, LevelKit.IronSlot, guard - ax * .18f, guard + ax * .18f, .024f);
        Sconces.TaperRing(mesh, LevelKit.IronSlot, pommel, pommel.Y - .03f, pommel.Y + .05f, .042f, .026f, 6, capTop: true);
        // Folded terracotta cloth draped across the lower blade
        Vector3 c0 = foot + Vector3.UnitY * .68f + iz * .05f - ax * .14f;
        Vector3 c1 = foot + Vector3.UnitY * .64f + iz * .05f + ax * .14f;
        Vector3 c2 = foot + Vector3.UnitY * .04f + iz * .14f + ax * .19f;
        Vector3 c3 = foot + Vector3.UnitY * .04f + iz * .14f - ax * .17f;
        mesh.Face(LevelKit.TrimSlot, c0, c1, c2, c3, Vector2.Zero, Vector2.UnitX, Vector2.One, Vector2.UnitY, iz);
        mesh.Face(LevelKit.TrimSlot, c1, c0, c3, c2, Vector2.Zero, Vector2.UnitX, Vector2.One, Vector2.UnitY, -iz);
    }

    private static void SolidBox(MeshBuilder mesh, int slot, Vector3 center, Vector3 vx, Vector3 vd, Vector3 vy)
    {
        Vector3[] b = [center - vx - vd - vy, center + vx - vd - vy, center + vx + vd - vy, center - vx + vd - vy];
        Vector3[] t = [center - vx - vd + vy, center + vx - vd + vy, center + vx + vd + vy, center - vx + vd + vy];
        for (int i = 0; i < 4; i++)
        {
            int next = (i + 1) % 4;
            Vector3 outward = (b[i] + b[next]) * .5f - (center - vy);
            mesh.Face(slot, b[i], b[next], t[next], t[i], Vector2.Zero, Vector2.UnitX, Vector2.One, Vector2.UnitY, outward);
        }
        mesh.Face(slot, t[0], t[1], t[2], t[3], Vector2.Zero, Vector2.UnitX, Vector2.One, Vector2.UnitY, Vector3.UnitY);
    }
}
