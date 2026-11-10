"""Fucili d'assalto. Origine: centro dell'impugnatura; y avanti; z su."""
from piezas import *


def ar_upper_lower(g, ZB, rail_to=0.40, rear=-0.118):
    """Carcassa tipo AR: superiore con guida e finestra di espulsione, inferiore con pozzo del caricatore e ponticello,
    impugnatura, spine, selettore, rilascio del caricatore. Ritorna la quota del piano della guida."""
    top = ZB + 0.0215
    # superiore
    g.prism('alluminio', [(rear, ZB - 0.024), (rear, top), (0.150, top), (0.150, ZB - 0.024)], -0.0165, 0.0165, r=0.0016, seg=3)
    g.box('alluminio', (0, rear - 0.004, ZB + 0.002), (0.0300, 0.014, 0.036), r=0.003)                               # collare posteriore
    # inferiore
    g.prism('alluminio', [(rear, ZB - 0.052), (rear, ZB - 0.021), (0.098, ZB - 0.021), (0.098, ZB - 0.060), (0.095, ZB - 0.092),
                          (0.026, ZB - 0.092), (0.020, ZB - 0.060), (-0.012, ZB - 0.052)], -0.0155, 0.0155, r=0.0016, seg=3)
    g.box('alluminio', (0, 0.064, ZB - 0.104), (0.0305, 0.074, 0.024), r=0.002)                                      # bocca del pozzo
    g.box('alluminio', (0, 0.064, ZB - 0.103), (0.0238, 0.066, 0.030), cut=True)                                     # cavita' del pozzo
    # ponticello e grilletto
    g.prism('alluminio', [(0.026, ZB - 0.060), (0.026, ZB - 0.082), (-0.002, ZB - 0.082), (-0.012, ZB - 0.060),
                          (-0.008, ZB - 0.060), (0.0, ZB - 0.0775), (0.0215, ZB - 0.0775), (0.0215, ZB - 0.060)], -0.0022, 0.0022, r=0.0008)
    g.prism('acciaio', [(0.004, ZB - 0.052), (0.014, ZB - 0.052), (0.018, ZB - 0.074), (0.011, ZB - 0.074)], -0.0030, 0.0030, r=0.0006)
    # finestra di espulsione con sportellino, deflettore, forward assist
    g.box('alluminio', (0.0168, -0.034, ZB + 0.0035), (0.0030, 0.060, 0.0220), cut=True)
    g.box('alluminio', (0.0168, -0.034, ZB + 0.0035), (0.0020, 0.048, 0.0170), r=0.0005)
    g.lathe('acciaio', (0.0168, -0.050, ZB + 0.0035), [(-0.001, 0.0042), (0.001, 0.0042)], segs=12, axis='x')          # perno dello sportellino
    g.prism('alluminio', [(-0.092, ZB - 0.008), (-0.058, ZB - 0.002), (-0.058, ZB + 0.014), (-0.092, ZB + 0.002)], 0.0150, 0.0222, r=0.0020, seg=3)
    g.lathe('alluminio', (0.0230, -0.108, ZB + 0.0150), [(0.0, 0.0090), (0.0175, 0.0090), (0.0175, 0.0060), (0.026, 0.0060)], segs=16, axis='y', rot=(0, 0, 0)) if False else None
    g.lathe('alluminio', (0.0200, -0.0, ZB - 0.0120), [(-0.066, 0.0075), (-0.040, 0.0075), (-0.040, 0.0095), (-0.020, 0.0095)], segs=16, axis='y')
    # spine di scomposizione, selettore (sinistra), rilascio caricatore e fermo otturatore (destra)
    for yy in (-0.085, 0.052):
        g.lathe('acciaio', (0.0, yy, ZB - 0.033), [(-0.0185, 0.0042), (0.0185, 0.0042)], segs=14, axis='x')
        g.lathe('acciaio', (0.0185, yy, ZB - 0.033), [(0.0, 0.0050), (0.0018, 0.0050)], segs=14, axis='x')
    g.lathe('acciaio', (-0.0165, -0.014, ZB - 0.045), [(-0.003, 0.0058), (0.0, 0.0058)], segs=14, axis='x')
    g.box('acciaio', (-0.0215, -0.0145, ZB - 0.045), (0.0045, 0.0300, 0.0045), r=0.0008, rot=(0, 28, 0))             # leva del selettore
    g.lathe('acciaio', (0.0158, 0.040, ZB - 0.052), [(0.0, 0.0070), (0.0050, 0.0070)], segs=14, axis='x')
    g.box('acciaio', (0.0172, 0.008, ZB - 0.030), (0.0040, 0.0150, 0.0060), r=0.0008)
    # impugnatura (polimero), inclinata: la cima va in avanti
    path = arc((-0.015, -0.062), (0.016, 0.060), 0.0045, 10)
    hws = [0.0185 + 0.0040 * sin(pi * i / 10) for i in range(11)]
    dps = [0.0215 + 0.0035 * sin(pi * i / 10) for i in range(11)]
    g.sweep('polimero', path, hws, dps, rad=0.007, seg=3)
    g.box('polimero', (0, -0.020, -0.004), (0.0360, 0.0120, 0.050), r=0.004, rot=(-14, 0, 0))                          # dorso dell'impugnatura
    g.box('polimero', (0, 0.0, -0.067), (0.0420, 0.0480, 0.0060), r=0.0022)                                          # tallone
    return top


