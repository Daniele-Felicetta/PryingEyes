"""
Arredi dei temi (fase 3): teatro (file di poltrone a ribalta in tre varianti, toeletta con specchio a lampadine, manichino da
sarta, faro su treppiede) e sanatorio (letto d'ospedale di ferro, carrello dei medicinali, asta per la flebo, sedia a rotelle,
lampada scialitica, paravento). Stessi attrezzi e stessa struttura di genera_arredi2.py (un Asset = una mesh per ruolo di
materiale, UV in metri). Il ferro verniciato e arrugginito ha il ruolo A_ruggine (texture ruggine di genera_texture.py).

Uso (dalla cartella del file):
  blender -b -P genera_arredi3.py -- build [nome ...]    FBX in arredi3/<nome>.fbx (poi da copiare in Assets/_PryingEyes/Models/Arredi)
  blender -b -P genera_arredi3.py -- render [nome ...]   anteprime in arredi3/render/<nome>.png (su CPU: non toglie la GPU a chi gioca;
                                                         ARREDI_GPU=1 per usare la GPU)
Assi: Z in alto, davanti = +Y (Unity +Z), origine al centro a terra (la lampada scialitica: al fondo del disco). Misure uguali a PropDefs.cs.
"""
import bpy, bmesh, math, os, sys, random
from mathutils import Vector, Matrix, noise
from math import sin, cos, pi, radians, sqrt

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, '..', 'chiesa'))
import genera_arredi as ga
import genera_chiesa as gc          # ruoli del ferro, dell'acciaio...; poly_prism
from genera_arredi import V, box, rounded_grid, lathe, sphere, cyl, tube, Asset, smoothstep
from genera_chiesa import poly_prism

OUT = os.path.join(HERE, 'arredi3')

# ruoli nuovi per le anteprime (in Unity li sostituisce TorreBuilder coi materiali del kit)
ga.ROLE.update({
    'A_ruggine': ((0.52, 0.50, 0.46), 0.15, 0.55), 'A_gomma': ((0.03, 0.03, 0.035), 0.0, 0.45),
    'A_specchio': ((0.50, 0.55, 0.57), 1.0, 0.06), 'A_lampadina': ((1.0, 0.82, 0.55), 0.0, 0.2),
    'A_neon': ((0.85, 0.95, 0.9), 0.0, 0.2),
})
_prev_material = ga.material


def _material(role):
    fresh = not (role in ga._mats and ga._mats[role].name in bpy.data.materials)
    m = _prev_material(role)
    if fresh and role in ('A_lampadina', 'A_neon'):
        bs = m.node_tree.nodes['Principled BSDF']
        bs.inputs['Emission Color'].default_value = (1.0, 0.72, 0.4, 1) if role == 'A_lampadina' else (0.8, 0.95, 0.9, 1)
        bs.inputs['Emission Strength'].default_value = 5.0
    return m


ga.material = _material


# ----------------------------------------------------------------------------- attrezzi
def merge(dst, src, M=None):
    """Copia le facce di un bmesh a parte dentro 'dst', dopo averle trasformate con M (4x4). Libera 'src'."""
    if M is not None:
        bmesh.ops.transform(src, matrix=M, verts=src.verts[:])
    mp = {v: dst.verts.new(v.co) for v in src.verts}
    for f in src.faces:
        dst.faces.new([mp[v] for v in f.verts])
    src.free()


def rotx(a, p):
    """Rotazione di 'a' radianti attorno all'asse X passante per il punto p."""
    return Matrix.Translation(p) @ Matrix.Rotation(a, 4, 'X') @ Matrix.Translation(-p)


def rotz(a, p=V(0, 0, 0)):
    return Matrix.Translation(p) @ Matrix.Rotation(a, 4, 'Z') @ Matrix.Translation(-p)


def torus_x(bm, c, R, r, segs=32, rs=8):
    """Toro con l'asse lungo X (ruote): anello nel piano YZ."""
    rings = []
    for i in range(segs):
        a = 2 * pi * i / segs
        ring = []
        for j in range(rs):
            b = 2 * pi * j / rs
            rr = R + r * cos(b)
            ring.append(bm.verts.new(Vector(c) + V(r * sin(b), cos(a) * rr, sin(a) * rr)))
        rings.append(ring)
    for i in range(segs):
        for j in range(rs):
            bm.faces.new((rings[i][j], rings[(i + 1) % segs][j], rings[(i + 1) % segs][(j + 1) % rs], rings[i][(j + 1) % rs]))


