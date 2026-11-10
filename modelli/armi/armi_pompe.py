"""Fucili a canna liscia. Origine: centro dell'impugnatura; y avanti; z su."""
from math import sin, cos, radians
from piezas import *
from armi_mitra import tilted_grip
from armi_pistole import trigger_guard
from armi_fucili2 import wrist_stock


@arma
def sg_cappellano():
    """CAPPELLANO: doppietta a canne affiancate con cani esterni, calcio e asta di noce, bascula che si apre verso il basso."""
    g = Gun('sg_cappellano', tex=2048)
    ZB = 0.082
    g.part('corpo')
    # bascula (acciaio inciso), perno, cani esterni
    g.prism('acciaio', [(0.010, ZB - 0.040), (0.010, ZB + 0.026), (0.092, ZB + 0.026), (0.092, ZB - 0.042), (0.040, ZB - 0.046)], -0.0200, 0.0200, r=0.0040, seg=3)
    g.lathe('acciaio', (0, 0.0870, ZB - 0.0340), [(-0.0210, 0.0070), (0.0210, 0.0070)], segs=16, axis='x')
    for sx in (-1, 1):
        g.prism('acciaio', [(0.020, ZB + 0.020), (0.006, ZB + 0.024), (-0.002, ZB + 0.030), (0.006, ZB + 0.034), (0.016, ZB + 0.030), (0.026, ZB + 0.024)], sx * 0.0100 - 0.0035, sx * 0.0100 + 0.0035, r=0.0010)
    wrist_stock(g, 'legno', ZB, -0.314, 0.040, y_front=0.050, hw=0.0170)
    g.prism('gomma', [(-0.306, ZB + 0.004), (-0.322, ZB + 0.000), (-0.330, ZB - 0.116), (-0.312, ZB - 0.122)], -0.0178, 0.0178, r=0.0030, seg=3) if False else None
    trigger_guard(g, 'acciaio', 0.075, 0.006, ZB - 0.044, ZB - 0.084, th=0.0050, w=0.0070)
    for k, sx in enumerate((-1, 1)):                             # due grilletti
        g.part('trigger' if k == 0 else 'trigger2', pivot=(0, 0.030 + k * 0.022, ZB - 0.044))
        g.prism('acciaio', [(0.028 + k * 0.022, ZB - 0.042), (0.034 + k * 0.022, ZB - 0.042), (0.040 + k * 0.022, ZB - 0.066), (0.034 + k * 0.022, ZB - 0.066)], -0.0030, 0.0030, r=0.0008)
    # canne affiancate + asta + bascula mobile (tutto in 'barrels': ruota attorno al perno)
    g.part('barrels', pivot=(0, 0.087, ZB - 0.034))
    for sx in (-1, 1):
        g.cyl('acciaio', sx * 0.0128, ZB, 0.060, 0.560, 0.0132, ch=0.0008, segs=24)
    g.box('acciaio', (0, 0.310, ZB + 0.0100), (0.0120, 0.500, 0.0090), r=0.0030)                  # nervatura fra le canne
    g.loft('legno', [(0.092, rrect(0.0270, 0.0210, 0.0100, 3, 0, ZB - 0.0140)), (0.290, rrect(0.0255, 0.0190, 0.0100, 3, 0, ZB - 0.0120))], r=0.0014)
    g.lathe('acciaio', (0, 0, ZB - 0.0140), [(0.285, 0.0215), (0.295, 0.0215)], segs=18) if False else None
    g.box('ottone', (0, 0.548, ZB + 0.0230), (0.0034, 0.0050, 0.0170), r=0.0006)               # pilastrino del mirino
    g.sphere('ottone', (0, 0.548, ZB + 0.0330), 0.0040, segs=12)
    g.part('corpo')
    g.empty('muzzle', (0, 0.562, ZB), (0, 1, 0))
    g.empty('ads', (0, 0.050, ZB + 0.0330), (0, 1, 0))
    g.empty('eject', (0.0200, 0.050, ZB + 0.020), (1, 0, 0))
    g.empty('grip_l', (0, 0.190, ZB - 0.014), (0, 1, 0))
    return g


