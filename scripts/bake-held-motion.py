"""Bakes an old first-person FBX clip into Rusty Riders held motion (content/held/motion.json).

The old FPP melee clips (old-game/Assets/MoreArt/FpMelee/FPP_*) animate whole arms on the UE4 mannequin rig with a
weapon_r (and weapon_l) bone at each grip. A held look's motion is the weapon alone, in camera space, from where its
stance seats it: a pose moves the grip and turns the look about it. So each frame becomes the weapon bone's motion
from rest, seen from the camera bone: its grip moved (metres, camera right, up, back) and turned about the grip
(degrees about camera X, Y, Z, as Frames.Turn reads them). The arms are not baked: our arms reach the grip by IK on any
rig, and the hand's closure is taken from the source hand's finger curl.

The frames are split into the action's phases (windup up to the strike, commit while the blade is fast, recovery to
the end): by default the strike is the frame the grip moves fastest and the commit ends when it slows below
--slow of that peak; --strike and --commit-end (frames) override. Then keys are kept only where the motion bends
more than --position metres or --angle degrees from a straight blend between kept keys, and each key is timed by its
phase and its moment through it, so it lands on the same beat whatever the action's own timing.

usage:
  blender -b --factory-startup --python scripts/bake-held-motion.py -- CLIP.fbx --rest REST.fbx --motion NAME
      --action ACTION [--look LOOK] [--hand r|l] [--strike F] [--commit-end F] [--position 0.02] [--angle 8]
      [--slow 0.4] [--scale 1] [--seat [--blade +y] [--edge +z]] [--out content/held/motion.json] [--looks ...] [--stances ...]

Each key also carries the body's lean (the upper arm's move from rest, as "shoulder"), so a lunging blow reaches as
far as the source's, and the source elbow's bend as the point the arm's elbow bends toward ("elbow"), so the forearm
follows the source's line. With --seat the look's nudge is set so it rests exactly where the source weapon rests (its
blade along --blade and its up side along --edge of the weapon bone; by default the axis leaving the fist past the index
finger, and the one along the fingers), so its turns about the grip follow the source
path, and its grips.hand to the source hand's hold on the weapon, so the wrist sits as the source's does.
"""
import argparse
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Quaternion, Vector

args = argparse.ArgumentParser()
args.add_argument("clip", type=Path)
args.add_argument("--rest", type=Path, required=True, help="a clip whose first frame is the weapon at rest (the set's idle)")
args.add_argument("--motion", required=True, help="the motion in motion.json to write the poses and track into")
args.add_argument("--action", required=True, help="the action id the track is for")
args.add_argument("--look", help="a look in looks.json to move by this motion")
args.add_argument("--hand", default="r", choices=["r", "l"])
args.add_argument("--strike", type=int)
args.add_argument("--commit-end", type=int)
args.add_argument("--position", type=float, default=0.02)
args.add_argument("--angle", type=float, default=8.0)
args.add_argument("--slow", type=float, default=0.4)
args.add_argument("--scale", type=float, default=1.0, help="multiplies the grip's movement")
args.add_argument("--out", type=Path, default=Path("content/held/motion.json"))
args.add_argument("--looks", type=Path, default=Path("content/held/looks.json"))
args.add_argument("--stances", type=Path, default=Path("content/held/stances.json"))
args.add_argument("--seat", action="store_true",
                  help="set the look's nudge so it rests where the source weapon rests (needs --look), so turns about the grip follow the source's path")
args.add_argument("--blade", default="auto",
                  help="the weapon bone's axis along the blade, toward the tip (+x, -x, +y, -y, +z or -z); auto: the one leaving the fist past the index finger")
args.add_argument("--edge", default="auto", help="the weapon bone's axis toward the look's up side; auto: the one most along the fingers")
o = args.parse_args(sys.argv[sys.argv.index("--") + 1:])

# The mannequin faces -Y with +Z up in Blender: camera right is -X, up is +Z, back is +Y.
TO_CAMERA = Matrix(((-1, 0, 0), (0, 0, 1), (0, 1, 0)))
# The camera-space mirror across the view's centre line: held motion is authored for the main (right) hand.
MIRROR = Matrix(((-1, 0, 0), (0, 1, 0), (0, 0, 1)))
FINGERS = ["index", "middle", "ring", "pinky"]


def load(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(path))
    rig = next(obj for obj in bpy.data.objects if obj.type == "ARMATURE")
    return rig, rig.animation_data.action


