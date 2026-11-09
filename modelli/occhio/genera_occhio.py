"""
Occhio volante (fase 4): generatore procedurale per Blender 4.5. Sostituisce il drone.

Uso (dalla cartella del file):
  blender -b -P genera_occhio.py -- build     geometria, scheletro, pesi, materiali -> occhio.blend
  blender -b -P genera_occhio.py -- render    anteprime Cycles (anche palpebre chiuse) -> render/
  blender -b -P genera_occhio.py -- export    FBX per Unity -> export/occhio.fbx
Le texture vengono da genera_occhio_tex.py (cartella Assets/_PryingEyes/Textures/Occhio).

Assi: Z in alto, l'iride guarda verso -Y, la destra e' +X. Unita': metri. L'animazione la fa Unity a codice
(Scripts/EyeRig.cs): qui ci sono solo mesh, ossa e pesi.
"""
import bpy, bmesh, math, os, sys, random
from mathutils import Vector, Matrix, Quaternion, noise
from math import sin, cos, pi, radians, sqrt, atan2, acos

DIR = os.path.dirname(os.path.abspath(__file__))
TEX = r"C:\Users\danie\Desktop\prying-eyes\unity\PryingEyes\Assets\_PryingEyes\Textures\Occhio"
BLEND = os.path.join(DIR, 'occhio.blend')
RENDER_DIR = os.path.join(DIR, 'render')
EXPORT_DIR = os.path.join(DIR, 'export')
ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []


def V(*a):
    return Vector(a)


# ----------------------------------------------------------------------------- misure
RG = 0.30                  # raggio del globo
HC = 0.045                 # sporgenza della cornea
THETA_C = 0.80             # estensione angolare della cornea (rad)
THETA_SPLIT = 1.745        # emisfero anteriore / posteriore (stesso valore di genera_occhio_tex.py)
FWD, UP, RGT = V(0, -1, 0), V(0, 0, 1), V(1, 0, 0)
TILE = 0.18                # metri coperti da una ripetizione della texture della carne


def d_of(theta, phi, fwd=FWD, up=UP, rgt=RGT):
    """Direzione a 'theta' dall'asse della pupilla, 'phi' attorno ad esso (0 = destra, 90 = su)."""
    return fwd * cos(theta) + (rgt * cos(phi) + up * sin(phi)) * sin(theta)


def bump(theta):
    t = theta / THETA_C
    return (1 - t * t) ** 2 if t < 1 else 0.0


def globe_r(theta, R=RG, h=HC):
    return R + h * bump(theta)


