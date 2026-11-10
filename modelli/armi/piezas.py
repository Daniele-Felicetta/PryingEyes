"""
Libreria di pezzi per le armi: guide Picatinny, astine M-LOK, bocche, caricatori, calci, mirini e ottiche.
Ogni funzione disegna sul Gun 'g' nella parte corrente. Assi: x destra, y avanti, z in alto, origine al centro
dell'impugnatura. Le misure sono quelle vere (metri) dove serve la leggibilita' del mirino.
"""
from math import sin, cos, pi, radians, sqrt
from kit import *


# ----------------------------------------------------------------------------- guide e astine
def picatinny(g, role, y0, y1, z, w=0.0212, pitch=0.0107, slot=0.0053, neck=0.0150, h=0.0068):
    """Guida Picatinny: base e collo continui, denti con scasso a ogni passo. z = piano di appoggio (faccia inferiore)."""
    g.box(role, (0, (y0 + y1) / 2, z + 0.0016), (w, y1 - y0, 0.0032))
    g.box(role, (0, (y0 + y1) / 2, z + 0.0032 + (h - 0.0032) * 0.5 - 0.0008), (neck, y1 - y0, h - 0.0032 - 0.0016))
    n = int((y1 - y0 - slot) / pitch)
    for i in range(n + 1):
        yc = y0 + slot + (pitch - slot) * 0.5 + i * pitch
        if yc + (pitch - slot) * 0.5 > y1:
            break
        g.box(role, (0, yc, z + h - 0.0016), (w, pitch - slot, 0.0032), r=0.0007)


def mlok_slots(g, role, y0, y1, hw, hh, count_y=3, bottom=True, sides=True, top=False, depth=0.008, length=0.032, wd=0.0072):
    """Asole M-LOK come tagli (cutter) nell'astina centrata sull'asse y con semisezione hw x hh (centro in 0, z=0 locale:
    lo chiama chi passa gia' le coordinate dell'astina in cx, cz). Qui y0..y1 soltanto: le asole stanno a passo regolare."""
    raise NotImplementedError


def slots_on(g, role, cx, cz, y0, y1, hw, hh, n=4, length=0.030, wd=0.0072, depth=0.0075, sides=True, bottom=True, top=False):
    """Asole (tagli) sull'astina rettangolare di semisezione hw x hh centrata in (cx, cz)."""
    step = (y1 - y0) / n
    for i in range(n):
        yc = y0 + step * (i + 0.5)
        if sides:
            for sx in (-1, 1):
                g.box(role, (cx + sx * (hw - depth / 2 + 0.0005), yc, cz), (depth, length, wd), r=0.0012, cut=True)
        if bottom:
            g.box(role, (cx, yc, cz - (hh - depth / 2 + 0.0005)), (wd, length, depth), r=0.0012, cut=True)
        if top:
            g.box(role, (cx, yc, cz + (hh - depth / 2 + 0.0005)), (wd, length, depth), r=0.0012, cut=True)


def vents_round(g, role, cx, cz, y0, y1, hw, rows=2, per_row=5, rad=0.0042, depth_axis='x'):
    """Fori di raffreddamento rotondi (cutter) su entrambi i lati di un'astina cilindrica/quadra."""
    step = (y1 - y0) / per_row
    for i in range(per_row):
        for k in range(rows):
            yc = y0 + step * (i + 0.5)
            zc = cz + (k - (rows - 1) / 2) * rad * 3.0
            g.lathe(role, (cx, yc, zc), [(-hw - 0.001, rad), (hw + 0.001, rad)], segs=14, axis='x', cut=True)


