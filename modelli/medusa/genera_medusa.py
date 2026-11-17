"""
Medusa volante (rifacimento 2026-11-17): generatore procedurale per Blender 4.5, dal riferimento dell'utente
(campana traslucida e squarciata con la luce verde dentro, bocca a denti lunghi, decine di tentacoli porosi che gocciolano).

Uso (dalla cartella del file):
  blender -b -P genera_medusa.py -- build            geometria, scheletro, pesi, materiali -> medusa.blend (+ holes.json)
  blender -b -P genera_medusa.py -- render [vista..] anteprime Cycles -> render/
  blender -b -P genera_medusa.py -- export           FBX per Unity -> export/medusa.fbx
Le texture vengono da genera_medusa_tex.py (cartella Assets/_PryingEyes/Textures/Medusa).

Assi: Z in alto, il davanti (la bocca guarda in giu', la fronte in avanti) e' +Y, la destra +X. Unita': metri.
L'animazione la fa Unity a codice (Scripts/JellyRig.cs): qui ci sono solo mesh, ossa e pesi.
Pezzi (un oggetto per materiale, tutti pesati sulle stesse ossa): m_gel (campana), m_interno (sottoombrello), m_carne
(costole, collo, labbra, braccia orali, tentacoli), m_velo (frange e drappi a merletto), m_denti, m_luce (organi e noduli).
"""
import sys, os, json
DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, DIR)
from medusa_lib import *

TEX = os.path.normpath(os.path.join(DIR, "..", "..", "unity", "PryingEyes", "Assets", "_PryingEyes", "Textures", "Medusa"))
BLEND = os.path.join(DIR, 'medusa.blend')
HOLES_JSON = os.path.join(DIR, 'holes.json')
RENDER_DIR = os.path.join(DIR, 'render')
EXPORT_DIR = os.path.join(DIR, 'export')
ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []

# ----------------------------------------------------------------------------- misure
BELL_R = 0.52              # raggio della campana
BELL_H = 0.47              # altezza della cupola sopra il piano del bordo
PHI = radians(103)         # estensione angolare (oltre 90 il bordo gira un po' in dentro)
NSECT = 8                  # settori della campana nello scheletro
S_JOINT = [0.0, 0.40, 0.70, 1.0]       # giunti delle catene di settore lungo il meridiano
S_CENTER = [0.20, 0.55, 0.85]          # centro di ogni osso (peso pieno)
MOUTH_Z = -0.17            # piano della bocca
MOUTH_R = 0.19             # raggio dell apertura
N_RIB = 12
RIB_PSI = [2 * pi * k / N_RIB + (0.07 * sin(k * 12.9898) + 0.05 * cos(k * 78.233)) for k in range(N_RIB)]


# ----------------------------------------------------------------------------- la campana
def ribs_field(s, psi):
    phi = s * PHI
    rr = max(0.07, BELL_R * sin(phi))
    acc = 0.0
    for k, pk in enumerate(RIB_PSI):
        d = wrap_angle(psi - rib_psi(k, s)) * rr
        acc += math.exp(-(d / 0.032) ** 2)
    return min(acc, 1.0) * smoothstep(0.03, 0.22, s)


def rib_psi(k, s):
    """Angolo della costola k a quota s (serpeggia un poco)."""
    return RIB_PSI[k] + 0.11 * nz(s * 2.3, k * 3.17, 0.5)


def bell_p(s, psi, relief=True):
    phi = s * PHI
    r = BELL_R * sin(phi)
    z = BELL_H * cos(phi)
    lob = 1.0 + 0.10 * sin(2 * psi + 0.7) + 0.065 * sin(3 * psi + 2.1) + 0.07 * fbm(cos(psi) * 1.1, sin(psi) * 1.1, 3.3, 2)
    lobz = 1.0 + 0.12 * sin(psi - 0.6) + 0.08 * fbm(cos(psi) * 0.8, sin(psi) * 0.8, 8.1, 2)
    cave = 1.0 - 0.15 * math.exp(-(wrap_angle(psi - 2.4) / 0.55) ** 2) * smoothstep(0.2, 0.8, s)    # un fianco ammaccato
    r *= lob * cave
    z *= lobz
    rim = smoothstep(0.68, 1.0, s)
    r += rim * (0.030 * sin(13 * psi + 1.0) + 0.020 * sin(23 * psi + 0.3) + 0.030 * fbm(psi * 3.0, 5.5, 0.0, 2)) + 0.05 * smoothstep(0.84, 1.0, s)
    z += rim * (0.050 * sin(9 * psi + 0.4) + 0.030 * sin(17 * psi + 2.1) + 0.040 * fbm(psi * 2.2, 1.1, 2.0, 2))
    if relief:
        rel = ribs_field(s, psi) * 0.014 + 0.006 * fbm(psi * 6.0, s * 6.0, 0.7, 3)
        r += rel * sin(phi)
        z += rel * cos(phi)
    return V(r * cos(psi), r * sin(psi), z)


def bell_n(s, psi):
    """Normale uscente della campana (differenze finite)."""
    if s < 0.012:
        return V(0, 0, 1)
    e = 0.004
    ds = bell_p(min(1.0, s + e), psi, False) - bell_p(max(0.0, s - e), psi, False)
    dp = bell_p(s, psi + e * 2, False) - bell_p(s, psi - e * 2, False)
    n = dp.cross(ds)
    if n.length < 1e-9:
        return V(0, 0, 1)
    n.normalize()
    # deve guardare verso fuori rispetto al centro
    c = bell_p(s, psi, False)
    if n.dot(c - V(0, 0, 0.1)) < 0:
        n = -n
    return n


class Arc:
    """Lunghezza d'arco del profilo medio: la UV a disco della campana ha raggio proporzionale ad essa."""

    def __init__(self):
        N = 240
        prev = bell_p(0.0, 0.0, False)
        self.t = [0.0]
        for k in range(1, N + 1):
            p = bell_p(k / N, 0.0, False)
            self.t.append(self.t[-1] + (p - prev).length)
            prev = p
        self.total = self.t[-1]
        self.N = N

    def rho(self, s):
        x = clamp(s) * self.N
        i = min(int(x), self.N - 1)
        a = self.t[i] + (self.t[i + 1] - self.t[i]) * (x - i)
        return 0.5 * a / self.total


