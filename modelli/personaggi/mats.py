"""
Procedural materials for the survivors (Cycles node trees). They are used for the previews and as the source of the bakes:
the exported textures are baked from these onto the low-poly meshes. Everything is driven by object-space coordinates (the
objects sit at the origin, so object space is world space), never by UVs: the high-poly meshes have none.
"""
from kit import *
import skeleton as sk


def eye_material(name='olho', iris=(0.22, 0.14, 0.07), sclera=(0.80, 0.77, 0.72)):
    """Eyeball whose front pole looks along local -y: sclera with faint veins, a streaked iris, a dark limbal ring and a pupil."""
    m = bpy.data.materials.new(name)
    nt = NT(m)
    n = nt.vmath('NORMALIZE', nt.obj)
    x, y, z = nt.xyz(n)
    theta = nt.math('ARCCOSINE', nt.math('MULTIPLY', y, -1.0), clamp=False)
    phi = nt.math('ARCTAN2', x, z)
    streak = nt.noise(nt.comb(nt.math('MULTIPLY', phi, 2.2), nt.math('MULTIPLY', theta, 9.0), 0.0), 7.0, 3.0, 0.6)
    fine = nt.noise(nt.comb(nt.math('MULTIPLY', phi, 5.0), nt.math('MULTIPLY', theta, 22.0), 3.0), 12.0, 2.0, 0.5)
    dark = tuple(c * 0.45 for c in iris)
    light = tuple(min(1.0, c * 1.9 + 0.03) for c in iris)
    iris_col = nt.mix(nt.math('MULTIPLY', streak, fine, clamp=True), dark, light)
    # limbal ring and pupil
    ring = nt.mr(theta, 0.36, 0.47, 0.0, 1.0, smooth=True)
    iris_col = nt.mix(nt.math('MULTIPLY', ring, 0.9), iris_col, (0.02, 0.015, 0.012))
    in_iris = nt.mr(theta, 0.455, 0.485, 1.0, 0.0, smooth=True)
    pupil = nt.mr(theta, 0.15, 0.185, 1.0, 0.0, smooth=True)
    iris_col = nt.mix(pupil, iris_col, (0.003, 0.003, 0.003))
    # sclera: slightly darker and redder towards the corners
    veins = nt.noise(nt.vmath('MULTIPLY', nt.obj, (260.0, 260.0, 260.0)), 3.0, 4.0, 0.7)
    red = nt.math('MULTIPLY', nt.mr(theta, 0.9, 1.5, 0.0, 1.0), nt.mr(veins, 0.45, 0.62, 0.0, 0.55), clamp=True)
    scl = nt.mix(red, sclera, (0.62, 0.30, 0.27))
    base = nt.mix(in_iris, scl, iris_col)
    # the lids shade the ball: darker under the upper lid and towards the corners (baked, so the eye sits in its socket at any distance)
    shade = nt.math('ADD', nt.math('MULTIPLY', nt.mr(z, 0.20, 0.85, 0.0, 1.0), 0.42), nt.math('MULTIPLY', nt.mr(theta, 0.95, 1.5, 0.0, 1.0), 0.40), clamp=True)
    base = nt.mix(shade, base, (0.0, 0.0, 0.0))
    nt.bsdf({'Base Color': base, 'Roughness': 0.12, 'Coat Weight': 1.0, 'Coat Roughness': 0.02, 'Specular IOR Level': 0.7})
    return m


# ----------------------------------------------------------------------------- cloth, leather, metal, rubber
def _scale(c, k):
    return tuple(min(1.0, v * k) for v in c)


