"""
Shared geometry and helpers of the track-mode models (the rider and the bikes), Blender 4.5 run headless.

Axes (the same as the weapons): x right, y FORWARD, z up, metres. The bike stands on z = 0 with the middle of its wheelbase at y = 0. The FBX
goes out with forward -Z and up Y and comes in with 'bake axis conversion', so Unity gets x right, y up, z forward: the Unity position of a point
(x, y, z) of this file is (x, z, y).

Every constant of the bike that the game also knows (the axles, the steering head, the swing arm pivot, the grips, the pegs) lives here and in
PistaBikeMesh.cs (Scripts/Pista): they must say the same thing. docs/engineering/track-models.md explains the pipeline.
"""
import bpy, bmesh, math, os, sys, time, importlib.util
from math import sin, cos, pi, radians, sqrt, atan2, exp, acos, degrees
from mathutils import Vector, Matrix, Quaternion, Euler
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
sys.path.insert(0, os.path.normpath(os.path.join(HERE, '..', 'personaggi')))
from kit import (V, clamp, lerp, smoothstep, reset, link, bm_obj, join_objects, select_only, apply_all, organic, bm_ellipsoid, bm_sphere,  # noqa: E402,F401
                 bm_capsule, bm_chain, flat_material, assign, setup_cycles, add_camera, add_area, save_blend, look_at)

OUT_DIR = os.path.normpath(os.path.join(HERE, '..', '..', 'unity', 'PryingEyes', 'Assets', '_PryingEyes', 'Resources', 'Moto'))
RENDER_DIR = os.path.join(HERE, 'render')
WORK_DIR = os.path.join(HERE, 'work')


def load_armikit():
    """The hard-surface kit of the weapons (box, prism, lathe, loft, sweep, rod, boolean cuts), with the weapon offset switched off."""
    spec = importlib.util.spec_from_file_location('armikit', os.path.join(HERE, '..', 'armi', 'kit.py'))
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    m.OFFSET = Vector((0.0, 0.0, 0.0))
    return m


# ----------------------------------------------------------------------------- what the bike and the game agree on
R_WHEEL = 0.31
WHEELBASE = 1.40
AXLE_F = V(0.0, WHEELBASE / 2, R_WHEEL)
AXLE_R = V(0.0, -WHEELBASE / 2, R_WHEEL)
RAKE = radians(24.0)
HEAD = AXLE_F + V(0.0, -sin(RAKE) * 0.64, cos(RAKE) * 0.64)            # the steering head: (0, 0.440, 0.895)
STEER_AXIS = (HEAD - AXLE_F).normalized()                                # from the front axle up to the head
SWING_PIVOT = V(0.0, -0.30, 0.45)
GRIP_FAIRING = V(0.30, 0.40, 0.905)                                       # the right grip of a bike with clip-ons (the left is mirrored)
GRIP_NAKED = V(0.30, 0.34, 1.00)
PEG = V(0.24, -0.22, 0.40)                                                # the right foot peg (the ball of the foot)


def mirror(v):
    return V(-v.x, v.y, v.z)


def two_bone(root, target, l1, l2, pole):
    """The middle joint of a two-bone chain from root to target: (mid, end). The joint bends toward 'pole'. Out of reach, the chain straightens."""
    d = target - root
    dist = clamp(d.length, abs(l1 - l2) + 1e-4, l1 + l2 - 1e-4)
    dirn = d.normalized()
    a = (l1 * l1 - l2 * l2 + dist * dist) / (2 * dist)
    h = sqrt(max(l1 * l1 - a * a, 0.0))
    p = pole - dirn * pole.dot(dirn)
    p = p.normalized() if p.length > 1e-6 else V(0, 0, 1)
    return root + dirn * a + p * h, root + dirn * dist


