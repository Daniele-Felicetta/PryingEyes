"""
Arredi del Guardaroba (fase 4): il castello del conte. Armadio gotico a due ante (chiuso e aperto, con i mantelli appesi), forbici giganti
da sala su un plinto, bara a coperchio spostato e bara incatenata; poi il resto del set (sartoria, forgia, terrazza). Stessi attrezzi e
stessa struttura di genera_arredi3.py (un Asset = una mesh per ruolo di materiale, UV in metri), piu' i GRUPPI: un pezzo che si muove (le
ante, il coperchio) e' un solo oggetto con un materiale per ruolo e l'origine sul perno, cosi' in Unity basta ruotarlo.

Uso (dalla cartella del file):
  blender -b -P genera_arredi4.py -- build [nome ...]    FBX in arredi4/<nome>.fbx (poi da copiare in Assets/_PryingEyes/Models/Arredi)
  blender -b -P genera_arredi4.py -- render [nome ...]   anteprime in arredi4/render/<nome>.png (su CPU: non toglie la GPU a chi gioca;
                                                         ARREDI_GPU=1 per usare la GPU)
Assi: Z in alto, davanti = +Y (Unity +Z), origine al centro a terra. Misure uguali a PropDefs.cs.
"""
import bpy, bmesh, math, os, sys, random
from mathutils import Vector, Matrix
from math import sin, cos, pi, radians, sqrt, atan2

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, '..', 'chiesa'))
import genera_arredi as ga
import genera_arredi3 as g3             # ruoli e materiali dei temi gia' fatti; merge, rotx
from genera_arredi import V, box, rounded_grid, lathe, sphere, cyl, tube, smoothstep
from genera_chiesa import poly_prism, gothic_pts, arch_slab, torus, flame

OUT = os.path.join(HERE, 'arredi4')

# ruoli nuovi per le anteprime (in Unity li sostituisce TorreBuilder coi materiali del kit)
ga.ROLE.update({
    'A_brace': ((1.0, 0.35, 0.06), 0.0, 0.6),          # carbone ardente
    'A_fuoco': ((1.0, 0.40, 0.08), 0.0, 0.6),          # lingue di fiamma grandi (braciere, forgia): piu' arancioni di A_fiamma, che a grandezza naturale esce bianca
    'A_seta': ((0.20, 0.05, 0.27), 0.0, 0.35),         # seta viola dell'interno delle bare
    'A_panno': ((0.045, 0.040, 0.048), 0.0, 0.9),      # panno nero: mantelli e cappotti
    'A_basalto': ((0.22, 0.23, 0.27), 0.0, 0.65),      # pietra grigio-blu del castello (plinti, gargoyle, urne): piu' fredda dell'arenaria della chiesa
})
_prev_material = ga.material


def _material(role):
    fresh = not (role in ga._mats and ga._mats[role].name in bpy.data.materials)
    m = _prev_material(role)
    if fresh and role in ('A_brace', 'A_fuoco'):
        bs = m.node_tree.nodes['Principled BSDF']
        bs.inputs['Emission Color'].default_value = (1.0, 0.30, 0.05, 1) if role == 'A_brace' else (1.0, 0.38, 0.07, 1)
        bs.inputs['Emission Strength'].default_value = 4.0 if role == 'A_brace' else 3.0
    return m


ga.material = _material


# ----------------------------------------------------------------------------- attrezzi
class Asset4(ga.Asset):
    """Come Asset, e in piu' i gruppi: bm(ruolo, gruppo) mette la geometria in un oggetto a parte (con piu' materiali, uno per ruolo) la cui
    origine e' il perno dato da pivot(gruppo, punto). Chi non ha gruppo resta una mesh per ruolo."""
    SURFACES = {'A_foglia'}         # superfici aperte: le normali restano come costruite

    def __init__(self, name):
        super().__init__(name)
        self.pivots = {}

    def bm(self, role, group=None):
        key = role if group is None else f'{role}@{group}'
        if key not in self.bms:
            self.bms[key] = bmesh.new()
        return self.bms[key]

    def pivot(self, group, p):
        self.pivots[group] = Vector(p)

    def build(self):
        root = bpy.data.objects.new(self.name, None)
        bpy.context.scene.collection.objects.link(root)
        self.objs = []
        groups = {}
        for key, bm in self.bms.items():
            role, _, group = key.partition('@')
            if not bm.faces:                  # un ruolo preso e mai usato (l'armadio chiuso senza mantelli): niente oggetto vuoto
                bm.free()
                continue
            groups.setdefault(group, []).append((role, bm))
        for group, parts in groups.items():
            if group == '':
                for role, bm in parts:
                    self._emit(root, f'{self.name}_{role[2:]}', [(role, bm)], Vector((0, 0, 0)))
            else:
                self._emit(root, f'{self.name}_{group}', parts, self.pivots.get(group, Vector((0, 0, 0))))
        self.root = root
        return root

    def _emit(self, root, name, parts, pivot):
        dst = bmesh.new()
        roles = []
        for role, src in parts:
            if role not in self.SURFACES:
                bmesh.ops.recalc_face_normals(src, faces=src.faces[:])
            idx = len(roles)
            roles.append(role)
            mp = {v: dst.verts.new(v.co - pivot) for v in src.verts}
            for f in src.faces:
                nf = dst.faces.new([mp[v] for v in f.verts])
                nf.material_index = idx
            src.free()
        ga.box_uv(dst, recalc=False)
        ga.sharpen(dst)
        me = bpy.data.meshes.new(name)
        dst.to_mesh(me)
        dst.free()
        ob = bpy.data.objects.new(name, me)
        ob.parent = root
        ob.location = pivot
        for role in roles:
            me.materials.append(ga.material(role))
        bpy.context.scene.collection.objects.link(ob)
        self.objs.append(ob)


def ellipse(c, u, v, a, b, n):
    """n punti di un'ellisse di centro c nel piano dei versori u, v (semiassi a, b)."""
    c, u, v = Vector(c), Vector(u), Vector(v)
    return [c + u * (a * cos(2 * pi * i / n)) + v * (b * sin(2 * pi * i / n)) for i in range(n)]


def loop_tube(bm, pts, r, w_axis, segs=8, r_in=None):
    """Tubo chiuso lungo una curva piana (punti in ordine) la cui normale di piano e' w_axis; sezione tonda di raggio r, o ovale (r_in nel
    piano, r fuori dal piano: un nastro)."""
    w_axis = Vector(w_axis).normalized()
    n = len(pts)
    rows = []
    for i, p in enumerate(pts):
        t = (pts[(i + 1) % n] - pts[(i - 1) % n]).normalized()
        nn = t.cross(w_axis).normalized()
        rin = r_in if r_in else r
        rows.append([bm.verts.new(p + nn * (cos(2 * pi * j / segs) * rin) + w_axis * (sin(2 * pi * j / segs) * r)) for j in range(segs)])
    for i in range(n):
        for j in range(segs):
            bm.faces.new((rows[i][j], rows[i][(j + 1) % segs], rows[(i + 1) % n][(j + 1) % segs], rows[(i + 1) % n][j]))


def loft(bm, secs, cap0=True, cap1=True):
    """Solido a sezioni: ogni sezione e' una lista di Vector con lo stesso numero di punti (contorno chiuso, stesso ordine)."""
    rows = [[bm.verts.new(p) for p in s] for s in secs]
    n = len(secs[0])
    for i in range(len(rows) - 1):
        for j in range(n):
            bm.faces.new((rows[i][j], rows[i][(j + 1) % n], rows[i + 1][(j + 1) % n], rows[i + 1][j]))
    if cap0:
        bm.faces.new(rows[0][::-1])
    if cap1:
        bm.faces.new(rows[-1])
    return rows


def rot_about(bms, p, angle, axis='Z'):
    """Ruota tutte le geometrie 'bms' di 'angle' radianti attorno all'asse verticale (o X/Y) passante per p."""
    p = Vector(p)
    M = Matrix.Translation(p) @ Matrix.Rotation(angle, 4, axis) @ Matrix.Translation(-p)
    for bm in bms:
        bmesh.ops.transform(bm, matrix=M, verts=bm.verts[:])


def drape(bm, cx, y, ztop, w, h, rnd, amp=0.03, folds=7.0, nx=9, nz=11):
    """Un mantello appeso: lastra sottile chiusa con pieghe verticali che si allargano verso il fondo, e la gruccia non c'e': la mette chi lo usa."""
    verts = rounded_grid(bm, V(cx, y, ztop - h / 2), V(w, 0.026, h), 0.008, nx=nx, ny=2, nz=nz)
    ph = rnd.uniform(0, 2 * pi)
    for v in verts:
        t = (ztop - v.co.z) / h
        v.co.y += amp * sin((v.co.x - cx) / w * folds + ph) * smoothstep(0.0, 0.5, t)
        v.co.x += (v.co.x - cx) * 0.20 * t


def cloak(bm, a, ztop, h, w_top, w_bot, r_top, r_bot, rnd, amp=0.025, folds=6.0, th=0.016, nx=12, nz=10):
    """Un mantello appeso a un gancio: lastra sottile chiusa che avvolge un palo verticale (arco attorno all'asse Z, centrato sulla direzione a) e
    si allarga e si scosta dal palo verso il basso, con pieghe verticali. ztop = orlo alto, h = lunghezza."""
    verts = rounded_grid(bm, V(0, 0, -h / 2), V(2.0, th, h), th * 0.4, nx=nx, ny=2, nz=nz)
    ph = rnd.uniform(0, 2 * pi)
    for v in verts:
        u = v.co.x
        t = min(1.0, max(0.0, -v.co.z / h))
        r = r_top + (r_bot - r_top) * t
        half = w_top / 2 + (w_bot / 2 - w_top / 2) * t
        rr = r + v.co.y + amp * sin(u * folds + ph) * smoothstep(0.0, 0.4, t)
        ang = a + u * half / r
        v.co = V(cos(ang) * rr, sin(ang) * rr, ztop + v.co.z)


