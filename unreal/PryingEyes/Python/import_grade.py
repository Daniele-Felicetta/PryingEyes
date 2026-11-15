"""Builds (or rebuilds) the post-process material that carries the game's Split Toning: /Game/Imported/Materials/M_UnityGrade.
import_scene.py does the same on every import; this script is for changing the material alone.
Run: tools/unreal/ue.ps1 python unreal/PryingEyes/Python/import_grade.py"""
import os
import sys

import unreal

_here = os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals() else os.path.join(
    unreal.Paths.project_dir(), "Python")
if _here not in sys.path:
    sys.path.insert(0, _here)
import pe_unity as pu  # noqa: E402

pu.build_grade_material()
unreal.EditorAssetLibrary.save_directory("/Game/Imported", only_if_is_dirty=True, recursive=True)
pu.log("IMPORT_GRADE_DONE")
