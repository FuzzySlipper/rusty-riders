using System.Numerics;
using Rusty.Engine;

namespace RustyRiders.Game.Held;

/// <summary>
/// A drawn hand's knuckle angles, read from its joints in the hand's canonical frame, in the terms a <see cref="HandShape"/>
/// gives them: each finger joint's curl (the bend from the previous bone, or from +Y at the knuckle, positive toward the
/// palm) and each thumb joint's [curl, swing] (its bone's rise out of the palm toward it, and its turn about the palm's
/// normal from +Y toward the thumb side, each from the previous bone's). A rig's <see cref="ArmRig.Grip"/> is the canonical
/// holding pose (<see cref="ArmDigits.Pose"/>) less its clip's knuckles, so every rig holds a look's shape alike.
/// </summary>
internal static class HandKnuckles
{
    private const float Degrees = 180 / MathF.PI;

    /// <param name="canonical">The hand's canonical frame in model space.</param>
    /// <param name="fingers">The index, middle, ring and little fingers' joints, base to tip.</param>
    /// <param name="thumb">The thumb's joints, base to tip.</param>
    /// <param name="left">The left hand, whose swing is mirrored.</param>
    internal static HandShape Read(JointPose[] joints, Quaternion canonical, uint[][] fingers, uint[] thumb, bool left)
    {
        Quaternion unturn = Quaternion.Inverse(canonical);
        float side = left ? -1 : 1;
        return new([.. fingers.Select(finger => Curls(Bones(joints, finger, unturn)))],
            [.. Thumb(Bones(joints, thumb, unturn), side)]);
    }

    // Each joint's bone in the canonical frame: toward the next joint, and for the last (no tip joint is named) along
    // the axis the previous bone runs in its own joint's frame, turned as the last joint is.
    private static Vector3[] Bones(JointPose[] joints, uint[] chain, Quaternion unturn)
    {
        Vector3 At(int i) => joints[chain[i]].Model.Translation;
        Quaternion Turn(int i) => joints[chain[i]].Model.Rotation;
        Vector3 along = Vector3.Normalize(Vector3.Transform(At(2) - At(1), Quaternion.Inverse(Turn(1))));
        Vector3[] bones = [At(1) - At(0), At(2) - At(1), Vector3.Transform(along, Turn(2))];
        return [.. bones.Select(b => Vector3.Normalize(Vector3.Transform(b, unturn)))];
    }

    // A finger curls about the knuckle line (X): from +Y toward the palm (-Z).
    private static float[] Curls(Vector3[] bones)
    {
        float before = 0;
        float[] curls = new float[3];
        for (int i = 0; i < 3; i++)
        {
            float pitch = MathF.Atan2(-bones[i].Z, bones[i].Y) * Degrees;
            curls[i] = Wrap(pitch - before);
            before = pitch;
        }
        return curls;
    }

    // A thumb rises out of the palm's plane toward the palm (-Z) and swings in it from +Y toward the thumb (-X on the right).
    private static IEnumerable<float[]> Thumb(Vector3[] bones, float side)
    {
        (float Rise, float Swing) before = (0, 0);
        foreach (Vector3 bone in bones)
        {
            (float Rise, float Swing) now = (MathF.Asin(Math.Clamp(-bone.Z, -1, 1)) * Degrees, MathF.Atan2(-bone.X, bone.Y) * Degrees * side);
            yield return [Wrap(now.Rise - before.Rise), Wrap(now.Swing - before.Swing)];
            before = now;
        }
    }

    private static float Wrap(float degrees) => degrees > 180 ? degrees - 360 : degrees <= -180 ? degrees + 360 : degrees;

    /// <summary>One hand's angles as a shape's JSON (whole degrees).</summary>
    internal static string Json(HandShape hand) =>
        FormattableString.Invariant($"{{\"fingers\": [{string.Join(", ", hand.Fingers.Select(f => $"[{string.Join(", ", f.Select(Round))}]"))}], \"thumb\": [{string.Join(", ", hand.Thumb.Select(j => $"[{Round(j[0])}, {Round(j[1])}]"))}]}}");

    /// <summary>The shape that takes one hand's angles to another's (<paramref name="to"/> less <paramref name="from"/>).</summary>
    internal static HandShape Difference(HandShape to, HandShape from) =>
        new([.. to.Fingers.Zip(from.Fingers, (t, f) => t.Zip(f, (a, b) => Wrap(a - b)).ToArray())],
            [.. to.Thumb.Zip(from.Thumb, (t, f) => t.Zip(f, (a, b) => Wrap(a - b)).ToArray())]);

    private static float Round(float degrees) => MathF.Round(degrees) is var r && r == 0 ? 0 : r;
}
