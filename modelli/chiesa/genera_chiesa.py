"""
Arredi della chiesa (piani procedurali): panca, altare e pala con l'Occhio, candelabro e stand di ceri (con fiamme come
mesh a parte), confessionale, pulpito, fonte battesimale, statua di monaco incappucciato, organo a canne, sarcofago,
lampadario a corona, stallo del coro e inginocchiatoio. Stesso schema di modelli/hotel/genera_arredi.py: riusa i suoi
attrezzi e la sua struttura (un Asset = una mesh per ruolo di materiale, UV in metri).

Uso (dalla cartella del file):
  blender -b -P genera_chiesa.py -- build [nome ...]    FBX in export/<nome>.fbx (tutti se non si nomina nulla)
  blender -b -P genera_chiesa.py -- render [nome ...]   anteprime Cycles in export/render/<nome>.png
Assi: Z in alto, il davanti guarda verso +Y (in Unity +Z), origine al centro a terra, metri. Misure uguali a PropDefs.cs.
"""
import bpy, bmesh, math, os, sys, random
from mathutils import Vector, Matrix
from math import sin, cos, pi, radians, sqrt

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, '..', 'hotel'))
import genera_arredi as ga
from genera_arredi import (V, box, rounded_grid, lathe, sphere, cyl, tube, Asset, smoothstep, bm_v)

OUT = os.path.join(HERE, 'export')

# ruoli nuovi per le anteprime (in Unity li sostituisce TorreBuilder coi materiali del kit)
ga.ROLE.update({
    'A_legno': ((0.12, 0.07, 0.04), 0.0, 0.45), 'A_pietra': ((0.60, 0.56, 0.48), 0.0, 0.8), 'A_ferro': ((0.05, 0.05, 0.055), 0.9, 0.45),
    'A_fiamma': ((1.0, 0.6, 0.15), 0.0, 0.5), 'A_cera': ((0.80, 0.75, 0.60), 0.0, 0.5), 'A_tessuto': ((0.32, 0.05, 0.07), 0.0, 0.9),
    'A_oro': ((0.78, 0.58, 0.2), 1.0, 0.3), 'A_canne': ((0.72, 0.69, 0.60), 1.0, 0.35), 'A_acciaio': ((0.55, 0.56, 0.58), 1.0, 0.4),
    'A_lenzuolo': ((0.70, 0.68, 0.60), 0.0, 0.95), 'A_porcellana': ((0.8, 0.8, 0.78), 0.0, 0.15), 'A_lacca': ((0.02, 0.02, 0.025), 0.0, 0.12),
    'A_osso': ((0.62, 0.58, 0.48), 0.0, 0.5),
})
_orig_material = ga.material


def _material(role):
    fresh = not (role in ga._mats and ga._mats[role].name in bpy.data.materials)
    m = _orig_material(role)
    if fresh and role == 'A_fiamma':
        bs = m.node_tree.nodes['Principled BSDF']
        bs.inputs['Emission Color'].default_value = (1.0, 0.55, 0.12, 1)
        bs.inputs['Emission Strength'].default_value = 8.0
    return m


ga.material = _material


# ----------------------------------------------------------------------------- primitive nuove
def poly_prism(bm, pts, axis, a0, a1):
    """Poligono (u, v) nel piano ortogonale all'asse, esteso da a0 ad a1. Le normali le sistema Asset.build."""
    def P(u, v, a):
        return Vector((a, u, v)) if axis == 'x' else Vector((u, a, v)) if axis == 'y' else Vector((u, v, a))
    f0 = [bm.verts.new(P(u, v, a0)) for u, v in pts]
    f1 = [bm.verts.new(P(u, v, a1)) for u, v in pts]
    bm.faces.new(f0[::-1])
    bm.faces.new(f1)
    n = len(pts)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((f0[i], f0[j], f1[j], f1[i]))


def gothic_pts(cx, zb, w, h, rise=None, n=9):
    """Contorno a sesto acuto (antiorario): base a quota zb, larghezza w, altezza totale h, imposta a h - rise."""
    a = w / 2
    rise = rise if rise is not None else min(h * 0.55, a * 1.6)
    rise = max(rise, a * 1.02)
    spring = zb + h - rise
    d = (rise * rise - a * a) / (2 * a)
    R = a + d
    pts = [(cx - a, zb), (cx + a, zb), (cx + a, spring)]
    for i in range(1, n):
        x = a * (1 - i / n)
        pts.append((cx + x, spring + sqrt(max(0.0, R * R - (x + d) ** 2))))
    pts.append((cx, zb + h))
    for i in range(n - 1, 0, -1):
        x = a * (1 - i / n)
        pts.append((cx - x, spring + sqrt(max(0.0, R * R - (x + d) ** 2))))
    pts.append((cx - a, spring))
    return pts


def arch_slab(bm, cx, zb, w, h, y0, y1, rise=None):
    """Lastra a sesto acuto nel piano XZ, spessa da y0 a y1."""
    poly_prism(bm, gothic_pts(cx, zb, w, h, rise), 'y', y0, y1)


