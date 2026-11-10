"""Fucili di precisione: Sentinella (a otturatore con telaio) e Altare-50 (antimateriale). Origine: centro dell'impugnatura; y avanti; z su."""
from math import sin, cos, radians
from piezas import *
from armi_mitra import tilted_grip
from armi_pistole import trigger_guard


@arma
def snp_sentinella():
    """SENTINELLA: fucile da cecchino a otturatore su telaio di alluminio, canna pesante con freno, calcio con guancia regolabile, bipiede, ottica 8x."""
    g = Gun('snp_sentinella', tex=2048)
    g.paint = {'alluminio': (0.13, 0.100, 0.052)}
    ZB = 0.100
    g.part('corpo')
    # telaio-ricevitore e guida
    g.box('alluminio', (0, 0.035, ZB - 0.004), (0.0340, 0.330, 0.0720), r=0.0035, seg=3)
    g.box('alluminio', (0.0172, 0.040, ZB + 0.0080), (0.0030, 0.080, 0.0200), cut=True)
    picatinny(g, 'alluminio', -0.125, 0.195, ZB + 0.0320)
    # astina a tubo con asole e dado
    g.loft('alluminio', [(0.185, rrect(0.0260, 0.0270, 0.0100, 3, 0, ZB - 0.0040)), (0.470, rrect(0.0240, 0.0250, 0.0100, 3, 0, ZB - 0.0040))], r=0.0014)
    slots_on(g, 'alluminio', 0, ZB - 0.0040, 0.215, 0.450, 0.0250, 0.0260, n=6, length=0.034, sides=True, bottom=True)
    g.cyl('acciaio', 0, ZB, 0.200, 0.790, 0.0140, ch=0.0008)
    g.cyl('acciaio', 0, ZB, 0.465, 0.800, 0.0128, ch=0.0008)
    muzzle_brake(g, 'acciaio', 0.790, ZB, rad=0.0210, length=0.090, ports=4)
    # calcio a telaio: guancia regolabile, calciolo
    zt = ZB - 0.012
    stock_side(g, 'polimero', [(-0.125, zt + 0.030), (-0.215, zt + 0.032), (-0.250, zt + 0.050), (-0.350, zt + 0.054), (-0.360, zt - 0.074), (-0.342, zt - 0.078),
                               (-0.262, zt - 0.040), (-0.125, zt - 0.036)], 0.0185, r=0.0070,
               pad=[(-0.350, zt + 0.056), (-0.368, zt + 0.056), (-0.374, zt - 0.076), (-0.358, zt - 0.076)])
    g.box('gomma', (0, -0.296, zt + 0.0570), (0.0300, 0.100, 0.0060), r=0.0022)
    for sx in (-1, 1):
        g.rod('alluminio', (sx * 0.0090, -0.292, zt + 0.0560), (sx * 0.0090, -0.292, zt + 0.0420), 0.0030)
    tilted_grip(g, 'polimero', 22, 0.050, -0.058, 0.0185, 0.0235, rad=0.0090)
    trigger_guard(g, 'alluminio', 0.062, -0.004, ZB - 0.040, ZB - 0.082, th=0.0050, w=0.0070)
    g.part('trigger', pivot=(0, 0.030, ZB - 0.042))
    g.prism('acciaio', [(0.026, ZB - 0.042), (0.034, ZB - 0.042), (0.042, ZB - 0.066), (0.034, ZB - 0.066)], -0.0035, 0.0035, r=0.0008)
    g.part('caricatore', pivot=(0, 0.080, ZB - 0.040))
    g.box('acciaio', (0, 0.082, ZB - 0.090), (0.0250, 0.0640, 0.0900), r=0.0030)
    g.box('polimero', (0, 0.082, ZB - 0.137), (0.0280, 0.0700, 0.0100), r=0.0022)
    # otturatore con maniglia a destra (figlia dell'otturatore)
    g.part('bolt', pivot=(0, 0.000, ZB))
    g.lathe('acciaio', (0, 0, ZB), [(-0.080, 0.0120), (0.060, 0.0120)], segs=22)
    g.part('bolt_handle', pivot=(0, -0.070, ZB), parent='bolt')
    g.rod('acciaio', (0.0100, -0.070, ZB), (0.0480, -0.082, ZB - 0.008), 0.0050)
    g.sphere('acciaio', (0.0560, -0.085, ZB - 0.0090), 0.0135, segs=14)
    g.part('corpo')
    bipod_folded(g, 0.420, ZB - 0.034, length=0.16)
    # ottica 8x
    L = 0.360
    y_oc = -0.115
    zs = ZB + 0.0320 + 0.0068 + 0.0155 + 0.0240 + 0.0055
    scope(g, y_oc + L / 2, zs, length=L, tube=0.0155, bell_obj=0.0400, bell_oc=0.0245, mount_h=0.024)
    g.empty('muzzle', (0, 0.882, ZB), (0, 1, 0))
    g.empty('ads', (0, y_oc - 0.0005, zs), (0, 1, 0))
    g.empty('eject', (0.0180, 0.040, ZB + 0.008), (1, 0, 0))
    g.empty('grip_l', (0, 0.300, ZB - 0.004), (0, 1, 0))
    return g


