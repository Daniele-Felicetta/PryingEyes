"""
Arredi iperrealistici (fase 5): rifacimento dei pezzi piu' usati e piu' poveri dell'albergo e della chiesa (sedia, tavolo con la tovaglia,
comodino, armadio, scrivania, scaffale, tavolo d'acciaio, panca). Stesse misure di PropDefs.cs, stessi nomi di file e di mesh dei vecchi
(genera_arredi2.py, genera_chiesa.py), ruoli di materiale gia' esistenti (piu' A_cartone e A_carta per lo scaffale): in Unity basta
sostituire gli FBX. Cambiano la finezza (modanature, tornitura, imbottiture, ferramenta, tessuti) e le UV, orientate perche' la venatura
del legno corra lungo ogni pezzo (arredi5_lib.grain_uv).

Uso (dalla cartella del file):
  blender -b -P genera_arredi5.py -- build [nome ...]    FBX in arredi5/<nome>.fbx (poi da copiare in Assets/_PryingEyes/Models/Arredi o Chiesa)
  blender -b -P genera_arredi5.py -- render [nome ...]   anteprime in arredi5/render/<nome>.png (su CPU; ARREDI_GPU=1 per la GPU,
                                                         ARREDI_FLAT=1 per i colori piatti invece delle texture del gioco)
Assi: Z in alto, davanti = +Y (Unity +Z), origine al centro a terra, metri.
"""
import bpy, bmesh, math, os, sys, random
from mathutils import Vector, Matrix, noise
from math import sin, cos, pi, radians, sqrt

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import arredi5_lib as L
from arredi5_lib import (ga, g3, g4, gc, V, bx, part, spline, rr_profile, round_profile, sweep, mould_bead, mould_ogee, mould_cove, frame_rect,
                         panel_field, stud, stud_row, knob, bail_pull, keyhole_plate, hinge, cushion, rect_loop, piping, tablecloth, Asset5,
                         crown_profile, base_profile, rect_path, prism_bevel)
from genera_arredi import box, lathe, sphere, cyl, tube, rounded_grid, smoothstep
from genera_chiesa import poly_prism, gothic_pts, torus

OUT = os.path.join(HERE, 'arredi5')


