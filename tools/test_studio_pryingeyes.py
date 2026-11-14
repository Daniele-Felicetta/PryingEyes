"""The Prying Eyes profile: the commands it issues to Blender, Unity and the labs, checked without running any of them."""
import hashlib
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

import studio_engine as engine
import studio_pryingeyes as pe
from content_catalog import ROOT, read_catalog
from room_catalog import validate_rooms

TOOLS = {"blender": Path("blender.exe"), "unity": Path("Unity.exe")}
# What the real tools leave behind and the profile then verifies.
LEFT_BEHIND = {"gunplay": ("gunplay-player.log", "gunplaytest RISULTATO SUPERATA"), "room-materials": ("room-materials-unity.log", "MATERIAL_IMPORT_PASS"),
               "weapon-state": ("weapon-state.json", '{"ok": true, "findings": []}'), "survival-state": ("survival-state.json", '{"ok": true, "findings": []}')}


class RecordingJob:
    """Stands in for a Job: records every step instead of running it, and leaves the files the real tool would."""
    def __init__(self, folder): self.folder = Path(folder); self.steps = []; self.report = {"artifacts": [], "findings": []}
    def save(self): pass
    def run(self, label, command, cwd=None, timeout=900):
        self.steps.append((label, [str(part) for part in command]))
        if label in LEFT_BEHIND: (self.folder / LEFT_BEHIND[label][0]).write_text(LEFT_BEHIND[label][1], encoding="utf-8")
        return self.folder / (label + ".log")


class PryingEyesProfile(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory(); self.addCleanup(self.folder.cleanup)
        self.catalogs = {"weapon": read_catalog(), "room": validate_rooms()}
        self.job = RecordingJob(self.folder.name); self.build = Path(self.folder.name) / "build"

    def context(self, identity):
        kind, entry = engine.resolve(pe.PRYING_EYES, self.catalogs, identity)
        return kind, entry, engine.Context(self.job, pe.PRYING_EYES, TOOLS, self.catalogs[kind.name], self.build)

    def test_weapon_build_bakes_then_previews_then_builds_the_player(self):
        kind, entry, ctx = self.context("p45_argine")
        built = kind.build(ctx, entry)
        labels = [label for label, _ in self.job.steps]
        names = [label[5:] for label in labels if label.startswith("bake-")]
        self.assertEqual(names[0], "p45_argine")                                  # the requested weapon first, family shop weapons after
        self.assertEqual(labels, [step for name in names for step in ("bake-" + name, "pack-" + name)] + ["preview", "unity"])
        self.assertEqual(dict(self.job.steps)["unity"][1:], [
            "-batchmode", "-projectPath", str(pe.UNITY), "-executeMethod", "PryingEyes.EditorTools.ProductionCatalog.BuildAsset",
            "-asset", "p45_argine", "-buildOut", str(self.build), "-quit", "-logFile", str(Path(self.folder.name) / "unity.log")])
        self.assertEqual(built, self.build / "PryingEyes.exe")

    def test_room_build_needs_a_city_block_then_verifies_imported_materials(self):
        kind, entry, ctx = self.context("room_workshop_station")
        built = kind.build(ctx, entry)
        self.assertEqual([label for label, _ in self.job.steps], ["unity", "room-materials"])
        self.assertEqual(dict(self.job.steps)["unity"][4:10], ["-executeMethod", "PryingEyes.EditorTools.CityRoomBuilder.BuildAsset",
                                                               "-room", "room_workshop_station", "-roomout", self.folder.name])
        self.assertEqual(built, self.build / "PryingEyes.exe")
        self.assertEqual([Path(a).name for a in self.job.report["artifacts"]][-2:], ["room_workshop_station.png", "plan.json"])
        with self.assertRaisesRegex(RuntimeError, "ROOM_NOT_IN_CITY"): kind.build(ctx, {"id": "room_unbound"})

    def test_room_preview_checks_the_editor_first(self):
        kind, entry, ctx = self.context("room_workshop_station")
        with patch.object(pe, "check_editor_closed", side_effect=RuntimeError("PROJECT_IN_USE: test")):
            with self.assertRaisesRegex(RuntimeError, "PROJECT_IN_USE"): kind.preview(ctx, entry)
        self.assertEqual(self.job.steps, [])                                      # nothing was launched over an open editor
        with patch.object(pe, "check_editor_closed"): kind.preview(ctx, entry)
        self.assertEqual([label for label, _ in self.job.steps], ["room-preview", "room-materials"])

    def test_before_build_checks_the_editor_then_runs_both_rule_labs(self):
        kind, entry, ctx = self.context("p45_argine")
        with patch.object(pe, "check_editor_closed") as check:
            pe.PRYING_EYES.before_build(ctx)
        check.assert_called_once()
        self.assertEqual([label for label, _ in self.job.steps], ["weapon-state", "survival-state"])

    def test_after_build_runs_gunplay_then_city_acceptance_with_assets_required(self):
        kind, entry, ctx = self.context("p45_argine")
        built = self.build / "PryingEyes.exe"
        pe.PRYING_EYES.after_build(ctx, built)
        self.assertEqual([label for label, _ in self.job.steps], ["gunplay", "city"])
        city = dict(self.job.steps)["city"]
        self.assertIn("--require-assets", city); self.assertEqual(city[city.index("--runtime") + 1], str(built))

    def test_registry_matches_the_command_line_the_docs_describe(self):
        self.assertEqual(list(pe.PRYING_EYES.kinds), ["weapon", "room"])
        self.assertEqual({name: suite.runtime for name, suite in pe.PRYING_EYES.suites.items()},
                         {"gunplay": True, "weapon-state": False, "survival": False})
        self.assertEqual(pe.PRYING_EYES.publish_dir, pe.EXE.parent)

    def test_lock_file_is_the_one_the_tool_used_before_it_was_split(self):
        # Two tool versions must exclude each other while both exist: same lock file as the original studio.py computed.
        identity = str((ROOT / "unity/PryingEyes").resolve()).replace("\\", "/")
        if os.name == "nt": identity = identity.casefold()
        expected = (ROOT / "unity_logs/studio").resolve() / "locks" / (hashlib.sha256(identity.encode()).hexdigest()[:24] + ".lock")
        self.assertEqual(engine.project_lock_path(pe.PRYING_EYES), expected)

    def test_hashed_sources_cover_the_pipeline_per_command(self):
        listing = {Path(p).relative_to(ROOT).as_posix() for p in pe.PRYING_EYES.tool_sources("gallery")}
        self.assertTrue({"tools/asset_graph.py", "tools/studio_gallery.py", "tools/studio_web/index.html", "tools/citylab.py",
                         "unity/PryingEyes/Assets/_PryingEyes/City/citta.json", "unity/PryingEyes/Packages/manifest.json",
                         "unity/PryingEyes/Packages/com.pryingeyes.weapons/Runtime/WeaponState.cs"} <= listing)
        plain = {Path(p).relative_to(ROOT).as_posix() for p in pe.PRYING_EYES.tool_sources("test")}
        self.assertNotIn("tools/asset_graph.py", plain); self.assertNotIn("tools/studio_gallery.py", plain)


if __name__ == "__main__": unittest.main()
