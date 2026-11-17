"""
Medusa volante: libreria di geometria per Blender 4.5 (usata da genera_medusa.py).

Tutto procedurale: tubi a trasporto parallelo, nastri con buchi (merletto), cupola parametrica con fori,
scheletro a catene e pesi calcolati mentre si costruisce (un solo bmesh per materiale, nessuna unione a posteriori).
Assi: Z in alto, il davanti e' +Y (in Unity diventa +Z), unita' metri.
"""
import bpy, bmesh, math, os, sys, random
from mathutils import Vector, Matrix, Quaternion, noise
from math import sin, cos, pi, radians, sqrt, atan2, acos, floor


def V(*a):
    return Vector(a)


def smoothstep(e0, e1, x):
    t = min(1.0, max(0.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


def lerp(a, b, t):
    return a + (b - a) * t


def clamp(x, lo=0.0, hi=1.0):
    return max(lo, min(hi, x))


def wrap_angle(a):
    """Porta un angolo in -pi..pi."""
    a = (a + pi) % (2 * pi) - pi
    return a


def nz(x, y=0.0, z=0.0):
    return noise.noise(Vector((x, y, z)))


def fbm(x, y=0.0, z=0.0, octaves=4, lac=2.03, gain=0.5):
    """Rumore frattale circa -1..1."""
    acc, amp, tot, f = 0.0, 1.0, 0.0, 1.0
    for o in range(octaves):
        acc += noise.noise(Vector((x * f + 13.1 * o, y * f - 7.7 * o, z * f + 3.3 * o))) * amp
        tot += amp
        amp *= gain
        f *= lac
    return acc / tot


# ----------------------------------------------------------------------------- scena
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


def save_blend(path):
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=path)


def select_only(obs, active=None):
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in obs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = active or obs[0]


# ----------------------------------------------------------------------------- curve
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
    """Punti equidistanti (per lunghezza d'arco) lungo una poligonale. Ritorna (punti, lunghezza)."""
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


def frames_along(curve, up_hint=V(0, 0, 1)):
    """Sistema di riferimento a trasporto parallelo: lista di (tangente, normale, binormale)."""
    n = len(curve) - 1
    t0 = (curve[1] - curve[0]).normalized()
    n0 = t0.cross(up_hint)
    if n0.length < 1e-3:
        n0 = t0.cross(V(1, 0, 0))
    n0.normalize()
    out, nrm = [], n0
    for i in range(n + 1):
        tg = (curve[min(i + 1, n)] - curve[max(i - 1, 0)]).normalized()
        nrm = (nrm - tg * nrm.dot(tg))
        if nrm.length < 1e-6:
            nrm = tg.cross(V(1, 0, 0))
        nrm.normalize()
        out.append((tg, nrm, tg.cross(nrm)))
    return out


# ----------------------------------------------------------------------------- scheletro
class Rig:
    def __init__(self):
        self.specs = []       # (nome, testa, coda, genitore)
        self.index = {}       # nome -> indice del gruppo di vertici
        self.chains = {}      # prefisso -> (nomi, punti giunto)

    def add(self, name, head, tail, parent):
        self.index[name] = len(self.specs)
        self.specs.append((name, Vector(head), Vector(tail), parent))

    def chain(self, prefix, joints, parent, first=0):
        names, par = [], parent
        for j in range(len(joints) - 1):
            nm = f'{prefix}_{first + j}'
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


def build_armature(rig, name='Medusa'):
    ad = bpy.data.armatures.new(name)
    ad.display_type = 'STICK'
    arm = bpy.data.objects.new(name, ad)
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


# ----------------------------------------------------------------------------- mesh
class Part:
    """Un bmesh per materiale: i pezzi vi si accumulano con le loro UV e i loro pesi."""

    def __init__(self, name, rig, tile=0.2):
        self.name = name
        self.rig = rig
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.new('UVMap')
        self.dv = self.bm.verts.layers.deform.verify()
        self.tile = tile

    # -- elementi
    def vert(self, co):
        return self.bm.verts.new(Vector(co))

    def face(self, vs, uvs=None):
        try:
            f = self.bm.faces.new(vs)
        except ValueError:
            return None
        if uvs is not None:
            for lo, uv in zip(f.loops, uvs):
                lo[self.uv].uv = uv
        f.smooth = True
        return f

    def weight(self, v, w):
        for b, x in w.items():
            if x > 1e-4:
                v[self.dv][self.rig.index[b]] = x

    def weight_all(self, verts, fn):
        """Pesi di ogni vertice dalla funzione fn(posizione) -> {osso: peso}; al massimo 4 influenze, normalizzate."""
        for v in verts:
            w = fn(v.co)
            top = sorted(((x, b) for b, x in w.items() if x > 1e-4), reverse=True)[:4]
            s = sum(x for x, b in top)
            if s <= 0:
                continue
            for x, b in top:
                v[self.dv][self.rig.index[b]] = x / s

    def orient(self, faces, ref):
        """Rigira le facce date se, in media, guardano contro ref(faccia) (vettore verso l'esterno)."""
        faces = [f for f in faces if f is not None and f.is_valid]
        if not faces:
            return
        tot = 0.0
        for f in faces:
            f.normal_update()
            tot += f.normal.dot(ref(f))
        if tot < 0:
            bmesh.ops.reverse_faces(self.bm, faces=faces)

    # -- chiusura
    def to_object(self, name, mats, coll=None):
        bm = self.bm
        loose = [v for v in bm.verts if not v.link_faces]
        if loose:
            bmesh.ops.delete(bm, geom=loose, context='VERTS')
        bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-6)
        me = bpy.data.meshes.new(name)
        bm.to_mesh(me)
        tris = sum(len(p.vertices) - 2 for p in me.polygons)
        bm.free()
        me.polygons.foreach_set('use_smooth', [True] * len(me.polygons))
        ob = bpy.data.objects.new(name, me)
        link(ob, coll)
        for nm, _, _, _ in self.rig.specs:
            ob.vertex_groups.new(name=nm)
        for m in mats:
            me.materials.append(m)
        return ob, tris


