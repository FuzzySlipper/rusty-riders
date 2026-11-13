using System.Numerics;
using Rusty.Engine;

namespace RustyRiders.Game.Levels.Kit;

/// <summary>
/// Stones whose faces the shader carves (<see cref="StoneFacesDefinition"/>): a plain box of the stone's rectangle (or rim),
/// standing out to its relief plus its kind's bulge, whose face vertices carry to the shader where they lie on the stone
/// and the stone's shape (<see cref="Data"/>). Its sides and every other vertex are white, drawn as usual.
/// </summary>
internal static partial class StoneShape
{
    /// <summary>The four corners of a stone's rectangle, anticlockwise from its bottom-left, as <see cref="Outline"/> starts.</summary>
    internal static List<Vector2> Box(float x0, float x1, float y0, float y1) => [new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1)];

    /// <param name="box">The stone's outline: its rectangle, or its chipped rim when <paramref name="chips"/> is false
    /// (the mesh keeps the chips, so the shader cuts none).</param>
    private static void Faced(MeshBuilder mesh, int slot, float metres, float bevel, StoneKind kind, StoneFrame frame, List<Vector2> box,
        bool chips, float relief, Vector2 patch, float back)
    {
        float minX = box.Min(p => p.X), maxX = box.Max(p => p.X), minY = box.Min(p => p.Y), maxY = box.Max(p => p.Y);
        float width = maxX - minX, height = maxY - minY;
        float b = MathF.Min(bevel, MathF.Min(relief / 2, MathF.Min(width / 4, height / 4)));
        // The face is a flat top at the bulge's peak, carved down in the shader; the sides stop where the carved bevel
        // meets them (as the mesh's do), so they do not stand above it.
        float front = relief + kind.Pillow, rear = back > 0 ? -(back + kind.Pillow) : 0;
        float sideFront = relief - b, sideRear = back > 0 ? -(back - b) : 0;
        Vector3 At(Vector2 p, float d) => frame.On(p) + frame.Out * d;
        Vector2 offset = patch * metres;
        Vector2 Uv(Vector2 p) => new((offset.X + p.X) / metres, -(offset.Y + p.Y) / metres);
        (float a, float c) = Data(chips ? kind : kind with { ChipChance = 0, Nicks = 0 }, b, patch);
        Color Local(Vector2 p) => new((p.X - minX) / width, (p.Y - minY) / height, a, c);

        int n = box.Count;
        float around = 0;
        for (int i = 0; i < n; i++)
        {
            Vector2 p = box[i], q = box[(i + 1) % n], d = q - p, half = (p + q) / 2;
            Vector3 outward = Vector3.Normalize(frame.On(half + Vector2.Normalize(new Vector2(d.Y, -d.X)) * .01f) - frame.On(half));
            float l0 = around, l1 = around + d.Length();
            mesh.Face(slot, At(p, sideRear), At(q, sideRear), At(q, sideFront), At(p, sideFront),
                new Vector2((offset.X + l0) / metres, sideRear / metres), new Vector2((offset.X + l1) / metres, sideRear / metres),
                new Vector2((offset.X + l1) / metres, sideFront / metres), new Vector2((offset.X + l0) / metres, sideFront / metres), outward);
            around = l1;
        }
        Cap(1, front);
        if (back > 0) Cap(-1, -rear);

        void Cap(float sign, float r)
        {
            Vector2 middle = new((minX + maxX) / 2, (minY + maxY) / 2);
            uint hub = mesh.Vertex(At(middle, sign * r), Uv(middle), Local(middle));
            uint[] ring = box.Select(p => mesh.Vertex(At(p, sign * r), Uv(p), Local(p))).ToArray();
            for (int i = 0; i < n; i++) mesh.Triangle(slot, hub, ring[i], ring[(i + 1) % n], frame.Out * sign);
        }
    }

    /// <summary>
    /// A stone's shape as two whole numbers the shader decodes (stone-faces.wgsl, <c>decode</c>), each over 2^19 so it
    /// fits a colour channel (the Engine takes 0 to 1) and stays exact in a float through interpolation; a stone's alpha is
    /// then under a half, and a white vertex's 1 marks it as no stone. The first: a seed from the stone's texture patch
    /// (8 bits), hewn (1), chip and nick chances in sevenths (3 and 3), bevel in centimetres (4). The second: bulge in
    /// millimetres (7 bits), facets in millimetres (6), chips in centimetres (5).
    /// </summary>
    private const float Scale = 1 << 19;

    private static (float A, float B) Data(StoneKind kind, float bevel, Vector2 patch)
    {
        static int Bits(float value, int max) => Math.Clamp((int)MathF.Round(value), 0, max);
        int seed = Bits(patch.X * 255, 255);
        int a = seed | (kind.Hewn ? 1 << 8 : 0) | Bits(kind.ChipChance * 7, 7) << 9 | Bits(kind.Nicks * 7, 7) << 12 | Bits(bevel * 100, 15) << 15;
        int c = Bits(kind.Pillow * 1000, 127) | Bits(kind.Facet * 1000, 63) << 7 | Bits(kind.Chip * 100, 31) << 13;
        return (a / Scale, c / Scale);
    }
}
