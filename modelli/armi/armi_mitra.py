"""Mitragliette e PDW. Origine: centro dell'impugnatura; y avanti; z su."""
from math import sin, cos, radians
from piezas import *


def tilted_grip(g, role, tilt, top, bottom, hw, dp, rad=0.007, cx=0.0):
    """Impugnatura a sezione arrotondata, inclinata di 'tilt' gradi (la cima va avanti), centrata sull'origine."""
    def P(h):
        return (cx + h * sin(radians(tilt)), h * cos(radians(tilt)))
    path = arc(P(top), P(bottom), 0.003, 9)
    hws = [hw + 0.0012 * sin(pi * i / 9) for i in range(10)]
    dps = [dp + 0.0018 * sin(pi * i / 9) for i in range(10)]
    g.sweep(role, path, hws, dps, rad=rad, seg=3)
    return P


def wire_stock(g, y_start, z, length=0.20, drop=0.035, half=0.0215, r=0.0035):
    """Calcio a filo (Uzi): due aste che scendono verso la spalla e una piastra ripiegata."""
    y_end = y_start - length
    for sx in (-1, 1):
        g.rod('acciaio', (sx * half, y_start, z), (sx * half, y_end + 0.012, z - drop), r)
    g.rod('acciaio', (-half, y_end + 0.012, z - drop), (half, y_end + 0.012, z - drop), r)
    g.prism('acciaio', [(y_end, z - drop - 0.052), (y_end + 0.010, z - drop - 0.052), (y_end + 0.014, z - drop + 0.030), (y_end + 0.002, z - drop + 0.030)], -0.0200, 0.0200, r=0.0014)


@arma
def smg_tarantola():
    """TARANTOLA: micro-mitra a otturatore aperto, scocca stampata a nervature, caricatore nell'impugnatura, calcio a filo, mire a diaframma."""
    g = Gun('smg_tarantola', tex=1024)
    ZB = 0.068
    g.part('corpo')
    # scocca
    g.box('acciaio', (0, 0.050, ZB), (0.0440, 0.250, 0.0560), r=0.0035, seg=3)
    g.box('acciaio', (0, -0.040, ZB + 0.0160), (0.0400, 0.060, 0.0300), r=0.0030)
    for sx in (-1, 1):                                       # nervature stampate sui fianchi
        for k in range(3):
            g.box('acciaio', (sx * 0.0222, 0.040, ZB - 0.0100 + k * 0.0100), (0.0016, 0.180, 0.0020), r=0.0004)
    for k in range(5):                                        # rigature del coperchio
        g.box('acciaio', (-0.0140 + k * 0.0070, 0.040, ZB + 0.0285), (0.0022, 0.200, 0.0018), cut=True)
    # finestra di espulsione a destra e feritoia del manubrio
    g.box('acciaio', (0.0222, -0.002, ZB + 0.0050), (0.0030, 0.060, 0.0170), cut=True)
    g.box('acciaio', (0, 0.020, ZB + 0.0290), (0.0042, 0.140, 0.0030), cut=True)
    # bocca: ghiera filettata e volata
    g.cyl('acciaio', 0, ZB, 0.175, 0.228, 0.0170, ch=0.0008)
    g.tube('acciaio', 0, ZB, 0.226, 0.250, 0.0105, 0.0045, segs=22)
    # impugnatura con il pozzo del caricatore
    P = tilted_grip(g, 'polimero', 6, 0.058, -0.052, 0.0195, 0.0245, rad=0.0080)
    g.box('polimero', (0, -0.026, ZB - 0.0300), (0.0440, 0.0460, 0.0280), r=0.0040)
    g.prism('acciaio', [(0.040, ZB - 0.030), (0.040, ZB - 0.060), (0.010, ZB - 0.068), (0.003, ZB - 0.040)], -0.0030, 0.0030, r=0.0010)      # ponticello
    g.prism('acciaio', [(0.020, ZB - 0.030), (0.027, ZB - 0.030), (0.033, ZB - 0.054), (0.025, ZB - 0.054)], -0.0035, 0.0035, r=0.0008)
    # secondo impugno anteriore
    tilted_grip_front = [(0.115, ZB - 0.027), (0.126, ZB - 0.028), (0.122, ZB - 0.095), (0.108, ZB - 0.095)]
    g.prism('polimero', tilted_grip_front, -0.0160, 0.0160, r=0.0070, seg=3)
    # caricatore (parte mobile) dentro l'impugnatura
    g.part('caricatore', pivot=(0, P(0.040)[0], P(0.040)[1]))
    g.box('acciaio', (0, P(-0.110)[0], P(-0.110)[1]), (0.0235, 0.0370, 0.150), rot=(-6, 0, 0), r=0.0016)
    for k in range(7):
        g.box('acciaio', (0.0120, P(-0.050 - k * 0.020)[0], P(-0.050 - k * 0.020)[1]), (0.0014, 0.030, 0.0040), rot=(-6, 0, 0))
    g.box('polimero', (0, P(-0.188)[0] - 0.003, P(-0.188)[1]), (0.0290, 0.0440, 0.0090), rot=(-6, 0, 0), r=0.0020)
    # mire: tacca a diaframma ribaltabile dietro, lama incappucciata davanti
    g.part('corpo')
    aperture_rear(g, -0.058, ZB + 0.0310, h=0.0260, ring_out=0.0100, ring_in=0.0044)
    post_front(g, 0.165, ZB + 0.0275, h=0.0230, w=0.0026, hood=True)
    # manubrio sul coperchio
    g.part('charge', pivot=(0, 0.0, ZB + 0.030))
    g.cyl('acciaio', 0, ZB + 0.0380, -0.010, 0.010, 0.0095, ch=0.0015, rot=(90, 0, 0))
    g.box('acciaio', (0, 0.0, ZB + 0.0310), (0.0200, 0.0180, 0.0060), r=0.0012)
    g.part('corpo')
    wire_stock(g, -0.075, ZB + 0.002, 0.19, 0.030)
    g.empty('muzzle', (0, 0.251, ZB), (0, 1, 0))
    g.empty('ads', (0, -0.058, ZB + 0.0310 + 0.0260 - 0.0020), (0, 1, 0))
    g.empty('eject', (0.0225, -0.002, ZB + 0.005), (1, 0, 0))
    g.empty('grip_l', (0, 0.120, ZB - 0.060), (0, 1, 0))
    return g


