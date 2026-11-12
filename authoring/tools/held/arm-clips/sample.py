"""Any humanoid clip (FBX, glTF/GLB, BVH) -> a joint track for retarget.py. Runs in Blender:

    blender -b --factory-startup --python authoring/tools/held/arm-clips/sample.py -- CLIP_FILE TRACK.json [--clip NAME] [--skeleton ID]

The track is the same few points whatever the source: each arm's shoulder, elbow, wrist and index/pinky knuckles, the
hips, and a "front" point ahead of the chest. Points are bone heads in world space, Y up. --skeleton names a bone map in
skeletons.json (found from the bone names when left out); --clip picks one animation from a file holding several.
"Front" is the chest's forward: the feet's toe direction in the rest pose, carried by the chest bone frame to frame,
so the facing comes from the skeleton and not from where the clip happens to stand.
"""
import json, os, re, sys
import bpy
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
src, out = args[0], args[1]
opt = dict(zip(args[2::2], args[3::2]))
maps = json.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "skeletons.json")))

ext = os.path.splitext(src)[1].lower()
if ext == ".fbx": bpy.ops.import_scene.fbx(filepath=src)
elif ext in (".glb", ".gltf"): bpy.ops.import_scene.gltf(filepath=src)
elif ext == ".bvh": bpy.ops.import_anim.bvh(filepath=src)
else: sys.exit(f"sample: unsupported {ext}")
arm = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")


def bare(name, prefix): return re.sub("^" + prefix, "", name) if prefix else name


def fits(m): return all(any(bare(b.name, m["prefix"]) == v for b in arm.pose.bones) for v in m["points"].values())


skeleton = opt.get("--skeleton") or next((k for k, m in maps.items() if fits(m)), None)
if not skeleton: sys.exit(f"sample: no bone map in skeletons.json fits {src}")
m = maps[skeleton]
bone = {bare(b.name, m["prefix"]): b for b in arm.pose.bones}
points = {k: bone[v] for k, v in m["points"].items()}
chest = bone[m["chest"]]

actions = [a for a in bpy.data.actions]
if "--clip" in opt:
    want = opt["--clip"]
    actions = [a for a in actions if a.name == want or a.name.startswith(want + "_") or a.name.endswith("|" + want)] or \
              [a for a in actions if want in a.name]
if not actions: sys.exit(f"sample: no animation {opt.get('--clip', '')!r}; have {[a.name for a in bpy.data.actions]}")
action = actions[0]
arm.animation_data_create()
arm.animation_data.action = action
if hasattr(arm.animation_data, "action_slot") and action.slots: arm.animation_data.action_slot = action.slots[0]

W = arm.matrix_world
# Rest facing: toes ahead of ankles, flattened, in the chest bone's rest frame.
toe = sum((W @ b.bone.head_local for b in (points["toe.R"], points["toe.L"])), Vector()) / 2
ank = sum((W @ b.bone.head_local for b in (points["ankle.R"], points["ankle.L"])), Vector()) / 2
up = (W @ ((points["shoulder.R"].bone.head_local + points["shoulder.L"].bone.head_local) / 2)
      - W @ ((points["hip.R"].bone.head_local + points["hip.L"].bone.head_local) / 2)).normalized()
fwd = toe - ank; fwd = (fwd - up * fwd.dot(up)).normalized()
chest_rest = (W @ chest.bone.matrix_local).to_3x3().normalized()
front_local = chest_rest.transposed() @ fwd


def yup(v): return [round(v.x, 5), round(v.z, 5), round(-v.y, 5)]


scene = bpy.context.scene
f0, f1 = (int(round(v)) for v in action.frame_range)
names = list(points) + ["front"]
frames = []
for f in range(f0, f1 + 1):
    scene.frame_set(f)
    row = [yup(W @ points[k].head) for k in points]
    cm = W @ chest.matrix
    row.append(yup(cm.translation + cm.to_3x3().normalized() @ front_local * 0.25))
    frames.append(row)

json.dump({"source": os.path.basename(src), "skeleton": skeleton, "clip": action.name,
           "fps": scene.render.fps / scene.render.fps_base, "points": names, "frames": frames}, open(out, "w"))
print(f"sample: {action.name} on {skeleton}, {len(frames)} frames at {scene.render.fps / scene.render.fps_base:g} fps -> {out}")
