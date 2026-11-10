"""Fucili: AK, bullpup, fucile da battaglia, carabina a leva, fucile a otturatore. Origine: centro dell'impugnatura; y avanti; z su."""
from math import sin, cos, radians
from piezas import *
from armi_mitra import tilted_grip
from armi_pistole import trigger_guard


def wrist_stock(g, role, ZB, butt_y, comb_z, y_front=0.030, hw=0.0170, pad=True):
    """Calcio a collo di cigno per leva e otturatore: il collo (alto 7,5 cm) passa dalla mano (origine) e sta sotto la corsa
    dell'otturatore; la guancia sale piano verso la spalla."""
    pts = [(0.060, ZB - 0.016), (0.030, ZB - 0.034), (-0.020, ZB - 0.046), (-0.090, ZB - 0.034), (-0.180, ZB - 0.016), (-0.290, ZB - 0.004),
           (butt_y + 0.012, ZB + comb_z - 0.036), (butt_y, ZB + comb_z - 0.040), (butt_y - 0.010, ZB - 0.118), (butt_y + 0.010, ZB - 0.124),
           (butt_y + 0.090, ZB - 0.098), (-0.150, ZB - 0.092), (-0.070, ZB - 0.104), (-0.010, ZB - 0.118), (0.035, ZB - 0.104), (0.065, ZB - 0.060)]
    g.prism(role, pts, -hw, hw, r=0.0055, seg=3)
    if pad:
        g.prism('gomma', [(butt_y + 0.008, ZB + comb_z - 0.038), (butt_y - 0.010, ZB + comb_z - 0.042), (butt_y - 0.020, ZB - 0.120), (butt_y - 0.002, ZB - 0.126)],
                -hw - 0.0008, hw + 0.0008, r=0.0030, seg=3)


