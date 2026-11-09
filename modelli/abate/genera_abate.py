"""
Abate (fase 4): il monaco dall'occhio ingrandito, in nero e oro, con l'aureola a raggiera e il bastone-ostensorio.
Sostituisce il bruto. Riusa il modello gia' pronto da gioco del monaco (monaco/export/monaco_export.blend: mesh, UV,
pesi) e il suo scheletro e le sue 5 animazioni (genera_monaco.py), quindi in Unity si muove con lo stesso Animator.

Uso (dalla cartella del file):
  blender -b -P genera_abate.py -- dump     triangoli della stoffa (UV + posizioni) -> stoffa_tris.npz
                                            (poi: python -I genera_abate_tex.py scrive le texture)
  blender -b -P genera_abate.py -- build    accessori, scheletro, animazioni -> abate_animato.blend + export/abate.fbx
  blender -b -P genera_abate.py -- render   anteprime Cycles -> render/
Assi come il monaco: Z in alto, guarda verso -Y, sua sinistra +X. Unita': metri (la scala la da' Unity).
"""
import bpy, bmesh, math, os, sys
import numpy as np
from mathutils import Vector, Matrix
from math import sin, cos, pi, radians

DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(os.path.dirname(DIR), 'monaco'))
import genera_monaco as M          # noqa: E402  (mesh del monaco, scheletro, pesi, animazioni, utilita')

ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
EXPORT_DIR = os.path.join(DIR, 'export')
RENDER_DIR = os.path.join(DIR, 'render')
TRIS = os.path.join(DIR, 'stoffa_tris.npz')
RIG_BLEND = os.path.join(DIR, 'abate_animato.blend')
TEX = r"C:\Users\danie\Desktop\prying-eyes\unity\PryingEyes\Assets\_PryingEyes\Models\Abate\Textures"


def V(*a):
    return Vector(a)


# ----------------------------------------------------------------------------- dump della stoffa
def stage_dump():
    bpy.ops.wm.open_mainfile(filepath=M.EXPORT_BLEND)
    ob = bpy.data.objects['stoffa']
    me = ob.data
    me.calc_loop_triangles()
    uv = me.uv_layers['UVMap'].data
    names = {vg.index: vg.name for vg in ob.vertex_groups}
    arm_w = np.zeros(len(me.vertices), np.float32)
    head_w = np.zeros(len(me.vertices), np.float32)
    for v in me.vertices:
        for g in v.groups:
            n = names[g.group]
            if n.startswith(('braccio', 'avambraccio', 'mano', 'clavicola')):
                arm_w[v.index] += g.weight
            elif n in ('testa', 'collo', 'occhio'):
                head_w[v.index] += g.weight
    T = len(me.loop_triangles)
    uvs = np.zeros((T, 3, 2), np.float32)
    pos = np.zeros((T, 3, 3), np.float32)
    region = np.zeros(T, np.int8)
    for i, lt in enumerate(me.loop_triangles):
        for k in range(3):
            uvs[i, k] = uv[lt.loops[k]].uv
            pos[i, k] = me.vertices[lt.vertices[k]].co
        aw = arm_w[list(lt.vertices)].mean()
        hw = head_w[list(lt.vertices)].mean()
        region[i] = 1 if aw > 0.5 else (2 if hw > 0.5 else 0)
    np.savez(TRIS, uv=uvs, pos=pos, region=region)
    print(f'[dump] {T} triangoli: tonaca {(region == 0).sum()} maniche {(region == 1).sum()} cappuccio {(region == 2).sum()}')


# ----------------------------------------------------------------------------- accessori
def blade(bm, c, ang, r0, r1, w, thick):
    """Raggio a rombo nel piano XZ (normale Y): base a r0, punta a r1, largo w, spesso 'thick'."""
    d = V(sin(ang), 0, cos(ang))
    t = V(cos(ang), 0, -sin(ang))
    y = V(0, 1, 0)
    mid = r0 + (r1 - r0) * 0.30
    pts = [c + d * r1, c + d * r0 + t * w / 2, c + d * r0 - t * w / 2,
           c + d * mid + y * thick / 2, c + d * mid - y * thick / 2,
           c + d * (r0 - 0.004) + y * thick * 0.35, c + d * (r0 - 0.004) - y * thick * 0.35]
    vs = [bm.verts.new(p) for p in pts]
    bmesh.ops.convex_hull(bm, input=vs)


