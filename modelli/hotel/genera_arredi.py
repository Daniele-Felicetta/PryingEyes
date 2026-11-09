"""
Arredi della hall (fase 4): divano e poltrona chesterfield imbottiti a bottoni, tavolino, banco della reception a
pannelli con piano di marmo, mobile delle chiavi a caselle, carrello dei bagagli, lampada da terra, orologio a pendolo e
palma morta in vaso. Sostituisce i divani di sfere e i parallelepipedi di HotelBuilder.

Uso (dalla cartella del file):
  blender -b -P genera_arredi.py -- build [nome ...]    FBX in arredi/<nome>.fbx (tutti se non si nomina nulla)
  blender -b -P genera_arredi.py -- render [nome ...]   anteprime Cycles in arredi/render/<nome>.png
Assi: Z in alto, la parte anteriore guarda verso +Y (in Unity diventa +Z), l'origine e' al centro, a terra. Unita': metri.

I materiali hanno il nome del ruolo (A_velluto, A_noce, A_ottone, A_marmo, ...): HotelBuilder li sostituisce coi suoi
materiali PBR della hall (stesse texture, stessa luce). Le UV sono in metri (proiezione a scatola): la ripetizione
della texture ha la misura giusta.
"""
import bpy, bmesh, math, os, sys, random
from mathutils import Vector, Matrix, noise
from math import sin, cos, pi, radians, sqrt

DIR = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(DIR, 'arredi')
ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []


def V(*a):
    return Vector(a)


