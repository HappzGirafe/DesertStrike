"""Remakes the houses of a map .blend (Village or Halloween): every roof is rebuilt to rest on its walls, and the
houses get trim and details. Run it after Tools/fix_map_rounds.py and before Tools/export_map_fbx.py:

    blender -b Art/Blender/village_map.blend --python Tools/remake_houses.py -- village
    blender -b Art/Blender/halloween_map.blend --python Tools/remake_houses.py -- halloween

The maps' generator set each roof too low, so the tops of the walls cut through it (a light stripe along every roof),
and built the gable triangles facing inwards, so the game hid them and the attics showed the roof's dark underside;
the walls' faces are turned outwards here.
A house is a wall block with two gable points (10 corners) plus a roof slab (12 corners) in a Roof_* mesh. Each roof
is rebuilt with its underside on the walls' sloping tops and the same overhangs. Then the script adds: a ridge cap,
boards along the roof's edges, frames and sills round the windows, frames round the doors, a little roof over a door
in a gable wall, boards on the corners of plaster houses, coloured shutters (Village) and caps on the chimneys.
The added parts are their own objects (House_Trim, Roof_Ridge, Chimney_Cap, Shutter_*), so running it again gives
the same result. The .blend is saved in place.
"""
import math
import os
import sys

import bmesh
import bpy
from mathutils import Vector

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from blender_build import UP, Builder, corners, flat_box, loose_parts, material, new_object, tinted_texture  # noqa: E402

STYLES = {
    "village": {
        "bodies": ["House_Plaster", "House_Plaster_Ochre", "House_Timber", "Barn_Red", "Wall_Stone"],
        "roofs": ["Roof_Red", "Roof_Brown", "Roof_Slate"],
        "windows": ["Window"],
        "doors": ["Door", "Wood_Planks"],
        "chimneys": ["Wall_Stone"],
        "plaster": ["House_Plaster", "House_Plaster_Ochre", "Barn_Red"],
        "shutters": ["House_Plaster", "House_Plaster_Ochre"],
        "canopies": ["House_Plaster", "House_Plaster_Ochre", "House_Timber"],
        "trim": ("village_Wood_Planks.png", 1.0),
        "cap": ("village_Wall_Stone.png", 2.0),
        "ridge": (0.30, 0.22, 0.19),
        "shutter_colours": {"Green": (0.45, 0.68, 0.42), "Blue": (0.42, 0.55, 0.78), "Red": (0.82, 0.36, 0.3)},
    },
    "halloween": {
        "bodies": ["House_Rotten_Plaster", "House_Plaster", "House_Plaster_Ochre", "House_Timber", "Barn_Red", "Stone_Dark"],
        "roofs": ["Roof_Brown", "Roof_Purple", "Roof_Slate"],
        "windows": ["Window_Boarded", "Window_Glow"],
        "doors": ["Wood_Dark"],
        "chimneys": ["Wall_Stone", "Stone_Dark"],
        "plaster": ["House_Rotten_Plaster", "House_Plaster", "House_Plaster_Ochre", "Barn_Red"],
        "shutters": [],
        "canopies": ["House_Rotten_Plaster", "House_Plaster", "House_Plaster_Ochre", "House_Timber"],
        "trim": ("halloween_Wood_Dark.png", 1.6),
        "cap": ("halloween_Stone_Dark.png", 1.0),
        "ridge": (0.13, 0.11, 0.15),
        "shutter_colours": {},
    },
}

style = STYLES[sys.argv[sys.argv.index("--") + 1]]

# ------------------------------------------------------------------------------------------------ houses and their parts

class Box:
    """An 8-corner part (door, window, chimney): centre, size along its own horizontal axes, and those axes."""

    def __init__(self, points):
        self.points = points
        low = min(p.z for p in points)
        base = [p for p in points if abs(p.z - low) < 1e-3]
        if len(base) != 4:
            raise ValueError("not an upright box")
        # The base rectangle's edge directions: from one corner to its two nearest neighbours.
        a = base[0]
        others = sorted(base[1:], key=lambda p: (p - a).length)
        e1, e2 = (others[0] - a), (others[1] - a)
        e1.z = e2.z = 0.0
        self.x, self.y = e1.normalized(), e2.normalized()
        self.size_x, self.size_y = e1.length, e2.length
        self.bottom, self.top = low, max(p.z for p in points)
        self.centre = sum(points, Vector()) / len(points)


