"""
Arredi della chiesa iperrealistici (fase 5, seconda serie): sarcofago della cripta, statua del monaco e inginocchiatoio. Stessi nomi di
file e di mesh dei vecchi (modelli/chiesa/genera_chiesa.py), stesse misure di PropDefs.cs: in Unity basta sostituire gli FBX.
Si appoggia agli attrezzi di arredi5_lib.py (modanature a sweep, imbottiture, UV con la venatura, livelli di dettaglio) e aggiunge quelli
della pietra: blocchi a maglia con spigoli arrotondati e una lieve irregolarita' (weather), cornici di rilievo ad arco, panneggio a lathe.

Uso (dalla cartella del file):
  blender -b -P genera_chiesa5.py -- build [nome ...]    FBX in arredi5/<nome>.fbx (poi da copiare in Assets/_PryingEyes/Models/Chiesa)
  blender -b -P genera_chiesa5.py -- render [nome ...]   anteprime in arredi5/render/<nome>.png (CPU; ARREDI_LOD=1|2 fotografa un livello piu' povero)
Assi: Z in alto, davanti = +Y (Unity +Z), origine al centro a terra, metri.
"""
import bpy, bmesh, math, os, sys, random
from mathutils import Vector, Matrix, noise
from math import sin, cos, pi, radians, sqrt

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import arredi5_lib as L
from arredi5_lib import (ga, g3, g4, gc, V, bx, part, spline, rr_profile, sweep, crown_profile, base_profile, rect_path, prism_bevel,
                         cushion, piping, rect_loop, stud_row, knob, Asset5)
from genera_arredi import box, lathe, sphere, cyl, tube, rounded_grid, smoothstep
from genera_chiesa import poly_prism, gothic_pts, torus

OUT = os.path.join(HERE, 'arredi5')
if os.path.exists(os.path.join(L.TEXDIR, 'pietra_massello_base.png')):
    L.PREVIEW_TEX['A_pietra'] = 'pietra_massello'


# ----------------------------------------------------------------------------- pietra
def weather(bm, amp=0.004, freq=6.0, seed=1):
    """Sposta ogni vertice di una piccola quantita' casuale e lenta (rumore di Perlin in funzione della posizione: i vertici coincidenti si muovono
    insieme): la pietra squadrata a mano non ha facce piane."""
    off = Vector((seed * 13.7, seed * 7.1, seed * 3.3))
    for v in bm.verts:
        v.co += noise.noise_vector(v.co * freq + off) * amp


def stone(bm, c, s, r=0.012, cell=0.08, amp=0.0035, seed=1):
    """Blocco di pietra: parallelepipedo a maglia (una cella ogni 'cell' metri) con gli spigoli arrotondati e le facce appena ondulate."""
    nx, ny, nz = (max(1, round(s[i] / cell)) for i in range(3))
    vs = rounded_grid(bm, c, s, min(r, 0.46 * min(s)), nx, ny, nz)
    off = Vector((seed * 13.7, seed * 7.1, seed * 3.3))
    for v in vs:
        v.co += noise.noise_vector(v.co * 7.0 + off) * amp
    return vs


def lathe_fn(bm, profile, fn, c=V(0, 0, 0), segs=32, closed_top=True, closed_bottom=True):
    """Solido di rivoluzione con il raggio modulato: fn(raggio, quota, angolo) -> raggio (pieghe di una tonaca, coste di un bacino)."""
    rings = []
    for r, h in profile:
        if r < 1e-6:
            rings.append([bm.verts.new(Vector(c) + V(0, 0, h))])
            continue
        rings.append([bm.verts.new(Vector(c) + V(cos(2 * pi * j / segs) * fn(r, h, 2 * pi * j / segs), sin(2 * pi * j / segs) * fn(r, h, 2 * pi * j / segs), h))
                      for j in range(segs)])
    for i in range(len(rings) - 1):
        a, b = rings[i], rings[i + 1]
        for j in range(segs):
            k = (j + 1) % segs
            if len(a) == 1 and len(b) == 1:
                continue
            if len(a) == 1:
                bm.faces.new((a[0], b[k], b[j]))
            elif len(b) == 1:
                bm.faces.new((a[j], a[k], b[0]))
            else:
                bm.faces.new((a[j], a[k], b[k], b[j]))
    if closed_bottom and len(rings[0]) > 1:
        bm.faces.new(rings[0][::-1])
    if closed_top and len(rings[-1]) > 1:
        bm.faces.new(rings[-1])
    return rings


