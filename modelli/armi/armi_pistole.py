"""Pistole e revolver. Origine: centro dell'impugnatura; y avanti; z su; l'asse dell'impugnatura e' inclinato (la cima va avanti)."""
from math import sin, cos, radians
from piezas import *

GT = 14.0


def gpt(h, y=0.0):
    """Punto sull'asse dell'impugnatura a quota h (lungo l'asse), spostato di y in avanti."""
    return (y + h * sin(radians(GT)), h * cos(radians(GT)))


def grip_sweep(g, role, top, bottom, hw=0.0145, dp=0.0195, rad=0.006, bulge=0.003):
    path = arc((gpt(top)[0], gpt(top)[1]), (gpt(bottom)[0], gpt(bottom)[1]), bulge, 9)
    hws = [hw + 0.0012 * sin(pi * i / 9) for i in range(10)]
    dps = [dp + 0.0020 * sin(pi * i / 9) for i in range(10)]
    g.sweep(role, path, hws, dps, rad=rad, seg=3)


def trigger_guard(g, role, y0, y1, ztop, zbot, th=0.0040, w=0.0042):
    """Ponticello: anello visto di lato (davanti inclinato, fondo piatto, dietro alla radice dell'impugnatura)."""
    path = [(y0 - 0.002, ztop), (y0 + 0.003, zbot + 0.012), (y0 - 0.002, zbot + 0.002), (y0 - 0.014, zbot), (y1 + 0.014, zbot), (y1 + 0.002, zbot + 0.010),
            (y1, ztop - 0.002)]
    g.sweep(role, path, w / 2, th / 2, rad=th * 0.45, seg=2)


def mag_cut_and_mag(g, ZB, hw=0.0115, dp=0.0165, bottom=-0.056, top_in=0.028, ribs=False, plate_role='polimero', body_role='acciaio'):
    """Pozzo nell'impugnatura (taglio) e caricatore mobile che ci sta dentro."""
    # taglio lungo l'asse dell'impugnatura, dal basso fino a top_in
    hmid = (bottom + top_in) / 2
    g.box('polimero', (0, gpt(hmid)[0], gpt(hmid)[1]), (hw * 2 + 0.0004, dp * 2, top_in - bottom + 0.004), rot=(-GT, 0, 0), cut=True)
    g.part('caricatore', pivot=(0, gpt(top_in)[0], gpt(top_in)[1]))
    g.box(body_role, (0, gpt(hmid - 0.002)[0], gpt(hmid - 0.002)[1]), (hw * 2, dp * 2 - 0.0008, top_in - bottom - 0.003), rot=(-GT, 0, 0), r=0.0012)
    if ribs:
        for k in range(6):
            h = bottom + 0.016 + k * 0.012
            g.box(body_role, (hw + 0.0005, gpt(h)[0] + 0.0, gpt(h)[1]), (0.0014, dp * 2 - 0.004, 0.0035), rot=(-GT, 0, 0))
    hb = bottom - 0.002
    g.box(plate_role, (0, gpt(hb)[0] - 0.001, gpt(hb)[1]), (hw * 2 + 0.003, dp * 2 + 0.008, 0.0105), rot=(-GT, 0, 0), r=0.0022)
    g.part('corpo')