@arma
def smg_vesper9():
    """VESPER-9: mitra 9 mm a otturatore chiuso, scocca tubolare con astina a feritoie, caricatore curvo, calcio telescopico, punto rosso."""
    g = Gun('smg_vesper9', tex=1024)
    ZB = 0.090
    g.part('corpo')
    # scocca: tubo di acciaio con finestra e guida
    g.lathe('acciaio', (0, 0, ZB), [(-0.082, 0.0232), (-0.080, 0.0240), (0.120, 0.0240), (0.122, 0.0232)], segs=32)
    g.box('acciaio', (0, -0.092, ZB - 0.002), (0.0420, 0.022, 0.0400), r=0.0040)          # culatta
    g.box('acciaio', (0.0215, -0.030, ZB + 0.0050), (0.0030, 0.048, 0.0170), cut=True)    # finestra di espulsione
    g.box('acciaio', (0, 0.022, ZB + 0.0222), (0.0200, 0.210, 0.0100), r=0.0016)
    picatinny(g, 'alluminio', -0.070, 0.120, ZB + 0.0238, w=0.0212)
    # blocco del grilletto e impugnatura (polimero)
    g.box('polimero', (0, 0.002, ZB - 0.0370), (0.0360, 0.1000, 0.0320), r=0.0050)
    P = tilted_grip(g, 'polimero', 18, 0.044, -0.056, 0.0170, 0.0220, rad=0.0080)
    g.box('polimero', (0, 0.0, ZB - 0.0345), (0.0390, 0.0300, 0.0100), r=0.0030)
    g.prism('acciaio', [(0.056, ZB - 0.040), (0.056, ZB - 0.066), (0.020, ZB - 0.066), (0.012, ZB - 0.040)], -0.0030, 0.0030, r=0.0010)       # ponticello
    g.prism('acciaio', [(0.030, ZB - 0.040), (0.037, ZB - 0.040), (0.044, ZB - 0.060), (0.036, ZB - 0.060)], -0.0035, 0.0035, r=0.0008)
    # astina polimerica con feritoie, canna e mirino a tunnel
    g.loft('polimero', [(0.060, rrect(0.0235, 0.0240, 0.0090, 3, 0, ZB)), (0.215, rrect(0.0220, 0.0220, 0.0100, 3, 0, ZB - 0.002))], r=0.0014)
    for k in range(6):
        for sx in (-1, 1):
            g.box('polimero', (sx * 0.0225, 0.090 + k * 0.020, ZB - 0.002), (0.0030, 0.0110, 0.0190), r=0.0020, cut=True)
    g.cyl('acciaio', 0, ZB, 0.200, 0.262, 0.0108, ch=0.0006)
    g.tube('acciaio', 0, ZB, 0.250, 0.275, 0.0175, 0.0122, segs=24)                        # mirino incappucciato
    g.box('acciaio', (0, 0.262, ZB), (0.0030, 0.0030, 0.0200), cut=True) if False else None
    g.lathe('acciaio', (0, 0.262, ZB + 0.0135), [(0.0, 0.0030), (0.0, 0.0030)], segs=3) if False else None
    # caricatore curvo da 30 (parte mobile)
    g.part('caricatore', pivot=(0, 0.056, ZB - 0.052))
    curved_mag(g, 'acciaio', (0.056, ZB - 0.052), (0.092, ZB - 0.185), 0.022, 0.0125, 0.0165, 0.0140, 0.0170, ribs=0)
    g.part('corpo')
    # punto rosso a tubo sulla guida
    reflex_tube(g, 0.045, ZB + 0.0238 + 0.0068 + 0.0275 + 0.0225 - 0.0068 - 0.0)
    # calcio telescopico: due aste e piastra
    for sx in (-1, 1):
        g.rod('acciaio', (sx * 0.0160, -0.100, ZB - 0.004), (sx * 0.0160, -0.250, ZB - 0.020), 0.0036)
    g.prism('polimero', [(-0.248, ZB - 0.070), (-0.262, ZB - 0.070), (-0.266, ZB + 0.024), (-0.252, ZB + 0.024)], -0.0220, 0.0220, r=0.0030, seg=3)
    g.prism('polimero', [(-0.100, ZB - 0.030), (-0.250, ZB - 0.034), (-0.250, ZB - 0.014), (-0.100, ZB - 0.006)], -0.0160, 0.0160, r=0.0040) if False else None
    # manubrio di armamento a sinistra
    g.part('charge', pivot=(-0.0300, 0.050, ZB))
    g.rod('acciaio', (-0.0240, 0.060, ZB + 0.004), (-0.0420, 0.060, ZB + 0.004), 0.0042)
    g.box('acciaio', (-0.0450, 0.060, ZB + 0.004), (0.0110, 0.0300, 0.0100), r=0.0020)
    g.part('corpo')
    zdot = ZB + 0.0238 + 0.0275 + 0.0225
    g.empty('muzzle', (0, 0.278, ZB), (0, 1, 0))
    g.empty('ads', (0, 0.045 - 0.034, zdot), (0, 1, 0))
    g.empty('eject', (0.0220, -0.030, ZB + 0.004), (1, 0, 0))
    g.empty('grip_l', (0, 0.150, ZB), (0, 1, 0))
    return g


