"""Builds the old game's first-person arms as Engine GLBs (run by scripts/import-fp-arms.sh under Blender).

painted: the Hands pack's arms (a 3ds Max CAT rig) from unity-import's GLB of Hands_Gun_03_Mesh, with the revolver's
         meshes removed and the old game's hand-painted texture (FirstPersonArms/Hand_Recolor.png) as base colour.
leather: MedievalFPArms' FPArms_Male-Leather-Armor (the UE4 mannequin rig the old ArmsCopy prefab used) with its
         Medium colour and normal maps, and the pack's unarmed idle as its clip (the Engine admits a skin with a clip).

usage: blender -b --factory-startup --python scripts/build-fp-arms.py -- ASSETS PAINTED_SOURCE_GLB OUT_DIR
"""
import sys
from pathlib import Path

import bpy

assets, painted_source, out = (Path(a) for a in sys.argv[sys.argv.index("--") + 1:])
more = assets / "MoreArt"


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def load(path, size):
    """An image, scaled down to at most size pixels a side (the pack's maps are 4K)."""
    image = bpy.data.images.load(str(path))
    if max(image.size) > size:
        image.scale(size, size)
    return image


def textured(material, colour, normal=None, size=1024):
    """Gives a material an image as its base colour, and optionally a normal map."""
    material.use_nodes = True
    nodes, links = material.node_tree.nodes, material.node_tree.links
    bsdf = next(n for n in nodes if n.type == "BSDF_PRINCIPLED")
    image = nodes.new("ShaderNodeTexImage")
    image.image = load(colour, size)
    links.new(image.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.75
    bsdf.inputs["Metallic"].default_value = 0.0
    if normal is not None:
        tex = nodes.new("ShaderNodeTexImage")
        tex.image = load(normal, size)
        tex.image.colorspace_settings.name = "Non-Color"
        map_node = nodes.new("ShaderNodeNormalMap")
        links.new(tex.outputs["Color"], map_node.inputs["Color"])
        links.new(map_node.outputs["Normal"], bsdf.inputs["Normal"])


def export(path):
    path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=str(path), export_format="GLB", export_animation_mode="ACTIONS",
                              export_image_format="AUTO", export_yup=True)
    print("BUILT", path)


# painted
reset()
bpy.ops.import_scene.gltf(filepath=str(painted_source))
for obj in list(bpy.data.objects):
    if obj.type == "MESH" and not any(s.material and s.material.name == "Arms_Mesh" for s in obj.material_slots):
        bpy.data.objects.remove(obj, do_unlink=True)
for obj in bpy.data.objects:
    if obj.type == "MESH":
        for slot in obj.material_slots:
            textured(slot.material, more / "FirstPersonArms/Hand_Recolor.png", size=2048)
export(out / "painted.glb")

# leather
reset()
pack = more / "FpMelee/MedievalFPArms"
bpy.ops.import_scene.fbx(filepath=str(pack / "Meshes/FPArms_Male-Leather-Armor_LPR_LOD0.fbx"))
rig = next(o for o in bpy.data.objects if o.type == "ARMATURE")
for action in list(bpy.data.actions):
    bpy.data.actions.remove(action)
before = set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=str(pack / "Animations/CHANM_FPArms_Unarmed_Idle.fbx"))
idle = bpy.data.actions[0]
idle.name = "idle"
for obj in set(bpy.data.objects) - before:
    bpy.data.objects.remove(obj, do_unlink=True)
rig.animation_data_create()
rig.animation_data.action = idle
if hasattr(rig.animation_data, "action_slot") and idle.slots:
    rig.animation_data.action_slot = idle.slots[0]
for obj in bpy.data.objects:
    if obj.type == "MESH":
        for slot in obj.material_slots:
            textured(slot.material, pack / "Textures/FPArms_Male-Leather-Armor_Medium_COL.png",
                     pack / "Textures/FPArms_Male-Leather-Armor_NRM.png")
export(out / "leather.glb")
