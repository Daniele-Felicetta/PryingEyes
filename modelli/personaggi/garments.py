"""
Garment and gear builders (high poly). Each builder returns Blender objects linked to the scene, already carrying their
material. The garments are closed solids grown from the body proxy (organic() unions) plus thin shells (solidified surfaces),
bands, straps and small hard-surface parts; the bake turns all of it into textures on the low-poly meshes.
"""
from kit import *
import skeleton as sk
import body as bd
from mathutils import Euler


def ZR(a):
    return Euler((0, 0, a)).to_matrix()


def solidify(ob, thickness, offset=-1.0):
    m = ob.modifiers.new('solid', 'SOLIDIFY')
    m.thickness = thickness
    m.offset = offset
    m.use_even_offset = True
    apply_all(ob)


def subdivide(ob, level=1):
    m = ob.modifiers.new('sub', 'SUBSURF')
    m.levels = m.render_levels = level
    apply_all(ob)
    ob.data.shade_smooth()


def finish(bm, name, mat, smooth=True):
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = bm_obj(bm, name)
    if smooth:
        ob.data.shade_smooth()
    assign(ob, mat)
    return ob


# ----------------------------------------------------------------------------- hard-surface helpers
def bm_band(bm, cx, cy, z, rx, ry, height, thick, seg=72):
    """Elliptical band with a rectangular section: inner ellipse (rx, ry), thickness 'thick' outwards, centred on height z."""
    rings = []
    for i in range(seg):
        a = 2 * pi * i / seg
        ca, sa = cos(a), sin(a)
        rings.append([bm.verts.new(V(cx + (rx + o) * ca, cy + (ry + o) * sa, z + dz))
                      for o, dz in ((0.0, -height / 2), (0.0, height / 2), (thick, height / 2), (thick, -height / 2))])
    for i in range(seg):
        a, b = rings[i], rings[(i + 1) % seg]
        for k in range(4):
            bm.faces.new((a[k], b[k], b[(k + 1) % 4], a[(k + 1) % 4]))


def bm_ribbon(bm, pts, width, thick, up=V(0, 0, 1), closed=False, cap=True):
    """Flat strap along a polyline: width across 'side', thickness along the strap normal."""
    n = len(pts)
    rows = []
    for i in range(n):
        t = (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized()
        side = t.cross(up)
        if side.length < 1e-6:
            side = t.cross(V(1, 0, 0))
        side.normalize()
        nrm = side.cross(t).normalized()
        w2, t2 = width / 2, thick / 2
        rows.append([bm.verts.new(pts[i] + side * sx * w2 + nrm * sn * t2)
                     for sx, sn in ((-1, -1), (1, -1), (1, 1), (-1, 1))])
    for i in range(n - 1):
        a, b = rows[i], rows[i + 1]
        for k in range(4):
            bm.faces.new((a[k], a[(k + 1) % 4], b[(k + 1) % 4], b[k]))
    if cap:
        bm.faces.new(rows[0][::-1])
        bm.faces.new(rows[-1])


def bm_cyl(bm, a, b, r, seg=12):
    a, b = Vector(a), Vector(b)
    d = b - a
    q = d.to_track_quat('Z', 'Y')
    M = Matrix.Translation((a + b) / 2) @ q.to_matrix().to_4x4()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=seg, radius1=r, radius2=r, depth=d.length, matrix=M)


def on_torso(P, z, x, g, off=0.0):
    """Point on the front of the torso at height z and lateral offset x (inflated by g) and the rotation that faces outwards."""
    rx, ry, cy = bd.torso_section(z, P, g)
    x = clamp(x, -rx * 0.97, rx * 0.97)
    s = sqrt(max(0.0, 1.0 - (x / rx) ** 2))
    nx, ny = x / (rx * rx), -s / ry
    ln = sqrt(nx * nx + ny * ny)
    nx, ny = nx / ln, ny / ln
    ang = atan2(nx, -ny)
    return V(x, cy - ry * s, z) + V(nx, ny, 0.0) * off, ZR(ang), V(nx, ny, 0.0)