def torus(bm, c, R, r, segs=28, rs=8, axis='Z'):
    rings = []
    for i in range(segs):
        a = 2 * pi * i / segs
        ring = []
        for j in range(rs):
            b = 2 * pi * j / rs
            rr = R + r * cos(b)
            p = V(cos(a) * rr, sin(a) * rr, r * sin(b))
            ring.append(bm.verts.new(Vector(c) + p))
        rings.append(ring)
    for i in range(segs):
        for j in range(rs):
            bm.faces.new((rings[i][j], rings[(i + 1) % segs][j], rings[(i + 1) % segs][(j + 1) % rs], rings[i][(j + 1) % rs]))


def flame(bm, c, s=1.0):
    """Fiamma di candela a goccia, alta 5 cm."""
    lathe(bm, [(0.0, 0.0), (0.0055 * s, 0.004 * s), (0.0095 * s, 0.014 * s), (0.0075 * s, 0.030 * s), (0.003 * s, 0.043 * s), (0.0, 0.052 * s)],
          c=Vector(c), segs=8, closed_bottom=False, closed_top=False)


def candle(wax, fl, c, h=0.14, r=0.013):
    cyl(wax, V(c[0], c[1], c[2]), V(c[0], c[1], c[2] + h), r, 10)
    # stoppino
    cyl(wax, V(c[0], c[1], c[2] + h), V(c[0], c[1], c[2] + h + 0.012), 0.0015, 4)
    flame(fl, (c[0], c[1], c[2] + h + 0.008))


def pointed_gable(bm, cx, zb, w, h, y0, y1):
    """Frontone triangolare (timpano) per i coronamenti."""
    poly_prism(bm, [(cx - w / 2, zb), (cx + w / 2, zb), (cx, zb + h)], 'y', y0, y1)


def pinnacle(bm, c, h, r=0.035):
    """Pinnacolo: fusto quadrato e cuspide."""
    box(bm, V(c[0], c[1], c[2] + h * 0.35), V(r * 2, r * 2, h * 0.7))
    lathe(bm, [(r * 1.5, 0.0), (r * 0.9, h * 0.1), (0.0, h * 0.3)], c=V(c[0], c[1], c[2] + h * 0.7), segs=4, closed_bottom=True)


# ----------------------------------------------------------------------------- panca
def panca():
    A = Asset('chi_panca')
    w = A.bm('A_legno')
    cush = A.bm('A_tessuto')
    L = 2.6
    # sedile e fascia
    box(w, V(0, 0.12, 0.425), V(L - 0.12, 0.44, 0.05), bevel=0.008, seg=2)
    box(w, V(0, 0.10, 0.34), V(L - 0.14, 0.04, 0.14), bevel=0.004, seg=1)
    box(w, V(0, -0.02, 0.28), V(L - 0.14, 0.34, 0.03), bevel=0.004, seg=1)
    # schienale: pannello basso, listelli, trave in alto
    by = -0.15
    box(w, V(0, by, 0.62), V(L - 0.12, 0.04, 0.26), bevel=0.006, seg=2)
    n = 18
    for i in range(n):
        x = -(L - 0.2) / 2 + (L - 0.2) * (i + 0.5) / n
        box(w, V(x, by, 0.82), V(0.032, 0.03, 0.17), bevel=0.003, seg=1)
    box(w, V(0, by, 0.93), V(L - 0.10, 0.07, 0.07), bevel=0.02, seg=3)
    box(w, V(0, by, 0.745), V(L - 0.12, 0.045, 0.03), bevel=0.004, seg=1)
    # fianchi: profilo alto dietro, scende in curva davanti
    side = [(-0.24, 0.0), (0.38, 0.0), (0.38, 0.30), (0.33, 0.45), (0.22, 0.52), (0.08, 0.58), (-0.02, 0.72), (-0.08, 0.90), (-0.12, 0.99), (-0.20, 0.99), (-0.24, 0.90)]
    for sx in (-1, 1):
        x = sx * (L / 2 - 0.035)
        poly_prism(w, side, 'x', x - 0.035, x + 0.035)
        # pomello sul retro
        sphere(w, V(x, -0.17, 1.03), 0.04, 10)
    # inginocchiatoio sul retro (serve la fila dietro)
    box(w, V(0, -0.52, 0.10), V(L - 0.25, 0.20, 0.12), bevel=0.01, seg=2)
    box(w, V(0, -0.52, 0.045), V(L - 0.30, 0.10, 0.09), bevel=0.005, seg=1)
    box(cush, V(0, -0.52, 0.168), V(L - 0.40, 0.17, 0.03), bevel=0.012, seg=2)
    return A


