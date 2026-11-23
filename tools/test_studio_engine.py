"""The production studio engine on the toy project: everything here would hold for any project, because nothing here is the game."""
import contextlib
import io
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

import studio_engine as engine
from fixtures.toy_project import Sprites, ToyProject

TOOLS = Path(__file__).resolve().parent
SPRITES = {"sprites": [{"id": "spark", "name": "Spark", "pixels": 3}, {"id": "ember", "name": "Ember", "pixels": 5}]}


def toy(root, project=ToyProject):
    (Path(root) / "sprites.json").write_text(json.dumps(SPRITES), encoding="utf-8")
    return project(root)


def run(project, *argv):
    """The command line, in process: (exit code, stdout, the report of the job it ran if any)."""
    out = io.StringIO()
    with contextlib.redirect_stdout(out): code = engine.main(project, list(argv))
    latest = project.output / "latest.json"
    report = json.loads(Path(json.loads(latest.read_text())["report"]).read_text()) if latest.is_file() else None
    return code, out.getvalue(), report


class EngineOnAToyProject(unittest.TestCase):
    def test_catalog_inspect_and_unknown_asset(self):
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as folder:
            project = toy(folder)
            code, out, _ = run(project, "catalog", "list")
            self.assertEqual(code, 0); self.assertIn("spark", out); self.assertIn("ember", out)
            code, out, _ = run(project, "asset", "inspect", "ember")
            self.assertEqual(json.loads(out.split("\n", 1)[1].rsplit("STUDIO:", 1)[0])["pixels"], 5)
            code, _, report = run(project, "asset", "inspect", "nothing")
            self.assertEqual(code, 1); self.assertEqual(report["findings"][0]["code"], "ASSET_UNKNOWN")
            self.assertEqual(run(project, "doctor")[1].splitlines()[1], "STUDIO: 2 sprites")

    def test_build_records_steps_sources_and_artifacts(self):
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as folder:
            project = toy(folder)
            code, _, report = run(project, "asset", "build", "ember")
            self.assertEqual((code, report["state"], report["ok"]), (0, "complete", True))
            self.assertEqual([(s["name"], s["exitCode"]) for s in report["steps"]], [("make", 0)])
            self.assertEqual(Path(report["artifacts"][0]).read_text(), "#####")
            self.assertIn("sprites.json", report["sources"])
            self.assertTrue(any(name.endswith("studio_engine.py") for name in report["sources"]))   # the tool is part of what it verified

    def test_source_changed_by_a_job_fails_it_and_names_the_file(self):
        class Rewriting(Sprites):
            def build(self, ctx, entry):
                built = super().build(ctx, entry)
                self.catalog.write_text(json.dumps({"sprites": [{"id": "spark", "name": "Spark", "pixels": 9}]}), encoding="utf-8")
                return built

        class Project(ToyProject):
            def __init__(self, root):
                super().__init__(root); self.content_types = (Rewriting(self.root),); self.kinds = {"sprite": self.content_types[0]}

        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as folder:
            code, _, report = run(toy(folder, Project), "asset", "build", "spark")
            self.assertEqual(code, 1); self.assertEqual(report["state"], "failed")
            self.assertEqual(report["findings"][0]["code"], "SOURCE_CHANGED_DURING_JOB")
            self.assertEqual(report["sourceChanges"], ["sprites.json"])

    def test_publish_copies_the_build_and_proves_every_file(self):
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as folder:
            project = toy(folder)
            code, _, report = run(project, "asset", "build", "spark", "--publish")
            self.assertEqual(code, 0); self.assertEqual(report["published"]["filesVerified"], 1)
            self.assertEqual((Path(folder) / "published/spark.pixels").read_text(), "###")

    def test_suite_runs_under_the_lock_and_records_its_step(self):
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as folder:
            code, _, report = run(toy(folder), "test", "smoke")
            self.assertEqual(code, 0); self.assertEqual([s["name"] for s in report["steps"]], ["smoke"])

    def test_a_suite_says_what_it_is_and_the_defaults_are_empty(self):
        bare = engine.Suite(lambda ctx, runtime: None)
        self.assertEqual((bare.runtime, bare.kind, bare.needs, bare.hazards, bare.seconds, bare.about), (False, "", (), (), 0, ""))
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as folder:
            suites = toy(folder).suites
            self.assertEqual((suites["careful"].kind, suites["careful"].needs, suites["careful"].hazards), ("demo", ("easel",), ("noise", "paint")))

    def test_suites_lists_every_suite_with_its_fields_and_writes_nothing(self):
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as folder:
            project = toy(folder)
            code, out, report = run(project, "suites", "--json")
            self.assertEqual(code, 0); self.assertIsNone(report)                                  # no job, no report: it reads the registry only
            self.assertFalse(project.output.exists())
            listing = json.loads(out)                                                             # stdout is the JSON and nothing else
            self.assertEqual(listing["schemaVersion"], 1)
            self.assertEqual(listing["suites"], [
                {"name": "smoke", "kind": "check", "needs": [], "hazards": [], "seconds": 1, "about": "Starts the interpreter once"},
                {"name": "careful", "kind": "demo", "needs": ["easel"], "hazards": ["noise", "paint"], "seconds": 2,
                 "about": "Declares what it can disturb: it runs only when every hazard is allowed"}])
            code, text, _ = run(project, "suites")
            lines = text.splitlines()
            self.assertEqual(code, 0); self.assertEqual(len(lines), 3)                            # a header and one line per suite
            self.assertTrue(lines[0].startswith("suite") and lines[1].startswith("smoke") and lines[2].startswith("careful"))
            self.assertIn("noise, paint", lines[2]); self.assertIn("easel", lines[2]); self.assertIn("-", lines[1])

    def test_a_suite_with_hazards_is_refused_until_each_one_is_allowed(self):
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as folder:
            project = toy(folder)
            code, _, report = run(project, "test", "careful")
            self.assertEqual((code, report["state"], report["steps"]), (1, "failed", []))         # refused at once: nothing ran
            self.assertEqual(report["findings"][0]["code"], "SUITE_HAZARD_NOT_ALLOWED")
            self.assertIn("noise, paint", report["findings"][0]["observed"]); self.assertIn("--allow noise --allow paint", report["findings"][0]["observed"])
            code, _, report = run(project, "test", "careful", "--allow", "noise")                 # one of two is not enough, and the message names the rest
            self.assertEqual((code, report["steps"], report["findings"][0]["code"]), (1, [], "SUITE_HAZARD_NOT_ALLOWED"))
            self.assertIn("careful declares paint;", report["findings"][0]["observed"])
            code, _, report = run(project, "test", "careful", "--allow", "noise", "--allow", "paint")
            self.assertEqual((code, report["state"], [step["name"] for step in report["steps"]]), (0, "complete", ["careful"]))

    def test_allow_means_nothing_to_a_suite_without_hazards(self):
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as folder:
            code, _, report = run(toy(folder), "test", "smoke", "--allow", "anything")
            self.assertEqual((code, report["state"]), (0, "complete"))

    def test_queue_refuses_a_hazardous_suite_at_submission_and_keeps_the_allowances(self):
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as folder:
            project = toy(folder)
            code, out, _ = run(project, "queue", "add", "test", "careful")
            refused = json.loads(out)
            self.assertEqual((code, refused["ok"], refused["findings"][0]["code"]), (1, False, "SUITE_HAZARD_NOT_ALLOWED"))
            self.assertEqual(run(project, "queue", "status")[1].count('"state"'), 0)               # nothing was queued
            queued = json.loads(run(project, "queue", "add", "test", "careful", "--allow", "noise", "--allow", "paint")[1])["result"]["id"]
            self.assertEqual(engine.Queue(project.output).get(queued)["request"], ["test", "careful", "--allow", "noise", "--allow", "paint"])
            self.assertEqual(run(project, "queue", "run")[0], 0)
            self.assertEqual(engine.Queue(project.output).get(queued)["state"], "complete")

    def test_queue_runs_requests_and_rejects_stale_sources_before_any_step(self):
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as folder:
            project = toy(folder)
            queued = json.loads(run(project, "queue", "add", "asset", "build", "spark")[1])["result"]["id"]
            self.assertEqual(run(project, "queue", "run")[0], 0)
            self.assertEqual(engine.Queue(project.output).get(queued)["state"], "complete")
            stale = json.loads(run(project, "queue", "add", "asset", "build", "ember")[1])["result"]["id"]
            (Path(folder) / "sprites.json").write_text(json.dumps({"sprites": SPRITES["sprites"] + [{"id": "new", "name": "N", "pixels": 1}]}), encoding="utf-8")
            run(project, "queue", "run")
            report = engine.Queue(project.output).report(stale)
            self.assertEqual((report["state"], report["findings"][0]["code"], report["steps"]), ("failed", "QUEUED_SOURCES_CHANGED", []))

    def test_unsupported_operations_fail_with_a_code_not_a_traceback(self):
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as folder:
            project = toy(folder)
            self.assertEqual(run(project, "gallery", "build")[2]["findings"][0]["code"], "GALLERY_NOT_SUPPORTED")
            self.assertEqual(run(project, "asset", "preview", "spark")[2]["findings"][0]["code"], "PREVIEW_NOT_SUPPORTED")
            self.assertEqual(run(project, "catalog", "affected", "sprites.json")[2]["findings"][0]["code"], "GRAPH_NOT_SUPPORTED")

    def test_entry_ids_must_be_unique_across_content_types(self):
        class Clone(Sprites):
            name = "clone"
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as folder:
            project = toy(folder); project.content_types = (*project.content_types, Clone(folder))
            with self.assertRaisesRegex(ValueError, "CONTENT_ID_COLLISION: spark"): engine.load_catalogs(project)

    def test_project_lock_excludes_another_process_and_releases(self):
        script = ("import sys; sys.path.insert(0, sys.argv[2]); from fixtures.toy_project import ToyProject; import studio_engine as engine\n"
                  "with engine.project_lock(ToyProject(sys.argv[1], output=sys.argv[3])): pass")
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as folder:
            project = toy(folder)
            command = [sys.executable, "-c", script, folder, str(TOOLS), str(Path(folder) / "other-output")]
            with engine.project_lock(project):
                rejected = subprocess.run(command, cwd=TOOLS, capture_output=True, text=True)
                self.assertNotEqual(rejected.returncode, 0); self.assertIn("PROJECT_BUSY", rejected.stderr)
            self.assertEqual(subprocess.run(command, cwd=TOOLS, capture_output=True).returncode, 0)

    def test_step_timeout_is_reported(self):
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as folder:
            job = engine.Job(toy(folder), "test", "timeout")
            with self.assertRaisesRegex(RuntimeError, "STEP_TIMEOUT"):
                job.run("slow", [sys.executable, "-c", "import time; time.sleep(10)"], timeout=.1)
            self.assertEqual(json.loads((job.folder / "report.json").read_text())["steps"][0]["error"], "STEP_TIMEOUT")

    def test_new_source_file_during_resource_wait_is_rejected(self):
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as folder:
            project = toy(folder); job = engine.Job(project, "test", "sources"); job.report["sources"] = {"existing.cs": "old"}
            job.source_guard = lambda: {"existing.cs": "old", "added.cs": "new"}
            with self.assertRaisesRegex(RuntimeError, "SOURCE_CHANGED_DURING_JOB"):
                with engine.project_lock(project, job): self.fail("A changed source set acquired execution permission")
            self.assertEqual(job.report["sourceChanges"], ["added.cs"]); self.assertEqual(job.report["steps"], [])

    def test_command_line_of_another_project_works_end_to_end(self):
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as folder:
            toy(folder)
            result = subprocess.run([sys.executable, TOOLS / "fixtures/toy_project.py", folder, "catalog", "list"], capture_output=True, text=True, timeout=30)
            self.assertEqual(result.returncode, 0, result.stderr); self.assertIn("Ember", result.stdout)

    def test_engine_needs_only_the_standard_library(self):
        # no game module, no jsonschema, no Pillow: a project that does not use them must not have to install them
        script = ("import sys; sys.path.insert(0, sys.argv[1]); import studio_engine\n"
                  "bad = sorted(m for m in sys.modules if m.split('.')[0] in ('jsonschema', 'PIL', 'content_catalog', 'room_catalog', 'citylab', 'studio_pryingeyes'))\n"
                  "sys.exit(','.join(bad) if bad else 0)")
        result = subprocess.run([sys.executable, "-I", "-c", script, str(TOOLS)], capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)


if __name__ == "__main__": unittest.main()
