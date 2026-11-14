import json
import re
from pathlib import Path
import tempfile
import unittest
from PIL import Image
from asset_graph import AssetGraph
from studio_gallery import bundle, safe_file


class GalleryContracts(unittest.TestCase):
    def build(self, root, report, sources):
        job = root / "jobs/20260101T120000Z-abcdef12"; job.mkdir(parents=True)
        (job / "report.json").write_text(json.dumps(report), encoding="utf-8")
        Image.new("RGB", (32, 24), "red").save(job / "example_lato.png")
        assets = [{"id": "example", "name": "Example", "type": "weapon", "category": "Weapon", "family": "", "tags": [], "stats": []}]
        graph = AssetGraph([{"id": "example", "name": "Example", "type": "weapon", "sources": [{"path": "recipe.py"}]}], root)
        return bundle(assets, graph, [job / "report.json"], root / "gallery", root=root, expected_sources=sources)

    def test_portable_images_reports_source_state_and_no_script_injection(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); (root / "recipe.py").write_text("value=1")
            evil = '</script><img src=x onerror="window.injection=true"> __SCRIPT__ __DATA__ __STYLE__'
            report = {"schemaVersion": 1, "operation": "asset.preview", "subject": "example", "state": "failed", "ok": False,
                      "sources": {"recipe.py": "outdated"}, "findings": [{"code": "ERROR", "observed": evil, "passed": False}], "artifacts": []}
            data = self.build(root, report, {"recipe.py": "current"})
            run = next(iter(data["runs"].values())); self.assertEqual(run["state"], "failed")
            self.assertEqual(run["sourceChanges"], ["recipe.py"])
            picture = run["images"]["example"]["lato"]
            self.assertEqual((picture["width"], picture["height"]), (32, 24))
            self.assertTrue((root / "gallery" / picture["url"]).is_file())
            self.assertTrue((root / "gallery" / run["report"]).is_file())
            html = (root / "gallery/index.html").read_text(encoding="utf-8")
            self.assertNotIn(evil, html); self.assertIn("\\u003c/script\\u003e", html)
            embedded = re.search(r'<script id="gallery-data" type="application/json">(.*?)</script>', html, re.S).group(1)
            self.assertEqual(json.loads(embedded)["runs"][run["key"]]["findings"][0]["observed"], evil)
            self.assertEqual(json.loads((root / "gallery/manifest.json").read_text())["runs"][run["key"]]["failureCount"], 1)
            copied = root / "gallery" / picture["url"]; original = copied.read_bytes(); copied.write_bytes(b"corrupted cached image")
            bundle(data["assets"], AssetGraph([], root), list((root / "jobs").glob("*/report.json")), root / "gallery", root=root)
            self.assertEqual(copied.read_bytes(), original)

    def test_empty_sources_do_not_become_a_current_verification(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            data = self.build(root, {"schemaVersion": 1, "operation": "asset.preview", "subject": "example", "ok": True,
                                    "state": "complete", "sources": {}}, {"recipe.py": "hash"})
            self.assertIsNone(next(iter(data["runs"].values()))["sourceChanges"])
            self.assertIsNone(safe_file(root.parent / "external.txt", root))

    def test_direct_and_queued_report_dates_sort_by_utc_job_identity(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self.build(root, {"schemaVersion": 1, "operation": "asset.preview", "subject": "example", "ok": True,
                              "createdAt": "20260101T120000Z"}, {})
            later = root / "jobs/20260101T130000Z-abcdef34"; later.mkdir()
            (later / "report.json").write_text(json.dumps({"schemaVersion": 1, "operation": "asset.preview", "subject": "example",
                "ok": True, "createdAt": "2026-01-01T13:00:00+00:00"}), encoding="utf-8")
            assets = [{"id": "example"}]
            data = bundle(assets, AssetGraph([], root), list((root / "jobs").glob("*/report.json")), root / "gallery", root=root)
            self.assertEqual(data["runs"][assets[0]["runs"][0]]["id"], later.name)


if __name__ == "__main__": unittest.main()