# ----------------------------------------------------------------------------- bocche
def flash_hider(g, role, y0, z, rad=0.0155, length=0.062, prongs=5):
    """Spegnifiamma tipo A2 'a gabbia': anello pieno dietro, poi un cilindro con le fessure tagliate."""
    g.lathe(role, (0, 0, z), [(y0, rad * 0.62), (y0, rad), (y0 + length * 0.12, rad), (y0 + length * 0.12, rad * 0.92),
                              (y0 + length * 0.75, rad * 0.92), (y0 + length, rad * 0.78)], segs=20)
    for i in range(prongs):
        a = 360.0 * i / prongs + 36
        g.box(role, (0, y0 + length * 0.58, z), (0.0042, length * 0.52, rad * 2.4), rot=(0, a, 0), cut=True)
    g.lathe(role, (0, 0, z), [(y0 + length * 0.80, 0.0), (y0 + length + 0.001, 0.0062)], segs=16, cut=True, cap=(False, True))


def muzzle_brake(g, role, y0, z, rad=0.0185, length=0.075, ports=3):
    """Freno di bocca: corpo cilindrico con tre coppie di sfoghi laterali."""
    g.lathe(role, (0, 0, z), [(y0, rad * 0.8), (y0, rad), (y0 + length, rad), (y0 + length, rad * 0.8)], segs=24)
    for i in range(ports):
        yc = y0 + length * (0.30 + 0.20 * i)
        g.box(role, (0, yc, z), (rad * 2.4, 0.0042, 0.0070), cut=True)
        g.box(role, (0, yc, z), (0.0070, 0.0042, rad * 2.4), cut=True) if i == 1 else None
    g.lathe(role, (0, 0, z), [(y0 + length - 0.0005, 0.0), (y0 + length + 0.001, 0.0075)], segs=16, cut=True, cap=(False, True))


def suppressor(g, role, y0, z, rad=0.0245, length=0.20):
    g.lathe(role, (0, 0, z), [(y0, rad * 0.78), (y0 + 0.003, rad), (y0 + length - 0.003, rad), (y0 + length, rad * 0.86)], segs=32)
    for i in range(3):
        g.lathe('gomma', (0, 0, z), [(y0 + length * (0.2 + 0.25 * i), rad * 1.01), (y0 + length * (0.2 + 0.25 * i) + 0.003, rad * 1.01)], segs=32, cap=(False, False)) if False else None
    g.lathe(role, (0, 0, z), [(y0 + length - 0.0005, 0.0), (y0 + length + 0.001, 0.0075)], segs=16, cut=True, cap=(False, True))


# ----------------------------------------------------------------------------- caricatori
def curved_mag(g, role_body, top, bottom, bulge, hw, dp, hw_b=None, dp_b=None, ribs=0, base=True, n=14):
    """Caricatore curvo (STANAG, AK): percorso da 'top' (y, z) a 'bottom' con una freccia 'bulge' in avanti (positiva).
    ribs = nervature trasversali sul fianco. Ritorna il percorso."""
    top, bottom = Vector(top), Vector(bottom)
    path = arc(top, bottom, -bulge, n)
    hwb = hw if hw_b is None else hw_b
    dpb = dp if dp_b is None else dp_b
    hws = [hw + (hwb - hw) * i / n for i in range(n + 1)]
    dps = [dp + (dpb - dp) * i / n for i in range(n + 1)]
    g.sweep(role_body, path, hws, dps, rad=0.0035, seg=2)
    if base:
        yb, zb = path[-1]
        g.sweep('polimero', [(yb, zb + 0.004), (yb, zb - 0.004)], hwb + 0.0012, dpb + 0.0014, rad=0.0025, seg=2)
    return path


# ----------------------------------------------------------------------------- mirini: ferro
def post_front(g, y, z0, h=0.034, w=0.0026, hood=True, role='acciaio'):
    """Mirino anteriore a lama con le orecchie di protezione (AR/pistole) su un piccolo basamento."""
    g.box(role, (0, y, z0 + h / 2), (w, 0.004, h), r=0.0004)
    if hood:
        for sx in (-1, 1):
            g.box(role, (sx * 0.0072, y, z0 + h * 0.46), (0.0030, 0.0042, h * 0.88), r=0.0006)
        g.box(role, (0, y, z0 + 0.0020), (0.0172, 0.0070, 0.0040), r=0.0006)


