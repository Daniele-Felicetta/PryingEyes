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
  blender -b -P genera_braccia.py -- libera                 braccia/mano_libera.fbx (la mano articolata: Unity la usa per tutte e due)
  blender -b -P genera_braccia.py -- manica                 braccia/manica_libera.fbx (la manica in posa neutra con le proporzioni vere)
  blender -b -P genera_braccia.py -- pose [id ...]          braccia/presa_sinistra.json e presa_destra.json (dove stanno le dita)
  blender -b -P genera_braccia.py -- vedi                   anteprime di mano e manica in braccia/render/ (Cycles su CPU)
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
# l'indice della mano destra, tenuto da parte da build_hand_bm per diventare tre falangi mobili (arm_side lo costruisce)
LAST_INDEX = {}
CURRENT_WID = ''
LAST_INFO = {}
# le articolazioni dell'ultima mano costruita (coordinate canoniche): in hands_for la sinistra si costruisce per ultima, quindi dopo
# hands_for qui ci sono le dita della sinistra avvolte all'astina (stage_pose le scrive per la mano libera di Unity)
LAST_HAND = {}
# tutte le mani costruite da build_hand_bm, nell'ordine (in hands_for: prima la destra, poi la sinistra): stage_pose le scrive tutte e due
HAND_LOG = []
_load_weapon = H.load_weapon


# armi il cui grilletto e' fuso nel corpo (nessuna parte 'trigger' nell'FBX): centro del grilletto preso dalla geometria di
# modelli/armi/armi_mitra.py, in coordinate d'autore. Serve solo alla posa dell'indice: la presa della mano resta quella di sempre.
TRIGGER_FUSED = {
    'pdw_bordone': (0.0, 0.056, 0.088 - 0.066),
    'smg_tarantola': (0.0, 0.0265, 0.068 - 0.042),
    'smg_vesper9': (0.0, 0.037, 0.090 - 0.050),
    'smg_cimbalo': (0.0, 0.037, 0.092 - 0.058),
}


def load_weapon_keep(wid):
    """Come genera_mani2.load_weapon, ma tiene le misure (serve la posizione del grilletto per la posa dell'indice)."""
    bvh, bvh_all, info = _load_weapon(wid)
    LAST_INFO.clear()
    LAST_INFO.update(info)
    if 'trigger_c' not in LAST_INFO and wid in TRIGGER_FUSED:
        LAST_INFO['trigger_c'] = V(*TRIGGER_FUSED[wid])
    return bvh, bvh_all, info


H.load_weapon = load_weapon_keep


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
    LAST_HAND.clear()
    LAST_HAND['fingers'] = []
    for fi, (nm, k, sp0, lens, radii) in enumerate(M.FINGERS):
        if fi == 0 and idx_curls is not None:
            curls, sp = idx_curls, idx_spread
        else:
            sp = M.SPREAD_PRESA[fi]
            curls = H.wrap_curls_poly(k, sp, lens, radii, poly)
        report.append(curls)
        pts, dirs, a = M.finger_chain(k, sp, lens, curls)
        LAST_HAND['fingers'].append([p.copy() for p in pts])
        cap(bm, (k[0] * 0.35, 0.004, 0.0075), (k[0], k[1] - 0.01, 0.0085), 0.003 * GS, 0.003 * GS, seg=8)
        if fi == 0 and idx_curls is not None and CURRENT_WID != KNIFE:
            # destra: la nocca resta nel guanto, le tre falangi diventano pezzi a parte con i perni alle articolazioni
            sph(bm, pts[0], radii[0] * GS * 1.10, seg=16)
            LAST_INDEX.clear()
            LAST_INDEX.update({'k': k, 'sp': sp, 'lens': lens, 'radii': radii, 'curls': curls, 'pts': pts})
            continue
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
    LAST_HAND['thumb'] = [p.copy() for p in tj]
    HAND_LOG.append({'fingers': [[p.copy() for p in f] for f in LAST_HAND['fingers']], 'thumb': [p.copy() for p in tj]})
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


