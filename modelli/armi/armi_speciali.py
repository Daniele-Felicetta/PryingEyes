"""Armi speciali e aliene del culto dell'Occhio: osso, carne, oro e iridi d'ambra. Origine: centro dell'impugnatura; y avanti; z su."""
from math import sin, cos, radians, pi
from piezas import *
from armi_mitra import tilted_grip
from armi_pistole import trigger_guard


def vertebrae(y0, y1, r0, amp, n):
    """Profilo di una canna a vertebre: n rigonfiamenti uno dopo l'altro."""
    prof = []
    for k in range(n):
        ya = y0 + (y1 - y0) * k / n
        yb = y0 + (y1 - y0) * (k + 1) / n
        for t in ((0.0, 0.25, 0.5, 0.75, 1.0) if k == 0 else (0.25, 0.5, 0.75, 1.0)):
            prof.append((ya + (yb - ya) * t, r0 + amp * sin(pi * t) ** 0.7))
    return prof


def thorn(g, role, x, y, z, h=0.020, r=0.0050, axis='z'):
    g.lathe(role, (x, y, z), [(0.0, r), (h, 0.0)], segs=8, axis=axis)


def eye_side(g, x_sign, y, z, r=0.0175):
    """Occhio sul fianco: sclera d'osso rigonfia, iride d'ambra con pupilla a fessura."""
    g.sphere('osso', (x_sign * 0.0200, y, z), r, segs=16, squash=(0.62, 1.0, 1.0))
    g.lathe('emit_ambra', (x_sign * 0.0292, y, z), [(-0.0005, 0.0100), (0.0005, 0.0100)], segs=18, axis='x')
    g.box('gomma', (x_sign * 0.0298, y, z), (0.0006, 0.0042, 0.0140))


@arma
def tu_turibolo():
    """TURIBOLO: lanciafiamme sacro d'ottone, serbatoio a incensiere forato, ugello a lanterna con la fiamma pilota, impugnatura a croce."""
    g = Gun('tu_turibolo', tex=2048)
    ZB = 0.098
    g.part('corpo')
    # corpo d'ottone con tre fasce, canna con anelli, ugello a lanterna
    g.box('ottone', (0, 0.050, ZB - 0.004), (0.0420, 0.1700, 0.0580), r=0.0075, seg=3)
    for y in (-0.025, 0.050, 0.125):
        g.box('ottone', (0, y, ZB - 0.004), (0.0470, 0.0120, 0.0640), r=0.0030)
    g.lathe('ottone', (0, 0, ZB), [(0.130, 0.0165), (0.482, 0.0150)], segs=24)
    for k in range(7):
        y = 0.170 + k * 0.045
        g.lathe('ottone', (0, 0, ZB), [(y, 0.0205), (y + 0.010, 0.0205)], segs=24)
    g.lathe('ottone', (0, 0, ZB), [(0.478, 0.0240), (0.494, 0.0240)], segs=24)
    for k in range(6):
        a = radians(60 * k + 30)
        g.rod('ottone', (0.0240 * cos(a), 0.494, ZB + 0.0240 * sin(a)), (0.0345 * cos(a), 0.590, ZB + 0.0345 * sin(a)), 0.0030)
    g.tube('ottone', 0, ZB, 0.584, 0.598, 0.0375, 0.0310, segs=24)
    g.sphere('emit_ambra', (0, 0.556, ZB), 0.0070, segs=12)                               # fiamma pilota
    # impugnatura, ponticello, grilletto, secondo impugno a pala e calcio a croce
    tilted_grip(g, 'legno', 12, 0.050, -0.056, 0.0170, 0.0220, rad=0.0080)
    trigger_guard(g, 'ottone', 0.056, -0.004, ZB - 0.033, ZB - 0.076, th=0.0050, w=0.0060)
    g.prism('legno', [(0.228, ZB - 0.020), (0.264, ZB - 0.020), (0.260, ZB - 0.108), (0.232, ZB - 0.108)], -0.0160, 0.0160, r=0.0075, seg=3)
    g.box('legno', (0, -0.080, ZB - 0.012), (0.0200, 0.1100, 0.0260), r=0.0060)
    g.box('legno', (0, -0.145, ZB - 0.0475), (0.0260, 0.0200, 0.1550), r=0.0060)         # asta verticale della croce (sotto la linea di mira)
    g.box('legno', (0, -0.145, ZB + 0.0160), (0.1100, 0.0200, 0.0280), r=0.0060)         # traversa
    g.prism('gomma', [(-0.155, ZB - 0.126), (-0.160, ZB - 0.126), (-0.160, ZB + 0.034), (-0.155, ZB + 0.034)], -0.0140, 0.0140, r=0.0010) if False else None
    # mire: tacca d'ottone dietro, lama davanti sulla canna
    nb = ZB + 0.0415
    notch_rear(g, 0.000, ZB + 0.0250, h=0.0170, w=0.0170, gap=0.0036, role='ottone', depth=0.0100, nb=nb)
    post_front(g, 0.452, ZB + 0.0150, h=0.0265, w=0.0026, hood=False, role='ottone')
    g.part('trigger', pivot=(0, 0.030, ZB - 0.034))
    g.prism('acciaio', [(0.026, ZB - 0.034), (0.034, ZB - 0.034), (0.042, ZB - 0.060), (0.034, ZB - 0.060)], -0.0035, 0.0035, r=0.0008)
    # serbatoio a incensiere (parte mobile)
    g.part('caricatore', pivot=(0, 0.140, ZB - 0.034))
    g.sphere('ottone', (0, 0.140, ZB - 0.098), 0.0600, segs=22, squash=(1.0, 1.0, 1.05))
    for dz in (-0.030, 0.0, 0.030):
        g.lathe('ottone', (0, 0.140, ZB - 0.098 + dz), [(-0.070, 0.0055), (0.070, 0.0055)], segs=10, axis='x', cut=True)
        g.lathe('ottone', (0.0, 0.140, ZB - 0.098 + dz), [(-0.070, 0.0055), (0.070, 0.0055)], segs=10, axis='y', cut=True)
    g.lathe('ottone', (0, 0.140, ZB - 0.0400), [(0.0, 0.0200), (0.008, 0.0200)], segs=18, axis='z')
    g.part('corpo')
    g.empty('muzzle', (0, 0.592, ZB), (0, 1, 0))
    g.empty('ads', (0, 0.000, nb), (0, 1, 0))
    g.empty('eject', (0.022, 0.0, ZB), (1, 0, 0))
    g.empty('grip_l', (0, 0.246, ZB - 0.062), (0, 1, 0))
    return g