def smoothstep(e0, e1, x):
    t = min(1.0, max(0.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


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


def orient(bm, ref):
    """Rigira tutte le facce se, in media, guardano verso l'interno rispetto al vettore di riferimento ref(faccia)."""
    tot = 0.0
    for f in bm.faces:
        f.normal_update()
        tot += f.normal.dot(ref(f))
    if tot < 0:
        bmesh.ops.reverse_faces(bm, faces=bm.faces[:])


def bm_obj(bm, name, coll=None, smooth=True, radial=None, closed=True):
    """radial: origine rispetto a cui le facce devono guardare in fuori (superfici aperte); senza, una mesh
    chiusa lascia decidere le normali a Blender; closed=False e nessun radial: l'orientamento l'ha gia' fatto
    chi chiama."""
    if radial is not None:
        o = Vector(radial)
        orient(bm, lambda f: f.calc_center_median() - o)
    elif closed:
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    if smooth:
        me.polygons.foreach_set('use_smooth', [True] * len(me.polygons))
    ob = bpy.data.objects.new(name, me)
    link(ob, coll)
    return ob


def save_blend(path):
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=path)


def select_only(obs, active=None):
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in obs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = active or obs[0]


# ----------------------------------------------------------------------------- globo
def front_uv(theta, phi):
    rho = theta / THETA_SPLIT
    return 0.25 + 0.25 * rho * cos(phi), 0.5 + 0.5 * rho * sin(phi)


def back_uv(theta, phi):
    rho = (pi - theta) / (pi - THETA_SPLIT)
    return 0.75 + 0.25 * rho * cos(phi), 0.5 + 0.5 * rho * sin(phi)


def eyeball(name, R=RG, center=V(0, 0, 0), fwd=FWD, up=UP, rgt=RGT, nphi=112, kf=44, kb=24,
            theta_max=pi, offset=0.0, with_uv=True):
    """Sfera con sporgenza della cornea. UV: disco anteriore e disco posteriore dell'atlante (le UV sono per
    angolo di faccia, quindi la cucitura passa dentro la stessa fila di vertici senza duplicarli)."""
    s = R / RG
    center = Vector(center)
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new('UVMap') if with_uv else None
    thetas = [THETA_SPLIT * k / kf for k in range(kf + 1)] + \
             [THETA_SPLIT + (pi - THETA_SPLIT) * k / kb for k in range(1, kb + 1)]
    thetas = [t for t in thetas if t <= theta_max + 1e-6]
    rings = []
    for th in thetas:
        r = globe_r(th, R, HC * s) + offset
        if th < 1e-6:
            rings.append([bm.verts.new(center + fwd * r)])
        elif abs(th - pi) < 1e-6:
            rings.append([bm.verts.new(center - fwd * r)])
        else:
            rings.append([bm.verts.new(center + d_of(th, 2 * pi * j / nphi, fwd, up, rgt) * r) for j in range(nphi)])
    for k in range(len(rings) - 1):
        a, b = rings[k], rings[k + 1]
        if len(a) == 1:
            for j in range(nphi):
                bm.faces.new((a[0], b[(j + 1) % nphi], b[j]))
        elif len(b) == 1:
            for j in range(nphi):
                bm.faces.new((a[j], a[(j + 1) % nphi], b[0]))
        else:
            for j in range(nphi):
                bm.faces.new((a[j], a[(j + 1) % nphi], b[(j + 1) % nphi], b[j]))
    if with_uv:
        def angles(co):
            d = (co - center)
            ln = d.length
            th = acos(max(-1.0, min(1.0, d.dot(fwd) / ln)))
            return th, atan2(d.dot(up), d.dot(rgt))
        for f in bm.faces:
            cth, cph = angles(f.calc_center_median())
            use_front = cth < THETA_SPLIT
            for lo in f.loops:
                th, ph = angles(lo.vert.co)
                if th < 1e-4 or abs(th - pi) < 1e-4:
                    ph = cph                               # ai poli l'angolo e' indefinito
                lo[uvl].uv = front_uv(th, ph) if use_front else back_uv(th, ph)
    return bm_obj(bm, name, radial=center if theta_max < pi - 1e-3 else None)


# ----------------------------------------------------------------------------- muscoli sul globo
def muscle(phi0, name):
    """Fascia carnosa che dall'equatore del globo converge verso il nervo (muscoli extraoculari)."""
    TA, TB = radians(88), radians(152)
    na, nb = 26, 14
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new('UVMap')
    grid = []
    for i in range(na + 1):
        a = i / na
        th = TA + (TB - TA) * a
        hw = 0.052 * (1 - a) ** 0.55 + 0.012
        row = []
        for j in range(nb + 1):
            b = -1 + 2 * j / nb
            lat = b * hw
            ph = phi0 + lat / (RG * sin(th))
            h = 0.016 * (1 - b * b) ** 0.7 * smoothstep(0.0, 0.14, a) * (1 - a) ** 0.35 * (1 + 0.25 * noise.noise(V(a * 5, b, phi0)))
            v = bm.verts.new(d_of(th, ph) * (RG + 0.002 + h))
            row.append((v, a * (TB - TA) * RG / TILE, lat / TILE))
        grid.append(row)
    for i in range(na):
        for j in range(nb):
            f = bm.faces.new((grid[i][j][0], grid[i + 1][j][0], grid[i + 1][j + 1][0], grid[i][j + 1][0]))
            for lo, (_, v_, u_) in zip(f.loops, (grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1])):
                lo[uvl].uv = (u_ + 0.5, v_)
    return bm_obj(bm, name, radial=V(0, 0, 0))


