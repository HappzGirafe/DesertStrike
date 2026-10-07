"""Adds structures to the Industrial map (Art/Blender/industrial_map.blend), then saves it; export it afterwards
with Tools/export_map_fbx.py:

    blender -b Art/Blender/industrial_map.blend --python Tools/industrial_structures.py

Bombsite B becomes a brick building of three rooms where the bomb can be planted, with two entrances (what stood on
the old open site is removed, and the site's floor tiles and its painted B move inside). Also silos, fuel tanks,
a water tower, a hangar to walk into with 20 crates in piles, a two-floor office, a guard booth, a loading dock on
the warehouse, a pipe rack, more containers, cover on site A (barriers, barrels, crates), and lamp posts. Roofs over
rooms people walk in go in Add_Ceiling, which the radar leaves out. Every new part is in an object whose name
starts with "Add_", one per material, and those are rebuilt on every run. Each
structure's footprint is checked against the map's walls, buildings, spawns and bombsite letters; overlaps are
printed (and the script stops) so a structure never blocks something by accident. New textures (containers in
other colours, light and dark metal, roller doors, office windows, hazard stripes) are written to Art/Textures.
"""
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Vector

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from blender_build import (UP, Builder, corners, load_rgb, loose_parts, material, new_object,  # noqa: E402
                           recoloured_texture, save_rgb)

INNER = (0.6, 90.0, -127.4, -0.6)   # inside the boundary wall: x from, x to, y from, y to


# ------------------------------------------------------------------------------------------------ textures

def roller_door_texture():
    rgb = np.zeros((256, 256, 3), dtype=np.float32)
    y = np.arange(256)[:, None]
    shade = 0.68 - 0.08 * (y % 16) / 16.0
    shade = np.where(y % 16 < 2, 0.4, shade)
    rgb[:] = shade[..., None] * np.array([1.0, 1.0, 1.03], dtype=np.float32)
    rgb[:, :10] = rgb[:, -10:] = 0.33
    rgb[-14:] = 0.3
    return save_rgb("industrial_Roller_Door", rgb)


def office_window_texture():
    h = w = 256
    y = np.arange(h)[:, None] / h
    x = np.arange(w)[None, :] / w
    glass = (np.array([0.27, 0.45, 0.58]) * (1 - y[..., None]) + np.array([0.15, 0.27, 0.37]) * y[..., None])
    streak = ((x + y > 0.55) & (x + y < 0.75))[..., None] * 0.1
    rgb = (glass + streak + 0 * x[..., None]).astype(np.float32)
    frame = np.array([0.6, 0.62, 0.64], dtype=np.float32)
    rgb[:14] = rgb[-14:] = frame
    rgb[:, :14] = rgb[:, -14:] = frame
    rgb[:, 124:132] = frame
    rgb[112:120] = frame
    return save_rgb("industrial_Office_Window", rgb)


