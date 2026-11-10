"""Mitragliatrici leggere. Origine: centro dell'impugnatura; y avanti; z su."""
from math import sin, cos, radians
from piezas import *
from armi_mitra import tilted_grip
from armi_pistole import trigger_guard


@arma
def lmg_salterio():
    """SALTERIO: mitragliatrice a tamburo da 75 colpi derivata da un fucile d'assalto, canna lunga, bipiede, calcio di noce."""
    g = Gun('lmg_salterio', tex=2048)
    g.paint = {'legno_scuro': (0.032, 0.010, 0.003), 'legno_chiaro': (0.115, 0.034, 0.009)}
    ZB = 0.100
    g.part('corpo')
    g.box('acciaio', (0, 0.025, ZB - 0.0050), (0.0340, 0.2450, 0.0660), r=0.0030, seg=3)
    g.prism('acciaio', [(-0.098, ZB + 0.020), (-0.092, ZB + 0.033), (0.138, ZB + 0.033), (0.148, ZB + 0.020)], -0.0165, 0.0165, r=0.0065, seg=3)
    g.box('acciaio', (0.0172, -0.010, ZB + 0.0090), (0.0040, 0.0900, 0.0200), cut=True)
    g.prism('acciaio', [(-0.055, ZB + 0.024), (0.022, ZB + 0.024), (0.030, ZB + 0.010), (-0.040, ZB - 0.014), (-0.052, ZB - 0.004)], 0.0172, 0.0190, r=0.0008)
    g.cyl('acciaio', 0, ZB + 0.0325, 0.145, 0.420, 0.0125, ch=0.0008)
    g.loft('legno', [(0.150, rrect(0.0185, 0.0170, 0.0080, 3, 0, ZB + 0.0320)), (0.360, rrect(0.0175, 0.0150, 0.0080, 3, 0, ZB + 0.0320))], r=0.0014)
    g.loft('legno', [(0.150, rrect(0.0230, 0.0240, 0.0100, 3, 0, ZB - 0.0120)), (0.370, rrect(0.0220, 0.0220, 0.0100, 3, 0, ZB - 0.0120))], r=0.0014)
    g.cyl('acciaio', 0, ZB, 0.145, 0.620, 0.0128, ch=0.0006)
    g.cyl('acciaio', 0, ZB + 0.0030, 0.380, 0.412, 0.0180, ch=0.0008)
    g.cyl('acciaio', 0, ZB, 0.595, 0.625, 0.0150, ch=0.0008)
    post_front(g, 0.608, ZB + 0.0128, h=0.0361, w=0.0026, hood=True)
    flash_hider(g, 'acciaio', 0.618, ZB, rad=0.0150, length=0.062, prongs=5)
    nb = ZB + 0.0489
    notch_rear(g, 0.108, ZB + 0.0440, h=0.0180, w=0.0190, gap=0.0036, depth=0.0100, nb=nb)
    stock_side(g, 'legno', [(-0.095, ZB + 0.020), (-0.250, ZB + 0.010), (-0.350, ZB + 0.014), (-0.358, ZB - 0.078), (-0.340, ZB - 0.082), (-0.250, ZB - 0.052), (-0.095, ZB - 0.038)], 0.0180, r=0.0060)
    g.prism('gomma', [(-0.348, ZB + 0.016), (-0.364, ZB + 0.016), (-0.370, ZB - 0.082), (-0.354, ZB - 0.084)], -0.0190, 0.0190, r=0.0020)
    tilted_grip(g, 'legno', 13, 0.050, -0.056, 0.0168, 0.0220, rad=0.0070)
    trigger_guard(g, 'acciaio', 0.060, -0.004, ZB - 0.037, ZB - 0.078, th=0.0040, w=0.0050)
    g.part('trigger', pivot=(0, 0.030, ZB - 0.040))
    g.prism('acciaio', [(0.026, ZB - 0.040), (0.034, ZB - 0.040), (0.042, ZB - 0.066), (0.034, ZB - 0.066)], -0.0035, 0.0035, r=0.0008)
    # tamburo da 75 (parte mobile) nel pozzo del caricatore
    g.part('caricatore', pivot=(0, 0.095, ZB - 0.040))
    cz = ZB - 0.125
    g.lathe('acciaio', (0, 0.100, cz), [(-0.034, 0.068), (-0.028, 0.080), (0.028, 0.080), (0.034, 0.068)], segs=36, axis='x')
    g.lathe('acciaio', (0, 0.100, cz), [(-0.038, 0.022), (0.038, 0.022)], segs=20, axis='x')
    for k in range(12):
        a = radians(30 * k)
        g.box('acciaio', (0.0300, 0.100 + 0.058 * sin(a), cz + 0.058 * cos(a)), (0.0012, 0.0120, 0.0300), r=0.0004, rot=(-30 * k, 0, 0))
    g.box('acciaio', (0, 0.096, ZB - 0.064), (0.0250, 0.0300, 0.0300), r=0.0030)
    g.part('recip', pivot=(0.0170, 0.030, ZB + 0.010))
    g.rod('acciaio', (0.0150, 0.030, ZB + 0.010), (0.0360, 0.030, ZB + 0.010), 0.0036)
    g.box('acciaio', (0.0400, 0.030, ZB + 0.010), (0.0100, 0.0200, 0.0180), r=0.0035)
    g.part('corpo')
    bipod_folded(g, 0.340, ZB - 0.036, length=0.20)
    g.empty('muzzle', (0, 0.681, ZB), (0, 1, 0))
    g.empty('ads', (0, 0.108, nb), (0, 1, 0))
    g.empty('eject', (0.0180, -0.010, ZB + 0.010), (1, 0, 0))
    g.empty('grip_l', (0, 0.240, ZB - 0.004), (0, 1, 0))
    return g