def sleeve_bm(W, G, D, E, S, X, seed=0.0, short_upper=False, v2=False):
    """Manica di giacca: anelli ellittici lungo l'asse (trasporto parallelo del riferimento: niente torsioni), pieghe. v2 (la
    manica neutra): pieghe di compressione storte e spezzate a diamante invece che ad anello, spirali e pieghe del gomito piu'
    marcate, l'avambraccio un po' piu' stretto verso il polso."""
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
        if v2:
            a = interp([(0.0, 0.0410), (0.06, 0.0412), (sE - 0.09, 0.0470), (sE, 0.0490), (sE + 0.10, 0.0515), (total, 0.0530)], s)
            b = interp([(0.0, 0.0360), (0.06, 0.0366), (sE - 0.09, 0.0430), (sE, 0.0465), (sE + 0.10, 0.0495), (total, 0.0510)], s)
            return a, b
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
        if not v2:
            for k, sk in enumerate((0.050, 0.074, 0.099, 0.126)):
                tilt = 0.010 * cos(th - 1.3 * k - seed)
                wob = 0.0060 * noise.noise(V(ct * 1.3 + k * 2.1, st * 1.3, seed + 0.7 * k))
                amp = (0.120 - 0.020 * k) * (0.35 + 0.65 * (noise.noise(V(ct * 2.2 + seed, st * 2.2, 3.0 + k)) * 0.5 + 0.5))
                f += amp * exp(-((s - sk - wob - tilt) / 0.0095) ** 2)
        else:
            # a diamante: ogni piega gira di traverso (fino a 3 cm di dislivello sul giro) e si spegne su un lato, come la stoffa
            # che si accartoccia davvero; tra una piega e l'altra la stoffa e' tesa
            for k, sk in enumerate((0.052, 0.080, 0.112)):
                tilt = 0.016 * cos(th * 1.0 - 1.9 * k - seed) + 0.006 * cos(2.0 * th + k)
                wob = 0.0070 * noise.noise(V(ct * 1.6 + k * 2.1, st * 1.6, seed + 0.7 * k))
                side = smoothstep(-0.35, 0.45, noise.noise(V(ct * 1.4 + seed + k, st * 1.4, 7.0 + k)))
                amp = (0.150 - 0.030 * k) * side
                f += amp * exp(-((s - sk - wob - tilt) / 0.0105) ** 2)
                f -= 0.025 * side * exp(-((s - sk - wob - tilt - 0.014) / 0.006) ** 2)
        # pieghe a spirale sull'avambraccio (due frequenze)
        tw = smoothstep(0.11, 0.18, s) * (1.0 - smoothstep(sE - 0.07, sE - 0.02, s))
        ksp = 1.7 if v2 else 1.0
        f += 0.040 * ksp * tw * sin(3.0 * th + 19.0 * s + 1.8 * noise.noise(V(s * 5.0, seed, 0.2)))
        f += 0.018 * ksp * tw * sin(5.0 * th - 31.0 * s + seed)
        # interno del gomito: pieghe fitte; fuori: tessuto tirato
        dth = ang_diff(th, th_in)
        inner = exp(-(dth / 0.95) ** 2)
        g = exp(-((s - sE) / 0.065) ** 2)
        f += (0.16 if v2 else 0.115) * inner * g * (0.50 + 0.50 * sin(52.0 * (s - sE) + 2.4 * noise.noise(V(ct, st, seed + 5.0))))
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
    finger = None
    if side == 'd' and LAST_INDEX:
        finger = index_finger(Mx, mats['guanto'], wid)
    Wc, Ec, Sc = (author_to_cam(p, hip) for p in (W, E, S))
    elbow = degrees((W - E).angle(S - E))
    print(f'  [{wid}] {side}: posa {pk}, polso piegato {bend:.0f} gradi, gomito {elbow:.0f} gradi, braccio {(S - E).length:.2f} m; '
          f'camera: polso {tuple(round(v, 3) for v in Wc)} gomito {tuple(round(v, 3) for v in Ec)} spalla {tuple(round(v, 3) for v in Sc)}')
    return {'objs': (glove, guard, sleeve), 'joints': {'polso': W, 'gomito': E, 'spalla': S}, 'finger': finger}


