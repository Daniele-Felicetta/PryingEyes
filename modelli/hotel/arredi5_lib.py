"""
Attrezzi della fase 5 degli arredi (rifacimento iperrealistico dei pezzi piu' usati): profili modanati lungo un percorso (cornici, bordi,
montanti curvi), imbottiture con corona e bottoni, tovaglia drappeggiata, ferramenta d'ottone, e il passaggio di UV che orienta la
venatura del legno lungo ogni pezzo. Si appoggia a genera_arredi.py (box, lathe, cyl, tube, rounded_grid, Asset) senza cambiarlo.

Assi come negli altri script: Z in alto, davanti = +Y, origine al centro a terra, metri.
"""
import bpy, bmesh, math, os, sys, random
from mathutils import Vector, Matrix, noise
from math import sin, cos, pi, radians, sqrt, atan2, exp

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, '..', 'chiesa'))
import genera_arredi as ga
import genera_arredi3 as g3          # merge, setup_cpu
import genera_arredi4 as g4          # ruoli del guardaroba, loop_tube, ellipse, cone
import genera_chiesa as gc           # poly_prism, gothic_pts, torus
from genera_arredi import V, box, lathe, sphere, cyl, tube, rounded_grid, smoothstep
from genera_chiesa import poly_prism, gothic_pts, torus

# ruoli nuovi: in Unity li mappa TorreBuilder (Editor) sui materiali del kit
ga.ROLE.update({
    'A_cartone': ((0.46, 0.34, 0.21), 0.0, 0.9),
    'A_carta': ((0.80, 0.77, 0.66), 0.0, 0.95),
    'A_bottiglia': ((0.03, 0.07, 0.04), 0.0, 0.08),
})


# ----------------------------------------------------------------------------- scorciatoie
def bx(bm, c, s, r=0.0, seg=2, rot=None):
    """box() col raggio dello smusso limitato a poco meno della meta' del lato piu' sottile (altrimenti il bevel si ripiega)."""
    r = min(r, 0.46 * min(s))
    return box(bm, Vector(c), Vector(s), bevel=r, seg=seg if r > 0 else 1, rot=rot)


def part(dst, builder, M=None):
    """Costruisce una sotto-struttura in un bmesh temporaneo (builder(tmp)) e la versa in dst dopo averla trasformata con M (4x4)."""
    tmp = bmesh.new()
    builder(tmp)
    g3.merge(dst, tmp, M)


def lathe_fn(bm, profile, fn, c=V(0, 0, 0), segs=32, closed_top=True, closed_bottom=True):
    """Solido di rivoluzione con il raggio modulato: fn(raggio, quota, angolo) -> raggio (pieghe di una tonaca, coste di un bacino, corda ritorta,
    pieghe a fisarmonica di un paralume). Ritorna gli anelli, cosi' chi lo usa puo' chiudere o collegare i bordi come preferisce."""
    rings = []
    for r, h in profile:
        if r < 1e-6:
            rings.append([bm.verts.new(Vector(c) + V(0, 0, h))])
            continue
        rings.append([bm.verts.new(Vector(c) + V(cos(2 * pi * j / segs) * fn(r, h, 2 * pi * j / segs), sin(2 * pi * j / segs) * fn(r, h, 2 * pi * j / segs), h))
                      for j in range(segs)])
    for i in range(len(rings) - 1):
        a, b = rings[i], rings[i + 1]
        for j in range(segs):
            k = (j + 1) % segs
            if len(a) == 1 and len(b) == 1:
                continue
            if len(a) == 1:
                bm.faces.new((a[0], b[k], b[j]))
            elif len(b) == 1:
                bm.faces.new((a[j], a[k], b[0]))
            else:
                bm.faces.new((a[j], a[k], b[k], b[j]))
    if closed_bottom and len(rings[0]) > 1:
        bm.faces.new(rings[0][::-1])
    if closed_top and len(rings[-1]) > 1:
        bm.faces.new(rings[-1])
    return rings