def wheel_x(tire, rim, spoke, cx, cy, cz, R, tw=0.03, spokes=18):
    """Ruota con asse X: copertone di gomma, cerchio, raggi sottili e mozzo."""
    torus_x(tire, V(cx, cy, cz), R - tw * 0.5, tw * 0.5, segs=28, rs=8)
    torus_x(rim, V(cx, cy, cz), R - tw - 0.006, 0.006, segs=28, rs=6)
    cyl(rim, V(cx - 0.02, cy, cz), V(cx + 0.02, cy, cz), 0.03, 10)
    for i in range(spokes):
        a = 2 * pi * i / spokes
        cyl(spoke, V(cx, cy + cos(a) * 0.03, cz + sin(a) * 0.03), V(cx, cy + cos(a) * (R - tw - 0.006), cz + sin(a) * (R - tw - 0.006)), 0.0016, 4, cap=False)


def caster(tire, fork, x, y, r=0.04, w=0.025, h_top=None):
    """Rotella piroettante: ruota di gomma sull'asse X e forcella d'acciaio."""
    cyl(tire, V(x - w / 2, y, r), V(x + w / 2, y, r), r, 12)
    h_top = h_top if h_top is not None else r * 2 + 0.03
    for sx in (-1, 1):
        box(fork, V(x + sx * (w / 2 + 0.004), y, r + (h_top - r) / 2), V(0.006, 0.03, h_top - r), bevel=0.0, seg=1)
    box(fork, V(x, y, h_top), V(w + 0.02, 0.04, 0.008))


# ============================================================================= TEATRO
SEAT_P = 0.58        # passo delle poltrone


def _standard(iron, wood, x):
    """Fianco in ghisa tra due poltrone: piede, montante, puntone dello schienale e sostegno del bracciolo (piano YZ)."""
    t = 0.018
    poly_prism(iron, [(-0.30, 0.0), (0.30, 0.0), (0.30, 0.03), (0.04, 0.05), (-0.04, 0.05), (-0.30, 0.03)], 'x', x - t, x + t)
    poly_prism(iron, [(-0.045, 0.045), (0.045, 0.045), (0.03, 0.44), (-0.03, 0.44)], 'x', x - t * 0.8, x + t * 0.8)
    poly_prism(iron, [(-0.19, 0.40), (-0.145, 0.40), (-0.255, 0.935), (-0.30, 0.935)], 'x', x - t * 0.7, x + t * 0.7)
    poly_prism(iron, [(0.0, 0.40), (0.04, 0.42), (0.31, 0.63), (0.29, 0.67), (0.0, 0.47)], 'x', x - t * 0.7, x + t * 0.7)
    sphere(iron, V(x, 0.0, 0.29), 0.027, 8)
    box(wood, V(x, 0.15, 0.675), V(0.07, 0.40, 0.045), bevel=0.01, seg=2)          # bracciolo di legno


def _seat(vel, wood, brass, x, up=0.0, missing=False):
    """Seduta a ribalta: telaio e cuscino di velluto, ruotati attorno alla cerniera di 'up' radianti (80 gradi = ribaltata)."""
    M = rotx(up, V(0, -0.14, 0.42))
    pan = bmesh.new()
    box(pan, V(x, 0.10, 0.405), V(0.46, 0.50, 0.025), bevel=0.004, seg=1)
    merge(wood, pan, M)
    if missing:
        return
    cush = bmesh.new()
    vs = rounded_grid(cush, V(x, 0.10, 0.455), V(0.50, 0.52, 0.09), 0.032, nx=8, ny=8, nz=3)
    for v in vs:
        dx, dy = (v.co.x - x) / 0.25, (v.co.y - 0.10) / 0.26
        if v.co.z > 0.45:
            v.co.z += 0.016 * max(0.0, 1 - dx * dx) * max(0.0, 1 - dy * dy)
    merge(vel, cush, M)


def _back(vel, wood, brass, x, tilt):
    """Schienale imbottito con la cima ad arco, pannello di legno dietro e targhetta col numero."""
    M = Matrix.Translation(V(x, -0.255, 0.715)) @ Matrix.Rotation(tilt, 4, 'X')
    b = bmesh.new()
    vs = rounded_grid(b, V(0, 0, 0), V(0.50, 0.08, 0.47), 0.03, nx=8, ny=3, nz=8)
    for v in vs:
        if v.co.z > 0.0:
            v.co.z -= 0.04 * (v.co.x / 0.25) ** 2 * smoothstep(0.0, 0.235, v.co.z)
        if v.co.y > 0.0:
            v.co.y += 0.014 * max(0.0, 1 - (v.co.x / 0.25) ** 2) * max(0.0, 1 - (v.co.z / 0.235) ** 2)
    merge(vel, b, M)
    w = bmesh.new()
    box(w, V(0, -0.052, -0.01), V(0.46, 0.014, 0.42), bevel=0.003, seg=1)
    merge(wood, w, M)
    p = bmesh.new()
    box(p, V(0, -0.062, 0.16), V(0.05, 0.006, 0.026))
    merge(brass, p, M)


