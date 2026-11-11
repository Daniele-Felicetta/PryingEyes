"""
Accessori dei nemici dei piani a tema (fase 4). I quattro nemici nuovi sono il monaco con un altro costume (stesso
scheletro e stesse clip, vedi genera_monaco.py): qui si fanno i pezzi che cambiano la silhouette, tutti rigidi, attaccati
a un osso dal Unity (Editor/NemiciTemiImport.cs):
  teatro      Maschera   acc_lanterna (asta con uncino e lanterna), acc_maschera (piatto di porcellana con il foro per l'occhio)
              Direttore  acc_diapason (asta con un diapason in cima), acc_cilindro (cilindro di seta), acc_farfallino
  sanatorio   Infermiere acc_flebo (asta della flebo con la sacca verde)
              Primario   acc_scialitica (aureola di lampade chirurgiche), acc_specchio (specchio frontale),
                         acc_uncino (asta con un divaricatore)
Tutto e' disegnato nel riferimento del monaco a riposo (Z in alto, guarda verso -Y, la sua sinistra +X; metri), cosi' il
Unity mette ogni pezzo al suo posto senza altri calcoli. Le aste sostituiscono quella di legno del monaco e tengono lo
stesso raggio di presa (0,018 m: la mano sinistra le afferra com'e').

Uso (dalla cartella del file):
  blender -b -P genera_accessori.py -- build            export/accessori_temi.fbx
  blender -b -P genera_accessori.py -- render [nome ..] anteprime dei quattro nemici in render/ (Cycles su CPU)
                                                         nomi: maschera direttore infermiere primario
"""
import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix
from math import sin, cos, pi, radians, sqrt, asin

HERE = os.path.dirname(os.path.abspath(__file__))
for sub in ('hotel', 'chiesa', 'monaco'):
    sys.path.insert(0, os.path.join(HERE, '..', sub))
import genera_arredi as ga               # noqa: E402  (primitive, Asset, ruoli)
import genera_monaco as M                # noqa: E402  (profilo del cappuccio, scheletro, studio per le anteprime)
from genera_arredi import V, box, lathe, sphere, cyl, tube, Asset   # noqa: E402

ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
EXPORT_DIR = os.path.join(HERE, 'export')
RENDER_DIR = os.path.join(HERE, 'render')
TEX_DIR = r"C:\Users\danie\Desktop\prying-eyes\unity\PryingEyes\Assets\_PryingEyes\Models\Temi\Textures"

# ruoli per le anteprime (in Unity li sostituisce NemiciTemiImport coi suoi materiali)
ga.ROLE.update({
    'A_acciaio': ((0.62, 0.64, 0.66), 1.0, 0.30), 'A_legno_scuro': ((0.10, 0.06, 0.035), 0.0, 0.45),
    'A_gomma': ((0.03, 0.03, 0.035), 0.0, 0.45), 'A_seta_nera': ((0.012, 0.012, 0.016), 0.0, 0.32),
    'A_nastro': ((0.20, 0.02, 0.03), 0.0, 0.55), 'A_porcellana': ((0.86, 0.84, 0.78), 0.0, 0.14),
    'A_oro': ((0.78, 0.58, 0.20), 1.0, 0.28), 'A_stoffa_bianca': ((0.88, 0.88, 0.84), 0.0, 0.85),
    'A_plastica': ((0.78, 0.84, 0.78), 0.0, 0.25), 'A_specchio': ((0.50, 0.55, 0.57), 1.0, 0.06),
    'A_luce_calda': ((1.0, 0.62, 0.25), 0.0, 0.2), 'A_luce_fredda': ((0.80, 0.95, 1.0), 0.0, 0.2),
    'A_luce_verde': ((0.55, 1.0, 0.35), 0.0, 0.2),
})
EMIT = {'A_luce_calda': ((1.0, 0.55, 0.18), 6.0), 'A_luce_fredda': ((0.8, 0.95, 1.0), 6.0), 'A_luce_verde': ((0.5, 1.0, 0.3), 4.0)}
_prev_material = ga.material


def _material(role):
    fresh = not (role in ga._mats and ga._mats[role].name in bpy.data.materials)
    m = _prev_material(role)
    if fresh and role in EMIT:
        bs = m.node_tree.nodes['Principled BSDF']
        bs.inputs['Emission Color'].default_value = (*EMIT[role][0], 1)
        bs.inputs['Emission Strength'].default_value = EMIT[role][1]
    return m