ARC = None


def disc_uv(s, psi):
    r = ARC.rho(s)
    return (0.5 + r * cos(psi), 0.5 + r * sin(psi))


def bell_weights(p):
    """Pesi di un punto sulla campana: mescola i due settori vicini e i due livelli vicini (8 settori x 3 livelli)."""
    psi = atan2(p.y, p.x) % (2 * pi)
    r = sqrt(p.x * p.x + p.y * p.y)
    phi = atan2(r / BELL_R, max(p.z, -0.4) / BELL_H)
    s = clamp(phi / PHI)
    if s <= S_CENTER[0]:
        lw = {0: 1.0}
    elif s >= S_CENTER[2]:
        lw = {2: 1.0}
    else:
        k = 0 if s < S_CENTER[1] else 1
        t = (s - S_CENTER[k]) / (S_CENTER[k + 1] - S_CENTER[k])
        lw = {k: 1 - t, k + 1: t}
    f = psi / (2 * pi / NSECT)
    i0 = int(floor(f)) % NSECT
    i1 = (i0 + 1) % NSECT
    t = f - floor(f)
    w = {}
    for k, a in lw.items():
        for i, b in ((i0, 1 - t), (i1, t)):
            nm = f'campana_{i}_{k}'
            w[nm] = w.get(nm, 0.0) + a * b
    return w


def sector_of(psi):
    return int(round(psi / (2 * pi / NSECT))) % NSECT


def solid_uv(P, verts, u=0.04, v=0.5):
    """UV costanti nella fascia piena del velo: gocce e fili restano pieni, senza merletto."""
    for vt in verts:
        for f in vt.link_faces:
            for lo in f.loops:
                lo[P.uv].uv = (u, v)


# ----------------------------------------------------------------------------- cupola e fori
HOLES = []      # (s, psi, raggio in metri)


def make_holes():
    rnd = random.Random(23)
    H = []
    # la calotta in alto a sinistra e' la piu' mangiata (come nel riferimento)
    for s, a, r in [(0.30, 150, 0.050), (0.43, 188, 0.072), (0.52, 128, 0.050), (0.37, 218, 0.040), (0.62, 166, 0.062),
                    (0.24, 118, 0.030), (0.50, 238, 0.034)]:
        H.append((s, radians(a), r))
    # fossa scura in alto a destra
    H.append((0.25, radians(-38), 0.040))
    # fascia verso il bordo
    for _ in range(9):
        H.append((rnd.uniform(0.62, 0.90), rnd.uniform(0, 2 * pi), rnd.uniform(0.032, 0.070)))
    # forellini sparsi
    for _ in range(14):
        H.append((rnd.uniform(0.25, 0.92), rnd.uniform(0, 2 * pi), rnd.uniform(0.014, 0.030)))
    return H


def shell_keep(s, psi):
    """False dove la campana e' bucata o sfilacciata."""
    sin_phi = max(0.07, BELL_R * sin(s * PHI))
    for i, (sc, pc, r) in enumerate(HOLES):
        ds = (s - sc) * ARC.total
        dm = wrap_angle(psi - pc) * sin_phi
        d = sqrt(ds * ds + dm * dm)
        if d > r * 1.45:
            continue
        a = atan2(dm, ds)
        ra = r * (0.78 + 0.42 * fbm(cos(a) * 1.4 + sc * 9, sin(a) * 1.4 + pc * 5, i * 1.7, 2))
        if d < ra:
            return False
    # bordo sfilacciato: tacche a grappoli
    if s > 0.935:
        n = fbm(psi * 4.2, s * 11.0, 1.7, 3)
        if n > 0.02 - (s - 0.935) * 5.0:
            return False
    return True


def dome(P, pfun, ns, nphi, uvfun, keep=None, flip=False, ref_in=False):
    """Cupola a griglia (polo in alto). Ritorna (vertici, facce). ref_in: normali verso l'interno."""
    rings, newv, faces = [], [], []
    for k in range(ns + 1):
        s = k / ns
        if k == 0:
            v = P.vert(pfun(0.0, 0.0))
            rings.append([v])
            newv.append(v)
        else:
            ring = [P.vert(pfun(s, 2 * pi * j / nphi)) for j in range(nphi)]
            rings.append(ring)
            newv += ring
    for k in range(ns):
        a, b = rings[k], rings[k + 1]
        for j in range(nphi):
            j1 = (j + 1) % nphi
            sm, pm = (k + 0.5) / ns, 2 * pi * (j + 0.5) / nphi
            if keep is not None and not keep(sm, pm):
                continue
            s0, s1 = k / ns, (k + 1) / ns
            p0, p1 = 2 * pi * j / nphi, 2 * pi * (j + 1) / nphi
            if len(a) == 1:
                f = P.face((a[0], b[j1], b[j]), [uvfun(s0, p0), uvfun(s1, p1), uvfun(s1, p0)])
            else:
                f = P.face((a[j], a[j1], b[j1], b[j]), [uvfun(s0, p0), uvfun(s0, p1), uvfun(s1, p1), uvfun(s1, p0)])
            if f is not None:
                faces.append(f)
    sign = -1.0 if ref_in else 1.0
    P.orient(faces, lambda f: (f.calc_center_median() - V(0, 0, 0.1)) * sign)
    return newv, faces


