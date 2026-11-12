import ast
from contextlib import nullcontext
import itertools
import json
import math
from pathlib import Path
import struct
import sys
import tempfile
import time
import traceback

import bpy
from mathutils import Quaternion, Vector


RENDER_SIZE = 768
MAX_OPERATIONS = 1024
MAX_LITERAL_DEPTH = 8
MAX_TRIANGLE_LIMIT = 1_000_000
MAX_TRIANGLE_COUNT = 2_147_483_647
DECIMATE_ATTEMPTS = 8
RENDER_VIEWS = (
    ("3-4.png", (-1, -1, 0.65)),
    ("front.png", (0, -1, 0)),
    ("right.png", (-1, 0, 0)),
    ("left.png", (1, 0, 0)),
    ("back.png", (0, 1, 0)),
    ("top.png", (0, 0, 1)),
)
RENDERABLE_TYPES = {"MESH", "CURVE", "SURFACE", "FONT", "META", "CURVES", "POINTCLOUD", "VOLUME", "GREASEPENCIL"}
STATIC_MESH_MODIFIERS = {"BEVEL", "BOOLEAN", "DECIMATE", "MIRROR", "SHRINKWRAP", "SIMPLE_DEFORM", "SOLIDIFY", "SUBSURF"}


def require_finished(result, action):
    if "FINISHED" not in result:
        raise RuntimeError(action + " did not finish.")


def number(value):
    if type(value) not in (int, float):
        raise ValueError("Expected a number.")
    if abs(value) > 1_000_000 or not math.isfinite(value):
        raise ValueError("Number is outside the modeling range.")
    return float(value)


def positive(value):
    value = number(value)
    if value <= 0:
        raise ValueError("Expected a positive size.")
    return value


def unit(value):
    value = number(value)
    if not 0 <= value <= 1:
        raise ValueError("Expected a number from 0 to 1.")
    return value


def integer(value, minimum, maximum):
    if type(value) is not int or not minimum <= value <= maximum:
        raise ValueError(f"Expected an integer from {minimum} to {maximum}.")
    return value


def flag(value):
    if type(value) is not bool:
        raise ValueError("Expected True or False.")
    return value


def vector(values, length=3):
    if not isinstance(values, (list, tuple)) or len(values) != length:
        raise ValueError(f"Expected {length} coordinates.")
    return tuple(number(value) for value in values)


def object_name(value):
    if not isinstance(value, str) or not value.strip() or "\0" in value:
        raise ValueError("Expected a nonempty Blender name.")
    return value


def scene_object(name):
    result = bpy.context.scene.objects.get(object_name(name))
    if result is None:
        raise ValueError(f"No object named {name!r} in the active scene.")
    return result


def editable_object(name):
    result = scene_object(name)
    if result.library is not None:
        raise ValueError(f"Object {name!r} is linked and cannot be edited directly.")
    return result


def new_object_name(name):
    name = object_name(name)
    if bpy.data.objects.get(name) is not None:
        raise ValueError(f"Object {name!r} already exists.")
    return name


def set_exact_name(object_data, name):
    object_data.name = name
    if object_data.name != name:
        raise ValueError(f"Blender could not use the exact name {name!r}.")


def make_object(name, data, location):
    result = bpy.data.objects.new(name, data)
    set_exact_name(result, name)
    bpy.context.scene.collection.objects.link(result)
    result.location = location
    return result


def make_data_editable(object_data):
    if object_data.data.users > 1 or object_data.data.library is not None:
        object_data.data = object_data.data.copy()


def primitive(name, kind, location, dimensions, rotation=(0, 0, 0)):
    name = new_object_name(name)
    position = vector(location)
    size = tuple(positive(value) for value in vector(dimensions))
    angles = tuple(math.radians(value) for value in vector(rotation))
    add = {
        "cube": bpy.ops.mesh.primitive_cube_add,
        "uv_sphere": bpy.ops.mesh.primitive_uv_sphere_add,
        "ico_sphere": bpy.ops.mesh.primitive_ico_sphere_add,
        "cylinder": bpy.ops.mesh.primitive_cylinder_add,
        "cone": bpy.ops.mesh.primitive_cone_add,
        "torus": bpy.ops.mesh.primitive_torus_add,
    }.get(kind)
    if add is None:
        raise ValueError("Unsupported primitive.")
    require_finished(add(align="WORLD", location=position), "Adding a primitive")
    result = bpy.context.object
    set_exact_name(result, name)
    bpy.context.view_layer.update()
    result.dimensions = size
    result.rotation_euler = angles


