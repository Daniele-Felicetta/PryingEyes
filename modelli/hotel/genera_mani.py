"""
Mani e avambracci del giocatore (fase 4): guanti neri e maniche, una mano sull'impugnatura e una sull'astina, nella posa
del fucile d'assalto. Si agganciano al viewmodel dell'arma (Weapon.cs) e si spostano per ogni arma.

Uso (dalla cartella del file):
  blender -b -P genera_mani.py -- build     FBX -> mani/mani.fbx (due Empty: mano_d, mano_s, ognuno con guanto e manica)
  blender -b -P genera_mani.py -- render    anteprime Cycles con un fucile segnaposto -> mani/render/
Le coordinate si scrivono nel riferimento dell'arma di Unity (x destra, y su, z avanti = verso la bocca) e U() le
porta in quelle di Blender; l'origine e' il centro del corpo del fucile (come BuildRifle).
"""
import bpy, bmesh, math, os, sys, random
from math import sin, cos, pi, radians, sqrt
from mathutils import Vector, Matrix, noise

DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(os.path.dirname(DIR), 'monaco'))
import genera_monaco as M          # noqa: E402  (capsule, remesh a voxel)

ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = os.path.join(DIR, 'mani')


def U(x, y, z):
    """Riferimento di Unity (x destra, y su, z avanti) -> Blender (x, y avanti, z su)."""
    return Vector((x, z, y))


def V3(x, y, z):
    return Vector((x, y, z))


def chain(bm, pts, radii, seg=12):
    for i in range(len(pts) - 1):
        M.bm_capsule(bm, U(*pts[i]), U(*pts[i + 1]), radii[i], radii[i + 1], seg)


def ellipsoid(bm, c, radii, frame=None, seg=20):
    """Ellissoide con assi (r, a, f) del riferimento locale (nel riferimento di Unity)."""
    if frame is not None:
        r, a, f = frame
        mu = Matrix(((r.x, a.x, f.x), (r.y, a.y, f.y), (r.z, a.z, f.z)))      # colonne = assi nel riferimento di Unity
        sw = Matrix(((1, 0, 0), (0, 0, 1), (0, 1, 0)))                       # Unity -> Blender (scambio y/z)
        rot = sw @ mu @ sw.inverted()
    else:
        rot = None
    # le dimensioni stanno negli assi locali (r, a, f) = (x, y, z) dell'ellissoide prima della rotazione
    M.bm_ellipsoid(bm, U(*c), (radii[0], radii[2], radii[1]), rot=rot, seg=seg)


# ----------------------------------------------------------------------------- mano destra: impugnatura
GRIP_C = V3(0.0, -0.12, 0.10)
GRIP_TILT = radians(12)


def hand_right(g):
    """Palmo sul dietro dell'impugnatura, tre dita che la avvolgono, indice disteso sul ponticello, pollice sul fianco sinistro."""
    t = GRIP_TILT
    a = V3(0, cos(t), sin(t))        # asse dell'impugnatura (la cima va in avanti)
    r = V3(1, 0, 0)
    f = V3(0, -sin(t), cos(t))

    def at(psi, h, R):
        return GRIP_C + a * h + (r * cos(psi) + f * sin(psi)) * R
    # dita medio, anulare, mignolo: partono dal dorso destro e girano davanti all'impugnatura
    for h, lens, rad in ((0.048, (0.046, 0.029, 0.023), 0.0118), (0.018, (0.043, 0.027, 0.021), 0.0112), (-0.012, (0.035, 0.022, 0.019), 0.0104)):
        psi = radians(338)
        R = 0.0435
        pts = [at(psi, h, R)]
        # Una curva continua evita le nocche spigolose e la torsione della punta.
        length = sum(lens) * 0.78
        for k in range(1, 9):
            u = k / 8
            pts.append(at(psi + u * length / 0.0435, h - 0.006 * u, R))
        chain(g, [(p.x, p.y, p.z) for p in pts], [rad * (1 - 0.2 * k / 8) for k in range(9)])
    # indice sul ponticello: lungo il fianco destro del calcio, fino davanti al grilletto
    ip = [(0.046, -0.083, 0.070), (0.056, -0.080, 0.105), (0.060, -0.080, 0.140), (0.060, -0.083, 0.168)]
    chain(g, ip, (0.0118, 0.0108, 0.0098, 0.0088))
    # pollice sul lato sinistro: dal palmo in avanti lungo la parte alta
    th = [(0.030, -0.052, 0.058), (-0.012, -0.050, 0.075), (-0.050, -0.050, 0.100), (-0.062, -0.052, 0.135), (-0.060, -0.057, 0.165)]
    chain(g, th, (0.0145, 0.0135, 0.0122, 0.0108, 0.0096))
    # palmo e dorso della mano
    pc = GRIP_C + a * 0.022 + (r * cos(radians(322)) + f * sin(radians(322))) * 0.041
    ellipsoid(g, (pc.x, pc.y, pc.z), (0.028, 0.050, 0.032), frame=(r, a, f))
    # polso e inizio avambraccio (dentro il guanto)
    wr = V3(0.040, -0.200, 0.030)
    chain(g, [(pc.x + 0.004, pc.y - 0.030, pc.z - 0.012), (wr.x, wr.y, wr.z)], (0.031, 0.027))
    return wr