ga.material = _material


# ----------------------------------------------------------------------------- attrezzi
def merge(dst, src, Mx=None):
    """Copia un bmesh a parte dentro 'dst' dopo averlo trasformato con Mx (4x4). Libera 'src'."""
    if Mx is not None:
        bmesh.ops.transform(src, matrix=Mx, verts=src.verts[:])
    mp = {v: dst.verts.new(v.co) for v in src.verts}
    for f in src.faces:
        dst.faces.new([mp[v] for v in f.verts])
    src.free()


# l'asta del monaco: piede a terra, direzione quasi verticale, la mano sinistra la stringe in G
SD = M.STAFF_DIR.copy()
G = V(0.2810226, -0.3603914, 1.0232003)              # la presa (scritta nello .blend esportato come scena['presa'])
B = G - SD * (G.z / SD.z)                             # piede dell'asta, a terra
E2 = (V(1, 0, 0) - SD * SD.x).normalized()            # sinistra del monaco, lungo l'asta
E1 = E2.cross(SD).normalized()                        # davanti (verso -Y)
GRIP_R = 0.0165                                       # raggio dell'asta alla presa (la mano chiude su 0,018)


def staff_frame(h=0.0, extra=None):
    """Riferimento locale dell'asta, destrorso: x = destra del monaco, y = davanti, z = lungo l'asta; origine a quota h."""
    R = Matrix(((-E2.x, E1.x, SD.x), (-E2.y, E1.y, SD.y), (-E2.z, E1.z, SD.z))).to_4x4()
    F = Matrix.Translation(B + SD * h) @ R
    return F if extra is None else F @ extra


def pole(bm, top, r=GRIP_R, foot=0.0):
    """Asta dritta dal piede a 'top' (locale)."""
    t = bmesh.new()
    cyl(t, V(0, 0, foot), V(0, 0, top), r, 14)
    merge(bm, t, staff_frame())


def closed_tube(bm, pts, r, segs=8):
    """Tubo lungo una spezzata chiusa (cornici)."""
    n = len(pts)
    rings = []
    for i, p in enumerate(pts):
        tg = (pts[(i + 1) % n] - pts[i - 1]).normalized()
        up = V(0, 0, 1) if abs(tg.z) < 0.9 else V(1, 0, 0)
        nx = tg.cross(up).normalized()
        ny = tg.cross(nx).normalized()
        rings.append([bm.verts.new(p + (nx * cos(2 * pi * j / segs) + ny * sin(2 * pi * j / segs)) * r) for j in range(segs)])
    for i in range(n):
        a, b = rings[i], rings[(i + 1) % n]
        for j in range(segs):
            bm.faces.new((a[j], a[(j + 1) % segs], b[(j + 1) % segs], b[j]))