# ----------------------------------------------------------------------------- tubi
def tube(P, ctrl, radii, segs=8, rings=40, wob=0.05, seed=0.0, bulge=None, twist=0.0, flat=1.0,
         tip=True, base_cap=False, uv_unit=False, tile=None, up_hint=V(0, 0, 1), curve_pts=None, lobes=0, lobe_amp=0.0):
    """Tubo lungo una curva. ctrl = punti di controllo (catmull) oppure curve_pts = poligonale gia' pronta.
    radii: profilo del raggio da radice a punta. bulge(q): moltiplicatore del raggio (noduli).
    twist: giri completi del riferimento lungo il tubo (nastri a spirale). uv_unit: u 0..1 attorno, v 0..1 lungo.
    lobes/lobe_amp: sezione a N lobi che ruota con twist (fune ritorta).
    Ritorna (vertici, curva ricampionata, riferimenti, lunghezza)."""
    tile = tile or P.tile
    if curve_pts is None:
        curve, total = resample(catmull(ctrl, max(60, rings * 3)), rings)
    else:
        curve, total = resample(curve_pts, rings)
    frs = frames_along(curve, up_hint)
    grid, info, newv, faces = [], [], [], []
    lens = 0.0
    for i, c in enumerate(curve):
        if i:
            lens += (curve[i] - curve[i - 1]).length
        q = i / rings
        r0 = radius_at(radii, q) * (bulge(q) if bulge else 1.0)
        tg, nr, bn = frs[i]
        ring = []
        for j in range(segs):
            a = 2 * pi * j / segs + twist * 2 * pi * q
            r = r0 * (1 + wob * noise.noise(Vector((cos(a) * 1.7 + seed, sin(a) * 1.7, lens * 18.0)))) * (1 + lobe_amp * cos(lobes * a) if lobes else 1.0)
            v = P.vert(c + (nr * cos(a) * flat + bn * sin(a)) * r)
            ring.append(v)
            newv.append(v)
        grid.append(ring)
        info.append((2 * pi * r0, lens))
    ring_of = {}
    for i in range(rings):
        (ca, la), (cb, lb) = info[i], info[i + 1]
        for j in range(segs):
            j1 = (j + 1) % segs
            if uv_unit:
                uvs = [(j / segs, i / rings), ((j + 1) / segs, i / rings), ((j + 1) / segs, (i + 1) / rings), (j / segs, (i + 1) / rings)]
            else:
                uvs = [(j / segs * ca / tile, la / tile), ((j + 1) / segs * ca / tile, la / tile),
                       ((j + 1) / segs * cb / tile, lb / tile), (j / segs * cb / tile, lb / tile)]
            f = P.face((grid[i][j], grid[i][j1], grid[i + 1][j1], grid[i + 1][j]), uvs)
            if f is not None:
                ring_of[f] = i
                faces.append(f)
    if tip:
        r_end = radius_at(radii, 1.0)
        tv = P.vert(curve[-1] + frs[-1][0] * r_end * 0.9)
        newv.append(tv)
        last = grid[-1]
        cl, ll = info[-1]
        for j in range(segs):
            j1 = (j + 1) % segs
            if uv_unit:
                uvs = [(j / segs, 1.0), ((j + 1) / segs, 1.0), ((j + 0.5) / segs, 1.0)]
            else:
                uvs = [(j / segs * cl / tile, ll / tile), ((j + 1) / segs * cl / tile, ll / tile), ((j + 0.5) / segs * cl / tile, ll / tile + 0.03)]
            f = P.face((last[j], last[j1], tv), uvs)
            if f is not None:
                ring_of[f] = rings
                faces.append(f)
    if base_cap:
        r_b = radius_at(radii, 0.0)
        bv = P.vert(curve[0] - frs[0][0] * r_b * 0.5)
        newv.append(bv)
        first = grid[0]
        for j in range(segs):
            j1 = (j + 1) % segs
            f = P.face((first[j1], first[j], bv), [(0, 0), (1, 0), (0.5, -0.03)])
            if f is not None:
                ring_of[f] = 0
                faces.append(f)
    P.orient(faces, lambda f: f.calc_center_median() - curve[min(ring_of.get(f, 0), rings)])
    return newv, curve, frs, total