def index_finger(Mx, mat, wid):
    """L'indice destro in tre falangi, ognuna con il suo perno, e la posa sul grilletto. Disteso lungo il telaio (disciplina di
    tiro) com'era nel guanto; la posa sul grilletto si cerca in coordinate canoniche della mano: il polpastrello (al 60% della terza
    falange) davanti al centro del grilletto vero dell'arma. Ritorna i pezzi, i perni (indice_d_1..4) e i punti della posa
    (grilletto_d_2..4) in coordinate d'autore; Weapon/TriggerFinger porta il dito da una posa all'altra."""
    d = LAST_INDEX
    k, sp, lens, radii, pts = d['k'], d['sp'], d['lens'], d['radii'], d['pts']
    GS = H.GS
    pieces = []
    for i in range(3):
        bm = bmesh.new()
        M.bm_capsule(bm, pts[i], pts[i + 1], radii[i] * GS, radii[i + 1] * GS, seg=18)
        M.bm_sphere(bm, pts[i + 1], radii[i + 1] * GS * (1.04 if i < 2 else 1.0), seg=16)
        if i == 2:
            d3 = (pts[3] - pts[2]).normalized()
            tip = pts[3] - d3 * radii[3] * 0.6
            M.bm_ellipsoid(bm, tuple(tip), (radii[3] * GS * 1.04, radii[3] * GS * 1.10, radii[3] * GS * 1.04), seg=14)
        bmesh.ops.triangulate(bm, faces=bm.faces[:])              # niente n-goni: l'FBX porta le tangenti
        bmesh.ops.transform(bm, matrix=Mx, verts=bm.verts)
        ob = mesh_obj(f'd_{wid}_indice{i + 1}', bm, mat)
        H.box_uv(ob)
        ob.location += OFFSET
        pieces.append(ob)
    best = trigger_pose(Mx, wid, k, sp, lens)
    tp = best[3]
    joints = {f'indice_d_{i + 1}': Mx @ pts[i] for i in range(4)}
    trigger = {f'grilletto_d_{i + 1}': Mx @ tp[i] for i in range(1, 4)}
    return {'pieces': pieces, 'joints': joints, 'trigger': trigger}


def trigger_pose(Mx, wid, k, sp, lens):
    """La posa dell'indice destro sul grilletto (coordinate canoniche della mano): il polpastrello davanti al centro del grilletto
    vero dell'arma; (scarto, pieghe, apertura, articolazioni)."""
    trig = LAST_INFO.get('trigger_c')
    best = None
    if trig is not None:
        target = Mx.inverted() @ trig
        target = target + V(0, 0.006, 0)                     # il polpastrello sta davanti alla lama del grilletto
        for spd in (sp, M.SPREAD_PRESA[0]):
            for c1 in range(-20, 82, 2):                     # la nocca si iperestende un poco (grilletti alti)
                for c2 in range(0, 112, 3):
                    curls = (c1, c2, c2 * 0.6)
                    p, _, _ = M.finger_chain(k, spd, lens, curls)
                    pad = p[2] + (p[3] - p[2]) * 0.6
                    err = (pad - target).length
                    if best is None or err < best[0]:
                        best = (err, curls, spd, p)
    if best is None or best[0] > 0.02:
        # grilletto non misurato o fuori portata: posa di presa leggera
        if trig is not None and best is not None:
            tg = Mx.inverted() @ trig
            print(f'  [{wid}] indice: grilletto a ({tg.x:.3f}, {tg.y:.3f}, {tg.z:.3f}) nella mano, nocca {tuple(round(c, 3) for c in k)}, '
                  f'scarto minimo {best[0] * 1000:.1f} mm')
        curls = (40, 60, 36)
        best = (0.0, curls, sp, M.finger_chain(k, sp, lens, curls)[0])
        print(f'  [{wid}] indice: grilletto {"non misurato" if trig is None else "non raggiunto"}, posa di ripiego')
    else:
        print(f'  [{wid}] indice: sul grilletto con pieghe {tuple(round(c) for c in best[1])}, scarto {best[0] * 1000:.1f} mm')
    return best


def material(name, color, rough, metal=0.0):
    return H.material(name, color, rough, metal)


def make_mats():
    return {
        'guanto': material('G_guanto', (0.045, 0.040, 0.036), 0.42),
        'manica': material('G_manica', (0.16, 0.13, 0.10), 0.9),
        'rinforzo': material('G_rinforzo', (0.03, 0.03, 0.032), 0.65),
    }


def arms_for(wid, cat):
    global CURRENT_WID
    CURRENT_WID = wid
    LAST_INDEX.clear()
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
        f = arms[side].get('finger')
        if f:
            parent = r
            for i in range(4):
                nm = f'indice_d_{i + 1}'
                e = bpy.data.objects.new(nm, None)
                e.empty_display_size = 0.01
                sc.collection.objects.link(e)
                e.location = f['joints'][nm] + OFFSET
                bpy.context.view_layer.update()
                e.parent = parent
                e.matrix_parent_inverse = parent.matrix_world.inverted()
                keep.append(e)
                if i < 3:
                    piece = f['pieces'][i]
                    bpy.context.view_layer.update()
                    piece.parent = e
                    piece.matrix_parent_inverse = e.matrix_world.inverted()
                    keep.append(piece)
                parent = e
            for nm, p in f['trigger'].items():
                g = bpy.data.objects.new(nm, None)
                g.empty_display_size = 0.01
                sc.collection.objects.link(g)
                g.location = p + OFFSET
                g.parent = r
                g.matrix_parent_inverse = inv
                keep.append(g)
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