# ----------------------------------------------------------------------------- Maschera
def lanterna():
    """Asta d'usciere: legno scuro con puntale e collarini d'ottone, uncino che porta la lanterna davanti a lui."""
    A = Asset('acc_lanterna')
    wood, br, glass = A.bm('A_legno_scuro'), A.bm('A_ottone'), A.bm('A_luce_calda')
    pole(wood, 1.60)
    t = bmesh.new()
    cyl(t, V(0, 0, 0.0), V(0, 0, 0.07), 0.021, 14)
    cyl(t, V(0, 0, 1.40), V(0, 0, 1.44), 0.0215, 14)
    cyl(t, V(0, 0, 1.58), V(0, 0, 1.64), 0.022, 14)
    merge(br, t, staff_frame())
    # uncino: sale, gira in avanti e scende; la lanterna pende dalla punta
    hook = V(0, 0.17, 1.66)
    t = bmesh.new()
    tube(t, [V(0, 0, 1.62), V(0, 0, 1.70), V(0, 0.05, 1.765), V(0, 0.11, 1.77), V(0, 0.16, 1.725), hook],
         [0.0145, 0.013, 0.012, 0.012, 0.0115, 0.011], segs=8, n=30)
    merge(br, t, staff_frame())
    # lanterna: tetto a piramide, quattro montanti, vetri accesi, base
    L = staff_frame(0.0, Matrix.Translation(hook))
    t = bmesh.new()                                                                       # tetto: piramide con le facce sugli assi
    lathe(t, [(0.088, -0.108), (0.084, -0.098), (0.0, -0.050)], c=V(0, 0, 0), segs=4, closed_top=True, closed_bottom=True)
    merge(br, t, L @ Matrix.Rotation(radians(45), 4, 'Z'))
    t = bmesh.new()
    cyl(t, V(0, 0, -0.012), V(0, 0, -0.050), 0.0045, 8)                                   # anello e catenella
    sphere(t, V(0, 0, -0.006), 0.011, 8)
    sphere(t, V(0, 0, -0.049), 0.011, 8)
    for sx in (-1, 1):
        for sy in (-1, 1):
            cyl(t, V(sx * 0.047, sy * 0.047, -0.285), V(sx * 0.047, sy * 0.047, -0.108), 0.0055, 8)
    box(t, V(0, 0, -0.114), V(0.112, 0.112, 0.012), bevel=0.003, seg=2)
    box(t, V(0, 0, -0.292), V(0.112, 0.112, 0.014), bevel=0.003, seg=2)
    lathe(t, [(0.032, -0.299), (0.012, -0.322)], c=V(0, 0, 0), segs=10)
    merge(br, t, L)
    t = bmesh.new()
    for sx, sy, w, d in ((1, 0, 0.004, 0.088), (-1, 0, 0.004, 0.088), (0, 1, 0.088, 0.004), (0, -1, 0.088, 0.004)):
        box(t, V(sx * 0.0465, sy * 0.0465, -0.2), V(w, d, 0.156), bevel=0.0)
    sphere(t, V(0, 0, -0.205), 0.02, 8)
    merge(glass, t, L)
    return A


