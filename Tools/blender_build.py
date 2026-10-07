"""Building blocks for the map scripts (Tools/remake_houses.py, Tools/industrial_structures.py): finding a mesh's
loose parts, adding boxes, cylinders and cones with tiling UVs to an object, materials and generated textures.
"""
import math
import os

import bmesh
import bpy
import numpy as np
from mathutils import Vector

UP = Vector((0, 0, 1))


def textures_dir():
    """Art/Textures, next to the .blend's Art/Blender folder."""
    return os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(bpy.data.filepath))), "Textures")


# ------------------------------------------------------------------------------------------------ reading meshes

def loose_parts(bm):
    """Groups of vertices joined by faces or sharing a position (the .glb import keeps every face's own corners)."""
    parent = list(range(len(bm.verts)))

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    bm.verts.ensure_lookup_table()
    for face in bm.faces:
        for vert in face.verts[1:]:
            a, b = find(face.verts[0].index), find(vert.index)
            if a != b:
                parent[b] = a
    first_at = {}
    for vert in bm.verts:
        key = tuple(round(c, 4) for c in vert.co)
        if key in first_at:
            a, b = find(first_at[key]), find(vert.index)
            if a != b:
                parent[b] = a
        else:
            first_at[key] = vert.index
    groups = {}
    for vert in bm.verts:
        groups.setdefault(find(vert.index), []).append(vert)
    return list(groups.values())


def corners(verts):
    """The distinct positions of a part's vertices."""
    return list({tuple(round(c, 4) for c in v.co): v.co.copy() for v in verts}.values())


# ------------------------------------------------------------------------------------------------ adding geometry

class Builder:
    """Adds closed pieces with UVs to an object's mesh; finish() writes them back."""

    def __init__(self, obj):
        self.obj = obj
        self.bm = bmesh.new()
        self.bm.from_mesh(obj.data)
        self.uv = self.bm.loops.layers.uv.verify()

    def finish(self):
        self.bm.normal_update()
        self.bm.to_mesh(self.obj.data)
        self.bm.free()
        self.obj.data.update()

    def piece(self, points, faces, uv_of=None, face_uvs=None):
        """A closed piece: faces are lists of point indices (any winding: the outside is worked out).
        UVs come from uv_of(face, position), or from face_uvs: per face, a {point index: (u, v)} map."""
        verts = [self.bm.verts.new(p) for p in points]
        index = {v: i for i, v in enumerate(verts)}
        made = []
        for f, corner_ids in enumerate(faces):
            try:
                made.append((f, self.bm.faces.new([verts[i] for i in corner_ids])))
            except ValueError:
                pass
        bmesh.ops.recalc_face_normals(self.bm, faces=[face for _, face in made])
        for f, face in made:
            face.normal_update()
            for loop in face.loops:
                if face_uvs is not None:
                    loop[self.uv].uv = face_uvs[f][index[loop.vert]]
                else:
                    loop[self.uv].uv = uv_of(face, loop.vert.co)
        return [face for _, face in made]

    def box(self, centre, x, y, z, hx, hy, hz, scale=None):
        """A box along the axes x, y, z (half sizes hx, hy, hz). UVs tile every `scale` metres, or show the whole
        texture once on each face when scale is None; upright faces keep the texture upright."""
        points = [centre + x * sx * hx + y * sy * hy + z * sz * hz for sx in (-1, 1) for sy in (-1, 1) for sz in (-1, 1)]
        faces = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]

        def uv_of(face, co):
            n = face.normal
            if abs(n.dot(UP)) > 0.7:
                t1, t2 = x, y
            else:
                t2 = UP - n * UP.dot(n)
                t2 = t2.normalized() if t2.length > 1e-6 else y
                t1 = t2.cross(n)
            if scale:
                return (co.dot(t1) / scale, co.dot(t2) / scale)
            us = [v.co.dot(t1) for v in face.verts]
            vs = [v.co.dot(t2) for v in face.verts]
            return ((co.dot(t1) - min(us)) / max(max(us) - min(us), 1e-6),
                    (co.dot(t2) - min(vs)) / max(max(vs) - min(vs), 1e-6))

        return self.piece(points, faces, uv_of)

    def block(self, centre, size, yaw=0.0, scale=None):
        """An upright box: centre of its base, size (x, y, z) before turning by yaw degrees about the vertical."""
        c, s = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
        x, y = Vector((c, s, 0)), Vector((-s, c, 0))
        return self.box(centre + UP * (size[2] / 2), x, y, UP, size[0] / 2, size[1] / 2, size[2] / 2, scale)

    def cylinder(self, base, axis, radius, length, segments=16, scale=2.0, caps=True, top_radius=None):
        """A cylinder (or a cone frustum with top_radius) from `base` along `axis`; the side's UVs wrap round it,
        every `scale` metres (or once round when scale is None); caps are flat."""
        axis = axis.normalized()
        side = (UP if abs(axis.dot(UP)) < 0.9 else Vector((1, 0, 0))).cross(axis).normalized()
        other = axis.cross(side)
        top_radius = radius if top_radius is None else top_radius
        points, faces, face_uvs = [], [], []
        for i in range(segments):
            angle = 2 * math.pi * i / segments
            d = side * math.cos(angle) + other * math.sin(angle)
            points.append(base + d * radius)
            points.append(base + axis * length + d * top_radius)
        around = 2 * math.pi * radius
        slant = math.hypot(length, radius - top_radius)
        for i in range(segments):
            j = (i + 1) % segments
            faces.append((2 * i, 2 * j, 2 * j + 1, 2 * i + 1))
            u0 = (i / segments) * (around / scale if scale else 1.0)
            u1 = ((i + 1) / segments) * (around / scale if scale else 1.0)
            v1 = slant / scale if scale else 1.0
            face_uvs.append({2 * i: (u0, 0.0), 2 * j: (u1, 0.0), 2 * j + 1: (u1, v1), 2 * i + 1: (u0, v1)})
        if caps:
            for level in (0, 1):
                ring = [2 * i + level for i in range(segments)]
                if level == 1 and top_radius < 1e-4:
                    continue
                faces.append(tuple(ring))
                r = radius if level == 0 else top_radius
                face_uvs.append({k: (0.5 + 0.5 * math.cos(2 * math.pi * n / segments) * (2 * r / scale if scale else 1.0),
                                     0.5 + 0.5 * math.sin(2 * math.pi * n / segments) * (2 * r / scale if scale else 1.0))
                                 for n, k in enumerate(ring)})
        return self.piece(points, faces, face_uvs=face_uvs)

    def cone(self, base, axis, radius, height, segments=16, scale=2.0):
        """A cone from a round base to a point."""
        axis = axis.normalized()
        side = (UP if abs(axis.dot(UP)) < 0.9 else Vector((1, 0, 0))).cross(axis).normalized()
        other = axis.cross(side)
        points = [base + (side * math.cos(2 * math.pi * i / segments) + other * math.sin(2 * math.pi * i / segments)) * radius
                  for i in range(segments)]
        points.append(base + axis * height)
        tip = segments
        slant = math.hypot(radius, height)
        faces, face_uvs = [], []
        for i in range(segments):
            j = (i + 1) % segments
            faces.append((i, j, tip))
            u0, u1 = i / segments * 2 * math.pi * radius / scale, (i + 1) / segments * 2 * math.pi * radius / scale
            face_uvs.append({i: (u0, 0.0), j: (u1, 0.0), tip: ((u0 + u1) / 2, slant / scale)})
        faces.append(tuple(range(segments)))
        face_uvs.append({i: (0.5 + 0.5 * math.cos(2 * math.pi * i / segments), 0.5 + 0.5 * math.sin(2 * math.pi * i / segments))
                         for i in range(segments)})
        return self.piece(points, faces, face_uvs=face_uvs)


