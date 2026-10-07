"""Turns a .glb model into a .blend in Art/Blender, with its textures saved as files in Art/Textures.

    blender -b --factory-startup --python Tools/import_glb.py -- <file.glb> <Art/Blender/name.blend> <texture prefix>

Textures packed inside the .glb are written out as <prefix>_<material>.png (and <prefix>_<material>_glow.png for
glow textures), so the export scripts can copy them next to the game's model. Faces of two-sided materials (spider
webs, nets, thin decals) are doubled with the other side facing out, because the game draws only front faces.
After this, export with Tools/export_gun_fbx.py (weapons) or Tools/export_map_fbx.py (maps).
"""
import os
import sys

import bmesh
import bpy

args = sys.argv[sys.argv.index("--") + 1:]
glb_path, blend_path, prefix = os.path.abspath(args[0]), os.path.abspath(args[1]), args[2]
textures_dir = os.path.join(os.path.dirname(os.path.dirname(blend_path)), "Textures")
os.makedirs(textures_dir, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=glb_path)


def reaches(node, socket_name, depth=0):
    """True when this node's output flows (through a few Mix/Math nodes) into a shader input of that name."""
    if depth > 4:
        return False
    for output in node.outputs:
        for link in output.links:
            if link.to_node.type in ("BSDF_PRINCIPLED", "BSDF_METALLIC") and link.to_socket.name == socket_name:
                return True
            if reaches(link.to_node, socket_name, depth + 1):
                return True
    return False


# 1. Name each image after the material that uses it, and write its packed bytes out as a file.
names = {}
for material in bpy.data.materials:
    if not material.node_tree:
        continue
    for node in material.node_tree.nodes:
        if node.type == "TEX_IMAGE" and node.image and node.image.name not in names:
            glow = reaches(node, "Emission Color") and not reaches(node, "Base Color")
            names[node.image.name] = f"{prefix}_{material.name}" + ("_glow" if glow else "")

written = 0
for image in list(bpy.data.images):
    if image.packed_file is None:
        continue
    data = bytes(image.packed_file.data)
    extension = ".jpg" if data[:2] == b"\xff\xd8" else ".png"
    name = names.get(image.name, f"{prefix}_{image.name}")
    path = os.path.join(textures_dir, name + extension)
    with open(path, "wb") as file:
        file.write(data)
    image.filepath = path
    image.unpack(method="REMOVE")
    image.name = name
    image.reload()
    written += 1

# 2. Two-sided materials: add the back faces.
doubled = 0
for obj in [o for o in bpy.context.scene.objects if o.type == "MESH"]:
    two_sided = {i for i, slot in enumerate(obj.material_slots) if slot.material and not slot.material.use_backface_culling}
    if not two_sided:
        continue
    mesh = bmesh.new()
    mesh.from_mesh(obj.data)
    faces = [f for f in mesh.faces if f.material_index in two_sided]
    copies = bmesh.ops.duplicate(mesh, geom=faces)["geom"]
    new_faces = [g for g in copies if isinstance(g, bmesh.types.BMFace)]
    bmesh.ops.reverse_faces(mesh, faces=new_faces)
    mesh.to_mesh(obj.data)
    mesh.free()
    doubled += len(new_faces)

os.makedirs(os.path.dirname(blend_path), exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=blend_path)
bpy.ops.file.make_paths_relative()
bpy.ops.wm.save_mainfile()
print(f"Imported {os.path.basename(glb_path)} -> {blend_path}: {written} textures written to {textures_dir}, "
      f"{doubled} back faces added")
