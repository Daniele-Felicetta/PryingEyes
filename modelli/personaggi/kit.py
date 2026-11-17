"""
Shared helpers for the character generators (Blender 4.5, run headless).

Adapted from modelli/monaco/genera_monaco.py: bmesh primitives, voxel "organic" fills, a small node-tree helper for
procedural materials, Cycles preview setup, and the bake/export plumbing. Axes: Z up, characters face -Y, the character's
right is -X. Units: metres.
"""
import bpy, bmesh, math, os, sys, time
from mathutils import Vector, Matrix, Quaternion, noise, kdtree
from mathutils.bvhtree import BVHTree
from math import sin, cos, pi, radians, sqrt, exp, atan2

ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []


def V(*a):
    return Vector(a)


def clamp(x, a=0.0, b=1.0):
    return a if x < a else b if x > b else x


def lerp(a, b, t):
    return a + (b - a) * t


def smoothstep(e0, e1, x):
    if e1 == e0:
        return 1.0 if x >= e1 else 0.0
    t = clamp((x - e0) / (e1 - e0))
    return t * t * (3 - 2 * t)


# ----------------------------------------------------------------------------- scene
def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.unit_settings.system = 'METRIC'
    return sc


def link(ob, coll_name=None):
    sc = bpy.context.scene
    coll = sc.collection
    if coll_name:
        coll = bpy.data.collections.get(coll_name)
        if coll is None:
            coll = bpy.data.collections.new(coll_name)
            sc.collection.children.link(coll)
    coll.objects.link(ob)
    return ob


def bm_obj(bm, name, coll=None):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    link(ob, coll)
    return ob


def save_blend(path):
    bpy.context.preferences.filepaths.save_version = 0
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=path)


def apply_all(ob):
    """Apply every modifier (evaluated mesh) without operators."""
    dg = bpy.context.evaluated_depsgraph_get()
    dg.update()
    ev = ob.evaluated_get(dg)
    me = bpy.data.meshes.new_from_object(ev, preserve_all_data_layers=True, depsgraph=dg)
    me.name = ob.name
    old = ob.data
    ob.modifiers.clear()
    ob.data = me
    if old.users == 0:
        bpy.data.meshes.remove(old)


def select_only(obs, active=None):
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in obs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = active or obs[0]


def join_objects(name, obs):
    if len(obs) > 1:
        select_only(obs)
        bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active if len(obs) > 1 else obs[0]
    ob.name = name
    ob.data.name = name
    return ob


# ----------------------------------------------------------------------------- bmesh primitives
def bm_ellipsoid(bm, c, r, rot=None, seg=24):
    R = rot.to_4x4() if rot is not None else Matrix.Identity(4)
    M = Matrix.Translation(Vector(c)) @ R @ Matrix.Diagonal((r[0], r[1], r[2], 1.0))
    bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=max(6, seg // 2), radius=1.0, matrix=M)


def bm_sphere(bm, c, r, seg=12):
    bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=max(6, seg // 2), radius=r,
                              matrix=Matrix.Translation(Vector(c)))


def bm_capsule(bm, a, b, ra, rb, seg=14):
    a, b = Vector(a), Vector(b)
    d = b - a
    L = d.length
    if L < 1e-6:
        bm_sphere(bm, a, max(ra, rb), seg)
        return
    q = d.to_track_quat('Z', 'Y')
    M = Matrix.Translation((a + b) / 2) @ q.to_matrix().to_4x4()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=seg, radius1=ra, radius2=rb, depth=L, matrix=M)
    bm_sphere(bm, a, ra, seg)
    bm_sphere(bm, b, rb, seg)


def bm_chain(bm, pts, radii, seg=12):
    for i in range(len(pts) - 1):
        bm_capsule(bm, pts[i], pts[i + 1], radii[i], radii[i + 1], seg)


def bm_box(bm, c, s, rot=None):
    """Box centred on c with full size s (x, y, z)."""
    R = rot.to_4x4() if rot is not None else Matrix.Identity(4)
    M = Matrix.Translation(Vector(c)) @ R @ Matrix.Diagonal((s[0] / 2, s[1] / 2, s[2] / 2, 1.0))
    bmesh.ops.create_cube(bm, size=2.0, matrix=M)


def bm_ring(bm, center, axis, R, r, seg=40, rseg=10, squash=1.0):
    """A torus-like band around 'axis' (collars, cuffs, hems)."""
    axis = Vector(axis).normalized()
    Mr = axis.to_track_quat('Z', 'Y').to_matrix()
    grid = []
    for i in range(seg):
        a = 2 * pi * i / seg
        ring = []
        for j in range(rseg):
            b = 2 * pi * j / rseg
            p = V((R + r * cos(b)) * cos(a), (R + r * cos(b)) * sin(a) * squash, r * sin(b))
            ring.append(bm.verts.new(Vector(center) + Mr @ p))
        grid.append(ring)
    for i in range(seg):
        for j in range(rseg):
            bm.faces.new((grid[i][j], grid[(i + 1) % seg][j], grid[(i + 1) % seg][(j + 1) % rseg], grid[i][(j + 1) % rseg]))


