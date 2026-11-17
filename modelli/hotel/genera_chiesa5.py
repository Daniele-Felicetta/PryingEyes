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
                         cushion, piping, rect_loop, stud_row, knob, Asset5, lathe_fn, ell, mould_bead, frame_rect, panel_field, stud)
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


# ----------------------------------------------------------------------------- altare
def shell(bm, pos, ni, nj, th, nrm):
    """Lastra sottile chiusa su una griglia (tessuti appesi con lo spessore vero e i bordi chiusi): pos(i, j) = punto della faccia esterna
    (i da 0 a ni, j da 0 a nj), nrm(i, j) = normale uscente; la faccia interna e' spostata di th all'indietro."""
    out = [[bm.verts.new(pos(i, j)) for j in range(nj + 1)] for i in range(ni + 1)]
    inn = [[bm.verts.new(pos(i, j) - nrm(i, j) * th) for j in range(nj + 1)] for i in range(ni + 1)]
    for i in range(ni):
        for j in range(nj):
            bm.faces.new((out[i][j], out[i + 1][j], out[i + 1][j + 1], out[i][j + 1]))
            bm.faces.new((inn[i][j], inn[i][j + 1], inn[i + 1][j + 1], inn[i + 1][j]))
        bm.faces.new((out[i][0], inn[i][0], inn[i + 1][0], out[i + 1][0]))
        bm.faces.new((out[i][nj], out[i + 1][nj], inn[i + 1][nj], inn[i][nj]))
    for j in range(nj):
        bm.faces.new((out[0][j], out[0][j + 1], inn[0][j + 1], inn[0][j]))
        bm.faces.new((out[ni][j], inn[ni][j], inn[ni][j + 1], out[ni][j + 1]))