# ----------------------------------------------------------------------------- bocca: collo, labbra, denti
def throat(P, rig):
    """Collo e gola: una superficie di rivoluzione continua (parete esterna, labbro, parete interna verso il fondo)."""
    prof = [(0.050, 0.372), (0.062, 0.30), (0.072, 0.20), (0.080, 0.08), (0.100, -0.03), (0.140, -0.11), (MOUTH_R, MOUTH_Z),
            (MOUTH_R + 0.012, MOUTH_Z - 0.040), (MOUTH_R - 0.012, MOUTH_Z - 0.058), (0.140, MOUTH_Z - 0.020), (0.100, -0.03),
            (0.075, 0.08), (0.055, 0.17), (0.032, 0.23), (0.006, 0.27)]
    nseg = 32
    rings, newv, faces = [], [], []
    lens = [0.0]
    for a, b in zip(prof, prof[1:]):
        lens.append(lens[-1] + sqrt((a[0] - b[0]) ** 2 + (a[1] - b[1]) ** 2))
    for i, (r, z) in enumerate(prof):
        ring = []
        for j in range(nseg):
            a = 2 * pi * j / nseg
            rr = r * (1 + 0.06 * fbm(cos(a) * 2 + i, sin(a) * 2, z * 6, 2)) * (1 + 0.05 * sin(a * 7 + z * 20))
            v = P.vert(V(rr * cos(a), rr * sin(a), z))
            ring.append(v)
            newv.append(v)
        rings.append(ring)
    for i in range(len(prof) - 1):
        for j in range(nseg):
            j1 = (j + 1) % nseg
            u0, u1 = j / nseg * 2 * pi * prof[i][0] / P.tile, (j + 1) / nseg * 2 * pi * prof[i][0] / P.tile
            u2, u3 = (j + 1) / nseg * 2 * pi * prof[i + 1][0] / P.tile, j / nseg * 2 * pi * prof[i + 1][0] / P.tile
            f = P.face((rings[i][j], rings[i][j1], rings[i + 1][j1], rings[i + 1][j]),
                       [(u0, lens[i] / P.tile), (u1, lens[i] / P.tile), (u2, lens[i + 1] / P.tile), (u3, lens[i + 1] / P.tile)])
            if f is not None:
                faces.append(f)
    f0 = faces[0]
    f0.normal_update()
    c = f0.calc_center_median()
    if f0.normal.dot(V(c.x, c.y, 0)) < 0:
        bmesh.ops.reverse_faces(P.bm, faces=faces)

    def w(p):
        z = p.z
        wb = smoothstep(-0.02, MOUTH_Z, z) if z > MOUTH_Z - 0.07 else 1.0
        w1 = (1 - smoothstep(-0.06, 0.12, z)) * (1 - wb)
        w0 = 1 - wb - w1
        return {'collo_0': max(0, w0), 'collo_1': max(0, w1), 'bocca': wb}
    P.weight_all(newv, w)
    return newv


N_LOBE = 7


def lips_and_teeth(P_carne, P_den, rig, rnd):
    """7 lobi di labbro che si aprono a petalo e tre file di denti curvi e lunghi."""
    lobes = []
    for i in range(N_LOBE):
        al = 2 * pi * (i + 0.5) / N_LOBE
        d = V(cos(al), sin(al), 0)
        base = V(0, 0, MOUTH_Z - 0.01) + d * (MOUTH_R - 0.005)
        ctrl = [base, base + d * 0.040 + V(0, 0, -0.050), base + d * 0.100 + V(0, 0, -0.120), base + d * 0.150 + V(0, 0, -0.205)]
        radii = [0.041, 0.039, 0.032, 0.017]
        vs, curve, frs, tot = tube(P_carne, ctrl, radii, segs=8, rings=16, seed=i * 1.3, wob=0.12, lobes=3, lobe_amp=0.10, twist=0.5)
        joints, _ = resample(curve, 2)
        rig.chain(f'labbro_{i}', joints, 'bocca')
        names, js = rig.chains[f'labbro_{i}']
        P_carne.weight_all(vs, lambda p, n=names, j=js: chain_weights(j, n, p))
        lobes.append((al, curve))
    # denti: file concentriche; ognuno e' un cono curvo verso l'asse con la punta traslucida
    rows = [(MOUTH_R - 0.012, MOUTH_Z - 0.03, 22, 0.13, 0.19, 0.0135, True),
            (0.150, MOUTH_Z - 0.00, 15, 0.09, 0.14, 0.0115, False),
            (0.105, MOUTH_Z + 0.05, 9, 0.05, 0.09, 0.0085, False)]
    for ri, (rad, z0, count, lmin, lmax, thick, on_lobes) in enumerate(rows):
        for t in range(count):
            al = 2 * pi * (t + rnd.uniform(-0.3, 0.3)) / count
            d = V(cos(al), sin(al), 0)
            length = rnd.uniform(lmin, lmax)
            base = V(0, 0, z0 + rnd.uniform(-0.008, 0.008)) + d * rad
            side = V(-d.y, d.x, 0) * rnd.uniform(-0.28, 0.28)
            tip = base + V(0, 0, -length) - d * (length * (0.42 if ri == 0 else 0.34)) + side * length
            mid = (base + tip) * 0.5 + d * (length * 0.16)
            vs, curve, frs, tot = tube(P_den, [base, mid, tip], [thick, thick * 0.80, thick * 0.36, thick * 0.07], segs=5, rings=6,
                                       wob=0.06, seed=ri * 10 + t, uv_unit=True, tip=True, base_cap=True)
            if on_lobes:
                k = min(range(N_LOBE), key=lambda i: abs(wrap_angle(lobes[i][0] - al)))
                P_den.weight_all(vs, lambda p, k=k: {f'labbro_{k}_1': 0.55 + 0.45 * smoothstep(MOUTH_Z - 0.01, MOUTH_Z - 0.17, p.z),
                                                     f'labbro_{k}_0': 0.45 - 0.45 * smoothstep(MOUTH_Z - 0.01, MOUTH_Z - 0.17, p.z)})
            else:
                P_den.weight_all(vs, lambda p: {'bocca': 1.0})
    return lobes


# ----------------------------------------------------------------------------- catene di tubi con nastri
def random_walk(root, d0, length, n, rnd, curl=0.25, droop=0.0, side_bias=None, freq=1.0):
    """Percorso morbido: parte in direzione d0 e serpeggia (S larghe)."""
    d = Vector(d0).normalized()
    pts, step = [Vector(root)], length / n
    ph1, ph2 = rnd.uniform(0, 2 * pi), rnd.uniform(0, 2 * pi)
    for i in range(n):
        t = i / max(1, n - 1)
        side = V(-d.y, d.x, 0)
        if side.length < 1e-3:
            side = V(1, 0, 0)
        side.normalize()
        k1 = sin(t * pi * 2.2 * freq + ph1) * curl
        k2 = sin(t * pi * 1.4 * freq + ph2) * curl * 0.7
        d = (d + side * k1 * 0.55 + d.cross(side) * k2 * 0.4 + V(0, 0, -droop)).normalized()
        if side_bias is not None:
            d = (d + side_bias * 0.04).normalized()
        pts.append(pts[-1] + d * step)
    return pts