def mesh(name, vertices, faces, location=(0, 0, 0)):
    name = new_object_name(name)
    position = vector(location)
    if not isinstance(vertices, (list, tuple)) or len(vertices) < 3:
        raise ValueError("A mesh needs at least three vertices.")
    if not isinstance(faces, (list, tuple)) or not faces:
        raise ValueError("A mesh needs faces.")
    coordinates = [vector(vertex) for vertex in vertices]
    polygons = []
    for face in faces:
        if not isinstance(face, (list, tuple)) or len(face) < 3:
            raise ValueError("Each mesh face needs at least three vertices.")
        indices = tuple(integer(index, 0, len(coordinates) - 1) for index in face)
        if len(set(indices)) != len(indices):
            raise ValueError("A mesh face cannot repeat a vertex.")
        polygons.append(indices)
    mesh_data = bpy.data.meshes.new(name)
    mesh_data.from_pydata(coordinates, [], polygons)
    if mesh_data.validate(verbose=False):
        raise ValueError("Blender rejected invalid mesh topology.")
    mesh_data.update()
    make_object(name, mesh_data, position)


def loft(name, rings, location=(0, 0, 0)):
    if not isinstance(rings, (list, tuple)) or len(rings) < 2:
        raise ValueError("A loft needs at least two rings.")
    if not isinstance(rings[0], (list, tuple)) or len(rings[0]) < 3:
        raise ValueError("A loft ring needs at least three points.")
    points_per_ring = len(rings[0])
    vertex_count = len(rings) * points_per_ring
    if 2 * vertex_count - 4 > MAX_TRIANGLE_COUNT:
        raise ValueError("The loft exceeds the supported triangle count.")

    vertices = []
    previous_ring = None
    for ring in rings:
        if not isinstance(ring, (list, tuple)) or len(ring) != points_per_ring:
            raise ValueError("All loft rings must have the same number of points.")
        coordinates = tuple(vector(point) for point in ring)
        if len(set(coordinates)) != points_per_ring:
            raise ValueError("A loft ring cannot repeat a point.")
        if previous_ring is not None:
            if any(previous == current for previous, current in zip(previous_ring, coordinates)):
                raise ValueError("Corresponding points on adjacent loft rings must differ.")
        vertices.extend(coordinates)
        previous_ring = coordinates

    faces = []
    for ring_index in range(len(rings) - 1):
        first = ring_index * points_per_ring
        following = first + points_per_ring
        for point_index in range(points_per_ring):
            next_point_index = (point_index + 1) % points_per_ring
            faces.append((first + point_index, first + next_point_index, following + next_point_index, following + point_index))
    faces.append(tuple(range(points_per_ring - 1, -1, -1)))
    faces.append(tuple(range(vertex_count - points_per_ring, vertex_count)))
    mesh(name, vertices, faces, location)


def curve(name, points, radius, closed=False, smooth=True):
    name = new_object_name(name)
    thickness = positive(radius)
    closed = flag(closed)
    smooth = flag(smooth)
    if not isinstance(points, (list, tuple)) or len(points) < 2:
        raise ValueError("A curve needs at least two points.")
    if closed and len(points) < 3:
        raise ValueError("A closed curve needs at least three points.")
    coordinates = [vector(point) for point in points]
    curve_data = bpy.data.curves.new(name, type="CURVE")
    curve_data.dimensions = "3D"
    curve_data.bevel_depth = thickness
    curve_data.bevel_resolution = 3
    curve_data.use_fill_caps = True
    if smooth:
        spline = curve_data.splines.new("BEZIER")
        spline.bezier_points.add(len(coordinates) - 1)
        for point, position in zip(spline.bezier_points, coordinates):
            point.co = position
            point.handle_left_type = "AUTO"
            point.handle_right_type = "AUTO"
    else:
        spline = curve_data.splines.new("POLY")
        spline.points.add(len(coordinates) - 1)
        for point, position in zip(spline.points, coordinates):
            point.co = (*position, 1)
    spline.use_cyclic_u = closed
    make_object(name, curve_data, (0, 0, 0))


def empty(name, location):
    make_object(new_object_name(name), None, vector(location))


def transform(name, location, rotation, scale):
    result = editable_object(name)
    if location is not None:
        result.location = vector(location)
    if rotation is not None:
        result.rotation_mode = "XYZ"
        result.rotation_euler = tuple(math.radians(value) for value in vector(rotation))
    if scale is not None:
        factors = vector(scale)
        if 0 in factors:
            raise ValueError("A scale component cannot be zero.")
        result.scale = factors


def duplicate(name, new_name):
    source = scene_object(name)
    new_name = new_object_name(new_name)
    result = source.copy()
    if source.data is not None:
        result.data = source.data.copy()
    set_exact_name(result, new_name)
    bpy.context.scene.collection.objects.link(result)


def parent(name, parent_name):
    result = editable_object(name)
    new_parent = None if parent_name is None else scene_object(parent_name)
    ancestor = new_parent
    while ancestor is not None:
        if ancestor == result:
            raise ValueError("Parenting would create a cycle.")
        ancestor = ancestor.parent
    bpy.context.view_layer.update()
    matrix = result.matrix_world.copy()
    result.parent = new_parent
    result.parent_type = "OBJECT"
    result.matrix_world = matrix


