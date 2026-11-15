"""
Braccia del giocatore, versione 3.

Parte dalle mani su misura di genera_mani2 (le dita avvolgono l'impugnatura e l'astina vere di ogni arma) e ci aggiunge:
  - guanto tattico: paranocche in gomma (oggetto a parte, materiale G_rinforzo), manichetta lunga sopra il polso con cinturino;
  - avambraccio e braccio dentro una manica di giacca: orlo spesso, pieghe di compressione dove la manica poggia sulla manichetta,
    pieghe a spirale sull'avambraccio, pieghe fitte all'interno del gomito, cucitura interna, grumi morbidi;
  - giunti per la cinematica inversa in Unity (Empty polso_X, gomito_X, spalla_X figli di mano_X): Weapon/ArmRig pelle la manica
    sulle ossa mano/avambraccio/braccio e piega il gomito quando la mano si sposta rispetto all'arma (ricarica, pompa, otturatore).

La posa di riposo si sceglie nel riferimento della CAMERA del gioco, con la posa al fianco di Weapon.UpdatePose per la classe
dell'arma: il gomito destro sta sotto il bordo dell'inquadratura e la spalla (virtuale: il modello in prima persona sta piu' avanti
di un braccio vero) dietro la camera; il braccio sinistro scende in basso a sinistra col gomito sul bordo.

Uso (Blender 4.5 headless, dalla cartella del file):
  blender -b -P genera_braccia.py -- prova [giocatore,destra,sinistra,sotto] id [id ...]   anteprime in braccia/render/
  blender -b -P genera_braccia.py -- build [id ...]                                       braccia/<id>.fbx (tutte se non indicate)
"""
import bpy, bmesh, math, os, sys, json, time
from math import sin, cos, pi, radians, degrees, acos, exp, atan2
from mathutils import Vector, Matrix, noise

DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, DIR)
import genera_mani2 as H          # noqa: E402  misure dell'impugnatura, mani avvolte, utilita' di mesh
M = H.M
V = H.V

ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = os.path.join(DIR, 'braccia')
OFFSET = H.OFFSET
CATALOG = os.path.normpath(os.path.join(DIR, '..', '..', 'unity', 'PryingEyes', 'Assets', '_PryingEyes', 'Resources', 'Production', 'weapons.json'))

LF = 0.262            # polso-gomito
HIP_YAW = -3.5        # Weapon.hipYaw
KNIFE = 'kn_coltello'


# ----------------------------------------------------------------------------- riferimento della camera
def catalog():
    try:
        with open(CATALOG, encoding='utf-8') as f:
            return {w['definition']['id']: w['definition'] for w in json.load(f)['weapons']}
    except Exception as e:                                   # noqa: BLE001
        print('[braccia] catalogo non letto:', e)
        return {}


def hip_for(cls):
    """Posa al fianco di Weapon.HipFor (riferimento camera: x destra, y su, z avanti)."""
    c = cls or ''
    if c in ('PISTOLA MITRAGLIATRICE', 'MICRO-MITRA'):
        return (0.21, -0.19, 0.54)
    if c.startswith('PISTOLA') or c == 'REVOLVER':
        return (0.175, -0.165, 0.50)
    return (0.28, -0.26, 0.62)


def _rot_neg_yaw(x, y, z, yaw):
    c, s = cos(radians(yaw)), sin(radians(yaw))
    return (x * c - z * s, y, x * s + z * c)


def cam_to_author(pc, hip, yaw=HIP_YAW):
    """Punto nel riferimento camera -> coordinate d'autore (x destra, y avanti, z su, origine nella presa destra)."""
    ux, uy, uz = _rot_neg_yaw(pc[0] - hip[0], pc[1] - hip[1], pc[2] - hip[2], yaw)
    return V(ux - OFFSET.x, uz - OFFSET.y, uy - OFFSET.z)


def cam_dir_to_author(dc, yaw=HIP_YAW):
    ux, uy, uz = _rot_neg_yaw(dc[0], dc[1], dc[2], yaw)
    return V(ux, uz, uy).normalized()


def author_to_cam(pa, hip, yaw=HIP_YAW):
    ux, uy, uz = pa.x + OFFSET.x, pa.z + OFFSET.z, pa.y + OFFSET.y
    c, s = cos(radians(yaw)), sin(radians(yaw))
    # R(yaw) = inversa di R(-yaw)
    x, z = ux * c + uz * s, -ux * s + uz * c
    return V(x + hip[0], uy + hip[1], z + hip[2])