# ----------------------------------------------------------------------------- mano libera (magie, potenziamenti, lanci)
# La sinistra che lascia l'arma: lo stesso guanto, ma in posa neutra (dita appena piegate e aperte a ventaglio, pollice staccato dal
# palmo) e con un perno a ogni articolazione. In Unity HandRig le da' la pelle (pesi per distanza dalle falangi) e la piega in qualunque
# posa; per passare senza scatto dalla mano avvolta all'astina a questa, stage_pose scrive arma per arma dove stanno le articolazioni
# della sinistra avvolta (presa_sinistra.json): Unity porta le falangi della mano libera esattamente li' e poi le muove.
FREE_CURLS = ((8, 12, 7), (10, 14, 8), (12, 16, 9), (14, 18, 10))
FREE_SPREAD = (-13.0, -3.0, 8.0, 19.0)          # ventaglio piu' aperto della mano rilassata: il remesh non fonde le dita tra loro
FINGER_NAMES = ('indice', 'medio', 'anulare', 'mignolo')
# assi della sinistra, gli stessi di genera_mani2.hands_for: dita verso +x d'autore, dorso verso -z, pollice verso +y (e' uno specchio)
XL, YL, ZL = V(0, -1, 0), V(1, 0, 0), V(0, 0, -1)


def free_thumb():
    """Pollice rilassato, staccato dal palmo di ~40 gradi (dalla nocca carpo-metacarpale CMC di genera_mani2)."""
    d1 = V(-0.62, 0.62, -0.48).normalized()
    d2 = V(-0.36, 0.86, -0.36).normalized()
    d3 = V(-0.22, 0.94, -0.26).normalized()
    mcp = H.CMC + d1 * 0.046
    ip = mcp + d2 * 0.035
    tip = ip + d3 * 0.029
    return [H.CMC.copy(), mcp, ip, tip]


def build_free_bm():
    """Mano destra canonica in posa neutra (come build_hand_bm, senza impugnatura): mesh, articolazioni delle dita e del pollice.
    Versione 2 (2026-11-16): guanto di cuoio senza paranocche di gomma (la placca ovale sul dorso sembrava una zampa): le nocche
    hanno un'imbottitura cucita nel guanto; giunture appena accennate (un guanto tirato non fa i rigonfiamenti del dito nudo);
    i tendini del dorso appena visibili. Pieghe e cuciture le fa free_details dopo il remesh."""
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
    fingers = []
    for fi, (nm, k, sp0, lens, radii) in enumerate(M.FINGERS):
        pts, dirs, a = M.finger_chain(k, FREE_SPREAD[fi], lens, FREE_CURLS[fi])
        fingers.append(pts)
        # tendine del dorso: dal polso alla nocca, sottile
        cap(bm, (k[0] * 0.35, 0.004, 0.0075), (k[0], k[1] - 0.01, 0.0085), 0.0026 * GS, 0.0026 * GS, seg=8)
        for i in range(3):
            cap(bm, pts[i], pts[i + 1], radii[i] * GS, radii[i + 1] * GS, seg=16)
        sph(bm, pts[0], radii[0] * GS * 1.04, seg=16)
        sph(bm, pts[1], radii[1] * GS * 1.01, seg=14)
        d3 = (pts[3] - pts[2]).normalized()
        tip = pts[3] - d3 * radii[3] * 0.6
        ell(bm, tuple(tip), (radii[3] * GS * 1.02, radii[3] * GS * 1.08, radii[3] * GS * 1.0), seg=12)
        # imbottitura della nocca: un cuscinetto basso sul dorso, dentro il guanto
        d0 = (pts[1] - pts[0]).normalized()
        up = V(0, 0, 1) - d0 * d0.z
        up = up.normalized() if up.length > 1e-6 else V(0, 0, 1)
        ell(bm, tuple(pts[0] + up * radii[0] * GS * 0.55 - d0 * 0.002), (radii[0] * GS * 0.95, 0.0085, radii[0] * GS * 0.62), seg=14)
    tj = free_thumb()
    for i in range(3):
        cap(bm, tj[i], tj[i + 1], H.THUMB_R[i], H.THUMB_R[i + 1], seg=16)
    sph(bm, tj[1], H.THUMB_R[1] * 1.06, seg=14)
    sph(bm, tj[2], H.THUMB_R[2] * 1.04, seg=14)
    return bm, fingers, tj