def smoothstep(e0, e1, x):
    t = min(1.0, max(0.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


# ----------------------------------------------------------------------------- primitive
def box(bm, c, s, bevel=0.0, seg=3, rot=None):
    """Parallelepipedo centrato in c, lati s; spigoli arrotondati con 'bevel'."""
    verts = bmesh.ops.create_cube(bm, size=1.0)['verts']
    M = Matrix.Translation(c) @ (rot.to_4x4() if rot is not None else Matrix.Identity(4)) @ Matrix.Diagonal((s[0], s[1], s[2], 1.0))
    bmesh.ops.transform(bm, matrix=M, verts=verts)
    if bevel > 0:
        edges = list({e for v in verts for e in v.link_edges})
        bmesh.ops.bevel(bm, geom=edges, offset=bevel, segments=seg, profile=0.5, affect='EDGES')
    return verts


def grid_box(bm, nx, ny, nz):
    """Superficie di un cubo unitario (-0.5..0.5) suddiviso nx x ny x nz, maglia saldata (vertici condivisi sugli spigoli).
    Ritorna i vertici creati. Le normali le sistema poi recalc_face_normals (superficie chiusa)."""
    verts = {}

    def vert(i, j, k):
        key = (i, j, k)
        v = verts.get(key)
        if v is None:
            v = bm.verts.new(Vector((i / nx - 0.5, j / ny - 0.5, k / nz - 0.5)))
            verts[key] = v
        return v
    for i in range(nx):
        for k in range(nz):
            bm.faces.new((vert(i, ny, k), vert(i + 1, ny, k), vert(i + 1, ny, k + 1), vert(i, ny, k + 1)))      # +Y
            bm.faces.new((vert(i, 0, k), vert(i, 0, k + 1), vert(i + 1, 0, k + 1), vert(i + 1, 0, k)))          # -Y
    for i in range(nx):
        for j in range(ny):
            bm.faces.new((vert(i, j, nz), vert(i + 1, j, nz), vert(i + 1, j + 1, nz), vert(i, j + 1, nz)))      # +Z
            bm.faces.new((vert(i, j, 0), vert(i, j + 1, 0), vert(i + 1, j + 1, 0), vert(i + 1, j, 0)))          # -Z
    for j in range(ny):
        for k in range(nz):
            bm.faces.new((vert(nx, j, k), vert(nx, j + 1, k), vert(nx, j + 1, k + 1), vert(nx, j, k + 1)))      # +X
            bm.faces.new((vert(0, j, k), vert(0, j, k + 1), vert(0, j + 1, k + 1), vert(0, j + 1, k)))          # -X
    return list(verts.values())


def rounded_grid(bm, c, s, r, nx=10, ny=None, nz=None):
    """Parallelepipedo a maglia regolare proiettato su un parallelepipedo arrotondato (raggio r): pronto per essere
    deformato (cuscini, imbottiture a bottoni). Risoluzione per asse: nx, ny, nz."""
    allv = grid_box(bm, nx, ny or nx, nz or nx)
    hx, hy, hz = s[0] / 2, s[1] / 2, s[2] / 2
    for v in allv:
        p = Vector((v.co.x * s[0], v.co.y * s[1], v.co.z * s[2]))
        q = Vector((max(-hx + r, min(hx - r, p.x)), max(-hy + r, min(hy - r, p.y)), max(-hz + r, min(hz - r, p.z))))
        d = p - q
        v.co = Vector(c) + (q + d.normalized() * r if d.length > 1e-9 else q)
    return allv


def lathe(bm, profile, c=V(0, 0, 0), segs=28, axis='Z', closed_top=True, closed_bottom=True, loop=False):
    """Solido di rivoluzione: profile = [(raggio, quota), ...] dal basso in alto. Un raggio nullo e' un polo (un solo
    vertice); loop=True collega l'ultimo anello al primo (profilo chiuso = guscio con spessore)."""
    rings = []
    for r, h in profile:
        if r < 1e-6:
            rings.append([bm.verts.new(Vector(c) + (V(0, 0, h) if axis == 'Z' else V(0, h, 0) if axis == 'Y' else V(h, 0, 0)))])
            continue
        ring = []
        for j in range(segs):
            ang = 2 * pi * j / segs
            if axis == 'Z':
                p = V(cos(ang) * r, sin(ang) * r, h)
            elif axis == 'Y':
                p = V(cos(ang) * r, h, sin(ang) * r)
            else:
                p = V(h, cos(ang) * r, sin(ang) * r)
            ring.append(bm.verts.new(Vector(c) + p))
        rings.append(ring)

    def link(a, b):
        for j in range(segs):
            if len(a) == 1 and len(b) == 1:
                continue
            if len(a) == 1:
                bm.faces.new((a[0], b[(j + 1) % segs], b[j]))
            elif len(b) == 1:
                bm.faces.new((a[j], a[(j + 1) % segs], b[0]))
            else:
                bm.faces.new((a[j], a[(j + 1) % segs], b[(j + 1) % segs], b[j]))
    for i in range(len(rings) - 1):
        link(rings[i], rings[i + 1])
    if loop:
        link(rings[-1], rings[0])
    if closed_bottom and len(rings[0]) > 1:
        bm.faces.new(rings[0][::-1])
    if closed_top and len(rings[-1]) > 1:
        bm.faces.new(rings[-1])
    return rings


def sphere(bm, c, r, seg=14):
    bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=max(6, seg // 2), radius=r, matrix=Matrix.Translation(Vector(c)))


def cyl(bm, a, b, r, segs=14, cap=True):
    """Cilindro tra due punti."""
    a, b = Vector(a), Vector(b)
    d = b - a
    q = d.to_track_quat('Z', 'Y')
    M = Matrix.Translation((a + b) / 2) @ q.to_matrix().to_4x4()
    bmesh.ops.create_cone(bm, cap_ends=cap, cap_tris=False, segments=segs, radius1=r, radius2=r, depth=d.length, matrix=M)


def tube(bm, pts, radii, segs=10, n=40):
    """Tubo morbido lungo una spezzata liscia (Catmull-Rom)."""
    pts = [Vector(p) for p in pts]
    ext = [pts[0] * 2 - pts[1]] + pts + [pts[-1] * 2 - pts[-2]]
    curve = []
    nseg = len(pts) - 1
    for i in range(n + 1):
        u = i / n * nseg
        j = min(int(u), nseg - 1)
        t = u - j
        p0, p1, p2, p3 = ext[j], ext[j + 1], ext[j + 2], ext[j + 3]
        curve.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3))
    grid = []
    nrm = (curve[1] - curve[0]).cross(V(0, 0, 1))
    if nrm.length < 1e-3:
        nrm = (curve[1] - curve[0]).cross(V(1, 0, 0))
    nrm.normalize()
    for i, c in enumerate(curve):
        tg = (curve[min(i + 1, n)] - curve[max(i - 1, 0)]).normalized()
        nrm = (nrm - tg * nrm.dot(tg)).normalized()
        bn = tg.cross(nrm)
        q = i / n * (len(radii) - 1)
        k = min(int(q), len(radii) - 2)
        r = radii[k] + (radii[k + 1] - radii[k]) * (q - k)
        grid.append([bm.verts.new(c + (nrm * cos(2 * pi * j / segs) + bn * sin(2 * pi * j / segs)) * r) for j in range(segs)])
    for i in range(n):
        for j in range(segs):
            bm.faces.new((grid[i][j], grid[i][(j + 1) % segs], grid[i + 1][(j + 1) % segs], grid[i + 1][j]))
    bm.faces.new(grid[0][::-1])
    bm.faces.new(grid[-1])
    return curve


# ----------------------------------------------------------------------------- oggetti e materiali
ROLE = {   # nome del ruolo -> colore/metallo/ruvidezza per le anteprime (in Unity li sostituisce HotelBuilder)
    'A_velluto': ((0.10, 0.30, 0.22), 0.0, 0.85), 'A_noce': ((0.17, 0.09, 0.045), 0.0, 0.38),
    'A_ottone': ((0.72, 0.52, 0.17), 1.0, 0.32), 'A_marmo': ((0.80, 0.78, 0.74), 0.0, 0.15),
    'A_cuoio': ((0.30, 0.15, 0.07), 0.0, 0.5), 'A_vaso': ((0.50, 0.26, 0.16), 0.0, 0.8),
    'A_terra': ((0.06, 0.045, 0.03), 0.0, 1.0), 'A_foglia': ((0.22, 0.20, 0.07), 0.0, 0.7),
    'A_paralume': ((0.95, 0.80, 0.55), 0.0, 0.9), 'A_vetro': ((0.8, 0.9, 0.9), 0.0, 0.05),
    'A_scuro': ((0.03, 0.03, 0.035), 0.0, 0.6), 'A_fondo': ((0.04, 0.09, 0.06), 0.0, 0.95),
    'A_quadrante': ((0.82, 0.78, 0.66), 0.0, 0.5),
}
_mats = {}


def material(role):
    if role in _mats and _mats[role].name in bpy.data.materials:
        return _mats[role]
    col, met, rough = ROLE[role]
    m = bpy.data.materials.new(role)
    m.use_nodes = True
    bs = m.node_tree.nodes['Principled BSDF']
    bs.inputs['Base Color'].default_value = (*col, 1)
    bs.inputs['Metallic'].default_value = met
    bs.inputs['Roughness'].default_value = rough
    if role == 'A_paralume':
        bs.inputs['Emission Color'].default_value = (1.0, 0.72, 0.4, 1)
        bs.inputs['Emission Strength'].default_value = 2.0
    if role == 'A_vetro':
        bs.inputs['Transmission Weight'].default_value = 1.0
    _mats[role] = m
    return m


def sharpen(bm, angle=38.0):
    """Spigoli piu' netti di 'angle' gradi duri, il resto liscio."""
    lim = radians(angle)
    for f in bm.faces:
        f.smooth = True
    for e in bm.edges:
        if len(e.link_faces) == 2 and e.calc_face_angle(0.0) > lim:
            e.smooth = False


def box_uv(bm, scale=1.0, offset=(0.0, 0.0), recalc=True):
    """UV in metri con proiezione a scatola (asse dominante della normale della faccia)."""
    uvl = bm.loops.layers.uv.verify()
    if recalc:
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    for f in bm.faces:
        f.normal_update()
        n = f.normal
        ax, ay, az = abs(n.x), abs(n.y), abs(n.z)
        for lo in f.loops:
            p = lo.vert.co
            if az >= ax and az >= ay:
                uv = (p.x, p.y)
            elif ay >= ax:
                uv = (p.x, p.z)
            else:
                uv = (p.y, p.z)
            lo[uvl].uv = (uv[0] * scale + offset[0], uv[1] * scale + offset[1])


class Asset:
    """Un arredo: una mesh per ruolo di materiale, tutte figlie di un Empty col nome dell'arredo."""

    def __init__(self, name):
        self.name = name
        self.bms = {}

    def bm(self, role):
        if role not in self.bms:
            self.bms[role] = bmesh.new()
        return self.bms[role]

    def build(self):
        root = bpy.data.objects.new(self.name, None)
        bpy.context.scene.collection.objects.link(root)
        self.objs = []
        for role, bm in self.bms.items():
            box_uv(bm, recalc=(role != 'A_foglia'))
            sharpen(bm)
            me = bpy.data.meshes.new(f'{self.name}_{role[2:]}')
            bm.to_mesh(me)
            bm.free()
            ob = bpy.data.objects.new(f'{self.name}_{role[2:]}', me)
            ob.parent = root
            me.materials.append(material(role))
            bpy.context.scene.collection.objects.link(ob)
            self.objs.append(ob)
        self.root = root
        return root

    def tris(self):
        return sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in self.objs)


