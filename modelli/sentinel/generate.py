"""Build the Sentinel's articulated FBX with Blender in background mode."""

import math
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector


bpy.ops.wm.read_factory_settings(use_empty=True)
OUT = Path(__file__).resolve().parents[2] / 'unity/PryingEyes/Assets/_PryingEyes/Resources/Sentinel'
OUT.mkdir(parents=True, exist_ok=True)

PALETTE = {
    'Iron': (0.085, 0.066, 0.057),
    'Brass': (0.58, 0.31, 0.10),
    'Verdigris': (0.09, 0.28, 0.24),
    'Enamel': (0.18, 0.039, 0.026),
    'Ember': (1.0, 0.39, 0.06),
}
materials = {}
for name, rgb in PALETTE.items():
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*rgb, 1)
    m.use_nodes = True
    shader = m.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Base Color'].default_value = (*rgb, 1)
    shader.inputs['Metallic'].default_value = 0 if name == 'Ember' else 0.62
    shader.inputs['Roughness'].default_value = 0.36
    if name == 'Ember':
        shader.inputs['Emission Color'].default_value = (*rgb, 1)
        shader.inputs['Emission Strength'].default_value = 2.6
    materials[name] = m


def pivot(name, parent=None, pos=(0, 0, 0)):
    node = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(node)
    node.parent = parent
    node.location = pos
    return node


def finish(obj, parent, mat, bevel=0):
    obj.parent = parent
    obj.data.materials.append(materials[mat])
    if bevel:
        mod = obj.modifiers.new('Rounded edges', 'BEVEL')
        mod.width = bevel
        mod.segments = 2
        mod = obj.modifiers.new('Panel normals', 'WEIGHTED_NORMAL')
        mod.keep_sharp = True
    return obj


def plate(parent, points, front, depth, mat, bevel=0.007):
    """Extruded outline in Blender's X/Z plane. The face looks down -Y."""
    n = len(points)
    if sum(x * points[(i + 1) % n][1] - points[(i + 1) % n][0] * z
           for i, (x, z) in enumerate(points)) < 0:
        points = points[::-1]
    mesh = bpy.data.meshes.new(mat + ' panel')
    mesh.from_pydata([(x, y, z) for y in (front, front + depth) for x, z in points], [],
                     [tuple(range(n)), tuple(reversed(range(n, 2 * n)))] +
                     [(i, i + n, (i + 1) % n + n, (i + 1) % n) for i in range(n)])
    mesh.update()
    obj = bpy.data.objects.new(mat + ' panel', mesh)
    bpy.context.collection.objects.link(obj)
    return finish(obj, parent, mat, bevel)


def tube(parent, points, mat, radius):
    curve = bpy.data.curves.new(mat + ' trim', 'CURVE')
    curve.dimensions = '3D'
    curve.bevel_depth = radius
    curve.bevel_resolution = 1
    spline = curve.splines.new('POLY')
    spline.points.add(len(points) - 1)
    for point, co in zip(spline.points, points):
        point.co = (*co, 1)
    obj = bpy.data.objects.new(mat + ' trim', curve)
    bpy.context.collection.objects.link(obj)
    finish(obj, parent, mat)


def lathe(parent, pos, radius, width, mat, ring=False, turn=0):
    if ring:
        bpy.ops.mesh.primitive_torus_add(major_segments=32, minor_segments=8,
                                        major_radius=radius, minor_radius=width, location=pos)
    else:
        bpy.ops.mesh.primitive_cylinder_add(vertices=32, radius=radius, depth=width, location=pos)
    obj = bpy.context.object
    obj.rotation_euler = (math.pi / 2, 0, turn)
    for poly in obj.data.polygons:
        poly.use_smooth = ring or len(poly.vertices) == 4
    return finish(obj, parent, mat, 0 if ring else min(0.009, width * .18))


def lancet(width, height):
    return [(0, height), (-width * .66, height * .68), (-width, height * .24),
            (-width, -height * .55), (-width * .51, -height * .84), (0, -height),
            (width * .51, -height * .84), (width, -height * .55), (width, height * .24),
            (width * .66, height * .68)]


