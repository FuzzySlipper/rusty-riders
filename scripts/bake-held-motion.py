"""Bakes old first-person clips into Rusty Riders held motion (content/held/motion.json).

The old first-person clips animate whole arms with a weapon at the hand. A held look's motion is the weapon alone, in
camera space, from where its stance seats it: a pose moves the grip and turns the look about it. So each frame becomes
the weapon bone's motion from rest, seen from the eye: its grip moved (metres, camera right, up, back) and turned about
the grip (degrees about camera X, Y, Z, as Frames.Turn reads them). The arms are not baked as a skeleton: ours reach the
grip by IK on any rig. What the source arm does that IK cannot infer is baked beside each key: the hand's closure (from
the source fingers' curl), the body's lean (the upper arm's move from rest, "shoulder") and the elbow's bend ("elbow").

Frames split into the action's phases (windup up to the strike, commit while the weapon is fast, recovery to the end):
`from` and `to` bake part of a clip. By default the strike is the frame the grip moves fastest and the commit ends when it slows below `slow` of that peak;
`strike` and `commitEnd` (a frame, or a fraction of the clip when below 1) override. Each key is timed by its phase and
its moment through it, so it lands on the same beat whatever the action's own timing.

Every kind keeps only the keys the motion needs: a frame is dropped while the curve the game draws through the kept keys
(a straight blend for actions and transitions, the closed Catmull-Rom of HeldLoop.At for loops) stays within the
recipe's `tolerance` for what it bakes (`action`, `transition`, or a loop's `idle`, `walk` or `sprint`) of every source
frame: `position` millimetres for the grip, the lean and the elbow's pole, and `angle` degrees for the turn and the
fingers' curl behind a key's closure. An entry may give its own `tolerance`. A standing sway is small and always
playing, so it keeps closer to its source than a sprint or a swing, which move fast and far.

With `twoHanded` (a weapon held in both hands, such as the FPP longsword), the other hand is baked too: at rest its
palm's place on the same weapon becomes the look's grips.off (look space) and its canonical frame from the look's its
grips.offHand; per key, its closure (offGrip) and its elbow's bend (offElbow). It needs `seat`, which measures them on
the seated look.

With `seat`, the look's nudge is set so it rests where the source weapon rests, and its grips.hand to the source hand's
hold on the weapon, so turns about the grip follow the source path and the wrist sits as the source's does. Which
weapon-bone axes are the look's length and up side follows from how the hand holds it (`hold`): a `fist` holds a hilt
across the palm, its blade leaving past the index finger with its edges along the fingers; a `pistol` grip's barrel runs
along the hand with its sights toward the index finger. `blade` and `edge` name the axes outright.

Sources (`source`): `mannequin` is the UE4 mannequin of FpMelee/FPP_* (FBX, a camera bone, weapon_r and weapon_l);
`cat` is the Hands pack's 3ds Max CAT rig (its ASCII FBX converted to GLB by unity-import first), which has no camera
bone: its eye is a fixed point above the shoulders, and its weapon bone is named per clip (`weapon`). `weapon: "palm"`
puts the look in the palm of a hand with no weapon bone of its own (a casting hand); a source's weapon bone is not always
at its hand (the dagger set's weapon_l hangs beside the other hand).

usage (scripts/bake-held-motions.sh runs every bake in art/held-motion/bakes.json):
  blender -b --factory-startup --python scripts/bake-held-motion.py -- --recipe art/held-motion/bakes.json [--only NAME]
      [--assets old-game/Assets] [--converted DIR]
  blender -b --factory-startup --python scripts/bake-held-motion.py -- --clip CLIP --rest REST --motion NAME
      --action ACTION [--look LOOK] [--seat] [--hand r|l] [--source mannequin|cat] [--weapon BONE] [--hold fist|pistol]
      [--blade AXIS] [--edge AXIS] [--strike F] [--commit-end F] [--no-closure] [--position MM] [--angle DEG] [--slow 0.4]
      [--scale 1]
"""
import argparse
import json
import math
import re
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Quaternion, Vector