@arma
def lmg_campana249():
    """CAMPANA-249: mitragliatrice leggera a nastro con scatola di cartucce, coperchio di alimentazione, scudo termico, maniglia, bipiede e ottica 3x."""
    g = Gun('lmg_campana249', tex=2048)
    ZB = 0.104
    g.part('corpo')
    g.box('acciaio', (0, 0.045, ZB - 0.003), (0.0520, 0.3100, 0.0820), r=0.0040, seg=3)
    g.box('acciaio', (0.0262, -0.020, ZB + 0.0100), (0.0040, 0.0900, 0.0200), cut=True)
    picatinny(g, 'alluminio', -0.095, 0.185, ZB + 0.0410)
    stock_side(g, 'polimero', [(-0.110, ZB + 0.030), (-0.250, ZB + 0.024), (-0.350, ZB + 0.030), (-0.358, ZB - 0.078), (-0.340, ZB - 0.084), (-0.250, ZB - 0.060), (-0.110, ZB - 0.040)], 0.0215, r=0.0075,
               pad=[(-0.350, ZB + 0.032), (-0.368, ZB + 0.032), (-0.374, ZB - 0.086), (-0.356, ZB - 0.086)])
    g.box('polimero', (0, -0.250, ZB - 0.012), (0.0300, 0.2000, 0.0100), cut=True) if False else None
    tilted_grip(g, 'polimero', 14, 0.050, -0.056, 0.0185, 0.0235, rad=0.0090)
    trigger_guard(g, 'polimero', 0.064, -0.004, ZB - 0.040, ZB - 0.082, th=0.0050, w=0.0080)
    g.part('trigger', pivot=(0, 0.032, ZB - 0.042))
    g.prism('acciaio', [(0.028, ZB - 0.042), (0.036, ZB - 0.042), (0.044, ZB - 0.068), (0.036, ZB - 0.068)], -0.0035, 0.0035, r=0.0008)
    g.part('corpo')
    # canna con scudo termico forato, tubo del gas, maniglia di trasporto, mire ribaltabili
    g.cyl('acciaio', 0, ZB, 0.200, 0.600, 0.0128, ch=0.0008)
    g.tube('acciaio', 0, ZB, 0.200, 0.430, 0.0225, 0.0185, segs=24)
    for k in range(6):
        g.box('acciaio', (0, 0.240 + k * 0.032, ZB), (0.0500, 0.0120, 0.0090), cut=True)
        g.box('acciaio', (0, 0.240 + k * 0.032, ZB), (0.0090, 0.0120, 0.0500), cut=True)
    g.cyl('acciaio', 0, ZB + 0.0240, 0.200, 0.470, 0.0100, ch=0.0006)
    flash_hider(g, 'acciaio', 0.590, ZB, rad=0.0150, length=0.065, prongs=4)
    g.rod('acciaio', (0, 0.060, ZB + 0.0460), (0, 0.060, ZB + 0.0900), 0.0090)
    g.rod('acciaio', (0, 0.240, ZB + 0.0300), (0, 0.240, ZB + 0.0900), 0.0090)
    g.box('acciaio', (0, 0.150, ZB + 0.0960), (0.0260, 0.2000, 0.0130), r=0.0050)
    g.box('acciaio', (0, 0.455, ZB + 0.0180), (0.0300, 0.0300, 0.0300), r=0.0030)
    # coperchio di alimentazione (parte mobile, cerniera dietro)
    g.part('cover', pivot=(0, -0.045, ZB + 0.0420))
    g.box('acciaio', (0, 0.050, ZB + 0.0480), (0.0540, 0.1700, 0.0110), r=0.0035)
    g.box('acciaio', (0, 0.050, ZB + 0.0560), (0.0360, 0.1100, 0.0070), r=0.0030)
    # scatola di cartucce con il nastro (parte mobile), a sinistra sotto il ricevitore
    g.part('caricatore', pivot=(-0.0300, 0.070, ZB - 0.040))
    g.box('acciaio', (-0.0420, 0.070, ZB - 0.110), (0.0700, 0.1500, 0.0900), r=0.0040)
    g.box('polimero', (-0.0420, 0.070, ZB - 0.158), (0.0720, 0.1520, 0.0080), r=0.0030)
    for k in range(7):
        g.box('ottone', (-0.0200 - k * 0.0, 0.020 + k * 0.016, ZB - 0.052 + k * 0.0045), (0.0300, 0.0100, 0.0100), r=0.0020)
    g.part('corpo')
    bipod_folded(g, 0.420, ZB - 0.048, length=0.20)
    zo = ZB + 0.0410 + 0.0068 + 0.0235 + 0.0215
    acog(g, 0.040, zo, length=0.150)
    g.part('charge', pivot=(0.0270, 0.100, ZB + 0.020))
    g.rod('acciaio', (0.0270, 0.100, ZB + 0.020), (0.0500, 0.100, ZB + 0.020), 0.0050)
    g.box('acciaio', (0.0560, 0.100, ZB + 0.020), (0.0120, 0.0260, 0.0200), r=0.0035)
    g.part('corpo')
    g.empty('muzzle', (0, 0.656, ZB), (0, 1, 0))
    g.empty('ads', (0, 0.040 - 0.075 - 0.0005, zo), (0, 1, 0))
    g.empty('eject', (0.0270, -0.020, ZB + 0.010), (1, 0, 0))
    g.empty('grip_l', (0, 0.300, ZB - 0.010), (0, 1, 0))
    return g
