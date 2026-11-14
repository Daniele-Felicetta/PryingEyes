"""Equipment found in the abandoned city district."""
from pathlib import Path
import sys
import math
sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools"))
from content_catalog import read_catalog, SCHEMA
import json
from piezas import *
from armi_pistole import make_pistol, finish_pistol


def municipal_pistol(entry):
    """A catalog recipe: all variant identity, dimensions and finish come from data."""
    p = entry['source']['parameters']
    contract = json.loads(SCHEMA.read_text())['properties']['weapons']['items']['properties']['source']['properties']['parameters']
    if set(p) != set(contract['required']):
        raise ValueError('MODEL_PARAMETERS: missing or unknown parameters')
    for key, value in p.items():
        spec = contract['properties'][key]
        if 'enum' in spec:
            if value not in spec['enum']: raise ValueError('MODEL_PARAMETERS: ' + key)
        elif key == 'polymerColor':
            if not isinstance(value, list) or len(value) != 3 or any(not isinstance(v, (int, float)) or not math.isfinite(v) or not 0 <= v <= 1 for v in value):
                raise ValueError('MODEL_PARAMETERS: polymerColor')
        elif isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value) or not spec['minimum'] <= value <= spec['maximum'] or (spec['type'] == 'integer' and not isinstance(value, int)):
            raise ValueError('MODEL_PARAMETERS: ' + key)
    identity = entry['definition']['id']
    g = Gun(identity, tex=int(p['textureSize']))
    g.paint = {'polimero': tuple(p['polymerColor'])}
    zb, y0, y1 = p['boreHeight'], p['barrelStart'], p['barrelEnd']
    top = make_pistol(g, zb, y0, y1, frame='polimero', grip='polimero',
                      serr=p['rearSerrations'], serr_front=p['frontSerrations'], ribs=True)
    finish_pistol(g, identity, zb, y0, y1, top)
    g.part('corpo')
    g.box('acciaio', (0, y1 - 0.013, zb - 0.011), (0.030, 0.075, 0.022), r=0.003)
    g.box('polimero', (0, 0.092, zb - 0.034), (0.029, 0.055, 0.014), r=0.002)
    g.box('acciaio', (0.020, 0.090, zb - 0.040), (0.012, 0.033, 0.018), r=0.002)
    g.box('vetro', (0.020, 0.110, zb - 0.040), (0.010, 0.002, 0.012), r=0.001)
    g.box('ottone', (-0.014, -0.045, zb + 0.011), (0.002, 0.025, 0.006), r=0.0005)
    g.empty('muzzle', (0, y1 + 0.028, zb), (0, 1, 0))
    return g


def register_catalog(registry):
    for entry in read_catalog()['weapons']:
        family = entry['source'].get('family')
        if family is None:
            continue
        if family != 'municipal_pistol':
            raise ValueError('MODEL_FAMILY_UNKNOWN: ' + family)
        identity = entry['definition']['id']
        if identity in registry:
            raise ValueError('MODEL_DUPLICATE: ' + identity)
        registry[identity] = lambda entry=entry: municipal_pistol(entry)
