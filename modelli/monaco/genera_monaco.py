"""
Monaco dall'occhio: generatore procedurale per Blender 4.5.

Uso (dalla cartella del file):
  blender -b -P genera_monaco.py -- geo       geometria + simulazioni stoffa  -> monaco_geo.blend
  blender -b -P genera_monaco.py -- mat       materiali procedurali            -> monaco.blend
  blender -b -P genera_monaco.py -- render    anteprime Cycles                 -> render/
  blender -b -P genera_monaco.py -- export    bake texture, versione da gioco  -> export/monaco_export.blend
  blender -b -P genera_monaco.py -- rig       scheletro, pesi, animazioni      -> export/monaco.fbx, monaco.glb
  blender -b -P genera_monaco.py -- pose      fotogrammi chiave delle animazioni -> render/pose/
Opzioni: nocloth (salta le simulazioni), clay (render in creta).

Assi: Z in alto, il monaco guarda verso -Y, la sua destra e' -X. Unita': metri.
"""
import bpy, bmesh, math, random, sys, os, time
from mathutils import Vector, Matrix, Euler, noise, kdtree
from math import sin, cos, pi, radians, sqrt, exp, atan2

DIR = os.path.dirname(os.path.abspath(__file__))
GEO_BLEND = os.path.join(DIR, 'monaco_geo.blend')
MAT_BLEND = os.path.join(DIR, 'monaco.blend')
RENDER_DIR = os.path.join(DIR, 'render')
EXPORT_DIR = os.path.join(DIR, 'export')
ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []


def V(*a):
    return Vector(a)


# ----------------------------------------------------------------------------- posa
EYE_C = V(0.0, -0.205, 1.525)
EYE_R = 0.085
EYE_TILT = 8.0                      # gradi verso il basso
HEAD_C = V(0.0, -0.09, 1.44)
HEAD_R = (0.105, 0.12, 0.15)
J = {
    'r_sh': V(-0.19, 0.0, 1.245), 'r_el': V(-0.245, -0.03, 1.00), 'r_wr': V(-0.215, -0.12, 0.77),
    'l_sh': V(0.19, 0.0, 1.245), 'l_el': V(0.265, -0.04, 1.00), 'l_wr': V(0.30, -0.285, 1.02),
}
STAFF_DIR = V(-0.03, 0.05, 1.0).normalized()
STAFF_TOP = 1.48

# dita della mano destra canonica: pollice verso -x, dita verso +y, dorso verso +z
FINGERS = [
    # nome, nocca, apertura (gradi), falangi (m), raggi (mcp, pip, dip, punta)
    ('indice',  (-0.026, 0.086, 0.002), -6, (0.049, 0.029, 0.0235), (0.0100, 0.0092, 0.0079, 0.0067)),
    ('medio',   (-0.006, 0.091, 0.003),  0, (0.055, 0.0335, 0.0245), (0.0103, 0.0094, 0.0081, 0.0069)),
    ('anulare', (0.014, 0.087, 0.002),   5, (0.050, 0.031, 0.0235), (0.0097, 0.0089, 0.0076, 0.0064)),
    ('mignolo', (0.031, 0.077, -0.002), 11, (0.039, 0.0235, 0.021), (0.0086, 0.0078, 0.0067, 0.0057)),
]
CURL = {
    'rilassata': [(20, 28, 14), (22, 32, 16), (26, 36, 18), (30, 40, 20)],
    'presa':     [(66, 92, 42), (70, 92, 44), (74, 92, 44), (78, 90, 44)],
}
SPREAD_PRESA = [-2, 0, 2, 5]
THUMB_R = (0.0135, 0.0115, 0.0101, 0.0084)


# ----------------------------------------------------------------------------- utilita'
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
    bpy.context.preferences.filepaths.save_version = 0   # niente copie .blend1
    bpy.ops.wm.save_as_mainfile(filepath=path)


def apply_all(ob):
    """Applica tutti i modificatori (anche la stoffa al frame corrente) senza operatori."""
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


def bm_torus(bm, center, axis, R, r, seg=64, rseg=16, jitter=0.0, seed=0.0):
    axis = Vector(axis).normalized()
    Mr = axis.to_track_quat('Z', 'Y').to_matrix()
    grid = []
    for i in range(seg):
        a = 2 * pi * i / seg
        ring = []
        for j in range(rseg):
            b = 2 * pi * j / rseg
            rr = r * (1 + jitter * noise.noise(V(cos(a) * 2.3, sin(a) * 2.3, seed)))
            p = V((R + rr * cos(b)) * cos(a), (R + rr * cos(b)) * sin(a), rr * sin(b))
            ring.append(bm.verts.new(Vector(center) + Mr @ p))
        grid.append(ring)
    for i in range(seg):
        for j in range(rseg):
            bm.faces.new((grid[i][j], grid[(i + 1) % seg][j], grid[(i + 1) % seg][(j + 1) % rseg],
                          grid[i][(j + 1) % rseg]))


def organic(name, fill, voxel, sm_iter=6, sm_fac=0.5, coll=None):
    """Unione di primitive sovrapposte -> remesh a voxel -> smussatura."""
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


def make_collider(ob, outer=0.004, inner=0.02, friction=5.0):
    if 'Collision' not in [m.type for m in ob.modifiers]:
        ob.modifiers.new('Collision', 'COLLISION')
    ob.collision.thickness_outer = outer
    ob.collision.thickness_inner = inner
    ob.collision.cloth_friction = friction


def remove_collider(ob):
    for m in list(ob.modifiers):
        if m.type == 'COLLISION':
            ob.modifiers.remove(m)