def free_details(ob, Mf, fingers, tj):
    """Il cuoio dopo il remesh: pieghe trasversali sul dorso delle articolazioni (MCP, PIP, DIP: due o tre solchi sottili,
    piu' marcati sulla prima) e la cucitura lungo i fianchi di ogni dito (il solco con un filo rialzato accanto), come i guanti veri,
    che sono cuciti a fette. Tutto nel riferimento canonico della mano (dita +y, dorso +z), spostando i vertici lungo la normale."""
    Mi = Mf.inverted()
    R3 = Mf.to_3x3()
    segs = []
    for fi, f in enumerate(fingers):
        radii = M.FINGERS[fi][4]
        for i in range(3):
            a, b = f[i], f[i + 1]
            d = (b - a).normalized()
            dor = V(0, 0, 1) - d * d.z
            dor = dor.normalized() if dor.length > 1e-6 else V(0, 0, 1)
            segs.append((a, b, d, dor, i, radii[i] * H.GS, radii[i + 1] * H.GS))
    joints = []
    for f in fingers:
        for i in range(3):
            d = (f[i + 1] - f[max(0, i - 1)]).normalized()
            dor = V(0, 0, 1) - d * d.z
            dor = dor.normalized() if dor.length > 1e-6 else V(0, 0, 1)
            joints.append((f[i], d, dor, (0.00042, 0.00030, 0.00024)[i], (3, 3, 2)[i]))
    me = ob.data
    for v in me.vertices:
        p = Mi @ v.co
        n = (R3.transposed() @ v.normal).normalized()
        disp = 0.0
        # pieghe sul dorso delle articolazioni
        for (J, d, dor, amp, nlines) in joints:
            r = p - J
            t = r.dot(d)
            if abs(t) > 0.0055:
                continue
            rad = r - d * t
            if rad.length > 0.016:
                continue
            c = rad.normalized().dot(dor) if rad.length > 1e-6 else 0.0
            if c < 0.15:
                continue
            half = 0.0016 * nlines * 0.5
            env = smoothstep(half + 0.0012, half, abs(t)) * smoothstep(0.15, 0.6, c)
            disp -= amp * env * (0.5 + 0.5 * cos(2 * pi * t / 0.0016))
        # cucitura sui fianchi: dove la normale e' di traverso al dito (ne' dorso ne' palmo)
        best = None
        for (a, b, d, dor, i, ra, rb) in segs:
            ab = b - a
            t = max(0.0, min(1.0, (p - a).dot(ab) / max(1e-9, ab.length_squared)))
            q = a + ab * t
            dd = (p - q).length
            if best is None or dd < best[0]:
                best = (dd, a, b, d, dor, t, ra + (rb - ra) * t, i)
        # solo sul dito vero: dentro 1,3 raggi dall'asse e, sulla prima falange, oltre la membrana tra le dita
        if best is not None and best[0] < best[6] * 1.3 and not (best[7] == 0 and best[5] < 0.30):
            _, a, b, d, dor, t, _, _ = best
            lat = n - d * n.dot(d)
            if lat.length > 1e-6:
                c = lat.normalized().dot(dor)
                groove = exp(-(c / 0.10) ** 2)
                ridge = exp(-((abs(c) - 0.17) / 0.06) ** 2)
                disp += -0.00030 * groove + 0.00012 * ridge
        if disp != 0.0:
            v.co = v.co + v.normal * disp
    me.update()