def drape_weights(p, names, js, rim_bone):
    """Drappo del bordo: in cima segue l'osso del bordo, piu' giu' la sua catena."""
    w = chain_weights(js, names, p)
    top = 1.0 - smoothstep(0.0, 0.07, (p - js[0]).length)
    w = {k: v * (1 - top) for k, v in w.items()}
    w[rim_bone] = w.get(rim_bone, 0.0) + top
    return w


def veil_p(s, psi):
    return bell_p(s, psi) + bell_n(s, psi) * (0.030 + 0.030 * (fbm(psi * 2.1, s * 2.7, 4.4, 3) + 0.6)) * smoothstep(0.0, 0.2, s)


def lace_lips(P_car, P_gel, faces):
    """Un labbro carnoso attorno a ogni buco e lungo il bordo sfilacciato della campana."""
    n = 0
    for verts in boundary_loops(P_gel.bm, faces):
        if len(verts) < 5:
            continue
        lp = [Vector(v.co) for v in verts]
        if (lp[0] - lp[-1]).length > 1e-6:
            lp = lp + [lp[0]]
            verts = verts + [verts[0]]
        pts = smooth_loop(lp, iters=5, k=0.6)
        # il bordo del guscio segue il percorso arrotondato: niente scalini sotto il labbro
        for v, p in zip(verts, pts):
            v.co = p
        out = []
        for p in pts:
            psi = atan2(p.y, p.x) % (2 * pi)
            r = sqrt(p.x * p.x + p.y * p.y)
            phi = atan2(r / BELL_R, max(p.z, -0.4) / BELL_H)
            sv = clamp(phi / PHI)
            out.append(p + bell_n(sv, psi) * 0.004)
        length = sum((a - b).length for a, b in zip(out, out[1:]))
        rings = max(8, int(length / 0.028))
        rad = 0.0058 + 0.0010 * (n % 3)
        vs, _, _, _ = tube(P_car, None, [rad, rad * 1.25, rad, rad * 1.2, rad], segs=4, rings=rings, wob=0.22, tip=False, curve_pts=out, seed=n * 1.7)
        P_car.weight_all(vs, bell_weights)
        n += 1
    return n


def satellite(P, curve, frs, radii, turns, phase, radius, names, js, seed):
    """Filo sottile che si avvolge a elica attorno a una fune."""
    n = len(curve) - 1
    pts = []
    for i in range(n + 1):
        q = i / n
        tg, nr, bn = frs[i]
        th = phase + 2 * pi * turns * q
        pts.append(curve[i] + (nr * cos(th) + bn * sin(th)) * (radius_at(radii, q) * 1.35 + radius * 0.4))
    vs, _, _, _ = tube(P, None, [radius, radius * 0.85, radius * 0.5, radius * 0.18], segs=4, rings=n, wob=0.12, curve_pts=pts, seed=seed)
    P.weight_all(vs, lambda p, nn=names, jj=js: chain_weights(jj, nn, p))


# ----------------------------------------------------------------------------- build
def make_materials():
    mats = {}

    def load(path, cs):
        if not os.path.exists(path):
            return None
        im = bpy.data.images.get(os.path.basename(path)) or bpy.data.images.load(path)
        im.colorspace_settings.name = cs
        im.alpha_mode = 'CHANNEL_PACKED'
        return im

    def principled(name, base_rgb, rough, tex=None, alpha=None, emit=None, emit_k=0.0, sss=0.0, transm=0.0, ior=1.4):
        m = bpy.data.materials.new(name)
        m.use_nodes = True
        nt = m.node_tree
        nt.nodes.clear()
        out = nt.nodes.new('ShaderNodeOutputMaterial')
        bs = nt.nodes.new('ShaderNodeBsdfPrincipled')
        nt.links.new(bs.outputs[0], out.inputs['Surface'])
        bs.inputs['Base Color'].default_value = (*base_rgb, 1)
        bs.inputs['Roughness'].default_value = rough
        bs.inputs['IOR'].default_value = ior
        bs.inputs['Subsurface Weight'].default_value = sss
        bs.inputs['Transmission Weight'].default_value = transm
        if sss > 0:
            bs.inputs['Subsurface Radius'].default_value = (0.9, 0.35, 0.2)
        im = load(os.path.join(TEX, tex + '_base.png'), 'sRGB') if tex else None
        if im is not None:
            n = nt.nodes.new('ShaderNodeTexImage')
            n.image = im
            n.interpolation = 'Cubic'
            nt.links.new(n.outputs['Color'], bs.inputs['Base Color'])
            if alpha:
                nt.links.new(n.outputs['Alpha'], bs.inputs['Alpha'])
        nm_ = load(os.path.join(TEX, tex + '_normal.png'), 'Non-Color') if tex else None
        if nm_ is not None:
            n2 = nt.nodes.new('ShaderNodeTexImage')
            n2.image = nm_
            nn = nt.nodes.new('ShaderNodeNormalMap')
            nt.links.new(n2.outputs['Color'], nn.inputs['Color'])
            nt.links.new(nn.outputs['Normal'], bs.inputs['Normal'])
        mk = load(os.path.join(TEX, tex + '_mask.png'), 'Non-Color') if tex else None
        if mk is not None:
            n4 = nt.nodes.new('ShaderNodeTexImage')
            n4.image = mk
            inv = nt.nodes.new('ShaderNodeMath')
            inv.operation = 'SUBTRACT'
            inv.inputs[0].default_value = 1.0
            nt.links.new(n4.outputs['Alpha'], inv.inputs[1])
            nt.links.new(inv.outputs[0], bs.inputs['Roughness'])
        em = load(os.path.join(TEX, tex + '_emiss.png'), 'sRGB') if tex else None
        if em is not None:
            n3 = nt.nodes.new('ShaderNodeTexImage')
            n3.image = em
            nt.links.new(n3.outputs['Color'], bs.inputs['Emission Color'])
            bs.inputs['Emission Strength'].default_value = emit_k
        elif emit is not None:
            bs.inputs['Emission Color'].default_value = (*emit, 1)
            bs.inputs['Emission Strength'].default_value = emit_k
        return m

    mats['M_gel'] = principled('M_gel', (0.85, 0.74, 0.55), 0.06, 'gel', alpha=True, emit_k=2.0, transm=0.6, ior=1.25)
    mats['M_cappa'] = principled('M_cappa', (0.85, 0.70, 0.55), 0.10, 'cappa', alpha=True, sss=0.2, transm=0.3)
    mats['M_velo'] = principled('M_velo', (0.85, 0.66, 0.52), 0.12, 'velo', alpha=True, sss=0.2, transm=0.3)
    mats['M_carne'] = principled('M_carne', (0.42, 0.10, 0.11), 0.22, 'carne', sss=0.3)
    mats['M_interno'] = principled('M_interno', (0.34, 0.07, 0.08), 0.25, 'interno', emit_k=1.2, sss=0.25)
    mats['M_denti'] = principled('M_denti', (0.86, 0.80, 0.62), 0.20, 'dente', sss=0.35)
    mats['M_luce'] = principled('M_luce', (0.20, 0.30, 0.05), 0.3, 'luce', emit=(0.55, 1.0, 0.12), emit_k=4.5)
    return mats