def smoothstep(e0, e1, x):
    t = min(1.0, max(0.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


# ----------------------------------------------------------------------------- corpo
def build_body():
    def fill(bm):
        bm_ellipsoid(bm, (0, 0.03, 0.85), (0.14, 0.11, 0.10))
        bm_ellipsoid(bm, (0, 0.02, 0.99), (0.12, 0.095, 0.11))
        bm_ellipsoid(bm, (0, 0.0, 1.13), (0.13, 0.10, 0.12))
        bm_capsule(bm, (-0.19, 0, 1.245), (0.19, 0, 1.245), 0.048, 0.048)
        bm_capsule(bm, (0, -0.02, 1.22), (0, -0.07, 1.34), 0.046, 0.042)
        for s in ('r', 'l'):
            bm_capsule(bm, J[s + '_sh'], J[s + '_el'], 0.044, 0.036)
            bm_capsule(bm, J[s + '_el'], J[s + '_wr'], 0.033, 0.024)
            sx = -1 if s == 'r' else 1
            hip, kn = V(0.10 * sx, 0.03, 0.82), V(0.11 * sx, -0.01, 0.46)
            an, toe = V(0.12 * sx, 0.03, 0.08), V(0.13 * sx, -0.10, 0.03)
            bm_capsule(bm, hip, kn, 0.075, 0.05)
            bm_capsule(bm, kn, an, 0.048, 0.035)
            bm_capsule(bm, an, toe, 0.035, 0.028)
    return organic('corpo', fill, voxel=0.01, sm_iter=6)


# ----------------------------------------------------------------------------- testa e occhio
def build_head():
    ax = V(0, -cos(radians(EYE_TILT)), -sin(radians(EYE_TILT)))

    def fill(bm):
        bm_ellipsoid(bm, HEAD_C, HEAD_R, seg=40)
        bm_capsule(bm, HEAD_C + V(0, 0.01, -0.10), V(0, -0.05, 1.27), 0.07, 0.05, seg=20)
        # orbita carnosa attorno all'occhio
        bm_torus(bm, EYE_C + ax * 0.012, ax, 0.080, 0.019, seg=72, rseg=16, jitter=0.3, seed=2.0)
        # zigomi e pieghe sotto l'occhio
        rnd = random.Random(11)
        for i in range(10):
            a = radians(200 + 140 * i / 9)
            p = EYE_C + ax * 0.0 + V(cos(a) * 0.095, 0, sin(a) * 0.095) + V(0, 0.02, 0)
            bm_sphere(bm, p, 0.018 + 0.01 * rnd.random(), seg=12)
    ob = organic('testa', fill, voxel=0.003, sm_iter=4)
    ob.data.shade_smooth()
    return ob


def build_eye():
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new('UVMap')
    bmesh.ops.create_uvsphere(bm, u_segments=72, v_segments=40, radius=EYE_R)
    for f in bm.faces:
        for lo in f.loops:
            co = lo.vert.co
            lo[uvl].uv = (0.5 + 0.5 * co.x / EYE_R, 0.5 + 0.5 * co.y / EYE_R)
    ob = bm_obj(bm, 'occhio')
    ob.data.shade_smooth()
    ob.location = EYE_C
    ob.rotation_euler = (radians(90 + EYE_TILT), 0, 0)   # +Z locale -> davanti, un po' in basso
    return ob


def build_orb(head):
    # piccolo occhio luminoso blu sotto l'occhio grande, sul lato destro del monaco
    x, z, r = -0.052, 1.385, 0.0115
    ok, loc, nor, idx = head.ray_cast(V(x, -0.6, z), V(0, 1, 0))
    y = loc.y if ok else -0.19
    bm = bmesh.new()
    bm_sphere(bm, (x, y - r * 0.35, z), r, seg=24)
    ob = bm_obj(bm, 'occhietto')
    ob.data.shade_smooth()
    return ob


# ----------------------------------------------------------------------------- mani
def finger_chain(knuckle, spread, lens, curls):
    a = radians(spread)
    phi = 0.0
    pts = [Vector(knuckle)]
    dirs = []
    for L, th in zip(lens, curls):
        phi += radians(th)
        d = V(sin(a) * cos(phi), cos(a) * cos(phi), -sin(phi))
        dirs.append((d, phi))
        pts.append(pts[-1] + d * L)
    return pts, dirs, a


def seg_dist_yz(p, q, c):
    py, pz, qy, qz = p.y, p.z, q.y, q.z
    dy, dz = qy - py, qz - pz
    L2 = dy * dy + dz * dz
    t = 0.0 if L2 < 1e-12 else max(0.0, min(1.0, ((c[0] - py) * dy + (c[1] - pz) * dz) / L2))
    return sqrt((py + dy * t - c[0]) ** 2 + (pz + dz * t - c[1]) ** 2)


GRIP_RS = 0.018
GRIP_C = (0.072, -0.0145 - GRIP_RS + 0.003)   # (y, z): il bastone tocca il palmo


def wrap_curls(knuckle, spread, lens, radii, maxc=(100, 115, 85)):
    """Piega ogni falange finche' non tocca il bastone (cilindro lungo x)."""
    a = radians(spread)
    phi = 0.0
    p = Vector(knuckle)
    curls = []
    for i, L in enumerate(lens):
        rr = max(radii[i], radii[i + 1]) + 0.0008
        found = maxc[i]
        for step in range(0, maxc[i] + 1):
            ph = phi + radians(step)
            d = V(sin(a) * cos(ph), cos(a) * cos(ph), -sin(ph))
            if seg_dist_yz(p + d * (L * 0.3), p + d * L, GRIP_C) <= GRIP_RS + rr:
                found = step
                break
        phi += radians(found)
        curls.append(found)
        p = p + V(sin(a) * cos(phi), cos(a) * cos(phi), -sin(phi)) * L
    return tuple(curls)


def grip_setup():
    CURL['presa'] = [wrap_curls(k, sp, lens, radii)
                     for (nm, k, sp0, lens, radii), sp in zip(FINGERS, SPREAD_PRESA)]
    print('[presa] pieghe dita:', CURL['presa'])
    return (GRIP_RS, GRIP_C[0], GRIP_C[1])


def thumb_joints(pose, grip):
    cmc = V(-0.020, 0.014, -0.011)
    if pose == 'rilassata':
        d1 = V(-0.50, 0.78, -0.38).normalized()
        d2 = V(-0.22, 0.86, -0.46).normalized()
        d3 = V(-0.08, 0.86, -0.50).normalized()
        mcp = cmc + d1 * 0.046
        ip = mcp + d2 * 0.035
        tip = ip + d3 * 0.029
        up = (V(-0.75, 0.0, 0.66) - d3 * d3.dot(V(-0.75, 0.0, 0.66))).normalized()
        return [cmc, mcp, ip, tip], up
    Rs, yc, zc = grip
    al = atan2(cmc.z - zc, cmc.y - yc)
    ca, sa = cos(al), sin(al)
    mcp = V(-0.040, yc + (Rs + THUMB_R[1] + 0.004) * ca, zc + (Rs + THUMB_R[1] + 0.004) * sa)
    ip = V(-0.067, yc + (Rs + THUMB_R[2] + 0.001) * ca, zc + (Rs + THUMB_R[2] + 0.001) * sa)
    tip = V(-0.092, yc + (Rs + THUMB_R[3]) * ca, zc + (Rs + THUMB_R[3]) * sa)
    return [cmc, mcp, ip, tip], V(0, ca, sa)


def hand_frame(side):
    if side == 'r':
        W = J['r_wr']
        F = (J['r_wr'] - J['r_el']).normalized()
        fwd = V(0, -1, 0)
        L = (fwd - F * fwd.dot(F)).normalized()      # lato del pollice
        X, Y = -L, F
        Z = X.cross(Y)
        R = Matrix((X, Y, Z)).transposed().to_4x4()
        return Matrix.Translation(W) @ R, False
    W = J['l_wr']
    Farm = (J['l_wr'] - J['l_el']).normalized()
    F = (Farm - STAFF_DIR * Farm.dot(STAFF_DIR)).normalized()
    X, Y = STAFF_DIR.copy(), F                       # pollice in su, lungo il bastone
    Z = X.cross(Y)
    R = Matrix((X, Y, Z)).transposed().to_4x4()
    return Matrix.Translation(W) @ R @ Matrix.Diagonal((-1, 1, 1, 1)), True


def build_hand(side, grip):
    pose = 'rilassata' if side == 'r' else 'presa'
    arm = J[side + '_wr'] - J[side + '_el']
    FA = arm.length
    bm = bmesh.new()
    nails, joints = [], []
    # palmo, polso, avambraccio
    bm_ellipsoid(bm, (0.003, 0.046, -0.002), (0.036, 0.047, 0.0125), seg=28)
    bm_ellipsoid(bm, (-0.021, 0.026, -0.009), (0.016, 0.027, 0.012), Euler((0, 0, radians(-25))).to_matrix())
    bm_ellipsoid(bm, (0.025, 0.034, -0.007), (0.012, 0.030, 0.010))
    bm_ellipsoid(bm, (0.0, -0.004, 0.0), (0.027, 0.020, 0.016))
    bm_capsule(bm, (0, -FA, 0), (0, -0.006, 0), 0.031, 0.0235, seg=18)
    bm_sphere(bm, (0.022, -0.004, 0.007), 0.0065)
    curls = CURL[pose]
    spreads = SPREAD_PRESA if pose == 'presa' else [f[2] for f in FINGERS]
    for (nm, k, sp0, lens, radii), cu, sp in zip(FINGERS, curls, spreads):
        pts, dirs, a = finger_chain(k, sp, lens, cu)
        bm_capsule(bm, (k[0] * 0.35, 0.004, 0.0075), (k[0], k[1] - 0.01, 0.0085), 0.003, 0.003, seg=8)
        for i in range(3):
            bm_capsule(bm, pts[i], pts[i + 1], radii[i], radii[i + 1])
        bm_sphere(bm, pts[0], radii[0] * 1.18)
        bm_sphere(bm, pts[1], radii[1] * 1.12)
        bm_sphere(bm, pts[2], radii[2] * 1.08)
        joints += [(pts[0], radii[0] * 1.7), (pts[1], radii[1] * 1.6), (pts[2], radii[2] * 1.5)]
        d3, phi3 = dirs[2]
        up = V(sin(a) * sin(phi3), cos(a) * sin(phi3), cos(phi3))
        lat = d3.cross(up)
        nc = pts[2] + d3 * (lens[2] * 0.6) + up * (radii[3] * 0.62)
        rot = Matrix((lat, d3, up)).transposed()
        bm_ellipsoid(bm, nc, (radii[3] * 0.82, lens[2] * 0.36, radii[3] * 0.30), rot, seg=12)
        nails.append((nc, rot, (radii[3] * 0.95, lens[2] * 0.44, radii[3] * 0.7)))
    tj, tup = thumb_joints(pose, grip)
    for i in range(3):
        bm_capsule(bm, tj[i], tj[i + 1], THUMB_R[i], THUMB_R[i + 1])
    bm_sphere(bm, tj[1], THUMB_R[1] * 1.15)
    bm_sphere(bm, tj[2], THUMB_R[2] * 1.1)
    joints += [(tj[1], THUMB_R[1] * 1.6), (tj[2], THUMB_R[2] * 1.5)]
    d3 = (tj[3] - tj[2]).normalized()
    tup = (tup - d3 * tup.dot(d3)).normalized()
    lat = d3.cross(tup)
    nc = tj[2] + d3 * 0.017 + tup * (THUMB_R[3] * 0.62)
    rot = Matrix((lat, d3, tup)).transposed()
    bm_ellipsoid(bm, nc, (THUMB_R[3] * 0.85, 0.011, THUMB_R[3] * 0.3), rot, seg=12)
    nails.append((nc, rot, (THUMB_R[3] * 1.0, 0.013, THUMB_R[3] * 0.7)))

    M, mirrored = hand_frame(side)
    bmesh.ops.transform(bm, matrix=M, verts=bm.verts)
    if mirrored:
        bmesh.ops.reverse_faces(bm, faces=bm.faces)
    name = 'mano_destra' if side == 'r' else 'mano_sinistra'
    ob = bm_obj(bm, name)
    m = ob.modifiers.new('remesh', 'REMESH')
    m.mode, m.voxel_size, m.adaptivity, m.use_smooth_shade = 'VOXEL', 0.0013, 0.0, True
    s = ob.modifiers.new('smooth', 'SMOOTH')
    s.factor, s.iterations = 0.5, 5
    apply_all(ob)
    # maschere per il materiale: R = unghie, G = nocche/pieghe
    Mw = M.to_3x3()
    nails_w = [(M @ c, (Mw @ rot), sz) for (c, rot, sz) in nails]
    joints_w = [(M @ c, r) for (c, r) in joints]
    me = ob.data
    col = me.color_attributes.new('maschere', 'FLOAT_COLOR', 'POINT')
    vals = []
    for v in me.vertices:
        p = v.co
        nail = 0.0
        for c, rot, sz in nails_w:
            d = p - c
            if d.length > 0.03:
                continue
            ax0, ax1, ax2 = rot.col[0].normalized(), rot.col[1].normalized(), rot.col[2].normalized()
            e = (d.dot(ax0) / sz[0]) ** 2 + (d.dot(ax1) / sz[1]) ** 2 + (d.dot(ax2) / sz[2]) ** 2
            nail = max(nail, 1.0 - smoothstep(0.55, 1.0, e))
        jn = 0.0
        for c, r in joints_w:
            d = (p - c).length
            if d < r * 1.6:
                jn = max(jn, 1.0 - smoothstep(r * 0.6, r * 1.6, d))
        vals += [nail, jn, 0.0, 1.0]
    col.data.foreach_set('color', vals)
    return ob, M


def build_hands():
    grip = grip_setup()
    Rs, yc, zc = grip
    hr, _ = build_hand('r', grip)
    hl, Ml = build_hand('l', grip)
    G = Ml @ V(0.0, yc, zc)
    return hr, hl, G, Rs


def build_hand_proxies():
    """Ellissoidi semplici al posto delle mani, solo per le collisioni della stoffa."""
    obs = []
    for side in ('r', 'l'):
        M, mir = hand_frame(side)
        bm = bmesh.new()
        bm_ellipsoid(bm, (0.0, 0.05, -0.01), (0.045, 0.075, 0.03), seg=16)
        bmesh.ops.transform(bm, matrix=M, verts=bm.verts)
        if mir:
            bmesh.ops.reverse_faces(bm, faces=bm.faces)
        obs.append(bm_obj(bm, 'proxy_mano_' + side, 'aiuti'))
    return obs


# ----------------------------------------------------------------------------- bastone
def build_staff(G, Rs):
    Sd = STAFF_DIR
    e2 = (V(1, 0, 0) - Sd * Sd.x).normalized()
    e1 = e2.cross(Sd).normalized()
    B = G - Sd * (G.z / Sd.z)
    top_z = 1.215
    Lsh = (top_z - B.z) / Sd.z
    hg = (G.z - B.z) / Sd.z
    knots = [(0.16, 0.006, 1.0), (0.43, 0.005, 3.1), (0.68, 0.007, 5.2), (1.13, 0.0045, 0.4), (0.92, 0.004, 2.2)]
    seg = 18
    nr = int(Lsh / 0.008)
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new('UVMap')
    rings = []
    for k in range(nr + 1):
        h = Lsh * k / nr
        wg = exp(-((h - hg) / 0.07) ** 2)
        wgw = exp(-((h - hg) / 0.16) ** 2)
        wob = (e1 * (0.0065 * noise.noise(V(h * 2.2, 0.3, 1.7)) + 0.0022 * noise.noise(V(h * 9, 4.1, 0.2)))
               + e2 * (0.0065 * noise.noise(V(h * 2.2, 5.3, 3.7)) + 0.0022 * noise.noise(V(h * 9, 1.1, 7.2))))
        c = B + Sd * h + wob * (1 - wgw)
        rb = 0.0165 + 0.0014 * noise.noise(V(h * 5, 7.7, 2.2))
        rb = rb * (1 - wg) + Rs * wg
        rb *= 0.93 + 0.07 * smoothstep(0.0, 0.05, h)
        ring = []
        for i in range(seg):
            th = 2 * pi * i / seg
            r = rb
            for kh, ka, kang in knots:
                r += ka * exp(-((h - kh) / 0.022) ** 2) * max(0.0, cos(th - kang)) ** 2 * (1 - wg)
            r *= 1 + 0.07 * noise.noise(V(cos(th) * 0.6, sin(th) * 0.6, h * 16)) * (1 - wg)
            ring.append(bm.verts.new(c + (e1 * cos(th) + e2 * sin(th)) * r))
        rings.append((ring, h))
    for k in range(nr):
        for i in range(seg):
            i2 = (i + 1) % seg
            f = bm.faces.new((rings[k][0][i], rings[k][0][i2], rings[k + 1][0][i2], rings[k + 1][0][i]))
            for lo, (kk, ii) in zip(f.loops, ((k, i), (k, i + 1), (k + 1, i + 1), (k + 1, i))):
                lo[uvl].uv = (ii / seg * 0.106, rings[kk][1])
    cb = bm.verts.new(B + Sd * 0.0)
    for i in range(seg):
        i2 = (i + 1) % seg
        f = bm.faces.new((rings[0][0][i2], rings[0][0][i], cb))
        for lo in f.loops:
            lo[uvl].uv = (0.5, 0.0)
    bm.normal_update()
    bm.faces.ensure_lookup_table()
    # controlla che le normali guardino fuori
    f0 = bm.faces[len(bm.faces) // 2]
    if f0.normal.dot(f0.calc_center_median() - (B + Sd * Lsh * 0.5)) < 0:
        bmesh.ops.reverse_faces(bm, faces=bm.faces)
    shaft = bm_obj(bm, 'bastone')
    shaft.data.shade_smooth()

    # cima intrecciata: tre rami che si avvolgono, nodo, croce e due riccioli
    O = B + Sd * (Lsh - 0.025)

    def P(h, a, rr):
        return O + Sd * h + (e1 * cos(a) + e2 * sin(a)) * rr

    def fill(bm):
        H = 0.17
        for j in range(3):
            pts, rad = [], []
            n = 30
            for i in range(n + 1):
                t = i / n
                pts.append(P(t * H, 2 * pi * j / 3 + t * 1.6 * pi, 0.0105 + 0.007 * t * t))
                rad.append(0.0088 - 0.0024 * t)
            bm_chain(bm, pts, rad, seg=10)
        K = O + Sd * (H + 0.012)
        rnd = random.Random(5)
        for i in range(5):
            a = 2 * pi * i / 5
            bm_sphere(bm, K + (e1 * cos(a) + e2 * sin(a)) * 0.009 + Sd * (rnd.random() - 0.5) * 0.012,
                      0.0135 + 0.004 * rnd.random(), seg=14)
        # punta
        pts = [K + Sd * (0.11 * t) + e1 * (0.012 * t * t) for t in [i / 10 for i in range(11)]]
        bm_chain(bm, pts, [0.0095 - 0.006 * (i / 10) for i in range(11)], seg=10)
        # traversa della croce
        cc = K + Sd * 0.062 + e1 * 0.0013
        for s in (-1, 1):
            pts = [cc + e2 * (s * 0.040 * t) - Sd * (0.006 * t ** 3) for t in [i / 8 for i in range(9)]]
            bm_chain(bm, pts, [0.0068 + 0.0016 * (i / 8) ** 4 for i in range(9)], seg=10)
        # riccioli laterali
        for s in (-1, 1):
            C = K + e2 * (s * 0.036) - Sd * 0.006
            pts, rad = [], []
            n = 40
            for i in range(n + 1):
                ph = 1.85 * pi * i / n
                rho = 0.026 * (1 - 0.5 * ph / (2 * pi))
                pts.append(C + e2 * (-s * rho * cos(ph)) - Sd * (rho * sin(ph)))
                rad.append(0.0072 - 0.0034 * i / n)
            bm_chain(bm, pts, rad, seg=10)
    orn = organic('bastone_cima', fill, voxel=0.0011, sm_iter=4)
    return shaft, orn, B


# ----------------------------------------------------------------------------- stoffa
def interp_profile(prof, z):
    if z >= prof[0][0]:
        return list(prof[0][1:])
    for (z0, *a0), (z1, *a1) in zip(prof, prof[1:]):
        if z1 <= z <= z0:
            t = (z0 - z) / (z0 - z1)
            return [x0 + (x1 - x0) * t for x0, x1 in zip(a0, a1)]
    return list(prof[-1][1:])


def spow(x, n):
    """Superellisse: segno(x)*|x|^(2/n). n=2 ellisse, n>2 piu' squadrata."""
    return math.copysign(abs(x) ** (2.0 / n), x)


def fold_wave(t, z, seed, nfold):
    """Pieghe irregolari attorno all'anello (valore circa in [-1, 1])."""
    return (0.65 * sin(nfold * t + 1.4 * sin(3 * t + seed) + 2.0 * noise.noise(V(z * 1.5, seed, 0.3)))
            + 0.35 * noise.noise(V(cos(t) * 2.2, sin(t) * 2.2, z * 2.0 + seed)))


def build_tube(name, rows, M, t0_fn=None, t_start=0.0, fold_fn=None):
    """Anelli (z, cx, cy, ax, ay, n_davanti) dall'alto in basso. t=0 davanti (-Y), cresce verso +X.
    t0_fn(z, ax, n) = semi-apertura frontale (0 = anello chiuso). fold_fn(t, z) = scala radiale.
    UV 'tessuto' in metri."""
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new('tessuto')
    lr = bm.verts.layers.int.new('riga')
    lc = bm.verts.layers.int.new('col')
    grid, U, VV = [], [], []
    vacc, prev_back = 0.0, None
    for k, row in enumerate(rows):
        z, cx, cy, ax, ay = row[:5]
        nf = row[5] if len(row) > 5 else 2.0
        t0 = t0_fn(z, ax, nf) if t0_fn else 0.0
        ts = [t_start + t0 + (2 * pi - 2 * t0) * i / M for i in range(M + 1)]

        def pt(t):
            n = nf if cos(t) > 0 else 2.0
            s = fold_fn(t, z) if fold_fn else 1.0
            return V(cx + ax * s * spow(sin(t), n), cy - ay * s * spow(cos(t), n), z)
        # arco cumulativo da t_start
        us = []
        s = 0.0
        prev = pt(t_start)
        tprev = t_start
        for t in ts:
            n = max(1, int(abs(t - tprev) / 0.01))
            for j in range(1, n + 1):
                q = pt(tprev + (t - tprev) * j / n)
                s += (q - prev).length
                prev = q
            tprev = t
            us.append(s)
        back = pt(t_start + pi)
        if prev_back is not None:
            vacc += (back - prev_back).length
        prev_back = back
        ring = []
        for i, t in enumerate(ts):
            v = bm.verts.new(pt(t))
            v[lr] = k
            v[lc] = i
            ring.append(v)
        grid.append(ring)
        U.append(us)
        VV.append(vacc)
    for k in range(len(rows) - 1):
        for i in range(M):
            quad = ((k, i), (k + 1, i), (k + 1, i + 1), (k, i + 1))
            vs = [grid[a][b] for a, b in quad]
            if len(set(vs)) < 3:
                continue
            f = bm.faces.new(vs)
            for lo, (a, b) in zip(f.loops, quad):
                lo[uvl].uv = (U[a][b], VV[a])
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    ob = bm_obj(bm, name)
    ob['v_tot'] = VV[-1]
    return ob


def build_sleeve(name, s0, el, end, r0, r1, ra0, ra1, M=36, fillet=0.09, step=0.012, seed=0.0):
    a = (s0 - el).normalized()
    b = (end - el).normalized()
    ang = a.angle(b)
    td = min(fillet / math.tan(ang / 2), 0.45 * min((s0 - el).length, (end - el).length))
    p1, p2 = el + a * td, el + b * td
    pts = []

    def line(p, q):
        n = max(1, int((q - p).length / step))
        for i in range(n):
            pts.append(p.lerp(q, i / n))
    line(s0, p1)
    for i in range(10):
        t = i / 10
        pts.append(p1 * (1 - t) ** 2 + el * (2 * (1 - t) * t) + p2 * (t * t))
    line(p2, end)
    pts.append(end.copy())
    # lunghezza d'arco
    S = [0.0]
    for i in range(1, len(pts)):
        S.append(S[-1] + (pts[i] - pts[i - 1]).length)
    Ltot = S[-1]
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new('tessuto')
    lr = bm.verts.layers.int.new('riga')
    grid = []
    Zup = V(0, 0, 1)
    for k, p in enumerate(pts):
        T = (pts[min(k + 1, len(pts) - 1)] - pts[max(k - 1, 0)]).normalized()
        N = (Zup - T * Zup.dot(T))
        N = N.normalized() if N.length > 1e-4 else V(1, 0, 0)
        Bv = T.cross(N)
        s = S[k] / Ltot
        r = r0 + (r1 - r0) * s ** 1.3
        ra = ra0 + (ra1 - ra0) * s
        fac = min(1.0, max(0.0, (1 - abs(T.z)) * 2.5))
        c = p - N * max(0.0, r - ra - 0.008) * fac
        ring = []
        for i in range(M + 1):
            th = pi + 2 * pi * i / M
            # pieghe a spirale che crescono verso il polsino
            fw = 1.0 + 0.16 * smoothstep(0.15, 1.0, s) * sin(5 * th + 9.0 * s + seed + 1.5 * noise.noise(V(s * 4, seed, 1.0)))
            v = bm.verts.new(c + (N * cos(th) + Bv * sin(th)) * (r * fw))
            v[lr] = k
            ring.append((v, th * r))
        grid.append(ring)
    for k in range(len(pts) - 1):
        for i in range(M):
            quad = ((k, i), (k, i + 1), (k + 1, i + 1), (k + 1, i))
            f = bm.faces.new([grid[a][b][0] for a, b in quad])
            for lo, (a, b) in zip(f.loops, quad):
                lo[uvl].uv = (grid[a][b][1], S[a])
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bm.normal_update()
    bm.faces.ensure_lookup_table()
    f0 = bm.faces[len(bm.faces) // 2]
    ref = pts[len(pts) // 2]
    if f0.normal.dot(f0.calc_center_median() - ref) < 0:
        bmesh.ops.reverse_faces(bm, faces=bm.faces)
    ob = bm_obj(bm, name)
    ob['v_tot'] = Ltot
    return ob


def pin_group(ob, weights):
    vg = ob.vertex_groups.new(name='pin')
    for i, w in weights.items():
        if w > 0:
            vg.add([i], min(1.0, w), 'REPLACE')
    return vg


def push_out(ob, coll, margin=0.012, zmax=10.0, maxd=0.10):
    n = 0
    for v in ob.data.vertices:
        if v.co.z > zmax:
            continue
        ok, loc, nor, idx = coll.closest_point_on_mesh(v.co)
        if not ok:
            continue
        d = (v.co - loc).dot(nor)
        if d < margin and (v.co - loc).length < maxd:
            v.co = loc + nor * margin
            n += 1
    print(f'[push_out] {ob.name}: {n} vertici spostati fuori da {coll.name}')


def simulate(ob, frames, quality=6, mass=0.35, tension=25.0, shear=12.0, bending=3.0, self_coll=True):
    sc = bpy.context.scene
    cm = ob.modifiers.new('Cloth', 'CLOTH')
    s = cm.settings
    s.quality = quality
    s.mass = mass
    s.tension_stiffness = tension
    s.compression_stiffness = tension
    s.shear_stiffness = shear
    s.bending_stiffness = bending
    s.tension_damping = 5.0
    s.compression_damping = 5.0
    s.shear_damping = 5.0
    s.air_damping = 1.0
    s.vertex_group_mass = 'pin'
    c = cm.collision_settings
    c.use_collision = True
    c.distance_min = 0.006
    c.collision_quality = 4
    c.use_self_collision = self_coll
    c.self_distance_min = 0.005
    c.self_friction = 5.0
    cm.point_cache.frame_start = 1
    cm.point_cache.frame_end = frames
    sc.frame_start, sc.frame_end = 1, frames
    t = time.time()
    for f in range(1, frames + 1):
        sc.frame_set(f)
    print(f'[sim] {ob.name}: {frames} frame, {len(ob.data.vertices)} vertici, {time.time() - t:.1f}s')
    apply_all(ob)
    sc.frame_set(1)


ROBE_PROFILE = [  # z, cy, ax, ay
    (1.16, 0.00, 0.150, 0.125), (1.05, 0.01, 0.165, 0.135), (0.90, 0.02, 0.195, 0.155),
    (0.70, 0.03, 0.215, 0.175), (0.40, 0.03, 0.255, 0.215), (0.012, 0.03, 0.350, 0.300)]
HOOD_PROFILE = [  # z, cy, ax, ay, esponente superellisse davanti (cappuccio profondo, bordo davanti all'occhio)
    (1.80, 0.030, 0.0, 0.0, 2.0), (1.76, 0.000, 0.05, 0.06, 2.0), (1.71, -0.035, 0.10, 0.13, 2.5),
    (1.65, -0.055, 0.145, 0.19, 3.0), (1.58, -0.060, 0.163, 0.218, 3.4), (1.48, -0.060, 0.168, 0.225, 3.5),
    (1.38, -0.055, 0.165, 0.215, 3.3), (1.31, -0.045, 0.175, 0.195, 3.0), (1.25, -0.030, 0.290, 0.225, 2.4),
    (1.21, -0.020, 0.315, 0.240, 2.2), (1.13, -0.010, 0.350, 0.260, 2.0), (1.05, 0.000, 0.380, 0.280, 2.0)]
OPEN_ZC, OPEN_H, OPEN_W = 1.465, 0.16, 0.115


def hood_t0(z, ax, nf=2.0):
    dz = (z - OPEN_ZC) / OPEN_H
    if abs(dz) >= 1.0 or ax <= 1e-4:
        return 0.0
    w = OPEN_W * sqrt(1 - dz * dz)
    return math.asin(min(0.98, (min(1.0, w / ax)) ** (nf / 2.0)))


def rows_from_profile(prof, step):
    z0, z1 = prof[0][0], prof[-1][0]
    n = int((z0 - z1) / step)
    rows = []
    for k in range(n + 1):
        z = z0 - (z0 - z1) * k / n
        rows.append((z, 0.0, *interp_profile(prof, z)))
    return rows


def build_robe():
    def folds(t, z):
        return 1.0 + 0.15 * smoothstep(1.05, 0.1, z) * (0.75 * fold_wave(t, z, 1.3, 7) + 0.25 * fold_wave(t, z, 6.1, 13))
    ob = build_tube('tunica', rows_from_profile(ROBE_PROFILE, 0.015), 96, t_start=pi, fold_fn=folds)
    rr = ob.data.attributes['riga'].data
    pin_group(ob, {i: 1.0 for i in range(len(rr)) if rr[i].value == 0})
    return ob


def build_hood():
    rows = rows_from_profile(HOOD_PROFILE, 0.011)

    def folds(t, z):
        return 1.0 + 0.09 * smoothstep(1.27, 1.08, z) * fold_wave(t, z, 4.1, 9)
    ob = build_tube('cappuccio', rows, 96, t0_fn=hood_t0, t_start=0.0, fold_fn=folds)
    me = ob.data
    rr, cc = me.attributes['riga'].data, me.attributes['col'].data
    open_rows = {k for k, r in enumerate(rows) if hood_t0(r[0], r[3], r[5]) > 0}
    lo, hi = min(open_rows) - 1, max(open_rows) + 1
    w = {}
    for v in me.vertices:
        k, c = rr[v.index].value, cc[v.index].value
        wt = smoothstep(1.64, 1.70, v.co.z)
        if lo <= k <= hi and c in (0, 96):
            wt = 1.0
        w[v.index] = wt
    pin_group(ob, w)
    ob['rim_rows'] = [lo, hi]
    return ob


def build_sleeves():
    out = []
    for side in ('r', 'l'):
        sh, el, wr = J[side + '_sh'], J[side + '_el'], J[side + '_wr']
        s0 = sh + (el - sh) * 0.32
        F = (wr - el).normalized()
        end = wr + F * (0.03 if side == 'r' else 0.0)
        ob = build_sleeve('manica_' + ('destra' if side == 'r' else 'sinistra'), s0, el, end,
                          0.058, 0.125 if side == 'r' else 0.115, 0.044, 0.025, seed=2.0 if side == 'r' else 5.0)
        rr = ob.data.attributes['riga'].data
        pin_group(ob, {i: 1.0 for i in range(len(rr)) if rr[i].value == 0})
        out.append(ob)
    return out


def subdivide(ob, levels=1):
    m = ob.modifiers.new('sub', 'SUBSURF')
    m.levels = levels
    m.render_levels = levels
    m.quality = 3
    apply_all(ob)


def smooth_boundary(bm, iters=8, fac=0.5):
    """Arrotonda i bordi strappati (toglie i gradini della griglia)."""
    for _ in range(iters):
        new = {}
        for v in bm.verts:
            if not v.is_boundary:
                continue
            nb = [e.other_vert(v) for e in v.link_edges if e.is_boundary]
            if len(nb) != 2:
                continue
            new[v] = v.co.lerp((nb[0].co + nb[1].co) * 0.5, fac)
        for v, co in new.items():
            v.co = co


def tatter(ob, edge_dist, seed, depth=0.22, slit_spacing=0.07, slit_w=0.014, jag=0.05, holes=0.86,
           min_island=40, smooth_iters=8):
    """Strappi: orlo irregolare + tagli verticali dal bordo (lingue di stoffa) + pochi buchi.
    edge_dist(u, v, co) -> distanza dal bordo libero in metri."""
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    uvl = bm.loops.layers.uv['tessuto']
    rnd = random.Random(seed)
    data = []
    umax = 0.0
    for f in bm.faces:
        n = len(f.loops)
        u = sum(lo[uvl].uv.x for lo in f.loops) / n
        v = sum(lo[uvl].uv.y for lo in f.loops) / n
        umax = max(umax, u)
        data.append((f, u, v, f.calc_center_median()))
    slits = []
    if slit_spacing:
        u = rnd.random() * slit_spacing
        while u < umax + slit_spacing:
            big = rnd.random() < 0.22                       # ogni tanto uno strappo largo
            L = depth * ((0.55 + 0.45 * rnd.random()) if big else (0.15 + 0.85 * rnd.random() ** 0.9))
            w = slit_w * ((2.4 + 1.4 * rnd.random()) if big else (0.45 + 0.9 * rnd.random()))
            slant = (rnd.random() - 0.5) * 0.3
            slits.append((u, L, w, rnd.random() * 10, slant))
            u += slit_spacing * (0.45 + 1.1 * rnd.random()) * (1.6 if big else 1.0)
    kill = []
    for f, u, v, c in data:
        d = edge_dist(u, v, c)
        # orlo frastagliato
        j = jag * (0.25 + 0.75 * (0.5 + 0.5 * noise.noise(V(u * 9.0, seed, 0.7))))
        j += jag * 0.35 * noise.noise(V(u * 37.0, seed + 3.3, 0.2))
        if d < j:
            kill.append(f)
            continue
        # tagli verticali
        hit = False
        if d < depth:
            for (us, L, w, ph, slant) in slits:
                if d >= L or abs(u - us) > 0.15:
                    continue
                uc = us + slant * d + 0.018 * noise.noise(V(d * 12.0, ph, seed))
                ragged = 0.55 + 0.9 * (0.5 + 0.5 * noise.noise(V(d * 45.0, ph + 2.0, seed)))
                if abs(u - uc) < 0.5 * w * ragged * (1.0 - 0.8 * (d / L) ** 1.3):
                    hit = True
                    break
        if hit:
            kill.append(f)
            continue
        h = noise.noise(V(u * 4.0, v * 4.0, seed + 4.4)) + 0.45 * noise.noise(V(u * 12.0, v * 12.0, seed + 2.2))
        if h > holes:
            kill.append(f)
    bmesh.ops.delete(bm, geom=kill, context='FACES')
    # elimina i brandelli troppo piccoli (connessi per lato)
    seen = set()
    small = []
    for f in bm.faces:
        if f.index in seen:
            continue
        stack, isl = [f], []
        seen.add(f.index)
        while stack:
            g = stack.pop()
            isl.append(g)
            for e in g.edges:
                for h2 in e.link_faces:
                    if h2.index not in seen:
                        seen.add(h2.index)
                        stack.append(h2)
        if len(isl) < min_island:
            small += isl
    bm.faces.index_update()
    bmesh.ops.delete(bm, geom=list(set(small)), context='FACES')
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context='VERTS')
    smooth_boundary(bm, smooth_iters)
    bm.to_mesh(ob.data)
    bm.free()
    print(f'[strappi] {ob.name}: tolte {len(kill)} facce + {len(set(small))} brandelli')


def solidify(ob, thick=0.004):
    m = ob.modifiers.new('spessore', 'SOLIDIFY')
    m.thickness = thick
    m.offset = -1.0
    m.use_even_offset = True
    m.use_rim = True
    m.material_offset = 1
    return m


def build_necklace(hood):
    """Cordino a V sul petto con una piccola croce, appoggiato sulla mantellina."""
    def on_surface(x, z, off=0.004):
        ok, loc, nor, idx = hood.ray_cast(V(x, -0.8, z), V(0, 1, 0))
        if not ok:
            return None, None
        return loc + nor * off, nor
    ctrl = [(-0.075, 1.31), (-0.062, 1.24), (-0.040, 1.16), (-0.016, 1.10), (0.0, 1.085),
            (0.016, 1.10), (0.040, 1.16), (0.062, 1.24), (0.075, 1.31)]
    pts = []
    for i in range(len(ctrl) - 1):
        for j in range(6):
            t = j / 6
            x = ctrl[i][0] * (1 - t) + ctrl[i + 1][0] * t
            z = ctrl[i][1] * (1 - t) + ctrl[i + 1][1] * t
            p, n = on_surface(x, z)
            if p is not None:
                pts.append(p)
    p, n = on_surface(*ctrl[-1])
    if p is not None:
        pts.append(p)
    bm = bmesh.new()
    bm_chain(bm, pts, [0.0019] * len(pts), seg=8)
    pc, nc = on_surface(0.0, 1.055, 0.007)
    if pc is not None:
        up = V(0, 0, 1)
        side = up.cross(nc).normalized()
        upp = nc.cross(side).normalized()
        rot = Matrix((side, upp, nc)).transposed()
        for (cx, cz, sx, sz) in ((0, 0, 0.0045, 0.024), (0, 0.009, 0.015, 0.0045)):
            M = Matrix.Translation(pc + upp * cz) @ rot.to_4x4() @ Matrix.Diagonal((sx, sz, 0.0028, 1))
            bmesh.ops.create_cube(bm, size=2.0, matrix=M)
    ob = bm_obj(bm, 'collana')
    ob.data.shade_smooth()
    return ob


def build_floor():
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=2.5)
    return bm_obj(bm, 'pavimento_collisione', 'aiuti')


# ----------------------------------------------------------------------------- fase: geometria
def stage_geo():
    sc = reset()
    t0 = time.time()
    body = build_body()
    head = build_head()
    eye = build_eye()
    orb = build_orb(head)
    hr, hl, G, Rs = build_hands()
    shaft, orn, B = build_staff(G, Rs)
    shaft['presa'] = list(G)
    floor = build_floor()
    prox = build_hand_proxies()
    print(f'[geo] solidi pronti in {time.time() - t0:.1f}s  presa={tuple(round(x, 3) for x in G)}  base bastone={tuple(round(x, 3) for x in B)}')
    if 'nocloth' not in ARGS:
        for c in [body, head, eye, floor, shaft, orn] + prox:
            make_collider(c)
        robe = build_robe()
        simulate(robe, 60, mass=0.4, tension=30, bending=4.0)
        make_collider(robe)
        sleeves = build_sleeves()
        for sl in sleeves:
            push_out(sl, robe, margin=0.012, zmax=1.13)
            simulate(sl, 50, mass=0.25, tension=25, bending=2.0)
            make_collider(sl)
        hood = build_hood()
        simulate(hood, 60, mass=0.3, tension=35, bending=5.0)
        necklace = build_necklace(hood)
        for c in [body, head, eye, floor, shaft, orn, robe] + prox + sleeves:
            remove_collider(c)
        # rifinitura: suddivisione, strappi, spessore
        vt = robe['v_tot']
        subdivide(robe)
        tatter(robe, lambda u, v, co: vt - v, seed=1.0, depth=0.32, slit_spacing=0.085, slit_w=0.032, jag=0.06, holes=0.86,
               smooth_iters=3)
        for sl in sleeves:
            lt = sl['v_tot']
            subdivide(sl)
            tatter(sl, lambda u, v, co, lt=lt: lt - v, seed=3.0 + len(sl.name), depth=0.16, slit_spacing=0.06,
                   slit_w=0.024, jag=0.035, holes=0.92, smooth_iters=3)
        vh = hood['v_tot']
        lo_r, hi_r = hood['rim_rows']
        rr, cc = hood.data.attributes['riga'].data, hood.data.attributes['col'].data
        rim = [v.co.copy() for v in hood.data.vertices
               if lo_r <= rr[v.index].value <= hi_r and cc[v.index].value in (0, 96)]
        kd = kdtree.KDTree(len(rim))
        for i, p in enumerate(rim):
            kd.insert(p, i)
        kd.balance()
        subdivide(hood)
        tatter(hood, lambda u, v, co: vh - v, seed=7.0, depth=0.13, slit_spacing=0.07, slit_w=0.026, jag=0.04,
               holes=0.86, smooth_iters=3)
        tatter(hood, lambda u, v, co: kd.find(co)[2], seed=8.0, depth=0.0, slit_spacing=None, jag=0.014,
               holes=9.0, smooth_iters=4)
        for ob in [robe, hood] + sleeves:
            for vg in list(ob.vertex_groups):
                ob.vertex_groups.remove(vg)
            ob.data.shade_smooth()
            solidify(ob)
    for ob in list(bpy.data.collections['aiuti'].objects):
        bpy.data.objects.remove(ob)
    save_blend(GEO_BLEND)
    print(f'[geo] salvato {GEO_BLEND} in {time.time() - t0:.1f}s')


# ----------------------------------------------------------------------------- render
def setup_cycles(samples=96, res=(1280, 720)):
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
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
    if 'filmic' in ARGS:
        sc.view_settings.view_transform = 'Filmic'
        sc.view_settings.look = 'Medium High Contrast'
    else:
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


def stage_clay():
    bpy.ops.wm.open_mainfile(filepath=GEO_BLEND)
    sc = bpy.context.scene
    setup_cycles(48, (1100, 1100))
    mat = bpy.data.materials.new('creta')
    mat.use_nodes = True
    mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.36, 0.34, 0.32, 1)
    mat.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = 0.6
    for ob in sc.objects:
        if ob.type == 'MESH':
            ob.data.materials.clear()
            ob.data.materials.append(mat)
            ob.data.materials.append(mat)
    w = bpy.data.worlds.new('w')
    sc.world = w
    w.use_nodes = True
    w.node_tree.nodes['Background'].inputs['Color'].default_value = (0.05, 0.05, 0.055, 1)
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=4)
    fl = bm_obj(bm, 'pavimento', 'scena')
    fl.data.materials.append(mat)
    add_area('key', (-1.8, -2.2, 2.6), (0, 0, 1.0), 260, 1.5)
    add_area('fill', (2.2, -1.5, 1.5), (0, 0, 1.0), 70, 2.0)
    add_area('rim', (0.8, 2.0, 2.2), (0, 0, 1.2), 160, 1.0)
    os.makedirs(RENDER_DIR, exist_ok=True)
    views = {'fronte': ((0.0, -3.3, 1.0), 0.86), 'tre_quarti': ((-2.1, -2.6, 1.05), 0.86),
             'lato': ((3.2, -0.6, 1.0), 0.86), 'retro': ((0.9, 3.2, 1.2), 0.86),
             'volto': ((-0.25, -1.25, 1.42), 1.38), 'mano': ((0.75, -1.05, 1.1), 1.08)}
    pick = [a for a in ARGS if a in views] or list(views)
    for nm in pick:
        loc, tz = views[nm]
        lens = 60 if nm in ('volto', 'mano') else 45
        tgt = (0, -0.1, tz) if nm != 'mano' else (0.28, -0.33, 1.06)
        if nm == 'volto':
            tgt = (0, -0.2, 1.45)
        add_camera(loc, tgt, lens)
        sc.render.filepath = os.path.join(RENDER_DIR, f'creta_{nm}.png')
        bpy.ops.render.render(write_still=True)
        print('[render]', sc.render.filepath)


# ----------------------------------------------------------------------------- materiali
class NT:
    """Piccolo aiuto per costruire alberi di nodi."""

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

    def wave(self, vec, scale, axis='X'):
        n = self.node('ShaderNodeTexWave', wave_type='BANDS', bands_direction=axis)
        self.val(n.inputs['Vector'], vec)
        n.inputs['Scale'].default_value = scale
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


def mat_fabric(name, inner=False):
    m = bpy.data.materials.new(name)
    t = NT(m)
    uv = t.uv('tessuto')
    # trama a tela: fili leggermente storti, sopra/sotto alternati
    jn = t.node('ShaderNodeTexNoise')
    t.val(jn.inputs['Vector'], uv)
    jn.inputs['Scale'].default_value = 35.0
    jn.inputs['Detail'].default_value = 2.0
    jit = t.vmath('SCALE', t.vmath('SUBTRACT', jn.outputs['Color'], (0.5, 0.5, 0.5)), scale=0.0022)
    uvd = t.vmath('ADD', uv, jit)
    pitch = 0.0028
    ws = 2 * pi / (20 * pitch)
    warp = t.wave(uvd, ws, 'X')
    weft = t.wave(uvd, ws, 'Y')
    chk = t.checker(uvd, 1.0 / pitch)
    weave = t.mixf(chk, weft, warp)
    slub = t.noise(t.vmath('MULTIPLY', uvd, (25.0, 300.0, 1.0)), 1.0, 3.0)
    fuzz = t.noise(uv, 1400.0, 3.0, 0.6)
    h = t.math('ADD', t.math('MULTIPLY', weave, 0.6), t.math('ADD', t.math('MULTIPLY', slub, 0.25), t.math('MULTIPLY', fuzz, 0.2)))
    macro = t.noise(t.obj, 22.0, 6.0, 0.55)
    nrm = t.bump(macro, 0.2, 0.006)
    crumple = t.noise(t.obj, 70.0, 5.0, 0.6, 0.3)
    nrm = t.bump(crumple, 0.14, 0.0018, nrm)
    nrm = t.bump(h, 0.16, 0.0007, nrm)
    # colore
    blot = t.noise(t.obj, 2.6, 5.0, 0.55)
    col = t.mix(t.mr(blot, 0.3, 0.7), (0.034, 0.0085, 0.0085), (0.072, 0.0165, 0.0150))
    fade = t.mr(t.noise(t.obj, 7.0, 3.0), 0.55, 0.72)
    col = t.mix(t.math('MULTIPLY', fade, 0.55), col, (0.090, 0.036, 0.030))
    stain = t.mr(t.noise(t.obj, 4.5, 4.0, 0.6, 0.4), 0.62, 0.7)
    col = t.mix(t.math('MULTIPLY', stain, 0.7), col, (0.022, 0.009, 0.007))
    if not inner:
        x, y, z = t.xyz(t.obj)
        dirt = t.math('MULTIPLY', t.mr(z, 0.6, 0.0, 0.0, 1.0, True),
                      t.mr(t.noise(t.obj, 9.0, 4.0, 0.6), 0.25, 0.7, 0.35, 1.0))
        col = t.mix(dirt, col, (0.028, 0.021, 0.016))
    col = t.mix(1.0, col, t.mr(weave, 0.0, 1.0, 0.72, 1.12), 'MULTIPLY')
    ptn = t.node('ShaderNodeNewGeometry').outputs['Pointiness']
    col = t.mix(t.mr(ptn, 0.5, 0.62, 0.0, 0.3), col, (0.085, 0.038, 0.032))
    col = t.mix(1.0, col, t.mr(t.ao(0.10), 0.0, 1.0, 0.35, 1.0), 'MULTIPLY')
    if inner:
        col = t.mix(1.0, col, (0.5, 0.5, 0.5), 'MULTIPLY')
    rough = t.mr(t.noise(t.obj, 12.0, 2.0), 0.3, 0.7, 0.84, 0.97)
    t.bsdf({'Base Color': col, 'Roughness': rough, 'Normal': nrm, 'Sheen Weight': 0.22,
            'Sheen Roughness': 0.55, 'Sheen Tint': (0.5, 0.26, 0.26, 1.0), 'Specular IOR Level': 0.25})
    return m


def mat_skin():
    m = bpy.data.materials.new('pelle')
    t = NT(m)
    o = t.obj
    msk = t.node('ShaderNodeSeparateColor')
    t.val(msk.inputs[0], t.attr('maschere'))
    nail, joint = msk.outputs[0], msk.outputs[1]
    var = t.noise(o, 55.0, 4.0, 0.55)
    col = t.mix(t.mr(var, 0.3, 0.7), (0.20, 0.18, 0.14), (0.31, 0.22, 0.18))
    spots = t.mr(t.noise(o, 130.0, 2.0, 0.5), 0.6, 0.68)
    col = t.mix(t.math('MULTIPLY', spots, 0.75), col, (0.19, 0.11, 0.055))
    vein = t.mr(t.math('ABSOLUTE', t.math('SUBTRACT', t.noise(o, 38.0, 2.0, 0.4), 0.5)), 0.0, 0.025, 1.0, 0.0)
    col = t.mix(t.math('MULTIPLY', vein, 0.35), col, (0.20, 0.22, 0.27))
    col = t.mix(t.math('MULTIPLY', joint, 0.65), col, (0.34, 0.16, 0.14))
    col = t.mix(nail, col, (0.43, 0.36, 0.22))
    col = t.mix(1.0, col, t.mr(t.ao(0.012), 0.0, 1.0, 0.35, 1.0), 'MULTIPLY')
    crease = t.mr(t.voronoi(o, 480.0, 'DISTANCE_TO_EDGE'), 0.0, 0.07, 0.0, 1.0)
    pores = t.noise(o, 1600.0, 2.0)
    wr = t.noise(t.vmath('MULTIPLY', o, (1.0, 1.0, 1.0)), 180.0, 5.0, 0.65)
    h = t.math('ADD', t.math('MULTIPLY', crease, t.mixf(joint, 0.35, 1.0)), t.math('ADD', t.math('MULTIPLY', pores, 0.25), t.math('MULTIPLY', wr, 0.45)))
    nrm = t.bump(h, 0.42, 0.0009)
    rough = t.mixf(nail, t.mr(var, 0.0, 1.0, 0.44, 0.62), 0.28)
    t.bsdf({'Base Color': col, 'Roughness': rough, 'Normal': nrm, 'Subsurface Weight': 0.22,
            'Subsurface Radius': (1.0, 0.38, 0.22), 'Subsurface Scale': 0.004, 'Specular IOR Level': 0.45})
    return m


def mat_face():
    m = bpy.data.materials.new('pelle_volto')
    t = NT(m)
    o = t.obj
    var = t.noise(o, 28.0, 5.0, 0.6)
    col = t.mix(t.mr(var, 0.3, 0.7), (0.026, 0.016, 0.013), (0.060, 0.034, 0.026))
    d = t.vmath('DISTANCE', o, tuple(EYE_C))
    wet = t.mr(d, EYE_R + 0.022, EYE_R + 0.004)
    col = t.mix(t.math('MULTIPLY', wet, 0.85), col, (0.11, 0.025, 0.02))
    col = t.mix(1.0, col, t.mr(t.ao(0.05), 0.0, 1.0, 0.4, 1.0), 'MULTIPLY')
    h = t.math('ADD', t.noise(t.vmath('MULTIPLY', o, (1.0, 1.0, 2.2)), 70.0, 6.0, 0.62), t.math('MULTIPLY', t.noise(o, 420.0, 3.0), 0.35))
    t.bsdf({'Base Color': col, 'Roughness': t.mixf(wet, 0.78, 0.3), 'Normal': t.bump(h, 0.3, 0.0015),
            'Subsurface Weight': 0.12, 'Subsurface Radius': (1.0, 0.3, 0.2), 'Subsurface Scale': 0.003})
    return m


def mat_eye():
    m = bpy.data.materials.new('occhio')
    t = NT(m)
    p = t.vmath('SCALE', t.obj, scale=1.0 / EYE_R)
    x, y, z = t.xyz(p)
    rho = t.vmath('LENGTH', t.comb(x, y, 0.0))
    ang = t.math('ARCTAN2', y, x)
    front = t.mr(z, -0.05, 0.05)
    iris = t.math('MULTIPLY', t.mr(rho, 0.575, 0.545), front)
    pupil = t.math('MULTIPLY', t.mr(rho, 0.258, 0.238), front)
    polar = t.comb(t.math('MULTIPLY', t.math('COSINE', ang), 13.0), t.math('MULTIPLY', t.math('SINE', ang), 13.0),
                   t.math('MULTIPLY', rho, 7.0))
    fib = t.noise(polar, 1.0, 6.0, 0.62)
    crypt = t.mr(t.voronoi(t.comb(t.math('MULTIPLY', t.math('COSINE', ang), 7.0),
                                  t.math('MULTIPLY', t.math('SINE', ang), 7.0), t.math('MULTIPLY', rho, 16.0)), 1.0), 0.0, 0.35, 0.55, 1.0)
    icol = t.ramp(rho, [(0.24, (0.10, 0.035, 0.008)), (0.29, (0.30, 0.10, 0.018)), (0.36, (0.52, 0.20, 0.035)),
                        (0.46, (0.40, 0.15, 0.026)), (0.53, (0.13, 0.05, 0.012)), (0.575, (0.03, 0.015, 0.008))])
    icol = t.mix(1.0, icol, t.mr(fib, 0.25, 0.75, 0.55, 1.45), 'MULTIPLY')
    icol = t.mix(1.0, icol, crypt, 'MULTIPLY')
    scl = t.mix(t.mr(rho, 0.6, 0.98, 0.0, 1.0, True), (0.52, 0.45, 0.35), (0.42, 0.19, 0.14))
    scl = t.mix(t.math('SUBTRACT', 1.0, front), scl, (0.30, 0.08, 0.06))
    wn = t.node('ShaderNodeTexNoise')
    t.val(wn.inputs['Vector'], p)
    pw = t.vmath('ADD', p, t.vmath('SCALE', t.vmath('SUBTRACT', wn.outputs['Color'], (0.5, 0.5, 0.5)), scale=0.25))
    vmask = t.math('MULTIPLY', t.mr(t.noise(p, 2.5, 3.0), 0.38, 0.56), t.mr(rho, 0.6, 0.93))
    vein = t.math('MULTIPLY', t.mr(t.voronoi(pw, 5.0, 'DISTANCE_TO_EDGE'), 0.0, 0.012, 1.0, 0.0), vmask)
    vein2 = t.math('MULTIPLY', t.mr(t.voronoi(pw, 11.0, 'DISTANCE_TO_EDGE'), 0.0, 0.008, 1.0, 0.0),
                   t.math('MULTIPLY', vmask, t.mr(rho, 0.75, 0.97)))
    scl = t.mix(t.math('MULTIPLY', t.math('MAXIMUM', vein, t.math('MULTIPLY', vein2, 0.7)), 0.85), scl, (0.32, 0.025, 0.02))
    col = t.mix(iris, scl, icol)
    col = t.mix(pupil, col, (0.003, 0.003, 0.003))
    t.bsdf({'Base Color': col, 'Roughness': 0.06, 'Coat Weight': 1.0, 'Coat Roughness': 0.02, 'Coat IOR': 1.38,
            'Specular IOR Level': 0.6, 'Subsurface Weight': t.math('MULTIPLY', t.math('SUBTRACT', 1.0, iris), 0.15),
            'Subsurface Radius': (1.0, 0.4, 0.3), 'Subsurface Scale': 0.003})
    return m


def mat_orb():
    m = bpy.data.materials.new('occhietto')
    t = NT(m)
    t.bsdf({'Base Color': (0.05, 0.2, 0.55), 'Roughness': 0.08, 'Coat Weight': 1.0,
            'Emission Color': (0.12, 0.45, 1.0, 1.0), 'Emission Strength': 7.0})
    return m


def mat_wood(G):
    m = bpy.data.materials.new('legno')
    t = NT(m)
    o = t.obj
    st = t.vmath('MULTIPLY', o, (1.0, 1.0, 0.07))
    grain = t.noise(st, 85.0, 7.0, 0.6, 0.4)
    crack = t.mr(t.voronoi(t.vmath('MULTIPLY', o, (1.0, 1.0, 0.09)), 70.0, 'DISTANCE_TO_EDGE'), 0.0, 0.05, 0.0, 1.0)
    col = t.mix(t.mr(grain, 0.3, 0.7), (0.014, 0.009, 0.006), (0.045, 0.027, 0.015))
    col = t.mix(t.math('SUBTRACT', 1.0, crack), col, (0.010, 0.006, 0.004))
    grip = t.mr(t.vmath('DISTANCE', o, tuple(G)), 0.09, 0.03)
    col = t.mix(t.math('MULTIPLY', grip, 0.6), col, (0.06, 0.034, 0.018))
    col = t.mix(1.0, col, t.mr(t.ao(0.03), 0.0, 1.0, 0.4, 1.0), 'MULTIPLY')
    h = t.math('ADD', t.math('MULTIPLY', grain, 0.6), t.math('MULTIPLY', crack, 0.5))
    rough = t.mixf(grip, t.mr(grain, 0.0, 1.0, 0.62, 0.85), 0.32)
    t.bsdf({'Base Color': col, 'Roughness': rough, 'Normal': t.bump(h, 0.6, 0.0016), 'Specular IOR Level': 0.4})
    return m


def mat_under():
    m = bpy.data.materials.new('sottoveste')
    t = NT(m)
    n = t.noise(t.obj, 300.0, 3.0)
    col = t.mix(t.mr(t.noise(t.obj, 6.0, 3.0), 0.3, 0.7), (0.016, 0.012, 0.010), (0.034, 0.024, 0.018))
    t.bsdf({'Base Color': col, 'Roughness': 0.95, 'Normal': t.bump(n, 0.4, 0.001), 'Sheen Weight': 0.4})
    return m


def mat_metal():
    m = bpy.data.materials.new('metallo')
    t = NT(m)
    tar = t.mr(t.noise(t.obj, 260.0, 4.0), 0.35, 0.7)
    x, y, z = t.xyz(t.obj)
    cross = t.mr(z, 1.083, 1.077)
    metal = t.mix(tar, (0.13, 0.095, 0.06), (0.035, 0.03, 0.025))
    col = t.mix(cross, (0.028, 0.018, 0.012), metal)
    t.bsdf({'Base Color': col, 'Metallic': t.math('MULTIPLY', cross, t.mr(tar, 0.0, 1.0, 0.75, 0.35)),
            'Roughness': t.mixf(cross, 0.62, t.mr(tar, 0.0, 1.0, 0.5, 0.75))})
    return m


def assign(ob, *mats):
    ob.data.materials.clear()
    for m in mats:
        ob.data.materials.append(m)


def stage_mat():
    bpy.ops.wm.open_mainfile(filepath=GEO_BLEND)
    o = bpy.data.objects
    fab, fab_in = mat_fabric('stoffa'), mat_fabric('stoffa_interno', inner=True)
    for nm in ('tunica', 'cappuccio', 'manica_destra', 'manica_sinistra'):
        if nm in o:
            assign(o[nm], fab, fab_in)
    skin = mat_skin()
    for nm in ('mano_destra', 'mano_sinistra'):
        assign(o[nm], skin)
    assign(o['testa'], mat_face())
    assign(o['occhio'], mat_eye())
    assign(o['occhietto'], mat_orb())
    wood = mat_wood(Vector(o['bastone']['presa']))
    assign(o['bastone'], wood)
    assign(o['bastone_cima'], wood)
    assign(o['corpo'], mat_under())
    if 'collana' in o:
        assign(o['collana'], mat_metal())
    save_blend(MAT_BLEND)
    print('[mat] salvato', MAT_BLEND)


# ----------------------------------------------------------------------------- scena e render
def mat_stone_floor():
    m = bpy.data.materials.new('pietra_pavimento')
    t = NT(m)
    o = t.obj
    vo = t.node('ShaderNodeTexVoronoi', feature='F1')
    dn = t.node('ShaderNodeTexNoise')
    t.val(dn.inputs['Vector'], o)
    t.val(vo.inputs['Vector'], t.vmath('ADD', o, t.vmath('SCALE', t.vmath('SUBTRACT', dn.outputs['Color'], (0.5, 0.5, 0.5)), scale=0.12)))
    vo.inputs['Scale'].default_value = 1.7
    edge = t.voronoi(o, 1.7, 'DISTANCE_TO_EDGE')
    mortar = t.mr(edge, 0.0, 0.025, 1.0, 0.0)
    stone = t.mix(t.mr(t.noise(o, 3.0, 4.0), 0.3, 0.7), (0.045, 0.040, 0.034), (0.11, 0.098, 0.082))
    vsep = t.node('ShaderNodeSeparateColor')
    t.val(vsep.inputs[0], vo.outputs['Color'])
    stone = t.mix(1.0, stone, t.mr(vsep.outputs[0], 0.0, 1.0, 0.65, 1.15), 'MULTIPLY')
    col = t.mix(mortar, stone, (0.018, 0.016, 0.014))
    grime = t.noise(o, 14.0, 6.0, 0.6)
    col = t.mix(1.0, col, t.mr(grime, 0.3, 0.75, 0.6, 1.15), 'MULTIPLY')
    h = t.math('ADD', t.math('MULTIPLY', t.math('SUBTRACT', 1.0, mortar), 0.7), t.math('MULTIPLY', grime, 0.3))
    t.bsdf({'Base Color': col, 'Roughness': t.mr(grime, 0.2, 0.8, 0.55, 0.92), 'Normal': t.bump(h, 0.5, 0.01)})
    return m


def mat_stone_wall():
    m = bpy.data.materials.new('pietra_muro')
    t = NT(m)
    o = t.obj
    x, y, z = t.xyz(o)
    br = t.node('ShaderNodeTexBrick', offset=0.5, squash=1.0)
    t.val(br.inputs['Vector'], t.comb(t.math('ADD', x, y), z, 0.0))
    br.inputs['Scale'].default_value = 1.0
    br.inputs['Mortar Size'].default_value = 0.012
    br.inputs['Brick Width'].default_value = 0.55
    br.inputs['Row Height'].default_value = 0.3
    br.inputs['Color1'].default_value = (0.10, 0.09, 0.075, 1)
    br.inputs['Color2'].default_value = (0.06, 0.055, 0.047, 1)
    br.inputs['Mortar'].default_value = (0.02, 0.018, 0.016, 1)
    grime = t.noise(o, 6.0, 6.0, 0.6)
    col = t.mix(1.0, br.outputs['Color'], t.mr(grime, 0.3, 0.75, 0.55, 1.2), 'MULTIPLY')
    h = t.math('ADD', t.math('SUBTRACT', 1.0, br.outputs['Fac']), t.math('MULTIPLY', t.noise(o, 25.0, 5.0), 0.4))
    t.bsdf({'Base Color': col, 'Roughness': 0.88, 'Normal': t.bump(h, 0.6, 0.015)})
    return m


def mat_emit(name, color, strength):
    m = bpy.data.materials.new(name)
    t = NT(m)
    em = t.node('ShaderNodeEmission')
    em.inputs['Color'].default_value = (*color, 1.0)
    em.inputs['Strength'].default_value = strength
    t.nt.links.new(em.outputs[0], t.out.inputs['Surface'])
    return m


def add_point(name, loc, power, color, radius=0.01, coll='scena'):
    l = bpy.data.lights.new(name, 'POINT')
    l.energy, l.color, l.shadow_soft_size = power, color, radius
    ob = bpy.data.objects.new(name, l)
    ob.location = loc
    link(ob, coll)
    return ob


def build_set():
    """Interno di pietra con candele a sinistra e una finestra fredda dietro a destra."""
    def plane(name, size, loc, rot, mat, coll='set'):
        bm = bmesh.new()
        bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=size / 2)
        ob = bm_obj(bm, name, coll)
        ob.location, ob.rotation_euler = loc, rot
        ob.data.materials.append(mat)
        return ob
    plane('pavimento', 9.0, (0, 0, 0), (0, 0, 0), mat_stone_floor())
    wall = mat_stone_wall()
    plane('muro_fondo', 9.0, (0, 2.6, 4.5), (radians(90), 0, 0), wall)
    plane('muro_sinistro', 9.0, (-2.2, 0, 4.5), (radians(90), 0, radians(90)), wall)
    wax = bpy.data.materials.new('cera')
    NT(wax).bsdf({'Base Color': (0.55, 0.45, 0.30), 'Roughness': 0.45, 'Subsurface Weight': 0.6,
                  'Subsurface Radius': (1.0, 0.6, 0.3), 'Subsurface Scale': 0.02})
    flame = mat_emit('fiamma', (1.0, 0.55, 0.18), 60.0)
    iron = bpy.data.materials.new('ferro')
    NT(iron).bsdf({'Base Color': (0.03, 0.028, 0.026), 'Metallic': 1.0, 'Roughness': 0.55})
    for i, (y, z) in enumerate([(-1.25, 1.45), (-0.95, 1.55), (-0.65, 1.42), (0.55, 1.5), (0.85, 1.6)]):
        bm = bmesh.new()
        h = 0.10 + 0.05 * ((i * 37) % 5) / 4
        bmesh.ops.create_cone(bm, cap_ends=True, segments=16, radius1=0.014, radius2=0.013, depth=h,
                              matrix=Matrix.Translation((-2.05, y, z + h / 2)))
        c = bm_obj(bm, f'candela_{i}', 'set')
        c.data.materials.append(wax)
        bm = bmesh.new()
        bm_ellipsoid(bm, (-2.05, y, z + h + 0.016), (0.006, 0.006, 0.016), seg=12)
        f = bm_obj(bm, f'fiamma_{i}', 'set')
        f.data.materials.append(flame)
        bm = bmesh.new()
        bm_capsule(bm, (-2.2, y, z - 0.03), (-2.05, y, z - 0.01), 0.006, 0.006, seg=8)
        bmesh.ops.create_cone(bm, cap_ends=True, segments=16, radius1=0.03, radius2=0.035, depth=0.012,
                              matrix=Matrix.Translation((-2.05, y, z - 0.006)))
        s = bm_obj(bm, f'braccio_{i}', 'set')
        s.data.materials.append(iron)
        add_point(f'luce_candela_{i}', (-2.0, y, z + h + 0.02), 9.0, (1.0, 0.58, 0.3), 0.015, 'set')
    win = plane('finestra', 1.0, (1.5, 2.58, 2.3), (radians(90), 0, 0), mat_emit('vetro', (0.42, 0.58, 1.0), 2.2))
    win.scale = (0.45, 1.3, 1.0)
    lw = bpy.data.lights.new('luce_finestra', 'AREA')
    lw.energy, lw.size, lw.color = 220, 0.9, (0.55, 0.68, 1.0)
    ow = bpy.data.objects.new('luce_finestra', lw)
    ow.location = (1.4, 2.3, 2.3)
    look_at(ow, (0.0, -0.1, 1.15))
    link(ow, 'set')
    lk = bpy.data.lights.new('candele_fill', 'AREA')
    lk.energy, lk.size, lk.color = 75, 0.6, (1.0, 0.64, 0.40)
    ok = bpy.data.objects.new('candele_fill', lk)
    ok.location = (-1.6, -1.2, 1.6)
    look_at(ok, (0.0, -0.15, 1.3))
    link(ok, 'set')
    add_point('luce_occhietto', (-0.052, -0.23, 1.385), 0.25, (0.25, 0.6, 1.0), 0.004, 'set')


