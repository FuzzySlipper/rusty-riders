"""Bakes old first-person clips into Rusty Riders held motion (content/held/motion.json).

The old first-person clips animate whole arms with a weapon at the hand. A held look's motion is the weapon alone, in
camera space, from where its stance seats it: a pose moves the grip and turns the look about it. So each frame becomes
the weapon bone's motion from rest, seen from the eye: its grip moved (metres, camera right, up, back) and turned about
the grip (degrees about camera X, Y, Z, as Frames.Turn reads them). The arms are not baked as a skeleton: ours reach the
grip by IK on any rig. What the source arm does that IK cannot infer is baked beside each key: the hand's closure (from
the source fingers' curl), the body's lean (the upper arm's move from rest, "shoulder") and the elbow's bend ("elbow").

Frames split into the action's phases (windup up to the strike, commit while the weapon is fast, recovery to the end):
`from` and `to` bake part of a clip. By default the strike is the frame the grip moves fastest and the commit ends when it slows below `slow` of that peak;
`strike` and `commitEnd` (a frame, or a fraction of the clip when below 1) override. Keys are kept only where the
motion bends more than `position` metres or `angle` degrees from a straight blend between kept keys, each timed by its
phase and its moment through it, so it lands on the same beat whatever the action's own timing.

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
      [--blade AXIS] [--edge AXIS] [--strike F] [--commit-end F] [--no-closure] [--position 0.02] [--angle 8] [--slow 0.4]
      [--scale 1]
"""
import argparse
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector

args = argparse.ArgumentParser()
args.add_argument("--recipe", type=Path, help="a JSON list of bakes (art/held-motion/bakes.json)")
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
args.add_argument("--position", type=float, default=0.02)
args.add_argument("--angle", type=float, default=8.0)
args.add_argument("--slow", type=float, default=0.4)
args.add_argument("--scale", type=float, default=1.0, help="multiplies the grip's movement")
args.add_argument("--seat", action="store_true")
args.add_argument("--no-closure", action="store_true", help="keep the hand on its grip (a gesture's open hand would drop a wand)")
args.add_argument("--two-handed", action="store_true", help="bake the other hand on the same weapon too (its off grip, hold, closure and elbow)")
args.add_argument("--out", type=Path, default=Path("content/held/motion.json"))
args.add_argument("--looks", type=Path, default=Path("content/held/looks.json"))
args.add_argument("--stances", type=Path, default=Path("content/held/stances.json"))
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

    position, angle = spec.get("position", 0.02), spec.get("angle", 8.0)

    def bent(a, b):
        """The frame between keys a and b furthest from a straight blend, and how far (scaled to the tolerances)."""
        worst, at = 0.0, None
        for i in range(a + 1, b):
            t = (i - a) / (b - a)
            grip = poses[a][0].lerp(poses[b][0], t)
            turn = poses[a][1].slerp(poses[b][1], t)
            lean = poses[a][3].lerp(poses[b][3], t)
            off = max((poses[i][0] - grip).length / position, (poses[i][3] - lean).length / position,
                      math.degrees(poses[i][1].rotation_difference(turn).angle) / angle)
            if off > worst:
                worst, at = off, i
        return worst, at

    # Keys: the phase ends always, then wherever a straight blend between keys bends past the tolerances.
    keys = sorted({strike, end, len(frames) - 1})
    while True:
        split = next((at for a, b in zip([0] + keys, keys) for worst, at in [bent(a, b)] if worst > 1), None)
        if split is None:
            break
        keys = sorted(set(keys) | {split})

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
    out.write_text(json.dumps(motions, indent=2) + "\n")

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


def degrees(q):
    """Degrees about X, Y, Z that Frames.Turn (System.Numerics yaw Y, pitch X, roll Z) turns back into q, in camera axes."""
    m = q.to_matrix()
    cx, cy, cz = m.col[0], m.col[1], m.col[2]
    return [round(math.degrees(v), 1) for v in (math.asin(max(-1, min(1, -cz.y))), math.atan2(cz.x, cz.z), math.atan2(cx.y, cy.y))]


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
    for entry in json.loads(cli.recipe.read_text()):
        if cli.only and entry["name"] != cli.only:
            continue
        # A cat clip is read as unity-import converted it: models/<its Assets path>.glb.
        def where(path):
            return cli.converted / "models" / Path(path).with_suffix(".glb") if entry.get("source") == "cat" else cli.assets / path
        bake(dict(entry, clip=str(where(entry["clip"])), rest=str(where(entry["rest"])) if entry.get("rest") else None))
else:
    bake({"clip": str(cli.clip), "rest": str(cli.rest) if cli.rest else None, "from": cli.start, "to": cli.stop, "motion": cli.motion, "action": cli.action, "look": cli.look,
          "hand": cli.hand, "source": cli.source, "weapon": cli.weapon, "hold": cli.hold, "blade": cli.blade, "edge": cli.edge,
          "strike": cli.strike, "commitEnd": cli.commit_end, "position": cli.position, "angle": cli.angle, "slow": cli.slow,
          "scale": cli.scale, "seat": cli.seat, "noClosure": cli.no_closure, "twoHanded": cli.two_handed})