# ----------------------------------------------------------------------------- altare e pala
def altare():
    A = Asset('chi_altare')
    st = A.bm('A_pietra')
    cloth = A.bm('A_tessuto')
    gold = A.bm('A_oro')
    W, D, H = 2.4, 1.0, 1.05
    # base e fusto: due gradini di zoccolo e il corpo con tre arcatelle cieche sul davanti
    box(st, V(0, 0, 0.04), V(W - 0.02, D - 0.02, 0.08), bevel=0.01, seg=2)
    box(st, V(0, 0, 0.45), V(W - 0.20, D - 0.16, 0.74), bevel=0.012, seg=2)
    for i, x in enumerate((-0.72, 0.0, 0.72)):
        arch_slab(st, x, 0.16, 0.50, 0.58, D / 2 - 0.085, D / 2 - 0.045)
    # mensa con sporgenza
    box(st, V(0, 0, 0.995), V(W, D, 0.10), bevel=0.012, seg=2)
    box(st, V(0, 0, 0.935), V(W - 0.08, D - 0.08, 0.04))
    # tovaglia scura che scende davanti e un po' sui lati (frange in basso)
    box(cloth, V(0, 0.0, 1.05), V(W - 0.30, D - 0.10, 0.012), bevel=0.003, seg=1)
    box(cloth, V(0, D / 2 - 0.035, 0.80), V(W - 0.34, 0.012, 0.50), bevel=0.003, seg=1)
    for i in range(14):
        x = -(W - 0.40) / 2 + (W - 0.40) * (i + 0.5) / 14
        cyl(gold, V(x, D / 2 - 0.028, 0.55), V(x, D / 2 - 0.028, 0.50), 0.006, 6)
    box(gold, V(0, D / 2 - 0.03, 0.555), V(W - 0.34, 0.006, 0.012))
    # leggio e tre piccoli dischi d'oro con l'Occhio sulla mensa
    cyl(gold, V(0, -0.1, 1.045), V(0, -0.1, 1.062), 0.10, 20)
    sphere(gold, V(0, -0.1, 1.09), 0.05, 12)
    return A


def pala():
    A = Asset('chi_pala')
    stone = A.bm('A_pietra')
    dark = A.bm('A_scuro')
    gold = A.bm('A_oro')
    width, depth = 3.36, 0.44
    box(stone, V(0, 0, 0.11), V(width, depth, 0.22), bevel=0.022, seg=3)
    box(stone, V(0, -0.014, 0.33), V(width - 0.10, depth - 0.045, 0.25), bevel=0.017, seg=2)
    box(stone, V(0, 0, 0.47), V(width - 0.035, depth, 0.075), bevel=0.012, seg=2)

    def frame(outer, inner, back, front):
        # Cornice chiusa con smusso sul bordo interno.
        bevel = [(x * 0.22 + u * 0.78, z * 0.22 + v * 0.78) for (x, z), (u, v) in zip(outer, inner)]
        loops = [[stone.verts.new(V(x, y, z)) for x, z in outline]
                 for outline, y in ((outer, back), (outer, front - 0.014), (bevel, front), (inner, front - 0.018), (inner, back))]
        for ring in range(len(loops)):
            a, b = loops[ring - 1], loops[ring]
            for i in range(len(outer)):
                j = (i + 1) % len(outer)
                stone.faces.new((a[i], a[j], b[j], b[i]))

    for cx, span, height in ((-1.10, 0.92, 1.90), (0.0, 1.36, 2.60), (1.10, 0.92, 1.90)):
        base = 0.51
        arch_slab(stone, cx, base, span, height, -0.18, 0.033)
        frame(gothic_pts(cx, base, span, height),
              gothic_pts(cx, base + 0.080, span - 0.16, height - 0.18), 0.02, 0.19)
        frame(gothic_pts(cx, base + 0.062, span - 0.12, height - 0.13),
              gothic_pts(cx, base + 0.111, span - 0.221, height - 0.23), 0.042, 0.119)

        for sign in (-1, 1):
            x = cx + sign * (span * 0.5 - 0.053)
            shaft = height - span * 0.8
            profile = [(0.047, 0), (0.050, 0.04), (0.035, 0.077), (0.028, 0.106),
                       (0.028, shaft - 0.105), (0.037, shaft - 0.079),
                       (0.050, shaft - 0.025), (0.048, shaft)]
            lathe(stone, profile, c=V(x, 0.136, base), segs=12)
        if cx != 0.0:
            z = base + 0.91
            for i in range(4):
                a = i * pi * 0.5
                lathe(stone, [(0.078, 0), (0.078, 0.012), (0.058, 0.037), (0, 0.040)],
                      c=V(cx + cos(a) * 0.09, 0.030, z + sin(a) * 0.09), segs=16, axis='Y')
            sphere(dark, V(cx, 0.059, z), 0.032, 10)

    for x in (-1.60, -0.67, 0.67, 1.60):
        pillar = 2.20 if abs(x) > 1.0 else 2.55
        box(stone, V(x, 0.025, 0.50 + pillar * 0.5), V(0.10, 0.29, pillar), bevel=0.014, seg=2)
        pinnacle(stone, (x, 0.025, 0.50 + pillar), 0.28, 0.043)
    pinnacle(stone, (0, 0.024, 3.04), 0.33, 0.044)

    # L'occhio e' una borchia di metallo incastonata nel calcare del vano centrale.
    z = 1.96
    lathe(stone, [(0.265, 0), (0.265, 0.060), (0.237, 0.091), (0.211, 0.092)],
          c=V(0, 0.035, z), segs=36, axis='Y')
    lathe(dark, [(0.0, 0), (0.211, 0), (0.211, 0.026), (0, 0.029)],
          c=V(0, 0.083, z), segs=32, axis='Y')
    for sign in (-1, 1):
        lid = [V(0.175 * cos(pi * i / 18), 0.132, z + sign * 0.078 * sin(pi * i / 18)) for i in range(19)]
        tube(gold, lid, (0.011, 0.011), segs=7, n=24)
    lathe(gold, [(0.028, 0), (0.062, 0), (0.069, 0.010), (0.057, 0.025), (0.028, 0.029)],
          c=V(0, 0.125, z), segs=24, axis='Y')
    sphere(dark, V(0, 0.138, z), 0.029, 12)
    return A