def surface(name, color, rough=0.8, sheen=0.0, grain=(380.0, 0.5), fold=(50.0, 0.35), stain=0.3, dirt=0.35, wear=0.0,
            pattern=None, tint=0.12, metal=0.0, spec=0.5, ao_dist=0.035, dirt_col=(0.022, 0.018, 0.014), light=None):
    """Generic worn material: colour variation, crevice darkening, stains, dirt rising from the floor, edge wear and two bump scales."""
    m = bpy.data.materials.new(name)
    nt = NT(m)
    x, y, z = nt.xyz(nt.obj)
    big = nt.noise(nt.obj, 5.5, 5.0, 0.55)
    mid = nt.noise(nt.obj, 22.0, 5.0, 0.55)
    col = nt.mix(nt.mr(big, 0.35, 0.65, 0.0, 1.0), _scale(color, 1.0 - tint), _scale(color, 1.0 + tint))
    if light is not None:
        col = nt.mix(nt.mr(mid, 0.4, 0.62, 0.0, 1.0), col, light)
    if pattern == 'twill':
        d = nt.math('ADD', nt.math('ADD', x, y), z)
        band = nt.wave(nt.comb(d, d, d), 900.0, 'X', 'SIN')
        col = nt.mix(nt.math('MULTIPLY', band, 0.10), col, _scale(color, 1.25))
    elif pattern == 'tweed':
        fl = nt.noise(nt.obj, 900.0, 2.0, 0.7)
        col = nt.mix(nt.mr(fl, 0.4, 0.62, 0.0, 0.6), col, _scale(color, 1.6))
    # crevices and stains
    ao = nt.ao(ao_dist)
    col = nt.mix(nt.mr(ao, 0.15, 0.95, 0.0, 1.0), nt.mix(0.55, col, dirt_col), col)
    st = nt.math('MULTIPLY', nt.mr(nt.noise(nt.obj, 9.0, 6.0, 0.6), 0.52, 0.78, 0.0, 1.0), stain)
    col = nt.mix(st, col, dirt_col)
    low = nt.math('MULTIPLY', nt.mr(z, 0.0, 0.62, 1.0, 0.0), dirt)
    col = nt.mix(nt.math('MULTIPLY', low, nt.mr(mid, 0.3, 0.7, 0.4, 1.0)), col, dirt_col)
    if wear > 0.0:
        # edge wear: lighter, shinier where the surface is convex
        edge = nt.math('SUBTRACT', 1.0, nt.mr(nt.ao(0.012, True), 0.5, 1.0, 0.0, 1.0))
        wmask = nt.math('MULTIPLY', nt.mr(nt.noise(nt.obj, 40.0, 4.0, 0.6), 0.45, 0.7, 0.0, 1.0), wear)
        col = nt.mix(nt.math('MULTIPLY', edge, wmask), col, _scale(color, 1.9))
    h1 = nt.noise(nt.obj, grain[0], 6.0, 0.62)
    h2 = nt.noise(nt.obj, fold[0], 4.0, 0.5)
    nrm = nt.bump(nt.math('ADD', nt.math('MULTIPLY', h1, grain[1]), nt.math('MULTIPLY', h2, fold[1])), 1.0, 0.002)
    r = nt.mixf(nt.mr(mid, 0.3, 0.7, 0.0, 1.0), max(0.05, rough - 0.12), min(1.0, rough + 0.12))
    inputs = {'Base Color': col, 'Roughness': r, 'Metallic': metal, 'Normal': nrm, 'Specular IOR Level': spec}
    if sheen > 0.0:
        inputs.update({'Sheen Weight': sheen, 'Sheen Roughness': 0.5})
    nt.bsdf(inputs)
    return m


def fabric(name, color, **kw):
    kw.setdefault('rough', 0.92)
    kw.setdefault('sheen', 0.25)
    kw.setdefault('spec', 0.3)
    return surface(name, color, **kw)


def leather(name, color, **kw):
    kw.setdefault('rough', 0.52)
    kw.setdefault('grain', (520.0, 0.8))
    kw.setdefault('wear', 0.35)
    kw.setdefault('spec', 0.5)
    return surface(name, color, **kw)


def rubber(name, color=(0.025, 0.024, 0.023), **kw):
    kw.setdefault('rough', 0.78)
    kw.setdefault('stain', 0.15)
    kw.setdefault('dirt', 0.4)
    return surface(name, color, **kw)


def metal(name, color=(0.55, 0.5, 0.38), rough=0.38, **kw):
    kw.setdefault('stain', 0.25)
    kw.setdefault('dirt', 0.1)
    kw.setdefault('wear', 0.0)
    return surface(name, color, rough=rough, metal=1.0, grain=(700.0, 0.25), fold=(60.0, 0.1), spec=0.6, **kw)


