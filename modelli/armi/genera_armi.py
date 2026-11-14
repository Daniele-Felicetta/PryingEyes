"""
Armi del gioco: modelli hard-surface da script Blender (Blender 4.5, headless).

Uso (dalla cartella del file):
  blender -b -P genera_armi.py -- render <nome ...> [rapido]   anteprime in render/<nome>_<vista>.png
  blender -b -P genera_armi.py -- build <nome ...>             FBX in export/<nome>.fbx (senza texture)
  blender -b -P genera_armi.py -- bake <nome ...>              UV + bake PBR (tex/<nome>_*.png) + FBX
  blender -b -P genera_armi.py -- sheet [nome ...]             foglio con una vista laterale per arma (render/foglio.png)
  blender -b -P genera_armi.py -- lista                        elenco delle armi
Senza nomi: tutte.
"""
import bpy, os, sys, math, importlib
from math import radians
from mathutils import Vector

DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, DIR)
import kit                                    # noqa: E402
from kit import *                             # noqa: E402,F401

ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
REG = {}


def load_registry():
    for mod in ('armi_fucili', 'armi_fucili2', 'armi_pistole', 'armi_mitra', 'armi_pompe', 'armi_pesanti', 'armi_lunghi', 'armi_speciali', 'armi_coltello', 'armi_urbane'):
        try:
            m = importlib.import_module(mod)
        except ModuleNotFoundError as ex:
            if ex.name != mod:
                raise
            continue
        for k in dir(m):
            f = getattr(m, k)
            if callable(f) and getattr(f, 'e_arma', False):
                REG[k] = f


def world_bbox(objs):
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for o in objs:
        if o.type != 'MESH':
            continue
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            lo = Vector((min(lo.x, w.x), min(lo.y, w.y), min(lo.z, w.z)))
            hi = Vector((max(hi.x, w.x), max(hi.y, w.y), max(hi.z, w.z)))
    return lo, hi


def shoot(path, loc, target, fov=34.0, hfit=True):
    cam = camera(loc, target, fov)
    cam.data.sensor_fit = 'HORIZONTAL' if hfit else 'VERTICAL'
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def make(nm, material_fn=None):
    reset()
    g = REG[nm]()
    mf = material_fn
    if mf is None:
        import mats
        mats._cache.clear()
        mf = mats.material
    root = g.build(material_fn=mf)
    bpy.context.view_layer.update()
    return g


def views(g, nm, which, samples, res, tag=''):
    objs = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    lo, hi = world_bbox(objs)
    c = (lo + hi) / 2
    L = max(hi.y - lo.y, 0.3)
    setup_cycles(samples, res)
    studio()
    os.makedirs(os.path.join(DIR, 'render'), exist_ok=True)
    out = os.path.join(DIR, 'render')
    H = max(hi.z - lo.z, 0.05)
    dist = max((L / 2) / math.tan(radians(36 / 2)), (H / 2) / math.tan(radians(10.4))) * 1.12
    for v in which:
        if v == 'lato':
            shoot(os.path.join(out, f'{nm}_lato{tag}.png'), c + Vector((dist, 0, 0)), c, 36)
        elif v == 'tre_quarti':
            shoot(os.path.join(out, f'{nm}_34{tag}.png'), c + Vector((dist * 0.62, -dist * 0.72, dist * 0.34)), c, 36)
        elif v == 'giocatore':
            shoot(os.path.join(out, f'{nm}_giocatore{tag}.png'), Vector((-0.28, -0.72, 0.38)) + kit.OFFSET, Vector((-0.02, 0.5, 0.20)) + kit.OFFSET, 60, hfit=False)
        elif v == 'ads':
            e = g.objs.get('ads')
            if e is not None:
                p = e.matrix_world.translation
                shoot(os.path.join(out, f'{nm}_ads{tag}.png'), p + Vector((0, -0.30, 0)), p + Vector((0, 1, 0)), 55, hfit=False)
        elif v == 'sopra':
            shoot(os.path.join(out, f'{nm}_sopra{tag}.png'), c + Vector((0, 0, dist)), c + Vector((0, 0, 0)), 36)


def stage_render(names):
    quick = 'rapido' in ARGS
    which = [a for a in ARGS if a in ('lato', 'tre_quarti', 'giocatore', 'ads', 'sopra')] or ['lato', 'tre_quarti', 'giocatore', 'ads']
    for nm in names:
        g = make(nm)
        views(g, nm, which, 24 if quick else 64, (1100, 620) if quick else (1600, 900))
        print('[render]', nm)


def stage_build(names):
    os.makedirs(os.path.join(DIR, 'export'), exist_ok=True)
    for nm in names:
        g = make(nm)
        objs = [g.root] + list(g.objs.values())
        kit.export_fbx(os.path.join(DIR, 'export', nm + '.fbx'), objs)
        tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in g.objs.values() if o.type == 'MESH')
        print(f'[build] {nm}: {len(g.objs)} oggetti, {tris} triangoli')


def stage_sheet(names):
    from PIL import Image  # solo se disponibile in Blender
    raise SystemExit('usa sheet_pil.py (python di sistema) con le immagini di render/<nome>_lato.png')


if __name__ == '__main__':
    load_registry()
    if 'lista' in ARGS:
        print('\n'.join(sorted(REG)))
        sys.exit(0)
    names = [a for a in ARGS if a in REG] or sorted(REG)
    if 'render' in ARGS:
        stage_render(names)
    if 'build' in ARGS:
        stage_build(names)
    if 'bake' in ARGS:
        import bake
        bake.stage_bake(names, make, REG)