def delete(name):
    result = editable_object(name)
    if len(result.users_scene) > 1:
        raise ValueError("Cannot delete an object shared by multiple scenes.")
    bpy.data.objects.remove(result, do_unlink=True)


def visible(name, enabled):
    result = editable_object(name)
    hidden = not flag(enabled)
    result.hide_render = hidden
    if bpy.context.view_layer.objects.get(result.name) is not None:
        result.hide_set(hidden)


def material(name, rgba, metallic=0, roughness=0.5):
    name = object_name(name)
    if bpy.data.materials.get(name) is not None:
        raise ValueError(f"Material {name!r} already exists. Use update_material to edit an existing simple local Principled material, or use a unique name to create a separate material.")
    color = tuple(unit(value) for value in vector(rgba, 4))
    metal = unit(metallic)
    rough = unit(roughness)
    result = bpy.data.materials.new(name)
    set_exact_name(result, name)
    result.diffuse_color = color
    result.use_nodes = True
    shader = next(node for node in result.node_tree.nodes if node.type == "BSDF_PRINCIPLED")
    shader.inputs["Base Color"].default_value = color
    shader.inputs["Alpha"].default_value = color[3]
    shader.inputs["Metallic"].default_value = metal
    shader.inputs["Roughness"].default_value = rough


def update_material(name, rgba=None, metallic=None, roughness=None):
    name = object_name(name)
    result = bpy.data.materials.get(name)
    if result is None:
        raise ValueError(f"No material named {name!r}.")
    if result.library is not None or result.override_library is not None or not result.is_editable:
        raise ValueError(f"Material {name!r} is not an editable local material.")
    tree = result.node_tree
    if not result.use_nodes or tree is None:
        raise ValueError(f"Material {name!r} does not use a Principled shader.")
    if len(tree.nodes) != 2 or len(tree.links) != 1:
        raise ValueError(f"Material {name!r} must have only a Principled shader connected directly to Material Output.")
    link = tree.links[0]
    shader = link.from_node
    if (shader.type != "BSDF_PRINCIPLED" or link.from_socket.identifier != "BSDF"
            or link.to_node.type != "OUTPUT_MATERIAL" or link.to_socket.identifier != "Surface"
            or not link.is_valid or link.is_muted):
        raise ValueError(f"Material {name!r} must have an active Principled BSDF connection to Material Output Surface.")

    values = {}
    if rgba is not None:
        color = tuple(unit(value) for value in vector(rgba, 4))
        if result.is_property_readonly("diffuse_color"):
            raise ValueError(f"Material {name!r} has a read-only viewport color.")
        values["Base Color"] = color
        values["Alpha"] = color[3]
    if metallic is not None:
        values["Metallic"] = unit(metallic)
    if roughness is not None:
        values["Roughness"] = unit(roughness)

    socket_updates = []
    for socket_name, value in values.items():
        socket = shader.inputs.get(socket_name)
        if socket is None or socket.is_linked or socket.is_property_readonly("default_value"):
            raise ValueError(f"Material {name!r} has an unavailable, linked, or read-only {socket_name} input.")
        socket_updates.append((socket, value))

    for socket, value in socket_updates:
        socket.default_value = value
    if rgba is not None:
        result.diffuse_color = values["Base Color"]


def assign_material(name, material_name, faces=None):
    result = editable_object(name)
    source = bpy.data.materials.get(object_name(material_name))
    if source is None:
        raise ValueError(f"No material named {material_name!r}.")
    if result.type not in {"MESH", "CURVE", "SURFACE", "FONT"}:
        raise ValueError("This object does not support material assignment.")
    make_data_editable(result)
    materials = result.data.materials
    if faces is None:
        materials.clear()
        materials.append(source)
        if result.type == "MESH":
            for polygon in result.data.polygons:
                polygon.material_index = 0
        elif result.type in {"CURVE", "SURFACE"}:
            for spline in result.data.splines:
                spline.material_index = 0
        return
    if result.type != "MESH" or not isinstance(faces, (list, tuple)):
        raise ValueError("Only a mesh supports a list of face indices.")
    polygons = result.data.polygons
    indices = [integer(index, 0, len(polygons) - 1) for index in faces]
    slot = materials.find(source.name)
    if slot < 0:
        slot = len(materials)
        materials.append(source)
    for index in indices:
        polygons[index].material_index = slot


def smooth(name, enabled=True):
    result = editable_object(name)
    enabled = flag(enabled)
    if result.type != "MESH":
        raise ValueError("Smooth shading requires a mesh.")
    make_data_editable(result)
    for polygon in result.data.polygons:
        polygon.use_smooth = enabled


def geometry(name):
    result = editable_object(name)
    if result.type not in {"MESH", "CURVE", "SURFACE", "FONT"}:
        raise ValueError("The object cannot use modeling modifiers.")
    return result


def bevel(name, width, segments=3):
    width = positive(width)
    segments = integer(segments, 1, 8)
    modifier = geometry(name).modifiers.new("Bevel", "BEVEL")
    modifier.width = width
    modifier.segments = segments