args = argparse.ArgumentParser()
args.add_argument("--recipe", type=Path, help="a recipe of bakes and their tolerance (art/held-motion/bakes.json)")
args.add_argument("--only", help="bake only the recipe entry of this name")
args.add_argument("--assets", type=Path, default=Path("old-game/Assets"), help="where a recipe's mannequin clips are")
args.add_argument("--converted", type=Path, help="where unity-import put a recipe's cat clips")
args.add_argument("--clip", type=Path)
args.add_argument("--rest", type=Path, help="a clip whose first frame is the weapon at rest (the set's idle); without it, the clip's first baked frame")
args.add_argument("--from", dest="start", type=int, help="the first frame to bake")
args.add_argument("--to", dest="stop", type=int, help="the last frame to bake")
args.add_argument("--motion", help="the motion in motion.json to write the poses and track into")
args.add_argument("--action", help="the action id the track is for")
args.add_argument("--look", help="a look in looks.json to move by this motion")
args.add_argument("--hand", default="r", choices=["r", "l"], help="which source hand; a left hand is mirrored into the main hand's")
args.add_argument("--source", default="mannequin", choices=["mannequin", "cat"])
args.add_argument("--weapon", help="the source's weapon bone (the mannequin's weapon_r or weapon_l by default), or palm")
args.add_argument("--hold", default="fist", choices=["fist", "pistol"])
args.add_argument("--blade", help="the weapon bone's axis along the look's length, toward its tip (+x ... -z); default from --hold")
args.add_argument("--edge", help="the weapon bone's axis toward the look's up side; default from --hold")
args.add_argument("--strike", type=float)
args.add_argument("--commit-end", type=float)
args.add_argument("--position", type=float, default=20.0, help="how far (mm) a kept key's curve may stray from the source")
args.add_argument("--angle", type=float, default=8.0, help="how far (degrees) a kept key's curve may turn from the source")
args.add_argument("--slow", type=float, default=0.4)
args.add_argument("--scale", type=float, default=1.0, help="multiplies the grip's movement")
args.add_argument("--seat", action="store_true")
args.add_argument("--no-closure", action="store_true", help="keep the hand on its grip (a gesture's open hand would drop a wand)")
args.add_argument("--two-handed", action="store_true", help="bake the other hand on the same weapon too (its off grip, hold, closure and elbow)")
args.add_argument("--out", type=Path, default=Path("content/held/motion.json"))
args.add_argument("--looks", type=Path, default=Path("content/held/looks.json"))
args.add_argument("--stances", type=Path, default=Path("content/held/stances.json"))
args.add_argument("--arms", type=Path, default=Path("content/held/arms.json"), help="the arms' pole, where an action's elbows start")
cli = args.parse_args(sys.argv[sys.argv.index("--") + 1:])

# Both sources face -Y with +Z up in Blender: camera right is -X, up is +Z, back is +Y.
TO_CAMERA = Matrix(((-1, 0, 0), (0, 0, 1), (0, 1, 0)))
# The camera-space mirror across the view's centre line: held motion is authored for the main (right) hand.
MIRROR = Matrix(((-1, 0, 0), (0, 1, 0), (0, 0, 1)))
NAMES = ["+x", "-x", "+y", "-y", "+z", "-z"]
# How a hand holds a weapon, as its length's and up side's directions in the hand's canonical frame (+X toward the little
# finger, +Y along the fingers, +Z out of the back): a fist's blade leaves past the index finger with its edges along the
# fingers; a pistol's barrel runs along the hand with its sights toward the index finger.
HOLDS = {"fist": (Vector((-1, 0, 0)), Vector((0, 1, 0))), "pistol": (Vector((0, 1, 0)), Vector((-1, 0, 0)))}