@arma
def smg_cimbalo():
    """CIMBALO-45: mitra .45 in polimero con guida continua, astina corta, calcio laterale ripiegabile (esteso) e olografico."""
    g = Gun('smg_cimbalo', tex=1024)
    g.paint = {'polimero': (0.040, 0.042, 0.040)}
    ZB = 0.092
    g.part('corpo')
    g.loft('polimero', [(-0.095, rrect(0.0220, 0.0300, 0.0090, 3, 0, ZB - 0.004)), (0.150, rrect(0.0225, 0.0290, 0.0100, 3, 0, ZB - 0.004))], r=0.0018)
    g.box('polimero', (0.0225, -0.020, ZB + 0.0020), (0.0030, 0.058, 0.0180), cut=True)
    picatinny(g, 'alluminio', -0.090, 0.200, ZB + 0.0262, w=0.0212)
    g.loft('polimero', [(0.140, rrect(0.0215, 0.0270, 0.0090, 3, 0, ZB - 0.006)), (0.215, rrect(0.0195, 0.0230, 0.0090, 3, 0, ZB - 0.008))], r=0.0014)
    g.cyl('acciaio', 0, ZB, 0.200, 0.262, 0.0108, ch=0.0006)
    g.tube('acciaio', 0, ZB, 0.250, 0.280, 0.0135, 0.0075, segs=24)
    for k in range(4):
        g.box('polimero', (0.0, 0.158 + k * 0.016, ZB - 0.030), (0.030, 0.0060, 0.0050), cut=True)       # feritoie sotto
    # blocco grilletto, impugnatura, ponticello
    g.box('polimero', (0, 0.0, ZB - 0.0470), (0.0380, 0.1100, 0.0300), r=0.0050)
    P = tilted_grip(g, 'polimero', 14, 0.040, -0.058, 0.0170, 0.0225, rad=0.0080)
    g.prism('acciaio', [(0.058, ZB - 0.050), (0.058, ZB - 0.074), (0.020, ZB - 0.074), (0.012, ZB - 0.050)], -0.0030, 0.0030, r=0.0010)
    g.prism('acciaio', [(0.030, ZB - 0.048), (0.037, ZB - 0.048), (0.044, ZB - 0.068), (0.036, ZB - 0.068)], -0.0035, 0.0035, r=0.0008)
    # caricatore dritto da 25 in polimero (parte mobile)
    g.part('caricatore', pivot=(0, 0.082, ZB - 0.062))
    g.box('polimero', (0, 0.084, ZB - 0.140), (0.0270, 0.0400, 0.150), rot=(-4, 0, 0), r=0.0030)
    for k in range(5):
        g.box('polimero', (0.0138, 0.084, ZB - 0.100 - k * 0.020), (0.0014, 0.030, 0.0040), rot=(-4, 0, 0))
    g.box('polimero', (0, 0.0860, ZB - 0.2200), (0.0300, 0.0460, 0.0100), rot=(-4, 0, 0), r=0.0022)
    g.part('corpo')
    holo_sight(g, 0.050, ZB + 0.0262 + 0.0160 + 0.0060 + 0.0305 + 0.0020)
    # calcio laterale esteso: due fili e piastra
    for sx in (-1, 1):
        g.rod('acciaio', (sx * 0.0210, -0.095, ZB + 0.010), (sx * 0.0210, -0.250, ZB - 0.020), 0.0036)
    g.prism('polimero', [(-0.250, ZB - 0.070), (-0.262, ZB - 0.070), (-0.266, ZB + 0.018), (-0.252, ZB + 0.018)], -0.0210, 0.0210, r=0.0030, seg=3)
    # manubrio (parte mobile) in alto a sinistra
    g.part('charge', pivot=(0, 0.060, ZB + 0.0260))
    g.box('acciaio', (-0.0150, 0.060, ZB + 0.0290), (0.0300, 0.0200, 0.0100), r=0.0020)
    g.part('corpo')
    zh = ZB + 0.0262 + 0.0160 + 0.0060 + 0.0305 + 0.0020
    g.empty('muzzle', (0, 0.281, ZB), (0, 1, 0))
    g.empty('ads', (0, 0.050 - 0.041, zh), (0, 1, 0))
    g.empty('eject', (0.0230, -0.020, ZB + 0.002), (1, 0, 0))
    g.empty('grip_l', (0, 0.150, ZB - 0.010), (0, 1, 0))
    return g