def mirror(name, axis):
    if axis not in ("X", "Y", "Z"):
        raise ValueError("Expected mirror axis X, Y, or Z.")
    modifier = geometry(name).modifiers.new("Mirror", "MIRROR")
    modifier.use_axis = (axis == "X", axis == "Y", axis == "Z")


def subdivide(name, levels=1):
    levels = integer(levels, 1, 3)
    modifier = geometry(name).modifiers.new("Subdivision", "SUBSURF")
    modifier.levels = levels
    modifier.render_levels = levels


def fit_surface(name, target_name, offset=0):
    offset = number(offset)
    if offset < 0:
        raise ValueError("A surface offset cannot be negative.")
    source = editable_object(name)
    target = scene_object(target_name)
    if source == target:
        raise ValueError("Surface fitting needs two distinct objects.")
    if source.type != "MESH" or target.type != "MESH":
        raise ValueError("Surface fitting needs two mesh objects.")
    if source.override_library is not None or not source.is_editable:
        raise ValueError("The surface detail mesh must be editable locally.")
    if not source.data.polygons:
        raise ValueError("The surface detail mesh needs faces.")
    evaluated_target = target.evaluated_get(bpy.context.evaluated_depsgraph_get())
    if not evaluated_target.data.polygons:
        raise ValueError("The evaluated target mesh has no surface faces.")
    for modifier in source.modifiers:
        if modifier.type != "SHRINKWRAP" or modifier.target != target:
            continue
        if modifier.wrap_method != "NEAREST_SURFACEPOINT" or modifier.wrap_mode not in ("ABOVE_SURFACE", "ON_SURFACE"):
            continue
        modifier_base_name, name_separator, name_suffix = modifier.name.partition(".")
        if modifier_base_name != "Surface Fit":
            continue
        if name_separator and not (len(name_suffix) >= 3 and name_suffix.isascii() and name_suffix.isdecimal()):
            continue
        modifier.wrap_mode = "ON_SURFACE"
        modifier.offset = offset
        return
    modifier = source.modifiers.new("Surface Fit", "SHRINKWRAP")
    modifier.wrap_method = "NEAREST_SURFACEPOINT"
    modifier.wrap_mode = "ON_SURFACE"
    modifier.offset = offset
    modifier.target = target


def solidify(name, thickness):
    thickness = positive(thickness)
    modifier = geometry(name).modifiers.new("Solidify", "SOLIDIFY")
    modifier.thickness = thickness


def deform(name, method, axis, amount):
    if method not in ("BEND", "TWIST", "TAPER", "STRETCH"):
        raise ValueError("Unsupported deformation.")
    if axis not in ("X", "Y", "Z"):
        raise ValueError("Expected deformation axis X, Y, or Z.")
    amount = number(amount)
    modifier = geometry(name).modifiers.new("Deform", "SIMPLE_DEFORM")
    modifier.deform_method = method
    modifier.deform_axis = axis
    if method in ("BEND", "TWIST"):
        modifier.angle = math.radians(amount)
    else:
        modifier.factor = amount


def boolean(name, cutter_name, operation):
    if operation not in ("DIFFERENCE", "UNION", "INTERSECT"):
        raise ValueError("Unsupported Boolean operation.")
    target = editable_object(name)
    cutter = scene_object(cutter_name)
    if target.type != "MESH" or cutter.type != "MESH" or target == cutter:
        raise ValueError("A Boolean needs two distinct mesh objects.")
    modifier = target.modifiers.new("Boolean", "BOOLEAN")
    modifier.operation = operation
    modifier.solver = "EXACT"
    modifier.object = cutter


OPERATIONS = {
    "primitive": primitive, "mesh": mesh, "loft": loft, "curve": curve, "empty": empty,
    "transform": transform, "duplicate": duplicate, "parent": parent, "delete": delete, "visible": visible,
    "material": material, "update_material": update_material, "assign_material": assign_material, "smooth": smooth,
    "bevel": bevel, "mirror": mirror, "subdivide": subdivide, "fit_surface": fit_surface,
    "solidify": solidify, "deform": deform, "boolean": boolean,
}


def read_literal(node, depth=0):
    if depth > MAX_LITERAL_DEPTH:
        raise ValueError("Literal nesting is too deep.")
    if isinstance(node, ast.Constant):
        value = node.value
        if type(value) in (int, float):
            number(value)
            return value
        if value is None or type(value) in (str, bool):
            return value
    if isinstance(node, (ast.List, ast.Tuple)):
        return tuple(read_literal(element, depth + 1) for element in node.elts)
    if isinstance(node, ast.UnaryOp) and isinstance(node.op, (ast.UAdd, ast.USub)):
        if isinstance(node.operand, ast.Constant) and type(node.operand.value) in (int, float):
            value = node.operand.value
            number(value)
            return -value if isinstance(node.op, ast.USub) else value
    raise ValueError("Only literal values and literal lists or tuples are allowed.")