def build_studio():
    w = bpy.data.lights
    for nm, loc, tgt, pw, sz in (('studio_key', (-1.9, -2.3, 2.4), (0, -0.1, 1.1), 420, 1.6),
                                 ('studio_fill', (2.4, -1.6, 1.4), (0, -0.1, 1.0), 120, 2.2),
                                 ('studio_rim', (0.9, 2.2, 2.3), (0, 0, 1.3), 320, 1.2)):
        l = w.new(nm, 'AREA')
        l.energy, l.size = pw, sz
        ob = bpy.data.objects.new(nm, l)
        ob.location = loc
        look_at(ob, tgt)
        link(ob, 'studio')
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=4.0)
    fl = bm_obj(bm, 'studio_pavimento', 'studio')
    gm = bpy.data.materials.new('grigio')
    NT(gm).bsdf({'Base Color': (0.16, 0.16, 0.165), 'Roughness': 0.7})
    fl.data.materials.append(gm)


def set_visible(coll_name, vis):
    c = bpy.data.collections.get(coll_name)
    if c:
        c.hide_render = not vis


def stage_render():
    bpy.ops.wm.open_mainfile(filepath=MAT_BLEND)
    sc = bpy.context.scene
    hq = 'hq' in ARGS
    setup_cycles(256 if hq else 96, (1408, 768))
    w = bpy.data.worlds.new('mondo')
    sc.world = w
    w.use_nodes = True
    bg = w.node_tree.nodes['Background']
    fog = w.node_tree.nodes.new('ShaderNodeVolumePrincipled')
    fog.inputs['Anisotropy'].default_value = 0.45
    w.node_tree.links.new(fog.outputs[0], w.node_tree.nodes['World Output'].inputs['Volume'])
    build_set()
    build_studio()
    os.makedirs(RENDER_DIR, exist_ok=True)
    shots = {
        'eroe':  ('set', (-0.55, -3.55, 1.0), (0.42, -0.1, 0.9), 30, (1408, 768)),
        'volto': ('set', (-0.42, -1.25, 1.47), (0.0, -0.22, 1.47), 75, (1100, 1100)),
        'mano':  ('set', (0.95, -1.3, 1.12), (0.27, -0.34, 1.12), 70, (1100, 1100)),
        'fronte': ('studio', (0.0, -3.4, 0.95), (0.0, -0.08, 0.86), 42, (900, 1200)),
        'lato':  ('studio', (3.4, -0.4, 0.95), (0.0, -0.08, 0.86), 42, (900, 1200)),
        'retro': ('studio', (0.4, 3.4, 0.95), (0.0, -0.08, 0.86), 42, (900, 1200)),
        'tq':    ('studio', (-2.4, -2.5, 1.05), (0.0, -0.08, 0.86), 42, (900, 1200)),
    }
    pick = [a for a in ARGS if a in shots] or list(shots)
    for nm in pick:
        env, loc, tgt, lens, res = shots[nm]
        set_visible('set', env == 'set')
        set_visible('studio', env == 'studio')
        bg.inputs['Color'].default_value = (0.0025, 0.003, 0.0045, 1) if env == 'set' else (0.035, 0.036, 0.04, 1)
        fog.inputs['Density'].default_value = 0.012 if env == 'set' else 0.0
        sc.render.resolution_x, sc.render.resolution_y = res
        add_camera(loc, tgt, lens)
        sc.render.filepath = os.path.join(RENDER_DIR, f'{nm}' + ('_filmic' if 'filmic' in ARGS else '') + '.png')
        t = time.time()
        bpy.ops.render.render(write_still=True)
        print(f'[render] {sc.render.filepath} in {time.time() - t:.1f}s')