def sunburst(name, center, R, n, r_short, r_long, ring_r, blade_w, thick, jewels=0, jewel_r=0.02):
    """Aureola a raggiera: anello (o due), raggi alternati lunghi e corti, gemme sull'anello."""
    c = Vector(center)
    bm = bmesh.new()
    M.bm_torus(bm, c, V(0, 1, 0), R, ring_r, seg=72, rseg=12)
    M.bm_torus(bm, c, V(0, 1, 0), R * 1.27, ring_r * 0.6, seg=72, rseg=10)
    for i in range(n):
        a = 2 * pi * i / n
        long_ = i % 2 == 0
        blade(bm, c, a, R * 1.27 + ring_r * 0.5, r_long if long_ else r_short, blade_w * (1.0 if long_ else 0.75), thick)
    for i in range(jewels):
        a = 2 * pi * (i + 0.5) / jewels
        M.bm_sphere(bm, c + V(sin(a), 0, cos(a)) * R, jewel_r, seg=14)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    uvl = bm.loops.layers.uv.new('UVMap')
    for f in bm.faces:
        for lo in f.loops:
            co = lo.vert.co
            lo[uvl].uv = ((co.x - c.x) * 2.2 + 0.5, (co.z - c.z) * 2.2 + 0.5 + co.y * 1.3)
    ob = M.bm_obj(bm, name)
    ob.data.shade_smooth()
    return ob


# ----------------------------------------------------------------------------- pesi
def only(bone):
    return lambda p: {bone: 1.0}


# ----------------------------------------------------------------------------- materiali di anteprima
def gold_material():
    m = bpy.data.materials.new('M_oro')
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    bs = nt.nodes.new('ShaderNodeBsdfPrincipled')
    nt.links.new(bs.outputs[0], out.inputs['Surface'])

    def tex(name, cs):
        n = nt.nodes.new('ShaderNodeTexImage')
        n.image = bpy.data.images.load(os.path.join(TEX, name))
        n.image.colorspace_settings.name = cs
        n.image.alpha_mode = 'CHANNEL_PACKED'
        return n
    b = tex('abate_oro_base.png', 'sRGB')
    mk = tex('abate_oro_mask_unity.png', 'Non-Color')
    nn = tex('abate_oro_normal.png', 'Non-Color')
    nm = nt.nodes.new('ShaderNodeNormalMap')
    inv = nt.nodes.new('ShaderNodeMath')
    inv.operation = 'SUBTRACT'
    inv.inputs[0].default_value = 1.0
    sep = nt.nodes.new('ShaderNodeSeparateColor')
    nt.links.new(b.outputs['Color'], bs.inputs['Base Color'])
    nt.links.new(mk.outputs['Alpha'], inv.inputs[1])
    nt.links.new(inv.outputs[0], bs.inputs['Roughness'])
    nt.links.new(mk.outputs['Color'], sep.inputs['Color'])
    nt.links.new(sep.outputs['Red'], bs.inputs['Metallic'])
    nt.links.new(nn.outputs['Color'], nm.inputs['Color'])
    nt.links.new(nm.outputs['Normal'], bs.inputs['Normal'])
    return m


def swap_cloth_textures():
    """Per le anteprime: la stoffa del monaco prende le texture dell'abate (nello stesso nodo, stessa UV)."""
    for mat in bpy.data.materials:
        if mat.name not in ('M_stoffa', 'M_stoffa_interno') or not mat.node_tree:
            continue
        for n in mat.node_tree.nodes:
            if n.type != 'TEX_IMAGE' or n.image is None:
                continue
            nm = n.image.name
            kind = 'base' if 'base' in nm else 'normal' if 'normal' in nm else None
            if kind is None:
                if 'rough' in nm:      # la ruvidezza dell'abate sta nell'alfa della maschera
                    continue
                continue
            im = bpy.data.images.load(os.path.join(TEX, f'abate_stoffa_{kind}.png'))
            im.colorspace_settings.name = 'sRGB' if kind == 'base' else 'Non-Color'
            n.image = im