def pistol_slide(g, ZB, y0, y1, sh=0.0305, sw=0.0255, serr=6, serr_front=0, ports=True, sights='dots', rear_y=None, front_y=None,
                 role='acciaio', slope=True, rear_h=0.0062, front_h=0.0058, optic_cut=False):
    """Carrello (parte mobile 'slide') con feritoia di espulsione, rigature, mire. Ritorna la quota della sommita'."""
    g.part('slide', pivot=(0, 0, ZB))
    zc = ZB + 0.0035
    L = y1 - y0
    g.box(role, (0, (y0 + y1) / 2, zc), (sw, L, sh), r=0.0020, seg=3)
    top = zc + sh / 2
    if slope:
        # muso rastremato: il davanti e' leggermente piu' basso e stretto
        g.box(role, (0, y1 - 0.006, zc - 0.002), (sw - 0.0016, 0.012, sh - 0.002), r=0.0014)
    if ports:
        g.box(role, (sw / 2 - 0.002, y0 + L * 0.40, ZB + 0.0095), (0.0090, L * 0.27, 0.0150), cut=True)
        g.box('cromo', (sw / 2 - 0.0098, y0 + L * 0.40, ZB + 0.0085), (0.0016, L * 0.18, 0.0090), r=0.0004)     # estrattore
    for i in range(serr):
        y = y0 + 0.007 + i * 0.0050
        for sx in (-1, 1):
            g.box(role, (sx * (sw / 2 - 0.0004), y, zc - 0.0010), (0.0022, 0.0016, sh * 0.78), cut=True)
    for i in range(serr_front):
        y = y1 - 0.034 + i * 0.0050
        for sx in (-1, 1):
            g.box(role, (sx * (sw / 2 - 0.0004), y, zc - 0.0010), (0.0022, 0.0016, sh * 0.78), cut=True)
    # bocca della canna sporgente
    g.tube('acciaio', 0, ZB, y1 - 0.004, y1 + 0.004, 0.0098, 0.0042, segs=22, ch=0.0006)
    if optic_cut:
        g.box(role, (0, y0 + L * 0.45, top - 0.002), (0.0230, 0.0300, 0.0040), cut=True)
    return top


def pistol_sights(g, top, y_rear, y_front, dots=True, kind='dots'):
    """Mire a tre punti: tacca posteriore con due punti, lama anteriore con un punto."""
    if kind == 'dots':
        notch_rear(g, y_rear, top - 0.0005, h=0.0066, w=0.0165, gap=0.0036, depth=0.0085)
        post_front(g, y_front, top - 0.0005, h=0.0058, w=0.0030, hood=False)
        if dots:
            for sx in (-1, 1):
                dot(g, (sx * 0.0058, y_rear - 0.0043, top + 0.0030), 0.0013, 'y')
            dot(g, (0, y_front - 0.0021, top + 0.0044), 0.0014, 'y')
    elif kind == 'fixed':
        notch_rear(g, y_rear, top - 0.0005, h=0.0062, w=0.0150, gap=0.0040, depth=0.0070)
        g.box('acciaio', (0, y_front, top + 0.0025), (0.0030, 0.0090, 0.0060), r=0.0006)
        g.box('emit', (0, y_front - 0.0046, top + 0.0030), (0.0016, 0.0003, 0.0030))
        for sx in (-1, 1):
            g.box('emit', (sx * 0.0058, y_rear - 0.0036, top + 0.0030), (0.0016, 0.0003, 0.0024))


