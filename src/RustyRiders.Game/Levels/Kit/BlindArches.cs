using System.Numerics;

namespace RustyRiders.Game.Levels.Kit;

/// <summary>A planned blind-arch or recessed alcove bay on a room wall.</summary>
internal readonly record struct BlindArchBay(Vector2 Middle, Vector2 Along, Vector2 Inward, float Radius, float Sill, float Spring,
    float Recess, int Segments, float Pointed)
{
    /// <summary>Whether a stone rectangle on the wall run overlaps this recessed alcove's opening.</summary>
    internal bool Overlaps(Vector2 origin, Vector2 along, Vector2 inward, float x0, float x1, float y0, float y1, float margin)
    {
        if (Recess <= 0 || Vector2.Dot(inward, Inward) < .95f) return false;
        Vector2 delta = origin - Middle;
        if (MathF.Abs(Vector2.Dot(delta, Inward)) > .15f) return false;
        float baseS = Vector2.Dot(delta, Along);
        float s0 = baseS + x0, s1 = baseS + x1;
        if (s0 > s1) (s0, s1) = (s1, s0);
        float r = Radius + margin;
        if (s1 <= -r || s0 >= r || y1 <= Sill || y0 >= Spring + r) return false;
        float closestS = Math.Clamp(0, s0, s1);
        float maxY = Spring + DoorArches.HeightAt(closestS, r, r, Pointed);
        return y0 < maxY;
    }
}

/// <summary>
/// The kit's blind arches and recessed alcoves: in the bay between each two neighbouring pilasters of a room wall, an
/// arched opening from the sill to the springing and round/pointed above it, framed by a ring (smooth or voussoirs)
/// with a sill shelf under it. When <see cref="BlindArchDefinition.Recess"/> is above zero and solid wall backs the
/// bay, the wall opens into a genuine recessed alcove with thick reveal jambs, arched soffit, recessed back panel, and
/// a deep interior shelf; otherwise it falls back to a panel proud of the wall. They do not collide.
/// </summary>
internal static class BlindArches
{
    private const float PanelProud = .02f; // the panel's offset from the wall face when not recessed

    /// <summary>Finds every valid blind-arch or recessed-alcove bay between neighbouring room pilasters.</summary>
    internal static List<BlindArchBay> Plan(RoomPlan plan, BlindArchDefinition arch, PilasterDefinition pilasters,
        IEnumerable<PilasterPlace> places, Func<Vector2, Vector2, Vector2, bool> behind, Dictionary<(int X, int Z), float> ground)
    {
        List<BlindArchBay> bays = [];
        foreach (IGrouping<int, PilasterPlace> run in places.GroupBy(p => p.Run))
        {
            PilasterPlace[] ordered = run.OrderBy(p => p.Index).ToArray();
            for (int i = 0; i + 1 < ordered.Length; i++)
            {
                (PilasterPlace a, PilasterPlace b) = (ordered[i], ordered[i + 1]);
                if (b.Index != a.Index + 1) continue; // a pilaster left out between them: an opening
                float r = (Vector2.Distance(a.Centre, b.Centre) - pilasters.BaseWidth) / 2 - arch.Margin - arch.Ring.Width;
                float spring = MathF.Min(arch.Spring, a.Height - arch.TopClearance - r - arch.Ring.Width);
                if (r <= .45f || spring - arch.Sill < 1) continue;
                Vector2 middle = (a.Centre + b.Centre) / 2, reach = a.Along * (r + arch.Ring.Width);
                if (behind(middle - reach, middle + reach, a.Inward)) continue;
                float recess = arch.Recess > 0 && SolidBehind(plan, middle, a.Along, a.Inward, r + .35f, arch.Recess + .55f, ground)
                    ? arch.Recess : 0;
                bays.Add(new BlindArchBay(middle, a.Along, a.Inward, r, arch.Sill, spring, recess, arch.Segments, arch.Pointed));
            }
        }
        return bays;
    }

    private static bool SolidBehind(RoomPlan plan, Vector2 middle, Vector2 along, Vector2 inward, float halfWidth, float depth,
        Dictionary<(int X, int Z), float> ground)
    {
        float step = plan.Rules.GridMetres * .5f;
        for (float d = step; d <= depth; d += step)
            for (float s = -halfWidth; s <= halfWidth + 1e-3f; s += step)
                if (ground.ContainsKey(plan.Square(middle + along * s - inward * d))) return false;
        return true;
    }