def ring_frame(bm, outer, inner, y0, y1, ch=0.010):
    """Cornice di rilievo chiusa nel piano XZ tra due contorni con lo stesso numero di punti: esce da y0 fino a y1 con lo smusso 'ch' sul bordo
    interno (come le cornici della pala). y1 > y0 sporge verso +Y."""
    n = len(outer)
    mid = [(o[0] * 0.78 + i[0] * 0.22, o[1] * 0.78 + i[1] * 0.22) for o, i in zip(outer, inner)]
    sgn = 1 if y1 >= y0 else -1
    loops = [[bm.verts.new(V(x, y, z)) for x, z in pts] for pts, y in
             ((outer, y0), (outer, y1 - sgn * ch), (mid, y1), (inner, y1 - sgn * ch * 1.6), (inner, y0))]
    for k in range(len(loops)):
        a, b = loops[k - 1], loops[k]
        for i in range(n):
            j = (i + 1) % n
            bm.faces.new((a[i], a[j], b[j], b[i]))


# ----------------------------------------------------------------------------- ossa
def long_bone(bone, a, b, r=0.016):
    """Osso lungo: diafisi leggermente piu' sottile al centro e due epifisi a doppio nodo."""
    a, b = Vector(a), Vector(b)
    d = (b - a)
    n = d.normalized()
    side = n.cross(V(0, 0, 1))
    if side.length < 1e-3:
        side = V(1, 0, 0)
    side.normalize()
    cyl(bone, a + n * 0.02, b - n * 0.02, r * 0.78, 10)
    for end, sgn in ((a, 1), (b, -1)):
        for off in (-0.55, 0.55):
            sphere(bone, end + n * (0.018 * sgn) + side * (r * off), r * 1.05, 8)
        sphere(bone, end + n * (0.04 * sgn), r * 0.95, 8)


def rib(bone, c, r_arc, ang, r=0.0068, span=2.2):
    """Costola: arco di circonferenza in un piano verticale, con la punta che si assottiglia."""
    pts = []
    for k in range(7):
        t = k / 6
        a = -span / 2 + span * t
        pts.append(c + Vector((cos(ang) * sin(a) * r_arc, sin(ang) * sin(a) * r_arc, (1 - cos(a)) * r_arc * 0.55)))
    tube(bone, pts, (r, r * 0.9, r * 0.45), segs=6, n=12)


def ell(dst, p, size, seg=14, rot=None):
    """Ellissoide con i tre semiassi 'size', centrato in p, ruotato dalla matrice 3x3 'rot' se data."""
    t = bmesh.new()
    sphere(t, V(0, 0, 0), 1.0, seg)
    bmesh.ops.transform(t, matrix=Matrix.Diagonal((size[0], size[1], size[2], 1.0)), verts=t.verts)
    g3.merge(dst, t, Matrix.Translation(V(*p)) @ (rot.to_4x4() if rot is not None else Matrix.Identity(4)))


