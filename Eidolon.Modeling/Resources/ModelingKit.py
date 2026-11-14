import ast
from array import array
from bisect import bisect_right
from collections import Counter
from contextlib import nullcontext
from functools import partial
import itertools
import json
import math
from pathlib import Path
import struct
import sys
import tempfile
import time
import traceback
import zlib

import bpy
from mathutils import Quaternion, Vector
from mathutils.bvhtree import BVHTree


RENDER_SIZE = 768
MAX_OPERATIONS = 1024
MAX_LITERAL_DEPTH = 8
SURFACE_UV_LAYER = "Eidolon Surface UV"
SURFACE_TEXTURE_SIZE = 256
MAX_SURFACE_TEXTURES = 16
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


def outline_plate(name, origin, across, up, outline, thickness, edge_radius=0, z_span=None, exposure=None):
    origin = vector(origin)
    across = Vector(vector(across))
    up = Vector(vector(up))
    if across.length <= 1e-6 or up.length <= 1e-6 or across.cross(up).length <= 1e-6 * across.length * up.length:
        raise ValueError("Plate axes must span a plane.")
    if not isinstance(outline, (list, tuple)) or not 3 <= len(outline) <= 128:
        raise ValueError("A plate needs 3 to 128 outline points.")
    thickness = positive(thickness)
    edge_radius = number(edge_radius)
    if not 0 <= edge_radius <= thickness / 2:
        raise ValueError("Plate edge radius must be between zero and half its thickness.")
    coordinates = [vector(point, 2) for point in outline]
    if len(set(coordinates)) != len(coordinates):
        raise ValueError("Plate outline points must be distinct.")
    vertices = [tuple(across * horizontal + up * vertical) for horizontal, vertical in coordinates]
    if z_span is not None:
        bottom, top = vector(z_span, 2)
        lowest = min(point[2] for point in vertices)
        highest = max(point[2] for point in vertices)
        if top <= bottom or highest - lowest <= 1e-6:
            raise ValueError("Fitting a plate to a Z span requires an ascending span and a non-horizontal contour.")
        scale = (top - bottom) / (highest - lowest)
        vertices = [(x, y, bottom - origin[2] + (z - lowest) * scale) for x, y, z in vertices]
    if exposure is not None:
        if not isinstance(exposure, (list, tuple)) or len(exposure) not in (4, 5):
            raise ValueError("Plate exposure needs a target, a horizontal outward direction, contour indices, a gap and optionally a maximum shift.")
        target_name, outward, exposed_indices, gap = exposure[:4]
        maximum_shift = positive(exposure[4]) if len(exposure) == 5 else math.inf
        direction = surface_ray_direction(outward)
        if abs(direction.z) > 1e-6:
            raise ValueError("Plate exposure needs a horizontal outward direction to preserve its height.")
        gap = positive(gap)
        if not isinstance(exposed_indices, (list, tuple)) or not exposed_indices:
            raise ValueError("Plate exposure needs at least one exposed contour point.")
        exposed_indices = {integer(index, 0, len(vertices) - 1) for index in exposed_indices}
        if len(exposed_indices) == len(vertices):
            raise ValueError("Plate exposure needs at least one unmarked attachment point.")
        target = scene_object(target_name)
        if target.type != "MESH" or target.name not in bpy.context.view_layer.objects:
            raise ValueError("The plate exposure target must be a mesh in the active view layer.")
        bpy.context.view_layer.update()
        dependencies = bpy.context.evaluated_depsgraph_get()
        sample_gap = surface_gap_sampler(target, dependencies, surface_ray_caster(target, dependencies), -direction)
        points = [Vector(origin) + Vector(vertex) for vertex in vertices]
        samples = [points[index] for index in exposed_indices]
        for index in exposed_indices:
            following = (index + 1) % len(points)
            if following in exposed_indices:
                samples.extend(points[index].lerp(points[following], step / 4) for step in range(1, 4))
        shift = 0
        margin = gap + thickness / 2
        for point in samples:
            signed_gap, tolerance = sample_gap(point, margin)
            if signed_gap is not None:
                shift = max(shift, margin + tolerance - signed_gap)
        root_depth = 0
        for index, point in enumerate(points):
            if index not in exposed_indices:
                signed_gap, tolerance = sample_gap(point, margin)
                if signed_gap is not None:
                    root_depth = max(root_depth, -signed_gap - tolerance)
        origin = tuple(Vector(origin) + direction * min(shift, root_depth, maximum_shift))
    mesh(name, vertices, [tuple(range(len(vertices)))], origin)
    solidify(name, thickness)
    geometry(name).modifiers[-1].offset = 0
    if edge_radius:
        bevel(name, edge_radius, 3)


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


def radial_loft(name, rings, rim=None, segments=48, closed=True, profile_steps=3, cover_target=None, clearance=0, section_power=2, rim_roll=0):
    if not isinstance(rings, (list, tuple)) or not 2 <= len(rings) <= 32:
        raise ValueError("A radial loft needs 2 to 32 rings.")
    segments = integer(segments, 12, 96)
    profile_steps = integer(profile_steps, 1, 4)
    closed = flag(closed)
    section_power = number(section_power)
    if not 1.5 <= section_power <= 4:
        raise ValueError("Radial loft section_power must be between 1.5 and 4.")
    rim_roll = unit(rim_roll)
    if rim_roll and rim is None:
        raise ValueError("Radial loft rim_roll requires a shaped rim.")
    clearance = number(clearance)
    if clearance < 0 or (cover_target is None and clearance > 0):
        raise ValueError("Radial loft clearance requires a cover target and cannot be negative.")
    sections = []
    for ring in rings:
        center_x, center_y, height, radius_x, radius_y = vector(ring, 5)
        sections.append((center_x, center_y, height, positive(radius_x), positive(radius_y)))
    if any(first[2] >= second[2] for first, second in zip(sections, sections[1:])):
        raise ValueError("Radial loft rings must ascend in Z.")
    controls = []
    if rim is not None:
        if not isinstance(rim, (list, tuple)) or not 3 <= len(rim) <= 48:
            raise ValueError("A shaped rim needs 3 to 48 angle and height pairs.")
        for control in rim:
            if not isinstance(control, (list, tuple)) or len(control) not in (2, 3):
                raise ValueError("Each rim control needs an angle, a height and optionally a radius scale.")
            angle, height = vector(control[:2], 2)
            radius_scale = number(control[2]) if len(control) == 3 else 1
            if not 0 <= angle < 360 or height >= sections[-1][2]:
                raise ValueError("Rim angles must be in [0, 360) and rim heights must be below the top ring.")
            if not 0.5 <= radius_scale <= 1.75:
                raise ValueError("Rim radius scales must be between 0.5 and 1.75.")
            if controls and angle <= controls[-1][0]:
                raise ValueError("Rim angles must be specified in ascending order.")
            controls.append((angle, height, radius_scale))
        controls.insert(0, (controls[-1][0] - 360, *controls[-1][1:]))
        controls.append((controls[1][0] + 360, *controls[1][1:]))

    heights = [section[2] for section in sections]
    bottom, top_height = heights[0], heights[-1]
    angle_step = 360 / segments
    angles = [index * angle_step for index in range(segments)]
    if controls:
        explicit_angles = [control[0] for control in controls]
        angles = sorted([angle for angle in angles
                         if all(abs(angle - explicit) >= angle_step / 3 for explicit in explicit_angles)]
                        + explicit_angles[1:-1])
    segments = len(angles)
    rim_profiles = []
    for degrees in angles:
        height = bottom
        radius_scale = 1
        for control_index, ((first_angle, first_height, first_radius_scale), (second_angle, second_height, second_radius_scale)) in enumerate(zip(controls, controls[1:])):
            if first_angle <= degrees <= second_angle:
                factor = (degrees - first_angle) / (second_angle - first_angle)
                weight = factor
                if profile_steps > 1:
                    previous_control = controls[control_index - 1] if control_index > 0 else controls[-3]
                    following_control = controls[control_index + 2] if control_index + 2 < len(controls) else controls[2]
                    if (first_height - previous_control[1]) * (second_height - first_height) <= 0:
                        weight = factor * factor * (2 - factor)
                    if (following_control[1] - second_height) * (second_height - first_height) <= 0:
                        weight = factor + factor * factor * (1 - factor) if weight == factor else factor * factor * (3 - 2 * factor)
                height = first_height + (second_height - first_height) * weight
                radius_scale = first_radius_scale + (second_radius_scale - first_radius_scale) * weight
                break
        rim_profiles.append((math.radians(degrees), height, radius_scale))

    def profile_value(section_index, channel, fraction):
        start = sections[section_index][channel]
        end = sections[section_index + 1][channel]
        if profile_steps == 1:
            return start + (end - start) * fraction
        delta = end - start
        if delta == 0:
            return start
        previous = sections[max(0, section_index - 1)][channel]
        following = sections[min(len(sections) - 1, section_index + 2)][channel]
        width = heights[section_index + 1] - heights[section_index]
        start_span = heights[section_index + 1] - heights[max(0, section_index - 1)]
        end_span = heights[min(len(sections) - 1, section_index + 2)] - heights[section_index]
        start_tangent = min(2, max(0, (end - previous) * width / (start_span * delta))) * delta
        end_tangent = min(2, max(0, (following - start) * width / (end_span * delta))) * delta
        squared = fraction * fraction
        cubed = squared * fraction
        return start + delta * (3 * squared - 2 * cubed) + start_tangent * (cubed - 2 * squared + fraction) + end_tangent * (cubed - squared)

    profile_heights = [start + (end - start) * step / profile_steps
                       for start, end in zip(heights, heights[1:]) for step in range(profile_steps)] + [top_height]
    if rim_roll:
        profile_heights = sorted(set(profile_heights + [bottom + (top_height - bottom) * fraction for fraction in (0.035, 0.075, 0.12, 0.18, 0.24)]))
    cast = None
    if cover_target is not None:
        target = scene_object(cover_target)
        if target.type != "MESH":
            raise ValueError("The radial loft cover target must be a mesh.")
        bpy.context.view_layer.update()
        dependencies = bpy.context.evaluated_depsgraph_get()
        target_evaluated = target.evaluated_get(dependencies)
        target_bounds = [target_evaluated.matrix_world @ Vector(corner) for corner in target_evaluated.bound_box]
        cast = surface_ray_caster(target, dependencies)
    vertices = []
    for height in profile_heights:
        fraction = (height - bottom) / (top_height - bottom)
        for radians, rim_height, rim_radius_scale in rim_profiles:
            vertex_height = rim_height + (top_height - rim_height) * fraction
            index = min(len(sections) - 2, max(0, bisect_right(heights, vertex_height) - 1))
            start, end = sections[index], sections[index + 1]
            amount = min(1, max(0, (vertex_height - start[2]) / (end[2] - start[2])))
            center_x, center_y, radius_x, radius_y = (profile_value(index, channel, amount) for channel in (0, 1, 3, 4))
            radius_scale = 1 + (rim_radius_scale - 1) * (1 - fraction) ** 2
            if rim_roll and fraction < 0.25:
                wave = math.sin(math.pi * fraction / 0.25) ** 2
                radius_scale += rim_roll * max(0, rim_radius_scale - 1) * wave
            radius_x *= radius_scale
            radius_y *= radius_scale
            center = Vector((center_x, center_y, vertex_height))
            sine = math.sin(radians)
            cosine = math.cos(radians)
            exponent = 2 / section_power
            radial = Vector((radius_x * math.copysign(abs(sine) ** exponent, sine),
                             -radius_y * math.copysign(abs(cosine) ** exponent, cosine), 0))
            if cast is not None:
                direction = radial.normalized()
                reach = max(0, max(direction.dot(corner - center) for corner in target_bounds)) + radial.length + clearance
                hit = cast(center + direction * reach, -direction, reach)
                if hit is not None:
                    required = direction.dot(hit - center) + clearance
                    difference = abs(required - radial.length)
                    rounded_join = max(0, clearance - difference)
                    radius = max(required, radial.length)
                    if rounded_join:
                        radius += rounded_join ** 2 / (4 * clearance)
                    radial = direction * radius
            vertices.append(tuple(center + radial))
    faces = []
    for ring_index in range(len(profile_heights) - 1):
        first = ring_index * segments
        following = first + segments
        for index in range(segments):
            next_index = (index + 1) % segments
            faces.append((first + index, first + next_index, following + next_index, following + index))
    if closed:
        faces.append(tuple(range(segments - 1, -1, -1)))
    top = len(vertices) - segments
    faces.append(tuple(range(top, len(vertices))))
    mesh(name, vertices, faces)
    smooth(name)