# pose del braccio nel riferimento camera: direzione dell'avambraccio (dal polso verso il gomito) e spalla virtuale
POSES = {
    #          dir avambraccio d     spalla d             dir avambraccio s       spalla s
    'fucile':  ((0.24, -0.66, -0.71), (0.25, -0.30, -0.05), (-0.40, -0.64, -0.66), (-0.20, -0.56, 0.18)),
    'mitra':   ((0.24, -0.66, -0.71), (0.24, -0.32, -0.05), (-0.42, -0.62, -0.66), (-0.20, -0.52, 0.12)),
    'pistola': ((0.22, -0.68, -0.70), (0.24, -0.34, -0.06), (-0.34, -0.66, -0.67), (-0.18, -0.48, 0.05)),
    'coltello': ((0.20, -0.70, -0.68), (0.22, -0.36, -0.08), (-0.34, -0.66, -0.67), (-0.18, -0.48, 0.05)),
}


def pose_key(wid, cls):
    if wid == KNIFE:
        return 'coltello'
    c = cls or ''
    if c in ('PISTOLA MITRAGLIATRICE', 'MICRO-MITRA', 'MITRA'):
        return 'mitra'
    if c.startswith('PISTOLA') or c == 'REVOLVER':
        return 'pistola'
    return 'fucile'


# ----------------------------------------------------------------------------- mano: dita piu' lisce (guanto, non salsicce)
def build_hand_bm(poly, thumb_zc, idx_curls=(4, 3, 0), idx_spread=0.0, thumb=None):
    """Come genera_mani2.build_hand_bm, con le nocche meno gonfie (un guanto tirato non fa i rigonfiamenti delle dita nude) e i
    polpastrelli un po' schiacciati verso il palmo."""
    bm = bmesh.new()
    ell, cap, sph = M.bm_ellipsoid, M.bm_capsule, M.bm_sphere
    GS = H.GS
    g = 1.08
    ell(bm, (0.003, 0.046, -0.002), (0.036 * g, 0.047, 0.0125 * g), seg=28)
    ell(bm, (-0.021, 0.026, -0.009), (0.016 * g, 0.027, 0.012 * g), Matrix.Rotation(radians(-25), 3, 'Z'))
    ell(bm, (0.025, 0.034, -0.007), (0.012 * g, 0.030, 0.010 * g))
    ell(bm, (0.0, -0.004, 0.0), (0.027 * g, 0.020, 0.016 * g))
    cap(bm, (0, -0.050, 0), (0, -0.006, 0), 0.0300, 0.0255, seg=18)
    sph(bm, (0.022, -0.004, 0.007), 0.0065 * g)
    report = []
    for fi, (nm, k, sp0, lens, radii) in enumerate(M.FINGERS):
        if fi == 0 and idx_curls is not None:
            curls, sp = idx_curls, idx_spread
        else:
            sp = M.SPREAD_PRESA[fi]
            curls = H.wrap_curls_poly(k, sp, lens, radii, poly)
        report.append(curls)
        pts, dirs, a = M.finger_chain(k, sp, lens, curls)
        cap(bm, (k[0] * 0.35, 0.004, 0.0075), (k[0], k[1] - 0.01, 0.0085), 0.003 * GS, 0.003 * GS, seg=8)
        for i in range(3):
            cap(bm, pts[i], pts[i + 1], radii[i] * GS, radii[i + 1] * GS, seg=16)
        sph(bm, pts[0], radii[0] * GS * 1.10, seg=16)
        sph(bm, pts[1], radii[1] * GS * 1.04, seg=14)
        sph(bm, pts[2], radii[2] * GS * 1.02, seg=14)
        # polpastrello: la punta del guanto e' piena (ellissoide un filo piu' largo verso il palmo)
        d3 = (pts[3] - pts[2]).normalized()
        tip = pts[3] - d3 * radii[3] * 0.6
        ell(bm, tuple(tip), (radii[3] * GS * 1.04, radii[3] * GS * 1.10, radii[3] * GS * 1.04), seg=12)
    tj = thumb if thumb is not None else H.thumb_joints(thumb_zc)
    for i in range(3):
        cap(bm, tj[i], tj[i + 1], H.THUMB_R[i], H.THUMB_R[i + 1], seg=16)
    sph(bm, tj[1], H.THUMB_R[1] * 1.06, seg=14)
    sph(bm, tj[2], H.THUMB_R[2] * 1.04, seg=14)
    return bm, report, tj


H.build_hand_bm = build_hand_bm        # anche hands_for e build_left la usano


