"""Il coltello del giocatore (fase 2 dei round): non e' un'arma da sparo, ma passa dalla stessa pipeline (kit, bake, mani su misura)
cosi' la mano si adatta all'impugnatura vera. Origine: centro dell'impugnatura; y avanti; z su; l'asse dell'impugnatura e' inclinato
come nelle pistole (la cima va avanti)."""
from math import sin, cos, radians
from piezas import *
from armi_pistole import grip_sweep, gpt, GT


def lozenge(hw, ht, z):
    """Sezione a rombo della lama: (x, z) in senso antiorario, centrata a quota z."""
    return [(-hw, z), (0.0, z + ht), (hw, z), (0.0, z - ht)]


@arma
def kn_coltello():
    """MISERICORDIA: pugnale rituale a impugnatura verticale. Pugno chiuso su un manico di cuoio nero, sopra il pugno la guardia
    d'ottone con un occhio d'ambra (lo vede il giocatore, da dietro) e la lama a foglia, a rombo, lunga 20 cm: esce dal pugno in
    avanti come una canna, cosi' la mano si modella sull'impugnatura come per una pistola."""
    g = Gun('kn_coltello', tex=1024)
    g.paint = {'gomma': (0.018, 0.017, 0.016), 'inox': (0.62, 0.64, 0.68)}
    g.bevel['inox'] = 0.0                        # il filo e' gia' nella sezione a rombo: lo smusso globale la farebbe collassare
    ZB = 0.076                                   # quota dell'asse della lama
    g.part('corpo')

    # manico: cuoio nero avvolto, fasce d'ottone, pomolo a goccia
    grip_sweep(g, 'gomma', 0.052, -0.055, hw=0.0148, dp=0.0200, rad=0.0072, bulge=0.002)
    for h in (-0.046, -0.032, 0.022, 0.038):     # la fascia a meta' resta libera: li' si misura la presa della mano
        g.box('ottone', (0, gpt(h)[0], gpt(h)[1]), (0.0318, 0.0428, 0.0030), rot=(-GT, 0, 0), r=0.0011)
    g.lathe('ottone', (0, gpt(-0.058)[0], gpt(-0.058)[1]), [(-0.012, 0.0), (-0.009, 0.0110), (-0.002, 0.0185), (0.008, 0.0170), (0.014, 0.0150)],
            segs=22, axis='z', rot=(-GT, 0, 0), squash=(0.92, 1.08))
    g.sphere('ottone', (0, gpt(-0.074)[0] - 0.0005, gpt(-0.074)[1]), 0.0058, segs=12)

    # guardia: blocco d'ottone sopra il pugno (porta la lama), crociera con le punte a bocciolo
    g.box('ottone', (0, 0.012, ZB - 0.013), (0.0300, 0.0580, 0.0300), r=0.0075, seg=3)
    g.box('ottone', (0, gpt(0.040)[0] + 0.012, ZB - 0.030), (0.0235, 0.0300, 0.0100), r=0.0040)           # collo verso il manico
    g.box('ottone', (0, 0.044, ZB), (0.0780, 0.0100, 0.0125), r=0.0040, seg=3)                             # crociera
    for sx in (-1, 1):
        g.sphere('ottone', (sx * 0.0405, 0.044, ZB), 0.0072, segs=12, squash=(1.0, 0.85, 1.0))
    g.box('ottone', (0, 0.050, ZB), (0.0220, 0.0100, 0.0200), r=0.0045)                                    # radice della lama (ricasso)
    # occhio sul dietro del blocco, quello che il giocatore ha davanti: sclera d'osso, iride d'ambra, pupilla a fessura
    yb = -0.0170
    g.lathe('osso', (0, yb, ZB - 0.0090), [(0.0010, 0.0100), (-0.0016, 0.0100), (-0.0016, 0.0094)], segs=22, axis='y', squash=(1.25, 1.0))
    g.lathe('emit_ambra', (0, yb - 0.0020, ZB - 0.0090), [(0.0, 0.0072), (-0.0006, 0.0072)], segs=22, axis='y')
    g.box('gomma', (0, yb - 0.0030, ZB - 0.0090), (0.0030, 0.0008, 0.0108))

    # lama a foglia, sezione a rombo: sale dalla crociera alla punta
    prof = [(0.056, 0.0128), (0.085, 0.0152), (0.120, 0.0168), (0.160, 0.0152), (0.200, 0.0114), (0.232, 0.0064), (0.254, 0.0008)]
    g.loft('inox', [(y, lozenge(hw, hw * 0.26, ZB)) for y, hw in prof], cap=True, r=0.0)

    g.empty('muzzle', (0, 0.254, ZB), (0, 1, 0))
    g.empty('ads', (0, 0.050, ZB + 0.020), (0, 1, 0))
    g.empty('eject', (0.0200, 0.0, ZB), (1, 0, 0))
    g.empty('grip_l', (0, 0.080, ZB - 0.050), (0, 1, 0))
    return g