def loft_path(name, sections, across=(1, 0, 0), segments=24, beyond=None):
    if not isinstance(sections, (list, tuple)) or not 2 <= len(sections) <= 256:
        raise ValueError("A path loft needs 2 to 256 sections.")
    segments = integer(segments, 3, 64)
    across_direction = Vector(vector(across))
    if across_direction.length <= 1e-6:
        raise ValueError("The path loft width direction cannot be zero.")
    across_direction.normalize()
    centers = []
    radii = []
    for section in sections:
        if not isinstance(section, (list, tuple)) or len(section) != 3:
            raise ValueError("Each path loft section needs a center and two radii.")
        center, width, height = section
        centers.append(Vector(vector(center)))
        radii.append((positive(width), positive(height)))
    if beyond is not None:
        if not isinstance(beyond, (list, tuple)) or len(beyond) != 3:
            raise ValueError("A loft tip constraint needs a target, a horizontal axis and an exposed length.")
        target_name, axis_name, exposed_length = beyond
        if axis_name not in ("X", "Y"):
            raise ValueError("A loft tip constraint uses X or Y.")
        axis = "XY".index(axis_name)
        exposed_length = positive(exposed_length)
        if abs(centers[-1][axis] - centers[0][axis]) <= 1e-6:
            raise ValueError("The loft path must extend along the constrained axis.")
        side = math.copysign(1, centers[-1][axis] - centers[0][axis])
        target = scene_object(target_name)
        if target.type != "MESH":
            raise ValueError("The loft tip constraint needs a mesh target.")
        bpy.context.view_layer.update()
        evaluated = target.evaluated_get(bpy.context.evaluated_depsgraph_get())
        vertices = [evaluated.matrix_world @ vertex.co for vertex in evaluated.data.vertices]
        tip_height = centers[-1].z
        boundary = None
        for edge in evaluated.data.edges:
            start, end = (vertices[index] for index in edge.vertices)
            if not min(start.z, end.z) <= tip_height <= max(start.z, end.z):
                continue
            if start.z == end.z:
                distance = max(side * start[axis], side * end[axis])
            else:
                fraction = (tip_height - start.z) / (end.z - start.z)
                distance = side * (start[axis] + fraction * (end[axis] - start[axis]))
            boundary = max(boundary, distance) if boundary is not None else distance
        if boundary is not None:
            additional = max(0, boundary + exposed_length - side * centers[-1][axis])
            for index, center in enumerate(centers[1:], 1):
                center[axis] += side * additional * (index / (len(centers) - 1)) ** 2
    directions = []
    for previous, following in zip(centers, centers[1:]):
        direction = following - previous
        if direction.length <= 1e-6:
            raise ValueError("Adjacent path loft centers must differ.")
        directions.append(direction.normalized())

    rings = []
    previous_tangent = None
    for index, (center, (width, height)) in enumerate(zip(centers, radii)):
        if index == 0:
            tangent = directions[0]
            right = across_direction - tangent * across_direction.dot(tangent)
        else:
            if index == len(centers) - 1:
                tangent = directions[-1]
            else:
                tangent = directions[index - 1] + directions[index]
                if tangent.length <= 1e-6:
                    raise ValueError("A path loft cannot reverse direction at one section.")
                tangent.normalize()
            right = previous_tangent.rotation_difference(tangent) @ right
            right = right - tangent * right.dot(tangent)
        if right.length <= 1e-6:
            raise ValueError("The path loft width direction is parallel to the path start.")
        right.normalize()
        up = tangent.cross(right)
        rings.append([tuple(center + right * (width * math.cos(angle)) + up * (height * math.sin(angle))) for angle in (math.tau * segment / segments for segment in range(segments))])
        previous_tangent = tangent
    loft(name, rings)


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


def append_part(parts_path, name, kind, new_name):
    name = object_name(name)
    new_name = new_object_name(new_name)
    if kind not in ("object", "collection"):
        raise ValueError("Append a part as an object or collection.")
    libraries_before = set(bpy.data.libraries)
    images_before = set(bpy.data.images)
    group = make_object(new_name, None, (0, 0, 0)) if kind == "collection" else None
    with bpy.data.libraries.load(parts_path, link=False) as (available, appended):
        if kind == "object":
            if name not in available.objects:
                raise ValueError(f"No object {name!r} in the selected parts source.")
            appended.objects = [name]
        else:
            if name not in available.collections:
                raise ValueError(f"No collection {name!r} in the selected parts source.")
            appended.collections = [name]
    part = appended.objects[0] if kind == "object" else appended.collections[0]
    if part is None or part.library is not None:
        raise ValueError(f"Could not append {name!r} as a local part.")
    if set(bpy.data.libraries) - libraries_before:
        raise ValueError("A part cannot depend on an external Blender library.")
    for image in set(bpy.data.images) - images_before:
        if image.source not in ("GENERATED", "VIEWER") and not image.packed_files:
            raise ValueError(f"Pack the part's image {image.name!r} in the source .blend first.")
    objects = set(part.all_objects) if kind == "collection" else {part}
    if not any(obj.type in ("MESH", "CURVE") for obj in objects):
        raise ValueError("The selected part needs a mesh or curve.")
    for obj in objects:
        if obj.type not in ("MESH", "CURVE", "EMPTY") or obj.library is not None or obj.instance_collection is not None:
            raise ValueError("Use local meshes, curves and ordinary groups for appended parts.")
        if obj.parent is not None and obj.parent not in objects:
            raise ValueError(f"Include the parent of {obj.name!r} in the selected collection.")
    scene_collection = bpy.context.scene.collection
    if group is None:
        set_exact_name(part, new_name)
        scene_collection.objects.link(part)
        return
    scene_collection.children.link(part)
    for obj in objects:
        if obj.parent is None:
            obj.parent = group


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


def material(name, rgba, metallic=0, roughness=0.5, color_space="linear"):
    name = object_name(name)
    if bpy.data.materials.get(name) is not None:
        raise ValueError(f"Material {name!r} already exists. Use update_material to edit an existing simple local Principled material, or use a unique name to create a separate material.")
    color = tuple(unit(value) for value in vector(rgba, 4))
    if color_space == "srgb":
        color = tuple(channel / 12.92 if channel <= 0.04045 else ((channel + 0.055) / 1.055) ** 2.4 for channel in color[:3]) + (color[3],)
    elif color_space != "linear":
        raise ValueError("Material colors must use linear or srgb.")
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


def write_point_colors(name, layer_name, color_for_vertex):
    result = editable_object(name)
    layer_name = object_name(layer_name)
    if result.type != "MESH":
        raise ValueError("Point colors require a mesh.")
    existing = result.data.color_attributes.get(layer_name)
    if existing is not None and (existing.domain != "POINT" or existing.data_type != "FLOAT_COLOR"):
        raise ValueError("An existing point color layer must use POINT and FLOAT_COLOR.")
    world = result.matrix_world.copy()
    colors = [
        vector(color_for_vertex(world @ vertex.co, vertex, tuple(existing.data[vertex.index].color) if existing is not None else None), 4)
        for vertex in result.data.vertices
    ]
    make_data_editable(result)
    attributes = result.data.color_attributes
    layer = attributes.get(layer_name)
    if layer is None:
        layer = attributes.new(name=layer_name, type="FLOAT_COLOR", domain="POINT")
    for item, color in zip(layer.data, colors):
        item.color = color
    attributes.active_color = layer
    attributes.render_color_index = list(attributes).index(layer)
    result.data.update()


