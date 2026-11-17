"""
The four survivors. Each entry says how the body is shaped (SPEC) and what the character wears and looks like (LOOKS); build(name)
returns the high-poly parts as (object, group) pairs, group being one of 'skin', 'outfit', 'hair', 'eyes' (one texture set each
after the bake). All four share the skeleton, the garment patterns and the pipeline: they differ in proportions, face, skin, hair,
colours of the clothes, facial hair and cap, so on a screen with four players nobody is mistaken for somebody else.
Colours are linear values (a dark olive canvas is about 0.09).

hair_style: ('shell',) is the short hair at the sides and the back that shows under a cap; ('do', {...}) is a full head of hair
(garments.hair_do: hairline, length at the nape, how far the shell is stretched down); 'braid' adds the plait down the back.
"""
from kit import *
import skeleton as sk
import body as bd
import garments as gm
import mats

ORDER = ['custode', 'dottoressa', 'reporter', 'cacciatrice']

SPEC = {
    'custode': dict(
        title='Il Custode',
        body=dict(shoulder=1.02, chest=1.05, waist=1.10, hips=1.06, belly=0.018, arm=1.05, thigh=1.04, neck=1.08,
                  face=dict(width=1.03, jaw=1.08, nose_h=1.12, nose_w=1.12, brow=1.15, lip=0.9)),
    ),
    'dottoressa': dict(
        title='La Dottoressa',
        body=dict(shoulder=0.93, chest=0.95, waist=0.90, hips=1.08, belly=0.0, arm=0.92, thigh=1.04, neck=0.90,
                  face=dict(width=0.95, jaw=0.94, nose_h=0.95, nose_w=0.92, brow=0.90, lip=1.08)),
    ),
    'reporter': dict(
        title='Il Reporter',
        body=dict(shoulder=0.98, chest=0.97, waist=0.95, hips=0.98, belly=0.0, arm=0.98, thigh=0.97, neck=0.98,
                  face=dict(width=0.98, jaw=0.96, nose_h=1.02, nose_w=0.98, brow=1.0, lip=1.0)),
    ),
    'cacciatrice': dict(
        title='La Cacciatrice',
        body=dict(shoulder=0.96, chest=0.97, waist=0.90, hips=1.06, belly=0.0, arm=0.98, thigh=1.08, neck=0.94,
                  face=dict(width=0.97, jaw=0.95, nose_h=1.0, nose_w=0.94, brow=0.96, lip=1.06)),
    ),
}

LOOKS = {
    'custode': dict(
        skin=(0.40, 0.27, 0.205), stubble=0.5, stubble_col=(0.26, 0.25, 0.24), weather=0.35, ruddy=0.35,
        hair=(0.22, 0.22, 0.23), hair_light=(0.52, 0.51, 0.50), hair_style=('shell',), moustache=True, cap=True,
        iris=(0.20, 0.14, 0.08),
        jacket=(0.085, 0.092, 0.050), cuff=(0.060, 0.038, 0.022), trousers=(0.030, 0.040, 0.075), patch=(0.055, 0.032, 0.020),
        scarf=(0.20, 0.022, 0.028), boots=(0.060, 0.034, 0.021), gloves=(0.10, 0.050, 0.026), cap_col=(0.090, 0.082, 0.066),
    ),
    'dottoressa': dict(
        skin=(0.30, 0.19, 0.14), stubble=0.0, stubble_col=(0.1, 0.1, 0.1), weather=0.15, ruddy=0.25,
        hair=(0.070, 0.045, 0.030), hair_light=(0.20, 0.12, 0.07),
        hair_style=('do', dict(z_back=-0.078, tall=0.035, hairline=0.054, side_drop=0.030)), moustache=False, cap=False,
        iris=(0.10, 0.18, 0.10),
        jacket=(0.42, 0.40, 0.34), cuff=(0.30, 0.28, 0.24), trousers=(0.035, 0.035, 0.040), patch=(0.04, 0.04, 0.045),
        scarf=(0.020, 0.120, 0.130), boots=(0.040, 0.025, 0.018), gloves=(0.020, 0.020, 0.022), cap_col=(0.09, 0.08, 0.07),
    ),
    'reporter': dict(
        skin=(0.55, 0.38, 0.29), stubble=0.25, stubble_col=(0.22, 0.16, 0.10), weather=0.10, ruddy=0.30,
        hair=(0.35, 0.25, 0.10), hair_light=(0.60, 0.45, 0.20),
        hair_style=('do', dict(z_back=-0.040, hairline=0.060, side_drop=0.036)), moustache=False, cap=False,
        iris=(0.10, 0.15, 0.25),
        jacket=(0.34, 0.20, 0.030), cuff=(0.20, 0.12, 0.025), trousers=(0.060, 0.100, 0.200), patch=(0.05, 0.08, 0.16),
        scarf=(0.35, 0.35, 0.36), boots=(0.12, 0.070, 0.035), gloves=(0.16, 0.090, 0.050), cap_col=(0.09, 0.08, 0.07),
    ),
    'cacciatrice': dict(
        skin=(0.42, 0.29, 0.21), stubble=0.0, stubble_col=(0.1, 0.1, 0.1), weather=0.30, ruddy=0.40,
        hair=(0.17, 0.060, 0.030), hair_light=(0.40, 0.16, 0.07), hair_style=('shell',), braid=True, moustache=False, cap=True,
        iris=(0.22, 0.12, 0.05),
        jacket=(0.13, 0.025, 0.030), cuff=(0.07, 0.035, 0.025), trousers=(0.050, 0.060, 0.030), patch=(0.05, 0.035, 0.02),
        scarf=(0.28, 0.15, 0.030), boots=(0.025, 0.020, 0.020), gloves=(0.20, 0.12, 0.060), cap_col=(0.055, 0.045, 0.035),
    ),
}