@arma
def ak_arcangelo():
    """ARCANGELO-47: fucile d'assalto a ricevitore stampato, mobili di legno, tubo del gas sopra la canna, caricatore a banana, freno di bocca obliquo."""
    g = Gun('ak_arcangelo', tex=2048)
    g.paint = {'legno_scuro': (0.032, 0.010, 0.003), 'legno_chiaro': (0.115, 0.034, 0.009)}
    ZB = 0.100
    g.part('corpo')
    g.box('acciaio', (0, 0.025, ZB - 0.0050), (0.0340, 0.2450, 0.0660), r=0.0030, seg=3)
    g.prism('acciaio', [(-0.098, ZB + 0.020), (-0.092, ZB + 0.033), (0.138, ZB + 0.033), (0.148, ZB + 0.020)], -0.0165, 0.0165, r=0.0065, seg=3)
    g.box('acciaio', (0.0172, -0.010, ZB + 0.0090), (0.0040, 0.0900, 0.0200), cut=True)                  # finestra di espulsione
    for k in range(3):
        g.box('acciaio', (0.0172, 0.040 + k * 0.026, ZB - 0.020), (0.0020, 0.0140, 0.0070), cut=True)    # asole di raffreddamento
    g.prism('acciaio', [(-0.055, ZB + 0.024), (0.022, ZB + 0.024), (0.030, ZB + 0.010), (-0.040, ZB - 0.014), (-0.052, ZB - 0.004)], 0.0172, 0.0190, r=0.0008)   # leva di sicurezza
    # tubo del gas, astine di legno, blocco del gas, mirino
    g.cyl('acciaio', 0, ZB + 0.0325, 0.145, 0.372, 0.0125, ch=0.0008)
    g.loft('legno', [(0.150, rrect(0.0185, 0.0170, 0.0080, 3, 0, ZB + 0.0320)), (0.340, rrect(0.0175, 0.0150, 0.0080, 3, 0, ZB + 0.0320))], r=0.0014)
    g.loft('legno', [(0.150, rrect(0.0230, 0.0240, 0.0100, 3, 0, ZB - 0.0120)), (0.330, rrect(0.0220, 0.0200, 0.0100, 3, 0, ZB - 0.0100))], r=0.0014)
    for k in range(5):
        g.box('legno', (0.0, 0.176 + k * 0.026, ZB - 0.0120), (0.0500, 0.0040, 0.0036), cut=True) if k % 2 == 0 else None
    g.cyl('acciaio', 0, ZB, 0.145, 0.450, 0.0108, ch=0.0006)
    g.cyl('acciaio', 0, ZB + 0.0030, 0.340, 0.372, 0.0170, ch=0.0008)
    g.cyl('acciaio', 0, ZB, 0.385, 0.412, 0.0150, ch=0.0008)                                             # base del mirino
    post_front(g, 0.398, ZB + 0.0120, h=0.0369, w=0.0026, hood=True)
    g.lathe('acciaio', (0, 0, ZB), [(0.440, 0.0120), (0.470, 0.0128), (0.470, 0.0060)], segs=22)         # freno obliquo
    g.box('acciaio', (0, 0.462, ZB + 0.0110), (0.030, 0.0100, 0.0060), rot=(-24, 0, 0), cut=True)
    g.box('acciaio', (0, 0.448, ZB), (0.0050, 0.0050, 0.030), cut=True) if False else None
    # tacca di mira regolabile sul tubo (il fondo dell'intaglio e' alla quota della punta del mirino)
    nb = ZB + 0.0489
    notch_rear(g, 0.108, ZB + 0.0440, h=0.0180, w=0.0190, gap=0.0036, depth=0.0100, nb=nb)
    # calcio e impugnatura di legno, calciolo d'acciaio
    stock_side(g, 'legno', [(-0.095, ZB + 0.020), (-0.250, ZB + 0.010), (-0.350, ZB + 0.014), (-0.358, ZB - 0.078), (-0.340, ZB - 0.082), (-0.250, ZB - 0.052), (-0.095, ZB - 0.038)], 0.0180, r=0.0060)
    g.prism('acciaio', [(-0.348, ZB + 0.016), (-0.362, ZB + 0.016), (-0.368, ZB - 0.082), (-0.352, ZB - 0.084)], -0.0185, 0.0185, r=0.0020)
    tilted_grip(g, 'legno', 13, 0.050, -0.056, 0.0168, 0.0220, rad=0.0070)
    trigger_guard(g, 'acciaio', 0.060, -0.004, ZB - 0.037, ZB - 0.078, th=0.0040, w=0.0050)
    g.part('trigger', pivot=(0, 0.030, ZB - 0.040))
    g.prism('acciaio', [(0.026, ZB - 0.040), (0.034, ZB - 0.040), (0.042, ZB - 0.066), (0.034, ZB - 0.066)], -0.0035, 0.0035, r=0.0008)
    # caricatore a banana (parte mobile)
    g.part('caricatore', pivot=(0, 0.085, ZB - 0.040))
    path = curved_mag(g, 'acciaio', (0.085, ZB - 0.040), (0.168, ZB - 0.205), 0.046, 0.0125, 0.0185, 0.0125, 0.0190, ribs=0)
    for k in range(6):
        yy, zz = path[2 + 2 * k]
        g.box('acciaio', (0.0128, yy, zz), (0.0014, 0.030, 0.0040), r=0.0004)
    # maniglia di armamento a destra (parte mobile: avanti e indietro a ogni colpo)
    g.part('recip', pivot=(0.0170, 0.030, ZB + 0.010))
    g.rod('acciaio', (0.0150, 0.030, ZB + 0.010), (0.0360, 0.030, ZB + 0.010), 0.0036)
    g.box('acciaio', (0.0400, 0.030, ZB + 0.010), (0.0100, 0.0200, 0.0180), r=0.0035)
    g.part('corpo')
    g.empty('muzzle', (0, 0.471, ZB), (0, 1, 0))
    g.empty('ads', (0, 0.108, nb), (0, 1, 0))
    g.empty('eject', (0.0180, -0.010, ZB + 0.010), (1, 0, 0))
    g.empty('grip_l', (0, 0.240, ZB - 0.004), (0, 1, 0))
    return g


