"""
Kit di modellazione hard-surface per le armi (fase 5).

Ogni arma e' un oggetto Gun: si disegnano i pezzi con poche primitive (scatola con smusso, prisma da profilo laterale,
solido di rivoluzione lungo un asse, loft tra sezioni, sweep lungo una curva), si ritagliano con booleane e si ottiene
un FBX con un oggetto per ogni parte mobile (caricatore, carrello, pompa...) piu' il corpo fisso, i materiali per ruolo
(W_acciaio, W_polimero, ...) e dei Empty di aggancio (muzzle, ads, eject, grip_l, ...).

Assi dell'autore: x destra, y avanti (verso la bocca), z in alto, metri. L'origine e' il centro dell'impugnatura
(dove sta la mano destra); a fine lavoro tutto si trasla di OFFSET per coincidere col riferimento delle mani di
Weapon.cs (centro del fucile d'assalto di prima).
"""
import bpy, bmesh, math, os, sys
from math import sin, cos, pi, radians, sqrt, atan2
from mathutils import Vector, Matrix, Euler

DIR = os.path.dirname(os.path.abspath(__file__))
ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []

# l'impugnatura (centro) sta in G; le mani dell'FBX 'mani' stanno nel riferimento del vecchio fucile: G = (0, +0.10, -0.12) in assi Blender
OFFSET = Vector((0.0, 0.10, -0.12))


def V(x=0.0, y=0.0, z=0.0):
    return Vector((x, y, z))


def arma(f):
    """Registra una funzione che costruisce un Gun (la trova genera_armi.py)."""
    f.e_arma = True
    return f


def rotm(rot):
    if rot is None:
        return Matrix.Identity(4)
    return Euler((radians(rot[0]), radians(rot[1]), radians(rot[2])), 'XYZ').to_matrix().to_4x4()


# ----------------------------------------------------------------------------- sezioni
def rrect(hw, hh, rad=0.003, seg=3, cx=0.0, cz=0.0):
    """Rettangolo con angoli arrotondati (x, z), 4*(seg+1) punti: sezione per loft e sweep."""
    rad = max(0.0002, min(rad, hw - 1e-5, hh - 1e-5))
    pts = []
    for ccx, ccz, a0 in ((hw - rad, hh - rad, 0), (-hw + rad, hh - rad, 90), (-hw + rad, -hh + rad, 180), (hw - rad, -hh + rad, 270)):
        for k in range(seg + 1):
            a = radians(a0 + 90.0 * k / seg)
            pts.append((cx + ccx + rad * cos(a), cz + ccz + rad * sin(a)))
    return pts


def ellipse(a, b, n=20, cx=0.0, cz=0.0):
    return [(cx + a * cos(2 * pi * i / n), cz + b * sin(2 * pi * i / n)) for i in range(n)]


def cprof(y0, y1, r, ch=0.0006):
    """Profilo di un cilindro con gli spigoli smussati (per Gun.lathe)."""
    return [(y0, r - ch), (y0 + ch, r), (y1 - ch, r), (y1, r - ch)]


def arc(p0, p1, bulge, n=10):
    """Punti (y, z) da p0 a p1 con una freccia 'bulge' (positiva = verso la normale sinistra di p0->p1)."""
    p0, p1 = Vector(p0), Vector(p1)
    d = p1 - p0
    nrm = Vector((-d.y, d.x))
    nrm = nrm.normalized() if nrm.length > 1e-9 else nrm
    out = []
    for i in range(n + 1):
        t = i / n
        out.append(p0 + d * t + nrm * (bulge * 4 * t * (1 - t)))
    return [(p.x, p.y) for p in out]