def make_pistol(g, ZB=0.080, y0=-0.070, y1=0.118, frame='polimero', grip='polimero', optic=None, ribs=False, comp=False,
                serr=6, serr_front=0, sights='dots', big=False):
    g.part('corpo')
    # telaio con guida
    zf0, zf1 = ZB - 0.040, ZB - 0.0115
    g.box(frame, (0, (y0 + 0.020 + y1 - 0.012) / 2 - 0.0, (zf0 + zf1) / 2), (0.0238, (y1 - 0.012) - (y0 + 0.020), zf1 - zf0), r=0.0022, seg=3)
    g.box(frame, (0, y0 + 0.0205, ZB - 0.0235), (0.0256, 0.036, 0.0225), r=0.0022)                       # coda del telaio
    for i in range(4):
        g.box(frame, (0, 0.088 + i * 0.0, ZB - 0.0402), (0.030, 0.0030, 0.0030), cut=True) if False else None
    g.box(frame, (0, 0.085, zf0 + 0.0045), (0.0060, 0.0220, 0.0030), cut=True)       # scasso della guida
    g.box(frame, (0, 0.098, zf0 + 0.0045), (0.0060, 0.0050, 0.0030), cut=True)
    trigger_guard(g, frame, 0.040, 0.0, zf0 + 0.001, zf0 - 0.0165)
    # impugnatura
    grip_sweep(g, grip, 0.052, -0.055, hw=0.0150, dp=0.0205, rad=0.0075)
    g.box(grip, (0, gpt(-0.008)[0] - 0.0195, gpt(-0.008)[1]), (0.0270, 0.0100, 0.082), rot=(-GT, 0, 0), r=0.0045)       # dorso
    mag_cut_and_mag(g, ZB, ribs=ribs)
    # leva di fermo del carrello (sinistra), rilascio del caricatore
    g.prism('acciaio', [(0.012, ZB - 0.0155), (0.050, ZB - 0.0145), (0.052, ZB - 0.0120), (0.012, ZB - 0.0125)], -0.0152, -0.0122, r=0.0006)
    g.lathe('acciaio', (0.0128, gpt(0.0)[0] + 0.026, ZB - 0.030), [(0.0, 0.0050), (0.0030, 0.0050)], segs=14, axis='x')
    # grilletto
    g.part('trigger', pivot=(0, 0.0165, ZB - 0.040))
    g.prism('acciaio', [(0.0175, ZB - 0.0345), (0.0235, ZB - 0.0345), (0.0285, ZB - 0.0510), (0.0215, ZB - 0.0510)], -0.0035, 0.0035, r=0.0008)
    g.part('corpo')
    top = pistol_slide(g, ZB, y0, y1, serr=serr, serr_front=serr_front, sights=sights)
    return top
    # (le mire vanno sul carrello: le disegna il chiamante dopo pistol_slide)


def finish_pistol(g, name, ZB, y0, y1, top, sights='dots', optic=None, comp=False, rear_off=0.012, front_off=0.011):
    g.part('slide', pivot=(0, 0, ZB))
    if optic is None:
        pistol_sights(g, top, y0 + rear_off, y1 - front_off, kind=sights)
        ads_z = top + 0.0053
        ads_y = y0 + rear_off
    else:
        ads_z = optic['z']
        ads_y = optic['y']
    g.part('corpo')
    g.empty('muzzle', (0, y1 + (0.030 if comp else 0.004), ZB), (0, 1, 0))
    g.empty('ads', (0, ads_y, ads_z), (0, 1, 0))
    g.empty('eject', (0.0130, y0 + 0.074, ZB + 0.010), (1, 0, 0))
    g.empty('grip_l', (0, 0.020, ZB - 0.045), (0, 1, 0))


@arma
def p9_pieta():
    """PIETÀ P-9: pistola 9 mm a percussione lanciata, telaio in polimero, mire a tre punti notturne."""
    g = Gun('p9_pieta', tex=1024)
    g.paint = {'polimero': (0.020, 0.021, 0.024)}
    ZB = 0.080
    top = make_pistol(g, ZB, serr=6, serr_front=3)
    finish_pistol(g, 'p9_pieta', ZB, -0.070, 0.118, top)
    return g