def organic(name, fill, voxel, sm_iter=6, sm_fac=0.5, coll=None):
    """Union of overlapping primitives -> voxel remesh -> smoothing."""
    bm = bmesh.new()
    fill(bm)
    ob = bm_obj(bm, name, coll)
    m = ob.modifiers.new('remesh', 'REMESH')
    m.mode = 'VOXEL'
    m.voxel_size = voxel
    m.adaptivity = 0.0
    m.use_smooth_shade = True
    if sm_iter:
        s = ob.modifiers.new('smooth', 'SMOOTH')
        s.factor = sm_fac
        s.iterations = sm_iter
    apply_all(ob)
    return ob


def mirror_x(ob):
    """Mirror an object's mesh across x = 0 (in world space, object at the origin) and fix the winding."""
    me = ob.data
    for v in me.vertices:
        v.co.x = -v.co.x
    me.flip_normals()
    me.update()


def bvh_of(ob):
    dg = bpy.context.evaluated_depsgraph_get()
    return BVHTree.FromObject(ob, dg)


def bm_from(ob):
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    return bm


# ----------------------------------------------------------------------------- materials
class NT:
    """Small helper to build node trees."""

    def __init__(self, mat):
        mat.use_nodes = True
        self.nt = mat.node_tree
        self.nt.nodes.clear()
        self.out = self.nt.nodes.new('ShaderNodeOutputMaterial')
        tc = self.nt.nodes.new('ShaderNodeTexCoord')
        self.obj, self.gen = tc.outputs['Object'], tc.outputs['Generated']

    def node(self, typ, **kw):
        n = self.nt.nodes.new(typ)
        for k, v in kw.items():
            setattr(n, k, v)
        return n

    def val(self, sock, v):
        if isinstance(v, bpy.types.NodeSocket):
            self.nt.links.new(v, sock)
        elif isinstance(v, (tuple, list)) and len(v) == 3 and sock.type == 'RGBA':
            sock.default_value = (*v, 1.0)
        else:
            sock.default_value = v

    def math(self, op, a, b=0.0, c=0.0, clamp=False):
        n = self.node('ShaderNodeMath', operation=op, use_clamp=clamp)
        self.val(n.inputs[0], a)
        self.val(n.inputs[1], b)
        self.val(n.inputs[2], c)
        return n.outputs[0]

    def vmath(self, op, a, b=(0.0, 0.0, 0.0), scale=1.0):
        n = self.node('ShaderNodeVectorMath', operation=op)
        self.val(n.inputs[0], a)
        self.val(n.inputs[1], b)
        self.val(n.inputs[3], scale)
        return n.outputs[1] if op in ('LENGTH', 'DOT_PRODUCT', 'DISTANCE') else n.outputs[0]

    def xyz(self, v):
        n = self.node('ShaderNodeSeparateXYZ')
        self.val(n.inputs[0], v)
        return n.outputs[0], n.outputs[1], n.outputs[2]

    def comb(self, x, y, z):
        n = self.node('ShaderNodeCombineXYZ')
        for i, c in enumerate((x, y, z)):
            self.val(n.inputs[i], c)
        return n.outputs[0]

    def mix(self, fac, a, b, blend='MIX'):
        n = self.node('ShaderNodeMix', data_type='RGBA', blend_type=blend)
        self.val(n.inputs[0], fac)
        self.val(n.inputs[6], a)
        self.val(n.inputs[7], b)
        return n.outputs[2]

    def mixf(self, fac, a, b):
        n = self.node('ShaderNodeMix', data_type='FLOAT')
        self.val(n.inputs[0], fac)
        self.val(n.inputs[2], a)
        self.val(n.inputs[3], b)
        return n.outputs[0]

    def noise(self, vec, scale, detail=4.0, rough=0.5, dist=0.0):
        n = self.node('ShaderNodeTexNoise')
        self.val(n.inputs['Vector'], vec)
        n.inputs['Scale'].default_value = scale
        n.inputs['Detail'].default_value = detail
        n.inputs['Roughness'].default_value = rough
        n.inputs['Distortion'].default_value = dist
        return n.outputs['Fac']

    def voronoi(self, vec, scale, feature='F1', rand=1.0, out='Distance'):
        n = self.node('ShaderNodeTexVoronoi', feature=feature)
        self.val(n.inputs['Vector'], vec)
        n.inputs['Scale'].default_value = scale
        n.inputs['Randomness'].default_value = rand
        return n.outputs[out]

    def wave(self, vec, scale, axis='X', profile='SIN', distortion=0.0):
        n = self.node('ShaderNodeTexWave', wave_type='BANDS', bands_direction=axis, wave_profile=profile)
        self.val(n.inputs['Vector'], vec)
        n.inputs['Scale'].default_value = scale
        n.inputs['Distortion'].default_value = distortion
        return n.outputs['Fac']

    def checker(self, vec, scale):
        n = self.node('ShaderNodeTexChecker')
        self.val(n.inputs['Vector'], vec)
        n.inputs['Scale'].default_value = scale
        n.inputs['Color1'].default_value = (1, 1, 1, 1)
        n.inputs['Color2'].default_value = (0, 0, 0, 1)
        return n.outputs['Fac']

    def mr(self, v, a0, a1, b0=0.0, b1=1.0, smooth=False):
        n = self.node('ShaderNodeMapRange', clamp=True, interpolation_type='SMOOTHSTEP' if smooth else 'LINEAR')
        self.val(n.inputs[0], v)
        n.inputs[1].default_value, n.inputs[2].default_value = a0, a1
        n.inputs[3].default_value, n.inputs[4].default_value = b0, b1
        return n.outputs[0]

    def ramp(self, fac, stops):
        n = self.node('ShaderNodeValToRGB')
        el = n.color_ramp.elements
        el[0].position, el[0].color = stops[0][0], (*stops[0][1], 1.0)
        el[1].position, el[1].color = stops[-1][0], (*stops[-1][1], 1.0)
        for p, c in stops[1:-1]:
            e = el.new(p)
            e.color = (*c, 1.0)
        self.val(n.inputs['Fac'], fac)
        return n.outputs['Color']

    def bump(self, height, strength, distance, normal=None):
        n = self.node('ShaderNodeBump')
        self.val(n.inputs['Height'], height)
        n.inputs['Strength'].default_value = strength
        n.inputs['Distance'].default_value = distance
        if normal is not None:
            self.nt.links.new(normal, n.inputs['Normal'])
        return n.outputs['Normal']

    def ao(self, dist, only_local=True):
        n = self.node('ShaderNodeAmbientOcclusion', only_local=only_local, samples=16)
        n.inputs['Distance'].default_value = dist
        return n.outputs['AO']

    def attr(self, name):
        n = self.node('ShaderNodeAttribute', attribute_name=name)
        return n.outputs['Color']

    def uv(self, name):
        n = self.node('ShaderNodeUVMap', uv_map=name)
        return n.outputs['UV']

    def bsdf(self, inputs):
        n = self.node('ShaderNodeBsdfPrincipled')
        for k, v in inputs.items():
            self.val(n.inputs[k], v)
        self.nt.links.new(n.outputs[0], self.out.inputs['Surface'])
        return n


