"""Stands the Halloween map's dead trees up. Run it before Tools/export_map_fbx.py:

    blender -b Art/Blender/halloween_map.blend --python Tools/fix_halloween_trees.py

The map's generator laid every tree trunk flat on the ground (a 5-7 m beam along the map's Y axis), while the four
branches it put on top stayed in the air where the trunk's top should be: in the game the trees were floating twigs
and logs lying about. Each fallen trunk (a 12-triangle beam in Wood_Dark, half sunk into the ground, with branches in
the air over one end) is taken out and a standing trunk is built at that end instead: round, tapering, leaning a
little, with roots spreading at its foot, reaching up into its branches. The .blend is saved in place; running it
again finds no fallen trunks and changes nothing.
"""
import math
import random

import bmesh
import bpy
from mathutils import Vector

obj = bpy.data.objects["Wood_Dark"]
mesh = obj.data
bm = bmesh.new()
bm.from_mesh(mesh)
bm.faces.ensure_lookup_table()
uv = bm.loops.layers.uv.active


# Triangles touching at a corner make one piece (the mesh holds loose triangles).
def key(v):
    return (round(v.co.x, 2), round(v.co.y, 2), round(v.co.z, 2))


parent = {}


def find(a):
    while parent.setdefault(a, a) != a:
        parent[a] = parent[parent[a]]
        a = parent[a]
    return a


for face in bm.faces:
    keys = [key(v) for v in face.verts]
    for k in keys[1:]:
        parent[find(keys[0])] = find(k)
pieces = {}
for face in bm.faces:
    pieces.setdefault(find(key(face.verts[0])), []).append(face)


def bounds(faces):
    pts = [v.co for f in faces for v in f.verts]
    return (Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts))),
            Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts))))


described = [(faces, *bounds(faces)) for faces in pieces.values()]
# Branches: pieces that start well above the ground.
branches = [(lo + hi) / 2 for faces, lo, hi in described if lo.z > 1.5]

fallen = []
for faces, lo, hi in described:
    size = hi - lo
    long_axis = 0 if size.x > size.y else 1
    length, thickness = max(size.x, size.y), min(size.x, size.y)
    if len(faces) != 12 or lo.z > -0.2 or hi.z > 0.7 or length < 4.0 or thickness > 1.4:
        continue
    # The trunk's foot is the end under the branches.
    ends = []
    for end in (lo, hi):
        foot = (lo + hi) / 2
        foot[long_axis] = end[long_axis]
        near = min(((Vector((b.x, b.y)) - Vector((foot.x, foot.y))).length for b in branches), default=99.0)
        ends.append((near, foot))
    near, foot = min(ends, key=lambda e: e[0])
    if near > 3.5:
        continue
    fallen.append((faces, Vector((foot.x, foot.y, 0.0)), length, thickness))

print(f"fallen trunks: {len(fallen)}")
random.seed(31)
SIDES = 10
for faces, foot, length, thickness in fallen:
    material_index = faces[0].material_index
    bmesh.ops.delete(bm, geom=list({f for f in faces}), context="FACES")
    # The branches meet a little below the trunk's top; it stands a bit taller so they grow out of it.
    height = length + 0.4
    base_r, top_r = max(0.32, thickness * 0.42), 0.12
    lean = Vector((random.uniform(-0.25, 0.25), random.uniform(-0.25, 0.25), 0.0))
    rings = []
    levels = 7
    for i in range(levels + 1):
        t = i / levels
        z = -0.3 + t * (height + 0.3)
        # Wide at the foot, narrowing quickly and then slowly; bent a little as it goes up.
        r = top_r + (base_r - top_r) * (1.0 - t) ** 1.6
        if i == 0:
            r *= 1.35
        centre = foot + lean * (t * t) + Vector((0.0, 0.0, z))
        twist = random.uniform(0.0, 0.3)
        ring = []
        for s in range(SIDES):
            a = 2.0 * math.pi * s / SIDES + twist
            wobble = 1.0 + random.uniform(-0.08, 0.08)
            ring.append(bm.verts.new(centre + Vector((math.cos(a) * r * wobble, math.sin(a) * r * wobble, 0.0))))
        rings.append((ring, z))
    tip = bm.verts.new(foot + lean + Vector((0.0, 0.0, height + 0.35)))
    new_faces = []
    for (ring, z0), (above, z1) in zip(rings, rings[1:]):
        for s in range(SIDES):
            a, b = ring[s], ring[(s + 1) % SIDES]
            c, d = above[(s + 1) % SIDES], above[s]
            face = bm.faces.new((a, b, c, d))
            new_faces.append((face, [(s / SIDES, z0 / 2.0), ((s + 1) / SIDES, z0 / 2.0),
                                     ((s + 1) / SIDES, z1 / 2.0), (s / SIDES, z1 / 2.0)]))
    top_ring, top_z = rings[-1]
    for s in range(SIDES):
        face = bm.faces.new((top_ring[s], top_ring[(s + 1) % SIDES], tip))
        new_faces.append((face, [(s / SIDES, top_z / 2.0), ((s + 1) / SIDES, top_z / 2.0), (s / SIDES + 0.05, (top_z + 0.4) / 2.0)]))
    # Roots: low wedges spreading from the foot.
    for k in range(4):
        a = random.uniform(0.0, 2.0 * math.pi) if k == 0 else a + math.pi / 2 + random.uniform(-0.4, 0.4)
        out = Vector((math.cos(a), math.sin(a), 0.0))
        side = Vector((-out.y, out.x, 0.0))
        reach = random.uniform(0.9, 1.4)
        p0 = foot + out * base_r * 0.6 + Vector((0.0, 0.0, 0.45))
        p1 = foot + out * base_r * 0.8 + side * 0.18 + Vector((0.0, 0.0, -0.05))
        p2 = foot + out * base_r * 0.8 - side * 0.18 + Vector((0.0, 0.0, -0.05))
        p3 = foot + out * (base_r + reach) + Vector((0.0, 0.0, -0.05))
        v0, v1, v2, v3 = (bm.verts.new(p) for p in (p0, p1, p2, p3))
        for tri in ((v0, v1, v3), (v0, v3, v2), (v1, v2, v3), (v0, v2, v1)):   # closed, so its faces point out
            face = bm.faces.new(tri)
            new_faces.append((face, [(0.0, 0.0), (0.2, 0.0), (0.1, 0.5)]))
    for face, coords in new_faces:
        face.material_index = material_index
        face.smooth = False
        if uv is not None:
            for loop, c in zip(face.loops, coords):
                loop[uv].uv = c
    bmesh.ops.recalc_face_normals(bm, faces=[f for f, _ in new_faces])
    print(f"tree at ({foot.x:.1f}, {foot.y:.1f}): trunk {height:.1f} m")

bm.to_mesh(mesh)
bm.free()
mesh.update()
if fallen:
    bpy.ops.wm.save_mainfile()
    print("Saved", bpy.data.filepath)