def skull(bone, dark, c, yaw=0.0, s=1.0):
    """Cranio: calotta, volto con orbite e cavita' nasale scure, zigomi, arcata dei denti e mandibola. Guarda verso +Y locale (poi ruotato di yaw)."""
    tmp_b, tmp_d = bmesh.new(), bmesh.new()
    ell(tmp_b, (0, -0.006, 0.088), (0.077, 0.094, 0.083), 16)                  # calotta
    ell(tmp_b, (0, 0.070, 0.050), (0.052, 0.046, 0.050), 12)                    # mascellare
    for sx in (-1, 1):
        ell(tmp_b, (sx * 0.061, 0.052, 0.062), (0.017, 0.030, 0.016), 8)       # zigomi
        ell(tmp_d, (sx * 0.030, 0.092, 0.076), (0.021, 0.014, 0.022), 10)      # orbite
        ell(tmp_b, (sx * 0.075, -0.004, 0.032), (0.012, 0.022, 0.030), 8)      # tempie
    ell(tmp_d, (0.0, 0.110, 0.048), (0.008, 0.010, 0.016), 8)                  # cavita' nasale
    for k in range(8):
        a = -0.62 + 1.24 * k / 7
        bx(tmp_b, V(sin(a) * 0.052, 0.098 + cos(a) * 0.012, 0.022), V(0.0085, 0.0075, 0.014), 0.001, 1, rot=Matrix.Rotation(-a, 3, 'Z'))
    jaw = [V(-0.055, 0.020, 0.030), V(-0.046, 0.078, 0.004), V(0.0, 0.106, -0.004), V(0.046, 0.078, 0.004), V(0.055, 0.020, 0.030)]
    tube(tmp_b, jaw, (0.0105, 0.0095, 0.0095, 0.0095, 0.0105), segs=8, n=18)
    for sx in (-1, 1):
        bx(tmp_b, V(sx * 0.057, 0.012, 0.050), V(0.012, 0.022, 0.050), 0.003, 1)  # rami della mandibola
    M = Matrix.Translation(Vector(c)) @ Matrix.Rotation(yaw, 4, 'Z') @ Matrix.Scale(s, 4)
    g3.merge(bone, tmp_b, M)
    g3.merge(dark, tmp_d, M)