@arma
def bp_orifiamma():
    """ORIFIAMMA: fucile bullpup in polimero, caricatore dietro l'impugnatura, impugnatura anteriore verticale, ottica integrata."""
    g = Gun('bp_orifiamma', tex=2048)
    g.paint = {'polimero': (0.030, 0.036, 0.018), 'polimero_usura': (0.085, 0.095, 0.050)}
    ZB = 0.098
    g.part('corpo')
    body = [(-0.325, ZB + 0.036), (0.135, ZB + 0.036), (0.146, ZB + 0.018), (0.146, ZB - 0.030), (0.100, ZB - 0.047), (-0.040, ZB - 0.050), (-0.100, ZB - 0.062), (-0.322, ZB - 0.068)]
    g.prism('polimero', body, -0.0245, 0.0245, r=0.0090, seg=4)
    g.prism('polimero', [(-0.210, ZB + 0.024), (-0.100, ZB + 0.024), (-0.100, ZB - 0.024), (-0.210, ZB - 0.024)], 0.0240, 0.0252, r=0.0030)   # pannello laterale
    g.box('polimero', (0.0245, -0.020, ZB + 0.0050), (0.0030, 0.060, 0.0180), cut=True)                                                   # espulsione destra
    tilted_grip(g, 'polimero', 10, 0.042, -0.056, 0.0185, 0.0235, rad=0.0090)
    trigger_guard(g, 'polimero', 0.058, -0.004, ZB - 0.048, ZB - 0.088, th=0.0050, w=0.0070)
    g.part('trigger', pivot=(0, 0.030, ZB - 0.050))
    g.prism('acciaio', [(0.026, ZB - 0.050), (0.034, ZB - 0.050), (0.042, ZB - 0.074), (0.034, ZB - 0.074)], -0.0035, 0.0035, r=0.0008)
    # impugnatura anteriore verticale
    g.prism('polimero', [(0.088, ZB - 0.040), (0.114, ZB - 0.040), (0.110, ZB - 0.122), (0.092, ZB - 0.122)], -0.0150, 0.0150, r=0.0070, seg=3)
    # guida superiore e ottica integrata
    g.part('corpo')
    picatinny(g, 'alluminio', -0.215, 0.135, ZB + 0.036)
    zo = ZB + 0.036 + 0.0068 + 0.0235 + 0.0215
    acog(g, -0.050, zo, length=0.155)
    # canna e spegnifiamma, pistone del gas
    g.cyl('acciaio', 0, ZB, 0.140, 0.400, 0.0105, ch=0.0006)
    g.cyl('acciaio', 0, ZB + 0.0200, 0.140, 0.300, 0.0070, ch=0.0004)
    flash_hider(g, 'acciaio', 0.395, ZB, rad=0.0150, length=0.060, prongs=4)
    # caricatore dietro l'impugnatura (parte mobile)
    g.part('caricatore', pivot=(0, -0.070, ZB - 0.060))
    g.box('polimero', (0, -0.068, ZB - 0.135), (0.0235, 0.0420, 0.140), rot=(-6, 0, 0), r=0.0030)
    for k in range(5):
        g.box('polimero', (0.0120, -0.068, ZB - 0.090 - k * 0.020), (0.0012, 0.032, 0.0040), rot=(-6, 0, 0))
    g.box('polimero', (0, -0.062, ZB - 0.2090), (0.0260, 0.0460, 0.0090), rot=(-6, 0, 0), r=0.0020)
    g.part('charge', pivot=(-0.026, 0.060, ZB + 0.020))
    g.rod('acciaio', (-0.0245, 0.060, ZB + 0.020), (-0.040, 0.060, ZB + 0.020), 0.0045)
    g.box('acciaio', (-0.045, 0.060, ZB + 0.020), (0.0100, 0.0300, 0.0120), r=0.0030)
    g.part('corpo')
    g.prism('gomma', [(-0.322, ZB + 0.038), (-0.340, ZB + 0.038), (-0.346, ZB - 0.070), (-0.326, ZB - 0.070)], -0.0250, 0.0250, r=0.0035, seg=3)
    g.empty('muzzle', (0, 0.456, ZB), (0, 1, 0))
    g.empty('ads', (0, -0.050 - 0.0775, zo), (0, 1, 0))
    g.empty('eject', (0.0250, -0.020, ZB + 0.005), (1, 0, 0))
    g.empty('grip_l', (0, 0.101, ZB - 0.070), (0, 1, 0))
    return g


