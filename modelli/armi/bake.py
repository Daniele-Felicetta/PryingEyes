"""
UV e bake PBR di un'arma in un solo atlante (colore, ruvidezza, metallicita', normali tangenti, occlusione), poi FBX.
I pezzi di vetro e il reticolo (lente_*, retino) restano fuori dall'atlante: in Unity hanno materiali propri.
Le immagini grezze vanno in tex/raw/; pack_tex.py (python di sistema) le impacchetta come le vuole Unity.
"""
import bpy, os, sys, time
from math import radians

DIR = os.path.dirname(os.path.abspath(__file__))
import kit, mats

NO_ATLAS = ('lente_', 'retino')


def is_atlas_obj(name):
    return not name.startswith(NO_ATLAS)


def unwrap(objs):
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=radians(66), island_margin=0.0030, area_weight=0.0, correct_aspect=True, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode='OBJECT')


def new_image(name, size, noncolor):
    if name in bpy.data.images:
        bpy.data.images.remove(bpy.data.images[name])
    im = bpy.data.images.new(name, size, size, alpha=False, float_buffer=False)
    im.generated_color = (0.0, 0.0, 0.0, 1.0)
    if noncolor:
        im.colorspace_settings.name = 'Non-Color'
    return im


def attach(matset, im):
    for m in matset:
        nt = m.node_tree
        n = nt.nodes.get('BAKE_IMG')
        if n is None:
            n = nt.nodes.new('ShaderNodeTexImage')
            n.name = 'BAKE_IMG'
        n.image = im
        for x in nt.nodes:
            x.select = False
        n.select = True
        nt.nodes.active = n


def bake(objs, typ, samples, **kw):
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.context.scene.cycles.samples = samples
    bpy.ops.object.bake(type=typ, margin=10, margin_type='EXTEND', use_clear=True, use_selected_to_active=False, **kw)


def stage_bake(names, make, REG):
    out = os.path.join(DIR, 'tex', 'raw')
    os.makedirs(out, exist_ok=True)
    for nm in names:
        t0 = time.time()
        g = make(nm)
        bake_gun(g, nm, out)
        print(f'[bake] {nm} in {time.time() - t0:.0f} s')


def bake_gun(g, nm, outdir):
    sc = bpy.context.scene
    size = g.tex
    kit.setup_cycles(32, (256, 256))
    objs = [o for k, o in g.objs.items() if o.type == 'MESH' and is_atlas_obj(k)]
    unwrap(objs)
    matset = []
    for o in objs:
        for m in o.data.materials:
            if m is not None and m not in matset and m.name.split('.')[0][2:] in mats.BAKED:
                matset.append(m)
    # fondale per l'AO
    if sc.world is None:
        sc.world = bpy.data.worlds.new('mondo')
    sc.world.use_nodes = True
    sc.world.light_settings.distance = 0.05
    passes = [('base', 'EMIT', False, 40, {}),
              ('rough', 'ROUGHNESS', True, 24, {}),
              ('metal', 'EMIT', True, 24, {}),
              ('normal', 'NORMAL', True, 24, {'normal_space': 'TANGENT'}),
              ('ao', 'AO', True, 160, {})]
    for key, typ, nonc, samples, kw in passes:
        im = new_image(f'bk_{nm}_{key}', size, nonc)
        attach(matset, im)
        mode = {'base': 'color', 'metal': 'metal'}.get(key)
        if mode:
            mats.set_mode(matset, mode)
        t0 = time.time()
        bake(objs, typ, samples, **kw)
        if mode:
            mats.set_mode(matset, 'full')
        im.filepath_raw = os.path.join(outdir, f'{nm}_{key}.png')
        im.file_format = 'PNG'
        im.save()
        print(f'   {key}: {time.time() - t0:.1f} s')
    finish_materials(g, nm)
    # FBX: un solo materiale d'atlante + i ruoli speciali
    kit.setup_cycles(8, (64, 64))
    objs_all = [g.root] + list(g.objs.values())
    kit.export_fbx(os.path.join(DIR, 'export', nm + '.fbx'), objs_all)
    tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in g.objs.values() if o.type == 'MESH')
    print(f'[fbx] {nm}: {len(g.objs)} oggetti, {tris} triangoli')


def finish_materials(g, nm):
    """Ogni mesh dell'atlante: i ruoli cotti diventano uno slot 'A_<arma>', gli altri (emit) restano."""
    atlas = bpy.data.materials.new('A_' + nm)
    atlas.use_nodes = True
    for k, o in g.objs.items():
        if o.type != 'MESH' or not is_atlas_obj(k):
            continue
        old = [m for m in o.data.materials]
        new_slots = []
        remap = {}
        for i, m in enumerate(old):
            role = m.name.split('.')[0][2:]
            tgt = atlas if role in mats.BAKED else m
            if tgt not in new_slots:
                new_slots.append(tgt)
            remap[i] = new_slots.index(tgt)
        # togliere gli slot azzera gli indici dei poligoni: si salvano prima e si riassegnano dopo
        old_idx = [p.material_index for p in o.data.polygons]
        o.data.materials.clear()
        for m in new_slots:
            o.data.materials.append(m)
        for p, oi in zip(o.data.polygons, old_idx):
            p.material_index = remap.get(oi, 0)