# ----------------------------------------------------------------------------- esportazione
TEX_DIR = os.path.join(EXPORT_DIR, 'textures')
EXPORT_BLEND = os.path.join(EXPORT_DIR, 'monaco_export.blend')
RIG_BLEND = os.path.join(EXPORT_DIR, 'monaco_animato.blend')
FPS = 30
BAKE_SETS = [  # nome, oggetti, risoluzione, uv
    ('stoffa', ['tunica', 'cappuccio', 'manica_destra', 'manica_sinistra'], 4096, 'pack'),
    ('pelle', ['mano_destra', 'mano_sinistra'], 2048, 'smart'),
    ('volto', ['testa'], 1024, 'smart'),
    ('occhio', ['occhio'], 2048, 'eye'),
    ('legno', ['bastone', 'bastone_cima'], 2048, 'smart'),
    ('sottoveste', ['corpo'], 1024, 'smart'),
    ('metallo', ['collana'], 512, 'smart'),
]
# rapporto di decimazione per la versione da gioco (la stoffa senza spessore; lo spessore si rimette dopo)
BAKE_RAY = {'stoffa': (0.008, 0.03), 'pelle': (0.002, 0.005), 'volto': (0.005, 0.012), 'occhio': (0.002, 0.004),
            'legno': (0.003, 0.008), 'sottoveste': (0.006, 0.02), 'metallo': (0.0015, 0.004)}