# ----------------------------------------------------------------------------- divano e poltrona chesterfield
def tufted_pad(bm, c, s, r, ncols, rows_z, depth=0.034, side=+1, margin=0.07, res=0.014):
    """Imbottitura a bottoni a rombi sul lato +Y (o -Y): maglia suddivisa, con affossamenti ai bottoni e pieghe lungo le
    diagonali che li uniscono. Ritorna le posizioni dei bottoni (in coordinate del mondo)."""
    verts = rounded_grid(bm, c, s, r, nx=max(24, int(s[0] / res)), ny=6, nz=max(16, int(s[2] / res)))
    hx, hz = s[0] / 2 - margin, s[2] / 2 - margin
    pts = []
    for ri, zr in enumerate(rows_z):
        n = ncols if ri % 2 == 0 else ncols - 1
        for ci in range(n):
            x = -hx + (2 * hx) * ((ci + (0.5 if ri % 2 else 0.0)) / (ncols - 1))
            pts.append((x, zr))
    segs = []
    # collegamenti diagonali: punti vicini in colonna alternata
    step_x = (2 * hx) / (ncols - 1) / 2.0
    step_z = abs(rows_z[1] - rows_z[0]) if len(rows_z) > 1 else 0.2
    for i, (x0, z0) in enumerate(pts):
        for j in range(i + 1, len(pts)):
            x1, z1 = pts[j]
            if abs(abs(x1 - x0) - step_x) < 1e-3 and abs(abs(z1 - z0) - step_z) < 1e-3:
                segs.append(((x0, z0), (x1, z1)))
    cx, cy, cz = c
    for v in verts:
        local = v.co - Vector(c)
        if side * local.y < s[1] / 2 * 0.55:
            continue
        x, z = local.x, local.z
        # dentro i bordi arrotondati l'effetto sfuma
        edge = smoothstep(0.0, 0.07, min(s[0] / 2 - abs(x), s[2] / 2 - abs(z)))
        d = 0.0
        for (px, pz) in pts:
            dd = (x - px) ** 2 + (z - pz) ** 2
            if dd < 0.03:
                d -= depth * math.exp(-dd / (2 * 0.020 ** 2))
        for (a, b) in segs:
            ax, az = a
            bx, bz = b
            vx, vz = bx - ax, bz - az
            L2 = vx * vx + vz * vz
            t = max(0.0, min(1.0, ((x - ax) * vx + (z - az) * vz) / L2))
            dd = (x - (ax + t * vx)) ** 2 + (z - (az + t * vz)) ** 2
            if dd < 0.0030:
                d -= depth * 0.50 * math.exp(-dd / (2 * 0.013 ** 2)) * (0.65 + 0.35 * sin(pi * t))
        # rigonfiamento tra i bottoni: effetto cuscino
        bulge = 0.016 * (1 - (2 * x / s[0]) ** 4)
        v.co.y += side * (d + bulge) * edge
    return [(Vector((cx + x, cy + side * (s[1] / 2 - depth * 1.0), cz + z))) for x, z in pts]