def ell(dst, p, size, seg=14, rot=None):
    """Ellissoide con i tre semiassi 'size', centrato in p, ruotato dalla matrice 3x3 'rot' se data."""
    t = bmesh.new()
    sphere(t, V(0, 0, 0), 1.0, seg)
    bmesh.ops.transform(t, matrix=Matrix.Diagonal((size[0], size[1], size[2], 1.0)), verts=t.verts)
    g3.merge(dst, t, Matrix.Translation(V(*p)) @ (rot.to_4x4() if rot is not None else Matrix.Identity(4)))


def spline(pts, n=6):
    """Catmull-Rom attraverso pts: n campioni per tratto, ritorna una lista di Vector."""
    pts = [Vector(p) for p in pts]
    ext = [pts[0] * 2 - pts[1]] + pts + [pts[-1] * 2 - pts[-2]]
    nseg = len(pts) - 1
    out = []
    for i in range(nseg * n + 1):
        u = i / n
        j = min(int(u), nseg - 1)
        t = u - j
        p0, p1, p2, p3 = ext[j], ext[j + 1], ext[j + 2], ext[j + 3]
        out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3))
    return out


def rr_profile(w, h, r, n=3):
    """Rettangolo smussato (a = larghezza, b = altezza) centrato nell'origine, n tratti per angolo."""
    r = max(1e-4, min(r, w / 2 - 1e-5, h / 2 - 1e-5))
    pts = []
    for cx, cy, a0 in ((w / 2 - r, h / 2 - r, 0), (-(w / 2 - r), h / 2 - r, 90), (-(w / 2 - r), -(h / 2 - r), 180), (w / 2 - r, -(h / 2 - r), 270)):
        for k in range(n + 1):
            a = radians(a0 + 90 * k / n)
            pts.append((cx + r * cos(a), cy + r * sin(a)))
    return pts


def round_profile(r, n=10):
    return [(r * cos(2 * pi * k / n), r * sin(2 * pi * k / n)) for k in range(n)]


def sweep(bm, path, profile, ref=V(0, 0, 1), closed=False, taper=None, cap=True):
    """Trascina un profilo 2D (a, b) lungo una spezzata. L'asse b del profilo guarda verso 'ref' (proiettato ortogonale alla tangente), a e'
    laterale. Sui vertici interni l'offset laterale e' allungato dell'inglete, cosi' una cornice rettangolare ha gli angoli a 45 gradi.
    taper = (s0, s1): scala il profilo da inizio a fine. Ritorna gli anelli di vertici."""
    P = [Vector(p) for p in path]
    n = len(P)
    rings = []
    for i in range(n):
        if closed:
            d0 = (P[i] - P[i - 1]).normalized()
            d1 = (P[(i + 1) % n] - P[i]).normalized()
        else:
            d0 = (P[i] - P[i - 1]).normalized() if i > 0 else (P[1] - P[0]).normalized()
            d1 = (P[i + 1] - P[i]).normalized() if i < n - 1 else d0
        T = d0 + d1
        T = T.normalized() if T.length > 1e-6 else d1
        m = 1.0 / max(0.3, T.dot(d0))
        b = Vector(ref)
        b = b - T * b.dot(T)
        if b.length < 1e-6:
            b = Vector((1, 0, 0)) - T * T.x
        b.normalize()
        a = T.cross(b).normalized()
        s = 1.0 if not taper else taper[0] + (taper[1] - taper[0]) * (i / max(1, n - 1))
        rings.append([bm.verts.new(P[i] + a * (pa * s * m) + b * (pb * s)) for pa, pb in profile])
    k = len(profile)
    for i in range(n if closed else n - 1):
        r0, r1 = rings[i], rings[(i + 1) % n]
        for j in range(k):
            bm.faces.new((r0[j], r0[(j + 1) % k], r1[(j + 1) % k], r1[j]))
    if not closed and cap:
        bm.faces.new(rings[0][::-1])
        bm.faces.new(rings[-1])
    return rings