def wrinkles(ob, amp=0.0016, freq=46.0, seed=1.0, gain=2.2, stretch=0.55):
    """Fold relief: noise along the normals, stronger around the joints (elbows, knees, shoulders, hips, waist)."""
    me = ob.data
    off1, off2 = V(seed * 11.3, seed * 7.7, seed * 3.1), V(seed * 5.9, seed * 13.1, seed * 9.3)
    joints = []
    for k in ('elbow', 'knee', 'shoulder', 'hip'):
        joints += [sk.J[k], sk.mirror(sk.J[k])]
    joints += [V(0, 0, 1.06), V(0, 0, 1.30)]
    for v in me.vertices:
        p = v.co
        jw = 0.0
        for j in joints:
            d = (p - j).length
            if d < 0.19:
                jw = max(jw, exp(-(d / 0.085) ** 2))
        q = V(p.x * freq, p.y * freq, p.z * freq * stretch)
        f = noise.noise(q + off1) + 0.5 * noise.noise(V(p.x, p.y, p.z) * freq * 2.3 + off2)
        v.co = p + v.normal * (amp * (0.45 + gain * jw) * f)
    me.update()


def shell(name, mat, center, radii, keep, thick, offset=-1.0, rot=None, seg=48, sub=1):
    """Open piece of an ellipsoid (faces kept where keep(centre) is true), solidified: caps, hoods, hair pieces."""
    bm = bmesh.new()
    bm_ellipsoid(bm, center, radii, rot, seg)
    dead = [f for f in bm.faces if not keep(f.calc_center_median())]
    bmesh.ops.delete(bm, geom=dead, context='FACES')
    ob = bm_obj(bm, name)
    solidify(ob, thick, offset)
    if sub:
        subdivide(ob, sub)
    assign(ob, mat)
    return ob


def hair_shell(mat, P, name='hair', z_lo=-0.058, y_front=-0.018, z_hi=0.085, off=0.0045, thick=0.0042, ear=True):
    """Short hair over the back and sides of the skull: a slightly larger cranium cut by clean planes, with a notch for each ear."""
    c = sk.HEAD_C
    w = P.get('face', {}).get('width', 1.0)
    bm = bmesh.new()
    bm_ellipsoid(bm, c + V(0.0, 0.012, 0.030), (0.077 * w + off, 0.095 + off, 0.092 + off), None, 96)
    for co, no, kw in ((c + V(0, 0, z_lo), V(0, 0, 1), dict(clear_inner=True)),
                       (c + V(0, y_front, 0), V(0, 1, 0), dict(clear_inner=True)),
                       (c + V(0, 0, z_hi), V(0, 0, 1), dict(clear_outer=True))):
        geom = list(bm.verts) + list(bm.edges) + list(bm.faces)
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=co, plane_no=no, **kw)
    if ear:
        dead = []
        for f in bm.faces:
            p = f.calc_center_median() - c
            if abs(p.x) > 0.050 and ((p.y - 0.016) / 0.030) ** 2 + ((p.z + 0.012) / 0.040) ** 2 < 1.0:
                dead.append(f)
        bmesh.ops.delete(bm, geom=dead, context='FACES')
    ob = bm_obj(bm, name)
    solidify(ob, thick, -1.0)
    ob.data.shade_smooth()
    assign(ob, mat)
    return ob