@arma
def sp_spora():
    """SPORA: lanciaspine organico, canna a vertebre con spine sul dorso, sacco di carne al posto del caricatore, occhi sui fianchi, bocca di carne."""
    g = Gun('sp_spora', tex=2048)
    ZB = 0.092
    g.paint = {'osso': (0.40, 0.34, 0.22), 'carne': (0.34, 0.055, 0.045)}
    g.part('corpo')
    secs = [(-0.110, rrect(0.016, 0.022, 0.009, 3, 0, ZB)), (-0.040, rrect(0.021, 0.031, 0.012, 3, 0, ZB + 0.003)), (0.060, rrect(0.024, 0.034, 0.014, 3, 0, ZB + 0.002)),
            (0.170, rrect(0.020, 0.027, 0.012, 3, 0, ZB)), (0.215, rrect(0.016, 0.022, 0.010, 3, 0, ZB))]
    g.loft('osso', secs, r=0.0012)
    g.lathe('osso', (0, 0, ZB), vertebrae(0.200, 0.612, 0.0125, 0.0042, 11), segs=18)
    g.lathe('carne', (0, 0, ZB), [(0.612, 0.0100), (0.612, 0.0180), (0.640, 0.0220), (0.652, 0.0170), (0.652, 0.0100)], segs=20, loop=True)
    for k in range(12):
        y = -0.060 + k * 0.058
        thorn(g, 'osso', 0, y, ZB + (0.034 if y < 0.19 else 0.016), h=0.021 if y < 0.19 else 0.016, r=0.0052 if y < 0.19 else 0.0040)
    for sx in (-1, 1):
        eye_side(g, sx, 0.075, ZB + 0.012)
    # impugnatura a mano, guardamano d'osso per la sinistra, ponticello
    tilted_grip(g, 'osso', 16, 0.050, -0.058, 0.0185, 0.0235, rad=0.0090)
    trigger_guard(g, 'osso', 0.060, -0.004, ZB - 0.037, ZB - 0.076, th=0.0060, w=0.0070)
    g.loft('osso', [(0.200, rrect(0.0240, 0.0260, 0.0110, 3, 0, ZB - 0.002)), (0.345, rrect(0.0225, 0.0245, 0.0110, 3, 0, ZB - 0.002))], r=0.0012)
    stock_side(g, 'osso', [(-0.105, ZB + 0.020), (-0.200, ZB + 0.016), (-0.290, ZB + 0.000), (-0.340, ZB - 0.030), (-0.352, ZB - 0.075), (-0.330, ZB - 0.082),
                           (-0.285, ZB - 0.050), (-0.200, ZB - 0.034), (-0.105, ZB - 0.030)], 0.0170, r=0.0080,
               pad=[(-0.340, ZB - 0.034), (-0.358, ZB - 0.040), (-0.362, ZB - 0.080), (-0.342, ZB - 0.082)])
    # mire: tacca nell'osso dietro, spina con bocciolo d'ambra davanti
    nb = ZB + 0.0440
    notch_rear(g, 0.020, ZB + 0.0330, h=0.0140, w=0.0120, gap=0.0034, role='osso', depth=0.0100, nb=nb)
    g.lathe('osso', (0, 0.600, ZB + 0.0130), [(0.0, 0.0042), (0.031, 0.0014)], segs=8, axis='z')
    g.sphere('emit_ambra', (0, 0.600, ZB + 0.0455), 0.0030, segs=10)
    g.part('trigger', pivot=(0, 0.030, ZB - 0.038))
    g.prism('osso', [(0.026, ZB - 0.038), (0.034, ZB - 0.038), (0.042, ZB - 0.062), (0.034, ZB - 0.062)], -0.0035, 0.0035, r=0.0008)
    # sacco di carne (parte mobile)
    g.part('caricatore', pivot=(0, 0.085, ZB - 0.034))
    g.rod('osso', (0, 0.085, ZB - 0.034), (0, 0.085, ZB - 0.062), 0.0140)
    g.sphere('carne', (0, 0.085, ZB - 0.090), 0.0380, segs=20, squash=(0.78, 1.0, 1.25))
    g.part('corpo')
    g.empty('muzzle', (0, 0.653, ZB), (0, 1, 0))
    g.empty('ads', (0, 0.020, nb), (0, 1, 0))
    g.empty('eject', (0.022, 0.0, ZB + 0.010), (1, 0, 0))
    g.empty('grip_l', (0, 0.272, ZB - 0.002), (0, 1, 0))
    return g


