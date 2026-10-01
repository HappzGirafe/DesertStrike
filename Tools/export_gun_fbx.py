"""Exports a weapon .blend from Art/Blender to an .fbx the game can load.

Usage (from the project folder):
    A weapon's own model:
    blender -b Art/Blender/Glock18.blend --python Tools/export_gun_fbx.py -- Assets/Resources/Models/Glock18.fbx
    A skin that brings its own model goes in its skin folder as model.fbx:
    blender -b Art/Blender/nogektestskin.blend --python Tools/export_gun_fbx.py -- "Assets/Resources/Skins/Knife/nogektestskin/model.fbx"

The game finds the weapon's parts by name, so meshes are renamed by the texture they use:
  "peredr..." (in front of the handle)  -> "Guard"
  "ruchka" / "rychka" (handle, grip)     -> "Grip"
  "nogen" (knife blade)                  -> "Blade"
  "stvol" (barrel) / "skin" (slide)      -> "Slide"
  untextured (silencer, trigger guard..) -> "Detail"
Image paths are also pointed at the source textures in Art/Textures and the .blend is saved,
so the file does not depend on a Downloads folder.
"""
import os
import sys

import bpy

out_path = os.path.abspath(sys.argv[sys.argv.index("--") + 1])
blend_dir = os.path.dirname(bpy.data.filepath)
textures_dir = os.path.abspath(os.path.join(blend_dir, "..", "Textures"))

# 1. Point textures at the project's source copies.
relinked = False
for image in bpy.data.images:
    if not image.filepath:
        continue
    local = os.path.join(textures_dir, os.path.basename(bpy.path.abspath(image.filepath)))
    if os.path.exists(local) and os.path.normpath(bpy.path.abspath(image.filepath)) != os.path.normpath(local):
        image.filepath = bpy.path.relpath(local)
        relinked = True
if relinked:
    bpy.ops.wm.save_mainfile()
    print("Relinked textures to", textures_dir)


# 2. Name the parts by the texture they use.
def images_of(obj):
    names = set()
    for slot in obj.material_slots:
        material = slot.material
        if material and material.use_nodes:
            for node in material.node_tree.nodes:
                if node.type == "TEX_IMAGE" and node.image:
                    names.add(node.image.name.lower())
    return names


for obj in bpy.data.objects:
    if obj.type != "MESH":
        continue
    images = images_of(obj)
    if any("peredr" in name for name in images):
        obj.name = "Guard"
    elif any("ruchk" in name or "rychk" in name for name in images):
        obj.name = "Grip"
    elif any("nogen" in name for name in images):
        obj.name = "Blade"
    elif any("stvol" in name or "skin" in name for name in images):
        obj.name = "Slide"
    else:
        obj.name = "Detail"

# 3. Export only the meshes, with axes and scale baked for Unity (barrel = +Z, up = +Y).
bpy.ops.object.select_all(action="DESELECT")
for obj in bpy.data.objects:
    obj.select_set(obj.type == "MESH")
os.makedirs(os.path.dirname(out_path), exist_ok=True)
bpy.ops.export_scene.fbx(
    filepath=out_path,
    use_selection=True,
    object_types={"MESH"},
    apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_ALL",
    axis_forward="-Z",
    axis_up="Y",
    bake_space_transform=True,
    mesh_smooth_type="FACE",
    path_mode="STRIP",
    embed_textures=False,
)
print("Exported", out_path, "parts:", [o.name for o in bpy.data.objects if o.type == "MESH"])
