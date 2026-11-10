"""
Materiali procedurali per le armi (nodi Cycles) e modalita' di bake.

Ogni ruolo (W_acciaio, W_alluminio, W_polimero, W_legno, W_gomma, W_ottone, W_cromo, W_inox) ha una rete di nodi che
produce colore, metallicita', ruvidezza e normale (rumore, graffi, usura sugli spigoli, sporco nelle cavita') e li
collega a un Principled BSDF. Per il bake la stessa rete si collega a un Emission (metallicita') con set_mode().
I ruoli vetro, emit e retino sono semplici: in Unity li sostituiscono materiali dedicati e non finiscono nell'atlante.
"""
import bpy
from math import radians

BAKED = ('acciaio', 'alluminio', 'polimero', 'legno', 'gomma', 'ottone', 'cromo', 'inox', 'osso', 'carne')
_cache = {}


def nd(nt, typ, props=None, loc=None, **inputs):
    n = nt.nodes.new(typ)
    for k, v in (props or {}).items():
        setattr(n, k, v)
    for k, v in inputs.items():
        n.inputs[k.replace('_', ' ')].default_value = v
    return n


def ln(nt, a, b):
    nt.links.new(a, b)


def math_(nt, op, a, b=None, clamp=False):
    n = nt.nodes.new('ShaderNodeMath')
    n.operation = op
    n.use_clamp = clamp
    for i, v in enumerate((a, b)):
        if v is None:
            continue
        if isinstance(v, (int, float)):
            n.inputs[i].default_value = v
        else:
            ln(nt, v, n.inputs[i])
    return n.outputs['Value']


def maprange(nt, val, a0, a1, b0=0.0, b1=1.0, clamp=True):
    n = nt.nodes.new('ShaderNodeMapRange')
    n.clamp = clamp
    n.inputs['From Min'].default_value = a0
    n.inputs['From Max'].default_value = a1
    n.inputs['To Min'].default_value = b0
    n.inputs['To Max'].default_value = b1
    if isinstance(val, (int, float)):
        n.inputs['Value'].default_value = val
    else:
        ln(nt, val, n.inputs['Value'])
    return n.outputs['Result']


def mix(nt, fac, c0, c1):
    n = nt.nodes.new('ShaderNodeMixRGB')
    n.blend_type = 'MIX'
    if isinstance(fac, (int, float)):
        n.inputs['Fac'].default_value = fac
    else:
        ln(nt, fac, n.inputs['Fac'])
    for nm, c in (('Color1', c0), ('Color2', c1)):
        if isinstance(c, (tuple, list)):
            n.inputs[nm].default_value = (*c[:3], 1.0)
        else:
            ln(nt, c, n.inputs[nm])
    return n.outputs['Color']


def noise(nt, vec, scale, detail=4.0, rough=0.55, dist=0.0):
    n = nd(nt, 'ShaderNodeTexNoise', Scale=scale, Detail=detail, Roughness=rough, Distortion=dist)
    ln(nt, vec, n.inputs['Vector'])
    return n.outputs['Fac']


def mapping(nt, vec, scale=(1, 1, 1), loc=(0, 0, 0), rot=(0, 0, 0)):
    n = nt.nodes.new('ShaderNodeMapping')
    n.inputs['Scale'].default_value = scale
    n.inputs['Location'].default_value = loc
    n.inputs['Rotation'].default_value = rot
    ln(nt, vec, n.inputs['Vector'])
    return n.outputs['Vector']


def ramp(nt, fac, stops):
    n = nt.nodes.new('ShaderNodeValToRGB')
    el = n.color_ramp.elements
    while len(el) > 2:
        el.remove(el[-1])
    el[0].position, el[0].color = stops[0][0], (*stops[0][1][:3], 1.0)
    el[1].position, el[1].color = stops[1][0], (*stops[1][1][:3], 1.0)
    for pos, col in stops[2:]:
        e = el.new(pos)
        e.color = (*col[:3], 1.0)
    ln(nt, fac, n.inputs['Fac'])
    return n.outputs['Color']


def bump(nt, height, strength, dist=0.001):
    n = nd(nt, 'ShaderNodeBump', Strength=strength, Distance=dist)
    ln(nt, height, n.inputs['Height'])
    return n.outputs['Normal']


