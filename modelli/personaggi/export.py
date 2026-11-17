"""
Export stage: high-poly parts -> low-poly meshes, UVs, bakes, skin weights, armature, FBX, and a preview of the baked result.

The high-poly scene is the one saved by the 'build' stage (work/<id>_hp.blend); every object carries a 'group' property:
skin, outfit, hair or eyes. Each group becomes one mesh with one material (base colour, tangent normal, mask).
"""
import os
from kit import *
import skeleton as sk
import body as bd
import mats

DIR = os.path.dirname(os.path.abspath(__file__))
UNITY_DIR = os.path.abspath(os.path.join(DIR, '../../unity/PryingEyes/Assets/_PryingEyes/Models/Survivors'))
TEX_DIR = os.path.join(UNITY_DIR, 'Textures')
WORK_DIR = os.path.join(DIR, 'work')
RENDER_DIR = os.path.join(DIR, 'render')

RES = {'skin': 1024, 'outfit': 2048, 'hair': 1024, 'eyes': 256}
# target triangle counts of the low-poly parts (a part not listed is kept as it is)
BUDGET = {
    'head': 5200, 'ears': 900, 'neck': 700, 'jacket': 7000, 'trousers': 5200, 'boots': 3000, 'glove_L': 1500, 'glove_R': 1500, 'cap': 1100,
    'scarf': 1800, 'hair': 1800, 'moustache': 700, 'brows': 500, 'hood': 2200, 'bag': 1500, 'coat': 7500, 'vest': 5000, 'braid': 1100,
}
RIGID_HEAD = {'head', 'ears', 'eye', 'hair', 'moustache', 'brows', 'cap', 'peak', 'glasses', 'hood', 'beanie', 'bun', 'mask', 'goggles'}
RIGID_HIPS = {'keys', 'pouch'}
TORSO_ONLY = {'jacket', 'zip', 'placket', 'pockets', 'buttons', 'belt', 'buckle', 'scarf', 'neck', 'coat', 'vest', 'collar', 'harness'}


def tri_count(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)


def bake_transform(ob):
    """Move the object's transform into its mesh so that the object sits at the origin."""
    ob.data.transform(ob.matrix_world)
    ob.matrix_world = Matrix.Identity(4)


def copy_object(ob, name=None):
    c = ob.copy()
    c.data = ob.data.copy()
    if name:
        c.name = name
    link(c)
    return c


def low_part(ob):
    """Low-poly copy of a part (no materials). A part that carries a proxy in its 'lowproxy' property (hair sheets and plaits, whose
    shape the decimation would wreck) gets a copy of that; any other is decimated to its budget; flat parts keep their flat shading."""
    proxy = ob.get('lowproxy')
    if proxy:
        src = bpy.data.objects[proxy]
        lp = copy_object(src, ob.name + '_low')
        lp.hide_render = False
        lp.pop('group', None)
        lp.data.materials.clear()
        bake_transform(lp)
        return lp
    lp = copy_object(ob, ob.name + '_low')
    n = tri_count(lp)
    smooth = len(lp.data.polygons) > 0 and lp.data.polygons[0].use_smooth
    goal = BUDGET.get(ob.name)
    if goal and n > goal * 1.1:
        m = lp.modifiers.new('dec', 'DECIMATE')
        m.decimate_type = 'COLLAPSE'
        m.ratio = goal / n
        m.use_collapse_triangulate = True
        apply_all(lp)
    lp.data.materials.clear()
    if smooth:
        lp.data.shade_smooth()
    bake_transform(lp)
    return lp


def weights_for(name, p):
    if name in RIGID_HEAD:
        return {'Head': 1.0}
    if name in RIGID_HIPS:
        return {'Hips': 1.0}
    if name == 'braid':                         # follows the head at the nape and the upper chest lower down, so it bends
        t = smoothstep(1.36, 1.52, p.z)
        return {'Head': t, 'UpperChest': 1.0 - t}
    if name.startswith('cuff'):
        return sk.hand_weights(p, fingers=False)
    if name.startswith('glove'):
        return sk.hand_weights(p, fingers=True)
    if name in TORSO_ONLY:
        return sk.body_weights(p, arm_margin=0.012, fingers=False, leg_scale=0.0)
    return sk.body_weights(p, fingers=False)


def apply_weights(ob, part_of_vertex):
    """Vertex groups from the rest-pose position of each vertex (at most 4 influences, normalised)."""
    me = ob.data
    groups = {}
    for v in me.vertices:
        w = weights_for(part_of_vertex[v.index], v.co)
        top = sorted(w.items(), key=lambda kv: -kv[1])[:4]
        s = sum(x for _, x in top) or 1.0
        for b, x in top:
            groups.setdefault(b, []).append((v.index, x / s))
    for b, lst in groups.items():
        vg = ob.vertex_groups.new(name=b)
        for idx, x in lst:
            vg.add([idx], x, 'REPLACE')