class House:
    """A wall block with gable points: footprint frame (ridge axis r, across axis a), base, eave and ridge heights."""

    def __init__(self, mesh_name, points):
        self.mesh = mesh_name
        zs = sorted({round(p.z, 3) for p in points})
        self.z0, self.z2 = zs[0], zs[-1]
        apexes = [p for p in points if abs(p.z - self.z2) < 1e-3]
        tops = [p for p in points if abs(p.z - self.z0) > 1e-3 and abs(p.z - self.z2) > 1e-3]
        self.z1 = sum(p.z for p in tops) / len(tops)
        ridge = apexes[1] - apexes[0]
        ridge.z = 0.0
        self.L = ridge.length / 2
        self.r = ridge.normalized()
        self.a = Vector((-self.r.y, self.r.x, 0.0))
        self.c = (apexes[0] + apexes[1]) / 2
        self.c.z = 0.0
        self.W = max(abs((p - self.c).dot(self.a)) for p in points)
        self.tan = (self.z2 - self.z1) / self.W

    def local(self, p):
        d = p - self.c
        return d.dot(self.r), d.dot(self.a)

    def world(self, along, across, z):
        return self.c + self.r * along + self.a * across + UP * z

    def contains(self, p, margin):
        along, across = self.local(p)
        return abs(along) <= self.L + margin and abs(across) <= self.W + margin

    def wall_of(self, box):
        """The wall a door or window sits on: (outward normal, along-wall axis, wall half-length, offset of the
        wall plane from the centre), or None when the box is not flat against a wall."""
        along, across = self.local(box.centre)
        thin = box.x if box.size_x < box.size_y else box.y
        for normal, axis, half, offset, position in ((self.r, self.a, self.W, self.L, along), (self.a, self.r, self.L, self.W, across)):
            if abs(thin.dot(normal)) > 0.9 and abs(abs(position) - offset) < 0.35:
                sign = 1.0 if position > 0 else -1.0
                return normal * sign, axis, half, offset
        return None


# ------------------------------------------------------------------------------------------------ the houses

houses = []
flipped = 0
for name in style["bodies"]:
    obj = bpy.data.objects.get(name)
    if obj is None:
        continue
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    for verts in loose_parts(bm):
        points = corners(verts)
        if len(points) == 10 and len({round(p.z, 3) for p in points}) == 3:
            house = House(name, points)
            houses.append(house)
            # The generator built both gable triangles facing inwards, so the game hid them and the attic showed
            # the roof's dark underside: turn every face of the walls outwards.
            part = set(verts)
            middle = house.c + Vector((0, 0, (house.z0 + house.z1) / 2))   # the middle of the wall block
            for face in {f for v in verts for f in v.link_faces if all(w in part for w in f.verts)}:
                face.normal_update()
                outward = face.calc_center_median() - middle
                if abs(face.normal.z) < 0.5:
                    outward.z = 0.0                                          # walls and gables face out sideways
                if face.normal.dot(outward) < 0:
                    face.normal_flip()
                    flipped += 1
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
print(f"[houses] {len(houses)} houses: {sorted({h.mesh for h in houses})}; {flipped} faces turned outwards")