# ----------------------------------------------------------------------------- sarcofago della cripta
def sarcofago():
    """Sarcofago di pietra: zoccolo modanato, cassa con pilastrini d'angolo, un'arcata cieca per ogni lato lungo (cornici di rilievo, colonnine, nicchie
    scure) e un medaglione con la croce sui lati corti, cimasa, coperchio a falde arrotondate spostato di lato in modo che si veda dentro: la
    cassa e' vuota, con il fondo scuro, un cranio, due femori, costole e vertebre sparse. 2,3 x 1,0 x 1,0 m."""
    A = Asset5('chi_sarcofago')
    st, dk, bone = A.bm('A_pietra'), A.bm('A_scuro'), A.bm('A_osso')
    hx, hy = 1.03, 0.435                 # filo esterno delle pareti
    zb, zt, t = 0.11, 0.745, 0.095        # base e bordo della cassa, spessore delle pareti
    h = zt - zb
    zc = (zb + zt) / 2
    stone(st, V(0, 0, 0.055), V(2 * hx + 0.10, 2 * hy + 0.10, 0.11), 0.012, 0.10, seed=2)
    sweep(st, rect_path(0, 0, zb, hx, hy), base_profile(0.05, 0.075), ref=V(0, 0, 1), closed=True)
    # pareti: due lunghe, due corte, tutte tra lo zoccolo e il bordo; il fondo e' una lastra scura
    stone(st, V(0, hy - t / 2, zc), V(2 * hx, t, h), 0.010, 0.09, seed=3)
    stone(st, V(0, -hy + t / 2, zc), V(2 * hx, t, h), 0.010, 0.09, seed=4)
    for sx in (-1, 1):
        stone(st, V(sx * (hx - t / 2), 0, zc), V(t, 2 * hy - 2 * t + 0.004, h), 0.010, 0.09, seed=5 + sx)
    bx(dk, V(0, 0, 0.49), V(2 * hx - 2 * t + 0.01, 2 * hy - 2 * t + 0.01, 0.02), 0.002, 1)     # fondo a meta' altezza: terra e ossa si vedono dall'alto
    # pilastrini d'angolo (nascondono gli attacchi delle pareti) e cimasa
    for sx in (-1, 1):
        for sy in (-1, 1):
            stone(st, V(sx * (hx - 0.045), sy * (hy - 0.045), zc), V(0.105, 0.105, h), 0.016, 0.07, seed=9 + sx + 3 * sy)
    sweep(st, rect_path(0, 0, zt - 0.075, hx, hy), crown_profile(0.045, 0.075), ref=V(0, 0, 1), closed=True)
    # arcata cieca sui lati lunghi: cornice di rilievo ad arco, nicchia scura, colonnine con base e capitello
    n_arch = 4
    pitch = (2 * hx - 0.34) / n_arch
    for sy in (-1, 1):
        def arcade(tb, tt, sy=sy):
            for k in range(n_arch):
                cx = -hx + 0.17 + pitch * (k + 0.5)
                outer = gothic_pts(cx, 0.225, pitch - 0.07, 0.40, n=7)
                inner = gothic_pts(cx, 0.255, pitch - 0.13, 0.34, n=7)
                ring_frame(tb, outer, inner, hy, hy + 0.024, 0.010)
                poly_prism(tt, inner, 'y', hy + 0.004, hy + 0.012)
            for k in range(n_arch + 1):
                x = -hx + 0.17 + pitch * k
                col = [(0.0, 0.0), (0.030, 0.0), (0.032, 0.010), (0.021, 0.018), (0.017, 0.030), (0.017, 0.300), (0.022, 0.314), (0.034, 0.326), (0.0, 0.326)]
                part(tb, lambda m: lathe(m, col, c=V(0, 0, 0), segs=12), Matrix.Translation(V(x, hy + 0.030, 0.226)))
            bx(tb, V(0, hy + 0.008, 0.665), V(2 * hx - 0.30, 0.016, 0.026), 0.004, 1)       # cordolo sopra l'arcata
        if sy > 0:
            arcade(st, dk)
        else:
            tmp_s, tmp_d = bmesh.new(), bmesh.new()
            arcade(tmp_s, tmp_d)
            M = Matrix.Scale(-1, 4, V(0, 1, 0))
            g3.merge(st, tmp_s, M)
            g3.merge(dk, tmp_d, M)
    # lati corti: medaglione con una croce
    for sx in (-1, 1):
        tmp_s, tmp_d = bmesh.new(), bmesh.new()
        bm_r = bmesh.new()
        torus(tmp_s, V(0, 0, 0), 0.165, 0.014, 28, 8)
        # il torus e' orizzontale: lo si porta nel piano YZ
        M_ring = Matrix.Translation(V(sx * hx, 0, 0.44)) @ Matrix.Rotation(radians(90), 4, 'Y')
        g3.merge(st, tmp_s, M_ring)
        for y, z, w, hh in ((0.0, 0.44, 0.030, 0.230), (0.0, 0.475, 0.150, 0.030)):
            bx(st, V(sx * (hx + 0.008), y, z), V(0.016, w, hh), 0.004, 1)
        lathe(dk, [(0.0, 0.0), (0.150, 0.0), (0.150, 0.004), (0.0, 0.004)], c=V(sx * (hx + 0.001), 0, 0.44), segs=24, axis='X')
    # coperchio a falde arrotondate, spostato verso un'estremita' (si vede il vano a sinistra)
    x0, x1 = -0.30, 1.07
    roof = [(0.0, 0.985), (0.14, 0.968), (0.27, 0.925), (0.38, 0.860), (0.45, 0.800), (0.485, 0.775)]
    lid = [(-0.505, 0.752), (0.505, 0.752)] + [(y, z) for y, z in reversed(roof)] + [(-y, z) for y, z in roof[1:]]
    prism_bevel(st, [(y, z) for y, z in lid], 'x', x0, x1, 0.008, 2)
    # il solido a falde e' sottile in altezza: lo si ispessisce con un secondo corpo piu' basso e largo (gola sotto la gronda)
    stone(st, V((x0 + x1) / 2, 0, 0.7615), V(x1 - x0, 2 * hy + 0.07, 0.033), 0.010, 0.09, seed=21)
    # sul colmo: un nastro di pietra e una croce a rilievo
    xm = (x0 + x1) / 2
    bx(st, V(xm, 0, 0.984), V(x1 - x0 - 0.16, 0.085, 0.012), 0.004, 1)
    bx(st, V(xm, 0, 0.994), V(0.52, 0.042, 0.016), 0.004, 1)
    bx(st, V(xm + 0.04, 0, 0.994), V(0.044, 0.230, 0.016), 0.004, 1)
    # contenuto: fondo scuro, cranio vicino all'apertura, due femori, costole e vertebre
    zf = 0.50
    skull(bone, dk, V(-0.64, 0.06, zf + 0.002), yaw=radians(215), s=1.0)
    long_bone(bone, V(-0.86, -0.22, zf + 0.017), V(-0.50, 0.02, zf + 0.017), 0.017)
    long_bone(bone, V(-0.82, -0.02, zf + 0.040), V(-0.46, -0.27, zf + 0.017), 0.016)
    for k, (x, y, ang) in enumerate(((-0.74, -0.08, 0.3), (-0.58, -0.12, 0.9), (-0.84, 0.24, 2.0), (-0.52, 0.22, 1.2), (-0.46, 0.10, 0.2))):
        rib(bone, V(x, y, zf + 0.008), 0.085, ang, 0.0068, 2.1)
    for k in range(5):
        sphere(bone, V(-0.90 + k * 0.012, 0.28 - k * 0.035, zf + 0.012), 0.0145, 8)
    return A