class Source:
    """How a source rig names what the baker reads, and where its eye is."""

    def __init__(self, kind, hand, weapon=None):
        if kind == "mannequin":
            s = hand
            self.eye_bone, self.weapon = "camera", weapon or f"weapon_{s}"
            self.hand, self.upper, self.lower = f"hand_{s}", f"upperarm_{s}", f"lowerarm_{s}"
            self.knuckles = {d: f"{d}_01_{s}" for d in ("index", "middle", "pinky")}
            self.fingers = [f"{f}_0{i}_{s}" for f in ("index", "middle", "ring", "pinky") for i in (1, 2, 3)]
        else:
            s = hand.upper()
            self.eye_bone, self.weapon = None, weapon
            self.hand, self.upper, self.lower = f"CATRig{s}ArmPalm", f"CATRig{s}Arm1", f"CATRig{s}Arm21"
            self.knuckles = {"index": f"CATRig{s}ArmDigit21", "middle": f"CATRig{s}ArmDigit31", "pinky": f"CATRig{s}ArmDigit51"}
            self.fingers = [f"CATRig{s}ArmDigit{d}{i}" for d in (2, 3, 4, 5) for i in (1, 2, 3)]
        # The CAT rig's eye (Blender world): over its shoulders' hub, as the mannequin's camera bone sits about 0.2 m
        # above its upper arms and 0.1 m ahead of them.
        self.eye = Vector((0, 0.0, 1.64))

    @staticmethod
    def load(path):
        bpy.ops.wm.read_factory_settings(use_empty=True)
        if path.suffix.lower() == ".glb":
            bpy.ops.import_scene.gltf(filepath=str(path))
        else:
            bpy.ops.import_scene.fbx(filepath=str(path))
        rig = next(obj for obj in bpy.data.objects if obj.type == "ARMATURE")
        return rig, rig.animation_data.action