def fila(name, up=(0, 0, 0, 0), missing=(), tilts=(0.23, 0.23, 0.23, 0.23)):
    A = Asset(name)
    iron, wood, vel, brass = A.bm('A_ferro'), A.bm('A_legno'), A.bm('A_velluto'), A.bm('A_ottone')
    for k in range(5):
        _standard(iron, wood, (-2 + k) * SEAT_P)
    for i in range(4):
        x = (-1.5 + i) * SEAT_P
        _seat(vel, wood, brass, x, up[i], i in missing)
        _back(vel, wood, brass, x, tilts[i])
    return A


def toeletta():
    A = Asset('tea_toeletta')
    wood, br, mir, bulb, por = A.bm('A_legno'), A.bm('A_ottone'), A.bm('A_specchio'), A.bm('A_lampadina'), A.bm('A_porcellana')
    W, D = 1.2, 0.5
    box(wood, V(0, 0, 0.765), V(W, D, 0.04), bevel=0.008, seg=2)
    leg = [(0.026, 0.0), (0.032, 0.03), (0.024, 0.08), (0.022, 0.30), (0.036, 0.36), (0.030, 0.42), (0.022, 0.50), (0.034, 0.60), (0.040, 0.68), (0.034, 0.745)]
    for sx in (-1, 1):
        for sy in (-1, 1):
            lathe(wood, leg, c=V(sx * (W / 2 - 0.045), sy * (D / 2 - 0.045), 0.0), segs=10)
    # grembiule con due cassetti e pomelli d'ottone
    box(wood, V(0, D / 2 - 0.03, 0.70), V(W - 0.10, 0.03, 0.12), bevel=0.004, seg=1)
    box(wood, V(0, -D / 2 + 0.03, 0.70), V(W - 0.10, 0.03, 0.12), bevel=0.004, seg=1)
    for sx in (-1, 1):
        box(wood, V(sx * 0.27, D / 2 - 0.005, 0.70), V(0.40, 0.012, 0.09), bevel=0.003, seg=1)
        sphere(br, V(sx * 0.27, D / 2 + 0.012, 0.70), 0.014, 8)
    # specchio: cornice, vetro annerito e fila di lampadine tutt'attorno
    mw, mh, mz, my = 1.0, 0.84, 1.30, -D / 2 + 0.06
    box(wood, V(0, my - 0.02, mz), V(mw + 0.16, 0.04, mh + 0.16), bevel=0.01, seg=2)
    box(mir, V(0, my + 0.004, mz), V(mw, 0.012, mh))
    for i in range(8):
        x = -mw / 2 - 0.02 + (mw + 0.04) * i / 7
        for zz in (mz + mh / 2 + 0.045, mz - mh / 2 - 0.045):
            cyl(br, V(x, my + 0.012, zz), V(x, my + 0.03, zz), 0.014, 8)
            sphere(bulb, V(x, my + 0.045, zz), 0.024, 8)
    for i in range(1, 6):
        z = mz - mh / 2 - 0.045 + (mh + 0.09) * i / 6
        for xx in (-mw / 2 - 0.045, mw / 2 + 0.045):
            cyl(br, V(xx, my + 0.012, z), V(xx, my + 0.03, z), 0.014, 8)
            sphere(bulb, V(xx, my + 0.045, z), 0.024, 8)
    # sul piano: un vasetto di cipria, un flacone e una spazzola
    lathe(por, [(0.0, 0.0), (0.05, 0.0), (0.055, 0.03), (0.05, 0.05), (0.0, 0.052)], c=V(-0.30, 0.0, 0.785), segs=14)
    lathe(br, [(0.0, 0.0), (0.045, 0.0), (0.045, 0.012), (0.0, 0.013)], c=V(-0.30, 0.0, 0.835), segs=14)
    lathe(por, [(0.0, 0.0), (0.035, 0.0), (0.038, 0.07), (0.016, 0.11), (0.012, 0.15), (0.0, 0.15)], c=V(0.25, -0.05, 0.785), segs=10)
    box(wood, V(0.38, 0.05, 0.79), V(0.05, 0.16, 0.014), bevel=0.004, seg=1, rot=Matrix.Rotation(0.4, 3, 'Z'))
    return A


