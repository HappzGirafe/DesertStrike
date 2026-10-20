"""Makes an export copy of a weapon .blend whose texture pictures are not where it says (it was made on another
computer, in a folder "текстури"): each picture is looked for by name in Art/Textures (and Art/текстури), and one that
is still missing becomes a plain colour that stands in for it (chosen by the picture's name), so the model can go into
the game before its pictures arrive. The original .blend is not changed.

    blender -b -Y Art/Blender/<model>.blend --python Tools/stand_in_textures.py -- <copy.blend>
"""
import os
import sys

import bpy

out = os.path.abspath(sys.argv[sys.argv.index("--") + 1])
project = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
textures = os.path.join(project, "Art", "Textures")
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from blender_looks import relink_missing_images  # noqa: E402

print("stand-ins: found", relink_missing_images([textures, os.path.join(project, "Art", "текстури")]))

# Picture name (start) -> stand-in colour (linear RGB), or a picture of the project to use instead.
STAND_INS = {
    "хелло вын": (0.80, 0.20, 0.01),      # Halloween: pumpkin orange
    "хелло він": (0.06, 0.015, 0.10),     # Halloween 2: night purple
    "костявой": (0.72, 0.64, 0.48),       # bone
    "аля порез": (0.40, 0.01, 0.01),      # cuts: blood red
    "зелений": (0.10, 0.70, 0.04),        # green
    "m4a1 balck": (0.02, 0.02, 0.025),    # black
    "teg9": "teg92.png",                  # the TEC-9's grey
}


def stand_in(name):
    for start, value in STAND_INS.items():
        if name.lower().startswith(start):
            return value
    return (0.35, 0.35, 0.35)


replaced = set()
for material in bpy.data.materials:
    if not material.node_tree:
        continue
    nodes, links = material.node_tree.nodes, material.node_tree.links
    for node in list(nodes):
        if node.type != "TEX_IMAGE" or not node.image:
            continue
        if os.path.exists(bpy.path.abspath(node.image.filepath)):
            continue
        value = stand_in(node.image.name)
        replaced.add(f"{node.image.name.split('.')[0]} -> {value}")
        if isinstance(value, str):
            node.image = bpy.data.images.load(os.path.join(textures, value), check_existing=True)
            continue
        for link in list(node.outputs["Color"].links):
            target = link.to_socket
            links.remove(link)
            if target.name == "Base Color":
                target.default_value = (*value, 1.0)
        for bsdf in [n for n in nodes if n.type in ("BSDF_PRINCIPLED", "BSDF_METALLIC")]:
            if not bsdf.inputs["Base Color"].is_linked:
                bsdf.inputs["Base Color"].default_value = (*value, 1.0)
        nodes.remove(node)

print("stand-ins:", sorted(replaced))
bpy.ops.wm.save_as_mainfile(filepath=out, copy=True)
print("Saved", out)
