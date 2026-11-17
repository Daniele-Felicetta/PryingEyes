"""
Survivors: procedural characters for Blender 4.5 (headless). Four players, four looks, one shared skeleton.

Usage (from this folder; run one Blender at a time: each one wants every core):
  blender -b -P genera_personaggi.py -- body            nude proxy, head, hands: clay previews -> render/body_*.png
  blender -b -P genera_personaggi.py -- face <id> [s9] [skin] [fine] [hair]   the sculpted head alone, a few seconds -> render/<id>_lab_*.png
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
import face as fc
import hair as hr
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


LAB_VIEWS = {
    'f': ((0.0, -0.80, 1.640), (0, -0.06, 1.625), 85),
    'tq': ((-0.50, -0.65, 1.66), (0, -0.06, 1.62), 85),
    'lat': ((-0.85, -0.05, 1.65), (0, -0.04, 1.62), 85),
    'sotto': ((0.0, -0.55, 1.45), (0, -0.05, 1.60), 70),
}
LAB_VIEWS_HAIR = dict(LAB_VIEWS)
LAB_VIEWS_HAIR.update({
    'dietro': ((0.0, 0.95, 1.68), (0, 0.0, 1.60), 85),
    'dtq': ((0.60, 0.75, 1.74), (0, 0.0, 1.60), 85),
    'alto': ((0.0, -0.30, 2.20), (0, 0.0, 1.62), 70),
    'tp': ((0.0, 1.60, 2.05), (0, 0.0, 1.50), 50),      # about what the third-person camera sees
    'tq2': ((-0.62, -0.50, 1.64), (0, -0.02, 1.60), 70),
    'seam': ((0.18, 0.40, 1.67), (0.06, 0.09, 1.65), 85),
    'boca': ((0.0, -0.30, 1.575), (0.0, -0.09, 1.552), 100),
    'boca_tq': ((-0.18, -0.26, 1.575), (0.0, -0.09, 1.552), 100),
    'crown': ((0.0, 0.30, 1.86), (0.0, 0.0, 1.76), 85),
    'temple': ((-0.34, -0.22, 1.66), (-0.06, -0.06, 1.62), 100),
    'occhi': ((0.0, -0.30, 1.640), (0, -0.07, 1.632), 70),
    'occhi_tq': ((0.16, -0.26, 1.645), (0.02, -0.07, 1.632), 70),
    'seam2': ((-0.30, 0.22, 1.66), (-0.08, 0.04, 1.645), 100),
    'lat2': ((-0.75, 0.30, 1.64), (0, 0.0, 1.60), 70),
})


def debug_material(back=(1.0, 0.0, 1.0)):
    """Grey on the outside, magenta where a face is seen from behind (a hole in the skin shows the inside of the head)."""
    m = bpy.data.materials.new('dbg')
    nt = NT(m)
    geo = nt.node('ShaderNodeNewGeometry')
    mix = nt.node('ShaderNodeMixShader')
    grey = nt.node('ShaderNodeBsdfDiffuse')
    grey.inputs['Color'].default_value = (0.5, 0.5, 0.5, 1.0)
    em = nt.node('ShaderNodeEmission')
    em.inputs['Color'].default_value = (*back, 1.0)
    nt.nt.links.new(geo.outputs['Backfacing'], mix.inputs['Fac'])
    nt.nt.links.new(grey.outputs['BSDF'], mix.inputs[1])
    nt.nt.links.new(em.outputs['Emission'], mix.inputs[2])
    out = [n for n in m.node_tree.nodes if n.type == 'OUTPUT_MATERIAL'][0]
    m.node_tree.links.new(mix.outputs['Shader'], out.inputs['Surface'])
    return m


def stage_face(name, clay=True, subdiv=6, fstage=None, with_hair=False, debug=False):
    """The head alone (face, ears, eyes, and with with_hair the hair, brows, moustache and plait) in clay or with its materials, from
    several sides, in a few seconds: a quick look at a face or a hairstyle."""
    reset()
    setup_cycles(24, (800, 800))
    P = bd.body_params(**ch.SPEC[name]['body'])
    if fstage is not None:
        P['face'] = dict(P.get('face', {}), stage=fstage)
    L = ch.LOOKS[name]
    head = fc.build_head(P, subdiv=subdiv, verbose=True)
    ears = fc.build_ears(P)
    eyes = fc.build_eyes(P)
    print('[info] midline', fc.midline(head))
    eye_m = mats.eye_material(iris=L['iris'])
    if clay:
        mat = clay_material()
        hmat = flat_material('creta_cabelo', (0.20, 0.17, 0.14), 0.6)
    else:
        mat = ch.skin_material(L, P)
        fc.paint(head, P, 'head', hr.hairline_tint(L['hair_style']) if with_hair else None)
        fc.paint(ears, P, 'ears')
        hmat = mats.hair('cabelo', color=L['hair'], light=L['hair_light'])
    for ob in [head, ears]:
        ob.data.materials.clear()
        ob.data.materials.append(mat)
    for ob in eyes:
        ob.data.materials.clear()
        ob.data.materials.append(eye_m)
    if with_hair:
        t = time.time()
        hi, lo = hr.hair(P, L['hair_style'], hmat)
        print('[hair] sheet %d tris, proxy %d tris, %.1fs' % (sum(len(p.vertices) - 2 for p in hi.data.polygons),
                                                              sum(len(p.vertices) - 2 for p in lo.data.polygons), time.time() - t))
        if 'proxy' in ARGS:                         # look at the low-poly sheet instead of the high-poly one
            hi.hide_render = True
            lo.hide_render = False
            assign(lo, hmat)
        elif 'noproxy' in ARGS:
            bpy.data.objects.remove(lo)
        pieces = [hr.brows(head, P, hmat, **L.get('brows', {}))]
        if L.get('moustache'):
            pieces.append(hr.moustache(head, P, hmat, **L['moustache']))
        if L.get('braid'):
            pieces.append(ch.gm_braid(hmat))
        for ob in pieces:
            print('[hair]', ob.name, len(ob.data.polygons), 'faces')
        if L.get('cap'):
            import garments as gm
            cap = gm.flat_cap(mats.fabric('tweed', L['cap_col'], pattern='tweed', stain=0.3), mats.fabric('veludo', L['cuff'], stain=0.3))
    if debug:
        for ob in [head, ears] + ([hi] if with_hair else []):
            ob.data.materials.clear()
            ob.data.materials.append(debug_material())
        for ob in eyes:
            ob.data.materials.clear()
            ob.data.materials.append(flat_material('verde', (0.1, 0.6, 0.1), 0.5))
    studio()
    if debug:
        if with_hair:
            shoot({'seam2': LAB_VIEWS_HAIR['seam2'], 'temple': LAB_VIEWS_HAIR['temple'], 'lat2': LAB_VIEWS_HAIR['lat2']}, name + '_lab')
            return
        shoot({'occhio': ((0.0, -0.22, 1.636), (0.0, -0.07, 1.632), 95), 'occhio2': ((0.0, -0.18, 1.57), (0.0, -0.07, 1.632), 95),
               'occhio3': ((0.18, -0.20, 1.64), (0.02, -0.07, 1.632), 95), 'occhio4': ((-0.18, -0.20, 1.64), (-0.02, -0.07, 1.632), 95)}, name + '_lab')
        return
    shoot(LAB_VIEWS_HAIR if with_hair else LAB_VIEWS, name + '_lab')


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
    elif stage == 'face':
        stage_face(ARGS[1] if len(ARGS) > 1 else 'custode', clay='skin' not in ARGS, subdiv=8 if "fine" in ARGS else 7,
                   fstage=next((int(a[1:]) for a in ARGS if len(a) > 1 and a[0] == 's' and a[1:].isdigit()), None),
                   with_hair='hair' in ARGS, debug='dbg' in ARGS)
    elif stage == 'export':
        stage_export(ARGS[1] if len(ARGS) > 1 else 'custode')
    else:
        print('unknown stage', stage)