def manichino():
    A = Asset('tea_manichino')
    lin, wood, br = A.bm('A_lenzuolo'), A.bm('A_legno'), A.bm('A_ottone')
    # busto da sarta: collo, spalle, seno, vita, fianchi; rivestito di tela
    prof = [(0.0, 0.76), (0.11, 0.765), (0.15, 0.80), (0.165, 0.86), (0.158, 0.93), (0.132, 1.01), (0.125, 1.06), (0.145, 1.12), (0.168, 1.20), (0.170, 1.28),
            (0.175, 1.34), (0.150, 1.42), (0.100, 1.47), (0.058, 1.49), (0.050, 1.55), (0.0, 1.57)]
    rings = lathe(lin, prof, c=V(0, 0, 0), segs=28, closed_bottom=True)
    for ring in rings:
        for v in (ring if len(ring) > 1 else []):
            v.co.x *= 1.12            # un po' piu' largo che profondo
    # collo con pomello d'ottone, asta di legno e treppiede
    sphere(br, V(0, 0, 1.60), 0.032, 10)
    cyl(wood, V(0, 0, 0.14), V(0, 0, 0.78), 0.013, 10)
    cyl(wood, V(0, 0, 0.50), V(0, 0, 0.56), 0.024, 10)
    for k in range(3):
        a = 2 * pi * k / 3 + 0.3
        ex, ey = cos(a) * 0.25, sin(a) * 0.25
        cyl(wood, V(0, 0, 0.20), V(ex, ey, 0.03), 0.013, 8)
        sphere(wood, V(ex, ey, 0.025), 0.02, 8)
    box(wood, V(0, 0, 0.20), V(0.06, 0.06, 0.06), bevel=0.01, seg=1)
    # spilli e un centimetro da sarta appeso alla spalla
    for (x, z) in ((0.06, 1.26), (-0.02, 1.18), (0.09, 1.10)):
        sphere(br, V(x, 0.16, z), 0.006, 6)
    return A


def faro():
    A = Asset('tea_faro')
    iron, st, gl = A.bm('A_ferro'), A.bm('A_acciaio'), A.bm('A_vetro')
    # treppiede, colonna telescopica, forcella e proiettore Fresnel inclinato in avanti
    for k in range(3):
        a = 2 * pi * k / 3 + 0.5
        cyl(st, V(0, 0, 1.0), V(cos(a) * 0.42, sin(a) * 0.42, 0.02), 0.014, 8)
        sphere(iron, V(cos(a) * 0.42, sin(a) * 0.42, 0.02), 0.024, 8)
    cyl(st, V(0, 0, 0.30), V(0, 0, 1.30), 0.026, 12)
    cyl(st, V(0, 0, 1.25), V(0, 0, 1.78), 0.018, 12)
    box(iron, V(0, 0, 1.0), V(0.07, 0.07, 0.10), bevel=0.01, seg=1)
    cx, cz = 0.0, 1.78
    tilt = radians(-24)
    can = bmesh.new()
    lathe(can, [(0.0, -0.16), (0.10, -0.16), (0.125, -0.10), (0.13, 0.12), (0.125, 0.19), (0.0, 0.19)], c=V(0, 0, 0), segs=22, axis='Y')
    for i in range(6):
        lathe(can, [(0.13, -0.14 + i * 0.024), (0.145, -0.136 + i * 0.024), (0.13, -0.126 + i * 0.024)], c=V(0, 0, 0), segs=22, axis='Y', closed_top=False, closed_bottom=False)
    merge(iron, can, Matrix.Translation(V(0, 0, cz)) @ Matrix.Rotation(tilt, 4, 'X'))
    lens = bmesh.new()
    lathe(lens, [(0.0, 0.19), (0.11, 0.19), (0.115, 0.215), (0.0, 0.24)], c=V(0, 0, 0), segs=22, axis='Y')
    merge(gl, lens, Matrix.Translation(V(0, 0, cz)) @ Matrix.Rotation(tilt, 4, 'X'))
    for sx in (-1, 1):
        box(iron, V(sx * 0.15, 0.0, cz), V(0.02, 0.07, 0.30), bevel=0.004, seg=1)       # forcella
    # alette: quattro lamiere aperte davanti al proiettore
    for (dx, dz, vertical) in ((0.14, 0.0, True), (-0.14, 0.0, True), (0.0, 0.14, False), (0.0, -0.14, False)):
        fl = bmesh.new()
        box(fl, V(dx, 0.30, dz), V(0.01, 0.14, 0.26) if vertical else V(0.26, 0.14, 0.01), bevel=0.0, seg=1)
        merge(iron, fl, Matrix.Translation(V(0, 0, cz)) @ Matrix.Rotation(tilt, 4, 'X'))
    return A


