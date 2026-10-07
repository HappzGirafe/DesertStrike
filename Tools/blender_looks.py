"""What a Blender material looks like, written to parts.json for the game (shared by the export scripts).

A part's look: its base texture (copied next to the exported model) or colour, metallic, smoothness, a glow
(emission colour and/or glow texture), and transparency ("OPAQUE", "MASK" = cut-out with a cutoff, or "BLEND").
Materials made by Blender's glTF importer and hand-made Principled materials both work.
"""
import os
import shutil

import bpy


def _hex(color):
    return "#{:02X}{:02X}{:02X}".format(*(round(min(max(c, 0.0), 1.0) ** (1 / 2.2) * 255) for c in color[:3]))


def _image_feeding(socket, depth=0):
    """The image texture node whose output reaches this input (through Mix/Math nodes), or None."""
    if socket is None or not socket.is_linked or depth > 6:
        return None
    node = socket.links[0].from_node
    if node.type == "TEX_IMAGE" and node.image:
        return node
    for inp in node.inputs:
        found = _image_feeding(inp, depth + 1)
        if found:
            return found
    return None


def _mix_factor(socket):
    """For an emission input fed by MIX(MULTIPLY) of a texture and a colour: that colour."""
    if socket is None or not socket.is_linked:
        return None
    node = socket.links[0].from_node
    if node.type != "MIX":
        return None
    for inp in node.inputs:
        if inp.type == "RGBA" and not inp.is_linked and inp.name in ("A", "B", "Color1", "Color2") and inp.enabled:
            value = inp.default_value
            if any(c < 0.999 for c in value[:3]):
                return value
    return None


def _cutoff(alpha_socket):
    """glTF MASK materials end in a LESS_THAN math node whose second value is the cutoff."""
    node = alpha_socket.links[0].from_node if alpha_socket.is_linked else None
    for _ in range(4):
        if node is None:
            break
        if node.type == "MATH" and node.operation == "LESS_THAN":
            return round(node.inputs[1].default_value, 3)
        node = next((i.links[0].from_node for i in node.inputs if i.is_linked), None)
    return 0.5


def copy_texture(image_node, out_dir, name=None):
    """Copies the node's image file next to the model as <name>.<ext> (default: the file's own name);
    returns that name, or "" when the file is missing."""
    source = bpy.path.abspath(image_node.image.filepath)
    if not os.path.exists(source):
        print("Missing texture (the part gets a plain colour in the game):", source)
        return ""
    name = name or os.path.splitext(os.path.basename(source))[0]
    extension = os.path.splitext(source)[1].lower() or ".png"
    shutil.copyfile(source, os.path.join(out_dir, name + extension))
    return name


def look_of(obj, out_dir, texture_name=None):
    """The look of the object's first material. Its textures are copied to out_dir, named texture_name (and
    texture_name_glow), or under their own file names when texture_name is None."""
    look = {"name": obj.name, "texture": "", "color": "", "metallic": 0.0, "smoothness": 0.2,
            "emission": "", "emissionTexture": "", "alpha": "OPAQUE", "cutoff": 0.5}
    material = obj.material_slots[0].material if obj.material_slots else None
    if not material or not material.node_tree:
        return look
    bsdf = next((n for n in material.node_tree.nodes if n.type in ("BSDF_PRINCIPLED", "BSDF_METALLIC")), None)
    if bsdf is None:
        return look

    base = bsdf.inputs.get("Base Color")
    image = _image_feeding(base)
    if image:
        look["texture"] = copy_texture(image, out_dir, texture_name)
    if base is not None and not base.is_linked:
        look["color"] = _hex(base.default_value)

    roughness = bsdf.inputs["Roughness"].default_value if "Roughness" in bsdf.inputs else 0.5
    look["smoothness"] = round(max(0.0, 1.0 - roughness) * 0.6, 2)
    if bsdf.type == "BSDF_METALLIC":
        look["metallic"] = 0.5
    elif "Metallic" in bsdf.inputs:
        look["metallic"] = round(bsdf.inputs["Metallic"].default_value, 2)

    strength = bsdf.inputs.get("Emission Strength")
    emission = bsdf.inputs.get("Emission Color")
    if emission is not None and strength is not None and strength.default_value > 0.0:
        glow = _image_feeding(emission)
        if glow:
            look["emissionTexture"] = copy_texture(glow, out_dir, texture_name + "_glow" if texture_name else None)
            factor = _mix_factor(emission)
            look["emission"] = _hex(factor) if factor is not None else "#FFFFFF"
        elif any(c > 0.001 for c in emission.default_value[:3]):
            look["emission"] = _hex([c * min(strength.default_value, 1.0) for c in emission.default_value[:3]])

    alpha = bsdf.inputs.get("Alpha")
    if alpha is not None and alpha.is_linked:
        blended = getattr(material, "surface_render_method", "") == "BLENDED" or getattr(material, "blend_method", "") == "BLEND"
        look["alpha"] = "BLEND" if blended else "MASK"
        if look["alpha"] == "MASK":
            look["cutoff"] = _cutoff(alpha)
    elif alpha is not None and alpha.default_value < 0.999:
        look["alpha"] = "BLEND"
    return look
