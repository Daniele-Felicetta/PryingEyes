"""
Arredi d'albergo per le stanze dei piani procedurali: letto matrimoniale disfatto, comodino, armadio con un'anta
aperta, scrivania, sedia, tavolo da ristorante con la tovaglia, pianoforte a coda, fornello con cappa, tavolo d'acciaio
e scaffale. Stessi attrezzi e struttura di genera_arredi.py / genera_chiesa.py (Asset = una mesh per ruolo).

Uso (dalla cartella del file):
  blender -b -P genera_arredi2.py -- build [nome ...]    FBX in arredi2/<nome>.fbx
  blender -b -P genera_arredi2.py -- render [nome ...]   anteprime in arredi2/render/<nome>.png
Assi: Z in alto, davanti = +Y (Unity +Z), origine al centro a terra. Misure uguali a PropDefs.cs.
"""
import bpy, bmesh, math, os, sys, random
from mathutils import Vector, Matrix, noise
from math import sin, cos, pi, radians, sqrt

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, '..', 'chiesa'))
import genera_arredi as ga
import genera_chiesa as gc          # ruoli nuovi, poly_prism, arch_slab, torus...
from genera_arredi import V, box, rounded_grid, lathe, sphere, cyl, tube, Asset
from genera_chiesa import poly_prism, arch_slab, torus

OUT = os.path.join(HERE, 'arredi2')


def rot_z(a):
    return Matrix.Rotation(a, 3, 'Z')


# ----------------------------------------------------------------------------- letto
def letto():
    A = Asset('arr_letto')
    wood = A.bm('A_noce')
    sheet = A.bm('A_lenzuolo')
    pad = A.bm('A_tessuto')
    W, L = 1.75, 2.15
    y0 = -L / 2
    # testiera alta e piediera bassa con profilo curvo
    hb = [(-W / 2, 0.16), (W / 2, 0.16), (W / 2, 0.82), (0.55, 0.96), (0.0, 1.05), (-0.55, 0.96), (-W / 2, 0.82)]
    poly_prism(wood, hb, 'y', y0 + 0.02, y0 + 0.10)
    poly_prism(pad, [(-0.74, 0.50), (0.74, 0.50), (0.74, 0.86), (0.42, 0.93), (0.0, 0.98), (-0.42, 0.93), (-0.74, 0.86)], 'y', y0 + 0.10, y0 + 0.14)
    fb = [(-W / 2, 0.16), (W / 2, 0.16), (W / 2, 0.52), (0.5, 0.58), (0.0, 0.60), (-0.5, 0.58), (-W / 2, 0.52)]
    poly_prism(wood, fb, 'y', L / 2 - 0.08, L / 2 - 0.01)
    # telaio e gambe
    for sx in (-1, 1):
        box(wood, V(sx * (W / 2 - 0.04), 0.0, 0.30), V(0.06, L - 0.08, 0.16), bevel=0.006, seg=1)
        for sy in (-1, 1):
            box(wood, V(sx * (W / 2 - 0.05), sy * (L / 2 - 0.08), 0.12), V(0.08, 0.08, 0.24), bevel=0.01, seg=1)
    box(wood, V(0, 0.0, 0.31), V(W - 0.1, L - 0.2, 0.04))
    # materasso e lenzuola sfatte
    rounded_grid(sheet, V(0, 0.02, 0.44), V(W - 0.16, L - 0.22, 0.22), 0.05, nx=26, ny=34, nz=5)
    top = rounded_grid(sheet, V(0, 0.18, 0.575), V(W - 0.22, L - 0.62, 0.05), 0.02, nx=30, ny=38, nz=3)
    for v in top:
        n = noise.noise(Vector((v.co.x * 3.0, v.co.y * 3.0, 0.0)))
        if v.co.z > 0.58:
            v.co.z += 0.03 * n + 0.02 * noise.noise(Vector((v.co.x * 9.0, v.co.y * 9.0, 1.0)))
    # piega del lenzuolo gettata verso il piede e cuscini
    rounded_grid(sheet, V(0.05, 0.84, 0.62), V(W - 0.30, 0.34, 0.14), 0.06, nx=18, ny=8, nz=4)
    for sx in (-1, 1):
        rounded_grid(sheet, V(sx * 0.42, y0 + 0.40, 0.68), V(0.64, 0.42, 0.16), 0.07, nx=14, ny=10, nz=5)
    return A