def hair_do(mat, P, z_back=-0.045, hairline=0.056, side_drop=0.034, tall=0.0, off=0.0045, thick=0.0045, ear=True, name='hair'):
    """A full head of hair for a character without a cap: the skull covered from a hairline on the forehead (it dips towards the
    temples) over the crown and down to z_back (relative to the eye line) at the nape, with a notch for each ear. A lower z_back is longer
    hair behind; 'tall' stretches the shell downwards so that the longer cut stands off the neck instead of sinking into it."""
    c = sk.HEAD_C
    w = P.get('face', {}).get('width', 1.0)
    centre = c + V(0.0, 0.012, 0.030 - tall * 0.5)
    radii = (0.077 * w + off, 0.095 + off, 0.092 + tall * 0.5 + off)

    def keep(p):
        q = p - c
        if q.z < z_back:
            return False
        if ear and abs(q.x) > 0.050 and ((q.y - 0.016) / 0.030) ** 2 + ((q.z + 0.012) / 0.040) ** 2 < 1.0:
            return False
        if q.y > 0.020:                                   # behind the ears: everything above the nape line
            return True
        return q.z > hairline - side_drop * smoothstep(0.030, 0.066, abs(q.x))

    bm = bmesh.new()
    bm_ellipsoid(bm, centre, radii, None, 96)
    dead = [f for f in bm.faces if not keep(f.calc_center_median())]
    bmesh.ops.delete(bm, geom=dead, context='FACES')
    ob = bm_obj(bm, name)
    solidify(ob, thick, -1.0)
    ob.data.shade_smooth()
    assign(ob, mat)
    return ob


def braid(mat, name='braid', links=30):
    """A plait that starts under the cap, follows the back of the neck and lies down the back of the jacket (flattened links that
    alternate left and right, merged into one skin). Its weights blend the head into the upper chest so that it bends."""
    path = [(0.074, 1.606, 0.019), (0.097, 1.552, 0.021), (0.110, 1.472, 0.022), (0.128, 1.392, 0.021),
            (0.142, 1.312, 0.019), (0.150, 1.222, 0.016), (0.150, 1.150, 0.009)]

    def at(t):
        f = t * (len(path) - 1)
        i = min(int(f), len(path) - 2)
        u = f - i
        a, b = path[i], path[i + 1]
        return tuple(a[k] + (b[k] - a[k]) * u for k in range(3))

    def fill(bm):
        for i in range(links):
            y, z, r = at(i / (links - 1.0))
            side = 0.0050 * (1 if i % 2 else -1) * (0.4 + r / 0.022)
            bm_ellipsoid(bm, V(side, y, z), (r * 0.80, r * 0.72, r * 1.10), None, 16)

    ob = organic(name, fill, 0.0016, sm_iter=2, sm_fac=0.4)
    assign(ob, mat)
    return ob


def hair_from_head(head, keep, mat, off=0.0035, thick=0.0035, name='hair', sub=1):
    """A hair piece: the faces of the head selected by keep(head-local centre), lifted along the normals and solidified."""
    bm = bm_from(head)
    bm.normal_update()
    new = bmesh.new()
    vmap = {}
    for f in bm.faces:
        if not keep(f.calc_center_median() - sk.HEAD_C):
            continue
        vs = []
        for v in f.verts:
            if v.index not in vmap:
                vmap[v.index] = new.verts.new(v.co + v.normal * off)
            vs.append(vmap[v.index])
        new.faces.new(vs)
    bm.free()
    ob = bm_obj(new, name)
    solidify(ob, thick, 1.0)
    ob.data.shade_smooth()
    assign(ob, mat)
    return ob


# ----------------------------------------------------------------------------- garments
def neck_skin(P, mat, name='neck'):
    def fill(bm):
        bd.neck_fill(bm, P)
        bd.torso_fill(bm, P, grow=0.0, top=1.44, bottom=1.31, shoulders=False)
    ob = organic(name, fill, 0.004, sm_iter=4, sm_fac=0.5)
    assign(ob, mat)
    return ob