DECIMATE = {'tunica': 0.35, 'cappuccio': 0.35, 'manica_destra': 0.4, 'manica_sinistra': 0.4,
            'mano_destra': 0.1, 'mano_sinistra': 0.1, 'testa': 0.15, 'bastone_cima': 0.1,
            'corpo': 0.3, 'collana': 0.5}


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


def make_bake_uv(ob, mode):
    me = ob.data
    if mode == 'eye':
        uvd = me.uv_layers.new(name='bake')
        for poly in me.polygons:
            cz = sum(me.vertices[vi].co.z for vi in poly.vertices) / len(poly.vertices)
            for li in poly.loop_indices:
                co = me.vertices[me.loops[li].vertex_index].co
                if cz >= 0.0:
                    uvd.data[li].uv = (0.5 + 0.48 * co.x / EYE_R, 0.5 + 0.48 * co.y / EYE_R)
                else:
                    uvd.data[li].uv = (0.94 + 0.05 * co.x / EYE_R, 0.94 + 0.05 * co.y / EYE_R)
        me.uv_layers.active = uvd
        uvd.active_render = True
        return
    src = me.uv_layers.get('tessuto') if mode == 'pack' else None
    if src is not None:
        me.uv_layers.active = src
    uv = me.uv_layers.new(name='bake', do_init=src is not None)
    me.uv_layers.active = uv
    uv.active_render = True
    select_only([ob])
    bpy.context.scene.tool_settings.use_uv_select_sync = True
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    if mode == 'smart':
        bpy.ops.uv.smart_project(angle_limit=radians(66), island_margin=0.003)
    bpy.ops.uv.pack_islands(margin=0.003, rotate=True)
    bpy.ops.object.mode_set(mode='OBJECT')