def comodino():
    A = Asset('arr_comodino')
    wood = A.bm('A_noce')
    br = A.bm('A_ottone')
    W, D, H = 0.5, 0.42, 0.55
    for sx in (-1, 1):
        for sy in (-1, 1):
            box(wood, V(sx * (W / 2 - 0.03), sy * (D / 2 - 0.03), 0.07), V(0.045, 0.045, 0.14), bevel=0.006, seg=1)
    box(wood, V(0, 0, 0.33), V(W - 0.04, D - 0.04, 0.42), bevel=0.008, seg=1)
    box(wood, V(0, 0, H - 0.02), V(W, D, 0.04), bevel=0.01, seg=2)
    box(wood, V(0, D / 2 - 0.015, 0.40), V(W - 0.12, 0.02, 0.13), bevel=0.004, seg=1)        # cassetto alto
    box(wood, V(0, D / 2 - 0.015, 0.22), V(W - 0.12, 0.02, 0.15), bevel=0.004, seg=1)       # sportello
    sphere(br, V(0, D / 2 + 0.01, 0.40), 0.014, 8)
    sphere(br, V(0.08, D / 2 + 0.01, 0.22), 0.014, 8)
    return A


def armadio():
    A = Asset('arr_armadio')
    wood = A.bm('A_noce')
    br = A.bm('A_ottone')
    felt = A.bm('A_fondo')
    W, D, H = 1.3, 0.62, 2.2
    # corpo: fiancate, schiena, ripiano alto; zoccolo e cornice
    box(wood, V(0, 0, 0.07), V(W, D, 0.14), bevel=0.01, seg=1)
    for sx in (-1, 1):
        box(wood, V(sx * (W / 2 - 0.02), 0.0, 1.17), V(0.04, D - 0.02, 2.06), bevel=0.004, seg=1)
    box(wood, V(0, -D / 2 + 0.02, 1.17), V(W - 0.04, 0.04, 2.06), bevel=0.004, seg=1)
    box(felt, V(0, -D / 2 + 0.045, 1.12), V(W - 0.10, 0.01, 1.9))
    box(wood, V(0, 0, 2.2 - 0.06), V(W + 0.06, D + 0.04, 0.10), bevel=0.012, seg=2)
    box(wood, V(0, 0, 2.2 - 0.0), V(W + 0.10, D + 0.08, 0.04), bevel=0.01, seg=2)
    box(wood, V(0, 0, 0.55), V(W - 0.06, D - 0.1, 0.03))
    # anta destra chiusa con specchio e pannelli; anta sinistra aperta di 38 gradi
    dw = W / 2 - 0.03
    def door(x_hinge, sgn, angle):
        pts = []
        # costruisco l'anta nel suo riferimento (cerniera sull'origine) e la ruoto attorno a Z
        d = bmesh.new()
        box(d, V(sgn * dw / 2, 0.0, 1.17), V(dw, 0.035, 1.98), bevel=0.004, seg=1)
        box(d, V(sgn * dw / 2, 0.02, 1.62), V(dw - 0.16, 0.012, 1.0), bevel=0.003, seg=1)
        box(d, V(sgn * dw / 2, 0.02, 0.62), V(dw - 0.16, 0.012, 0.50), bevel=0.003, seg=1)
        box(d, V(sgn * (dw - 0.08), 0.03, 1.15), V(0.02, 0.02, 0.18), bevel=0.003, seg=1)
        M = Matrix.Translation(Vector((x_hinge, D / 2 - 0.02, 0.0))) @ Matrix.Rotation(angle, 4, 'Z')
        bmesh.ops.transform(d, matrix=M, verts=d.verts[:])
        # versa le facce nel bmesh del legno
        mp = {}
        for v in d.verts:
            mp[v] = wood.verts.new(v.co)
        for f in d.faces:
            wood.faces.new([mp[v] for v in f.verts])
        d.free()
    door(W / 2 - 0.02, -1, 0.0)
    door(-(W / 2 - 0.02), 1, radians(-38))
    # maniglie
    sphere(br, V(0.06, D / 2 + 0.03, 1.15), 0.02, 8)
    return A


