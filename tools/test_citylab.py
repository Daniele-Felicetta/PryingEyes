"""Small acceptance check for the standalone city recipe validator."""
import json
import subprocess
import sys
import tempfile
import unittest
import hashlib
from types import SimpleNamespace
from unittest.mock import patch
from pathlib import Path
from xml.etree import ElementTree

from citylab import RECIPE, ROOT, run_native


class CityLabCheck(unittest.TestCase):
    def test_report_rejects_wrong_layout_or_recipe(self):
        with tempfile.TemporaryDirectory() as folder:
            folder = Path(folder)
            exe = folder / "PryingEyes.exe"; exe.write_bytes(b"test binary")
            assembly = folder / "PryingEyes_Data/Managed/Assembly-CSharp.dll"
            assembly.parent.mkdir(parents=True); assembly.write_bytes(b"test assembly")
            expected = {"spawns": [{"x": 0, "z": 24.0}]}
            native = {"ok": True, "layout": expected, "recipeSha256": hashlib.sha256(RECIPE.read_bytes()).hexdigest(), "rooms": []}
            def write_report(*args, **kwargs):
                (folder / "report.json").write_text(json.dumps(native), encoding="utf-8")
                return SimpleNamespace(returncode=0)
            with patch("citylab.subprocess.run", side_effect=write_report):
                self.assertEqual(run_native(exe, folder, 1, RECIPE, expected, []), 0)
                native["layout"] = {"spawns": [{"x": 0, "z": 23.0}]}
                self.assertEqual(run_native(exe, folder, 1, RECIPE, expected, []), 1)
                native["layout"] = expected
                native["rooms"] = [{"block": "Building_-1_-1", "id": "stale_room"}]
                self.assertEqual(run_native(exe, folder, 1, RECIPE, expected, []), 1)
                native["rooms"] = []; native["recipeSha256"] = "old recipe"
                self.assertEqual(run_native(exe, folder, 1, RECIPE, expected, []), 1)

    def test_missing_binary_cannot_reuse_stale_success(self):
        with tempfile.TemporaryDirectory() as folder:
            folder = Path(folder)
            (folder / "report.json").write_text('{"ok": true}', encoding="utf-8")
            self.assertEqual(run_native(folder / "missing.exe", folder, 1, RECIPE), 1)
            self.assertFalse((folder / "report.json").exists())
            result = json.loads((folder / "run.json").read_text(encoding="utf-8"))
            self.assertFalse(result["ok"])
            self.assertIn("error", result)

    def run_recipe(self, recipe):
        with tempfile.TemporaryDirectory() as folder:
            folder = Path(folder)
            source, report, preview = (folder / name for name in ("recipe.json", "report.json", "preview.svg"))
            source.write_text(json.dumps(recipe), encoding="utf-8")
            run = subprocess.run([sys.executable, str(ROOT / "tools/citylab.py"), "--recipe", str(source),
                                  "--report", str(report), "--out", str(preview)], capture_output=True, text=True)
            data = json.loads(report.read_text(encoding="utf-8"))
            if run.returncode == 0:
                ElementTree.fromstring(preview.read_text(encoding="utf-8"))
            return run.returncode, data

    def test_recipe_and_invalid_variants(self):
        recipe = json.loads(RECIPE.read_text(encoding="utf-8"))
        code, result = self.run_recipe(recipe)
        self.assertEqual(code, 0)
        self.assertEqual(result["entrances"], 4)
        self.assertEqual(len(result["recipeSha256"]), 64)
        for changes in ({"halfWidth": 33}, {"halfWidth": 3e38}, {"roadHalfWidth": float("nan")},
                        {"shopWeapons": ["unknown", "unknown", "unknown"]}, {"shopWeapons": "p9_ronda"}):
            with self.subTest(changes=changes):
                code, result = self.run_recipe({**recipe, **changes})
                self.assertEqual(code, 1)
                self.assertFalse(result["ok"])
                self.assertTrue(result["findings"])

    def test_smaller_valid_variant_moves_spawns_inside_bounds(self):
        with tempfile.TemporaryDirectory() as folder:
            folder = Path(folder)
            recipe = json.loads(RECIPE.read_text(encoding="utf-8")); recipe["halfWidth"] = 34
            source = folder / "recipe.json"; source.write_text(json.dumps(recipe), encoding="utf-8")
            result = subprocess.run([sys.executable, str(ROOT / "tools/citylab.py"), "--recipe", str(source),
                "--report", str(folder / "report.json"), "--out", str(folder / "map.svg")], capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            plan = json.loads((folder / "city_layout.json").read_text(encoding="utf-8"))
            self.assertEqual(plan["spawns"][2]["x"], -29)


if __name__ == "__main__":
    unittest.main()