# ----------------------------------------------------------------------------- inginocchiatoio
def inginocchiatoio():
    """Inginocchiatoio di quercia scura: zoccolo modanato, pedana con il cuscino imbottito a filetto e borchie, fianchi a profilo curvo, schiena con
    arco a sesto acuto e croce d'ottone, leggio inclinato con il bordo d'appoggio e un libro aperto. 0,56 x 0,46 x 0,96 m."""
    A = Asset5('chi_inginocchiatoio')
    w, cl, br, lea, pg = A.bm('A_legno'), A.bm('A_tessuto'), A.bm('A_ottone'), A.bm('A_cuoio'), A.bm('A_carta')
    sweep(w, rect_path(0, 0.0, 0.0, 0.26, 0.205), base_profile(0.02, 0.07), ref=V(0, 0, 1), closed=True)
    bx(w, V(0, 0.0, 0.035), V(0.50, 0.38, 0.07), 0.006, 2)
    # fianchi: profilo (y, z) dal basso davanti alla schiena alta; poi il bordo smussato
    key = [(0.21, 0.07), (0.21, 0.20), (0.17, 0.30), (0.08, 0.40), (0.02, 0.58), (-0.04, 0.80), (-0.10, 0.93), (-0.17, 0.955), (-0.21, 0.90)]
    outline = [(p.x, p.y) for p in spline([V(a, b, 0) for a, b in key], 4)] + [(-0.21, 0.07)]
    for sx in (-1, 1):
        a, b = sorted((sx * 0.262, sx * 0.226))
        prism_bevel(w, outline, 'x', a, b, 0.004, 1)
    # pedana, cuscino imbottito e pannello frontale
    bx(w, V(0, 0.115, 0.145), V(0.45, 0.20, 0.145), 0.006, 2)
    L.panel_field(w, -0.19, 0.19, 0.10, 0.205, 0.216, 0.005, +1, mould=0.020)
    cushion(cl, V(0, 0.118, 0.250), V(0.43, 0.185, 0.050), 0.018, nx=14, ny=8, nz=3, crown=0.012, sag=0.003, wrinkle=0.0008, seed=6)
    piping(cl, rect_loop(0, 0.118, 0.2725, 0.2075, 0.0925, 0.018), 0.0035, 6)
    stud_row(br, V(-0.15, 0.2125, 0.246), V(0.15, 0.2125, 0.246), 0.050, 0.0033, 'y', +1)
    # schiena: tavola alta con l'arco a sesto acuto in rilievo e la croce d'ottone
    bx(w, V(0, -0.185, 0.51), V(0.45, 0.040, 0.88), 0.005, 2)
    outer = gothic_pts(0, 0.20, 0.36, 0.64, n=8)
    inner = gothic_pts(0, 0.22, 0.32, 0.60, n=8)
    ring_frame(w, outer, inner, -0.165, -0.150, 0.006)
    prism_bevel(w, gothic_pts(0, 0.22, 0.32, 0.60, n=8), 'y', -0.1665, -0.158, 0.003, 1)
    bx(br, V(0, -0.1555, 0.68), V(0.016, 0.004, 0.115), 0.001, 1)
    bx(br, V(0, -0.1555, 0.705), V(0.066, 0.004, 0.016), 0.001, 1)
    # leggio inclinato con labbro d'appoggio e libro aperto
    tilt = Matrix.Translation(V(0, -0.02, 0.775)) @ Matrix.Rotation(radians(-16), 4, 'X')
    part(w, lambda m: bx(m, V(0, 0, 0), V(0.48, 0.25, 0.026), 0.005, 2), tilt)
    part(w, lambda m: bx(m, V(0, 0.118, 0.022), V(0.48, 0.022, 0.030), 0.005, 2), tilt)
    part(lea, lambda m: bx(m, V(0, 0.004, 0.0215), V(0.30, 0.19, 0.012), 0.003, 2), tilt)
    part(pg, lambda m: (bx(m, V(-0.073, 0.004, 0.0300), V(0.140, 0.178, 0.0055), 0.001, 1, rot=Matrix.Rotation(radians(2), 3, 'X')),
                        bx(m, V(0.073, 0.004, 0.0300), V(0.140, 0.178, 0.0055), 0.001, 1, rot=Matrix.Rotation(radians(-2), 3, 'X'))), tilt)
    part(br, lambda m: bx(m, V(0, 0.10, 0.040), V(0.30, 0.006, 0.006), 0.001, 1), tilt)
    # fascia sotto il leggio e mensole
    bx(w, V(0, -0.02, 0.715), V(0.45, 0.20, 0.034), 0.004, 1)
    return A


