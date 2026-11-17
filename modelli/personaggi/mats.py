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
         brow_col=(0.05, 0.04, 0.032), ruddy=0.25, weather=0.0):
    """Skin in head-local coordinates (eye line at z = 0): lips, brows, flushed cheeks and nose, eye bags, stubble."""
    m = bpy.data.materials.new(name)
    nt = NT(m)
    p = nt.vmath('SUBTRACT', nt.obj, tuple(sk.HEAD_C))
    x, y, z = nt.xyz(p)
    ax = nt.math('ABSOLUTE', x)
    big = nt.noise(nt.obj, 16.0, 5.0, 0.55)
    fine = nt.noise(nt.obj, 420.0, 5.0, 0.6)
    col = nt.mix(nt.mr(big, 0.3, 0.7, 0.0, 1.0), _scale(tone, 0.93), _scale(tone, 1.07))
    flush = tuple(min(1.0, c) for c in (tone[0] * 1.12, tone[1] * 0.78, tone[2] * 0.72))
    # cheeks and nose are redder
    cheek = nt.math('MULTIPLY', nt.mr(ax, 0.02, 0.05, 0.0, 1.0), nt.mr(z, -0.055, -0.02, 0.0, 1.0))
    cheek = nt.math('MULTIPLY', cheek, nt.mr(y, -0.02, -0.07, 0.0, 1.0))
    nose = nt.math('MULTIPLY', nt.mr(ax, 0.02, 0.008, 0.0, 1.0), nt.mr(z, -0.044, -0.030, 0.0, 1.0))
    col = nt.mix(nt.math('MULTIPLY', nt.math('ADD', cheek, nose, clamp=True), ruddy), col, flush)
    # eye bags
    bag = nt.math('MULTIPLY', nt.mr(z, -0.024, -0.012, 0.0, 1.0), nt.mr(z, -0.003, -0.010, 1.0, 0.0))
    bag = nt.math('MULTIPLY', bag, nt.math('MULTIPLY', nt.mr(ax, 0.016, 0.024, 0.0, 1.0), nt.mr(ax, 0.050, 0.040, 0.0, 1.0)))
    col = nt.mix(nt.math('MULTIPLY', bag, 0.35), col, _scale(tone, 0.55))
    # lips
    lz = nt.math('MULTIPLY', nt.mr(z, -0.091, -0.0855, 0.0, 1.0), nt.mr(z, -0.0665, -0.060, 1.0, 0.0))
    lz = nt.math('MULTIPLY', lz, nt.mr(ax, 0.027, 0.020, 0.0, 1.0))
    lz = nt.math('MULTIPLY', lz, nt.mr(y, -0.079, -0.088, 0.0, 1.0))
    col = nt.mix(nt.math('MULTIPLY', lz, 0.9), col, lip)
    # mouth line
    ml = nt.math('MULTIPLY', nt.mr(z, -0.0805, -0.0768, 1.0, 0.0), nt.mr(z, -0.0720, -0.0768, 0.0, 1.0))
    ml = nt.math('MULTIPLY', ml, nt.mr(ax, 0.030, 0.022, 0.0, 1.0))
    col = nt.mix(nt.math('MULTIPLY', ml, 0.8), col, _scale(lip, 0.22))
    # brows
    bz = nt.math('MULTIPLY', nt.mr(z, 0.014, 0.020, 0.0, 1.0), nt.mr(z, 0.040, 0.033, 1.0, 0.0))
    bx = nt.math('MULTIPLY', nt.mr(ax, 0.008, 0.016, 0.0, 1.0), nt.mr(ax, 0.058, 0.046, 1.0, 0.0))
    br = nt.math('MULTIPLY', nt.math('MULTIPLY', bz, bx), nt.mr(y, -0.078, -0.086, 0.0, 1.0))
    br = nt.math('MULTIPLY', br, nt.mr(fine, 0.3, 0.6, 0.35, 1.0))
    col = nt.mix(nt.math('MULTIPLY', br, 0.85), col, brow_col)
    if stubble > 0.0:
        sz = nt.mr(z, -0.0, -0.045, 0.0, 1.0)
        sy = nt.mr(y, 0.02, -0.04, 0.0, 1.0)
        dots = nt.mr(nt.noise(nt.obj, 1500.0, 1.0, 0.5), 0.55, 0.7, 0.0, 1.0)
        sm = nt.math('MULTIPLY', nt.math('MULTIPLY', sz, sy), nt.math('MULTIPLY', dots, stubble))
        col = nt.mix(sm, col, stubble_col)
    if weather > 0.0:
        wr = nt.math('MULTIPLY', nt.mr(nt.noise(nt.obj, 60.0, 4.0, 0.6), 0.5, 0.75, 0.0, 1.0), weather)
        col = nt.mix(wr, col, _scale(tone, 0.7))
    pores = nt.noise(nt.obj, 900.0, 5.0, 0.65)
    nrm = nt.bump(pores, 0.35, 0.0008)
    b = nt.bsdf({'Base Color': col, 'Roughness': 0.46, 'Normal': nrm, 'Specular IOR Level': 0.45,
                 'Subsurface Weight': 0.12, 'Subsurface Scale': 0.01})
    b.inputs['Subsurface Radius'].default_value = (1.0, 0.35, 0.22)
    return m


def hair(name, color=(0.30, 0.30, 0.31), light=(0.55, 0.55, 0.56), rough=0.5, flow=(520.0, 520.0, 60.0), spec=0.5):
    """Hair: streaks along the object z axis (falling hair), darker in the depth."""
    m = bpy.data.materials.new(name)
    nt = NT(m)
    st = nt.noise(nt.vmath('MULTIPLY', nt.obj, flow), 1.0, 3.0, 0.65)
    st2 = nt.noise(nt.vmath('MULTIPLY', nt.obj, (flow[0] * 0.25, flow[1] * 0.25, flow[2] * 0.4)), 1.0, 2.0, 0.5)
    mixv = nt.math('ADD', nt.math('MULTIPLY', st, 0.65), nt.math('MULTIPLY', st2, 0.35), clamp=True)
    col = nt.mix(nt.mr(mixv, 0.35, 0.7, 0.0, 1.0), color, light)
    ao = nt.ao(0.012)
    col = nt.mix(nt.mr(ao, 0.2, 1.0, 0.25, 1.0), _scale(color, 0.4), col)
    nrm = nt.bump(st, 0.8, 0.0012)
    nt.bsdf({'Base Color': col, 'Roughness': rough, 'Normal': nrm, 'Specular IOR Level': spec,
             'Sheen Weight': 0.2, 'Sheen Roughness': 0.4})
    return m