# ----------------------------------------------------------------------------- curves and tubes
def catmull(pts, seg=8):
    """Catmull-Rom through the points: (positions, parameter) where the parameter is the control index plus the fraction (0..len-1)."""
    P = [Vector(p) for p in pts]
    if len(P) < 3:
        out = [P[0].lerp(P[-1], k / seg) for k in range(seg + 1)]
        return out, [k / seg * (len(P) - 1) for k in range(seg + 1)]
    Q = [P[0] * 2 - P[1]] + P + [P[-1] * 2 - P[-2]]
    out, par = [], []
    for i in range(1, len(Q) - 2):
        p0, p1, p2, p3 = Q[i - 1], Q[i], Q[i + 1], Q[i + 2]
        for k in range(seg):
            t = k / seg
            out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3))
            par.append(i - 1 + t)
    out.append(P[-1])
    par.append(len(P) - 1.0)
    return out, par


def interp(vals, par):
    """A value per control point, interpolated smoothly at a parameter."""
    i = min(int(par), len(vals) - 2)
    t = par - i
    t = t * t * (3 - 2 * t)
    return vals[i] + (vals[i + 1] - vals[i]) * t


def tube(pts, radii, sides=22, ratio=None, ref=None, seg=8, split=None, cap=(True, True), name='tube'):
    """A swept tube through control points. radii: one per point; ratio: the flattening (the second axis over the first) per point.
    ref: the direction of the first axis at the start (the u = 0 meridian of the UVs: u runs round the tube, v along it, 0..1 per part).
    split: the index of a control point where the tube is cut in two parts (the elbow, the knee): the ring there is duplicated, the faces
    before it have material 0 and the ones after it material 1, and v restarts at 0 (the shader paints the two parts by their own v).
    Returns the bmesh (UV layer 'UVMap')."""
    path, par = catmull(pts, seg)
    N = len(path)
    ratio = ratio or [1.0] * len(pts)
    tang = []
    for i in range(N):
        a, b = path[max(i - 1, 0)], path[min(i + 1, N - 1)]
        tang.append((b - a).normalized())
    n0 = Vector(ref) if ref is not None else V(1, 0, 0)
    n0 = n0 - tang[0] * n0.dot(tang[0])
    if n0.length < 1e-6:
        n0 = V(0, 0, 1) - tang[0] * tang[0].z
    n0.normalize()
    normals = [n0]
    for i in range(1, N):
        n = normals[-1] - tang[i] * normals[-1].dot(tang[i])
        normals.append(n.normalized())
    arc = [0.0]
    for i in range(1, N):
        arc.append(arc[-1] + (path[i] - path[i - 1]).length)
    cut = None
    if split is not None:
        cut = min(range(N), key=lambda i: abs(par[i] - split))
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new('UVMap')
    rings, rmeta = [], []
    for i in range(N):
        rad = interp(radii, par[i])
        rat = interp(ratio, par[i])
        b = tang[i].cross(normals[i])
        ring = []
        for j in range(sides):
            th = 2 * pi * j / sides
            ring.append(bm.verts.new(path[i] + normals[i] * (cos(th) * rad) + b * (sin(th) * rad * rat)))
        rings.append(ring)
        rmeta.append(i)
        if cut is not None and i == cut:           # the duplicate ring: the second part starts here
            ring2 = [bm.verts.new(v.co.copy()) for v in ring]
            rings.append(ring2)
            rmeta.append(i)
    # arc length of each part
    first_len = arc[cut] if cut is not None else arc[-1]
    second_len = (arc[-1] - arc[cut]) if cut is not None else 1.0
    cut_k = None
    if cut is not None:
        cut_k = [k for k, i in enumerate(rmeta) if i == cut][0]       # the ring of the first part at the cut; cut_k + 1 is the duplicate
    for k in range(len(rings) - 1):
        if cut_k is not None and k == cut_k:
            continue                                                    # no faces between a ring and its duplicate
        A, B = rings[k], rings[k + 1]
        second = cut_k is not None and k > cut_k
        for j in range(sides):
            f = bm.faces.new((A[j], A[(j + 1) % sides], B[(j + 1) % sides], B[j]))
            f.smooth = True
            f.material_index = 1 if second else 0
            for L, kk, jj in zip(f.loops, (k, k, k + 1, k + 1), (j, j + 1, j + 1, j)):
                i = rmeta[kk]
                if cut_k is None:
                    vv = arc[i] / max(arc[-1], 1e-9)
                elif kk <= cut_k:
                    vv = arc[i] / max(first_len, 1e-9)
                else:
                    vv = (arc[i] - arc[cut]) / max(second_len, 1e-9)
                L[uv].uv = (jj / sides, vv)
    if cap[0]:
        f = bm.faces.new(rings[0][::-1])
        f.smooth = True
    if cap[1]:
        f = bm.faces.new(rings[-1])
        f.smooth = True
        if cut_k is not None:
            f.material_index = 1
    return bm