def jacket(P, fab, metal_mat, trim, g=0.020, hem=0.855, sleeve_g=0.018, name='jacket', collar=0.0, zip_top=1.40,
           chest_pockets=True, hip_pockets=True, cuff_mat=None):
    """Hip-length jacket: torso and sleeves (one solid), standing collar, zip, placket, pockets, cuffs."""
    def fill(bm):
        bd.torso_fill(bm, P, grow=g, top=1.445, bottom=hem, flare=(0.09, 0.010))
        for s in ('L', 'R'):
            bd.arm_fill(bm, s, P, grow=sleeve_g, shoulder_r=0.047)
        if collar > 0.0:
            for i in range(3):
                bm_ring(bm, (0, 0.000 - 0.004 * i, 1.436 + collar * i / 2.0), (0, 0, 1), 0.074 + 0.003 * i, 0.012, seg=44, rseg=10, squash=0.92)
    ob = organic(name, fill, 0.0045, sm_iter=3, sm_fac=0.4)
    wrinkles(ob, seed=1.0)
    assign(ob, fab)
    objs = [ob]
    # cuffs at the wrists
    for s in ('L', 'R'):
        m = (lambda v: v) if s == 'L' else sk.mirror
        el, wr = m(sk.J['elbow']), m(sk.J['wrist'])
        d = (wr - el).normalized()
        bm = bmesh.new()
        bm_ring(bm, wr - d * 0.012, d, 0.0318 + sleeve_g, 0.0085, seg=36, rseg=10)
        bm_ring(bm, wr - d * 0.034, d, 0.0345 + sleeve_g, 0.0070, seg=36, rseg=10)
        objs.append(finish(bm, f'cuff_{s}', cuff_mat or fab))
    # zip, placket
    bm = bmesh.new()
    z = zip_top
    k = 0
    while z > hem + 0.012:
        pos, R, n = on_torso(P, z, 0.0, g, 0.0008)
        bm_box(bm, pos + V(0.0026 * (1 if k % 2 else -1), 0, 0), (0.0045, 0.0042, 0.0095), R)
        z -= 0.0115
        k += 1
    pos, R, n = on_torso(P, zip_top - 0.035, 0.0, g, 0.0015)
    bm_box(bm, pos, (0.013, 0.0050, 0.034), R)
    objs.append(finish(bm, 'zip', metal_mat, smooth=False))
    bm = bmesh.new()
    z = zip_top
    while z > hem + 0.01:
        for sx in (-0.018, 0.018):
            pos, R, n = on_torso(P, z, sx, g, 0.0010)
            bm_box(bm, pos, (0.026, 0.0034, 0.0225), R)
        z -= 0.020
    objs.append(finish(bm, 'placket', fab, smooth=False))
    # pockets
    bm = bmesh.new()
    bt = bmesh.new()
    pockets = []
    if chest_pockets:
        pockets += [(1.215, 0.078, 0.082, 0.092), (1.215, -0.078, 0.082, 0.092)]
    if hip_pockets:
        pockets += [(0.985, 0.118, 0.115, 0.125), (0.985, -0.118, 0.115, 0.125)]
    for z, x, w, h in pockets:
        pos, R, n = on_torso(P, z, x, g, 0.0018)
        bm_box(bm, pos, (w, 0.0045, h), R)
        pos2, R2, n2 = on_torso(P, z + h * 0.32, x, g, 0.0044)
        bm_box(bm, pos2, (w * 1.02, 0.0042, h * 0.38), R2)
        bm_sphere(bt, pos2 + n2 * 0.0035, 0.0048, 8)
    objs.append(finish(bm, 'pockets', fab, smooth=False))
    objs.append(finish(bt, 'buttons', metal_mat))
    return objs


def trousers(P, fab, patch_mat, g=0.015, top=1.07, name='trousers', patches=True):
    def fill(bm):
        bd.torso_fill(bm, P, grow=0.010, top=top, bottom=0.84, shoulders=False)
        for s in ('L', 'R'):
            bd.leg_fill(bm, s, P, grow=g, foot=False, ankle_grow=g - 0.002)
    ob = organic(name, fill, 0.0045, sm_iter=3, sm_fac=0.4)
    wrinkles(ob, seed=2.0, amp=0.0018)
    assign(ob, fab)
    objs = [ob]
    if patches:
        bm = bmesh.new()
        for s in ('L', 'R'):
            m = (lambda v: v) if s == 'L' else sk.mirror
            kn = m(sk.J['knee'])
            bm_ellipsoid(bm, kn + V(0, -(0.062 * P['thigh'] + g) + 0.0015, 0.010), (0.034, 0.008, 0.046), None, 18)
        objs.append(finish(bm, 'patches', patch_mat))
    return objs


