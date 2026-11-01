using System.Numerics;
using System.Text.Json.Serialization;
using RustyRiders.Game.Content;

namespace RustyRiders.Game.Actions;

/// <summary>How a melee blow finds what it strikes. Each is one Engine spatial query, dispatched by <see cref="ActionResolution"/>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MeleeHitKind>))]
internal enum MeleeHitKind
{
    /// <summary>A capsule of the blow's width along the aim from the eye as it lands: the first body within reach.</summary>
    Thrust,
    /// <summary>A ray along the aim as it lands: the first body within reach, width ignored.</summary>
    Ray,
    /// <summary>Every body a sphere of the delivery's radius overlaps, centred its range along the aim, with a clear line to it.</summary>
    Sphere,
    /// <summary>
    /// A blade (a capsule of the blow's width from the eye out to its reach) turned about the eye from one bearing to
    /// another across the commit, on admitted time: each body it passes is struck as the blade reaches it, once. A
    /// surface it meets first stops it.
    /// </summary>
    Sweep,
}

/// <summary>
/// A melee blow's hit: its kind; for a sweep the bearings the blade turns from and to; and how many bodies one blow may
/// strike (nearest or earliest first; a thrust and a ray strike one).
/// </summary>
internal sealed record MeleeHit(MeleeHitKind Kind, Bearing? From = null, Bearing? To = null, int Limit = 1)
{
    internal static readonly MeleeHit Thrust = new(MeleeHitKind.Thrust);

    internal void Validate(string path, string at)
    {
        bool sweep = Kind == MeleeHitKind.Sweep;
        Authored.Require(sweep ? From is not null && To is not null : From is null && To is null, path, at,
            sweep ? "a sweep turns from one bearing to another: give from and to." : "only a sweep has from and to.");
        Authored.AtLeast(path, $"{at}.limit", Limit, 1);
        Authored.Require(Limit == 1 || Kind is MeleeHitKind.Sphere or MeleeHitKind.Sweep, path, $"{at}.limit", "a thrust or a ray strikes one body.");
        From?.Validate(path, $"{at}.from");
        To?.Validate(path, $"{at}.to");
    }
}

/// <summary>A direction from the eye in degrees right of the aim and up from it.</summary>
internal sealed record Bearing(float Right, float Up)
{
    internal void Validate(string path, string at)
    {
        Authored.Within(path, $"{at}.right", Right, -180, 180);
        Authored.Within(path, $"{at}.up", Up, -89, 89);
    }

    /// <summary>
    /// The turn from the aim to this bearing (blended toward <paramref name="to"/> by <paramref name="t"/>), under which
    /// -Z, a blade's length in its own frame, points along it.
    /// </summary>
    internal static Quaternion Toward(Vector3 aim, Bearing from, Bearing to, float t)
    {
        float right = float.Lerp(from.Right, to.Right, t) * MathF.PI / 180, up = float.Lerp(from.Up, to.Up, t) * MathF.PI / 180;
        Quaternion facing = Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateWorld(Vector3.Zero, aim, Vector3.UnitY));
        return Quaternion.Normalize(facing * Quaternion.CreateFromAxisAngle(Vector3.UnitY, -right) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, up));
    }
}