# ----------------------------------------------------------------------------- sedia (ristorante, camere, sale)
def sedia():
    """Sedia da sala d'albergo: gambe anteriori tornite, montanti posteriori curvi che diventano i fianchi dello schienale, traversa
    arcuata, fusi torniti e un'anima a vaso, sedile imbottito a velluto con bottoni d'ottone, traverse tornite. 0,44 x 0,44 x 0,90 m."""
    A = Asset5('arr_sedia')
    wood, vel, brass = A.bm('A_noce'), A.bm('A_velluto'), A.bm('A_ottone')
    X, Yf, Yr = 0.185, 0.185, -0.185
    leg = [(0.0, 0.0), (0.011, 0.0), (0.0135, 0.006), (0.0125, 0.040), (0.0165, 0.070), (0.0145, 0.100), (0.0150, 0.200), (0.0195, 0.250),
           (0.0170, 0.275), (0.0185, 0.300), (0.0225, 0.325), (0.0205, 0.345), (0.0205, 0.362)]
    for sx in (-1, 1):
        lathe(wood, leg, c=V(sx * X, Yf, 0), segs=16)
        bx(wood, V(sx * X, Yf, 0.395), V(0.042, 0.042, 0.07), 0.004, 2)
    # montanti posteriori: partono dal pavimento, salgono dritti fino al sedile e si piegano all'indietro fino alla traversa
    prof = rr_profile(0.026, 0.034, 0.006, 3)
    for sx in (-1, 1):
        pts = [V(sx * X, Yr - 0.012, 0.0), V(sx * X, Yr - 0.004, 0.20), V(sx * X, Yr, 0.43), V(sx * X, Yr - 0.022, 0.62), V(sx * X, Yr - 0.050, 0.78),
               V(sx * X, Yr - 0.073, 0.875)]
        sweep(wood, spline(pts, 5), prof, ref=V(1, 0, 0), taper=(1.0, 0.80))
        part(brass, lambda t: lathe(t, [(0.0, 0.0), (0.011, 0.0), (0.0125, 0.005), (0.0105, 0.012), (0.006, 0.019), (0.0, 0.022)], c=V(0, 0, 0), segs=10),
             Matrix.Translation(V(sx * X, Yr - 0.073, 0.872)))
    # fasce del sedile
    bx(wood, V(0, Yf, 0.395), V(2 * X - 0.040, 0.026, 0.070), 0.004, 2)
    bx(wood, V(0, Yr, 0.395), V(2 * X - 0.040, 0.026, 0.070), 0.004, 2)
    for sx in (-1, 1):
        bx(wood, V(sx * X, 0.0, 0.395), V(0.026, 2 * abs(Yr) - 0.040, 0.070), 0.004, 2)
    bx(wood, V(0, 0.0, 0.438), V(2 * X, 2 * X, 0.014), 0.003, 2)
    # sedile imbottito: cuscino con corona, filetto e bottoni d'ottone sotto il bordo
    cushion(vel, V(0, 0.004, 0.462), V(0.386, 0.386, 0.058), 0.022, nx=16, ny=16, nz=4, crown=0.017, sag=0.004, wrinkle=0.0012, seed=4)
    piping(vel, rect_loop(0, 0.004, 0.4855, 0.1845, 0.1845, 0.026), 0.0042, 6)
    stud_row(brass, V(-0.150, 0.197, 0.462), V(0.150, 0.197, 0.462), 0.026, 0.0038, 'y', +1)
    stud_row(brass, V(0.193, 0.150, 0.462), V(0.193, -0.145, 0.462), 0.026, 0.0038, 'x', +1)
    stud_row(brass, V(-0.193, 0.150, 0.462), V(-0.193, -0.145, 0.462), 0.026, 0.0038, 'x', -1)
    # traverse: due laterali e una centrale, con i collari torniti
    for sx in (-1, 1):
        cyl(wood, V(sx * X, Yr + 0.01, 0.155), V(sx * X, Yf - 0.01, 0.155), 0.0105, 10)
        for yy in (Yr + 0.01, Yf - 0.01):
            lathe(wood, [(0.0, -0.016), (0.0135, -0.016), (0.0155, -0.008), (0.0135, 0.0), (0.0155, 0.008), (0.0135, 0.016), (0.0, 0.016)],
                  c=V(sx * X, yy, 0.155), segs=10, axis='Y')
    cyl(wood, V(-X, 0.0, 0.155), V(X, 0.0, 0.155), 0.0095, 10)
    # schienale: traversa alta arcuata (con la parte centrale piu' alta), traversa di mezzo, traversa bassa, tre fusi e l'anima a vaso
    crest = []
    for i in range(13):
        t = i / 12
        x = -(X - 0.012) + 2 * (X - 0.012) * t
        env = 1 - ((2 * t - 1) ** 2)
        crest.append(V(x, Yr - 0.058 - 0.014 * env, 0.815 + 0.026 * env))
    sweep(wood, crest, rr_profile(0.024, 0.070, 0.009, 3), ref=V(0, 0, 1))
    for z, h, th in ((0.652, 0.034, 0.020), (0.520, 0.030, 0.019)):
        yy = Yr - (0.022 + 0.028 * (z - 0.62) / 0.16) if z > 0.6 else Yr - 0.006
        sweep(wood, [V(-(X - 0.010), yy, z), V(0.0, yy - 0.008, z), V(X - 0.010, yy, z)], rr_profile(th, h, 0.006, 3), ref=V(0, 0, 1))
    spindle = [(0.0, 0.0), (0.0065, 0.0), (0.0075, 0.012), (0.0115, 0.034), (0.0085, 0.058), (0.0115, 0.080), (0.0065, 0.100), (0.0, 0.1)]
    for dx in (-0.065, 0.0, 0.065):
        lathe(wood, spindle, c=V(dx, Yr - 0.004 - 0.0, 0.534), segs=10)
    vase = [(0.034, 0.664), (0.027, 0.690), (0.019, 0.715), (0.0165, 0.742), (0.026, 0.768), (0.050, 0.790), (0.058, 0.805)]
    outline = [(x, z) for x, z in vase] + [(-x, z) for x, z in reversed(vase)]
    # l'anima segue l'inclinazione dello schienale (circa 10,6 gradi all'indietro) attorno alla traversa di mezzo
    part(wood, lambda t: poly_prism(t, outline, 'y', -0.008, 0.008),
         Matrix.Translation(V(0, Yr - 0.0276, 0.652)) @ Matrix.Rotation(radians(10.6), 4, 'X') @ Matrix.Translation(V(0, 0, -0.652)))
    return A


