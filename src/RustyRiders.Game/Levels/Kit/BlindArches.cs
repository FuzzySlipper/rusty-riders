using System.Numerics;

namespace RustyRiders.Game.Levels.Kit;

/// <summary>
/// The kit's blind arches: in the bay between each two neighbouring pilasters of a room wall, a panel of the
/// <see cref="BlindArchDefinition.Panel"/> material just proud of the wall, straight-sided from the sill to the springing
/// and round above it, framed by a ring that stands out from the wall round its sides and head (smooth, or laid in
/// voussoirs), with a sill under it.
/// A bay with an opening behind it takes none. They do not collide.
/// </summary>
internal static class BlindArches
{
    private const float PanelProud = .02f; // the panel's offset from the wall face

    /// <param name="behind">Whether an opening lies behind the wall between two points on it (the pilasters' test).</param>
    /// <param name="voussoirs">Counts the voussoirs of their rings.</param>
    internal static int Build(MeshBuilder mesh, KitRecipe recipe, BlindArchDefinition arch, PilasterDefinition pilasters,
        IEnumerable<PilasterPlace> places, Func<Vector2, Vector2, Vector2, bool> behind, int seed, ref int voussoirs)
    {
        int built = 0;
        foreach (IGrouping<int, PilasterPlace> run in places.GroupBy(p => p.Run))
        {
            PilasterPlace[] ordered = run.OrderBy(p => p.Index).ToArray();
            for (int i = 0; i + 1 < ordered.Length; i++)
            {
                (PilasterPlace a, PilasterPlace b) = (ordered[i], ordered[i + 1]);
                if (b.Index != a.Index + 1) continue; // a pilaster left out between them: an opening
                float r = (Vector2.Distance(a.Centre, b.Centre) - pilasters.Width) / 2 - arch.Margin - arch.Ring.Width;
                float spring = MathF.Min(arch.Spring, a.Height - arch.TopClearance - r - arch.Ring.Width);
                if (r <= 0 || spring - arch.Sill < 1) continue;
                Vector2 middle = (a.Centre + b.Centre) / 2, reach = a.Along * (r + arch.Ring.Width);
                if (behind(middle - reach, middle + reach, a.Inward)) continue;
                voussoirs += Bay(mesh, recipe, arch, middle, a.Along, a.Inward, r, spring, new Random(unchecked(seed * 7919 + 4_000_037 + built * 104729)));
                built++;
            }
        }
        return built;
    }

