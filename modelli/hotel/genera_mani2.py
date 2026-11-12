"""
Mani e avambracci del giocatore, versione 2: per ognuna delle 28 armi si MISURA l'impugnatura vera (sezione, inclinazione, quota del
grilletto) e il guanto viene costruito attorno a quella, invece di riusare una sola posa tarata sul fucile d'assalto.

Pipeline:
  1. si importa l'FBX dell'arma (modelli/armi/export/<id>.fbx) e se ne fa un BVH (senza il caricatore);
  2. destra: si cerca l'inclinazione dell'asse dell'impugnatura che da' la sezione minima nel punto di presa (origine dell'arma);
     da raggi lanciati dall'esterno si ricava il contorno della sezione, e la mano (dita come quelle del monaco, ingrassate a guanto)
     piega ogni falange finche' tocca quel contorno; palmo sul fianco destro, pollice sul fianco sinistro, indice disteso;
  3. sinistra: stessa cosa sulla sezione dell'astina nel punto grip_l (mano sotto, dita su per il lato destro);
  4. guanto = mano + polsino, manica = tubo che si allarga; materiali G_guanto / G_manica (li rimappa ManiImport).

Uso (dalla cartella del file, Blender 4.5 headless):
  blender -b -P genera_mani2.py -- misura [id ...]      tabella delle misure (inclinazione, sezione) senza costruire le mani
  blender -b -P genera_mani2.py -- prova id [id ...]    costruisce le mani e rende tre viste per arma in mani2/render/
  blender -b -P genera_mani2.py -- build [id ...]       FBX con tutte le armi (o quelle indicate) -> mani2/mani.fbx
Coordinate d'autore: x destra, y avanti (verso la bocca), z su, origine nel centro dell'impugnatura. Alla fine tutto si trasla di
OFFSET (come kit.OFFSET) per stare nel riferimento dei modelli delle armi in Unity.
"""
import bpy, bmesh, math, os, sys, time
from math import sin, cos, pi, radians, sqrt, atan2, degrees, acos
from mathutils import Vector, Matrix, Euler, noise
from mathutils.bvhtree import BVHTree

DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(os.path.dirname(DIR), 'monaco'))
import genera_monaco as M          # noqa: E402  (capsule, ellissoidi, remesh a voxel, dita del monaco)

ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
ARMI = os.path.normpath(os.path.join(os.path.dirname(DIR), 'armi', 'export'))
OUT = os.path.join(DIR, 'mani2')
OFFSET = Vector((0.0, 0.10, -0.12))          # kit.OFFSET: dal centro dell'impugnatura al riferimento dei modelli in Unity

GS = 1.10            # ingrasso del guanto rispetto al dito nudo
Y_FRONT = 0.120      # y del davanti dell'impugnatura nel riferimento della mano (le nocche stanno a 0,077-0,091: le dita partono ~3 cm prima)
Z_PALM = -0.0140     # z della faccia dell'impugnatura che poggia sul palmo
MAX_BEND = 30.0      # massima flessione del polso (gradi) tra l'asse della mano e quello dell'avambraccio
# armi in cui la sinistra non tiene l'arma (WeaponDef.hand = 0): compare solo nella ricarica
ONE_HANDED = {'p9_pieta', 'm45_vigilia', 'rv_cardinale', 'mp_serafino', 'de_eremita', 'kn_coltello'}


def V(x=0.0, y=0.0, z=0.0):
    return Vector((x, y, z))