@arma
def sg_breacher12():
    """BREACHER-12: fucile a pompa con calcio sintetico, scudo termico forato, serbatoio tubolare, mira a diaframma 'fantasma' e mirino a lama."""
    g = Gun('sg_breacher12', tex=2048)
    g.paint = {'polimero': (0.022, 0.023, 0.026)}
    ZB = 0.082
    g.part('corpo')
    # carcassa con finestra di espulsione a destra
    g.prism('acciaio', [(0.010, ZB - 0.034), (0.010, ZB + 0.028), (0.210, ZB + 0.028), (0.210, ZB - 0.032), (0.100, ZB - 0.040)], -0.0172, 0.0172, r=0.0040, seg=3)
    g.box('acciaio', (0.0172, 0.095, ZB + 0.010), (0.0040, 0.070, 0.0160), cut=True)
    for k in range(4):
        g.box('acciaio', (0, 0.025 + k * 0.0, ZB), (0.0001, 0.0001, 0.0001), cut=True) if False else None
    wrist_stock(g, 'polimero', ZB, -0.316, 0.040, y_front=0.040, hw=0.0170)
    trigger_guard(g, 'acciaio', 0.080, 0.012, ZB - 0.038, ZB - 0.076, th=0.0050, w=0.0070)
    g.part('trigger', pivot=(0, 0.036, ZB - 0.038))
    g.prism('acciaio', [(0.034, ZB - 0.038), (0.040, ZB - 0.038), (0.046, ZB - 0.062), (0.040, ZB - 0.062)], -0.0030, 0.0030, r=0.0008)
    g.part('corpo')
    # canna, serbatoio sotto la canna, scudo termico forato, mire
    g.cyl('acciaio', 0, ZB, 0.210, 0.700, 0.0128, ch=0.0008)
    g.cyl('acciaio', 0, ZB - 0.0235, 0.200, 0.600, 0.0096, ch=0.0006)
    g.cyl('acciaio', 0, ZB - 0.0235, 0.596, 0.612, 0.0116, ch=0.0006)
    g.tube('acciaio', 0, ZB + 0.0030, 0.240, 0.520, 0.0200, 0.0164, segs=24)
    for k in range(5):
        for a in (0, 90, 180, 270):
            g.lathe('acciaio', (0, 0.275 + k * 0.045, ZB + 0.0030), [(0.0150, 0.0050), (0.0230, 0.0050)], segs=10, axis='x', rot=(a, 0, 0), cut=True) if False else None
    aperture_rear(g, 0.050, ZB + 0.028, h=0.0230, ring_out=0.0105, ring_in=0.0052)
    post_front(g, 0.688, ZB + 0.0125, h=0.0364, w=0.0030, hood=True)
    # pompa (parte mobile) con rigature e barre di azione
    g.part('pump', pivot=(0, 0.300, ZB - 0.0235))
    g.loft('polimero', [(0.240, rrect(0.0235, 0.0210, 0.0100, 3, 0, ZB - 0.0235)), (0.390, rrect(0.0235, 0.0210, 0.0100, 3, 0, ZB - 0.0235))], r=0.0014)
    for k in range(7):
        g.box('polimero', (0, 0.262 + k * 0.020, ZB - 0.0235), (0.0520, 0.0070, 0.0090), cut=True)
    for sx in (-1, 1):
        g.rod('acciaio', (sx * 0.0150, 0.140, ZB - 0.0150), (sx * 0.0150, 0.245, ZB - 0.0150), 0.0030)
    g.part('corpo')
    g.empty('muzzle', (0, 0.702, ZB), (0, 1, 0))
    g.empty('ads', (0, 0.050, ZB + 0.0489), (0, 1, 0))
    g.empty('eject', (0.0180, 0.095, ZB + 0.010), (1, 0, 0))
    g.empty('grip_l', (0, 0.315, ZB - 0.0235), (0, 1, 0))
    return g