def thumb_left(poly):
    """Pollice della mano sinistra sotto l'astina: sale lungo il fianco sinistro dell'astina e punta in avanti, a filo del pezzo
    (il pollice di genera_mani2 scavalcava l'astina e da lontano sembrava un uncino). Coordinate canoniche della mano: per la
    sinistra -z e' su, -x e' avanti, -y e' verso il fianco sinistro dell'astina. Si cerca lo scostamento laterale piu' vicino al
    pezzo per cui nessun punto del pollice entra nella sezione (distanza con segno dal contorno)."""
    R = H.THUMB_R
    best = None
    for k in range(-16, 10):
        a = k * 0.05
        d1 = V(-0.66, 0.10 + a, -0.72).normalized()
        d2 = V(-0.93, 0.05 + a, -0.30).normalized()
        d3 = V(-0.99, 0.02 + a * 0.5, -0.08).normalized()
        mcp = H.CMC + d1 * 0.046
        ip = mcp + d2 * 0.035
        tip = ip + d3 * 0.029
        pts = [H.CMC.lerp(mcp, 1.0), mcp.lerp(ip, 0.5), ip, ip.lerp(tip, 0.5), tip]
        rr = [R[1], R[1], R[2], R[2], R[3]]
        if all(H.sdf_poly(poly, p.y, p.z) >= r + 0.0008 for p, r in zip(pts, rr)):
            if best is None or a > best[0]:
                best = (a, [H.CMC, mcp, ip, tip])
    return best[1] if best else None


def build_left(poly_h, zmin_h):
    tj = thumb_left(poly_h)
    return build_hand_bm(poly_h, zmin_h - H.THUMB_R[2] - 0.0025, idx_curls=None, thumb=tj)


H.build_left = build_left


def knuckle_guard_bm():
    """Paranocche in gomma sul dorso (coordinate canoniche della mano destra: pollice -x, dita +y, dorso +z): una placca sopra le
    nocche e una cupola per nocca, un po' piu' alta sul medio."""
    bm = bmesh.new()
    GS = H.GS
    ks = [f[1] for f in M.FINGERS]
    rr = [f[4][0] for f in M.FINGERS]
    for i, (k, r) in enumerate(zip(ks, rr)):
        top = k[2] + r * GS * 1.10 + 0.0012
        M.bm_ellipsoid(bm, (k[0], k[1] - 0.004, top - 0.0016), (r * GS * 1.02, 0.0085, 0.0046 + (0.0006 if i == 1 else 0.0)), seg=16)
    # placca: dal centro del dorso alle nocche, sottile
    M.bm_ellipsoid(bm, (0.002, 0.066, 0.0133), (0.035, 0.019, 0.0034), seg=24)
    return bm