def write_mesh_uvs(name, layer_name, coordinates_for_face, *, activate=True):
    result = editable_object(name)
    layer_name = object_name(layer_name)
    if result.type != "MESH" or result.mode != "OBJECT":
        raise ValueError("UV coordinates require a mesh in Object Mode.")
    world = result.matrix_world.copy()
    positions = [world @ vertex.co for vertex in result.data.vertices]
    face_coordinates = []
    for polygon in result.data.polygons:
        coordinates = tuple(coordinates_for_face(tuple(positions[index].copy() for index in polygon.vertices)))
        if len(coordinates) != polygon.loop_total:
            raise ValueError("Expected one UV coordinate for each face corner.")
        face_coordinates.extend(zip(polygon.loop_indices, (vector(uv, 2) for uv in coordinates)))
    make_data_editable(result)
    layers = result.data.uv_layers
    layer = layers.get(layer_name)
    if layer is None:
        layer = layers.new(name=layer_name)
    for loop_index, coordinates in face_coordinates:
        layer.data[loop_index].uv = coordinates
    if activate:
        layers.active = layer
        layer.active_render = True
    result.data.update()


def project_uv(name, projection, axes):
    result = editable_object(name)
    if result.type != "MESH" or result.mode != "OBJECT" or not result.data.polygons:
        raise ValueError("UV projection requires a mesh with faces in Object Mode.")
    if projection == "PLANAR" and axes in ("XY", "XZ", "YX", "YZ", "ZX", "ZY"):
        u_axis, v_axis = ("XYZ".index(axis) for axis in axes)
    elif projection == "CYLINDRICAL" and axes in ("X", "Y", "Z"):
        v_axis = "XYZ".index(axes)
        radial_first = (v_axis + 1) % 3
        radial_second = (v_axis + 2) % 3
    else:
        raise ValueError("Use PLANAR with two distinct ordered axes or CYLINDRICAL with one axis.")

    bpy.context.view_layer.update()
    world = result.matrix_world.copy()
    positions = [world @ vertex.co for vertex in result.data.vertices]
    lower = tuple(min(point[axis] for point in positions) for axis in range(3))
    upper = tuple(max(point[axis] for point in positions) for axis in range(3))
    if upper[v_axis] <= lower[v_axis]:
        raise ValueError("UV projection has no extent along V.")
    if projection == "PLANAR" and upper[u_axis] <= lower[u_axis]:
        raise ValueError("Planar UV projection has no extent along U.")
    if projection == "CYLINDRICAL":
        if upper[radial_first] <= lower[radial_first] or upper[radial_second] <= lower[radial_second]:
            raise ValueError("Cylindrical UV projection needs extent on both radial axes.")
        center_first = (lower[radial_first] + upper[radial_first]) / 2
        center_second = (lower[radial_second] + upper[radial_second]) / 2

    def normalized(point, axis):
        return 0.01 + 0.98 * (point[axis] - lower[axis]) / (upper[axis] - lower[axis])

    def coordinates_for_face(corners):
        if projection == "PLANAR":
            return [(normalized(point, u_axis), normalized(point, v_axis)) for point in corners]
        coordinates = [
            (math.atan2(point[radial_second] - center_second, point[radial_first] - center_first) / math.tau + 0.5,
             normalized(point, v_axis))
            for point in corners
        ]
        if max(uv[0] for uv in coordinates) - min(uv[0] for uv in coordinates) > 0.5:
            return [(u + 1 if u < 0.5 else u, v) for u, v in coordinates]
        return coordinates

    write_mesh_uvs(name, SURFACE_UV_LAYER, coordinates_for_face, activate=False)


def create_packed_color_texture(path, size, color_at_uv):
    path = Path(path)
    if path.suffix.lower() != ".png":
        raise ValueError("A color texture needs a PNG path.")
    width, height = (integer(dimension, 1, 4096) for dimension in size)
    rows = bytearray()
    for row in range(height):
        rows.append(0)
        for column in range(width):
            color = tuple(color_at_uv((column + 0.5) / width, 1 - (row + 0.5) / height))
            if len(color) != 3:
                raise ValueError("A color texture needs three sRGB channels in the 0–255 range.")
            rows.extend(min(255, max(0, round(channel))) for channel in color)

    def png_chunk(chunk_type, content):
        return struct.pack(">I", len(content)) + chunk_type + content + struct.pack(">I", zlib.crc32(chunk_type + content))

    header = struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)
    path.write_bytes(b"\x89PNG\r\n\x1a\n" + png_chunk(b"IHDR", header) + png_chunk(b"IDAT", zlib.compress(rows, 8)) + png_chunk(b"IEND", b""))
    image = bpy.data.images.load(str(path.resolve()), check_existing=False)
    image.colorspace_settings.name = "sRGB"
    image.pack()
    return image


def unlinked_base_color(material_name):
    source = bpy.data.materials.get(object_name(material_name))
    if source is None or not source.is_editable or source.override_library is not None or not source.use_nodes:
        raise ValueError("A color texture requires an editable node material.")
    nodes = source.node_tree.nodes
    output = next((node for node in nodes if node.type == "OUTPUT_MATERIAL" and node.is_active_output), None)
    surface = output.inputs["Surface"] if output is not None else None
    if surface is None or not surface.is_linked or surface.links[0].from_node.type != "BSDF_PRINCIPLED":
        raise ValueError("The active material output must use a Principled BSDF.")
    shader = surface.links[0].from_node
    base_color = shader.inputs["Base Color"]
    if base_color.is_linked:
        raise ValueError("The base color input already has a texture or another connection.")
    return source, shader, base_color


def connect_base_color_texture(material_name, image, *, uv_layer=None, color_factor=None):
    source, shader, base_color = unlinked_base_color(material_name)
    nodes = source.node_tree.nodes
    links = source.node_tree.links
    texture = nodes.new("ShaderNodeTexImage")
    texture.label = image.name
    texture.image = image
    texture.location = (shader.location.x - 310, shader.location.y + 260)
    if uv_layer is not None:
        uv_map = nodes.new("ShaderNodeUVMap")
        uv_map.uv_map = uv_layer
        uv_map.location = (texture.location.x - 180, texture.location.y)
        links.new(uv_map.outputs["UV"], texture.inputs["Vector"])
    if color_factor is None:
        links.new(texture.outputs["Color"], base_color)
        return
    # Use Blender's glTF color factor node layout so flat viewers retain the tint.
    tint = nodes.new("ShaderNodeMix")
    tint.data_type = "RGBA"
    tint.blend_type = "MULTIPLY"
    tint.inputs["Factor"].default_value = 1
    tint.inputs[7].default_value = (*color_factor, 1)
    tint.location = (shader.location.x - 150, shader.location.y)
    links.new(texture.outputs["Color"], tint.inputs[6])
    links.new(tint.outputs[2], base_color)


def apply_surface_texture(material_name, pattern, color_start, color_end, repeats=8):
    if pattern not in ("GRADIENT", "BANDS", "FLOW", "WEAVE", "MOTTLE"):
        raise ValueError("Use GRADIENT, BANDS, FLOW, WEAVE, or MOTTLE for a surface texture.")
    repeats = integer(repeats, 1, 48)
    first_color = tuple(unit(channel) for channel in vector(color_start, 3))
    second_color = tuple(unit(channel) for channel in vector(color_end, 3))
    source, unused_shader, unused_socket = unlinked_base_color(material_name)
    found_surface = False
    for result in bpy.context.scene.objects:
        slots = {index for index, slot in enumerate(result.material_slots) if slot.material == source}
        if not slots:
            continue
        if result.type != "MESH":
            raise ValueError(f"Texture material {material_name!r} is also used by non-mesh object {result.name!r}.")
        if not any(polygon.material_index in slots for polygon in result.data.polygons):
            continue
        if result.data.uv_layers.get(SURFACE_UV_LAYER) is None:
            raise ValueError(f"Run project_uv on mesh {result.name!r} before applying its texture.")
        found_surface = True
    if not found_surface:
        raise ValueError("Assign the texture material to a mesh before applying its texture.")

    def linear(channel):
        return channel / 12.92 if channel <= 0.04045 else ((channel + 0.055) / 1.055) ** 2.4

    def srgb(channel):
        return channel * 12.92 if channel <= 0.0031308 else 1.055 * channel ** (1 / 2.4) - 0.055

    first_linear = tuple(linear(channel) for channel in first_color)
    second_linear = tuple(linear(channel) for channel in second_color)
    color_factor = tuple(max(first, second) for first, second in zip(first_linear, second_linear))

    def color_at_uv(u, v):
        if pattern == "GRADIENT":
            weight = v * v * (3 - 2 * v)
        elif pattern == "BANDS":
            weight = 0.5 + 0.5 * math.cos(math.tau * repeats * u)
        elif pattern == "FLOW":
            bend = 0.075 * math.sin(math.pi * v)
            phase = math.tau * (repeats * (u + bend) + 0.09 * math.sin(math.tau * 2 * u))
            weight = 0.48 + 0.32 * math.cos(phase) + 0.14 * (v - 0.5)
        elif pattern == "WEAVE":
            weight = 0.5 + 0.5 * math.cos(math.tau * repeats * u) * math.cos(math.tau * repeats * v)
        else:
            weight = 0.5 + 0.25 * math.sin(math.tau * repeats * u + math.sin(math.tau * 2 * v))
            weight += 0.25 * math.cos(math.tau * (repeats * v - 3 * u) + math.sin(math.tau * 5 * u))
        return [
            255 * srgb((first + (second - first) * weight) / factor) if factor > 0 else 0
            for first, second, factor in zip(first_linear, second_linear, color_factor)
        ]

    with tempfile.TemporaryDirectory() as texture_directory:
        image = create_packed_color_texture(Path(texture_directory) / "Surface.png", (SURFACE_TEXTURE_SIZE, SURFACE_TEXTURE_SIZE), color_at_uv)
    image.name = material_name + " Surface"
    image.filepath = "//Surface.png"
    connect_base_color_texture(material_name, image, uv_layer=SURFACE_UV_LAYER, color_factor=color_factor)