@arma
def br_gloria():
    """GLORIA: fucile da battaglia a ricevitore stampato, astina di polimero a feritoie, caricatore da 20, ottica ACOG 4x sulla guida."""
    g = Gun('br_gloria', tex=2048)
    g.paint = {'polimero': (0.115, 0.088, 0.046), 'polimero_usura': (0.20, 0.16, 0.09)}
    ZB = 0.100
    g.part('corpo')
    g.box('acciaio', (0, 0.022, ZB - 0.003), (0.0370, 0.3060, 0.0740), r=0.0030, seg=3)
    for sx in (-1, 1):
        for k in range(2):
            g.box('acciaio', (sx * 0.0188, 0.020, ZB - 0.020 + k * 0.022), (0.0016, 0.220, 0.0030), r=0.0004)
    g.box('acciaio', (0.0188, -0.010, ZB + 0.0060), (0.0040, 0.0800, 0.0200), cut=True)
    picatinny(g, 'alluminio', -0.108, 0.150, ZB + 0.0340)
    g.loft('polimero', [(0.140, rrect(0.0275, 0.0235, 0.0100, 3, 0, ZB - 0.0120)), (0.340, rrect(0.0255, 0.0215, 0.0100, 3, 0, ZB - 0.0120))], r=0.0016)
    for k in range(6):
        for sx in (-1, 1):
            g.box('polimero', (sx * 0.0265, 0.180 + k * 0.024, ZB - 0.0100), (0.0034, 0.0140, 0.0200), r=0.0030, cut=True)
    g.cyl('acciaio', 0, ZB, 0.172, 0.545, 0.0125, ch=0.0008)
    g.cyl('acciaio', 0, ZB, 0.335, 0.365, 0.0170, ch=0.0008)
    fsb_tower(g, 0.490, ZB + 0.0125, ZB + 0.052)
    flash_hider(g, 'acciaio', 0.518, ZB, rad=0.0170, length=0.070, prongs=6)
    stock_side(g, 'polimero', [(-0.130, ZB + 0.030), (-0.250, ZB + 0.026), (-0.365, ZB + 0.022), (-0.373, ZB - 0.074), (-0.355, ZB - 0.080), (-0.250, ZB - 0.052), (-0.130, ZB - 0.036)], 0.0200, r=0.0070,
               pad=[(-0.365, ZB + 0.024), (-0.383, ZB + 0.024), (-0.389, ZB - 0.082), (-0.372, ZB - 0.082)])
    tilted_grip(g, 'polimero', 20, 0.046, -0.058, 0.0175, 0.0225, rad=0.0085)
    trigger_guard(g, 'polimero', 0.062, -0.006, ZB - 0.037, ZB - 0.082, th=0.0050, w=0.0080)
    g.part('trigger', pivot=(0, 0.030, ZB - 0.040))
    g.prism('acciaio', [(0.026, ZB - 0.040), (0.034, ZB - 0.040), (0.042, ZB - 0.068), (0.034, ZB - 0.068)], -0.0035, 0.0035, r=0.0008)
    g.part('caricatore', pivot=(0, 0.075, ZB - 0.040))
    path = curved_mag(g, 'acciaio', (0.075, ZB - 0.040), (0.092, ZB - 0.190), 0.010, 0.0145, 0.0275, 0.0145, 0.0285, ribs=0)
    for k in range(6):
        yy, zz = path[2 + 2 * k]
        g.box('acciaio', (0.0150, yy, zz), (0.0014, 0.040, 0.0045), r=0.0004)
    g.part('charge', pivot=(-0.0300, 0.150, ZB - 0.004))
    g.rod('acciaio', (-0.0260, 0.150, ZB - 0.004), (-0.0440, 0.150, ZB - 0.004), 0.0042)
    g.box('acciaio', (-0.0470, 0.150, ZB - 0.004), (0.0100, 0.0300, 0.0140), r=0.0030)
    g.part('corpo')
    zo = ZB + 0.0340 + 0.0068 + 0.0235 + 0.0215
    acog(g, 0.015, zo, length=0.150)
    g.empty('muzzle', (0, 0.590, ZB), (0, 1, 0))
    g.empty('ads', (0, 0.015 - 0.075 - 0.0005, zo), (0, 1, 0))
    g.empty('eject', (0.0190, -0.010, ZB + 0.006), (1, 0, 0))
    g.empty('grip_l', (0, 0.240, ZB - 0.012), (0, 1, 0))
    return g