@arma
def m45_vigilia():
    """VIGILIA .45: pistola 1911 tutta d'acciaio, guancette di noce, martello esterno, mire fisse a punti."""
    g = Gun('m45_vigilia', tex=1024)
    ZB = 0.082
    y0, y1 = -0.072, 0.128
    g.part('corpo')
    # telaio d'acciaio
    zf0, zf1 = ZB - 0.0395, ZB - 0.0105
    g.box('acciaio', (0, (y0 + 0.014 + y1 - 0.018) / 2, (zf0 + zf1) / 2), (0.0222, (y1 - 0.018) - (y0 + 0.014), zf1 - zf0), r=0.0016, seg=3)
    g.box('acciaio', (0, 0.092, zf0 + 0.0040), (0.0090, 0.0200, 0.0030), cut=True)
    # ponticello e dorso con sicura a pistone
    trigger_guard(g, 'acciaio', 0.045, 0.002, zf0 + 0.001, zf0 - 0.0160, th=0.0040)
    grip_sweep(g, 'legno', 0.052, -0.054, hw=0.0140, dp=0.0190, rad=0.0045)
    g.box('acciaio', (0, gpt(-0.008)[0] - 0.0195, gpt(-0.008)[1]), (0.0250, 0.0086, 0.080), rot=(-GT, 0, 0), r=0.0035)   # dorso
    g.prism('acciaio', [(gpt(0.050)[0] - 0.018, ZB - 0.012), (gpt(0.050)[0] - 0.036, ZB + 0.003), (gpt(0.050)[0] - 0.050, ZB + 0.003), (gpt(0.050)[0] - 0.040, ZB - 0.020)], -0.0112, 0.0112, r=0.0014)  # sicura a pistone
    # guancette quadrettate (due pannelli di noce) con vite
    for sx in (-1, 1):
        g.box('legno', (sx * 0.0166, gpt(-0.002)[0] + 0.001, gpt(-0.002)[1]), (0.0036, 0.0310, 0.090), rot=(-GT, 0, 0), r=0.0012)
        g.lathe('ottone', (sx * 0.0190, gpt(0.026)[0] + 0.001, gpt(0.026)[1]), [(0.0, 0.0042), (0.0010, 0.0042)], segs=14, axis='x')
    mag_cut_and_mag(g, ZB, hw=0.0105, dp=0.0160, ribs=False, plate_role='acciaio')
    g.lathe('acciaio', (0.0120, gpt(0.0)[0] + 0.027, ZB - 0.030), [(0.0, 0.0048), (0.0028, 0.0048)], segs=14, axis='x')
    g.prism('acciaio', [(0.010, ZB - 0.0150), (0.058, ZB - 0.0140), (0.060, ZB - 0.0115), (0.010, ZB - 0.0120)], -0.0140, -0.0112, r=0.0006)     # fermo carrello
    # martello esterno (parte mobile)
    g.part('hammer', pivot=(0, y0 + 0.004, ZB + 0.002))
    g.prism('acciaio', [(y0 + 0.008, ZB + 0.002), (y0 - 0.012, ZB + 0.010), (y0 - 0.0155, ZB + 0.0155), (y0 - 0.006, ZB + 0.0155), (y0 + 0.010, ZB + 0.011)], -0.0050, 0.0050, r=0.0008)
    g.part('trigger', pivot=(0, 0.020, ZB - 0.040))
    g.prism('acciaio', [(0.0235, ZB - 0.0340), (0.0305, ZB - 0.0340), (0.0345, ZB - 0.0530), (0.0285, ZB - 0.0530)], -0.0032, 0.0032, r=0.0008)
    g.part('corpo')
    top = pistol_slide(g, ZB, y0, y1, sh=0.0290, sw=0.0225, serr=7, serr_front=0, role='acciaio', slope=False)
    # estremita': boccola di canna e cannocchiale della molla
    g.tube('acciaio', 0, ZB - 0.0200, y1 - 0.030, y1 + 0.0, 0.0072, 0.0035, segs=18)
    finish_pistol(g, 'm45_vigilia', ZB, y0, y1, top, sights='fixed', rear_off=0.010, front_off=0.012)
    return g