def maschera():
    """Piatto di porcellana sul davanti del cappuccio, con il foro per l'occhio, bordo e anello d'oro, una lacrima d'oro."""
    A = Asset('acc_maschera')
    por, oro, lac, rosso = A.bm('A_porcellana'), A.bm('A_oro'), A.bm('A_gomma'), A.bm('A_nastro')
    zc, hz, hw, off = 1.505, 0.19, 0.150, 0.016          # centro e semiassi del piatto, distanza dalla stoffa
    thick = 0.008                                          # spessore del piatto (dalla faccia esterna verso la stoffa)
    ez, er = M.EYE_C.z, 0.088                              # foro per l'occhio
    prof = M.HOOD_PROFILE

    def surf_xz(x, z, d=off, with_n=False):
        """Punto sul davanti del cappuccio (superellisse del profilo) a quota z e ascissa x, spostato di d lungo la normale."""
        cy, ax, ay, nf = M.interp_profile(prof, z)
        s = max(-0.985, min(0.985, x / ax))
        t = asin(math.copysign(abs(s) ** (nf / 2.0), s))
        y = cy - ay * M.spow(cos(t), nf)
        n = V(x / ax ** 2, (y - cy) / ay ** 2, 0).normalized()
        p = V(x, y, z) + n * d
        return (p, n) if with_n else p

    def width(z):
        return hw * sqrt(max(0.0, 1 - ((z - zc) / hz) ** 2))

    def surf(z, u):
        return surf_xz(u * width(z), z)

    # --- piatto: griglia sul davanti del cappuccio con il foro tondo (i vertici sul bordo del foro stanno esattamente sul cerchio)
    nz, nt = 46, 40
    xz = []
    for k in range(nz + 1):
        z = zc - hz + 2 * hz * k / nz
        xz.append([[(-1 + 2 * i / nt) * width(z), z] for i in range(nt + 1)])
    cut = set()
    for k in range(nz):
        for i in range(nt):
            cx = sum(xz[a][b][0] for a, b in ((k, i), (k, i + 1), (k + 1, i), (k + 1, i + 1))) / 4
            cz = sum(xz[a][b][1] for a, b in ((k, i), (k, i + 1), (k + 1, i), (k + 1, i + 1))) / 4
            if cx * cx + (cz - ez) ** 2 < er * er:
                cut.add((k, i))
    keep = [(k, i) for k in range(nz) for i in range(nt) if (k, i) not in cut]
    for k in range(nz + 1):
        for i in range(nt + 1):
            cells = [(a, b) for a, b in ((k - 1, i - 1), (k - 1, i), (k, i - 1), (k, i)) if 0 <= a < nz and 0 <= b < nt]
            if any(c in cut for c in cells) and any(c not in cut for c in cells):
                x, z = xz[k][i]
                d = math.hypot(x, z - ez)
                xz[k][i] = [x * er / d, ez + (z - ez) * er / d]
    bm = bmesh.new()
    vo, vi = {}, {}

    def vert(table, k, i, d):
        if k in (0, nz):                                    # ai poli (larghezza nulla) un vertice solo per riga
            i = 0
        key = (k, i)
        if key not in table:
            table[key] = bm.verts.new(surf_xz(xz[k][i][0], xz[k][i][1], d))
        return table[key]
    for k, i in keep:
        cell = ((k, i), (k, i + 1), (k + 1, i + 1), (k + 1, i))
        o = [vert(vo, a, b, off) for a, b in cell]
        n = [vert(vi, a, b, off - thick) for a, b in cell]
        if len(set(o)) >= 3:
            bm.faces.new(list(dict.fromkeys(o)))                          # faccia esterna (normale in fuori)
            bm.faces.new(list(dict.fromkeys(n))[::-1])                    # faccia interna (verso la stoffa)
    # pareti sul bordo (foro e contorno): ogni lato di una cella tenuta che non ha accanto un'altra cella tenuta
    keepset = set(keep)
    sides = (((0, 1), (-1, 0)), ((1, 2), (0, 1)), ((2, 3), (1, 0)), ((3, 0), (0, -1)))      # (spigolo, cella confinante)
    for k, i in keep:
        cell = ((k, i), (k, i + 1), (k + 1, i + 1), (k + 1, i))
        for (ea, eb), (dk, di) in sides:
            if (k + dk, i + di) in keepset:
                continue
            ao, bo = vert(vo, *cell[ea], off), vert(vo, *cell[eb], off)
            ai, bi = vert(vi, *cell[ea], off - thick), vert(vi, *cell[eb], off - thick)
            if ao is bo:
                continue
            bm.faces.new((ao, ai, bi, bo))
    merge(por, bm)
    # --- bordo: tubo d'oro lungo il contorno; anello attorno al foro; lacrima; sopracciglio e guance laccati
    ne = 36
    edge = [surf(zc - hz + 2 * hz * k / ne, -1) for k in range(ne + 1)] + [surf(zc - hz + 2 * hz * (ne - k) / ne, 1) for k in range(1, ne)]
    t = bmesh.new()
    closed_tube(t, edge, 0.0058, 6)
    merge(oro, t)
    # anello attorno al foro: segue la curvatura del cappuccio (un anello piano starebbe a mezz'aria sui lati)
    ring = [surf_xz((er + 0.006) * cos(2 * pi * i / 56), ez + (er + 0.006) * sin(2 * pi * i / 56), off + 0.002) for i in range(56)]
    t = bmesh.new()
    closed_tube(t, ring, 0.0068, 8)
    # lacrima sotto il foro: goccia con la punta in su, poggiata sul piatto
    zd = ez - er - 0.055
    drop = surf_xz(0.0, zd, off + 0.002)
    lathe(t, [(0.0, -0.016), (0.010, -0.012), (0.0115, -0.004), (0.0085, 0.010), (0.004, 0.024), (0.0, 0.034)],
          c=drop, segs=12)
    merge(oro, t)
    # sopracciglio nero, alzato su un lato (da sguardo); guance rosse a goccia schiacciata
    brow = [surf_xz(x, ez + er + 0.036 + 0.020 * (1 - (x / 0.07) ** 2) + 0.010 * x / 0.07, off + 0.001)
            for x in (-0.07, -0.036, 0.0, 0.036, 0.07)]
    t = bmesh.new()
    tube(t, brow, [0.0025, 0.0062, 0.0075, 0.0058, 0.0022], segs=8, n=28)
    merge(lac, t)
    for sx in (-1, 1):
        p, n = surf_xz(sx * 0.104, ez - 0.088, off, with_n=True)
        t = bmesh.new()
        sphere(t, V(0, 0, 0), 0.026, 12)
        merge(rosso, t, Matrix.Translation(p) @ n.to_track_quat('Z', 'Y').to_matrix().to_4x4() @ Matrix.Diagonal((1.0, 1.0, 0.16, 1.0)))
    return A