# ============================================================================= SANATORIO
def letto_ferro():
    A = Asset('san_letto')
    fe, mat, sheet, gum, steel = A.bm('A_ruggine'), A.bm('A_lenzuolo'), A.bm('A_lenzuolo'), A.bm('A_gomma'), A.bm('A_acciaio')
    W, L = 0.95, 2.05
    hx = 0.44
    y_head, y_foot = -L / 2 + 0.03, L / 2 - 0.03
    r = 0.016
    # testiera (alta) e piediera (bassa): montanti, due traverse ad arco, fusi verticali
    for yy, hh, mid in ((y_head, 1.05, 0.66), (y_foot, 0.80, 0.52)):
        for sx in (-1, 1):
            cyl(fe, V(sx * hx, yy, 0.10), V(sx * hx, yy, hh), r, 10)
            sphere(fe, V(sx * hx, yy, hh + 0.008), r * 1.35, 8)
        tube(fe, [V(-hx, yy, hh - 0.02), V(0, yy, hh + 0.015), V(hx, yy, hh - 0.02)], [r * 0.9, r * 0.9, r * 0.9], segs=8, n=12)
        cyl(fe, V(-hx, yy, mid), V(hx, yy, mid), r * 0.8, 8)
        cyl(fe, V(-hx, yy, 0.30), V(hx, yy, 0.30), r * 0.8, 8)
        n = 9
        for i in range(1, n):
            x = -hx + 2 * hx * i / n
            cyl(fe, V(x, yy, mid), V(x, yy, hh - 0.03), 0.0075, 6)
            cyl(fe, V(x, yy, 0.30), V(x, yy, mid), 0.0075, 6)
    # telaio con le doppie rotaie, rete a molle (griglia di fili), traverse
    for sx in (-1, 1):
        cyl(fe, V(sx * hx, y_head, 0.43), V(sx * hx, y_foot, 0.43), 0.014, 8)
        cyl(fe, V(sx * (hx - 0.02), y_head, 0.37), V(sx * (hx - 0.02), y_foot, 0.37), 0.01, 6)
    for i in range(10):
        y = y_head + (y_foot - y_head) * (i + 0.5) / 10
        cyl(fe, V(-hx, y, 0.43), V(hx, y, 0.43), 0.0055, 5)
    for i in range(1, 9):
        x = -hx + 2 * hx * i / 9
        cyl(fe, V(x, y_head, 0.435), V(x, y_foot, 0.435), 0.0045, 5)
    # rotelle ai quattro angoli
    for sx in (-1, 1):
        for yy in (y_head, y_foot):
            cyl(fe, V(sx * hx, yy, 0.08), V(sx * hx, yy, 0.10), 0.02, 8)
            caster(gum, steel, sx * hx, yy, r=0.04, w=0.026)
    # materasso rigato, lenzuolo sfatto, cuscino
    rounded_grid(mat, V(0, 0.0, 0.50), V(0.86, 1.90, 0.12), 0.04, nx=10, ny=22, nz=3)
    sh = rounded_grid(sheet, V(0, 0.20, 0.585), V(0.84, 1.40, 0.05), 0.02, nx=14, ny=26, nz=2)
    for v in sh:
        if v.co.z > 0.59:
            v.co.z += 0.030 * noise.noise(Vector((v.co.x * 4.0, v.co.y * 3.0, 0.5))) + 0.014 * noise.noise(Vector((v.co.x * 11.0, v.co.y * 9.0, 2.0)))
    rounded_grid(sheet, V(0.0, -0.78, 0.63), V(0.58, 0.36, 0.14), 0.06, nx=10, ny=8, nz=4)
    return A


def carrello_medico():
    A = Asset('san_carrello')
    st, gum, gl, por, lin = A.bm('A_acciaio'), A.bm('A_gomma'), A.bm('A_vetro'), A.bm('A_porcellana'), A.bm('A_lenzuolo')
    W, D, H = 0.76, 0.46, 0.88
    hx, hy = W / 2 - 0.03, D / 2 - 0.03
    for sx in (-1, 1):
        for sy in (-1, 1):
            cyl(st, V(sx * hx, sy * hy, 0.09), V(sx * hx, sy * hy, H), 0.012, 8)
            caster(gum, st, sx * hx, sy * hy, r=0.04, w=0.024, h_top=0.09)
    for z, h in ((0.20, 0.022), (H - 0.02, 0.026)):
        box(st, V(0, 0, z), V(W, D, h), bevel=0.006, seg=1)
        for sx in (-1, 1):
            box(st, V(sx * (W / 2 - 0.004), 0, z + 0.016), V(0.008, D, 0.03))
        for sy in (-1, 1):
            box(st, V(0, sy * (D / 2 - 0.004), z + 0.016), V(W, 0.008, 0.03))
    # maniglia sul lato corto posteriore: due montanti e la barra
    for sx in (-1, 1):
        cyl(st, V(sx * hx, -hy, H), V(sx * hx, -hy - 0.08, H + 0.10), 0.011, 8)
    cyl(st, V(-hx, -hy - 0.08, H + 0.10), V(hx, -hy - 0.08, H + 0.10), 0.012, 8)
    # sul piano: una bacinella, flaconi, una siringa e una garza arrotolata; sotto, un secchio
    lathe(por, [(0.0, 0.0), (0.07, 0.0), (0.11, 0.025), (0.12, 0.045), (0.105, 0.045), (0.06, 0.012), (0.0, 0.01)], c=V(-0.18, 0.04, H - 0.006), segs=16)
    for x, h, r in ((0.05, 0.14, 0.025), (0.12, 0.10, 0.022), (0.18, 0.17, 0.020)):
        lathe(gl, [(0.0, 0.0), (r, 0.0), (r, h * 0.7), (r * 0.45, h * 0.86), (r * 0.4, h), (0.0, h)], c=V(x, 0.05, H - 0.006), segs=10)
    cyl(gl, V(0.0, -0.08, H - 0.0), V(0.12, -0.10, H - 0.0), 0.007, 6)
    cyl(lin, V(-0.02, 0.10, H + 0.012), V(0.07, 0.10, H + 0.012), 0.022, 10)
    lathe(st, [(0.0, 0.0), (0.10, 0.0), (0.12, 0.22), (0.125, 0.23), (0.112, 0.23), (0.0, 0.195)], c=V(0.18, 0.0, 0.21), segs=14, closed_top=False)
    return A


