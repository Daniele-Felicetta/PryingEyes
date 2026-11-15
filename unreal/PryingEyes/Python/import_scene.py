"""Imports a room exported by Unity's UnrealExport.cs: meshes, materials and textures become assets under /Game/Scene/<tag>,
and Saved/PE/scene_<tag>.json (already in Unreal's axes and centimetres) tells the game where everything stands.
Run: tools/unreal/ue.ps1 python unreal/PryingEyes/Python/import_scene.py export=<folder written by UnrealExport>
Set nanite=1 to build the meshes with Nanite."""
import json
import math
import os
import sys

import unreal

_here = os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals() else os.path.join(
    unreal.Paths.project_dir(), "Python")
if _here not in sys.path:
    sys.path.insert(0, _here)
import pe_unity as pu  # noqa: E402

export_dir = os.path.abspath(os.environ["PE_EXPORT"])
nanite = os.environ.get("PE_NANITE", "0") == "1"
with open(os.path.join(export_dir, "scene.json"), "r", encoding="utf-8") as fh:
    scene = json.load(fh)
tag = pu.sanitize(scene["tag"])
dest = "/Game/Scene/" + tag
pu.log("scene", tag, len(scene["nodes"]), "nodes,", len(scene["meshes"]), "meshes,", len(scene["mats"]), "materials,",
       len(scene["lights"]), "lights")

if unreal.EditorAssetLibrary.does_directory_exist(dest):
    unreal.EditorAssetLibrary.delete_directory(dest)    # derived from the export: start from nothing
pu.build_master()
pu.build_grade_material()
env_file = os.path.join(export_dir, "env.exr")
if os.path.exists(env_file):
    pu.import_environment(env_file)


def P(v):
    """Unity (x right, y up, z forward) -> Unreal (X forward, Y right, Z up): an even permutation, no mirroring."""
    return [v[2], v[0], v[1]]


def cm(v):
    return [x * 100.0 for x in v]


# ---- meshes: built in C++ (PEImportLibrary) from the raw buffers, saved as static meshes
mesh_assets = []
for i, m in enumerate(scene["meshes"]):
    path = "%s/SM_%d" % (dest, i)
    sm = unreal.PEImportLibrary.build_static_mesh_from_file(os.path.join(export_dir, m["file"]), path, nanite)
    if sm is None:
        pu.warn("mesh", i, m["name"], "not built")
        mesh_assets.append(None)
        continue
    unreal.EditorAssetLibrary.save_loaded_asset(sm)
    mesh_assets.append(path)
pu.log("meshes built:", sum(1 for a in mesh_assets if a), "of", len(mesh_assets))

# ---- materials
mat_assets = []
skipped_shaders = {}
for i, m in enumerate(scene["mats"]):
    data = pu.mat_from_export(m, export_dir)
    ap = pu.translate_material(data, dest + "/Materials", "MI_%d_%s" % (i, pu.sanitize(m["name"])))
    if ap is None:
        skipped_shaders[m["shader"]] = skipped_shaders.get(m["shader"], 0) + 1
    mat_assets.append(ap)
pu.log("materials translated:", sum(1 for a in mat_assets if a), "of", len(mat_assets), "; shaders without equivalent:", skipped_shaders)

# ---- nodes
nodes = []
dropped = 0
for n in scene["nodes"]:
    mesh = mesh_assets[n["mesh"]] if n["mesh"] < len(mesh_assets) else None
    if mesh is None:
        dropped += 1
        continue
    mats = [(mat_assets[k] if 0 <= k < len(mat_assets) and mat_assets[k] else "") for k in n["mats"]]
    if mats and not any(mats):
        dropped += 1          # every slot uses a shader this prototype does not translate (haze cards, dust)
        continue
    m = n["m"]            # column-major: right, up, forward, translation (Unity's world matrix)
    nodes.append({"mesh": mesh, "mats": mats, "shadow": bool(n.get("shadow", True)), "group": n.get("group", ""),
                  "x": P(m[8:11]), "y": P(m[0:3]), "z": P(m[4:7]), "o": cm(P(m[12:15]))})
pu.log("nodes kept:", len(nodes), "dropped:", dropped)

# ---- lights
lights = []
for l in scene["lights"]:
    lights.append({"name": l.get("name", ""), "type": l["type"], "pos": cm(P(l["pos"])), "dir": P(l["fwd"]),
                   "color": [l["r"], l["g"], l["b"]],
                   "intensity": l["intensity"], "range": l["range"], "spot": l["spot"], "inner": l["inner"],
                   "shadows": bool(l["shadows"])})

out = {
    "tag": tag,
    "camera": {"pos": cm(P(scene["camPos"])), "fwd": P(scene["camFwd"]), "up": P(scene["camUp"]), "fov": scene["fov"]},
    "look": scene["look"],
    "nodes": nodes,
    "lights": lights,
}
if scene.get("monk"):
    mk = scene["monk"]
    out["monk"] = {"pos": cm(P(mk["pos"])), "yaw": mk["yaw"], "anim": mk["anim"], "time": mk["time"]}
pu.write_json("scene_%s.json" % tag, out)
unreal.EditorAssetLibrary.save_directory("/Game/Scene", only_if_is_dirty=True, recursive=True)
unreal.EditorAssetLibrary.save_directory("/Game/Imported", only_if_is_dirty=True, recursive=True)
pu.log("IMPORT_SCENE_DONE", tag)