# ----------------------------------------------------------------------------- maschere comuni
def masks(nt):
    tc = nt.nodes.new('ShaderNodeTexCoord')
    obj = tc.outputs['Object']
    aoe = nd(nt, 'ShaderNodeAmbientOcclusion', props={'inside': True, 'samples': 6, 'only_local': True}, Distance=0.0030)
    edge = math_(nt, 'SUBTRACT', 1.0, aoe.outputs['AO'])
    aoc = nd(nt, 'ShaderNodeAmbientOcclusion', props={'inside': False, 'samples': 6, 'only_local': False}, Distance=0.014)
    cav = math_(nt, 'SUBTRACT', 1.0, aoc.outputs['AO'])
    blot = noise(nt, obj, 38.0, 5.0, 0.6, 0.4)
    fine = noise(nt, obj, 900.0, 2.0, 0.5)
    # graffi: linee di livello (|rumore - 0.5| piccolo) di un rumore molto allungato lungo y / x: sottili e rade
    st = mapping(nt, obj, scale=(40.0, 1.3, 30.0))
    nz = noise(nt, st, 36.0, 1.0, 0.5)
    c1 = maprange(nt, math_(nt, 'ABSOLUTE', math_(nt, 'SUBTRACT', nz, 0.5)), 0.0, 0.0035, 1.0, 0.0)
    st2 = mapping(nt, obj, scale=(1.0, 22.0, 26.0), rot=(0.0, 0.0, 0.5))
    nz2 = noise(nt, st2, 48.0, 1.0, 0.5)
    c2 = maprange(nt, math_(nt, 'ABSOLUTE', math_(nt, 'SUBTRACT', nz2, 0.5)), 0.0, 0.0030, 1.0, 0.0)
    scratches = math_(nt, 'MAXIMUM', c1, math_(nt, 'MULTIPLY', c2, 0.8))
    return dict(obj=obj, edge=edge, cav=cav, blot=blot, fine=fine, scratch=scratches)


def wear_of(nt, m, amount=1.0, scr_w=0.45):
    """Usura: spigoli (interrotti da chiazze) piu' graffi."""
    e = maprange(nt, m['edge'], 0.30, 0.78, 0.0, 1.0)
    patch = maprange(nt, m['blot'], 0.32, 0.62, 0.25, 1.0)
    w = math_(nt, 'MULTIPLY', e, patch)
    s = math_(nt, 'MULTIPLY', m['scratch'], scr_w)
    return math_(nt, 'MULTIPLY', math_(nt, 'ADD', w, s, clamp=True), amount, clamp=True)


# ----------------------------------------------------------------------------- ruoli
def _principled(nt, base, metallic, rough, normal=None, coat=0.0, coat_rough=0.2, emis=None):
    p = nd(nt, 'ShaderNodeBsdfPrincipled')
    for nm, v in (('Base Color', base), ('Metallic', metallic), ('Roughness', rough)):
        if isinstance(v, (tuple, list)):
            p.inputs[nm].default_value = (*v[:3], 1.0)
        elif isinstance(v, (int, float)):
            p.inputs[nm].default_value = v
        else:
            ln(nt, v, p.inputs[nm])
    if normal is not None:
        ln(nt, normal, p.inputs['Normal'])
    if coat > 0:
        p.inputs['Coat Weight'].default_value = coat
        p.inputs['Coat Roughness'].default_value = coat_rough
    return p