# profili di modanatura (a = verso il lato, b = verso l'esterno), poligoni chiusi. Larghezze in metri.
def mould_bead(w=0.020, h=0.012):
    """Fascetta con tondino: la cornice piu' semplice dei pannelli."""
    pts = [(-w / 2, 0.0), (w / 2, 0.0), (w / 2, h * 0.35)]
    for k in range(1, 6):
        a = pi * k / 6
        pts.append((w / 2 - (w / 2) * (1 - cos(a)) * 0.55, h * 0.35 + h * 0.65 * sin(a) * (0.6 + 0.4 * (1 - k / 6))))
    pts += [(-w / 2, h * 0.35)]
    return pts


def mould_ogee(w=0.030, h=0.022):
    """Gola rovescia (S): cornici e cimase."""
    pts = [(-w / 2, 0.0), (w / 2, 0.0)]
    n = 8
    for k in range(n + 1):
        t = k / n
        pts.append((w / 2 - w * 0.78 * smoothstep(0.0, 1.0, t), h * (0.2 + 0.8 * t) if t < 1 else h))
    pts.append((-w / 2, h))
    return pts


def mould_cove(w=0.040, h=0.030):
    """Sguscio concavo per le cornici alte degli armadi."""
    pts = [(-w / 2, 0.0), (w / 2, 0.0), (w / 2, h * 0.1)]
    n = 8
    for k in range(1, n + 1):
        a = (pi / 2) * k / n
        pts.append((w / 2 - w * (1 - cos(a)) * 0.9, h * 0.1 + h * 0.9 * sin(a)))
    pts.append((-w / 2, h))
    return pts


def crown_profile(w=0.05, h=0.06):
    """Cimasa: a = 0 sulla parete, positivo verso l'esterno; stretta in basso, sporgente in alto (sguscio concavo e listello)."""
    pts = [(0.0, 0.0), (0.25 * w, 0.0), (0.25 * w, 0.12 * h)]
    for k in range(1, 9):
        t = k / 8
        pts.append((0.25 * w + 0.75 * w * (1 - cos(pi / 2 * t)), 0.12 * h + 0.5 * h * sin(pi / 2 * t)))
    pts += [(w, 0.82 * h), (0.86 * w, h), (0.0, h)]
    return pts


def base_profile(w=0.03, h=0.08):
    """Zoccolo modanato: a = 0 sulla parete, largo in basso e rastremato con una gola (S) verso l'alto."""
    pts = [(0.0, 0.0), (w, 0.0), (w, 0.22 * h)]
    for k in range(1, 9):
        t = k / 8
        pts.append((w - 0.72 * w * smoothstep(0.0, 1.0, t), 0.22 * h + 0.70 * h * t))
    pts += [(0.28 * w, 0.95 * h), (0.0, h)]
    return pts


def rect_path(cx, cy, z, hx, hy):
    """Rettangolo antiorario nel piano XY a quota z (per sweep chiusi: il lato esterno e' a destra della direzione di marcia)."""
    return [V(cx - hx, cy - hy, z), V(cx + hx, cy - hy, z), V(cx + hx, cy + hy, z), V(cx - hx, cy + hy, z)]


def prism_bevel(bm, pts, axis, a0, a1, r=0.004, seg=1):
    """poly_prism con gli spigoli dei due contorni smussati (le facce laterali tra punti vicini restano lisce)."""
    tmp = bmesh.new()
    poly_prism(tmp, pts, axis, a0, a1)
    bmesh.ops.recalc_face_normals(tmp, faces=tmp.faces[:])
    edges = [e for e in tmp.edges if any(len(f.verts) > 4 for f in e.link_faces)]
    try:
        bmesh.ops.bevel(tmp, geom=edges, offset=r, segments=seg, profile=0.5, affect='EDGES')
    except Exception:
        pass
    g3.merge(bm, tmp)


# ----------------------------------------------------------------------------- pannelli e cornici
def frame_rect(bm, x0, x1, z0, z1, y, prof, face=+1):
    """Cornice modanata chiusa nel piano XZ a profondita' y, sporgente verso +Y (face=+1) o -Y."""
    path = [Vector((x0, y, z0)), Vector((x1, y, z0)), Vector((x1, y, z1)), Vector((x0, y, z1))]
    prof = [(a, b * face) for a, b in prof]
    sweep(bm, path, prof, ref=V(0, face, 0), closed=True)