# ----------------------------------------------------------------------------- skin and hair
def skin(name, tone=(0.50, 0.33, 0.25), lip=(0.38, 0.14, 0.12), stubble=0.0, stubble_col=(0.06, 0.05, 0.045),
         hair_col=(0.05, 0.04, 0.03), ruddy=0.25, weather=0.0, age=0.0):
    """Skin in head-local coordinates (eye line z = 0). The features that depend on the shape of this face (lips, mouth line, brow
    zone, lash line, nostrils, eye sockets, flush, beard, the tint under the hairline) are not computed here: face.paint writes them on
    the vertices as the colour attributes fa, fb and fc, so that they always sit on the sculpted features."""
    m = bpy.data.materials.new(name)
    nt = NT(m)
    p = nt.vmath('SUBTRACT', nt.obj, tuple(sk.HEAD_C))
    x, y, z = nt.xyz(p)
    big = nt.noise(nt.obj, 14.0, 5.0, 0.55)
    mid = nt.noise(nt.obj, 90.0, 4.0, 0.6)
    fine = nt.noise(nt.obj, 420.0, 5.0, 0.6)
    lips, mline, brow, lash = nt.attr4('fa')
    nostril, socket, flush, beard = nt.attr4('fb')
    hairl, lidt, _c, _d = nt.attr4('fc')
    col = nt.mix(nt.mr(big, 0.3, 0.7, 0.0, 1.0), _scale(tone, 0.93), _scale(tone, 1.07))
    # a little more colour in the large blotches (capillaries, sun): the flush colour at low strength
    flush_col = tuple(min(1.0, c) for c in (tone[0] * 1.14, tone[1] * 0.72, tone[2] * 0.66))
    col = nt.mix(nt.math('MULTIPLY', nt.mr(mid, 0.35, 0.75, 0.0, 0.22), 1.0), col, flush_col)
    col = nt.mix(nt.math('MULTIPLY', flush, ruddy, clamp=True), col, flush_col)
    # eyelids: thinner, darker and redder skin; sockets: shadowed
    lid_col = tuple(min(1.0, c) for c in (tone[0] * 0.80, tone[1] * 0.52, tone[2] * 0.47))
    col = nt.mix(nt.math('MULTIPLY', lidt, 0.60), col, lid_col)
    sock_col = (tone[0] * 0.55, tone[1] * 0.33, tone[2] * 0.29)
    col = nt.mix(nt.math('MULTIPLY', socket, 0.42), col, sock_col)
    # the shade of the beard zone: stubble is a darkening with dots, never a flat colour
    if stubble > 0.0:
        dots = nt.mr(nt.noise(nt.obj, 1700.0, 1.0, 0.5), 0.50, 0.66, 0.0, 1.0)
        shade = nt.math('MULTIPLY', beard, nt.math('ADD', nt.math('MULTIPLY', dots, 0.65 * stubble), 0.30 * stubble))
        col = nt.mix(nt.math('MULTIPLY', shade, 1.0, clamp=True), col, stubble_col)
    if weather > 0.0:
        wr = nt.math('MULTIPLY', nt.mr(nt.noise(nt.obj, 60.0, 4.0, 0.6), 0.5, 0.75, 0.0, 1.0), weather)
        col = nt.mix(wr, col, _scale(tone, 0.72))
    # hair roots under the edge of the hair (a soft shade, so the hairline is never a hard line)
    col = nt.mix(nt.math('MULTIPLY', hairl, 0.55), col, hair_col)
    # lips (the vermilion is lighter at the border and darker along the mouth), mouth line, nostrils, brow shade, lashes
    lip_lines = nt.noise(nt.vmath('MULTIPLY', nt.obj, (900.0, 180.0, 60.0)), 2.0, 2.0, 0.6)
    lipc = nt.mix(nt.mr(lip_lines, 0.4, 0.7, 0.0, 0.25), lip, _scale(lip, 0.6))
    col = nt.mix(nt.math('MULTIPLY', lips, 0.95), col, lipc)
    col = nt.mix(nt.math('MULTIPLY', mline, 0.85), col, _scale(lip, 0.20))
    col = nt.mix(nt.math('MULTIPLY', nostril, 0.92), col, (0.050, 0.020, 0.018))
    brow_shade = tuple(0.55 * c + 0.45 * h for c, h in zip(_scale(tone, 0.72), hair_col))
    col = nt.mix(nt.math('MULTIPLY', brow, 0.60), col, brow_shade)
    col = nt.mix(nt.math('MULTIPLY', lash, 0.90), col, (0.014, 0.009, 0.007))
    # relief: pores everywhere, fine lines on the lips, creases of the forehead and the outer corners of the eyes with age
    pores = nt.noise(nt.obj, 900.0, 5.0, 0.65)
    mic = nt.noise(nt.obj, 260.0, 4.0, 0.55)
    height = nt.math('ADD', nt.math('MULTIPLY', pores, 0.55), nt.math('MULTIPLY', mic, 0.45))
    height = nt.math('ADD', height, nt.math('MULTIPLY', nt.math('MULTIPLY', lips, nt.math('SUBTRACT', lip_lines, 0.5)), 1.4))
    if age > 0.0:
        fz = nt.math('MULTIPLY', nt.mr(z, 0.040, 0.052, 0.0, 1.0), nt.mr(z, 0.086, 0.074, 0.0, 1.0))
        fz = nt.math('MULTIPLY', fz, nt.mr(y, -0.050, -0.072, 0.0, 1.0))
        wob = nt.math('MULTIPLY', nt.noise(nt.obj, 55.0, 2.0, 0.5), 0.012)
        lines = nt.wave(nt.comb(0.0, 0.0, nt.math('ADD', z, wob)), 520.0, 'Z', 'SIN')
        height = nt.math('ADD', height, nt.math('MULTIPLY', nt.math('MULTIPLY', fz, nt.math('SUBTRACT', lines, 0.5)), 2.2 * age))
    nrm = nt.bump(height, 0.38, 0.0009)
    rough = nt.mixf(nt.mr(fine, 0.35, 0.65, 0.0, 1.0), 0.40, 0.56)
    rough = nt.mixf(nt.math('MULTIPLY', lips, 1.0, clamp=True), rough, 0.32)
    b = nt.bsdf({'Base Color': col, 'Roughness': rough, 'Normal': nrm, 'Specular IOR Level': 0.45,
                 'Subsurface Weight': 0.12, 'Subsurface Scale': 0.01})
    b.inputs['Subsurface Radius'].default_value = (1.0, 0.35, 0.22)
    return m