def loft3d(rings, closed=True, cap_start=False, cap_end=False, mat=0, smooth=True, bm=None):
    """Quads between rings of points (every ring the same number of points). A ring that runs counter-clockwise seen from the side the loft
    advances toward gives faces that look outward. With 'bm' the loft is added to that bmesh."""
    bm = bm if bm is not None else bmesh.new()
    vr = [[bm.verts.new(p) for p in ring] for ring in rings]
    n = len(vr[0])
    for A, B in zip(vr[:-1], vr[1:]):
        for j in (range(n) if closed else range(n - 1)):
            f = bm.faces.new((A[j], A[(j + 1) % n], B[(j + 1) % n], B[j]))
            f.smooth = smooth
            f.material_index = mat
    if cap_start:
        f = bm.faces.new(vr[0][::-1])
        f.smooth = smooth
        f.material_index = mat
    if cap_end:
        f = bm.faces.new(vr[-1])
        f.smooth = smooth
        f.material_index = mat
    return bm


def orient_outward(bm, centre):
    """Flip every face together when most of them look toward 'centre' (open patches, which recalc_face_normals cannot judge)."""
    bm.faces.ensure_lookup_table()
    bm.normal_update()                       # the normals of faces made by hand are zero until this: every patch would have been flipped
    score = sum(1 if f.normal.dot(f.calc_center_median() - centre) > 0 else -1 for f in bm.faces)
    if score < 0:
        bmesh.ops.reverse_faces(bm, faces=list(bm.faces))


def superellipse(a, b, p, n, cx=0.0, cy=0.0):
    """n points of |x/a|^p + |y/b|^p = 1, counter-clockwise from +x."""
    out = []
    e = 2.0 / p
    for j in range(n):
        th = 2 * pi * j / n
        c, s = cos(th), sin(th)
        out.append((cx + a * math.copysign(abs(c) ** e, c), cy + b * math.copysign(abs(s) ** e, s)))
    return out


def new_object(bm, name, mats=(), collection=None):
    ob = bm_obj(bm, name, collection)
    for m in mats:
        ob.data.materials.append(m)
    return ob


# ----------------------------------------------------------------------------- finishing
def smooth_object(ob, angle=50.0):
    """Smooth shading with sharp edges beyond an angle (Blender 4.5: per-edge smooth flag)."""
    me = ob.data
    for p in me.polygons:
        p.use_smooth = True
    bm = bmesh.new()
    bm.from_mesh(me)
    for e in bm.edges:
        if len(e.link_faces) == 2:
            try:
                e.smooth = e.calc_face_angle(0.0) < radians(angle)
            except ValueError:
                e.smooth = True
    bm.to_mesh(me)
    bm.free()


def decimate(ob, tris):
    n = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    if n <= tris:
        return n
    m = ob.modifiers.new('dec', 'DECIMATE')
    m.decimate_type = 'COLLAPSE'
    m.ratio = tris / n
    m.use_collapse_triangulate = True
    apply_all(ob)
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)