def scrivania():
    A = Asset('arr_scrivania')
    wood = A.bm('A_noce')
    leather = A.bm('A_cuoio')
    br = A.bm('A_ottone')
    W, D, H = 1.25, 0.62, 0.76
    box(wood, V(0, 0, H - 0.02), V(W, D, 0.04), bevel=0.008, seg=2)
    box(leather, V(0, 0, H + 0.001), V(W - 0.12, D - 0.12, 0.004))
    # cassettiera a destra, gambe a sinistra
    box(wood, V(W / 2 - 0.22, 0.0, 0.37), V(0.40, D - 0.06, 0.70), bevel=0.006, seg=1)
    for i in range(3):
        z = 0.56 - i * 0.2
        box(wood, V(W / 2 - 0.22, D / 2 - 0.0, z), V(0.34, 0.02, 0.17), bevel=0.004, seg=1)
        sphere(br, V(W / 2 - 0.22, D / 2 + 0.02, z), 0.014, 8)
    for sy in (-1, 1):
        box(wood, V(-W / 2 + 0.05, sy * (D / 2 - 0.05), 0.37), V(0.06, 0.06, 0.70), bevel=0.008, seg=1)
    box(wood, V(-W / 2 + 0.05, 0.0, 0.66), V(0.04, D - 0.1, 0.07))
    return A


def sedia():
    A = Asset('arr_sedia')
    wood = A.bm('A_noce')
    vel = A.bm('A_velluto')
    for sx in (-1, 1):
        for sy in (-1, 1):
            h = 0.88 if sy < 0 else 0.45
            box(wood, V(sx * 0.20, sy * 0.20, h / 2), V(0.035, 0.035, h), bevel=0.005, seg=1)
    box(wood, V(0, 0, 0.43), V(0.43, 0.43, 0.04), bevel=0.006, seg=1)
    rounded_grid(vel, V(0, 0.0, 0.47), V(0.40, 0.40, 0.06), 0.025, nx=8, ny=8, nz=3)
    box(wood, V(0, -0.20, 0.86), V(0.40, 0.035, 0.06), bevel=0.012, seg=2)
    box(wood, V(0, -0.20, 0.64), V(0.40, 0.03, 0.04), bevel=0.004, seg=1)
    for x in (-0.12, 0.0, 0.12):
        box(wood, V(x, -0.20, 0.75), V(0.025, 0.025, 0.18), bevel=0.004, seg=1)
    return A


def tavolo():
    A = Asset('arr_tavolo')
    wood = A.bm('A_noce')
    cloth = A.bm('A_lenzuolo')
    br = A.bm('A_ottone')
    box(wood, V(0, 0, 0.74), V(0.95, 0.95, 0.04), bevel=0.01, seg=1)
    lathe(wood, [(0.0, 0.0), (0.22, 0.0), (0.22, 0.04), (0.08, 0.10), (0.06, 0.6), (0.12, 0.7), (0.0, 0.72)], segs=12, closed_bottom=False)
    # tovaglia: piano e quattro lembi che pendono, sporca e un po' storta
    box(cloth, V(0, 0, 0.765), V(0.96, 0.96, 0.012), bevel=0.003, seg=1)
    for sx, sy, ww, dd in ((1, 0, 0.012, 0.96), (-1, 0, 0.012, 0.96), (0, 1, 0.96, 0.012), (0, -1, 0.96, 0.012)):
        drop = 0.34 if (sx, sy) != (0, -1) else 0.22
        box(cloth, V(sx * 0.475, sy * 0.475, 0.765 - drop / 2), V(ww if ww > 0.5 else 0.012, dd if dd > 0.5 else 0.012, drop), bevel=0.002, seg=1)
    # candeliere al centro
    lathe(br, [(0.0, 0.0), (0.045, 0.0), (0.02, 0.03), (0.012, 0.15), (0.03, 0.17), (0.0, 0.18)], c=V(0, 0, 0.77), segs=10, closed_bottom=False)
    return A


