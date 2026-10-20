"""Exports a map .blend (from Tools/import_glb.py) to the game: Assets/Resources/Maps/<id>/model.fbx + parts.json.

    blender -b Art/Blender/<map>.blend --python Tools/export_map_fbx.py -- Assets/Resources/Maps/<id> [--reuse-textures <dir>]

Every mesh keeps its object name, because the game reads the marked areas by name: TSpawn_Tiles (Terrorist spawn
and buy zone), CTSpawn_Tiles (SWAT), Site_Tiles (both bombsites; Decal_A / Decal_B tell which is which). A map made
without them gets them from Tools/map_markers.py. Each part's look (texture, colour, glow, transparency) goes to
parts.json with its texture copied next to the model; --reuse-textures: a texture that folder holds already (the map's
own textures, for its closed versions) is not copied again.

Only what shows in the viewport goes (a collection or object hidden with its eye stays out), and nothing lying wholly
outside the map's floor (the biggest part, seen from above, and 5 m round it). A part with several materials becomes one part for each,
and a solid part whose faces mostly point inwards (Blender draws both sides, the game only the front) is turned outwards.
Pictures the .blend cannot find (made on another computer) are looked for by name in Art/Textures.
"""
import hashlib
import json
import os
import sys

import bmesh
import bpy
from mathutils import Vector

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from blender_looks import look_of, relink_missing_images  # noqa: E402

args = sys.argv[sys.argv.index("--") + 1:]
out_dir = os.path.abspath(args[0])
reuse_dir = os.path.abspath(args[args.index("--reuse-textures") + 1]) if "--reuse-textures" in args else None
os.makedirs(out_dir, exist_ok=True)
project = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
found = relink_missing_images([os.path.join(project, "Art", "Textures"), os.path.join(project, "Art", "текстури")])
if found:
    print("Pictures found by name:", sorted(set(found)))

if bpy.context.object and bpy.context.object.mode != "OBJECT":
    bpy.ops.object.mode_set(mode="OBJECT")


def footprint(thing):
    """The part's box seen from above: (x0, y0, x1, y1)."""
    corners = [thing.matrix_world @ Vector(corner) for corner in thing.bound_box]
    xs, ys = [c.x for c in corners], [c.y for c in corners]
    return min(xs), min(ys), max(xs), max(ys)


meshes = [obj for obj in bpy.data.objects if obj.type == "MESH" and obj.visible_get()]
hidden = [obj.name for obj in bpy.data.objects if obj.type == "MESH" and not obj.visible_get()]
if hidden:
    print("Hidden, not exported:", hidden)

# Parts wholly outside the floor, 5 m round it (a spare copy parked beside the map), stay out.
if meshes:
    boxes = {obj.name: footprint(obj) for obj in meshes}
    x0, y0, x1, y1 = max(boxes.values(), key=lambda b: (b[2] - b[0]) * (b[3] - b[1]))
    x0, y0, x1, y1 = x0 - 5.0, y0 - 5.0, x1 + 5.0, y1 + 5.0
    outside = [obj for obj in meshes if boxes[obj.name][0] > x1 or boxes[obj.name][2] < x0
               or boxes[obj.name][1] > y1 or boxes[obj.name][3] < y0]
    if outside:
        print("Outside the map's floor, not exported:", [obj.name for obj in outside])
    meshes = [obj for obj in meshes if obj not in outside]

# A part whose faces use several materials: one part for each material (the game gives a part one look).
for obj in list(meshes):
    used = {polygon.material_index for polygon in obj.data.polygons}
    if len(used) < 2:
        continue
    if obj.data.users > 1:
        obj.data = obj.data.copy()   # a mesh shared with other parts: this part's own copy
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    before = set(bpy.data.objects)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.separate(type="MATERIAL")
    bpy.ops.object.mode_set(mode="OBJECT")
    meshes += [o for o in bpy.data.objects if o not in before and o.type == "MESH"]
    print(f"Split {obj.name} by material into {len(used)} parts")

# Faces turned inwards show in Blender (it draws both sides) but not in the game: a solid piece whose faces mostly
# point inwards gets them turned outwards. Each piece of a part (one part may hold many) is looked at on its own;
# flat pieces (a floor, a decal, a roof plane) stay as they are.
def pieces(bm):
    """The part's separate pieces: faces joined by edges."""
    seen, found = set(), []
    for start in bm.faces:
        if start.index in seen:
            continue
        piece, stack = [], [start]
        seen.add(start.index)
        while stack:
            face = stack.pop()
            piece.append(face)
            for edge in face.edges:
                for other in edge.link_faces:
                    if other.index not in seen:
                        seen.add(other.index)
                        stack.append(other)
        found.append(piece)
    return found


turned = []
for obj in meshes:
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.faces.ensure_lookup_table()
    changed = 0
    for piece in pieces(bm):
        verts = {v for face in piece for v in face.verts}
        if len(piece) < 4:
            continue
        lo = Vector((min(v.co.x for v in verts), min(v.co.y for v in verts), min(v.co.z for v in verts)))
        hi = Vector((max(v.co.x for v in verts), max(v.co.y for v in verts), max(v.co.z for v in verts)))
        if min((hi - lo) * Vector(obj.scale)) < 0.2:
            continue
        centre = sum((v.co for v in verts), Vector()) / len(verts)
        inward = sum(1 for face in piece if (face.calc_center_median() - centre).dot(face.normal) < 0)
        if inward * 2 > len(piece):
            bmesh.ops.recalc_face_normals(bm, faces=piece)
            changed += 1
    if changed:
        bm.to_mesh(obj.data)
        turned.append(f"{obj.name} ({changed})")
    bm.free()
if turned:
    print("Faces turned outwards:", turned)

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

def base_image(obj):
    """The picture of the part's material (the one its faces use), or None."""
    slot = obj.data.polygons[0].material_index if len(obj.data.polygons) > 0 else 0
    material = obj.material_slots[min(slot, len(obj.material_slots) - 1)].material if obj.material_slots else None
    if material is None or not material.node_tree:
        return None
    return next((n.image for n in material.node_tree.nodes if n.type == "TEX_IMAGE" and n.image), None)


def picture_key(image):
    """The same picture, however many copies of it the .blend holds: its file, or what is packed."""
    if image is None:
        return None
    if image.packed_file is not None:
        return "packed:" + hashlib.md5(image.packed_file.data).hexdigest()
    return "file:" + os.path.normcase(os.path.abspath(bpy.path.abspath(image.filepath)))


# A part's texture is named after the part; a picture that several parts use keeps its own name (copied once, one
# material in the game, and a palette named "...Palette..." is imported without blurring, see MapImportSettings).
users = {}
for obj in meshes:
    key = picture_key(base_image(obj))
    if key is not None:
        users[key] = users.get(key, 0) + 1
looks = [look_of(obj, out_dir, None if users.get(picture_key(base_image(obj)), 0) > 1 else obj.name, reuse_dir)
         for obj in meshes]
with open(os.path.join(out_dir, "parts.json"), "w") as file:
    json.dump({"parts": looks}, file, indent=2)
textured = sum(1 for look in looks if look["texture"])
print(f"Exported map {out_dir}: {len(meshes)} parts ({textured} textured), "
      f"glow: {[l['name'] for l in looks if l['emission']]}, see-through: {[l['name'] + '=' + l['alpha'] for l in looks if l['alpha'] != 'OPAQUE']}")