def ar_stock(g, ZB, tube_y=-0.272):
    """Calcio telescopico M4: tubo del tampone, guscio del calcio con guancia, calciolo di gomma."""
    zt = ZB - 0.012
    g.cyl('alluminio', 0, zt, -0.132, -0.200, 0.0138, ch=0.0008)
    g.cyl('alluminio', 0, zt - 0.003, -0.126, -0.134, 0.0170, ch=0.0008)
    ring = [(-0.130, rrect(0.0196, 0.0250, 0.009, 3, 0, zt - 0.011)), (-0.205, rrect(0.0204, 0.0272, 0.010, 3, 0, zt - 0.016)),
            (-0.272, rrect(0.0188, 0.0330, 0.010, 3, 0, zt - 0.020))]
    g.loft('polimero', ring, r=0.0016, seg=2)
    g.box('polimero', (0, -0.200, zt + 0.0150), (0.0270, 0.150, 0.0100), r=0.003)                                       # guancia
    g.prism('gomma', [(-0.270, zt - 0.052), (-0.282, zt - 0.052), (-0.285, zt + 0.014), (-0.272, zt + 0.014)], -0.0185, 0.0185, r=0.0022)
    g.box('acciaio', (0, -0.242, zt - 0.047), (0.0120, 0.032, 0.0070), r=0.0015)                                        # leva di regolazione
    g.box('acciaio', (0, -0.214, zt - 0.0425), (0.0060, 0.0060, 0.0100), r=0.0008)                                       # attacco della cinghia


def ar_handguard(g, ZB, y0=0.082, y1=0.395, hw=0.0225, hh=0.0235):
    """Astina libera con asole M-LOK (tagli), anello del dado, guida in cima."""
    zc = ZB - 0.0020
    g.loft('alluminio', [(y0, rrect(hw, hh, 0.0075, 3, 0, zc)), (y1, rrect(hw, hh, 0.0075, 3, 0, zc))], r=0.0012, seg=2)
    g.lathe('acciaio', (0, 0, ZB), [(y0 - 0.020, 0.0235), (y0 - 0.020, 0.0295), (y0 + 0.004, 0.0295), (y0 + 0.004, 0.0235)], segs=28)  # dado
    slots_on(g, 'alluminio', 0, zc, y0 + 0.030, y1 - 0.025, hw, hh, n=5, length=0.034, sides=True, bottom=True)
    g.lathe('alluminio', (0, 0, ZB), [(y1, 0.0),(y1 + 0.001, hh)], segs=4) if False else None
    g.box('alluminio', (0, y1 - 0.004, zc), (hw * 2 + 0.0010, 0.008, hh * 2 + 0.0010), r=0.0022)                      # bordo anteriore


def ar_barrel(g, ZB, y_end=0.44, brake='hider'):
    g.cyl('acciaio', 0, ZB, 0.080, y_end, 0.0108, ch=0.0008)
    g.cyl('acciaio', 0, ZB, 0.372, 0.404, 0.0152, ch=0.0006)                  # blocco del gas / base mirino
    g.box('acciaio', (0, 0.388, ZB + 0.0095), (0.0136, 0.016, 0.014), r=0.0012)
    if brake == 'hider':
        flash_hider(g, 'acciaio', y_end - 0.004, ZB)


