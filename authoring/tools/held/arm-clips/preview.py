"""A clip pack's clip on a rig's arms, seen from the game's view, as one contact sheet. Runs in Blender:

    blender -b --factory-startup --python authoring/tools/held/arm-clips/preview.py -- PACK.glb CLIP SHEET.png [--rig painted] [--frames N]
        [--blade X,Y,Z] [--from game|side|front|above]

The rig (content/held/rigs/<rig>.json, painted by default) gives the arms model, the main hand and the palm. The camera is
where the game puts the view for the rig's clip-led arms (shared shoulder plus the rig's carry in
content/held/clips.json, rig.py), looking along the arms' +Z, with the walker's 75 degree vertical field of view. A
thin bar from the right hand's palm stands in for a blade so its path reads; --blade is its direction in the hand
bone's own frame. Frames are evenly spaced over the clip and labelled by phase (0..1) for placing ClipMarks.
"""
import math, os, subprocess, sys, tempfile
import bpy
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
pack, clip, sheet = args[0], args[1], args[2]
opt = dict(zip(args[3::2], args[4::2]))
count = int(opt.get("--frames", 12))
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from rig import Rig
rig = Rig(opt.get("--rig", "painted"))
hand = rig.profile["main"]["hand"]
eye = rig.camera()

for o in list(bpy.data.objects): bpy.data.objects.remove(o)  # the factory scene's cube, light and camera
bpy.ops.import_scene.gltf(filepath=rig.model)
arms = next(o for o in bpy.data.objects if o.type == "ARMATURE")
before = set(bpy.data.actions)
bpy.ops.import_scene.gltf(filepath=pack)
for o in [o for o in bpy.data.objects if o.type == "ARMATURE" and o != arms]: bpy.data.objects.remove(o)
named = [a for a in bpy.data.actions if a.name == clip or a.name.startswith(clip + ".")]
action = next((a for a in named if a not in before), named[0] if named else None)  # the pack's own, else the arms' copy
if action is None: sys.exit(f"preview: no clip {clip!r} in {pack}; new actions {[a.name for a in bpy.data.actions if a not in before]}")
arms.animation_data_create(); arms.animation_data.action = action
if hasattr(arms.animation_data, "action_slot") and action.slots: arms.animation_data.action_slot = action.slots[0]

# Blade stand-in: 70 cm out of the fist along the hand bone's --blade axis, from near the palm. Bone-parented
# objects sit at the bone's tail, in its frame, in the armature's units (a rig imported at a scale divides by it).
axis = Vector([float(v) for v in opt.get("--blade", "1,0,0").split(",")])
units = 1 / arms.matrix_world.to_scale().x
bpy.ops.mesh.primitive_cube_add(size=1)
blade = bpy.context.object; blade.scale = (Vector((0.012, 0.012, 0.012)) + axis * 0.7) * units
blade.parent = arms; blade.parent_type = "BONE"; blade.parent_bone = hand
blade.matrix_parent_inverse.identity()
blade.location = Vector((0, 0.08 * units - arms.pose.bones[hand].length, -0.03 * units)) + axis * 0.35 * units

cam_data = bpy.data.cameras.new("view"); cam_data.sensor_fit = "VERTICAL"; cam_data.angle_y = math.radians(75); cam_data.clip_start = 0.01
cam = bpy.data.objects.new("view", cam_data); bpy.context.scene.collection.objects.link(cam)
# --from side/front/above look at the arms from outside (with a marker where the game's view is) to see where they go.
view = opt.get("--from", "game")
if view == "game":
    cam.location = (eye[0], -eye[2], eye[1]); cam.rotation_euler = (math.radians(90), 0, math.radians(180))
else:
    cam.location = {"side": (-2.4, -0.5, 1.6), "front": (0.4, -2.6, 1.9), "above": (0, -0.5, 4.2)}[view]
    cam_data.angle_y = math.radians(40)
    target = bpy.data.objects.new("target", None); bpy.context.scene.collection.objects.link(target); target.location = (0, -0.45, 1.5)
    track = cam.constraints.new("TRACK_TO"); track.target = target; track.track_axis = "TRACK_NEGATIVE_Z"; track.up_axis = "UP_Y"
    bpy.ops.mesh.primitive_cone_add(radius1=0.05, depth=0.12, location=(eye[0], -eye[2] - 0.06, eye[1]), rotation=(math.radians(90), 0, 0))
scene = bpy.context.scene; scene.camera = cam
scene.render.engine = "BLENDER_WORKBENCH"; scene.render.resolution_x, scene.render.resolution_y = 480, 270
scene.display.shading.light = "STUDIO"; scene.display.shading.color_type = "OBJECT"
scene.world = scene.world or bpy.data.worlds.new("w"); scene.render.film_transparent = False

f0, f1 = action.frame_range
tmp = tempfile.mkdtemp()
for k in range(count):
    t = k / (count - 1)
    scene.frame_set(int(round(f0)), subframe=0); scene.frame_float = f0 + (f1 - f0) * t
    scene.render.filepath = os.path.join(tmp, f"f{k:02d}.png"); bpy.ops.render.render(write_still=True)
cols = 4; rows = math.ceil(count / cols)
label = ",".join(f"drawtext=text='{k / (count - 1):.2f}':x=8+{k % cols}*480:y=8+{k // cols}*270:fontsize=22:fontcolor=white:box=1:boxcolor=black@0.5"
                 for k in range(count))
subprocess.run(["ffmpeg", "-loglevel", "error", "-y", "-framerate", "1", "-i", os.path.join(tmp, "f%02d.png"),
                "-vf", f"tile={cols}x{rows},{label}", "-frames:v", "1", sheet], check=True)
print(f"preview: {clip} {f1 - f0:.0f} frames -> {sheet}")