# ----------------------------------------------------------------------------- Direttore
def diapason():
    """Asta d'ebano con un diapason d'acciaio in cima, i rebbi larghi quanto basta per una sfera di luce in mezzo."""
    A = Asset('acc_diapason')
    wood, st, br = A.bm('A_legno_scuro'), A.bm('A_acciaio'), A.bm('A_ottone')
    pole(wood, 1.36)
    t = bmesh.new()
    cyl(t, V(0, 0, 0.0), V(0, 0, 0.06), 0.021, 14)
    merge(st, t, staff_frame())
    t = bmesh.new()
    cyl(t, V(0, 0, 1.33), V(0, 0, 1.39), 0.0235, 14)
    merge(br, t, staff_frame())
    # diapason nel piano x-z (visto di fronte e' una U), rebbi alti 0,34 m
    c = V(0, 0, 1.47)
    r_u = 0.034
    pts = [V(-r_u, 0, 1.80), V(-r_u, 0, 1.47)]
    pts += [c + V(-r_u * cos(a), 0, -r_u * sin(a)) for a in (pi * 0.25, pi * 0.5, pi * 0.75)]
    pts += [V(r_u, 0, 1.47), V(r_u, 0, 1.80)]
    t = bmesh.new()
    tube(t, pts, [0.0088] * len(pts), segs=10, n=60)
    cyl(t, V(0, 0, 1.39), V(0, 0, 1.44), 0.0115, 10)
    for sx in (-1, 1):
        sphere(t, V(sx * r_u, 0, 1.80), 0.0095, 10)
    merge(st, t, staff_frame())
    return A


def cilindro():
    """Cilindro di seta nera con nastro, un po' inclinato all'indietro; poggia sulla punta del cappuccio."""
    A = Asset('acc_cilindro')
    silk, band = A.bm('A_seta_nera'), A.bm('A_nastro')
    c = V(0.0, 0.004, 1.745)
    tilt = Matrix.Translation(c) @ Matrix.Rotation(radians(-7.0), 4, 'X')
    t = bmesh.new()
    lathe(t, [(0.100, 0.0), (0.168, 0.0), (0.176, 0.005), (0.168, 0.010), (0.100, 0.010)], segs=40, closed_top=False, closed_bottom=False, loop=True)
    lathe(t, [(0.108, 0.0), (0.1075, 0.06), (0.1045, 0.14), (0.1010, 0.23), (0.0985, 0.258), (0.090, 0.270), (0.0, 0.272)],
          segs=40, closed_top=True, closed_bottom=False)
    merge(silk, t, tilt)
    t = bmesh.new()
    lathe(t, [(0.1085, 0.012), (0.1100, 0.012), (0.1105, 0.050), (0.1090, 0.050)], segs=40, closed_top=False, closed_bottom=False, loop=True)
    merge(band, t, tilt)
    return A


def farfallino():
    """Papillon bianco sul petto, sopra la mantellina del cappuccio."""
    A = Asset('acc_farfallino')
    w = A.bm('A_stoffa_bianca')
    c = V(0.0, -0.271, 1.188)
    t = bmesh.new()
    sphere(t, V(0, 0, 0), 0.0135, 10)
    merge(w, t, Matrix.Translation(c))
    for s in (-1, 1):
        t = bmesh.new()
        lathe(t, [(0.004, 0.008), (0.030, 0.070)], c=V(0, 0, 0), segs=6, axis='X', closed_top=True, closed_bottom=True)
        mx = Matrix.Diagonal((s, 0.42, 1.0, 1.0))
        merge(w, t, Matrix.Translation(c) @ mx)
    return A


