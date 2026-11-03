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
    /// A blade (a capsule of the blow's width from the eye out to its reach) turned about the eye through its arc across
    /// the commit, on admitted time: each body it passes is struck as the blade reaches it, once. A surface it meets
    /// first stops it.
    /// </summary>
    Sweep,
}

/// <summary>
/// A melee blow's hit: its kind; for a sweep the arc the blade turns through across the commit, authored in the view as
/// Dark Messiah ties its sweeps to the view rather than to the drawn sword (the arms clip only has to sell it); and how
/// many bodies one blow may strike (nearest or earliest first; a thrust and a ray strike one).
/// </summary>
internal sealed record MeleeHit(MeleeHitKind Kind, SweepArc? Arc = null, int Limit = 1)
{
    internal static readonly MeleeHit Thrust = new(MeleeHitKind.Thrust);

    internal void Validate(string path, string at)
    {
        bool sweep = Kind == MeleeHitKind.Sweep;
        Authored.Require(sweep == Arc is not null, path, $"{at}.arc", sweep ? "a sweep turns through an arc." : "only a sweep has an arc.");
        Authored.AtLeast(path, $"{at}.limit", Limit, 1);
        Authored.Require(Limit == 1 || Kind is MeleeHitKind.Sphere or MeleeHitKind.Sweep, path, $"{at}.limit", "a thrust or a ray strikes one body.");
        Arc?.Validate(path, $"{at}.arc");
    }
}

/// <summary>
/// A sweep's arc in the view: the blade turns at an even rate from <see cref="From"/> to <see cref="To"/> (degrees right
/// of the aim, left negative) in a plane through the aim, rolled <see cref="Tilt"/> degrees counter-clockwise as the
/// player sees it, so a positive tilt raises its right end (a cut from upper right to lower left).
/// </summary>
internal sealed record SweepArc(float From, float To, float Tilt = 0)
{
    internal void Validate(string path, string at)
    {
        Authored.Within(path, $"{at}.from", From, -180, 180);
        Authored.Within(path, $"{at}.to", To, -180, 180);
        Authored.Require(From != To, path, $"{at}.to", "an arc turns: from and to differ.");
        Authored.Within(path, $"{at}.tilt", Tilt, -89, 89);
    }

    /// <summary>Degrees across the arc from <see cref="From"/>.</summary>
    internal float Span => MathF.Abs(To - From);

    /// <summary>The turn from the aim to where the blade is <paramref name="t"/> (0 to 1) along the arc, under which -Z, a blade's length in its own frame, points along it.</summary>
    internal Quaternion Along(Vector3 aim, float t)
    {
        float right = float.Lerp(From, To, Math.Clamp(t, 0, 1)) * MathF.PI / 180;
        Quaternion facing = Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateWorld(Vector3.Zero, aim, Vector3.UnitY));
        return Quaternion.Normalize(facing * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, Tilt * MathF.PI / 180)
            * Quaternion.CreateFromAxisAngle(Vector3.UnitY, -right));
    }
}