def sample(rig, frame):
    """
    The weapon bone's grip and turn and the upper arm's place, in camera axes relative to the camera bone (a left
    hand's mirrored into the right's), and the hand's finger curl (degrees).
    """
    bpy.context.scene.frame_set(frame)
    def world(name):
        return rig.matrix_world @ rig.pose.bones[name].matrix
    weapon, camera, shoulder = world(f"weapon_{o.hand}"), world("camera"), world(f"upperarm_{o.hand}")
    side = MIRROR if o.hand == "l" else Matrix.Identity(3)

    def at(name):
        return side @ TO_CAMERA @ (world(name).translation - camera.translation)
    grip = side @ TO_CAMERA @ (weapon.translation - camera.translation)
    # Mirroring a turn conjugates it by the mirror (the weapon's own axes stay as the bone gives them).
    turn = (side @ TO_CAMERA @ weapon.to_3x3().normalized() @ side).to_quaternion()
    place = side @ TO_CAMERA @ (shoulder.translation - camera.translation)
    curl = 0.0
    for finger in FINGERS:
        for i in (1, 2, 3):
            bone = rig.pose.bones[f"{finger}_0{i}_{o.hand}"]
            curl += math.degrees(bone.matrix_basis.to_quaternion().angle)
    # The elbow's bend as a pole: out from the middle of shoulder and wrist through the elbow, as far again.
    wrist, elbow = at(f"hand_{o.hand}"), at(f"lowerarm_{o.hand}")
    pole = elbow + (elbow - (place + wrist) / 2)
    # The hand's canonical frame (+Y to the middle knuckle, +X from the index knuckle toward the little one, +Z out of
    # the back), mirrored to a right hand's for a left.
    y = (at(f"middle_01_{o.hand}") - wrist).normalized()
    across = at(f"pinky_01_{o.hand}") - at(f"index_01_{o.hand}")
    x = (across - y * across.dot(y)).normalized()
    hand = Matrix((x, y, x.cross(y))).transposed().to_quaternion()
    # The weapon bone's own axes in the hand's canonical frame, to tell which runs along the blade.
    axes = hand.inverted().to_matrix() @ side @ TO_CAMERA @ weapon.to_3x3().normalized()
    return grip, turn, curl / len(FINGERS), place, pole, hand, axes


def degrees(q):
    """Degrees about X, Y, Z that Frames.Turn (System.Numerics yaw Y, pitch X, roll Z) turns back into q, in camera axes."""
    m = q.to_matrix()
    cx, cy, cz = m.col[0], m.col[1], m.col[2]
    x = math.asin(max(-1, min(1, -cz.y)))
    y = math.atan2(cz.x, cz.z)
    z = math.atan2(cx.y, cy.y)
    return [round(math.degrees(v), 1) for v in (x, y, z)]


rig, action = load(o.rest)
rest_grip, rest_turn, rest_curl, rest_place, _, rest_hand, rest_axes = sample(rig, int(action.frame_range[0]))
rig, action = load(o.clip)
first, last = (int(f) for f in action.frame_range)
frames = list(range(first, last + 1))
samples = [sample(rig, f) for f in frames]
# Each frame from rest: the grip's move (camera space) and the turn about it, left of the rest turn as a pose applies
# it; the finger curl; and how far the body leans the shoulder (a lunge lets the arm reach further).
poses = [((grip - rest_grip) * o.scale, (turn @ rest_turn.inverted()).normalized(), curl - rest_curl, (place - rest_place) * o.scale, pole)
         for grip, turn, curl, place, pole, _, _ in samples]

# Phases from the grip's speed (a wrist roll can turn the weapon fastest without carrying the blow).
speed = [0.0] + [(poses[i][0] - poses[i - 1][0]).length for i in range(1, len(poses))]
strike = frames.index(o.strike) if o.strike else max(range(len(speed)), key=speed.__getitem__)
if o.commit_end:
    end = frames.index(o.commit_end)
else:
    end = next((i for i in range(strike + 1, len(speed)) if speed[i] < o.slow * speed[strike]), len(speed) - 1)
# The strike is the end of windup, when the action lands: the frame the grip moves fastest.
bounds = {"Windup": (0, strike), "Commit": (strike, end), "Recovery": (end, len(frames) - 1)}


def timed(i):
    for phase in ("Windup", "Commit", "Recovery"):
        a, b = bounds[phase]
        if i <= b:
            return phase, round((i - a) / max(1, b - a), 3)
    return "Recovery", 1.0