# ----------------------------------------------------------------------------- tavolo da ristorante con la tovaglia
def tavolo():
    """Tavolo quadrato da sala coperto da una tovaglia lunga che cade in pieghe, con un candeliere d'ottone dal cero consumato e due
    coperti (piatto, calice). Sotto l'orlo si vedono le quattro gambe tornite. 0,95 x 0,95 x 0,76 m."""
    A = Asset5('arr_tavolo')
    wood, cloth, brass = A.bm('A_noce'), A.bm('A_lenzuolo'), A.bm('A_ottone')
    ceram, glass, wax = A.bm('A_porcellana'), A.bm('A_vetro'), A.bm('A_cera')
    bx(wood, V(0, 0, 0.73), V(0.95, 0.95, 0.04), 0.012, 3)
    for sx in (-1, 1):
        bx(wood, V(sx * 0.395, 0, 0.685), V(0.035, 0.80, 0.07), 0.004, 2)
        bx(wood, V(0, sx * 0.395, 0.685), V(0.80, 0.035, 0.07), 0.004, 2)
    leg = [(0.0, 0.0), (0.012, 0.0), (0.0145, 0.008), (0.013, 0.040), (0.0185, 0.090), (0.0165, 0.140), (0.0170, 0.250), (0.0235, 0.330), (0.021, 0.370),
           (0.0235, 0.400), (0.0225, 0.620), (0.0285, 0.640), (0.0285, 0.710)]
    for sx in (-1, 1):
        for sy in (-1, 1):
            lathe(wood, leg, c=V(sx * 0.40, sy * 0.40, 0.0), segs=14)
    tablecloth(cloth, 0.478, 0.752, 0.375, seed=11, per=96, rows=10, folds=7, amp=0.03)
    # candeliere d'ottone al centro, con il cero acceso fino a meta' e le colature
    lathe(brass, [(0.0, 0.0), (0.050, 0.0), (0.056, 0.006), (0.050, 0.014), (0.022, 0.024), (0.014, 0.040), (0.022, 0.056), (0.012, 0.074), (0.016, 0.100), (0.024, 0.112),
                  (0.036, 0.118), (0.034, 0.124), (0.0, 0.124)], c=V(0, 0, 0.7556), segs=20)
    lathe(wax, [(0.0, 0.0), (0.0185, 0.0), (0.0175, 0.100), (0.0150, 0.112), (0.0125, 0.118), (0.0, 0.115)], c=V(0, 0, 0.8796), segs=14)
    for k, (a, ln) in enumerate(((0.4, 0.055), (2.2, 0.036), (4.1, 0.07))):
        cyl(wax, V(cos(a) * 0.0185, sin(a) * 0.0185, 0.99 - ln), V(cos(a) * 0.0185, sin(a) * 0.0185, 0.987), 0.0042, 6)
    # due coperti ai lati opposti
    plate = [(0.0, 0.0), (0.045, 0.0), (0.050, 0.004), (0.095, 0.011), (0.118, 0.018), (0.121, 0.024), (0.112, 0.0235), (0.100, 0.0172), (0.052, 0.0092), (0.0, 0.0092)]
    stem = [(0.0, 0.0), (0.030, 0.0), (0.031, 0.004), (0.022, 0.008), (0.0045, 0.015), (0.004, 0.060), (0.0075, 0.075), (0.026, 0.092), (0.0305, 0.118),
            (0.0285, 0.146), (0.0262, 0.148), (0.0272, 0.118), (0.023, 0.093), (0.0015, 0.078), (0.0, 0.078)]
    for sy in (-1, 1):
        yy = sy * 0.30
        lathe(ceram, plate, c=V(0, yy, 0.7535), segs=22)
        lathe(glass, stem, c=V(0.20, yy + sy * 0.06, 0.7535), segs=14)
        bx(brass, V(-0.15, yy, 0.7555), V(0.012, 0.15, 0.003), 0.0008, 1)
        bx(brass, V(0.15 + 0.0, yy - sy * 0.02, 0.7555), V(0.012, 0.13, 0.003), 0.0008, 1)
    return A


# ----------------------------------------------------------------------------- comodino
def comodino():
    """Comodino con piano modanato, quattro gambe tornite, un cassetto e uno sportello a specchi di pannello, maniglia a staffa,
    pomello e mostrina della serratura d'ottone. 0,50 x 0,42 x 0,55 m."""
    A = Asset5('arr_comodino')
    wood, br, dark = A.bm('A_noce'), A.bm('A_ottone'), A.bm('A_scuro')
    leg = [(0.0, 0.0), (0.011, 0.0), (0.0135, 0.008), (0.0125, 0.030), (0.0165, 0.070), (0.019, 0.118), (0.019, 0.150)]
    for sx in (-1, 1):
        for sy in (-1, 1):
            lathe(wood, leg, c=V(sx * 0.205, sy * 0.165, 0.0), segs=14)
    bx(wood, V(0, 0, 0.3325), V(0.45, 0.37, 0.375), 0.006, 2)
    bx(wood, V(0, 0, 0.535), V(0.50, 0.42, 0.03), 0.008, 3)
    sweep(wood, rect_path(0, 0, 0.494, 0.225, 0.185), crown_profile(0.022, 0.026), ref=V(0, 0, 1), closed=True)
    sweep(wood, rect_path(0, 0, 0.145, 0.225, 0.185), base_profile(0.012, 0.034), ref=V(0, 0, 1), closed=True)
    # cassetto (appena socchiuso) e sportello: specchi di pannello con cornice a tondino
    bx(wood, V(0, 0.199, 0.437), V(0.41, 0.022, 0.145), 0.004, 2)
    panel_field(wood, -0.197, 0.197, 0.370, 0.504, 0.210, 0.004, +1, mould=0.020)
    bx(wood, V(0, 0.196, 0.254), V(0.41, 0.022, 0.176), 0.004, 2)
    panel_field(wood, -0.197, 0.197, 0.176, 0.332, 0.207, 0.004, +1, mould=0.020)
    bail_pull(br, V(0, 0.214, 0.455), 0.075, +1)
    keyhole_plate(br, dark, V(0, 0.214, 0.394), +1, 0.8)
    knob(br, V(0.145, 0.211, 0.258), 0.0135, +1)
    return A