def bake(spec):
    hand_side = spec.get("hand", "r")
    src = Source(spec.get("source", "mannequin"), hand_side, spec.get("weapon"))
    side = MIRROR if hand_side == "l" else Matrix.Identity(3)

    def sample(rig, frame):
        """
        The weapon bone's grip and turn, the upper arm's place, the elbow's bend as a pole and the hand's canonical frame,
        in camera axes relative to the eye (a left hand's mirrored into the right's), the weapon bone's axes in that hand
        frame, and the fingers' curl (degrees).
        """
        bpy.context.scene.frame_set(frame)

        def world(name):
            return rig.matrix_world @ rig.pose.bones[name].matrix
        eye = world(src.eye_bone).translation if src.eye_bone else src.eye

        def at(name):
            return side @ TO_CAMERA @ (world(name).translation - eye)
        place, wrist, elbow = at(src.upper), at(src.hand), at(src.lower)
        # The elbow's bend as a pole: out from the middle of shoulder and wrist through the elbow, as far again.
        pole = elbow + (elbow - (place + wrist) / 2)
        y = (at(src.knuckles["middle"]) - wrist).normalized()
        across = at(src.knuckles["pinky"]) - at(src.knuckles["index"])
        x = (across - y * across.dot(y)).normalized()
        hand = Matrix((x, y, x.cross(y))).transposed().to_quaternion()
        if src.weapon == "palm":
            # A hand with no weapon bone of its own (a casting hand): the look rides in the palm, between the wrist and
            # the middle knuckle, turned with the hand.
            grip, turn = (wrist + at(src.knuckles["middle"])) / 2, hand
            axes = Matrix.Identity(3)
        else:
            weapon = world(src.weapon)
            grip = side @ TO_CAMERA @ (weapon.translation - eye)
            # Mirroring a turn conjugates it by the mirror (the weapon's own axes stay as the bone gives them).
            turn = (side @ TO_CAMERA @ weapon.to_3x3().normalized() @ side).to_quaternion()
            axes = hand.inverted().to_matrix() @ side @ TO_CAMERA @ weapon.to_3x3().normalized()
        curl = sum(math.degrees(rig.pose.bones[f].matrix_basis.to_quaternion().angle) for f in src.fingers) / 4
        return grip, turn, curl, place, pole, hand, axes

    two_handed = bool(spec.get("twoHanded"))
    if two_handed and not (spec.get("look") and spec.get("seat")):
        raise SystemExit(f"{spec.get('name', spec['clip'])}: a two-handed bake seats its look (look and seat): the off grip is measured on it.")
    off = Source(spec.get("source", "mannequin"), "l" if hand_side == "r" else "r") if two_handed else None

    def sample_off(rig, frame):
        """
        The other hand on the same weapon (a two-handed source): its palm (camera axes, between the wrist and the middle
        knuckle), its canonical frame as a left hand's (+X toward its thumb; a right hand's mirrored when the main hand
        is a left), its elbow's bend as a pole and its fingers' curl.
        """
        bpy.context.scene.frame_set(frame)
        eye = (rig.matrix_world @ rig.pose.bones[off.eye_bone].matrix).translation if off.eye_bone else off.eye

        def at(name):
            return side @ TO_CAMERA @ ((rig.matrix_world @ rig.pose.bones[name].matrix).translation - eye)
        place, wrist, elbow = at(off.upper), at(off.hand), at(off.lower)
        y = (at(off.knuckles["middle"]) - wrist).normalized()
        across = at(off.knuckles["index"]) - at(off.knuckles["pinky"])
        x = (across - y * across.dot(y)).normalized()
        frame_turn = Matrix((x, y, x.cross(y))).transposed().to_quaternion()
        curl = sum(math.degrees(rig.pose.bones[f].matrix_basis.to_quaternion().angle) for f in off.fingers) / 4
        return (wrist + at(off.knuckles["middle"])) / 2, frame_turn, elbow + (elbow - (place + wrist) / 2), curl

    rig, action = Source.load(Path(spec["clip"]))
    first, last = (int(f) for f in action.frame_range)
    # A part of the clip only (a cast whose hand starts and ends out of view); its first frame is the rest without a
    # rest clip.
    first, last = int(spec.get("from") or first), int(spec.get("to") or last)
    if spec.get("rest"):
        rest_rig, rest_action = Source.load(Path(spec["rest"]))
        rest_grip, rest_turn, rest_curl, rest_place, _, rest_hand, rest_axes = sample(rest_rig, int(rest_action.frame_range[0]))
        rest_off = sample_off(rest_rig, int(rest_action.frame_range[0])) if two_handed else None
        rig, action = Source.load(Path(spec["clip"]))
    else:
        rest_grip, rest_turn, rest_curl, rest_place, _, rest_hand, rest_axes = sample(rig, first)
        rest_off = sample_off(rig, first) if two_handed else None
    frames = list(range(first, last + 1))
    scale = spec.get("scale", 1.0)
    # Each frame from rest: the grip's move and the turn about it (left of the rest turn, as a pose applies it), the
    # fingers' curl, the shoulder's lean and the elbow's pole.
    # A two-handed source's other hand: its closure (curl from rest) and its elbow's pole, per frame.
    poses, offs = [], []
    for frame in frames:
        grip, turn, curl, place, pole, _, _ = sample(rig, frame)
        poses.append(((grip - rest_grip) * scale, (turn @ rest_turn.inverted()).normalized(), curl - rest_curl,
                      (place - rest_place) * scale, pole))
        if two_handed:
            _, _, off_pole, off_curl = sample_off(rig, frame)
            offs.append((off_curl - rest_off[3], off_pole))

    def frame_of(value, default):
        if value is None:
            return default
        return round(value * (len(frames) - 1)) if value < 1 else frames.index(int(value))

    kind = spec.get("kind", "action")
    if kind == "loop":
        return bake_loop(spec, poses, frames)
    if kind in ("equip", "unequip"):
        return bake_transition(spec, kind, poses, frames)

    # Phases from the grip's speed (a wrist roll can turn the weapon fastest without carrying the blow).
    speed = [0.0] + [(poses[i][0] - poses[i - 1][0]).length for i in range(1, len(poses))]
    strike = frame_of(spec.get("strike"), max(range(len(speed)), key=speed.__getitem__))
    slow = spec.get("slow", 0.4)
    end = frame_of(spec.get("commitEnd"),
                   next((i for i in range(strike + 1, len(speed)) if speed[i] < slow * speed[strike]), len(speed) - 1))
    end = min(max(end, strike + 1), len(frames) - 1)
    bounds = {"Windup": (0, strike), "Commit": (strike, end), "Recovery": (end, len(frames) - 1)}

    def timed(i):
        for phase in ("Windup", "Commit", "Recovery"):
            a, b = bounds[phase]
            if i <= b:
                return phase, round((i - a) / max(1, b - a), 3)
        return "Recovery", 1.0

    position, angle = tolerance(spec)

    def closed(curl):
        """A key's closure as the fingers' curl (degrees) it draws: none with noClosure, clamped to -1...1 of 40 degrees."""
        return 0.0 if spec.get("noClosure") else max(-40.0, min(40.0, curl))

    # The game starts a track where the hands rest (HeldView.Keyed): the look at rest, the hand at its grip, no lean and
    # the elbows toward the arms' pole (the off hand's mirrored), so the blend to the first key starts there, not at the
    # clip's first frame.
    arms_pole = Vector(json.loads(Path(spec.get("arms", cli.arms)).read_text())["pole"])
    start, off_start = (Vector(), Quaternion(), 0.0, Vector(), arms_pole), (0.0, MIRROR @ arms_pole)

    def off(i, a, b):
        """How far frame i strays from a straight blend of keys a and b, scaled to the tolerances, over every channel a key carries."""
        t = (i - a) / (b - a)
        grip, turn, curl, lean, pole = poses[i]
        ka = start if a == 0 else poses[a]
        kb = poses[b]
        worst = max((grip - ka[0].lerp(kb[0], t)).length / position,
                    math.degrees(turn.rotation_difference(ka[1].slerp(kb[1], t)).angle) / angle,
                    (lean - ka[3].lerp(kb[3], t)).length / position,
                    (pole - ka[4].lerp(kb[4], t)).length / position,
                    abs(closed(curl) - (closed(ka[2]) * (1 - t) + closed(kb[2]) * t)) / angle)
        if two_handed:
            off_curl, off_pole = offs[i]
            oa = off_start if a == 0 else offs[a]
            worst = max(worst, (off_pole - oa[1].lerp(offs[b][1], t)).length / position,
                        abs(closed(off_curl) - (closed(oa[0]) * (1 - t) + closed(offs[b][0]) * t)) / angle)
        return worst

    # Keys: the phase ends always (the track starts at rest, frame 0), then the fewest the straight blends need.
    keys = reduce(len(frames), lambda kept: linear_worst(kept, off), {0, strike, end, len(frames) - 1})[1:]

    out = Path(spec.get("out", cli.out))
    motions = json.loads(out.read_text())
    motion = motions["motions"].setdefault(spec["motion"], {"poses": {"rest": {"position": [0, 0, 0], "rotation": [0, 0, 0]}}, "tracks": {}})
    prefix = spec["action"]
    for name in [n for n in motion["poses"] if n.startswith(f"{prefix}-")]:
        del motion["poses"][name]
    track = []
    for n, i in enumerate(keys, 1):
        grip, turn, curl, lean, pole = poses[i]
        if i == len(frames) - 1 and grip.length < 1e-3 and turn.angle < math.radians(0.5):
            pose = "rest"
        else:
            pose = f"{prefix}-{n}"
            motion["poses"][pose] = {"position": [round(v, 3) for v in grip], "rotation": degrees(turn)}
        phase, at = timed(i)
        closure = 0 if spec.get("noClosure") else round(max(-1, min(1, curl / 40)), 2)
        key = {"pose": pose, "phase": phase, "at": at, "ease": "Linear", "grip": closure,
               "shoulder": [round(v, 3) for v in lean], "elbow": [round(v, 3) for v in pole]}
        if two_handed:
            key.update(offGrip=0 if spec.get("noClosure") else round(max(-1, min(1, offs[i][0] / 40)), 2),
                       offElbow=[round(v, 3) for v in offs[i][1]])
        track.append(key)
    motion["tracks"][spec["action"]] = track
    write(out, motions)

    seated = ""
    if spec.get("look"):
        looks_path = Path(spec.get("looks", cli.looks))
        looks = json.loads(looks_path.read_text())
        look = next(entry for entry in looks["looks"] if entry["id"] == spec["look"])
        look["motion"] = spec["motion"]
        if spec.get("seat"):
            tip_way, up_way = HOLDS[spec.get("hold", "fist")]

            def most(along, exclude=None):
                """The weapon bone's signed axis that runs most along a direction of the hand's canonical frame."""
                names = [n for n in NAMES if exclude is None or n[1] != exclude[1]]
                return max(names, key=lambda n: (rest_axes @ axis(n)).dot(along))
            blade = spec.get("blade") or most(tip_way)
            edge = spec.get("edge") or most(up_way, blade)
            tip, up = axis(blade), axis(edge)
            # The look's own frame (length along -Z, up side +Y) in the weapon bone's axes, then the turn its stance gives.
            frame = Matrix((up.cross(-tip), up, -tip)).transposed().to_quaternion()
            stance = json.loads(Path(spec.get("stances", cli.stances)).read_text())["stances"][look["stance"]]
            sgrip, toward, sup = (Vector(stance[k]) for k in ("grip", "toward", "up"))
            seat_turn = (rest_turn @ frame @ between(Vector((0, 0, -1)), Vector((0, 1, 0)), toward - sgrip, sup).inverted()).normalized()
            look["nudge"] = {"grip": [round(v, 3) for v in rest_grip - sgrip], "turn": degrees(seat_turn)}
            # The hand holds the look as the source hand holds its weapon: its canonical frame from the look's.
            look["grips"]["hand"] = degrees((rest_turn @ frame).inverted() @ rest_hand)
            seated = f"; {spec['look']} seated where the source rests ({blade} length, {edge} up)"
            if two_handed:
                # The other hand's palm on the same look at rest, in look space (the look's grip at the weapon bone), and
                # its hold: its canonical frame from the look's, as is.
                look_turn = (rest_turn @ frame).normalized()
                off_grip = look_turn.inverted() @ (rest_off[0] - rest_grip) + Vector(look["grips"]["main"])
                look["grips"]["off"] = [round(v, 3) for v in off_grip]
                look["grips"]["offHand"] = degrees(look_turn.inverted() @ rest_off[1])
                seated += f", off grip {look['grips']['off']}"
        looks_path.write_text(json.dumps(looks, indent=2) + "\n")
    name = spec.get("name", Path(spec["clip"]).stem)
    print(f"BAKED {name}: {Path(spec['clip']).name} -> motions.{spec['motion']}.tracks.{spec['action']}: {len(frames)} frames at "
          f"{bpy.context.scene.render.fps} fps, strike frame {frames[strike]}, commit to {frames[end]}, {len(track)} keys{seated}")