def surface_texture():
    """Tileable mottling and fine scuffs shared by all metal pieces."""
    size = 512
    rng = np.random.default_rng(72)

    def noise(cells):
        grid = rng.random((cells, cells))
        coordinate = np.arange(size) * (cells / size)
        index = coordinate.astype(int)
        fraction = coordinate - index
        weight = fraction * fraction * (3 - 2 * fraction)
        row = grid[:, index] * (1 - weight) + grid[:, (index + 1) % cells] * weight
        return row[index] * (1 - weight[:, None]) + row[(index + 1) % cells] * weight[:, None]

    stains = noise(7) * .44 + noise(30) * .34 + noise(100) * .22
    grain = rng.random((size, size))
    metal = np.clip(.55 + stains * .54 + (grain - .5) * .13, 0, 1)
    for _ in range(510):
        x, y = rng.integers(0, size, size=2)
        length = rng.integers(2, 38)
        d = np.arange(length)
        metal[(y + d // 8) % size, (x + d) % size] -= np.sin(d / length * math.pi) * rng.uniform(.04, .16)
    rgba = np.dstack((metal, metal, metal, np.ones_like(metal))).astype(np.float32)
    texture = bpy.data.images.new('Patina', width=size, height=size, alpha=False)
    texture.pixels.foreach_set(rgba.ravel())
    texture.filepath_raw = str(OUT / 'Patina.png')
    texture.file_format = 'PNG'
    texture.save()


def planar_uv(obj):
    uv = obj.data.uv_layers.active or obj.data.uv_layers.new()
    matrix = obj.matrix_world
    basis = matrix.to_3x3()
    coordinates = [matrix @ vertex.co for vertex in obj.data.vertices]
    for polygon in obj.data.polygons:
        normal = basis @ polygon.normal
        axes = [0, 1, 2]
        axes.remove(max(axes, key=lambda axis: abs(normal[axis])))
        for loop in polygon.loop_indices:
            point = coordinates[obj.data.loops[loop].vertex_index]
            uv.data[loop].uv = (point[axes[0]] * 2.3, point[axes[1]] * 2.3)


root = pivot('Sentinel')
body = pivot('Chassis', root)
plate(body, lancet(.355, .64), -.195, .39, 'Iron', .038)
for y, sign in [(-.217, 1), (.216, -1)]:
    plate(body, lancet(.315, .58), y, .022, 'Brass')
    plate(body, lancet(.292, .551), y - .011 * sign, .014, 'Verdigris')
    outline = lancet(.263, .52)
    tube(body, [(x, y - .022 * sign, z) for x, z in outline + outline[:1]], 'Brass', .009)
    points = [(0, .50), (-.095, .38), (-.14, .275), (-.09, .302), (0, .36), (.09, .302), (.14, .275), (.095, .38)]
    plate(body, points, y - .027 * sign, .014, 'Iron', .004)
    tube(body, [(x, y - .046 * sign, z) for x, z in points + points[:1]], 'Brass', .007)
    lathe(body, (0, y - .046 * sign, .414), .021, .012, 'Brass', ring=True)
    for side in [-1, 1]:
        for i in range(3):
            x = side * (.105 + i * .059)
            bottom = -.51 + i * .047
            points = [(x, bottom + .21), (x - .018, bottom + .176), (x - .018, bottom),
                      (x + .018, bottom), (x + .018, bottom + .176)]
            plate(body, points, y - .022 * sign, .012, 'Enamel')
            tube(body, [(a, y - .035 * sign, z) for a, z in points + points[:1]], 'Brass', .005)
        for z in [-.25, -.14, .29]:
            lathe(body, (side * .258, y - .029 * sign, z), .012, .015, 'Brass')

eye_z = .04
lathe(body, (0, -.24, eye_z), .239, .072, 'Brass')
lathe(body, (0, -.28, eye_z), .204, .029, 'Iron')
lathe(body, (0, -.3, eye_z), .189, .013, 'Enamel')
lathe(body, (0, -.318, eye_z), .205, .012, 'Brass', ring=True)
lathe(body, (0, -.321, eye_z), .153, .009, 'Brass', ring=True)
for i in range(12):
    angle = math.tau * i / 12
    lathe(body, (.222 * math.cos(angle), -.286, eye_z + .222 * math.sin(angle)), .008, .012, 'Iron')

aperture = pivot('Aperture', body, (0, -.335, eye_z))
lathe(aperture, (0, 0, 0), .106, .051, 'Ember')
lathe(aperture, (0, -.028, 0), .05, .014, 'Iron')
lathe(aperture, (0, -.038, 0), .054, .005, 'Brass', ring=True)
for i in range(6):
    angle = math.tau * i / 6
    blade = pivot(f'Shutter{i}', body, (.137 * math.sin(angle), -.359, eye_z + .137 * math.cos(angle)))
    blade.rotation_euler.y = angle
    plate(blade, [(-.064, .026), (.066, .026), (.061, -.091), (.018, -.125), (-.044, -.067)],
          0, .017, 'Iron', .004)
    tube(blade, [(.063, -.003, .021), (.058, -.003, -.089), (.018, -.003, -.122)], 'Brass', .004)

for side, label in [(-1, 'Left'), (1, 'Right')]:
    wing = pivot('Wing' + label, body, (side * .315, 0, .045))
    lathe(wing, (side * .045, 0, 0), .102, .25, 'Iron')
    lathe(wing, (side * .045, -.126, 0), .095, .029, 'Brass')
    lathe(wing, (side * .045, -.147, 0), .037, .019, 'Enamel')
    for i in range(3):
        feather = pivot(f'Feather{label}{i}', wing, (side * (.101 + .052 * i), .034 * i, -.05 * i))
        feather.rotation_euler.y = side * i * .31
        length = .69 - i * .057
        points = [(0, .055), (.16, .121), (.36, .102), (.54, .017), (length, -.14),
                  (.43, -.12), (.16, -.075), (.015, -.035)]
        plate(feather, [(side * x, z) for x, z in points], -.044, .105, 'Brass', .013)
        plate(feather, [(side * (.22 + (x - .22) * .91), z * .81) for x, z in points],
              -.053, .018, 'Verdigris', .005)
        for yy in [-.06, .063]:
            tube(feather, [(side * x, yy, z) for x, z in [(0.075, .03), (.24, .042), (.39, -.005),
                                                         (.51, -.068), (length - .022, -.119)]], 'Brass', .006)
            for j in range(3):
                x = .20 + .093 * j
                z = .061 - .014 * j
                tube(feather, [(side * x, yy, z), (side * (x + .027), yy, z - .033),
                               (side * (x + .086), yy, z - .055), (side * (x + .035), yy, z - .103)],
                     'Brass', .0032)
        lathe(feather, (side * .043, -.055, .01), .024, .023, 'Iron')

chain = pivot('Censer', body, (0, 0, -.54))
for i in range(4):
    lathe(chain, (0, 0, -.041 - i * .045), .032, .006, 'Brass', ring=True, turn=i * math.pi / 2)
lathe(chain, (0, 0, -.255), .067, .085, 'Ember').rotation_euler.x = 0
for i in range(6):
    a = math.tau * i / 6
    tube(chain, [(r * math.cos(a), r * math.sin(a), z) for r, z in
                 [(.014, -.184), (.076, -.223), (.083, -.257), (.065, -.295), (.012, -.33)]], 'Brass', .009)
for z, r in [(-.225, .075), (-.256, .083), (-.291, .067)]:
    lathe(chain, (0, 0, z), r, .007, 'Brass', ring=True).rotation_euler.x = 0

# Collapse the static pieces on each pivot to limit the per-enemy renderer count.
for node in [obj for obj in bpy.context.scene.objects if obj.type == 'EMPTY']:
    parts = [obj for obj in node.children if obj.type in {'MESH', 'CURVE'}]
    if not parts:
        continue
    bpy.ops.object.select_all(action='DESELECT')
    for obj in parts:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.convert(target='MESH')
    bpy.ops.object.join()
    mesh = bpy.context.object
    mesh.name = node.name + '_Mesh'
    planar_uv(mesh)

surface_texture()
bpy.ops.export_scene.fbx(filepath=str(OUT / 'Sentinel.fbx'), object_types={'EMPTY', 'MESH'},
                         axis_forward='-Z', axis_up='Y', bake_anim=False, add_leaf_bones=False)
