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
      [--slow 0.4] [--scale 1] [--out content/held/motion.json] [--looks content/held/looks.json]
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
o = args.parse_args(sys.argv[sys.argv.index("--") + 1:])

# The mannequin faces -Y with +Z up in Blender: camera right is -X, up is +Z, back is +Y.
TO_CAMERA = Matrix(((-1, 0, 0), (0, 0, 1), (0, 1, 0)))
FINGERS = ["index", "middle", "ring", "pinky"]


def load(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(path))
    rig = next(obj for obj in bpy.data.objects if obj.type == "ARMATURE")
    return rig, rig.animation_data.action


def sample(rig, frame):
    """The weapon bone's grip and turn in camera axes, relative to the camera bone, and the hand's finger curl (degrees)."""
    bpy.context.scene.frame_set(frame)
    def world(name):
        return rig.matrix_world @ rig.pose.bones[name].matrix
    weapon, camera = world(f"weapon_{o.hand}"), world("camera")
    grip = TO_CAMERA @ (weapon.translation - camera.translation)
    turn = (TO_CAMERA @ weapon.to_3x3().normalized()).to_quaternion()
    curl = 0.0
    for finger in FINGERS:
        for i in (1, 2, 3):
            bone = rig.pose.bones[f"{finger}_0{i}_{o.hand}"]
            curl += math.degrees(bone.matrix_basis.to_quaternion().angle)
    return grip, turn, curl / len(FINGERS)


def degrees(q):
    """Degrees about X, Y, Z that Frames.Turn (System.Numerics yaw Y, pitch X, roll Z) turns back into q, in camera axes."""
    m = q.to_matrix()
    cx, cy, cz = m.col[0], m.col[1], m.col[2]
    x = math.asin(max(-1, min(1, -cz.y)))
    y = math.atan2(cz.x, cz.z)
    z = math.atan2(cx.y, cy.y)
    return [round(math.degrees(v), 1) for v in (x, y, z)]


rig, action = load(o.rest)
rest_grip, rest_turn, rest_curl = sample(rig, int(action.frame_range[0]))
rig, action = load(o.clip)
first, last = (int(f) for f in action.frame_range)
frames = list(range(first, last + 1))
samples = [sample(rig, f) for f in frames]
# Each frame from rest: the grip's move (camera space) and the turn about it, left of the rest turn as a pose applies it.
poses = [((grip - rest_grip) * o.scale, (turn @ rest_turn.inverted()).normalized(), curl - rest_curl) for grip, turn, curl in samples]

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
        off = max((poses[i][0] - grip).length / o.position, math.degrees(poses[i][1].rotation_difference(turn).angle) / o.angle)
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
    grip, turn, curl = poses[i]
    if (grip.length < 1e-3 and turn.angle < math.radians(0.5)) and i == len(frames) - 1:
        pose = "rest"
    else:
        pose = f"{prefix}-{n}"
        motion["poses"][pose] = {"position": [round(v, 3) for v in grip], "rotation": degrees(turn)}
    phase, at = timed(i)
    track.append({"pose": pose, "phase": phase, "at": at, "ease": "Linear", "grip": round(max(-1, min(1, curl / 40)), 2)})
motion["tracks"][o.action] = track
o.out.write_text(json.dumps(motions, indent=2) + "\n")
if o.look:
    looks = json.loads(o.looks.read_text())
    next(look for look in looks["looks"] if look["id"] == o.look)["motion"] = o.motion
    o.looks.write_text(json.dumps(looks, indent=2) + "\n")
print(f"BAKED {o.clip.name} -> motions.{o.motion}.tracks.{o.action}: {len(frames)} frames at "
      f"{bpy.context.scene.render.fps} fps, strike frame {frames[strike]}, commit to {frames[end]}, {len(track)} keys")