# ----------------------------------------------------------------------------- l'arma
class Gun:
    def __init__(self, name, tex=2048):
        self.name = name
        self.tex = tex
        self.bms = {}
        self.cuts = {}
        self.pivots = {'corpo': Vector((0, 0, 0))}
        self.order = ['corpo']
        self.cur = 'corpo'
        self.empties = {}
        self.bevel = {}
        self.objs = {}
        self.root = None
        self.paint = {}
        self.parents = {}

    # -- parti
    def part(self, name, pivot=None, parent=None):
        """Da qui in poi le primitive vanno nella parte 'name' (oggetto separato, origine nel perno 'pivot')."""
        self.cur = name
        if parent is not None:
            self.parents[name] = parent
        if pivot is not None:
            self.pivots[name] = Vector(pivot)
        self.pivots.setdefault(name, Vector((0, 0, 0)))
        if name not in self.order:
            self.order.append(name)
        return self

    def empty(self, name, pos, fwd=None):
        """Segnaposto: posizione e (opzionale) direzione dell'asse y locale."""
        self.empties[name] = (Vector(pos), None if fwd is None else Vector(fwd))
        return self

    def _bm(self, role, cut=False):
        d = self.cuts if cut else self.bms
        k = (self.cur, role)
        if k not in d:
            d[k] = bmesh.new()
        return d[k]

    @staticmethod
    def _fin(bm, n0, r=0.0, seg=2):
        bm.faces.ensure_lookup_table()
        faces = [bm.faces[i] for i in range(n0, len(bm.faces))]
        if not faces:
            return
        bmesh.ops.recalc_face_normals(bm, faces=faces)
        if r > 0:
            edges = list({e for f in faces for e in f.edges})
            try:
                bmesh.ops.bevel(bm, geom=edges, offset=r, segments=seg, profile=0.5, affect='EDGES', clamp_overlap=True)
            except Exception as ex:                       # smusso impossibile su un pezzo troppo piccolo: si lascia vivo
                print('[kit] bevel saltato:', ex)

    # -- primitive
    def box(self, role, c, s, r=0.0, seg=2, rot=None, cut=False):
        """Scatola centrata in c con lati s; r = raggio dello smusso (0 = spigoli vivi, ci pensa lo smusso globale)."""
        bm = self._bm(role, cut)
        n0 = len(bm.faces)
        vs = bmesh.ops.create_cube(bm, size=1.0)['verts']
        M = Matrix.Translation(Vector(c)) @ rotm(rot) @ Matrix.Diagonal((s[0], s[1], s[2], 1.0))
        bmesh.ops.transform(bm, matrix=M, verts=vs)
        self._fin(bm, n0, r, seg)
        return self

    def prism(self, role, pts, x0, x1, r=0.0, seg=2, cut=False):
        """Profilo laterale pts=[(y, z), ...] estruso in x tra x0 e x1: carcasse, ponticelli, tacche, impugnature piatte."""
        bm = self._bm(role, cut)
        n0 = len(bm.faces)
        a = [bm.verts.new((x0, y, z)) for y, z in pts]
        b = [bm.verts.new((x1, y, z)) for y, z in pts]
        n = len(a)
        for j in range(n):
            bm.faces.new((a[j], a[(j + 1) % n], b[(j + 1) % n], b[j]))
        bm.faces.new(a[::-1])
        bm.faces.new(b)
        self._fin(bm, n0, r, seg)
        return self

    def lathe(self, role, c, prof, segs=24, axis='y', rot=None, squash=(1.0, 1.0), cap=(True, True), loop=False, r=0.0, cut=False):
        """Solido di rivoluzione intorno a un asse (y = canna, x, z). prof = [(quota_lungo_asse, raggio), ...];
        raggio 0 = polo. squash = (sx, sz) schiaccia la sezione; loop=True chiude il profilo su se stesso (tubo cavo)."""
        bm = self._bm(role, cut)
        n0 = len(bm.faces)
        c = Vector(c)
        R3 = rotm(rot).to_3x3()

        def P(h, rad, a):
            ca, sa = cos(a) * rad * squash[0], sin(a) * rad * squash[1]
            q = Vector((ca, h, sa)) if axis == 'y' else Vector((h, ca, sa)) if axis == 'x' else Vector((ca, sa, h))
            return c + R3 @ q
        rings = []
        for h, rad in prof:
            if rad < 1e-7:
                rings.append([bm.verts.new(P(h, 0.0, 0.0))])
            else:
                rings.append([bm.verts.new(P(h, rad, 2 * pi * j / segs)) for j in range(segs)])

        def link(A, B):
            for j in range(segs):
                if len(A) == 1 and len(B) == 1:
                    continue
                if len(A) == 1:
                    bm.faces.new((A[0], B[(j + 1) % segs], B[j]))
                elif len(B) == 1:
                    bm.faces.new((A[j], A[(j + 1) % segs], B[0]))
                else:
                    bm.faces.new((A[j], A[(j + 1) % segs], B[(j + 1) % segs], B[j]))
        for i in range(len(rings) - 1):
            link(rings[i], rings[i + 1])
        if loop:
            link(rings[-1], rings[0])
        else:
            if cap[0] and len(rings[0]) > 1:
                bm.faces.new(rings[0][::-1])
            if cap[1] and len(rings[-1]) > 1:
                bm.faces.new(rings[-1])
        self._fin(bm, n0, r, 2)
        return self

    def cyl(self, role, x, z, y0, y1, rad, ch=0.0006, segs=24, rot=None, squash=(1.0, 1.0), cut=False):
        """Cilindro smussato lungo y tra y0 e y1, centrato in (x, z)."""
        return self.lathe(role, (x, 0.0, z), cprof(y0, y1, rad, min(ch, rad * 0.4)), segs=segs, rot=rot, squash=squash, cut=cut)

    def tube(self, role, x, z, y0, y1, r_out, r_in, segs=28, ch=0.0004, cut=False):
        """Tubo cavo (canne con anima visibile, bocche, silenziatori)."""
        ch = min(ch, (r_out - r_in) * 0.45)
        prof = [(y0, r_in), (y0, r_out - ch), (y0 + ch, r_out), (y1 - ch, r_out), (y1, r_out - ch), (y1, r_in)]
        return self.lathe(role, (x, 0.0, z), prof, segs=segs, loop=True, cut=cut)

    def sphere(self, role, c, rad, segs=14, squash=(1.0, 1.0, 1.0), cut=False):
        bm = self._bm(role, cut)
        n0 = len(bm.faces)
        vs = bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=max(6, segs // 2), radius=rad)['verts']
        M = Matrix.Translation(Vector(c)) @ Matrix.Diagonal((squash[0], squash[1], squash[2], 1.0))
        bmesh.ops.transform(bm, matrix=M, verts=vs)
        self._fin(bm, n0)
        return self

    def loft(self, role, secs, cap=True, r=0.0, seg=2, cut=False):
        """secs = [(y, [(x, z), ...]), ...]: sezioni con lo stesso numero di punti (calci, impugnature, astine, ottiche)."""
        bm = self._bm(role, cut)
        n0 = len(bm.faces)
        rings = [[bm.verts.new((x, y, z)) for x, z in pts] for y, pts in secs]
        n = len(rings[0])
        for A, B in zip(rings[:-1], rings[1:]):
            for j in range(n):
                bm.faces.new((A[j], A[(j + 1) % n], B[(j + 1) % n], B[j]))
        if cap:
            bm.faces.new(rings[0][::-1])
            bm.faces.new(rings[-1])
        self._fin(bm, n0, r, seg)
        return self

    def sweep(self, role, path, hw, dp, rad=0.003, seg=2, x=0.0, cap=True, cut=False):
        """Sezione rettangolare arrotondata lungo una curva nel piano (y, z): caricatori curvi, impugnature, calci.
        path = [(y, z), ...]; hw = semilarghezza in x, dp = semispessore lungo la normale al percorso (numeri o liste
        con un valore per punto)."""
        bm = self._bm(role, cut)
        n0 = len(bm.faces)
        m = len(path)
        hws = hw if isinstance(hw, (list, tuple)) else [hw] * m
        dps = dp if isinstance(dp, (list, tuple)) else [dp] * m
        rings = []
        for i in range(m):
            p0 = Vector(path[max(i - 1, 0)])
            p1 = Vector(path[min(i + 1, m - 1)])
            t = (p1 - p0)
            t = t.normalized() if t.length > 1e-9 else Vector((0, 1))
            nrm = Vector((-t.y, t.x))                   # normale nel piano: (y, z)
            sec = rrect(hws[i], dps[i], rad, seg)
            ring = []
            for sx, su in sec:
                ring.append(bm.verts.new((x + sx, path[i][0] + nrm.x * su, path[i][1] + nrm.y * su)))
            rings.append(ring)
        n = len(rings[0])
        for A, B in zip(rings[:-1], rings[1:]):
            for j in range(n):
                bm.faces.new((A[j], A[(j + 1) % n], B[(j + 1) % n], B[j]))
        if cap:
            bm.faces.new(rings[0][::-1])
            bm.faces.new(rings[-1])
        self._fin(bm, n0)
        return self

    def rod(self, role, a, b, r, segs=10, cut=False):
        """Asta cilindrica da a a b (fili del calcio, bipiedi, leve)."""
        bm = self._bm(role, cut)
        n0 = len(bm.faces)
        a, b = Vector(a), Vector(b)
        d = b - a
        if d.length < 1e-6:
            return self
        q = Vector((0, 0, 1)).rotation_difference(d.normalized())
        M = Matrix.Translation((a + b) / 2) @ q.to_matrix().to_4x4()
        bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segs, radius1=r, radius2=r, depth=d.length, matrix=M)
        self._fin(bm, n0)
        return self

    # -- costruzione degli oggetti Blender
    def _obj(self, name, bm):
        me = bpy.data.meshes.new(name)
        bm.to_mesh(me)
        ob = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(ob)
        return ob

    def build(self, bevel=0.0005, smooth_deg=52, material_fn=None):
        """Valuta booleane e smussi, unisce i ruoli di ogni parte in un solo oggetto, crea gli Empty e la radice."""
        root = bpy.data.objects.new(self.name, None)
        bpy.context.scene.collection.objects.link(root)
        self.root = root
        late = []
        for part in self.order:
            fin = bmesh.new()
            roles = []
            for (p, role), bm in sorted(self.bms.items(), key=lambda kv: kv[0][1]):
                if p != part or len(bm.faces) == 0:
                    continue
                ob = self._obj(part + '_' + role, bm)
                cobj = None
                cb = self.cuts.get((p, role))
                if cb is not None and len(cb.faces):
                    cobj = self._obj('cutter', cb)
                    m = ob.modifiers.new('taglio', 'BOOLEAN')
                    m.operation = 'DIFFERENCE'
                    m.object = cobj
                    m.solver = 'EXACT'
                    m.use_self = True              # i pezzi si sovrappongono: senza questo l'esatta puo' far sparire interi solidi
                    m.use_hole_tolerant = True
                bw = self.bevel.get(role, bevel)
                if bw > 0:
                    m = ob.modifiers.new('smusso', 'BEVEL')
                    m.width = bw
                    m.segments = 2
                    m.limit_method = 'ANGLE'
                    m.angle_limit = radians(38)
                    m.use_clamp_overlap = True
                    m.loop_slide = True
                bpy.context.view_layer.update()
                dg = bpy.context.evaluated_depsgraph_get()
                me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg), preserve_all_data_layers=False, depsgraph=dg)
                bad_v = [v.index for v in me.vertices if v.co.x != v.co.x]
                if bad_v:
                    # un modificatore ha prodotto vertici NaN: si riparte dal pezzo senza smusso e senza booleana
                    print(f'[kit] {self.name}/{part}/{role}: {len(bad_v)} vertici NaN dopo i modificatori, riprovo senza smusso')
                    bpy.data.meshes.remove(me)
                    ob.modifiers.clear()
                    if cobj is not None:
                        m = ob.modifiers.new('taglio', 'BOOLEAN')
                        m.operation = 'DIFFERENCE'
                        m.object = cobj
                        m.solver = 'EXACT'
                        m.use_self = True
                        m.use_hole_tolerant = True
                    bpy.context.view_layer.update()
                    dg = bpy.context.evaluated_depsgraph_get()
                    me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg), preserve_all_data_layers=False, depsgraph=dg)
                    bad_v = [v.index for v in me.vertices if v.co.x != v.co.x]
                    if bad_v:
                        print(f'[kit]   ancora NaN ({len(bad_v)}): uso il pezzo grezzo')
                        bpy.data.meshes.remove(me)
                        me = bpy.data.meshes.new(part + role)
                        bm.to_mesh(me)
                if role not in roles:
                    roles.append(role)
                mi = roles.index(role)
                n0 = len(fin.faces)
                fin.from_mesh(me)
                fin.faces.ensure_lookup_table()
                for i in range(n0, len(fin.faces)):
                    fin.faces[i].material_index = mi
                bpy.data.objects.remove(ob)
                if cobj is not None:
                    bpy.data.objects.remove(cobj)
                bpy.data.meshes.remove(me)
            if not roles:
                fin.free()
                continue
            # sistemazione: nessuna normale invertita dalle booleane, smussi lisci e spigoli netti
            bmesh.ops.remove_doubles(fin, verts=fin.verts, dist=1e-6)
            bmesh.ops.dissolve_degenerate(fin, edges=fin.edges, dist=1e-6)
            fin.faces.ensure_lookup_table()
            bad = [f for f in fin.faces if f.calc_area() < 1e-10]
            if bad:
                bmesh.ops.delete(fin, geom=bad, context='FACES')
            lone = [v for v in fin.verts if not v.link_faces]
            if lone:
                bmesh.ops.delete(fin, geom=lone, context='VERTS')
            ngons = [f for f in fin.faces if len(f.verts) > 4]
            if ngons:
                bmesh.ops.triangulate(fin, faces=ngons, quad_method='BEAUTY', ngon_method='BEAUTY')
            fin.edges.ensure_lookup_table()
            for e in fin.edges:
                if len(e.link_faces) == 2:
                    try:
                        e.smooth = e.calc_face_angle(0.0) < radians(smooth_deg)
                    except ValueError:
                        e.smooth = True
                else:
                    e.smooth = True
            for f in fin.faces:
                f.smooth = True
            piv = self.pivots.get(part, Vector((0, 0, 0)))
            bmesh.ops.translate(fin, vec=-piv, verts=fin.verts)
            me = bpy.data.meshes.new(part)
            fin.to_mesh(me)
            fin.free()
            ob = bpy.data.objects.new(part, me)
            bpy.context.scene.collection.objects.link(ob)
            ob.location = piv + OFFSET
            ob.parent = root
            pending_parent = self.parents.get(part)
            if pending_parent is not None:
                late.append((ob, pending_parent))
            for role in roles:
                me.materials.append((material_fn or default_material)(role, self))
            self.objs[part] = ob
        # parti figlie di altre parti (maniglia dell'otturatore nell'otturatore): si rilegano mantenendo la posizione
        bpy.context.view_layer.update()
        for ob, pname in late:
            par = self.objs.get(pname)
            if par is not None:
                world = ob.matrix_world.copy()
                ob.parent = par
                ob.matrix_parent_inverse = par.matrix_world.inverted()
        for nm, (pos, fwd) in self.empties.items():
            e = bpy.data.objects.new(nm, None)
            e.empty_display_type = 'ARROWS'
            e.empty_display_size = 0.02
            e.location = pos + OFFSET
            if fwd is not None:
                e.rotation_euler = Vector(fwd).to_track_quat('Y', 'Z').to_euler()
            bpy.context.scene.collection.objects.link(e)
            e.parent = root
            self.objs[nm] = e
        # custom property: dimensioni utili al lato Unity
        return root