def read_operations(path):
    tree = ast.parse(path.read_text(encoding="utf-8"))
    if not 1 <= len(tree.body) <= MAX_OPERATIONS:
        raise ValueError("Invalid number of modeling operations.")
    operations = []
    for statement in tree.body:
        if not isinstance(statement, ast.Expr) or not isinstance(statement.value, ast.Call):
            raise ValueError("Only standalone modeling calls are allowed.")
        call = statement.value
        if not isinstance(call.func, ast.Name) or call.keywords:
            raise ValueError("Use a named modeling function with positional literals only.")
        function = OPERATIONS.get(call.func.id)
        if function is None:
            raise ValueError(f"Modeling function {call.func.id!r} is not allowed.")
        arguments = tuple(read_literal(argument) for argument in call.args)
        operations.append((function, arguments))
    return operations


def publish_json(path, value):
    temporary_path = path.with_suffix(path.suffix + ".tmp")
    with temporary_path.open("x", encoding="utf-8") as output:
        json.dump(value, output, ensure_ascii=False, allow_nan=False)
    temporary_path.replace(path)


def scene_inventory(scene):
    bpy.context.view_layer.update()
    objects = []
    for result in scene.objects:
        item = {
            "name": result.name,
            "type": result.type,
            "linked": result.library is not None,
            "visible_in_render": not result.hide_render,
            "parent": result.parent.name if result.parent else None,
            "local_location": list(result.location),
            "local_rotation_degrees": [math.degrees(angle) for angle in result.matrix_basis.to_euler("XYZ")],
            "local_scale": list(result.scale),
            "world_location": list(result.matrix_world.translation),
            "dimensions": list(result.dimensions),
            "materials": [slot.material.name if slot.material else None for slot in result.material_slots],
            "modifiers": [modifier.type for modifier in result.modifiers],
        }
        if result.type == "MESH":
            item["vertices"] = len(result.data.vertices)
            item["faces"] = len(result.data.polygons)
        objects.append(item)
    return {"scene": scene.name, "objects": objects, "materials": [item.name for item in bpy.data.materials]}


def scene_bounds():
    bpy.context.view_layer.update()
    lower = None
    upper = None
    for instance in bpy.context.evaluated_depsgraph_get().object_instances:
        result = instance.object
        if result.type not in RENDERABLE_TYPES or result.hide_render:
            continue
        for point in result.bound_box:
            position = instance.matrix_world @ Vector(point)
            if not all(math.isfinite(value) for value in position):
                raise ValueError("The scene has invalid geometry bounds.")
            if lower is None:
                lower = position.copy()
                upper = position.copy()
            else:
                for axis in range(3):
                    lower[axis] = min(lower[axis], position[axis])
                    upper[axis] = max(upper[axis], position[axis])
    if lower is None:
        return None
    return lower, upper