# ----------------------------------------------------------------------------- build
def stage_build():
    bpy.ops.wm.open_mainfile(filepath=M.EXPORT_BLEND)
    sc = bpy.context.scene
    sc.render.fps = M.FPS
    G = Vector(list(sc['presa']))
    specs = M.bone_specs(G)

    gold = gold_material()
    halo = sunburst('aureola', (0.0, 0.205, 1.53), 0.285, 22, 0.47, 0.62, 0.015, 0.045, 0.012, jewels=8, jewel_r=0.022)
    halo.data.materials.append(gold)
    # ostensorio in cima al bastone: l'ornamento di legno arriva a z=1.49
    top = V(0.285, -0.365, 1.60)
    burst = sunburst('ostensorio', top, 0.085, 14, 0.135, 0.18, 0.0095, 0.03, 0.009, jewels=0)
    burst.data.materials.append(gold)
    eye = bpy.data.objects['occhio']
    arm = M.build_armature(specs)

    M.set_weights(halo, only('testa'))
    M.set_weights(burst, only('bastone'))
    meshes = [ob for ob in sc.objects if ob.type == 'MESH']
    for ob in meshes:
        ob.parent = arm
        md = ob.modifiers.new('Armatura', 'ARMATURE')
        md.object = arm
    acts = [M.make_action(arm, nm, **(fn(G) if fn is M.anim_death else fn())) for nm, fn in M.ANIMS]
    arm.animation_data.action = acts[0]
    sc.frame_start, sc.frame_end = 0, 120
    M.save_blend(RIG_BLEND)
    os.makedirs(EXPORT_DIR, exist_ok=True)
    M.select_only(meshes + [arm], arm)
    bpy.ops.export_scene.fbx(filepath=os.path.join(EXPORT_DIR, 'abate.fbx'), use_selection=True,
                             object_types={'ARMATURE', 'MESH'}, apply_unit_scale=True,
                             apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
                             mesh_smooth_type='FACE', use_tspace=True, add_leaf_bones=False,
                             primary_bone_axis='Y', secondary_bone_axis='X', armature_nodetype='NULL',
                             bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                             bake_anim_use_all_bones=True, bake_anim_force_startend_keying=True,
                             bake_anim_step=1.0, bake_anim_simplify_factor=0.0,
                             path_mode='RELATIVE', embed_textures=False)
    print('[build] ossa:', len(arm.data.bones), ' azioni:', [a.name for a in acts], ' mesh:', [o.name for o in meshes])


# ----------------------------------------------------------------------------- render
def stage_render():
    bpy.ops.wm.open_mainfile(filepath=RIG_BLEND)
    sc = bpy.context.scene
    swap_cloth_textures()
    M.setup_cycles(int(os.environ.get('ABATE_SAMPLES', '48')), (700, 900))
    w = bpy.data.worlds.new('mondo')
    sc.world = w
    w.use_nodes = True
    w.node_tree.nodes['Background'].inputs['Color'].default_value = (0.03, 0.03, 0.036, 1)
    M.build_studio()
    arm = bpy.data.objects['Monaco']
    os.makedirs(RENDER_DIR, exist_ok=True)
    views = {
        'fronte': ((0.0, -3.9, 1.25), (0.0, 0.0, 1.05), 40),
        'tre_quarti': ((-2.6, -3.0, 1.35), (0.0, 0.0, 1.05), 40),
        'dietro': ((1.8, 3.4, 1.4), (0.0, 0.0, 1.05), 40),
        'volto': ((-0.55, -1.35, 1.55), (0.0, -0.05, 1.5), 50),
    }
    pick = [a for a in ARGS if a in views] or list(views)
    arm.animation_data.action = bpy.data.actions['fermo']
    sc.frame_set(0)
    for nm in pick:
        loc, tgt, lens = views[nm]
        M.add_camera(loc, tgt, lens)
        sc.render.filepath = os.path.join(RENDER_DIR, f'{nm}.png')
        bpy.ops.render.render(write_still=True)
        print('[render]', sc.render.filepath)


if __name__ == '__main__':
    if 'dump' in ARGS:
        stage_dump()
    if 'build' in ARGS:
        stage_build()
    if 'render' in ARGS:
        stage_render()