def bake_set(name, ob, res, high=None):
    """Bake su 'ob'. Se c'e' 'high', i dettagli vengono proiettati dalla versione pesante (selected to active)."""
    os.makedirs(TEX_DIR, exist_ok=True)
    mats = [m for m in ob.data.materials if m]
    imgs = {}
    # raggi corti dove le superfici sono vicine (dita a pochi mm l'una dall'altra)
    ext, ray = BAKE_RAY.get(name, (0.006, 0.02))
    s2a = {'use_selected_to_active': True, 'cage_extrusion': ext, 'max_ray_distance': ray} if high else {}
    passes = (('base', 'DIFFUSE', 'sRGB', {'pass_filter': {'COLOR'}, **s2a}),
              ('rough', 'ROUGHNESS', 'Non-Color', dict(s2a)),
              ('normal', 'NORMAL', 'Non-Color', {'normal_space': 'TANGENT', **s2a}))
    select_only([high, ob] if high else [ob], ob)
    for kind, btype, cs, kw in passes:
        img = bpy.data.images.new(f'monaco_{name}_{kind}', res, res, alpha=False)
        img.colorspace_settings.name = cs
        for m in mats:
            nt = m.node_tree
            n = nt.nodes.get('__bake__') or nt.nodes.new('ShaderNodeTexImage')
            n.name = '__bake__'
            n.image = img
            nt.nodes.active = n
        t = time.time()
        bpy.ops.object.bake(type=btype, margin=16, use_clear=True, **kw)
        img.filepath_raw = os.path.join(TEX_DIR, f'monaco_{name}_{kind}.png')
        img.file_format = 'PNG'
        img.save()
        imgs[kind] = img
        print(f'[bake] {name}/{kind} {res}px in {time.time() - t:.1f}s')
    for m in mats:
        n = m.node_tree.nodes.get('__bake__')
        if n:
            m.node_tree.nodes.remove(n)
    return imgs


def unity_mask(name, rough_img, metallic):
    """RGBA per URP Lit: R = metallico, A = levigatezza (1 - ruvidita')."""
    import numpy as np
    w, h = rough_img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    rough_img.pixels.foreach_get(px)
    px = px.reshape(-1, 4)
    out = np.zeros_like(px)
    out[:, 0] = metallic
    out[:, 3] = 1.0 - px[:, 0]
    img = bpy.data.images.new(f'monaco_{name}_mask_unity', w, h, alpha=True)
    img.colorspace_settings.name = 'Non-Color'
    img.pixels.foreach_set(out.ravel())
    img.filepath_raw = os.path.join(TEX_DIR, f'monaco_{name}_mask_unity.png')
    img.file_format = 'PNG'
    img.save()


def export_material(name, imgs, metallic=0.0, tint=None):
    m = bpy.data.materials.new('M_' + name)
    t = NT(m)
    b = t.node('ShaderNodeTexImage', image=imgs['base'])
    r = t.node('ShaderNodeTexImage', image=imgs['rough'])
    nn = t.node('ShaderNodeTexImage', image=imgs['normal'])
    nm = t.node('ShaderNodeNormalMap')
    t.nt.links.new(nn.outputs['Color'], nm.inputs['Color'])
    base = b.outputs['Color'] if tint is None else t.mix(1.0, b.outputs['Color'], tint, 'MULTIPLY')
    t.bsdf({'Base Color': base, 'Roughness': r.outputs['Color'], 'Normal': nm.outputs['Normal'], 'Metallic': metallic})
    return m


def stage_export():
    bpy.ops.wm.open_mainfile(filepath=MAT_BLEND)
    sc = bpy.context.scene
    setup_cycles(int(os.environ.get('MONACO_BAKE_SAMPLES', '48')), (512, 512))
    sc.render.bake.margin = 16
    o = bpy.data.objects
    # niente spessore durante il bake: l'interno condivide le UV dell'esterno
    for ob in o:
        for md in ob.modifiers:
            if md.type == 'SOLIDIFY':
                md.show_render = md.show_viewport = False
    # pesi dello scheletro: calcolati pezzo per pezzo prima di unire (dopo l'unione non si sa piu' chi e' chi)
    G = Vector(o['bastone']['presa'])
    sc['presa'] = list(G)
    skins = skin_functions(bone_specs(G))
    final = []
    for name, names, res, uvmode in BAKE_SETS:
        obs = [o[n] for n in names if n in o]
        if not obs:
            continue
        solid = any(md.type == 'SOLIDIFY' for ob in obs for md in ob.modifiers)
        # versione leggera: copie decimate pezzo per pezzo (le pieghe restano, i dettagli vanno nella normal map)
        lows = []
        for ob in obs:
            lo = ob.copy()
            lo.data = ob.data.copy()
            link(lo)
            lo.modifiers.clear()
            r = DECIMATE.get(ob.name, 1.0)
            if r < 1.0:
                dm = lo.modifiers.new('decima', 'DECIMATE')
                dm.decimate_type = 'COLLAPSE'
                dm.ratio = r
                apply_all(lo)
            set_weights(lo, skins[ob.name])
            lows.append(lo)
        high = join_objects(name + '_alta', obs)
        low = join_objects(name, lows)
        make_bake_uv(low, uvmode)
        imgs = bake_set(name, low, res, high)
        bpy.data.objects.remove(high)
        metal = 0.85 if name == 'metallo' else 0.0
        unity_mask(name, imgs['rough'], metal)
        mats = [export_material(name, imgs, metal)]
        if name == 'stoffa':
            mats.append(export_material(name + '_interno', imgs, 0.0, (0.5, 0.5, 0.5)))
        assign(low, *mats)
        if solid:
            solidify(low)
        low.data.shade_smooth()
        final.append(low)
    orb = o['occhietto']
    m = bpy.data.materials.new('M_occhietto')
    NT(m).bsdf({'Base Color': (0.08, 0.25, 0.55), 'Roughness': 0.1,
                'Emission Color': (0.22, 0.58, 1.0, 1.0), 'Emission Strength': 22.0})
    assign(orb, m)
    set_weights(orb, skins['occhietto'])
    final.append(orb)
    # applica i modificatori, pulisci attributi e UV
    tris = 0
    for ob in final:
        for md in ob.modifiers:
            md.show_render = md.show_viewport = True
        apply_all(ob)
        if ob.matrix_world != Matrix.Identity(4):        # l'occhio: trasformazione dentro la mesh, serve per lo skinning
            ob.data.transform(ob.matrix_world)
            ob.matrix_world = Matrix.Identity(4)
        me = ob.data
        # per nome, uno alla volta: i riferimenti ai layer non valgono piu' dopo una rimozione
        for nm in [u.name for u in me.uv_layers if u.name != 'bake']:
            me.uv_layers.remove(me.uv_layers[nm])
        if me.uv_layers.get('bake'):
            me.uv_layers['bake'].name = 'UVMap'
        for nm in ('riga', 'col', 'maschere'):
            a = me.attributes.get(nm)
            if a is not None:
                me.attributes.remove(a)
        n = sum(len(p.vertices) - 2 for p in me.polygons)
        tris += n
        print(f'[export] {ob.name}: {n} triangoli, {len(ob.vertex_groups)} gruppi ossa')
    print(f'[export] totale {tris} triangoli')
    # tieni solo la versione da gioco: lo scheletro e l'FBX/GLB li fa la fase 'rig'
    keep = set(final)
    for ob in list(o):
        if ob not in keep:
            bpy.data.objects.remove(ob)
    os.makedirs(EXPORT_DIR, exist_ok=True)
    save_blend(EXPORT_BLEND)
    print('[export] fatto:', EXPORT_BLEND)


# ----------------------------------------------------------------------------- scheletro e pesi
LEG = {'d': (V(-0.10, 0.03, 0.82), V(-0.11, -0.01, 0.46), V(-0.12, 0.03, 0.08), V(-0.13, -0.10, 0.03))}
LEG['s'] = tuple(V(-p.x, p.y, p.z) for p in LEG['d'])


def right_hand_joints():
    """Articolazioni della mano destra (posa rilassata) nel mondo: dita, pollice, direzione del dorso."""
    grip = grip_setup()
    M, _ = hand_frame('r')
    fingers = []
    for (nm, k, sp0, lens, radii), cu in zip(FINGERS, CURL['rilassata']):
        pts, dirs, a = finger_chain(k, sp0, lens, cu)
        fingers.append(([M @ p for p in pts], radii))
    tj, tup = thumb_joints('rilassata', grip)
    return fingers, [M @ p for p in tj], M.to_3x3().col[2].normalized()


def bone_specs(G):
    """Ossa: nome, testa, coda, genitore, direzione a cui puntare l'asse Z locale (rollio)."""
    fingers, thumb, dorsal = right_hand_joints()
    up, front = V(0, 0, 1), V(0, -1, 0)
    Fr = (J['r_wr'] - J['r_el']).normalized()
    Fa = (J['l_wr'] - J['l_el']).normalized()
    Fl = (Fa - STAFF_DIR * Fa.dot(STAFF_DIR)).normalized()

    def avg(i):
        return sum((f[0][i] for f in fingers), Vector()) / len(fingers)
    ea = V(0, -cos(radians(EYE_TILT)), -sin(radians(EYE_TILT)))
    S = [
        ('radice', V(0, 0, 0), V(0, 0, 0.25), None, front),
        ('bacino', V(0, 0.03, 0.86), V(0, 0.03, 0.98), 'radice', front),
        ('spina', V(0, 0.03, 0.98), V(0, 0.015, 1.10), 'bacino', front),
        ('petto', V(0, 0.015, 1.10), V(0, -0.02, 1.24), 'spina', front),
        ('collo', V(0, -0.03, 1.24), V(0, -0.07, 1.34), 'petto', front),
        ('testa', V(0, -0.07, 1.34), V(0, -0.09, 1.62), 'collo', front),
        ('occhio', EYE_C.copy(), EYE_C + ea * 0.10, 'testa', up),
        ('clavicola_d', V(-0.03, -0.01, 1.235), J['r_sh'].copy(), 'petto', front),
        ('braccio_d', J['r_sh'].copy(), J['r_el'].copy(), 'clavicola_d', front),
        ('avambraccio_d', J['r_el'].copy(), J['r_wr'].copy(), 'braccio_d', front),
        ('mano_d', J['r_wr'].copy(), J['r_wr'] + Fr * 0.085, 'avambraccio_d', front),
        ('dita1_d', avg(0), avg(1), 'mano_d', dorsal),
        ('dita2_d', avg(1), avg(3), 'dita1_d', dorsal),
        ('pollice1_d', thumb[1], thumb[2], 'mano_d', dorsal),
        ('pollice2_d', thumb[2], thumb[3], 'pollice1_d', dorsal),
        ('clavicola_s', V(0.03, -0.01, 1.235), J['l_sh'].copy(), 'petto', front),
        ('braccio_s', J['l_sh'].copy(), J['l_el'].copy(), 'clavicola_s', front),
        ('avambraccio_s', J['l_el'].copy(), J['l_wr'].copy(), 'braccio_s', up),
        ('mano_s', J['l_wr'].copy(), J['l_wr'] + Fl * 0.085, 'avambraccio_s', up),
        # il bastone e' indipendente (resta piantato, cade da solo); il braccio sinistro lo insegue con l'IK
        ('bastone', G.copy(), G + STAFF_DIR * 0.40, None, front),
        ('presa_s', J['l_wr'].copy(), J['l_wr'] + Fl * 0.085, 'bastone', up),
    ]
    for s in ('d', 's'):
        hip, kn, an, toe = LEG[s]
        S += [('coscia_' + s, hip, kn, 'bacino', front), ('stinco_' + s, kn, an, 'coscia_' + s, front),
              ('piede_' + s, an, toe, 'stinco_' + s, up)]
    return S


def seg_tp(p, a, b):
    ab = b - a
    L2 = ab.length_squared
    t = 0.0 if L2 < 1e-12 else max(0.0, min(1.0, (p - a).dot(ab) / L2))
    return t, (p - (a + ab * t)).length


