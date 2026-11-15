"""Imports the monk (skeletal mesh, animation, ten materials with their textures) from the Unity project.
Run: tools/unreal/ue.ps1 python unreal/PryingEyes/Python/import_monk.py"""
import os
import sys

import unreal

_here = os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals() else os.path.join(
    unreal.Paths.project_dir(), "Python")
if _here not in sys.path:
    sys.path.insert(0, _here)
import pe_unity as pu  # noqa: E402

pu.use_legacy_fbx()
# everything under /Game/Imported is derived from the Unity folder: start from nothing so that no setting is inherited
if unreal.EditorAssetLibrary.does_directory_exist("/Game/Imported/Models/Monaco"):
    unreal.EditorAssetLibrary.delete_directory("/Game/Imported/Models/Monaco")
pu.build_master()

mdir = os.path.join(pu.UNITY_ROOT, "Models", "Monaco")
fbx = os.path.join(mdir, "monaco.fbx")
anim_fbx = os.path.join(mdir, "mischia.fbx")

mesh = None
skeleton = None
anims = {}
for p in pu.import_fbx(fbx, skeletal=True, animations=True):
    asset = unreal.load_asset(p)
    pu.log("imported", p, type(asset).__name__)
    if isinstance(asset, unreal.SkeletalMesh):
        mesh = asset
    elif isinstance(asset, unreal.Skeleton):
        skeleton = asset
    elif isinstance(asset, unreal.AnimSequence):
        anims[p.split(".")[0]] = asset.get_editor_property("sequence_length")
if mesh is None:
    raise RuntimeError("the monk did not import")
if skeleton is None:
    skeleton = mesh.get_editor_property("skeleton")

b = mesh.get_bounds()
pu.log("monk bounds origin", b.origin, "extent", b.box_extent, "(centimetres: a monk is about 90 half height)")

# the five takes of monaco.fbx (fermo, cammina, attacco, colpito, morte) arrive with the mesh as monaco_Anim_Monaco_<take>;
# mischia.fbx holds the sixth clip on its own
anim_path = ""
for p in pu.import_fbx(anim_fbx, skeletal=True, animation_skeleton=skeleton):
    asset = unreal.load_asset(p)
    if isinstance(asset, unreal.AnimSequence):
        anim_path = p
        anims[p.split(".")[0]] = asset.get_editor_property("sequence_length")
        pu.log("animation", p, "length", asset.get_editor_property("sequence_length"))
# every animation asset that now sits next to the mesh, takes of monaco.fbx included
for ap in unreal.EditorAssetLibrary.list_assets(os.path.dirname(mesh.get_path_name().split(".")[0]), recursive=False):
    a = unreal.load_asset(ap)
    if isinstance(a, unreal.AnimSequence):
        anims[ap.split(".")[0]] = a.get_editor_property("sequence_length")
pu.log("animations:", anims)

slots = {}
mat_map = pu.fbx_material_map(fbx)
pu.log("FBX materials bound by Unity:", mat_map)
for sm in mesh.get_editor_property("materials"):
    slot = str(sm.get_editor_property("material_slot_name"))
    mat_path = mat_map.get(slot)
    if not mat_path:
        pu.warn("no Unity material for slot", slot)
        continue
    slots[slot] = pu.unity_material(mat_path)

# also write the materials into the mesh asset so that it can be placed anywhere
try:
    new = []
    for sm in mesh.get_editor_property("materials"):
        slot = str(sm.get_editor_property("material_slot_name"))
        if slot in slots and slots[slot]:
            sm.set_editor_property("material_interface", unreal.load_asset(slots[slot]))
        new.append(sm)
    mesh.set_editor_property("materials", new)
    pu.log("materials assigned inside the skeletal mesh asset")
except Exception as e:  # noqa: BLE001
    pu.warn("could not assign materials in the asset (the scene will do it from the sidecar):", e)

unreal.EditorAssetLibrary.save_loaded_asset(mesh)
# a floor for the test scene: one of the hotel's kit materials
floor = pu.unity_material(os.path.join(pu.UNITY_ROOT, "Materials", "Kit", "kit_legno_scuro.mat"))
pu.write_json("monk.json", {"mesh": mesh.get_path_name().split(".")[0], "anim": anim_path.split(".")[0],
                            "anims": anims, "slots": slots, "floor": floor})
unreal.EditorAssetLibrary.save_directory("/Game/Imported", only_if_is_dirty=True, recursive=True)
pu.log("IMPORT_MONK_DONE")