@arma
def sg_martello12():
    """MARTELLO-12: fucile semiautomatico da combattimento a ricevitore stampato, caricatore a scatola da otto cartucce, astina di polimero, punto rosso."""
    g = Gun('sg_martello12', tex=2048)
    g.paint = {'polimero': (0.020, 0.022, 0.024), 'acciaio': (0.040, 0.044, 0.050)}
    ZB = 0.098
    g.part('corpo')
    g.box('acciaio', (0, 0.050, ZB - 0.003), (0.0450, 0.2700, 0.0720), r=0.0035, seg=3)
    g.prism('acciaio', [(-0.092, ZB + 0.024), (-0.086, ZB + 0.034), (0.172, ZB + 0.034), (0.182, ZB + 0.022)], -0.0220, 0.0220, r=0.0070, seg=3)
    g.box('acciaio', (0.0225, 0.0, ZB + 0.0080), (0.0040, 0.0900, 0.0220), cut=True)
    picatinny(g, 'alluminio', -0.070, 0.175, ZB + 0.0340)
    g.cyl('acciaio', 0, ZB, 0.180, 0.580, 0.0132, ch=0.0008)
    g.cyl('acciaio', 0, ZB + 0.0340, 0.180, 0.400, 0.0110, ch=0.0006)
    g.loft('polimero', [(0.170, rrect(0.0255, 0.0245, 0.0100, 3, 0, ZB)), (0.390, rrect(0.0240, 0.0225, 0.0100, 3, 0, ZB))], r=0.0016)
    for k in range(5):
        for sx in (-1, 1):
            g.box('polimero', (sx * 0.0245, 0.215 + k * 0.032, ZB), (0.0030, 0.0200, 0.0120), r=0.0040, cut=True)
    g.tube('acciaio', 0, ZB, 0.560, 0.600, 0.0160, 0.0105, segs=24)
    for k in range(3):
        g.box('acciaio', (0, 0.570 + k * 0.011, ZB), (0.0400, 0.0040, 0.0070), cut=True)
    stock_side(g, 'polimero', [(-0.092, ZB + 0.020), (-0.250, ZB + 0.016), (-0.350, ZB + 0.020), (-0.358, ZB - 0.076), (-0.340, ZB - 0.082), (-0.250, ZB - 0.052), (-0.092, ZB - 0.040)], 0.0200, r=0.0070,
               pad=[(-0.350, ZB + 0.022), (-0.368, ZB + 0.022), (-0.374, ZB - 0.084), (-0.356, ZB - 0.084)])
    tilted_grip(g, 'polimero', 14, 0.050, -0.056, 0.0178, 0.0230, rad=0.0085)
    trigger_guard(g, 'acciaio', 0.060, -0.004, ZB - 0.039, ZB - 0.080, th=0.0045, w=0.0060)
    g.part('trigger', pivot=(0, 0.030, ZB - 0.040))
    g.prism('acciaio', [(0.026, ZB - 0.040), (0.034, ZB - 0.040), (0.042, ZB - 0.066), (0.034, ZB - 0.066)], -0.0035, 0.0035, r=0.0008)
    # caricatore a scatola: le cartucce stanno di traverso, quindi e' largo
    g.part('caricatore', pivot=(0, 0.085, ZB - 0.040))
    path = curved_mag(g, 'polimero', (0.085, ZB - 0.040), (0.112, ZB - 0.185), 0.030, 0.0360, 0.0230, 0.0360, 0.0240, ribs=0)
    for k in range(4):
        yy, zz = path[3 + 3 * k]
        g.box('polimero', (0.0364, yy, zz), (0.0014, 0.036, 0.0050), r=0.0004)
    g.part('charge', pivot=(0.0230, 0.030, ZB + 0.010))
    g.rod('acciaio', (0.0230, 0.030, ZB + 0.010), (0.0400, 0.030, ZB + 0.010), 0.0040)
    g.box('acciaio', (0.0440, 0.030, ZB + 0.010), (0.0100, 0.0200, 0.0160), r=0.0030)
    g.part('corpo')
    zdot = ZB + 0.0340 + 0.0068 + 0.0275 + 0.0225
    reflex_tube(g, 0.050, zdot)
    g.empty('muzzle', (0, 0.601, ZB), (0, 1, 0))
    g.empty('ads', (0, 0.050 - 0.034, zdot), (0, 1, 0))
    g.empty('eject', (0.0230, 0.0, ZB + 0.008), (1, 0, 0))
    g.empty('grip_l', (0, 0.280, ZB), (0, 1, 0))
    return g