# 1. Roofs: remove each house's old roof slab and build one resting on the walls.
roof_of = {}
for name in style["roofs"]:
    obj = bpy.data.objects.get(name)
    if obj is None:
        continue
    builder = Builder(obj)
    for verts in loose_parts(builder.bm):
        points = corners(verts)
        if len(points) != 12:
            continue
        middle = sum(points, Vector()) / len(points)
        middle.z = 0.0
        house = min(houses, key=lambda h: (h.c - middle).length, default=None)
        if house is None or (house.c - middle).length > 0.8 or house in roof_of:
            continue
        gable_overhang = max(abs(house.local(p)[0]) for p in points) - house.L
        eave_overhang = max(abs(house.local(p)[1]) for p in points) - house.W
        roof_of[house] = (name, min(max(gable_overhang, 0.3), 0.8), min(max(eave_overhang, 0.3), 0.8))
        bmesh.ops.delete(builder.bm, geom=list(set(verts)), context="VERTS")

    for house, (roof_name, g_o, e_o) in roof_of.items():
        if roof_name != name:
            continue
        t, lift = 0.25, 0.01
        E = house.W + e_o
        cos = 1 / math.sqrt(1 + house.tan ** 2)
        z_eave = house.z1 - e_o * house.tan + lift
        z_ridge = house.z2 + lift
        profile = [(-E, z_eave), (-E, z_eave + t), (0.0, z_ridge + t), (E, z_eave + t), (E, z_eave), (0.0, z_ridge)]
        ends = (-(house.L + g_o), house.L + g_o)
        points = [house.world(along, across, z) for along in ends for across, z in profile]
        quads = [(i, (i + 1) % 6, 6 + (i + 1) % 6, 6 + i) for i in range(6)]
        quads += [(0, 1, 2, 5), (5, 2, 3, 4), (6, 7, 8, 11), (11, 8, 9, 10)]

        def roof_uv(face, co, house=house, E=E, cos=cos):
            along, across = house.local(co)
            n = face.normal
            if abs(n.dot(house.r)) > 0.9:                      # gable ends
                return (across / 2.5, co.z / 2.5)
            if abs(n.z) < 0.3:                                 # eave edges
                return (along / 3.0, co.z / 2.5)
            return (along / 3.0, (E - abs(across)) / cos / 2.5)   # slopes: tiles run up to the ridge

        builder.piece(points, quads, roof_uv)
    builder.finish()
print(f"[houses] {len(roof_of)} roofs rebuilt")

# 2. The added parts: trim boards, ridge caps, chimney caps and shutters, each in its own object.
trim = Builder(new_object("House_Trim", material("House_Trim", style["trim"][0])))
trim_scale = style["trim"][1]
ridge = Builder(new_object("Roof_Ridge", material("Roof_Ridge", None, style["ridge"])))
caps = Builder(new_object("Chimney_Cap", material("Chimney_Cap", style["cap"][0])))
shutters = {}
for colour, tint in style["shutter_colours"].items():
    image = tinted_texture(style["trim"][0], f"village_Shutter_{colour}", tint)
    shutters[colour] = Builder(new_object(f"Shutter_{colour}", material(f"Shutter_{colour}", image)))

for house, (roof_name, g_o, e_o) in roof_of.items():
    t = 0.25
    E = house.W + e_o
    cos = 1 / math.sqrt(1 + house.tan ** 2)
    sin = house.tan * cos
    z_eave = house.z1 - e_o * house.tan + 0.01
    z_ridge = house.z2 + 0.01
    end = house.L + g_o
    # Ridge cap along the top, deep enough to sit on both slopes.
    cap_top, cap_bottom = z_ridge + t + 0.08, z_ridge + t - 0.2 * house.tan - 0.02
    ridge.box(house.world(0, 0, (cap_top + cap_bottom) / 2), house.r, house.a, UP, end + 0.06, 0.2, (cap_top - cap_bottom) / 2, None)
    # Boards along both gable ends (covering the roof's cut edge) and under both eaves.
    for side in (-1, 1):
        gable = house.r * side
        for slope in (-1, 1):
            low = house.world(side * (end + 0.005), slope * E, z_eave + t + 0.02)
            high = house.world(side * (end + 0.005), 0.0, z_ridge + t + 0.02)
            down = -(house.a * slope * sin + UP * cos)          # square to the slope, below it, in the gable's plane
            width = t * cos + 0.12
            flat_box(trim, low + down * (width / 2), high + down * (width / 2), down, width, 0.06, gable, trim_scale)
        fascia_top = z_eave + t + 0.02
        start = house.world(-end, side * (E + 0.005), fascia_top - 0.17)
        stop = house.world(end, side * (E + 0.005), fascia_top - 0.17)
        flat_box(trim, start, stop, UP, 0.34, 0.05, house.a * side, trim_scale)
    # Corner boards on plaster walls: one on each wall at every corner, the long wall's covering the corner's edge.
    if house.mesh in style["plaster"]:
        for sr in (-1, 1):
            for sa in (-1, 1):
                corner_r = house.L * sr
                corner_a = house.W * sa
                bottom, top = house.z0, house.z1
                flat_box(trim, house.world(corner_r, corner_a - sa * 0.11, bottom), house.world(corner_r, corner_a - sa * 0.11, top),
                         house.a, 0.22, 0.05, house.r * sr, trim_scale)
                flat_box(trim, house.world(corner_r - sr * 0.085, corner_a, bottom), house.world(corner_r - sr * 0.085, corner_a, top),
                         house.r, 0.27, 0.05, house.a * sa, trim_scale)