# ----------------------------------------------------------------------------- armadio
def armadio():
    """Armadio a due ante con zoccolo e cimasa modanati, specchi di pannello, cerniere, pomelli e mostrine d'ottone; l'anta sinistra e'
    socchiusa sul vuoto interno con la barra e tre abiti scuri appesi. 1,30 x 0,62 x 2,2 m."""
    A = Asset5('arr_armadio')
    wood, br, felt, dark, coat = A.bm('A_noce'), A.bm('A_ottone'), A.bm('A_tessuto'), A.bm('A_scuro'), A.bm('A_panno')   # fodera di stoffa rossa
    W, D = 1.30, 0.62
    hx, hy = 0.63, 0.29
    bx(wood, V(0, 0, 0.06), V(W, D, 0.12), 0.01, 2)
    sweep(wood, rect_path(0, 0, 0.12, hx, hy), base_profile(0.02, 0.05), ref=V(0, 0, 1), closed=True)
    for sx in (-1, 1):
        bx(wood, V(sx * (hx - 0.015), 0, 1.115), V(0.03, 2 * hy, 1.99), 0.003, 1)
    bx(wood, V(0, 0, 2.115), V(2 * hx, 2 * hy, 0.03), 0.003, 1)
    bx(wood, V(0, 0, 0.20), V(2 * hx - 0.06, 2 * hy - 0.04, 0.04), 0.003, 1)
    bx(wood, V(0, 0, 0.60), V(2 * hx - 0.06, 2 * hy - 0.06, 0.03), 0.003, 1)
    bx(felt, V(0, -hy + 0.006, 1.115), V(2 * hx - 0.06, 0.01, 1.96))
    cyl(br, V(-hx + 0.03, 0.0, 1.86), V(hx - 0.03, 0.0, 1.86), 0.0115, 12)
    sweep(wood, rect_path(0, 0, 2.13, hx, hy), crown_profile(0.045, 0.085), ref=V(0, 0, 1), closed=True)
    bx(wood, V(0, 0, 2.205), V(2 * hx + 0.09, 2 * hy + 0.09, 0.02), 0.004, 2)
    # frontone centrale sulla cimasa: arco ribassato con una conchiglia d'ottone
    arc = [(-0.30, 2.215)] + [(-0.30 + 0.60 * k / 14, 2.215 + 0.085 * sin(pi * k / 14) ** 0.8) for k in range(1, 14)] + [(0.30, 2.215)]
    poly_prism(wood, arc, 'y', hy + 0.045 - 0.032, hy + 0.045 - 0.0)
    # specchi di pannello e ferramenta delle due ante (legno, ottone e mostrina scura in tre bmesh separati, ruotati insieme attorno ai cardini)
    dw = 0.612

    def door(x_hinge, sgn, angle):
        tw, tb, td = bmesh.new(), bmesh.new(), bmesh.new()
        bx(tw, V(sgn * dw / 2, 0.0, 1.13), V(dw, 0.032, 1.90), 0.004, 1)
        xa, xb = sorted((sgn * 0.062, sgn * (dw - 0.062)))
        panel_field(tw, xa, xb, 0.975, 2.035, 0.016, 0.009, +1, mould=0.030)
        panel_field(tw, xa, xb, 0.235, 0.915, 0.016, 0.009, +1, mould=0.030)
        for z in (0.36, 1.13, 1.90):
            hinge(tb, V(sgn * 0.003, 0.025, z), 0.10, +1)
        knob(tb, V(sgn * (dw - 0.05), 0.025, 1.10), 0.0155, +1)
        keyhole_plate(tb, td, V(sgn * (dw - 0.05), 0.025, 1.035), +1)
        M = Matrix.Translation(V(x_hinge, hy + 0.0165, 0.0)) @ Matrix.Rotation(angle, 4, 'Z')
        g3.merge(wood, tw, M)
        g3.merge(br, tb, M)
        g3.merge(dark, td, M)

    door(hx - 0.0125, -1, 0.0)
    door(-(hx - 0.0125), +1, radians(32))   # socchiusa a 32 gradi: sporge di ~25 cm oltre l'ingombro di PropDefs, che non cambia
    # abiti appesi alla barra (si vedono dall'anta socchiusa)
    rnd = random.Random(21)
    for cx, w, h in ((-0.40, 0.42, 1.05), (-0.20, 0.36, 1.30), (0.12, 0.40, 0.95)):
        g4.drape(coat, cx, 0.0, 1.84, w, h, rnd, amp=0.03, folds=6.0, nx=14, nz=14)
        cyl(br, V(cx, 0.0, 1.835), V(cx, 0.0, 1.87), 0.003, 6)
    return A


