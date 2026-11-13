using System.Numerics;

namespace RustyRiders.Game.Levels.Kit;

/// <summary>
/// Where a stone's outline lies: <see cref="On"/> takes a point of the outline (metres, x along and y up the surface it
/// is laid on) to its place on that surface, and <see cref="Out"/> is the way its face stands out. A wall's frame is
/// flat; an arch's or rib's follows the curve (<see cref="VoussoirBand"/>).
/// </summary>
internal readonly record struct StoneFrame(Func<Vector2, Vector3> On, Vector3 Out);

/// <summary>
/// One stone's shape, shared by wall stones and voussoirs: its kind's irregular outline (chipped corners, nicked
/// edges) run out from its surface to its relief, bevelled in to a face that bulges to a peak and stands a little
/// unevenly round its edge; a hewn face is flat facets, any other smooth.
/// </summary>
internal static partial class StoneShape
{
    /// <summary>
    /// A stone's outline, anticlockwise from its bottom-left: each corner of the rectangle chipped off by chance, and
    /// each edge by chance nicked with a shallow notch. Chips and nicks stay within a third of the stone's smaller side.
    /// </summary>
    internal static List<Vector2> Outline(StoneKind kind, Random random, float x0, float x1, float y0, float y1)
    {
        Vector2[] corners = [new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1)];
        float most = MathF.Min(kind.Chip, MathF.Min(x1 - x0, y1 - y0) / 3);
        List<Vector2> rim = [];
        for (int c = 0; c < 4; c++)
        {
            Vector2 corner = corners[c], before = corners[(c + 3) % 4], after = corners[(c + 1) % 4];
            if (most > 0 && random.NextDouble() < kind.ChipChance)
            {
                rim.Add(corner + Vector2.Normalize(before - corner) * Between(random, most * .3f, most));
                rim.Add(corner + Vector2.Normalize(after - corner) * Between(random, most * .3f, most));
            }
            else rim.Add(corner);
            if (most > 0 && random.NextDouble() < kind.Nicks)
            {
                // A nick partway along the edge to the next corner, cut into the stone.
                Vector2 edge = after - corner, dir = Vector2.Normalize(edge), into = new(-dir.Y, dir.X);
                float length = edge.Length(), width = Between(random, most * .4f, most), at = Between(random, .25f, .75f) * length;
                if (width < length / 3)
                {
                    rim.Add(corner + dir * (at - width / 2));
                    rim.Add(corner + dir * at + into * Between(random, most * .2f, most * .5f));
                    rim.Add(corner + dir * (at + width / 2));
                }
            }
        }
        return rim;
    }

    /// <summary>
    /// Builds a stone of outline <paramref name="rim"/> (from <see cref="Outline"/>) in <paramref name="frame"/>, its face
    /// <paramref name="relief"/> out. With a <paramref name="back"/>, it is two-faced: it runs that far the other way
    /// too, to a second face (a rib's stone, seen from both sides). With a <paramref name="box"/> (its rectangle, laid
    /// as the rim is) and <paramref name="faces"/>, it is that plain box instead (or its rim, with the faces' mesh
    /// outlines), its face left to the shader (<see cref="Faced"/>).
    /// </summary>
    internal static void Build(MeshBuilder mesh, int slot, float metres, float bevel, StoneKind kind, Random random, StoneFrame frame,
        List<Vector2> rim, float relief, Vector2 patch, float back = 0, StoneFacesDefinition? faces = null, List<Vector2>? box = null)
    {
        if (faces is not null && box is not null)
        {
            // Draw what the carved faces would, so a level lays the same stones either way.
            for (int i = 0; i < (back > 0 ? 2 : 1) * (rim.Count + (kind.Hewn ? 2 : 0)); i++) random.NextDouble();
            Faced(mesh, slot, metres, bevel, kind, frame, faces.MeshOutlines ? rim : box, !faces.MeshOutlines, relief, patch, back);
            return;
        }
        float width = rim.Max(p => p.X) - rim.Min(p => p.X), height = rim.Max(p => p.Y) - rim.Min(p => p.Y);
        float b = MathF.Min(bevel, MathF.Min(relief / 2, MathF.Min(width / 4, height / 4)));
        if (back > 0) b = MathF.Min(b, back / 2);
        float front = relief - b, rear = back > 0 ? b - back : 0; // where the sides meet the bevels
        Vector3 At(Vector2 p, float d) => frame.On(p) + frame.Out * d;
        Vector2 offset = patch * metres; // each stone shows its own patch of the texture
        Vector2 Uv(Vector2 p) => new((offset.X + p.X) / metres, -(offset.Y + p.Y) / metres);

        Vector2 middle = rim.Aggregate(Vector2.Zero, (sum, p) => sum + p) / rim.Count;
        Vector2[] face = rim.Select(p => p + Vector2.Normalize(middle - p) * b * 1.4f).ToArray();
        int n = rim.Count;
        Vector3[] outward = new Vector3[n];
        float around = 0;
        for (int i = 0; i < n; i++)
        {
            Vector2 p = rim[i], q = rim[(i + 1) % n], d = q - p, half = (p + q) / 2;
            // The outline runs anticlockwise, so (d.Y, -d.X) points out of it; the frame carries that onto its surface.
            outward[i] = Vector3.Normalize(frame.On(half + Vector2.Normalize(new Vector2(d.Y, -d.X)) * .01f) - frame.On(half));
            float l0 = around, l1 = around + d.Length();
            mesh.Face(slot, At(p, rear), At(q, rear), At(q, front), At(p, front),
                new Vector2((offset.X + l0) / metres, rear / metres), new Vector2((offset.X + l1) / metres, rear / metres),
                new Vector2((offset.X + l1) / metres, front / metres), new Vector2((offset.X + l0) / metres, front / metres), outward[i]);
            around = l1;
        }
        Cap(1, relief, front);
        if (back > 0) Cap(-1, back, -rear);

        // A face on one side, <paramref name="sign"/>: its bevel from the sides' edge, then a fan from its peak, bulged by the
        // kind's pillow; a hewn face's peak sits off the middle, so its facets differ.
        void Cap(float sign, float r, float edge)
        {
            Vector3 out_ = frame.Out * sign;
            float[] lift = rim.Select(_ => sign * (r + Between(random, -kind.Facet, kind.Facet))).ToArray();
            if (b > 0)
                for (int i = 0; i < n; i++)
                    mesh.Face(slot, At(rim[i], sign * edge), At(rim[(i + 1) % n], sign * edge), At(face[(i + 1) % n], lift[(i + 1) % n]), At(face[i], lift[i]),
                        Uv(rim[i]), Uv(rim[(i + 1) % n]), Uv(face[(i + 1) % n]), Uv(face[i]), outward[i] + out_);
            Vector2 peak = kind.Hewn ? middle + new Vector2(Between(random, -.25f, .25f) * width, Between(random, -.25f, .25f) * height) : middle;
            Vector3 top = At(peak, sign * (r + kind.Pillow));
            if (kind.Hewn)
                for (int i = 0; i < n; i++)
                    mesh.Triangle(slot, mesh.Vertex(top, Uv(peak)), mesh.Vertex(At(face[i], lift[i]), Uv(face[i])),
                        mesh.Vertex(At(face[(i + 1) % n], lift[(i + 1) % n]), Uv(face[(i + 1) % n])), out_);
            else
            {
                uint hub = mesh.Vertex(top, Uv(middle));
                uint[] ring = Enumerable.Range(0, n).Select(i => mesh.Vertex(At(face[i], lift[i]), Uv(face[i]))).ToArray();
                for (int i = 0; i < n; i++) mesh.Triangle(slot, hub, ring[i], ring[(i + 1) % n], out_);
            }
        }
    }

    internal static StoneKind Pick(Random random, StoneKind[] kinds, float total)
    {
        double pick = random.NextDouble() * total;
        foreach (StoneKind kind in kinds)
            if ((pick -= kind.Weight) < 0) return kind;
        return kinds[^1];
    }

    internal static float Between(Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);
}