def boxes_in(names):
    found = []
    for name in names:
        obj = bpy.data.objects.get(name)
        if obj is None:
            continue
        bm = bmesh.new()
        bm.from_mesh(obj.data)
        for verts in loose_parts(bm):
            points = corners(verts)
            if len(points) == 8:
                try:
                    found.append((name, Box(points)))
                except ValueError:
                    pass
        bm.free()
    return found


def house_of(box, margin=0.4):
    best = [h for h in houses if h.contains(box.centre, margin) and box.bottom >= h.z0 - 0.7]
    return min(best, key=lambda h: (h.c - Vector((box.centre.x, box.centre.y, 0))).length) if best else None


# 3. Doors: a frame, and a little roof over the ones in a gable wall.
doors = []
for name, box in boxes_in(style["doors"]):
    width, height = max(box.size_x, box.size_y), box.top - box.bottom
    house = house_of(box)
    if house is None or min(box.size_x, box.size_y) > 0.25 or not (0.8 <= width <= 4.5 and 1.8 <= height <= 4.2):
        continue
    wall = house.wall_of(box)
    if wall is None:
        continue
    normal, axis, half, offset = wall
    along = (box.centre - house.c).dot(axis)
    surface = house.c + normal * offset
    doors.append((house, normal, along - width / 2, along + width / 2))
    f = 0.12 if width < 2.5 else 0.18
    for s in (-1, 1):
        x = along + s * (width / 2 + f / 2)
        flat_box(trim, surface + axis * x + UP * box.bottom, surface + axis * x + UP * (box.top + f), axis, f, 0.16, normal, trim_scale)
    flat_box(trim, surface + axis * (along - width / 2 - f) + UP * (box.top + f / 2),
             surface + axis * (along + width / 2 + f) + UP * (box.top + f / 2), UP, f, 0.16, normal, trim_scale)
    if width >= 2.5:
        # A barn door: two crossed boards on it.
        for s in (-1, 1):
            a0 = surface + axis * (along - s * (width / 2 - 0.15)) + UP * (box.bottom + 0.15) + normal * 0.06
            a1 = surface + axis * (along + s * (width / 2 - 0.15)) + UP * (box.top - 0.15) + normal * 0.06
            flat_box(trim, a0, a1, (a1 - a0).cross(normal).normalized(), 0.16, 0.05, normal, trim_scale)
    elif house.mesh in style["canopies"] and abs(normal.dot(house.r)) > 0.9:
        # Gable wall: a small wooden roof over the door, on two brackets.
        z = box.top + f + 0.12
        depth, w = 0.8, width / 2 + 0.45
        tilt = 0.18
        centre = surface + axis * along + normal * (depth / 2) + UP * (z + 0.1 - tilt / 2)
        slope_dir = (normal * depth - UP * tilt).normalized()
        lid = slope_dir.cross(axis).normalized()
        if lid.z < 0:
            lid = -lid
        trim.box(centre, axis, slope_dir, lid, w, math.hypot(depth, tilt) / 2, 0.05, trim_scale)
        for s in (-1, 1):
            b0 = surface + axis * (along + s * (w - 0.15)) + UP * (z - 0.45)
            b1 = surface + axis * (along + s * (w - 0.15)) + normal * (depth - 0.12) + UP * (z - 0.02)
            flat_box(trim, b0, b1, axis, 0.08, 0.08, (b1 - b0).cross(axis).normalized(), trim_scale)