# ----------------------------------------------------------------------------- scaffale
def scaffale():
    """Scaffale da magazzino: montanti d'acciaio ad angolare, cinque ripiani di legno con bordo d'acciaio, controventi a croce e il carico
    (scatoloni di cartone con nastro ed etichetta, vasi di vetro, latte, pile di biancheria e libri). 1,20 x 0,45 x 1,9 m."""
    A = Asset5('arr_scaffale')
    wood, iron, cart, paper = A.bm('A_noce'), A.bm('A_ferro'), A.bm('A_cartone'), A.bm('A_carta')
    glass, steel, linen, lea = A.bm('A_vetro'), A.bm('A_acciaio'), A.bm('A_lenzuolo'), A.bm('A_cuoio')
    W, D, H = 1.2, 0.45, 1.9
    leg, t = 0.036, 0.0035
    for sx in (-1, 1):
        for sy in (-1, 1):
            prof = [(0.0, 0.0), (0.0, -sx * leg), (-sy * t, -sx * leg), (-sy * t, -sx * t), (-sy * leg, -sx * t), (-sy * leg, 0.0)]
            sweep(iron, [V(sx * W / 2, sy * D / 2, 0.0), V(sx * W / 2, sy * D / 2, H)], prof, ref=V(1, 0, 0))
    zs = [0.12 + i * 0.42 for i in range(5)]
    for z in zs:
        bx(wood, V(0, 0, z), V(W - 0.075, D - 0.075, 0.032), 0.003, 2)
        for sy in (-1, 1):
            bx(iron, V(0, sy * (D / 2 - 0.0055), z - 0.012), V(W - 0.07, 0.005, 0.048), 0.001, 1)
    for sgn in (-1, 1):
        sweep(iron, [V(-0.54 * sgn, -D / 2 + 0.006, 0.17), V(0.54 * sgn, -D / 2 + 0.006, 1.75)], rr_profile(0.026, 0.004, 0.001, 1), ref=V(0, 1, 0))
    rnd = random.Random(8)
    for z in zs:
        top = z + 0.016
        x = -W / 2 + 0.07
        while x < W / 2 - 0.12:
            kind = rnd.choice(('box', 'box', 'box', 'jar', 'linen', 'books', 'cans', 'gap'))
            if kind == 'gap':
                x += rnd.uniform(0.08, 0.2)
                continue
            y = rnd.uniform(-0.03, 0.03)
            if kind == 'box':
                bw, bd, bh = rnd.uniform(0.17, 0.28), rnd.uniform(0.22, 0.32), rnd.uniform(0.14, 0.30)
                rz = rnd.uniform(-0.07, 0.07)
                c = V(x + bw / 2, y, top + bh / 2 + 0.0004)
                bx(cart, c, V(bw, bd, bh), 0.004, 2, rot=Matrix.Rotation(rz, 3, 'Z'))
                part(paper, lambda tm: bx(tm, V(0, 0, bh / 2 + 0.0005), V(0.045, bd * 0.98, 0.0012)),
                     Matrix.Translation(c) @ Matrix.Rotation(rz, 4, 'Z'))
                part(paper, lambda tm: bx(tm, V(rnd.uniform(-0.03, 0.03), bd / 2 + 0.0008, 0.015), V(min(0.11, bw * 0.55), 0.0012, 0.07)),
                     Matrix.Translation(c) @ Matrix.Rotation(rz, 4, 'Z'))
                x += bw + rnd.uniform(0.01, 0.05)
            elif kind == 'jar':
                r = rnd.uniform(0.04, 0.055)
                h = rnd.uniform(0.13, 0.22)
                for k in range(rnd.choice((1, 2, 2))):
                    xx, yy = x + r + k * (2 * r + 0.012), y + rnd.uniform(-0.02, 0.02)
                    lathe(glass, [(0.0, 0.0), (r * 0.9, 0.0), (r, 0.006), (r, h * 0.8), (r * 0.7, h * 0.92), (r * 0.55, h), (r * 0.5, h), (r * 0.5, h * 0.94), (0.0, h * 0.94)],
                          c=V(xx, yy, top), segs=14)
                    lathe(steel, [(0.0, 0.0), (r * 0.58, 0.0), (r * 0.6, 0.004), (r * 0.6, 0.016), (0.0, 0.016)], c=V(xx, yy, top + h - 0.004), segs=12)
                    x += 0.0
                x += (2 * r + 0.012) * 2
            elif kind == 'linen':
                for k in range(rnd.randint(2, 4)):
                    rounded_grid(linen, V(x + 0.14, y, top + 0.02 + k * 0.042), V(0.28, 0.34, 0.04), 0.018, nx=10, ny=10, nz=3)
                x += 0.32
            elif kind == 'books':
                for k in range(rnd.randint(4, 7)):
                    bh, bw = rnd.uniform(0.18, 0.27), rnd.uniform(0.025, 0.05)
                    bx(lea, V(x + bw / 2, y, top + bh / 2 + 0.0004), V(bw, 0.18, bh), 0.003, 1, rot=Matrix.Rotation(rnd.uniform(-0.04, 0.04), 3, 'Y'))
                    x += bw + 0.002
                x += 0.03
            else:
                for k in range(rnd.randint(2, 3)):
                    xx = x + 0.04 + k * 0.088
                    lathe(steel, [(0.0, 0.0), (0.038, 0.0), (0.04, 0.004), (0.04, 0.098), (0.038, 0.102), (0.0, 0.102)], c=V(xx, y, top), segs=14)
                x += 0.27
    return A