def arch_ring(bm, cx, zo, wo, ho, zi, wi, hi, y0, y1):
    """Cornice a sesto acuto: anello tra due contorni gotici (stesso numero di punti), spesso da y0 a y1; dentro resta il vuoto per il vetro."""
    po, pin = gothic_pts(cx, zo, wo, ho), gothic_pts(cx, zi, wi, hi)
    n = len(po)
    fo0 = [bm.verts.new(Vector((u, y0, v))) for u, v in po]
    fo1 = [bm.verts.new(Vector((u, y1, v))) for u, v in po]
    fi0 = [bm.verts.new(Vector((u, y0, v))) for u, v in pin]
    fi1 = [bm.verts.new(Vector((u, y1, v))) for u, v in pin]
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((fo1[i], fo1[j], fi1[j], fi1[i]))
        bm.faces.new((fo0[j], fo0[i], fi0[i], fi0[j]))
        bm.faces.new((fo0[i], fo0[j], fo1[j], fo1[i]))
        bm.faces.new((fi0[j], fi0[i], fi1[i], fi1[j]))


def cone(bm, a, b, r, segs=6, r2=0.0):
    """Cono (o tronco di cono) dalla base a, di raggio r, alla punta b, di raggio r2."""
    a, b = Vector(a), Vector(b)
    d = b - a
    q = d.to_track_quat('Z', 'Y')
    M = Matrix.Translation((a + b) / 2) @ q.to_matrix().to_4x4()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segs, radius1=r, radius2=r2, depth=d.length, matrix=M)


