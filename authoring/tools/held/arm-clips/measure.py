"""Where an arm clip puts the right hand and elbow in the view, frame by frame, for any first-person arms. In Blender:

    blender -b --factory-startup --python authoring/tools/held/arm-clips/measure.py -- ARMS.glb CLIP[,CLIP...] OUT.json
        [--rig ID] [--hand BONE] [--elbow BONE] [--camera X,Y,Z] [--fov DEGREES] [--aspect W/H] [--pack PACK.glb]

The camera is a point in the arms model's glTF space looking along +Z (Y up, right toward -X), as both the PSX pack and
an imported Source view model stand. For each frame: the hand's bearing from the view (degrees right and up of its
forward), the hand's and the elbow's distance from the camera, and whether the hand is on screen at that vertical field
of view and aspect. The summary per clip is what to compare across sources and rigs: the hand's sweep, the closest
the elbow comes, the share of frames with the hand in view. With --rig (a profile in content/held/rigs), the hand,
elbow and camera default to that rig's in Rusty Riders' view (rig.py); without it, to the PSX arms'.
"""
import json, math, os, sys
import bpy
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
arms_path, clips, out = args[0], args[1].split(","), args[2]
opt = dict(zip(args[3::2], args[4::2]))
if "--rig" in opt:
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    from rig import Rig
    rig = Rig(opt["--rig"]); bones = rig.profile["main"]
    hand, elbow = opt.get("--hand", bones["hand"]), opt.get("--elbow", bones["lower"])
    camera = Vector([float(v) for v in opt["--camera"].split(",")]) if "--camera" in opt else Vector(rig.camera().tolist())
else:
    hand, elbow = opt.get("--hand", "hand.R"), opt.get("--elbow", "forearm.R")
    camera = Vector([float(v) for v in opt.get("--camera", "0,1.743,0").split(",")])
fov = math.radians(float(opt.get("--fov", 75))); aspect = eval(opt.get("--aspect", "16/9"), {})

for o in list(bpy.data.objects): bpy.data.objects.remove(o)
bpy.ops.import_scene.gltf(filepath=arms_path)
arms = next(o for o in bpy.data.objects if o.type == "ARMATURE")
if "--pack" in opt:
    bpy.ops.import_scene.gltf(filepath=opt["--pack"])
    for o in [o for o in bpy.data.objects if o.type == "ARMATURE" and o != arms]: bpy.data.objects.remove(o)


def gltf(v): return Vector((v.x, v.z, -v.y))  # Blender Z-up back to glTF Y-up


def view(p):
    d = gltf(p) - camera
    return math.degrees(math.atan2(-d.x, d.z)), math.degrees(math.atan2(d.y, math.hypot(d.x, d.z))), d


tan_v = math.tan(fov / 2); tan_h = tan_v * aspect
report = {}
for clip in clips:
    named = [a for a in bpy.data.actions if a.name == clip or a.name.startswith(clip + ".")]
    if not named: sys.exit(f"measure: no clip {clip!r}")
    action = named[-1]
    arms.animation_data_create(); arms.animation_data.action = action
    if hasattr(arms.animation_data, "action_slot") and action.slots: arms.animation_data.action_slot = action.slots[0]
    f0, f1 = (int(round(v)) for v in action.frame_range)
    rows = []
    for f in range(f0, f1 + 1):
        bpy.context.scene.frame_set(f)
        h = arms.matrix_world @ arms.pose.bones[hand].head; e = arms.matrix_world @ arms.pose.bones[elbow].head
        yaw, pitch, d = view(h); _, _, de = view(e)
        seen = d.z > 0 and abs(d.x / d.z) <= tan_h and abs(d.y / d.z) <= tan_v
        rows.append({"yaw": round(yaw, 1), "pitch": round(pitch, 1), "hand": round(d.length, 3), "elbow": round(de.length, 3), "seen": seen})
    fps = bpy.context.scene.render.fps / bpy.context.scene.render.fps_base
    report[clip] = {
        "seconds": round((f1 - f0) / fps, 3),
        "yaw": [min(r["yaw"] for r in rows), max(r["yaw"] for r in rows)],
        "pitch": [min(r["pitch"] for r in rows), max(r["pitch"] for r in rows)],
        "handDistance": [min(r["hand"] for r in rows), max(r["hand"] for r in rows)],
        "elbowClosest": min(r["elbow"] for r in rows),
        "handSeen": round(sum(r["seen"] for r in rows) / len(rows), 2),
        "frames": rows,
    }
    s = report[clip]
    print(f"measure: {clip}: {s['seconds']} s, hand yaw {s['yaw'][0]}..{s['yaw'][1]}, pitch {s['pitch'][0]}..{s['pitch'][1]}, "
          f"{s['handDistance'][0]}..{s['handDistance'][1]} m away, elbow at least {s['elbowClosest']} m, hand seen {s['handSeen']:.0%}")
json.dump(report, open(out, "w"))