def chesterfield(name, W, D=0.95, arm_w=0.22, ncols=6, back_h=0.88, arm_h=0.68):
    A = Asset(name)
    vel, wood = A.bm('A_velluto'), A.bm('A_noce')
    seat_top = 0.48
    # struttura imbottita
    box(vel, V(0, 0, 0.245), V(W - 0.02, D - 0.02, 0.27), bevel=0.04, seg=4)
    # cuscino del sedile: piu' soffice e rigonfio al centro
    sw, sd = W - 2 * arm_w + 0.02, D - 0.30
    rounded_grid(vel, V(0, 0.07, seat_top - 0.065), V(sw, sd, 0.14), 0.065, nx=36, ny=16, nz=6)
    # schienale a bottoni
    back_t = 0.25
    bw = W - 2 * arm_w + 0.06
    by = -D / 2 + back_t / 2 + 0.01
    bz0, bz1 = 0.36, back_h
    hh = bz1 - bz0
    rows = [-0.30 * hh, 0.0, 0.30 * hh]            # righe di bottoni, relative al centro dell'imbottitura
    pts = tufted_pad(vel, V(0, by, (bz0 + bz1) / 2), V(bw, back_t, bz1 - bz0), 0.07, ncols, rows, depth=0.042, side=+1)
    # braccioli a rotolo, con la fronte tonda e il bottone al centro
    for sx in (-1, 1):
        ax = sx * (W / 2 - arm_w / 2)
        rr = 0.142
        zc = arm_h - rr
        box(vel, V(ax, 0, (zc + 0.10) / 2), V(arm_w, D - 0.02, zc - 0.10), bevel=0.02, seg=3)
        lathe(vel, [(0.0, -(D - 0.02) / 2), (rr * 0.95, -(D - 0.02) / 2 + 0.01), (rr, -(D - 0.02) / 2 + 0.05), (rr, (D - 0.02) / 2 - 0.05),
                    (rr * 0.98, (D - 0.02) / 2 - 0.012), (rr * 0.78, (D - 0.02) / 2 + 0.014), (rr * 0.5, (D - 0.02) / 2 + 0.03), (0.0, (D - 0.02) / 2 + 0.035)],
              c=V(ax, 0, zc), segs=32, axis='Y')
        sphere(vel, V(ax, (D - 0.02) / 2 + 0.034, zc), 0.019, 12)
        # bottoni sul retro e sul fianco
        sphere(vel, V(ax, -(D - 0.02) / 2 - 0.004, zc), 0.019, 12)
    for p in pts:
        sphere(vel, p + V(0, 0.003, 0), 0.016, 10)
    # piedini torniti
    foot = [(0.032, 0.0), (0.050, 0.012), (0.036, 0.032), (0.028, 0.050), (0.046, 0.080), (0.060, 0.098), (0.055, 0.112)]
    for sx in (-1, 1):
        for sy in (-1, 1):
            lathe(wood, foot, c=V(sx * (W / 2 - 0.13), sy * (D / 2 - 0.13), 0.0), segs=20)
    return A


# ----------------------------------------------------------------------------- tavolino
def tavolino():
    A = Asset('arr_tavolino')
    wood, brass, marble = A.bm('A_noce'), A.bm('A_ottone'), A.bm('A_marmo')
    L, Wd, H = 1.35, 0.68, 0.48
    box(wood, V(0, 0, H - 0.03), V(L, Wd, 0.06), bevel=0.014, seg=3)
    box(marble, V(0, 0, H - 0.003), V(L - 0.16, Wd - 0.16, 0.012), bevel=0.003, seg=2)
    # cornice d'ottone attorno al marmo
    for sx in (-1, 1):
        box(brass, V(sx * (L - 0.15) / 2, 0, H - 0.0035), V(0.012, Wd - 0.15, 0.014))
    for sy in (-1, 1):
        box(brass, V(0, sy * (Wd - 0.15) / 2, H - 0.0035), V(L - 0.138, 0.012, 0.014))
    # grembiule
    for sy in (-1, 1):
        box(wood, V(0, sy * (Wd / 2 - 0.05), H - 0.09), V(L - 0.14, 0.03, 0.10), bevel=0.006)
    for sx in (-1, 1):
        box(wood, V(sx * (L / 2 - 0.05), 0, H - 0.09), V(0.03, Wd - 0.14, 0.10), bevel=0.006)
    # gambe tornite e piano basso
    leg = [(0.030, 0.0), (0.036, 0.03), (0.026, 0.07), (0.024, 0.16), (0.040, 0.21), (0.033, 0.25), (0.024, 0.30), (0.036, 0.37), (0.052, 0.41), (0.050, 0.45)]
    for sx in (-1, 1):
        for sy in (-1, 1):
            c = V(sx * (L / 2 - 0.07), sy * (Wd / 2 - 0.07), 0.0)
            lathe(wood, leg, c=c, segs=20)
            cyl(brass, c + V(0, 0, 0.0), c + V(0, 0, 0.02), 0.032, 16)
    box(wood, V(0, 0, 0.13), V(L - 0.16, Wd - 0.18, 0.025), bevel=0.008)
    for sx in (-1, 1):
        box(wood, V(sx * (L / 2 - 0.07), 0, 0.13), V(0.02, Wd - 0.18, 0.04))
    return A