def ar_mag(g, ZB, pivot_top=(0.060, None), drop=0.150, bulge=-0.014, depth=0.0300):
    """Caricatore STANAG: parte mobile con pivot in alto. drop = lunghezza, bulge = curvatura in avanti."""
    zt = ZB - 0.074
    yt = 0.064
    g.part('caricatore', pivot=(0.0, yt, zt))
    path = arc((yt, zt), (yt + 0.026 * drop / 0.150, zt - drop), bulge, 14)
    g.sweep('acciaio', path, 0.0120, depth, rad=0.004, seg=2)
    yb, zb = path[-1]
    g.box('polimero', (0, yb + 0.001, zb - 0.004), (0.0270, depth * 2 + 0.009, 0.0100), r=0.0030)
    g.part('corpo')


def radians_deg(a):
    return a


def charging_handle(g, ZB):
    g.part('charge', pivot=(0.0, -0.126, ZB + 0.012))
    g.box('acciaio', (0, -0.122, ZB + 0.0145), (0.0180, 0.030, 0.0080), r=0.0016)
    for sx in (-1, 1):
        g.box('acciaio', (sx * 0.0215, -0.138, ZB + 0.0140), (0.0150, 0.0160, 0.0120), r=0.0030)
    g.box('acciaio', (0, -0.147, ZB + 0.0140), (0.040, 0.008, 0.0090), r=0.0018)
    g.part('corpo')


@arma
def ar9_malleus():
    """AR-9 MALLEUS: carabina d'assalto M4: guida superiore, astina M-LOK, calcio telescopico, punto rosso."""
    g = Gun('ar9_malleus', tex=2048)
    ZB = 0.125
    g.part('corpo')
    top = ar_upper_lower(g, ZB)
    ar_stock(g, ZB)
    ar_handguard(g, ZB)
    ar_barrel(g, ZB)
    picatinny(g, 'alluminio', -0.112, 0.146, top)
    picatinny(g, 'alluminio', 0.092, 0.385, top)
    ar_mag(g, ZB)
    charging_handle(g, ZB)
    zdot = top + 0.0068 + 0.0275 + 0.0225 - 0.0068
    reflex_tube(g, 0.050, zdot)
    g.empty('muzzle', (0, 0.505, ZB), (0, 1, 0))
    g.empty('ads', (0, 0.050 - 0.034, zdot), (0, 1, 0))
    g.empty('eject', (0.022, -0.030, ZB + 0.004), (1, 0, 0))
    g.empty('grip_l', (0, 0.215, ZB), (0, 1, 0))
    return g


def ar_a2_stock(g, ZB):
    """Calcio fisso A2 lungo, in polimero, con calciolo di gomma."""
    zt = ZB - 0.012
    stock_side(g, 'polimero', [(-0.125, zt + 0.022), (-0.330, zt + 0.034), (-0.338, zt - 0.075), (-0.320, zt - 0.080), (-0.250, zt - 0.036), (-0.125, zt - 0.026)],
               0.0185, r=0.006, pad=[(-0.330, zt + 0.036), (-0.348, zt + 0.036), (-0.354, zt - 0.080), (-0.336, zt - 0.080)])
    g.box('acciaio', (0, -0.250, zt - 0.040), (0.0090, 0.0090, 0.0100), r=0.0010)