def bent(a, b):
    """The frames between keys a and b furthest from a straight blend, and how far (scaled to the tolerances)."""
    worst, at = 0.0, None
    for i in range(a + 1, b):
        t = (i - a) / (b - a)
        grip = poses[a][0].lerp(poses[b][0], t)
        turn = poses[a][1].slerp(poses[b][1], t)
        lean = poses[a][3].lerp(poses[b][3], t)
        off = max((poses[i][0] - grip).length / o.position, (poses[i][3] - lean).length / o.position,
                  math.degrees(poses[i][1].rotation_difference(turn).angle) / o.angle)
        if off > worst:
            worst, at = off, i
    return worst, at


# Keys: the phase ends always, then wherever a straight blend between keys bends past the tolerances.
keys = sorted({strike, end, len(frames) - 1})
changed = True
while changed:
    changed = False
    for a, b in zip([0] + keys, keys):
        worst, at = bent(a, b)
        if worst > 1:
            keys = sorted(set(keys) | {at})
            changed = True
            break

motions = json.loads(o.out.read_text())
motion = motions["motions"].setdefault(o.motion, {"poses": {"rest": {"position": [0, 0, 0], "rotation": [0, 0, 0]}}, "tracks": {}})
prefix = o.action
for name in [n for n in motion["poses"] if n.startswith(f"{prefix}-")]:
    del motion["poses"][name]
track = []
for n, i in enumerate(keys, 1):
    grip, turn, curl, lean, pole = poses[i]
    if (grip.length < 1e-3 and turn.angle < math.radians(0.5)) and i == len(frames) - 1:
        pose = "rest"
    else:
        pose = f"{prefix}-{n}"
        motion["poses"][pose] = {"position": [round(v, 3) for v in grip], "rotation": degrees(turn)}
    phase, at = timed(i)
    track.append({"pose": pose, "phase": phase, "at": at, "ease": "Linear", "grip": round(max(-1, min(1, curl / 40)), 2),
                  "shoulder": [round(v, 3) for v in lean], "elbow": [round(v, 3) for v in pole]})
motion["tracks"][o.action] = track
o.out.write_text(json.dumps(motions, indent=2) + "\n")
NAMES = ["+x", "-x", "+y", "-y", "+z", "-z"]


def axis(name):
    return Vector({"x": (1, 0, 0), "y": (0, 1, 0), "z": (0, 0, 1)}[name[1]]) * (-1 if name[0] == "-" else 1)


def most(along, exclude=None):
    """The weapon bone's signed axis that runs most along a direction of the hand's canonical frame."""
    names = [n for n in NAMES if exclude is None or n[1] != exclude[1]]
    return max(names, key=lambda n: (rest_axes @ axis(n)).dot(along))


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


seated = ""
if o.look:
    looks = json.loads(o.looks.read_text())
    look = next(look for look in looks["looks"] if look["id"] == o.look)
    look["motion"] = o.motion
    if o.seat:
        # The look's own frame (length along -Z, up side +Y) in the weapon bone's axes, then the turn its stance gives.
        # A fist holds a hilt across the palm: the blade leaves past the index finger (-X in the hand's frame) and its
        # edges face along the fingers (+Y).
        blade = most(Vector((-1, 0, 0))) if o.blade == "auto" else o.blade
        edge = most(Vector((0, 1, 0)), blade) if o.edge == "auto" else o.edge
        tip, edge_axis = axis(blade), axis(edge)
        edge = f"{blade} blade, {edge} edge"
        frame = Matrix((edge_axis.cross(-tip), edge_axis, -tip)).transposed().to_quaternion()
        stance = json.loads(o.stances.read_text())["stances"][look["stance"]]
        grip, toward, up = (Vector(stance[k]) for k in ("grip", "toward", "up"))
        turn = (rest_turn @ frame @ between(Vector((0, 0, -1)), Vector((0, 1, 0)), toward - grip, up).inverted()).normalized()
        look["nudge"] = {"grip": [round(v, 3) for v in rest_grip - grip], "turn": degrees(turn)}
        # The hand holds the look as the source hand holds its weapon: its canonical frame from the look's.
        look["grips"]["hand"] = degrees((rest_turn @ frame).inverted() @ rest_hand)
        seated = f"; {o.look} seated where the source rests ({edge}; nudge {look['nudge']}, hand {look['grips']['hand']})"
    o.looks.write_text(json.dumps(looks, indent=2) + "\n")
print(f"BAKED {o.clip.name} -> motions.{o.motion}.tracks.{o.action}: {len(frames)} frames at "
      f"{bpy.context.scene.render.fps} fps, strike frame {frames[strike]}, commit to {frames[end]}, {len(track)} keys{seated}")