def ribbon(P, curve, frs, width, offset, turns=1.0, theta0=0.0, wseg=3, holes=0.0, ragged=0.0, seed=0.0,
           tile=None, ripple=0.0, curl=0.0, stride=1):
    """Nastro (frangia) che si avvolge a spirale attorno a un tubo: parte dalla sua superficie (offset(q)) verso fuori per
    width(q) metri. holes = frazione di facce tolte (merletto), piu' fitte verso il bordo libero; ragged: bordo sfilacciato.
    UV: u 0..1 sulla larghezza, v lunghezza / tile. Ritorna i vertici creati."""
    tile = tile or P.tile
    n = len(curve) - 1
    grid, newv = [], []
    lens = 0.0
    idx = list(range(0, n, stride)) + [n]
    for i in range(n + 1):
        if i:
            lens += (curve[i] - curve[i - 1]).length
        if i not in idx:
            continue
        q = i / n
        tg, nr, bn = frs[i]
        th = theta0 + 2 * pi * turns * q
        radial = nr * cos(th) + bn * sin(th)
        side = tg.cross(radial)
        w = width(q)
        off = offset(q)
        row = []
        for b in range(wseg + 1):
            t = b / wseg
            # il bordo libero ondeggia e si arriccia
            rip = ripple * nz(seed + q * 7.0, t * 2.1, 1.3) * t
            pos = curve[i] + radial * (off + w * t * (1 + rip)) + side * (curl * w * t * t) + tg * (0.1 * w * rip)
            v = P.vert(pos)
            row.append(v)
            newv.append(v)
        grid.append((row, lens))
    faces = []
    rnd = random.Random(int(seed * 1000) + 7)
    for i in range(len(grid) - 1):
        (ra, la), (rb, lb) = grid[i], grid[i + 1]
        for b in range(wseg):
            t = (b + 0.5) / wseg
            p_hole = holes * (0.15 + 0.85 * t * t)
            if ragged > 0 and t > 0.6 and nz(seed * 3 + i * 0.37, b * 1.7, 5.1) > 1.0 - 2 * ragged:
                continue
            if rnd.random() < p_hole * (0.5 + 0.5 * (nz(i * 0.21 + seed, b * 0.9, 2.2) + 1.0)):
                continue
            uvs = [(b / wseg, la / tile), ((b + 1) / wseg, la / tile), ((b + 1) / wseg, lb / tile), (b / wseg, lb / tile)]
            f = P.face((ra[b], ra[b + 1], rb[b + 1], rb[b]), uvs)
            if f is not None:
                faces.append(f)
    # il giro delle facce (radiale, poi tangente) da' sempre normali coerenti (verso -side): niente da orientare
    return newv