@arma
def lv_predicatore():
    """PREDICATORE: carabina a leva calibro .30-30, canna ottagonale, serbatoio tubolare, mobili di noce, tacca a corna e lama."""
    g = Gun('lv_predicatore', tex=2048)
    ZB = 0.082
    g.part('corpo')
    # carcassa: acciaio brunito con sportello di caricamento a destra
    g.prism('acciaio', [(0.018, ZB - 0.036), (0.018, ZB + 0.0270), (0.158, ZB + 0.0270), (0.158, ZB - 0.036), (0.090, ZB - 0.040)], -0.0158, 0.0158, r=0.0035, seg=3)
    g.prism('acciaio', [(0.082, ZB - 0.030), (0.116, ZB - 0.030), (0.116, ZB - 0.008), (0.082, ZB - 0.008)], 0.0150, 0.0172, r=0.0010)   # sportello di caricamento
    g.box('acciaio', (0.0000, 0.060, ZB + 0.0275), (0.0130, 0.050, 0.0030), cut=True)                       # finestra di espulsione in alto
    wrist_stock(g, 'legno', ZB, -0.318, 0.040, y_front=0.030, hw=0.0172)
    # canna ottagonale, serbatoio, fascette, asta della leva
    g.lathe('acciaio', (0, 0, ZB), [(0.158, 0.0128), (0.578, 0.0118)], segs=8, rot=(0, 0, 22.5))
    g.cyl('acciaio', 0, ZB - 0.0225, 0.158, 0.520, 0.0086, ch=0.0006)
    g.loft('legno', [(0.158, rrect(0.0172, 0.0235, 0.0090, 3, 0, ZB - 0.0140)), (0.345, rrect(0.0150, 0.0200, 0.0090, 3, 0, ZB - 0.0145))], r=0.0012)
    for y in (0.300, 0.470):
        g.lathe('acciaio', (0, 0, ZB - 0.010), [(y, 0.0230), (y + 0.012, 0.0230)], segs=22)
    g.cyl('acciaio', 0, ZB - 0.0225, 0.516, 0.524, 0.0100, ch=0.0006)
    # mire: tacca a corna sulla canna e lama con inserto in fondo
    nb = ZB + 0.0300
    notch_rear(g, 0.215, ZB + 0.0120, h=0.0230, w=0.0200, gap=0.0042, depth=0.0100, nb=nb)
    g.box('acciaio', (0, 0.573, ZB + 0.0118 + 0.0090), (0.0030, 0.0120, 0.0180), r=0.0006)
    g.box('emit', (0, 0.5792, ZB + 0.0262), (0.0020, 0.0003, 0.0050))
    # leva con anello (parte mobile), cane e grilletto
    g.part('lever', pivot=(0, 0.020, ZB - 0.038))
    path = [(0.020, ZB - 0.038), (0.030, ZB - 0.078), (0.075, ZB - 0.100), (0.120, ZB - 0.094), (0.126, ZB - 0.062), (0.110, ZB - 0.040)]
    g.sweep('acciaio', path, 0.0050, 0.0040, rad=0.0020, seg=2)
    g.part('bolt', pivot=(0, 0.040, ZB))
    g.box('acciaio', (0, 0.070, ZB + 0.0080), (0.0100, 0.0500, 0.0120), r=0.0020)
    g.part('hammer', pivot=(0, 0.020, ZB + 0.010))
    g.prism('acciaio', [(0.024, ZB + 0.008), (0.006, ZB + 0.014), (-0.004, ZB + 0.030), (0.006, ZB + 0.034), (0.016, ZB + 0.022), (0.026, ZB + 0.016)], -0.0050, 0.0050, r=0.0010)
    g.part('trigger', pivot=(0, 0.060, ZB - 0.036))
    g.prism('acciaio', [(0.058, ZB - 0.034), (0.066, ZB - 0.034), (0.074, ZB - 0.060), (0.066, ZB - 0.060)], -0.0035, 0.0035, r=0.0008)
    g.part('corpo')
    g.empty('muzzle', (0, 0.580, ZB), (0, 1, 0))
    g.empty('ads', (0, 0.215, nb), (0, 1, 0))
    g.empty('eject', (0.0, 0.080, ZB + 0.027), (0, 0, 1))
    g.empty('grip_l', (0, 0.235, ZB - 0.014), (0, 1, 0))
    return g