def panel_field(wood, x0, x1, z0, z1, y, depth, face=+1, bevel=0.004, prof=None, mould=0.020):
    """Specchio di pannello: campo rialzato smussato dentro una cornice modanata. y = piano del telaio, depth = rilievo del campo."""
    cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
    bx(wood, V(cx, y + face * depth / 2, cz), V(x1 - x0 - 2 * mould, depth, z1 - z0 - 2 * mould), bevel, 2)
    frame_rect(wood, x0 + mould / 2, x1 - mould / 2, z0 + mould / 2, z1 - mould / 2, y, prof or mould_bead(mould, depth * 1.1), face)


# ----------------------------------------------------------------------------- ferramenta
def stud(bm, c, r=0.004, axis='y', sgn=1):
    """Chiodo a testa tonda: cupoletta di 8 lati."""
    prof = [(0.0, 0.0), (r, 0.0), (r * 0.94, r * 0.38), (r * 0.62, r * 0.8), (0.0, r)]
    if axis == 'z':
        lathe(bm, prof, c=Vector(c), segs=8, axis='Z', closed_top=True, closed_bottom=False)
        return
    tmp = bmesh.new()
    lathe(tmp, prof, c=V(0, 0, 0), segs=8, axis='Z', closed_top=True, closed_bottom=False)
    rot = Matrix.Rotation(-sgn * pi / 2, 4, 'X') if axis == 'y' else Matrix.Rotation(sgn * pi / 2, 4, 'Y')
    g3.merge(bm, tmp, Matrix.Translation(Vector(c)) @ rot)


def stud_row(bm, a, b, pitch, r=0.004, axis='y', sgn=1):
    a, b = Vector(a), Vector(b)
    n = max(1, int(round((b - a).length / pitch)))
    for i in range(n + 1):
        stud(bm, a + (b - a) * (i / n), r, axis, sgn)


def knob(bm, c, r=0.016, sgn=1, axis='y'):
    """Pomello d'ottone tornito con rosetta, sporgente da una faccia verticale (axis y) di sgn."""
    prof = [(0.0, 0.0), (r * 1.25, 0.0), (r * 1.28, 0.0025), (r * 1.0, 0.004), (r * 0.30, 0.006), (r * 0.30, 0.013), (r * 0.72, 0.017), (r * 0.98, 0.026),
            (r * 0.85, 0.036), (r * 0.5, 0.041), (0.0, 0.043)]
    tmp = bmesh.new()
    lathe(tmp, prof, c=V(0, 0, 0), segs=14, axis='Z', closed_top=True, closed_bottom=False)
    rot = Matrix.Rotation(-sgn * pi / 2, 4, 'X') if axis == 'y' else Matrix.Rotation(sgn * pi / 2, 4, 'Y')
    g3.merge(bm, tmp, Matrix.Translation(Vector(c)) @ rot)


def bail_pull(brass, c, width=0.09, sgn=1):
    """Maniglia a staffa per cassetti (faccia verso +Y se sgn=1): due rosette e un archetto d'ottone."""
    cx, cy, cz = c
    for dx in (-width / 2, width / 2):
        tmp = bmesh.new()
        lathe(tmp, [(0.0, 0.0), (0.017, 0.0), (0.019, 0.0015), (0.017, 0.0035), (0.010, 0.0055), (0.0, 0.006)], c=V(0, 0, 0), segs=14, axis='Z')
        g3.merge(brass, tmp, Matrix.Translation(V(cx + dx, cy, cz)) @ Matrix.Rotation(-sgn * pi / 2, 4, 'X'))
    pts = []
    for k in range(9):
        a = pi * k / 8
        pts.append(V(cx - width / 2 + width * (1 - cos(a)) / 2, cy + sgn * (0.008 + 0.021 * sin(a)), cz))
    pts.insert(0, V(cx - width / 2, cy + sgn * 0.004, cz))
    pts.append(V(cx + width / 2, cy + sgn * 0.004, cz))
    tube(brass, pts, [0.0045] * 3, segs=7, n=18)


