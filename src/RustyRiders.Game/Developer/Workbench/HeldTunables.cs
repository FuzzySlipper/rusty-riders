using Rusty.Engine;
using Rusty.Engine.Debugging;
using RustyRiders.Game.Actions;
using RustyRiders.Game.Held;

namespace RustyRiders.Game.Developer.Workbench;

/// <summary>
/// The first-person values the Engine tunables panel shows for the look and action the motion viewer shows: the look's
/// stance (every look in it), its nudge and grip, the action's key timing and curves, and the held key's pose. Each
/// control applies its value to the loaded <see cref="HeldCatalog"/> as the console commands do, and names where it is
/// saved (its content file and JSON pointer), so the panel's Save writes it there. Adapted from rusty-hotel's
/// <c>ViewmodelTunables</c> (docs/reuse.md).
/// </summary>
internal static class HeldTunables
{
    internal static List<TunableGroup> For(HeldCatalog held, string look, ActionDefinition action, int key, Action publish)
    {
        HeldLookDefinition model = held.Look(look)!;
        HeldMotion motion = held.Motion.Motions[model.Motion];
        int lookIndex = Array.IndexOf(held.Looks, model);
        List<TunableGroup> groups = [];
        void Reseat(IEnumerable<HeldLookDefinition> looks)
        {
            foreach (HeldLookDefinition each in looks) held.Seat(each);
            publish();
        }

        HeldStance stance = held.Stances.Stances[model.Stance];
        string stancePointer = $"/stances/{Escape(model.Stance)}";
        Action reseatStance = () => Reseat(held.Looks.Where(l => l.Stance == model.Stance));
        groups.Add(new($"stance.{model.Stance}", $"Stance · {model.Stance} (every look in it)",
        [
            .. Vector("stance.grip", "grip", stance.Grip, -0.8f, 0.8f, .005f, StanceCatalog.Path, $"{stancePointer}/grip", reseatStance),
            .. Vector("stance.toward", "toward", stance.Toward, -12, 12, .01f, StanceCatalog.Path, $"{stancePointer}/toward", reseatStance),
            .. Vector("stance.up", "up", stance.Up, -1, 1, .01f, StanceCatalog.Path, $"{stancePointer}/up", reseatStance)
        ]) { File = StanceCatalog.Path });

        string lookPointer = $"/looks/{lookIndex}";
        Action reseatLook = () => Reseat([model]);
        groups.Add(new($"look.{look}", $"Look · {look}",
        [
            .. Vector("nudge.grip", "nudge grip", model.Adjust.Grip, -0.2f, 0.2f, .002f, HeldCatalog.Path, $"{lookPointer}/nudge/grip", reseatLook),
            .. Vector("nudge.turn", "nudge turn", model.Adjust.Turn, -60, 60, .5f, HeldCatalog.Path, $"{lookPointer}/nudge/turn", reseatLook),
            .. Vector("grip.main", "grip (look space)", model.Grips.Main, -0.6f, 0.6f, .002f, HeldCatalog.Path, $"{lookPointer}/grips/main", reseatLook),
            .. (model.Grips.Off is { } off
                ? Vector("grip.off", "off grip (look space)", off, -0.6f, 0.6f, .002f, HeldCatalog.Path, $"{lookPointer}/grips/off", publish)
                : []),
            .. (model.Grips.OffHand is { } offHold
                ? Vector("grip.offHand", "off hand hold", offHold, -180, 180, 1, HeldCatalog.Path, $"{lookPointer}/grips/offHand", publish)
                : []),
            .. (model.Grips.Hand is { } hand
                ? Vector("grip.hand", "hand hold (this look)", hand, -180, 180, 1, HeldCatalog.Path, $"{lookPointer}/grips/hand", publish)
                : [])
        ]) { File = HeldCatalog.Path });
        if (model.Model is { } fitted)
            groups.Add(new($"model.{look}", $"Model fit · {look} (its length along -Z, up +Y)",
            [
                .. Vector("model.rotation", "turn", fitted.Rotation, -180, 180, 1, HeldCatalog.Path, $"{lookPointer}/model/rotation", reseatLook),
                new(new("model.length", "length", TunableKind.Slider, 0.05f, 2, .01f, fitted.Length) { Source = new(HeldCatalog.Path, $"{lookPointer}/model/length") },
                    v => { fitted.Length = v; publish(); })
            ]) { File = HeldCatalog.Path });

        // The keys' timing and curves, for the action's track (its own, or the motion's default): each key's phase,
        // its moment through that phase, the hand's closure and the curve it eases in by. Moving a key moves the timeline.
        string motionPointer = $"/motions/{Escape(model.Motion)}";
        if (motion.Track(action.Id) is { } track)
        {
            string trackId = motion.Tracks.ContainsKey(action.Id) ? action.Id : HeldMotionCatalog.DefaultTrack;
            List<Tunable> timing = [];
            for (int i = 0; i < track.Length; i++)
            {
                int index = i;
                MotionKey k = track[i];
                string at = $"{motionPointer}/tracks/{Escape(trackId)}/{i}";
                timing.Add(new(new($"key.{i}.phase", $"{i} {k.Pose} phase", TunableKind.Choice, 0, Phases.Length - 1, 1, Array.IndexOf(Phases, k.Phase))
                    { Options = [.. Phases.Select(p => p.ToString())], Reshapes = true, Source = new(HeldMotionCatalog.Path, $"{at}/phase") },
                    v => { track[index] = track[index] with { Phase = Phases[(int)MathF.Round(v)] }; publish(); }));
                timing.Add(new(new($"key.{i}.at", $"{i} {k.Pose} at", TunableKind.Slider, 0, 1, .01f, k.At)
                    { Reshapes = true, Source = new(HeldMotionCatalog.Path, $"{at}/at") },
                    v => { track[index] = track[index] with { At = v }; publish(); }));
                timing.Add(new(new($"key.{i}.grip", $"{i} {k.Pose} hand closed", TunableKind.Slider, -1, 1, .05f, k.Grip)
                    { Source = new(HeldMotionCatalog.Path, $"{at}/grip") },
                    v => { track[index] = track[index] with { Grip = v }; publish(); }));
                if (model.Grips.Off is not null)
                    timing.Add(new(new($"key.{i}.offGrip", $"{i} {k.Pose} off hand closed", TunableKind.Slider, -1, 1, .05f, k.OffGrip)
                        { Source = new(HeldMotionCatalog.Path, $"{at}/offGrip") },
                        v => { track[index] = track[index] with { OffGrip = v }; publish(); }));
                timing.Add(new(new($"key.{i}.ease", $"{i} {k.Pose} ease", TunableKind.Choice, 0, HeldMotionCatalog.Named.Length - 1, 1,
                    Array.IndexOf(HeldMotionCatalog.Named, k.Ease))
                    { Options = [.. HeldMotionCatalog.Named.Select(e => e.ToString())], Source = new(HeldMotionCatalog.Path, $"{at}/ease") },
                    v => { track[index] = track[index] with { Ease = HeldMotionCatalog.Named[(int)MathF.Round(v)] }; publish(); }));
            }
            groups.Add(new($"track.{model.Motion}.{trackId}", $"Keys · {model.Motion} · {trackId} (every look in it)", timing) { File = HeldMotionCatalog.Path });
        }

        if (key >= 0 && motion.Track(action.Id) is { } keys && key < keys.Length)
        {
            string name = keys[key].Pose;
            HeldPose pose = motion.Poses[name];
            string posePointer = $"{motionPointer}/poses/{Escape(name)}";
            // The viewer holds this key and reads its pose as it is now.
            groups.Add(new($"pose.{model.Motion}.{name}", $"Pose · {name} ({model.Motion}, every look in it)",
            [
                .. Vector("pose.position", "move", pose.Position, -0.5f, 0.5f, .005f, HeldMotionCatalog.Path, $"{posePointer}/position", publish),
                .. Vector("pose.rotation", "turn", pose.Rotation, -180, 180, 1, HeldMotionCatalog.Path, $"{posePointer}/rotation", publish)
            ]) { File = HeldMotionCatalog.Path });
        }
        // Feedback punches: how far each swings and turns, for how long and how often.
        List<Tunable> feel = [];
        foreach (FeedbackKind kind in Enum.GetValues<FeedbackKind>())
        {
            HeldPunch punch = held.Feedback.Punch(kind);
            string field = char.ToLowerInvariant(kind.ToString()[0]) + kind.ToString()[1..];
            feel.AddRange(Vector($"feel.{field}.move", $"{field} move", punch.Move, -0.15f, 0.15f, .002f, HeldFeedback.Path, $"/{field}/move", publish));
            feel.AddRange(Vector($"feel.{field}.turn", $"{field} turn", punch.Turn, -45, 45, .5f, HeldFeedback.Path, $"/{field}/turn", publish));
            feel.Add(new(new($"feel.{field}.seconds", $"{field} seconds", TunableKind.Slider, .02f, 1, .01f, punch.Seconds)
                { Source = new(HeldFeedback.Path, $"/{field}/seconds") }, v => punch.Seconds = v));
            feel.Add(new(new($"feel.{field}.frequency", $"{field} swings", TunableKind.Slider, .5f, 6, .25f, punch.Frequency)
                { Source = new(HeldFeedback.Path, $"/{field}/frequency") }, v => punch.Frequency = v));
        }
        groups.Add(new("feedback", "Feedback (riders.dev.held.feel to try one)", feel) { File = HeldFeedback.Path });

        // The arms: where the right shoulder sits and the right elbow's pole (the left's are mirrored), and how a hand
        // holds a look (its turn from the look and its palm from the wrist), for both hands alike.
        ArmsDefinition arms = held.Arms;
        groups.Add(new("arms", "Arms, every rig (the left mirrors the right)",
        [
            .. Vector("arms.shoulder", "shoulder", arms.Shoulder, -0.6f, 0.6f, .005f, ArmsDefinition.Path, "/shoulder", publish),
            .. Vector("arms.pole", "elbow toward", arms.Pole, -2, 2, .01f, ArmsDefinition.Path, "/pole", publish),
            .. Vector("arms.hold", "hold", arms.Hold, -180, 180, 1, ArmsDefinition.Path, "/hold", publish)
        ]) { File = ArmsDefinition.Path });
        // The rig drawing the arms: how its model is placed, where its palm is, and how its hands close on the clip.
        ArmRig rig = held.Rigs[arms.Rig];
        string rigPath = ArmRig.PathOf(arms.Rig);
        string[] joints = ["base", "middle", "tip"];
        List<Tunable> rigControls =
        [
            .. Vector("rig.turn", "model turn", rig.Turn, -180, 180, 1, rigPath, "/turn", publish),
            new(new("rig.scale", "model scale", TunableKind.Slider, 0.1f, 3, .01f, rig.Scale) { Source = new(rigPath, "/scale") }, v => { rig.Scale = v; publish(); }),
            .. Vector("rig.palm", "palm", rig.Palm, -0.15f, 0.15f, .002f, rigPath, "/palm", publish)
        ];
        for (int i = 0; i < 3; i++)
        {
            int joint = i;
            rigControls.Add(new(new($"rig.grip.finger.{i}", $"grip finger {joints[i]} curl", TunableKind.Slider, -90, 120, 1, rig.Grip.Fingers[i])
                { Source = new(rigPath, $"/grip/fingers/{i}") }, v => { rig.Grip.Fingers[joint] = v; publish(); }));
            rigControls.Add(new(new($"rig.grip.thumb.{i}.curl", $"grip thumb {joints[i]} curl", TunableKind.Slider, -90, 120, 1, rig.Grip.Thumb[i][0])
                { Source = new(rigPath, $"/grip/thumb/{i}/0") }, v => { rig.Grip.Thumb[joint][0] = v; publish(); }));
            rigControls.Add(new(new($"rig.grip.thumb.{i}.swing", $"grip thumb {joints[i]} swing", TunableKind.Slider, -90, 90, 1, rig.Grip.Thumb[i][1])
                { Source = new(rigPath, $"/grip/thumb/{i}/1") }, v => { rig.Grip.Thumb[joint][1] = v; publish(); }));
        }
        groups.Add(new($"rig.{arms.Rig}", $"Rig · {arms.Rig}", rigControls) { File = rigPath });
        return groups;
    }

    private static readonly MotionPhase[] Phases = Enum.GetValues<MotionPhase>();

    // Three sliders over one authored point, applied in place and saved to its elements.
    private static IEnumerable<Tunable> Vector(string id, string label, float[] values, float min, float max, float step, string file, string pointer,
        Action applied)
    {
        string[] axes = ["x", "y", "z"];
        for (int i = 0; i < 3; i++)
        {
            int axis = i;
            yield return new(new($"{id}.{axes[i]}", $"{label} {axes[i]}", TunableKind.Slider, min, max, step, values[i])
                { Source = new(file, $"{pointer}/{i}") },
                v =>
                {
                    values[axis] = v;
                    applied();
                });
        }
    }

    // A JSON pointer reference token (RFC 6901).
    private static string Escape(string token) => token.Replace("~", "~0").Replace("/", "~1");
}