def flat_material(name, color, rough=0.6, metal=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = (*color, 1.0)
    b.inputs['Roughness'].default_value = rough
    b.inputs['Metallic'].default_value = metal
    return m


def assign(ob, *mats):
    ob.data.materials.clear()
    for m in mats:
        ob.data.materials.append(m)


# ----------------------------------------------------------------------------- preview render
def setup_cycles(samples=64, res=(1000, 1000), engine='CYCLES'):
    sc = bpy.context.scene
    sc.render.engine = engine
    if engine == 'CYCLES':
        prefs = bpy.context.preferences.addons['cycles'].preferences
        prefs.compute_device_type = 'OPTIX'
        prefs.refresh_devices()
        for d in prefs.devices:
            d.use = d.type == 'OPTIX'
        sc.cycles.device = 'GPU'
        sc.cycles.samples = samples
        sc.cycles.use_denoising = True
        sc.cycles.denoiser = 'OPTIX'
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.resolution_percentage = 100
    sc.render.image_settings.file_format = 'PNG'
    sc.view_settings.view_transform = 'AgX'
    sc.view_settings.look = 'AgX - Medium High Contrast'


def look_at(ob, target):
    d = Vector(target) - ob.location
    ob.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()


def add_camera(loc, target, lens=50):
    cam = bpy.data.objects.get('camera')
    if cam is None:
        cam = bpy.data.objects.new('camera', bpy.data.cameras.new('camera'))
        link(cam, 'scena')
    cam.location = loc
    cam.data.lens = lens
    look_at(cam, target)
    bpy.context.scene.camera = cam
    return cam


def add_area(name, loc, target, power, size, color=(1, 1, 1)):
    l = bpy.data.lights.new(name, 'AREA')
    l.energy, l.size, l.color = power, size, color
    ob = bpy.data.objects.new(name, l)
    ob.location = loc
    look_at(ob, target)
    link(ob, 'scena')
    return ob


def studio(floor_color=(0.12, 0.12, 0.13), bg=(0.035, 0.035, 0.04), size=6.0):
    """Floor, world colour and a three-light rig (key, fill, rim) around a figure at the origin."""
    sc = bpy.context.scene
    w = bpy.data.worlds.new('w')
    sc.world = w
    w.use_nodes = True
    w.node_tree.nodes['Background'].inputs['Color'].default_value = (*bg, 1)
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=size)
    fl = bm_obj(bm, 'floor', 'scena')
    fl.data.materials.append(flat_material('floor', floor_color, 0.8))
    add_area('key', (-2.0, -2.6, 2.7), (0, 0, 1.0), 300, 1.6, (1.0, 0.93, 0.86))
    add_area('fill', (2.4, -1.8, 1.6), (0, 0, 1.0), 90, 2.2, (0.82, 0.9, 1.0))
    add_area('rim', (0.9, 2.4, 2.4), (0, 0, 1.3), 220, 1.2, (1.0, 0.96, 0.9))