def keyhole_plate(brass, dark, c, sgn=1, s=1.0):
    """Mostrina a goccia con foro della chiave."""
    cx, cy, cz = c
    plate = [(0.0, 0.017), (0.011, 0.009), (0.013, -0.004), (0.008, -0.016), (-0.008, -0.016), (-0.013, -0.004), (-0.011, 0.009)]
    poly_prism(brass, [(cx + u * s, cz + v * s) for u, v in plate], 'y', cy, cy + sgn * 0.003)
    lathe(dark, [(0.0, 0.0), (0.0045 * s, 0.0), (0.0045 * s, 0.0008), (0.0, 0.0008)], c=V(cx, cy + sgn * 0.0032, cz + 0.005 * s), segs=10, axis='Y')
    poly_prism(dark, [(cx - 0.0016 * s, cz + 0.004 * s), (cx + 0.0016 * s, cz + 0.004 * s), (cx + 0.0026 * s, cz - 0.010 * s), (cx - 0.0026 * s, cz - 0.010 * s)],
               'y', cy + sgn * 0.0031, cy + sgn * 0.0036)


def hinge(brass, c, h=0.08, sgn=1):
    """Cerniera a bandelle: due alette e il perno a rullo (faccia verso +Y se sgn=1)."""
    cx, cy, cz = c
    cyl(brass, V(cx, cy, cz - h / 2), V(cx, cy, cz + h / 2), 0.0055, 10)
    for dx in (-1, 1):
        bx(brass, V(cx + dx * 0.012, cy - sgn * 0.0005, cz), V(0.020, 0.0025, h), 0.0006, 1)


# ----------------------------------------------------------------------------- tessuti e imbottiture
def cushion(bm, c, size, r, nx=14, ny=14, nz=4, crown=0.02, sag=0.0, wrinkle=0.0, seed=1):
    """Cuscino: parallelepipedo a maglia fitta arrotondato (rounded_grid) con la faccia superiore rigonfia al centro (corona), un
    affossamento facoltativo e una leggera stropicciatura. c = centro, size = (x, y, z)."""
    c = Vector(c)
    verts = rounded_grid(bm, c, size, r, nx=nx, ny=ny, nz=nz)
    top = c.z + size[2] / 2
    for v in verts:
        if v.co.z < top - r * 0.6:
            continue
        u = (v.co.x - c.x) / (size[0] / 2)
        w = (v.co.y - c.y) / (size[1] / 2)
        env = max(0.0, 1 - u * u) * max(0.0, 1 - w * w)
        dz = crown * env - sag * exp(-((u * 1.4) ** 2 + (w * 1.4) ** 2))
        if wrinkle:
            dz += wrinkle * env * noise.noise(Vector((v.co.x * 18 + seed, v.co.y * 18, seed * 2.0)))
        v.co.z += dz
    return verts


def rect_loop(cx, cy, z, hx, hy, r, n=4):
    """Contorno rettangolare con angoli tondi (raggio r) nel piano XY a quota z, per tubi chiusi (filetti, bordature)."""
    pts = []
    for (px, py, a0) in ((hx - r, hy - r, 0), (-(hx - r), hy - r, 90), (-(hx - r), -(hy - r), 180), (hx - r, -(hy - r), 270)):
        for k in range(n + 1):
            a = radians(a0 + 90 * k / n)
            pts.append(V(cx + px + r * cos(a), cy + py + r * sin(a), z))
    return pts


def piping(bm, pts, r=0.004, segs=6):
    """Filetto (cordoncino) lungo un contorno chiuso orizzontale."""
    g4.loop_tube(bm, pts, r, V(0, 0, 1), segs=segs)