# ----------------------------------------------------------------------------- Infermiere
def flebo():
    """Asta della flebo d'acciaio con piede di gomma, traversa a due ganci e una sacca che brilla di verde; il tubicino scende verso la mano."""
    A = Asset('acc_flebo')
    st, gum, plast, liq = A.bm('A_acciaio'), A.bm('A_gomma'), A.bm('A_plastica'), A.bm('A_luce_verde')
    pole(st, 1.62)
    t = bmesh.new()
    cyl(t, V(0, 0, 0.0), V(0, 0, 0.045), 0.022, 14)
    merge(gum, t, staff_frame())
    t = bmesh.new()
    sphere(t, V(0, 0, 1.625), 0.021, 12)
    cyl(t, V(-0.17, 0, 1.58), V(0.17, 0, 1.58), 0.009, 10)
    for sx in (-1, 1):
        tube(t, [V(sx * 0.17, 0, 1.575), V(sx * 0.17, 0, 1.625), V(sx * 0.155, 0, 1.652)], [0.0072, 0.0068, 0.0066], segs=8, n=12)
    merge(st, t, staff_frame())
    # sacca: il liquido che si accende, con la testata, i bordi e il raccordo di plastica
    # (la sacca pende dal lato esterno, a sinistra del monaco: dall'altra parte coprirebbe l'occhio, che e' il punto debole)
    bx = -0.17
    t = bmesh.new()
    box(t, V(bx, 0, 1.445), V(0.090, 0.028, 0.170), bevel=0.012, seg=3)
    merge(liq, t, staff_frame())
    t = bmesh.new()
    box(t, V(bx, 0, 1.548), V(0.104, 0.036, 0.026), bevel=0.007, seg=2)
    box(t, V(bx, 0, 1.338), V(0.052, 0.036, 0.030), bevel=0.007, seg=2)
    for sx in (-1, 1):
        box(t, V(bx + sx * 0.0465, 0, 1.445), V(0.008, 0.034, 0.176), bevel=0.003, seg=2)
    cyl(t, V(bx, 0, 1.338), V(bx, 0, 1.30), 0.0065, 8)
    merge(plast, t, staff_frame())
    t = bmesh.new()
    tube(t, [V(bx, 0, 1.305), V(bx - 0.02, 0.03, 1.20), V(bx + 0.05, 0.06, 1.12), V(bx + 0.12, 0.05, 1.07), V(-0.032, 0.03, 1.04)],
         [0.0036] * 5, segs=6, n=40)
    merge(plast, t, staff_frame())
    return A


# ----------------------------------------------------------------------------- Primario
def scialitica():
    """Lampada scialitica come aureola: disco d'acciaio dietro la testa, anello di lampade fredde rivolto in avanti, braccio al cappuccio."""
    A = Asset('acc_scialitica')
    st, neon = A.bm('A_acciaio'), A.bm('A_luce_fredda')
    c = V(0.0, 0.0, 1.56)
    yf, yb = 0.176, 0.230                                   # faccia davanti e dietro del disco
    t = bmesh.new()
    lathe(t, [(0.0, yb), (0.300, yb), (0.312, yb - 0.010), (0.312, yf + 0.014), (0.298, yf), (0.0, yf)],
          c=c, segs=48, axis='Y', closed_top=True, closed_bottom=True)
    tube(t, [V(0, yb, 1.56), V(0, yb - 0.02, 1.50), V(0, 0.160, 1.42)], [0.014, 0.012, 0.012], segs=8, n=12)
    # dorso: mozzo con la maniglia sterile e tre nervature concentriche (da dietro la lampada non e' un piatto liscio)
    lathe(t, [(0.058, yb - 0.004), (0.058, yb + 0.030), (0.040, yb + 0.040), (0.016, yb + 0.044), (0.016, yb + 0.085),
              (0.026, yb + 0.092), (0.0, yb + 0.096)], c=c, segs=24, axis='Y', closed_top=True, closed_bottom=True)
    for R in (0.11, 0.19, 0.26):
        M.bm_torus(t, c + V(0, yb, 0), V(0, 1, 0), R, 0.005, seg=56, rseg=6)
    merge(st, t)
    t = bmesh.new()
    for i in range(12):
        a = 2 * pi * i / 12
        sphere(t, c + V(cos(a) * 0.238, yf - 0.002 - c.y, sin(a) * 0.238), 0.029, 10)
    for i in range(6):
        a = 2 * pi * i / 6 + 0.26
        sphere(t, c + V(cos(a) * 0.118, yf - 0.002 - c.y, sin(a) * 0.118), 0.029, 10)
    sphere(t, c + V(0, yf - 0.004 - c.y, 0), 0.041, 10)
    merge(neon, t)
    return A