def stage_libera():
    """braccia/mano_libera.fbx: radice mano_libera nell'origine canonica, guanto (mano + manichetta) e rinforzo (il cinturino) e
    le articolazioni come Empty L_<dito>_1..4, L_pollice_0..3, L_polso, L_palmo, L_dorso, L_manichetta. Versione 2: senza
    paranocche, piu' fitta (voxel 1,3 mm, niente decimazione), pieghe e cuciture del cuoio (free_details)."""
    t0 = time.time()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    mats = make_mats()
    bm, fingers, tj = build_free_bm()
    Mf = H.frame_matrix(XL, YL, ZL, V(0, 0, 0))
    W = V(0, -0.006, 0)                  # il polso della sinistra di genera_mani2 (WL = OL + YL * -0.006)
    G = V(0, -1, 0)                      # la manichetta prolunga la mano: Unity la piega tra palmo e avambraccio col polso
    X = V(1, 0, 0)
    gb = gauntlet_bm(W, G, X)
    tmp = bpy.data.meshes.new('tmp')
    gb.to_mesh(tmp)
    gb.free()
    bm.from_mesh(tmp)
    bpy.data.meshes.remove(tmp)
    bmesh.ops.transform(bm, matrix=Mf, verts=bm.verts)
    bmesh.ops.reverse_faces(bm, faces=bm.faces)
    glove = H.make_obj('libera_guanto', bm, voxel=0.0013, sm_iter=4, folds=0.00025, seed=5.0, decimate=1.0)
    # free_details (pieghe e cuciture a spostamento di vertici) per ora no: dopo la decimazione i solchi da 0,3 mm diventavano
    # puntini scuri; il dettaglio del cuoio lo da' la normal map del materiale
    dm = glove.modifiers.new('decima', 'DECIMATE')
    dm.ratio = 0.5
    M.apply_all(glove)
    glove.data.shade_smooth()
    kb = strap_bm(W, G, X)
    bmesh.ops.transform(kb, matrix=Mf, verts=kb.verts)
    bmesh.ops.reverse_faces(kb, faces=kb.faces)
    guard = H.make_obj('libera_rinforzo', kb, voxel=0.0012, sm_iter=2, folds=0.0, decimate=0.6)
    for o, mt in ((glove, mats['guanto']), (guard, mats['rinforzo'])):
        o.data.materials.clear()
        o.data.materials.append(mt)
        H.box_uv(o)
    joints = {'L_polso': W, 'L_palmo': V(0.003, 0.046, -0.002), 'L_dorso': V(0.003, 0.046, 0.030), 'L_manichetta': W + G * 0.070}
    for fi, nm in enumerate(FINGER_NAMES):
        for i in range(4):
            joints[f'L_{nm}_{i + 1}'] = fingers[fi][i]
    for i in range(4):
        joints[f'L_pollice_{i}'] = tj[i]
    sc = bpy.context.scene
    top = bpy.data.objects.new('mano_libera', None)
    sc.collection.objects.link(top)
    keep = [top]
    for o in (glove, guard):
        o.parent = top
        keep.append(o)
    for nm, p in joints.items():
        e = bpy.data.objects.new(nm, None)
        e.empty_display_size = 0.01
        sc.collection.objects.link(e)
        e.location = Mf @ p
        e.parent = top
        keep.append(e)
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in keep:
        o.select_set(True)
    bpy.context.view_layer.objects.active = top
    os.makedirs(OUT, exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, 'mano_libera.fbx'), use_selection=True, object_types={'EMPTY', 'MESH'}, apply_unit_scale=True,
                             apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y', mesh_smooth_type='FACE', use_tspace=True,
                             add_leaf_bones=False, bake_anim=False, path_mode='RELATIVE', embed_textures=False)
    nv = len(glove.data.vertices) + len(guard.data.vertices)
    print(f'[libera] mano_libera.fbx: {nv} vertici, {len(joints)} articolazioni, {time.time() - t0:.1f} s')


