"""Exports a weapon .blend from Art/Blender to an .fbx the game can load.

Usage (from the project folder; Tools/export_models.ps1 runs it for every model):
    blender -b Art/Blender/<file>.blend --python Tools/export_gun_fbx.py -- <output .fbx> [--roles "Main=A,B;Grip=C"] [--copy-textures]

  A weapon's default model:      Assets/Resources/Models/<weapon name>/model.fbx
  A skin that brings its model:  Assets/Resources/Skins/<weapon name>/<skin name>/model.fbx

The game paints a weapon by part: "Main" (body, slide, blade) gets the skin's main texture, "Grip" (grip, stock,
handle) the grip colour, and "Detail" (barrel, magazine, sights...) the detail colour. Parts are named for that:
  --roles "Main=Cube.002,Cube.003;Grip=Cube"  names the listed objects; every other mesh becomes Detail.
  Without --roles the texture names decide:
    "peredr" (in front of the handle) -> Guard     "ruchka" / "rychka" / "wood" -> Grip
    "nogen" (knife blade)             -> Blade     "skin" / "stvol" / "steel" / "ocnova" -> Slide (main)
    anything else                     -> Detail
--copy-textures makes the model's own look its default: each part's texture (or colour) from Blender is copied
next to the .fbx and listed in parts.json, which the game reads. (FBX itself cannot carry Blender 5's material
nodes, which is why the look travels separately.)
Flat meshes (reference pictures on a plane) are left out.
Texture paths are pointed at the copies in Art/Textures when they exist there, and the .blend is saved,
so the file does not depend on a Downloads folder.
"""
import json
import os
import shutil
import sys

import bpy

args = sys.argv[sys.argv.index("--") + 1:]
out_path = os.path.abspath(args[0])
roles_spec = args[args.index("--roles") + 1] if "--roles" in args else None
copy_textures = "--copy-textures" in args
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

meshes = [obj for obj in bpy.data.objects if obj.type == "MESH" and min(obj.dimensions) > 1e-4]


# 2. Name the parts by their role.
def images_of(obj):
    names = set()
    for slot in obj.material_slots:
        material = slot.material
        if material and material.node_tree:
            for node in material.node_tree.nodes:
                if node.type == "TEX_IMAGE" and node.image:
                    names.add(node.image.name.lower())
    return names


def role_by_texture(obj):
    images = images_of(obj)
    if any("peredr" in name for name in images):
        return "Guard"
    if any(key in name for name in images for key in ("ruchk", "rychk", "wood")):
        return "Grip"
    if any("nogen" in name for name in images):
        return "Blade"
    if any(key in name for name in images for key in ("skin", "stvol", "steel", "ocnov", "osnov")):
        return "Slide"
    return "Detail"


roles = {}
if roles_spec:
    for part in roles_spec.split(";"):
        role, _, names = part.partition("=")
        for name in names.split(","):
            if name.strip():
                roles[name.strip()] = role.strip()
assigned = {obj.name: roles.get(obj.name, "Detail") if roles_spec else role_by_texture(obj) for obj in meshes}
for obj in meshes:
    obj.name = assigned[obj.name]

# 3. Export only those meshes, with axes and scale baked for Unity (barrel = +Z, up = +Y).
if bpy.context.object and bpy.context.object.mode != "OBJECT":
    bpy.ops.object.mode_set(mode="OBJECT")
for obj in bpy.data.objects:
    try:
        obj.select_set(obj in meshes)
    except RuntimeError:
        pass   # not in the view layer
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
print("Exported", out_path, "parts:", sorted(obj.name for obj in meshes))


# 4. The model's own look, for the game's Default skin.
def look_of(obj):
    look = {"name": obj.name, "texture": "", "color": "", "metallic": 0.0, "smoothness": 0.2}
    material = obj.material_slots[0].material if obj.material_slots else None
    if not material or not material.node_tree:
        return look
    textured = any(node.type == "TEX_IMAGE" and node.image for node in material.node_tree.nodes)
    for node in material.node_tree.nodes:
        if node.type == "TEX_IMAGE" and node.image and not look["texture"]:
            source = bpy.path.abspath(node.image.filepath)
            if os.path.exists(source):
                name = os.path.basename(source)
                shutil.copyfile(source, os.path.join(os.path.dirname(out_path), name))
                look["texture"] = os.path.splitext(name)[0]
            else:
                print("Missing texture (the part gets a plain colour in the game):", source)
        if node.type in ("BSDF_PRINCIPLED", "BSDF_METALLIC"):
            # With a texture the base colour is not what the part looks like, so it is only kept for untextured parts.
            color = node.inputs["Base Color"].default_value
            if not textured:
                look["color"] = "#{:02X}{:02X}{:02X}".format(*(round(min(max(c, 0.0), 1.0) ** (1 / 2.2) * 255) for c in color[:3]))
            roughness = node.inputs["Roughness"].default_value if "Roughness" in node.inputs else 0.5
            look["smoothness"] = round(max(0.0, 1.0 - roughness) * 0.6, 2)
            if node.type == "BSDF_METALLIC":
                look["metallic"] = 0.5
            elif "Metallic" in node.inputs:
                look["metallic"] = round(node.inputs["Metallic"].default_value, 2)
    return look


if copy_textures:
    looks = [look_of(obj) for obj in meshes]
    with open(os.path.join(os.path.dirname(out_path), "parts.json"), "w") as file:
        json.dump({"parts": looks}, file, indent=2)
    print("Wrote parts.json:", ", ".join(f"{l['name']}={l['texture'] or l['color']}" for l in looks))