def build_armature(name):
    ad = bpy.data.armatures.new(name)
    ad.display_type = 'STICK'
    arm = bpy.data.objects.new(name, ad)
    link(arm)
    select_only([arm])
    bpy.ops.object.mode_set(mode='EDIT')
    ebs = {}
    for nm, h, t, par in sk.bone_specs():
        eb = ad.edit_bones.new(nm)
        eb.head, eb.tail = h, t
        if par:
            eb.parent = ebs[par]
        ebs[nm] = eb
    bpy.ops.object.mode_set(mode='OBJECT')
    for pb in arm.pose.bones:
        pb.rotation_mode = 'QUATERNION'
    return arm


def bake_material(name):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    return m


def make_group(prefix, group, parts, res):
    """Low-poly mesh of a group: decimated parts joined, one UV atlas, textures baked from the joined high-poly parts."""
    highs = [copy_object(o) for o in parts]
    for h in highs:
        bake_transform(h)
    high = join_objects(f'{group}_high', highs)
    lows = [low_part(o) for o in parts]
    names = [o.name for o in parts]
    for i, lp in enumerate(lows):                   # tag every vertex with its part: join may reorder, attributes follow
        at = lp.data.attributes.new('part', 'INT', 'POINT')
        at.data.foreach_set('value', [i] * len(lp.data.vertices))
    low = join_objects(f'{prefix}_{group}', lows)
    at = low.data.attributes['part']
    part_of_vertex = [names[d.value] for d in at.data]
    low.data.attributes.remove(at)
    make_bake_uv(low, margin=0.003 if res >= 1024 else 0.006)
    mat = bake_material(f'{prefix}_{group}')
    low.data.materials.append(mat)
    t = time.time()
    imgs = bake_set(prefix, group, low, res, TEX_DIR, high=high, ext=0.006, ray=0.02)
    unity_mask(prefix, group, imgs['rough'], 0.0, TEX_DIR)
    print(f'[export] {group}: {tri_count(low)} tris, {res}px, bake {time.time() - t:.0f}s')
    bpy.data.objects.remove(high)
    return low, part_of_vertex


def make_eyes(prefix, parts):
    """Half eyeballs with a planar UV (the iris at the centre); one texture shared by both eyes, baked from the eye material."""
    lows = []
    for o in parts:
        lp = copy_object(o, o.name + '_low')
        bm = bmesh.new()
        bm.from_mesh(lp.data)
        dead = [v for v in bm.verts if v.co.y > 0.001]
        bmesh.ops.delete(bm, geom=dead, context='VERTS')
        uv = bm.loops.layers.uv.new('bake')
        for f in bm.faces:
            for l in f.loops:
                l[uv].uv = (0.5 + l.vert.co.x / (2 * bd.EYE_R), 0.5 + l.vert.co.z / (2 * bd.EYE_R))
        bm.to_mesh(lp.data)
        bm.free()
        lp.data.uv_layers.active = lp.data.uv_layers['bake']
        lows.append(lp)
    src = lows[0]
    mat0 = parts[0].data.materials[0]
    src.data.materials.clear()
    src.data.materials.append(mat0)
    select_only([src])
    imgs = bake_set(prefix, 'eyes', src, RES['eyes'], TEX_DIR, with_normal=False)
    unity_mask(prefix, 'eyes', imgs['rough'], 0.0, TEX_DIR)
    for lp in lows:
        bake_transform(lp)
        lp.data.materials.clear()
    part_of_vertex = []
    for lp in lows:
        part_of_vertex += ['eye'] * len(lp.data.vertices)
    low = join_objects(f'{prefix}_eyes', lows)
    low.data.materials.append(bake_material(f'{prefix}_eyes'))
    return low, part_of_vertex


def preview_material(prefix, group):
    """What Unity will show: the baked base colour, roughness and normal in a Principled shader."""
    m = bpy.data.materials.new(f'pv_{prefix}_{group}')
    nt = NT(m)
    def img(kind, cs, ext):
        p = os.path.join(TEX_DIR, f'{prefix}_{group}_{kind}.{ext}')
        n = nt.node('ShaderNodeTexImage')
        n.image = bpy.data.images.load(p)
        n.image.colorspace_settings.name = cs
        n.interpolation = 'Smart'
        uvn = nt.uv('bake')
        nt.nt.links.new(uvn, n.inputs['Vector'])
        return n
    base = img('base', 'sRGB', 'jpg')
    rough = img('rough', 'Non-Color', 'png')
    inputs = {'Base Color': base.outputs['Color'], 'Roughness': rough.outputs['Color']}
    if group != 'eyes':
        nrm = img('normal', 'Non-Color', 'jpg')
        nm = nt.node('ShaderNodeNormalMap')
        nm.uv_map = 'bake'
        nt.nt.links.new(nrm.outputs['Color'], nm.inputs['Color'])
        inputs['Normal'] = nm.outputs['Normal']
    nt.bsdf(inputs)
    return m