@arma
def amr_altare50():
    """ALTARE-50: fucile antimateriale semiautomatico calibro .50, freno di bocca a sfoghi, bipiede, maniglia di trasporto, ottica 10x."""
    g = Gun('amr_altare50', tex=2048)
    g.paint = {'acciaio': (0.042, 0.050, 0.037), 'alluminio': (0.040, 0.046, 0.034)}
    ZB = 0.104
    g.part('corpo')
    # ricevitore massiccio e guida lunga
    g.box('acciaio', (0, 0.020, ZB - 0.003), (0.0640, 0.4400, 0.0950), r=0.0050, seg=3)
    g.box('acciaio', (0.0322, 0.010, ZB + 0.0100), (0.0030, 0.120, 0.0260), cut=True)
    for k in range(4):
        g.box('acciaio', (0.0322, 0.100 + k * 0.032, ZB - 0.020), (0.0030, 0.020, 0.0090), cut=True)
    picatinny(g, 'alluminio', -0.190, 0.240, ZB + 0.0445)
    # canna con scanalature e freno
    g.cyl('acciaio', 0, ZB, 0.240, 0.800, 0.0225, ch=0.0010)
    for k in range(6):
        g.box('acciaio', (0, 0.330 + k * 0.034, ZB), (0.060, 0.0120, 0.0090), cut=True)
        g.box('acciaio', (0, 0.330 + k * 0.034, ZB), (0.0090, 0.0120, 0.060), cut=True)
    g.cyl('acciaio', 0, ZB, 0.240, 0.330, 0.0300, ch=0.0012)
    muzzle_brake(g, 'acciaio', 0.790, ZB, rad=0.0320, length=0.150, ports=5)
    # maniglia di trasporto
    g.rod('acciaio', (0, 0.020, ZB + 0.0460), (0, 0.020, ZB + 0.0900), 0.0100)
    g.box('acciaio', (0, 0.020, ZB + 0.0960), (0.0300, 0.1400, 0.0120), r=0.0050)
    # calcio a tubo con calciolo molto grande
    zt = ZB - 0.020
    g.cyl('acciaio', 0, zt, -0.200, -0.420, 0.0220, ch=0.0010)
    g.prism('gomma', [(-0.412, zt - 0.100), (-0.446, zt - 0.100), (-0.452, zt + 0.062), (-0.418, zt + 0.062)], -0.0360, 0.0360, r=0.0060, seg=3)
    g.box('polimero', (0, -0.300, zt + 0.0320), (0.0420, 0.2200, 0.0180), r=0.0070)            # guancia
    tilted_grip(g, 'polimero', 22, 0.050, -0.060, 0.0215, 0.0260, rad=0.0100)
    trigger_guard(g, 'acciaio', 0.070, -0.008, ZB - 0.052, ZB - 0.095, th=0.0055, w=0.0080)
    g.part('trigger', pivot=(0, 0.034, ZB - 0.055))
    g.prism('acciaio', [(0.030, ZB - 0.055), (0.038, ZB - 0.055), (0.046, ZB - 0.082), (0.038, ZB - 0.082)], -0.0040, 0.0040, r=0.0008)
    g.part('caricatore', pivot=(0, 0.080, ZB - 0.052))
    g.box('acciaio', (0, 0.082, ZB - 0.115), (0.0420, 0.1000, 0.1250), r=0.0040)
    g.box('polimero', (0, 0.082, ZB - 0.180), (0.0460, 0.1080, 0.0110), r=0.0025)
    g.part('charge', pivot=(0.0330, -0.110, ZB + 0.020))
    g.rod('acciaio', (0.0330, -0.110, ZB + 0.020), (0.0620, -0.110, ZB + 0.020), 0.0060)
    g.sphere('acciaio', (0.0680, -0.110, ZB + 0.020), 0.0150, segs=14)
    g.part('corpo')
    bipod_folded(g, 0.470, ZB - 0.062, length=0.22)
    L = 0.400
    y_oc = -0.140
    zs = ZB + 0.0445 + 0.0068 + 0.0175 + 0.0240 + 0.0055
    scope(g, y_oc + L / 2, zs, length=L, tube=0.0175, bell_obj=0.0450, bell_oc=0.0260, mount_h=0.024)
    g.empty('muzzle', (0, 0.942, ZB), (0, 1, 0))
    g.empty('ads', (0, y_oc - 0.0005, zs), (0, 1, 0))
    g.empty('eject', (0.0330, 0.010, ZB + 0.010), (1, 0, 0))
    g.empty('grip_l', (0, 0.320, ZB - 0.020), (0, 1, 0))
    return g