def finish_surface(name, material_name, projection, axes, pattern, strength=0.12, repeats=12):
    strength = number(strength)
    if not 0 < strength <= 0.35:
        raise ValueError("Surface finish strength must be greater than zero and at most 0.35.")
    material_data, _, color_input = unlinked_base_color(material_name)
    target = editable_object(name)
    if target.type != "MESH" or material_data not in tuple(target.data.materials):
        raise ValueError("Assign the finish material to the target mesh first.")
    for result in bpy.context.scene.objects:
        if result != target and any(slot.material == material_data for slot in result.material_slots):
            raise ValueError("Finish surface needs a material dedicated to this mesh.")

    def display_channel(channel):
        return min(1, channel * 12.92 if channel <= 0.0031308 else 1.055 * channel ** (1 / 2.4) - 0.055)

    color = tuple(color_input.default_value)[:3]
    darker = tuple(display_channel(channel * (1 - strength)) for channel in color)
    lighter = tuple(display_channel(channel * (1 + strength / 2)) for channel in color)
    project_uv(name, projection, axes)
    apply_surface_texture(material_name, pattern, darker, lighter, repeats)


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


def surface_ray_caster(target, dependencies):
    evaluated_target = target.evaluated_get(dependencies)
    target_mesh = evaluated_target.data
    if not target_mesh.polygons:
        raise ValueError("The evaluated projection target has no mesh faces.")
    target_world = evaluated_target.matrix_world.copy()
    try:
        world_to_target = target_world.inverted()
    except ValueError as error:
        raise ValueError("The projection target has a singular world transform.") from error
    world_to_target_direction = world_to_target.to_3x3()
    surface = BVHTree.FromPolygons([vertex.co for vertex in target_mesh.vertices], [tuple(polygon.vertices) for polygon in target_mesh.polygons])

    def cast(origin_world, direction_world, max_distance):
        origin_target = world_to_target @ origin_world
        direction_target = world_to_target_direction @ direction_world
        direction_scale = math.hypot(*direction_target)
        distance_target = max_distance * direction_scale
        if direction_scale == 0 or not math.isfinite(distance_target) or not all(math.isfinite(value) for value in origin_target):
            raise ValueError("The projection ray cannot be represented in target space.")
        location, _normal, _face_index, _distance = surface.ray_cast(origin_target, direction_target / direction_scale, distance_target)
        if location is None:
            return None
        return target_world @ location

    return cast


def surface_ray_direction(ray_direction):
    direction_components = vector(ray_direction)
    direction_length = math.hypot(*direction_components)
    if direction_length == 0:
        raise ValueError("The surface ray direction cannot be zero.")
    return Vector(tuple(component / direction_length for component in direction_components))


def surface_gap_sampler(target, dependencies, cast, direction):
    evaluated = target.evaluated_get(dependencies)
    front = min(direction.dot(evaluated.matrix_world @ Vector(corner)) for corner in evaluated.bound_box)

    def sample(point, max_gap):
        tolerance = max(1e-6, max(abs(front), *(abs(value) for value in point)) * 2e-7)
        start_distance = max(0, direction.dot(point) - front) + 4 * tolerance
        hit = cast(point - direction * start_distance, direction, start_distance + max_gap + 2 * tolerance)
        return (None if hit is None else (hit - point).dot(direction)), tolerance

    return sample


def triangle_surface_samples(first, second, third):
    yield (first + second + third) / 3
    for row in range(5):
        for column in range(5 - row):
            if (row, column) in ((4, 0), (0, 4), (0, 0)):
                continue
            yield (first * row + second * column + third * (4 - row - column)) / 4