# ----------------------------------------------------------------------------- tavolo d'acciaio
def tavolo_acciaio():
    """Tavolo da lavoro d'acciaio inox (cucina, lavanderia, obitorio, sala operatoria): piano a bordi smussati con grembiule, gambe quadre
    con piedini regolabili, ripiano basso con bordo rialzato, traversa, un cassetto con maniglia a barra. 1,9 x 0,8 x 0,9 m."""
    A = Asset5('arr_tavolo_acciaio')
    st, dk = A.bm('A_acciaio'), A.bm('A_scuro')
    W, D, H = 1.9, 0.8, 0.9
    bx(st, V(0, 0, H - 0.02), V(W, D, 0.04), 0.009, 3)
    for sy in (-1, 1):
        bx(st, V(0, sy * (D / 2 - 0.025), H - 0.065), V(W - 0.05, 0.022, 0.055), 0.003, 1)
    for sx in (-1, 1):
        bx(st, V(sx * (W / 2 - 0.025), 0, H - 0.065), V(0.022, D - 0.05, 0.055), 0.003, 1)
    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = sx * (W / 2 - 0.055), sy * (D / 2 - 0.055)
            bx(st, V(x, y, 0.4 + 0.02), V(0.042, 0.042, 0.8), 0.003, 1)
            lathe(st, [(0.0, 0.0), (0.030, 0.0), (0.032, 0.006), (0.030, 0.010), (0.011, 0.012), (0.011, 0.034), (0.0, 0.034)], c=V(x, y, 0.0), segs=14)
    bx(st, V(0, 0, 0.255), V(W - 0.12, D - 0.12, 0.02), 0.003, 1)
    for sy in (-1, 1):
        bx(st, V(0, sy * (D / 2 - 0.062), 0.285), V(W - 0.12, 0.012, 0.045), 0.002, 1)
    for sx in (-1, 1):
        bx(st, V(sx * (W / 2 - 0.062), 0, 0.285), V(0.012, D - 0.12, 0.045), 0.002, 1)
    for sy in (-1, 1):
        bx(st, V(0, sy * (D / 2 - 0.055), 0.215), V(W - 0.1, 0.03, 0.04), 0.002, 1)
    # cassetto sotto il piano, con la fessura scura e la maniglia a barra
    bx(st, V(0.45, D / 2 - 0.012, H - 0.105), V(0.74, 0.024, 0.115), 0.004, 2)
    bx(dk, V(0.45, D / 2 - 0.0005, H - 0.0445), V(0.74, 0.0008, 0.006))
    for dx in (-0.30, 0.30):
        cyl(st, V(0.45 + dx, D / 2, H - 0.105), V(0.45 + dx, D / 2 + 0.032, H - 0.105), 0.006, 8)
    cyl(st, V(0.15, D / 2 + 0.032, H - 0.105), V(0.75, D / 2 + 0.032, H - 0.105), 0.0085, 10)
    return A


# ----------------------------------------------------------------------------- scrivania
def scrivania():
    """Scrivania di noce con piano a bordo modanato e cuoio incorniciato da un filo d'ottone, cassettiera a tre cassetti con maniglie a
    staffa, gambe torniture a sinistra, e sul piano calamaio, sottomano, libri e un candeliere. 1,25 x 0,62 x 0,76 m."""
    A = Asset5('arr_scrivania')
    wood, lea, br, dark = A.bm('A_noce'), A.bm('A_cuoio'), A.bm('A_ottone'), A.bm('A_scuro')
    glass, paper, wax = A.bm('A_vetro'), A.bm('A_carta'), A.bm('A_cera')
    W, D, H = 1.25, 0.62, 0.76
    bx(wood, V(0, 0, H - 0.02), V(W, D, 0.04), 0.010, 3)
    sweep(wood, rect_path(0, 0, 0.692, 0.600, 0.288), crown_profile(0.018, 0.028), ref=V(0, 0, 1), closed=True)
    bx(lea, V(-0.05, 0.0, H + 0.0009), V(1.00, 0.47, 0.0035), 0.001, 1)
    for sy in (-1, 1):
        bx(br, V(-0.05, sy * 0.2365, H + 0.0026), V(1.012, 0.004, 0.0016), 0.0004, 1)
    for sx in (-1, 1):
        bx(br, V(-0.05 + sx * 0.504, 0.0, H + 0.0026), V(0.004, 0.469, 0.0016), 0.0004, 1)
    bx(wood, V(0.40, 0, 0.375), V(0.42, 0.56, 0.65), 0.006, 2)
    bx(wood, V(0.40, 0, 0.02), V(0.45, 0.58, 0.04), 0.006, 2)
    for zc, h in ((0.600, 0.150), (0.425, 0.185), (0.205, 0.215)):
        bx(wood, V(0.40, 0.2915, zc), V(0.385, 0.021, h), 0.004, 2)
        panel_field(wood, 0.2125, 0.5875, zc - h / 2 + 0.012, zc + h / 2 - 0.012, 0.302, 0.0035, +1, mould=0.018)
        bail_pull(br, V(0.40, 0.3055, zc), 0.07, +1)
    keyhole_plate(br, dark, V(0.40, 0.3055, 0.640), +1, 0.7)
    # gamba tornita: piede a puntale, fusto che si assottiglia verso il basso, due anelli, un vaso sotto il blocco della fascia
    leg = [(0.0, 0.0), (0.012, 0.0), (0.0165, 0.007), (0.0155, 0.026), (0.0165, 0.080), (0.0195, 0.180), (0.0225, 0.280), (0.0250, 0.350),
           (0.0300, 0.372), (0.0305, 0.392), (0.0255, 0.408), (0.0235, 0.440), (0.0270, 0.462), (0.0290, 0.474), (0.0255, 0.490),
           (0.0270, 0.530), (0.0310, 0.575), (0.0325, 0.605), (0.0325, 0.700)]
    for sy in (-1, 1):
        lathe(wood, leg, c=V(-0.55, sy * 0.25, 0.0), segs=18)
    bx(wood, V(-0.55, 0.0, 0.64), V(0.04, 0.50, 0.07), 0.004, 2)
    bx(wood, V(-0.20, -0.27, 0.40), V(0.80, 0.02, 0.62), 0.004, 1)
    bx(wood, V(0.0, 0.272, 0.665), V(1.14, 0.03, 0.075), 0.004, 1)
    # sul piano: calamaio, sottomano, pila di libri, candeliere
    lathe(glass, [(0.0, 0.0), (0.040, 0.0), (0.044, 0.006), (0.044, 0.030), (0.030, 0.042), (0.020, 0.046), (0.020, 0.052), (0.0, 0.052)], c=V(-0.40, -0.10, H + 0.0035), segs=16)
    lathe(br, [(0.0, 0.0), (0.024, 0.0), (0.026, 0.004), (0.024, 0.012), (0.0, 0.014)], c=V(-0.40, -0.10, H + 0.0035 + 0.050), segs=14)
    bx(paper, V(-0.10, 0.05, H + 0.0045), V(0.32, 0.24, 0.0045), 0.001, 1, rot=Matrix.Rotation(radians(-5), 3, 'Z'))
    for k, (bw, bd, bh, rz) in enumerate(((0.22, 0.15, 0.034, 8), (0.20, 0.14, 0.028, -5), (0.19, 0.13, 0.026, 12))):
        bx(lea, V(0.18, -0.12, H + 0.0035 + bh / 2 + sum((0.034, 0.028, 0.026)[:k])), V(bw, bd, bh), 0.003, 2, rot=Matrix.Rotation(radians(rz), 3, 'Z'))
        bx(paper, V(0.18, -0.12 + 0.0, H + 0.0035 + bh / 2 + sum((0.034, 0.028, 0.026)[:k])), V(bw - 0.01, bd - 0.01, bh - 0.008), 0.001, 1, rot=Matrix.Rotation(radians(rz), 3, 'Z'))
    lathe(br, [(0.0, 0.0), (0.040, 0.0), (0.044, 0.005), (0.036, 0.012), (0.014, 0.020), (0.010, 0.050), (0.015, 0.075), (0.026, 0.088), (0.030, 0.094), (0.0, 0.094)],
          c=V(-0.47, 0.13, H + 0.0035), segs=16)
    lathe(wax, [(0.0, 0.0), (0.0135, 0.0), (0.0125, 0.075), (0.010, 0.082), (0.0, 0.080)], c=V(-0.47, 0.13, H + 0.0035 + 0.094), segs=12)
    return A