def fps():
    return bpy.context.scene.render.fps / bpy.context.scene.render.fps_base


def motion_of(spec):
    out = Path(spec.get("out", cli.out))
    motions = json.loads(out.read_text())
    return out, motions, motions["motions"].setdefault(spec["motion"], {"poses": {"rest": {"position": [0, 0, 0], "rotation": [0, 0, 0]}}, "tracks": {}})


def bake_loop(spec, poses, frames):
    """
    A sway loop (`state`: idle, walk or sprint): offsets from the set's rest around the clip (a cycle, its last frame
    its first), keyed where the closed curve the game draws through them (HeldLoop.At) would miss the source by the
    tolerances. Its cycle is the clip's seconds for idle; walking and sprinting give theirs in metres, the clip's seconds
    at our `speed` (m/s), so the bob keeps the source's step cadence at our pace.
    """
    seconds = (len(frames) - 1) / fps()
    cycle = seconds if spec["state"] == "idle" else seconds * spec["speed"]
    position, angle = tolerance(spec)
    count = len(frames) - 1
    # Each frame as a loop key stores it (tenth-of-a-millimetre and tenth-of-a-degree steps).
    keyed = [([round(v, 4) for v in grip], [round(v, 1) for v in euler(turn)]) for grip, turn, _, _, _ in poses[:count]]
    # The source, judged at every frame and halfway between (where a curve through fewer keys bulges most): at a frame
    # its pose, between two the curve through every frame.
    every = [k / count for k in range(count)]
    source = []
    for i in range(count):
        source.append((i / count, i, poses[i][0], poses[i][1]))
        move, turn = loop_at(every, keyed, (i + 0.5) / count)
        source.append(((i + 0.5) / count, (i + 1) % count, move, turned(turn)))

    def worst(kept):
        at, values = [k / count for k in kept], [keyed[k] for k in kept]
        far, where = 0.0, None
        for phase, frame, move, turn in source:
            drawn, drawn_turn = loop_at(at, values, phase)
            off = max((drawn - move).length / position, math.degrees(turn.rotation_difference(turned(drawn_turn)).angle) / angle)
            if off > far:
                far, where = off, frame
        return far, where
    # Start from four keys evenly around (a closed curve needs a few to bend through).
    kept = reduce(count, worst, {round(k * count / 4) for k in range(4)})
    keys = [{"at": round(k / count, 3), "position": keyed[k][0], "rotation": keyed[k][1]} for k in kept]
    out, motions, motion = motion_of(spec)
    motion.setdefault("loops", {})[spec["state"]] = {"cycle": round(cycle, 3), "keys": keys}
    write(out, motions)
    print(f"BAKED {spec.get('name')}: {Path(spec['clip']).name} -> motions.{spec['motion']}.loops.{spec['state']}: {len(keys)} keys "
          f"of {count} frames, cycle {cycle:.2f} {'s' if spec['state'] == 'idle' else 'm'}")