def hazard_texture():
    y = np.arange(256)[:, None]
    x = np.arange(256)[None, :]
    stripe = ((x + y) // 32) % 2 == 0
    rgb = np.where(stripe[..., None], np.array([0.95, 0.76, 0.1]), np.array([0.09, 0.09, 0.09])).astype(np.float32)
    return save_rgb("industrial_Hazard", rgb)


metal = "industrial_Container_Metal.png"
images = {
    "Container_Red": recoloured_texture(metal, "industrial_Container_Red", (0.62, 0.17, 0.13)),
    "Container_Green": recoloured_texture(metal, "industrial_Container_Green", (0.18, 0.42, 0.24)),
    "Container_Orange": recoloured_texture(metal, "industrial_Container_Orange", (0.86, 0.44, 0.12)),
    "Container_Blue": metal,
    "Metal_Light": recoloured_texture(metal, "industrial_Metal_Light", (0.74, 0.76, 0.77)),
    "Metal_Roof": recoloured_texture(metal, "industrial_Metal_Roof", (0.36, 0.38, 0.41)),
    "Barrel_Red": recoloured_texture(metal, "industrial_Barrel_Red", (0.66, 0.15, 0.1)),
    "Roller_Door": roller_door_texture(),
    "Office_Window": office_window_texture(),
    "Hazard": hazard_texture(),
    "Concrete": "industrial_Wall_Concrete.png",
    "Crate": "industrial_Crate_Wood.png",
    "Pipe_Yellow": "industrial_Crane_Paint.png",
    "Brick": "industrial_Building_Brick.png",
    "Ceiling": "industrial_Metal_Roof.png",   # roofs over rooms people walk in: the radar leaves them out
}
colours = {"Metal_Dark": (0.24, 0.26, 0.29), "Pipe": (0.6, 0.62, 0.65), "Lamp": (0.95, 0.93, 0.8)}

# Remove what an earlier run added, then one object per material.
for obj in [o for o in bpy.data.objects if o.name.startswith("Add_")]:
    bpy.data.objects.remove(obj, do_unlink=True)
parts = {}
for name, image in images.items():
    parts[name] = Builder(new_object("Add_" + name, material("Add_" + name, image, metallic=0.3 if "Metal" in name or "Container" in name else 0.0)))
for name, colour in colours.items():
    parts[name] = Builder(new_object("Add_" + name, material("Add_" + name, None, colour, metallic=0.4 if name != "Lamp" else 0.0)))

footprints = []   # (structure, 2D polygon) checked against the map at the end


def rect(cx, cy, sx, sy, yaw=0.0):
    c, s = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
    return [(cx + c * dx * sx / 2 - s * dy * sy / 2, cy + s * dx * sx / 2 + c * dy * sy / 2) for dx, dy in ((-1, -1), (1, -1), (1, 1), (-1, 1))]


def circle(cx, cy, r, n=12):
    return [(cx + r * math.cos(2 * math.pi * i / n), cy + r * math.sin(2 * math.pi * i / n)) for i in range(n)]


def axes(yaw):
    c, s = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
    return Vector((c, s, 0)), Vector((-s, c, 0))


# ------------------------------------------------------------------------------------------------ structures

def container(name, x, y, yaw=0.0, level=0):
    """A 20-foot shipping container (6.06 x 2.44 x 2.59 m), `level` 1 on top of another."""
    parts["Container_" + name].block(Vector((x, y, 2.59 * level)), (6.06, 2.44, 2.59), yaw, None)
    if level == 0:
        footprints.append((f"container {name}", rect(x, y, 6.06, 2.44, yaw)))


def barrels(x, y, count, colour_cycle=("Red", "Blue")):
    spots = [(0, 0), (0.75, 0.1), (0.35, 0.68), (-0.4, 0.62), (1.05, 0.8)]
    for i in range(count):
        dx, dy = spots[i]
        name = "Barrel_Red" if colour_cycle[i % len(colour_cycle)] == "Red" else "Container_Blue"
        parts[name].cylinder(Vector((x + dx, y + dy, 0)), UP, 0.32, 0.92, 12, 1.0)
        parts["Metal_Dark"].cylinder(Vector((x + dx, y + dy, 0.3)), UP, 0.335, 0.05, 12, None, caps=True)
    footprints.append((f"barrels {x},{y}", circle(x + 0.35, y + 0.35, 1.1)))


def crates(x, y, count, yaw=0.0):
    ax, ay = axes(yaw)
    spots = [(0, 0, 0), (1.25, 0.05, 0), (0.6, 0.0, 1.2), (0.05, 1.25, 0)]
    for dx, dy, dz in spots[:count]:
        parts["Crate"].block(Vector((x, y, dz)) + ax * dx + ay * dy, (1.2, 1.2, 1.2), yaw, None)
    footprints.append((f"crates {x},{y}", rect(x + 0.6, y + 0.6, 2.8, 2.8, yaw)))


def barrier(x, y, yaw):
    """A concrete road barrier with a hazard-striped top."""
    parts["Concrete"].block(Vector((x, y, 0)), (2.0, 0.62, 0.45), yaw, 4.0)
    parts["Concrete"].block(Vector((x, y, 0.45)), (2.0, 0.3, 0.38), yaw, 4.0)
    parts["Hazard"].block(Vector((x, y, 0.83)), (2.0, 0.3, 0.06), yaw, 1.0)
    footprints.append((f"barrier {x},{y}", rect(x, y, 2.0, 0.62, yaw)))


def ladder(base, up_to, facing, width=0.5):
    """Two rails and rungs from `base` up to height `up_to`, set off a wall that faces along `facing`."""
    side = UP.cross(facing).normalized()
    height = up_to - base.z
    for s in (-1, 1):
        rail = base + side * (s * width / 2)
        parts["Metal_Dark"].box(rail + UP * (height / 2), side, facing, UP, 0.03, 0.03, height / 2)
    rung = 0.35
    while rung < height:
        parts["Metal_Dark"].box(base + UP * rung, side, facing, UP, width / 2, 0.025, 0.025)
        rung += 0.35


def silo(x, y, r=2.6, h=13.0):
    parts["Concrete"].cylinder(Vector((x, y, 0)), UP, r + 0.35, 0.6, 20, 4.0)
    parts["Metal_Light"].cylinder(Vector((x, y, 0.6)), UP, r, h - 0.6, 20, 3.0)
    parts["Metal_Roof"].cone(Vector((x, y, h)), UP, r + 0.15, 2.4, 20, 3.0)
    for z in (4.0, 8.0, 12.0):
        parts["Metal_Dark"].cylinder(Vector((x, y, z)), UP, r + 0.06, 0.18, 20, None)
    footprints.append((f"silo {x},{y}", circle(x, y, r + 0.35)))


def fuel_tank(x, y, r=2.3, h=5.0, with_ladder=False):
    parts["Concrete"].cylinder(Vector((x, y, 0)), UP, r + 0.4, 0.3, 20, 4.0)
    parts["Metal_Light"].cylinder(Vector((x, y, 0.3)), UP, r, h - 0.3, 20, 3.0)
    parts["Metal_Light"].cone(Vector((x, y, h)), UP, r, 0.7, 20, 3.0)
    parts["Metal_Dark"].cylinder(Vector((x, y, h - 0.25)), UP, r + 0.05, 0.12, 20, None)
    if with_ladder:
        ladder(Vector((x - r - 0.12, y, 0.3)), h, Vector((-1, 0, 0)))
    footprints.append((f"fuel tank {x},{y}", circle(x, y, r + 0.4)))


def water_tower(x, y):
    leg_top, spread = 10.0, 2.1
    for sx in (-1, 1):
        for sy in (-1, 1):
            foot = Vector((x + sx * (spread + 0.5), y + sy * (spread + 0.5), 0))
            top = Vector((x + sx * spread, y + sy * spread, leg_top))
            along = (top - foot).normalized()
            across = (Vector((1, 0, 0)) - along * along.x).normalized()
            parts["Metal_Dark"].box((foot + top) / 2, across, along.cross(across), along, 0.16, 0.16, (top - foot).length / 2)
            parts["Concrete"].block(foot, (0.8, 0.8, 0.4), 0, 4.0)
    for level in (3.5, 7.0):
        for (ax, ay) in ((1, 0), (0, 1)):
            for s in (-1, 1):
                offset = spread + 0.5 * (1 - level / leg_top)
                centre = Vector((x + (s * offset if ay else 0), y + (s * offset if ax else 0), level))
                parts["Metal_Dark"].box(centre, Vector((ax, ay, 0)), Vector((-ay, ax, 0)), UP, offset, 0.06, 0.06)
    parts["Metal_Dark"].block(Vector((x, y, leg_top)), (2 * spread + 0.6, 2 * spread + 0.6, 0.25), 0, None)
    parts["Metal_Light"].cylinder(Vector((x, y, leg_top + 0.25)), UP, 2.6, 3.4, 20, 3.0)
    parts["Metal_Roof"].cone(Vector((x, y, leg_top + 3.65)), UP, 2.75, 1.5, 20, 3.0)
    ladder(Vector((x + spread + 0.55, y, 0.0)), leg_top, Vector((1, 0, 0)))
    footprints.append((f"water tower {x},{y}", rect(x, y, 2 * spread + 1.9, 2 * spread + 1.9)))


def gable_prism(builder, cx, cy, length, width, eave, ridge, yaw, scale, base=0.0, overhang=0.0, thickness=None):
    """A building block with a gable top (thickness None), or a gable roof slab `thickness` thick resting on such a
    block, `overhang` past its walls; the ridge runs along the yaw direction."""
    ax, ay = axes(yaw)
    c = Vector((cx, cy, 0))
    L, W = length / 2 + overhang, width / 2 + overhang
    tan = (ridge - eave) / (width / 2)
    if thickness is None:
        profile = [(-W, base), (-W, eave), (0.0, ridge), (W, eave), (W, base)]
        quads = []
    else:
        low = eave - overhang * tan
        profile = [(-W, low), (-W, low + thickness), (0.0, ridge + thickness), (W, low + thickness), (W, low), (0.0, ridge)]
    n = len(profile)
    points = [c + ax * along + ay * across + UP * z for along in (-L, L) for across, z in profile]
    faces = [(i, (i + 1) % n, n + (i + 1) % n, n + i) for i in range(n)]
    if thickness is None:
        faces += [tuple(range(n)), tuple(range(n, 2 * n))]
    else:
        faces += [(0, 1, 2, 5), (5, 2, 3, 4), (n, n + 1, n + 2, n + 5), (n + 5, n + 2, n + 3, n + 4)]

    def uv_of(face, co):
        d = co - c
        along, across = d.dot(ax), d.dot(ay)
        normal = face.normal
        if abs(normal.dot(ax)) > 0.9:
            return (across / scale, co.z / scale)
        if abs(normal.z) < 0.3:
            return (along / scale, co.z / scale)
        return (along / scale, (W - abs(across)) / scale)

    builder.piece(points, faces, uv_of)


def wall(builder, label, start, end, height, thickness, openings=(), scale=3.0):
    """A straight wall along its centre line from `start` to `end` (x, y), with doorways: (from, to, top) in metres
    along it from `start`; a lintel closes each doorway above its top."""
    a, b = Vector((start[0], start[1], 0)), Vector((end[0], end[1], 0))
    d = b - a
    length = d.length
    d.normalize()
    side = Vector((-d.y, d.x, 0))
    spans, at = [], 0.0
    for o0, o1, top in sorted(openings):
        if o0 > at:
            spans.append((at, o0, 0.0, height))
        if top < height:
            spans.append((o0, o1, top, height))
        at = o1
    if at < length:
        spans.append((at, length, 0.0, height))
    for s0, s1, z0, z1 in spans:
        builder.box(a + d * ((s0 + s1) / 2) + UP * ((z0 + z1) / 2), d, side, UP, (s1 - s0) / 2, thickness / 2, (z1 - z0) / 2, scale)
        if z0 == 0.0:
            mid = a + d * ((s0 + s1) / 2)
            along_x = abs(d.x) > 0.5
            footprints.append((f"wall {label}", rect(mid.x, mid.y, (s1 - s0) if along_x else thickness,
                                                     thickness if along_x else (s1 - s0))))


def gable_end(builder, x, y_from, y_to, eave, ridge, thickness, scale=3.0):
    """The triangle of wall above the eaves at one end of a gable roof running along x."""
    mid = (y_from + y_to) / 2
    points = [Vector((x + dx, py, pz)) for dx in (0.0, thickness) for py, pz in ((y_from, eave), (mid, ridge), (y_to, eave))]
    faces = [(0, 1, 2), (3, 4, 5), (0, 1, 4, 3), (1, 2, 5, 4), (2, 0, 3, 5)]
    builder.piece(points, faces, lambda face, co: (co.y / scale, co.z / scale))


def lamp_hanging(x, y, z, cable):
    parts["Metal_Dark"].block(Vector((x, y, z + 0.12)), (0.04, 0.04, cable), 0, None)
    parts["Lamp"].block(Vector((x, y, z)), (1.0, 0.3, 0.12), 0, None)


def crate_pile(label, crates_at):
    """Crates (1.2 m) at (x, y, level, yaw); level 1 sits on level 0."""
    xs, ys = [], []
    for cx, cy, level, yaw in crates_at:
        parts["Crate"].block(Vector((cx, cy, 1.2 * level)), (1.2, 1.2, 1.2), yaw, None)
        xs += [cx - 0.75, cx + 0.75]
        ys += [cy - 0.75, cy + 0.75]
    footprints.append((f"crates {label}", [(min(xs), min(ys)), (max(xs), min(ys)), (max(xs), max(ys)), (min(xs), max(ys))]))
    return len(crates_at)


def hangar(x, y, length=18.0, width=14.0, eave=7.0, ridge=9.6):
    """A metal hangar to walk into, its ridge along x: two big doorways in the west gable wall (their roller doors
    rolled up) and a side door in the north wall; inside, 20 crates in five piles."""
    metal, t = parts["Metal_Light"], 0.3
    xa, xb, ya, yb = x - length / 2, x + length / 2, y - width / 2, y + width / 2
    parts["Concrete"].block(Vector((x, y, 0)), (length + 0.6, width + 0.6, 0.08), 0, 4.0)
    wall(metal, "hangar west", (xa + t / 2, ya), (xa + t / 2, yb), eave, t, [(1.4, 5.4, 5.0), (8.6, 12.6, 5.0)])
    wall(metal, "hangar east", (xb - t / 2, ya), (xb - t / 2, yb), eave, t)
    wall(metal, "hangar north", (xa + t, yb - t / 2), (xb - t, yb - t / 2), eave, t, [(12.1, 15.1, 3.4)])
    wall(metal, "hangar south", (xa + t, ya + t / 2), (xb - t, ya + t / 2), eave, t)
    gable_end(metal, xa, ya, yb, eave, ridge, t)
    gable_end(metal, xb - t, ya, yb, eave, ridge, t)
    gable_prism(parts["Ceiling"], x, y, length, width, eave, ridge + 0.02, 0, 3.0, overhang=0.5, thickness=0.22)
    for door_y in (ya + 3.4, ya + 10.6):
        parts["Metal_Dark"].block(Vector((xa - 0.25, door_y, 5.0)), (0.5, 4.6, 0.6), 0, None)    # rolled-up door
        parts["Roller_Door"].block(Vector((xa - 0.06, door_y, 4.6)), (0.1, 4.0, 0.4), 0, None)
    for side in (-1, 1):
        for i in range(5):
            wx = xa + 2.4 + i * 3.3
            parts["Office_Window"].block(Vector((wx, y + side * (width / 2 + 0.04), 4.9)), (2.2, 0.1, 1.1), 0, None)
    for sx in (-1, 1):
        for sy in (-1, 1):
            parts["Metal_Dark"].block(Vector((x + sx * (length / 2 + 0.02), y + sy * (width / 2 + 0.02), 0.0)), (0.36, 0.36, eave), 0, None)
    for lx in (x - 5.0, x, x + 5.0):
        lamp_hanging(lx, y, 6.2, 1.6)
    count = crate_pile("hangar 1", [(76.0, -119.0, 0, 0), (77.25, -119.0, 0, 3), (76.0, -117.75, 0, -2), (77.25, -117.75, 0, 0),
                                    (76.6, -119.0, 1, 4), (76.6, -117.75, 1, -3)])
    count += crate_pile("hangar 2", [(79.0, -108.6, 0, 0), (80.25, -108.6, 0, 2), (81.5, -108.6, 0, -3),
                                     (79.6, -108.6, 1, 5), (80.85, -108.6, 1, 0), (80.2, -108.6, 2, -4)])
    count += crate_pile("hangar 3", [(84.5, -113.0, 0, 0), (85.75, -113.0, 0, 6), (85.1, -113.0, 1, -2)])
    count += crate_pile("hangar 4", [(86.8, -118.6, 0, 0), (86.8, -117.35, 0, -5), (85.55, -118.6, 0, 3)])
    count += crate_pile("hangar 5", [(78.0, -114.5, 0, 8), (78.0, -114.5, 1, -6)])
    return count


# ------------------------------------------------------------------------------------------------ bombsite B

def delete_parts(mesh_name, inside):
    """Deletes a map mesh's loose parts whose middle is inside(centre); returns how many went."""
    obj = bpy.data.objects.get(mesh_name)
    if obj is None:
        return 0
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    gone = 0
    for verts in loose_parts(bm):
        points = corners(verts)
        if inside(sum(points, Vector()) / len(points)):
            bmesh.ops.delete(bm, geom=list(set(verts)), context="VERTS")
            gone += 1
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    return gone


def add_floor(mesh_name, x0, x1, y0, y1, z, scale):
    """A flat rectangle facing up in a map mesh, its texture tiling every `scale` metres like the rest of it."""
    obj = bpy.data.objects[mesh_name]
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    uv = bm.loops.layers.uv.verify()
    face = bm.faces.new([bm.verts.new((px, py, z)) for px, py in ((x0, y0), (x1, y0), (x1, y1), (x0, y1))])
    face.normal_update()
    if face.normal.z < 0:
        face.normal_flip()
    for loop in face.loops:
        loop[uv].uv = (loop.vert.co.x / scale, loop.vert.co.y / scale)
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()


def move_decal(mesh_name, cx, cy, size):
    """Moves a painted letter (keeping its turn) to (cx, cy) and makes it `size` metres across."""
    mesh = bpy.data.objects[mesh_name].data
    xs, ys = [v.co.x for v in mesh.vertices], [v.co.y for v in mesh.vertices]
    ox, oy = (min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2
    s = size / max(max(xs) - min(xs), max(ys) - min(ys))
    for v in mesh.vertices:
        v.co.x, v.co.y = cx + (v.co.x - ox) * s, cy + (v.co.y - oy) * s
    mesh.update()


def b_site():
    """Bombsite B, rebuilt as a brick building of three rooms where the bomb can be planted: two rooms at the north
    and a hall at the south, joined by doorways. One entrance is in the north wall (from the middle of the map,
    the Terrorists' way in), one in the east wall (from the lane SWAT come up by the warehouse). The site's tiles
    are its floors and the B is painted in the hall; crates, barrels and lamps inside. What stood here goes."""
    x0, x1, y0, y1 = 9.0, 39.0, -59.0, -37.0          # outside faces of the walls
    region = lambda c: 8.0 <= c.x <= 40.0 and -60.0 <= c.y <= -36.0
    cleared = delete_parts("Wall_Concrete", region) + delete_parts("Crate_Wood", region)
    delete_parts("Site_Tiles", lambda c: c.x < 48.0)    # site B's old open floor (site A is at x > 55)
    rooms = [(9.5, 23.5, -47.0, -37.5), (24.0, 38.5, -47.0, -37.5), (9.5, 38.5, -58.5, -47.5)]
    for rx0, rx1, ry0, ry1 in rooms:
        add_floor("Site_Tiles", rx0, rx1, ry0, ry1, 0.06, 4.0)
    move_decal("Decal_B", 24.0, -53.0, 6.0)

    brick, h, t = parts["Brick"], 5.0, 0.5
    wall(brick, "B north", (x0, y1 - t / 2), (x1, y1 - t / 2), h, t, [(19.0, 22.0, 3.0)])
    wall(brick, "B south", (x0, y0 + t / 2), (x1, y0 + t / 2), h, t)
    wall(brick, "B west", (x0 + t / 2, y1 - t), (x0 + t / 2, y0 + t), h, t)
    wall(brick, "B east", (x1 - t / 2, y1 - t), (x1 - t / 2, y0 + t), h, t, [(13.0, 16.0, 3.0)])
    wall(brick, "B hall", (x0 + t, -47.25), (x1 - t, -47.25), h, t, [(8.5, 11.0, 2.6), (23.5, 26.0, 2.6)])
    wall(brick, "B rooms", (23.75, y1 - t), (23.75, -47.0), h, t, [(2.5, 4.9, 2.6)])
    parts["Ceiling"].block(Vector(((x0 + x1) / 2, (y0 + y1) / 2, h)), (x1 - x0 + 0.4, y1 - y0 + 0.4, 0.3), 0, 3.0)
    for lx, ly in ((16.5, -42.2), (31.2, -42.2), (16.0, -53.0), (32.0, -53.0)):
        lamp_hanging(lx, ly, 4.45, 0.45)

    crates(11.0, -40.4, 3)
    barrels(20.3, -44.7, 3)
    crates(35.2, -40.0, 2)
    crates(12.4, -56.7, 3)
    crates(31.0, -57.0, 2, yaw=8)
    barrels(36.0, -49.6, 3, ("Blue", "Red"))
    return cleared


def office(x, y, sx=9.0, sy=7.0, floors=2):
    """A concrete office: rows of windows on both floors, a door on the east side, a flat roof with a parapet,
    air conditioners and a stair hut."""
    floor_h = 3.2
    h = floors * floor_h
    parts["Concrete"].block(Vector((x, y, 0)), (sx, sy, h), 0, 4.0)
    for cx, cy, wx, wy in ((x, y + sy / 2 - 0.125, sx, 0.25), (x, y - sy / 2 + 0.125, sx, 0.25),
                           (x + sx / 2 - 0.125, y, 0.25, sy - 0.5), (x - sx / 2 + 0.125, y, 0.25, sy - 0.5)):
        parts["Concrete"].block(Vector((cx, cy, h)), (wx, wy, 0.55), 0, 4.0)   # parapet round the roof
    for f in range(floors):
        z = f * floor_h + 1.1
        for i in range(3):
            wx = x - sx / 2 + 1.6 + i * 2.9
            for side in (-1, 1):
                parts["Office_Window"].block(Vector((wx, y + side * (sy / 2 + 0.03), z)), (1.6, 0.08, 1.3), 0, None)
                parts["Metal_Dark"].block(Vector((wx, y + side * (sy / 2 + 0.09), z - 0.12)), (1.8, 0.2, 0.1), 0, None)
        for j in range(2):
            wy = y - sy / 2 + 1.9 + j * 3.2
            if f == 0 and j == 1:
                continue   # the door is there
            parts["Office_Window"].block(Vector((x + sx / 2 + 0.03, wy, z)), (0.08, 1.6, 1.3), 0, None)
            parts["Office_Window"].block(Vector((x - sx / 2 - 0.03, wy, z)), (0.08, 1.6, 1.3), 0, None)
    door_y = y - sy / 2 + 1.9 + 3.2
    parts["Metal_Dark"].block(Vector((x + sx / 2 + 0.04, door_y, 0)), (0.1, 1.2, 2.3), 0, None)
    parts["Concrete"].block(Vector((x + sx / 2 + 0.6, door_y, 2.5)), (1.2, 2.0, 0.15), 0, 4.0)
    parts["Concrete"].block(Vector((x - 2.0, y + 1.2, h)), (2.2, 2.2, 2.3), 0, 4.0)
    parts["Metal_Dark"].block(Vector((x - 2.0 + 1.12, y + 1.2, h)), (0.06, 0.9, 2.0), 0, None)
    for dx in (1.3, 3.0):
        parts["Metal_Light"].block(Vector((x + dx, y - 1.6, h)), (1.2, 0.9, 0.75), 0, None)
        parts["Metal_Dark"].cylinder(Vector((x + dx, y - 1.6, h + 0.75)), UP, 0.32, 0.04, 12, None)
    footprints.append((f"office {x},{y}", rect(x + 0.5, y, sx + 1.2, sy)))


def guard_booth(x, y):
    parts["Concrete"].block(Vector((x, y, 0)), (2.6, 2.6, 1.0), 0, 4.0)
    for (ex, ey, wx, wy) in ((0, 1.3, 2.6, 0.12), (0, -1.3, 2.6, 0.12), (1.3, 0, 0.12, 2.6), (-1.3, 0, 0.12, 2.6)):
        parts["Office_Window"].block(Vector((x + ex * 0.97, y + ey * 0.97, 1.0)), (max(wx, 0.12), max(wy, 0.12), 1.3), 0, None)
    for sx in (-1, 1):
        for sy in (-1, 1):
            parts["Metal_Dark"].block(Vector((x + sx * 1.25, y + sy * 1.25, 1.0)), (0.14, 0.14, 1.3), 0, None)
    parts["Concrete"].block(Vector((x, y, 2.3)), (2.6, 2.6, 0.4), 0, 4.0)
    parts["Metal_Roof"].block(Vector((x, y, 2.7)), (3.3, 3.3, 0.18), 0, 3.0)
    # The barrier's post and its arm, raised.
    parts["Metal_Dark"].block(Vector((x + 2.0, y - 1.0, 0)), (0.35, 0.35, 1.1), 0, None)
    parts["Hazard"].block(Vector((x + 2.0, y - 1.0, 1.1)), (0.12, 0.12, 4.2), 0, 1.0)
    footprints.append((f"guard booth {x},{y}", rect(x + 0.5, y - 0.3, 3.8, 3.4)))


def loading_dock(x_wall, y_from, y_to, depth=2.8):
    """A low concrete apron (easy to step on) in front of the warehouse's two big doors, a hazard stripe painted
    along its edge, the doors rolled up into their housings, and a canopy over it all."""
    cy, length = (y_from + y_to) / 2, abs(y_to - y_from)
    parts["Concrete"].block(Vector((x_wall + depth / 2, cy, 0)), (depth, length, 0.12), 0, 4.0)
    parts["Hazard"].block(Vector((x_wall + depth - 0.2, cy, 0.12)), (0.3, length, 0.02), 0, 1.0)
    for dy in (-length / 4, length / 4):
        parts["Metal_Dark"].block(Vector((x_wall + 0.3, cy + dy, 4.6)), (0.6, 4.6, 0.6), 0, None)    # rolled-up door
        parts["Roller_Door"].block(Vector((x_wall + 0.06, cy + dy, 4.2)), (0.12, 4.0, 0.4), 0, None)
    canopy = Vector((x_wall + depth / 2 + 0.2, cy, 5.7))
    parts["Metal_Roof"].box(canopy, Vector((1, 0, -0.08)).normalized(), Vector((0, 1, 0)), Vector((0.08, 0, 1)).normalized(),
                            depth / 2 + 0.2, length / 2, 0.08, 3.0)   # sloping down away from the wall
    footprints.append(("wall warehouse dock", rect(x_wall + depth / 2, cy, depth, length)))


def warehouse():
    """The old brick warehouse, made into one to walk into: its two big doors open onto the loading dock (east)
    and a door in the south wall faces the SWAT spawn; the west side is the map's boundary wall. Inside: a
    concrete floor, steel columns, lamps and 20 crates in five piles. Its roof is a Ceiling (left out of the radar)."""
    old = bpy.data.objects.get("Building_Brick")
    if old is not None:
        bpy.data.objects.remove(old, do_unlink=True)
    delete_parts("Wall_Concrete", lambda c: c.z > 6.5 and c.x < 36.0 and c.y < -60.0)   # its old flat roof
    # A low wall at its north-east corner reached inside.
    delete_parts("Wall_Concrete", lambda c: abs(c.x - 34.5) < 0.6 and abs(c.y + 63.5) < 0.6 and c.z < 1.0)

    brick, h, t = parts["Brick"], 7.0, 0.5
    x0, x1, y0, y1 = 0.6, 34.5, -107.0, -63.5
    wall(brick, "warehouse east", (x1 - t / 2, y1), (x1 - t / 2, y0), h, t, [(16.625, 20.625, 4.6), (22.875, 26.875, 4.6)])
    wall(brick, "warehouse north", (x0, y1 - t / 2), (x1 - t, y1 - t / 2), h, t)
    wall(brick, "warehouse south", (x0, y0 + t / 2), (x1 - t, y0 + t / 2), h, t, [(5.4, 7.8, 3.0)])
    parts["Ceiling"].block(Vector(((x0 + x1) / 2, (y0 + y1) / 2, h)), (x1 - x0 + 0.2, y1 - y0 + 0.4, 0.3), 0, 3.0)
    parts["Concrete"].block(Vector(((x0 + x1 - t) / 2, (y0 + y1) / 2, 0)), (x1 - x0 - t, y1 - y0 - 2 * t, 0.04), 0, 4.0)
    for cx in (12.0, 23.0):
        for cy in (-78.0, -92.0):
            parts["Metal_Dark"].block(Vector((cx, cy, 0)), (0.45, 0.45, h), 0, None)
            footprints.append((f"column {cx},{cy}", rect(cx, cy, 0.45, 0.45)))
    for lx in (8.0, 19.0, 29.0):
        for ly in (-73.0, -97.0):
            lamp_hanging(lx, ly, 5.6, 1.3)
    count = crate_pile("warehouse 1", [(6.0, -70.0, 0, 0), (7.25, -70.0, 0, 4), (8.5, -70.0, 0, -3),
                                       (6.6, -70.0, 1, 2), (7.85, -70.0, 1, -5), (7.2, -70.0, 2, 3)])
    count += crate_pile("warehouse 2", [(14.0, -98.0, 0, 0), (15.25, -98.0, 0, -4), (14.0, -96.75, 0, 3), (15.25, -96.75, 0, 0),
                                        (14.6, -98.0, 1, 6), (14.6, -96.75, 1, -2)])
    count += crate_pile("warehouse 3", [(27.0, -70.0, 0, 0), (28.25, -70.0, 0, 5), (27.6, -70.0, 1, -3)])
    count += crate_pile("warehouse 4", [(28.0, -102.0, 0, 0), (28.0, -100.75, 0, -4), (26.75, -102.0, 0, 2)])
    count += crate_pile("warehouse 5", [(19.0, -85.0, 0, 7), (19.0, -85.0, 1, -5)])
    return count


def pipe_rack(x_from, x_to, y, step=6.0):
    """Frames along a wall carrying three pipes."""
    x = x_from
    while x <= x_to + 1e-3:
        for dy in (-0.7, 0.7):
            parts["Metal_Dark"].block(Vector((x, y + dy, 0)), (0.25, 0.25, 4.0), 0, None)
        parts["Metal_Dark"].block(Vector((x, y, 4.0)), (0.3, 1.8, 0.22), 0, None)
        x += step
    for dy, r, name in ((-0.5, 0.24, "Pipe"), (0.0, 0.18, "Pipe_Yellow"), (0.5, 0.24, "Pipe")):
        parts[name].cylinder(Vector((x_from - 0.6, y + dy, 4.22 + r)), Vector((1, 0, 0)), r, x_to - x_from + 1.2, 12, 1.0)
    footprints.append(("pipe rack", rect((x_from + x_to) / 2, y, x_to - x_from + 1.2, 1.8)))


def lamp_post(x, y, facing_yaw):
    ax, ay = axes(facing_yaw)
    parts["Concrete"].block(Vector((x, y, 0)), (0.6, 0.6, 0.3), 0, 4.0)
    parts["Metal_Dark"].block(Vector((x, y, 0.3)), (0.18, 0.18, 6.7), 0, None)
    parts["Metal_Dark"].block(Vector((x, y, 6.9)) + ax * 0.7, (1.5, 0.12, 0.12), facing_yaw, None)
    parts["Lamp"].block(Vector((x, y, 6.72)) + ax * 1.25, (0.7, 0.36, 0.18), facing_yaw, None)
    footprints.append((f"lamp {x},{y}", rect(x, y, 0.6, 0.6)))


# ------------------------------------------------------------------------------------------------ the layout
# Positions are Blender metres (the game turns the map round; see ModelMap). T spawn is top right (x 70-90,
# y 0 to -14), SWAT bottom left (x 0-23, y -110 to -128), site B left of the middle, site A right of it.

silo(5.0, -4.6)
silo(5.0, -12.2)
parts["Metal_Dark"].block(Vector((5.0, -8.4, 12.6)), (1.0, 2.6, 0.15), 0, None)          # walkway between the silos
ladder(Vector((7.75, -12.2, 0.6)), 13.0, Vector((1, 0, 0)))

fuel_tank(86.8, -27.0, with_ladder=True)
fuel_tank(86.8, -33.6)
parts["Pipe"].cylinder(Vector((84.2, -27.0, 0.9)), Vector((0, -1, 0)), 0.16, 6.6, 10, 1.0)
for ty in (-27.0, -33.6):
    parts["Pipe"].cylinder(Vector((84.2, ty, 0.9)), Vector((1, 0, 0)), 0.16, 0.5, 10, 1.0)

water_tower(85.8, -99.6)
hangar_crates = hangar(79.6, -113.4)
cleared = b_site()
office(48.2, -107.5)
guard_booth(62.0, -5.0)
warehouse_crates = warehouse()
loading_dock(34.5, -79.0, -91.5)
pipe_rack(17.5, 47.5, -1.8)

container("Red", 31.0, -15.8)
container("Green", 31.0, -15.8, level=1)
container("Orange", 52.0, -6.2, yaw=90)
container("Blue", 47.0, -30.0, yaw=8)
container("Green", 88.0, -55.0, yaw=90)
container("Orange", 60.0, -121.2)
container("Orange", 60.0, -121.2, level=1)
container("Blue", 66.6, -121.4, yaw=4)

for bx, by, yaw in ((62.0, -50.0, 20), (64.2, -48.9, 35), (75.4, -80.4, 0), (78.0, -80.4, 0),
                    (60.5, -22.5, -30), (67.0, -36.0, 10)):
    barrier(bx, by, yaw)

barrels(82.0, -76.0, 3)
barrels(46.0, -101.2, 3, ("Blue", "Red"))
barrels(30.0, -4.6, 3)
barrels(56.0, -118.2, 4)

crates(64.0, -18.0, 3)
crates(58.0, -100.0, 3)

for lx, ly, yaw in ((60.4, -1.6, -40), (74.0, -19.4, 120), (37.6, -76.6, 0), (26.0, -108.4, 90), (69.0, -103.0, 180)):
    lamp_post(lx, ly, yaw)


# ------------------------------------------------------------------------------------------------ checking

def hull(points):
    pts = sorted(set((round(x, 3), round(y, 3)) for x, y in points))
    if len(pts) < 3:
        return pts

    def cross(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])
    lower, upper = [], []
    for p in pts:
        while len(lower) >= 2 and cross(lower[-2], lower[-1], p) <= 0:
            lower.pop()
        lower.append(p)
    for p in reversed(pts):
        while len(upper) >= 2 and cross(upper[-2], upper[-1], p) <= 0:
            upper.pop()
        upper.append(p)
    return lower[:-1] + upper[:-1]


def overlap(a, b, margin):
    """Separating-axis test for two convex polygons, with a gap of `margin` metres still counting as touching."""
    for poly in (a, b):
        for i in range(len(poly)):
            x1, y1 = poly[i]
            x2, y2 = poly[(i + 1) % len(poly)]
            nx, ny = y2 - y1, x1 - x2
            length = math.hypot(nx, ny)
            if length < 1e-9:
                continue
            nx, ny = nx / length, ny / length
            pa = [x * nx + y * ny for x, y in a]
            pb = [x * nx + y * ny for x, y in b]
            if max(pa) + margin < min(pb) or max(pb) + margin < min(pa):
                return False
    return True


obstacles = []
for obj in bpy.data.objects:
    if obj.type != "MESH" or obj.name.startswith("Add_") or obj.name.startswith("Ground") or obj.name.startswith("Road"):
        continue
    margin = 1.0 if obj.name.endswith("_Tiles") and "Spawn" in obj.name else 0.3
    if obj.name == "Site_Tiles":
        continue
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    for verts in loose_parts(bm):
        points = corners(verts)
        if len(points) == 24 and obj.name == "Wall_Concrete":
            continue   # the boundary wall: checked against INNER instead
        if min(p.z for p in points) > 3.0:
            continue   # high up (the crane's arm, the warehouse roof)
        obstacles.append((f"{obj.name}@({sum(p.x for p in points) / len(points):.1f},{sum(p.y for p in points) / len(points):.1f})",
                          hull([(p.x, p.y) for p in points]), margin))
    bm.free()

problems = []
for label, poly in footprints:
    against_boundary = label.startswith("wall warehouse")   # the warehouse uses the boundary wall as its west side
    if not against_boundary and any(not (INNER[0] + 0.2 <= x <= INNER[1] - 0.2 and INNER[2] + 0.2 <= y <= INNER[3] - 0.2) for x, y in poly):
        problems.append(f"{label}: outside the boundary wall")
    for name, other, margin in obstacles:
        if len(other) >= 3 and overlap(poly, other, margin):
            problems.append(f"{label}: touches {name}")
for i, (label, poly) in enumerate(footprints):
    for other_label, other in footprints[i + 1:]:
        stacked = label.startswith("container") and other_label.startswith("container")
        joined = label.startswith("wall ") and other_label.startswith("wall ")   # a building's walls meet
        if overlap(poly, other, 0.2) and not stacked and not joined:
            problems.append(f"{label}: touches {other_label}")

for builder in parts.values():
    builder.finish()
if problems:
    print("[industrial] NOT SAVED, overlaps:\n  " + "\n  ".join(problems))
    sys.exit(1)
bpy.ops.wm.save_mainfile()
print(f"[industrial] site B rebuilt ({cleared} old parts cleared), {hangar_crates} crates in the hangar, {warehouse_crates} in the warehouse; "
      f"added {len(footprints)} structures in {len(parts)} objects, "
      f"{sum(len(bpy.data.objects['Add_' + n].data.polygons) for n in parts)} faces")