@arma
def rv_cardinale():
    """CARDINALE .357: revolver a sei colpi, canna da 4", tamburo che si apre a sinistra, guancette di noce, mire regolabili."""
    g = Gun('rv_cardinale', tex=1024)
    ZB = 0.083
    g.part('corpo')
    # telaio (acciaio inox satinato, ruolo inox) + canna con nervatura e leva dell'estrattore
    g.prism('acciaio', [(-0.060, ZB - 0.034), (-0.060, ZB + 0.030), (0.020, ZB + 0.030), (0.020, ZB - 0.034), (0.002, ZB - 0.040), (-0.030, ZB - 0.040)], -0.0112, 0.0112, r=0.0020, seg=3)
    g.prism('acciaio', [(0.020, ZB - 0.016), (0.020, ZB + 0.0225), (0.060, ZB + 0.0225), (0.060, ZB - 0.016)], -0.0112, 0.0112, r=0.0014)  # collo
    g.lathe('acciaio', (0, 0, ZB), cprof(0.056, 0.176, 0.0138, 0.0008), segs=28)                  # canna
    g.box('acciaio', (0, 0.118, ZB + 0.0138), (0.0072, 0.118, 0.0060), r=0.0010)                     # nervatura
    g.lathe('acciaio', (0, 0, ZB - 0.0122), cprof(0.040, 0.128, 0.0104, 0.0006), segs=20)           # custodia dell'estrattore
    g.lathe('acciaio', (0, 0, ZB), [(0.173, 0.0128), (0.176, 0.0128), (0.176, 0.0060), (0.1735, 0.0060)], segs=24, loop=True)  # bocca
    # mire: tacca regolabile dietro, rampa con inserto davanti
    g.box('acciaio', (0, -0.040, ZB + 0.0345), (0.0170, 0.0120, 0.0090), r=0.0010)
    g.box('acciaio', (0, -0.040, ZB + 0.0365), (0.0036, 0.0130, 0.0100), cut=True)
    g.prism('acciaio', [(0.160, ZB + 0.0138), (0.174, ZB + 0.0138), (0.174, ZB + 0.0290), (0.168, ZB + 0.0230)], -0.0017, 0.0017, r=0.0006)
    g.box('emit', (0, 0.1742, ZB + 0.0250), (0.0020, 0.0003, 0.0070))
    # impugnatura di noce
    grip_sweep(g, 'legno', 0.050, -0.056, hw=0.0140, dp=0.0215, rad=0.0070)
    g.prism('acciaio', [(gpt(0.050)[0] - 0.026, ZB - 0.040), (gpt(0.050)[0] + 0.020, ZB - 0.040), (gpt(0.050)[0] + 0.020, ZB - 0.052), (gpt(0.050)[0] - 0.024, ZB - 0.052)], -0.0100, 0.0100, r=0.0020)
    # ponticello e grilletto
    trigger_guard(g, 'acciaio', 0.052, -0.002, ZB - 0.040, ZB - 0.0620, th=0.0040)
    g.part('trigger', pivot=(0, 0.014, ZB - 0.032))
    g.prism('acciaio', [(0.012, ZB - 0.0300), (0.020, ZB - 0.0300), (0.028, ZB - 0.0540), (0.020, ZB - 0.0540)], -0.0035, 0.0035, r=0.0008)
    g.part('hammer', pivot=(0, -0.054, ZB + 0.006))
    g.prism('acciaio', [(-0.050, ZB + 0.004), (-0.066, ZB + 0.010), (-0.072, ZB + 0.020), (-0.064, ZB + 0.024), (-0.054, ZB + 0.014), (-0.046, ZB + 0.008)], -0.0042, 0.0042, r=0.0008)
    # tamburo con sei camere e scanalature (parte mobile: perno a sinistra, sotto la canna)
    g.part('cyl', pivot=(-0.0210, 0.0, ZB - 0.0130))
    yc0, yc1 = -0.020, 0.040
    g.lathe('acciaio', (0, 0, ZB), [(yc0, 0.0214), (yc0 + 0.001, 0.0222), (yc1 - 0.001, 0.0222), (yc1, 0.0214)], segs=32)
    for k in range(6):
        a = radians(60 * k + 30)
        cx, cz = 0.0136 * cos(a), ZB + 0.0136 * sin(a)
        g.lathe('acciaio', (cx, 0, cz), [(yc0 - 0.002, 0.0046), (yc1 + 0.002, 0.0046)], segs=14, cut=True)
        g.lathe('acciaio', (0.0222 * cos(a + radians(30)) , 0, ZB + 0.0222 * sin(a + radians(30))), [(yc0 + 0.014, 0.0060), (yc1 - 0.008, 0.0060)], segs=3, cut=True) if False else None
    g.lathe('acciaio', (0, 0, ZB), [(yc0 - 0.003, 0.0058), (yc0, 0.0058)], segs=14)
    g.part('corpo')
    g.empty('muzzle', (0, 0.178, ZB), (0, 1, 0))
    g.empty('ads', (0, -0.040, ZB + 0.0390), (0, 1, 0))
    g.empty('eject', (0.012, 0.0, ZB), (1, 0, 0))
    g.empty('grip_l', (0, 0.020, ZB - 0.045), (0, 1, 0))
    return g