# ----------------------------------------------------------------------------- palpebre
def eyelid(upper, name):
    """Palpebra come cuscino lenticolare sul globo, ruota attorno all'asse X passante per il centro.
    Spessore e raggio crescono verso il bordo, che e' un labbro arrotondato (materiale 'bordo')."""
    sg = 1.0 if upper else -1.0
    PSI = radians(80)
    E0 = radians(44) if upper else radians(34)
    W0 = radians(40) if upper else radians(34)
    T0 = 0.020 if upper else 0.017
    RIN = RG + 0.003 + (0.0 if upper else 0.0)
    LIFT = 0.052 if upper else 0.048
    nst, nin, nlip, nout = 56, 14, 8, 14
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new('UVMap')
    rows = []
    rim_rows = set()

    def pos(psi, eps, r):
        return V(sin(psi) * cos(eps), -cos(psi) * cos(eps), sin(eps)) * r

    for i in range(nst + 1):
        x = -1 + 2 * i / nst
        psi = x * PSI
        shape = max(0.0, cos(x * pi / 2)) + 1e-4
        eps_e = sg * E0 * shape ** 0.85
        Wd = W0 * shape ** 0.6
        t0 = T0 * shape ** 0.5
        lift = LIFT * shape
        loop = []
        # superficie interna: dal bordo esterno (s=1) al labbro (s=0)
        for k in range(nin + 1):
            s = 1 - k / nin
            eps = eps_e + sg * Wd * s
            r = RIN + lift * (1 - s) ** 2.2
            loop.append((pos(psi, eps, r), eps, 0))
        # labbro: semicirconferenza dal lato interno a quello esterno
        r_in0 = RIN + lift
        for k in range(1, nlip):
            chi = pi * k / nlip
            eps = eps_e - sg * (t0 / 2) * sin(chi) / (r_in0 + t0 / 2)
            r = r_in0 + (t0 / 2) * (1 - cos(chi))
            loop.append((pos(psi, eps, r), eps, 1))
        # superficie esterna: dal labbro (s=0) al bordo esterno (s=1), dove lo spessore si annulla
        for k in range(nout + 1):
            s = k / nout
            eps = eps_e + sg * Wd * s
            r = RIN + lift * (1 - s) ** 2.2 + t0 * (1 - s) ** 0.75
            loop.append((pos(psi, eps, r), eps, 2))
        rows.append(loop)
    verts = []
    for loop in rows:
        verts.append([bm.verts.new(p) for p, _, _ in loop])
    n = len(rows[0])
    for i in range(nst):
        for k in range(n - 1):
            f = bm.faces.new((verts[i][k], verts[i + 1][k], verts[i + 1][k + 1], verts[i][k + 1]))
            f.material_index = 1 if rows[0][k][2] == 1 or rows[0][k + 1][2] == 1 else 0
            for lo in f.loops:
                p = lo.vert.co
                lo[uvl].uv = (p.z / TILE, p.x / TILE)         # fibre parallele al bordo
    # chiusura: il primo e l'ultimo punto del profilo coincidono (spessore 0 al bordo esterno)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-5)
    ob = bm_obj(bm, name)          # cuscino chiuso: normali verso l'esterno del solido
    return ob


# ----------------------------------------------------------------------------- tubi: nervo e tentacoli
def catmull(pts, n):
    pts = [Vector(p) for p in pts]
    ext = [pts[0] * 2 - pts[1]] + pts + [pts[-1] * 2 - pts[-2]]
    out = []
    segs = len(pts) - 1
    for i in range(n + 1):
        u = i / n * segs
        j = min(int(u), segs - 1)
        t = u - j
        p0, p1, p2, p3 = ext[j], ext[j + 1], ext[j + 2], ext[j + 3]
        out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3))
    return out