def aperture_rear(g, y, z0, h=0.030, ring_out=0.0105, ring_in=0.0046, role='acciaio', width=0.020):
    """Mirino posteriore a diaframma ('peep'): due ali di protezione e un anello forato (visto da dietro e' un cerchio)."""
    g.box(role, (0, y, z0 + 0.003), (width, 0.012, 0.006), r=0.0008)
    for sx in (-1, 1):
        g.box(role, (sx * 0.0078, y, z0 + 0.016), (0.0030, 0.011, 0.024), r=0.0007)
    ring_c = z0 + h - ring_out * 0.2
    g.lathe(role, (0, y, ring_c), [(-0.0030, ring_in), (-0.0030, ring_out), (0.0030, ring_out), (0.0030, ring_in)], segs=24, axis='y', loop=True)
    g.box(role, (0, y, z0 + 0.006 + (ring_c - ring_out - z0 - 0.006) / 2), (0.007, 0.007, ring_c - ring_out - z0 - 0.006 + 0.002))


def notch_rear(g, y, z0, h=0.012, w=0.016, gap=0.0036, role='acciaio', depth=0.008, nb=None):
    """Tacca di mira a V/U aperta: blocco con un intaglio verticale. nb = quota del fondo dell'intaglio (di default 22% dell'altezza)."""
    g.box(role, (0, y, z0 + h / 2), (w, depth, h), r=0.0007)
    nb = z0 + 0.22 * h if nb is None else nb
    g.box(role, (0, y, (nb + z0 + h + 0.002) / 2), (gap, depth + 0.002, z0 + h + 0.002 - nb), cut=True)


def dot(g, c, r=0.0016, axis='y'):
    g.lathe('emit', c, [(0.0, r), (0.0009, r)], segs=12, axis=axis, cap=(True, True))


# ----------------------------------------------------------------------------- ottiche
def reflex_tube(g, y, z, length=0.068, rad=0.0225, mount_h=0.020, rail_z=None, role='alluminio'):
    """Punto rosso a tubo (stile Aimpoint): corpo cavo, campane anteriore e posteriore, montaggio, torretta e cappuccio.
    (y, z) = centro della finestra, dove sta il puntino; ads = (0, y, z). Aggiunge la parte 'retino' (disco con il puntino)."""
    y0, y1 = y - length / 2, y + length / 2
    g.tube(role, 0, z, y0, y1, rad, rad - 0.0032, segs=32)
    g.tube(role, 0, z, y0 - 0.002, y0 + 0.014, rad + 0.0022, rad - 0.0012, segs=32)       # campana posteriore
    g.tube(role, 0, z, y1 - 0.016, y1 + 0.002, rad + 0.0026, rad - 0.0012, segs=32)       # campana anteriore (obiettivo)
    zb = z - rad
    g.box(role, (0, y - 0.004, zb - mount_h / 2 + 0.002), (0.030, 0.052, mount_h), r=0.0018)
    g.box('acciaio', (0, y - 0.004, zb - mount_h - 0.0035), (0.0235, 0.058, 0.0070), r=0.0008)
    for sx in (-1, 1):
        g.lathe('acciaio', (sx * 0.0125, y - 0.025, zb - mount_h - 0.0035), [(-0.003, 0.0045), (0.003, 0.0045)], segs=14, axis='x')   # viti
    g.lathe(role, (rad * 0.92, y - 0.002, z + 0.004), [(0.0, 0.0105), (0.010, 0.0105)], segs=20, axis='x')                          # torretta destra
    g.lathe(role, (0, y + 0.004, z + rad * 0.88), [(0.0, 0.0095), (0.008, 0.0095)], segs=20, axis='z')                              # torretta alta
    # lenti: una verso l'occhio (piatta) e una obiettivo
    g.part('lente_dietro', pivot=(0, y0 + 0.0125, z))
    g.lathe('vetro', (0, y0 + 0.0125, z), [(-0.0006, rad - 0.0042), (0.0006, rad - 0.0042)], segs=32)
    g.part('lente_davanti', pivot=(0, y1 - 0.008, z))
    g.lathe('vetro', (0, y1 - 0.008, z), [(-0.0006, rad - 0.0042), (0.0006, rad - 0.0042)], segs=32)
    g.part('retino', pivot=(0, y + 0.012, z))
    g.lathe('retino', (0, y + 0.012, z), [(-0.0002, rad - 0.0044), (0.0002, rad - 0.0044)], segs=32)
    g.part('corpo')