@arma
def bolt_rosario():
    """ROSARIO: fucile a otturatore con cassa di legno intera, maniglia piegata, caricatore a scatola, mira a diaframma sul ricevitore e mirino incappucciato."""
    g = Gun('bolt_rosario', tex=2048)
    g.paint = {'legno_scuro': (0.080, 0.040, 0.016), 'legno_chiaro': (0.270, 0.150, 0.065)}
    ZB = 0.080
    g.part('corpo')
    # ricevitore cilindrico con ponte, canna rastremata, cassa di legno
    g.lathe('acciaio', (0, 0, ZB), [(0.020, 0.0160), (0.150, 0.0160)], segs=26)
    g.box('acciaio', (0, 0.020, ZB + 0.0040), (0.0300, 0.050, 0.0240), r=0.0030)
    g.box('acciaio', (0, 0.140, ZB + 0.0040), (0.0300, 0.030, 0.0240), r=0.0030)
    g.box('acciaio', (0.0145, 0.075, ZB + 0.0060), (0.0030, 0.080, 0.0150), cut=True)
    g.lathe('acciaio', (0, 0, ZB), [(0.150, 0.0152), (0.300, 0.0132), (0.640, 0.0107)], segs=26)
    wrist_stock(g, 'legno', ZB, -0.320, 0.036, y_front=0.030, hw=0.0172)
    g.loft('legno', [(0.030, rrect(0.0185, 0.0310, 0.0090, 3, 0, ZB - 0.0150)), (0.150, rrect(0.0182, 0.0250, 0.0090, 3, 0, ZB - 0.0120)),
                     (0.470, rrect(0.0160, 0.0170, 0.0090, 3, 0, ZB - 0.0060))], r=0.0014)
    for y in (0.330, 0.455):
        g.lathe('acciaio', (0, 0, ZB - 0.004), [(y, 0.0190), (y + 0.010, 0.0190)], segs=22)
    # mire: diaframma sul ricevitore, lama incappucciata in bocca
    z_line = ZB + 0.0435
    aperture_rear(g, 0.012, ZB + 0.0160, h=0.0295, ring_out=0.0100, ring_in=0.0044)
    post_front(g, 0.624, ZB + 0.0105, h=0.0330, w=0.0026, hood=True)
    g.tube('acciaio', 0, ZB + 0.0150, 0.612, 0.636, 0.0118, 0.0085, segs=18)
    # otturatore con maniglia piegata (la maniglia e' figlia dell'otturatore)
    g.part('bolt', pivot=(0, 0.020, ZB))
    g.lathe('acciaio', (0, 0, ZB), [(-0.030, 0.0122), (0.060, 0.0122)], segs=22)
    g.lathe('acciaio', (0, 0, ZB), [(-0.034, 0.0150), (-0.028, 0.0150), (-0.028, 0.0110)], segs=18)
    g.part('bolt_handle', pivot=(0, 0.000, ZB), parent='bolt')
    g.rod('acciaio', (0.0100, 0.000, ZB), (0.0620, -0.012, ZB - 0.020), 0.0046)
    g.sphere('acciaio', (0.0690, -0.014, ZB - 0.0225), 0.0120, segs=14)
    g.part('caricatore', pivot=(0, 0.080, ZB - 0.030))
    g.box('acciaio', (0, 0.082, ZB - 0.062), (0.0280, 0.0840, 0.0480), r=0.0030)
    g.part('trigger', pivot=(0, 0.062, ZB - 0.040))
    g.prism('acciaio', [(0.058, ZB - 0.040), (0.066, ZB - 0.040), (0.074, ZB - 0.064), (0.066, ZB - 0.064)], -0.0035, 0.0035, r=0.0008)
    g.part('corpo')
    trigger_guard(g, 'acciaio', 0.120, 0.030, ZB - 0.052, ZB - 0.082, th=0.0050, w=0.0070)
    g.empty('muzzle', (0, 0.641, ZB), (0, 1, 0))
    g.empty('ads', (0, 0.012, z_line), (0, 1, 0))
    g.empty('eject', (0.0150, 0.075, ZB + 0.008), (1, 0, 0))
    g.empty('grip_l', (0, 0.260, ZB - 0.012), (0, 1, 0))
    return g