# ----------------------------------------------------------------------------- panca di chiesa
def panca():
    """Panca da chiesa: fianchi a profilo curvo con un arco gotico a rilievo e un pinnacolo tornito, sedile col bordo tondo, schienale a specchi di
    pannello con una fila di archi a traforo sotto la cimasa, leggio per i libri sul retro, inginocchiatoio imbottito. 2,6 x 1,0 x 1,0 m (in
    Unity la lunghezza si scala: RoomKitsChiesa)."""
    A = Asset5('chi_panca')
    w, cl, br, lea = A.bm('A_legno'), A.bm('A_tessuto'), A.bm('A_ottone'), A.bm('A_cuoio')
    L, t = 2.6, 0.05
    key = [(0.36, 0.0), (0.36, 0.22), (0.345, 0.34), (0.28, 0.43), (0.16, 0.49), (0.06, 0.575), (-0.02, 0.72), (-0.085, 0.885), (-0.125, 0.975), (-0.19, 1.005),
           (-0.245, 0.975)]
    outline = [(p.x, p.y) for p in spline([V(a, b, 0) for a, b in key], 5)] + [(-0.265, 0.90), (-0.265, 0.0)]
    for sx in (-1, 1):
        x0, x1 = sorted((sx * L / 2, sx * (L / 2 - t)))
        prism_bevel(w, outline, 'x', x0, x1, 0.004, 1)
        xo = sx * L / 2
        for cw, ch, dz, th in ((0.36, 0.36, 0.0, 0.009), (0.29, 0.30, 0.03, 0.017)):
            pts = gothic_pts(0.08, 0.06 + dz, cw, ch)
            a, b = sorted((xo, xo + sx * th))
            poly_prism(w, pts, 'x', a, b)
        part(w, lambda tm: lathe(tm, [(0.0, 0.0), (0.024, 0.0), (0.030, 0.010), (0.021, 0.022), (0.034, 0.044), (0.029, 0.060), (0.014, 0.074), (0.0, 0.080)], c=V(0, 0, 0), segs=14),
             Matrix.Translation(V(sx * (L / 2 - t / 2), -0.19, 0.995)))
        bx(br, V(xo + sx * 0.0015, -0.17, 0.78), V(0.003, 0.058, 0.030), 0.0006, 1)
        for yy in (-0.19, -0.15):
            stud(br, V(xo + sx * 0.003, yy, 0.78), 0.0032, 'x', sx)
    Lin = L - 2 * t
    bx(w, V(0, 0.14, 0.435), V(Lin, 0.44, 0.045), 0.012, 3)
    bx(w, V(0, 0.335, 0.37), V(Lin, 0.035, 0.10), 0.006, 2)
    bx(w, V(0, 0.05, 0.36), V(Lin, 0.04, 0.06), 0.004, 1)
    bx(w, V(0, -0.115, 0.23), V(Lin, 0.03, 0.34), 0.004, 1)
    # schienale: traversa bassa e di mezzo, montanti, specchi di pannello e archi a traforo
    bx(w, V(0, -0.16, 0.50), V(Lin, 0.04, 0.10), 0.006, 2)
    bx(w, V(0, -0.16, 0.74), V(Lin, 0.04, 0.07), 0.006, 2)
    n = 6
    xs = [-(L / 2 - t) + i * Lin / n for i in range(n + 1)]
    for x in xs:
        bx(w, V(x, -0.16, 0.62), V(0.05, 0.04, 0.30), 0.005, 2)
        lathe(w, [(0.0, 0.0), (0.014, 0.0), (0.012, 0.010), (0.012, 0.145), (0.016, 0.152), (0.0, 0.158)], c=V(x, -0.16, 0.775), segs=10)
    for i in range(n):
        xa, xb = xs[i] + 0.04, xs[i + 1] - 0.04
        panel_field(w, xa, xb, 0.552, 0.704, -0.14, 0.009, +1, mould=0.026)
        cx, bw = (xs[i] + xs[i + 1]) / 2, xs[i + 1] - xs[i]
        g4.arch_ring(w, cx, 0.775, bw - 0.045, 0.155, 0.79, bw - 0.09, 0.12, -0.18, -0.14)
    bx(w, V(0, -0.165, 0.965), V(Lin, 0.07, 0.07), 0.02, 3)
    sweep(w, [V(-Lin / 2, -0.165, 0.995), V(Lin / 2, -0.165, 0.995)], [(a, b) for a, b in mould_cove(0.095, 0.022)], ref=V(0, 0, 1), cap=True)
    # leggio sul retro con i libri dei canti
    bx(w, V(0, -0.228, 0.64), V(0.95, 0.10, 0.022), 0.004, 2)
    bx(w, V(0, -0.282, 0.665), V(0.95, 0.012, 0.045), 0.004, 2)
    rnd = random.Random(5)
    x = -0.40
    for k in range(7):
        bw = rnd.uniform(0.028, 0.045)
        bh = rnd.uniform(0.17, 0.21)
        bx(lea, V(x + bw / 2, -0.225, 0.651 + bh / 2), V(bw, 0.12, bh), 0.003, 1, rot=Matrix.Rotation(rnd.uniform(-0.06, 0.06), 3, 'Y'))
        x += bw + 0.003
    # inginocchiatoio imbottito, retto da tre mensole
    bx(w, V(0, -0.46, 0.085), V(L - 0.30, 0.16, 0.06), 0.01, 2)
    cushion(cl, V(0, -0.46, 0.148), V(L - 0.40, 0.14, 0.05), 0.018, nx=46, ny=5, nz=3, crown=0.010, sag=0.0, wrinkle=0.0015, seed=7)
    for x in (-0.95, 0.0, 0.95):
        bx(w, V(x, -0.30, 0.075), V(0.05, 0.25, 0.05), 0.008, 2)
    return A