@arma
def pdw_bordone():
    """BORDONE: arma per la difesa personale bullpup, corpo organico, caricatore da 50 piatto sopra la canna, anello di mira integrato."""
    g = Gun('pdw_bordone', tex=1024)
    ZB = 0.088
    g.part('corpo')
    # corpo organico in polimero: profilo laterale con impugnatura a foro, estruso con grande raggio
    body = [(-0.175, ZB - 0.060), (-0.178, ZB + 0.010), (-0.160, ZB + 0.034), (0.040, ZB + 0.034), (0.100, ZB + 0.022), (0.205, ZB + 0.014),
            (0.212, ZB - 0.010), (0.205, ZB - 0.036), (0.100, ZB - 0.040), (0.060, ZB - 0.050), (0.040, ZB - 0.040), (0.005, ZB - 0.036), (-0.030, ZB - 0.045), (-0.060, ZB - 0.068)]
    g.prism('polimero', body, -0.0235, 0.0235, r=0.0075, seg=4)
    # foro dell'impugnatura (pollice) e ponticello integrato
    g.prism('polimero', [(0.010, ZB - 0.050), (0.040, ZB - 0.050), (0.040, ZB - 0.100), (0.010, ZB - 0.100)], -0.0300, 0.0300, r=0.0040, cut=True)
    P = tilted_grip(g, 'polimero', 12, 0.030, -0.058, 0.0185, 0.0230, rad=0.0090)
    g.box('polimero', (0, -0.045, ZB - 0.058), (0.0400, 0.0800, 0.0200), r=0.0060)
    g.prism('acciaio', [(0.050, ZB - 0.052), (0.056, ZB - 0.052), (0.062, ZB - 0.080), (0.054, ZB - 0.080)], -0.0035, 0.0035, r=0.0008)    # grilletto
    # guida superiore; il caricatore piatto sta sulla guida e l'anello di mira integrato sta sopra il caricatore
    picatinny(g, 'alluminio', -0.130, 0.120, ZB + 0.0340)
    zs = ZB + 0.0850
    g.part('lente_dietro', pivot=(0, -0.026, zs))
    g.lathe('vetro', (0, -0.026, zs), [(-0.0005, 0.0125), (0.0005, 0.0125)], segs=28)
    g.part('retino', pivot=(0, 0.002, zs))
    g.lathe('retino', (0, 0.002, zs), [(-0.0002, 0.0120), (0.0002, 0.0120)], segs=28)
    g.part('corpo')
    g.tube('alluminio', 0, zs, -0.034, 0.050, 0.0185, 0.0120, segs=28)
    for yy in (-0.026, 0.040):                                                   # due ponti sul corpo
        g.box('alluminio', (0, yy, ZB + 0.0540), (0.0300, 0.0100, 0.0420), r=0.0030)
    # canna con bocca corta e spegnifiamma
    g.cyl('acciaio', 0, ZB, 0.195, 0.246, 0.0115, ch=0.0006)
    g.tube('acciaio', 0, ZB, 0.236, 0.272, 0.0150, 0.0085, segs=24)
    for k in range(3):
        g.box('acciaio', (0, 0.242 + k * 0.0100, ZB), (0.0360, 0.0036, 0.0060), cut=True)
    # caricatore piatto da 50 sopra la canna (parte mobile, si sfila verso l'alto)
    g.part('caricatore', pivot=(0, 0.060, ZB + 0.0420))
    g.prism('polimero', [(-0.086, ZB + 0.0415), (0.150, ZB + 0.0415), (0.158, ZB + 0.0535), (0.150, ZB + 0.0655), (-0.086, ZB + 0.0655), (-0.094, ZB + 0.0535)], -0.0185, 0.0185, r=0.0045, seg=3)
    for k in range(7):
        g.box('acciaio', (0.0190, 0.000 + k * 0.026 - 0.015, ZB + 0.0535), (0.0014, 0.0120, 0.0090), r=0.0003)
    g.part('corpo')
    # piastra di calcio in gomma e manubrio di armamento
    g.prism('gomma', [(-0.178, ZB - 0.062), (-0.188, ZB - 0.062), (-0.190, ZB + 0.012), (-0.180, ZB + 0.014)], -0.0215, 0.0215, r=0.0030, seg=3)
    g.part('charge', pivot=(0, -0.080, ZB + 0.040))
    g.box('acciaio', (-0.026, -0.082, ZB + 0.040), (0.0120, 0.0300, 0.0070), r=0.0016)
    g.part('corpo')
    g.empty('muzzle', (0, 0.273, ZB), (0, 1, 0))
    g.empty('ads', (0, -0.026, ZB + 0.0850), (0, 1, 0))
    g.empty('eject', (0.0, 0.040, ZB - 0.040), (0, 0, -1))
    g.empty('grip_l', (0, 0.115, ZB - 0.030), (0, 1, 0))
    return g