def tablecloth(bm, half, z_top, drop, seed=3, thick=0.004, per=112, rows=12, folds=8, amp=0.03, corner=0.055, hem=0.035):
    """Tovaglia quadrata (lato 2*half) stesa sul piano a quota z_top: lastra superiore smussata e lembi che cadono in pieghe,
    piu' cadenti verso gli spigoli; guscio chiuso di spessore thick. L'orlo e' leggermente irregolare."""
    rnd = random.Random(seed)
    ph = [rnd.uniform(0, 2 * pi) for _ in range(4)]
    bx(bm, V(0, 0, z_top + thick / 2), V(2 * half, 2 * half, thick), 0.0035, 2)

    def base(s):
        k = int(s * 4) % 4
        t = (s * 4) - int(s * 4)
        if k == 0:
            return V(half, -half + 2 * half * t, 0), V(1, 0, 0)
        if k == 1:
            return V(half - 2 * half * t, half, 0), V(0, 1, 0)
        if k == 2:
            return V(-half, half - 2 * half * t, 0), V(-1, 0, 0)
        return V(-half + 2 * half * t, -half, 0), V(0, -1, 0)

    def surf(i, j, inner):
        s = i / per
        v = j / rows
        p, nrm = base(s)
        # pieghe: somma di sinusoidi, ampiezza crescente col cadere, smorzate vicino agli spigoli (dove cade la piega d'angolo)
        sc = abs(((s * 4) % 1.0) - 0.5) * 2          # 0 agli spigoli, 1 a meta' lato
        w = (0.60 * sin(2 * pi * folds * s + ph[0]) + 0.28 * sin(2 * pi * folds * 2.3 * s + ph[1]) + 0.12 * sin(2 * pi * folds * 5.1 * s + ph[2]))
        out = 0.003 + (v ** 1.35) * (amp * w * (0.35 + 0.65 * sc) + 0.020) + (v ** 1.6) * 0.012
        # piega d'angolo: la stoffa in eccesso si raccoglie agli spigoli, sporgendo lungo la diagonale
        k = int(round(s * 4)) % 4
        dist = abs(s * 4 - round(s * 4))
        cg = exp(-(dist / 0.075) ** 2) * (v ** 1.5) * corner
        bis = V(1 if k in (0, 3) else -1, 1 if k in (0, 1) else -1, 0).normalized()
        pos = p + nrm * out + bis * cg
        if inner:
            pos = pos - nrm * thick - bis * (cg * 0.0)
        z = z_top - v * drop * (1.0 + hem * sin(2 * pi * (s * 3 + ph[3]) / 1.0)) + (0.0 if inner else 0.0) * thick
        return V(pos.x, pos.y, z)

    outer = [[bm.verts.new(surf(i, j, False)) for j in range(rows + 1)] for i in range(per)]
    inner = [[bm.verts.new(surf(i, j, True)) for j in range(rows + 1)] for i in range(per)]
    for i in range(per):
        i2 = (i + 1) % per
        for j in range(rows):
            bm.faces.new((outer[i][j], outer[i2][j], outer[i2][j + 1], outer[i][j + 1]))
            bm.faces.new((inner[i][j], inner[i][j + 1], inner[i2][j + 1], inner[i2][j]))
        bm.faces.new((outer[i][0], inner[i][0], inner[i2][0], outer[i2][0]))                        # bordo alto
        bm.faces.new((outer[i][rows], outer[i2][rows], inner[i2][rows], inner[i][rows]))           # orlo in basso


# ----------------------------------------------------------------------------- UV con la venatura lungo il pezzo
def _islands(bm):
    parent = {}

    def find(v):
        while parent.setdefault(v, v) != v:
            parent[v] = parent[parent[v]]
            v = parent[v]
        return v
    for f in bm.faces:
        vs = [find(v) for v in f.verts]
        for a in vs[1:]:
            ra, rb = find(vs[0]), find(a)
            if ra != rb:
                parent[ra] = rb
    groups = {}
    for f in bm.faces:
        groups.setdefault(find(f.verts[0]), []).append(f)
    return list(groups.values())