# ----------------------------------------------------------------------------- statua del monaco
def cowl(bm, bm_in, outer, inner, c, a0, a1, n=26):
    """Cappuccio: guscio di rivoluzione con spessore, aperto sul davanti (settore da a0 ad a1 radianti, 90 gradi = +Y): il foglio esterno e i bordi
    dell'apertura vanno in bm, il foglio interno (rivolto verso l'asse, con l'avvolgimento gia' giusto) in bm_in. Profili outer e inner = liste
    (raggio, quota) con lo stesso numero di righe; un raggio nullo e' il vertice in cima."""
    def sheet(dst, prof):
        rows = []
        for r, z in prof:
            if r < 1e-6:
                rows.append([dst.verts.new(V(c[0], c[1], c[2] + z))] * (n + 1))
            else:
                rows.append([dst.verts.new(V(c[0] + cos(a0 + (a1 - a0) * k / n) * r, c[1] + sin(a0 + (a1 - a0) * k / n) * r, c[2] + z)) for k in range(n + 1)])
        return rows

    def quad(dst, vs, flip=False):
        uniq = []
        for v in vs:
            if v not in uniq:
                uniq.append(v)
        if len(uniq) >= 3:
            dst.faces.new(uniq[::-1] if flip else uniq)
    ro, ri = sheet(bm, outer), sheet(bm_in, inner)
    for i in range(len(ro) - 1):
        for k in range(n):
            quad(bm, [ro[i][k], ro[i][k + 1], ro[i + 1][k + 1], ro[i + 1][k]])
            quad(bm_in, [ri[i][k], ri[i][k + 1], ri[i + 1][k + 1], ri[i + 1][k]], True)
    # bordi dell'apertura e del bordo di sotto: strisce di pietra tra il foglio esterno e quello interno
    rim = {}

    def rv(i, k):
        k = 0 if ri[i][0] is ri[i][n] else k                   # in cima i due bordi si incontrano nello stesso vertice
        if (i, k) not in rim:
            rim[(i, k)] = bm.verts.new(ri[i][k].co.copy())
        return rim[(i, k)]
    for k in (0, n):
        for i in range(len(ro) - 1):
            quad(bm, [ro[i][k], ro[i + 1][k], rv(i + 1, k), rv(i, k)], k == 0)
    for k in range(n):
        quad(bm, [ro[0][k], ro[0][k + 1], rv(0, k + 1), rv(0, k)], True)