def render_views(scene, result_directory, views, bounds):
    if bounds is None:
        raise ValueError("The scene contains no renderable model.")
    lower, upper = bounds
    center = (lower + upper) / 2
    diameter = max((upper - lower).length, 0.001)
    corners = [Vector(point) - center for point in itertools.product(*zip(lower, upper))]
    render = scene.render
    view = scene.view_settings
    render_settings = {
        "engine": "BLENDER_EEVEE_NEXT",
        "resolution_x": RENDER_SIZE, "resolution_y": RENDER_SIZE, "resolution_percentage": 100,
        "pixel_aspect_x": 1, "pixel_aspect_y": 1,
        "use_border": False, "use_compositing": False, "use_sequencer": False, "use_multiview": False,
        "use_file_extension": True, "film_transparent": False, "filepath": render.filepath,
    }
    studio_view_settings = {
        "view_transform": "AgX", "look": "AgX - Base Contrast", "exposure": 0.0, "gamma": 1.0,
        "use_curve_mapping": False, "use_white_balance": False,
    }
    previous_settings = {name: getattr(render, name) for name in render_settings}
    previous_display_device = scene.display_settings.display_device
    previous_view_settings = {name: getattr(view, name) for name in studio_view_settings}
    previous_format = render.image_settings.file_format
    previous_color_mode = render.image_settings.color_mode
    previous_color_depth = render.image_settings.color_depth
    previous_color_management = render.image_settings.color_management
    previous_camera = scene.camera
    previous_world = scene.world
    previous_lights = [(light, light.hide_render) for light in scene.objects if light.type == "LIGHT" and light.library is None]
    camera_data = bpy.data.cameras.new("Eidolon Render Camera")
    camera = bpy.data.objects.new("Eidolon Render Camera", camera_data)
    world = bpy.data.worlds.new("Eidolon Render World")
    render_lights = []
    try:
        scene.collection.objects.link(camera)
        scene.camera = camera
        world.use_nodes = True
        background = next(node for node in world.node_tree.nodes if node.type == "BACKGROUND")
        background.inputs["Color"].default_value = (0.16, 0.16, 0.16, 1)
        background.inputs["Strength"].default_value = 0.55
        scene.world = world
        for light, unused in previous_lights:
            light.hide_render = True
        for direction, power, size in (((-3, -4, 5), 240, 0.70), ((4, -3, 2), 65, 1.0), ((1, 4, 3), 130, 0.75)):
            light_data = bpy.data.lights.new("Eidolon Render Light", "AREA")
            light = bpy.data.objects.new("Eidolon Render Light", light_data)
            render_lights.append((light, light_data))
            scene.collection.objects.link(light)
            light.location = center + Vector(direction).normalized() * diameter * 1.5
            light.rotation_euler = (center - light.location).to_track_quat("-Z", "Y").to_euler()
            light_data.energy = power * diameter ** 2
            light_data.size = size * diameter
        for name, value in render_settings.items():
            setattr(render, name, value)
        scene.display_settings.display_device = "sRGB"
        for name, value in studio_view_settings.items():
            setattr(view, name, value)
        render.image_settings.file_format = "PNG"
        render.image_settings.color_mode = "RGB"
        render.image_settings.color_depth = "8"
        render.image_settings.color_management = "FOLLOW_SCENE"
        camera_data.type = "ORTHO"
        camera_data.clip_start = max(0.0001, diameter / 1000)
        camera_data.clip_end = diameter * 5
        camera.rotation_mode = "QUATERNION"
        for filename, direction in views:
            offset = Vector(direction).normalized()
            rotation = Quaternion() if direction == (0, 0, 1) else (-offset).to_track_quat("-Z", "Y")
            camera.location = center + offset * diameter * 2
            camera.rotation_quaternion = rotation
            right = rotation @ Vector((1, 0, 0))
            up = rotation @ Vector((0, 1, 0))
            width = 2 * max(abs(point.dot(right)) for point in corners)
            height = 2 * max(abs(point.dot(up)) for point in corners)
            camera_data.ortho_scale = max(width, height, diameter * 0.05) * 1.12
            render.filepath = str(result_directory / filename)
            require_finished(bpy.ops.render.render(write_still=True, layer=bpy.context.view_layer.name), "Rendering " + filename)
    finally:
        scene.camera = previous_camera
        scene.world = previous_world
        for light, previous_hidden in previous_lights:
            light.hide_render = previous_hidden
        for name, value in previous_settings.items():
            setattr(render, name, value)
        scene.display_settings.display_device = previous_display_device
        for name, value in previous_view_settings.items():
            setattr(view, name, value)
        render.image_settings.file_format = previous_format
        render.image_settings.color_mode = previous_color_mode
        render.image_settings.color_depth = previous_color_depth
        render.image_settings.color_management = previous_color_management
        for light, light_data in render_lights:
            bpy.data.objects.remove(light, do_unlink=True)
            bpy.data.lights.remove(light_data)
        bpy.data.objects.remove(camera, do_unlink=True)
        bpy.data.cameras.remove(camera_data)
        bpy.data.worlds.remove(world)


def export_glb(path):
    require_finished(bpy.ops.export_scene.gltf(filepath=str(path), check_existing=False, export_format="GLB", use_active_scene=True, use_renderable=True, export_apply=True), "Exporting the GLB result")


def glb_triangle_counts(path):
    with path.open("rb") as stream:
        signature, version, file_length, json_length, chunk_type = struct.unpack("<4sIII4s", stream.read(20))
        if signature != b"glTF" or version != 2 or file_length != path.stat().st_size:
            raise ValueError("The Blender export is not a complete GLB 2.0 file.")
        if chunk_type != b"JSON" or json_length > file_length - 20:
            raise ValueError("The Blender export has an invalid GLB JSON chunk.")
        document = json.loads(stream.read(json_length))
    scenes = document.get("scenes", [])
    if not scenes:
        return 0, {}
    nodes = document.get("nodes", [])
    meshes = document.get("meshes", [])
    accessors = document.get("accessors", [])
    node_indices = list(scenes[document.get("scene", 0)].get("nodes", []))
    visited = set()
    triangle_count = 0
    object_counts = {}
    while node_indices:
        node_index = node_indices.pop()
        if node_index in visited:
            raise ValueError("The active GLB scene repeats a node.")
        visited.add(node_index)
        node = nodes[node_index]
        node_indices.extend(node.get("children", []))
        if "EXT_mesh_gpu_instancing" in node.get("extensions", {}):
            raise ValueError("The triangle count cannot be measured for a GPU-instanced GLB.")
        if "mesh" not in node:
            continue
        object_count = 0
        for primitive in meshes[node["mesh"]]["primitives"]:
            mode = primitive.get("mode", 4)
            if mode not in (4, 5, 6):
                continue
            accessor_index = primitive["indices"] if "indices" in primitive else primitive["attributes"]["POSITION"]
            count = accessors[accessor_index]["count"]
            if type(count) is not int or count < 0:
                raise ValueError("The GLB triangle accessor has an invalid count.")
            if mode == 4:
                if count % 3:
                    raise ValueError("The GLB export contains an incomplete triangle.")
                object_count += count // 3
            else:
                object_count += max(0, count - 2)
        triangle_count += object_count
        if triangle_count > MAX_TRIANGLE_COUNT:
            raise ValueError("The exported triangle count exceeds the supported result range.")
        name = node.get("name")
        if name is not None:
            object_counts[name] = object_count if name not in object_counts else None
    return triangle_count, object_counts