def hair(name, color=(0.30, 0.30, 0.31), light=(0.55, 0.55, 0.56), rough=0.48, spec=0.5, gloss=0.2):
    """Hair: strands that follow the flow stored on the vertices (`hflow`), locks that differ in tone (`hf`: lock id, distance from the
    root), a darker depth between the locks and a fine relief along the strands. Where there is no flow the strands fall along z."""
    m = bpy.data.materials.new(name)
    nt = NT(m)
    idc, along, _b, _a = nt.attr4('hf')
    fl = nt.attr('hflow')
    d = nt.vmath('SUBTRACT', nt.vmath('MULTIPLY', fl, (2.0, 2.0, 2.0)), (1.0, 1.0, 1.0))
    dn = nt.vmath('NORMALIZE', d)
    dist = nt.vmath('DOT_PRODUCT', nt.obj, dn)
    q = nt.vmath('SUBTRACT', nt.obj, nt.vmath('SCALE', dn, scale=dist))
    s1 = nt.noise(q, 1300.0, 2.0, 0.55)
    s2 = nt.noise(q, 260.0, 3.0, 0.6)
    s3 = nt.noise(q, 60.0, 2.0, 0.5)
    tone = nt.math('ADD', nt.math('ADD', nt.math('MULTIPLY', s1, 0.40), nt.math('MULTIPLY', s2, 0.35)),
                   nt.math('ADD', nt.math('MULTIPLY', s3, 0.15), nt.math('MULTIPLY', idc, 0.30)), clamp=True)
    col = nt.mix(nt.mr(tone, 0.30, 0.75, 0.0, 1.0), color, light)
    ao = nt.ao(0.010)
    col = nt.mix(nt.mr(ao, 0.2, 1.0, 0.30, 1.0), _scale(color, 0.35), col)
    # the ends of the locks are a little lighter and drier
    col = nt.mix(nt.math('MULTIPLY', nt.mr(along, 0.80, 1.0, 0.0, 1.0), 0.25), col, _scale(light, 1.15))
    h = nt.math('ADD', nt.math('MULTIPLY', s1, 0.55), nt.math('MULTIPLY', s2, 0.45))
    nrm = nt.bump(h, 0.9, 0.0010)
    r = nt.mixf(nt.mr(s2, 0.3, 0.7, 0.0, 1.0), max(0.05, rough - 0.10), min(1.0, rough + 0.10))
    nt.bsdf({'Base Color': col, 'Roughness': r, 'Normal': nrm, 'Specular IOR Level': spec,
             'Sheen Weight': gloss, 'Sheen Roughness': 0.4})
    return m