@arma
def mp_serafino():
    """SERAFINO-18: pistola mitragliatrice con compensatore sul carrello, caricatore lungo da 33 colpi e punto rosso aperto."""
    g = Gun('mp_serafino', tex=1024)
    ZB = 0.080
    y0, y1 = -0.070, 0.118
    top = make_pistol(g, ZB, y0, y1, ribs=True, serr=5, serr_front=0, sights='dots')
    # caricatore lungo (parte mobile): estensione sotto l'impugnatura
    g.part('caricatore', pivot=(0, gpt(0.028)[0], gpt(0.028)[1]))
    g.box('acciaio', (0, gpt(-0.092)[0] - 0.001, gpt(-0.092)[1]), (0.0230, 0.0310, 0.074), rot=(-GT, 0, 0), r=0.0012)
    g.box('polimero', (0, gpt(-0.132)[0] - 0.002, gpt(-0.132)[1]), (0.0270, 0.0400, 0.0100), rot=(-GT, 0, 0), r=0.0022)
    for k in range(5):
        h = -0.075 - k * 0.012
        g.box('acciaio', (0.0120, gpt(h)[0] - 0.001, gpt(h)[1]), (0.0014, 0.027, 0.0035), rot=(-GT, 0, 0))
    g.part('slide', pivot=(0, 0, ZB))
    # compensatore: tagli sul davanti del carrello
    for i in range(2):
        g.box('acciaio', (0, y1 - 0.016 + i * 0.011, ZB + 0.0185), (0.0090, 0.0050, 0.0200), cut=True)
    g.box('acciaio', (0, y1 + 0.011, ZB), (0.0240, 0.020, 0.0290), r=0.0018)
    g.box('acciaio', (0, y1 + 0.011, ZB + 0.0080), (0.0090, 0.0050, 0.0200), cut=True)
    g.tube('acciaio', 0, ZB, y1 + 0.002, y1 + 0.0215, 0.0098, 0.0044, segs=22)
    # punto rosso aperto montato sul carrello
    reflex_open(g, y0 + 0.032, top + 0.0150, w=0.0260, h=0.0200, length=0.034, base_h=0.0070)
    g.part('corpo')
    g.empty('muzzle', (0, y1 + 0.022, ZB), (0, 1, 0))
    g.empty('ads', (0, y0 + 0.038, top + 0.0150), (0, 1, 0))
    g.empty('eject', (0.0130, y0 + 0.074, ZB + 0.010), (1, 0, 0))
    g.empty('grip_l', (0, 0.020, ZB - 0.045), (0, 1, 0))
    return g


