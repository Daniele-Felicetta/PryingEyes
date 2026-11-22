"""
Track-mode models: the rider and the bikes, procedural, for Blender 4.5 (headless). One Blender at a time: each one wants every core.

Usage (from this folder):
  blender -b -P genera_moto.py -- pilota [views]   the rider on a rough mock of the bike, views (default all five) -> render/pilota_*.png
  blender -b -P genera_moto.py -- export_pilota    the rider -> FBX into Assets/_PryingEyes/Resources/Moto

Axes: x right, y forward, z up, metres (geom.py). docs/engineering/track-models.md explains the pipeline and how the game reads it.
"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from geom import *
import rider as rd

# ----------------------------------------------------------------------------- materials of the previews (the game has its own)
def rider_materials():
    """One material per ROLE (the game reads the name of the material: it picks the shader, the colours and the part of the livery)."""
    red = (0.62, 0.05, 0.06)
    blk = (0.035, 0.035, 0.04)
    return {
        'suit': preview_material('suit', red, 0.36, 0.0, 0.5),
        'hump': preview_material('hump', blk, 0.42, 0.0, 0.3),
        'arm_upper': preview_material('arm_upper', red, 0.36, 0.0, 0.5),
        'arm_fore': preview_material('arm_fore', blk, 0.42, 0.0, 0.3),
        'leg_thigh': preview_material('leg_thigh', blk, 0.42, 0.0, 0.3),
        'leg_shin': preview_material('leg_shin', blk, 0.42, 0.0, 0.3),
        'slider': preview_material('slider', (0.5, 0.5, 0.52), 0.5),
        'glove': preview_material('glove', (0.03, 0.03, 0.035), 0.5),
        'boot': preview_material('boot', blk, 0.45, 0.0, 0.3),
        'helmet': preview_material('helmet', (0.88, 0.88, 0.86), 0.25, 0.0, 0.8),
        'seal': preview_material('seal', (0.015, 0.015, 0.017), 0.7),
        'visor': preview_material('visor', (0.02, 0.025, 0.035), 0.06, 0.9),
    }


ROLES = {'torso': ['suit'], 'hump': ['hump'], 'arm': ['arm_upper', 'arm_fore'], 'leg': ['leg_thigh', 'leg_shin'], 'slider': ['slider'],
         'glove': ['glove'], 'boot': ['boot'], 'shell': ['helmet'], 'spoiler': ['helmet'], 'seal': ['seal'], 'visor': ['visor'], 'black': ['seal']}
BUDGET = {'torso': 9000, 'glove': 1600, 'boot': 2400}


def dress(parts, mats):
    """Materials (by role), smooth shading and the triangle budget of every part (previews and export alike)."""
    for name, obs in parts.items():
        for ob in obs:
            key = ob.name.split('_')[-1] if ob.name.startswith('helmet_') else ob.name.split('_')[0]
            ob.data.materials.clear()
            for r in ROLES[key]:
                ob.data.materials.append(mats[r])
            if key in BUDGET:
                decimate(ob, BUDGET[key])
            for p in ob.data.polygons:
                p.use_smooth = True


# ----------------------------------------------------------------------------- a rough bike, only to see where the rider sits
def mock_bike():
    m = preview_material('mock', (0.22, 0.22, 0.24), 0.5)
    t = preview_material('mock_tyre', (0.02, 0.02, 0.022), 0.9)
    objs = []

    def add(bm, name, mat):
        ob = bm_obj(bm, name, None)
        ob.data.materials.append(mat)
        for p in ob.data.polygons:
            p.use_smooth = True
        objs.append(ob)

    for nm, ax, w in (('front', AXLE_F, 0.125), ('rear', AXLE_R, 0.19)):
        bm = bmesh.new()
        bmesh.ops.create_cone(bm, cap_ends=True, segments=40, radius1=R_WHEEL, radius2=R_WHEEL, depth=w,
                              matrix=Matrix.Translation(ax) @ Matrix.Rotation(radians(90), 4, 'Y'))
        add(bm, 'mock_wheel_' + nm, t)
    for nm, c, r in (('tank', V(0, 0.10, 0.90), (0.17, 0.34, 0.12)), ('fairing', V(0, 0.46, 0.74), (0.20, 0.42, 0.26)),
                     ('tail', V(0, -0.70, 0.91), (0.10, 0.30, 0.08)), ('engine', V(0, -0.02, 0.50), (0.14, 0.24, 0.20)),
                     ('seat', V(0, -0.36, 0.865), (0.12, 0.30, 0.025))):
        bm = bmesh.new()
        bm_ellipsoid(bm, c, r, None, 28)
        add(bm, 'mock_' + nm, m)
    bm = bmesh.new()
    for sd in (-1, 1):
        bm_capsule(bm, V(sd * 0.30, 0.40, 0.905), V(sd * 0.16, 0.40, 0.905), 0.0165, 0.0165, 10)
        bm_capsule(bm, V(sd * 0.10, HEAD.y, HEAD.z), AXLE_F, 0.022, 0.022, 10)
        bm_capsule(bm, V(sd * 0.24, PEG.y, PEG.z), V(sd * 0.24, PEG.y + 0.05, PEG.z), 0.012, 0.012, 8)
        bm_capsule(bm, V(sd * 0.095, SWING_PIVOT.y, SWING_PIVOT.z), AXLE_R, 0.03, 0.03, 10)
    add(bm, 'mock_bits', m)
    return objs


# ----------------------------------------------------------------------------- scene and photos
def studio_world():
    sc = bpy.context.scene
    w = bpy.data.worlds.new('mondo')
    sc.world = w
    w.use_nodes = True
    nt = w.node_tree
    bg = nt.nodes['Background']
    bg.inputs['Color'].default_value = (0.42, 0.46, 0.52, 1)
    bg.inputs['Strength'].default_value = 0.9
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=7.0)
    fl = bm_obj(bm, 'floor', None)
    fl.data.materials.append(flat_material('floor', (0.16, 0.16, 0.17), 0.7))
    add_area('key', (-2.6, -2.4, 3.4), (0, 0, 0.9), 600, 2.2, (1.0, 0.95, 0.88))
    add_area('fill', (3.0, -1.6, 1.8), (0, 0, 0.9), 220, 2.6, (0.85, 0.92, 1.0))
    add_area('rim', (1.2, 3.0, 2.8), (0, 0, 1.1), 420, 1.6, (1.0, 0.97, 0.92))
    add_area('top', (0, 0.2, 4.2), (0, 0, 0.8), 260, 3.0, (1, 1, 1))


def shoot(views, prefix, res=(1100, 800), samples=28):
    sc = bpy.context.scene
    setup_cycles(samples, res)
    os.makedirs(RENDER_DIR, exist_ok=True)
    for nm, (loc, tgt, lens) in views.items():
        add_camera(loc, tgt, lens)
        sc.render.filepath = os.path.join(RENDER_DIR, '%s_%s.png' % (prefix, nm))
        bpy.ops.render.render(write_still=True)
        print('[render]', sc.render.filepath)


VIEWS = {
    'lato': ((3.4, -0.05, 1.05), (0, -0.05, 0.9), 62),
    'tre_quarti': ((2.3, 2.3, 1.55), (0, 0.0, 0.85), 55),
    'dietro_alto': ((0.9, -3.2, 2.0), (0, 0.0, 1.0), 52),
    'davanti': ((1.2, 3.4, 1.15), (0, 0.0, 0.95), 55),
    'casco': ((1.05, 0.95, 1.50), (0.0, 0.22, 1.28), 80),
}


def stage_pilota():
    reset()
    parts, J = rd.build_rider()
    for k in ('hip_c', 'shoulder_c', 'neck', 'head', 'hip_R', 'knee_R', 'ankle_R', 'shoulder_R', 'elbow_R', 'wrist_R', 'grip_R'):
        print('[joint] %-11s (%.3f, %.3f, %.3f)' % (k, J[k].x, J[k].y, J[k].z))
    dress(parts, rider_materials())
    print('[info] triangles:', {k: sum(tri_count(o) for o in v) for k, v in parts.items()})
    mock_bike()
    studio_world()
    which = ARGS[1:] or list(VIEWS)
    shoot({k: VIEWS[k] for k in which}, 'pilota')


def set_origin(ob, origin):
    """The origin of the object at 'origin', the mesh where it was (the shader of the game draws the livery from the position in the part's own frame)."""
    ob.data.transform(Matrix.Translation(-Vector(origin)))
    ob.location = Vector(origin)


def joint_empties(J):
    out = []
    for k, v in J.items():
        if isinstance(v, Vector) and k not in ('u', 'n'):
            e = bpy.data.objects.new('J_' + k, None)
            e.empty_display_type = 'PLAIN_AXES'
            e.empty_display_size = 0.02
            e.location = v
            link(e)
            out.append(e)
    return out


def stage_export_pilota():
    reset()
    parts, J = rd.build_rider()
    dress(parts, rider_materials())
    for s in 'RL':
        leg, slider = parts['leg_' + s]
        parts['leg_' + s] = [join_objects('leg_' + s, [leg, slider])]
    for nm in ('torso', 'hump'):
        set_origin(parts[nm][0], J['hip_c'])
    objs = [o for v in parts.values() for o in v]
    for o in objs:
        if not o.name.startswith('helmet_') and o.name not in ('torso', 'hump'):
            bake_transform(o)
    print('[info] triangles:', {k: sum(tri_count(o) for o in v) for k, v in parts.items()})
    save_blend(os.path.join(WORK_DIR, 'pilota.blend'))
    export_fbx(os.path.join(OUT_DIR, 'pilota.fbx'), objs + joint_empties(J))


MOTO_VIEWS = {
    'lato': ((3.7, 0.0, 0.95), (0, 0.0, 0.62), 40),
    'tre_quarti': ((2.9, 2.4, 1.55), (0, 0.05, 0.62), 42),
    'davanti': ((1.4, 3.4, 1.25), (0, 0.2, 0.68), 42),
    'dietro_alto': ((1.5, -3.3, 1.9), (0, -0.1, 0.70), 42),
    'dietro_basso': ((-1.6, -2.4, 0.55), (0, -0.2, 0.60), 40),
    'motore': ((2.0, 0.55, 0.40), (0, 0.15, 0.40), 42),
    'testa': ((0.95, 1.9, 1.35), (0, 0.45, 0.88), 40),
}


def stage_moto():
    """A bike with the rider on it (the rider as the game poses it, at rest), photographs -> render/moto<style>_*.png."""
    import bike
    style = int(ARGS[1]) if len(ARGS) > 1 and ARGS[1].isdigit() else 0
    which = [a for a in ARGS[2:]] or list(MOTO_VIEWS)
    reset()
    bike.build_bike(style, preview=True)
    parts, J = rd.build_rider(naked=(style == 2))
    dress(parts, rider_materials())
    print('[info] bike triangles:', sum(tri_count(o) for o in bpy.data.objects if o.type == 'MESH' and not o.name.startswith(('helmet', 'torso', 'hump', 'arm', 'leg', 'glove', 'boot'))))
    studio_world()
    shoot({k: MOTO_VIEWS[k] for k in which}, 'moto%d' % style)


def stage_export_moto():
    """The bike of this style (no rider, no plates) -> Resources/Moto/moto<style>.fbx."""
    import bike
    style = int(ARGS[1]) if len(ARGS) > 1 and ARGS[1].isdigit() else 0
    reset()
    g, root = bike.build_bike(style)
    objs = [ob for nm, ob in g.objs.items() if ob.type == 'MESH']
    for ob in objs:                                  # the parts go out flat, with no parent: the importer turns every node the same way and the game puts each under its own node
        mw = ob.matrix_world.copy()
        ob.parent = None
        ob.matrix_world = mw
    print('[info] triangles:', {ob.name: tri_count(ob) for ob in objs}, 'total', sum(tri_count(ob) for ob in objs))
    save_blend(os.path.join(WORK_DIR, 'moto%d.blend' % style))
    export_fbx(os.path.join(OUT_DIR, 'moto%d.fbx' % style), objs, smooth='EDGE')


if __name__ == '__main__':
    stage = ARGS[0] if ARGS else 'pilota'
    {'pilota': stage_pilota, 'export_pilota': stage_export_pilota, 'moto': stage_moto, 'export_moto': stage_export_moto}[stage]()