def loop_at(at, values, phase):
    """HeldLoop.At: a loop's (move, Euler degrees) at a phase, on the closed Catmull-Rom curve through its keys."""
    n = len(at)
    i = max((k for k in range(n) if at[k] <= phase), default=n - 1)
    a, b = at[i], at[(i + 1) % n]
    span, into = (b - a + 1) % 1, (phase - a + 1) % 1
    t = 0 if span <= 0 else into / span
    p0, p1, p2, p3 = (values[(i + d) % n] for d in (-1, 0, 1, 2))

    def curve(c):
        v0, v1, v2, v3 = (Vector(p[c]) for p in (p0, p1, p2, p3))
        return 0.5 * (2 * v1 + (v2 - v0) * t + (2 * v0 - 5 * v1 + 4 * v2 - v3) * t * t + (3 * v1 - v0 - 3 * v2 + v3) * t * t * t)
    return curve(0), curve(1)


def bake_transition(spec, kind, poses, frames):
    """
    How a look is drawn (`equip`) or put away (`unequip`): keys through poses from the set's rest, kept where a straight
    blend would miss by the tolerances, each at its fraction of the clip, over the clip's seconds times `pace` (below 1
    for a quicker swap than the source's).
    """
    position, angle = tolerance(spec)

    def off(i, a, b):
        t = (i - a) / (b - a)
        grip = poses[a][0].lerp(poses[b][0], t)
        turn = poses[a][1].slerp(poses[b][1], t)
        return max((poses[i][0] - grip).length / position, math.degrees(poses[i][1].rotation_difference(turn).angle) / angle)
    keys = reduce(len(frames), lambda kept: linear_worst(kept, off), {0, len(frames) - 1})
    out, motions, motion = motion_of(spec)
    for name in [n for n in motion["poses"] if n.startswith(f"{kind}-")]:
        del motion["poses"][name]
    track = []
    for n, i in enumerate(keys, 1):
        grip, turn, _, _, _ = poses[i]
        pose = f"{kind}-{n}"
        motion["poses"][pose] = {"position": [round(v, 3) for v in grip], "rotation": degrees(turn)}
        track.append({"pose": pose, "at": round(i / (len(frames) - 1), 3), "ease": "Linear"})
    seconds = (len(frames) - 1) / fps() * spec.get("pace", 1.0)
    motion[kind] = {"seconds": round(seconds, 3), "keys": track}
    write(out, motions)
    print(f"BAKED {spec.get('name')}: {Path(spec['clip']).name} -> motions.{spec['motion']}.{kind}: {len(track)} keys over {seconds:.2f} s")