def altare():
    """Altare di pietra: zoccolo e base modanata, corpo di conci con un'arcata cieca sul davanti (cinque arcate a sesto acuto, colonnine, nicchie
    scure, e nella centrale l'Occhio d'oro a medaglione con i raggi), cimasa, mensa con le croci di consacrazione, un drappo di velluto steso sulla
    mensa che pende dai due lati corti con la bordura e le frange d'oro, e sopra messale su cuscino, calice con patena e ostensorio a raggi con
    l'Occhio. 2,4 x 1,0 x 1,05 m (i raggi dell'ostensorio arrivano a 1,35 m)."""
    A = Asset5('chi_altare')
    st, dk, gold, cl = A.bm('A_pietra'), A.bm('A_scuro'), A.bm('A_oro'), A.bm('A_tessuto')
    lea, pg = A.bm('A_cuoio'), A.bm('A_carta')
    yf = 0.38                                   # filo del fronte del corpo
    stone(st, V(0, 0, 0.07), V(2.34, 0.94, 0.14), 0.012, 0.12, seed=41)
    sweep(st, rect_path(0, 0, 0.14, 1.02, yf), base_profile(0.05, 0.08), ref=V(0, 0, 1), closed=True)
    stone(st, V(0, 0, 0.50), V(2.04, 2 * yf, 0.72), 0.010, 0.12, seed=42)
    sweep(st, rect_path(0, 0, 0.795, 1.02, yf), crown_profile(0.06, 0.065), ref=V(0, 0, 1), closed=True)
    stone(st, V(0, 0, 0.93), V(2.40, 1.00, 0.14), 0.014, 0.12, seed=43)
    # arcata cieca: cornici di rilievo a sesto acuto con la nicchia scura, colonnine tra le arcate, cordolo sopra
    for cx, w, h in ((-0.775, 0.25, 0.50), (-0.46, 0.25, 0.50), (0.0, 0.53, 0.52), (0.46, 0.25, 0.50), (0.775, 0.25, 0.50)):
        ring_frame(st, gothic_pts(cx, 0.20, w, h, n=7), gothic_pts(cx, 0.235, w - 0.07, h - 0.06, n=7), yf, yf + 0.022, 0.009)
        poly_prism(dk, gothic_pts(cx, 0.235, w - 0.07, h - 0.06, n=7), 'y', yf + 0.003, yf + 0.011)
    col = [(0.0, 0.0), (0.030, 0.0), (0.032, 0.010), (0.021, 0.018), (0.017, 0.030), (0.017, 0.374), (0.022, 0.388), (0.034, 0.400), (0.0, 0.400)]
    for x in (-0.93, -0.62, -0.30, 0.30, 0.62, 0.93):
        part(st, lambda m: lathe(m, col, c=V(0, 0, 0), segs=10), Matrix.Translation(V(x, yf + 0.030, 0.215)))
    bx(st, V(0, yf + 0.008, 0.745), V(1.90, 0.016, 0.026), 0.004, 1)
    # l'Occhio nella nicchia centrale: anello, palpebre, iride, pupilla e sedici raggi
    ey, ez = yf + 0.018, 0.455
    part(gold, lambda t: torus(t, V(0, 0, 0), 0.118, 0.0105, 32, 6), Matrix.Translation(V(0, ey, ez)) @ Matrix.Rotation(radians(90), 4, 'X'))
    for s in (-1, 1):
        tube(gold, [V(0.082 * cos(pi * i / 14), ey, ez + s * 0.036 * sin(pi * i / 14)) for i in range(15)], (0.0055, 0.0055), segs=6, n=16)
    lathe(gold, [(0.0, 0.0), (0.030, 0.0), (0.032, 0.004), (0.0, 0.008)], c=V(0, ey - 0.002, ez), segs=20, axis='Y')
    sphere(dk, V(0, ey + 0.010, ez), 0.0135, 8)
    for k in range(16):
        th = 2 * pi * k / 16
        rl = 0.050 if k % 2 == 0 else 0.030
        rm = 0.140 + rl / 2
        bx(gold, V(sin(th) * rm, ey, ez + cos(th) * rm), V(0.009, 0.006, rl), 0.0, 1, rot=Matrix.Rotation(th, 3, 'Y'))
    # croci di consacrazione incise agli angoli della mensa
    for sx in (-1, 1):
        for sy in (-1, 1):
            bx(gold, V(sx * 1.0, sy * 0.40, 1.0015), V(0.060, 0.010, 0.003), 0.0, 1)
            bx(gold, V(sx * 1.0, sy * 0.40, 1.0015), V(0.010, 0.060, 0.003), 0.0, 1)
    # drappo di velluto sulla mensa, con i due lembi che pendono dai lati corti (pieghe, bordura e frange d'oro)
    rnd = random.Random(7)
    bx(cl, V(0, 0, 1.003), V(2.412, 0.62, 0.006), 0.003, 2)

    def cloth_pos(sx, ph, s, t, lift=0.0):
        fold = 0.60 * sin(2 * pi * 4.5 * s + ph[0]) + 0.40 * sin(2 * pi * 9.7 * s + ph[1])
        return V(sx * (1.209 + 0.004 * t + 0.022 * fold * (0.25 + 0.75 * t) ** 1.2 + lift), -0.31 + 0.62 * s,
                 1.0 - 0.50 * t * (1.0 + 0.03 * sin(2 * pi * 3 * s + ph[0])))
    ni, nj = 22, 8
    for sx in (-1, 1):
        ph = (rnd.uniform(0, 6.28), rnd.uniform(0, 6.28))
        nrm = lambda i, j, sx=sx: V(sx, 0, 0)
        shell(cl, lambda i, j: cloth_pos(sx, ph, i / ni, j / nj), ni, nj, 0.006, nrm)
        shell(gold, lambda i, j: cloth_pos(sx, ph, i / ni, 0.86 + 0.10 * j, 0.0025), ni, 1, 0.003, nrm)
        for i in range(ni + 1):
            p = cloth_pos(sx, ph, i / ni, 1.0)
            cyl(gold, V(p.x, p.y, p.z + 0.004), V(p.x, p.y, p.z - rnd.uniform(0.040, 0.062)), 0.0026, 4)
    # messale aperto su un cuscino, calice e patena, ostensorio a raggi con l'Occhio
    cushion(cl, V(-0.52, 0.08, 1.033), V(0.36, 0.26, 0.054), 0.02, nx=10, ny=8, nz=3, crown=0.010, sag=0.0, wrinkle=0.0008, seed=3)
    bx(lea, V(-0.52, 0.08, 1.0635), V(0.31, 0.22, 0.010), 0.003, 2)
    for sg in (-1, 1):
        bx(pg, V(-0.52 + sg * 0.072, 0.08, 1.0725), V(0.140, 0.200, 0.0075), 0.001, 1, rot=Matrix.Rotation(sg * radians(-2.5), 3, 'Y'))
    lathe(gold, [(0.0, 0.0), (0.040, 0.0), (0.042, 0.004), (0.036, 0.010), (0.014, 0.020), (0.011, 0.030), (0.020, 0.042), (0.012, 0.052), (0.011, 0.075),
                 (0.016, 0.082), (0.030, 0.098), (0.036, 0.125), (0.0375, 0.138), (0.0355, 0.140), (0.034, 0.134), (0.026, 0.106), (0.0, 0.100)],
          c=V(0.52, 0.04, 1.0055), segs=22)
    lathe(gold, [(0.0, 0.0), (0.050, 0.0), (0.057, 0.003), (0.0575, 0.0055), (0.0535, 0.0045), (0.040, 0.0025), (0.0, 0.0025)], c=V(0.50, 0.22, 1.0055), segs=24)
    mx, my, mz = 0.0, -0.27, 1.0055
    lathe(gold, [(0.0, 0.0), (0.075, 0.0), (0.080, 0.006), (0.066, 0.016), (0.040, 0.026), (0.020, 0.036), (0.014, 0.052), (0.028, 0.064), (0.0145, 0.078),
                 (0.0125, 0.150), (0.026, 0.160), (0.014, 0.170), (0.0, 0.172)], c=V(mx, my, mz), segs=22)
    zl = mz + 0.225
    part(gold, lambda t: torus(t, V(0, 0, 0), 0.052, 0.0065, 26, 6), Matrix.Translation(V(mx, my, zl)) @ Matrix.Rotation(radians(90), 4, 'X'))
    lathe(dk, [(0.0, 0.0), (0.048, 0.0), (0.048, 0.004), (0.0, 0.004)], c=V(mx, my - 0.002, zl), segs=24, axis='Y')
    for s in (-1, 1):
        tube(gold, [V(mx + 0.040 * cos(pi * i / 10), my + 0.004, zl + s * 0.016 * sin(pi * i / 10)) for i in range(11)], (0.0035, 0.0035), segs=5, n=12)
    lathe(gold, [(0.0, 0.0), (0.0145, 0.0), (0.0155, 0.003), (0.0, 0.006)], c=V(mx, my + 0.002, zl), segs=16, axis='Y')
    sphere(dk, V(mx, my + 0.0095, zl), 0.0065, 8)
    for k in range(20):
        th = 2 * pi * k / 20
        rl = 0.060 if k % 2 == 0 else 0.034
        rm = 0.062 + rl / 2
        bx(gold, V(mx + sin(th) * rm, my, zl + cos(th) * rm), V(0.008, 0.005, rl), 0.0, 1, rot=Matrix.Rotation(th, 3, 'Y'))
    return A


