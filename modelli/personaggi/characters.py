"""
The four survivors. Each entry says how the body is shaped (SPEC) and what the character wears and looks like (LOOKS); build(name)
returns the high-poly parts as (object, group) pairs, group being one of 'skin', 'outfit', 'hair', 'eyes' (one texture set each
after the bake). All four share the skeleton, the garment patterns and the pipeline: they differ in proportions, face, skin, hair,
colours of the clothes, facial hair and cap, so on a screen with four players nobody is mistaken for somebody else.
Colours are linear values (a dark olive canvas is about 0.09).

hair_style: a dict for hair.sheet_object (the lower edge of the hair around the head, the thickness, how locks are cut: see hair.py);
a style with 'start' is the short hair at the sides and back that shows under a cap; 'release' makes the hair hang free (a bob).
'braid' adds the plait down the back; 'moustache' and 'brows' (size, thickness) describe the facial hair.
"""
from kit import *
import skeleton as sk
import body as bd
import face as fc
import garments as gm
import hair as hr
import mats

ORDER = ['custode', 'dottoressa', 'reporter', 'cacciatrice']

SPEC = {
    'custode': dict(
        title='Il Custode',
        body=dict(shoulder=1.02, chest=1.05, waist=1.10, hips=1.06, belly=0.018, arm=1.05, thigh=1.04, neck=1.08,
                  face=dict(width=1.03, jaw=1.02, chin=1.0, nose_h=1.10, nose_w=1.10, nose_len=1.05, brow=1.25, lip=0.90, cheek=0.95,
                            age=0.75, mouth_w=1.05, eye=0.97)),
    ),
    'dottoressa': dict(
        title='La Dottoressa',
        body=dict(shoulder=0.93, chest=0.95, waist=0.90, hips=1.08, belly=0.0, arm=0.92, thigh=1.04, neck=0.90,
                  face=dict(width=0.94, jaw=0.90, chin=0.88, nose_h=0.88, nose_w=0.86, nose_len=0.95, brow=0.75, lip=1.15, cheek=1.10,
                            hollow=0.25, age=0.10, mouth_w=0.94, eye=1.08, eye_tilt=0.02)),
    ),
    'reporter': dict(
        title='Il Reporter',
        body=dict(shoulder=0.98, chest=0.97, waist=0.95, hips=0.98, belly=0.0, arm=0.98, thigh=0.97, neck=0.98,
                  face=dict(width=0.98, jaw=0.98, chin=1.0, nose_h=1.0, nose_w=0.98, brow=1.0, lip=1.0, cheek=1.0, age=0.15, mouth_w=1.0)),
    ),
    'cacciatrice': dict(
        title='La Cacciatrice',
        body=dict(shoulder=0.96, chest=0.97, waist=0.90, hips=1.06, belly=0.0, arm=0.98, thigh=1.08, neck=0.94,
                  face=dict(width=0.96, jaw=0.94, chin=0.98, nose_h=0.98, nose_w=0.90, brow=0.92, lip=1.05, cheek=1.18, hollow=0.40,
                            age=0.20, mouth_w=0.98, eye=1.02, eye_tilt=0.03)),
    ),
}