def resample(curve, n):
    """Punti equidistanti (per lunghezza d'arco) lungo una poligonale."""
    ls = [0.0]
    for a, b in zip(curve, curve[1:]):
        ls.append(ls[-1] + (b - a).length)
    tot = ls[-1]
    out, j = [], 0
    for i in range(n + 1):
        d = tot * i / n
        while j < len(curve) - 2 and ls[j + 1] < d:
            j += 1
        seg = ls[j + 1] - ls[j]
        t = 0 if seg < 1e-9 else (d - ls[j]) / seg
        out.append(curve[j] + (curve[j + 1] - curve[j]) * t)
    return out, tot


def radius_at(radii, q):
    q = min(max(q, 0.0), 1.0) * (len(radii) - 1)
    j = min(int(q), len(radii) - 2)
    t = q - j
    t = t * t * (3 - 2 * t)
    return radii[j] * (1 - t) + radii[j + 1] * t


def tube(name, ctrl, radii, segs=12, rings=56, seed=0.0, wob=0.07):
    curve, total = resample(catmull(ctrl, 160), rings)
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new('UVMap')
    # sistema di riferimento a trasporto parallelo
    t0 = (curve[1] - curve[0]).normalized()
    n0 = t0.cross(V(0, 0, 1))
    if n0.length < 1e-3:
        n0 = t0.cross(V(1, 0, 0))
    n0.normalize()
    frames = []
    nrm = n0
    for i, c in enumerate(curve):
        tg = ((curve[min(i + 1, rings)] - curve[max(i - 1, 0)])).normalized()
        nrm = (nrm - tg * nrm.dot(tg)).normalized()
        frames.append((tg, nrm, tg.cross(nrm)))
    grid, info = [], []
    lens = 0.0
    for i, c in enumerate(curve):
        if i:
            lens += (curve[i] - curve[i - 1]).length
        q = i / rings
        r0 = radius_at(radii, q)
        tg, nr, bn = frames[i]
        ring = []
        for j in range(segs):
            a = 2 * pi * j / segs
            r = r0 * (1 + wob * noise.noise(V(cos(a) * 1.7 + seed, sin(a) * 1.7, lens * 18.0)))
            ring.append(bm.verts.new(c + (nr * cos(a) + bn * sin(a)) * r))
        grid.append(ring)
        info.append((2 * pi * r0, lens))
    # punta arrotondata
    tip = bm.verts.new(curve[-1] + frames[-1][0] * radii[-1] * 0.9)
    ring_of = {}
    for i in range(rings):
        (ca, la), (cb, lb) = info[i], info[i + 1]
        for j in range(segs):
            j1 = (j + 1) % segs
            f = bm.faces.new((grid[i][j], grid[i][j1], grid[i + 1][j1], grid[i + 1][j]))
            ring_of[f] = i
            uvs = [(j / segs * ca / TILE, la / TILE), ((j + 1) / segs * ca / TILE, la / TILE),
                   ((j + 1) / segs * cb / TILE, lb / TILE), (j / segs * cb / TILE, lb / TILE)]
            for lo, uv in zip(f.loops, uvs):
                lo[uvl].uv = uv
    last = grid[-1]
    cl, ll = info[-1]
    for j in range(segs):
        f = bm.faces.new((last[j], last[(j + 1) % segs], tip))
        ring_of[f] = rings
        for lo, uv in zip(f.loops, [(j / segs * cl / TILE, ll / TILE), ((j + 1) / segs * cl / TILE, ll / TILE),
                                    ((j + 0.5) / segs * cl / TILE, ll / TILE + 0.03)]):
            lo[uvl].uv = uv
    orient(bm, lambda f: f.calc_center_median() - curve[ring_of[f]])
    ob = bm_obj(bm, name, closed=False)
    return ob, curve