def pianoforte():
    A = Asset('arr_pianoforte')
    lac = A.bm('A_lacca')
    white = A.bm('A_porcellana')
    blk = A.bm('A_scuro')
    br = A.bm('A_ottone')
    # profilo a coda in pianta (x larghezza, y lunghezza): fianco dritto a sinistra, curva a destra, coda in fondo
    Wd, Ln = 1.45, 2.1
    y_front, y_back = Ln / 2 - 0.08, -Ln / 2
    xt = -Wd / 2 + 0.55
    pts = [(-Wd / 2, y_front), (-Wd / 2, y_back + 0.30), (-Wd / 2 + 0.08, y_back + 0.08), (-Wd / 2 + 0.25, y_back), (xt, y_back)]
    n = 16
    for i in range(1, n + 1):
        t = i / n
        k = min(1.0, t / 0.72)
        x = xt + (Wd / 2 - xt) * (1 - (1 - k) ** 2)
        pts.append((x, y_back + (y_front - y_back) * t))
    poly_prism(lac, pts, 'z', 0.68, 0.93)
    # coperchio aperto: lastra inclinata sul lato sinistro, sostenuta da un'asta
    lid = [(-Wd / 2, y_front), (-Wd / 2, y_back + 0.5), (-Wd / 2 + 0.05, y_back + 0.12), (-Wd / 2 + 0.25, y_back), (Wd / 2 - 0.1, y_back + 0.9), (Wd / 2, y_front - 0.5)]
    # in pianta la lastra e' la stessa forma; ruotata attorno al bordo sinistro
    d = bmesh.new()
    poly_prism(d, lid, 'z', 0.0, 0.025)
    M = Matrix.Translation(V(-Wd / 2, 0, 0.93)) @ Matrix.Rotation(radians(-40), 4, 'Y') @ Matrix.Translation(V(Wd / 2, 0, 0))
    bmesh.ops.transform(d, matrix=M, verts=d.verts[:])
    mp = {v: lac.verts.new(v.co) for v in d.verts}
    for f in d.faces:
        lac.faces.new([mp[v] for v in f.verts])
    d.free()
    cyl(blk, V(Wd / 2 - 0.45, y_back + 0.9, 0.93), V(Wd / 2 - 0.45 - 0.35, y_back + 0.9, 1.40), 0.01, 6)
    # tastiera: 36 tasti bianchi e neri, e il leggio
    kx0 = -0.58
    for i in range(36):
        x = kx0 + i * 0.0325
        box(white, V(x, y_front + 0.07, 0.69 + 0.04), V(0.0305, 0.25, 0.026), bevel=0.003, seg=1)
    pattern = [1, 1, 0, 1, 1, 1, 0]
    for i in range(36):
        if pattern[i % 7]:
            box(blk, V(kx0 + i * 0.0325 + 0.0163, y_front + 0.02, 0.69 + 0.065), V(0.018, 0.15, 0.028), bevel=0.002, seg=1)
    box(lac, V(0, y_front - 0.02, 0.69 + 0.02), V(1.28, 0.06, 0.06))
    box(lac, V(0, y_front + 0.23, 0.69 + 0.035), V(1.30, 0.12, 0.06))
    box(lac, V(0.0, y_front - 0.20, 1.02), V(0.7, 0.03, 0.26), rot=Matrix.Rotation(radians(-18), 3, 'X'))
    # gambe a rocchetto e lira dei pedali
    for (x, y) in ((-Wd / 2 + 0.07, y_front - 0.08), (Wd / 2 - 0.1, y_front - 0.08), (-Wd / 2 + 0.2, y_back + 0.22)):
        lathe(lac, [(0.0, 0.0), (0.04, 0.0), (0.045, 0.06), (0.03, 0.12), (0.055, 0.3), (0.07, 0.5), (0.07, 0.68), (0.0, 0.68)], c=V(x, y, 0.0), segs=10, closed_bottom=False)
    box(lac, V(0, y_front - 0.15, 0.30), V(0.22, 0.05, 0.5))
    for dx in (-0.07, 0.0, 0.07):
        box(br, V(dx, y_front - 0.08, 0.06), V(0.04, 0.12, 0.012))
    return A