def evaluated_triangle_count(result):
    bpy.context.view_layer.update()
    evaluated = result.evaluated_get(bpy.context.evaluated_depsgraph_get()).data
    evaluated.calc_loop_triangles()
    return len(evaluated.loop_triangles)


def reducible_meshes(scene, object_counts):
    if scene.render.use_simplify:
        raise ValueError("Cannot safely reduce a scene with render simplification enabled.")
    for instance in bpy.context.evaluated_depsgraph_get().object_instances:
        if instance.is_instance and instance.object.type in RENDERABLE_TYPES and not instance.object.hide_render:
            raise ValueError("Cannot safely reduce a scene with geometry instances.")
    protected = set()
    for result in scene.objects:
        for modifier in result.modifiers:
            if not modifier.show_render and not modifier.show_viewport:
                continue
            if modifier.type not in STATIC_MESH_MODIFIERS:
                raise ValueError("Cannot safely reduce a scene with unsupported geometry modifiers.")
            if modifier.show_viewport != modifier.show_render:
                raise ValueError("Cannot safely reduce a scene with different render and viewport modifiers.")
            if modifier.type == "SUBSURF" and modifier.levels != modifier.render_levels:
                raise ValueError("Cannot safely reduce a scene with different subdivision levels.")
            if modifier.type == "SHRINKWRAP":
                if modifier.wrap_method != "NEAREST_SURFACEPOINT":
                    raise ValueError("Cannot safely reduce a scene with unsupported surface fitting.")
                protected.add(result)
                if modifier.target is not None:
                    protected.add(modifier.target)
            if modifier.type == "BOOLEAN":
                if modifier.object is not None:
                    protected.add(modifier.object)
                if modifier.collection is not None:
                    protected.update(modifier.collection.all_objects)
        for constraint in result.constraints:
            if constraint.type == "SHRINKWRAP" and constraint.target is not None:
                protected.add(constraint.target)
    candidates = []
    for name, count in object_counts.items():
        if count is None or count < 4:
            continue
        result = scene.objects.get(name)
        if result is None or result.type != "MESH" or result in protected:
            continue
        if result.library is not None or result.override_library is not None or len(result.users_scene) != 1:
            continue
        if result.data.shape_keys is not None or result.find_armature() is not None or result.animation_data is not None:
            continue
        if not result.visible_get() or result.hide_render:
            continue
        if evaluated_triangle_count(result) == count:
            candidates.append((count, name, result))
    return sorted(candidates, key=lambda candidate: (-candidate[0], candidate[1]))


def reduce_mesh(result, current_count, target_count):
    modifier = result.modifiers.new("Eidolon Triangle Limit", "DECIMATE")
    keep_modifier = False
    try:
        modifier.decimate_type = "COLLAPSE"
        modifier.use_collapse_triangulate = True
        lower_ratio = 0
        upper_ratio = 1
        ratio = target_count / current_count
        best_count = current_count
        best_ratio = 1
        fitting_count = 0
        fitting_ratio = 0
        for attempt in range(DECIMATE_ATTEMPTS):
            modifier.ratio = ratio
            measured_count = evaluated_triangle_count(result)
            if 0 < measured_count <= target_count:
                if attempt == 0:
                    keep_modifier = True
                    return measured_count
                if measured_count > fitting_count:
                    fitting_count = measured_count
                    fitting_ratio = ratio
                lower_ratio = ratio
            elif measured_count == 0:
                lower_ratio = ratio
            else:
                upper_ratio = ratio
                if measured_count < best_count:
                    best_count = measured_count
                    best_ratio = ratio
            ratio = (lower_ratio + upper_ratio) / 2
        if fitting_count > 0:
            modifier.ratio = fitting_ratio
            keep_modifier = True
            return fitting_count
        if best_count < current_count:
            modifier.ratio = best_ratio
            keep_modifier = True
        return best_count
    finally:
        if not keep_modifier:
            result.modifiers.remove(modifier)