# ----------------------------------------------------------------------------- misure dell'arma
def load_weapon(wid):
    """Importa l'FBX dell'arma; ritorna (bvh senza caricatore, bvh intero, info) nel riferimento d'autore (origine = presa destra)."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.join(ARMI, wid + '.fbx'), axis_forward='-Z', axis_up='Y')
    bpy.context.view_layer.update()
    va, ta, vn, tn = [], [], [], []
    info = {}
    for ob in list(bpy.context.scene.objects):
        base = ob.name.split('.')[0]
        if ob.type == 'EMPTY':
            info[base] = ob.matrix_world.translation - OFFSET
        elif ob.type == 'MESH':
            me = ob.data
            me.calc_loop_triangles()
            mw = ob.matrix_world
            vs = [(mw @ v.co) - OFFSET for v in me.vertices]
            ts = [tuple(t.vertices) for t in me.loop_triangles]
            if base == 'trigger':
                info['trigger_c'] = (V(*[min(p[i] for p in vs) for i in range(3)]) + V(*[max(p[i] for p in vs) for i in range(3)])) / 2
            dests = [(va, ta)] if base == 'caricatore' else [(va, ta), (vn, tn)]
            for VV, TT in dests:
                o = len(VV)
                VV.extend(vs)
                TT.extend([(a + o, b + o, c + o) for a, b, c in ts])
    info['_mesh_count'] = len([o for o in bpy.context.scene.objects if o.type == 'MESH'])
    return BVHTree.FromPolygons(vn, tn), BVHTree.FromPolygons(va, ta), info


def slice_radii(bvh, C, ex, ey, rmax, n=96):
    """Distanze dal centro C alla prima superficie, lungo n direzioni del piano (ex, ey), con raggi lanciati dall'esterno verso C."""
    out = []
    for k in range(n):
        a = 2 * pi * k / n
        d = ex * cos(a) + ey * sin(a)
        h = bvh.ray_cast(C + d * rmax, -d, rmax + 0.004)
        if h[0] is None:
            out.append(None)
            continue
        r = rmax - h[3]
        out.append(r if r > 0.003 else None)
    return out