@arma
def pu_pupilla():
    """PUPILLA: fucile-occhio d'osso e d'oro, la volata e' un bulbo con l'iride d'ambra e la pupilla a fessura, anello di mira sul dorso, cella d'energia."""
    g = Gun('pu_pupilla', tex=2048)
    ZB = 0.100
    g.paint = {'osso': (0.50, 0.46, 0.36), 'carne': (0.30, 0.05, 0.05)}
    g.part('corpo')
    secs = [(-0.130, rrect(0.015, 0.022, 0.009, 3, 0, ZB)), (-0.050, rrect(0.0215, 0.0300, 0.012, 3, 0, ZB + 0.002)), (0.080, rrect(0.0240, 0.0320, 0.013, 3, 0, ZB + 0.002)),
            (0.200, rrect(0.0200, 0.0260, 0.011, 3, 0, ZB))]
    g.loft('osso', secs, r=0.0012)
    for y in (-0.090, -0.020, 0.050, 0.120, 0.188):
        g.lathe('ottone', (0, 0, ZB), [(y, 0.0250), (y + 0.007, 0.0250)], segs=24, squash=(1.0, 1.28))
    g.lathe('ottone', (0, 0, ZB), cprof(0.195, 0.640, 0.0135), segs=24)
    for y in (0.290, 0.390, 0.490):
        g.lathe('ottone', (0, 0, ZB), [(y, 0.0185), (y + 0.012, 0.0185)], segs=24)
    # bulbo oculare alla bocca: sclera, anello d'oro, iride d'ambra e pupilla
    g.sphere('osso', (0, 0.665, ZB), 0.0330, segs=22)
    g.tube('ottone', 0, ZB, 0.655, 0.667, 0.0360, 0.0300, segs=24)
    g.lathe('emit_ambra', (0, 0.697, ZB), [(-0.0005, 0.0170), (0.0005, 0.0170)], segs=24)
    g.box('gomma', (0, 0.6978, ZB), (0.0042, 0.0006, 0.0250))
    # anello di mira sul dorso, su due piedistalli d'osso
    zs = ZB + 0.0680
    for yy in (-0.026, 0.034):
        g.box('osso', (0, yy, ZB + 0.0470), (0.0200, 0.0110, 0.0330), r=0.0030)
    g.part('lente_dietro', pivot=(0, -0.028, zs))
    g.lathe('vetro', (0, -0.028, zs), [(-0.0005, 0.0113), (0.0005, 0.0113)], segs=28)
    g.part('retino', pivot=(0, 0.004, zs))
    g.lathe('retino', (0, 0.004, zs), [(-0.0002, 0.0110), (0.0002, 0.0110)], segs=28)
    g.part('corpo')
    g.tube('ottone', 0, zs, -0.034, 0.040, 0.0170, 0.0115, segs=28)
    # guardamano d'osso, impugnatura, calcio organico con calciolo di carne, strisce d'energia
    g.loft('osso', [(0.190, rrect(0.0225, 0.0255, 0.0110, 3, 0, ZB - 0.002)), (0.345, rrect(0.0215, 0.0240, 0.0110, 3, 0, ZB - 0.002))], r=0.0012)
    tilted_grip(g, 'osso', 14, 0.050, -0.058, 0.0180, 0.0230, rad=0.0085)
    trigger_guard(g, 'ottone', 0.060, -0.004, ZB - 0.037, ZB - 0.076, th=0.0050, w=0.0060)
    stock_side(g, 'osso', [(-0.125, ZB + 0.020), (-0.220, ZB + 0.014), (-0.300, ZB - 0.004), (-0.346, ZB - 0.034), (-0.358, ZB - 0.078), (-0.334, ZB - 0.084),
                           (-0.290, ZB - 0.052), (-0.205, ZB - 0.036), (-0.125, ZB - 0.030)], 0.0165, r=0.0080,
               pad=[(-0.346, ZB - 0.038), (-0.364, ZB - 0.044), (-0.368, ZB - 0.082), (-0.348, ZB - 0.084)])
    for sx in (-1, 1):
        g.box('emit_ambra', (sx * 0.0168, -0.200, ZB - 0.010), (0.0010, 0.1300, 0.0050))
        g.box('emit_ambra', (sx * 0.0246, 0.100, ZB + 0.002), (0.0010, 0.1000, 0.0040))
    g.part('trigger', pivot=(0, 0.030, ZB - 0.038))
    g.prism('ottone', [(0.026, ZB - 0.038), (0.034, ZB - 0.038), (0.042, ZB - 0.062), (0.034, ZB - 0.062)], -0.0035, 0.0035, r=0.0008)
    # cella d'energia (parte mobile): custodia d'osso con anima luminosa
    g.part('caricatore', pivot=(0, 0.070, ZB - 0.034))
    g.box('osso', (0, 0.070, ZB - 0.082), (0.0270, 0.0500, 0.0900), r=0.0050)
    for sx in (-1, 1):
        g.box('emit_ambra', (sx * 0.0138, 0.070, ZB - 0.080), (0.0010, 0.0360, 0.0700))
    g.part('charge', pivot=(-0.025, -0.030, ZB + 0.020))
    g.rod('osso', (-0.020, -0.030, ZB + 0.020), (-0.046, -0.030, ZB + 0.020), 0.0040)
    g.sphere('osso', (-0.050, -0.030, ZB + 0.020), 0.0080, segs=10)
    g.part('corpo')
    g.empty('muzzle', (0, 0.699, ZB), (0, 1, 0))
    g.empty('ads', (0, -0.028, zs), (0, 1, 0))
    g.empty('eject', (0.022, 0.0, ZB + 0.010), (1, 0, 0))
    g.empty('grip_l', (0, 0.262, ZB - 0.002), (0, 1, 0))
    return g