def statua():
    """Statua del monaco incappucciato: piedistallo di pietra con zoccolo e cornice modanati e la targa scura, tonaca con le pieghe a canne d'organo
    (lathe con il raggio modulato), maniche larghe e mani giunte sul cordone, cappuccio aperto sul davanti che lascia un vuoto scuro con l'unico
    occhio d'oro. 0,74 x 0,74 x 2,1 m."""
    A = Asset5('chi_statua')
    A.OPEN = {'A_foglia', 'A_scuro'}        # il foglio interno del cappuccio e' un guscio aperto con l'avvolgimento gia' rivolto verso l'asse
    st, gold, dk = A.bm('A_pietra'), A.bm('A_oro'), A.bm('A_scuro')
    # piedistallo
    stone(st, V(0, 0, 0.05), V(0.74, 0.74, 0.10), 0.010, 0.10, seed=31)
    sweep(st, rect_path(0, 0, 0.10, 0.30, 0.30), base_profile(0.04, 0.09), ref=V(0, 0, 1), closed=True)
    stone(st, V(0, 0, 0.33), V(0.60, 0.60, 0.46), 0.008, 0.10, seed=32)
    sweep(st, rect_path(0, 0, 0.555, 0.30, 0.30), crown_profile(0.05, 0.085), ref=V(0, 0, 1), closed=True)
    stone(st, V(0, 0, 0.66), V(0.70, 0.70, 0.04), 0.008, 0.10, seed=33)
    bx(dk, V(0, 0.303, 0.33), V(0.30, 0.008, 0.17), 0.002, 1)               # targa
    bx(gold, V(0, 0.3085, 0.33), V(0.20, 0.003, 0.012), 0.0005, 1)
    bx(gold, V(0, 0.3085, 0.295), V(0.15, 0.003, 0.008), 0.0005, 1)
    # tonaca: raggio che si allarga all'orlo, strozza in vita, spalle larghe; pieghe che si aprono verso il basso
    z0 = 0.68
    key = [V(0.318, z0, 0), V(0.305, 0.74, 0), V(0.282, 0.88, 0), V(0.256, 1.02, 0), V(0.234, 1.16, 0), V(0.208, 1.30, 0), V(0.198, 1.40, 0),
           V(0.210, 1.48, 0), V(0.232, 1.55, 0), V(0.236, 1.595, 0), V(0.205, 1.640, 0), V(0.140, 1.662, 0)]
    prof = [(p.x, p.y) for p in spline(key, 4)]

    def folds(r, z, a):
        u = max(0.0, min(1.0, (z - z0) / 0.95))
        amp = 0.105 * (1 - u) ** 1.3 + 0.014
        w = 0.55 * cos(6 * a + 1.4 * sin(2.1 * z) + 0.4) + 0.45 * cos(10 * a + 0.9 + 1.8 * z)
        return r * (1 + amp * w)
    lathe_fn(st, prof, folds, c=V(0, 0, 0), segs=56, closed_top=True, closed_bottom=True)
    # piedi che escono dall'orlo
    for sx in (-1, 1):
        ell(st, (sx * 0.085, 0.292, z0 + 0.024), (0.052, 0.092, 0.027), 10)
    # maniche larghe e mani giunte sul petto
    for sx in (-1, 1):
        pts = [V(sx * 0.214, 0.0, 1.585), V(sx * 0.262, 0.06, 1.420), V(sx * 0.178, 0.170, 1.380), V(sx * 0.078, 0.228, 1.440)]
        tube(st, pts, (0.053, 0.053, 0.070, 0.064), segs=14, n=22)
        ell(st, (sx * 0.024, 0.245, 1.462), (0.036, 0.064, 0.028), 10, rot=Matrix.Rotation(sx * radians(10), 3, 'Z'))
    # cordone in vita con il nodo e le due code
    torus(st, V(0, 0, 1.305), 0.205, 0.0125, 40, 6)
    sphere(st, V(0.0, 0.212, 1.305), 0.024, 10)
    tube(st, [V(0.012, 0.216, 1.298), V(0.030, 0.238, 1.15), V(0.025, 0.247, 0.99)], (0.012, 0.011, 0.009), segs=8, n=14)
    tube(st, [V(-0.012, 0.216, 1.298), V(-0.036, 0.236, 1.20), V(-0.058, 0.240, 1.06)], (0.012, 0.011, 0.009), segs=8, n=14)
    # cappuccio aperto sul davanti: fuori pietra, dentro il foglio scuro (il vuoto), e nel vuoto l'occhio d'oro
    outer = [(0.224, 1.58), (0.243, 1.68), (0.247, 1.78), (0.233, 1.88), (0.199, 1.97), (0.139, 2.04), (0.066, 2.088), (0.0, 2.108)]
    inner = [(0.181, 1.58), (0.202, 1.68), (0.206, 1.78), (0.193, 1.88), (0.162, 1.97), (0.110, 2.04), (0.050, 2.075), (0.0, 2.091)]
    po = [(p.x, p.y) for p in spline([V(r, z, 0) for r, z in outer], 3)]
    pi_ = [(p.x, p.y) for p in spline([V(r, z, 0) for r, z in inner], 3)]
    cowl(st, dk, po, pi_, V(0, -0.01, 0), radians(90 + 46), radians(450 - 46), 28)
    ell(gold, (0.0, 0.020, 1.83), (0.042, 0.042, 0.042), 14)
    ell(dk, (0.0, 0.058, 1.83), (0.018, 0.012, 0.018), 8)
    part(gold, lambda t: torus(t, V(0, 0, 0), 0.052, 0.0070, 24, 6), Matrix.Translation(V(0, 0.030, 1.83)) @ Matrix.Rotation(radians(90), 4, 'X'))
    weather(st, 0.0008, 9.0, seed=5)
    return A