ASSETS = {'arr_sedia': sedia, 'arr_tavolo': tavolo, 'arr_comodino': comodino, 'arr_armadio': armadio, 'arr_scaffale': scaffale,
          'arr_tavolo_acciaio': tavolo_acciaio, 'arr_scrivania': scrivania, 'chi_panca': panca}
VIEWS = {
    'arr_sedia': ((1.15, 1.5, 1.1), (0, 0, 0.47), 38), 'arr_tavolo': ((1.8, 2.0, 1.5), (0, 0, 0.45), 38),
    'arr_comodino': ((1.1, 1.4, 0.9), (0, 0, 0.3), 40), 'arr_armadio': ((2.9, 3.6, 2.2), (0, 0, 1.05), 40),
    'arr_scaffale': ((2.0, 2.8, 1.7), (0, 0, 0.9), 40), 'arr_tavolo_acciaio': ((2.6, 3.0, 1.7), (0, 0, 0.45), 40),
    'arr_scrivania': ((1.9, 2.3, 1.5), (0, 0, 0.4), 40), 'chi_panca': ((3.6, 3.6, 1.9), (0, 0, 0.5), 40),
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
    # l'FBX porta anche i due livelli di dettaglio piu' poveri (ARREDI_NOLOD=1 li toglie); ARREDI_LOD=1|2 con 'render' fotografa solo quel livello
    names = [a for a in ga.ARGS if a in ASSETS] or list(ASSETS)
    for nm in names:
        try:
            if 'build' in ga.ARGS:
                Asset5.WITH_LODS, Asset5.SHOW_LOD = os.environ.get('ARREDI_NOLOD') != '1', 0
                ga.stage([nm], 'build')
            if 'render' in ga.ARGS:
                Asset5.WITH_LODS, Asset5.SHOW_LOD = False, int(os.environ.get('ARREDI_LOD', '0'))
                ga.stage([nm], 'render')
        except Exception as e:          # un modello rotto non ferma gli altri
            import traceback
            print(f'[ERRORE] {nm}: {e}')
            traceback.print_exc()