def stage_build():
    global ARC, HOLES
    reset()
    ARC = Arc()
    HOLES = make_holes()
    rnd = random.Random(5)
    rig = Rig()
    mats = make_materials()
    P_gel = Part('m_gel', rig)
    P_int = Part('m_interno', rig)
    P_car = Part('m_carne', rig, tile=0.2)
    P_vel = Part('m_velo', rig, tile=0.2)
    P_den = Part('m_denti', rig)
    P_luz = Part('m_luce', rig, tile=0.2)
    P_cap = Part('m_cappa', rig)

    # --- scheletro del corpo: radice, corpo, 8 settori x 3 ossa lungo il meridiano, collo e bocca
    rig.add('radice', V(0, 0, 0), V(0, 0, 0.05), None)
    rig.add('corpo', V(0, 0, 0.06), V(0, 0, 0.2), 'radice')
    for i in range(NSECT):
        psi = 2 * pi * i / NSECT
        joints = []
        for s in S_JOINT:
            p = bell_p(s, psi, False)
            joints.append(p - bell_n(s, psi) * 0.03 if s > 0.01 else V(0, 0, p.z - 0.03))
        rig.chain(f'campana_{i}', joints, 'corpo')
    rig.add('collo_0', V(0, 0, 0.37), V(0, 0, 0.12), 'corpo')
    rig.add('collo_1', V(0, 0, 0.12), V(0, 0, MOUTH_Z), 'collo_0')
    rig.add('bocca', V(0, 0, MOUTH_Z), V(0, 0, MOUTH_Z - 0.10), 'collo_1')
    rig.add('fronte', V(0, 0.55, 0.05), V(0, 0.62, 0.05), 'corpo')

    # --- cupola: guscio di gel con buchi e labbra carnose, sottoombrello, velo esterno, costole e vene
    gv, gf = dome(P_gel, lambda s, psi: bell_p(s, psi), 58, 116, disc_uv, keep=shell_keep)
    P_gel.weight_all(gv, bell_weights)
    n_lips = lace_lips(P_car, P_gel, gf)
    print(f'[build] labbra attorno ai buchi: {n_lips}')

    def inner_p(s, psi):
        th = 0.085 * (1 - s) ** 1.3 + 0.012
        return bell_p(s, psi, False) - bell_n(s, psi) * th
    iv, _ = dome(P_int, inner_p, 32, 72, disc_uv, keep=lambda s, psi: s < 0.975, ref_in=True)
    P_int.weight_all(iv, bell_weights)
    vv, _ = dome(P_cap, veil_p, 44, 96, disc_uv, keep=lambda s, psi: 0.10 <= s <= 0.985)
    P_cap.weight_all(vv, bell_weights)

    for k in range(N_RIB):
        ctrl = [bell_p(s, rib_psi(k, s)) + bell_n(s, rib_psi(k, s)) * 0.004 for s in [0.05 + 0.92 * t / 21 for t in range(22)]]
        vs, _, _, _ = tube(P_car, ctrl, [0.016, 0.018, 0.016, 0.012, 0.008], segs=6, rings=18, seed=k * 2.1, wob=0.12, tip=True)
        P_car.weight_all(vs, bell_weights)
    for k in range(12):                       # vene secondarie che nascono a meta' e arrivano al bordo
        s0 = rnd.uniform(0.22, 0.55)
        psi0 = rnd.uniform(0, 2 * pi)
        ph = rnd.uniform(0, 6.28)
        amp = rnd.uniform(0.12, 0.32)
        ctrl = []
        for t in range(14):
            s = s0 + (0.96 - s0) * t / 13
            psi = psi0 + amp * sin((s - s0) * 7 + ph) + 0.6 * (s - s0) * (1 if k % 2 else -1)
            ctrl.append(bell_p(s, psi) + bell_n(s, psi) * 0.004)
        vs, _, _, _ = tube(P_car, ctrl, [0.009, 0.008, 0.006, 0.004], segs=5, rings=16, seed=k * 3.3, wob=0.10)
        P_car.weight_all(vs, bell_weights)

    # --- organi che brillano dentro la campana (lobi, canale ad anello, bulbo centrale)
    for k in range(5):
        psi = 2 * pi * k / 5 + 0.3
        ctrl = [bell_p(s, psi + 0.35 * sin(s * 4 + k)) - bell_n(s, psi) * 0.048 for s in (0.20, 0.32, 0.44, 0.56, 0.68)]
        vs, _, _, _ = tube(P_luz, ctrl, [0.012, 0.032, 0.038, 0.028, 0.010], segs=8, rings=16, seed=k, wob=0.14)
        P_luz.weight_all(vs, bell_weights)
    ctrl = [bell_p(0.80 + 0.02 * nz(t * 1.7, 2.2), 2 * pi * t / 26 * 1.04) - bell_n(0.8, 2 * pi * t / 26) * 0.032 for t in range(27)]
    vs, _, _, _ = tube(P_luz, ctrl, [0.007, 0.009, 0.007, 0.009, 0.007], segs=6, rings=40, wob=0.12)
    P_luz.weight_all(vs, bell_weights)
    vs = blob(P_luz, V(0, 0, BELL_H - 0.075), (0.055, 0.055, 0.040), seg=10, rings=7)
    P_luz.weight_all(vs, bell_weights)
    vs = blob(P_luz, V(0, 0, 0.17), (0.040, 0.040, 0.055), seg=8, rings=6)          # brace in fondo alla gola
    P_luz.weight_all(vs, lambda p: {'collo_0': 1.0})

    # --- collo, labbra, denti
    throat(P_car, rig)
    lobes = lips_and_teeth(P_car, P_den, rig, rnd)

    # --- braccia orali: grosse funi carnose con frange a merletto, attorno alla bocca
    n_arm = 6
    arm_tips = []
    for i in range(n_arm):
        al = 2 * pi * (i + rnd.uniform(-0.15, 0.15)) / n_arm
        d = V(cos(al), sin(al), 0)
        root = V(0, 0, MOUTH_Z + 0.07) + d * (MOUTH_R + 0.03)
        length = rnd.uniform(0.60, 1.05)
        pts = random_walk(root, d * 0.75 + V(0, 0, -0.65), length, 8, rnd, curl=0.60, droop=0.03, freq=1.15)
        radii = [0.034, 0.038, 0.031, 0.022, 0.013, 0.005]
        vs, curve, frs, tot = tube(P_car, pts, radii, segs=8, rings=30, seed=40 + i, wob=0.16, lobes=3, lobe_amp=0.08, twist=rnd.uniform(1.5, 3.0),
                                   bulge=lambda q, i=i: 1 + 0.25 * sin(q * 15 + i) * (1 - q * 0.6))
        joints, _ = resample(curve, 5)
        rig.chain(f'braccio_{i}', joints, 'bocca')
        names, js = rig.chains[f'braccio_{i}']
        P_car.weight_all(vs, lambda p, n=names, j=js: chain_weights(j, n, p))
        rv = ribbon(P_vel, curve, frs, width=lambda q: 0.130 * (1 - q) ** 0.7 + 0.008, offset=lambda q: radius_at(radii, q) * 0.85,
                    turns=rnd.uniform(1.4, 2.4), theta0=rnd.uniform(0, 6.28), wseg=3, holes=0.0, ragged=0.12, seed=i * 1.9, stride=2,
                    ripple=0.5, curl=0.4, tile=0.07)
        P_vel.weight_all(rv, lambda p, n=names, j=js: chain_weights(j, n, p))
        satellite(P_car, curve, frs, radii, rnd.uniform(2, 4), rnd.uniform(0, 6.28), 0.009, names, js, 77 + i)
        arm_tips.append((curve[-1], names[-1]))

    # --- tentacoli: lunghe funi porose con noduli luminosi, fili a elica e nastri a merletto
    N_TENT = 16
    lengths = [1.55, 1.20, 1.82, 1.35, 1.68, 1.15, 1.92, 1.42, 1.72, 1.26, 1.52, 1.86, 1.32, 1.62, 1.46, 1.76]
    tent_tips, beads = [], []
    for j in range(N_TENT):
        psi = 2 * pi * (j + rnd.uniform(0.25, 0.75)) / N_TENT
        outd = V(cos(psi), sin(psi), 0)
        sec = sector_of(psi)
        rimp = bell_p(0.965, psi, False)
        root = V(rimp.x * 0.93, rimp.y * 0.93, rimp.z + 0.02)
        pts = random_walk(root, outd * 0.40 + V(0, 0, -0.90), lengths[j], 10, rnd, curl=0.55, droop=0.0, freq=rnd.uniform(0.8, 1.45))
        radii = [0.034, 0.029, 0.021, 0.013, 0.0055]
        nod = sorted(rnd.uniform(0.12, 0.88) for _ in range(rnd.choice((2, 3, 3, 4))))
        bulge = lambda q, nod=nod: 1 + 0.60 * sum(math.exp(-((q - n) / 0.022) ** 2) for n in nod)
        vs, curve, frs, tot = tube(P_car, pts, radii, segs=8, rings=40, seed=100 + j, wob=0.14, bulge=bulge, lobes=3, lobe_amp=0.10,
                                   twist=rnd.uniform(2.0, 5.0))
        nb = 8 if lengths[j] > 1.4 else 7
        joints, _ = resample(curve, nb)
        rig.chain(f'tent_{j}', joints, f'campana_{sec}_2')
        names, js = rig.chains[f'tent_{j}']
        P_car.weight_all(vs, lambda p, n=names, jj=js: chain_weights(jj, n, p))
        if j % 4 != 3:
            rv = ribbon(P_vel, curve, frs, width=lambda q: 0.115 * (1 - q) ** 0.75 + 0.008, offset=lambda q: radius_at(radii, q) * 0.9,
                        turns=rnd.uniform(1.8, 3.4), theta0=rnd.uniform(0, 6.28), wseg=3, holes=0.0, ragged=0.10, seed=j * 2.3, stride=2,
                        ripple=0.6, curl=0.5, tile=0.07)
            P_vel.weight_all(rv, lambda p, n=names, jj=js: chain_weights(jj, n, p))
        if j % 2 == 0:
            satellite(P_car, curve, frs, radii, rnd.uniform(3, 6), rnd.uniform(0, 6.28), 0.0085, names, js, 300 + j)
        for q in nod:
            if rnd.random() < 0.8:
                beads.append((curve[int(q * 40)], names, js, radius_at(radii, q) * 1.05))
        tent_tips.append((curve[-1], names[-1]))
    for pos, names, js, r in beads:
        rr = r * rnd.uniform(1.15, 1.5)
        vs = blob(P_luz, pos, (rr, rr, rr * 1.25), seg=7, rings=5)
        P_luz.weight_all(vs, lambda p, n=names, jj=js: chain_weights(jj, n, p))

    # --- drappi del bordo: lembi a merletto che pendono dalla campana
    N_DRAP = 14
    drap_tips = []
    for k in range(N_DRAP):
        psi = 2 * pi * (k + rnd.uniform(0.0, 1.0)) / N_DRAP
        sec = sector_of(psi)
        outd = V(cos(psi), sin(psi), 0)
        length = rnd.uniform(0.32, 0.70)
        w0 = rnd.uniform(0.15, 0.28)
        base = bell_p(0.955, psi, False)
        root = V(base.x, base.y, base.z + 0.01)
        pts = random_walk(root, outd * 0.35 + V(0, 0, -0.93), length, 4, rnd, curl=0.22)
        curve, tot = resample(catmull(pts, 40), 12)
        tang = V(-outd.y, outd.x, 0)
        nw = 4
        grid, nv, lens = [], [], 0.0
        for i in range(13):
            if i:
                lens += (curve[i] - curve[i - 1]).length
            q = i / 12
            ww = w0 * (1 - q) ** 0.8 * (1 + 0.22 * nz(k * 3.1, q * 4.0)) + 0.012
            row = []
            for b in range(nw + 1):
                t = b / nw - 0.5
                sag = 0.03 * sin(b * 1.9 + k) * (1 - q)
                v = P_vel.vert(curve[i] + tang * (ww * t) + outd * (sag + 0.018 * nz(k, q * 5, b)))
                row.append(v)
                nv.append(v)
            grid.append((row, lens, ww))
        faces = []
        # UV del velo: u = dall'attacco (0) al bordo libero (1), v = traverso in metri / 0.2 (cosi' il merletto si fa piu' fitto in basso)
        for i in range(12):
            (ra, la, wa), (rb, lb, wb) = grid[i], grid[i + 1]
            ua, ub = la / lens, lb / lens
            for b in range(nw):
                f = P_vel.face((ra[b], ra[b + 1], rb[b + 1], rb[b]),
                               [(ua, (b / nw - 0.5) * wa / 0.2), (ua, ((b + 1) / nw - 0.5) * wa / 0.2),
                                (ub, ((b + 1) / nw - 0.5) * wb / 0.2), (ub, (b / nw - 0.5) * wb / 0.2)])
                if f is not None:
                    faces.append(f)
        P_vel.orient(faces, lambda f: outd)
        joints, _ = resample(curve, 2)
        rig.chain(f'drappo_{k}', joints, f'campana_{sec}_2')
        names, js = rig.chains[f'drappo_{k}']
        P_vel.weight_all(nv, lambda p, n=names, jj=js, sc=sec: drape_weights(p, n, jj, f'campana_{sc}_2'))
        drap_tips.append((curve[-1], names[-1]))

    # --- fili sottili tra le braccia (gruppi che si muovono insieme)
    for g in range(5):
        psi = 2 * pi * (g + rnd.uniform(0.0, 0.8)) / 5
        outd = V(cos(psi), sin(psi), 0)
        sec = sector_of(psi)
        rimp = bell_p(0.90, psi, False)
        length = rnd.uniform(0.45, 0.85)
        base_pts = random_walk(V(rimp.x * 0.7, rimp.y * 0.7, rimp.z - 0.02), outd * 0.2 + V(0, 0, -0.97), length, 5, rnd, curl=0.3)
        curves = []
        for t in range(3):
            off = V(rnd.uniform(-0.03, 0.03), rnd.uniform(-0.03, 0.03), rnd.uniform(-0.02, 0.02))
            ctrl = [p + off * (0.3 + i * 0.3) for i, p in enumerate(base_pts)]
            vs, curve, frs, tot = tube(P_car, ctrl, [0.0050, 0.0042, 0.0028, 0.0012], segs=4, rings=14, seed=200 + g * 3 + t, wob=0.1)
            curves.append((vs, curve))
        joints, _ = resample(curves[0][1], 3)
        rig.chain(f'filo_{g}', joints, f'campana_{sec}_2')
        names, js = rig.chains[f'filo_{g}']
        for vs, curve in curves:
            P_car.weight_all(vs, lambda p, n=names, jj=js: chain_weights(jj, n, p))

    # --- gocce appese alle punte (braccia, tentacoli, drappi) e ai denti
    for pos, name in arm_tips + tent_tips[::2] + drap_tips[::2]:
        ln = rnd.uniform(0.05, 0.12)
        r = rnd.uniform(0.005, 0.009)
        thread = [pos, pos + V(0, 0, -ln * 0.5), pos + V(0, 0, -ln)]
        vs, curve, frs, tot = tube(P_vel, thread, [0.0012, 0.0010, 0.0008], segs=4, rings=4, wob=0.0, uv_unit=True, tip=False)
        bv = blob(P_vel, pos + V(0, 0, -ln - r * 1.2), (r, r, r * 1.5), seg=7, rings=5)
        solid_uv(P_vel, vs + bv)
        P_vel.weight_all(vs + bv, lambda p, n=name: {n: 1.0})
    for ri in range(8):
        al = 2 * pi * (ri + 0.3) / 8
        pos = V(cos(al) * 0.12, sin(al) * 0.12, MOUTH_Z - 0.17)
        r = rnd.uniform(0.004, 0.007)
        bv = blob(P_vel, pos + V(0, 0, -0.02), (r, r, r * 1.6), seg=6, rings=4)
        solid_uv(P_vel, bv)
        P_vel.weight_all(bv, lambda p: {'bocca': 1.0})

    # --- oggetti, armatura, materiali
    arm = build_armature(rig)
    objs, total = [], 0
    for P, mname in ((P_gel, 'M_gel'), (P_cap, 'M_cappa'), (P_int, 'M_interno'), (P_car, 'M_carne'), (P_vel, 'M_velo'), (P_den, 'M_denti'), (P_luz, 'M_luce')):
        ob, tris = P.to_object(P.name, [mats[mname]])
        ob.parent = arm
        md = ob.modifiers.new('Armatura', 'ARMATURE')
        md.object = arm
        objs.append(ob)
        total += tris
        print(f'[build] {P.name}: {tris} triangoli, {len(ob.data.vertices)} vertici')
    print(f'[build] totale {total} triangoli, {len(arm.data.bones)} ossa')
    # fori per il generatore di texture: centro in UV a disco e raggio in unita' UV
    mpu = ARC.total / 0.5
    organs = [[(ARC.rho(s), 2 * pi * k / 5 + 0.3 + 0.35 * sin(s * 4 + k)) for s in [0.20 + 0.48 * t / 24 for t in range(25)]] for k in range(5)]
    ring = [(ARC.rho(0.80 + 0.02 * nz(t * 1.7, 2.2)), 2 * pi * t / 26 * 1.04) for t in range(27)]
    with open(HOLES_JSON, 'w') as fh:
        json.dump({'meters_per_uv': mpu, 'holes': [{'u': disc_uv(s, p)[0], 'v': disc_uv(s, p)[1], 'r': r / mpu * 1.0, 'rho': ARC.rho(s)} for s, p, r in HOLES],
                   'ribs': [[(ARC.rho(s), rib_psi(k, s)) for s in [0.04 + 0.93 * t / 40 for t in range(41)]] for k in range(N_RIB)],
                   'organs': organs, 'ring': ring, 'bulb': 0.075 / mpu}, fh)
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
    sc.cycles.transparent_max_bounces = 16
    sc.cycles.max_bounces = 10
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