def scarf(P, mat, name='scarf', turns=3, z0=1.452, tail=True, g=0.0):
    """Thick scarf wound around the neck with one end hanging over the chest."""
    def fill(bm):
        for i in range(turns):
            bm_ring(bm, (0, 0.000 - 0.004 * i, z0 + 0.021 * i), (0, 0, 1), 0.070 + 0.003 * i + g, 0.0195, seg=48, rseg=12, squash=0.94)
        if tail:
            pts = [V(0.034, -0.082, 1.44), V(0.046, -0.112, 1.36), V(0.050, -0.122, 1.26), V(0.052, -0.121, 1.15), V(0.050, -0.118, 1.06)]
            bm_ribbon(bm, pts, 0.070, 0.016, up=V(1, 0, 0))
    ob = organic(name, fill, 0.0032, sm_iter=3, sm_fac=0.45)
    wrinkles(ob, seed=6.0, amp=0.0022, gain=0.5, freq=60.0)
    assign(ob, mat)
    return ob


def boots(P, leather_mat, sole_mat, name='boots', height=0.23, g=0.0):
    def fill(bm):
        for s in ('L', 'R'):
            m = (lambda v: v) if s == 'L' else sk.mirror
            an, kn = m(sk.J['ankle']), m(sk.J['knee'])
            d = (kn - an).normalized()
            bm_capsule(bm, an + d * -0.01, an + d * height, 0.054 + g, 0.058 + g, seg=22)
            bm_ellipsoid(bm, an + V(0, -0.052, -0.036), (0.054 + g, 0.130 + g, 0.050 + g), None, 26)
            bm_ellipsoid(bm, an + V(0, -0.130, -0.052), (0.049 + g, 0.058 + g, 0.036 + g), None, 20)
            bm_ellipsoid(bm, an + V(0, 0.030, -0.030), (0.050 + g, 0.058 + g, 0.062 + g), None, 18)
            bm_ring(bm, an + d * height, d, 0.054 + g, 0.0085, seg=36, rseg=10)
    ob = organic(name, fill, 0.0032, sm_iter=3, sm_fac=0.4)
    wrinkles(ob, seed=3.0, amp=0.0012, gain=1.0)
    assign(ob, leather_mat)
    bm = bmesh.new()
    for s in ('L', 'R'):
        m = (lambda v: v) if s == 'L' else sk.mirror
        an = m(sk.J['ankle'])
        bm_ellipsoid(bm, V(an.x, an.y - 0.056, 0.015), (0.059, 0.142, 0.015), None, 28)
        bm_ellipsoid(bm, V(an.x, an.y + 0.034, 0.020), (0.052, 0.062, 0.020), None, 20)
    so = bm_obj(bm, 'soles')
    so.data.shade_smooth()
    assign(so, sole_mat)
    return [ob, so]


def gloves(P, leather_mat, cuff=0.07, grow=0.0018):
    L, R = bd.build_hands(P, grow=grow, cuff=cuff, name='glove_L')
    L.name = L.data.name = 'glove_L'
    R.name = R.data.name = 'glove_R'
    for o in (L, R):
        assign(o, leather_mat)
    wrinkles(L, seed=4.0, amp=0.0008, gain=1.0, freq=90.0)
    wrinkles(R, seed=5.0, amp=0.0008, gain=1.0, freq=90.0)
    return [L, R]


def flat_cap(mat, band_mat, name='cap', z0=0.050):
    """Newsboy / flat cap: a full crown and a short peak, sitting on the brow."""
    c = sk.HEAD_C
    def keep(p):
        rz, ry = p.z - c.z, p.y - c.y
        return rz > z0 - 0.036 * smoothstep(0.0, 0.07, ry)
    crown = shell(name, mat, c + V(0.0, 0.016, 0.050), (0.092, 0.112, 0.080), keep, thick=0.006, offset=-1.0, seg=56, sub=1)
    bm = bmesh.new()
    rot = Euler((radians(14), 0, 0)).to_matrix()
    bm_ellipsoid(bm, c + V(0.0, -0.104, 0.043), (0.058, 0.046, 0.0045), rot, 24)
    peak = finish(bm, 'peak', mat)
    return [crown, peak]


