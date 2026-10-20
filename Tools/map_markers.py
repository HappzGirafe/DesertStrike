"""Adds what the game needs to a map made without it, while it is exported (the .blend itself is not saved):
the marked areas it reads by object name (TSpawn_Tiles, CTSpawn_Tiles, Site_Tiles, Decal_A, Decal_B; see ModelMap),
as flat tiles 2 cm under the floor where nobody sees them, and ramps where bots must climb a ledge.

    blender -b -Y <map>.blend --python Tools/map_markers.py --python Tools/export_map_fbx.py -- <out dir> --markers Tools/maps/<map>.json

Tools/maps/<map>.json, in the .blend's own coordinates (metres, z up):
    {"areas": {"TSpawn_Tiles": [[x0, y0, x1, y1, z]], "CTSpawn_Tiles": [...], "Site_Tiles": [[...], [...]]},
     "letters": {"Decal_A": [x, y, z], "Decal_B": [x, y, z]},
     "ramps": [{"top": [x, y, z], "bottom": [x, y, z], "width": w, "look": "<object whose material it takes>"}]}
An area's rectangle is a spawn (its tiles hold the spawn points) or a bombsite; each site belongs to the nearer letter.
"""
import json
import os
import sys

import bmesh
import bpy
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
spec_path = args[args.index("--markers") + 1] if "--markers" in args else None
if spec_path:
    with open(spec_path, encoding="utf-8") as file:
        spec = json.load(file)
    collection = bpy.data.collections.new("Game markers")
    bpy.context.scene.collection.children.link(collection)

    def add(name, vertices, faces, material=None):
        mesh = bpy.data.meshes.new(name)
        mesh.from_pydata(vertices, [], faces)
        mesh.update()
        thing = bpy.data.objects.new(name, mesh)
        collection.objects.link(thing)
        if material is not None:
            mesh.materials.append(material)
        return thing

    for name, rects in spec.get("areas", {}).items():
        vertices, faces = [], []
        for x0, y0, x1, y1, z in rects:
            z -= 0.02   # under the floor
            base = len(vertices)
            vertices += [(x0, y0, z), (x1, y0, z), (x1, y1, z), (x0, y1, z)]
            faces.append((base, base + 1, base + 2, base + 3))
        add(name, vertices, faces)
    for name, (x, y, z) in spec.get("letters", {}).items():
        z -= 0.02
        add(name, [(x - 0.25, y - 0.25, z), (x + 0.25, y - 0.25, z), (x + 0.25, y + 0.25, z), (x - 0.25, y + 0.25, z)], [(0, 1, 2, 3)])
    for i, ramp in enumerate(spec.get("ramps", [])):
        top, bottom = Vector(ramp["top"]), Vector(ramp["bottom"])
        along = Vector((bottom.x - top.x, bottom.y - top.y, 0.0)).normalized()
        across = Vector((-along.y, along.x, 0.0)) * (ramp["width"] / 2.0)
        top -= along * 0.4   # a little onto the ledge, so there is no seam
        low = Vector((top.x, top.y, bottom.z))   # under the ramp's top edge
        vertices = [top - across, top + across, bottom + across, bottom - across, low - across, low + across]
        faces = [(0, 3, 2, 1), (0, 1, 5, 4), (3, 4, 5, 2), (0, 4, 3), (1, 2, 5)]
        look = bpy.data.objects.get(ramp.get("look", ""))
        material = look.material_slots[0].material if look is not None and look.material_slots else None
        thing = add(f"Ramp_{i + 1}", [tuple(v) for v in vertices], faces, material)
        # Faces turned outwards.
        bm = bmesh.new()
        bm.from_mesh(thing.data)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.to_mesh(thing.data)
        bm.free()
        # The ledge's texture runs the same way on the ramp (planar UVs, 4 m a tile like a box's faces).
        uv = thing.data.uv_layers.new()
        for loop in thing.data.loops:
            v = thing.data.vertices[loop.vertex_index].co
            uv.data[loop.index].uv = (v.x / 4.0, v.y / 4.0)
    print(f"map markers: {len(spec.get('areas', {}))} areas, {len(spec.get('letters', {}))} letters, "
          f"{len(spec.get('ramps', []))} ramps from {os.path.basename(spec_path)}")