FORE_R = V3(0.28, -0.40, -0.87).normalized()      # direzione dell'avambraccio destro: indietro, in basso, un po' a destra


# ----------------------------------------------------------------------------- mano sinistra: astina
HG_C = V3(0.0, 0.005, 0.31)


def hand_left(g):
    """Palmo sotto l'astina, quattro dita che salgono sul lato destro, pollice sul sinistro."""
    def at(psi, z, R):
        return V3(HG_C.x + cos(psi) * R, HG_C.y + sin(psi) * R, z)
    for z, lens, rad in ((0.395, (0.044, 0.028, 0.022), 0.0108), (0.360, (0.046, 0.030, 0.023), 0.0116),
                         (0.325, (0.043, 0.027, 0.021), 0.0111), (0.292, (0.035, 0.022, 0.019), 0.0103)):
        psi = radians(212)
        R = 0.0455
        pts = [at(psi, z, R)]
        length = sum(lens) * 0.78
        for k in range(1, 9):
            u = k / 8
            pts.append(at(psi + u * length / R, z + 0.003 * u, R))
        chain(g, [(p.x, p.y, p.z) for p in pts], [rad * (1 - 0.2 * k / 8) for k in range(9)])
    # pollice: dal palmo lungo il lato sinistro, in avanti
    th = [(-0.040, -0.040, 0.235), (-0.050, -0.012, 0.275), (-0.047, 0.012, 0.325), (-0.040, 0.026, 0.372), (-0.032, 0.032, 0.405)]
    chain(g, th, (0.0150, 0.0140, 0.0126, 0.0112, 0.0098))
    # palmo
    ellipsoid(g, (-0.020, -0.046, 0.262), (0.034, 0.020, 0.052))
    wr = V3(-0.062, -0.078, 0.200)
    chain(g, [(-0.030, -0.050, 0.240), (wr.x, wr.y, wr.z)], (0.031, 0.027))
    return wr


FORE_L = V3(-0.58, -0.42, -0.70).normalized()


def forearm(wrist, direction, length=0.46):
    """Manica: tubo che si allarga dal polso al gomito, con qualche piega."""
    def fill(bm):
        pts = [wrist + direction * (0.035 + length * k / 4.0) for k in range(5)]
        rad = (0.0395, 0.043, 0.048, 0.054, 0.058)
        chain(bm, [(p.x, p.y, p.z) for p in pts], rad, seg=16)
    return fill


def cuff(wrist, direction):
    """Polsino del guanto: anello piu' largo sopra il polso."""
    def fill(bm):
        chain(bm, [(wrist.x, wrist.y, wrist.z), (wrist.x + direction.x * 0.06, wrist.y + direction.y * 0.06, wrist.z + direction.z * 0.06)], (0.036, 0.0405), seg=18)
    return fill


# ----------------------------------------------------------------------------- scena
def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.unit_settings.system = 'METRIC'
    return bpy.context.scene


def make_obj(name, fill, voxel, sm_iter=3, folds=0.0, seed=0.0):
    ob = M.organic(name, fill, voxel=voxel, sm_iter=sm_iter, sm_fac=0.5)
    if folds > 0:
        me = ob.data
        for v in me.vertices:
            n = v.normal
            d = noise.noise(Vector((v.co.x * 22 + seed, v.co.y * 22, v.co.z * 22))) * folds
            v.co += n * d
    dm = ob.modifiers.new('decima', 'DECIMATE')
    dm.ratio = 0.35
    M.apply_all(ob)
    ob.data.shade_smooth()
    return ob


def box_uv(ob, scale=1.0):
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    uvl = bm.loops.layers.uv.verify()
    for f in bm.faces:
        f.normal_update()
        n = f.normal
        ax, ay, az = abs(n.x), abs(n.y), abs(n.z)
        for lo in f.loops:
            p = lo.vert.co
            uv = (p.x, p.y) if az >= ax and az >= ay else (p.x, p.z) if ay >= ax else (p.y, p.z)
            lo[uvl].uv = (uv[0] * scale, uv[1] * scale)
    bm.to_mesh(ob.data)
    bm.free()