def enforce_triangle_limit(scene, result_directory, maximum_triangle_count):
    with tempfile.TemporaryDirectory(dir=result_directory) as temporary_directory:
        preview_path = Path(temporary_directory) / "Budget.glb"
        export_glb(preview_path)
        triangle_count, object_counts = glb_triangle_counts(preview_path)
        if triangle_count <= maximum_triangle_count:
            return triangle_count
        reduced_objects = {}
        for count, name, result in reducible_meshes(scene, object_counts):
            target_count = max(1, count - (triangle_count - maximum_triangle_count))
            reduced_count = reduce_mesh(result, count, target_count)
            if reduced_count < count:
                reduced_objects[name] = reduced_count
                triangle_count -= count - reduced_count
            if triangle_count <= maximum_triangle_count:
                break
        if reduced_objects:
            export_glb(preview_path)
            triangle_count, object_counts = glb_triangle_counts(preview_path)
            for name, count in reduced_objects.items():
                if object_counts.get(name) != count:
                    raise ValueError("The reduced mesh does not match its GLB export.")
        if triangle_count > maximum_triangle_count:
            raise ValueError(f"Cannot meet the limit of {maximum_triangle_count} triangles with the supported mesh reductions.")
        return triangle_count


def apply_and_export(scene, directory, control_directory, stage, maximum_triangle_count):
    if bpy.context.scene != scene:
        raise RuntimeError("The request scene is no longer active.")
    operations = read_operations(control_directory / (stage + ".ops"))
    if bpy.context.mode != "OBJECT":
        require_finished(bpy.ops.object.mode_set(mode="OBJECT"), "Entering Object Mode")
    for function, arguments in operations:
        function(*arguments)
    result_directory = directory / stage
    result_directory.mkdir()
    expected_triangle_count = None
    if maximum_triangle_count > 0:
        expected_triangle_count = enforce_triangle_limit(scene, result_directory, maximum_triangle_count)
    render_views(scene, result_directory, RENDER_VIEWS, scene_bounds())
    require_finished(bpy.ops.wm.save_as_mainfile(filepath=str(result_directory / "Model.blend"), check_existing=False, relative_remap=True, copy=True), "Saving the Blender result")
    glb_path = result_directory / "Model.glb"
    export_glb(glb_path)
    triangle_count, unused = glb_triangle_counts(glb_path)
    if maximum_triangle_count > 0 and triangle_count > maximum_triangle_count:
        raise ValueError(f"The final GLB has {triangle_count} triangles, exceeding the limit of {maximum_triangle_count}.")
    if expected_triangle_count is not None and triangle_count != expected_triangle_count:
        raise ValueError("The final GLB triangle count differs from the scene checked before rendering.")
    publish_json(result_directory / "Scene.json", scene_inventory(scene))
    return triangle_count


def open_request(directory, control_directory, source_path):
    if source_path:
        require_finished(bpy.ops.wm.open_mainfile(filepath=source_path, load_ui=False, use_scripts=False), "Opening the source scene")
    else:
        for result in list(bpy.context.scene.objects):
            bpy.data.objects.remove(result, do_unlink=True)
    working_file = directory / "Working.blend"
    require_finished(bpy.ops.wm.save_as_mainfile(filepath=str(working_file), check_existing=False, relative_remap=True), "Saving the working scene")
    scene = bpy.context.scene
    if source_path:
        bounds = scene_bounds()
        if bounds is not None:
            render_views(scene, control_directory, (("Source.png", RENDER_VIEWS[0][1]),), bounds)
    publish_json(control_directory / "Scene.json", scene_inventory(scene))
    return scene


def error_message(error):
    traceback.print_exc()
    return f"{type(error).__name__}: {error}"


def main():
    directory_argument, source_path, triangle_limit_argument = sys.argv[sys.argv.index("--") + 1:]
    directory = Path(directory_argument)
    control_directory = directory / "Control"
    try:
        maximum_triangle_count = integer(int(triangle_limit_argument), 0, MAX_TRIANGLE_LIMIT)
        scene = open_request(directory, control_directory, source_path)
        publish_json(control_directory / "Opened.json", {"error": ""})
    except Exception as error:
        publish_json(control_directory / "Opened.json", {"error": error_message(error)})
        raise

    is_background = bpy.app.background
    poll_interval_seconds = 0.15

    def poll():
        if (control_directory / "Stop").is_file():
            if not is_background:
                bpy.ops.wm.quit_blender()
            return None
        for stage in ("Draft", "Refined"):
            status = control_directory / (stage + ".json")
            if status.exists():
                continue
            if (control_directory / (stage + ".ops")).is_file():
                try:
                    context = nullcontext()
                    if not is_background:
                        windows = bpy.context.window_manager.windows
                        if not windows:
                            raise RuntimeError("The Blender request window has closed.")
                        context = bpy.context.temp_override(window=windows[0])
                    with context:
                        triangle_count = apply_and_export(scene, directory, control_directory, stage, maximum_triangle_count)
                except Exception as error:
                    publish_json(status, {"error": error_message(error)})
                else:
                    publish_json(status, {"error": "", "triangleCount": triangle_count})
            break
        return poll_interval_seconds

    if is_background:
        while poll() is not None:
            time.sleep(poll_interval_seconds)
    else:
        bpy.app.timers.register(poll, first_interval=poll_interval_seconds)


if __name__ == "__main__":
    main()