def stage_pose(ids, cat):
    """braccia/presa_sinistra.json: per ogni arma, le articolazioni della sinistra avvolta all'astina (o a coppa sul caricatore per le
    armi a una mano) in coordinate d'autore relative a mano_s, piu' polso, gomito e spalla (con questi tre Unity ricava da solo come
    gli assi d'autore finiscono nei suoi). Nessuna mesh: non si riesporta nessun FBX."""
    global CURRENT_WID
    out = []
    out_d = []
    for wid in ids:
        if wid == KNIFE:
            continue
        t0 = time.time()
        bpy.ops.wm.read_factory_settings(use_empty=True)
        CURRENT_WID = wid
        LAST_INDEX.clear()
        HAND_LOG.clear()
        res = H.hands_for(wid)
        hand = {'fingers': [list(f) for f in LAST_HAND['fingers']], 'thumb': list(LAST_HAND['thumb'])}
        # la destra (la prima mano costruita): dita avvolte all'impugnatura e indice disteso lungo il telaio, piu' la posa sul
        # grilletto; giunti polso/gomito/spalla come quelli dell'FBX dell'arma (servono a Unity per ritrovare gli assi d'autore)
        if HAND_LOG and LAST_INDEX:
            hd = HAND_LOG[0]
            bmr, Mxr, Wr, _, _ = res['right']
            clsr = (cat.get(wid) or {}).get('cls')
            hipr = hip_for(clsr)
            dR_, sR_, _, _ = POSES[pose_key(wid, clsr)]
            backr = (Mxr.to_3x3() @ V(0, -1, 0)).normalized()
            limr = min(62.0, 40.0 + 1.2 * max(0.0, res['T'] - 12.0))
            Dr, _ = H.limit_bend(backr, cam_dir_to_author(dR_), limr)
            Er = Wr + Dr * LF
            Sr = cam_to_author(sR_, hipr)
            anchor_d = res['anchor_d']
            tb = trigger_pose(Mxr, wid, LAST_INDEX['k'], LAST_INDEX['sp'], LAST_INDEX['lens'])

            def reld(p):
                return [round(c, 6) for c in (p - anchor_d)]

            out_d.append({
                'id': wid,
                'polso': reld(Wr), 'gomito': reld(Er), 'spalla': reld(Sr),
                'dita': [c for f in hd['fingers'] for p in f for c in reld(Mxr @ p)],
                'pollice': [c for p in hd['thumb'] for c in reld(Mxr @ p)],
                'palmo': reld(Mxr @ V(0.003, 0.046, -0.002)),
                'dorso': reld(Mxr @ V(0.003, 0.046, 0.030)),
                'grilletto': [c for p in tb[3][1:] for c in reld(Mxr @ p)],
            })
        bm, Mx, W, D_old, mirrored = res['left']
        cls = (cat.get(wid) or {}).get('cls')
        hip = hip_for(cls)
        dR, sR, dL, sL = POSES[pose_key(wid, cls)]
        back = (Mx.to_3x3() @ V(0, -1, 0)).normalized()
        D, bend = H.limit_bend(back, cam_dir_to_author(dL), 40.0)
        E = W + D * LF
        S = cam_to_author(sL, hip)
        anchor = res['anchor_s']

        def rel(p):
            return [round(c, 6) for c in (p - anchor)]

        def canon(c):
            return Mx @ (c if isinstance(c, Vector) else V(*c))

        # vettori piatti (x, y, z, x, y, z...): li legge JsonUtility di Unity, che non conosce gli array annidati
        out.append({
            'id': wid,
            'polso': rel(W), 'gomito': rel(E), 'spalla': rel(S),
            'dita': [c for f in hand['fingers'] for p in f for c in rel(canon(p))],
            'pollice': [c for p in hand['thumb'] for c in rel(canon(p))],
            'palmo': rel(canon((0.003, 0.046, -0.002))),
            'dorso': rel(canon((0.003, 0.046, 0.030))),
        })
        res['left'][0].free()
        res['right'][0].free()
        print(f'[pose] {wid}: {time.time() - t0:.1f} s')
    os.makedirs(OUT, exist_ok=True)
    with open(os.path.join(OUT, 'presa_sinistra.json'), 'w', encoding='utf-8') as f:
        json.dump({'versione': 1, 'armi': out}, f, indent=1)
    with open(os.path.join(OUT, 'presa_destra.json'), 'w', encoding='utf-8') as f:
        json.dump({'versione': 1, 'armi': out_d}, f, indent=1)
    print(f'[pose] presa_sinistra.json: {len(out)} armi, presa_destra.json: {len(out_d)} armi')


# ----------------------------------------------------------------------------- manica in posa neutra
# Una manica sola per tutte le armi e tutte e due le braccia (Unity la specchia per la destra e la piega con le ossa del braccio):
# modellata con le proporzioni vere del braccio di ArmRig (omero 30 cm, avambraccio 26,2 cm) e il gomito piegato di 40 gradi, come
# lo tiene chi imbraccia un'arma. Prima ogni arma aveva la sua manica modellata sulla spalla finta (omero virtuale fino a 75 cm):
# col braccio vero ArmRig la stirava e la girava molto, e la manica si deformava (pieghe schiacciate, gomito che si svuota).
UPPER = 0.30


def stage_manica():
    """braccia/manica_libera.fbx: radice manica_libera, la manica (materiale G_manica) e i giunti polso_s, gomito_s, spalla_s
    (braccio sinistro, coordinate d'autore: avambraccio lungo -y dal polso, gomito piegato verso -z)."""
    t0 = time.time()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    mats = make_mats()
    W = V(0, 0, 0)
    D = V(0, -1, 0)                                   # dal polso al gomito
    E = W + D * LF
    flex = radians(40.0)
    U = V(0, -cos(flex), -sin(flex))                  # dal gomito alla spalla: angolo al gomito 140 gradi
    S = E + U * UPPER
    G = D                                             # la manichetta segue l'avambraccio (HandRig.UpdateCuff)
    X = V(1, 0, 0)
    slb, sE, total = sleeve_bm(W, G, D, E, S, X, seed=9.0, v2=True)
    sleeve = mesh_obj('manica_libera_manica', slb, mats['manica'])
    sc = bpy.context.scene
    top = bpy.data.objects.new('manica_libera', None)
    sc.collection.objects.link(top)
    keep = [top, sleeve]
    sleeve.parent = top
    for nm, p in (('polso_s', W), ('gomito_s', E), ('spalla_s', S)):
        e = bpy.data.objects.new(nm, None)
        e.empty_display_size = 0.01
        sc.collection.objects.link(e)
        e.location = p
        e.parent = top
        keep.append(e)
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in keep:
        o.select_set(True)
    bpy.context.view_layer.objects.active = top
    os.makedirs(OUT, exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, 'manica_libera.fbx'), use_selection=True, object_types={'EMPTY', 'MESH'}, apply_unit_scale=True,
                             apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y', mesh_smooth_type='FACE', use_tspace=True,
                             add_leaf_bones=False, bake_anim=False, path_mode='RELATIVE', embed_textures=False)
    print(f'[manica] manica_libera.fbx: {len(sleeve.data.vertices)} vertici, gomito a {degrees((W - E).angle(S - E)):.0f} gradi, '
          f'lunga {total:.2f} m, {time.time() - t0:.1f} s')