def build_role(role, paint=None):
    nm = 'W_' + role
    m = bpy.data.materials.new(nm)
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    paint = paint or {}
    if role in ('vetro', 'emit', 'retino') or role.startswith('emit'):
        if role == 'vetro':
            g = nd(nt, 'ShaderNodeBsdfGlass', Color=(0.55, 0.78, 0.85, 1), Roughness=0.0, IOR=1.45)
            ln(nt, g.outputs['BSDF'], out.inputs['Surface'])
        else:
            e = nd(nt, 'ShaderNodeEmission', Color=(0.1, 1.0, 0.25, 1) if role == 'emit' else (1.0, 0.55, 0.08, 1) if role == 'emit_ambra' else (1.0, 0.05, 0.03, 1), Strength=6.0)
            ln(nt, e.outputs['Emission'], out.inputs['Surface'])
        return m
    mk = masks(nt)
    obj = mk['obj']
    metal_sock = None
    if role == 'acciaio':
        base0 = paint.get('acciaio', (0.052, 0.056, 0.066))
        bare = (0.44, 0.45, 0.47)
        w = wear_of(nt, mk, 1.0, 0.40)
        vary = maprange(nt, mk['blot'], 0.3, 0.7, 0.75, 1.15)
        base = mix(nt, w, mix(nt, 0.0, base0, base0), bare)
        base = _mulrgb(nt, base, mix(nt, 0.0, (1, 1, 1), (1, 1, 1)), vary)
        base = _mulrgb(nt, base, None, math_(nt, 'SUBTRACT', 1.0, math_(nt, 'MULTIPLY', mk['cav'], 0.55)))
        rough = math_(nt, 'ADD', maprange(nt, w, 0.0, 1.0, 0.50, 0.30), math_(nt, 'MULTIPLY', math_(nt, 'SUBTRACT', mk['fine'], 0.5), 0.14))
        metal = maprange(nt, w, 0.0, 1.0, 0.88, 1.0)
        nrm = bump(nt, math_(nt, 'ADD', math_(nt, 'MULTIPLY', mk['fine'], 0.5), math_(nt, 'MULTIPLY', mk['scratch'], -1.2)), 0.35, 0.0008)
        p = _principled(nt, base, metal, rough, nrm)
        metal_sock = metal
    elif role == 'alluminio':
        base0 = paint.get('alluminio', (0.034, 0.036, 0.040))
        bare = (0.58, 0.59, 0.61)
        w = wear_of(nt, mk, 1.0, 0.30)
        base = mix(nt, w, base0, bare)
        base = _mulrgb(nt, base, None, math_(nt, 'SUBTRACT', 1.0, math_(nt, 'MULTIPLY', mk['cav'], 0.5)))
        rough = math_(nt, 'ADD', maprange(nt, w, 0.0, 1.0, 0.58, 0.34), math_(nt, 'MULTIPLY', math_(nt, 'SUBTRACT', mk['fine'], 0.5), 0.12))
        metal = maprange(nt, w, 0.0, 1.0, 0.82, 1.0)
        nrm = bump(nt, math_(nt, 'ADD', math_(nt, 'MULTIPLY', mk['fine'], 0.6), math_(nt, 'MULTIPLY', mk['scratch'], -1.0)), 0.30, 0.0007)
        p = _principled(nt, base, metal, rough, nrm)
        metal_sock = metal
    elif role == 'polimero':
        base0 = paint.get('polimero', (0.024, 0.025, 0.027))
        w = wear_of(nt, mk, 0.7, 0.25)
        light = paint.get('polimero_usura', (0.075, 0.077, 0.082))
        base = mix(nt, w, base0, light)
        base = _mulrgb(nt, base, None, math_(nt, 'SUBTRACT', 1.0, math_(nt, 'MULTIPLY', mk['cav'], 0.45)))
        vor = nt.nodes.new('ShaderNodeTexVoronoi')
        vor.feature = 'F1'
        vor.inputs['Scale'].default_value = 650.0
        vor.inputs['Randomness'].default_value = 1.0
        ln(nt, obj, vor.inputs['Vector'])
        stip = maprange(nt, vor.outputs['Distance'], 0.0, 0.62, 1.0, 0.0)
        rough = math_(nt, 'ADD', maprange(nt, w, 0.0, 1.0, 0.78, 0.60), math_(nt, 'MULTIPLY', math_(nt, 'SUBTRACT', mk['fine'], 0.5), 0.10))
        nrm = bump(nt, math_(nt, 'ADD', math_(nt, 'MULTIPLY', stip, 0.8), math_(nt, 'MULTIPLY', mk['fine'], 0.3)), 0.55, 0.0006)
        p = _principled(nt, base, 0.0, rough, nrm)
        metal_sock = 0.0
    elif role == 'legno':
        # venatura lungo y: coordinate molto compresse in y
        st = mapping(nt, obj, scale=(1.0, 0.045, 1.0))
        wv = nt.nodes.new('ShaderNodeTexWave')
        wv.wave_type = 'BANDS'
        wv.bands_direction = 'X'
        wv.wave_profile = 'SIN'
        wv.inputs['Scale'].default_value = 70.0
        wv.inputs['Distortion'].default_value = 7.0
        wv.inputs['Detail'].default_value = 3.0
        wv.inputs['Detail Scale'].default_value = 1.8
        wv.inputs['Detail Roughness'].default_value = 0.6
        ln(nt, st, wv.inputs['Vector'])
        grain = wv.outputs['Fac']
        dark = paint.get('legno_scuro', (0.030, 0.012, 0.005))
        lite = paint.get('legno_chiaro', (0.125, 0.056, 0.022))
        wood = ramp(nt, grain, [(0.15, dark), (0.85, lite)])
        w = wear_of(nt, mk, 0.8, 0.20)
        base = mix(nt, math_(nt, 'MULTIPLY', w, 0.55), wood, (0.20, 0.11, 0.055))
        base = _mulrgb(nt, base, None, math_(nt, 'SUBTRACT', 1.0, math_(nt, 'MULTIPLY', mk['cav'], 0.5)))
        pores = noise(nt, mapping(nt, obj, scale=(1.0, 0.06, 1.0)), 900.0, 1.0, 0.5)
        rough = math_(nt, 'ADD', maprange(nt, w, 0.0, 1.0, 0.36, 0.62), math_(nt, 'MULTIPLY', math_(nt, 'SUBTRACT', pores, 0.5), 0.15))
        nrm = bump(nt, math_(nt, 'ADD', math_(nt, 'MULTIPLY', grain, 0.2), math_(nt, 'MULTIPLY', pores, 0.5)), 0.30, 0.0005)
        p = _principled(nt, base, 0.0, rough, nrm, coat=0.35, coat_rough=0.18)
        metal_sock = 0.0
    elif role == 'osso':
        # osso d'avorio ingiallito: sporco giallo nelle cavita', crepe sottili, schegge chiare sugli spigoli, lucentezza velata
        base0 = paint.get('osso', (0.42, 0.37, 0.27))
        w = wear_of(nt, mk, 0.5, 0.15)
        dirty = mix(nt, math_(nt, 'MULTIPLY', mk['cav'], 1.4, clamp=True), base0, (0.17, 0.10, 0.045))
        crack = mix(nt, math_(nt, 'MULTIPLY', mk['scratch'], 0.7), dirty, (0.10, 0.055, 0.03))
        base = mix(nt, w, crack, (0.60, 0.55, 0.44))
        rough = math_(nt, 'ADD', maprange(nt, w, 0.0, 1.0, 0.46, 0.60), math_(nt, 'MULTIPLY', math_(nt, 'SUBTRACT', mk['fine'], 0.5), 0.12))
        nrm = bump(nt, math_(nt, 'ADD', math_(nt, 'MULTIPLY', mk['fine'], 0.5), math_(nt, 'MULTIPLY', mk['blot'], 0.4)), 0.30, 0.0006)
        p = _principled(nt, base, 0.0, rough, nrm, coat=0.22, coat_rough=0.30)
        metal_sock = 0.0
    elif role == 'carne':
        # carne umida: rete di vene (distanza dal bordo di Voronoi), rosso scuro, lucida come bagnata
        vor = nt.nodes.new('ShaderNodeTexVoronoi')
        vor.feature = 'DISTANCE_TO_EDGE'
        vor.inputs['Scale'].default_value = 160.0
        ln(nt, obj, vor.inputs['Vector'])
        veins = maprange(nt, vor.outputs['Distance'], 0.0, 0.07, 1.0, 0.0)
        base = mix(nt, veins, paint.get('carne', (0.30, 0.050, 0.045)), (0.13, 0.010, 0.020))
        base = _mulrgb(nt, base, None, math_(nt, 'SUBTRACT', 1.0, math_(nt, 'MULTIPLY', mk['cav'], 0.5)))
        rough = math_(nt, 'ADD', 0.20, math_(nt, 'MULTIPLY', math_(nt, 'SUBTRACT', mk['blot'], 0.5), 0.16))
        nrm = bump(nt, math_(nt, 'ADD', math_(nt, 'MULTIPLY', veins, 0.5), math_(nt, 'MULTIPLY', mk['fine'], 0.25)), 0.55, 0.0012)
        p = _principled(nt, base, 0.0, rough, nrm, coat=0.65, coat_rough=0.06)
        metal_sock = 0.0
    elif role == 'gomma':
        base0 = (0.016, 0.016, 0.017)
        w = wear_of(nt, mk, 0.5, 0.2)
        base = mix(nt, w, base0, (0.06, 0.06, 0.062))
        wv = nt.nodes.new('ShaderNodeTexWave')
        wv.wave_type = 'BANDS'
        wv.bands_direction = 'Z'
        wv.inputs['Scale'].default_value = 260.0
        ln(nt, obj, wv.inputs['Vector'])
        nrm = bump(nt, math_(nt, 'ADD', math_(nt, 'MULTIPLY', wv.outputs['Fac'], 0.6), math_(nt, 'MULTIPLY', mk['fine'], 0.4)), 0.5, 0.0008)
        p = _principled(nt, base, 0.0, 0.88, nrm)
        metal_sock = 0.0
    elif role == 'ottone':
        base0 = (0.74, 0.50, 0.19)
        w = wear_of(nt, mk, 0.5, 0.2)
        base = mix(nt, w, base0, (0.88, 0.66, 0.30))
        base = _mulrgb(nt, base, None, math_(nt, 'SUBTRACT', 1.0, math_(nt, 'MULTIPLY', mk['cav'], 0.6)))
        rough = math_(nt, 'ADD', 0.30, math_(nt, 'MULTIPLY', math_(nt, 'SUBTRACT', mk['fine'], 0.5), 0.14))
        nrm = bump(nt, math_(nt, 'MULTIPLY', mk['scratch'], -1.0), 0.2, 0.0006)
        p = _principled(nt, base, 1.0, rough, nrm)
        metal_sock = 1.0
    elif role in ('cromo', 'inox'):
        bright = role == 'cromo'
        base0 = paint.get(role, (0.62, 0.63, 0.65) if bright else (0.50, 0.51, 0.53))
        w = wear_of(nt, mk, 0.25, 0.7)
        base = _mulrgb(nt, mix(nt, 0.0, base0, base0), None, math_(nt, 'SUBTRACT', 1.0, math_(nt, 'MULTIPLY', mk['cav'], 0.5)))
        rough = math_(nt, 'ADD', 0.10 if bright else 0.30, math_(nt, 'MULTIPLY', math_(nt, 'ADD', mk['scratch'], math_(nt, 'MULTIPLY', mk['fine'], 0.4)), 0.30))
        nrm = bump(nt, math_(nt, 'MULTIPLY', mk['scratch'], -1.0), 0.15, 0.0005)
        p = _principled(nt, base, 1.0, rough, nrm)
        metal_sock = 1.0
    else:
        base = (0.2, 0.2, 0.2)
        p = _principled(nt, base, 0.0, 0.5)
        metal_sock = 0.0
    ln(nt, p.outputs['BSDF'], out.inputs['Surface'])
    # shader di servizio per il bake del colore: il bake DIFFUSE da' nero sui metalli (il diffuso pesa 1 - metallico),
    # quindi il colore di base si cuoce come emissione
    ec = nt.nodes.new('ShaderNodeEmission')
    if isinstance(base, (tuple, list)):
        ec.inputs['Color'].default_value = (*base[:3], 1.0)
    else:
        ln(nt, base, ec.inputs['Color'])
    m['emission_color'] = ec.name
    # shader di servizio per il bake della metallicita'
    em = nt.nodes.new('ShaderNodeEmission')
    if isinstance(metal_sock, (int, float)) or metal_sock is None:
        val = nt.nodes.new('ShaderNodeValue')
        val.outputs[0].default_value = float(metal_sock or 0.0)
        metal_sock = val.outputs[0]
    comb = nt.nodes.new('ShaderNodeCombineColor')
    for ch in ('Red', 'Green', 'Blue'):
        ln(nt, metal_sock, comb.inputs[ch])
    ln(nt, comb.outputs['Color'], em.inputs['Color'])
    m['principled'] = p.name
    m['emission'] = em.name
    m['output'] = out.name
    return m