def material(name, color, rough, metal=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = (*color, 1)
    b.inputs['Roughness'].default_value = rough
    b.inputs['Metallic'].default_value = metal
    return m


def build():
    sc = reset()
    glove_m = material('G_guanto', (0.05, 0.05, 0.055), 0.45)
    sleeve_m = material('G_manica', (0.10, 0.11, 0.09), 0.85)
    objs = []
    roots = {}
    for side, hand, wrist_fn, fdir in (('d', hand_right, None, FORE_R), ('s', hand_left, None, FORE_L)):
        wr = [None]

        def fill_glove(bm, hand=hand, fdir=fdir, wr=wr):
            wr[0] = hand(bm)
            cuff(wr[0], fdir)(bm)
        glove = make_obj(f'mano_{side}_guanto', fill_glove, voxel=0.0034, folds=0.0007, seed=1.0 if side == 'd' else 5.0)
        sleeve = make_obj(f'mano_{side}_manica', forearm(wr[0], fdir), voxel=0.0055, sm_iter=4, folds=0.0035, seed=3.0 if side == 'd' else 9.0)
        glove.data.materials.append(glove_m)
        sleeve.data.materials.append(sleeve_m)
        box_uv(glove)
        box_uv(sleeve)
        root = bpy.data.objects.new(f'mano_{side}', None)
        bpy.context.scene.collection.objects.link(root)
        glove.parent = root
        sleeve.parent = root
        roots[side] = root
        objs += [glove, sleeve]
    return roots, objs


def stage_build():
    roots, objs = build()
    tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs)
    print(f'[build] {len(objs)} mesh, {tris} triangoli')
    os.makedirs(OUT, exist_ok=True)
    top = bpy.data.objects.new('mani', None)
    bpy.context.scene.collection.objects.link(top)
    for r in roots.values():
        r.parent = top
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in [top] + list(roots.values()) + objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = top
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, 'mani.fbx'), use_selection=True,
                             object_types={'EMPTY', 'MESH'}, apply_unit_scale=True,
                             apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
                             mesh_smooth_type='FACE', use_tspace=True, add_leaf_bones=False,
                             bake_anim=False, path_mode='RELATIVE', embed_textures=False)


def look_at(ob, target):
    d = Vector(target) - ob.location
    ob.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()


def stage_render():
    roots, objs = build()
    sc = bpy.context.scene
    # fucile segnaposto (corpo, astina, impugnatura, calcio) nel riferimento dell'arma
    gun = material('fucile', (0.12, 0.12, 0.13), 0.35, 0.6)
    bm = bmesh.new()
    M.bm_ellipsoid(bm, U(0, 0, 0), (0.05, 0.215, 0.06))
    M.bm_capsule(bm, U(0, 0.005, 0.18), U(0, 0.005, 0.44), 0.0375, 0.0375, 24)
    M.bm_capsule(bm, U(0, -0.19, 0.082), U(0, -0.05, 0.118), 0.031, 0.031, 24)
    M.bm_capsule(bm, U(0, -0.01, -0.24), U(0, -0.01, -0.34), 0.038, 0.038, 24)
    M.bm_capsule(bm, U(0, 0.015, 0.44), U(0, 0.015, 0.70), 0.016, 0.016, 12)
    gobj = M.bm_obj(bm, 'fucile')
    gobj.data.materials.append(gun)
    M.setup_cycles(int(os.environ.get('MANI_SAMPLES', '48')), (1000, 600))
    w = bpy.data.worlds.new('mondo')
    sc.world = w
    w.use_nodes = True
    w.node_tree.nodes['Background'].inputs['Color'].default_value = (0.06, 0.06, 0.07, 1)
    for nm, loc, pw, sz, col in (('key', (-0.5, 0.4, 0.9), 40, 0.6, (1.0, 0.85, 0.7)), ('fill', (0.8, -0.3, 0.2), 14, 0.8, (0.8, 0.85, 1.0))):
        l = bpy.data.lights.new(nm, 'AREA')
        l.energy, l.size, l.color = pw, sz, col
        lo = bpy.data.objects.new(nm, l)
        lo.location = U(*loc)
        look_at(lo, U(0, -0.05, 0.2))
        bpy.context.scene.collection.objects.link(lo)
    os.makedirs(os.path.join(OUT, 'render'), exist_ok=True)
    views = {
        # come lo vede il giocatore: camera a (-0.28, 0.26, -0.62) dal centro dell'arma, FOV verticale 60
        'giocatore': (U(-0.28, 0.26, -0.62), U(-0.28, 0.26, 0.5), 0.0),
        'lato_destro': (U(0.65, -0.05, 0.12), U(0, -0.1, 0.12), 1.0),
        'sotto': (U(-0.25, -0.55, 0.30), U(0, 0, 0.25), 1.0),
        'tre_quarti': (U(-0.55, 0.28, -0.30), U(0.02, -0.12, 0.20), 1.0),
    }
    pick = [a for a in ARGS if a in views] or list(views)
    for nm in pick:
        loc, tgt, mode = views[nm]
        cam = bpy.data.objects.get('camera')
        if cam is None:
            cam = bpy.data.objects.new('camera', bpy.data.cameras.new('camera'))
            bpy.context.scene.collection.objects.link(cam)
        cam.location = loc
        cam.data.lens_unit = 'FOV'
        cam.data.angle = radians(60) if nm == 'giocatore' else radians(40)
        cam.data.sensor_fit = 'VERTICAL'
        look_at(cam, tgt)
        cam.data.clip_start = 0.03
        sc.camera = cam
        sc.render.filepath = os.path.join(OUT, 'render', f'{nm}.png')
        bpy.ops.render.render(write_still=True)
        print('[render]', sc.render.filepath)


if __name__ == '__main__':
    if 'build' in ARGS:
        stage_build()
    if 'render' in ARGS:
        stage_render()