def specchio():
    """Specchio frontale da otorino: fascia di cuoio attorno al cappuccio e disco concavo con il foro in mezzo, inclinato in su."""
    A = Asset('acc_specchio')
    leather, mir, st = A.bm('A_cuoio'), A.bm('A_specchio'), A.bm('A_acciaio')
    zb = 1.645
    cy, ax, ay, nf = M.interp_profile(M.HOOD_PROFILE, zb)
    b = ay
    n = 64
    t = bmesh.new()

    def ring_at(grow):
        """Sezione del cappuccio a quota zb, ingrandita di 'grow' (stesso superellisse del tessuto)."""
        pts = []
        for i in range(n):
            th = 2 * pi * i / n
            e = nf if cos(th) > 0 else 2.0
            pts.append(V((ax + grow) * M.spow(sin(th), e), cy - (ay + grow) * M.spow(cos(th), e), 0.0))
        return pts
    # fascia: anello piatto spesso 8 mm e alto 28 mm, subito fuori dal cappuccio
    outer, inner = ring_at(0.011), ring_at(0.003)
    vo = [[t.verts.new(V(p.x, p.y, zb + dz)) for dz in (-0.014, 0.014)] for p in outer]
    vi = [[t.verts.new(V(p.x, p.y, zb + dz)) for dz in (-0.014, 0.014)] for p in inner]
    for i in range(n):
        j = (i + 1) % n
        t.faces.new((vo[i][0], vo[j][0], vo[j][1], vo[i][1]))          # fuori
        t.faces.new((vi[i][1], vi[j][1], vi[j][0], vi[i][0]))          # dentro
        t.faces.new((vo[i][1], vo[j][1], vi[j][1], vi[i][1]))          # sopra
        t.faces.new((vo[i][0], vi[i][0], vi[j][0], vo[j][0]))          # sotto
    merge(leather, t)
    # perno e disco
    front = V(0, cy - b - 0.011, zb)
    t = bmesh.new()
    sphere(t, V(0, 0, 0), 0.014, 10)
    cyl(t, V(0, 0, 0), V(0, 0.0, 0.020), 0.0085, 8)
    merge(st, t, Matrix.Translation(front + V(0, -0.002, 0.004)) @ Matrix.Rotation(radians(70), 4, 'X'))
    t = bmesh.new()
    lathe(t, [(0.009, -0.004), (0.054, 0.002), (0.060, 0.004), (0.060, 0.011), (0.054, 0.010), (0.009, 0.004)],
          c=V(0, 0, 0), segs=40, closed_top=False, closed_bottom=False, loop=True)
    merge(mir, t, Matrix.Translation(front + V(0, -0.024, 0.026)) @ Matrix.Rotation(radians(70), 4, 'X'))
    return A


def uncino():
    """Asta d'acciaio con un divaricatore chirurgico in cima: una lama piatta che sale, gira in avanti e scende."""
    A = Asset('acc_uncino')
    st, gum = A.bm('A_acciaio'), A.bm('A_gomma')
    pole(st, 1.50)
    t = bmesh.new()
    cyl(t, V(0, 0, 0.0), V(0, 0, 0.045), 0.022, 14)
    merge(gum, t, staff_frame())
    t = bmesh.new()
    cyl(t, V(0, 0, 1.47), V(0, 0, 1.53), 0.0215, 14)
    merge(st, t, staff_frame())
    # lama: archi di cerchio nel piano y-z, scatole piatte che seguono la curva (larga di piu' verso la punta)
    R0 = 0.17
    path = [V(0, 0, 1.50)]
    for k in range(1, 15):
        th = pi - pi * k / 14
        path.append(V(0, R0 + R0 * cos(th), 1.60 + R0 * sin(th)))
    path.append(V(0, 2 * R0, 1.44))
    t = bmesh.new()
    for i in range(len(path) - 1):
        p, q = path[i], path[i + 1]
        d = q - p
        ang = math.atan2(d.z, d.y)
        w = 0.052 + 0.034 * i / (len(path) - 2)
        box(t, (p + q) / 2, V(w, d.length * 1.04, 0.008), rot=Matrix.Rotation(ang, 3, 'X'))
    merge(st, t, staff_frame())
    return A


ASSETS = {
    'acc_lanterna': lanterna, 'acc_maschera': maschera,
    'acc_diapason': diapason, 'acc_cilindro': cilindro, 'acc_farfallino': farfallino,
    'acc_flebo': flebo,
    'acc_scialitica': scialitica, 'acc_specchio': specchio, 'acc_uncino': uncino,
}

# chi indossa cosa (per le anteprime)
LOOKS = {
    'maschera': ['acc_lanterna', 'acc_maschera'],
    'direttore': ['acc_diapason', 'acc_cilindro', 'acc_farfallino'],
    'infermiere': ['acc_flebo'],
    'primario': ['acc_scialitica', 'acc_specchio', 'acc_uncino'],
}


