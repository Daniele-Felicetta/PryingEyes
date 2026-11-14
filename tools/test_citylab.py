"""Small acceptance check for the standalone city recipe validator."""
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from xml.etree import ElementTree

from citylab import RECIPE, ROOT, run_native


class CityLabCheck(unittest.TestCase):
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
        for changes in ({"halfWidth": 34}, {"roadHalfWidth": float("nan")},
                        {"shopWeapons": ["unknown", "unknown", "unknown"]}, {"shopWeapons": "p9_ronda"}):
            with self.subTest(changes=changes):
                code, result = self.run_recipe({**recipe, **changes})
                self.assertEqual(code, 1)
                self.assertFalse(result["ok"])
                self.assertTrue(result["findings"])


if __name__ == "__main__":
    unittest.main()