# ----------------------------------------------------------------------------- fonte battesimale
def _oct(ap):
    """Ottagono regolare di apotema ap con un lato piano rivolto a +Y: lista di (x, y) in senso antiorario."""
    R = ap / cos(pi / 8)
    return [(R * cos(pi / 8 + k * pi / 4), R * sin(pi / 8 + k * pi / 4)) for k in range(8)]


def _oct_ring(ap, z):
    return [V(x, y, z) for x, y in _oct(ap)]


def _quatrefoil(n=16, d=0.030, a=0.036):
    """Contorno di un quadrilobo (n punti antiorari, centrato nell'origine): quattro cerchi di raggio a a distanza d dal centro, uniti da cuspidi."""
    pts = []
    for k in range(n):
        th = 2 * pi * k / n
        dl = th - round(th / (pi / 2)) * (pi / 2)
        r = d * cos(dl) + sqrt(max(0.0, a * a - (d * sin(dl)) ** 2))
        pts.append((r * cos(th), r * sin(th)))
    return pts


def fonte():
    """Fonte battesimale ottagonale: zoccolo a gradini con la base modanata, fusto con otto colonnine e una fascia, capitello, vasca di pietra con
    il bordo e otto quadrilobi in rilievo, l'acqua nera, e il coperchio di quercia a piramide ottagonale (nervature agli spigoli, fascia e razze
    d'ottone, pomo) cerniera sul retro e tenuto aperto di 30 gradi da un'asta d'ottone. 0,9 x 0,9 x 1,1 m (il coperchio aperto arriva a 1,45 m)."""
    A = Asset5('chi_fonte')
    A.KEEP_LOD = {'A_lacca'}                # l'acqua (28 triangoli) non deve sparire ai livelli lontani: resterebbe il fondo di pietra
    A.LODS = ((1, 0.30), (2, 0.16))         # le colonnine sottili reggono meno la decimazione: il livello 2 tiene il 16 % invece del 9 %
    st, wd, br, dk, lq = A.bm('A_pietra'), A.bm('A_legno'), A.bm('A_ottone'), A.bm('A_scuro'), A.bm('A_lacca')
    prism_bevel(st, _oct(0.45), 'z', 0.0, 0.085, 0.007, 1)
    prism_bevel(st, _oct(0.40), 'z', 0.085, 0.165, 0.007, 1)
    sweep(st, [V(x, y, 0.165) for x, y in _oct(0.34)], base_profile(0.045, 0.075), ref=V(0, 0, 1), closed=True)
    prism_bevel(st, _oct(0.115), 'z', 0.165, 0.60, 0.004, 1)
    sweep(st, [V(x, y, 0.38) for x, y in _oct(0.117)], rr_profile(0.045, 0.034, 0.012, 2), ref=V(0, 0, 1), closed=True)
    colf = [(0.0, 0.0), (0.030, 0.0), (0.032, 0.010), (0.022, 0.020), (0.0175, 0.034), (0.0175, 0.33), (0.022, 0.346), (0.034, 0.358), (0.0, 0.358)]
    for k in range(8):
        a = k * pi / 4
        part(st, lambda m: lathe(m, colf, c=V(0, 0, 0), segs=10), Matrix.Translation(V(cos(a) * 0.152, sin(a) * 0.152, 0.245)))
    prism_bevel(st, _oct(0.205), 'z', 0.598, 0.680, 0.006, 1)
    # vasca: sezioni ottagonali (apotema, quota) dal collo al bordo e giu' fino al pelo dell'acqua
    prof = [(0.190, 0.675), (0.215, 0.695), (0.265, 0.725), (0.330, 0.752), (0.388, 0.775), (0.409, 0.795), (0.409, 0.935), (0.430, 0.945), (0.430, 0.957),
            (0.395, 0.962), (0.382, 0.945), (0.372, 0.905)]
    g4.loft(st, [_oct_ring(ap, z) for ap, z in prof], True, True)
    poly_prism(lq, _oct(0.371), 'z', 0.903, 0.907)
    # otto quadrilobi in rilievo sulla fascia verticale della vasca (costruiti sul lato +Y e ruotati)
    qf = _quatrefoil(16)
    ap, zc = 0.409, 0.865

    def rel_s(t):
        ring_frame(t, [(x, zc + y) for x, y in qf], [(0.60 * x, zc + 0.60 * y) for x, y in qf], ap, ap + 0.016, 0.006)

    def rel_d(t):
        poly_prism(t, [(0.60 * x, zc + 0.60 * y) for x, y in qf], 'y', ap + 0.0015, ap + 0.006)
    for k in range(8):
        part(st, rel_s, Matrix.Rotation(k * pi / 4, 4, 'Z'))
        part(dk, rel_d, Matrix.Rotation(k * pi / 4, 4, 'Z'))
    # coperchio: piramide ottagonale concava di quercia, nervature agli otto spigoli, fascia d'ottone alla gronda, razze sulle facce e pomo
    hyy, zh = -0.452, 0.962
    Mlid = Matrix.Translation(V(0, hyy, zh)) @ Matrix.Rotation(radians(30), 4, 'X') @ Matrix.Translation(V(0, -hyy, -zh))
    secs = [(0.438, 0.957), (0.438, 0.990), (0.412, 1.004), (0.330, 1.045), (0.215, 1.105), (0.115, 1.152), (0.055, 1.182), (0.040, 1.192), (0.040, 1.200)]

    def lid_wood(t):
        g4.loft(t, [_oct_ring(ap_, z) for ap_, z in secs], True, True)
        for k in range(8):
            a = pi / 8 + k * pi / 4
            pts = [V((ap_ / cos(pi / 8) + 0.004) * cos(a), (ap_ / cos(pi / 8) + 0.004) * sin(a), z + 0.002) for ap_, z in secs[1:-2]]
            tube(t, pts, (0.0105, 0.0095, 0.0080, 0.0065, 0.0050, 0.0040), segs=5, n=9)

    def lid_brass(t):
        ring = []
        for k in range(8):
            ring.append(V(0.444 / cos(pi / 8) * cos(pi / 8 + k * pi / 4), 0.444 / cos(pi / 8) * sin(pi / 8 + k * pi / 4), 0.972))
            ring.append(V(0.444 * cos((k + 1) * pi / 4), 0.444 * sin((k + 1) * pi / 4), 0.972))
        g4.loop_tube(t, ring, 0.012, V(0, 0, 1), segs=6, r_in=0.0035)
        for k in range(8):
            d = V(cos(k * pi / 4), sin(k * pi / 4), 0)
            cyl(t, d * 0.409 + V(0, 0, 1.0075), d * 0.080 + V(0, 0, 1.172), 0.0042, 5)
        lathe(t, [(0.0, 1.198), (0.032, 1.198), (0.034, 1.204), (0.020, 1.212), (0.013, 1.220), (0.0, 1.220)], c=V(0, 0, 0), segs=14)
        sphere(t, V(0, 0, 1.246), 0.026, 12)
        lathe(t, [(0.0, 1.268), (0.011, 1.268), (0.0, 1.292)], c=V(0, 0, 0), segs=8)
        for x in (-0.20, 0.20):
            bx(t, V(x, -0.4405, 0.975), V(0.085, 0.004, 0.05), 0.0008, 1)
    part(wd, lid_wood, Mlid)
    part(br, lid_brass, Mlid)
    # cerniere sul retro e asta che tiene aperto il coperchio
    for x in (-0.20, 0.20):
        cyl(br, V(x - 0.05, hyy, zh), V(x + 0.05, hyy, zh), 0.0095, 10)
        bx(br, V(x, -0.428, 0.9645), V(0.085, 0.05, 0.004), 0.0008, 1)
    p_lid = Mlid @ V(-0.12, 0.10, 0.957)
    p_rim = V(-0.12, 0.412, 0.9655)
    cyl(br, p_rim, p_lid, 0.0065, 8)
    sphere(br, p_rim, 0.0105, 8)
    sphere(br, p_lid, 0.0105, 8)
    return A