# ----------------------------------------------------------------------------- candele
def candelabro():
    A = Asset('chi_candelabro')
    iron = A.bm('A_ferro')
    brass = A.bm('A_ottone')
    wax = A.bm('A_cera')
    fl = A.bm('A_fiamma')
    # tre piedi a voluta e fusto a nodi
    for k in range(3):
        a = 2 * pi * k / 3 + pi / 6
        pts = [V(0.03 * cos(a), 0.03 * sin(a), 0.22), V(0.14 * cos(a), 0.14 * sin(a), 0.14), V(0.25 * cos(a), 0.25 * sin(a), 0.04), V(0.28 * cos(a), 0.28 * sin(a), 0.0)]
        tube(iron, pts, [0.014, 0.016, 0.02, 0.022], 8, 14)
        sphere(iron, V(0.28 * cos(a), 0.28 * sin(a), 0.014), 0.026, 8)
    prof = [(0.0, 0.18), (0.05, 0.19), (0.06, 0.24), (0.025, 0.28), (0.022, 0.55), (0.04, 0.58), (0.045, 0.64), (0.022, 0.68),
            (0.02, 1.0), (0.035, 1.03), (0.04, 1.07), (0.02, 1.12), (0.018, 1.36), (0.05, 1.40), (0.045, 1.43), (0.0, 1.45)]
    lathe(iron, prof, segs=14, closed_bottom=False, closed_top=False)
    # corona di bracci: uno centrale e sei attorno
    h0 = 1.40
    for k in range(7):
        if k == 0:
            cx, cy, ch = 0.0, 0.0, h0 + 0.13
            cyl(iron, V(0, 0, h0), V(0, 0, ch), 0.012, 8)
        else:
            a = 2 * pi * (k - 1) / 6
            pts = [V(0.0, 0.0, h0 - 0.02), V(0.10 * cos(a), 0.10 * sin(a), h0 - 0.0), V(0.20 * cos(a), 0.20 * sin(a), h0 + 0.07), V(0.25 * cos(a), 0.25 * sin(a), h0 + 0.15)]
            tube(iron, pts, [0.012, 0.012, 0.01, 0.01], 8, 12)
            cx, cy, ch = 0.25 * cos(a), 0.25 * sin(a), h0 + 0.15
        # coppetta, candela e fiamma
        lathe(iron, [(0.0, 0.0), (0.028, 0.0), (0.03, 0.012), (0.018, 0.02)], c=V(cx, cy, ch), segs=10, closed_top=False)
        candle(wax, fl, (cx, cy, ch + 0.01), h=0.12 + 0.05 * ((k * 7) % 3) / 2.0)
    return A


def ceri():
    A = Asset('chi_ceri')
    iron = A.bm('A_ferro')
    wax = A.bm('A_cera')
    fl = A.bm('A_fiamma')
    sand = A.bm('A_scuro')
    W, D = 0.9, 0.45
    # montanti e tre ripiani inclinati; sul primo, un vassoio di sabbia
    for sx in (-1, 1):
        for sy in (-1, 1):
            cyl(iron, V(sx * (W / 2 - 0.03), sy * (D / 2 - 0.05), 0.0), V(sx * (W / 2 - 0.03), sy * (D / 2 - 0.08), 1.18), 0.014, 8)
    for z, dep in ((0.45, 0.40), (0.78, 0.33), (1.10, 0.26)):
        box(iron, V(0, 0, z), V(W - 0.02, dep, 0.016), bevel=0.003, seg=1)
        box(iron, V(0, dep / 2, z + 0.012), V(W - 0.02, 0.012, 0.03))
    box(sand, V(0, 0.0, 0.48), V(W - 0.10, 0.34, 0.02))
    rnd = random.Random(5)
    for z, dep, n in ((0.48, 0.30, 8), (0.80, 0.24, 6), (1.12, 0.17, 4)):
        for i in range(n):
            x = -(W - 0.20) / 2 + (W - 0.20) * (i + 0.5) / n
            for row in (0, 1):
                if rnd.random() < 0.18:
                    continue
                y = (-0.07 if row == 0 else 0.08) * dep / 0.3
                h = rnd.uniform(0.05, 0.11)
                cyl(wax, V(x, y, z), V(x, y, z + h), 0.015, 10)
                flame(fl, (x, y, z + h + 0.004))
    return A