LOOKS = {
    'custode': dict(
        skin=(0.40, 0.27, 0.205), stubble=0.5, stubble_col=(0.26, 0.25, 0.24), weather=0.35, ruddy=0.35,
        hair=(0.20, 0.20, 0.21), hair_light=(0.55, 0.54, 0.52),
        hair_style=dict(start=[(0, 0.062), (60, 0.060), (90, 0.052), (110, 0.040), (130, 0.026), (180, 0.022)],
                        edge=[(0, 0.058), (38, 0.054), (52, 0.030), (60, 0.008), (66, -0.016), (74, -0.012), (82, 0.020), (116, 0.020), (124, -0.004), (136, -0.028), (150, -0.040), (165, -0.046), (180, -0.048)],
                        top=0.0075, side=0.0075, clump=5.5, relief=0.0011, tips=0.0025, saw=0.0030, seed=2.0),
        moustache=dict(size=1.15, droop=1.0, thick=0.0085), brows=dict(thick=0.0016, scale=1.15), cap=True,
        iris=(0.20, 0.14, 0.08),
        jacket=(0.085, 0.092, 0.050), cuff=(0.060, 0.038, 0.022), trousers=(0.030, 0.040, 0.075), patch=(0.055, 0.032, 0.020),
        scarf=(0.20, 0.022, 0.028), boots=(0.060, 0.034, 0.021), gloves=(0.10, 0.050, 0.026), cap_col=(0.090, 0.082, 0.066),
    ),
    'dottoressa': dict(
        skin=(0.30, 0.19, 0.14), stubble=0.0, stubble_col=(0.1, 0.1, 0.1), weather=0.15, ruddy=0.25,
        hair=(0.060, 0.038, 0.024), hair_light=(0.21, 0.125, 0.07),
        hair_style=dict(release=93.0, hang_front=(30.0, 52.0),
                        edge=[(0, 0.050), (20, 0.044), (32, 0.030), (42, -0.030), (56, -0.070), (96, -0.076), (130, -0.082), (180, -0.078)],
                        edge_r=[(0, 0.050), (20, 0.034), (30, 0.016), (40, -0.034), (56, -0.070), (96, -0.076), (130, -0.082), (180, -0.078)],
                        top=0.0135, side=0.0155, front=0.004, bulge=0.006, curl=0.008, clump=7.0, relief=0.0013, wobble=0.12, tips=0.0045, saw=0.0045, part=(14.0, 2.2), seed=3.0),
        moustache=None, brows=dict(thick=0.0011, scale=0.85), cap=False,
        iris=(0.10, 0.18, 0.10),
        jacket=(0.42, 0.40, 0.34), cuff=(0.30, 0.28, 0.24), trousers=(0.035, 0.035, 0.040), patch=(0.04, 0.04, 0.045),
        scarf=(0.020, 0.120, 0.130), boots=(0.040, 0.025, 0.018), gloves=(0.020, 0.020, 0.022), cap_col=(0.09, 0.08, 0.07),
    ),
    'reporter': dict(
        skin=(0.55, 0.38, 0.29), stubble=0.25, stubble_col=(0.22, 0.16, 0.10), weather=0.10, ruddy=0.30,
        hair=(0.20, 0.13, 0.045), hair_light=(0.46, 0.32, 0.11),     # dark blond: the paler one read as a shaved head against the skin
        hair_style=dict(edge=[(0, 0.054), (25, 0.050), (42, 0.040), (60, 0.012), (68, -0.016), (75, -0.012), (82, 0.024), (115, 0.024), (124, 0.000), (136, -0.026), (150, -0.038), (165, -0.044), (180, -0.046)],
                        edge_r=[(0, 0.054), (20, 0.040), (38, 0.034), (55, 0.026), (62, 0.008), (68, -0.016), (75, -0.012), (82, 0.024), (115, 0.024), (124, 0.000), (136, -0.026), (150, -0.038), (165, -0.044), (180, -0.046)],
                        top=0.014, side=0.0065, front=0.005, front_w=55.0, clump=6.0, relief=0.0012, wobble=0.14, tips=0.0035, saw=0.004, part=(-10.0, 2.5), seed=4.0),
        moustache=None, brows=dict(thick=0.0013, scale=1.0), cap=False,
        iris=(0.10, 0.15, 0.25),
        jacket=(0.34, 0.20, 0.030), cuff=(0.20, 0.12, 0.025), trousers=(0.060, 0.100, 0.200), patch=(0.05, 0.08, 0.16),
        scarf=(0.35, 0.35, 0.36), boots=(0.12, 0.070, 0.035), gloves=(0.16, 0.090, 0.050), cap_col=(0.09, 0.08, 0.07),
    ),
    'cacciatrice': dict(
        skin=(0.42, 0.29, 0.21), stubble=0.0, stubble_col=(0.1, 0.1, 0.1), weather=0.30, ruddy=0.40,
        hair=(0.17, 0.060, 0.030), hair_light=(0.40, 0.16, 0.07),
        hair_style=dict(start=[(0, 0.062), (60, 0.060), (90, 0.052), (110, 0.040), (130, 0.026), (180, 0.022)],
                        edge=[(0, 0.058), (38, 0.054), (52, 0.030), (60, 0.008), (66, -0.016), (74, -0.012), (82, 0.022), (116, 0.022), (124, -0.002), (136, -0.028), (150, -0.040), (165, -0.046), (180, -0.048)],
                        top=0.0075, side=0.0075, clump=6.0, relief=0.0011, tips=0.002, saw=0.0030, seed=5.0),
        braid=True, moustache=None, brows=dict(thick=0.0012, scale=0.95), cap=True,
        iris=(0.22, 0.12, 0.05),
        jacket=(0.13, 0.025, 0.030), cuff=(0.07, 0.035, 0.025), trousers=(0.050, 0.060, 0.030), patch=(0.05, 0.035, 0.02),
        scarf=(0.28, 0.15, 0.030), boots=(0.025, 0.020, 0.020), gloves=(0.20, 0.12, 0.060), cap_col=(0.055, 0.045, 0.035),
    ),
}