def surface_oval(name, target_name, center, horizontal, vertical, max_distance, offset, surface_error, lower_material, upper_material, minimum_scale=1, max_center_shift=0, optional=False):
    optional = flag(optional)
    center = Vector(vector(center))
    horizontal = Vector(vector(horizontal))
    vertical = Vector(vector(vertical))
    normal = horizontal.cross(vertical)
    if normal.length <= 1e-8:
        raise ValueError("Surface oval axes must be nonzero and nonparallel.")
    normal.normalize()
    for material_name in (lower_material, upper_material):
        if bpy.data.materials.get(object_name(material_name)) is None:
            raise ValueError("Create the surface oval materials first.")
    vertices = []
    faces = []
    columns = 9
    rows = 25
    for row in range(rows):
        angle = (row / (rows - 1) - 0.5) * math.pi
        width = max(0.002, math.cos(angle))
        for column in range(columns):
            point = center + vertical * math.sin(angle) + horizontal * (width * (2 * column / (columns - 1) - 1))
            vertices.append(tuple(point))
    for row in range(rows - 1):
        for column in range(columns - 1):
            index = row * columns + column
            faces.append((index, index + 1, index + columns + 1, index + columns))
    mesh(name, vertices, faces)
    try:
        assign_material(name, lower_material)
        assign_material(name, upper_material, tuple(range((rows // 2) * (columns - 1), len(faces))))
        smooth(name)
        direction = tuple(-normal)
        project_surface(name, target_name, direction, max_distance, offset, surface_error, minimum_scale, max_center_shift)
        check_surface_gap(name, target_name, direction, offset + surface_error, 0)
    except Exception as error:
        result = scene_object(name)
        data = result.data
        delete(name)
        if data.users == 0:
            bpy.data.meshes.remove(data)
        if optional and isinstance(error, SurfaceProjectionMiss):
            print(f"Skipped optional surface oval: {error}", flush=True)
            return
        raise


def optional_surface_oval(name, target_name, center, horizontal, vertical, max_distance, offset, surface_error, material_name, minimum_scale=0.75, max_center_shift=0):
    surface_oval(name, target_name, center, horizontal, vertical, max_distance, offset, surface_error,
                 material_name, material_name, minimum_scale, max_center_shift, True)


def surface_stroke(name, target_name, points, ray_direction, width, material_name, max_distance, offset, surface_error):
    if not isinstance(points, (list, tuple)) or not 2 <= len(points) <= 32:
        raise ValueError("A surface stroke needs two to thirty-two path points.")
    direction = surface_ray_direction(ray_direction)
    half_width = positive(width) / 2
    controls = [Vector(vector(point)) for point in points]
    path = []
    for index in range(len(controls) - 1):
        start, end = controls[index:index + 2]
        if ((end - start).cross(direction)).length <= 1e-8:
            raise ValueError("Stroke path segments must have distinct projected points.")
        previous = controls[max(0, index - 1)]
        following = controls[min(len(controls) - 1, index + 2)]
        for step in range(6):
            fraction = step / 6
            square = fraction * fraction
            cube = square * fraction
            path.append((2 * start + (end - previous) * fraction
                         + (2 * previous - 5 * start + 4 * end - following) * square
                         + (3 * start - previous - 3 * end + following) * cube) / 2)
    path.append(controls[-1])
    vertices = []
    for index, point in enumerate(path):
        tangent = path[min(len(path) - 1, index + 1)] - path[max(0, index - 1)]
        across = direction.cross(tangent)
        if across.length <= 1e-8:
            raise ValueError("The stroke folds within its projection plane.")
        taper = min(1, 0.2 + 0.45 * min(index, len(path) - 1 - index))
        across = across.normalized() * (half_width * taper)
        vertices.extend((tuple(point - across), tuple(point + across)))
    faces = [(index, index + 1, index + 3, index + 2) for index in range(0, len(vertices) - 2, 2)]
    mesh(name, vertices, faces)
    assign_material(name, material_name)
    project_surface(name, target_name, ray_direction, max_distance, offset, surface_error)
    check_surface_gap(name, target_name, ray_direction, offset + surface_error)


def surface_sheet(name, targets, rows, ray_direction, max_distance, offsets, steps=4, minimum_width_scale=1, edge_clearance=0):
    if not isinstance(targets, (list, tuple)) or not 1 <= len(targets) <= 4:
        raise ValueError("A surface sheet needs one to four target meshes.")
    if not isinstance(rows, (list, tuple)) or not 2 <= len(rows) <= 16:
        raise ValueError("A surface sheet needs two to sixteen rows.")
    if not isinstance(rows[0], (list, tuple)) or not 2 <= len(rows[0]) <= 32:
        raise ValueError("A surface sheet needs two to thirty-two points per row.")
    if not isinstance(offsets, (list, tuple)) or len(offsets) != len(rows):
        raise ValueError("Give each row one signed surface offset.")
    columns = len(rows[0])
    points = [tuple(Vector(vector(point)) for point in row) for row in rows]
    if any(len(row) != columns for row in points):
        raise ValueError("All sheet rows must have the same length.")
    offsets = tuple(number(offset) for offset in offsets)
    direction = surface_ray_direction(ray_direction)
    distance = positive(max_distance)
    steps = integer(steps, 1, 8)
    minimum_width_scale = number(minimum_width_scale)
    if not 0.5 <= minimum_width_scale <= 1:
        raise ValueError("Surface sheet minimum_width_scale must be between 0.5 and 1.")
    edge_clearance = number(edge_clearance)
    if edge_clearance < 0:
        raise ValueError("Surface sheet edge clearance cannot be negative.")
    new_object_name(name)

    view_layer = bpy.context.view_layer
    view_layer.update()
    dependencies = bpy.context.evaluated_depsgraph_get()
    casters = []
    for target_name in targets:
        target = scene_object(target_name)
        if target.type != "MESH" or target.name not in view_layer.objects:
            raise ValueError("Every sheet target must be a mesh in the active view layer.")
        casters.append(surface_ray_caster(target, dependencies))

    def interpolate(row, column):
        index = min(columns - 2, column // steps)
        factor = (column - index * steps) / steps
        before = row[max(0, index - 1)]
        start = row[index]
        end = row[index + 1]
        after = row[min(columns - 1, index + 2)]
        squared = factor * factor
        cubed = squared * factor
        return (start * (2 * cubed - 3 * squared + 1) + (end - before) * (cubed - 2 * squared + factor) * 0.5
                + end * (3 * squared - 2 * cubed) + (after - start) * (cubed - squared) * 0.5)

    vertices = []
    width = (columns - 1) * steps + 1
    height = (len(rows) - 1) * steps + 1
    for row in range(height):
        index = min(len(rows) - 2, row // steps)
        factor = (row - index * steps) / steps
        offset = offsets[index] + (offsets[index + 1] - offsets[index]) * factor
        origins = [interpolate(points[index], column).lerp(interpolate(points[index + 1], column), factor) for column in range(width)]
        center = (origins[(width - 1) // 2] + origins[width // 2]) / 2
        attempts = 1 if minimum_width_scale == 1 else 7
        for attempt in range(attempts):
            scale = 1 if attempts == 1 else 1 - (1 - minimum_width_scale) * attempt / (attempts - 1)
            fitted = []
            for column, point in enumerate(origins):
                origin = center + (point - center) * scale
                hits = [hit for cast in casters if (hit := cast(origin, direction, distance)) is not None]
                if not hits:
                    break
                hit = min(hits, key=lambda point: direction.dot(point - origin))
                width_fraction = abs(2 * column / (width - 1) - 1)
                fitted.append(tuple(hit - direction * (offset + edge_clearance * width_fraction ** 2 * min(1, row / steps))))
            if len(fitted) == width:
                vertices.extend(fitted)
                break
        else:
            raise ValueError(f"Surface sheet {name!r} missed every target at row {row}, column {column}.")

    faces = []
    for row in range(height - 1):
        for column in range(width - 1):
            index = row * width + column
            faces.append((index, index + width, index + width + 1, index + 1))
    first, second, third = (Vector(vertices[index]) for index in faces[0][:3])
    if (second - first).cross(third - first).dot(direction) > 0:
        faces = [indices[::-1] for indices in faces]
    mesh(name, vertices, faces)
    smooth(name)


class SurfaceProjectionMiss(ValueError):
    pass


def project_surface(name, target_name, ray_direction, max_distance, offset=0, surface_error=None, minimum_scale=1, max_center_shift=0):
    direction_world = surface_ray_direction(ray_direction)
    max_distance = positive(max_distance)
    offset = number(offset)
    if offset < 0:
        raise ValueError("A surface offset cannot be negative.")
    minimum_scale = number(minimum_scale)
    if not 0.5 <= minimum_scale <= 1:
        raise ValueError("Projection minimum_scale must be between 0.5 and 1.")
    max_center_shift = number(max_center_shift)
    if max_center_shift < 0:
        raise ValueError("Projection max_center_shift cannot be negative.")
    if surface_error is not None:
        surface_error = positive(surface_error)
        if surface_error >= offset:
            raise ValueError("Surface refinement requires a positive error smaller than the offset.")
    if bpy.context.mode != "OBJECT":
        raise ValueError("Surface projection requires Object Mode.")

    source = editable_object(name)
    target = scene_object(target_name)
    if source == target:
        raise ValueError("Surface projection needs two distinct objects.")
    if source.type != "MESH" or target.type != "MESH":
        raise ValueError("Surface projection needs two mesh objects.")
    if source.override_library is not None or not source.is_editable:
        raise ValueError("The projected detail must be editable locally.")
    if not source.data.polygons:
        raise ValueError("The projected detail mesh needs faces.")
    if source.modifiers or source.data.shape_keys is not None:
        raise ValueError("Project a detail mesh with no modifiers or shape keys; build its required vertices before projecting.")
    view_layer = bpy.context.view_layer
    view_layer.update()
    if view_layer.objects.get(name) is None or view_layer.objects.get(target_name) is None:
        raise ValueError("Both projection objects must be in the active view layer.")

    dependencies = bpy.context.evaluated_depsgraph_get()
    source_world = source.evaluated_get(dependencies).matrix_world.copy()
    try:
        world_to_source = source_world.inverted()
    except ValueError as error:
        raise ValueError("The projected detail has a singular world transform.") from error
    cast = surface_ray_caster(target, dependencies)
    displacement = direction_world * offset
    def project(point, index):
        hit = cast(point, direction_world, max_distance)
        if hit is None:
            raise SurfaceProjectionMiss(f"Projection of {name!r} missed {target_name!r} at sample {index} within {max_distance:g} world units.")
        return hit - displacement

    origins = [source_world @ vertex.co for vertex in source.data.vertices]
    if minimum_scale < 1 or max_center_shift > 0:
        center = sum(origins, Vector((0, 0, 0))) / len(origins)
        shifts = [Vector((0, 0, 0))]
        if max_center_shift > 0:
            target_evaluated = target.evaluated_get(dependencies)
            target_center = target_evaluated.matrix_world @ (sum((Vector(corner) for corner in target_evaluated.bound_box), Vector((0, 0, 0))) / 8)
            inward = target_center - center
            inward -= direction_world * inward.dot(direction_world)
            if inward.length > 1e-6:
                inward *= min(1, max_center_shift / inward.length)
                shifts.extend(inward * step / 4 for step in range(1, 5))
        source.data.calc_loop_triangles()
        patch_triangles = [tuple(triangle.vertices) for triangle in source.data.loop_triangles]

        def fit_origins():
            best_candidate = None
            for step in range(7 if minimum_scale < 1 else 1):
                scale = 1 - (1 - minimum_scale) * step / 6
                for shift in shifts:
                    candidate = [center + shift + (point - center) * scale for point in origins]
                    hits = [cast(point, direction_world, max_distance) for point in candidate]
                    if any(hit is None for hit in hits):
                        continue
                    if best_candidate is None:
                        best_candidate = candidate
                    if surface_error is None:
                        return candidate
                    # A grazing ray at the edge can hit even though adjoining faces cannot be fitted.
                    if all((hit := cast((candidate[a] + candidate[b] + candidate[c]) / 3, direction_world, max_distance)) is not None
                           and abs(direction_world.dot(hit - (hits[a] + hits[b] + hits[c]) / 3)) <= 4 * surface_error
                           for a, b, c in patch_triangles):
                        return candidate
            return best_candidate if best_candidate is not None else origins

        origins = fit_origins()
    projected = [project(point, index) for index, point in enumerate(origins)]
    if surface_error is not None:
        source.data.calc_loop_triangles()
        triangles = [(tuple(triangle.vertices), triangle.polygon_index) for triangle in source.data.loop_triangles]
        sample_gap = surface_gap_sampler(target, dependencies, cast, direction_world)
        split = False
        for level in range(5):
            within_error = True
            for indices, _ in triangles:
                for point in triangle_surface_samples(*(projected[index] for index in indices)):
                    gap, tolerance = sample_gap(point, offset + surface_error)
                    if gap is None or abs(gap - offset) > surface_error + tolerance:
                        within_error = False
                        break
                if not within_error:
                    break
            if within_error:
                break
            if level == 4 or len(triangles) * 4 > 16384:
                raise SurfaceProjectionMiss(f"Projection of {name!r} could not follow the surface within the refinement limit.")
            if source.vertex_groups or source.data.uv_layers or source.data.has_custom_normals:
                raise ValueError("Refine an untextured detail mesh without custom attributes or vertex groups.")
            if any(not item.is_internal and not item.is_required and item.name not in {"material_index", "sharp_face"} for item in source.data.attributes):
                raise ValueError("Refine an untextured detail mesh without custom attributes or vertex groups.")
            split = True
            midpoints = {}
            refined = []
            for (first, second, third), polygon_index in triangles:
                edges = []
                for start, end in ((first, second), (second, third), (third, first)):
                    edge = (min(start, end), max(start, end))
                    if edge not in midpoints:
                        index = len(origins)
                        origin = (origins[start] + origins[end]) / 2
                        projected.append(project(origin, index))
                        origins.append(origin)
                        midpoints[edge] = index
                    edges.append(midpoints[edge])
                ab, bc, ca = edges
                refined.extend((indices, polygon_index) for indices in ((first, ab, ca), (ab, second, bc), (ca, bc, third), (ab, bc, ca)))
            triangles = refined

    coordinates = []
    for point in projected:
        coordinate = world_to_source @ point
        if not all(math.isfinite(value) for value in coordinate):
            raise ValueError("Surface projection produced an invalid vertex coordinate.")
        coordinates.append(coordinate)

    if surface_error is not None and split:
        original = source.data
        replacement = bpy.data.meshes.new(original.name + " Projected")
        try:
            replacement.from_pydata(coordinates, [], [indices for indices, _ in triangles])
            for material_slot in original.materials:
                replacement.materials.append(material_slot)
            for polygon, (_, original_index) in zip(replacement.polygons, triangles):
                original_polygon = original.polygons[original_index]
                polygon.material_index = original_polygon.material_index
                polygon.use_smooth = original_polygon.use_smooth
            replacement.update()
        except Exception:
            bpy.data.meshes.remove(replacement)
            raise
        source.data = replacement
        if original.users == 0:
            original_name = original.name
            bpy.data.meshes.remove(original)
            replacement.name = original_name
        return
    make_data_editable(source)
    for vertex, coordinate in zip(source.data.vertices, coordinates):
        vertex.co = coordinate
    source.data.update()


def check_surface_gap(name, target_name, ray_direction, max_gap, max_inset=0):
    direction_world = surface_ray_direction(ray_direction)
    max_gap = positive(max_gap)
    max_inset = number(max_inset)
    if max_inset < 0:
        raise ValueError("The allowed surface inset cannot be negative.")
    detail = scene_object(name)
    target = scene_object(target_name)
    if detail == target:
        raise ValueError("The surface gap check needs two distinct objects.")
    if detail.type != "MESH" or target.type != "MESH":
        raise ValueError("The surface gap check needs two mesh objects.")
    view_layer = bpy.context.view_layer
    view_layer.update()
    if view_layer.objects.get(name) is None or view_layer.objects.get(target_name) is None:
        raise ValueError("Both surface gap objects must be in the active view layer.")

    dependencies = bpy.context.evaluated_depsgraph_get()
    evaluated_detail = detail.evaluated_get(dependencies)
    if not evaluated_detail.data.polygons:
        raise ValueError("The evaluated detail mesh needs faces for the surface gap check.")
    cast = surface_ray_caster(target, dependencies)
    sample_gap = surface_gap_sampler(target, dependencies, cast, direction_world)

    def check(point, location):
        gap, tolerance = sample_gap(point, max_gap)
        if gap is None:
            raise SurfaceProjectionMiss(f"Surface gap check for {name!r}: no surface of {target_name!r} within the allowed gap at {location}; check direction and overlap.")
        if gap < -max_inset - tolerance:
            raise SurfaceProjectionMiss(f"Surface gap check for {name!r}: {location} is inset by {-gap:g}; allowed {max_inset:g}.")
        if gap > max_gap + tolerance:
            raise SurfaceProjectionMiss(f"Surface gap check for {name!r}: {location} is raised by {gap:g}; allowed {max_gap:g}.")

    mesh_data = evaluated_detail.data
    positions = [evaluated_detail.matrix_world @ vertex.co for vertex in mesh_data.vertices]
    for index, point in enumerate(positions):
        check(point, f"vertex {index}")
    mesh_data.calc_loop_triangles()
    for triangle in mesh_data.loop_triangles:
        for point in triangle_surface_samples(*(positions[index] for index in triangle.vertices)):
            check(point, f"face {triangle.polygon_index} sample")


def optional_surface_patch(name, vertices, faces, material_name, target_name, ray_direction, max_distance, offset, surface_error, minimum_scale=1, max_center_shift=0):
    mesh(name, vertices, faces)
    try:
        assign_material(name, material_name)
        project_surface(name, target_name, ray_direction, max_distance, offset, surface_error, minimum_scale, max_center_shift)
        check_surface_gap(name, target_name, ray_direction, offset + surface_error)
    except SurfaceProjectionMiss as error:
        result = scene_object(name)
        data = result.data
        delete(name)
        if data.users == 0:
            bpy.data.meshes.remove(data)
        print(f"Skipped optional surface patch: {error}", flush=True)


def surface_diamond(name, target_name, side, height, width, length, material_name, offset, surface_error, across_position=0):
    if side not in ("LEFT", "RIGHT", "FRONT", "BACK"):
        raise ValueError("The diamond must face LEFT, RIGHT, FRONT or BACK.")
    height = number(height)
    width = positive(width)
    length = positive(length)
    across_position = number(across_position)
    target = scene_object(target_name)
    bpy.context.view_layer.update()
    evaluated = target.evaluated_get(bpy.context.evaluated_depsgraph_get())
    bounds = [evaluated.matrix_world @ Vector(corner) for corner in evaluated.bound_box]
    outward = Vector({"LEFT": (1, 0, 0), "RIGHT": (-1, 0, 0), "FRONT": (0, -1, 0), "BACK": (0, 1, 0)}[side])
    up = Vector((0, 0, 1))
    across = up.cross(outward)
    inner = min(point.dot(outward) for point in bounds)
    outer = max(point.dot(outward) for point in bounds)
    center = (min(point.dot(across) for point in bounds) + max(point.dot(across) for point in bounds)) / 2
    reach = outer - inner + max(width, length)
    origin = outward * (outer + max(width, length) / 2) + across * (center + across_position) + up * height
    vertices = [tuple(origin - across * (width / 2)), tuple(origin - up * (length / 2)),
                tuple(origin + across * (width / 2)), tuple(origin + up * (length / 2)), tuple(origin)]
    optional_surface_patch(name, vertices, ((0, 1, 4), (1, 2, 4), (2, 3, 4), (3, 0, 4)), material_name, target_name, tuple(-outward),
                           reach, offset, surface_error, 0.65, min(width, length) / 4)


def attach_to_surface(name, target_name, anchor, ray_origin, ray_direction, max_distance, inset=0):
    anchor_local = Vector(vector(anchor))
    origin_world = Vector(vector(ray_origin))
    direction_components = vector(ray_direction)
    direction_length = math.hypot(*direction_components)
    if direction_length == 0:
        raise ValueError("The attachment ray direction cannot be zero.")
    direction_world = Vector(tuple(component / direction_length for component in direction_components))
    max_distance = number(max_distance)
    if max_distance <= 0:
        raise ValueError("The attachment ray distance must be positive.")
    inset = number(inset)
    if inset < 0:
        raise ValueError("The attachment inset cannot be negative.")

    source = editable_object(name)
    target = scene_object(target_name)
    if source == target:
        raise ValueError("Surface attachment needs two distinct objects.")
    if target.type != "MESH":
        raise ValueError("The attachment target must be a mesh.")
    if source.override_library is not None or not source.is_editable:
        raise ValueError("The attached object must be editable locally.")
    if any(not constraint.mute and constraint.influence > 0 for constraint in source.constraints):
        raise ValueError("Surface attachment cannot move an object with active transform constraints.")
    if source.parent is not None and source.parent_type != "OBJECT":
        raise ValueError("Surface attachment supports ordinary object parenting only.")
    ancestor = target.parent
    while ancestor is not None:
        if ancestor == source:
            raise ValueError("The attachment target cannot be a child of the attached object.")
        ancestor = ancestor.parent

    view_layer = bpy.context.view_layer
    if view_layer.objects.get(name) is None or view_layer.objects.get(target_name) is None:
        raise ValueError("Both attachment objects must be in the active view layer.")
    dependencies = bpy.context.evaluated_depsgraph_get()
    evaluated_source = source.evaluated_get(dependencies)
    evaluated_target = target.evaluated_get(dependencies)
    if not evaluated_target.data.polygons:
        raise ValueError("The evaluated attachment target has no mesh faces.")
    target_world = evaluated_target.matrix_world
    try:
        world_to_target = target_world.inverted()
    except ValueError as error:
        raise ValueError("The attachment target has a singular world transform.") from error

    origin_target = world_to_target @ origin_world
    direction_target = world_to_target.to_3x3() @ direction_world
    direction_scale = math.hypot(*direction_target)
    distance_target = max_distance * direction_scale
    if direction_scale == 0 or not math.isfinite(distance_target) or not all(math.isfinite(value) for value in origin_target):
        raise ValueError("The attachment ray cannot be represented in target space.")
    hit, hit_location, _normal, _face_index = evaluated_target.ray_cast(origin_target, direction_target / direction_scale, distance=distance_target)
    if not hit:
        raise ValueError(f"The attachment ray missed {target_name!r} within {max_distance:g} world units.")

    destination = target_world @ hit_location + direction_world * inset
    translation = destination - (evaluated_source.matrix_world @ anchor_local)
    if source.parent is not None:
        evaluated_parent = source.parent.evaluated_get(dependencies)
        parent_space = evaluated_parent.matrix_world @ source.matrix_parent_inverse
        try:
            translation = parent_space.to_3x3().inverted() @ translation
        except ValueError as error:
            raise ValueError("The attached object's parent has a singular transform.") from error
    new_location = source.location + translation
    if not all(math.isfinite(value) for value in new_location):
        raise ValueError("The attachment would move the object to an invalid location.")
    source.location = new_location


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


def deform_mesh_vertices(name, position_for_vertex):
    result = editable_object(name)
    if result.type != "MESH" or result.mode != "OBJECT":
        raise ValueError("Vertex deformation requires a mesh in Object Mode.")
    if result.data.shape_keys is not None:
        raise ValueError("Vertex deformation does not support shape keys.")
    world = result.matrix_world.copy()
    inverse = world.inverted()
    positions = []
    for vertex in result.data.vertices:
        position = world @ vertex.co
        replacement = vector(tuple(position_for_vertex(position.copy())))
        if replacement != tuple(position):
            positions.append((vertex.index, inverse @ Vector(replacement)))
    if not positions:
        return
    make_data_editable(result)
    for index, position in positions:
        result.data.vertices[index].co = position
    result.data.update()


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


def blend_volumes(name, source_names, voxel_size):
    name = new_object_name(name)
    voxel_size = positive(voxel_size)
    if voxel_size < 0.0001:
        raise ValueError("The voxel size must be at least 0.0001 world units.")
    if not isinstance(source_names, (list, tuple)) or not 2 <= len(source_names) <= 16:
        raise ValueError("Volume blending needs 2 to 16 source mesh names.")
    names = [object_name(source_name) for source_name in source_names]
    if len(set(names)) != len(names):
        raise ValueError("Volume blending needs distinct source meshes.")
    if bpy.context.mode != "OBJECT":
        raise ValueError("Volume blending requires Object Mode.")

    dependencies = bpy.context.evaluated_depsgraph_get()
    view_layer = bpy.context.view_layer
    vertices = []
    faces = []
    input_size = 0
    for source_name in names:
        source = scene_object(source_name)
        if source.type != "MESH" or view_layer.objects.get(source_name) is None:
            raise ValueError(f"Volume source {source_name!r} must be a mesh in the active view layer.")
        evaluated = source.evaluated_get(dependencies)
        source_mesh = evaluated.data
        if not source_mesh.polygons:
            raise ValueError(f"Volume source {source_name!r} has no evaluated mesh faces.")
        input_size += len(source_mesh.vertices) + len(source_mesh.loops)
        if input_size > 300_000:
            raise ValueError("Volume sources exceed the combined limit of 300,000 vertices and face corners.")
        world_matrix = evaluated.matrix_world
        determinant = world_matrix.to_3x3().determinant()
        if determinant == 0 or not math.isfinite(determinant):
            raise ValueError(f"Volume source {source_name!r} has an invalid world transform.")
        vertex_start = len(vertices)
        for vertex in source_mesh.vertices:
            position = tuple(world_matrix @ vertex.co)
            if not all(math.isfinite(coordinate) for coordinate in position):
                raise ValueError(f"Volume source {source_name!r} has invalid world coordinates.")
            vertices.append(position)

        edge_directions = {}
        for polygon in source_mesh.polygons:
            corners = tuple(polygon.vertices)
            for first, second in zip(corners, corners[1:] + corners[:1]):
                edge = (min(first, second), max(first, second))
                direction = 1 if first < second else -1
                if edge in edge_directions:
                    if edge_directions[edge] != -direction:
                        raise ValueError(f"Volume source {source_name!r} needs closed faces with consistent winding.")
                    edge_directions[edge] = 0
                else:
                    edge_directions[edge] = direction
            if determinant < 0:
                corners = corners[::-1]
            faces.append(tuple(vertex_start + index for index in corners))
        if any(edge_directions.values()):
            raise ValueError(f"Volume source {source_name!r} has open edges.")

    bounds = [(min(axis), max(axis)) for axis in zip(*vertices)]
    grid_dimensions = [math.ceil((upper - lower) / voxel_size) + 4 for lower, upper in bounds]
    if max(grid_dimensions) > 512 or math.prod(grid_dimensions) > 8_000_000:
        raise ValueError("The voxel grid is too large; increase the voxel size or blend fewer volumes at a time.")
    center = tuple((lower + upper) / 2 for lower, upper in bounds)
    mesh_data = bpy.data.meshes.new(name)
    result = None
    try:
        mesh_data.from_pydata([tuple(value - origin for value, origin in zip(vertex, center)) for vertex in vertices], [], faces)
        if mesh_data.validate(verbose=False):
            raise ValueError("Blender rejected the combined source mesh topology.")
        mesh_data.update()
        result = bpy.data.objects.new(name, mesh_data)
        set_exact_name(result, name)
        bpy.context.scene.collection.objects.link(result)
        result.location = center
        mesh_data.remesh_voxel_size = voxel_size
        mesh_data.remesh_voxel_adaptivity = 0
        mesh_data.use_remesh_fix_poles = False
        mesh_data.use_remesh_preserve_volume = False
        mesh_data.use_remesh_preserve_attributes = False
        with bpy.context.temp_override(active_object=result, object=result):
            require_finished(bpy.ops.object.voxel_remesh(), "Blending volumes with voxel remesh")
        if not mesh_data.polygons:
            raise ValueError("Voxel remesh produced no faces; use a finer voxel size or thicker volumes.")
        if len(mesh_data.loops) > 1_000_000:
            raise ValueError("Voxel remesh produced too many face corners; increase the voxel size.")
        components = list(range(len(mesh_data.vertices)))
        remaining = len(components)
        for edge in mesh_data.edges:
            first, second = edge.vertices
            while components[first] != first:
                components[first] = components[components[first]]
                first = components[first]
            while components[second] != second:
                components[second] = components[components[second]]
                second = components[second]
            if first != second:
                components[second] = first
                remaining -= 1
        if remaining != 1:
            raise ValueError("Voxel remesh did not form one connected volume; make the sources overlap or adjust the voxel size.")
    except Exception:
        if result is not None:
            bpy.data.objects.remove(result, do_unlink=True)
        bpy.data.meshes.remove(mesh_data)
        raise


def sdf_body(name, volumes, blend_radius, cell_size):
    name = new_object_name(name)
    blend_radius = positive(blend_radius)
    cell_size = positive(cell_size)
    if cell_size < 0.0001:
        raise ValueError("The SDF cell size must be at least 0.0001 world units.")
    if not isinstance(volumes, (list, tuple)) or not 1 <= len(volumes) <= 16:
        raise ValueError("An SDF body needs 1 to 16 ellipsoid volumes.")
    ellipsoids = []
    for volume in volumes:
        if not isinstance(volume, (list, tuple)) or len(volume) != 2:
            raise ValueError("An SDF volume needs a center and three radii.")
        center = vector(volume[0])
        radii = vector(volume[1])
        smallest_radius = min(radii)
        if smallest_radius <= 0:
            raise ValueError("SDF ellipsoid radii must be positive.")
        if smallest_radius < cell_size:
            raise ValueError("Use a cell size no greater than the smallest ellipsoid radius.")
        ellipsoids.append((center, radii, smallest_radius))

    # Each smooth minimum lowers the field by at most blend_radius / 4.
    padding = (len(ellipsoids) - 1) * blend_radius * 0.25 + 2 * cell_size
    lower = [min(center[axis] - radii[axis] for center, radii, _ in ellipsoids) - padding for axis in range(3)]
    upper = [max(center[axis] + radii[axis] for center, radii, _ in ellipsoids) + padding for axis in range(3)]
    dimensions = [math.ceil((end - start) / cell_size) + 1 for start, end in zip(lower, upper)]
    sample_count = math.prod(dimensions)
    if max(dimensions) > 256 or sample_count > 1_500_000 or sample_count * len(ellipsoids) > 18_000_000:
        raise ValueError("The SDF grid is too large; increase the cell size or reduce the body extent, blend radius, or volume count.")

    width, height, depth = dimensions
    layer_size = width * height
    origin = ellipsoids[0][0]
    coordinates = [[start - origin[axis] + index * cell_size for index in range(dimensions[axis])] for axis, start in enumerate(lower)]
    local_ellipsoids = [
        (tuple(value - offset for value, offset in zip(center, origin)), tuple(1 / radius for radius in radii), smallest_radius)
        for center, radii, smallest_radius in ellipsoids
    ]
    samples = array("d")
    for z in coordinates[2]:
        for y in coordinates[1]:
            for x in coordinates[0]:
                field = math.inf
                for (center_x, center_y, center_z), (inverse_x, inverse_y, inverse_z), smallest_radius in local_ellipsoids:
                    delta_x, delta_y, delta_z = x - center_x, y - center_y, z - center_z
                    radial = math.hypot(delta_x * inverse_x, delta_y * inverse_y, delta_z * inverse_z)
                    # Outside: distance to the radial surface point. Inside: a signed estimate using the smallest radius.
                    if radial > 1:
                        distance = math.hypot(delta_x, delta_y, delta_z) * (1 - 1 / radial)
                    else:
                        distance = (radial - 1) * smallest_radius
                    overlap = max(0, blend_radius - abs(field - distance)) / blend_radius
                    field = min(field, distance) - overlap * overlap * blend_radius * 0.25
                samples.append(field)

    vertices = []
    faces = []
    edge_vertices = {}

    def grid_point(sample_index):
        grid_z, layer_index = divmod(sample_index, layer_size)
        grid_y, grid_x = divmod(layer_index, width)
        return coordinates[0][grid_x], coordinates[1][grid_y], coordinates[2][grid_z]

    def edge_vertex(first, second):
        edge = (min(first, second), max(first, second))
        if edge not in edge_vertices:
            first_value, second_value = samples[first], samples[second]
            fraction = first_value / (first_value - second_value)
            # Avoid coincident vertices when the level set lands exactly on a grid point.
            fraction = min(0.9999, max(0.0001, fraction))
            edge_vertices[edge] = len(vertices)
            vertices.append(tuple(start + (end - start) * fraction for start, end in zip(grid_point(first), grid_point(second))))
        return edge_vertices[edge]

    # All cells use the same face diagonals, and crossing edges share vertex indices.
    corner_offsets = (0, 1, width, width + 1, layer_size, layer_size + 1, layer_size + width, layer_size + width + 1)
    tetrahedra = ((0, 1, 3, 7), (0, 3, 2, 7), (0, 2, 6, 7), (0, 6, 4, 7), (0, 4, 5, 7), (0, 5, 1, 7))
    for grid_z in range(depth - 1):
        for grid_y in range(height - 1):
            for grid_x in range(width - 1):
                cell_start = grid_z * layer_size + grid_y * width + grid_x
                corners = tuple(cell_start + offset for offset in corner_offsets)
                inside = tuple(samples[index] < 0 for index in corners)
                if all(inside) or not any(inside):
                    continue
                for tetrahedron in tetrahedra:
                    inner = [corner for corner in tetrahedron if inside[corner]]
                    outer = [corner for corner in tetrahedron if not inside[corner]]
                    if not inner or not outer:
                        continue
                    if len(inner) == 1:
                        polygon = [edge_vertex(corners[inner[0]], corners[corner]) for corner in outer]
                    elif len(outer) == 1:
                        polygon = [edge_vertex(corners[corner], corners[outer[0]]) for corner in inner]
                    else:
                        polygon = [edge_vertex(corners[inner[inner_index]], corners[outer[outer_index]]) for inner_index, outer_index in ((0, 0), (0, 1), (1, 1), (1, 0))]
                    first, second, third = (vertices[index] for index in polygon[:3])
                    edge_one = tuple(end - start for start, end in zip(first, second))
                    edge_two = tuple(end - start for start, end in zip(first, third))
                    normal = tuple(edge_one[(axis + 1) % 3] * edge_two[(axis + 2) % 3] - edge_one[(axis + 2) % 3] * edge_two[(axis + 1) % 3] for axis in range(3))
                    toward_outside = tuple(((outer[0] >> axis) & 1) - ((inner[0] >> axis) & 1) for axis in range(3))
                    if sum(component * direction for component, direction in zip(normal, toward_outside)) < 0:
                        polygon.reverse()
                    faces.append(tuple(polygon[:3]))
                    if len(polygon) == 4:
                        faces.append((polygon[0], polygon[2], polygon[3]))
                    if len(faces) > 400_000:
                        raise ValueError("The SDF surface exceeds 400,000 triangles; use a larger cell size.")
    if not faces:
        raise ValueError("The SDF grid produced no surface.")
    mesh(name, vertices, faces, origin)


OPERATIONS = {
    "primitive": primitive, "mesh": mesh, "outline_plate": outline_plate, "loft": loft, "radial_loft": radial_loft, "loft_path": loft_path, "blend_volumes": blend_volumes, "sdf_body": sdf_body, "curve": curve, "empty": empty,
    "transform": transform, "duplicate": duplicate, "parent": parent, "delete": delete, "visible": visible,
    "material": material, "update_material": update_material, "assign_material": assign_material, "smooth": smooth,
    "project_uv": project_uv, "apply_surface_texture": apply_surface_texture, "finish_surface": finish_surface,
    "bevel": bevel, "mirror": mirror, "subdivide": subdivide, "fit_surface": fit_surface,
    "project_surface": project_surface, "check_surface_gap": check_surface_gap, "optional_surface_patch": optional_surface_patch, "surface_sheet": surface_sheet, "surface_oval": surface_oval,
    "optional_surface_oval": optional_surface_oval, "surface_stroke": surface_stroke, "surface_diamond": surface_diamond,
    "attach_to_surface": attach_to_surface, "solidify": solidify, "deform": deform, "boolean": boolean,
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


def read_operations(path, parts_path=None):
    tree = ast.parse(path.read_text(encoding="utf-8"))
    if not 1 <= len(tree.body) <= MAX_OPERATIONS:
        raise ValueError("Invalid number of modeling operations.")
    operations = []
    surface_texture_count = 0
    for statement in tree.body:
        if not isinstance(statement, ast.Expr) or not isinstance(statement.value, ast.Call):
            raise ValueError("Only standalone modeling calls are allowed.")
        call = statement.value
        if not isinstance(call.func, ast.Name) or call.keywords:
            raise ValueError("Use a named modeling function with positional literals only.")
        if call.func.id == "append_part":
            if parts_path is None:
                raise ValueError("Select a Blender parts source before appending a part.")
            function = partial(append_part, parts_path)
        else:
            function = OPERATIONS.get(call.func.id)
        if function is None:
            raise ValueError(f"Modeling function {call.func.id!r} is not allowed.")
        if function in (apply_surface_texture, finish_surface):
            surface_texture_count += 1
            if surface_texture_count > MAX_SURFACE_TEXTURES:
                raise ValueError(f"Use at most {MAX_SURFACE_TEXTURES} surface textures per request.")
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


def reference_profiles(directory, control_directory):
    manifest = control_directory / "References.json"
    if not manifest.is_file():
        return []
    profiles = []
    for direction, filename in json.loads(manifest.read_text(encoding="utf-8")):
        if direction not in ("front", "left", "right", "back"):
            continue
        image = None
        try:
            image = bpy.data.images.load(str(directory / filename), check_existing=False)
            original_width, original_height = image.size
            if min(original_width, original_height) < 32 or original_width * original_height > 16_000_000:
                continue
            scale = 192 / max(original_width, original_height)
            width = min(original_width, max(1, round(original_width * scale)))
            height = min(original_height, max(1, round(original_height * scale)))
            image.scale(width, height)
            pixels = array("f", [0]) * (width * height * 4)
            image.pixels.foreach_get(pixels)
            border = [y * width + x for y in range(height) for x in range(width) if x in (0, width - 1) or y in (0, height - 1)]
            transparent = sum(pixels[index * 4 + 3] < 0.05 for index in border) >= 0.95 * len(border)
            background = tuple(sorted(pixels[index * 4 + channel] for index in border)[len(border) // 2] for channel in range(3))

            def difference(index):
                return max(abs(pixels[index * 4 + channel] - background[channel]) for channel in range(3))

            if not transparent and sum(difference(index) < 0.085 for index in border) < 0.95 * len(border):
                continue
            foreground = {
                index for index in range(width * height)
                if (pixels[index * 4 + 3] > 0.5 if transparent else difference(index) > 0.105)
            }
            largest = []
            while foreground:
                component = [foreground.pop()]
                for index in component:
                    x = index % width
                    y = index // width
                    for nx, ny in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
                        if 0 <= nx < width and 0 <= ny < height:
                            neighbor = ny * width + nx
                            if neighbor in foreground:
                                foreground.remove(neighbor)
                                component.append(neighbor)
                if len(component) > len(largest):
                    largest = component
            if not 0.02 * width * height <= len(largest) <= 0.75 * width * height:
                continue
            lower = min(index // width for index in largest)
            upper = max(index // width for index in largest)
            span = upper - lower + 1
            if span < 40:
                continue
            bands = []
            subject_center = (min(index % width for index in largest) + max(index % width for index in largest) + 1) / 2
            for level in range(5, 100, 5):
                row = lower + round((span - 1) * level / 100)
                columns = [index % width for index in largest if index // width == row]
                if columns:
                    bands.append([level, round(100 * (min(columns) - subject_center) / span, 1),
                                  round(100 * (max(columns) + 1 - subject_center) / span, 1)])
            profile = {"view": direction, "outline_rows_percent_of_subject_height": bands}
            mask = set(largest)
            upper_widths = [(row, sum(row * width + x in mask for x in range(width)))
                            for row in range(lower + round((span - 1) * 0.55), upper + 1)]
            peak_width = max(count for _, count in upper_widths)
            if peak_width:
                last_broad_row = max(row for row, count in upper_widths if count >= peak_width * 0.35)
                narrow_height = round(100 * (last_broad_row - lower) / (span - 1))
                if narrow_height < 96:
                    profile["upper_outline_narrows_above_percent"] = narrow_height
            if not image.is_float:
                def colors_at(index):
                    return tuple(min(15, int(max(0, pixels[index * 4 + channel]) * 16)) for channel in range(3))

                interior = [index for index in largest if index % width not in (0, width - 1)
                            and all(neighbor in mask for neighbor in (index - 1, index + 1, index - width, index + width))]
                color_counts = Counter(colors_at(index) for index in interior)
                if interior and sum(count for _, count in color_counts.most_common(12)) >= 0.6 * len(interior):
                    color_rows = []
                    for level in range(20, 91, 10):
                        row = lower + round((span - 1) * level / 100)
                        runs = []
                        for color, indices in itertools.groupby(range(width), lambda x: colors_at(row * width + x) if row * width + x in mask else None):
                            columns = list(indices)
                            if color is None or len(columns) < max(3, round(span * 0.035)):
                                continue
                            left = round(100 * (columns[0] - subject_center) / span)
                            right = round(100 * (columns[-1] + 1 - subject_center) / span)
                            rgb = "".join(f"{value * 17:02X}" for value in color)
                            runs.append([left, right, rgb])
                        if runs:
                            color_rows.append([level, runs])
                    if color_rows:
                        profile["flat_color_rows"] = color_rows
            profiles.append(profile)
        except (OSError, RuntimeError, ValueError) as error:
            print(f"Skipping reference outline {direction}: {error}", flush=True)
        finally:
            if image is not None:
                bpy.data.images.remove(image)
    return profiles


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


def render_views(scene, result_directory, views, bounds, *, samples=None):
    if bounds is None:
        raise ValueError("The scene contains no renderable model.")
    previous_samples = None
    if samples is not None:
        sample_property = scene.eevee.bl_rna.properties["taa_render_samples"]
        samples = integer(samples, sample_property.hard_min, sample_property.hard_max)
        previous_samples = scene.eevee.taa_render_samples
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
        if samples is not None:
            scene.eevee.taa_render_samples = samples
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
        if previous_samples is not None:
            scene.eevee.taa_render_samples = previous_samples
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


def apply_and_export(scene, directory, control_directory, stage, maximum_triangle_count, parts_path=None):
    if bpy.context.scene != scene:
        raise RuntimeError("The request scene is no longer active.")
    operations = read_operations(control_directory / (stage + ".ops"), parts_path)
    if bpy.context.mode != "OBJECT":
        require_finished(bpy.ops.object.mode_set(mode="OBJECT"), "Entering Object Mode")
    for operation_index, (function, arguments) in enumerate(operations, start=1):
        try:
            function(*arguments)
        except Exception as error:
            function_name = function.func.__name__ if isinstance(function, partial) else function.__name__
            raise RuntimeError(f"{stage} operation {operation_index} ({function_name}): {error}") from error
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


def open_request(directory, control_directory, source_path, parts_path=None):
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
    inventory = scene_inventory(scene)
    profiles = reference_profiles(directory, control_directory)
    if profiles:
        inventory["reference_outline_hints"] = {
            "row_format": ["height_from_bottom", "left_from_subject_center", "right_from_subject_center"],
            "view_profiles": profiles,
        }
    if parts_path is not None:
        with bpy.data.libraries.load(parts_path, link=False) as (available, _):
            inventory["parts_source"] = {"objects": sorted(available.objects), "collections": sorted(available.collections)}
    publish_json(control_directory / "Scene.json", inventory)
    return scene


def error_message(error):
    traceback.print_exc()
    return f"{type(error).__name__}: {error}"


def main():
    directory_argument, source_path, triangle_limit_argument, *parts_arguments = sys.argv[sys.argv.index("--") + 1:]
    parts_path = parts_arguments[0] if parts_arguments else None
    directory = Path(directory_argument)
    control_directory = directory / "Control"
    try:
        maximum_triangle_count = integer(int(triangle_limit_argument), 0, MAX_TRIANGLE_LIMIT)
        scene = open_request(directory, control_directory, source_path, parts_path)
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
                        triangle_count = apply_and_export(scene, directory, control_directory, stage, maximum_triangle_count, parts_path)
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
