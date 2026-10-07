"""Turns upright the round parts that the maps' generator built along the wrong axis, in a map .blend from
Tools/import_glb.py (run it once, before Tools/export_map_fbx.py):

    blender -b Art/Blender/<map>.blend --python Tools/fix_map_rounds.py -- <mesh name> [<mesh name> ...]

village_map.glb, map.glb (Industrial) and halloween_map.glb made some cylinders and cones along the wrong axis: tree
trunks and pine-tree cones, ghosts, fence spikes and lamp caps lie on their sides, the well and the cauldron lie on
their sides with their water and potion standing up, and cart wheels lie flat. In the listed meshes (others, such
as gravestones, towers and campfire logs, are right) every cylinder or cone lying along Blender's Y axis is stood
up, a cone with its tip up and its base where it was, and every flat wheel resting on the ground is stood up with
its axle pointing to its cart's other wheel. The .blend is saved in place; running it again changes nothing.
"""
import math
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

names = sys.argv[sys.argv.index("--") + 1:]


def parts_of(bm):
    """Loose parts: vertices joined by faces or sharing a position (the import keeps every face's own corners)."""
    parent = list(range(len(bm.verts)))

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    def join(a, b):
        a, b = find(a), find(b)
        if a != b:
            parent[b] = a

    for face in bm.faces:
        for vert in face.verts[1:]:
            join(face.verts[0].index, vert.index)
    first_at = {}
    for vert in bm.verts:
        key = tuple(round(c, 4) for c in vert.co)
        if key in first_at:
            join(first_at[key], vert.index)
        else:
            first_at[key] = vert.index
    groups = {}
    for vert in bm.verts:
        groups.setdefault(find(vert.index), []).append(vert)
    return list(groups.values())


def mean(points):
    return sum(points, Vector()) / len(points)


def is_ring(points):
    centre = mean(points)
    radii = [(p - centre).length for p in points if (p - centre).length > 1e-3]
    return len(radii) >= 4 and max(radii) - min(radii) <= 0.02 * max(radii) + 0.002


def round_shape(points):
    """("prism", axis, centre, radius, length) or ("cone", axis, base centre, tip, radius) for a part whose points
    lie on two levels along X, Y or Z with a round ring on one or both; None for anything else."""
    for axis in (Vector((0, 1, 0)), Vector((0, 0, 1)), Vector((1, 0, 0))):
        levels = {}
        for p in points:
            levels.setdefault(round(p.dot(axis), 3), []).append(p)
        if len(levels) != 2:
            continue
        (low, a), (high, b) = sorted(levels.items())
        if len(a) >= 5 and len(b) >= 5 and is_ring(a) and is_ring(b):
            radius = max((p - mean(a)).length for p in a)
            return "prism", axis, (mean(a) + mean(b)) / 2, radius, high - low
        for ring, tip in ((a, b), (b, a)):
            if len(ring) >= 5 and len(tip) == 1 and is_ring(ring):
                radius = max((p - mean(ring)).length for p in ring)
                return "cone", axis, mean(ring), tip[0], radius
    return None


total = {}
for name in names:
    obj = bpy.data.objects.get(name)
    if obj is None or obj.type != "MESH":
        print(f"[fix] {name}: no such mesh")
        continue
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.verts.ensure_lookup_table()

    turns = []    # (verts, pivot, rotation)
    wheels = []   # (verts, centre)
    for verts in parts_of(bm):
        points = list({tuple(round(c, 4) for c in v.co): v.co.copy() for v in verts}.values())
        if len(points) < 6 or len(points) > 60:
            continue
        shape = round_shape(points)
        if shape is None:
            continue
        kind, axis = shape[0], shape[1]
        if axis.y == 1:
            # Lying along Y: turn it about X to stand along Z (a cone around its base, tip up).
            if kind == "cone":
                base, tip = shape[2], shape[3]
                angle = math.radians(-90 if tip.y < base.y else 90)
                turns.append((verts, base, Matrix.Rotation(angle, 3, "X")))
            elif not (shape[4] < shape[3] and abs(shape[2].z - shape[3]) < 0.05):   # not a wheel standing on the ground
                turns.append((verts, shape[2], Matrix.Rotation(math.radians(90), 3, "X")))
        elif axis.z == 1 and kind == "prism":
            centre, radius, length = shape[2], shape[3], shape[4]
            # A flat disc whose middle is one radius above the ground: a wheel lying down.
            if length < radius and abs(centre.z - radius) < 0.05:
                wheels.append((verts, centre))

    for verts, centre in wheels:
        others = [c for v, c in wheels if v is not verts and (c - centre).length < 3.0]
        if not others:
            print(f"[fix] {name}: wheel at {tuple(round(c, 2) for c in centre)} has no partner; left as it is")
            continue
        axle = min(others, key=lambda c: (c - centre).length) - centre
        axle.z = 0.0
        axle.normalize()
        # Turn the disc's axis (Z) onto the axle: 90 degrees about the horizontal line across it.
        turns.append((verts, centre, Matrix.Rotation(math.radians(90), 3, Vector((0, 0, 1)).cross(axle))))

    for verts, pivot, rotation in turns:
        for vert in verts:
            vert.co = pivot + rotation @ (vert.co - pivot)
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    total[name] = len(turns)

bpy.ops.wm.save_mainfile()
print(f"[fix] {bpy.path.basename(bpy.data.filepath)}: parts stood up per mesh {total}")