# 4. Windows: a frame and a sill, and shutters on plaster houses in the Village.
colours = list(shutters)
windows = []
for name, box in boxes_in(style["windows"]):
    house = house_of(box)
    wall = house.wall_of(box) if house is not None else None
    if wall is not None:
        normal, axis = wall[0], wall[1]
        windows.append((house, box, wall, (box.centre - house.c).dot(axis), max(box.size_x, box.size_y)))
for house, box, wall, along, width in windows:
    normal, axis, half, offset = wall
    surface = house.c + normal * offset
    f = 0.09
    for s in (-1, 1):
        x = along + s * (width / 2 + f / 2)
        flat_box(trim, surface + axis * x + UP * (box.bottom - f), surface + axis * x + UP * (box.top + f), axis, f, 0.14, normal, trim_scale)
        zline = box.top + f / 2 if s > 0 else box.bottom - f / 2
        flat_box(trim, surface + axis * (along - width / 2) + UP * zline, surface + axis * (along + width / 2) + UP * zline, UP, f, 0.14, normal, trim_scale)
    flat_box(trim, surface + axis * (along - width / 2 - 0.2) + UP * (box.bottom - f - 0.04),
             surface + axis * (along + width / 2 + 0.2) + UP * (box.bottom - f - 0.04), UP, 0.08, 0.22, normal, trim_scale)
    if house.mesh in style["shutters"] and colours:
        # Open shutters on both sides, where the wall has room: not past a corner, a door or the next window.
        shutter_w = width / 2 + 0.04
        reach = width / 2 + f + shutter_w + 0.04
        blocked = abs(along) + reach > half - 0.3 or box.top + 0.04 > house.z1 or any(
            h is house and n.dot(normal) > 0.9 and d0 - 0.1 < along + reach and along - reach < d1 + 0.1
            for h, n, d0, d1 in doors) or any(
            h is house and other is not box and w2[0].dot(normal) > 0.9 and abs(other.centre.z - box.centre.z) < 1.0
            and abs(a2 - along) < reach + wd2 / 2 + f + 0.05
            for h, other, w2, a2, wd2 in windows)
        if not blocked:
            colour = colours[houses.index(house) % len(colours)]
            for s in (-1, 1):
                x = along + s * (width / 2 + f + 0.02 + shutter_w / 2)
                flat_box(shutters[colour], surface + axis * x + UP * (box.bottom - 0.02), surface + axis * x + UP * (box.top + 0.02),
                         axis, shutter_w, 0.05, normal, trim_scale)

# 5. Chimneys: a cap on top.
capped = 0
for name, box in boxes_in(style["chimneys"]):
    house = house_of(box, margin=0.6)
    if house is None or max(box.size_x, box.size_y) > 1.5 or box.top < house.z1 + 1.0 or box.bottom > house.z0 + 0.1:
        continue
    caps.box(Vector((box.centre.x, box.centre.y, box.top + 0.08)), box.x, box.y, UP,
             box.size_x / 2 + 0.12, box.size_y / 2 + 0.12, 0.08, style["cap"][1])
    capped += 1

for builder in [trim, ridge, caps] + list(shutters.values()):
    builder.finish()
bpy.ops.wm.save_mainfile()
print(f"[houses] {len(doors)} doors framed, {capped} chimneys capped; "
      f"trim {len(bpy.data.objects['House_Trim'].data.polygons)} faces, "
      f"shutters {[len(bpy.data.objects['Shutter_' + c].data.polygons) for c in colours]}")