# ----------------------------------------------------------------------------- funzioni di forma
def smoothstep(e0, e1, x):
    t = min(1.0, max(0.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


def interp(table, x):
    if x <= table[0][0]:
        return table[0][1]
    for (x0, y0), (x1, y1) in zip(table, table[1:]):
        if x <= x1:
            t = (x - x0) / max(1e-9, x1 - x0)
            return y0 + (y1 - y0) * t
    return table[-1][1]


def ang_diff(a, b):
    d = (a - b + pi) % (2 * pi) - pi
    return d


def perp(v, axis):
    w = v - axis * v.dot(axis)
    return w.normalized() if w.length > 1e-6 else axis.orthogonal().normalized()


# ----------------------------------------------------------------------------- manichetta del guanto
def gauntlet_bm(W, G, X, length=0.070):
    """Manichetta del guanto dal polso W lungo G, sezione ellittica larga lungo X (pollice-mignolo); bordo superiore arrotolato.
    Il cinturino (oggetto a parte) le gira intorno a 2,4 cm dal polso."""
    bm = bmesh.new()
    Xp = perp(X, G)
    Yp = G.cross(Xp)
    n_ring, M_ = 16, 40
    rings = []
    for k in range(n_ring + 1):
        s = -0.006 + (length + 0.006) * k / n_ring
        a = interp([(-0.006, 0.0335), (0.010, 0.0340), (0.040, 0.0372), (length, 0.0398)], s)
        b = interp([(-0.006, 0.0255), (0.010, 0.0262), (0.040, 0.0298), (length, 0.0330)], s)
        lip = 0.0016 * exp(-((s - (length - 0.004)) / 0.004) ** 2)
        c = W + G * s
        ring = []
        for i in range(M_):
            th = 2 * pi * i / M_
            ring.append(bm.verts.new(c + Xp * ((a + lip) * cos(th)) + Yp * ((b + lip) * sin(th))))
        rings.append(ring)
    for k in range(n_ring):
        for i in range(M_):
            bm.faces.new((rings[k][i], rings[k][(i + 1) % M_], rings[k + 1][(i + 1) % M_], rings[k + 1][i]))
    bm.faces.new(list(reversed(rings[0])))
    bm.faces.new(rings[-1])
    return bm


def strap_bm(W, G, X, at=0.024):
    """Cinturino in gomma attorno alla manichetta e linguetta sul dorso."""
    bm = bmesh.new()
    Xp = perp(X, G)
    Yp = G.cross(Xp)
    M_ = 48
    half = 0.0105
    rings = []
    for k, s in enumerate((at - half, at - half * 0.6, at + half * 0.6, at + half)):
        bulge = 0.0028 if 0 < k < 3 else 0.0010
        a = interp([(0.010, 0.0340), (0.040, 0.0372)], s) + bulge
        b = interp([(0.010, 0.0262), (0.040, 0.0298)], s) + bulge
        c = W + G * s
        rings.append([bm.verts.new(c + Xp * (a * cos(2 * pi * i / M_)) + Yp * (b * sin(2 * pi * i / M_))) for i in range(M_)])
    for k in range(3):
        for i in range(M_):
            bm.faces.new((rings[k][i], rings[k][(i + 1) % M_], rings[k + 1][(i + 1) % M_], rings[k + 1][i]))
    # linguetta: un cuscinetto sul dorso (verso -Yp e' il palmo; il dorso e' +Yp per la destra)
    c = W + G * at + Yp * (interp([(0.010, 0.0262), (0.040, 0.0298)], at) + 0.0035)
    R = Matrix((Xp, G, Yp)).transposed().to_4x4()
    bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.Translation(c) @ R @ Matrix.Diagonal((0.030, 0.019, 0.0035, 1.0)))
    return bm


# ----------------------------------------------------------------------------- manica
def centerline(W, G, D, E, S, fillet=0.080, hem=0.052):
    """Linea d'asse della manica: dall'orlo (sulla manichetta) si raccorda alla linea dell'avambraccio, raccordo al gomito, braccio."""
    A = W + G * hem
    sA = (A - W).dot(D)
    U = (S - E).normalized()
    ang = (-D).angle(U)
    td = min(fillet / math.tan(max(0.2, ang) / 2), 0.45 * min(LF - sA, (S - E).length))
    p1 = E - D * td
    p2 = E + U * td
    off = A - (W + D * sA)
    pts = []
    s = sA
    while s < LF - td:
        k = 1 - smoothstep(sA, sA + 0.07, s)
        pts.append(W + D * s + off * k)
        s += 0.002
    for i in range(48):
        t = i / 48
        pts.append(p1 * (1 - t) ** 2 + E * (2 * (1 - t) * t) + p2 * (t * t))
    L2 = (S - p2).length
    n = max(2, int(L2 / 0.002))
    for i in range(n + 1):
        pts.append(p2.lerp(S, i / n))
    arc = [0.0]
    for i in range(1, len(pts)):
        arc.append(arc[-1] + (pts[i] - pts[i - 1]).length)
    # arco del gomito: il punto della polilinea piu' vicino a E
    sE = arc[min(range(len(pts)), key=lambda i: (pts[i] - E).length)]
    return pts, arc, sE


def resample(pts, arc, sE, short_upper):
    """Passo variabile: fitto nell'orlo (pieghe), medio sull'avambraccio, fitto al gomito, rado sul braccio fuori campo."""
    total = arc[-1]
    out = []
    s = 0.0
    j = 0
    while s <= total:
        while j < len(arc) - 2 and arc[j + 1] < s:
            j += 1
        t = (s - arc[j]) / max(1e-9, arc[j + 1] - arc[j])
        out.append((s, pts[j].lerp(pts[j + 1], min(1.0, max(0.0, t)))))
        if s < 0.14:
            step = 0.0034
        elif abs(s - sE) < 0.08:
            step = 0.0055
        elif s < sE:
            step = 0.0075
        else:
            step = 0.018 if not short_upper else 0.010
        s += step
    if out[-1][0] < total - 1e-4:
        out.append((total, pts[-1]))
    return out


def sleeve_bm(W, G, D, E, S, X, seed=0.0, short_upper=False):
    """Manica di giacca: anelli ellittici lungo l'asse (trasporto parallelo del riferimento: niente torsioni), pieghe."""
    pts, arc, sE = centerline(W, G, D, E, S)
    samples = resample(pts, arc, sE, short_upper)
    total = samples[-1][0]
    # interno del gomito (verso cui si chiude l'angolo) e direzione di partenza del riferimento
    vin = ((W - E).normalized() + (S - E).normalized())
    vin = vin.normalized() if vin.length > 1e-4 else perp(V(0, 0, -1), D)
    T0 = (samples[1][1] - samples[0][1]).normalized()
    N = perp(X, T0)
    frames = []
    prevT = T0
    for i, (s, p) in enumerate(samples):
        a_ = samples[max(0, i - 1)][1]
        b_ = samples[min(len(samples) - 1, i + 1)][1]
        T = (b_ - a_).normalized()
        # trasporto parallelo
        axis = prevT.cross(T)
        if axis.length > 1e-7:
            ang = prevT.angle(T)
            N = (Matrix.Rotation(ang, 3, axis.normalized()) @ N)
        N = perp(N, T)
        frames.append((s, p, T, N, T.cross(N)))
        prevT = T
    # angolo dell'interno del gomito nel riferimento locale al gomito
    fE = min(frames, key=lambda f: abs(f[0] - sE))
    th_in = atan2(vin.dot(fE[4]), vin.dot(fE[3]))
    th_seam = th_in + 0.35

    def radii(s):
        a = interp([(0.0, 0.0426), (0.05, 0.0428), (sE - 0.05, 0.0472), (sE, 0.0498), (sE + 0.10, 0.0525), (total, 0.0540)], s)
        b = interp([(0.0, 0.0372), (0.05, 0.0378), (sE - 0.05, 0.0428), (sE, 0.0470), (sE + 0.10, 0.0505), (total, 0.0520)], s)
        return a, b

    th_down = None          # lato verso il basso (la stoffa pende): calcolato per anello sotto

    def folds(s, th, down_th):
        f = 1.0
        ct, st = cos(th), sin(th)
        # polsino della giacca: fascia rialzata di 3,5 cm sull'orlo, con la cucitura che la chiude
        band = smoothstep(0.000, 0.003, s) * (1.0 - smoothstep(0.031, 0.037, s))
        f += 0.050 * band
        f -= 0.018 * exp(-((s - 0.036) / 0.0022) ** 2)
        # pieghe di compressione sopra il polsino (la manica e' spinta giu' e si accumula): anelli storti e spezzati
        for k, sk in enumerate((0.050, 0.074, 0.099, 0.126)):
            tilt = 0.010 * cos(th - 1.3 * k - seed)
            wob = 0.0060 * noise.noise(V(ct * 1.3 + k * 2.1, st * 1.3, seed + 0.7 * k))
            amp = (0.120 - 0.020 * k) * (0.35 + 0.65 * (noise.noise(V(ct * 2.2 + seed, st * 2.2, 3.0 + k)) * 0.5 + 0.5))
            f += amp * exp(-((s - sk - wob - tilt) / 0.0095) ** 2)
        # pieghe a spirale sull'avambraccio (due frequenze)
        tw = smoothstep(0.11, 0.18, s) * (1.0 - smoothstep(sE - 0.07, sE - 0.02, s))
        f += 0.040 * tw * sin(3.0 * th + 19.0 * s + 1.8 * noise.noise(V(s * 5.0, seed, 0.2)))
        f += 0.018 * tw * sin(5.0 * th - 31.0 * s + seed)
        # interno del gomito: pieghe fitte; fuori: tessuto tirato
        dth = ang_diff(th, th_in)
        inner = exp(-(dth / 0.95) ** 2)
        g = exp(-((s - sE) / 0.065) ** 2)
        f += 0.115 * inner * g * (0.50 + 0.50 * sin(52.0 * (s - sE) + 2.4 * noise.noise(V(ct, st, seed + 5.0))))
        f -= 0.015 * (1.0 - inner) * g
        # la stoffa pende sotto l'avambraccio
        if down_th is not None:
            f += 0.030 * exp(-(ang_diff(th, down_th) / 0.8) ** 2) * smoothstep(0.05, 0.12, s) * (1.0 - smoothstep(sE - 0.04, sE + 0.02, s))
        # grumi morbidi e cucitura interna
        f += 0.028 * noise.noise(V(ct * 1.6 + seed, st * 1.6, s * 7.0))
        f -= 0.014 * exp(-(ang_diff(th, th_seam) / 0.030) ** 2)
        f += 0.005 * exp(-((abs(ang_diff(th, th_seam)) - 0.055) / 0.020) ** 2)
        return f

    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new('tessuto')
    M_ = 48
    grid = []
    vcoord = []
    down = V(0, 0, -1)

    def down_angle(T, N_, B_):
        d = down - T * down.dot(T)
        if d.length < 0.2:
            return None
        return atan2(d.dot(B_), d.dot(N_))

    for (s, p, T, N_, B_) in frames:
        a, b = radii(s)
        dth = down_angle(T, N_, B_)
        ring = []
        for i in range(M_ + 1):
            th = 2 * pi * i / M_
            f = folds(s, th, dth)
            ring.append(p + N_ * (a * f * cos(th)) + B_ * (b * f * sin(th)))
        grid.append(ring)
        vcoord.append(s)
    # orlo: il bordo gira dentro (spessore 5 mm) e risale 1,5 cm all'interno, cosi' l'apertura non e' un foglio
    s0, p0, T0_, N0, B0 = frames[0]
    a0, b0 = radii(0.0)
    d0 = down_angle(T0_, N0, B0)
    lip = []
    for (ds, dr) in ((0.016, -0.0055), (0.0025, -0.0050), (-0.0012, -0.0022)):
        ring = []
        for i in range(M_ + 1):
            th = 2 * pi * i / M_
            f = folds(0.0, th, d0)
            ring.append(p0 + T0_ * ds + N0 * ((a0 * f + dr) * cos(th)) + B0 * ((b0 * f + dr) * sin(th)))
        lip.append(ring)
    allrings = lip + grid
    allv = [-0.016, -0.0025, 0.0012] + vcoord
    circ = 2 * pi * 0.05
    vs = [[bm.verts.new(q) for q in ring] for ring in allrings]
    for k in range(len(allrings) - 1):
        for i in range(M_):
            quad = ((k, i), (k + 1, i), (k + 1, i + 1), (k, i + 1))
            f = bm.faces.new([vs[a][b] for a, b in quad])
            for lo, (a, b) in zip(f.loops, quad):
                lo[uvl].uv = (b / M_ * circ, allv[a])
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-7)
    bm.normal_update()
    # normali verso l'esterno (controllo su una faccia a meta' manica)
    bm.faces.ensure_lookup_table()
    mid = len(frames) // 2
    fmid = None
    cmid = frames[mid][1]
    best = 1e9
    for f in bm.faces:
        c = f.calc_center_median()
        d = (c - cmid).length
        if d < best:
            best, fmid = d, f
    if fmid is not None and fmid.normal.dot(fmid.calc_center_median() - cmid) < 0:
        bmesh.ops.reverse_faces(bm, faces=bm.faces)
    return bm, sE, total