# ----------------------------------------------------------------------------- materiali segnaposto (mats.py li sostituisce)
_PLAIN = {
    'acciaio': ((0.06, 0.065, 0.075), 1.0, 0.42), 'alluminio': ((0.045, 0.047, 0.052), 0.9, 0.5), 'polimero': ((0.03, 0.03, 0.032), 0.0, 0.65),
    'legno': ((0.16, 0.08, 0.035), 0.0, 0.4), 'gomma': ((0.02, 0.02, 0.02), 0.0, 0.9), 'ottone': ((0.75, 0.52, 0.17), 1.0, 0.3),
    'cromo': ((0.6, 0.6, 0.62), 1.0, 0.15), 'inox': ((0.5, 0.5, 0.52), 1.0, 0.3), 'vetro': ((0.2, 0.3, 0.4), 0.0, 0.05),
    'emit': ((0.1, 1.0, 0.2), 0.0, 0.5), 'retino': ((1.0, 0.05, 0.03), 0.0, 0.5), 'emit_ambra': ((1.0, 0.55, 0.08), 0.0, 0.5),
    'osso': ((0.42, 0.37, 0.27), 0.0, 0.5), 'carne': ((0.30, 0.05, 0.045), 0.0, 0.25),
}


def default_material(role, gun=None):
    nm = 'W_' + role
    if nm in bpy.data.materials:
        return bpy.data.materials[nm]
    col, met, rough = _PLAIN.get(role, ((0.2, 0.2, 0.2), 0.0, 0.5))
    m = bpy.data.materials.new(nm)
    m.use_nodes = True
    b = m.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = (*col, 1)
    b.inputs['Metallic'].default_value = met
    b.inputs['Roughness'].default_value = rough
    if role in ('emit', 'retino', 'emit_ambra'):
        b.inputs['Emission Color'].default_value = (*col, 1)
        b.inputs['Emission Strength'].default_value = 3.0
    return m


