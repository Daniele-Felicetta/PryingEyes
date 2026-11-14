"""Equipment found in the abandoned city district."""
from piezas import *
from armi_pistole import make_pistol, finish_pistol


@arma
def p9_ronda():
    """RONDA-9: compact municipal sidearm with a long shroud and work light."""
    g = Gun('p9_ronda', tex=1024)
    g.paint = {'polimero': (0.027, 0.037, 0.041)}
    zb, y0, y1 = 0.080, -0.070, 0.130
    top = make_pistol(g, zb, y0, y1, frame='polimero', grip='polimero',
                      serr=7, serr_front=4, ribs=True)
    finish_pistol(g, 'p9_ronda', zb, y0, y1, top)
    g.part('corpo')
    g.box('acciaio', (0, 0.117, zb - 0.011), (0.030, 0.075, 0.022), r=0.003)
    g.box('polimero', (0, 0.092, zb - 0.034), (0.029, 0.055, 0.014), r=0.002)
    g.box('acciaio', (0.020, 0.090, zb - 0.040), (0.012, 0.033, 0.018), r=0.002)
    g.box('vetro', (0.020, 0.110, zb - 0.040), (0.010, 0.002, 0.012), r=0.001)
    g.box('ottone', (-0.014, -0.045, zb + 0.011), (0.002, 0.025, 0.006), r=0.0005)
    g.empty('muzzle', (0, 0.158, zb), (0, 1, 0))
    return g