def mesh_obj(name, bm, mat, smooth=True):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    if smooth:
        me.shade_smooth()
    me.materials.append(mat)
    return ob


# ----------------------------------------------------------------------------- un braccio
def arm_side(res, side, cls, wid, mats):
    key = 'right' if side == 'd' else 'left'
    bm, Mx, W, D_old, mirrored = res[key]
    hip = hip_for(cls)
    pk = pose_key(wid, cls)
    dR, sR, dL, sL = POSES[pk]
    d_cam, s_cam = (dR, sR) if side == 'd' else (dL, sL)
    R3 = Mx.to_3x3()
    back = (R3 @ V(0, -1, 0)).normalized()          # dal palmo verso il polso
    X = (R3 @ V(1, 0, 0)).normalized()              # larghezza della mano (pollice-mignolo)
    # impugnature molto inclinate (calcio del fucile a pompa, pistole): l'asse della mano sale di T gradi e con 40 gradi di polso
    # l'avambraccio resterebbe quasi orizzontale, all'altezza dell'occhio in mira; la destra puo' piegare di piu' (il polso lo
    # coprono manichetta e polsino, che si dividono la piega)
    lim = 40.0 if side == 's' else min(62.0, 40.0 + 1.2 * max(0.0, res['T'] - 12.0))
    D, bend = H.limit_bend(back, cam_dir_to_author(d_cam), lim)
    E = W + D * LF
    S = cam_to_author(s_cam, hip)
    if wid == KNIFE:
        # il coltello si muove molto (fendente, affondo): braccio rigido e corto, niente spalla lontana che entri in campo
        S = E + (S - E).normalized() * 0.16
    G = (back + D).normalized()
    # guanto: mano + manichetta, rifatto a voxel
    bmesh.ops.transform(bm, matrix=Mx, verts=bm.verts)
    if mirrored:
        bmesh.ops.reverse_faces(bm, faces=bm.faces)
    gb = gauntlet_bm(W, G, X)
    tmp = bpy.data.meshes.new('tmp')
    gb.to_mesh(tmp)
    gb.free()
    bm.from_mesh(tmp)
    bpy.data.meshes.remove(tmp)
    glove = H.make_obj(f'{side}_{wid}_guanto', bm, voxel=0.0024, sm_iter=3, folds=0.0004, seed=1.0 if side == 'd' else 5.0, decimate=0.30)
    # paranocche (canoniche -> autore)
    kb = knuckle_guard_bm()
    bmesh.ops.transform(kb, matrix=Mx, verts=kb.verts)
    if mirrored:
        bmesh.ops.reverse_faces(kb, faces=kb.faces)
    sb = strap_bm(W, G, X)
    tmp = bpy.data.meshes.new('tmp')
    sb.to_mesh(tmp)
    sb.free()
    kb.from_mesh(tmp)
    bpy.data.meshes.remove(tmp)
    guard = H.make_obj(f'{side}_{wid}_rinforzo', kb, voxel=0.0015, sm_iter=2, folds=0.0, decimate=0.35)
    # manica
    slb, sE, total = sleeve_bm(W, G, D, E, S, X, seed=3.0 if side == 'd' else 9.0, short_upper=(wid == KNIFE))
    sleeve = mesh_obj(f'{side}_{wid}_manica', slb, mats['manica'])
    for o, mt in ((glove, mats['guanto']), (guard, mats['rinforzo'])):
        o.data.materials.clear()
        o.data.materials.append(mt)
        H.box_uv(o)
    for o in (glove, guard, sleeve):
        o.location += OFFSET
    Wc, Ec, Sc = (author_to_cam(p, hip) for p in (W, E, S))
    elbow = degrees((W - E).angle(S - E))
    print(f'  [{wid}] {side}: posa {pk}, polso piegato {bend:.0f} gradi, gomito {elbow:.0f} gradi, braccio {(S - E).length:.2f} m; '
          f'camera: polso {tuple(round(v, 3) for v in Wc)} gomito {tuple(round(v, 3) for v in Ec)} spalla {tuple(round(v, 3) for v in Sc)}')
    return {'objs': (glove, guard, sleeve), 'joints': {'polso': W, 'gomito': E, 'spalla': S}}