def pose_axis(arm, name, world_axis, deg):
    """Ruota l'osso di 'deg' gradi attorno a un asse espresso nel mondo (a riposo): lo converte nello spazio locale dell'osso."""
    bone = arm.data.bones[name]
    local = bone.matrix_local.to_3x3().inverted() @ Vector(world_axis)
    pb = arm.pose.bones[name]
    pb.rotation_mode = 'AXIS_ANGLE'
    pb.rotation_axis_angle = (radians(deg), local.x, local.y, local.z)


def pose_all(arm, body_pitch=0.0, contract=0.0, mouth=0.0):
    """Posa di prova: inclina il corpo (gradi, positivo = la bocca va in avanti), contrae la campana e apre la bocca."""
    for pb in arm.pose.bones:
        pb.rotation_mode = 'QUATERNION'
        pb.rotation_quaternion = (1, 0, 0, 0)
    pose_axis(arm, 'corpo', (1, 0, 0), body_pitch)
    for i in range(NSECT):
        psi = 2 * pi * i / NSECT
        axis = (-sin(psi), cos(psi), 0)             # asse tangente: il bordo si chiude verso l'asse
        for k, a in enumerate((9, 13, 17)):
            pose_axis(arm, f'campana_{i}_{k}', axis, contract * a)
    for i in range(6):
        al = 2 * pi * (i + 0.5) / 6
        pose_axis(arm, f'labbro_{i}_0', (-sin(al), cos(al), 0), -mouth * 30)