def skin_functions(specs):
    """Una funzione per pezzo: posizione nel mondo -> {osso: peso}. Regole per zona, non 'bone heat'
    (che sulla stoffa bucata e sottile fallisce)."""
    SEG = {nm: (h, t) for nm, h, t, par, z in specs}
    fingers, thumb, dorsal = right_hand_joints()

    def robe(p):
        z, x = p.z, p.x
        pet = smoothstep(1.02, 1.14, z)
        spi = smoothstep(0.84, 0.98, z) * (1 - pet)
        bac = 1.0 - pet - spi
        lf = 0.6 * smoothstep(0.85, 0.30, z)          # l'orlo segue le gambe solo in parte
        s = smoothstep(-0.07, 0.07, x)
        sh = 0.35 * smoothstep(0.56, 0.38, z)           # poco stinco: in ginocchio l'orlo non deve girare col polpaccio
        w = {'petto': pet, 'spina': spi, 'bacino': bac * (1 - lf)}
        for side, f in (('d', 1 - s), ('s', s)):
            w['coscia_' + side] = bac * lf * f * (1 - sh)
            w['stinco_' + side] = bac * lf * f * sh
        return w

    def hood(p):
        z, ax = p.z, abs(p.x)
        side = 's' if p.x > 0 else 'd'
        hd = smoothstep(1.28, 1.37, z)
        nk = (1 - hd) * smoothstep(1.20, 1.28, z)
        rest = 1 - hd - nk
        cl = rest * 0.45 * smoothstep(0.10, 0.26, ax)
        br = rest * 0.35 * smoothstep(0.22, 0.34, ax)
        return {'testa': hd, 'collo': nk, 'clavicola_' + side: cl, 'braccio_' + side: br, 'petto': rest - cl - br}

    def sleeve(side):
        jk = {'d': 'r', 's': 'l'}[side]                 # J usa r/l, le ossa d/s
        sh, el, wr = J[jk + '_sh'], J[jk + '_el'], J[jk + '_wr']
        end = wr + (wr - el).normalized() * 0.06

        def f(p):
            t1, d1 = seg_tp(p, sh, el)
            t2, d2 = seg_tp(p, el, end)
            a, b = d1 ** 4, d2 ** 4
            fore = a / (a + b + 1e-12)
            top = 0.5 * smoothstep(0.30, 0.0, t1) * (1 - fore)
            return {'clavicola_' + side: top, 'braccio_' + side: 1 - fore - top, 'avambraccio_' + side: fore}
        return f

    def hand(side):
        jk = {'d': 'r', 's': 'l'}[side]
        W = J[jk + '_wr']
        F = (J[jk + '_wr'] - J[jk + '_el']).normalized()

        def f(p):
            hw = smoothstep(-0.02, 0.012, (p - W).dot(F))
            w = {'avambraccio_' + side: 1 - hw, 'mano_' + side: hw}
            if side != 'd' or hw <= 0:
                return w
            best = None
            for pts, radii in fingers:
                for i in range(3):
                    t, d = seg_tp(p, pts[i], pts[i + 1])
                    r = radii[i] * (1 - t) + radii[i + 1] * t
                    if d < r * 1.9 and (best is None or d - r < best[0]):
                        best = (d - r, i, t)
            for i in (1, 2):
                t, d = seg_tp(p, thumb[i], thumb[i + 1])
                r = THUMB_R[i] * (1 - t) + THUMB_R[i + 1] * t
                if d < r * 1.9 and (best is None or d - r < best[0]):
                    best = (d - r, 10 + i, t)
            if best is None:
                return w
            _, i, t = best
            if i == 0:                                   # falange prossimale
                k = smoothstep(0.0, 0.35, t)
                w['mano_d'], w['dita1_d'] = hw * (1 - k), hw * k
            elif i in (1, 2):                            # media e distale
                k = smoothstep(0.0, 0.25, t) if i == 1 else 1.0
                w['mano_d'] = 0.0
                w['dita1_d'], w['dita2_d'] = hw * (1 - k), hw * k
            elif i == 11:
                k = smoothstep(0.0, 0.3, t)
                w['mano_d'], w['pollice1_d'] = hw * (1 - k), hw * k
            else:
                k = smoothstep(0.0, 0.25, t)
                w['mano_d'] = 0.0
                w['pollice1_d'], w['pollice2_d'] = hw * (1 - k), hw * k
            return w
        return f

    def face(p):
        hd = smoothstep(1.25, 1.33, p.z)
        return {'testa': hd, 'collo': 1 - hd}

    body_bones = [nm for nm, h, t, par, z in specs
                  if nm not in ('radice', 'occhio', 'bastone', 'presa_s') and not nm.startswith(('dita', 'pollice'))]

    def body(p):
        ws = sorted(((1.0 / (seg_tp(p, *SEG[nm])[1] ** 4 + 1e-7), nm) for nm in body_bones), reverse=True)
        return {nm: x for x, nm in ws[:3]}

    def one(b):
        return lambda p: {b: 1.0}
    return {'tunica': robe, 'cappuccio': hood, 'manica_destra': sleeve('d'), 'manica_sinistra': sleeve('s'),
            'mano_destra': hand('d'), 'mano_sinistra': hand('s'), 'testa': face, 'occhio': one('occhio'),
            'occhietto': one('testa'), 'bastone': one('bastone'), 'bastone_cima': one('bastone'),
            'corpo': body, 'collana': one('petto')}


def set_weights(ob, fn):
    """Gruppi di vertici con al massimo 4 ossa per vertice (limite di Unity), normalizzati."""
    groups = {}
    mw = ob.matrix_world
    for v in ob.data.vertices:
        w = fn(mw @ v.co)
        top = sorted(((x, b) for b, x in w.items() if x > 1e-4), reverse=True)[:4]
        s = sum(x for x, b in top)
        if s <= 0:
            continue
        for x, b in top:
            g = groups.get(b)
            if g is None:
                g = groups[b] = ob.vertex_groups.get(b) or ob.vertex_groups.new(name=b)
            g.add([v.index], x / s, 'REPLACE')


def build_armature(specs):
    ad = bpy.data.armatures.new('Monaco')
    ad.display_type = 'STICK'
    arm = bpy.data.objects.new('Monaco', ad)
    link(arm)
    select_only([arm])
    bpy.ops.object.mode_set(mode='EDIT')
    ebs = {}
    for nm, h, t, par, z in specs:
        eb = ad.edit_bones.new(nm)
        eb.head, eb.tail = h, t
        d = (t - h).normalized()
        zz = z - d * z.dot(d)
        if zz.length < 1e-3:
            zz = V(1, 0, 0) - d * d.x
        eb.align_roll(zz.normalized())
        if par:
            eb.parent = ebs[par]
        eb.use_deform = nm not in ('radice', 'presa_s')
        ebs[nm] = eb
    bpy.ops.object.mode_set(mode='OBJECT')
    pbs = arm.pose.bones
    for pb in pbs:
        pb.rotation_mode = 'QUATERNION'
    # braccio sinistro: il polso va dove sta la presa sul bastone, la mano ne copia l'orientamento
    ik = pbs['avambraccio_s'].constraints.new('IK')
    ik.name = 'IK'
    ik.target, ik.subtarget = arm, 'presa_s'
    ik.chain_count = 2
    cr = pbs['mano_s'].constraints.new('COPY_ROTATION')
    cr.name = 'Presa'
    cr.target, cr.subtarget = arm, 'presa_s'
    return arm


# ----------------------------------------------------------------------------- animazioni
# Rotazioni in gradi sugli assi del mondo (X = sinistra del monaco, Y = dietro, Z = su), relative al genitore.
# +X piega in avanti un osso che punta in su e porta indietro uno che punta in giu'; +Z gira verso la sua sinistra;
# +Y inclina verso la sua sinistra. Per l'occhio: +X guarda in basso, +Z guarda alla sua sinistra.
# 'rot_local' (dita): assi locali, -X chiude il dito verso il palmo.
def cyc(keys, n, step=2):
    """Curva ciclica (frame, tupla) campionata ogni 'step' frame con Catmull-Rom: niente scatti al giro."""
    ks = sorted(keys)
    ext = [(f - n, v) for f, v in ks] + ks + [(f + n, v) for f, v in ks]
    out = []
    for fr in range(0, n + 1, step):
        t = fr % n
        i = max(j for j in range(1, len(ext) - 2) if ext[j][0] <= t)
        (_, a), (f1, b), (f2, c), (_, d) = ext[i - 1], ext[i], ext[i + 1], ext[i + 2]
        u = (t - f1) / (f2 - f1)
        out.append((fr, tuple(0.5 * (2 * q1 + (-q0 + q2) * u + (2 * q0 - 5 * q1 + 4 * q2 - q3) * u * u
                                     + (-q0 + 3 * q1 - 3 * q2 + q3) * u ** 3)
                              for q0, q1, q2, q3 in zip(a, b, c, d))))
    return out


def anim_idle():
    R = {
        'petto': [(0, (0, 0, 0)), (30, (-1.6, 0, 0)), (60, (0, 0, 0)), (90, (-1.3, 0, 0)), (120, (0, 0, 0))],
        'spina': [(0, (0, 0, 0)), (30, (-0.7, 0, 0)), (60, (0, 0, 0)), (90, (-0.5, 0, 0)), (120, (0, 0, 0))],
        'bacino': [(0, (0, 0, 0)), (60, (0, 1.0, 0.8)), (120, (0, 0, 0))],
        'testa': [(0, (0, 0, 0)), (40, (1.5, 3.0, -4.0)), (85, (-1.0, -2.0, 4.5)), (120, (0, 0, 0))],
        'occhio': [(0, (0, 0, 0)), (12, (0, 0, 0)), (15, (-2, 0, 18)), (36, (-2, 0, 17)), (39, (6, 0, -14)),
                   (68, (5, 0, -13)), (71, (0, 0, 0)), (94, (0, 0, 1)), (97, (-8, 0, 7)), (114, (-8, 0, 6)),
                   (117, (0, 0, 0)), (120, (0, 0, 0))],
        'braccio_d': [(0, (0, 0, 0)), (60, (-2.5, 0, 0)), (120, (0, 0, 0))],
        'avambraccio_d': [(0, (0, 0, 0)), (50, (-4, 0, 0)), (100, (1, 0, 0)), (120, (0, 0, 0))],
        'bastone': [(0, (0, 0, 0)), (60, (0.6, 0.4, 0)), (120, (0, 0, 0))],
    }
    RL = {
        'dita1_d': [(0, (0, 0, 0)), (48, (0, 0, 0)), (54, (-14, 0, 0)), (60, (-4, 0, 0)), (66, (-16, 0, 0)),
                    (78, (0, 0, 0)), (120, (0, 0, 0))],
        'dita2_d': [(0, (0, 0, 0)), (48, (0, 0, 0)), (54, (-18, 0, 0)), (60, (-5, 0, 0)), (66, (-20, 0, 0)),
                    (78, (0, 0, 0)), (120, (0, 0, 0))],
    }
    return dict(n=120, loop=True, rot=R, rot_local=RL)


def anim_walk():
    """Passo strascicato in loco, 36 frame = 1.2 s, ~1 m/s. Il bastone si pianta col piede destro: mentre
    poggia, la punta scorre indietro alla stessa velocita' del piede, poi si alza e torna avanti."""
    n, h = 36, 18

    def sh(keys, off):
        return [((f + off) % n, (v, 0, 0)) for f, v in keys]
    thigh = [(0, -22), (9, -4), (18, 16), (22, 12), (27, -8), (32, -20)]
    shin = [(0, 4), (4, 12), (9, 5), (18, 12), (22, 30), (27, 42), (32, 16)]
    foot = [(0, -10), (4, 0), (9, 0), (18, 10), (22, 22), (27, 6), (32, -6)]
    R = {}
    for side, off in (('d', 0), ('s', h)):
        R['coscia_' + side] = cyc(sh(thigh, off), n)
        R['stinco_' + side] = cyc(sh(shin, off), n)
        R['piede_' + side] = cyc(sh(foot, off), n)
    R['bacino'] = cyc([(0, (0, 0, 5)), (9, (0, 2.5, 0)), (18, (0, 0, -5)), (27, (0, -2.5, 0))], n)
    R['spina'] = cyc([(0, (7, 0, -3)), (18, (7, 0, 3))], n)
    R['petto'] = cyc([(0, (3, 0, -3)), (18, (3, 0, 3))], n)
    R['testa'] = cyc([(0, (-7, 0, 2)), (9, (-8, 0, 0)), (18, (-7, 0, -2)), (27, (-8, 0, 0))], n)
    R['braccio_d'] = cyc([(0, (12, 0, 0)), (18, (-12, 0, 0))], n)
    R['avambraccio_d'] = cyc([(0, (-8, 0, 0)), (18, (-14, 0, 0))], n)
    R['occhio'] = cyc([(0, (0, 0, 0)), (12, (0, 0, -5)), (24, (2, 0, 4))], n)
    R['bastone'] = cyc([(0, (-12, 0, 0)), (9, (0, 0, 0)), (18, (12, 0, 0)), (24, (4, 0, 0)), (30, (-8, 0, 0))], n)
    L = {'bacino': cyc([(0, (0, 0, -0.018)), (9, (0, 0, 0.008)), (18, (0, 0, -0.018)), (27, (0, 0, 0.008))], n),
         'bastone': cyc([(0, (0, -0.10, 0)), (9, (0, 0, 0)), (18, (0, 0.10, 0)), (24, (0, 0.06, 0.06)),
                         (30, (0, -0.04, 0.06))], n)}
    return dict(n=n, loop=True, rot=R, loc=L)


def anim_attack():
    """Colpo a terra: solleva il bastone (fotogrammi 0-13), lo pianta con forza al 16 (qui arriva il danno e
    l'onda d'urto in Unity), l'occhio si gonfia; il braccio sinistro segue il bastone con l'IK."""
    R = {
        'bastone': [(0, (0, 0, 0)), (10, (-6, 0, 0)), (13, (-8, 0, 0)), (16, (5, 0, 0)), (19, (5, 0, 0)),
                    (30, (2, 0, 0)), (40, (0, 0, 0))],
        'petto': [(0, (0, 0, 0)), (10, (-9, 0, 4)), (13, (-10, 0, 4)), (16, (12, 0, -5)), (22, (10, 0, -4)),
                  (40, (0, 0, 0))],
        'spina': [(0, (0, 0, 0)), (10, (-4, 0, 2)), (16, (8, 0, -3)), (22, (7, 0, -2)), (40, (0, 0, 0))],
        'bacino': [(0, (0, 0, 0)), (10, (-2, 0, 0)), (16, (4, 0, 0)), (40, (0, 0, 0))],
        'testa': [(0, (0, 0, 0)), (10, (-10, 0, -3)), (13, (-11, 0, -3)), (16, (10, 0, 3)), (22, (8, 0, 2)),
                  (40, (0, 0, 0))],
        'occhio': [(0, (0, 0, 0)), (10, (-6, 0, 0)), (16, (8, 0, 0)), (28, (3, 0, 0)), (40, (0, 0, 0))],
        'braccio_d': [(0, (0, 0, 0)), (10, (-35, 12, 0)), (13, (-38, 12, 0)), (16, (-25, 5, 0)), (22, (-22, 5, 0)),
                      (40, (0, 0, 0))],
        'avambraccio_d': [(0, (0, 0, 0)), (10, (-30, 0, 0)), (16, (-15, 0, 0)), (22, (-14, 0, 0)), (40, (0, 0, 0))],
        'coscia_d': [(0, (0, 0, 0)), (16, (-6, 0, 0)), (24, (-5, 0, 0)), (40, (0, 0, 0))],
        'coscia_s': [(0, (0, 0, 0)), (16, (-8, 0, 0)), (24, (-6, 0, 0)), (40, (0, 0, 0))],
        'stinco_d': [(0, (0, 0, 0)), (16, (12, 0, 0)), (24, (10, 0, 0)), (40, (0, 0, 0))],
        'stinco_s': [(0, (0, 0, 0)), (16, (14, 0, 0)), (24, (11, 0, 0)), (40, (0, 0, 0))],
    }
    RL = {
        'dita1_d': [(0, (0, 0, 0)), (10, (18, 0, 0)), (14, (18, 0, 0)), (17, (-45, 0, 0)), (24, (-40, 0, 0)),
                    (40, (0, 0, 0))],
        'dita2_d': [(0, (0, 0, 0)), (10, (12, 0, 0)), (14, (12, 0, 0)), (17, (-55, 0, 0)), (24, (-50, 0, 0)),
                    (40, (0, 0, 0))],
    }
    L = {'bastone': [(0, (0, 0, 0)), (10, (0, 0.03, 0.36)), (13, (0, 0.03, 0.40)), (16, (0, -0.05, 0.0)),
                     (19, (0, -0.05, -0.004)), (30, (0, -0.02, 0.0)), (40, (0, 0, 0))],
         'bacino': [(0, (0, 0, 0)), (10, (0, 0.02, 0.01)), (16, (0, -0.04, -0.03)), (24, (0, -0.03, -0.025)),
                    (40, (0, 0, 0))]}
    S = {'occhio': [(0, 1.0), (10, 1.1), (16, 1.28), (22, 1.22), (34, 1.0), (40, 1.0)]}
    return dict(n=40, loop=False, rot=R, rot_local=RL, loc=L, scale=S)