@arma
def de_eremita():
    """EREMITA .50: pistola magnum a gas, canna da 6" con guida, carrello massiccio, freno di bocca e impugnatura grossa."""
    g = Gun('de_eremita', tex=1024)
    ZB = 0.088
    y0, y1 = -0.078, 0.150
    g.part('corpo')
    zf0, zf1 = ZB - 0.046, ZB - 0.0120
    g.box('acciaio', (0, (y0 + 0.020 + y1 - 0.010) / 2, (zf0 + zf1) / 2), (0.0300, (y1 - 0.010) - (y0 + 0.020), zf1 - zf0), r=0.0026, seg=3)
    g.box('acciaio', (0, y0 + 0.026, ZB - 0.0260), (0.0300, 0.044, 0.0260), r=0.0026)
    # canna esterna triangolare con guida superiore (la nervatura corre sul carrello fino al muso)
    g.prism('acciaio', [(0.050, ZB - 0.0120), (0.050, ZB + 0.0140), (y1 + 0.020, ZB + 0.0080), (y1 + 0.020, ZB - 0.0120)], -0.0150, 0.0150, r=0.0014)
    g.box('acciaio', (0, 0.084, ZB + 0.0195), (0.0120, 0.150, 0.0060), r=0.0010)
    for i in range(14):
        g.box('acciaio', (0, 0.014 + i * 0.0105, ZB + 0.0228), (0.0120, 0.0052, 0.0016), cut=True)   # scassi della guida
    trigger_guard(g, 'acciaio', 0.046, 0.002, zf0 + 0.001, zf0 - 0.0200, th=0.0050, w=0.0050)
    grip_sweep(g, 'polimero', 0.054, -0.060, hw=0.0185, dp=0.0235, rad=0.0090)
    g.box('polimero', (0, gpt(-0.008)[0] - 0.0225, gpt(-0.008)[1]), (0.0340, 0.0110, 0.090), rot=(-GT, 0, 0), r=0.0050)
    mag_cut_and_mag(g, ZB, hw=0.0135, dp=0.0195, bottom=-0.062, plate_role='acciaio')
    g.prism('acciaio', [(0.012, ZB - 0.0200), (0.060, ZB - 0.0190), (0.064, ZB - 0.0160), (0.012, ZB - 0.0160)], -0.0170, -0.0150, r=0.0006)
    g.part('trigger', pivot=(0, 0.022, ZB - 0.046))
    g.prism('acciaio', [(0.0235, ZB - 0.0400), (0.0315, ZB - 0.0400), (0.0355, ZB - 0.0600), (0.0285, ZB - 0.0600)], -0.0035, 0.0035, r=0.0008)
    g.part('corpo')
    # carrello (parte mobile)
    g.part('slide', pivot=(0, 0, ZB))
    g.box('acciaio', (0, (y0 + y1 - 0.040) / 2 + 0.010, ZB + 0.0040), (0.0310, (y1 - 0.040) - y0, 0.0360), r=0.0028, seg=3)
    g.box('acciaio', (0.0124, y0 + 0.074, ZB + 0.0095), (0.0090, 0.040, 0.0140), cut=True)
    for i in range(7):
        y = y0 + 0.008 + i * 0.0052
        for sx in (-1, 1):
            g.box('acciaio', (sx * 0.0153, y, ZB + 0.0030), (0.0024, 0.0017, 0.026), cut=True)
    top = ZB + 0.0040 + 0.0180
    # mire: tacca regolabile sul carrello e lama con punto sulla nervatura
    notch_rear(g, y0 + 0.012, top - 0.0005, h=0.0080, w=0.0190, gap=0.0042, depth=0.0100)
    for sx in (-1, 1):
        dot(g, (sx * 0.0068, y0 + 0.0065, top + 0.0040), 0.0014, 'y')
    g.box('acciaio', (0, y1 - 0.034, ZB + 0.0275), (0.0036, 0.0120, 0.0110), r=0.0006)
    g.box('emit', (0, y1 - 0.0402, ZB + 0.0300), (0.0018, 0.0003, 0.0036))
    # freno di bocca
    g.tube('acciaio', 0, ZB, y1 - 0.010, y1 + 0.030, 0.0125, 0.0058, segs=28)
    for i in range(2):
        g.box('acciaio', (0, y1 + 0.008 + i * 0.011, ZB + 0.0110), (0.0140, 0.0050, 0.0200), cut=True)
    g.part('corpo')
    g.empty('muzzle', (0, y1 + 0.031, ZB), (0, 1, 0))
    g.empty('ads', (0, y0 + 0.012, top + 0.0060), (0, 1, 0))
    g.empty('eject', (0.0155, y0 + 0.074, ZB + 0.0095), (1, 0, 0))
    g.empty('grip_l', (0, 0.020, ZB - 0.050), (0, 1, 0))
    return g
