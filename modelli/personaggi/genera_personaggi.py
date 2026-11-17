"""
Survivors: procedural characters for Blender 4.5 (headless). Four players, four looks, one shared skeleton.

Usage (from this folder; run one Blender at a time: each one wants every core):
  blender -b -P genera_personaggi.py -- body            nude proxy, head, hands: clay previews -> render/body_*.png
  blender -b -P genera_personaggi.py -- head <id>       face, hair, cap and collar only, five sides, about a minute -> render/<id>_testa_*.png
  blender -b -P genera_personaggi.py -- build <id>      garments + accessories + materials -> work/<id>_hp.blend and render/<id>_*.png
  blender -b -P genera_personaggi.py -- export <id>     low-poly + UV + bakes + skin weights + FBX + textures into the Unity project, previews

Axes: Z up, the character faces -Y, its right is -X. Units: metres. See docs/game/characters.md.
"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from kit import *
import skeleton as sk
import body as bd
import mats
import characters as ch
import export as ex

DIR = os.path.dirname(os.path.abspath(__file__))
RENDER_DIR = os.path.join(DIR, 'render')
EXPORT_DIR = os.path.join(DIR, 'export')
WORK_DIR = os.path.join(DIR, 'work')


def clay_material():
    return flat_material('creta', (0.36, 0.34, 0.32), 0.6)


def shoot(views, prefix, lens_for=None, res=(900, 1100)):
    sc = bpy.context.scene
    os.makedirs(RENDER_DIR, exist_ok=True)
    for nm, (loc, tgt, lens) in views.items():
        add_camera(loc, tgt, lens)
        sc.render.filepath = os.path.join(RENDER_DIR, f'{prefix}_{nm}.png')
        bpy.ops.render.render(write_still=True)
        print('[render]', sc.render.filepath)


STD_VIEWS = {
    'fronte': ((0.0, -4.2, 0.95), (0, 0, 0.88), 50),
    'tre_quarti': ((-2.8, -3.4, 1.15), (0, 0, 0.88), 50),
    'lato': ((4.2, 0.0, 0.95), (0, 0, 0.88), 50),
    'retro': ((1.2, 4.0, 1.2), (0, 0, 0.9), 50),
}
FACE_VIEWS = {
    'volto': ((0.0, -0.85, 1.66), (0, -0.06, 1.62), 85),
    'volto_tq': ((-0.50, -0.70, 1.68), (0, -0.06, 1.62), 85),
    'volto_lato': ((-0.85, -0.10, 1.66), (0, -0.05, 1.62), 85),
    'occhio': ((0.11, -0.32, 1.665), (0.0315, -0.07, 1.632), 135),
}
HAND_VIEWS = {
    'mano': ((0.95, -0.55, 0.95), (0.42, -0.05, 0.82), 85),
}


def stage_body():
    reset()
    sc = bpy.context.scene
    setup_cycles(48, (900, 1100))
    P = bd.body_params()
    body = bd.build_body(P)
    head = bd.build_head(P)
    ears = bd.build_ears(P)
    eyes = bd.build_eyes(P)
    hands = bd.build_hands(P)
    print('[info] profile', bd.head_profile(head))
    clay = clay_material()
    eye_mat = mats.eye_material()
    for ob in [body, head, ears, *hands]:
        ob.data.materials.clear()
        ob.data.materials.append(clay)
    for ob in eyes:
        ob.data.materials.clear()
        ob.data.materials.append(eye_mat)
    studio()
    shoot({k: v for k, v in STD_VIEWS.items()}, 'body')
    setup_cycles(64, (1100, 1100))
    shoot(FACE_VIEWS, 'body')
    shoot(HAND_VIEWS, 'body')


def stage_build(name):
    """High-poly character with its procedural materials, previewed from four sides and two close-ups."""
    reset()
    setup_cycles(40, (900, 1100))
    t = time.time()
    parts, P = ch.build(name)
    print('[info] built in %.1fs' % (time.time() - t), [(o.name, g, len(o.data.polygons)) for o, g in parts])
    for o, g in parts:
        o['group'] = g
    save_blend(os.path.join(WORK_DIR, f'{name}_hp.blend'))
    studio()
    shoot(STD_VIEWS, name)
    setup_cycles(56, (1100, 1100))
    shoot({'volto': FACE_VIEWS['volto'], 'volto_tq': FACE_VIEWS['volto_tq']}, name)


HEAD_VIEWS = {
    'volto': FACE_VIEWS['volto'], 'volto_tq': FACE_VIEWS['volto_tq'], 'volto_lato': FACE_VIEWS['volto_lato'],
    'nuca': ((0.55, 0.95, 1.64), (0.0, 0.04, 1.52), 70),
    'alto': ((0.0, -0.55, 2.10), (0.0, 0.0, 1.62), 70),
}


def stage_head(name):
    """Face, hair, cap, collar and scarf only, from five sides at low quality: a quick look at a face or a hairstyle (about a minute)."""
    reset()
    setup_cycles(20, (800, 800))
    parts, P = ch.build(name, head_only=True)
    for o, g in parts:
        o['group'] = g
    studio()
    shoot(HEAD_VIEWS, name + '_testa')


def stage_export(name):
    """Low-poly, bakes, weights and FBX; then previews of the baked result at rest and in a test pose."""
    arm, lows = ex.stage_export(name)
    setup_cycles(40, (900, 1100))
    studio()
    shoot({k: STD_VIEWS[k] for k in ('fronte', 'tre_quarti', 'retro')}, f'{name}_low')
    ex.pose_test(arm)
    shoot({'p_tq': STD_VIEWS['tre_quarti'], 'p_lato': STD_VIEWS['lato']}, f'{name}_low')
    setup_cycles(56, (1100, 1100))
    shoot({'volto': FACE_VIEWS['volto'], 'volto_tq': FACE_VIEWS['volto_tq']}, f'{name}_low')
    # the roughness maps only fed the previews above: Unity reads the mask (smoothness in its alpha), so they are not shipped
    for fn in os.listdir(ex.TEX_DIR):
        if fn.startswith(name + '_') and fn.endswith('_rough.png'):
            os.remove(os.path.join(ex.TEX_DIR, fn))


if __name__ == '__main__':
    stage = ARGS[0] if ARGS else 'body'
    if stage == 'body':
        stage_body()
    elif stage == 'build':
        stage_build(ARGS[1] if len(ARGS) > 1 else 'custode')
    elif stage == 'head':
        stage_head(ARGS[1] if len(ARGS) > 1 else 'custode')
    elif stage == 'export':
        stage_export(ARGS[1] if len(ARGS) > 1 else 'custode')
    else:
        print('unknown stage', stage)