def fornello():
    A = Asset('arr_fornello')
    st = A.bm('A_acciaio')
    dk = A.bm('A_scuro')
    W, D, H = 1.7, 0.85, 2.2
    box(st, V(0, 0, 0.45), V(W, D, 0.9), bevel=0.01, seg=1)
    box(st, V(0, 0, 0.92), V(W + 0.02, D + 0.02, 0.04), bevel=0.006, seg=1)
    box(dk, V(0, D / 2 + 0.002, 0.4), V(W - 0.2, 0.01, 0.52))       # fronte dei forni
    for sx in (-1, 1):
        box(st, V(sx * 0.4, D / 2 + 0.012, 0.42), V(0.74, 0.02, 0.46), bevel=0.004, seg=1)
        cyl(st, V(sx * 0.4 - 0.3, D / 2 + 0.06, 0.64), V(sx * 0.4 + 0.3, D / 2 + 0.06, 0.64), 0.012, 8)
    for i in range(6):
        x = -0.65 + (i % 3) * 0.65
        y = -0.18 + (i // 3) * 0.38
        lathe(dk, [(0.0, 0.0), (0.13, 0.0), (0.12, 0.02), (0.05, 0.03), (0.0, 0.035)], c=V(x, y, 0.94), segs=14)
        cyl(st, V(x, y, 0.94), V(x, y, 0.975), 0.05, 12)
    for i in range(6):
        sphere(st, V(-0.72 + i * 0.29, D / 2 + 0.03, 0.85), 0.03, 8)
    # alzata e cappa
    box(st, V(0, -D / 2 + 0.04, 1.25), V(W, 0.04, 0.6))
    poly_prism(st, [(-W / 2, 1.55), (W / 2, 1.55), (0.38, 2.0), (-0.38, 2.0)], 'y', -D / 2 + 0.0, D / 2 - 0.05)
    box(st, V(0, -D / 2 + 0.2, 2.1), V(0.6, 0.38, 0.2))
    return A


def tavolo_acciaio():
    A = Asset('arr_tavolo_acciaio')
    st = A.bm('A_acciaio')
    W, D, H = 1.9, 0.8, 0.9
    box(st, V(0, 0, H - 0.025), V(W, D, 0.05), bevel=0.01, seg=1)
    box(st, V(0, D / 2 - 0.02, H - 0.08), V(W - 0.04, 0.03, 0.08))
    box(st, V(0, -D / 2 + 0.02, H - 0.08), V(W - 0.04, 0.03, 0.08))
    for sx in (-1, 1):
        for sy in (-1, 1):
            cyl(st, V(sx * (W / 2 - 0.05), sy * (D / 2 - 0.05), 0.0), V(sx * (W / 2 - 0.05), sy * (D / 2 - 0.05), H - 0.05), 0.022, 8)
    box(st, V(0, 0, 0.22), V(W - 0.14, D - 0.14, 0.03))
    return A


def scaffale():
    A = Asset('arr_scaffale')
    wood = A.bm('A_noce')
    iron = A.bm('A_ferro')
    box_ = A.bm('A_cuoio')
    W, D, H = 1.2, 0.45, 1.9
    for sx in (-1, 1):
        for sy in (-1, 1):
            box(iron, V(sx * (W / 2 - 0.02), sy * (D / 2 - 0.02), H / 2), V(0.035, 0.035, H), bevel=0.004, seg=1)
    rnd = random.Random(8)
    for i in range(5):
        z = 0.12 + i * 0.42
        box(wood, V(0, 0, z), V(W - 0.04, D - 0.02, 0.035), bevel=0.004, seg=1)
        x = -W / 2 + 0.12
        while x < W / 2 - 0.15:
            if rnd.random() < 0.7:
                bw = rnd.uniform(0.14, 0.26)
                bh = rnd.uniform(0.12, 0.28)
                box(box_, V(x + bw / 2, rnd.uniform(-0.04, 0.04), z + 0.0175 + bh / 2), V(bw, D - 0.14, bh), bevel=0.004, seg=1, rot=rot_z(rnd.uniform(-0.08, 0.08)))
                x += bw + 0.04
            else:
                x += 0.15
    return A


ASSETS = {
    'arr_letto': letto, 'arr_comodino': comodino, 'arr_armadio': armadio, 'arr_scrivania': scrivania, 'arr_sedia': sedia,
    'arr_tavolo': tavolo, 'arr_pianoforte': pianoforte, 'arr_fornello': fornello, 'arr_tavolo_acciaio': tavolo_acciaio,
    'arr_scaffale': scaffale,
}
VIEWS = {
    'arr_letto': ((3.2, 3.8, 2.0), (0, 0, 0.5), 40), 'arr_comodino': ((1.3, 1.6, 0.9), (0, 0, 0.3), 40),
    'arr_armadio': ((2.6, 3.2, 1.8), (0, 0, 1.0), 40), 'arr_scrivania': ((2.0, 2.2, 1.4), (0, 0, 0.4), 40),
    'arr_sedia': ((1.4, 1.6, 1.2), (0, 0, 0.45), 40), 'arr_tavolo': ((2.0, 2.2, 1.4), (0, 0, 0.4), 40),
    'arr_pianoforte': ((3.4, 4.0, 2.3), (0, 0, 0.7), 38), 'arr_fornello': ((2.9, 3.5, 2.1), (0, 0, 1.0), 38),
    'arr_tavolo_acciaio': ((2.6, 3.0, 1.6), (0, 0, 0.45), 40), 'arr_scaffale': ((2.2, 2.9, 1.7), (0, 0, 0.9), 38),
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