def skin_material(L, P, name='pele'):
    """The skin of a character: tone, lips (from the tone unless the look gives them), stubble, weather, age of the face."""
    tone = L['skin']
    lip = L.get('lip', (tone[0] * 0.88, tone[1] * 0.40, tone[2] * 0.42))
    return mats.skin(name, tone=tone, lip=lip, stubble=L['stubble'], stubble_col=L['stubble_col'], hair_col=L['hair'],
                     ruddy=L['ruddy'], weather=L['weather'], age=P.get('face', {}).get('age', 0.0))


def gm_braid(hair_mat):
    """The plait (high-poly weave) and its low-poly proxy tube."""
    hi = hr.braid(hair_mat)
    lo = hr.braid_proxy(hair_mat)
    hi['lowproxy'] = lo.name
    return hi


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
    skin = skin_material(L, P)
    hair = mats.hair('cabelo', color=L['hair'], light=L['hair_light'])
    canvas = mats.fabric('lona', L['jacket'], pattern='twill', stain=0.4, dirt=0.35)
    cord = mats.fabric('veludo', L['cuff'], stain=0.3)
    trousers = mats.fabric('calca', L['trousers'], pattern='twill', stain=0.35, dirt=0.45)
    brown = mats.leather('couro', L['gloves'])
    boot = mats.leather('bota', L['boots'], wear=0.5)
    rubber = mats.rubber('sola')
    brass = mats.metal('latao', (0.62, 0.50, 0.25))
    eye = mats.eye_material(iris=L['iris'])
    # body parts that show: the sculpted head (with the masks its skin material reads), ears, neck, eyes
    style = L['hair_style']
    head = fc.build_head(P, subdiv=8 if not head_only else 7, verbose=True)
    fc.paint(head, P, 'head', hr.hairline_tint(style))
    head.data.materials.append(skin)
    ears = fc.build_ears(P)
    fc.paint(ears, P, 'ears')
    ears.data.materials.append(skin)
    neck = gm.neck_skin(P, skin)
    fc.paint(neck, P, 'neck')
    add([head, ears, neck], 'skin')
    add(fc.build_eyes(P), 'eyes')
    for e in [o for o, g in parts if g == 'eyes']:
        e.data.materials.append(eye)
    # hair: the sheet over the head (and its low-poly proxy), brows, moustache, plait
    hi, lo = hr.hair(P, style, hair)
    add(hi, 'hair')
    if L.get('braid'):
        add(gm_braid(hair), 'hair')
    if L.get('moustache'):
        add(hr.moustache(head, P, hair, **L['moustache']), 'hair')
    add(hr.brows(head, P, hair, **L.get('brows', {})), 'hair')
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