# ----------------------------------------------------------------------------- build
def build_all():
    ga.reset()
    roots, objs = [], []
    for nm, fn in ASSETS.items():
        A = fn()
        r = A.build()
        roots.append(r)
        objs += A.objs
        print(f'[build] {nm}: {A.tris()} triangoli, mesh: {[o.name for o in A.objs]}')
    os.makedirs(EXPORT_DIR, exist_ok=True)
    ga.select_only(roots + objs, roots[0])
    bpy.ops.export_scene.fbx(filepath=os.path.join(EXPORT_DIR, 'accessori_temi.fbx'), use_selection=True,
                             object_types={'EMPTY', 'MESH'}, apply_unit_scale=True,
                             apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
                             mesh_smooth_type='EDGE', use_tspace=True, add_leaf_bones=False,
                             bake_anim=False, path_mode='RELATIVE', embed_textures=False)
    print('[build] esportato', os.path.join(EXPORT_DIR, 'accessori_temi.fbx'))


# ----------------------------------------------------------------------------- anteprime
def swap_cloth(name):
    """La stoffa del monaco prende la base del costume (se esiste), stessa UV e stessa normal map."""
    base = os.path.join(TEX_DIR, f'{name}_stoffa_base.png')
    if not os.path.exists(base):
        return
    for mat in bpy.data.materials:
        if mat.name not in ('M_stoffa', 'M_stoffa_interno') or not mat.node_tree:
            continue
        for n in mat.node_tree.nodes:
            if n.type == 'TEX_IMAGE' and n.image is not None and 'base' in n.image.name:
                im = bpy.data.images.load(base)
                im.colorspace_settings.name = 'sRGB'
                n.image = im


def render_look(name):
    bpy.ops.wm.open_mainfile(filepath=M.EXPORT_BLEND)
    ga._mats.clear()                                     # i materiali della scena precedente non esistono piu'
    sc = bpy.context.scene
    print('[render] oggetti:', [o.name for o in sc.objects])
    print('[render] materiali:', [m.name for m in bpy.data.materials])
    swap_cloth(name)
    for ob in list(sc.objects):
        if ob.name == 'legno':
            bpy.data.objects.remove(ob)
    for nm in LOOKS[name]:
        ASSETS[nm]().build()
    # anteprime su CPU: non si toglie la GPU a chi gioca
    sc.render.engine = 'CYCLES'
    sc.cycles.device = 'CPU'
    sc.cycles.samples = int(os.environ.get('TEMI_SAMPLES', '40'))
    sc.cycles.use_denoising = True
    sc.cycles.denoiser = 'OPENIMAGEDENOISE'
    sc.render.threads_mode = 'FIXED'
    sc.render.threads = int(os.environ.get('TEMI_THREADS', '8'))
    sc.render.resolution_x, sc.render.resolution_y = 700, 900
    sc.render.image_settings.file_format = 'PNG'
    sc.view_settings.view_transform = 'AgX'
    sc.view_settings.look = 'AgX - Medium High Contrast'
    w = bpy.data.worlds.new('mondo')
    sc.world = w
    w.use_nodes = True
    w.node_tree.nodes['Background'].inputs['Color'].default_value = (0.03, 0.03, 0.036, 1)
    M.build_studio()
    os.makedirs(RENDER_DIR, exist_ok=True)
    views = {'fronte': ((0.0, -3.9, 1.35), (0.0, 0.0, 1.05), 40),
             'tre_quarti': ((-2.6, -3.0, 1.45), (0.0, 0.0, 1.05), 40),
             'volto': ((-0.55, -1.35, 1.6), (0.0, -0.05, 1.55), 50)}
    pick = [a for a in ARGS if a in views] or ['tre_quarti', 'volto']
    for v in pick:
        loc, tgt, lens = views[v]
        M.add_camera(loc, tgt, lens)
        sc.render.filepath = os.path.join(RENDER_DIR, f'{name}_{v}.png')
        bpy.ops.render.render(write_still=True)
        print('[render]', sc.render.filepath)


if __name__ == '__main__':
    if 'build' in ARGS:
        build_all()
    for nm in [a for a in ARGS if a in LOOKS]:
        render_look(nm)