    /// <returns>How many voussoirs its ring was laid in.</returns>
    private static int Bay(MeshBuilder mesh, KitRecipe recipe, BlindArchDefinition arch, Vector2 middle, Vector2 along, Vector2 inward,
        float r, float spring, Random random)
    {
        // Clear of any wall stones: the panel just proud of the deepest, the ring standing out from there.
        float proud = PanelProud + (recipe.WallStones?.Deepest ?? 0);
        float sill = arch.Sill, w = arch.Ring.Width, front = proud + arch.Ring.Depth;
        Vector3 At(float s, float y, float out_) => OpenGround.Gltf(middle + along * s + inward * out_, y);
        Vector3 toRoom = OpenGround.Gltf(inward, 0);

        // The panel: a rectangle to the springing and a fan for the head.
        int panel = LevelKit.Slot(arch.Panel);
        float pm = recipe.Metres(arch.Panel);
        Vector2 Uv(float s, float y) => new(s / pm, -y / pm);
        mesh.Face(panel, At(-r, sill, proud), At(r, sill, proud), At(r, spring, proud), At(-r, spring, proud),
            Uv(-r, sill), Uv(r, sill), Uv(r, spring), Uv(-r, spring), toRoom);
        (float S, float Y)[] head = Arc(r, spring, arch.Segments);
        uint hub = mesh.Vertex(At(0, spring, proud), Uv(0, spring));
        for (int k = 0; k < arch.Segments; k++)
            mesh.Triangle(panel, hub, mesh.Vertex(At(head[k].S, head[k].Y, proud), Uv(head[k].S, head[k].Y)),
                mesh.Vertex(At(head[k + 1].S, head[k + 1].Y, proud), Uv(head[k + 1].S, head[k + 1].Y)), toRoom);

        // The ring: up one side, round the head and down the other, a band w wide standing front metres out; laid in
        // voussoirs over a strip of their backing just proud of the wall stones, or one smooth band.
        int ring = LevelKit.Slot(arch.Ring.Surface), laid = 0;
        float rm = recipe.Metres(arch.Ring.Surface), around = 0;
        (float S, float Y)[] inner = [(-r, sill), .. head, (r, sill)];
        (float S, float Y)[] outer = [(-r - w, sill), .. Arc(r + w, spring, arch.Segments), (r + w, sill)];
        if (arch.Ring.Stones is { } v)
        {
            VoussoirBand band = new(inner.Select(p => At(p.S, p.Y, 0)).ToArray(), outer.Select(p => At(p.S, p.Y, 0)).ToArray(), w);
            band.Strip(mesh, LevelKit.Slot(v.Backing), recipe.Metres(v.Backing), toRoom, proud, 0);
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
            // Its edges face away from (outer) and toward (inner) the bay's middle at this height.
            float middleY = Math.Clamp((y0 + y1) / 2, sill, spring);
            Vector3 away = At((o0 + o1) / 2, (z0 + z1) / 2, 0) - At(0, middleY, 0);
            mesh.Face(ring, At(o0, z0, 0), At(o1, z1, 0), At(o1, z1, front), At(o0, z0, front),
                new Vector2(u0, 0), new Vector2(u1, 0), new Vector2(u1, front / rm), new Vector2(u0, front / rm), away);
            mesh.Face(ring, At(s0, y0, 0), At(s1, y1, 0), At(s1, y1, front), At(s0, y0, front),
                new Vector2(u0, 0), new Vector2(u1, 0), new Vector2(u1, front / rm), new Vector2(u0, front / rm), -away);
            around += length;
        }

        // The sill: a block under the bay, w deep, as wide as the ring.
        float wide = r + w, bottom = sill - w;
        mesh.Face(ring, At(-wide, bottom, front), At(wide, bottom, front), At(wide, sill, front), At(-wide, sill, front),
            new Vector2(-wide / rm, -bottom / rm), new Vector2(wide / rm, -bottom / rm), new Vector2(wide / rm, -sill / rm), new Vector2(-wide / rm, -sill / rm), toRoom);
        mesh.Face(ring, At(-wide, sill, 0), At(wide, sill, 0), At(wide, sill, front), At(-wide, sill, front),
            new Vector2(-wide / rm, 0), new Vector2(wide / rm, 0), new Vector2(wide / rm, front / rm), new Vector2(-wide / rm, front / rm), Vector3.UnitY);
        mesh.Face(ring, At(-wide, bottom, 0), At(wide, bottom, 0), At(wide, bottom, front), At(-wide, bottom, front),
            new Vector2(-wide / rm, 0), new Vector2(wide / rm, 0), new Vector2(wide / rm, front / rm), new Vector2(-wide / rm, front / rm), -Vector3.UnitY);
        foreach (float end in (float[])[-wide, wide])
            mesh.Face(ring, At(end, bottom, 0), At(end, sill, 0), At(end, sill, front), At(end, bottom, front),
                Vector2.Zero, new Vector2(0, w / rm), new Vector2(front / rm, w / rm), new Vector2(front / rm, 0), OpenGround.Gltf(along * MathF.Sign(end), 0));
        return laid;
    }

    /// <summary>A round head of radius <paramref name="r"/> from one springing to the other, <paramref name="segments"/> pieces.</summary>
    private static (float S, float Y)[] Arc(float r, float spring, int segments) => Enumerable.Range(0, segments + 1).Select(k =>
    {
        float angle = MathF.PI * (1 - k / (float)segments);
        return (r * MathF.Cos(angle), spring + r * MathF.Sin(angle));
    }).ToArray();
}