# ----------------------------------------------------------------------------- bake
def make_bake_uv(ob, margin=0.004, angle=66.0):
    me = ob.data
    uv = me.uv_layers.new(name='bake')
    me.uv_layers.active = uv
    uv.active_render = True
    select_only([ob])
    bpy.context.scene.tool_settings.use_uv_select_sync = True
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=radians(angle), island_margin=margin)
    bpy.ops.uv.pack_islands(margin=margin, rotate=True)
    bpy.ops.object.mode_set(mode='OBJECT')


def bake_set(prefix, name, ob, res, tex_dir, high=None, ext=0.006, ray=0.02, with_color=True, with_normal=True):
    """Bake base colour, roughness and tangent normal onto 'ob'. With 'high', details are projected from it."""
    os.makedirs(tex_dir, exist_ok=True)
    mats = [m for m in ob.data.materials if m]
    imgs = {}
    s2a = {'use_selected_to_active': True, 'cage_extrusion': ext, 'max_ray_distance': ray} if high else {}
    passes = []
    if with_color:
        passes.append(('base', 'DIFFUSE', 'sRGB', {'pass_filter': {'COLOR'}, **s2a}))
    passes.append(('rough', 'ROUGHNESS', 'Non-Color', dict(s2a)))
    if with_normal:
        passes.append(('normal', 'NORMAL', 'Non-Color', {'normal_space': 'TANGENT', **s2a}))
    select_only([high, ob] if high else [ob], ob)
    for kind, btype, cs, kw in passes:
        img = bpy.data.images.new(f'{prefix}_{name}_{kind}', res, res, alpha=False)
        img.colorspace_settings.name = cs
        for m in mats:
            nt = m.node_tree
            n = nt.nodes.get('__bake__') or nt.nodes.new('ShaderNodeTexImage')
            n.name = '__bake__'
            n.image = img
            nt.nodes.active = n
        t = time.time()
        bpy.ops.object.bake(type=btype, margin=16, use_clear=True, **kw)
        ext_ = 'jpg' if kind in ('base', 'normal') else 'png'
        img.filepath_raw = os.path.join(tex_dir, f'{prefix}_{name}_{kind}.{ext_}')
        img.file_format = 'JPEG' if ext_ == 'jpg' else 'PNG'
        if ext_ == 'jpg':
            bpy.context.scene.render.image_settings.quality = 92
        img.save()
        imgs[kind] = img
        print(f'[bake] {name}/{kind} {res}px in {time.time() - t:.1f}s')
    for m in mats:
        n = m.node_tree.nodes.get('__bake__')
        if n:
            m.node_tree.nodes.remove(n)
    return imgs


def unity_mask(prefix, name, rough_img, metallic, tex_dir):
    """RGBA mask for URP Lit: R = metallic, A = smoothness (1 - roughness)."""
    import numpy as np
    w, h = rough_img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    rough_img.pixels.foreach_get(px)
    px = px.reshape(-1, 4)
    out = np.zeros_like(px)
    out[:, 0] = metallic
    out[:, 3] = 1.0 - px[:, 0]
    img = bpy.data.images.new(f'{prefix}_{name}_mask', w, h, alpha=True)
    img.colorspace_settings.name = 'Non-Color'
    img.pixels.foreach_set(out.ravel())
    img.filepath_raw = os.path.join(tex_dir, f'{prefix}_{name}_mask.png')
    img.file_format = 'PNG'
    img.save()
    bpy.data.images.remove(img)
