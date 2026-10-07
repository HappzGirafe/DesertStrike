"""Exports a map .blend (from Tools/import_glb.py) to the game: Assets/Resources/Maps/<id>/model.fbx + parts.json.

    blender -b Art/Blender/<map>.blend --python Tools/export_map_fbx.py -- Assets/Resources/Maps/<id>

Every mesh keeps its object name, because the game reads the marked areas by name: TSpawn_Tiles (Terrorist spawn
and buy zone), CTSpawn_Tiles (SWAT), Site_Tiles (both bombsites; Decal_A / Decal_B tell which is which). Each
part's look (texture, colour, glow, transparency) goes to parts.json with its texture copied next to the model.
"""
import json
import os
import sys

import bpy

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from blender_looks import look_of  # noqa: E402

out_dir = os.path.abspath(sys.argv[sys.argv.index("--") + 1])
os.makedirs(out_dir, exist_ok=True)

meshes = [obj for obj in bpy.data.objects if obj.type == "MESH"]
if bpy.context.object and bpy.context.object.mode != "OBJECT":
    bpy.ops.object.mode_set(mode="OBJECT")
for obj in bpy.data.objects:
    try:
        obj.select_set(obj in meshes)
    except RuntimeError:
        pass   # not in the view layer

bpy.ops.export_scene.fbx(
    filepath=os.path.join(out_dir, "model.fbx"),
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

looks = [look_of(obj, out_dir, obj.name) for obj in meshes]
with open(os.path.join(out_dir, "parts.json"), "w") as file:
    json.dump({"parts": looks}, file, indent=2)
textured = sum(1 for look in looks if look["texture"])
print(f"Exported map {out_dir}: {len(meshes)} parts ({textured} textured), "
      f"glow: {[l['name'] for l in looks if l['emission']]}, see-through: {[l['name'] + '=' + l['alpha'] for l in looks if l['alpha'] != 'OPAQUE']}")