def radii_to_poly(rs):
    n = len(rs)
    # filtro mediano a 3 punti: toglie i raggi che hanno preso uno spigolo vicino
    f = list(rs)
    for k in range(n):
        w = [x for x in (rs[k - 1], rs[k], rs[(k + 1) % n]) if x is not None]
        if len(w) >= 2:
            f[k] = sorted(w)[len(w) // 2]
    pts = []
    for k, r in enumerate(f):
        if r is None:
            continue
        a = 2 * pi * k / n
        pts.append((r * cos(a), r * sin(a)))
    return pts


def poly_area(pts):
    s = 0.0
    for i in range(len(pts)):
        x1, y1 = pts[i]
        x2, y2 = pts[(i + 1) % len(pts)]
        s += x1 * y2 - x2 * y1
    return abs(s) / 2


def ellipse_poly(hw, dp, n=48):
    return [(hw * cos(2 * pi * k / n), dp * sin(2 * pi * k / n)) for k in range(n)]


def find_grip(bvh, G):
    """Inclinazione (la cima va in avanti) per cui la sezione nel punto di presa e' minima. Ritorna (T, a_up, u, v, poly, hits)."""
    u = V(1, 0, 0)

    def at(T):
        t = radians(T)
        a_up = V(0, sin(t), cos(t))
        v = a_up.cross(u)                    # avanti e un po' in basso: nel piano della sezione
        rs = slice_radii(bvh, G, u, v, 0.052)
        hits = sum(1 for r in rs if r is not None)
        return a_up, v, rs, hits

    best = None
    for T in [x * 2.0 for x in range(0, 17)]:               # 0..32 gradi: oltre sarebbe una fetta obliqua della cassa, non la presa
        a_up, v, rs, hits = at(T)
        if hits < 0.82 * len(rs):
            continue
        area = poly_area(radii_to_poly(rs))
        if best is None or area < best[0]:
            best = (area, T)
    if best is None:
        return None
    T0 = best[1]
    for T in [T0 + dt * 0.5 for dt in range(-4, 5)]:
        if T < 0.0 or T > 32.0:
            continue
        a_up, v, rs, hits = at(T)
        if hits < 0.82 * len(rs):
            continue
        area = poly_area(radii_to_poly(rs))
        if area < best[0]:
            best = (area, T)
    T = best[1]
    a_up, v, rs, hits = at(T)
    return T, a_up, u, v, radii_to_poly(rs), hits


def clip_depth(poly, depth):
    """Tiene i punti del contorno entro 'depth' metri dal davanti (v massimo); il dietro si chiude con una retta."""
    vmax = max(p[1] for p in poly)
    keep = [p for p in poly if p[1] >= vmax - depth]
    return keep if len(keep) >= 8 else poly


def probe_left_edge(bvh, G, a_up, v, heights, vs_, x0=-0.12):
    """Faccia sinistra della geometria sopra e intorno alla presa: minimo x colpito da raggi che vanno verso destra."""
    best = None
    for s in heights:
        for vp in vs_:
            o = G + a_up * s + v * vp + V(x0, 0, 0)
            h = bvh.ray_cast(o, V(1, 0, 0), 0.12)
            if h[0] is not None and (best is None or h[0].x < best):
                best = h[0].x
    return best


# ----------------------------------------------------------------------------- contorno e dita
def sdf_poly(poly, y, z):
    """Distanza con segno (positiva fuori) dal contorno poligonale nel piano (y, z)."""
    d2 = 1e9
    inside = False
    n = len(poly)
    for i in range(n):
        y1, z1 = poly[i]
        y2, z2 = poly[(i + 1) % n]
        ey, ez = y2 - y1, z2 - z1
        L2 = ey * ey + ez * ez
        t = 0.0 if L2 < 1e-14 else max(0.0, min(1.0, ((y - y1) * ey + (z - z1) * ez) / L2))
        dy, dz = y1 + ey * t - y, z1 + ez * t - z
        d2 = min(d2, dy * dy + dz * dz)
        if (z1 > z) != (z2 > z) and y < ey * (z - z1) / ez + y1:
            inside = not inside
    d = sqrt(d2)
    return -d if inside else d


def wrap_curls_poly(knuckle, spread, lens, radii, poly, maxc=(100, 115, 85)):
    """Piega ogni falange finche' tocca il contorno (nel piano y, z della mano)."""
    a = radians(spread)
    phi = 0.0
    p = Vector(knuckle)
    curls = []
    for i, L in enumerate(lens):
        rr = max(radii[i], radii[i + 1]) * GS + 0.0006
        found = maxc[i]
        for step in range(0, maxc[i] + 1):
            ph = phi + radians(step)
            d = V(sin(a) * cos(ph), cos(a) * cos(ph), -sin(ph))
            if min(sdf_poly(poly, (p + d * L * t).y, (p + d * L * t).z) for t in (0.3, 0.5, 0.75, 1.0)) <= rr:
                found = step
                break
        phi += radians(found)
        curls.append(found)
        p = p + V(sin(a) * cos(phi), cos(a) * cos(phi), -sin(phi)) * L
    return tuple(curls)


THUMB_R = tuple(r * GS for r in M.THUMB_R)
CMC = V(-0.020, 0.014, -0.011)


def thumb_joints(zc):
    """Pollice disteso in avanti sul fianco sinistro: le direzioni sono quelle della mano rilassata del monaco e la profondita'
    (z, verso il lato sinistro) si regola finche' la punta cade alla quota zc."""
    best = None
    for kz in [0.2 + 0.05 * i for i in range(0, 60)]:
        d1 = V(-0.50, 0.62, -0.60 * kz).normalized()
        d2 = V(-0.22, 0.90, -0.40 * kz).normalized()
        d3 = V(-0.10, 0.96, -0.28 * kz).normalized()
        mcp = CMC + d1 * 0.046
        ip = mcp + d2 * 0.035
        tip = ip + d3 * 0.029
        err = abs(ip.z - zc)
        if best is None or err < best[0]:
            best = (err, [CMC, mcp, ip, tip])
    return best[1]


def build_hand_bm(poly, thumb_zc, idx_curls=(4, 3, 0), idx_spread=0.0):
    """Mano destra canonica (pollice verso -x, dita verso +y, dorso verso +z), ingrassata a guanto, con le dita avvolte al contorno.
    idx_curls=None: anche l'indice si avvolge; altrimenti resta disteso con quelle pieghe e quell'apertura (gradi)."""
    bm = bmesh.new()
    ell, cap, sph = M.bm_ellipsoid, M.bm_capsule, M.bm_sphere
    g = 1.08
    ell(bm, (0.003, 0.046, -0.002), (0.036 * g, 0.047, 0.0125 * g), seg=28)
    ell(bm, (-0.021, 0.026, -0.009), (0.016 * g, 0.027, 0.012 * g), Euler((0, 0, radians(-25))).to_matrix())
    ell(bm, (0.025, 0.034, -0.007), (0.012 * g, 0.030, 0.010 * g))
    ell(bm, (0.0, -0.004, 0.0), (0.027 * g, 0.020, 0.016 * g))
    cap(bm, (0, -0.050, 0), (0, -0.006, 0), 0.0300, 0.0255, seg=18)
    sph(bm, (0.022, -0.004, 0.007), 0.0065 * g)
    report = []
    for fi, (nm, k, sp0, lens, radii) in enumerate(M.FINGERS):
        if fi == 0 and idx_curls is not None:
            curls, sp = idx_curls, idx_spread                   # indice disteso lungo il telaio
        else:
            sp = M.SPREAD_PRESA[fi]
            curls = wrap_curls_poly(k, sp, lens, radii, poly)
        report.append(curls)
        pts, dirs, a = M.finger_chain(k, sp, lens, curls)
        cap(bm, (k[0] * 0.35, 0.004, 0.0075), (k[0], k[1] - 0.01, 0.0085), 0.003 * GS, 0.003 * GS, seg=8)
        for i in range(3):
            cap(bm, pts[i], pts[i + 1], radii[i] * GS, radii[i + 1] * GS)
        sph(bm, pts[0], radii[0] * GS * 1.18)
        sph(bm, pts[1], radii[1] * GS * 1.12)
        sph(bm, pts[2], radii[2] * GS * 1.08)
    tj = thumb_joints(thumb_zc)
    for i in range(3):
        cap(bm, tj[i], tj[i + 1], THUMB_R[i], THUMB_R[i + 1])
    sph(bm, tj[1], THUMB_R[1] * 1.15)
    sph(bm, tj[2], THUMB_R[2] * 1.1)
    return bm, report, tj


# ----------------------------------------------------------------------------- mesh
def make_obj(name, bm, voxel, sm_iter=3, folds=0.0, seed=0.0, decimate=0.5):
    ob = M.bm_obj(bm, name)
    m = ob.modifiers.new('remesh', 'REMESH')
    m.mode, m.voxel_size, m.adaptivity, m.use_smooth_shade = 'VOXEL', voxel, 0.0, True
    s = ob.modifiers.new('smooth', 'SMOOTH')
    s.factor, s.iterations = 0.5, sm_iter
    M.apply_all(ob)
    if folds > 0:
        for v in ob.data.vertices:
            n = v.normal
            v.co += n * noise.noise(Vector((v.co.x * 22 + seed, v.co.y * 22, v.co.z * 22))) * folds
    if decimate < 1.0:
        dm = ob.modifiers.new('decima', 'DECIMATE')
        dm.ratio = decimate
        M.apply_all(ob)
    ob.data.shade_smooth()
    return ob


def box_uv(ob, scale=1.0):
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    uvl = bm.loops.layers.uv.verify()
    for f in bm.faces:
        f.normal_update()
        n = f.normal
        ax, ay, az = abs(n.x), abs(n.y), abs(n.z)
        for lo in f.loops:
            p = lo.vert.co
            uv = (p.x, p.y) if az >= ax and az >= ay else (p.x, p.z) if ay >= ax else (p.y, p.z)
            lo[uvl].uv = (uv[0] * scale, uv[1] * scale)
    bm.to_mesh(ob.data)
    bm.free()


def frame_matrix(X, Y, Z, O):
    m = Matrix(((X.x, Y.x, Z.x, O.x), (X.y, Y.y, Z.y, O.y), (X.z, Y.z, Z.z, O.z), (0, 0, 0, 1)))
    return m


def limit_bend(back, D, max_deg):
    """Direzione dell'avambraccio D, portata entro max_deg gradi dal prolungamento della mano (back)."""
    D = D.normalized()
    ang = degrees(acos(max(-1.0, min(1.0, back.dot(D)))))
    if ang <= max_deg:
        return D, ang
    t = max_deg / ang
    # interpolazione sferica semplice
    D2 = (back * (1 - t) + D * t).normalized()
    return D2, degrees(acos(max(-1.0, min(1.0, back.dot(D2)))))


def sleeve_bm(W, D, length=0.44, dive=0.0, span=0.35):
    """Polsino del guanto (corto) e manica (tubo che si allarga) dal polso W lungo D.
    La manica deve uscire dall'inquadratura: la sinistra, che va dall'astina all'angolo in basso a sinistra, ne vuole piu' di un metro,
    altrimenti si vede il tappo tondo in fondo al tubo. Il raggio sale da 3,7 a ~4,7 cm e poi resta li' (piu' grosso, in mira, la
    manica riempirebbe un lato dello schermo)."""
    bmg = bmesh.new()
    pts = [W + D * (0.0 + 0.020 * k) for k in range(4)]
    for i in range(3):
        M.bm_capsule(bmg, pts[i], pts[i + 1], (0.0320, 0.0340, 0.0360)[i], (0.0340, 0.0360, 0.0375)[i], seg=18)
    bms = bmesh.new()
    n = max(4, int(round(length / 0.11)))
    # l'avambraccio scende: il polso e' dritto (limit_bend), ma il gomito sta molto piu' in basso della mano. Il tubo si piega
    # gradualmente verso il basso (dive gradi in tutto, a regime dopo ~35 cm); senza, con l'impugnatura inclinata e il calcio, la manica
    # resta orizzontale all'altezza dell'occhio e in mira il piano vicino la taglia in un lenzuolo.
    up = Vector((0.0, 0.0, 1.0))
    upp = (up - D * D.dot(up))
    upp = upp.normalized() if upp.length > 1e-4 else Vector((0.0, 1.0, 0.0))
    p = W + D * 0.050
    pts = [p.copy()]
    step = length / n
    for k in range(n):
        s = (k + 0.5) * step
        t = min(1.0, s / span)
        a = radians(dive) * t * t * (3.0 - 2.0 * t)
        d = (D * cos(a) - upp * sin(a)).normalized()
        p = p + d * step
        pts.append(p.copy())
    rr = [0.0365 + 0.0120 * (1.0 - math.exp(-(step * k) / 0.22)) for k in range(n + 1)]
    for i in range(n):
        M.bm_capsule(bms, pts[i], pts[i + 1], rr[i], rr[i + 1], seg=16)
    return bmg, bms


# ----------------------------------------------------------------------------- una arma
def hands_for(wid, log=print):
    bvh, bvh_all, info = load_weapon(wid)
    G = V(0, 0, 0)
    res = {'id': wid}
    # ------------------------------------------------ destra
    fg = find_grip(bvh, G)
    if fg is None:
        log(f'  [{wid}] impugnatura non misurabile: sezione di ripiego')
        T = 14.0
        t = radians(T)
        a_up = V(0, sin(t), cos(t))
        u = V(1, 0, 0)
        v = a_up.cross(u)
        poly3 = ellipse_poly(0.016, 0.021)
        measured = False
    else:
        T, a_up, u, v, poly3, hits = fg
        poly3 = clip_depth(poly3, 0.055)             # casse lunghe: la mano ne copre solo la parte davanti
        measured = True
    # quota del dito indice: il baricentro del grilletto lungo l'asse (senza grilletto: un po' sopra il centro)
    R = info.get('trigger_c', G + a_up * 0.028)
    X_h, Y_h, Z_h = -a_up, v, u
    ymax = max(p[1] for p in poly3)           # v
    zmax = max(p[0] for p in poly3)           # u
    zmin = min(p[0] for p in poly3)
    oy = Y_FRONT - ymax
    oz = Z_PALM - zmax
    ox = -0.026 - (R - G).dot(X_h)
    O = G - (X_h * ox + Y_h * oy + Z_h * oz)
    poly_h = [(p[1] + oy, p[0] + oz) for p in poly3]          # (y_h, z_h)
    # il pollice sta a sinistra della faccia sinistra della presa e di cio' che c'e' sopra (telaio, ricevitore)
    left = probe_left_edge(bvh, G, a_up, v, (0.025, 0.04, 0.055), (-0.01, 0.015, 0.04))
    uthumb = min(zmin, left if left is not None else zmin) - THUMB_R[2] - 0.0025
    thumb_zc = uthumb + oz
    # indice disteso e parallelo alla canna: aprendolo di -T gradi nel piano (x, y) della mano diventa orizzontale nel mondo
    bm, rep, tj = build_hand_bm(poly_h, thumb_zc, idx_curls=(5, 14, 8), idx_spread=-T)
    Mx = frame_matrix(X_h, Y_h, Z_h, O)
    W = O + Y_h * (-0.006)
    D, bend = limit_bend(-Y_h, V(0.18, -0.88, -0.34), MAX_BEND)
    log(f'  [{wid}] destra: inclinazione {T:.1f} gradi, sezione {(max(p[0] for p in poly3) - zmin) * 1000:.1f} x '
        f'{(ymax - min(p[1] for p in poly3)) * 1000:.1f} mm ({"misurata" if measured else "ripiego"}), curve {rep[1:]}, polso {bend:.0f} gradi')
    res['right'] = (bm, Mx, W, D, False)
    res['T'] = T
    res['anchor_d'] = G
    # ------------------------------------------------ sinistra: astina
    gl = info.get('grip_l')
    ex, ey = V(1, 0, 0), V(0, 0, 1)
    if gl is None:
        gl = G + V(0, 0.2, 0.0)
    rs = slice_radii(bvh, gl, ex, ey, 0.07)
    hits = sum(1 for r in rs if r is not None)
    if wid in ONE_HANDED:
        # la sinistra non regge l'arma: si vede solo nella ricarica, a coppa attorno a un caricatore
        polyl = ellipse_poly(0.019, 0.026)
        measured = False
    elif hits < 0.7 * len(rs):
        log(f'  [{wid}] astina non misurabile ({hits}/{len(rs)}): cerchio di ripiego')
        polyl = ellipse_poly(0.030, 0.030)
        measured = False
    else:
        polyl = radii_to_poly(rs)
        measured = True
    XL, YL, ZL = V(0, -1, 0), V(1, 0, 0), V(0, 0, -1)
    ymaxl = max(p[0] for p in polyl)          # x destro
    zmaxl = max(-p[1] for p in polyl)         # -z: la faccia sotto
    oyl = Y_FRONT - ymaxl
    ozl = Z_PALM - zmaxl
    oxl = 0.004 - (gl - gl).dot(XL)
    OL = gl - (XL * oxl + YL * oyl + ZL * ozl)
    poly_hl = [(p[0] + oyl, -p[1] + ozl) for p in polyl]
    zminl = min(p[1] for p in poly_hl)
    bml, repl, tjl = build_left(poly_hl, zminl)
    ML = frame_matrix(XL, YL, ZL, OL)
    WL = OL + YL * (-0.006)
    DL, bendl = limit_bend(-YL, V(-0.85, -0.42, -0.32), MAX_BEND + 6)
    log(f'  [{wid}] sinistra: sezione {(max(p[0] for p in polyl) - min(p[0] for p in polyl)) * 1000:.1f} x '
        f'{(max(p[1] for p in polyl) - min(p[1] for p in polyl)) * 1000:.1f} mm ({"misurata" if measured else "ripiego"}), curve {repl[1:]}, polso {bendl:.0f} gradi')
    res['left'] = (bml, ML, WL, DL, True)
    res['anchor_s'] = gl
    return res


def build_left(poly_h, zmin_h):
    """Mano sinistra sotto l'astina: stessa mano, con l'indice avvolto come le altre dita e il pollice sul lato sinistro."""
    bm, rep, tj = build_hand_bm(poly_h, zmin_h - THUMB_R[2] - 0.0025, idx_curls=None)
    return bm, rep, tj


def realize(res, mat_g, mat_m):
    """Dai dati di una mano ai due oggetti (guanto, manica) nel riferimento d'autore + OFFSET."""
    objs = {}
    wid = res['id']
    for side, key in (('d', 'right'), ('s', 'left')):
        bm, Mx, W, D, mirrored = res[key]
        # la mano e' in coordinate canoniche (si porta nel riferimento d'autore), il polsino e la manica gia' nel riferimento d'autore
        bmesh.ops.transform(bm, matrix=Mx, verts=bm.verts)
        if mirrored:
            bmesh.ops.reverse_faces(bm, faces=bm.faces)
        # la destra resta corta: una manica piu' lunga arriva alla camera e il piano vicino la taglia (in mira, con l'impugnatura
        # a 30 cm dall'occhio, ne esce un lenzuolo olivastro); la sinistra e' lontana dall'occhio e deve uscire dall'inquadratura
        if mirrored:
            cuff_bm, sleeve_b = sleeve_bm(W, D, length=1.20, dive=18.0, span=0.35)
        else:
            # misurato in mira (ArmiShots, [ArmiMani]): con l'impugnatura molto inclinata (T alto, calcio) il limite del polso lascia
            # l'avambraccio quasi orizzontale a 6-9 cm dall'occhio, in pieno campo; deve scendere a 40-55 gradi subito dopo il polsino
            cuff_bm, sleeve_b = sleeve_bm(W, D, length=0.44, dive=40.0 + 0.8 * max(0.0, res['T'] - 14.0), span=0.16)
        tmp = bpy.data.meshes.new('tmp')
        cuff_bm.to_mesh(tmp)
        cuff_bm.free()
        bm.from_mesh(tmp)                      # il polsino si unisce alla mano nello stesso guanto
        bpy.data.meshes.remove(tmp)
        glove = make_obj(f'{side}_{wid}_guanto', bm, voxel=0.0028, sm_iter=3, folds=0.0006, seed=1.0 if side == 'd' else 5.0, decimate=0.45)
        sleeve = make_obj(f'{side}_{wid}_manica', sleeve_b, voxel=0.0085, sm_iter=4, folds=0.0035, seed=3.0 if side == 'd' else 9.0, decimate=0.42)
        for o, mt in ((glove, mat_g), (sleeve, mat_m)):
            o.location += OFFSET
            o.data.materials.append(mt)
            box_uv(o)
        objs[side] = (glove, sleeve)
    return objs


def material(name, color, rough, metal=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = (*color, 1)
    b.inputs['Roughness'].default_value = rough
    b.inputs['Metallic'].default_value = metal
    return m


def weapon_ids():
    return sorted(f[:-4] for f in os.listdir(ARMI) if f.endswith('.fbx'))


# ----------------------------------------------------------------------------- fasi
def stage_misura(ids):
    for wid in ids:
        t0 = time.time()
        bvh, bvh_all, info = load_weapon(wid)
        fg = find_grip(bvh, V(0, 0, 0))
        if fg is None:
            print(f'[misura] {wid}: destra NON misurabile')
        else:
            T, a_up, u, v, poly, hits = fg
            hw = (max(p[0] for p in poly) - min(p[0] for p in poly)) / 2
            dp = (max(p[1] for p in poly) - min(p[1] for p in poly)) / 2
            print(f'[misura] {wid}: T={T:.1f} sezione {hw * 2000:.1f} x {dp * 2000:.1f} mm, raggi {hits}/96, grilletto {"si" if "trigger_c" in info else "no"}, '
                  f'grip_l {"si" if "grip_l" in info else "no"} ({time.time() - t0:.1f} s)')


def setup_cpu(samples, res):
    """Cycles su CPU (la GPU e' dell'utente), denoiser OpenImageDenoise."""
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    sc.cycles.device = 'CPU'
    sc.cycles.samples = samples
    sc.cycles.use_denoising = True
    sc.cycles.denoiser = 'OPENIMAGEDENOISE'
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.resolution_percentage = 100
    sc.render.image_settings.file_format = 'PNG'
    sc.view_settings.view_transform = 'AgX'
    sc.view_settings.look = 'AgX - Medium High Contrast'


def look_at(ob, target):
    d = Vector(target) - ob.location
    ob.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()


def stage_prova(ids):
    os.makedirs(os.path.join(OUT, 'render'), exist_ok=True)
    for wid in ids:
        res = hands_for(wid)
        mat_g = material('G_guanto', (0.05, 0.05, 0.055), 0.45)
        mat_m = material('G_manica', (0.10, 0.11, 0.09), 0.85)
        objs = realize(res, mat_g, mat_m)
        sc = bpy.context.scene
        gun = material('arma', (0.30, 0.30, 0.32), 0.35, 0.7)
        for ob in sc.objects:
            if ob.type == 'MESH' and not (ob.name[:2] in ('d_', 's_')):
                ob.data.materials.clear()
                ob.data.materials.append(gun)
        setup_cpu(int(os.environ.get('MANI_SAMPLES', '40')), (1100, 640))
        w = bpy.data.worlds.new('mondo')
        sc.world = w
        w.use_nodes = True
        w.node_tree.nodes['Background'].inputs['Color'].default_value = (0.07, 0.07, 0.08, 1)
        for nm, loc, pw, sz, col in (('key', (-0.6, -0.3, 0.9), 70, 0.7, (1.0, 0.88, 0.75)), ('fill', (0.9, -0.5, 0.2), 30, 0.9, (0.85, 0.9, 1.0)),
                                     ('top', (0.0, 0.2, 1.0), 40, 0.8, (1, 1, 1))):
            l = bpy.data.lights.new(nm, 'AREA')
            l.energy, l.size, l.color = pw, sz, col
            lo = bpy.data.objects.new(nm, l)
            lo.location = Vector(loc) + OFFSET
            look_at(lo, Vector((0, 0.05, 0.0)) + OFFSET)
            sc.collection.objects.link(lo)
        views = {
            'giocatore': (V(-0.28, -0.72, 0.38), V(-0.02, 0.5, 0.20), 60),
            'destra': (V(0.55, 0.0, 0.06), V(0, 0.02, 0.0), 40),
            'sinistra': (V(-0.55, 0.2, 0.0), V(0, 0.2, -0.01), 40),
            'sopra': (V(0.0, 0.05, 0.62), V(0.0, 0.05, 0.0), 40),
        }
        pick = [a for a in ARGS if a in views] or ['giocatore', 'destra', 'sinistra']
        cam = bpy.data.objects.new('camera', bpy.data.cameras.new('camera'))
        sc.collection.objects.link(cam)
        for nm in pick:
            loc, tgt, fov = views[nm]
            cam.location = loc + OFFSET
            cam.data.lens_unit = 'FOV'
            cam.data.angle = radians(fov)
            cam.data.sensor_fit = 'VERTICAL'
            cam.data.clip_start = 0.03
            look_at(cam, tgt + OFFSET)
            sc.camera = cam
            sc.render.filepath = os.path.join(OUT, 'render', f'{wid}_{nm}.png')
            bpy.ops.render.render(write_still=True)
            print('[prova]', sc.render.filepath)


def stage_build(ids):
    os.makedirs(OUT, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    allres = []
    for wid in ids:
        t0 = time.time()
        res = hands_for(wid)
        allres.append(res)
        # le mani si realizzano in una scena pulita per arma: qui serve solo la geometria, quindi si esporta a pezzi
        mat_g = material('G_guanto', (0.05, 0.05, 0.055), 0.45)
        mat_m = material('G_manica', (0.10, 0.11, 0.09), 0.85)
        objs = realize(res, mat_g, mat_m)
        export_one(wid, objs, res)
        print(f'[build] {wid}: {time.time() - t0:.1f} s')


def export_one(wid, objs, res):
    """Un FBX per arma (mani2/<id>.fbx): due Empty (mano_d, mano_s) con guanto e manica. Ogni Empty sta nel punto di presa (destra:
    centro dell'impugnatura, sinistra: grip_l) e le mesh gli sono figlie senza spostarsi: in Unity la posizione dell'Empty e' la
    posizione del palmo, quella che Weapon.cs sposta per la ricarica."""
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
        for o in objs[side]:
            o.parent = r
            o.matrix_parent_inverse = inv
            keep.append(o)
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in keep:
        o.select_set(True)
    bpy.context.view_layer.objects.active = top
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, wid + '.fbx'), use_selection=True, object_types={'EMPTY', 'MESH'}, apply_unit_scale=True,
                             apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y', mesh_smooth_type='FACE', use_tspace=True,
                             add_leaf_bones=False, bake_anim=False, path_mode='RELATIVE', embed_textures=False)


if __name__ == '__main__':
    ids = [a for a in ARGS if a in weapon_ids()] or weapon_ids()
    if 'misura' in ARGS:
        stage_misura(ids)
    if 'prova' in ARGS:
        stage_prova(ids)
    if 'build' in ARGS:
        stage_build(ids)