def material(name, color, rough, metal=0.0):
    return H.material(name, color, rough, metal)


def make_mats():
    return {
        'guanto': material('G_guanto', (0.045, 0.040, 0.036), 0.42),
        'manica': material('G_manica', (0.16, 0.13, 0.10), 0.9),
        'rinforzo': material('G_rinforzo', (0.03, 0.03, 0.032), 0.65),
    }


def arms_for(wid, cat):
    res = H.hands_for(wid)
    cls = (cat.get(wid) or {}).get('cls')
    mats = make_mats()
    return res, {side: arm_side(res, side, cls, wid, mats) for side in ('d', 's')}, cls


def export_one(wid, arms, res):
    """braccia/<id>.fbx: radice <id>, Empty mano_d / mano_s nel punto di presa (come genera_mani2: la ricarica sposta quello), figli
    guanto, rinforzo, manica e i giunti polso_X, gomito_X, spalla_X."""
    sc = bpy.context.scene
    top = bpy.data.objects.new(wid, None)
    sc.collection.objects.link(top)
    keep = [top]
    for side in ('d', 's'):
        r = bpy.data.objects.new(f'mano_{side}', None)
        r.location = res['anchor_' + side] + OFFSET
        sc.collection.objects.link(r)
        r.parent = top
        keep.append(r)
        bpy.context.view_layer.update()
        inv = r.matrix_world.inverted()
        for o in arms[side]['objs']:
            o.parent = r
            o.matrix_parent_inverse = inv
            keep.append(o)
        for nm, p in arms[side]['joints'].items():
            j = bpy.data.objects.new(f'{nm}_{side}', None)
            j.empty_display_size = 0.02
            sc.collection.objects.link(j)
            j.location = p + OFFSET
            j.parent = r
            j.matrix_parent_inverse = inv
            keep.append(j)
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in keep:
        o.select_set(True)
    bpy.context.view_layer.objects.active = top
    os.makedirs(OUT, exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, wid + '.fbx'), use_selection=True, object_types={'EMPTY', 'MESH'}, apply_unit_scale=True,
                             apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y', mesh_smooth_type='FACE', use_tspace=True,
                             add_leaf_bones=False, bake_anim=False, path_mode='RELATIVE', embed_textures=False)