def tolerance(spec):
    """How far (metres, degrees) a kept key's curve may stray from the source: the entry's, given in mm and degrees."""
    given = spec["tolerance"]
    return given["position"] / 1000, given["angle"]


def kind_of(entry):
    """Which of the recipe's tolerances an entry bakes by: action, transition, or its loop's state."""
    kind = entry.get("kind", "action")
    return "transition" if kind in ("equip", "unequip") else entry["state"] if kind == "loop" else kind


def reduce(count, worst, fixed):
    """
    The fewest frames (of count) to key, as sorted indices, starting from the fixed ones: worst(kept) gives the frame the
    curve through the kept keys strays from most and how far (scaled to the tolerances; 1 is at them). The worst frame
    is keyed until every frame is within, then each key not fixed is dropped while the curve without it stays within.
    """
    kept = sorted(fixed)
    while True:
        far, where = worst(kept)
        # Within, or as near as its keys' rounding lets the curve come (the worst frame is already a key).
        if far <= 1 or where in kept:
            break
        kept = sorted(set(kept) | {where})
    for k in [k for k in kept if k not in fixed]:
        fewer = [j for j in kept if j != k]
        if worst(fewer)[0] <= 1:
            kept = fewer
    return kept


def linear_worst(kept, off):
    """For reduce: the frame straight blends between kept keys stray from most, off(i, a, b) for frame i between a and b."""
    return max(((off(i, a, b), i) for a, b in zip(kept, kept[1:]) for i in range(a + 1, b)), default=(0.0, None))