def blob(P, center, radii, seg=8, rings=6, rot=None, uv_scale=1.0):
    """Ellissoide a facce (noduli, gocce). Ritorna i vertici."""
    ret = bmesh.ops.create_uvsphere(P.bm, u_segments=seg, v_segments=rings, radius=1.0, calc_uvs=False)
    vs = ret['verts']
    m = Matrix.Translation(Vector(center))
    if rot is not None:
        m = m @ rot.to_matrix().to_4x4()
    m = m @ Matrix.Diagonal((radii[0], radii[1], radii[2], 1.0))
    bmesh.ops.transform(P.bm, matrix=m, verts=vs)
    faces = set()
    for v in vs:
        for f in v.link_faces:
            faces.add(f)
    for f in faces:
        for lo in f.loops:
            co = lo.vert.co - Vector(center)
            lo[P.uv].uv = (0.5 + atan2(co.y, co.x) / (2 * pi) * uv_scale, 0.5 + co.z / max(radii[2], 1e-6) * 0.25 * uv_scale)
        f.smooth = True
    return vs


def boundary_loops(bm, faces):
    """Contorni liberi (bordi con una sola faccia) delle facce date, come liste di vertici ordinate. Serve per mettere un
    labbro carnoso attorno a ogni buco della campana."""
    bm.verts.index_update()
    fs = set(f for f in faces if f is not None and f.is_valid)
    edges = set()
    for f in fs:
        for e in f.edges:
            if len(e.link_faces) == 1:
                edges.add(e)
    adj = {}
    for e in edges:
        a, b = e.verts[0].index, e.verts[1].index
        adj.setdefault(a, set()).add(b)
        adj.setdefault(b, set()).add(a)
    bm.verts.ensure_lookup_table()
    used, loops = set(), []
    for start in list(adj.keys()):
        for nb in list(adj[start]):
            key = (min(start, nb), max(start, nb))
            if key in used:
                continue
            path = [start, nb]
            used.add(key)
            cur = nb
            while True:
                nxt = None
                for c in adj[cur]:
                    if (min(cur, c), max(cur, c)) not in used:
                        nxt = c
                        break
                if nxt is None:
                    break
                used.add((min(cur, nxt), max(cur, nxt)))
                path.append(nxt)
                cur = nxt
                if cur == start:
                    break
            loops.append([bm.verts[i] for i in path])
    return loops


def smooth_loop(pts, iters=2, k=0.5):
    """Arrotonda una poligonale (anche chiusa): media con i vicini, estremi fermi se aperta."""
    closed = (pts[0] - pts[-1]).length < 1e-6
    pts = [Vector(p) for p in pts]
    for _ in range(iters):
        out = []
        n = len(pts)
        for i, p in enumerate(pts):
            if not closed and (i == 0 or i == n - 1):
                out.append(p)
                continue
            a = pts[(i - 1) % n] if closed else pts[i - 1]
            b = pts[(i + 1) % n] if closed else pts[i + 1]
            out.append(p.lerp((a + b) * 0.5, k))
        if closed:
            out[-1] = out[0].copy()
        pts = out
    return pts