# ----------------------------------------------------------------------------- banco della reception
def banco(L=4.0):
    A = Asset('arr_banco')
    wood, brass, marble = A.bm('A_noce'), A.bm('A_ottone'), A.bm('A_marmo')
    Dp, H = 1.04, 1.12
    # basamento e corpo
    box(wood, V(0, 0, 0.07), V(L - 0.02, Dp + 0.06, 0.14), bevel=0.012)
    box(wood, V(0, 0, 0.14 + (H - 0.14 - 0.1) / 2), V(L - 0.08, Dp, H - 0.14 - 0.10), bevel=0.01)
    # cornice sotto il piano
    box(wood, V(0, 0.01, H - 0.07), V(L - 0.02, Dp + 0.08, 0.06), bevel=0.014)
    box(wood, V(0, 0.015, H - 0.12), V(L - 0.04, Dp + 0.05, 0.05), bevel=0.01)
    # pannelli in rilievo con cornici sul lato clienti (+Y)
    n = 4
    pw = (L - 0.2) / n
    for i in range(n):
        cx = -L / 2 + 0.1 + pw * (i + 0.5)
        box(wood, V(cx, Dp / 2 + 0.01, 0.62), V(pw - 0.14, 0.03, 0.78), bevel=0.012)
        # cornice a bordo + riquadro interno
        box(wood, V(cx, Dp / 2 + 0.022, 0.62), V(pw - 0.22, 0.014, 0.66), bevel=0.006)
        # medaglione d'ottone al centro del pannello
        lathe(brass, [(0.0, 0.0), (0.060, 0.0), (0.066, 0.006), (0.05, 0.016), (0.018, 0.02), (0.0, 0.02)], c=V(cx, Dp / 2 + 0.03, 0.62), segs=24, axis='Y')
    # lesene scanalate tra i pannelli
    for i in range(n + 1):
        cx = -L / 2 + 0.1 + pw * i
        lathe(wood, [(0.06, 0.16), (0.066, 0.17), (0.058, 0.20), (0.058, 0.96), (0.066, 0.99), (0.075, 1.0)], c=V(cx, Dp / 2 + 0.03, 0.0), segs=18)
    # piano di marmo con bordo bombato e filo d'ottone
    box(marble, V(0, 0.08, H + 0.025), V(L + 0.04, Dp + 0.42, 0.05), bevel=0.02, seg=4)
    box(brass, V(0, 0.08 + (Dp + 0.42) / 2 + 0.003, H + 0.03), V(L + 0.05, 0.008, 0.02))
    # poggiapiedi d'ottone sul lato clienti
    cyl(brass, V(-L / 2 + 0.2, Dp / 2 + 0.16, 0.16), V(L / 2 - 0.2, Dp / 2 + 0.16, 0.16), 0.028, 18)
    for x in np_linspace(-L / 2 + 0.3, L / 2 - 0.3, 4):
        cyl(brass, V(x, Dp / 2 + 0.03, 0.16), V(x, Dp / 2 + 0.16, 0.16), 0.016, 12)
    return A


def np_linspace(a, b, n):
    return [a + (b - a) * i / (n - 1) for i in range(n)]


# ----------------------------------------------------------------------------- mobile delle chiavi
def chiavi(W=5.0):
    A = Asset('arr_chiavi')
    wood, brass, back = A.bm('A_noce'), A.bm('A_ottone'), A.bm('A_fondo')
    Dp, H = 0.42, 2.72
    # armadio basso a ante
    box(wood, V(0, 0, 0.45), V(W, Dp, 0.9), bevel=0.012)
    nd = 8
    dw = (W - 0.1) / nd
    for i in range(nd):
        cx = -W / 2 + 0.05 + dw * (i + 0.5)
        box(wood, V(cx, Dp / 2 + 0.008, 0.46), V(dw - 0.08, 0.02, 0.72), bevel=0.01)
        box(wood, V(cx, Dp / 2 + 0.02, 0.46), V(dw - 0.2, 0.012, 0.58), bevel=0.005)
        cyl(brass, V(cx + dw * 0.3, Dp / 2 + 0.03, 0.46), V(cx + dw * 0.3, Dp / 2 + 0.05, 0.46), 0.012, 10)
    box(wood, V(0, 0.01, 0.93), V(W + 0.06, Dp + 0.06, 0.06), bevel=0.014)           # piano di lavoro
    # nicchie: fondo di feltro, montanti e ripiani
    z0, z1 = 0.96, 2.52
    box(back, V(0, -Dp / 2 + 0.03, (z0 + z1) / 2), V(W - 0.02, 0.03, z1 - z0))
    ncol, nrow = 20, 7
    cw, rh = (W - 0.06) / ncol, (z1 - z0) / nrow
    for i in range(ncol + 1):
        box(wood, V(-W / 2 + 0.03 + cw * i, 0.0, (z0 + z1) / 2), V(0.016, Dp - 0.06, z1 - z0))
    for j in range(nrow + 1):
        box(wood, V(0, 0.0, z0 + rh * j), V(W - 0.02, Dp - 0.06, 0.016))
    rnd = random.Random(12)
    for i in range(ncol):
        for j in range(nrow):
            if rnd.random() < 0.38:
                continue           # nicchia vuota: chiave portata via
            x = -W / 2 + 0.03 + cw * (i + 0.5)
            z = z0 + rh * (j + 1) - 0.035
            cyl(brass, V(x, 0.03, z), V(x, 0.05, z), 0.004, 8)                 # gancio
            box(brass, V(x, 0.07, z - 0.025), V(0.008, 0.008, 0.07), bevel=0.002)   # chiave
            box(brass, V(x, 0.07, z - 0.065), V(0.034, 0.012, 0.045), bevel=0.004)  # cartellino col numero
    # cornice e frontone
    box(wood, V(0, 0.0, H - 0.1), V(W + 0.14, Dp + 0.10, 0.1), bevel=0.014)
    box(wood, V(0, 0.0, H - 0.01), V(W + 0.06, Dp + 0.04, 0.08), bevel=0.012)
    box(wood, V(0, 0.0, H + 0.06), V(W - 0.4, Dp - 0.1, 0.07), bevel=0.012)
    for sx in (-1, 1):
        box(wood, V(sx * (W / 2 + 0.0), 0, (z0 + z1) / 2 + 0.04), V(0.05, Dp, z1 - z0 + 0.12), bevel=0.008)
    return A