def flebo():
    A = Asset('san_flebo')
    fe, st, gum, gl, gm = A.bm('A_ruggine'), A.bm('A_acciaio'), A.bm('A_gomma'), A.bm('A_vetro'), A.bm('A_gomma')
    # base a cinque razze con rotelle, asta, anello di regolazione, asta superiore e ganci
    for k in range(5):
        a = 2 * pi * k / 5 + 0.2
        ex, ey = cos(a) * 0.27, sin(a) * 0.27
        cyl(fe, V(0, 0, 0.075), V(ex, ey, 0.065), 0.012, 8)
        caster(gum, st, ex, ey, r=0.03, w=0.02, h_top=0.065)
    cyl(fe, V(0, 0, 0.07), V(0, 0, 0.14), 0.035, 12)
    cyl(st, V(0, 0, 0.12), V(0, 0, 1.40), 0.013, 10)
    cyl(st, V(0, 0, 1.24), V(0, 0, 1.31), 0.022, 10)
    cyl(st, V(0, 0, 1.30), V(0, 0, 2.02), 0.009, 10)
    for k in range(4):
        a = pi / 2 * k + 0.4
        cyl(st, V(0, 0, 1.99), V(cos(a) * 0.09, sin(a) * 0.09, 2.03), 0.006, 6)
        sphere(st, V(cos(a) * 0.09, sin(a) * 0.09, 2.03), 0.009, 6)
    # sacca appesa a un gancio, con il liquido, e il deflussore che scende ad ansa
    bag = bmesh.new()
    rounded_grid(bag, V(0, 0, 0), V(0.13, 0.035, 0.22), 0.012, nx=6, ny=3, nz=8)
    merge(gl, bag, Matrix.Translation(V(0.09, 0.0, 1.88)) @ Matrix.Rotation(0.12, 4, 'Z'))
    cyl(st, V(0.09, 0.0, 1.99), V(0.09, 0.0, 2.02), 0.005, 6)
    tube(gm, [V(0.09, 0.0, 1.77), V(0.10, 0.03, 1.55), V(0.05, 0.10, 1.25), V(0.0, 0.07, 1.00), V(0.12, 0.14, 0.95)], [0.0032, 0.0032, 0.0032, 0.0032, 0.0032], segs=6, n=32)
    return A