# ----------------------------------------------------------------------------- confessionale
def confessionale():
    A = Asset('chi_confessionale')
    w = A.bm('A_legno')
    cl = A.bm('A_tessuto')
    br = A.bm('A_ottone')
    dk = A.bm('A_scuro')
    W, D = 2.5, 1.15
    cw, sw = 0.92, 0.74       # larghezza cabina centrale e laterali
    # zoccolo e fiancate esterne
    box(w, V(0, 0, 0.07), V(W, D, 0.14), bevel=0.01, seg=2)
    # cabine laterali (penitenti): alte 2,25 m, aperte davanti con tenda; cabina centrale piu' alta
    xs = [-(cw / 2 + sw / 2 + 0.02), 0.0, (cw / 2 + sw / 2 + 0.02)]
    for i, cx in enumerate(xs):
        ww = cw if i == 1 else sw
        hh = 2.55 if i == 1 else 2.20
        # pareti: fondo e due lati
        box(w, V(cx, -D / 2 + 0.03, 0.14 + hh / 2), V(ww, 0.05, hh), bevel=0.004, seg=1)
        if i != 1:
            for sx in (-1, 1):
                if (i == 0 and sx == -1) or (i == 2 and sx == 1):
                    box(w, V(cx + sx * (ww / 2 - 0.025), 0.0, 0.14 + hh / 2), V(0.05, D - 0.02, hh), bevel=0.004, seg=1)
        # tetto
        box(w, V(cx, 0.0, 0.14 + hh + 0.03), V(ww + 0.06, D + 0.04, 0.06), bevel=0.012, seg=2)
    # cabina centrale: due lati pieni, porta con pannelli a sesto acuto e tendine
    cx = 0.0
    for sx in (-1, 1):
        box(w, V(sx * (cw / 2 - 0.025), 0.0, 0.14 + 2.55 / 2), V(0.05, D - 0.02, 2.55), bevel=0.004, seg=1)
    box(w, V(0, D / 2 - 0.03, 0.14 + 0.45), V(cw - 0.10, 0.045, 0.9), bevel=0.004, seg=1)       # pannello basso della porta
    arch_slab(w, 0.0, 0.18, cw - 0.22, 0.78, D / 2 - 0.02, D / 2 + 0.01)
    box(w, V(0, D / 2 - 0.03, 0.14 + 1.05), V(cw - 0.10, 0.045, 0.1))
    arch_slab(dk, 0.0, 1.38, cw - 0.22, 1.05, D / 2 - 0.04, D / 2 - 0.02, rise=0.5)           # sopra: apertura buia a sesto acuto
    # coronamento: frontone con pinnacoli
    pointed_gable(w, 0.0, 0.14 + 2.58, cw + 0.1, 0.34, -0.05, 0.05)
    pinnacle(w, (0.0, 0.0, 0.14 + 2.9), 0.28, 0.035)
    for sx in (-1, 1):
        pinnacle(w, (sx * (cw / 2 + 0.01), D / 2 - 0.03, 0.14 + 2.58), 0.22, 0.03)
    # griglie ai lati della cabina centrale (verso i penitenti) e tende laterali
    for sx in (-1, 1):
        gx = sx * (cw / 2 - 0.025)
        for k in range(6):
            cyl(br, V(gx, -0.05 + k * 0.03 - 0.075, 1.50), V(gx, -0.05 + k * 0.03 - 0.075, 1.74), 0.004, 6)
        for k in range(8):
            cyl(br, V(gx, -0.20, 1.50 + k * 0.03), V(gx, 0.10, 1.50 + k * 0.03), 0.003, 6)
        # tenda del penitente
        tx = sx * (cw / 2 + sw / 2 + 0.02)
        box(cl, V(tx, D / 2 - 0.05, 0.14 + 0.95), V(sw - 0.14, 0.02, 1.7), bevel=0.006, seg=2)
        box(br, V(tx, D / 2 - 0.045, 0.14 + 1.82), V(sw - 0.10, 0.02, 0.02))
        # inginocchiatoio interno
        box(w, V(tx, D / 2 - 0.30, 0.28), V(sw - 0.2, 0.22, 0.10))
    return A


# ----------------------------------------------------------------------------- pulpito
def pulpito():
    A = Asset('chi_pulpito')
    w = A.bm('A_legno')
    st = A.bm('A_pietra')
    gold = A.bm('A_oro')
    cl = A.bm('A_tessuto')
    # colonna di pietra a nodi fino alla piattaforma a 1,45 m
    lathe(st, [(0.0, 0.0), (0.30, 0.0), (0.30, 0.10), (0.17, 0.16), (0.14, 0.30), (0.14, 0.9), (0.2, 1.0), (0.26, 1.2), (0.40, 1.38), (0.0, 1.38)],
          segs=8, closed_bottom=False)
    # piattaforma e parapetto ottagonale con pannelli a sesto acuto
    R = 0.58
    box(w, V(0, 0, 1.43), V(1.08, 1.08, 0.06), bevel=0.01, seg=2, rot=Matrix.Rotation(pi / 8, 3, 'Z'))
    for k in range(8):
        a = 2 * pi * k / 8 + pi / 8
        px, py = cos(a) * R * 0.92, sin(a) * R * 0.92
        rot = Matrix.Rotation(a + pi / 2, 3, 'Z')
        if k == 0:
            continue                                      # il varco della scaletta tolta
        box(w, V(px, py, 1.46 + 0.45), V(0.46, 0.05, 0.9), bevel=0.006, seg=1, rot=rot)
    box(gold, V(0, 0, 1.46 + 0.9), V(1.15, 1.15, 0.03), bevel=0.01, seg=1, rot=Matrix.Rotation(pi / 8, 3, 'Z'))
    box(cl, V(0, -R * 0.9 + 0.05, 2.05), V(0.5, 0.02, 0.35), bevel=0.01, seg=1)
    # pannello dorsale e baldacchino
    box(w, V(0, -0.58, 2.6), V(0.95, 0.06, 2.3), bevel=0.01, seg=1)
    arch_slab(w, 0.0, 1.5, 0.95, 1.8, -0.62, -0.55)
    lathe(w, [(0.62, 0.0), (0.66, 0.06), (0.56, 0.10), (0.30, 0.14), (0.0, 0.34)], c=V(0, -0.28, 3.0), segs=8)
    sphere(gold, V(0, -0.28, 3.36), 0.05, 10)
    for k in range(8):
        a = 2 * pi * k / 8
        cyl(gold, V(cos(a) * 0.60, -0.28 + sin(a) * 0.60, 3.0 + 0.0), V(cos(a) * 0.60, -0.28 + sin(a) * 0.60, 2.93), 0.01, 6)
    return A