def grain_uv(bm):
    """UV in metri con proiezione a scatola, ma ruotate di 90 gradi dove serve perche' la venatura (asse U delle texture del legno) corra
    lungo il pezzo: per ogni isola connessa di facce l'asse piu' lungo del suo ingombro; se l'isola e' tozza (cornici, anelli) l'asse del lato piu'
    lungo di ogni faccia."""
    uvl = bm.loops.layers.uv.verify()
    for isl in _islands(bm):
        lo = [1e9] * 3
        hi = [-1e9] * 3
        for f in isl:
            for v in f.verts:
                for i in range(3):
                    lo[i] = min(lo[i], v.co[i])
                    hi[i] = max(hi[i], v.co[i])
        ext = [hi[i] - lo[i] for i in range(3)]
        order = sorted(range(3), key=lambda i: -ext[i])
        island_axis = order[0] if ext[order[0]] > 1.35 * ext[order[1]] else None
        for f in isl:
            f.normal_update()
            n = f.normal
            na = max(range(3), key=lambda i: abs(n[i]))
            p, q = [i for i in range(3) if i != na]
            if island_axis is not None:
                g = island_axis
            else:
                e = max(f.edges, key=lambda ed: ed.calc_length())
                d = e.verts[1].co - e.verts[0].co
                g = max(range(3), key=lambda i: abs(d[i]))
            for lo_ in f.loops:
                co = lo_.vert.co
                if g == q:
                    uv = (co[q], -co[p])
                else:
                    uv = (co[p], co[q])
                lo_[uvl].uv = uv


class Asset5(ga.Asset):
    """Un arredo come Asset, con le UV orientate sulla venatura e un solo passaggio di normali. Le mesh restano una per ruolo di materiale
    (nomi uguali ai vecchi: <arredo>_<ruolo>), cosi' Unity ritrova le stesse parti.

    Livelli di dettaglio: con WITH_LODS ogni mesh ha anche due copie decimate, <arredo>_LOD1_<ruolo> e <arredo>_LOD2_<ruolo>. Il nome non
    finisce per _LODn, quindi Unity non le raggruppa da solo: lo fa TorreBuilder.MakePropPrefab (LODGroup). SHOW_LOD = 1 o 2 tiene solo quel
    livello (per guardare le anteprime delle versioni lontane)."""
    OPEN = {'A_foglia'}
    LODS = ((1, 0.28), (2, 0.09))          # (livello, quota dei triangoli del livello 0)
    LOD_MIN_TRIS = 60                      # sotto questa soglia una mesh non ha bisogno di versioni piu' povere
    KEEP_LOD = frozenset()                 # ruoli che devono restare visibili a ogni livello anche se piccoli (l'acqua di una fonte): copiati interi
    WITH_LODS = False
    SHOW_LOD = 0

    def _decimated(self, root, ob, role, level, ratio):
        name = f'{self.name}_LOD{level}_{role[2:]}'
        tmp = bpy.data.objects.new(name + '_tmp', ob.data.copy())
        bpy.context.scene.collection.objects.link(tmp)
        md = tmp.modifiers.new('lod', 'DECIMATE')
        md.decimate_type = 'COLLAPSE'
        md.ratio = ratio
        md.use_collapse_triangulate = True
        me = bpy.data.meshes.new_from_object(tmp.evaluated_get(bpy.context.evaluated_depsgraph_get()))
        bpy.data.objects.remove(tmp)
        if not me.polygons:
            return None
        me.name = name
        me.materials.clear()
        me.materials.append(ga.material(role))
        out = bpy.data.objects.new(name, me)
        out.parent = root
        bpy.context.scene.collection.objects.link(out)
        return out

    def build(self):
        root = bpy.data.objects.new(self.name, None)
        bpy.context.scene.collection.objects.link(root)
        self.objs = []
        for role, bm in self.bms.items():
            if not bm.faces:
                bm.free()
                continue
            ngons = [f for f in bm.faces if len(f.verts) > 4]
            if ngons:
                bmesh.ops.triangulate(bm, faces=ngons)          # senza poligoni a piu' di 4 lati Blender esporta le tangenti
            if role not in self.OPEN:
                bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
            grain_uv(bm)
            ga.sharpen(bm)
            me = bpy.data.meshes.new(f'{self.name}_{role[2:]}')
            bm.to_mesh(me)
            bm.free()
            ob = bpy.data.objects.new(f'{self.name}_{role[2:]}', me)
            ob.parent = root
            me.materials.append(ga.material(role))
            bpy.context.scene.collection.objects.link(ob)
            lods = []
            if self.WITH_LODS or self.SHOW_LOD:
                tris = sum(len(p.vertices) - 2 for p in me.polygons)
                for level, ratio in self.LODS:
                    if tris >= self.LOD_MIN_TRIS or role in self.KEEP_LOD:
                        d = self._decimated(root, ob, role, level, ratio if tris >= self.LOD_MIN_TRIS else 1.0)
                        if d is not None:
                            lods.append((level, d))
            if self.SHOW_LOD:
                keep = [d for level, d in lods if level == self.SHOW_LOD]
                bpy.data.objects.remove(ob)
                self.objs.extend(keep)
                for level, d in lods:
                    if level != self.SHOW_LOD:
                        bpy.data.objects.remove(d)
                continue
            self.objs.append(ob)
            self.objs.extend(d for level, d in lods)
        self.root = root
        if self.WITH_LODS:
            print(f'[lod] {self.name}: livello 0 {self.tris()}, livello 1 {self.tris_lod(1)}, livello 2 {self.tris_lod(2)} triangoli')
        return root

    def tris(self):
        """Triangoli del livello 0 (le versioni decimate non contano)."""
        return sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in self.objs if '_LOD' not in o.name)

    def tris_lod(self, level):
        return sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in self.objs if f'_LOD{level}_' in o.name)


