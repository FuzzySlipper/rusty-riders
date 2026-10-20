using System.Numerics;
using RustyRiders.Game.Content;

namespace RustyRiders.Game.Held;

/// <summary>
/// Where held things sit in first person at rest, by kind, for the main (right) hand: a blade low at the right edge and
/// rising, a gun nearer the centre and converging on the crosshair. Each stance gives, in camera space (metres right,
/// up, back), where the grip sits, a point the item's length heads toward, and the way its up side faces. A look names
/// its stance and is seated on it, so every look of a kind sits alike; the off (left) hand holds its look mirrored.
/// Adapted from rusty-hotel's <c>StanceCatalog</c> (docs/reuse.md).
/// </summary>
internal sealed record StanceCatalog(Dictionary<string, HeldStance> Stances)
{
    internal const string Path = "held/stances.json";

    internal static StanceCatalog Load(Rusty.Engine.IEngineContext engine)
    {
        StanceCatalog catalog = Authored.Read(engine, Path, HeldJson.Default.StanceCatalog);
        Authored.Require(catalog.Stances.Count > 0, Path, "stances", "needs at least one stance.");
        foreach (var (id, stance) in catalog.Stances)
        {
            Authored.Point(Path, $"stances.{id}.grip", stance.Grip);
            Authored.Point(Path, $"stances.{id}.toward", stance.Toward);
            Authored.Point(Path, $"stances.{id}.up", stance.Up);
            Vector3 along = Authored.Vector(stance.Toward) - Authored.Vector(stance.Grip);
            Authored.Require(along.Length() > 0.05f, Path, $"stances.{id}.toward", "must lie away from the grip.");
            Authored.Require(Vector3.Cross(Vector3.Normalize(along), Authored.Vector(stance.Up)).Length() > 0.1f, Path,
                $"stances.{id}.up", "must not lie along the item's length.");
        }
        return catalog;
    }
}

/// <summary>A stance: the grip's place, a point the item's length heads toward, and the way its up side faces (camera space).</summary>
internal sealed record HeldStance(float[] Grip, float[] Toward, float[] Up)
{
    /// <summary>The turn that lays a look's length (-Z) toward the stance's point with its up side (+Y) facing the stance's up.</summary>
    internal Quaternion Turn()
    {
        Vector3 along = Vector3.Normalize(Authored.Vector(Toward) - Authored.Vector(Grip));
        return Frames.Between(-Vector3.UnitZ, Vector3.UnitY, along, Authored.Vector(Up));
    }
}

/// <summary>
/// Rotations between frames, the product's degree convention (about X, Y, Z: yaw about Y, then pitch about X, then roll
/// about Z, as <see cref="Quaternion.CreateFromYawPitchRoll"/>), and the mirror that puts a main-hand placement in the
/// off hand. From rusty-hotel's <c>Frames</c>.
/// </summary>
internal static class Frames
{
    /// <summary>The rotation taking <paramref name="tip"/> to <paramref name="toward"/> and <paramref name="up"/> as near <paramref name="upToward"/> as it can.</summary>
    internal static Quaternion Between(Vector3 tip, Vector3 up, Vector3 toward, Vector3 upToward)
    {
        (Vector3 a, Vector3 b, Vector3 c) = Basis(tip, up);
        (Vector3 x, Vector3 y, Vector3 z) = Basis(toward, upToward);
        // Each axis of the space goes where its share of the item's basis says, rebuilt on the target basis.
        Vector3 Image(Vector3 v) => x * Vector3.Dot(v, a) + y * Vector3.Dot(v, b) + z * Vector3.Dot(v, c);
        Vector3 ix = Image(Vector3.UnitX), iy = Image(Vector3.UnitY), iz = Image(Vector3.UnitZ);
        // System.Numerics matrices carry each basis vector's image as a row.
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new(ix.X, ix.Y, ix.Z, 0, iy.X, iy.Y, iy.Z, 0, iz.X, iz.Y, iz.Z, 0, 0, 0, 0, 1)));
    }

    /// <summary>Degrees about X, Y and Z as an authored turn.</summary>
    internal static Quaternion Turn(float[] degrees)
    {
        Vector3 d = Authored.Vector(degrees) * (MathF.PI / 180);
        return Quaternion.CreateFromYawPitchRoll(d.Y, d.X, d.Z);
    }

    /// <summary>Degrees about X, Y and Z that <see cref="Turn"/> turns back into <paramref name="turn"/>.</summary>
    internal static Vector3 Degrees(Quaternion turn)
    {
        // The rotation's columns are the images of the axes; for R = Ry Rx Rz, R12 = -sin x, R02 / R22 = tan y and
        // R10 / R11 = tan z (from rusty-hotel's Frames).
        Vector3 cx = Vector3.Transform(Vector3.UnitX, turn), cy = Vector3.Transform(Vector3.UnitY, turn), cz = Vector3.Transform(Vector3.UnitZ, turn);
        return new Vector3(MathF.Asin(Math.Clamp(-cz.Y, -1, 1)), MathF.Atan2(cz.X, cz.Z), MathF.Atan2(cx.Y, cy.Y)) * (180 / MathF.PI);
    }

    /// <summary>A camera-space point seen in the mirror across the view's centre line (x negated).</summary>
    internal static Vector3 Mirror(Vector3 point) => point with { X = -point.X };

    /// <summary>
    /// A turn seen in the same mirror: conjugating by the reflection keeps rotations about X and flips those about Y
    /// and Z. It preserves composition, so an offset between mirrored turns is the mirrored offset.
    /// </summary>
    internal static Quaternion Mirror(Quaternion turn) => new(turn.X, -turn.Y, -turn.Z, turn.W);

    private static (Vector3, Vector3, Vector3) Basis(Vector3 along, Vector3 upHint)
    {
        Vector3 a = Vector3.Normalize(along);
        Vector3 b = Vector3.Normalize(upHint - a * Vector3.Dot(upHint, a));
        return (a, b, Vector3.Cross(a, b));
    }
}