# ----------------------------------------------------------------------------- fasi
def stage_prova(ids, cat):
    os.makedirs(os.path.join(OUT, 'render'), exist_ok=True)
    for wid in ids:
        res, arms, cls = arms_for(wid, cat)
        sc = bpy.context.scene
        gun = material('arma', (0.30, 0.30, 0.32), 0.35, 0.7)
        for ob in sc.objects:
            if ob.type == 'MESH' and not (ob.name[:2] in ('d_', 's_')):
                ob.data.materials.clear()
                ob.data.materials.append(gun)
        # la sinistra delle armi a una mano compare solo in ricarica: nelle anteprime si nasconde come in gioco
        if wid in H.ONE_HANDED:
            for o in arms['s']['objs']:
                o.hide_render = True
        H.setup_cpu(int(os.environ.get('MANI_SAMPLES', '32')), (1280, 720))
        w = bpy.data.worlds.new('mondo')
        sc.world = w
        w.use_nodes = True
        w.node_tree.nodes['Background'].inputs['Color'].default_value = (0.05, 0.05, 0.06, 1)
        hip = hip_for(cls)
        camp = cam_to_author((0, 0, 0), hip)
        for nm, loc, pw, sz, col in (('key', (-0.5, -0.4, 0.8), 60, 0.8, (1.0, 0.88, 0.75)), ('fill', (0.9, -0.3, 0.1), 25, 0.9, (0.85, 0.9, 1.0)),
                                     ('top', (0.0, 0.3, 1.0), 30, 0.8, (1, 1, 1))):
            l = bpy.data.lights.new(nm, 'AREA')
            l.energy, l.size, l.color = pw, sz, col
            lo = bpy.data.objects.new(nm, l)
            lo.location = Vector(loc) + OFFSET
            H.look_at(lo, Vector((0, 0.05, -0.1)) + OFFSET)
            sc.collection.objects.link(lo)
        views = {
            # la vista del giocatore: camera vera della posa al fianco (FOV verticale 75 come in gioco)
            'giocatore': (camp, cam_to_author((0, 0, 1.0), hip), 75),
            'destra': (V(0.62, -0.05, 0.02), V(0, 0.0, -0.08), 48),
            'sinistra': (V(-0.70, 0.15, -0.05), V(0, 0.12, -0.10), 48),
            'sotto': (V(0.25, -0.15, -0.65), V(0.0, 0.05, -0.10), 55),
            'mano': (V(-0.16, -0.30, 0.16), V(0.0, 0.0, -0.03), 40),
            # le due mani viste dall'occhio del giocatore, da vicino (tele)
            'gsx': (camp, res['anchor_s'], 14),
            'gdx': (camp, V(0.0, 0.0, -0.02), 14),
            'manos': (V(-0.10, 0.02, 0.22), V(0.0, 0.22, -0.02), 40),
        }
        pick = [a for a in ARGS if a in views] or ['giocatore', 'destra']
        cam = bpy.data.objects.new('camera', bpy.data.cameras.new('camera'))
        sc.collection.objects.link(cam)
        for nm in pick:
            loc, tgt, fov = views[nm]
            cam.location = loc + OFFSET
            cam.data.lens_unit = 'FOV'
            cam.data.angle = radians(fov)
            cam.data.sensor_fit = 'VERTICAL'
            cam.data.clip_start = 0.05
            if nm == 'giocatore':
                # orientamento della camera del gioco: guarda lungo +z camera, nessun rollio (su = z d'autore)
                cam.rotation_euler = cam_dir_to_author((0, 0, 1)).to_track_quat('-Z', 'Y').to_euler()
            else:
                H.look_at(cam, tgt + OFFSET)
            sc.camera = cam
            sc.render.filepath = os.path.join(OUT, 'render', f'{wid}_{nm}.png')
            bpy.ops.render.render(write_still=True)
            print('[prova]', sc.render.filepath)


def stage_build(ids, cat):
    for wid in ids:
        t0 = time.time()
        bpy.ops.wm.read_factory_settings(use_empty=True)
        res, arms, cls = arms_for(wid, cat)
        export_one(wid, arms, res)
        print(f'[build] {wid}: {time.time() - t0:.1f} s')


if __name__ == '__main__':
    cat = catalog()
    all_ids = H.weapon_ids()
    ids = [a for a in ARGS if a in all_ids] or all_ids
    if 'prova' in ARGS:
        stage_prova(ids, cat)
    if 'build' in ARGS:
        stage_build(ids, cat)