def stage_vedi():
    """Anteprime (Cycles su CPU) di mano libera e manica neutra, da tre lati, con una luce morbida: braccia/render/vedi_*.png."""
    for nm in ('mano_libera', 'manica_libera'):
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=os.path.join(OUT, nm + '.fbx'))
        obs = [o for o in bpy.context.scene.objects if o.type == 'MESH']
        pts = [o.matrix_world @ Vector(c) for o in obs for c in o.bound_box]
        mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
        mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
        ctr = (mn + mx) / 2
        size = max((mx - mn).x, (mx - mn).y, (mx - mn).z)
        sc = bpy.context.scene
        sc.render.engine = 'CYCLES'
        sc.cycles.device = 'CPU'
        sc.cycles.samples = 24
        sc.cycles.use_denoising = False
        sc.render.resolution_x, sc.render.resolution_y = 900, 700
        w = bpy.data.worlds.new('w')
        w.use_nodes = True
        w.node_tree.nodes['Background'].inputs[0].default_value = (0.18, 0.18, 0.2, 1)
        sc.world = w
        for o in obs:
            # materiali d'anteprima: cuoio scuro lucido per il guanto, tela chiara opaca per la manica, gomma per il cinturino
            nm_o = o.name.lower()
            col, rough = ((0.20, 0.17, 0.15, 1), 0.38) if 'guanto' in nm_o else ((0.10, 0.10, 0.11, 1), 0.6) if 'rinforzo' in nm_o else ((0.42, 0.36, 0.28, 1), 0.85)
            mt = bpy.data.materials.new('vedi_' + o.name)
            mt.use_nodes = True
            bsdf = next((n for n in mt.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
            if bsdf is not None:
                bsdf.inputs['Base Color'].default_value = col
                bsdf.inputs['Roughness'].default_value = rough
            o.data.materials.clear()
            o.data.materials.append(mt)
        ld = bpy.data.lights.new('key', 'AREA')
        ld.energy, ld.size = 60.0, 0.6
        lk = bpy.data.objects.new('key', ld)
        sc.collection.objects.link(lk)
        cd = bpy.data.cameras.new('cam')
        cd.lens = 50
        cam = bpy.data.objects.new('cam', cd)
        sc.collection.objects.link(cam)
        sc.camera = cam
        for tag, dvec in (('a', Vector((0.6, -0.8, 0.5))), ('b', Vector((-0.7, 0.3, 0.6))), ('c', Vector((0.1, 0.4, -0.9)))):
            dvec = dvec.normalized()
            cam.location = ctr + dvec * size * 2.2
            cam.rotation_euler = (ctr - cam.location).to_track_quat('-Z', 'Y').to_euler()
            lk.location = ctr + (dvec + Vector((0.3, 0.3, 0.8))).normalized() * size * 2.0
            lk.rotation_euler = (ctr - lk.location).to_track_quat('-Z', 'Y').to_euler()
            sc.render.filepath = os.path.join(OUT, 'render', f'vedi_{nm}_{tag}.png')
            bpy.ops.render.render(write_still=True)
        print(f'[vedi] {nm}: 3 viste')


if __name__ == '__main__':
    cat = catalog()
    all_ids = H.weapon_ids()
    ids = [a for a in ARGS if a in all_ids] or all_ids
    if 'prova' in ARGS:
        stage_prova(ids, cat)
    if 'build' in ARGS:
        stage_build(ids, cat)
    if 'libera' in ARGS:
        stage_libera()
    if 'manica' in ARGS:
        stage_manica()
    if 'pose' in ARGS:
        stage_pose(ids, cat)
    if 'vedi' in ARGS:
        stage_vedi()