def moustache(mat, F=1.0, name='moustache'):
    c = sk.HEAD_C

    def fill(bm):
        for s in (-1, 1):
            pts = [V(s * 0.002, -0.0985, -0.0585), V(s * 0.012, -0.1005, -0.0620), V(s * 0.024, -0.0955, -0.0690), V(s * 0.033, -0.0850, -0.0765)]
            rad = [0.0072 * F, 0.0072 * F, 0.0060 * F, 0.0040 * F]
            bm_chain(bm, [c + p for p in pts], rad, seg=10)
    ob = organic(name, fill, 0.0014, sm_iter=3, sm_fac=0.5)
    assign(ob, mat)
    return ob


def brows(mat, thick=0.0042, name='brows'):
    c = sk.HEAD_C

    def fill(bm):
        for s in (-1, 1):
            pts = [V(s * 0.012, -0.0915, 0.0235), V(s * 0.028, -0.0905, 0.0300), V(s * 0.045, -0.0815, 0.0295), V(s * 0.056, -0.0700, 0.0230)]
            rad = [thick, thick * 1.05, thick * 0.9, thick * 0.6]
            bm_chain(bm, [c + p for p in pts], rad, seg=8)
    ob = organic(name, fill, 0.0012, sm_iter=2, sm_fac=0.5)
    assign(ob, mat)
    return ob


def belt_kit(P, leather_mat, metal_mat, g_over=0.0225, z=0.962, name='belt'):
    """Belt over the jacket with a buckle, a key ring on the left hip and a pouch on the right."""
    objs = []
    rx, ry, cy = bd.torso_section(z, P, g_over)
    bm = bmesh.new()
    bm_band(bm, 0.0, cy, z, rx, ry, 0.036, 0.006, seg=96)
    objs.append(finish(bm, name, leather_mat, smooth=True))
    bm = bmesh.new()
    pos, R, n = on_torso(P, z, 0.0, g_over + 0.006, 0.0)
    bm_box(bm, pos, (0.050, 0.006, 0.040), R)
    pos2, R2, n2 = on_torso(P, z, 0.0, g_over + 0.006, 0.0035)
    bm_box(bm, pos2, (0.034, 0.004, 0.026), R2)
    objs.append(finish(bm, 'buckle', metal_mat, smooth=False))
    # key ring
    bm = bmesh.new()
    hip = V(rx * 0.98, cy + 0.02, z - 0.02)
    bm_ring(bm, hip + V(0.012, 0, -0.030), (1, 0, 0), 0.019, 0.0017, seg=32, rseg=8)
    for i, (dx, dy, ln) in enumerate(((0.006, 0.0, 0.058), (0.012, 0.006, 0.050), (0.016, -0.006, 0.045), (0.010, 0.012, 0.055))):
        top = hip + V(0.014 + dx, dy, -0.045)
        bm_box(bm, top + V(0, 0, -ln / 2), (0.003, 0.0085, ln), None)
        bm_ring(bm, top, (1, 0, 0), 0.0085, 0.0013, seg=16, rseg=6)
    objs.append(finish(bm, 'keys', metal_mat, smooth=False))
    # pouch
    bm = bmesh.new()
    pos, R, n = on_torso(P, z - 0.040, -0.150, g_over, 0.020)
    bm_ellipsoid(bm, pos, (0.040, 0.028, 0.052), R, 18)
    pos2, R2, n2 = on_torso(P, z - 0.012, -0.150, g_over, 0.030)
    bm_ellipsoid(bm, pos2, (0.042, 0.030, 0.026), R2, 18)
    objs.append(finish(bm, 'pouch', leather_mat))
    return objs