def tendril_ctrl(alpha, length, base_psi=radians(52), droop=0.04, seed=0):
    """Punti di controllo di un tentacolo: nasce sul retro del globo e si allontana a spirale."""
    rnd = random.Random(seed)
    axis = V(0, 1, 0)
    side = V(cos(alpha), 0, sin(alpha))
    p0 = (axis * cos(base_psi) + side * sin(base_psi)) * (RG * 0.93)
    d = (axis * 0.55 + side * 0.85).normalized()
    pts = [p0]
    n = 6
    step = length / n
    for i in range(n):
        d = (d + axis * 0.30 + V(0, 0, -droop) + V(rnd.uniform(-0.2, 0.2), 0, rnd.uniform(-0.2, 0.2))).normalized()
        pts.append(pts[-1] + d * step)
    return pts


# ----------------------------------------------------------------------------- scheletro e pesi
class Rig:
    def __init__(self):
        self.specs = []       # (nome, testa, coda, genitore)
        self.chains = {}      # prefisso -> lista di punti giunto

    def add(self, name, head, tail, parent):
        self.specs.append((name, Vector(head), Vector(tail), parent))

    def chain(self, prefix, joints, parent):
        names = []
        par = parent
        for j in range(len(joints) - 1):
            nm = f'{prefix}_{j}'
            self.add(nm, joints[j], joints[j + 1], par)
            names.append(nm)
            par = nm
        self.chains[prefix] = (names, [Vector(p) for p in joints])
        return names


def chain_weights(joints, names, p):
    """Peso di un punto lungo una catena: mescola le due ossa piu' vicine in base alla posizione sull'asse."""
    best, bj, bt = 1e9, 0, 0.0
    for j in range(len(joints) - 1):
        a, b = joints[j], joints[j + 1]
        ab = b - a
        t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-12)))
        d = (p - (a + ab * t)).length
        if d < best:
            best, bj, bt = d, j, t
    c = bj + bt - 0.5
    j0 = int(math.floor(c))
    f = c - j0
    w = {}
    for jj, ww in ((j0, 1 - f), (j0 + 1, f)):
        jj = min(max(jj, 0), len(names) - 1)
        w[names[jj]] = w.get(names[jj], 0.0) + ww
    return w


def set_weights(ob, fn):
    groups = {}
    for v in ob.data.vertices:
        w = fn(v.co)
        top = sorted(((x, b) for b, x in w.items() if x > 1e-4), reverse=True)[:4]
        s = sum(x for x, b in top)
        if s <= 0:
            continue
        for x, b in top:
            g = groups.get(b)
            if g is None:
                g = groups[b] = ob.vertex_groups.get(b) or ob.vertex_groups.new(name=b)
            g.add([v.index], x / s, 'REPLACE')


def build_armature(rig):
    ad = bpy.data.armatures.new('Occhio')
    ad.display_type = 'STICK'
    arm = bpy.data.objects.new('Occhio', ad)
    link(arm)
    select_only([arm])
    bpy.ops.object.mode_set(mode='EDIT')
    ebs = {}
    for nm, h, t, par in rig.specs:
        eb = ad.edit_bones.new(nm)
        eb.head, eb.tail = h, t
        if (t - h).length < 1e-4:
            eb.tail = h + V(0, 0, 0.01)
        if par:
            eb.parent = ebs[par]
        eb.use_deform = nm != 'radice'
        ebs[nm] = eb
    bpy.ops.object.mode_set(mode='OBJECT')
    return arm


# ----------------------------------------------------------------------------- materiali
def img(path, colorspace='sRGB'):
    im = bpy.data.images.get(os.path.basename(path)) or bpy.data.images.load(path)
    im.colorspace_settings.name = colorspace
    im.alpha_mode = 'CHANNEL_PACKED'
    return im