@arma
def ar_trinita():
    """TRINITA': fucile a raffica da tre colpi, maniglia di trasporto con mira a diaframma, astina tonda, calcio fisso lungo."""
    g = Gun('ar_trinita', tex=2048)
    ZB = 0.125
    g.part('corpo')
    top = ar_upper_lower(g, ZB)
    ar_a2_stock(g, ZB)
    # maniglia di trasporto con apertura sotto
    g.prism('alluminio', [(-0.108, top), (-0.108, top + 0.052), (-0.098, top + 0.064), (0.045, top + 0.064), (0.070, top + 0.052), (0.085, top + 0.022), (0.090, top)], -0.0118, 0.0118, r=0.0030, seg=3)
    g.prism('alluminio', [(-0.074, top - 0.001), (-0.074, top + 0.038), (0.040, top + 0.038), (0.058, top + 0.018), (0.068, top - 0.001)], -0.0150, 0.0150, cut=True)
    aperture_rear(g, -0.121, top, h=0.0515, ring_out=0.0100, ring_in=0.0046, width=0.0200)
    # astina tonda a due semigusci con nervature
    g.lathe('polimero', (0, 0, ZB), [(0.078, 0.0225), (0.092, 0.0245), (0.285, 0.0232), (0.300, 0.0218)], segs=24)
    for k in range(7):
        g.lathe('polimero', (0, 0, ZB), [(0.105 + k * 0.026, 0.0246), (0.109 + k * 0.026, 0.0246)], segs=24, cap=(False, False)) if False else None
        g.box('polimero', (0.0, 0.110 + k * 0.026, ZB + 0.0250), (0.0300, 0.0070, 0.0030), cut=True)
        g.box('polimero', (0.0, 0.110 + k * 0.026, ZB - 0.0250), (0.0300, 0.0070, 0.0030), cut=True)
    g.lathe('acciaio', (0, 0, ZB), [(0.060, 0.0235), (0.060, 0.0295), (0.082, 0.0295), (0.082, 0.0235)], segs=28)
    g.cyl('acciaio', 0, ZB, 0.082, 0.470, 0.0102, ch=0.0006)
    g.cyl('acciaio', 0, ZB, 0.300, 0.340, 0.0140, ch=0.0006)
    fsb_tower(g, 0.372, ZB + 0.0105, ZB + 0.071)
    flash_hider(g, 'acciaio', 0.466, ZB)
    ar_mag(g, ZB)
    charging_handle(g, ZB)
    g.empty('muzzle', (0, 0.532, ZB), (0, 1, 0))
    g.empty('ads', (0, -0.121, top + 0.0495), (0, 1, 0))
    g.empty('eject', (0.022, -0.030, ZB + 0.004), (1, 0, 0))
    g.empty('grip_l', (0, 0.215, ZB), (0, 1, 0))
    return g


@arma
def dmr_lance7():
    """LANCE-7: fucile di precisione semiautomatico, canna pesante con freno di bocca, calcio con guancia regolabile, ottica 4x."""
    g = Gun('dmr_lance7', tex=2048)
    ZB = 0.125
    g.part('corpo')
    top = ar_upper_lower(g, ZB, rear=-0.118)
    zt = ZB - 0.012
    g.cyl('alluminio', 0, zt, -0.132, -0.205, 0.0138, ch=0.0008)
    stock_side(g, 'polimero', [(-0.120, zt + 0.028), (-0.215, zt + 0.032), (-0.250, zt + 0.058), (-0.342, zt + 0.062), (-0.352, zt - 0.070), (-0.334, zt - 0.074), (-0.262, zt - 0.040), (-0.215, zt - 0.040), (-0.120, zt - 0.030)],
               0.0190, r=0.0070, pad=[(-0.342, zt + 0.064), (-0.360, zt + 0.064), (-0.366, zt - 0.072), (-0.350, zt - 0.072)])
    g.box('gomma', (0, -0.292, zt + 0.0625), (0.0300, 0.090, 0.0050), r=0.0020)         # guancia regolabile
    ar_handguard(g, ZB, y0=0.082, y1=0.420, hw=0.0230, hh=0.0245)
    g.cyl('acciaio', 0, ZB, 0.082, 0.540, 0.0130, ch=0.0008)
    muzzle_brake(g, 'acciaio', 0.520, ZB, rad=0.0190, length=0.075)
    picatinny(g, 'alluminio', -0.112, 0.146, top)
    picatinny(g, 'alluminio', 0.092, 0.420, top)
    ar_mag(g, ZB, drop=0.105, bulge=-0.008, depth=0.0300)
    charging_handle(g, ZB)
    bipod_folded(g, 0.330, ZB - 0.034, length=0.14)
    # ottica 4x: oculare dietro alla guida, il calcio resta sotto la linea di mira
    L = 0.270
    y_oc = -0.098
    zs = top + 0.0068 + 0.0145 + 0.020 + 0.0055
    scope(g, y_oc + L / 2, zs, length=L, tube=0.0145, bell_obj=0.0300, bell_oc=0.0225, mount_h=0.020)
    g.empty('muzzle', (0, 0.598, ZB), (0, 1, 0))
    g.empty('ads', (0, y_oc - 0.0005, zs), (0, 1, 0))
    g.empty('eject', (0.022, -0.030, ZB + 0.004), (1, 0, 0))
    g.empty('grip_l', (0, 0.215, ZB), (0, 1, 0))
    return g
