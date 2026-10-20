using RustyRiders.Game.Content;

namespace RustyRiders.Game.Held;

/// <summary>
/// How each hand's digits sit on a look, added over the drawing rig's <see cref="ArmRig.Grip"/> bend: the main hand,
/// and the off hand when the look is held in both hands. A look in the off hand alone takes its main shape, mirrored as
/// the hand is. From rusty-hotel's <c>HeldShape</c> (docs/reuse.md).
/// </summary>
internal sealed record HeldShape(HandShape Main, HandShape? Off = null)
{
    internal void Validate(string path, string field, bool offGrip)
    {
        Main.Validate(path, $"{field}.main");
        Authored.Require((Off is null) || offGrip, path, $"{field}.off", "is only given for a look with an off grip.");
        Off?.Validate(path, $"{field}.off");
    }

    /// <summary>Takes a reloaded file's shapes in place (an off shape added or removed needs a restart).</summary>
    internal void Take(HeldShape fresh)
    {
        Main.Take(fresh.Main);
        if (Off is { } off && fresh.Off is { } next) off.Take(next);
    }
}

/// <summary>
/// Degrees added to one hand's digits over the rig's grip, under the motion keys' closure: each finger's three joints'
/// curl (the index, middle, ring and little fingers, base to tip; positive toward the palm) and each thumb joint's
/// [curl, swing]. Applied through each rig's digit frames, so one shape suits every rig profile.
/// </summary>
internal sealed record HandShape(float[][] Fingers, float[][] Thumb)
{
    internal void Validate(string path, string field)
    {
        Authored.Require(Fingers.Length == 4 && Fingers.All(f => f.Length == 3), path, $"{field}.fingers",
            "gives the index, middle, ring and little fingers' three joints' curl each, in degrees.");
        Authored.Require(Thumb.Length == 3 && Thumb.All(j => j.Length == 2), path, $"{field}.thumb", "gives three thumb joints' [curl, swing], in degrees.");
    }

    internal void Take(HandShape fresh)
    {
        for (int i = 0; i < 4; i++) fresh.Fingers[i].CopyTo(Fingers[i], 0);
        for (int i = 0; i < 3; i++) fresh.Thumb[i].CopyTo(Thumb[i], 0);
    }
}