def pbr_material(name, base, normal, mask, emiss=None, emit_strength=0.0, bump_strength=1.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    bs = nt.nodes.new('ShaderNodeBsdfPrincipled')
    nt.links.new(bs.outputs[0], out.inputs['Surface'])

    def tex(path, cs):
        n = nt.nodes.new('ShaderNodeTexImage')
        n.image = img(path, cs)
        n.interpolation = 'Cubic'
        return n
    b = tex(base, 'sRGB')
    nt.links.new(b.outputs['Color'], bs.inputs['Base Color'])
    mk = tex(mask, 'Non-Color')
    inv = nt.nodes.new('ShaderNodeMath')
    inv.operation = 'SUBTRACT'
    inv.inputs[0].default_value = 1.0
    nt.links.new(mk.outputs['Alpha'], inv.inputs[1])
    nt.links.new(inv.outputs[0], bs.inputs['Roughness'])
    nn = tex(normal, 'Non-Color')
    nm = nt.nodes.new('ShaderNodeNormalMap')
    nm.inputs['Strength'].default_value = bump_strength
    nt.links.new(nn.outputs['Color'], nm.inputs['Color'])
    nt.links.new(nm.outputs['Normal'], bs.inputs['Normal'])
    if emiss:
        e = tex(emiss, 'sRGB')
        nt.links.new(e.outputs['Color'], bs.inputs['Emission Color'])
        bs.inputs['Emission Strength'].default_value = emit_strength
    bs.inputs['Specular IOR Level'].default_value = 0.6
    return m


def make_materials():
    mats = {}
    mats['M_occhio'] = pbr_material('M_occhio', f'{TEX}\\occhio_base.png', f'{TEX}\\occhio_normal.png',
                                    f'{TEX}\\occhio_mask.png', f'{TEX}\\occhio_emiss.png', 0.9)
    mats['M_carne'] = pbr_material('M_carne', f'{TEX}\\carne_base.png', f'{TEX}\\carne_normal.png',
                                   f'{TEX}\\carne_mask.png')
    m = bpy.data.materials.new('M_cornea')
    m.use_nodes = True
    bs = m.node_tree.nodes['Principled BSDF']
    bs.inputs['Roughness'].default_value = 0.02
    bs.inputs['IOR'].default_value = 1.38
    bs.inputs['Transmission Weight'].default_value = 1.0
    mats['M_cornea'] = m
    m = bpy.data.materials.new('M_bordo')
    m.use_nodes = True
    bs = m.node_tree.nodes['Principled BSDF']
    bs.inputs['Base Color'].default_value = (0.42, 0.07, 0.08, 1)
    bs.inputs['Roughness'].default_value = 0.18
    bs.inputs['Subsurface Weight'].default_value = 0.15
    mats['M_bordo'] = m
    return mats


# ----------------------------------------------------------------------------- build
N_TENT = 6
EYE_TIPS = (0, 2, 3, 5)           # tentacoli che portano un occhietto sulla punta


def stage_build():
    sc = reset()
    random.seed(5)
    rig = Rig()
    rig.add('radice', V(0, 0, 0), V(0, -0.08, 0), None)
    rig.add('occhio', V(0, 0, 0), V(0, -0.18, 0), 'radice')
    rig.add('occhio_punta', V(0, -0.18, 0), V(0, -0.24, 0), 'occhio')
    rig.add('palpebra_su', V(0, 0, 0), V(0, -0.20, 0.22), 'radice')
    rig.add('palpebra_su_punta', V(0, -0.20, 0.22), V(0, -0.24, 0.27), 'palpebra_su')
    rig.add('palpebra_giu', V(0, 0, 0), V(0, -0.20, -0.22), 'radice')
    rig.add('palpebra_giu_punta', V(0, -0.20, -0.22), V(0, -0.24, -0.27), 'palpebra_giu')

    mats = make_materials()
    objs = {}

    # --- globo, cornea, muscoli
    globe = eyeball('globo')
    globe.data.materials.append(mats['M_occhio'])
    cornea = eyeball('cornea', theta_max=THETA_C + 0.14, offset=0.0035, with_uv=False, nphi=160, kf=56)
    cornea.data.materials.append(mats['M_cornea'])
    muscles = [muscle(phi0, f'muscolo_{i}') for i, phi0 in enumerate((radians(90), radians(0), radians(270), radians(180)))]
    for mu in muscles:
        mu.data.materials.append(mats['M_carne'])
    # --- palpebre
    lid_u = eyelid(True, 'pelle_su')        # la mesh non puo' chiamarsi come l'osso: in Unity i nomi si scambiano
    lid_d = eyelid(False, 'pelle_giu')
    for lid in (lid_u, lid_d):
        lid.data.materials.append(mats['M_carne'])
        lid.data.materials.append(mats['M_bordo'])

    # --- nervo ottico
    nerve_ctrl = [V(0, 0.22, 0), V(0, 0.42, -0.01), V(0.05, 0.64, -0.05), V(-0.05, 0.86, -0.12), V(0.03, 1.06, -0.26), V(0.0, 1.20, -0.36)]
    nerve, ncurve = tube('nervo', nerve_ctrl, [0.046, 0.040, 0.032, 0.025, 0.017, 0.009], segs=14, rings=70, seed=1.0)
    nerve.data.materials.append(mats['M_carne'])
    njoints, _ = resample(ncurve, 7)
    rig.chain('nervo', njoints, 'radice')

    # --- tentacoli, alcuni con occhietto
    tents, minieyes = [], []
    for i in range(N_TENT):
        alpha = radians(30 + i * 60)
        length = [0.78, 0.60, 0.92, 0.66, 0.84, 0.72][i]
        ctrl = tendril_ctrl(alpha, length, seed=40 + i)
        t, curve = tube(f'tentacolo_{i}', ctrl, [0.026, 0.020, 0.0145, 0.0105, 0.0072], segs=12, rings=44, seed=3.0 + i)
        t.data.materials.append(mats['M_carne'])
        joints, _ = resample(curve, 5)
        rig.chain(f'tent{i}', joints, 'radice')
        tents.append(t)
        if i in EYE_TIPS:
            tip_dir = (curve[-1] - curve[-3]).normalized()
            fwd = (V(0, -1, 0) * 0.8 + V(cos(alpha), 0, sin(alpha)) * 0.45).normalized()
            up = (V(0, 0, 1) - fwd * fwd.z).normalized()
            rgt = fwd.cross(up).normalized()
            me = eyeball(f'occhietto_{i}', R=0.040, center=curve[-1] + tip_dir * 0.022, fwd=fwd, up=up, rgt=-rgt,
                         nphi=40, kf=14, kb=8)
            me.data.materials.append(mats['M_occhio'])
            minieyes.append((i, me))

    # --- scheletro e pesi
    arm = build_armature(rig)
    set_weights(globe, lambda p: {'occhio': 1.0})
    set_weights(cornea, lambda p: {'occhio': 1.0})
    for mu in muscles:
        set_weights(mu, lambda p: {'occhio': 1.0})
    set_weights(lid_u, lambda p: {'palpebra_su': 1.0})
    set_weights(lid_d, lambda p: {'palpebra_giu': 1.0})
    names, joints = rig.chains['nervo']
    set_weights(nerve, lambda p, n=names, j=joints: chain_weights(j, n, p))
    for i, t in enumerate(tents):
        names, joints = rig.chains[f'tent{i}']
        # l'attacco al globo segue anche un po' l'occhio, il resto solo la catena
        set_weights(t, lambda p, n=names, j=joints: chain_weights(j, n, p))
    for i, me in minieyes:
        names, _ = rig.chains[f'tent{i}']
        set_weights(me, lambda p, n=names: {n[-1]: 1.0})

    allm = [globe, cornea, lid_u, lid_d, nerve] + muscles + tents + [m for _, m in minieyes]
    for ob in allm:
        ob.parent = arm
        md = ob.modifiers.new('Armatura', 'ARMATURE')
        md.object = arm
    tris = sum(sum(len(p.vertices) - 2 for p in ob.data.polygons) for ob in allm)
    print(f'[build] {len(allm)} oggetti, {tris} triangoli, {len(arm.data.bones)} ossa')
    save_blend(BLEND)


# ----------------------------------------------------------------------------- render
def setup_cycles(samples=64, res=(960, 720)):
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
    sc.cycles.transparent_max_bounces = 12
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


def pose_bone(arm, name, angle_deg):
    """Ruota un osso attorno all'asse X del mondo (positivo = abbassa il bordo davanti)."""
    pb = arm.pose.bones[name]
    pb.rotation_mode = 'XYZ'
    pb.rotation_euler = (radians(angle_deg), 0, 0)


def stage_render():
    bpy.ops.wm.open_mainfile(filepath=BLEND)
    sc = bpy.context.scene
    setup_cycles(int(os.environ.get('OCCHIO_SAMPLES', '64')), (960, 720))
    w = bpy.data.worlds.new('mondo')
    sc.world = w
    w.use_nodes = True
    w.node_tree.nodes['Background'].inputs['Color'].default_value = (0.02, 0.02, 0.024, 1)
    add_area('key', (-1.6, -2.2, 1.6), (0, 0, 0), 180, 1.4, (1.0, 0.82, 0.62))
    add_area('rim', (1.2, 1.6, 1.2), (0, 0, 0), 120, 1.0, (0.6, 0.72, 1.0))
    add_area('fill', (2.0, -1.4, -0.2), (0, 0, 0), 25, 2.0, (1.0, 0.9, 0.85))
    arm = bpy.data.objects['Occhio']
    os.makedirs(RENDER_DIR, exist_ok=True)
    views = {
        'fronte': ((0.0, -2.0, 0.05), (0, 0, 0), 60, (0, 0)),
        'tre_quarti': ((-1.5, -1.7, 0.45), (0, 0.15, -0.1), 45, (0, 0)),
        'lato': ((2.4, -0.2, 0.25), (0, 0.35, -0.15), 38, (0, 0)),
        'retro': ((0.9, 2.6, 0.6), (0, 0.4, -0.2), 38, (0, 0)),
        'chiuso': ((0.0, -2.0, 0.05), (0, 0, 0), 60, (44, -34)),
        'strizza': ((-1.0, -1.6, 0.2), (0, 0, 0), 55, (26, -18)),
        'spalancato': ((-1.0, -1.6, 0.2), (0, 0, 0), 55, (-14, 12)),
    }
    pick = [a for a in ARGS if a in views] or list(views)
    for nm in pick:
        loc, tgt, lens, (up, dn) = views[nm]
        pose_bone(arm, 'palpebra_su', up)
        pose_bone(arm, 'palpebra_giu', dn)
        add_camera(loc, tgt, lens)
        sc.render.filepath = os.path.join(RENDER_DIR, f'{nm}.png')
        bpy.ops.render.render(write_still=True)
        print('[render]', sc.render.filepath)


# ----------------------------------------------------------------------------- export
def stage_export():
    bpy.ops.wm.open_mainfile(filepath=BLEND)
    arm = bpy.data.objects['Occhio']
    for pb in arm.pose.bones:
        pb.rotation_mode = 'QUATERNION'
        pb.rotation_quaternion = (1, 0, 0, 0)
        pb.location = (0, 0, 0)
    meshes = [ob for ob in bpy.context.scene.objects if ob.type == 'MESH']
    os.makedirs(EXPORT_DIR, exist_ok=True)
    select_only(meshes + [arm], arm)
    bpy.ops.export_scene.fbx(filepath=os.path.join(EXPORT_DIR, 'occhio.fbx'), use_selection=True,
                             object_types={'ARMATURE', 'MESH'}, apply_unit_scale=True,
                             apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
                             mesh_smooth_type='FACE', use_tspace=True, add_leaf_bones=False,
                             primary_bone_axis='Y', secondary_bone_axis='X', armature_nodetype='NULL',
                             bake_anim=False, path_mode='RELATIVE', embed_textures=False)
    print('[export] fatto:', os.path.join(EXPORT_DIR, 'occhio.fbx'))


if __name__ == '__main__':
    if 'build' in ARGS:
        stage_build()
    if 'render' in ARGS:
        stage_render()
    if 'export' in ARGS:
        stage_export()