def bake_transform(ob):
    ob.data.transform(ob.matrix_world)
    ob.matrix_world = Matrix.Identity(4)


def tri_count(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)


# ----------------------------------------------------------------------------- ambient occlusion in the vertex colours
def hemisphere(n):
    """n directions over the upper hemisphere (z up), cosine weighted, evenly spread."""
    out = []
    ga = pi * (3 - sqrt(5))
    for i in range(n):
        r = sqrt((i + 0.5) / n)
        th = i * ga
        out.append(Vector((r * cos(th), r * sin(th), sqrt(max(0.0, 1 - r * r)))))
    return out


def bake_ao(objects, targets=None, samples=48, dist=0.16, bias=0.003, strength=0.9, floor=0.0):
    """Occlusion of every vertex of 'targets' (default: every object) by every object of the list, written in the colour attribute 'Col'
    (float, r = g = b = the light that gets in, 1 = open). The shader of the game multiplies the ambient light with it."""
    verts, polys = [], []
    for ob in objects:
        base = len(verts)
        mw = ob.matrix_world
        verts.extend([mw @ v.co for v in ob.data.vertices])
        for p in ob.data.polygons:
            polys.append(tuple(base + i for i in p.vertices))
    tree = BVHTree.FromPolygons(verts, polys)
    dirs = hemisphere(samples)
    t0 = time.time()
    count = 0
    for ob in (targets or objects):
        me = ob.data
        mw = ob.matrix_world
        mn = mw.to_3x3()
        if 'Col' in me.color_attributes:
            me.color_attributes.remove(me.color_attributes['Col'])
        ca = me.color_attributes.new('Col', 'FLOAT_COLOR', 'POINT')
        vals = []
        for v in me.vertices:
            p = mw @ v.co
            n = (mn @ v.normal).normalized()
            t = n.cross(V(0, 0, 1) if abs(n.z) < 0.9 else V(1, 0, 0)).normalized()
            b = n.cross(t)
            hit = 0.0
            for d in dirs:
                w = t * d.x + b * d.y + n * d.z
                r = tree.ray_cast(p + n * bias, w, dist)
                if r[0] is not None:
                    hit += 1.0 - clamp(r[3] / dist) * 0.5            # a near hit counts more than a far one
            ao = clamp(1.0 - strength * hit / samples, floor, 1.0)
            vals.append(ao)
            count += 1
        for i, a in enumerate(vals):
            ca.data[i].color = (a, a, a, 1.0)
    print('[ao] %d vertices in %.1fs' % (count, time.time() - t0))


# ----------------------------------------------------------------------------- materials for the previews (the game has its own)
def preview_material(name, color, rough=0.5, metal=0.0, coat=0.0, emit=None):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = (*color, 1.0)
    b.inputs['Roughness'].default_value = rough
    b.inputs['Metallic'].default_value = metal
    if coat > 0:
        b.inputs['Coat Weight'].default_value = coat
        b.inputs['Coat Roughness'].default_value = 0.05
    if emit is not None:
        b.inputs['Emission Color'].default_value = (*emit, 1.0)
        b.inputs['Emission Strength'].default_value = 4.0
    return m


def export_fbx(path, objs, smooth='FACE'):
    """smooth: 'FACE' (the smoothing of every face, as the organic models are made) or 'EDGE' (the sharp edges that the hard-surface kit marks)."""
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'EMPTY', 'MESH'}, apply_unit_scale=True,
                             apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
                             mesh_smooth_type=smooth, use_tspace=False, add_leaf_bones=False, bake_anim=False,
                             path_mode='STRIP', embed_textures=False, colors_type='LINEAR')
    sys.path.insert(0, os.path.normpath(os.path.join(HERE, '..', 'personaggi')))
    import scrub_fbx             # the exporter writes the absolute path of the .blend into the file: the repository is public
    scrub_fbx.RELATIVE = b'modelli/moto/work/'
    scrub_fbx.scrub(path)
    print('[export] fbx', path)