def holo_sight(g, y, z, rad=None, role='polimero', hw=0.0300, hh_out=0.0305, win_w=0.0185, win_h=0.0125, length=0.082, mount_h=0.016):
    """Olografico a finestra rettangolare (stile EOTech): scocca con cornice posteriore e anteriore, cappuccio, leva di sgancio.
    (y, z) = centro della finestra."""
    y0, y1 = y - length / 2, y + length / 2
    zb = z - hh_out
    # corpo: fondo, tetto e fianchi attorno alla finestra
    g.box(role, (0, y, z - win_h - (hh_out - win_h) / 2), (hw * 2, length, hh_out - win_h), r=0.002)           # fondo
    g.box(role, (0, y, z + win_h + (hh_out - win_h) / 2 + 0.002), (hw * 2 - 0.004, length, hh_out - win_h + 0.004), r=0.003)  # tetto
    for sx in (-1, 1):
        g.box(role, (sx * (win_w + (hw - win_w) / 2), y, z), ((hw - win_w), length, win_h * 2 + 0.002), r=0.0022)
    # cornici piu' scure davanti e dietro
    for yy, ln in ((y0 + 0.004, 0.008), (y1 - 0.004, 0.008)):
        g.box('alluminio', (0, yy, z), (hw * 2 + 0.0016, ln, (win_h + 0.0035) * 2 + 0.002), r=0.0012)
        g.box('alluminio', (0, yy, z), (win_w * 2, ln + 0.002, win_h * 2), cut=True)
    g.box('alluminio', (0, y, zb - mount_h / 2 + 0.002), (0.040, length * 0.7, mount_h), r=0.002)
    g.box('acciaio', (0, y - 0.003, zb - mount_h - 0.003), (0.0235, 0.058, 0.0060), r=0.0008)
    g.lathe('acciaio', (hw + 0.002, y - 0.012, z - 0.012), [(-0.004, 0.0068), (0.004, 0.0068)], segs=16, axis='x')       # leva di sgancio
    g.part('lente_dietro', pivot=(0, y0 + 0.008, z))
    g.box('vetro', (0, y0 + 0.008, z), (win_w * 2, 0.0008, win_h * 2))
    g.part('retino', pivot=(0, y0 + 0.012, z))
    g.box('retino', (0, y0 + 0.012, z), (win_w * 2 - 0.001, 0.0003, win_h * 2 - 0.001))
    g.part('corpo')


def reflex_open(g, y, z, w=0.032, h=0.026, length=0.040, base_h=0.010, role='alluminio'):
    """Riflesso aperto compatto (stile RMR): basamento, arco ad U e finestra inclinata; si monta direttamente."""
    g.box(role, (0, y, z - h / 2 - base_h / 2 + 0.002), (w, length, base_h), r=0.002)
    for sx in (-1, 1):
        g.box(role, (sx * (w / 2 - 0.0020), y - 0.004, z), (0.0042, length * 0.7, h), r=0.0016)
    g.box(role, (0, y - 0.006, z + h / 2 + 0.002), (w, length * 0.62, 0.0045), r=0.0016)
    g.part('lente_dietro', pivot=(0, y + 0.006, z))
    g.box('vetro', (0, y + 0.006, z), (w - 0.009, 0.0008, h - 0.004))
    g.part('retino', pivot=(0, y + 0.0075, z))
    g.box('retino', (0, y + 0.0075, z), (w - 0.010, 0.0003, h - 0.005))
    g.part('corpo')