def sedia_rotelle():
    A = Asset('san_sedia_ruote')
    fe, st, gum, spoke, vin = A.bm('A_ruggine'), A.bm('A_acciaio'), A.bm('A_gomma'), A.bm('A_acciaio'), A.bm('A_cuoio')
    hx = 0.235
    # ruote posteriori grandi (con corrimano) e piccole anteriori
    for sx in (-1, 1):
        wheel_x(gum, st, spoke, sx * 0.31, -0.12, 0.30, 0.30, tw=0.032)
        torus_x(st, V(sx * 0.345, -0.12, 0.30), 0.235, 0.0055, segs=24, rs=6)
        for k in range(4):
            a = pi / 2 * k + 0.3
            cyl(st, V(sx * 0.31, -0.12 + cos(a) * 0.235, 0.30 + sin(a) * 0.235), V(sx * 0.345, -0.12 + cos(a) * 0.235, 0.30 + sin(a) * 0.235), 0.004, 4)
        caster(gum, st, sx * 0.235, 0.36, r=0.075, w=0.03, h_top=0.20)
    # telaio: longheroni del sedile, montanti dello schienale con le impugnature, pedane
    for sx in (-1, 1):
        x = sx * hx
        cyl(fe, V(x, -0.26, 0.48), V(x, 0.24, 0.48), 0.012, 8)
        cyl(fe, V(x, -0.26, 0.48), V(x, -0.33, 0.93), 0.012, 8)
        cyl(gum, V(x, -0.33, 0.93), V(x, -0.44, 0.93), 0.017, 10)
        cyl(fe, V(x, -0.33, 0.93), V(x, -0.325, 0.915), 0.012, 6)
        cyl(fe, V(x, 0.24, 0.48), V(x, 0.34, 0.22), 0.011, 8)                    # tubo anteriore verso la forcella
        cyl(fe, V(x, 0.34, 0.22), V(x, 0.36, 0.20), 0.011, 8)
        cyl(fe, V(x, 0.34, 0.22), V(x, 0.44, 0.16), 0.010, 8)                    # pedana
        box(st, V(x, 0.47, 0.145), V(0.11, 0.16, 0.012), bevel=0.003, seg=1, rot=Matrix.Rotation(-0.25, 3, 'X'))
        cyl(fe, V(x, -0.30, 0.72), V(x, 0.05, 0.72), 0.011, 8)                   # bracciolo
        box(gum, V(x, -0.12, 0.745), V(0.055, 0.26, 0.026), bevel=0.006, seg=1)
        cyl(fe, V(x, 0.05, 0.72), V(x, 0.05, 0.48), 0.010, 8)
    cyl(fe, V(-hx, -0.26, 0.48), V(hx, -0.26, 0.48), 0.010, 8)
    cyl(fe, V(-hx, 0.24, 0.48), V(hx, 0.24, 0.48), 0.010, 8)
    cyl(fe, V(-hx, -0.30, 0.78), V(hx, -0.30, 0.78), 0.010, 8)
    cyl(fe, V(-hx, -0.26, 0.40), V(hx, 0.24, 0.30), 0.008, 6)                     # croce sotto il sedile
    cyl(fe, V(-hx, 0.24, 0.30), V(hx, -0.26, 0.40), 0.008, 6)
    # sedile e schienale in finta pelle, un po' afflosciati
    sv = rounded_grid(vin, V(0, -0.01, 0.47), V(0.44, 0.50, 0.035), 0.012, nx=10, ny=10, nz=2)
    for v in sv:
        v.co.z -= 0.025 * max(0.0, 1 - (v.co.x / 0.22) ** 2) * max(0.0, 1 - ((v.co.y + 0.01) / 0.25) ** 2) * (1 if v.co.z > 0.47 else 0.6)
    bk = bmesh.new()
    bv = rounded_grid(bk, V(0, 0, 0), V(0.44, 0.03, 0.40), 0.012, nx=10, ny=2, nz=10)
    for v in bv:
        v.co.y -= 0.02 * max(0.0, 1 - (v.co.x / 0.22) ** 2) * max(0.0, 1 - (v.co.z / 0.2) ** 2)
    merge(vin, bk, Matrix.Translation(V(0, -0.305, 0.68)) @ Matrix.Rotation(0.12, 4, 'X'))
    return A


def scialitica():
    A = Asset('san_scialitica')
    st, neon, gum = A.bm('A_acciaio'), A.bm('A_neon'), A.bm('A_gomma')
    # origine al fondo del disco: il disco sta tra z=0 e z=0.13, l'asta sale fino a 0.9 e il resto lo mette il piano (cilindro dal soffitto)
    lathe(st, [(0.0, 0.0), (0.34, 0.0), (0.385, 0.03), (0.395, 0.07), (0.37, 0.115), (0.30, 0.135), (0.0, 0.14)], c=V(0, 0, 0), segs=36)
    lathe(neon, [(0.0, -0.002), (0.31, -0.002), (0.325, 0.004), (0.0, 0.006)], c=V(0, 0, 0), segs=36, closed_bottom=True)
    for i in range(12):
        a = 2 * pi * i / 12
        sphere(neon, V(cos(a) * 0.265, sin(a) * 0.265, 0.004), 0.034, 8)
    for i in range(6):
        a = 2 * pi * i / 6 + 0.26
        sphere(neon, V(cos(a) * 0.145, sin(a) * 0.145, 0.004), 0.034, 8)
    sphere(neon, V(0, 0, 0.004), 0.046, 8)
    cyl(st, V(0, 0, -0.10), V(0, 0, 0.0), 0.022, 10)                                  # impugnatura centrale
    cyl(gum, V(0, 0, -0.17), V(0, 0, -0.10), 0.03, 10)
    cyl(st, V(0, 0, 0.14), V(0, 0, 0.62), 0.028, 12)
    sphere(st, V(0, 0, 0.62), 0.05, 12)
    cyl(st, V(0, 0, 0.62), V(0.0, 0.0, 0.92), 0.022, 12)
    return A