def _mulrgb(nt, c, c2, fac):
    """c * fac (fac scalare); c2 e' ignorato (compatibilita' di firma)."""
    n = nt.nodes.new('ShaderNodeMixRGB')
    n.blend_type = 'MULTIPLY'
    n.inputs['Fac'].default_value = 1.0
    if isinstance(c, (tuple, list)):
        n.inputs['Color1'].default_value = (*c[:3], 1.0)
    else:
        ln(nt, c, n.inputs['Color1'])
    comb = nt.nodes.new('ShaderNodeCombineColor')
    for ch in ('Red', 'Green', 'Blue'):
        if isinstance(fac, (int, float)):
            comb.inputs[ch].default_value = fac
        else:
            ln(nt, fac, comb.inputs[ch])
    ln(nt, comb.outputs['Color'], n.inputs['Color2'])
    return n.outputs['Color']


def material(role, gun=None):
    """Chiamata da Gun.build(): un materiale per ruolo per arma (la tinta del polimero ecc. sta in gun.paint)."""
    paint = getattr(gun, 'paint', None) if gun is not None else None
    key = (role, id(gun), 'x')
    if key in _cache and _cache[key].name in bpy.data.materials:
        return _cache[key]
    m = build_role(role, paint)
    m.name = 'W_' + role
    _cache[key] = m
    return m


def set_mode(mats, mode):
    """mode 'full' = Principled; 'metal' = Emission (metallicita' come colore); 'color' = Emission (colore di base) per il bake."""
    for m in mats:
        if 'output' not in m.keys():
            continue
        nt = m.node_tree
        out = nt.nodes[m['output']]
        src = nt.nodes[m['principled'] if mode == 'full' else m['emission'] if mode == 'metal' else m['emission_color']]
        ln(nt, src.outputs[0], out.inputs['Surface'])