# ----------------------------------------------------------------------------- carrello dei bagagli
def carrello():
    A = Asset('arr_carrello')
    brass, wood, dark, leather = A.bm('A_ottone'), A.bm('A_noce'), A.bm('A_scuro'), A.bm('A_cuoio')
    L, Wd = 1.05, 0.62
    box(wood, V(0, 0, 0.16), V(L, Wd, 0.035), bevel=0.008)
    for sx in (-1, 1):
        for sy in (-1, 1):
            c = V(sx * (L / 2 - 0.07), sy * (Wd / 2 + 0.01), 0.075)
            cyl(dark, c - V(0, 0.03 * sy * 0.0, 0), c + V(0, 0.04 * sy, 0), 0.075, 22)
            cyl(brass, c + V(0, 0.04 * sy, 0), c + V(0, 0.046 * sy, 0), 0.03, 14)
    # telaio ad anello attorno alla pedana, due montanti, traversa e pomelli
    for y in (-1, 1):
        cyl(brass, V(-L / 2, y * Wd / 2, 0.20), V(L / 2, y * Wd / 2, 0.20), 0.011, 12)
    for x in (-1, 1):
        cyl(brass, V(x * L / 2, -Wd / 2, 0.20), V(x * L / 2, Wd / 2, 0.20), 0.011, 12)
    for sx in (-1, 1):
        x = sx * (L / 2 - 0.04)
        cyl(brass, V(x, -Wd / 2 + 0.03, 0.14), V(x, -Wd / 2 + 0.03, 1.55), 0.014, 14)
        sphere(brass, V(x, -Wd / 2 + 0.03, 1.575), 0.028, 14)
        cyl(brass, V(x, -Wd / 2 + 0.03, 0.80), V(x, Wd / 2 - 0.03, 0.20), 0.009, 10)
    cyl(brass, V(-L / 2 + 0.04, -Wd / 2 + 0.03, 1.50), V(L / 2 - 0.04, -Wd / 2 + 0.03, 1.50), 0.013, 14)
    cyl(brass, V(-L / 2 + 0.04, -Wd / 2 + 0.03, 0.85), V(L / 2 - 0.04, -Wd / 2 + 0.03, 0.85), 0.009, 12)
    # bagagli: due valigie di cuoio e una cappelliera
    rounded_grid(leather, V(-0.05, 0.02, 0.40), V(0.78, 0.34, 0.45), 0.04, nx=14, ny=8, nz=10)
    rounded_grid(leather, V(0.03, 0.02, 0.76), V(0.60, 0.30, 0.30), 0.04, nx=12, ny=8, nz=8)
    lathe(leather, [(0.0, 0.0), (0.20, 0.0), (0.21, 0.012), (0.21, 0.24), (0.205, 0.255), (0.0, 0.255)], c=V(0.31, 0.0, 0.18), segs=26)
    for x in (-0.25, 0.20):
        box(brass, V(x, 0.02, 0.40), V(0.03, 0.352, 0.455), bevel=0.004)
        box(brass, V(x + 0.05, 0.02, 0.76), V(0.03, 0.312, 0.305), bevel=0.004)
    box(brass, V(-0.05, 0.195, 0.43), V(0.05, 0.012, 0.03), bevel=0.003)
    return A


# ----------------------------------------------------------------------------- lampada da terra
def lampada():
    A = Asset('arr_lampada')
    brass, shade = A.bm('A_ottone'), A.bm('A_paralume')
    lathe(brass, [(0.0, 0.0), (0.20, 0.0), (0.21, 0.01), (0.19, 0.03), (0.14, 0.05), (0.07, 0.08), (0.03, 0.12), (0.016, 0.16)], segs=32)
    stem = [(0.016, 0.16), (0.020, 0.30), (0.014, 0.34), (0.026, 0.40), (0.014, 0.46), (0.012, 0.70), (0.022, 0.78), (0.012, 0.86),
            (0.012, 1.20), (0.020, 1.28), (0.012, 1.34), (0.014, 1.40)]
    lathe(brass, stem, segs=18, closed_bottom=False)
    # paralume: guscio a tronco di cono con bordo (superficie doppia = spessore)
    shell = [(0.145, 1.38), (0.150, 1.385), (0.255, 1.12), (0.258, 1.115), (0.248, 1.115), (0.143, 1.375)]
    lathe(shade, [(0.256, 1.115), (0.147, 1.385), (0.140, 1.38), (0.249, 1.11)], segs=36, closed_top=False, closed_bottom=False, loop=True)
    cyl(brass, V(0, 0, 1.36), V(0, 0, 1.43), 0.005, 8)
    lathe(brass, [(0.014, 1.40), (0.03, 1.405), (0.034, 1.42), (0.0, 1.46)], segs=14)
    return A