def rot_world(arm, bone, axis, deg):
    """Rotate a pose bone about a world axis through its head, on top of whatever its parent already does."""
    pb = arm.pose.bones[bone]
    bpy.context.view_layer.update()
    rest = pb.bone.matrix_local.to_3x3()
    D = Matrix.Identity(3)
    if pb.parent:
        D = pb.parent.matrix.to_3x3() @ pb.parent.bone.matrix_local.to_3x3().inverted()
    W = Matrix.Rotation(radians(deg), 3, axis)
    pb.rotation_quaternion = (rest.inverted() @ D.inverted() @ W @ D @ rest).to_quaternion()
    bpy.context.view_layer.update()


def pose_test(arm):
    """A stride with the left arm raised and bent, the head turned and the trunk leaning: checks the skin weights."""
    for pb in arm.pose.bones:
        pb.rotation_quaternion = (1, 0, 0, 0)
    for bone, axis, deg in (('Spine', 'X', 12), ('Chest', 'X', 8), ('Head', 'Z', 35), ('Neck', 'X', 10),
                            ('UpperArm_L', 'X', -75), ('LowerArm_L', 'X', -85), ('Hand_L', 'Z', 20),
                            ('UpperArm_R', 'X', 25), ('LowerArm_R', 'X', -40),
                            ('Thigh_R', 'X', -50), ('Shin_R', 'X', 70), ('Foot_R', 'X', -15),
                            ('Thigh_L', 'X', 20), ('Shin_L', 'X', 8)):
        rot_world(arm, bone, axis, deg)
    for fn in ('Index', 'Middle', 'Ring', 'Pinky'):
        for k in (1, 2, 3):
            if f'{fn}{k}_L' in arm.pose.bones:
                rot_world(arm, f'{fn}{k}_L', 'X', -35)


def stage_export(prefix):
    path = os.path.join(WORK_DIR, f'{prefix}_hp.blend')
    bpy.ops.wm.open_mainfile(filepath=path)
    sc = bpy.context.scene
    bd.compute_hand()
    by_group = {}
    for ob in sc.objects:
        g = ob.get('group')
        if g:
            by_group.setdefault(g, []).append(ob)
    for g in by_group:
        by_group[g].sort(key=lambda o: o.name)
    sc.cycles.samples = 24
    sc.cycles.use_denoising = False
    out = []
    for g in ('skin', 'outfit', 'hair'):
        out.append((g, *make_group(prefix, g, by_group[g], RES[g])))
    out.append(('eyes', *make_eyes(prefix, by_group['eyes'])))
    arm = build_armature(prefix)
    lows = []
    for g, low, pov in out:
        apply_weights(low, pov)
        low.parent = arm
        md = low.modifiers.new('Armature', 'ARMATURE')
        md.object = arm
        lows.append(low)
    # save the low-poly scene, then export it
    for o in list(sc.objects):
        if o not in lows and o is not arm:
            bpy.data.objects.remove(o)
    for low in lows:
        low.data.materials.clear()
        low.data.materials.append(bake_material(low.name))
    os.makedirs(UNITY_DIR, exist_ok=True)
    select_only(lows + [arm], arm)
    bpy.ops.export_scene.fbx(filepath=os.path.join(UNITY_DIR, f'{prefix}.fbx'), use_selection=True,
                             object_types={'ARMATURE', 'MESH'}, apply_unit_scale=True,
                             apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
                             mesh_smooth_type='FACE', use_tspace=True, add_leaf_bones=False,
                             primary_bone_axis='Y', secondary_bone_axis='X', armature_nodetype='NULL',
                             bake_anim=False, path_mode='RELATIVE', embed_textures=False)
    import scrub_fbx            # the exporter writes the absolute path of the .blend into the file: the repository is public
    scrub_fbx.scrub(os.path.join(UNITY_DIR, f'{prefix}.fbx'))
    print('[export] fbx', os.path.join(UNITY_DIR, f'{prefix}.fbx'), 'bones', len(arm.data.bones),
          'tris', sum(tri_count(l) for l in lows))
    save_blend(os.path.join(WORK_DIR, f'{prefix}_low.blend'))
    # preview of what was baked
    for g, low, _ in out:
        low.data.materials.clear()
        low.data.materials.append(preview_material(prefix, g))
    return arm, lows