def ellipsoid(bm, c, r, seg=12, rot=None):
    """Ellissoide di semiassi r = (rx, ry, rz) centrato in c; rot: matrice 3x3 facoltativa (si applica dopo la scala)."""
    tmp = bmesh.new()
    bmesh.ops.create_uvsphere(tmp, u_segments=seg, v_segments=max(6, seg // 2), radius=1.0)
    M = Matrix.Translation(Vector(c)) @ (rot.to_4x4() if rot is not None else Matrix.Identity(4)) @ Matrix.Diagonal((r[0], r[1], r[2], 1.0))
    g3.merge(bm, tmp, M)


BAT = [(0.000, 0.020), (0.007, 0.030), (0.012, 0.018), (0.030, 0.024), (0.056, 0.036), (0.082, 0.026), (0.070, 0.010), (0.074, -0.006),
       (0.056, -0.002), (0.046, -0.018), (0.032, -0.006), (0.020, -0.014), (0.010, -0.030), (0.000, -0.020)]


def bat(bm, c, s=1.0, thick=0.006, flat=False):
    """Il pipistrello: sagoma piatta con le ali a festoni, larga circa 0.17 m a s = 1. Di fronte (piano XZ, spessore lungo +Y) o, con flat, sdraiato
    (piano XY, spessore lungo +Z). c e' il centro della faccia posteriore."""
    pts = [(x * s, z * s) for x, z in BAT] + [(-x * s, z * s) for x, z in reversed(BAT[1:-1])]
    if flat:
        poly_prism(bm, [(c[0] + u, c[1] + v) for u, v in pts], 'z', c[2], c[2] + thick)
    else:
        poly_prism(bm, [(c[0] + u, c[2] + v) for u, v in pts], 'y', c[1], c[1] + thick)


def chain(bm, a, b, r=0.012, wire=0.0035, pitch=None):
    """Catena di maglie ovali, alternate a 90 gradi, tra a e b."""
    a, b = Vector(a), Vector(b)
    d = b - a
    L = d.length
    t = d / L
    pitch = pitch or r * 1.55
    n = max(2, int(L / pitch))
    up = Vector((0, 0, 1)) if abs(t.z) < 0.9 else Vector((1, 0, 0))
    s1 = t.cross(up).normalized()
    s2 = t.cross(s1).normalized()
    for i in range(n):
        p = a + t * (L * (i + 0.5) / n)
        u, w = (s1, s2) if i % 2 == 0 else (s2, s1)
        loop_tube(bm, ellipse(p, t, u, pitch * 0.62, r, 10), wire, w, segs=5)


def spoked_wheel(bm, c, R, u, v, n_spokes=6, rim=0.012, rim_w=0.018, hub=0.03):
    """Ruota a raggi nel piano dei versori u, v (normale w = u x v): cerchione piatto, raggi e mozzo."""
    c, u, v = Vector(c), Vector(u), Vector(v)
    w = u.cross(v).normalized()
    loop_tube(bm, ellipse(c, u, v, R, R, 40), rim, w, segs=8, r_in=rim_w)
    for k in range(n_spokes):
        a = 2 * pi * k / n_spokes
        cyl(bm, c, c + (u * cos(a) + v * sin(a)) * (R - rim_w), rim * 0.45, 6)
    cyl(bm, c - w * 0.022, c + w * 0.022, hub, 12)


def cloth_sheet(bm, c, w, d, th, rnd, amp=0.02, freq=5.0, nx=10, ny=8):
    """Un telo steso (lastra sottile chiusa) con onde dolci, centro c, w lungo X, d lungo Y."""
    verts = rounded_grid(bm, V(*c), V(w, d, th), th * 0.4, nx=nx, ny=ny, nz=1)
    ph = rnd.uniform(0, 2 * pi)
    for v in verts:
        v.co.z += amp * sin((v.co.x - c[0]) / w * freq + ph) * cos((v.co.y - c[1]) / d * 3.2 + ph * 0.5)


def scissors(gold, steel, k=1.0, base=0.0, spread=7.0, wide=1.0, lod=1.0):
    """Forbici da sarto in piedi sugli anelli, alte circa 1.85 k + base: anelli e gambi dorati, lame d'acciaio a cuneo appena aperte (filo
    verso il centro) con un filetto d'oro, perno a vite. 'wide' ingrossa lame e anelli. Il punto piu' basso e' a quota 'base'."""
    PZ = base + 0.775 * k
    t = radians(spread)
    L = 1.10 * k
    T = 0.0065 * k * wide
    for sx in (-1, 1):
        s = -sx                                     # la lama va dal lato opposto all'anello
        yo = -sx * 0.0125 * k * wide                # una meta' davanti, l'altra dietro
        C = V(sx * 0.175 * k, yo, base + 0.212 * k)
        loop_tube(gold, ellipse(C, V(1, 0, 0), V(0, 0, 1), 0.125 * k, 0.20 * k, max(14, int(44 * lod))), 0.0125 * k * wide, V(0, 1, 0),
                  segs=max(5, int(10 * lod)), r_in=0.022 * k * wide)
        loop_tube(gold, ellipse(C, V(1, 0, 0), V(0, 0, 1), 0.085 * k, 0.158 * k, max(12, int(36 * lod))), 0.0045 * k, V(0, 1, 0),
                  segs=max(4, int(6 * lod)))
        shank = [V(sx * 0.087 * k, yo, base + 0.353 * k), V(sx * 0.075 * k, yo, base + 0.455 * k), V(sx * 0.050 * k, yo, base + 0.575 * k),
                 V(sx * 0.022 * k, yo, base + 0.685 * k), V(0.0, yo, PZ)]
        tube(gold, shank, [0.017 * k, 0.016 * k, 0.014 * k, 0.013 * k, 0.012 * k], segs=max(5, int(8 * lod)), n=max(8, int(24 * lod)))
        sphere(gold, V(sx * 0.081 * k, yo, base + 0.395 * k), 0.020 * k, max(6, int(10 * lod)))
        d = V(s * sin(t), 0, cos(t))
        p = V(s * cos(t), 0, -sin(t))
        stations = [(0.0, 0.070), (0.05, 0.088), (0.15, 0.092), (0.40, 0.082), (0.70, 0.062), (0.90, 0.034), (1.0, 0.004)]
        secs = []
        for st, w in stations:
            w = w * k * wide
            P = V(0, yo, PZ) + d * (L * st) - p * 0.03 * k
            tt = T * (0.35 + 0.65 * min(1.0, w / (0.05 * k * wide)))
            sec = [(0.0, 0.0), (0.45 * w, tt), (w, tt), (w, -tt), (0.45 * w, -tt)]
            secs.append([P + p * xl + V(0, yl, 0) for xl, yl in sec])
        loft(steel, secs)
        strip = []
        for st, w in stations[1:-2]:
            w = w * k * wide
            P = V(0, yo, PZ) + d * (L * st) - p * 0.03 * k + p * (w * 0.80) + V(0, T + 0.0008 * k, 0)
            e = 0.004 * k
            strip.append([P - p * e, P + p * e, P + p * e + V(0, 0.0022 * k, 0), P - p * e + V(0, 0.0022 * k, 0)])
        loft(gold, strip)
    yf = 0.0125 * k * wide + T
    lathe(gold, [(0.0, 0.0), (0.034 * k, 0.0), (0.034 * k, 0.008 * k), (0.028 * k, 0.013 * k), (0.0, 0.015 * k)], axis='Y', c=V(0, yf, PZ),
          segs=max(8, int(18 * lod)))
    lathe(gold, [(0.0, 0.0), (0.030 * k, 0.0), (0.030 * k, -0.008 * k), (0.024 * k, -0.012 * k), (0.0, -0.013 * k)], axis='Y', c=V(0, -yf, PZ),
          segs=max(6, int(8 * lod)))


# ============================================================================= ARMADIO
def armadio(name='gu_armadio', aperto=False):
    """L'armadio del conte: noce scuro con coronamento di arcatelle cieche e pinnacoli, due colonnine d'angolo, due ante a cornice con l'arco
    gotico in rilievo (cerniere d'ottone, maniglia a barra), un cassetto con anelli. Le ante sono gruppi (anta_sx, anta_dx) con l'origine
    sulla cerniera. 'aperto': le ante spalancate e dentro i mantelli neri appesi alla sbarra."""
    A = Asset4(name)
    W, D = 1.40, 0.66
    hw, hd = W / 2, D / 2
    wood, dark, br, pan = A.bm('A_noce'), A.bm('A_legno'), A.bm('A_ottone'), A.bm('A_panno')
    rnd = random.Random(7)
    # piedi a cipolla, zoccolo e modanatura
    for sx in (-1, 1):
        for sy in (-1, 1):
            lathe(dark, [(0.0, 0.0), (0.034, 0.0), (0.052, 0.022), (0.052, 0.046), (0.034, 0.070), (0.0, 0.074)], c=V(sx * (hw - 0.07), sy * (hd - 0.07), 0.0), segs=12)
    box(dark, V(0, 0, 0.115), V(W + 0.04, D + 0.03, 0.09), bevel=0.012, seg=2)
    box(wood, V(0, 0, 0.185), V(W + 0.012, D + 0.004, 0.05), bevel=0.008, seg=2)
    # cassa: fiancate, schiena, piano alto, fondo
    zb0, zb1 = 0.21, 2.27
    zc, zh = (zb0 + zb1) / 2, zb1 - zb0
    for sx in (-1, 1):
        box(wood, V(sx * (hw - 0.022), 0, zc), V(0.044, D - 0.004, zh), bevel=0.004, seg=1)
    box(dark, V(0, -hd + 0.02, zc), V(W - 0.08, 0.03, zh))
    box(wood, V(0, 0, zb1), V(W + 0.012, D + 0.004, 0.05), bevel=0.006, seg=2)
    box(wood, V(0, 0, 0.53), V(W - 0.08, D - 0.06, 0.03))
    box(dark, V(0, hd - 0.03, 0.50), V(W - 0.08, 0.04, 0.06))
    # cassetto sotto le ante: pannello, due specchiature scure, due anelli di ottone
    box(wood, V(0, hd - 0.004, 0.365), V(W - 0.07, 0.03, 0.265), bevel=0.004, seg=1)
    for sx in (-1, 1):
        box(dark, V(sx * 0.30, hd + 0.012, 0.365), V(0.50, 0.006, 0.17))
        loop_tube(br, ellipse(V(sx * 0.30, hd + 0.040, 0.355), V(1, 0, 0), V(0, 0, 1), 0.022, 0.022, 14), 0.0045, V(0, 1, 0), segs=6)
        sphere(br, V(sx * 0.30, hd + 0.022, 0.377), 0.011, 8)
    box(dark, V(0, hd + 0.001, 0.545), V(W - 0.08, 0.026, 0.022), bevel=0.003, seg=1)
    # le ante
    zlo, zhi = 0.555, 2.245
    dw = hw - 0.024
    yf = hd + 0.012
    th = 0.034

    def door(sgn, group, theta):
        xh = sgn * (hw - 0.02)
        hinge = V(xh, hd - 0.004, 0.0)
        w_, d_, b_ = A.bm('A_noce', group), A.bm('A_legno', group), A.bm('A_ottone', group)

        def X(u):
            return xh - sgn * u

        def part(bm, u0, u1, z0, z1, y0, y1, bev=0.004):
            box(bm, V(X((u0 + u1) / 2), (y0 + y1) / 2, (z0 + z1) / 2), V(u1 - u0, y1 - y0, z1 - z0), bevel=bev, seg=1)
        sw_out, sw_in, rt, rb = 0.080, 0.062, 0.100, 0.135
        part(w_, 0, sw_out, zlo, zhi, yf - th, yf)
        part(w_, dw - sw_in, dw, zlo, zhi, yf - th, yf)
        part(w_, sw_out, dw - sw_in, zhi - rt, zhi, yf - th, yf)
        part(w_, sw_out, dw - sw_in, zlo, zlo + rb, yf - th, yf)
        pu0, pu1 = sw_out, dw - sw_in
        pz0, pz1 = zlo + rb, zhi - rt
        cxp = X((pu0 + pu1) / 2)
        box(d_, V(cxp, yf - th / 2 - 0.004, (pz0 + pz1) / 2), V(pu1 - pu0 + 0.004, 0.016, pz1 - pz0 + 0.004))
        aw, ah = (pu1 - pu0) - 0.05, (pz1 - pz0) - 0.05
        arch_slab(w_, cxp, pz0 + 0.025, aw, ah, yf - 0.022, yf - 0.004)
        arch_slab(d_, cxp, pz0 + 0.060, aw - 0.07, ah - 0.075, yf - 0.012, yf - 0.002)
        bat(b_, V(cxp, yf - 0.012, pz1 - 0.21), s=1.35, thick=0.014)          # il pipistrello d'ottone nell'arco
        for zz in (0.80, 1.40, 2.00):
            cyl(b_, V(xh, yf - 0.012, zz - 0.045), V(xh, yf - 0.012, zz + 0.045), 0.0105, 10)
            box(b_, V(X(0.035), yf + 0.001, zz), V(0.07, 0.004, 0.085), bevel=0.001, seg=1)
        ux = dw - sw_in / 2
        zh_ = 1.30
        for dz in (-0.12, 0.12):
            cyl(b_, V(X(ux), yf + 0.004, zh_ + dz), V(X(ux), yf + 0.040, zh_ + dz), 0.0075, 8)
        cyl(b_, V(X(ux), yf + 0.040, zh_ - 0.15), V(X(ux), yf + 0.040, zh_ + 0.15), 0.0105, 10)
        for dz in (-0.15, 0.15):
            sphere(b_, V(X(ux), yf + 0.040, zh_ + dz), 0.0165, 8)
        if sgn > 0:
            lathe(b_, [(0.0, 0.0), (0.020, 0.0), (0.020, 0.004), (0.0, 0.005)], axis='Y', c=V(X(ux), yf + 0.001, 1.02), segs=12)
            box(d_, V(X(ux), yf + 0.005, 1.025), V(0.007, 0.002, 0.020))
        rot_about((w_, d_, b_), hinge, -sgn * theta)
        A.pivot(group, hinge)
    door(-1, 'anta_sx', radians(104) if aperto else 0.0)
    door(+1, 'anta_dx', radians(46) if aperto else 0.0)
    if not aperto:
        box(dark, V(0, hd - 0.03, 1.40), V(0.04, 0.02, zhi - zlo))       # la fessura tra le ante non mostra il vuoto
    # colonnine d'angolo
    for sx in (-1, 1):
        lathe(dark, [(0.0, 0.0), (0.038, 0.0), (0.038, 0.03), (0.026, 0.055), (0.020, 0.09), (0.020, 1.93), (0.027, 1.97), (0.040, 2.0), (0.040, 2.05), (0.0, 2.06)],
              c=V(sx * (hw - 0.012), hd - 0.005, 0.21), segs=14)
    # archi ciechi sulle fiancate
    for sx in (-1, 1):
        poly_prism(dark, gothic_pts(0.0, 0.52, D - 0.28, 1.30), 'x', min(sx * hw, sx * (hw + 0.012)), max(sx * hw, sx * (hw + 0.012)))
    # coronamento: gola, fregio, arcatelle cieche e pinnacoli
    box(dark, V(0, 0, 2.325), V(W + 0.10, D + 0.08, 0.07), bevel=0.012, seg=2)
    box(wood, V(0, 0, 2.375), V(W + 0.05, D + 0.03, 0.035), bevel=0.006, seg=2)
    box(dark, V(0, hd - 0.020, 2.50), V(W - 0.06, 0.03, 0.24))
    n = 5
    pitch = (W - 0.16) / n
    for i in range(n):
        cx = -hw + 0.08 + (i + 0.5) * pitch
        arch_slab(wood, cx, 2.395, pitch - 0.035, 0.215, hd - 0.016, hd + 0.026)
        arch_slab(dark, cx, 2.415, pitch - 0.085, 0.165, hd + 0.016, hd + 0.030)
    for i in range(n + 1):
        x = -hw + 0.08 + i * pitch
        h = 0.0 if i in (0, n) else 0.0
        top = 0.26 if i == n // 2 or i == n // 2 + 1 else 0.20
        lathe(wood, [(0.0, 0.0), (0.024, 0.0), (0.024, 0.020), (0.015, 0.036), (0.015, 0.095), (0.022, 0.112), (0.010, top * 0.78), (0.0, top)],
              c=V(x, hd + 0.004, 2.395), segs=10)
        sphere(br, V(x, hd + 0.004, 2.395 + top + 0.004), 0.010, 8)
    # dentro: sbarra, ripiani con la biancheria piegata, mantelli (solo da aperto)
    if aperto:
        cyl(br, V(-hw + 0.04, 0.0, 2.10), V(hw - 0.04, 0.0, 2.10), 0.011, 10)
        for i in range(5):
            x = -0.52 + i * 0.22 + rnd.uniform(-0.02, 0.02)
            h = rnd.uniform(1.15, 1.45)
            drape(pan, x, rnd.uniform(-0.02, 0.05), 2.08, rnd.uniform(0.42, 0.56), h, rnd, folds=rnd.uniform(5.0, 8.0))
            cyl(br, V(x, 0.0, 2.08), V(x, 0.0, 2.135), 0.004, 6)
            sphere(br, V(x, 0.0, 2.14), 0.009, 6)
        for z in (1.28, 1.78):
            box(wood, V(0.43, 0.0, z), V(0.50, D - 0.10, 0.026), bevel=0.003, seg=1)
        for k in range(4):
            box(pan, V(0.43 + rnd.uniform(-0.01, 0.01), 0.0, 1.28 + 0.026 + 0.04 + k * 0.075), V(0.42, D - 0.16, 0.07), bevel=0.012, seg=2)
    return A


# ============================================================================= FORBICI GIGANTI
def forbici():
    """Un paio di forbici da sarto alte due metri e mezzo, in piedi sugli anelli di un plinto ottagonale di pietra scura: lame d'acciaio larghe
    appena aperte (a sezione di cuneo, filo verso il centro) con un filetto d'oro, il perno a vite, anelli e gambi dorati; sul fronte del plinto
    un pipistrello d'ottone. Il trofeo del conte nella sartoria e nella forgia."""
    A = Asset4('gu_forbici')
    gold, steel, iron, stone, br = A.bm('A_oro'), A.bm('A_acciaio'), A.bm('A_ferro'), A.bm('A_basalto'), A.bm('A_ottone')
    lathe(stone, [(0.0, 0.0), (0.48, 0.0), (0.48, 0.07), (0.455, 0.09), (0.44, 0.10), (0.44, 0.21), (0.37, 0.23), (0.37, 0.27), (0.32, 0.285), (0.0, 0.285)],
          c=V(0, 0, 0), segs=8)
    rot_about((stone,), V(0, 0, 0), radians(22.5))            # una faccia piana guarda davanti
    base = 0.285
    k = 1.18
    scissors(gold, steel, k=k, base=base, spread=7.5, wide=1.45)
    for sx in (-1, 1):
        box(iron, V(sx * 0.175 * k, 0.0, base + 0.008), V(0.26, 0.07, 0.016), bevel=0.003, seg=1)
    bat(br, V(0.0, 0.44 * cos(radians(22.5)), 0.155), s=1.15, thick=0.012)
    return A



# ============================================================================= BARE
def bara(name='gu_bara', chiusa=False):
    """La bara del vampiro su una lastra di pietra: cassa esagonale di noce con la fodera di seta viola e il cuscino, maniglie d'ottone, il
    coperchio a gradini (gruppo 'coperchio', origine al suo centro) spostato di traverso a scoprire la testa; 'chiusa': coperchio al suo posto
    e due fasce di ferro con il lucchetto."""
    A = Asset4(name)
    wood, dark, br, silk, stone, iron = A.bm('A_noce'), A.bm('A_legno'), A.bm('A_ottone'), A.bm('A_seta'), A.bm('A_basalto'), A.bm('A_ferro')
    outline = [(-1.02, -0.17), (-0.56, -0.37), (0.30, -0.31), (1.02, -0.15), (1.02, 0.15), (0.30, 0.31), (-0.56, 0.37), (-1.02, 0.17)]
    box(stone, V(0, 0, 0.04), V(2.34, 0.96, 0.08), bevel=0.012, seg=2)
    box(stone, V(0, 0, 0.23), V(2.14, 0.80, 0.46), bevel=0.03, seg=2)
    box(stone, V(0, 0, 0.50), V(2.30, 0.92, 0.08), bevel=0.014, seg=2)
    zb = 0.54
    poly_prism(dark, outline, 'z', zb, zb + 0.035)
    n = len(outline)
    for i in range(n):
        a, b = Vector(outline[i]), Vector(outline[(i + 1) % n])
        e = b - a
        ln = e.length
        tdir = e / ln
        nout = Vector((tdir.y, -tdir.x))
        mid = (a + b) / 2 - nout * 0.02
        ang = atan2(e.y, e.x)
        box(wood, V(mid.x, mid.y, zb + 0.185), V(ln + 0.02, 0.04, 0.34), bevel=0.006, seg=1, rot=Matrix.Rotation(ang, 3, 'Z'))
        mid2 = (a + b) / 2 - nout * 0.005
        box(dark, V(mid2.x, mid2.y, zb + 0.355), V(ln + 0.03, 0.065, 0.028), bevel=0.005, seg=1, rot=Matrix.Rotation(ang, 3, 'Z'))
    poly_prism(silk, [(x * 0.90, y * 0.84) for x, y in outline], 'z', zb + 0.035, zb + 0.042)
    rounded_grid(silk, V(-0.76, 0.0, zb + 0.082), V(0.36, 0.27, 0.08), 0.03, nx=10)

    def handle(a, b, tpar, side):
        p2 = Vector(a).lerp(Vector(b), tpar)
        e = Vector(b) - Vector(a)
        tdir = e.normalized()
        nout = Vector((tdir.y, -tdir.x)) * side
        z = zb + 0.20
        p3 = V(p2.x, p2.y, z)
        t3 = V(tdir.x, tdir.y, 0)
        n3 = V(nout.x, nout.y, 0)
        cyl(br, p3 - t3 * 0.07, p3 - t3 * 0.07 + n3 * 0.048, 0.0085, 8)
        cyl(br, p3 + t3 * 0.07, p3 + t3 * 0.07 + n3 * 0.048, 0.0085, 8)
        cyl(br, p3 - t3 * 0.07 + n3 * 0.048, p3 + t3 * 0.07 + n3 * 0.048, 0.0095, 8)
    for tp in (0.18, 0.5, 0.82):
        handle(outline[1], outline[2], tp, 1.0)
        handle(outline[6], outline[5], tp, 1.0)
    # coperchio
    lw, ld, lb = A.bm('A_noce', 'coperchio'), A.bm('A_legno', 'coperchio'), A.bm('A_ottone', 'coperchio')
    zl = zb + 0.372
    poly_prism(lw, [(x * 1.04, y * 1.10) for x, y in outline], 'z', zl, zl + 0.045)
    poly_prism(ld, [(x * 0.82, y * 0.68) for x, y in outline], 'z', zl + 0.042, zl + 0.078)
    poly_prism(lw, [(x * 0.56, y * 0.32) for x, y in outline], 'z', zl + 0.074, zl + 0.100)
    for x, y in outline:
        sphere(lb, V(x * 0.94, y * 0.94, zl + 0.050), 0.014, 8)
    bat(lb, V(-0.42, 0.0, zl + 0.098), s=1.7, thick=0.010, flat=True)       # il pipistrello d'ottone sul petto
    mats = (lw, ld, lb)
    if chiusa:
        for x in (-0.30, 0.46):
            box(iron, V(x, 0.0, zl + 0.052), V(0.075, 0.80, 0.020), bevel=0.004, seg=1)
            for sy in (-1, 1):
                sphere(iron, V(x, sy * 0.36, zl + 0.066), 0.012, 8)
        box(iron, V(0.46, 0.0, zl + 0.085), V(0.07, 0.06, 0.07), bevel=0.01, seg=2)
        loop_tube(iron, ellipse(V(0.46, 0.0, zl + 0.12), V(1, 0, 0), V(0, 0, 1), 0.026, 0.030, 12), 0.006, V(0, 1, 0), segs=6)
        A.pivot('coperchio', V(0, 0, zl))
    else:
        M = Matrix.Translation(V(0.60, -0.07, 0.0)) @ Matrix.Rotation(radians(13), 4, 'Z')
        for bm in mats:
            bmesh.ops.transform(bm, matrix=M, verts=bm.verts[:])
        A.pivot('coperchio', M @ V(0, 0, zl))
    return A


# ============================================================================= SARTORIA
def tavolo_sarto():
    """Il tavolo da taglio del sarto: piano di noce su quattro gambe tornite con traverse e un ripiano di stoffe piegate; sopra un telo rosso steso
    col cartamodello appuntato, le forbici, il puntaspilli, tre rocchetti e un metro arrotolato; un panno nero cade dal bordo davanti."""
    A = Asset4('gu_tavolo_sarto')
    wood, dark, steel, gold = A.bm('A_noce'), A.bm('A_legno'), A.bm('A_acciaio'), A.bm('A_oro')
    red, black, silk, linen = A.bm('A_tessuto'), A.bm('A_panno'), A.bm('A_seta'), A.bm('A_lenzuolo')
    rnd = random.Random(11)
    W, D, H = 2.30, 1.10, 0.92
    box(wood, V(0, 0, H - 0.04), V(W, D, 0.08), bevel=0.012, seg=2)
    box(dark, V(0, 0, H - 0.105), V(W - 0.16, D - 0.16, 0.05), bevel=0.006, seg=1)
    leg = [(0.0, 0.0), (0.045, 0.0), (0.050, 0.03), (0.040, 0.06), (0.056, 0.10), (0.056, 0.14), (0.040, 0.18), (0.035, 0.30), (0.052, 0.38),
           (0.052, 0.45), (0.036, 0.52), (0.036, 0.70), (0.060, 0.76), (0.060, 0.80), (0.052, 0.83), (0.0, 0.83)]
    for sx in (-1, 1):
        for sy in (-1, 1):
            lathe(dark, leg, c=V(sx * (W / 2 - 0.09), sy * (D / 2 - 0.09), 0.0), segs=12)
    for sy in (-1, 1):
        box(dark, V(0, sy * (D / 2 - 0.09), 0.20), V(W - 0.20, 0.05, 0.05), bevel=0.006, seg=1)
    for sx in (-1, 1):
        box(dark, V(sx * (W / 2 - 0.09), 0, 0.20), V(0.05, D - 0.20, 0.05), bevel=0.006, seg=1)
    box(wood, V(0, 0, 0.245), V(W - 0.26, D - 0.26, 0.025), bevel=0.003, seg=1)
    for x, col in ((-0.62, black), (-0.05, red), (0.52, silk)):                       # stoffe piegate sul ripiano basso
        for k in range(3):
            box(col, V(x + rnd.uniform(-0.015, 0.015), rnd.uniform(-0.03, 0.03), 0.2575 + 0.035 + k * 0.07), V(0.46, 0.44, 0.065), bevel=0.016, seg=1)
    zt = H
    cloth_sheet(red, (-0.35, 0.04, zt + 0.012), 1.30, 0.82, 0.016, rnd, amp=0.006, freq=5.0)       # il telo rosso da tagliare
    pz = zt + 0.030                                                                                 # il cartamodello appuntato
    box(linen, V(-0.55, -0.02, pz), V(0.52, 0.36, 0.003), rot=Matrix.Rotation(radians(6), 3, 'Z'))
    for px, py in ((-0.78, -0.17), (-0.32, -0.17), (-0.78, 0.14), (-0.32, 0.14), (-0.55, -0.02)):
        cyl(steel, V(px, py, pz - 0.004), V(px, py, pz + 0.040), 0.0013, 5)
        sphere(gold, V(px, py, pz + 0.042), 0.004, 5)
    cloth_sheet(black, (0.80, 0.45, zt + 0.012), 0.56, 0.20, 0.012, rnd, amp=0.005, freq=4.0)    # il panno nero che cade dal bordo
    drape(black, 0.80, D / 2 + 0.014, zt + 0.006, 0.56, 0.46, rnd, amp=0.02, folds=5.0)
    tg, ts = bmesh.new(), bmesh.new()                                                                # le forbici sul cartamodello
    scissors(tg, ts, k=0.30, base=0.0, spread=9.0, wide=1.5, lod=0.4)
    M = Matrix.Translation(V(-0.52, -0.04, pz + 0.0015 + 0.012)) @ Matrix.Rotation(radians(28), 4, 'Z') @ Matrix.Rotation(radians(-90), 4, 'X')
    g3.merge(gold, tg, M)
    g3.merge(steel, ts, M)
    lathe(linen, [(0.0, 0.0), (0.040, 0.0), (0.040, 0.014), (0.0, 0.014)], c=V(0.62, -0.26, zt), segs=14)      # il metro arrotolato
    lathe(gold, [(0.0, 0.0), (0.012, 0.0), (0.012, 0.018), (0.0, 0.018)], c=V(0.62, -0.26, zt), segs=8)
    pc = V(0.93, 0.20, zt + 0.030)                                                                   # il puntaspilli a ellissoide con gli spilli
    ellipsoid(red, pc, (0.055, 0.055, 0.034), 10)
    for k in range(9):
        a = 2 * pi * k / 9
        d = V(cos(a), sin(a), 0.0)
        b = pc + d * 0.026 + V(0, 0, 0.034 * sqrt(1 - (0.026 / 0.055) ** 2) - 0.004)
        t = b + d * 0.020 + V(0, 0, 0.022)
        cyl(steel, b, t, 0.0012, 4)
        sphere(gold, t, 0.0036, 4)
    for i, col in enumerate((silk, black, red)):                                                     # tre rocchetti
        x, y = 0.98 + i * 0.065, -0.24 + (i % 2) * 0.05
        lathe(dark, [(0.0, 0.0), (0.020, 0.0), (0.020, 0.006), (0.012, 0.008), (0.012, 0.034), (0.020, 0.036), (0.020, 0.042), (0.0, 0.042)],
              c=V(x, y, zt), segs=10)
        cyl(col, V(x, y, zt + 0.008), V(x, y, zt + 0.034), 0.0185, 10)
    return A


def macchina_cucire():
    """La macchina da cucire a pedale: telai di ghisa con colonne tornite, piano di noce, pedale, ruota a raggi con la biella e la cinghia; sopra la
    testa laccata nera con filetti d'oro, il volantino d'acciaio, il rocchetto di seta viola e l'ago."""
    A = Asset4('gu_macchina_cucire')
    iron, wood, lac, gold = A.bm('A_ferro'), A.bm('A_noce'), A.bm('A_lacca'), A.bm('A_oro')
    steel, leather, dark, silk = A.bm('A_acciaio'), A.bm('A_cuoio'), A.bm('A_legno'), A.bm('A_seta')
    W, D = 0.95, 0.48
    zt = 0.79
    box(wood, V(0, 0, zt), V(W, D, 0.035), bevel=0.008, seg=1)
    col = [(0.0, 0.0), (0.026, 0.0), (0.030, 0.03), (0.020, 0.06), (0.016, 0.12), (0.022, 0.18), (0.016, 0.26), (0.016, 0.62), (0.030, 0.70),
           (0.030, 0.74), (0.0, 0.745)]
    for sx in (-1, 1):
        x = sx * (W / 2 - 0.09)
        box(iron, V(x, 0, 0.02), V(0.05, 0.42, 0.035), bevel=0.01, seg=1)
        for sy in (-1, 1):
            lathe(iron, col, c=V(x, sy * 0.15, 0.0), segs=10)
        tube(iron, [V(x, -0.15, 0.62), V(x, -0.08, 0.50), V(x, 0.08, 0.50), V(x, 0.15, 0.62)], [0.009] * 4, segs=6, n=14)
        box(iron, V(x, 0, 0.765), V(0.05, 0.40, 0.03), bevel=0.006, seg=1)
    cyl(iron, V(-W / 2 + 0.09, 0, 0.10), V(W / 2 - 0.09, 0, 0.10), 0.010, 8)
    box(iron, V(-0.05, 0, 0.115), V(0.40, 0.22, 0.012), bevel=0.003, seg=1)                          # il pedale
    wc = V(0.22, 0.0, 0.42)
    spoked_wheel(iron, wc, 0.17, V(1, 0, 0), V(0, 0, 1), n_spokes=8, rim=0.012, rim_w=0.016, hub=0.03)
    cyl(iron, wc + V(0.07, -0.03, 0.0), V(0.02, 0.0, 0.12), 0.006, 6)                               # la biella
    bz = zt + 0.0175 + 0.02
    box(lac, V(-0.04, 0, bz), V(0.46, 0.17, 0.04), bevel=0.012, seg=1)                               # la base
    box(steel, V(-0.16, 0, bz + 0.022), V(0.12, 0.07, 0.006), bevel=0.001, seg=1)                    # la placca dell'ago
    tube(lac, [V(0.19, 0, bz + 0.02), V(0.19, 0, bz + 0.15), V(0.13, 0, bz + 0.22), V(-0.08, 0, bz + 0.225), V(-0.18, 0, bz + 0.20),
               V(-0.20, 0, bz + 0.12)], [0.040, 0.040, 0.036, 0.034, 0.032, 0.030], segs=12, n=40)  # il braccio a collo di cigno
    cyl(steel, V(-0.20, 0, bz + 0.12), V(-0.20, 0, bz + 0.030), 0.0045, 6)                           # la barra dell'ago
    box(steel, V(-0.20, 0, bz + 0.026), V(0.03, 0.014, 0.010), bevel=0.001, seg=1)                   # il piedino
    lathe(steel, [(0.0, 0.0), (0.062, 0.0), (0.062, 0.014), (0.044, 0.022), (0.020, 0.030), (0.0, 0.030)], axis='X', c=V(0.225, 0, bz + 0.16), segs=20)
    cyl(steel, V(0.0, 0, bz + 0.25), V(0.0, 0, bz + 0.315), 0.004, 6)                                # il rocchetto di seta viola in cima
    lathe(dark, [(0.0, 0.0), (0.020, 0.0), (0.020, 0.006), (0.012, 0.008), (0.012, 0.040), (0.020, 0.042), (0.020, 0.048), (0.0, 0.048)],
          c=V(0.0, 0, bz + 0.262), segs=10)
    cyl(silk, V(0.0, 0, bz + 0.270), V(0.0, 0, bz + 0.302), 0.0185, 10)
    for z in (bz + 0.09, bz + 0.12, bz + 0.15):                                                      # filetti d'oro sul montante
        box(gold, V(0.19, 0.041, z), V(0.05, 0.003, 0.012), bevel=0.001, seg=1)
    cyl(leather, wc + V(0.0, 0.0, 0.17), V(0.225, 0.0, bz + 0.10), 0.0035, 5)                        # la cinghia
    return A


def stoffe():
    """Lo scaffale delle stoffe: due fiancate, schiena e cinque ripiani di noce con i rotoli di stoffa a fianco a fianco (rosso, viola, nero, crema,
    velluto), ognuno con l'anima di cartone che sporge, e qualche mucchio di tessuto piegato."""
    A = Asset4('gu_stoffe')
    dark, wood, core = A.bm('A_legno'), A.bm('A_noce'), A.bm('A_cera')
    colors = ['A_tessuto', 'A_seta', 'A_panno', 'A_lenzuolo', 'A_velluto']
    rnd = random.Random(5)
    W, D, H = 1.2, 0.45, 1.9
    Lb = D - 0.07
    for sx in (-1, 1):
        box(dark, V(sx * (W / 2 - 0.02), 0, H / 2), V(0.04, D, H), bevel=0.004, seg=1)
    box(dark, V(0, -D / 2 + 0.01, H / 2), V(W - 0.06, 0.02, H))
    box(wood, V(0, 0, H + 0.02), V(W + 0.05, D + 0.04, 0.04), bevel=0.008, seg=1)
    box(wood, V(0, 0, 0.04), V(W - 0.04, D - 0.02, 0.08), bevel=0.004, seg=1)
    levels = [0.10, 0.52, 0.94, 1.36, 1.78]

    def roll(role, cx, zc, r):
        lathe(A.bm(role), [(0.0, -Lb / 2), (r, -Lb / 2), (r, Lb / 2), (0.0, Lb / 2)], axis='Y', c=V(cx, 0.0, zc), segs=12)
        cyl(core, V(cx, -Lb / 2 - 0.012, zc), V(cx, Lb / 2 + 0.012, zc), 0.022, 6)

    for z in levels:
        box(wood, V(0, 0, z), V(W - 0.04, D - 0.02, 0.026), bevel=0.003, seg=1)
    for z in levels[:-1]:
        z0 = z + 0.013
        row = []
        x = -W / 2 + 0.07
        while True:
            r = rnd.uniform(0.060, 0.095)
            if x + 2 * r > W / 2 - 0.06:
                break
            row.append((x + r, r, rnd.choice(colors), rnd.random() < 0.18))
            x += 2 * r + 0.006
        for cx, r, role, folded in row:
            if folded:
                for k in range(3):
                    box(A.bm(role), V(cx, 0.0, z0 + 0.03 + k * 0.062), V(2 * r, Lb - 0.03, 0.058), bevel=0.014, seg=1)
            else:
                roll(role, cx, z0 + r, r)
        for (xa, ra, _, fa), (xb, rb, _, fb) in zip(row, row[1:]):
            if not fa and not fb and rnd.random() < 0.65:
                r2 = rnd.uniform(0.045, 0.065)
                roll(rnd.choice(colors), (xa + xb) / 2, z0 + max(ra, rb) + r2 * 0.80, r2)
    return A


def attaccapanni():
    """L'attaccapanni da spogliatoio: palo tornito su tre piedi a voluta, sei bracci d'ottone a due altezze, tre mantelli appesi (due neri e uno
    rosso) e un cilindro lucido in cima."""
    A = Asset4('gu_attaccapanni')
    dark, br, pan, red, lac = A.bm('A_legno'), A.bm('A_ottone'), A.bm('A_panno'), A.bm('A_tessuto'), A.bm('A_lacca')
    rnd = random.Random(3)
    lathe(dark, [(0, 0), (0.07, 0), (0.08, 0.03), (0.05, 0.08), (0.032, 0.16), (0.028, 0.30), (0.038, 0.34), (0.028, 0.39), (0.025, 1.50),
                 (0.034, 1.57), (0.05, 1.64), (0.04, 1.71), (0.0, 1.74)], c=V(0, 0, 0), segs=14)
    for k in range(3):
        a = 2 * pi * k / 3 + 0.4
        tube(dark, [V(0, 0, 0.16), V(cos(a) * 0.12, sin(a) * 0.12, 0.13), V(cos(a) * 0.25, sin(a) * 0.25, 0.08), V(cos(a) * 0.30, sin(a) * 0.30, 0.0)],
             [0.028, 0.026, 0.024, 0.018], segs=8, n=14)
    hooks = []
    for k in range(6):
        a = 2 * pi * k / 6 + 0.2
        z = 1.52 + (k % 2) * 0.10
        pts = [V(cos(a) * 0.03, sin(a) * 0.03, z), V(cos(a) * 0.12, sin(a) * 0.12, z + 0.01), V(cos(a) * 0.20, sin(a) * 0.20, z + 0.07),
               V(cos(a) * 0.23, sin(a) * 0.23, z + 0.15)]
        tube(br, pts, [0.011, 0.010, 0.009, 0.008], segs=6, n=12)
        sphere(br, pts[-1] + V(0, 0, 0.012), 0.016, 8)
        hooks.append((a, pts[2]))
    for idx, (a, p) in enumerate(hooks):
        if idx not in (0, 2, 4):
            continue
        cloak(red if idx == 4 else pan, a, p.z + 0.03, rnd.uniform(1.10, 1.30), 0.20, rnd.uniform(0.50, 0.58), 0.19, 0.30, rnd,
              folds=rnd.uniform(5.0, 7.0))
    lathe(lac, [(0, 0), (0.085, 0), (0.090, 0.004), (0.072, 0.012), (0.072, 0.15), (0.0, 0.155)], c=V(0, 0, 1.74), segs=14)
    lathe(lac, [(0, 0), (0.14, 0), (0.14, 0.008), (0.0, 0.008)], c=V(0, 0, 1.74), segs=14)
    return A


# ============================================================================= FORGIA
def braciere():
    """Il braciere: tre gambe di ferro battuto a zampa con un anello di rinforzo, catino di ferro con l'orlo a toro e due maniglie ad anello, un
    cumulo di braci incandescenti e sette lingue di fiamma."""
    A = Asset4('gu_braciere')
    iron, coal, fire = A.bm('A_ferro'), A.bm('A_brace'), A.bm('A_fuoco')
    rnd = random.Random(21)
    zb, R = 0.42, 0.31
    lathe(iron, [(0.0, zb), (0.10, zb + 0.004), (0.20, zb + 0.05), (0.27, zb + 0.13), (R, zb + 0.20), (R - 0.014, zb + 0.205), (R - 0.03, zb + 0.17),
                 (0.0, zb + 0.17)], c=V(0, 0, 0), segs=20)
    torus(iron, V(0, 0, zb + 0.202), R - 0.002, 0.012, segs=24, rs=6)
    for k in range(3):
        a = 2 * pi * k / 3 + 0.5
        tube(iron, [V(cos(a) * 0.15, sin(a) * 0.15, zb + 0.06), V(cos(a) * 0.23, sin(a) * 0.23, zb - 0.06), V(cos(a) * 0.30, sin(a) * 0.30, 0.18),
                    V(cos(a) * 0.34, sin(a) * 0.34, 0.015)], [0.024, 0.021, 0.018, 0.014], segs=6, n=12)
        sphere(iron, V(cos(a) * 0.345, sin(a) * 0.345, 0.025), 0.026, 6)
    torus(iron, V(0, 0, 0.22), 0.30, 0.009, segs=30, rs=5)
    for sx in (-1, 1):
        loop_tube(iron, ellipse(V(sx * (R + 0.028), 0, zb + 0.12), V(1, 0, 0), V(0, 0, 1), 0.034, 0.045, 12), 0.008, V(0, 1, 0), segs=5)
    for i in range(14):
        a, d = rnd.uniform(0, 2 * pi), rnd.uniform(0.0, 0.22)
        ellipsoid(coal, V(cos(a) * d, sin(a) * d, zb + 0.185 + 0.03 * (1 - d / 0.25)), (rnd.uniform(0.05, 0.08), rnd.uniform(0.045, 0.07), rnd.uniform(0.03, 0.045)),
                  6, rot=Matrix.Rotation(rnd.uniform(0, pi), 3, 'Z'))
    for k in range(7):
        a = 2 * pi * k / 7 + rnd.uniform(-0.2, 0.2)
        rr = 0.0 if k == 0 else rnd.uniform(0.07, 0.17)
        s = rnd.uniform(4.0, 7.5) if k else 8.5
        tmp = bmesh.new()
        flame(tmp, V(0, 0, 0), s)
        M = (Matrix.Translation(V(cos(a) * rr, sin(a) * rr, zb + 0.20)) @ Matrix.Rotation(rnd.uniform(-0.25, 0.25), 4, 'X')
             @ Matrix.Rotation(rnd.uniform(-0.25, 0.25), 4, 'Y') @ Matrix.Diagonal((0.8, 0.8, 1.0, 1.0)))
        g3.merge(fire, tmp, M)
    return A


def incudine():
    """L'incudine sul ceppo: tronco con tre cerchi di ferro, incudine di ferro con corno, tallone e piede, faccia d'acciaio, un ferro rovente
    sulla faccia e un martello appoggiato sul ceppo."""
    A = Asset4('gu_incudine')
    wood, dark, iron, steel, hot = A.bm('A_legno'), A.bm('A_scuro'), A.bm('A_ferro'), A.bm('A_acciaio'), A.bm('A_brace')
    x0, hs = -0.20, 0.50
    lathe(wood, [(0.0, 0.0), (0.30, 0.0), (0.285, 0.05), (0.27, 0.25), (0.275, 0.45), (0.285, hs), (0.0, hs)], c=V(x0, 0, 0), segs=16)
    for z, r in ((0.10, 0.285), (0.27, 0.275), (0.43, 0.282)):
        torus(iron, V(x0, 0, z), r, 0.011, segs=24, rs=5)
    body = [(-0.25, 0.0), (0.25, 0.0), (0.25, 0.04), (0.19, 0.06), (0.12, 0.10), (0.11, 0.19), (0.19, 0.245), (0.27, 0.27), (0.27, 0.30),
            (-0.20, 0.30), (-0.30, 0.285), (-0.31, 0.25), (-0.22, 0.22), (-0.12, 0.17), (-0.115, 0.10), (-0.17, 0.06), (-0.25, 0.04)]
    poly_prism(iron, [(x0 + x, hs + z) for x, z in body], 'y', -0.10, 0.10)
    lathe(iron, [(0.075, 0.0), (0.068, 0.10), (0.05, 0.25), (0.03, 0.38), (0.012, 0.45), (0.0, 0.46)], axis='X', c=V(x0 + 0.26, 0.0, hs + 0.225), segs=12)
    box(steel, V(x0 - 0.015, 0, hs + 0.305), V(0.57, 0.20, 0.012), bevel=0.003, seg=1)
    box(dark, V(x0 - 0.16, 0, hs + 0.309), V(0.035, 0.035, 0.006))
    cyl(dark, V(x0 - 0.06, 0, hs + 0.305), V(x0 - 0.06, 0, hs + 0.312), 0.014, 8)
    box(hot, V(x0 + 0.04, 0.02, hs + 0.318), V(0.32, 0.03, 0.022), bevel=0.004, seg=1, rot=Matrix.Rotation(radians(16), 3, 'Z'))
    cyl(wood, V(x0 - 0.18, 0.12, hs + 0.019), V(x0 + 0.08, 0.15, hs + 0.019), 0.018, 8)
    box(iron, V(x0 + 0.13, 0.155, hs + 0.04), V(0.075, 0.055, 0.055), bevel=0.008, seg=1, rot=Matrix.Rotation(radians(6), 3, 'Z'))
    return A


def crogiolo():
    """La forgia a fosso: focolare di pietra con le braci e le fiamme, due montanti di ferro con la traversa, una catena e il crogiolo di ferro
    pieno di metallo fuso che cola dal beccuccio; sul fronte del focolare un pipistrello d'oro."""
    A = Asset4('gu_crogiolo')
    stone, iron, steel, hot, fire, gold = A.bm('A_basalto'), A.bm('A_ferro'), A.bm('A_acciaio'), A.bm('A_brace'), A.bm('A_fuoco'), A.bm('A_oro')
    rnd = random.Random(31)
    box(stone, V(0, 0, 0.15), V(1.30, 0.90, 0.30), bevel=0.02, seg=1)
    for sy in (-1, 1):
        box(stone, V(0, sy * 0.33, 0.35), V(1.30, 0.24, 0.10), bevel=0.012, seg=1)
    for sx in (-1, 1):
        box(stone, V(sx * 0.53, 0, 0.35), V(0.24, 0.42, 0.10), bevel=0.012, seg=1)
    box(hot, V(0, 0, 0.315), V(0.80, 0.40, 0.03), bevel=0.01, seg=1)
    for i in range(10):
        ellipsoid(hot, V(rnd.uniform(-0.34, 0.34), rnd.uniform(-0.15, 0.15), 0.33), (0.07, 0.06, 0.04), 6, rot=Matrix.Rotation(rnd.uniform(0, pi), 3, 'Z'))
    for k in range(6):
        rr = 0.0 if k == 0 else rnd.uniform(0.0, 0.3)
        a = 2 * pi * k / 6
        tmp = bmesh.new()
        flame(tmp, V(0, 0, 0), rnd.uniform(6.0, 9.0))
        M = (Matrix.Translation(V(cos(a) * rr, sin(a) * rr * 0.5, 0.33)) @ Matrix.Rotation(rnd.uniform(-0.2, 0.2), 4, 'X')
             @ Matrix.Rotation(rnd.uniform(-0.2, 0.2), 4, 'Y') @ Matrix.Diagonal((0.8, 0.8, 1.0, 1.0)))
        g3.merge(fire, tmp, M)
    bat(gold, V(0, 0.45, 0.15), s=1.9, thick=0.012)
    for sx in (-1, 1):
        box(iron, V(sx * 0.72, 0, 0.925), V(0.10, 0.10, 1.85), bevel=0.01, seg=1)
        box(iron, V(sx * 0.72, 0, 0.02), V(0.22, 0.22, 0.04), bevel=0.004, seg=1)
        cyl(iron, V(sx * 0.72, 0, 1.40), V(sx * 0.40, 0, 1.78), 0.025, 6)
        sphere(iron, V(sx * 0.72, 0, 1.89), 0.06, 8)
    box(iron, V(0, 0, 1.80), V(1.54, 0.10, 0.10), bevel=0.01, seg=1)
    chain(iron, V(0, 0, 1.76), V(0, 0, 1.55), r=0.03, wire=0.007, pitch=0.058)
    tube(iron, [V(-0.31, 0, 1.18), V(-0.27, 0, 1.40), V(0, 0, 1.54), V(0.27, 0, 1.40), V(0.31, 0, 1.18)], [0.015] * 5, segs=6, n=18)
    lathe(iron, [(0.0, 0.70), (0.16, 0.705), (0.25, 0.78), (0.32, 0.92), (0.335, 1.02), (0.315, 1.12), (0.30, 1.20), (0.282, 1.20), (0.276, 1.12), (0.0, 1.10)],
          c=V(0, 0, 0), segs=20)
    cyl(hot, V(0, 0, 1.125), V(0, 0, 1.145), 0.274, 20)
    torus(steel, V(0, 0, 0.95), 0.327, 0.010, segs=24, rs=5)
    torus(steel, V(0, 0, 1.16), 0.309, 0.010, segs=24, rs=5)
    box(iron, V(0, 0.31, 1.17), V(0.12, 0.10, 0.05), bevel=0.01, seg=1, rot=Matrix.Rotation(radians(25), 3, 'X'))
    cyl(hot, V(0, 0.37, 1.16), V(0, 0.40, 0.98), 0.012, 6)
    sphere(hot, V(0, 0.40, 0.95), 0.022, 6)
    return A


# ============================================================================= TERRAZZA
def gargoyle():
    """Il gargoyle di guardia: una creatura di pietra accucciata su un plinto con la schiena a punte, ali di pipistrello a festoni, corna, orecchie,
    zanne e occhi di brace; le mani artigliate sul bordo anteriore e la coda arrotolata di lato."""
    A = Asset4('gu_gargoyle')
    st, eye = A.bm('A_basalto'), A.bm('A_brace')
    ZP = 0.50
    box(st, V(0, 0, 0.06), V(0.94, 0.94, 0.12), bevel=0.01, seg=1)
    box(st, V(0, 0, 0.30), V(0.78, 0.78, 0.36), bevel=0.012, seg=1)
    box(st, V(0, 0, 0.47), V(0.90, 0.90, 0.06), bevel=0.01, seg=1)
    arch_slab(st, 0.0, 0.16, 0.34, 0.28, 0.39, 0.405)

    def limb(pts, radii, segs=7, n=10):
        tube(st, pts, radii, segs=segs, n=n)
    # tronco, spalle, bacino e spine dorsali
    ellipsoid(st, V(0.0, -0.06, ZP + 0.44), (0.23, 0.31, 0.25), 12, rot=Matrix.Rotation(radians(41), 3, 'X'))
    ellipsoid(st, V(0.0, 0.10, ZP + 0.58), (0.28, 0.17, 0.17), 10)
    ellipsoid(st, V(0.0, -0.27, ZP + 0.33), (0.21, 0.20, 0.19), 10)
    for t in (0.0, 0.25, 0.5, 0.75, 1.0):
        P = V(0, 0.02, ZP + 0.735).lerp(V(0, -0.34, ZP + 0.52), t)
        cone(st, P, P + V(0, -0.05, 0.11 * (1.0 - 0.35 * t)), 0.026 * (1.0 - 0.3 * t), segs=5)
    for sx in (-1, 1):
        # zampe posteriori: coscia, ginocchio in alto, stinco, piede con tre artigli
        ellipsoid(st, V(sx * 0.24, -0.21, ZP + 0.30), (0.12, 0.19, 0.15), 8, rot=Matrix.Rotation(radians(-20), 3, 'X'))
        limb([V(sx * 0.22, -0.26, ZP + 0.30), V(sx * 0.28, -0.08, ZP + 0.40), V(sx * 0.28, 0.02, ZP + 0.34)], [0.085, 0.07, 0.055])
        limb([V(sx * 0.28, 0.02, ZP + 0.34), V(sx * 0.27, -0.06, ZP + 0.18), V(sx * 0.26, -0.10, ZP + 0.07)], [0.055, 0.045, 0.035])
        ellipsoid(st, V(sx * 0.26, 0.0, ZP + 0.04), (0.055, 0.13, 0.04), 8)
        for k in (-1, 0, 1):
            cone(st, V(sx * 0.26 + k * 0.032, 0.115, ZP + 0.035), V(sx * 0.26 + k * 0.040, 0.185, ZP + 0.012), 0.016, segs=5)
        # zampe anteriori: aggrappate al bordo del plinto
        limb([V(sx * 0.27, 0.06, ZP + 0.58), V(sx * 0.35, 0.18, ZP + 0.38), V(sx * 0.31, 0.31, ZP + 0.10)], [0.085, 0.065, 0.045], n=12)
        ellipsoid(st, V(sx * 0.31, 0.37, ZP + 0.045), (0.055, 0.085, 0.034), 8)
        for k in (-1, 0, 1):
            cone(st, V(sx * 0.31 + k * 0.032, 0.435, ZP + 0.035), V(sx * 0.31 + k * 0.036, 0.485, ZP - 0.035), 0.016, segs=5)
    # testa
    limb([V(0, 0.12, ZP + 0.64), V(0, 0.20, ZP + 0.72), V(0, 0.27, ZP + 0.76)], [0.115, 0.10, 0.09])
    ellipsoid(st, V(0, 0.31, ZP + 0.79), (0.150, 0.165, 0.145), 12)
    ellipsoid(st, V(0, 0.45, ZP + 0.735), (0.085, 0.105, 0.065), 10)
    ellipsoid(st, V(0, 0.43, ZP + 0.665), (0.075, 0.095, 0.035), 8, rot=Matrix.Rotation(radians(-12), 3, 'X'))
    ellipsoid(st, V(0, 0.40, ZP + 0.850), (0.165, 0.060, 0.035), 8)
    for sx in (-1, 1):
        sphere(eye, V(sx * 0.068, 0.452, ZP + 0.808), 0.021, 6)
        cone(st, V(sx * 0.040, 0.505, ZP + 0.715), V(sx * 0.040, 0.508, ZP + 0.650), 0.013, segs=5)
        cone(st, V(sx * 0.050, 0.470, ZP + 0.645), V(sx * 0.050, 0.478, ZP + 0.700), 0.011, segs=5)
        limb([V(sx * 0.085, 0.31, ZP + 0.90), V(sx * 0.14, 0.27, ZP + 1.03), V(sx * 0.15, 0.16, ZP + 1.14), V(sx * 0.12, 0.05, ZP + 1.19)],
             [0.030, 0.024, 0.014, 0.004], segs=6, n=12)
        ellipsoid(st, V(sx * 0.185, 0.25, ZP + 0.94), (0.016, 0.055, 0.13), 8, rot=Matrix.Rotation(radians(-sx * 25), 3, 'Y'))
    # ali di pipistrello: membrana a festoni con le dita
    for sx in (-1, 1):
        wb = bmesh.new()
        S = 9.5
        poly_prism(wb, [(x * S, z * S) for x, z in BAT], 'y', -0.009, 0.009)
        wrist = V(0.056 * S, 0.0, 0.036 * S)
        tube(wb, [V(0.002 * S, 0.0, 0.015 * S), V(0.020 * S, 0.0, 0.026 * S), V(0.038 * S, 0.0, 0.032 * S), wrist], [0.028, 0.022, 0.018, 0.016], segs=6, n=10)
        for tx, tz in ((0.082, 0.026), (0.074, -0.006), (0.046, -0.018), (0.010, -0.030)):
            tp = V(tx * S, 0.0, tz * S)
            tube(wb, [wrist, wrist.lerp(tp, 0.5), tp], [0.014, 0.010, 0.004], segs=5, n=6)
        M = (Matrix.Translation(V(sx * 0.13, -0.10, ZP + 0.62)) @ Matrix.Diagonal((float(sx), 1.0, 1.0, 1.0)) @ Matrix.Rotation(radians(-35), 4, 'Z')
             @ Matrix.Rotation(radians(-40), 4, 'Y'))
        g3.merge(st, wb, M)
    limb([V(0, -0.40, ZP + 0.22), V(0.16, -0.50, ZP + 0.14), V(0.38, -0.44, ZP + 0.08), V(0.45, -0.22, ZP + 0.05)], [0.055, 0.042, 0.03, 0.014], segs=6, n=14)
    cone(st, V(0.45, -0.26, ZP + 0.05), V(0.45, -0.17, ZP + 0.05), 0.04, segs=4)
    return A


def albero_secco():
    """L'albero morto della terrazza: un tronco contorto con le radici che escono dalla terra e rami che si biforcano tre volte, senza una foglia,
    in una vasca bassa di pietra."""
    A = Asset4('gu_albero_secco')
    bark, st, soil = A.bm('A_legno'), A.bm('A_basalto'), A.bm('A_terra')
    rnd = random.Random(12)
    box(st, V(0, 0, 0.03), V(1.5, 1.5, 0.06))
    for sx in (-1, 1):
        box(st, V(sx * 0.70, 0, 0.21), V(0.16, 1.56, 0.42), bevel=0.012, seg=1)
        box(st, V(0, sx * 0.70, 0.21), V(1.24, 0.16, 0.42), bevel=0.012, seg=1)
    ellipsoid(soil, V(0, 0, 0.36), (0.66, 0.66, 0.10), 12)

    def branch(p, d, length, r0, depth):
        pts, cur, dd = [Vector(p)], Vector(p), Vector(d).normalized()
        for _ in range(4):
            dd = (dd + V(rnd.uniform(-0.28, 0.28), rnd.uniform(-0.28, 0.28), rnd.uniform(-0.02, 0.10))).normalized()
            cur = cur + dd * (length / 4)
            pts.append(cur.copy())
        tube(bark, pts, [r0, r0 * 0.80, r0 * 0.60, r0 * 0.40, r0 * 0.14], segs=max(4, 9 - 2 * depth), n=(14, 10, 8, 6)[depth])
        if depth < 3:
            for _ in range((4, rnd.randint(2, 4), rnd.randint(2, 3))[depth]):
                t = rnd.uniform(0.30, 0.90)
                i = min(int(t * 4), 3)
                base = pts[i].lerp(pts[i + 1], t * 4 - i)
                tang = (pts[i + 1] - pts[i]).normalized()
                side = V(rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-0.1, 0.7))
                branch(base, (tang * 0.55 + side * 0.85).normalized(), length * rnd.uniform(0.50, 0.72), r0 * 0.52, depth + 1)
    branch(V(0, 0, 0.40), V(0.05, 0.0, 1.0), 1.7, 0.17, 0)
    for k in range(5):
        a = 2 * pi * k / 5 + rnd.uniform(-0.3, 0.3)
        tube(bark, [V(cos(a) * 0.06, sin(a) * 0.06, 0.62), V(cos(a) * 0.20, sin(a) * 0.20, 0.50), V(cos(a) * 0.42, sin(a) * 0.42, 0.45)],
             [0.13, 0.07, 0.025], segs=7, n=8)
    return A


def specchio():
    """Lo specchio del vampiro: cornice dorata a sesto acuto a gradini tra due montanti torniti con piedi a slitta, un vetro nero che non
    riflette niente e un pipistrello d'oro in cima."""
    A = Asset4('gu_specchio')
    gold, dark, mir, br = A.bm('A_oro'), A.bm('A_legno'), A.bm('A_lacca'), A.bm('A_ottone')
    W = 0.95
    zb, hf = 0.30, 1.65
    for sx in (-1, 1):
        box(dark, V(sx * 0.52, 0.0, 0.04), V(0.08, 0.56, 0.08), bevel=0.012, seg=1)
        lathe(dark, [(0.0, 0.0), (0.040, 0.0), (0.040, 0.03), (0.028, 0.06), (0.028, 0.28), (0.040, 0.32), (0.028, 0.36), (0.024, 1.78), (0.032, 1.84),
                     (0.044, 1.88), (0.0, 1.92)], c=V(sx * 0.52, 0.0, 0.08), segs=10)
        sphere(br, V(sx * 0.52, 0.0, 2.02), 0.032, 8)
    box(dark, V(0, 0, 0.12), V(1.0, 0.05, 0.05), bevel=0.008, seg=1)
    arch_ring(gold, 0.0, zb, W, hf, zb + 0.09, W - 0.18, hf - 0.18, -0.035, 0.032)            # la cornice
    arch_ring(gold, 0.0, zb + 0.03, W - 0.06, hf - 0.06, zb + 0.075, W - 0.15, hf - 0.15, 0.0, 0.044)    # il bordino in rilievo
    arch_slab(mir, 0.0, zb + 0.09, W - 0.18, hf - 0.18, -0.020, 0.018)                         # il vetro, arretrato di 1,4 cm
    bat(gold, V(0, 0.030, zb + hf - 0.047), s=0.9, thick=0.016)
    return A


def lampione():
    """Il lampione della terrazza: plinto di pietra, fusto di ferro con collari, lanterna a quattro vetri con una fiamma, tetto a piramide e in
    cima un pipistrello di ferro come banderuola."""
    A = Asset4('gu_lampione')
    iron, st, glass, fire = A.bm('A_ferro'), A.bm('A_basalto'), A.bm('A_paralume'), A.bm('A_fiamma')
    box(st, V(0, 0, 0.08), V(0.50, 0.50, 0.16), bevel=0.012, seg=1)
    box(st, V(0, 0, 0.26), V(0.38, 0.38, 0.20), bevel=0.012, seg=1)
    box(st, V(0, 0, 0.375), V(0.44, 0.44, 0.03), bevel=0.006, seg=1)
    lathe(iron, [(0.0, 0.0), (0.075, 0.0), (0.075, 0.03), (0.052, 0.06), (0.042, 0.16), (0.06, 0.20), (0.042, 0.24), (0.034, 0.34), (0.034, 1.50),
                 (0.048, 1.56), (0.034, 1.62), (0.034, 1.70), (0.046, 1.74), (0.0, 1.76)], c=V(0, 0, 0.39), segs=12)
    zl = 0.39 + 1.74
    for k in range(4):
        a = 2 * pi * k / 4 + pi / 4
        tube(iron, [V(cos(a) * 0.04, sin(a) * 0.04, zl - 0.22), V(cos(a) * 0.13, sin(a) * 0.13, zl - 0.13), V(cos(a) * 0.17, sin(a) * 0.17, zl - 0.01)],
             [0.010, 0.009, 0.008], segs=5, n=8)
    box(iron, V(0, 0, zl + 0.015), V(0.32, 0.32, 0.03), bevel=0.004, seg=1)
    for sx in (-1, 1):
        for sy in (-1, 1):
            cyl(iron, V(sx * 0.135, sy * 0.135, zl + 0.03), V(sx * 0.135, sy * 0.135, zl + 0.40), 0.011, 6)
    for dx, dy, w, d in ((0, 0.135, 0.27, 0.004), (0, -0.135, 0.27, 0.004), (0.135, 0, 0.004, 0.27), (-0.135, 0, 0.004, 0.27)):
        box(glass, V(dx, dy, zl + 0.215), V(w, d, 0.36))
    lathe(iron, [(0.0, 0.0), (0.05, 0.0), (0.05, 0.02), (0.0, 0.02)], c=V(0, 0, zl + 0.03), segs=10)
    flame(fire, V(0, 0, zl + 0.05), 5.0)
    roof = bmesh.new()
    lathe(roof, [(0.24, 0.0), (0.22, 0.02), (0.0, 0.17)], c=V(0, 0, 0), segs=4)
    g3.merge(iron, roof, Matrix.Translation(V(0, 0, zl + 0.40)) @ Matrix.Rotation(radians(45), 4, 'Z'))
    sphere(iron, V(0, 0, zl + 0.585), 0.024, 8)
    cyl(iron, V(0, 0, zl + 0.58), V(0, 0, zl + 0.84), 0.006, 5)
    bat(iron, V(0, -0.006, zl + 0.74), s=1.5, thick=0.012)
    return A


def urna():
    """L'urna di pietra su un plinto: pancia a baccello con due anse ad anello, un medaglione di pipistrello d'ottone sul davanti e dentro la
    terra secca con sei rami nudi."""
    A = Asset4('gu_urna')
    st, br, bark, soil = A.bm('A_basalto'), A.bm('A_ottone'), A.bm('A_legno'), A.bm('A_terra')
    rnd = random.Random(4)
    box(st, V(0, 0, 0.06), V(0.56, 0.56, 0.12), bevel=0.012, seg=1)
    box(st, V(0, 0, 0.15), V(0.46, 0.46, 0.06), bevel=0.01, seg=1)
    lathe(st, [(0.0, 0.18), (0.14, 0.18), (0.13, 0.24), (0.17, 0.30), (0.23, 0.44), (0.265, 0.60), (0.25, 0.72), (0.20, 0.80), (0.235, 0.84), (0.26, 0.86),
               (0.26, 0.90), (0.225, 0.90), (0.0, 0.86)], c=V(0, 0, 0), segs=16)
    for sx in (-1, 1):
        loop_tube(st, ellipse(V(sx * 0.285, 0, 0.58), V(1, 0, 0), V(0, 0, 1), 0.055, 0.11, 14), 0.022, V(0, 1, 0), segs=6)
    bat(br, V(0, 0.232, 0.50), s=1.3, thick=0.014)
    lathe(soil, [(0.0, 0.0), (0.22, 0.0), (0.22, 0.012), (0.0, 0.02)], c=V(0, 0, 0.87), segs=14)
    for k in range(6):
        a = 2 * pi * k / 6 + rnd.uniform(-0.3, 0.3)
        d = V(cos(a) * rnd.uniform(0.25, 0.5), sin(a) * rnd.uniform(0.25, 0.5), 1.0).normalized()
        L = rnd.uniform(0.35, 0.6)
        p0 = V(cos(a) * 0.06, sin(a) * 0.06, 0.86)
        tube(bark, [p0, p0 + d * L * 0.4, p0 + d * L * 0.75 + V(rnd.uniform(-0.04, 0.04), rnd.uniform(-0.04, 0.04), 0), p0 + d * L],
             [0.016, 0.011, 0.006, 0.002], segs=4, n=8)
    return A


ASSETS = {
    'gu_armadio': lambda: armadio('gu_armadio'),
    'gu_armadio_aperto': lambda: armadio('gu_armadio_aperto', aperto=True),
    'gu_forbici': forbici,
    'gu_bara': lambda: bara('gu_bara'),
    'gu_bara_chiusa': lambda: bara('gu_bara_chiusa', chiusa=True),
    'gu_tavolo_sarto': tavolo_sarto,
    'gu_macchina_cucire': macchina_cucire,
    'gu_stoffe': stoffe,
    'gu_attaccapanni': attaccapanni,
    'gu_braciere': braciere,
    'gu_incudine': incudine,
    'gu_crogiolo': crogiolo,
    'gu_gargoyle': gargoyle,
    'gu_albero_secco': albero_secco,
    'gu_specchio': specchio,
    'gu_lampione': lampione,
    'gu_urna': urna,
}
VIEWS = {
    'gu_armadio': ((3.0, 4.9, 2.1), (0, 0, 1.30), 42), 'gu_armadio_aperto': ((3.5, 4.6, 2.0), (0, 0.2, 1.25), 42),
    'gu_forbici': ((2.4, 5.0, 1.7), (0, 0, 1.2), 42),
    'gu_bara': ((3.0, 3.2, 2.0), (0.2, 0, 0.75), 40), 'gu_bara_chiusa': ((3.0, 3.2, 2.0), (0.1, 0, 0.75), 40),
    'gu_tavolo_sarto': ((2.7, 3.4, 2.0), (0, 0, 0.6), 42), 'gu_macchina_cucire': ((1.5, 1.9, 1.35), (0, 0, 0.68), 42),
    'gu_stoffe': ((2.3, 3.8, 1.7), (0, 0, 0.95), 42), 'gu_attaccapanni': ((1.7, 3.0, 1.5), (0, 0, 0.92), 42),
    'gu_braciere': ((1.6, 2.6, 1.3), (0, 0, 0.55), 42), 'gu_incudine': ((1.9, 2.5, 1.3), (0.05, 0, 0.45), 42),
    'gu_crogiolo': ((2.4, 3.6, 1.8), (0, 0, 0.95), 42), 'gu_gargoyle': ((2.2, 3.8, 1.6), (0, 0, 0.95), 42),
    'gu_albero_secco': ((4.5, 7.5, 3.0), (0, 0, 1.6), 42), 'gu_specchio': ((1.8, 3.6, 1.5), (0, 0, 1.0), 42),
    'gu_lampione': ((2.6, 5.6, 1.8), (0, 0, 1.45), 42), 'gu_urna': ((1.4, 2.3, 1.1), (0, 0, 0.6), 42),
}


if __name__ == '__main__':
    ga.ASSETS.update(ASSETS)
    ga.VIEWS.update(VIEWS)
    ga.OUT = OUT
    if os.environ.get('ARREDI_GPU') != '1':
        ga.setup_cycles = g3.setup_cpu
    names = [a for a in ga.ARGS if a in ASSETS] or list(ASSETS)
    for nm in names:
        try:
            if 'build' in ga.ARGS:
                ga.stage([nm], 'build')
            if 'render' in ga.ARGS:
                ga.stage([nm], 'render')
        except Exception as e:          # un modello rotto non ferma gli altri
            import traceback
            print(f'[ERRORE] {nm}: {e}')
            traceback.print_exc()