# ----------------------------------------------------------------------------- orologio a pendolo
def orologio():
    A = Asset('arr_orologio')
    wood, brass, glass, face = A.bm('A_noce'), A.bm('A_ottone'), A.bm('A_vetro'), A.bm('A_quadrante')
    box(wood, V(0, 0, 0.14), V(0.56, 0.34, 0.28), bevel=0.016)
    box(wood, V(0, 0, 0.30), V(0.50, 0.30, 0.06), bevel=0.012)
    box(wood, V(0, 0, 1.10), V(0.40, 0.25, 1.62), bevel=0.012)                 # fusto
    box(wood, V(0, 0.126, 0.99), V(0.30, 0.01, 1.04), bevel=0.004)               # incorniciatura della finestra
    box(glass, V(0, 0.134, 0.99), V(0.26, 0.004, 1.00))
    # pendolo e lenticchia
    cyl(brass, V(0, 0.0, 1.30), V(0, 0.0, 0.68), 0.004, 8)
    lathe(brass, [(0.0, 0.0), (0.075, 0.0), (0.082, 0.01), (0.075, 0.02), (0.0, 0.02)], c=V(0, 0.0, 0.64), segs=26, axis='Y')
    # cassa del movimento e quadrante
    box(wood, V(0, 0, 2.05), V(0.48, 0.28, 0.40), bevel=0.014)
    lathe(wood, [(0.0, 0.0), (0.25, 0.0), (0.25, 0.02), (0.0, 0.02)], c=V(0, 0.0, 2.23), segs=40, axis='Z')
    box(wood, V(0, 0, 2.30), V(0.54, 0.30, 0.06), bevel=0.012)
    lathe(wood, [(0.0, 2.33), (0.19, 2.33), (0.18, 2.40), (0.1, 2.48), (0.03, 2.52), (0.0, 2.53)], segs=30)
    lathe(face, [(0.0, 0.0), (0.165, 0.0), (0.165, 0.004), (0.0, 0.004)], c=V(0, 0.145, 2.07), segs=48, axis='Y')
    lathe(brass, [(0.150, 0.0), (0.172, 0.0), (0.172, 0.014), (0.150, 0.014)], c=V(0, 0.145, 2.07), segs=48, axis='Y', closed_top=False, closed_bottom=False, loop=True)
    for i in range(12):
        a = 2 * pi * i / 12
        cyl(brass, V(sin(a) * 0.125, 0.15, 2.07 + cos(a) * 0.125), V(sin(a) * 0.145, 0.15, 2.07 + cos(a) * 0.145), 0.004 if i % 3 else 0.007, 6)
    cyl(brass, V(0, 0.152, 2.07), V(sin(radians(305)) * 0.07, 0.152, 2.07 + cos(radians(305)) * 0.07), 0.006, 8)
    cyl(brass, V(0, 0.156, 2.07), V(sin(radians(60)) * 0.115, 0.156, 2.07 + cos(radians(60)) * 0.115), 0.004, 8)
    sphere(brass, V(0, 0.158, 2.07), 0.012, 10)
    sphere(brass, V(0, 0.0, 2.55), 0.028, 12)
    return A


# ----------------------------------------------------------------------------- palma morta in vaso
def palma():
    A = Asset('arr_palma')
    pot, soil, leaf, wood = A.bm('A_vaso'), A.bm('A_terra'), A.bm('A_foglia'), A.bm('A_noce')
    lathe(pot, [(0.0, 0.0), (0.17, 0.0), (0.20, 0.02), (0.27, 0.30), (0.30, 0.46), (0.33, 0.50), (0.33, 0.55), (0.29, 0.55), (0.28, 0.50),
                (0.0, 0.44)], segs=36)
    lathe(soil, [(0.0, 0.50), (0.28, 0.50), (0.27, 0.46), (0.0, 0.45)], segs=28, closed_bottom=False)
    rnd = random.Random(4)
    # fusto contorto e nodoso
    trunk = tube(wood, [V(0, 0, 0.48), V(0.03, 0.01, 0.95), V(-0.04, 0.02, 1.45), V(0.0, -0.02, 1.95), V(0.04, 0.0, 2.25)],
                 [0.058, 0.050, 0.044, 0.040, 0.026], segs=12, n=44)
    top = V(0.04, 0.0, 2.25)
    # fronde secche: una nervatura arcuata con foglioline appese
    nfr = 9
    for i in range(nfr):
        a = 2 * pi * i / nfr + rnd.uniform(-0.2, 0.2)
        d = V(cos(a), sin(a), 0)
        length = rnd.uniform(1.25, 1.65)
        up = rnd.uniform(0.25, 0.95)
        dead = rnd.random() < 0.5
        pts = []
        for k in range(13):
            t = k / 12
            p = top + d * (length * t * (0.85 if not dead else 0.75)) + V(0, 0, up * length * 0.55 * sin(pi * t * 0.85) - (length * 0.5 if dead else length * 0.22) * t * t)
            pts.append(p)
        # nervatura
        for k in range(12):
            cyl(leaf, pts[k], pts[k + 1], 0.011 * (1 - k / 14), 6)
        side = d.cross(V(0, 0, 1)).normalized()
        for k in range(1, 12):
            t = k / 12
            tg = (pts[k + 1] - pts[k - 1]).normalized()
            for sgn in (-1, 1):
                ll = length * 0.34 * (1 - t * 0.7) * rnd.uniform(0.85, 1.1)
                droop = V(0, 0, -1) * ll * (0.55 + 0.35 * t)
                tipp = pts[k] + side * sgn * ll * 0.62 + tg * ll * 0.35 + droop
                p0 = bm_v(leaf, pts[k] - side * sgn * 0.003)
                p1 = bm_v(leaf, pts[k] + tg * 0.05 + side * sgn * 0.003)
                p2 = bm_v(leaf, tipp)
                leaf.faces.new((p0, p1, p2))
                q0, q1, q2 = (bm_v(leaf, v.co) for v in (p0, p1, p2))
                leaf.faces.new((q2, q1, q0))
    return A