# ----------------------------------------------------------------------------- fonte
def fonte():
    A = Asset('chi_fonte')
    st = A.bm('A_pietra')
    w = A.bm('A_legno')
    br = A.bm('A_ottone')
    lathe(st, [(0.0, 0.0), (0.30, 0.0), (0.30, 0.06), (0.20, 0.12), (0.12, 0.2), (0.12, 0.6), (0.22, 0.7), (0.34, 0.78), (0.43, 0.9), (0.45, 0.96), (0.40, 0.96),
               (0.36, 0.86), (0.0, 0.84)], segs=8, closed_bottom=False)
    # coperchio di legno a cupola con anello e pomo
    lathe(w, [(0.46, 0.0), (0.43, 0.04), (0.34, 0.12), (0.18, 0.18), (0.05, 0.22), (0.0, 0.24)], c=V(0, 0, 0.97), segs=8)
    sphere(br, V(0, 0, 1.2), 0.04, 10)
    cyl(br, V(0, 0, 1.17), V(0, 0, 1.2), 0.012, 6)
    for k in range(8):
        a = 2 * pi * k / 8 + pi / 8
        box(br, V(cos(a) * 0.30, sin(a) * 0.30, 1.04), V(0.02, 0.12, 0.012), rot=Matrix.Rotation(a, 3, 'Z'))
    return A


# ----------------------------------------------------------------------------- statua del monaco
def statua():
    A = Asset('chi_statua')
    st = A.bm('A_pietra')
    gold = A.bm('A_oro')
    dk = A.bm('A_scuro')
    # piedistallo con targa
    box(st, V(0, 0, 0.30), V(0.70, 0.70, 0.60), bevel=0.02, seg=2)
    box(st, V(0, 0, 0.66), V(0.58, 0.58, 0.12), bevel=0.015, seg=2)
    box(dk, V(0, 0.356, 0.34), V(0.32, 0.012, 0.18))
    # tonaca: scampana fino ai piedi, strozza in vita, spalle larghe
    lathe(st, [(0.0, 0.72), (0.26, 0.72), (0.27, 0.78), (0.24, 1.0), (0.20, 1.3), (0.17, 1.46), (0.20, 1.62), (0.23, 1.70), (0.0, 1.72)], segs=14, closed_bottom=False)
    # cappuccio: cupola allungata, con l'interno scuro e un occhio solo scolpito
    lathe(st, [(0.0, 1.60), (0.14, 1.60), (0.20, 1.66), (0.215, 1.78), (0.17, 1.90), (0.08, 1.98), (0.0, 2.04)], segs=14, closed_bottom=False)
    sphere(dk, V(0, 0.115, 1.80), 0.115, 12)
    e = bm_v
    sphere(gold, V(0, 0.185, 1.815), 0.05, 12)
    sphere(dk, V(0, 0.228, 1.82), 0.022, 8)
    # braccia piegate, mani giunte sul petto
    for sx in (-1, 1):
        cyl(st, V(sx * 0.215, 0.0, 1.60), V(sx * 0.12, 0.17, 1.42), 0.062, 10)
        sphere(st, V(sx * 0.215, 0.0, 1.60), 0.07, 10)
    sphere(st, V(0, 0.18, 1.40), 0.075, 10)
    # un po' di pieghe: tre costole verticali sul davanti
    for x in (-0.09, 0.0, 0.09):
        cyl(st, V(x, 0.20, 0.8), V(x * 0.7, 0.17, 1.35), 0.018, 6)
    return A