def build_survivor(P, look, head_only=False):
    """The high-poly parts. head_only stops after the scarf and the cap (a quick look at a face, a hairstyle and the collar)."""
    parts = []
    t0 = [time.time()]

    def add(obs, group):
        for o in (obs if isinstance(obs, (list, tuple)) else [obs]):
            parts.append((o, group))
        print('[build] %-10s %-7s %.1fs' % (parts[-1][0].name, group, time.time() - t0[0]), flush=True)
        t0[0] = time.time()
    L = look
    skin = mats.skin('pele', tone=L['skin'], stubble=L['stubble'], stubble_col=L['stubble_col'], weather=L['weather'], ruddy=L['ruddy'])
    hair = mats.hair('cabelo', color=L['hair'], light=L['hair_light'])
    canvas = mats.fabric('lona', L['jacket'], pattern='twill', stain=0.4, dirt=0.35)
    cord = mats.fabric('veludo', L['cuff'], stain=0.3)
    trousers = mats.fabric('calca', L['trousers'], pattern='twill', stain=0.35, dirt=0.45)
    brown = mats.leather('couro', L['gloves'])
    boot = mats.leather('bota', L['boots'], wear=0.5)
    rubber = mats.rubber('sola')
    brass = mats.metal('latao', (0.62, 0.50, 0.25))
    eye = mats.eye_material(iris=L['iris'])
    # body parts that show
    head = bd.build_head(P)
    head.data.materials.append(skin)
    ears = bd.build_ears(P)
    ears.data.materials.append(skin)
    add([head, ears, gm.neck_skin(P, skin)], 'skin')
    add(bd.build_eyes(P), 'eyes')
    for e in [o for o, g in parts if g == 'eyes']:
        e.data.materials.append(eye)
    # hair: a full head of hair or the short sides and back that show under a cap; brows, moustache, plait
    style = L.get('hair_style', ('shell',))
    if style[0] == 'do':
        add(gm.hair_do(hair, P, **style[1]), 'hair')
    else:
        add(gm.hair_shell(hair, P), 'hair')
    if L.get('braid'):
        add(gm.braid(hair), 'hair')
    if L['moustache']:
        add(gm.moustache(hair, 1.15), 'hair')
    add(gm.brows(hair, 0.0050), 'hair')
    # clothes
    add(gm.jacket(P, canvas, brass, canvas, g=0.015, sleeve_g=0.011, collar=0.040, cuff_mat=cord), 'outfit')
    if not head_only:
        add(gm.trousers(P, trousers, mats.leather('remendo', L['patch'], wear=0.4)), 'outfit')
    add(gm.scarf(P, mats.fabric('la', L['scarf'], grain=(240.0, 0.9), stain=0.35, sheen=0.4)), 'outfit')
    if not head_only:
        add(gm.boots(P, boot, rubber), 'outfit')
        add(gm.gloves(P, brown), 'outfit')
    if L['cap']:
        add(gm.flat_cap(mats.fabric('tweed', L['cap_col'], pattern='tweed', stain=0.3), cord), 'outfit')
    if not head_only:
        add(gm.belt_kit(P, brown, brass), 'outfit')
    return parts


def build(name, head_only=False):
    spec = SPEC[name]
    P = bd.body_params(**spec['body'])
    return build_survivor(P, LOOKS[name], head_only), P
