using System.Numerics;
using static RustyRiders.Game.Levels.Kit.StoneShape;

namespace RustyRiders.Game.Levels.Kit;

/// <summary>
/// The band an arch's ring or a rib fills: its inner edge (the intrados) and outer edge as matching points on the
/// surface it stands from, <see cref="Thickness"/> apart. A point of a stone's outline is metres along the band's
/// middle line and metres out from its inner edge, so stones laid along it follow the curve.
/// </summary>
internal sealed class VoussoirBand
{
    private readonly Vector3[] inner, outer;
    private readonly float[] along; // metres along the middle line at each point

    internal VoussoirBand(Vector3[] inner, Vector3[] outer, float thickness)
    {
        (this.inner, this.outer, Thickness) = (inner, outer, thickness);
        along = new float[inner.Length];
        for (int k = 1; k < inner.Length; k++)
            along[k] = along[k - 1] + Vector3.Distance((inner[k] + outer[k]) / 2, (inner[k - 1] + outer[k - 1]) / 2);
    }

    internal float Thickness { get; }
    internal float Length => along[^1];

    /// <summary>A point <paramref name="p"/>.X metres along the band and <paramref name="p"/>.Y out from its inner edge.</summary>
    internal Vector3 On(Vector2 p)
    {
        int k = Math.Clamp(Array.BinarySearch(along, p.X) is var i && i < 0 ? ~i - 1 : i, 0, along.Length - 2);
        float f = (p.X - along[k]) / MathF.Max(along[k + 1] - along[k], 1e-6f);
        Vector3 a = Vector3.Lerp(inner[k], inner[k + 1], f), b = Vector3.Lerp(outer[k], outer[k + 1], f);
        return a + (b - a) * (p.Y / Thickness);
    }

    /// <summary>The outline with a point added wherever an edge crosses one of the band's points, so its sides bend with the band.</summary>
    internal List<Vector2> Bend(List<Vector2> rim)
    {
        List<Vector2> bent = [];
        for (int i = 0; i < rim.Count; i++)
        {
            Vector2 p = rim[i], q = rim[(i + 1) % rim.Count];
            bent.Add(p);
            IEnumerable<float> crossed = along.Where(x => x > MathF.Min(p.X, q.X) + 1e-3f && x < MathF.Max(p.X, q.X) - 1e-3f);
            foreach (float x in q.X > p.X ? crossed : crossed.Reverse()) bent.Add(Vector2.Lerp(p, q, (x - p.X) / (q.X - p.X)));
        }
        return bent;
    }

    /// <summary>A flat strip over the band <paramref name="out_"/> times <paramref name="d"/> off it, inset <paramref name="inset"/> from both edges: what a ring's joints show.</summary>
    internal void Strip(MeshBuilder mesh, int slot, float metres, Vector3 out_, float d, float inset)
    {
        float y0 = inset, y1 = Thickness - inset;
        if (y1 <= y0) return;
        for (int k = 0; k + 1 < along.Length; k++)
            mesh.Face(slot, On(new(along[k], y0)) + out_ * d, On(new(along[k + 1], y0)) + out_ * d, On(new(along[k + 1], y1)) + out_ * d,
                On(new(along[k], y1)) + out_ * d, new Vector2(along[k] / metres, y0 / metres), new Vector2(along[k + 1] / metres, y0 / metres),
                new Vector2(along[k + 1] / metres, y1 / metres), new Vector2(along[k] / metres, y1 / metres), out_);
    }
}

/// <summary>
/// Arches and ribs built of stones (<see cref="VoussoirDefinition"/>): a band is cut into voussoirs laid from each
/// springing toward a keystone at the crown, each a <see cref="StoneShape"/> of the definition's kinds with its face
/// jutting a little more or less and its edges a little off the band's.
/// </summary>
internal static class Voussoirs
{
    /// <summary>
    /// Lays <paramref name="band"/> in voussoirs of <paramref name="surface"/>, their faces <paramref name="face"/> out
    /// along <paramref name="out_"/>; with a <paramref name="back"/>, two-faced, that far the other way too. Returns how many.
    /// </summary>
    internal static int Lay(MeshBuilder mesh, KitRecipe recipe, VoussoirDefinition v, KitSurface surface, VoussoirBand band, Vector3 out_,
        float face, float back, Random random)
    {
        int slot = LevelKit.Slot(surface);
        float metres = recipe.Metres(surface), total = v.Stones.Sum(s => s.Weight), length = band.Length, g = v.Joint / 2;
        List<(float X0, float X1, StoneKind Kind, bool Key)> blocks = [];
        float keyFrom = length, keyTo = length;
        if (v.Keystone > 0 && length > 3 * v.Shortest)
        {
            StoneKind key = Pick(random, v.Stones, total);
            float half = MathF.Min(key.MaxLength, length / 3) / 2;
            (keyFrom, keyTo) = (length / 2 - half, length / 2 + half);
            blocks.Add((keyFrom, keyTo, key, true));
        }
        // From each springing up to the keystone (or, with none, from one springing to the other).
        foreach ((float x0, float x1, StoneKind kind) in Fill(0, keyFrom)) blocks.Add((x0, x1, kind, false));
        if (keyTo < length)
            foreach ((float x0, float x1, StoneKind kind) in Fill(0, length - keyTo)) blocks.Add((length - x1, length - x0, kind, false));

        StoneFrame frame = new(band.On, out_);
        foreach ((float x0, float x1, StoneKind kind, bool key) in blocks)
        {
            if (x1 - x0 <= 2 * g) continue;
            float extra = key ? v.Keystone : 0;
            float y0 = Between(random, -v.InnerSag, v.InnerSag) - extra, y1 = band.Thickness + Between(random, -v.OuterSag, v.OuterSag) + extra;
            List<Vector2> rim = band.Bend(Outline(kind, random, x0 + g, x1 - g, y0, y1));
            float jut = key ? v.MaxJut + extra : Between(random, v.MinJut, v.MaxJut);
            Vector2 patch = new((float)random.NextDouble(), (float)random.NextDouble());
            float backJut = key ? v.MaxJut + extra : Between(random, v.MinJut, v.MaxJut);
            Build(mesh, slot, metres, v.Bevel, kind, random, frame, rim, face + jut, patch, back > 0 ? back + backJut : 0);
        }
        return blocks.Count;

        List<(float, float, StoneKind)> Fill(float from, float to)
        {
            List<(float, float, StoneKind)> laid = [];
            float x = from;
            while (to - x > 1e-3f)
            {
                StoneKind kind = Pick(random, v.Stones, total);
                float end = x + Between(random, kind.MinLength, kind.MaxLength);
                if (to - end < v.Shortest * .6f) end = to; // no sliver of a stone against the keystone
                laid.Add((x, end, kind));
                x = end;
            }
            return laid;
        }
    }
}