def scope(g, y, z, length=0.34, tube=0.0155, bell_obj=0.032, bell_oc=0.0225, mag_label=None, mount_h=0.020, rail=True, role='alluminio'):
    """Cannocchiale: tubo centrale, campana oculare e obiettivo con paraluce, torrette di elevazione e deriva, anelli di
    montaggio. (y, z) = centro del cannocchiale; l'asse ottico e' lungo y. Parti 'lente_oculare', 'lente_obiettivo' e 'retino'."""
    y0, y1 = y - length / 2, y + length / 2
    g.lathe(role, (0, 0, z), [(y0, bell_oc * 0.85), (y0 + 0.002, bell_oc), (y0 + 0.030, bell_oc), (y0 + 0.062, tube * 1.18), (y0 + 0.070, tube),
                              (y1 - 0.095, tube), (y1 - 0.062, tube * 1.18), (y1 - 0.040, bell_obj * 0.88), (y1 - 0.034, bell_obj),
                              (y1 - 0.004, bell_obj), (y1, bell_obj * 0.94)], segs=40)
    g.tube(role, 0, z, y1 - 0.003, y1 + 0.060, bell_obj * 1.015, bell_obj * 0.96, segs=40)           # paraluce
    g.tube(role, 0, z, y0 - 0.012, y0 + 0.002, bell_oc * 1.05, bell_oc * 0.86, segs=40)               # anello oculare
    # torrette: elevazione (sopra) e deriva (destra) con cappuccio e rigatura
    yt = y + 0.010
    g.lathe(role, (0, yt, z + tube * 0.9), [(0.0, tube * 0.55), (0.008, tube * 0.55), (0.008, 0.0105), (0.027, 0.0105), (0.027, 0.0098)], segs=24, axis='z')
    g.lathe(role, (tube * 0.9, yt, z), [(0.0, tube * 0.55), (0.008, tube * 0.55), (0.008, 0.0105), (0.027, 0.0105), (0.027, 0.0098)], segs=24, axis='x')
    g.lathe(role, (0, y - 0.05, z - tube * 0.9), [(0.0, 0.0050), (0.012, 0.0050), (0.012, 0.012), (0.018, 0.012)], segs=20, axis='z')       # parallasse/illuminazione
    # anelli di montaggio
    for ym in (y - 0.075, y + 0.075):
        g.lathe(role, (0, ym, z), [(-0.011, tube * 1.08), (0.011, tube * 1.08)], segs=32, axis='y')
        g.box(role, (0, ym, z - tube - mount_h / 2 + 0.002), (0.034, 0.024, mount_h), r=0.002)
        g.box('acciaio', (0, ym, z - tube - mount_h - 0.0025), (0.0235, 0.026, 0.0060), r=0.0008)
        g.box('acciaio', (tube * 1.1, ym, z + tube * 0.9), (0.0100, 0.0100, 0.0100), r=0.0012)
    g.part('lente_oculare', pivot=(0, y0 - 0.0005, z))
    g.lathe('vetro', (0, y0 - 0.0005, z), [(-0.0005, bell_oc * 0.84), (0.0005, bell_oc * 0.84)], segs=40)
    g.part('lente_obiettivo', pivot=(0, y1 + 0.001, z))
    g.lathe('vetro', (0, y1 + 0.001, z), [(-0.0005, bell_obj * 0.94), (0.0005, bell_obj * 0.94)], segs=40)
    g.part('corpo')