def stage_render():
    bpy.ops.wm.open_mainfile(filepath=BLEND)
    sc = bpy.context.scene
    setup_cycles(int(os.environ.get('MEDUSA_SAMPLES', '96')), (960, 720))
    w = bpy.data.worlds.new('mondo')
    sc.world = w
    w.use_nodes = True
    bg = w.node_tree.nodes['Background']
    bg.inputs['Color'].default_value = (0.020, 0.006, 0.006, 1)
    bg.inputs['Strength'].default_value = 1.0
    # luci: luna rossa dietro e sopra, un riempimento tenue caldo davanti
    add_area('luna', (-1.2, -2.0, 2.4), (0, 0, 0.2), 900, 1.6, (1.0, 0.18, 0.08))
    add_area('chiave', (1.8, 2.2, 0.2), (0, 0, 0.0), 160, 1.8, (1.0, 0.78, 0.62))
    add_area('riemp', (-2.0, 1.6, -1.2), (0, 0, -0.3), 90, 2.5, (0.55, 0.62, 1.0))
    arm = bpy.data.objects['Medusa']
    os.makedirs(RENDER_DIR, exist_ok=True)
    views = {
        # nome: (camera, bersaglio, lente, (pitch, contrazione, bocca))
        'riferimento': ((1.5, 1.7, -0.9), (0.0, 0.0, -0.1), 38, (50, 0.0, 0.5)),
        'lato': ((2.6, 0.2, 0.2), (0, 0, -0.4), 30, (0, 0.0, 0.0)),
        'sotto': ((0.4, 0.9, -2.4), (0, 0, 0.0), 40, (0, 0.0, 0.5)),
        'tre_quarti': ((1.8, 1.9, 0.6), (0, 0, -0.35), 36, (25, 0.0, 0.0)),
        'contratta': ((2.6, 0.2, 0.2), (0, 0, -0.4), 30, (0, 1.0, 0.0)),
        'intero': ((3.4, 1.6, -0.4), (0, 0, -0.55), 34, (0, 0.0, 0.0)),
        'bocca': ((0.0, 0.85, -0.55), (0, 0, -0.18), 40, (60, 0.0, 1.0)),
    }
    pick = [a for a in ARGS if a in views] or list(views)
    for nm in pick:
        loc, tgt, lens, (pitch, contract, mouth) = views[nm]
        pose_all(arm, pitch, contract, mouth)
        add_camera(loc, tgt, lens)
        sc.render.filepath = os.path.join(RENDER_DIR, f'{nm}.png')
        bpy.ops.render.render(write_still=True)
        print('[render]', sc.render.filepath)