# ----------------------------------------------------------------------------- stallo del coro
def stallo():
    """Stallo del coro a tre posti: quattro fianchi di quercia (lastra unica con il bracciolo che sale in una curva concava fino al montante alto,
    pomo a testa tonda e pannelli sulle facce), basamento con i pannelli sul fronte, tre sedili, schienale a pannelli con due nicchie a sesto acuto
    e una colonnina per posto, trave di fregio, baldacchino con la balza di piccoli archi acuti, cimasa, cresta con pinnacoli e una fascia d'oro.
    2,8 x 0,9 x 2,3 m."""
    A = Asset5('chi_stallo')
    w, gold, dk = A.bm('A_legno'), A.bm('A_oro'), A.bm('A_scuro')
    xs = [-1.35, -0.45, 0.45, 1.35]
    sweep_pts = [(p.x, p.y) for p in spline([V(0.28, 0.80, 0), V(0.20, 0.775, 0), V(0.10, 0.790, 0), V(0.02, 0.850, 0), V(-0.06, 0.990, 0), V(-0.11, 1.230, 0),
                                              V(-0.14, 1.560, 0), V(-0.15, 1.92, 0)], 4)]
    outline = [(0.40, 0.0), (0.40, 0.70), (0.425, 0.74), (0.435, 0.79), (0.415, 0.835), (0.365, 0.848), (0.32, 0.828)] + sweep_pts + [(-0.45, 1.92), (-0.45, 0.0)]
    for x in xs:
        prism_bevel(w, outline, 'x', x - 0.05, x + 0.05, 0.004, 1)
        sphere(gold, V(x, 0.372, 0.866), 0.012, 8)
        for sg in (-1, 1):
            part(w, lambda t: panel_field(t, -0.27, 0.27, 0.10, 0.62, 0.0, 0.008, +1, mould=0.024),
                 Matrix.Translation(V(x + sg * 0.05, 0, 0)) @ Matrix.Rotation(sg * radians(-90), 4, 'Z'))
    # basamento con i pannelli sul fronte e tre sedili
    bx(w, V(0, 0.0, 0.25), V(2.70, 0.78, 0.50), 0.004, 1)
    for k in range(3):
        xa, xb = xs[k] + 0.05, xs[k + 1] - 0.05
        cx = (xa + xb) / 2
        panel_field(w, xa + 0.04, xb - 0.04, 0.06, 0.42, 0.39, 0.010, +1, mould=0.026)
        bx(w, V(cx, 0.03, 0.51), V(xb - xa - 0.02, 0.74, 0.04), 0.012, 2)
        # schienale: tavola, specchio a pannello, traversa, due nicchie a sesto acuto con la colonnina in mezzo, trave di fregio
        bx(w, V(cx, -0.425, 1.21), V(xb - xa, 0.05, 1.42), 0.004, 1)
        panel_field(w, xa + 0.03, xb - 0.03, 0.56, 0.76, -0.40, 0.008, +1, mould=0.022)
        bx(w, V(cx, -0.392, 0.80), V(xb - xa, 0.032, 0.04), 0.006, 2)
        for sg in (-1, 1):
            nx_ = cx + sg * 0.20
            ring_frame(w, gothic_pts(nx_, 0.86, 0.30, 0.74, n=7), gothic_pts(nx_, 0.89, 0.24, 0.68, n=7), -0.40, -0.378, 0.007)
            poly_prism(dk, gothic_pts(nx_, 0.89, 0.24, 0.68, n=7), 'y', -0.399, -0.390)
        colt = [(0.0, 0.0), (0.026, 0.0), (0.028, 0.010), (0.019, 0.018), (0.0155, 0.030), (0.0155, 0.60), (0.020, 0.614), (0.030, 0.626), (0.0, 0.626)]
        part(w, lambda t: lathe(t, colt, c=V(0, 0, 0), segs=10), Matrix.Translation(V(cx, -0.386, 0.88)))
        bx(w, V(cx, -0.378, 1.675), V(xb - xa, 0.05, 0.05), 0.008, 2)
    # baldacchino: cassa, balza di archi acuti pendenti (lastra sagomata), cimasa sul fronte, cresta con i coni e la fascia d'oro
    bx(w, V(0, -0.215, 1.94), V(2.80, 0.47, 0.08), 0.006, 2)
    pts = [(-1.40, 1.905), (-1.40, 1.775)]
    na, wa_ = 24, 2.70 / 24
    for i in range(na):
        x0 = -1.35 + i * wa_
        xl, xr = x0 + 0.006, x0 + wa_ - 0.006
        wa = xr - xl
        zs = 1.775
        pts.append((x0, zs))
        pts.append((xl, zs))
        for ph in (15, 30, 45):                    # arco di sinistra: centro nell'imposta di destra
            pts.append((xr - wa * cos(radians(ph)), zs + wa * sin(radians(ph))))
        pts.append(((xl + xr) / 2, zs + wa * sin(radians(60))))
        for ph in (45, 30, 15):                    # arco di destra: centro nell'imposta di sinistra
            pts.append((xl + wa * cos(radians(ph)), zs + wa * sin(radians(ph))))
        pts.append((xr, zs))
    pts += [(1.40, 1.775), (1.40, 1.905)]
    poly_prism(w, pts, 'y', 0.0, 0.026)
    sweep(w, [V(1.40, -0.02, 1.925), V(-1.40, -0.02, 1.925)], crown_profile(0.06, 0.055), ref=V(0, 0, 1), cap=True)
    bx(w, V(0, -0.28, 2.005), V(2.80, 0.12, 0.05), 0.006, 2)
    bx(gold, V(0, -0.2185, 2.005), V(2.76, 0.003, 0.014), 0.0008, 1)
    for i in range(11):
        x = -1.25 + 0.25 * i
        g4.cone(w, V(x, -0.28, 2.03), V(x, -0.28, 2.105), 0.022, 6, 0.0)
    for x in xs:
        bx(w, V(x, -0.26, 2.095), V(0.075, 0.075, 0.23), 0.006, 1)
        part(w, lambda t: lathe(t, [(0.058, 2.205), (0.040, 2.245), (0.0, 2.310)], c=V(0, 0, 0), segs=4), Matrix.Translation(V(x, -0.26, 0)) @ Matrix.Rotation(radians(45), 4, 'Z'))
        sphere(gold, V(x, -0.26, 2.318), 0.014, 8)
    return A


ASSETS = {'chi_sarcofago': sarcofago, 'chi_inginocchiatoio': inginocchiatoio, 'chi_statua': statua, 'chi_altare': altare, 'chi_fonte': fonte,
          'chi_stallo': stallo}
VIEWS = {
    'chi_sarcofago': ((-3.0, 3.1, 2.4), (-0.35, 0, 0.45), 40),
    'chi_inginocchiatoio': ((1.2, 1.5, 1.2), (0, 0, 0.45), 40),
    'chi_statua': ((1.9, 2.5, 2.2), (0, 0, 1.15), 40),
    'chi_altare': ((3.8, 3.4, 2.3), (0, 0, 0.55), 40),
    'chi_fonte': ((1.5, 1.9, 1.7), (0, 0, 0.65), 40),
    'chi_stallo': ((3.6, 3.4, 2.1), (0, 0, 1.1), 40),
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