def acog(g, y, z, length=0.150, rad=0.0235, role='alluminio'):
    """Ottica a prisma (stile ACOG): corpo tozzo con campana anteriore, maniglia di trasporto sopra, fibra ottica."""
    y0, y1 = y - length / 2, y + length / 2
    g.lathe(role, (0, 0, z), [(y0, rad * 0.82), (y0 + 0.003, rad * 0.95), (y0 + 0.028, rad), (y1 - 0.060, rad * 1.05), (y1 - 0.030, rad * 1.26),
                              (y1 - 0.002, rad * 1.30), (y1, rad * 1.20)], segs=36)
    g.tube(role, 0, z, y1 - 0.003, y1 + 0.020, rad * 1.30, rad * 1.18, segs=36)
    g.box(role, (0, y - 0.008, z + rad * 0.78), (0.0245, length * 0.62, 0.0170), r=0.003)                     # dorso con fibra ottica
    g.box('emit', (0, y - 0.008, z + rad * 0.78 + 0.0095), (0.0042, length * 0.5, 0.0030))
    g.box(role, (0, y - 0.004, z - rad - 0.008), (0.033, 0.084, 0.020), r=0.0025)
    g.box('acciaio', (0, y - 0.004, z - rad - 0.0185), (0.0235, 0.058, 0.0060), r=0.0008)
    g.lathe(role, (rad * 0.88, y + 0.016, z + 0.002), [(0.0, 0.0105), (0.010, 0.0105)], segs=20, axis='x')
    g.lathe(role, (0, y + 0.016, z + rad * 0.82), [(0.0, 0.0100), (0.008, 0.0100)], segs=20, axis='z')
    g.part('lente_oculare', pivot=(0, y0 - 0.0005, z))
    g.lathe('vetro', (0, y0 - 0.0005, z), [(-0.0005, rad * 0.78), (0.0005, rad * 0.78)], segs=36)
    g.part('lente_obiettivo', pivot=(0, y1 + 0.001, z))
    g.lathe('vetro', (0, y1 + 0.001, z), [(-0.0005, rad * 1.14), (0.0005, rad * 1.14)], segs=36)
    g.part('corpo')


# ----------------------------------------------------------------------------- pezzi comuni di armi lunghe
def fsb_tower(g, y, z_barrel, z_tip, role='acciaio', ears=True):
    """Torretta del mirino anteriore (AR A2, G3): basamento triangolare sulla canna e lama con le orecchie alla quota z_tip."""
    h = z_tip - z_barrel
    g.prism(role, [(y - 0.016, z_barrel + 0.006), (y + 0.016, z_barrel + 0.006), (y + 0.010, z_barrel + h * 0.55), (y - 0.004, z_barrel + h * 0.55)], -0.0070, 0.0070, r=0.0010)
    g.box(role, (0, y, z_barrel + h * 0.55 + (h * 0.45) / 2), (0.0028, 0.0050, h * 0.45), r=0.0004)
    if ears:
        for sx in (-1, 1):
            g.box(role, (sx * 0.0072, y, z_barrel + h * 0.55 + h * 0.20), (0.0026, 0.0060, h * 0.40), r=0.0006)


def bipod_folded(g, y, z, length=0.17, spread=0.03, role='acciaio'):
    """Bipiede ripiegato in avanti sotto la canna: cerniera e due gambe."""
    g.box(role, (0, y, z), (0.050, 0.020, 0.016), r=0.0030)
    for sx in (-1, 1):
        g.rod(role, (sx * spread, y, z - 0.004), (sx * (spread + 0.004), y + length, z - 0.010), 0.0045)
        g.box('gomma', (sx * (spread + 0.004), y + length + 0.006, z - 0.010), (0.012, 0.016, 0.012), r=0.0030)


def stock_side(g, role, pts, hw, r=0.006, pad=None):
    """Calcio a profilo laterale (y, z) estruso, con calciolo opzionale (lista di punti)."""
    g.prism(role, pts, -hw, hw, r=r, seg=3)
    if pad:
        g.prism('gomma', pad, -hw - 0.0008, hw + 0.0008, r=0.0030, seg=3)