# ----------------------------------------------------------------------------- scena, render, FBX
def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.unit_settings.system = 'METRIC'
    return sc


def setup_cycles(samples=64, res=(1280, 720)):
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
    sc.render.resolution_percentage = 100
    sc.render.image_settings.file_format = 'PNG'
    sc.view_settings.view_transform = 'AgX'
    sc.view_settings.look = 'AgX - Medium High Contrast'


def look_at(ob, target, roll=0.0):
    d = Vector(target) - ob.location
    ob.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()


def studio(strength=1.0):
    """Fondale scuro con sfumatura e tre luci morbide: chiave in alto a sinistra, riempimento freddo, controluce caldo."""
    sc = bpy.context.scene
    w = bpy.data.worlds.new('mondo')
    sc.world = w
    w.use_nodes = True
    nt = w.node_tree
    bg = nt.nodes['Background']
    bg.inputs['Color'].default_value = (0.045, 0.047, 0.055, 1)
    bg.inputs['Strength'].default_value = 1.0
    for nm, loc, pw, sz, col in (('chiave', (-0.7, -0.5, 0.9), 55 * strength, 0.9, (1.0, 0.9, 0.78)),
                                 ('riempimento', (0.9, -0.6, 0.3), 18 * strength, 1.2, (0.78, 0.86, 1.0)),
                                 ('contro', (0.1, 0.8, 0.6), 40 * strength, 0.7, (1.0, 0.82, 0.62))):
        l = bpy.data.lights.new(nm, 'AREA')
        l.energy, l.size, l.color = pw, sz, col
        lo = bpy.data.objects.new(nm, l)
        lo.location = Vector(loc)
        look_at(lo, (0.0, 0.05, 0.0))
        bpy.context.scene.collection.objects.link(lo)


def camera(loc, target, fov=40.0):
    cam = bpy.data.objects.get('camera')
    if cam is None:
        cam = bpy.data.objects.new('camera', bpy.data.cameras.new('camera'))
        bpy.context.scene.collection.objects.link(cam)
    cam.location = Vector(loc)
    cam.data.lens_unit = 'FOV'
    cam.data.angle = radians(fov)
    cam.data.sensor_fit = 'VERTICAL'
    cam.data.clip_start = 0.02
    look_at(cam, target)
    bpy.context.scene.camera = cam
    return cam


def export_fbx(path, objs):
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'EMPTY', 'MESH'}, apply_unit_scale=True,
                             apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
                             mesh_smooth_type='EDGE', use_tspace=True, add_leaf_bones=False, bake_anim=False,
                             path_mode='STRIP', embed_textures=False)