# ----------------------------------------------------------------------------- organo
def organo():
    A = Asset('chi_organo')
    w = A.bm('A_legno')
    pipes = A.bm('A_canne')
    gold = A.bm('A_oro')
    dk = A.bm('A_scuro')
    W, D = 3.6, 1.3
    # cassa bassa con pannelli, consolle sul davanti
    box(w, V(0, 0, 0.95), V(W, D, 1.9), bevel=0.012, seg=2)
    for i in range(7):
        x = -W / 2 + 0.28 + i * (W - 0.56) / 6
        arch_slab(w, x, 0.25, 0.38, 1.1, D / 2 - 0.005, D / 2 + 0.03, rise=0.55)
    box(w, V(0, D / 2 + 0.12, 1.02), V(1.5, 0.3, 0.07), bevel=0.01, seg=1)                    # leggio/tastiera
    box(dk, V(0, D / 2 + 0.17, 1.065), V(1.40, 0.18, 0.015))
    box(gold, V(0, D / 2 + 0.03, 1.55), V(W - 0.2, 0.04, 0.05))
    # canne: tre torri e due campi intermedi; ogni canna con la sua bocca
    def tower(cx, n, base_h, top_h, radius, spread, y):
        for i in range(n):
            t = i / (n - 1) if n > 1 else 0
            x = cx - spread / 2 + spread * t
            hh = base_h + (top_h - base_h) * (1 - abs(2 * t - 1))
            r = radius * (0.8 + 0.4 * (1 - abs(2 * t - 1)))
            zb = 1.9
            cyl(pipes, V(x, y, zb), V(x, y, zb + hh), r, 12)
            # bocca
            box(pipes, V(x, y + r * 0.95, zb + hh * 0.12), V(r * 1.5, 0.02, r * 1.3))
            lathe(pipes, [(r, 0.0), (r * 0.5, 0.08), (0.0, 0.1)], c=V(x, y, zb + hh), segs=12)
    tower(-1.2, 11, 1.6, 3.4, 0.045, 1.0, 0.3)
    tower(0.0, 13, 2.0, 3.6, 0.052, 1.2, 0.36)
    tower(1.2, 11, 1.6, 3.4, 0.045, 1.0, 0.3)
    tower(-0.6, 8, 1.1, 1.7, 0.032, 0.6, 0.24)
    tower(0.6, 8, 1.1, 1.7, 0.032, 0.6, 0.24)
    # coronamento in legno con archetti e pinnacoli
    box(w, V(0, 0.2, 5.38), V(W - 0.08, 0.5, 0.14), bevel=0.01, seg=1)
    for cx in (-1.2, 0.0, 1.2):
        arch_slab(w, cx, 4.5, 1.1, 0.9, 0.05, 0.12, rise=0.7) if cx != 0.0 else arch_slab(w, cx, 4.8, 1.3, 0.55, 0.05, 0.12, rise=0.6)
    for x in (-W / 2 + 0.1, -0.6, 0.6, W / 2 - 0.1):
        pinnacle(w, (x, 0.2, 5.3), 0.3, 0.05)
    return A


# ----------------------------------------------------------------------------- sarcofago
def sarcofago():
    A = Asset('chi_sarcofago')
    st = A.bm('A_pietra')
    dk = A.bm('A_scuro')
    bone = A.bm('A_osso')
    W, D, H = 2.3, 1.0, 1.0
    # cassa
    box(st, V(0, 0, 0.36), V(W - 0.1, D - 0.12, 0.72), bevel=0.02, seg=2)
    box(st, V(0, 0, 0.04), V(W, D, 0.08), bevel=0.012, seg=1)
    # tre arcatelle sulla fronte e sul retro
    for sy in (-1, 1):
        for x in (-0.7, 0.0, 0.7):
            arch_slab(st, x, 0.12, 0.42, 0.52, sy * (D / 2 - 0.06) - 0.02 + (0.02 if sy > 0 else 0.0), sy * (D / 2 - 0.06) + 0.02 + (0.02 if sy > 0 else 0.0))
    # coperchio a spiovente sfalsato (un po' aperto): il vano scuro si vede
    lid = [(-D / 2 + 0.02, 0.72), (D / 2 - 0.02, 0.72), (D / 2 - 0.10, 0.88), (0.0, 0.98), (-D / 2 + 0.10, 0.88)]
    poly_prism(st, [(y, z) for (y, z) in lid], 'x', -W / 2 + 0.38, W / 2 + 0.05)
    box(dk, V(-W / 2 + 0.18, 0.0, 0.76), V(0.4, D - 0.20, 0.06))
    # ossa nel vano scoperto
    cyl(bone, V(-W / 2 + 0.06, -0.20, 0.80), V(-W / 2 + 0.34, 0.12, 0.79), 0.022, 8)
    sphere(bone, V(-W / 2 + 0.20, 0.05, 0.80), 0.07, 10)
    return A


# ----------------------------------------------------------------------------- lampadario a corona
def lampadario():
    A = Asset('chi_lampadario')
    iron = A.bm('A_ferro')
    wax = A.bm('A_cera')
    fl = A.bm('A_fiamma')
    gold = A.bm('A_oro')
    R = 0.62
    torus(iron, V(0, 0, 0), R, 0.02, 36, 8)
    torus(iron, V(0, 0, -0.10), R * 0.55, 0.014, 28, 6)
    for k in range(12):
        a = 2 * pi * k / 12
        x, y = cos(a) * R, sin(a) * R
        lathe(iron, [(0.0, 0.0), (0.03, 0.0), (0.032, 0.015), (0.02, 0.03)], c=V(x, y, 0.015), segs=10, closed_top=False)
        candle(wax, fl, (x, y, 0.025), h=0.11 + 0.03 * (k % 3) / 2)
        # raggi che uniscono i due anelli
        cyl(iron, V(x, y, 0.0), V(cos(a) * R * 0.55, sin(a) * R * 0.55, -0.10), 0.008, 6)
    # tre catene che salgono a un anello
    for k in range(3):
        a = 2 * pi * k / 3 + pi / 6
        cyl(iron, V(cos(a) * R, sin(a) * R, 0.02), V(0, 0, 0.8), 0.009, 6)
    torus(gold, V(0, 0, 0.82), 0.05, 0.012, 14, 6)
    sphere(gold, V(0, 0, -0.16), 0.06, 10)
    lathe(gold, [(0.0, 0.0), (0.03, 0.04), (0.0, 0.12)], c=V(0, 0, -0.28), segs=10)
    return A