ASSETS = {'chi_sarcofago': sarcofago, 'chi_inginocchiatoio': inginocchiatoio, 'chi_statua': statua}
VIEWS = {
    'chi_sarcofago': ((-3.0, 3.1, 2.4), (-0.35, 0, 0.45), 40),
    'chi_inginocchiatoio': ((1.2, 1.5, 1.2), (0, 0, 0.45), 40),
    'chi_statua': ((1.9, 2.5, 2.2), (0, 0, 1.15), 40),
}


if __name__ == '__main__':
    ga.ASSETS.update(ASSETS)
    ga.VIEWS.update(VIEWS)
    ov = os.environ.get('ARREDI_VIEW')          # "x,y,z,tx,ty,tz,focale": fotografa da un'altra parte
    if ov:
        v = [float(t) for t in ov.split(',')]
        for nm in ASSETS:
            ga.VIEWS[nm] = ((v[0], v[1], v[2]), (v[3], v[4], v[5]), v[6])
    ga.OUT = OUT
    if os.environ.get('ARREDI_GPU') != '1':
        ga.setup_cycles = g3.setup_cpu
    names = [a for a in ga.ARGS if a in ASSETS] or list(ASSETS)
    for nm in names:
        try:
            if 'build' in ga.ARGS:
                Asset5.WITH_LODS, Asset5.SHOW_LOD = os.environ.get('ARREDI_NOLOD') != '1', 0
                ga.stage([nm], 'build')
            if 'render' in ga.ARGS:
                Asset5.WITH_LODS, Asset5.SHOW_LOD = False, int(os.environ.get('ARREDI_LOD', '0'))
                ga.stage([nm], 'render')
        except Exception as e:
            import traceback
            print(f'[ERRORE] {nm}: {e}')
            traceback.print_exc()