@arma
def sg_apocalisse12():
    """APOCALISSE-12: fucile automatico a tamburo da venti, scocca squadrata con astina integrata, impugnatura anteriore, freno di bocca, olografico."""
    g = Gun('sg_apocalisse12', tex=2048)
    ZB = 0.100
    g.part('corpo')
    body = [(-0.170, ZB + 0.040), (0.150, ZB + 0.040), (0.215, ZB + 0.024), (0.300, ZB + 0.024), (0.300, ZB - 0.036), (0.215, ZB - 0.048), (0.060, ZB - 0.052), (-0.040, ZB - 0.052), (-0.080, ZB - 0.040), (-0.170, ZB - 0.024)]
    g.prism('polimero', body, -0.0290, 0.0290, r=0.0080, seg=3)
    g.prism('acciaio', [(-0.110, ZB + 0.040), (-0.110, ZB + 0.046), (0.150, ZB + 0.046), (0.150, ZB + 0.040)], -0.0210, 0.0210, r=0.0020)
    g.box('polimero', (0.0292, 0.020, ZB + 0.0100), (0.0040, 0.0800, 0.0200), cut=True)
    for k in range(5):
        for sx in (-1, 1):
            g.box('polimero', (sx * 0.0290, 0.185 + k * 0.022, ZB - 0.010), (0.0030, 0.0120, 0.0300), r=0.0040, cut=True)
    picatinny(g, 'alluminio', -0.100, 0.200, ZB + 0.046)
    g.cyl('acciaio', 0, ZB, 0.296, 0.600, 0.0135, ch=0.0008)
    muzzle_brake(g, 'acciaio', 0.560, ZB, rad=0.0200, length=0.080, ports=3)
    stock_side(g, 'polimero', [(-0.170, ZB + 0.030), (-0.260, ZB + 0.026), (-0.340, ZB + 0.030), (-0.348, ZB - 0.078), (-0.330, ZB - 0.084), (-0.260, ZB - 0.054), (-0.170, ZB - 0.040)], 0.0215, r=0.0075,
               pad=[(-0.340, ZB + 0.032), (-0.358, ZB + 0.032), (-0.364, ZB - 0.086), (-0.346, ZB - 0.086)])
    tilted_grip(g, 'polimero', 14, 0.050, -0.056, 0.0190, 0.0240, rad=0.0090)
    trigger_guard(g, 'polimero', 0.064, -0.004, ZB - 0.052, ZB - 0.092, th=0.0050, w=0.0080)
    g.part('trigger', pivot=(0, 0.032, ZB - 0.054))
    g.prism('acciaio', [(0.028, ZB - 0.054), (0.036, ZB - 0.054), (0.044, ZB - 0.078), (0.036, ZB - 0.078)], -0.0035, 0.0035, r=0.0008)
    g.prism('polimero', [(0.200, ZB - 0.046), (0.232, ZB - 0.046), (0.226, ZB - 0.140), (0.204, ZB - 0.140)], -0.0170, 0.0170, r=0.0080, seg=3)       # impugnatura anteriore
    # tamburo da 20 (parte mobile): disco con nervature e mozzo
    g.part('caricatore', pivot=(0, 0.060, ZB - 0.054))
    cz = ZB - 0.135
    g.lathe('polimero', (0, 0.060, cz), [(-0.046, 0.058), (-0.041, 0.072), (0.041, 0.072), (0.046, 0.058)], segs=36, axis='x')
    for k in range(10):
        a = radians(36 * k)
        g.box('acciaio', (0.0425, 0.060 + 0.052 * sin(a), cz + 0.052 * cos(a)), (0.0012, 0.0150, 0.0180), r=0.0004, rot=(-36 * k, 0, 0))
    g.lathe('acciaio', (0, 0.060, cz), [(-0.050, 0.020), (0.050, 0.020)], segs=20, axis='x')
    g.part('corpo')
    zh = ZB + 0.046 + 0.0068 + 0.0305 + 0.016 + 0.006 - 0.0170
    holo_sight(g, 0.050, ZB + 0.046 + 0.0525)
    g.empty('muzzle', (0, 0.641, ZB), (0, 1, 0))
    g.empty('ads', (0, 0.050 - 0.041, ZB + 0.046 + 0.0525), (0, 1, 0))
    g.empty('eject', (0.0290, 0.020, ZB + 0.010), (1, 0, 0))
    g.empty('grip_l', (0, 0.216, ZB - 0.090), (0, 1, 0))
    return g
