"""Nuovo corpo a corpo, esportato come solo scheletro compatibile con i costumi esistenti.
Blender -b -P modelli/monaco/genera_attacco.py
Non rigenera mesh, texture o animazioni di camminata e morte.
"""
import os
import sys
import bpy
sys.path.insert(0, os.path.dirname(__file__))
import genera_monaco as M

bpy.ops.wm.open_mainfile(filepath=M.RIG_BLEND)
arm = bpy.data.objects['Monaco']
arm.animation_data_clear()
for action in list(bpy.data.actions):
    bpy.data.actions.remove(action)
# Avvolgimento laterale del busto e dell'arma, seguito da un affondo a quota torace.
R = {
 'bastone': [(0,(0,0,0)), (10,(22,0,-22)), (14,(28,0,-28)), (18,(73,0,18)), (21,(76,0,20)), (28,(38,0,8)), (38,(0,0,0))],
 'petto': [(0,(0,0,0)), (10,(-8,0,-22)), (14,(-10,0,-28)), (18,(15,0,18)), (22,(12,0,14)), (30,(3,0,5)), (38,(0,0,0))],
 'spina': [(0,(0,0,0)), (14,(-5,0,-9)), (18,(9,0,8)), (24,(7,0,5)), (38,(0,0,0))],
 'bacino': [(0,(0,0,0)), (14,(-3,0,-6)), (18,(5,0,6)), (27,(3,0,3)), (38,(0,0,0))],
 'testa': [(0,(0,0,0)), (14,(-8,0,12)), (18,(7,0,-8)), (25,(4,0,-4)), (38,(0,0,0))],
 'braccio_d': [(0,(0,0,0)), (10,(-28,8,-12)), (14,(-38,12,-18)), (18,(-60,-12,24)), (23,(-52,-8,18)), (38,(0,0,0))],
 'avambraccio_d': [(0,(0,0,0)), (14,(-55,0,0)), (18,(-12,0,0)), (23,(-18,0,0)), (38,(0,0,0))],
 'coscia_d': [(0,(0,0,0)), (14,(4,0,0)), (18,(-10,0,0)), (26,(-6,0,0)), (38,(0,0,0))],
 'coscia_s': [(0,(0,0,0)), (14,(-4,0,0)), (18,(3,0,0)), (38,(0,0,0))],
 'stinco_d': [(0,(0,0,0)), (14,(4,0,0)), (18,(15,0,0)), (26,(9,0,0)), (38,(0,0,0))],
 'stinco_s': [(0,(0,0,0)), (18,(7,0,0)), (38,(0,0,0))],
}
L = {
 'bastone': [(0,(0,0,0)), (10,(0.06,0.10,0.16)), (14,(0.08,0.13,0.20)), (18,(-0.06,-0.20,0.06)), (21,(-0.07,-0.21,0.05)), (28,(-0.02,-0.12,0.04)), (38,(0,0,0))],
 'bacino': [(0,(0,0,0)), (14,(0,0.05,-0.015)), (18,(0,-0.10,-0.04)), (24,(0,-0.07,-0.03)), (38,(0,0,0))],
}
act = M.make_action(arm, 'mischia', n=38, loop=False, rot=R, loc=L)
for curve in M.action_fcurves(act):
    for key in curve.keyframe_points:
        key.handle_left_type = 'AUTO_CLAMPED'
        key.handle_right_type = 'AUTO_CLAMPED'
arm.animation_data.action = act
bpy.context.scene.render.fps = 30
bpy.context.scene.frame_start = 0
bpy.context.scene.frame_end = 38
# La mano segue la presa con IK, ma non puo' raggiungere un bastone oltre la lunghezza del braccio.
# Controlliamo anche tra le chiavi: le curve possono superare la portata durante affondo e recupero.
worst_grip = (0.0, 0.0)
for sample in range(153):
    frame = sample / 4
    bpy.context.scene.frame_set(int(frame), subframe=frame - int(frame))
    bones = arm.pose.bones
    error = (bones['mano_s'].head - bones['presa_s'].head).length
    if error > worst_grip[0]:
        worst_grip = (error, frame)
print(f'[mischia] errore massimo della presa {worst_grip[0]:.6f} m al frame {worst_grip[1]:.2f}')
if worst_grip[0] > 0.003:
    raise RuntimeError('La mano perde il bastone: correggere la traiettoria prima di esportare')
bpy.context.scene.frame_set(0)
if 'check' in M.ARGS:
    sys.exit(0)
container = bpy.data.objects.new('AnimazioneMischia', None)
bpy.context.collection.objects.link(container)
arm.parent = container
M.select_only([container, arm], arm)
out = os.path.abspath(os.path.join(os.path.dirname(__file__), '../../unity/PryingEyes/Assets/_PryingEyes/Models/Monaco/mischia.fbx'))
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, object_types={'ARMATURE', 'EMPTY'},
 apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
 add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X', armature_nodetype='NULL',
 bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
 bake_anim_use_all_bones=True, bake_anim_force_startend_keying=True, bake_anim_step=1.0,
 bake_anim_simplify_factor=0.0, path_mode='RELATIVE', embed_textures=False)
print('[mischia] esportata', out)