def anim_hit():
    R = {
        'petto': [(0, (0, 0, 0)), (4, (-14, 0, 6)), (10, (3, 0, -1)), (18, (0, 0, 0))],
        'spina': [(0, (0, 0, 0)), (4, (-6, 0, 2)), (18, (0, 0, 0))],
        'testa': [(0, (0, 0, 0)), (4, (-16, 4, 8)), (9, (4, 0, -2)), (18, (0, 0, 0))],
        'occhio': [(0, (0, 0, 0)), (4, (-10, 0, 12)), (12, (2, 0, -2)), (18, (0, 0, 0))],
        'braccio_d': [(0, (0, 0, 0)), (4, (-14, 8, 0)), (18, (0, 0, 0))],
    }
    RL = {'dita1_d': [(0, (0, 0, 0)), (4, (-30, 0, 0)), (18, (0, 0, 0))],
          'dita2_d': [(0, (0, 0, 0)), (4, (-35, 0, 0)), (18, (0, 0, 0))]}
    L = {'bacino': [(0, (0, 0, 0)), (4, (0, 0.035, -0.01)), (18, (0, 0, 0))]}
    S = {'occhio': [(0, 1.0), (3, 0.82), (8, 1.06), (18, 1.0)]}
    return dict(n=18, loop=False, rot=R, rot_local=RL, loc=L, scale=S)


def staff_fall_keys(G, frames_angles, slide=(0.04, 0.0, 0.022)):
    """Bastone che cade di lato ruotando attorno alla punta appoggiata a terra: per ogni angolo (attorno
    a Y del mondo) calcola lo spostamento che tiene la punta ferma (la rotazione dell'osso e' attorno a G)."""
    base = G - STAFF_DIR * (G.z / STAFF_DIR.z)
    rot, loc = [], []
    for f, ang, k in frames_angles:
        Rm = Euler((0.0, radians(ang), 0.0), 'XYZ').to_matrix()
        target = base + Vector(slide) * k
        moved = G + Rm @ (base - G)
        rot.append((f, (0.0, ang, 0.0)))
        loc.append((f, tuple(target - moved)))
    return rot, loc


def anim_death(G):
    R = {
        'petto': [(0, (0, 0, 0)), (6, (-14, 0, 8)), (20, (10, 0, 0)), (34, (15, 0, 0)), (46, (-6, 0, 4)), (72, (-7, 0, 4))],
        'spina': [(0, (0, 0, 0)), (6, (-6, 0, 3)), (20, (18, 0, 0)), (34, (25, 0, 0)), (46, (-4, 0, 0)), (72, (-6, 0, 0))],
        'testa': [(0, (0, 0, 0)), (6, (-18, 0, 10)), (20, (12, 0, 0)), (34, (20, 0, 8)), (46, (-12, 0, 18)),
                  (52, (-6, 0, 20)), (72, (-8, 0, 22))],
        'occhio': [(0, (0, 0, 0)), (6, (-15, 0, 0)), (20, (-25, 0, 0)), (46, (-28, 0, 15)), (72, (-30, 0, 20))],
        # in ginocchio, poi lungo disteso: alla fine le gambe tornano quasi dritte (sennò bucano il pavimento)
        'coscia_d': [(0, (0, 0, 0)), (20, (-55, 0, -4)), (34, (-60, 0, -4)), (46, (-18, 0, -6)), (72, (-15, 0, -6))],
        'coscia_s': [(0, (0, 0, 0)), (20, (-55, 0, 4)), (34, (-60, 0, 4)), (46, (-20, 0, 6)), (72, (-16, 0, 6))],
        'stinco_d': [(0, (0, 0, 0)), (20, (95, 0, 0)), (34, (100, 0, 0)), (46, (12, 0, 0)), (72, (6, 0, 0))],
        'stinco_s': [(0, (0, 0, 0)), (20, (95, 0, 0)), (34, (100, 0, 0)), (46, (14, 0, 0)), (72, (7, 0, 0))],
        'piede_d': [(0, (0, 0, 0)), (20, (-35, 0, 0)), (46, (10, 0, 0)), (72, (15, 0, 0))],
        'piede_s': [(0, (0, 0, 0)), (20, (-35, 0, 0)), (46, (10, 0, 0)), (72, (15, 0, 0))],
        # disteso a faccia in giu': braccia lungo i fianchi (tese in avanti finirebbero sotto il pavimento)
        'braccio_d': [(0, (0, 0, 0)), (6, (-10, 6, 0)), (20, (8, 6, 0)), (34, (-30, 10, 0)), (46, (8, 16, 0)),
                      (72, (10, 18, 0))],
        'avambraccio_d': [(0, (0, 0, 0)), (34, (-15, 0, 0)), (46, (-8, 0, 0)), (72, (-6, 0, 0))],
        # braccio sinistro libero (l'IK si spegne al fotogramma 12): pende, poi si allunga in avanti
        'braccio_s': [(0, (0, 0, 0)), (12, (-10, 0, 0)), (20, (-15, 0, 0)), (34, (-28, 0, -10)), (46, (8, -16, 0)),
                      (72, (10, -18, 0))],
        'avambraccio_s': [(0, (0, 0, 0)), (12, (35, 0, 0)), (34, (30, 0, 0)), (46, (40, 0, 0)), (72, (42, 0, 0))],
        'radice': [(0, (0, 0, 0)), (20, (0, 0, 0)), (34, (40, 0, 0)), (46, (87, 0, 0)), (52, (82, 0, 0)),
                   (60, (86, 0, 0)), (72, (86, 0, 0))],
    }
    srot, sloc = staff_fall_keys(G, [(0, 0, 0), (8, 2, 0.1), (16, 18, 0.4), (24, 52, 0.8), (29, 88, 1.0),
                                     (32, 82, 1.0), (36, 88, 1.0), (72, 88, 1.0)])
    R['bastone'] = srot
    RL = {'dita1_d': [(0, (0, 0, 0)), (20, (-20, 0, 0)), (72, (-35, 0, 0))],
          'dita2_d': [(0, (0, 0, 0)), (20, (-25, 0, 0)), (72, (-40, 0, 0))]}
    L = {'bacino': [(0, (0, 0, 0)), (6, (0, 0.03, 0)), (20, (0, 0.02, -0.28)), (34, (0, 0, -0.30)), (72, (0, 0, -0.30))],
         'radice': [(0, (0, 0, 0)), (34, (0, 0, 0.02)), (46, (0, 0, 0.13)), (72, (0, 0, 0.13))],
         'bastone': sloc}
    S = {'occhio': [(0, 1.0), (6, 0.85), (20, 1.0), (72, 1.0)]}
    INF = {('avambraccio_s', 'IK'): [(0, 1.0), (6, 1.0), (12, 0.0), (72, 0.0)],
           ('mano_s', 'Presa'): [(0, 1.0), (6, 1.0), (12, 0.0), (72, 0.0)]}
    return dict(n=72, loop=False, rot=R, rot_local=RL, loc=L, scale=S, influence=INF)


ANIMS = [('fermo', anim_idle), ('cammina', anim_walk), ('attacco', anim_attack), ('colpito', anim_hit),
         ('morte', anim_death)]


def action_fcurves(act):
    try:
        return list(act.fcurves)
    except AttributeError:                      # API a slot di Blender 4.4+
        out = []
        for layer in act.layers:
            for strip in layer.strips:
                for cb in strip.channelbags:
                    out += list(cb.fcurves)
        return out


def make_action(arm, name, n, loop, rot=None, rot_local=None, loc=None, scale=None, influence=None):
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    ad = arm.animation_data or arm.animation_data_create()
    ad.action = act
    pbs = arm.pose.bones
    # ogni azione definisce tutte le ossa: un canale senza chiavi terrebbe il valore dell'azione valutata prima
    # (anche nell'FBX, che le cuoce una dopo l'altra)
    for pb in pbs:
        pb.rotation_quaternion = (1, 0, 0, 0)
        pb.location = (0, 0, 0)
        pb.scale = (1, 1, 1)
        for f in (0, n):
            for path in ('rotation_quaternion', 'location', 'scale'):
                pb.keyframe_insert(path, frame=f, group=pb.name)
    for tracks, local in ((rot or {}, False), (rot_local or {}, True)):
        for bn, keys in tracks.items():
            pb = pbs[bn]
            B = pb.bone.matrix_local.to_3x3()
            Bi = B.inverted()
            prev = None
            for f, r in keys:
                m = Euler([radians(a) for a in r], 'XYZ').to_matrix()
                q = m.to_quaternion() if local else (Bi @ m @ B).to_quaternion()
                if prev is not None and prev.dot(q) < 0:          # stesso emisfero: niente giri lunghi
                    q.negate()
                prev = q
                pb.rotation_quaternion = q
                pb.keyframe_insert('rotation_quaternion', frame=f, group=bn)
    for bn, keys in (loc or {}).items():
        pb = pbs[bn]
        Bi = pb.bone.matrix_local.to_3x3().inverted()
        for f, d in keys:
            pb.location = Bi @ Vector(d)
            pb.keyframe_insert('location', frame=f, group=bn)
    for bn, keys in (scale or {}).items():
        pb = pbs[bn]
        for f, s in keys:
            pb.scale = (s, s, s)
            pb.keyframe_insert('scale', frame=f, group=bn)
    # influenza dei vincoli del braccio sinistro: chiavi in ogni azione, sennò resta quella dell'ultima
    # azione valutata (la morte la porta a 0)
    inf = dict(influence or {})
    for key in (('avambraccio_s', 'IK'), ('mano_s', 'Presa')):
        inf.setdefault(key, [(0, 1.0), (n, 1.0)])
    for (bn, cn), keys in inf.items():
        con = pbs[bn].constraints.get(cn)
        if con is None:
            continue
        for f, v in keys:
            con.influence = v
            con.keyframe_insert('influence', frame=f)
        con.influence = 1.0
    for fc in action_fcurves(act):
        if loop:
            fc.modifiers.new('CYCLES')
        fc.update()
    act.use_frame_range = True
    act.frame_start, act.frame_end = 0, n
    act['loop'] = bool(loop)
    for pb in pbs:
        pb.rotation_quaternion = (1, 0, 0, 0)
        pb.location = (0, 0, 0)
        pb.scale = (1, 1, 1)
    ad.action = None
    return act


def stage_rig():
    bpy.ops.wm.open_mainfile(filepath=EXPORT_BLEND)
    sc = bpy.context.scene
    sc.render.fps = FPS
    G = Vector(list(sc['presa']))
    specs = bone_specs(G)
    meshes = [ob for ob in sc.objects if ob.type == 'MESH']
    arm = build_armature(specs)
    for ob in meshes:
        ob.parent = arm
        md = ob.modifiers.new('Armatura', 'ARMATURE')
        md.object = arm
        missing = [vg.name for vg in ob.vertex_groups if vg.name not in arm.data.bones]
        if missing:
            print(f'[rig] {ob.name}: gruppi senza osso {missing}')
    acts = [make_action(arm, nm, **(fn(G) if fn is anim_death else fn())) for nm, fn in ANIMS]
    arm.animation_data.action = acts[0]
    sc.frame_start, sc.frame_end = 0, 120
    save_blend(RIG_BLEND)
    select_only(meshes + [arm], arm)
    bpy.ops.export_scene.fbx(filepath=os.path.join(EXPORT_DIR, 'monaco.fbx'), use_selection=True,
                             object_types={'ARMATURE', 'MESH'}, apply_unit_scale=True,
                             apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
                             mesh_smooth_type='FACE', use_tspace=True, add_leaf_bones=False,
                             primary_bone_axis='Y', secondary_bone_axis='X', armature_nodetype='NULL',
                             bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                             bake_anim_use_all_bones=True, bake_anim_force_startend_keying=True,
                             bake_anim_step=1.0, bake_anim_simplify_factor=0.0,
                             path_mode='RELATIVE', embed_textures=False)
    bpy.ops.export_scene.gltf(filepath=os.path.join(EXPORT_DIR, 'monaco.glb'), export_format='GLB',
                              use_selection=True, export_apply=False, export_skins=True,
                              export_animations=True, export_animation_mode='ACTIONS')
    print('[rig] ossa:', len(arm.data.bones), ' azioni:', [a.name for a in acts])


POSE_FRAMES = {'fermo': [0, 40, 85], 'cammina': [0, 9, 18, 27], 'attacco': [0, 10, 13, 16, 24, 40],
               'colpito': [0, 4, 10], 'morte': [0, 6, 16, 24, 34, 46, 72]}


def stage_pose():
    """Fotogrammi chiave di ogni animazione, per controllare pose e deformazioni."""
    bpy.ops.wm.open_mainfile(filepath=RIG_BLEND)
    sc = bpy.context.scene
    setup_cycles(24, (420, 560))
    w = bpy.data.worlds.new('mondo')
    sc.world = w
    w.use_nodes = True
    w.node_tree.nodes['Background'].inputs['Color'].default_value = (0.035, 0.036, 0.04, 1)
    build_studio()
    arm = bpy.data.objects['Monaco']
    out = os.path.join(RENDER_DIR, 'pose')
    os.makedirs(out, exist_ok=True)
    pick = [a for a in ARGS if a in POSE_FRAMES] or list(POSE_FRAMES)
    for nm in pick:
        arm.animation_data.action = bpy.data.actions[nm]
        if nm == 'morte':
            sc.render.resolution_x, sc.render.resolution_y = 560, 420
            add_camera((3.4, -1.6, 1.3), (0.0, -0.7, 0.45), 32)
        else:
            sc.render.resolution_x, sc.render.resolution_y = 420, 560
            add_camera((-2.3, -2.9, 1.15), (0.05, -0.15, 0.85), 42)
        for f in POSE_FRAMES[nm]:
            sc.frame_set(f)
            sc.render.filepath = os.path.join(out, f'{nm}_{f:03d}.png')
            bpy.ops.render.render(write_still=True)
        print('[pose]', nm, POSE_FRAMES[nm])


if __name__ == '__main__':
    if 'geo' in ARGS:
        stage_geo()
    if 'clay' in ARGS:
        stage_clay()
    if 'mat' in ARGS:
        stage_mat()
    if 'render' in ARGS:
        stage_render()
    if 'export' in ARGS:
        stage_export()
    if 'rig' in ARGS:
        stage_rig()
    if 'pose' in ARGS:
        stage_pose()