# ----------------------------------------------------------------------------- export
def stage_export():
    bpy.ops.wm.open_mainfile(filepath=BLEND)
    arm = bpy.data.objects['Medusa']
    for pb in arm.pose.bones:
        pb.rotation_mode = 'QUATERNION'
        pb.rotation_quaternion = (1, 0, 0, 0)
        pb.location = (0, 0, 0)
    meshes = [ob for ob in bpy.context.scene.objects if ob.type == 'MESH']
    os.makedirs(EXPORT_DIR, exist_ok=True)
    select_only(meshes + [arm], arm)
    bpy.ops.export_scene.fbx(filepath=os.path.join(EXPORT_DIR, 'medusa.fbx'), use_selection=True,
                             object_types={'ARMATURE', 'MESH'}, apply_unit_scale=True,
                             apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
                             mesh_smooth_type='FACE', use_tspace=True, add_leaf_bones=False,
                             primary_bone_axis='Y', secondary_bone_axis='X', armature_nodetype='NULL',
                             bake_anim=False, path_mode='RELATIVE', embed_textures=False)
    print('[export] fatto:', os.path.join(EXPORT_DIR, 'medusa.fbx'))


if __name__ == '__main__':
    if 'build' in ARGS:
        stage_build()
    if 'render' in ARGS:
        stage_render()
    if 'export' in ARGS:
        stage_export()