def flat_box(builder, start, end, up, width, depth, outward, scale):
    """A board from `start` to `end` (its centre line on a surface), `width` across along `up`, sticking out
    `depth` along `outward`."""
    length = (end - start).length
    x = (end - start).normalized()
    centre = (start + end) / 2 + outward * (depth / 2)
    return builder.box(centre, x, up, outward, length / 2, width / 2, depth / 2, scale)


# ------------------------------------------------------------------------------------------------ materials, objects

def material(name, image_file=None, colour=None, metallic=0.0, roughness=0.85):
    """A Principled material with an image from Art/Textures, or a plain colour given as seen (sRGB)."""
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    nodes.clear()
    out = nodes.new("ShaderNodeOutputMaterial")
    bsdf = nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Metallic"].default_value = metallic
    mat.node_tree.links.new(bsdf.outputs[0], out.inputs[0])
    if image_file:
        image = bpy.data.images.load(os.path.join(textures_dir(), image_file), check_existing=True)
        image.reload()
        tex = nodes.new("ShaderNodeTexImage")
        tex.image = image
        mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    elif colour:
        bsdf.inputs["Base Color"].default_value = (*(c ** 2.2 for c in colour), 1.0)
    return mat


def new_object(name, mat):
    """An empty mesh object with one material and a UV map (an object of the same name is replaced)."""
    old = bpy.data.objects.get(name)
    if old is not None:
        bpy.data.objects.remove(old, do_unlink=True)
    mesh = bpy.data.meshes.new(name)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    obj.data.materials.append(mat)
    mesh.uv_layers.new(name="UVMap")
    return obj


def load_rgb(image_file):
    """An Art/Textures image as an (height, width, 3) array, top row first."""
    image = bpy.data.images.load(os.path.join(textures_dir(), image_file), check_existing=True)
    pixels = np.array(image.pixels[:], dtype=np.float32).reshape(image.size[1], image.size[0], 4)
    return pixels[::-1, :, :3].copy()


def save_rgb(name, rgb):
    """Writes Art/Textures/<name>.png from an (height, width, 3) array in 0..1, top row first; returns the file name."""
    path = os.path.join(textures_dir(), name + ".png")
    old = bpy.data.images.get(name)
    if old is not None:
        bpy.data.images.remove(old)
    h, w = rgb.shape[:2]
    rgba = np.concatenate([np.clip(rgb, 0.0, 1.0), np.ones((h, w, 1), dtype=np.float32)], axis=2)[::-1]
    image = bpy.data.images.new(name, w, h, alpha=True)
    image.pixels = rgba.astype(np.float32).ravel()
    image.filepath_raw = path
    image.file_format = "PNG"
    image.save()
    return name + ".png"


def tinted_texture(source_file, name, tint):
    """Writes Art/Textures/<name>.png: the source texture multiplied by a colour; returns the file name."""
    return save_rgb(name, load_rgb(source_file) * np.array(tint, dtype=np.float32))


def recoloured_texture(source_file, name, colour):
    """Writes Art/Textures/<name>.png: the source texture's light and shade in another colour (its own colour
    removed), e.g. the blue container metal in red; returns the file name."""
    rgb = load_rgb(source_file)
    light = rgb @ np.array([0.3, 0.59, 0.11], dtype=np.float32)
    light = light / max(float(light.mean()), 1e-3)
    return save_rgb(name, light[..., None] * np.array(colour, dtype=np.float32))