# ----------------------------------------------------------------------------- materiali delle anteprime con le texture vere del gioco
TEXDIR = os.path.normpath(os.path.join(HERE, '..', '..', 'unity', 'PryingEyes', 'Assets', '_PryingEyes', 'Textures', 'Hotel'))
PREVIEW_TEX = {'A_noce': 'noce', 'A_legno': 'noce', 'A_cuoio': 'cuoio', 'A_ottone': 'ottone', 'A_acciaio': 'acciaio', 'A_velluto': 'velluto',
               'A_tessuto': 'velluto', 'A_lenzuolo': 'velluto'}
_prev_mat = ga.material


def _material5(role):
    fresh = not (role in ga._mats and ga._mats[role].name in bpy.data.materials)
    m = _prev_mat(role)
    name = PREVIEW_TEX.get(role)
    if fresh and name and os.environ.get('ARREDI_FLAT') != '1':
        base = os.path.join(TEXDIR, name + '_base.png')
        nrm = os.path.join(TEXDIR, name + '_normal.png')
        if os.path.exists(base):
            nt = m.node_tree
            bs = nt.nodes['Principled BSDF']
            tint = tuple(bs.inputs['Base Color'].default_value)[:3]
            img = nt.nodes.new('ShaderNodeTexImage')
            img.image = bpy.data.images.load(base)
            if role in ('A_velluto', 'A_tessuto', 'A_lenzuolo'):
                mix = nt.nodes.new('ShaderNodeMix')
                mix.data_type = 'RGBA'
                mix.blend_type = 'MULTIPLY'
                mix.inputs[0].default_value = 1.0
                mix.inputs[7].default_value = (*tint, 1)
                nt.links.new(img.outputs['Color'], mix.inputs[6])
                nt.links.new(mix.outputs[2], bs.inputs['Base Color'])
            else:
                nt.links.new(img.outputs['Color'], bs.inputs['Base Color'])
            if os.path.exists(nrm):
                nimg = nt.nodes.new('ShaderNodeTexImage')
                nimg.image = bpy.data.images.load(nrm)
                nimg.image.colorspace_settings.name = 'Non-Color'
                nm = nt.nodes.new('ShaderNodeNormalMap')
                nm.inputs['Strength'].default_value = 0.6
                nt.links.new(nimg.outputs['Color'], nm.inputs['Color'])
                nt.links.new(nm.outputs['Normal'], bs.inputs['Normal'])
    return m


ga.material = _material5