def paravento():
    A = Asset('san_paravento')
    fe, cloth = A.bm('A_ruggine'), A.bm('A_lenzuolo')
    pw, ph = 0.46, 1.66
    # tre pannelli a zig-zag: telaio di tubi e tela sporca
    ang = (radians(28), radians(-28), radians(28))
    x, y = -0.61, -0.108
    for k in range(3):
        a = ang[k]
        dx, dy = cos(a) * pw, sin(a) * pw
        x0, y0 = x, y
        x1, y1 = x0 + dx, y0 + dy
        for (px, py) in ((x0, y0), (x1, y1)):
            cyl(fe, V(px, py, 0.03), V(px, py, ph), 0.012, 8)
            sphere(fe, V(px, py, ph + 0.006), 0.017, 8)
        cyl(fe, V(x0, y0, ph - 0.02), V(x1, y1, ph - 0.02), 0.01, 8)
        cyl(fe, V(x0, y0, 0.20), V(x1, y1, 0.20), 0.01, 8)
        cyl(fe, V(x0, y0, 0.05), V(x1, y1, 0.05), 0.008, 6)
        # tela: lastra sottile un po' ondulata, appesa ai tubi
        panel = bmesh.new()
        vs = rounded_grid(panel, V(0, 0, 0), V(pw - 0.03, 0.012, ph - 0.30), 0.004, nx=8, ny=2, nz=10)
        for v in vs:
            v.co.y += 0.012 * sin((v.co.x / (pw / 2) + 0.4 * k) * pi) * smoothstep(-0.7, 0.7, v.co.z)
        M = Matrix.Translation(V((x0 + x1) / 2, (y0 + y1) / 2, 0.20 + (ph - 0.30) / 2 + 0.05)) @ Matrix.Rotation(a, 4, 'Z')
        merge(cloth, panel, M)
        x, y = x1, y1
    return A


ASSETS = {
    'tea_fila': lambda: fila('tea_fila'),
    'tea_fila_b': lambda: fila('tea_fila_b', up=(0, radians(82), radians(38), 0), tilts=(0.23, 0.23, 0.26, 0.20)),
    'tea_fila_c': lambda: fila('tea_fila_c', up=(radians(80), 0, 0, radians(84)), missing=(2,), tilts=(0.22, 0.30, 0.23, 0.18)),
    'tea_toeletta': toeletta, 'tea_manichino': manichino, 'tea_faro': faro,
    'san_letto': letto_ferro, 'san_carrello': carrello_medico, 'san_flebo': flebo, 'san_sedia_ruote': sedia_rotelle,
    'san_scialitica': scialitica, 'san_paravento': paravento,
}
VIEWS = {
    'tea_fila': ((1.9, 1.9, 1.35), (0, 0, 0.5), 38), 'tea_fila_b': ((1.9, 1.9, 1.35), (0, 0, 0.5), 38), 'tea_fila_c': ((-1.7, -1.6, 1.4), (0, 0, 0.5), 38),
    'tea_toeletta': ((1.5, 2.0, 1.4), (0, 0, 0.9), 40), 'tea_manichino': ((1.5, 2.0, 1.2), (0, 0, 0.85), 40),
    'tea_faro': ((2.4, 3.0, 1.9), (0, 0, 1.0), 40),
    'san_letto': ((2.8, 3.0, 1.7), (0, 0, 0.5), 38), 'san_carrello': ((1.6, 1.9, 1.3), (0, 0, 0.5), 40),
    'san_flebo': ((1.8, 2.4, 1.6), (0, 0, 1.0), 40), 'san_sedia_ruote': ((1.9, 2.1, 1.3), (0, 0, 0.5), 38),
    'san_scialitica': ((1.8, 2.2, 0.9), (0, 0, 0.35), 40), 'san_paravento': ((1.9, 2.4, 1.4), (0, 0, 0.8), 40),
}


def setup_cpu(samples=48, res=(900, 700)):
    """Anteprime su CPU: niente OptiX, cosi' non si toglie la GPU a chi sta giocando. Pochi campioni, denoiser OpenImageDenoise."""
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    sc.cycles.device = 'CPU'
    sc.cycles.samples = min(samples, 40)
    sc.cycles.use_denoising = True
    sc.cycles.denoiser = 'OPENIMAGEDENOISE'
    sc.render.resolution_x, sc.render.resolution_y = 780, 620
    sc.render.image_settings.file_format = 'PNG'
    sc.view_settings.view_transform = 'AgX'
    sc.view_settings.look = 'AgX - Medium High Contrast'
    sc.render.threads_mode = 'FIXED'
    sc.render.threads = int(os.environ.get('ARREDI_THREADS', '6'))


if __name__ == '__main__':
    ga.ASSETS.update(ASSETS)
    ga.VIEWS.update(VIEWS)
    ga.OUT = OUT
    if os.environ.get('ARREDI_GPU') != '1':
        ga.setup_cycles = setup_cpu
    names = [a for a in ga.ARGS if a in ASSETS] or list(ASSETS)
    for nm in names:
        try:
            if 'build' in ga.ARGS:
                ga.stage([nm], 'build')
            if 'render' in ga.ARGS:
                ga.stage([nm], 'render')
        except Exception as e:          # un modello rotto non ferma gli altri
            import traceback
            print(f'[ERRORE] {nm}: {e}')
            traceback.print_exc()