    /// <param name="voussoirs">Counts the voussoirs of their rings.</param>
    internal static int Build(MeshBuilder mesh, KitRecipe recipe, BlindArchDefinition arch, IReadOnlyList<BlindArchBay> bays, int seed,
        ref int voussoirs)
    {
        for (int i = 0; i < bays.Count; i++)
            voussoirs += Bay(mesh, recipe, arch, bays[i], new Random(unchecked(seed * 7919 + 4_000_037 + i * 104729)));
        return bays.Count;
    }

    /// <returns>How many voussoirs its ring was laid in.</returns>
    private static int Bay(MeshBuilder mesh, KitRecipe recipe, BlindArchDefinition arch, BlindArchBay bay, Random random)
    {
        Vector2 middle = bay.Middle, along = bay.Along, inward = bay.Inward;
        float r = bay.Radius, sill = bay.Sill, spring = bay.Spring, w = arch.Ring.Width;
        bool recessed = bay.Recess > 0;
        float wallOut = recipe.WallStones?.Deepest ?? 0;
        float panelOut = recessed ? -bay.Recess : PanelProud + wallOut;
        float ringBase = recessed ? 0 : PanelProud + wallOut;
        float front = ringBase + arch.Ring.Depth + (recessed ? wallOut : 0);
        Vector3 At(float s, float y, float out_) => OpenGround.Gltf(middle + along * s + inward * out_, y);
        Vector3 toRoom = OpenGround.Gltf(inward, 0);

        // The back panel: a rectangle to the springing and an arched fan for the head.
        int panel = LevelKit.Slot(arch.Panel);
        float pm = recipe.Metres(arch.Panel);
        Vector2 Uv(float s, float y) => new(s / pm, -y / pm);
        mesh.Face(panel, At(-r, sill, panelOut), At(r, sill, panelOut), At(r, spring, panelOut), At(-r, spring, panelOut),
            Uv(-r, sill), Uv(r, sill), Uv(r, spring), Uv(-r, spring), toRoom);
        (float S, float Y)[] head = DoorArches.Curve(r, spring, r, arch.Segments, arch.Pointed);
        uint hub = mesh.Vertex(At(0, spring, panelOut), Uv(0, spring));
        for (int k = 0; k < arch.Segments; k++)
            mesh.Triangle(panel, hub, mesh.Vertex(At(head[k].S, head[k].Y, panelOut), Uv(head[k].S, head[k].Y)),
                mesh.Vertex(At(head[k + 1].S, head[k + 1].Y, panelOut), Uv(head[k + 1].S, head[k + 1].Y)), toRoom);

        // Recessed alcove reveals: thick grey stone jambs & curved arched soffit from wall face back to -recess.
        (float S, float Y)[] inner = [(-r, sill), .. head, (r, sill)];
        if (recessed)
        {
            int revealSlot = LevelKit.Slot(KitSurface.Wall);
            float wm = recipe.Metres(KitSurface.Wall), aroundReveal = 0;
            for (int k = 0; k + 1 < inner.Length; k++)
            {
                (float s0, float y0) = inner[k];
                (float s1, float y1) = inner[k + 1];
                float len = MathF.Sqrt((s1 - s0) * (s1 - s0) + (y1 - y0) * (y1 - y0));
                float midY = Math.Clamp((y0 + y1) / 2, sill, spring);
                Vector3 towardCentre = At(0, midY, 0) - At((s0 + s1) / 2, (y0 + y1) / 2, 0);
                float u0 = aroundReveal / wm, u1 = (aroundReveal + len) / wm, vd = bay.Recess / wm;
                mesh.Face(revealSlot, At(s0, y0, panelOut), At(s1, y1, panelOut), At(s1, y1, 0), At(s0, y0, 0),
                    new Vector2(u0, 0), new Vector2(u1, 0), new Vector2(u1, vd), new Vector2(u0, vd), towardCentre);
                aroundReveal += len;
            }
        }

        // The ring: up one side, round the head and down the other, a band w wide standing front metres out.
        int ring = LevelKit.Slot(arch.Ring.Surface), laid = 0;
        float rm = recipe.Metres(arch.Ring.Surface), around = 0;
        (float S, float Y)[] outer = [(-r - w, sill), .. DoorArches.Curve(r + w, spring, r + w, arch.Segments, arch.Pointed), (r + w, sill)];
        if (arch.Ring.Stones is { } v)
        {
            VoussoirBand band = new(inner.Select(p => At(p.S, p.Y, 0)).ToArray(), outer.Select(p => At(p.S, p.Y, 0)).ToArray(), w);
            band.Strip(mesh, LevelKit.Slot(v.Backing), recipe.Metres(v.Backing), toRoom, MathF.Max(ringBase, wallOut + PanelProud), 0);
            laid = Voussoirs.Lay(mesh, recipe, v, arch.Ring.Surface, band, toRoom, front, 0, random);
        }
        else for (int k = 0; k + 1 < inner.Length; k++)
        {
            (float s0, float y0) = inner[k];
            (float s1, float y1) = inner[k + 1];
            (float o0, float z0) = outer[k];
            (float o1, float z1) = outer[k + 1];
            float length = MathF.Sqrt((s1 - s0) * (s1 - s0) + (y1 - y0) * (y1 - y0));
            float u0 = around / rm, u1 = (around + length) / rm;
            mesh.Face(ring, At(s0, y0, front), At(s1, y1, front), At(o1, z1, front), At(o0, z0, front),
                new Vector2(u0, 0), new Vector2(u1, 0), new Vector2(u1, w / rm), new Vector2(u0, w / rm), toRoom);
            float middleY = Math.Clamp((y0 + y1) / 2, sill, spring);
            Vector3 away = At((o0 + o1) / 2, (z0 + z1) / 2, 0) - At(0, middleY, 0);
            mesh.Face(ring, At(o0, z0, 0), At(o1, z1, 0), At(o1, z1, front), At(o0, z0, front),
                new Vector2(u0, 0), new Vector2(u1, 0), new Vector2(u1, front / rm), new Vector2(u0, front / rm), away);
            mesh.Face(ring, At(s0, y0, 0), At(s1, y1, 0), At(s1, y1, front), At(s0, y0, front),
                new Vector2(u0, 0), new Vector2(u1, 0), new Vector2(u1, front / rm), new Vector2(u0, front / rm), -away);
            around += length;
        }

        // The sill / interior shelf: spans from the alcove's back wall out past the ring as a carved stone ledge.
        int shelfSlot = recessed ? LevelKit.Slot(arch.ShelfSurface) : ring;
        float sm = recessed ? recipe.Metres(arch.ShelfSurface) : rm;
        float wide = r + w + (recessed ? .08f : 0);
        float shelfFront = MathF.Max(front, wallOut + arch.ShelfOut);
        float shelfBack = recessed ? panelOut : 0;
        float bottom = sill - (recessed ? arch.ShelfThickness : w);
        float shelfDepth = shelfFront - shelfBack;
        mesh.Face(shelfSlot, At(-wide, bottom, shelfFront), At(wide, bottom, shelfFront), At(wide, sill, shelfFront), At(-wide, sill, shelfFront),
            new Vector2(-wide / sm, -bottom / sm), new Vector2(wide / sm, -bottom / sm), new Vector2(wide / sm, -sill / sm), new Vector2(-wide / sm, -sill / sm), toRoom);
        mesh.Face(shelfSlot, At(-wide, sill, shelfBack), At(wide, sill, shelfBack), At(wide, sill, shelfFront), At(-wide, sill, shelfFront),
            new Vector2(-wide / sm, 0), new Vector2(wide / sm, 0), new Vector2(wide / sm, shelfDepth / sm), new Vector2(-wide / sm, shelfDepth / sm), Vector3.UnitY);
        mesh.Face(shelfSlot, At(-wide, bottom, 0), At(wide, bottom, 0), At(wide, bottom, shelfFront), At(-wide, bottom, shelfFront),
            new Vector2(-wide / sm, 0), new Vector2(wide / sm, 0), new Vector2(wide / sm, shelfFront / sm), new Vector2(-wide / sm, shelfFront / sm), -Vector3.UnitY);
        foreach (float end in (float[])[-wide, wide])
            mesh.Face(shelfSlot, At(end, bottom, 0), At(end, sill, 0), At(end, sill, shelfFront), At(end, bottom, shelfFront),
                Vector2.Zero, new Vector2(0, (sill - bottom) / sm), new Vector2(shelfFront / sm, (sill - bottom) / sm), new Vector2(shelfFront / sm, 0),
                OpenGround.Gltf(along * MathF.Sign(end), 0));
        return laid;
    }
}
