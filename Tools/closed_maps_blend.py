"""Makes <map>_closed.blend: the map with the walls that close one half of it in small matches (1v1 to 3v3), from
what the game wrote with -ds-export-closed (Tools/closed_maps.ps1 runs both). Collection "Closed A" holds the walls
when site A is shut (B in play), "Closed B" the walls when B is shut; "Closed B" starts hidden (the eye in the
Outliner shows it). The map's own objects stay as they are, and every picture is packed into the file.

    blender -b -Y Art/Blender/<map>.blend --python Tools/closed_maps_blend.py -- <folder>/<map>.json <out.blend>
    blender -b -Y --factory-startup --python Tools/closed_maps_blend.py -- <folder>/dune.json <out.blend> <folder>/dune.obj

The walls come in the game's coordinates; they are lined up with the .blend by the map parts both have (by name).
"""
import json
import os
import sys

import bpy
import numpy as np
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
with open(args[0], encoding="utf-8") as file:
    data = json.load(file)
out = os.path.abspath(args[1])
obj_file = os.path.abspath(args[2]) if len(args) > 2 else None

if obj_file:
    # A map built from blocks (de_dune): an empty scene, and the map from the game's .obj.
    for thing in list(bpy.data.objects):
        bpy.data.objects.remove(thing)
    bpy.ops.wm.obj_import(filepath=obj_file)


def vec(d):
    return np.array([d["x"], d["y"], d["z"]], dtype=float)


# 1. The game's coordinates -> Blender's: an affine map fitted to the parts' centres, then rounded to the quarter turns
#    and mirroring it must be (the FBX export and the game only turn the map round and move it).
game, blend = [], []
for part in data["parts"]:
    thing = bpy.data.objects.get(part["name"])
    if thing is None or thing.type != "MESH":
        continue
    corners = [thing.matrix_world @ Vector(corner) for corner in thing.bound_box]
    game.append(vec(part["center"]))
    blend.append(np.array(sum(corners, Vector()) / 8.0))
game, blend = np.array(game), np.array(blend)
if len(game) < 4:
    sys.exit(f"Only {len(game)} map parts match by name: cannot line the walls up")


def fit(rows):
    x = np.hstack([game[rows], np.ones((rows.sum(), 1))])
    solution = np.linalg.lstsq(x, blend[rows], rcond=None)[0]
    return solution[:3], solution[3]


rows = np.ones(len(game), dtype=bool)
for _ in range(3):   # parts the game moved (or the .blend changed since) do not count
    turn, shift = fit(rows)
    error = np.linalg.norm(game @ turn + shift - blend, axis=1)
    rows = error < max(0.05, 3 * np.median(error))
scale = np.mean(np.linalg.norm(turn, axis=0))
rounded = np.round(turn / scale) * scale
if np.abs(turn - rounded).max() < 0.02 * scale:
    turn = rounded
    shift = np.mean(blend[rows] - game[rows] @ turn, axis=0)
error = np.linalg.norm(game[rows] @ turn + shift - blend[rows], axis=1)
print(f"closed maps: lined up by {rows.sum()} of {len(game)} parts, error {error.mean():.3f} m (largest {error.max():.3f} m)")


def to_blend(points):
    return np.asarray(points) @ turn + shift


def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


materials = {}


def material(color):
    rgb = tuple(round(color[k], 3) for k in ("r", "g", "b"))
    key = "Closed wall #%02X%02X%02X" % tuple(int(c * 255) for c in rgb)
    if key not in materials:
        mat = bpy.data.materials.new(key)
        mat.use_nodes = True
        linear = [srgb_to_linear(c) for c in rgb]
        mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*linear, 1.0)
        mat.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.85
        mat.diffuse_color = (*linear, 1.0)   # the solid view shows it too
        materials[key] = mat
    return materials[key]


# Corners of a box by bits (x, y, z) and its six faces, each turning outwards.
QUADS = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]


def add_walls(site, boxes, hidden):
    collection = bpy.data.collections.new(f"Closed {site}")
    bpy.context.scene.collection.children.link(collection)
    vertices, faces, slots, slot_of = [], [], [], {}
    for box in boxes:
        center, size = vec(box["center"]), vec(box["size"])
        corners = to_blend([center + size / 2 * np.array([sx, sy, sz]) for sx in (-1, 1) for sy in (-1, 1) for sz in (-1, 1)])
        lo, hi = corners.min(axis=0), corners.max(axis=0)
        mat = material(box["color"])
        if mat.name not in slot_of:
            slot_of[mat.name] = len(slots)
            slots.append(mat)
        base = len(vertices)
        vertices += [(x, y, z) for x in (lo[0], hi[0]) for y in (lo[1], hi[1]) for z in (lo[2], hi[2])]
        faces += [(tuple(base + i for i in quad), slot_of[mat.name]) for quad in QUADS]
    mesh = bpy.data.meshes.new(f"Closed {site} walls")
    mesh.from_pydata(vertices, [], [quad for quad, _ in faces])
    for mat in slots:
        mesh.materials.append(mat)
    mesh.polygons.foreach_set("material_index", [slot for _, slot in faces])
    mesh.update()
    walls = bpy.data.objects.new(f"Closed {site} walls", mesh)
    collection.objects.link(walls)
    bpy.context.view_layer.layer_collection.children[collection.name].hide_viewport = hidden
    print(f"closed maps: {site} closed: {len(boxes)} wall pieces")


add_walls("A", data["wallsA"], hidden=False)
add_walls("B", data["wallsB"], hidden=True)

try:
    bpy.ops.file.pack_all()
except RuntimeError as problem:
    print("closed maps: some pictures could not be packed:", problem)
os.makedirs(os.path.dirname(out), exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=out, copy=True, compress=True)
print("closed maps: saved", out)