# ----------------------------------------------------------------------------- stallo del coro
def stallo():
    A = Asset('chi_stallo')
    w = A.bm('A_legno')
    gold = A.bm('A_oro')
    W, D = 2.8, 0.9
    n = 3
    sw = W / n
    # sedili con bracciolo, schienale alto a sesto acuto, baldacchino
    box(w, V(0, 0.0, 0.22), V(W, D - 0.1, 0.44), bevel=0.01, seg=1)
    for i in range(n):
        cx = -W / 2 + sw * (i + 0.5)
        box(w, V(cx, 0.06, 0.49), V(sw - 0.16, D - 0.3, 0.05), bevel=0.008, seg=2)
        arch_slab(w, cx, 0.5, sw - 0.12, 1.55, -D / 2 + 0.02, -D / 2 + 0.10, rise=0.6)
        arch_slab(w, cx, 0.7, sw - 0.34, 1.0, -D / 2 + 0.10, -D / 2 + 0.115, rise=0.5)
    for i in range(n + 1):
        x = -W / 2 + sw * i
        box(w, V(x, 0.0, 0.52), V(0.07, D - 0.1, 0.12), bevel=0.01, seg=1)                    # bracciolo
        box(w, V(x, -D / 2 + 0.1, 1.15), V(0.09, 0.18, 2.05), bevel=0.01, seg=1)            # divisorio alto
        if i in (0, n):
            box(w, V(x, 0.12, 0.55), V(0.07, D - 0.34, 1.1), bevel=0.01, seg=1)
        pinnacle(w, (x, -D / 2 + 0.1, 2.18), 0.12, 0.04)
    box(w, V(0, -D / 2 + 0.16, 2.14), V(W, 0.34, 0.1), bevel=0.01, seg=1)
    box(w, V(0, -D / 2 + 0.16, 2.24), V(W - 0.06, 0.5, 0.06), bevel=0.01, seg=1)
    box(gold, V(0, -D / 2 + 0.31, 2.14), V(W - 0.1, 0.012, 0.03))
    return A


# ----------------------------------------------------------------------------- inginocchiatoio
def inginocchiatoio():
    A = Asset('chi_inginocchiatoio')
    w = A.bm('A_legno')
    cl = A.bm('A_tessuto')
    gold = A.bm('A_oro')
    # base con il cuscino, schienale con leggio inclinato
    box(w, V(0, 0.08, 0.10), V(0.56, 0.34, 0.20), bevel=0.01, seg=2)
    box(cl, V(0, 0.08, 0.215), V(0.48, 0.26, 0.05), bevel=0.02, seg=3)
    box(w, V(0, -0.14, 0.45), V(0.56, 0.12, 0.90), bevel=0.012, seg=2)
    arch_slab(w, 0.0, 0.5, 0.40, 0.46, -0.205, -0.075, rise=0.28)
    box(w, V(0, -0.02, 0.77), V(0.54, 0.22, 0.04), bevel=0.01, seg=2, rot=Matrix.Rotation(radians(-12), 3, 'X'))
    box(gold, V(0, 0.07, 0.775), V(0.40, 0.012, 0.012))
    return A


ASSETS = {
    'chi_panca': panca, 'chi_altare': altare, 'chi_pala': pala, 'chi_candelabro': candelabro, 'chi_ceri': ceri,
    'chi_confessionale': confessionale, 'chi_pulpito': pulpito, 'chi_fonte': fonte, 'chi_statua': statua, 'chi_organo': organo,
    'chi_sarcofago': sarcofago, 'chi_lampadario': lampadario, 'chi_stallo': stallo, 'chi_inginocchiatoio': inginocchiatoio,
}
VIEWS = {
    'chi_panca': ((2.6, 3.0, 1.5), (0, 0, 0.5), 40), 'chi_altare': ((2.8, 3.4, 1.7), (0, 0, 0.6), 40), 'chi_pala': ((3.6, 5.2, 2.6), (0, 0, 1.7), 36),
    'chi_candelabro': ((1.5, 2.0, 1.6), (0, 0, 0.9), 40), 'chi_ceri': ((1.8, 2.2, 1.3), (0, 0, 0.6), 40),
    'chi_confessionale': ((3.2, 4.4, 2.2), (0, 0, 1.2), 40), 'chi_pulpito': ((2.8, 3.6, 2.4), (0, 0, 1.7), 40),
    'chi_fonte': ((1.7, 2.1, 1.3), (0, 0, 0.55), 40), 'chi_statua': ((1.9, 2.5, 1.9), (0, 0, 1.1), 40),
    'chi_organo': ((5.0, 7.2, 3.6), (0, 0, 2.7), 36), 'chi_sarcofago': ((3.0, 3.6, 1.8), (0, 0, 0.5), 40),
    'chi_lampadario': ((1.9, 2.2, 0.9), (0, 0, 0.2), 40), 'chi_stallo': ((3.4, 4.0, 2.2), (0, 0, 1.2), 40),
    'chi_inginocchiatoio': ((1.4, 1.7, 1.1), (0, 0, 0.45), 40),
}

if __name__ == '__main__':
    ga.ASSETS.update(ASSETS)
    ga.VIEWS.update(VIEWS)
    ga.OUT = OUT
    names = [a for a in ga.ARGS if a in ASSETS] or list(ASSETS)
    if 'build' in ga.ARGS:
        ga.stage(names, 'build')
    if 'render' in ga.ARGS:
        ga.stage(names, 'render')