def write(out, motions):
    """motion.json as two-space JSON, with each number list (a position, rotation, lean or pole) on one line."""
    text = json.dumps(motions, indent=2)
    text = re.sub(r"\[\s*(-?[\d.eE+-]+(?:,\s*-?[\d.eE+-]+)*)\s*\]", lambda m: "[" + ", ".join(re.split(r",\s*", m.group(1))) + "]", text)
    out.write_text(text + "\n")


def euler(q):
    """Degrees about X, Y, Z that Frames.Turn (System.Numerics yaw Y, pitch X, roll Z) turns back into q, in camera axes."""
    m = q.to_matrix()
    cx, cy, cz = m.col[0], m.col[1], m.col[2]
    return [math.degrees(v) for v in (math.asin(max(-1, min(1, -cz.y))), math.atan2(cz.x, cz.z), math.atan2(cx.y, cy.y))]


def degrees(q):
    return [round(v, 1) for v in euler(q)]


def turned(d):
    """Frames.Turn: the turn of degrees about X (pitch), Y (yaw) and Z (roll), as System.Numerics composes them."""
    x, y, z = (math.radians(v) for v in d)
    return (Matrix.Rotation(y, 3, "Y") @ Matrix.Rotation(x, 3, "X") @ Matrix.Rotation(z, 3, "Z")).to_quaternion()


def axis(name):
    return Vector({"x": (1, 0, 0), "y": (0, 1, 0), "z": (0, 0, 1)}[name[1]]) * (-1 if name[0] == "-" else 1)


def basis(along, hint):
    a = along.normalized()
    b = (hint - a * hint.dot(a)).normalized()
    return a, b, a.cross(b)


def between(tip, up, toward, up_toward):
    """Frames.Between: the turn taking tip to toward and up as near up_toward as it can."""
    a, b, c = basis(tip, up)
    x, y, z = basis(toward, up_toward)
    image = [x * e.dot(a) + y * e.dot(b) + z * e.dot(c) for e in (Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1)))]
    return Matrix((image[0], image[1], image[2])).transposed().to_quaternion()


if cli.recipe:
    recipe = json.loads(cli.recipe.read_text())
    for entry in recipe["bakes"]:
        if cli.only and entry["name"] != cli.only:
            continue
        # A cat clip is read as unity-import converted it: models/<its Assets path>.glb.
        def where(path):
            return cli.converted / "models" / Path(path).with_suffix(".glb") if entry.get("source") == "cat" else cli.assets / path
        bake(dict(entry, clip=str(where(entry["clip"])), rest=str(where(entry["rest"])) if entry.get("rest") else None,
                  tolerance=entry.get("tolerance", recipe["tolerance"][kind_of(entry)])))
else:
    bake({"clip": str(cli.clip), "rest": str(cli.rest) if cli.rest else None, "from": cli.start, "to": cli.stop, "motion": cli.motion, "action": cli.action, "look": cli.look,
          "hand": cli.hand, "source": cli.source, "weapon": cli.weapon, "hold": cli.hold, "blade": cli.blade, "edge": cli.edge,
          "strike": cli.strike, "commitEnd": cli.commit_end, "tolerance": {"position": cli.position, "angle": cli.angle}, "slow": cli.slow,
          "scale": cli.scale, "seat": cli.seat, "noClosure": cli.no_closure, "twoHanded": cli.two_handed})
