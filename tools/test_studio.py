import copy
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

import studio
from content_catalog import ROOT, read_catalog, validate_catalog


class StudioCheck(unittest.TestCase):
    def test_existing_ids_cannot_be_reordered(self):
        data = read_catalog()
        data["weapons"][0], data["weapons"][1] = data["weapons"][1], data["weapons"][0]
        data["weapons"][0]["index"], data["weapons"][1]["index"] = 0, 1
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "catalog.json"; path.write_text(json.dumps(data), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "WEAPON_STABLE_INDEX"): validate_catalog(path)

    def test_catalog_contract_and_invalid_variants(self):
        catalog = validate_catalog()
        prefix = json.loads((ROOT / "tools/fixtures/weapon-prefix-v1.json").read_text())
        self.assertEqual([e["definition"]["id"] for e in catalog["weapons"][:29]], [e["id"] for e in prefix["definitions"]])
        changes = [lambda c: c["weapons"][0]["definition"].pop("damage"),
                   lambda c: c["weapons"][0]["definition"].update(damage="25"),
                   lambda c: c["weapons"][0]["definition"].update(damage=float("inf")),
                   lambda c: c["weapons"][0]["definition"].update(modes=["Unknown"]),
                   lambda c: c["weapons"][0]["definition"].update(reserve=99999),
                   lambda c: c["weapons"][1]["definition"].update(id=c["weapons"][0]["definition"]["id"]),
                   lambda c: c["weapons"][0].update(index=3),
                   lambda c: c["weapons"][-1]["source"]["parameters"].update(barrelEnd=99)]
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "catalog.json"
            for change in changes:
                data = copy.deepcopy(catalog); change(data); path.write_text(json.dumps(data), encoding="utf-8")
                with self.assertRaises(ValueError): validate_catalog(path)

    def test_project_lock_excludes_another_process_and_releases(self):
        with tempfile.TemporaryDirectory() as folder, patch.object(studio, "OUTPUT", Path(folder)):
            script = "import studio,sys; from pathlib import Path\nstudio.OUTPUT=Path(sys.argv[1])\nwith studio.project_lock(): pass"
            command = [sys.executable, "-c", script, str(Path(folder) / "other-output")]
            with studio.project_lock():
                rejected = subprocess.run(command, cwd=ROOT / "tools", capture_output=True, text=True)
                self.assertNotEqual(rejected.returncode, 0)
                self.assertIn("PROJECT_BUSY", rejected.stderr)
            self.assertEqual(subprocess.run(command, cwd=ROOT / "tools", capture_output=True).returncode, 0)

    def test_step_timeout_is_reported(self):
        with tempfile.TemporaryDirectory() as folder, patch.object(studio, "OUTPUT", Path(folder)):
            job = studio.Job("test", "timeout")
            with self.assertRaisesRegex(RuntimeError, "STEP_TIMEOUT"):
                job.run("slow", [sys.executable, "-c", "import time; time.sleep(10)"], timeout=.1)
            result = json.loads((job.folder / "report.json").read_text())
            self.assertEqual(result["steps"][0]["error"], "STEP_TIMEOUT")

    def test_new_source_file_during_resource_wait_is_rejected(self):
        with tempfile.TemporaryDirectory() as folder, patch.object(studio, "OUTPUT", Path(folder)), patch.object(studio, "PROJECT", Path(folder) / "project"):
            job = studio.Job("test", "sources"); job.report["sources"] = {"existing.cs": "old"}
            job.source_guard = lambda: {"existing.cs": "old", "added.cs": "new"}
            with self.assertRaisesRegex(RuntimeError, "SOURCE_CHANGED_DURING_JOB"):
                with studio.project_lock(job): self.fail("A changed source set acquired execution permission")
            self.assertEqual(job.report["sourceChanges"], ["added.cs"])
            self.assertEqual(job.report["steps"], [])


if __name__ == "__main__": unittest.main()