def bm_v(bm, p):
    return bm.verts.new(Vector(p))


ASSETS = {
    'arr_divano': lambda: chesterfield('arr_divano', 2.40, D=0.98, arm_w=0.23, ncols=7, back_h=0.90, arm_h=0.70),
    'arr_poltrona': lambda: chesterfield('arr_poltrona', 1.05, D=0.92, arm_w=0.21, ncols=4, back_h=0.88, arm_h=0.68),
    'arr_tavolino': tavolino,
    'arr_banco': banco,
    'arr_chiavi': chiavi,
    'arr_carrello': carrello,
    'arr_lampada': lampada,
    'arr_orologio': orologio,
    'arr_palma': palma,
}


# ----------------------------------------------------------------------------- scena, anteprime, esportazione
def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    _mats.clear()
    sc = bpy.context.scene
    sc.unit_settings.system = 'METRIC'
    return sc


def setup_cycles(samples=48, res=(900, 700)):
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    prefs = bpy.context.preferences.addons['cycles'].preferences
    prefs.compute_device_type = 'OPTIX'
    prefs.refresh_devices()
    for d in prefs.devices:
        d.use = d.type == 'OPTIX'
    sc.cycles.device = 'GPU'
    sc.cycles.samples = samples
    sc.cycles.use_denoising = True
    sc.cycles.denoiser = 'OPTIX'
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.image_settings.file_format = 'PNG'
    sc.view_settings.view_transform = 'AgX'
    sc.view_settings.look = 'AgX - Medium High Contrast'


def look_at(ob, target):
    d = Vector(target) - ob.location
    ob.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()


def select_only(obs, active=None):
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in obs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = active or obs[0]


# inquadrature per le anteprime: (posizione, bersaglio, lunghezza focale)
VIEWS = {
    'arr_divano': ((2.4, 3.2, 1.35), (0, 0, 0.45), 40), 'arr_poltrona': ((1.8, 2.6, 1.3), (0, 0, 0.45), 40),
    'arr_tavolino': ((1.8, 1.9, 1.1), (0, 0, 0.25), 40), 'arr_banco': ((2.6, 4.4, 2.0), (0, 0, 0.6), 38),
    'arr_chiavi': ((2.4, 5.2, 2.4), (0, 0, 1.4), 36), 'arr_carrello': ((2.2, 2.6, 1.5), (0, 0, 0.7), 38),
    'arr_lampada': ((2.0, 2.4, 1.2), (0, 0, 0.75), 38), 'arr_orologio': ((2.0, 3.0, 1.6), (0, 0, 1.2), 38),
    'arr_palma': ((2.8, 3.4, 1.8), (0, 0, 1.1), 40),
}


def stage(names, mode):
    for nm in names:
        sc = reset()
        A = ASSETS[nm]()
        A.build()
        print(f'[{mode}] {nm}: {A.tris()} triangoli, mesh: {[o.name for o in A.objs]}')
        if mode == 'render':
            setup_cycles(int(os.environ.get('ARREDI_SAMPLES', '48')))
            w = bpy.data.worlds.new('mondo')
            sc.world = w
            w.use_nodes = True
            w.node_tree.nodes['Background'].inputs['Color'].default_value = (0.05, 0.05, 0.055, 1)
            # pavimento e luci
            bm = bmesh.new()
            bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=6)
            me = bpy.data.meshes.new('pav')
            bm.to_mesh(me)
            fl = bpy.data.objects.new('pav', me)
            bpy.context.scene.collection.objects.link(fl)
            fm = bpy.data.materials.new('pavm')
            fm.use_nodes = True
            fm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.25, 0.24, 0.23, 1)
            me.materials.append(fm)
            for nme, loc, pw, size, col in (('key', (-2.0, 3.0, 3.0), 380, 2.0, (1.0, 0.85, 0.65)),
                                            ('rim', (2.6, -2.5, 2.6), 220, 1.5, (0.65, 0.75, 1.0)),
                                            ('fill', (3.0, 2.0, 0.8), 60, 3.0, (1.0, 0.9, 0.85))):
                l = bpy.data.lights.new(nme, 'AREA')
                l.energy, l.size, l.color = pw, size, col
                lo = bpy.data.objects.new(nme, l)
                lo.location = loc
                look_at(lo, (0, 0, 0.8))
                bpy.context.scene.collection.objects.link(lo)
            loc, tgt, lens = VIEWS[nm]
            cam = bpy.data.objects.new('camera', bpy.data.cameras.new('camera'))
            cam.location = loc
            cam.data.lens = lens
            look_at(cam, tgt)
            bpy.context.scene.collection.objects.link(cam)
            sc.camera = cam
            os.makedirs(os.path.join(OUT, 'render'), exist_ok=True)
            sc.render.filepath = os.path.join(OUT, 'render', nm + '.png')
            bpy.ops.render.render(write_still=True)
        else:
            os.makedirs(OUT, exist_ok=True)
            select_only([A.root] + A.objs, A.root)
            bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, nm + '.fbx'), use_selection=True,
                                     object_types={'EMPTY', 'MESH'}, apply_unit_scale=True,
                                     apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
                                     mesh_smooth_type='EDGE', use_tspace=True, add_leaf_bones=False,
                                     bake_anim=False, path_mode='RELATIVE', embed_textures=False)


if __name__ == '__main__':
    names = [a for a in ARGS if a in ASSETS] or list(ASSETS)
    if 'build' in ARGS:
        stage(names, 'build')
    if 'render' in ARGS:
        stage(names, 'render')
