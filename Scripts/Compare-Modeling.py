"""Render a baseline and candidate for the next modeling iteration."""

import argparse
from array import array
from pathlib import Path
import runpy
import struct
import sys
import zlib

import bpy
from mathutils import Vector


DIRECTIONS = (
    ("Front", (0, -1, 0)),
    ("Left", (1, 0, 0)),
    ("Right", (-1, 0, 0)),
    ("Back", (0, 1, 0)),
)
IMAGE_TYPES = {".png", ".jpg", ".jpeg", ".webp"}


def open_model(path, kit):
    kit["require_finished"](bpy.ops.wm.open_mainfile(filepath=str(path), load_ui=False, use_scripts=False), f"Opening {path.name}")
    bounds = kit["scene_bounds"]()
    if bounds is None or bounds[1].z - bounds[0].z <= 0:
        raise ValueError(f"{path.name} needs visible geometry with a positive Z height.")
    return bounds


def find_references(directory, views):
    if directory is None:
        return {}
    directory = directory.resolve()
    if not directory.is_dir():
        raise ValueError(f"Reference folder does not exist: {directory}")
    references = {}
    for name, _ in views:
        matches = [path for path in directory.iterdir()
                   if path.is_file() and path.stem.casefold() == name.casefold() and path.suffix.lower() in IMAGE_TYPES]
        if len(matches) > 1:
            raise ValueError(f"More than one reference image matches {name}.")
        if matches:
            references[name] = matches[0]
    return references


def write_comparison(path, images):
    tile_size = 512
    gutter = 8
    width = tile_size * len(images) + gutter * (len(images) - 1)
    background = 42
    canvas = bytearray([background]) * (width * tile_size * 3)
    for column, source in enumerate(images):
        image = bpy.data.images.load(str(source), check_existing=False)
        try:
            image_width, image_height = image.size
            scale = tile_size / max(image_width, image_height)
            image_width = max(1, round(image_width * scale))
            image_height = max(1, round(image_height * scale))
            image.scale(image_width, image_height)
            pixels = array("f", [0]) * (image_width * image_height * 4)
            image.pixels.foreach_get(pixels)
            left = column * (tile_size + gutter) + (tile_size - image_width) // 2
            top = (tile_size - image_height) // 2
            for row in range(image_height):
                base = (top + row) * width * 3 + left * 3
                pixel_base = (image_height - 1 - row) * image_width * 4
                canvas[base:base + image_width * 3] = bytes(
                    min(255, max(0, round(255 * pixels[pixel + channel] * pixels[pixel + 3] + background * (1 - pixels[pixel + 3]))))
                    for pixel in range(pixel_base, pixel_base + image_width * 4, 4) for channel in range(3))
        finally:
            bpy.data.images.remove(image)

    def chunk(tag, content):
        return struct.pack(">I", len(content)) + tag + content + struct.pack(">I", zlib.crc32(tag + content))

    stride = width * 3
    rows = b"".join(b"\x00" + canvas[start:start + stride] for start in range(0, len(canvas), stride))
    header = struct.pack(">IIBBBBB", width, tile_size, 8, 2, 0, 0, 0)
    path.write_bytes(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) + chunk(b"IDAT", zlib.compress(rows)) + chunk(b"IEND", b""))


def apply_clay():
    clay = bpy.data.materials.new("Comparison clay")
    clay.diffuse_color = (0.36, 0.36, 0.36, 1)
    clay.use_nodes = True
    shader = next(node for node in clay.node_tree.nodes if node.type == "BSDF_PRINCIPLED")
    shader.inputs["Base Color"].default_value = clay.diffuse_color
    shader.inputs["Roughness"].default_value = 0.78
    for obj in bpy.context.scene.objects:
        if not hasattr(obj.data, "materials"):
            continue
        if not obj.data.is_editable:
            obj.data = obj.data.copy()
        if not obj.data.materials:
            obj.data.materials.append(clay)
        for slot in obj.material_slots:
            slot.material = clay


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--baseline", type=Path, required=True, help="Reference or previously accepted .blend file")
    parser.add_argument("--candidate", type=Path, required=True, help="New .blend file, such as an Eidolon Draft/Model.blend")
    parser.add_argument("--references", type=Path, help="Folder with front, left, right or back images")
    parser.add_argument("--output", type=Path, required=True, help="A new directory for this iteration's images")
    parser.add_argument("--views", nargs="+", choices=[name.lower() for name, _ in DIRECTIONS], help="Limit a focused round to these directions")
    parser.add_argument("--clay", action="store_true", help="Use one neutral material to compare shape and seams")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    models = [path.resolve(strict=True) for path in (args.baseline, args.candidate)]
    for model in models:
        if model.suffix.lower() != ".blend" or not model.is_file():
            raise ValueError(f"Expected a .blend file: {model}")
    views = tuple((name, axis) for name, axis in DIRECTIONS if not args.views or name.lower() in args.views)
    references = find_references(args.references, views)
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)

    kit_path = Path(__file__).resolve().parents[1] / "Eidolon.Modeling/Resources/ModelingKit.py"
    kit = runpy.run_path(str(kit_path))
    bounds = [open_model(model, kit) for model in models]
    extents = [(upper - lower) / (upper.z - lower.z) for lower, upper in bounds]
    common_extent = Vector(tuple(max(extent[axis] for extent in extents) for axis in range(3)))
    for model, role in zip(models, ("baseline", "candidate")):
        lower, upper = open_model(model, kit)
        center = (lower + upper) / 2
        half_extent = common_extent * ((upper.z - lower.z) / 2)
        directory = output / role
        directory.mkdir()
        if args.clay:
            apply_clay()
        kit["render_views"](bpy.context.scene, directory, tuple((name + ".png", axis) for name, axis in views),
                            (center - half_extent, center + half_extent), samples=192)
    for name, _ in views:
        images = [references[name]] if name in references else []
        images.extend(output / role / (name + ".png") for role in ("baseline", "candidate"))
        comparison = output / (name + ".png")
        write_comparison(comparison, images)
        print(comparison, flush=True)


if __name__ == "__main__":
    main()
