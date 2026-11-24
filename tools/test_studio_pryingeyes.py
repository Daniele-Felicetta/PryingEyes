"""The Prying Eyes profile: the commands it issues to Blender, Unity, the labs and the player, checked without running any of them."""
import argparse
import contextlib
import hashlib
import io
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import types
import unittest
from unittest.mock import patch

import studio_engine as engine
import studio_pryingeyes as pe
from content_catalog import ROOT, read_catalog
from room_catalog import validate_rooms

TOOLS = {"blender": Path("blender.exe"), "unity": Path("Unity.exe")}
# What the real tools leave behind and the profile then verifies.
LEFT_BEHIND = {"gunplaytest": ("gunplaytest-player.log", "[PryingEyes] gunplaytest RISULTATO SUPERATA: 18/18"), "room-materials": ("room-materials-unity.log", "MATERIAL_IMPORT_PASS"),
               "weapon-state": ("weapon-state.json", '{"ok": true, "findings": []}'), "survival-state": ("survival-state.json", '{"ok": true, "findings": []}'),
               "economy-state": ("economy-state.json", '{"ok": true, "findings": []}'), "saves-state": ("saves-state.json", '{"ok": true, "findings": []}'),
               "track-state": ("track-state.json", '{"ok": true, "findings": []}'), "desert-state": ("desert-state.json", '{"ok": true, "findings": []}')}


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
        patcher = patch.object(pe, "processes_named", return_value=[]); patcher.start(); self.addCleanup(patcher.stop)       # what runs on this machine is not the test's business

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

    def test_before_build_checks_the_editor_then_runs_the_rule_labs(self):
        kind, entry, ctx = self.context("p45_argine")
        with patch.object(pe, "check_editor_closed") as check:
            pe.PRYING_EYES.before_build(ctx)
        check.assert_called_once()
        self.assertEqual([label for label, _ in self.job.steps], ["weapon-state", "survival-state", "economy-state", "saves-state", "track-state"])

    def desert_suite(self, left=None, job=None):
        """Runs the `desert` suite on a job that leaves the lab's report `left` (a dict), or no report at all."""
        job = job or self.job
        with patch.dict(LEFT_BEHIND):
            if left is None: del LEFT_BEHIND["desert-state"]
            else: LEFT_BEHIND["desert-state"] = ("desert-state.json", json.dumps(left))
            pe.PRYING_EYES.suites["desert"].run(engine.Context(job, pe.PRYING_EYES, TOOLS), None)

    def test_desert_suite_runs_the_lab_with_a_report_and_keeps_its_findings(self):
        finding = {"code": "NOISE_RANGE", "subject": "noise", "passed": True, "observed": "inside", "expected": ""}
        self.desert_suite({"ok": True, "findings": [finding]})
        report = Path(self.folder.name) / "desert-state.json"
        self.assertEqual(self.job.steps, [("desert-state", ["dotnet", "run", "-c", "Release", "--project", str(ROOT / "tools/desertlab"), "--", "--report", str(report)])])
        self.assertEqual(self.job.report["artifacts"], [str(report)])
        self.assertEqual(self.job.report["findings"], [{**finding, "evidence": str(report)}])

    def test_desert_suite_fails_on_a_failed_check_and_the_report_still_names_it(self):
        failed = {"code": "NOISE_RANGE", "subject": "noise", "passed": False, "observed": "1.2", "expected": ""}
        with self.assertRaisesRegex(RuntimeError, "DESERT_STATE_FAILED_OR_REPORT_MISSING"): self.desert_suite({"ok": False, "findings": [failed]})
        self.assertEqual([f["code"] for f in self.job.report["findings"] if not f["passed"]], ["NOISE_RANGE"])

    def test_desert_suite_fails_when_the_lab_leaves_no_report(self):
        with self.assertRaisesRegex(RuntimeError, "DESERT_STATE_FAILED_OR_REPORT_MISSING"): self.desert_suite(None)
        self.assertEqual(self.job.report["findings"], [])

    def plan_lab_report(self, **changes):
        """What the plan lab writes when it computes exactly the plans and the revision of the committed fixture, with `changes` applied."""
        fixture = json.loads(pe.PLAN_FIXTURE.read_text(encoding="utf-8"))
        return {"schemaVersion": 1, "operation": "plan.fingerprint", "ok": True, "revision": fixture["revision"], "plans": fixture["plans"], **changes}

    def plan_suite(self, report):
        """Runs the `plan` suite on a job whose lab leaves `report` (a dict), or no report at all (None)."""
        with patch.dict(LEFT_BEHIND):
            if report is not None: LEFT_BEHIND["plan-state"] = ("plan-state.json", json.dumps(report))
            pe.PRYING_EYES.suites["plan"].run(engine.Context(self.job, pe.PRYING_EYES, TOOLS), None)

    def test_plan_suite_runs_the_fingerprint_and_finds_the_fixture_unchanged(self):
        self.plan_suite(self.plan_lab_report())
        report = Path(self.folder.name) / "plan-state.json"
        self.assertEqual(self.job.steps, [("plan-state", ["dotnet", "run", "-c", "Release", "--project", str(ROOT / "tools/planlab"), "--", "fingerprint", "--report", str(report)])])
        self.assertEqual(self.job.report["artifacts"], [str(report)])
        findings = self.job.report["findings"]
        self.assertEqual([f["code"] for f in findings].count("PLAN_FINGERPRINT"), len(findings) - 1)       # one per plan, then the revision
        self.assertEqual(findings[-1]["code"], "PLAN_REVISION")
        self.assertTrue(all(f["passed"] and f["evidence"] == str(report) for f in findings))

    def test_plan_suite_names_the_plan_that_changed_and_what_to_do(self):
        report = self.plan_lab_report(); first = report["plans"][0]
        report["plans"] = [{**first, "sha256": "0" * 64}, *report["plans"][1:]]
        with self.assertRaises(RuntimeError) as raised: self.plan_suite(report)
        name = f"{first['layout']} seed {first['seed']} floor {first['floor']}"
        self.assertRegex(str(raised.exception), "^PLAN_FINGERPRINT_CHANGED: 1 of %d plans differ" % len(report["plans"]))
        self.assertIn(f"({name})", str(raised.exception)); self.assertIn("raise CoopProtocol.Revision", str(raised.exception)); self.assertIn("fingerprint --update", str(raised.exception))
        failed = [f for f in self.job.report["findings"] if not f["passed"]]
        self.assertEqual([(f["code"], f["subject"], f["observed"]) for f in failed], [("PLAN_FINGERPRINT", name, "0" * 64)])
        self.assertEqual(failed[0]["expected"], first["sha256"])

    def test_plan_suite_asks_for_the_fixture_when_only_the_revision_moved(self):
        with self.assertRaisesRegex(RuntimeError, "^PLAN_REVISION_STALE: CoopWire.cs says pe-coop-0 and plan-fingerprint.json was made at pe-coop-"):
            self.plan_suite(self.plan_lab_report(revision="pe-coop-0"))
        self.assertEqual([(f["code"], f["observed"]) for f in self.job.report["findings"] if not f["passed"]], [("PLAN_REVISION", "pe-coop-0")])

    def test_plan_suite_fails_without_a_report_and_without_a_fixture(self):
        with self.assertRaisesRegex(RuntimeError, "^PLAN_STATE_FAILED_OR_REPORT_MISSING"): self.plan_suite(None)
        self.assertEqual(self.job.report["findings"], [])
        with self.assertRaisesRegex(RuntimeError, "^PLAN_FIXTURE_MISSING"): pe.plan_state(self.job, Path(self.folder.name) / "absent.json")
        self.assertEqual(len(self.job.steps), 1)                                  # the lab ran for the first and was not started for the second

    def test_plan_findings_cover_a_plan_the_fixture_lacks_and_one_the_lab_lost(self):
        plan = lambda seed, sha: {"layout": "waves", "seed": seed, "floor": 1, "sha256": sha}
        findings = pe.plan_findings({"revision": "r", "plans": [plan(1, "a"), plan(2, "b")]}, {"revision": "r", "plans": [plan(1, "a"), plan(3, "c")]})
        self.assertEqual([(f["subject"], f["passed"], f["observed"], f["expected"]) for f in findings if f["code"] == "PLAN_FINGERPRINT"],
                         [("waves seed 1 floor 1", True, "a", "a"), ("waves seed 2 floor 1", False, "b", ""), ("waves seed 3 floor 1", False, "", "c")])
        self.assertTrue(findings[-1]["passed"])

    def test_the_committed_plan_fixture_is_well_formed(self):
        fixture = json.loads(pe.PLAN_FIXTURE.read_text(encoding="utf-8"))
        keys = [(p["layout"], p["seed"], p["floor"]) for p in fixture["plans"]]
        self.assertEqual(len(keys), len(set(keys)))
        self.assertTrue(fixture["revision"]); self.assertLessEqual({layout for layout, _, _ in keys}, {"default", "waves"})
        self.assertTrue(all(re.fullmatch("[0-9a-f]{64}", p["sha256"]) for p in fixture["plans"]))

    def test_after_build_runs_gunplaytest_on_the_new_build_then_city_acceptance_with_assets_required(self):
        kind, entry, ctx = self.context("p45_argine")
        built = self.build / "PryingEyes.exe"
        built.parent.mkdir(parents=True); built.write_bytes(b"")
        pe.PRYING_EYES.after_build(ctx, built)
        self.assertEqual([label for label, _ in self.job.steps], ["gunplaytest", "city"])
        gunplay = dict(self.job.steps)["gunplaytest"]
        self.assertEqual(gunplay[:1 + len(pe.PLAYER_ENTRIES["gunplaytest"]["args"])], [str(built), *pe.PLAYER_ENTRIES["gunplaytest"]["args"]])    # the build just made, the manifest's arguments
        city = dict(self.job.steps)["city"]
        self.assertIn("--require-assets", city); self.assertEqual(city[city.index("--runtime") + 1], str(built))

    def test_registry_matches_the_command_line_the_docs_describe(self):
        self.assertEqual(list(pe.PRYING_EYES.kinds), ["weapon", "room"])
        labs = ["weapon-state", "survival", "economy", "saves", "track", "desert", "plan"]
        suites = pe.PRYING_EYES.suites
        self.assertEqual(list(suites)[:len(labs)], labs)
        self.assertEqual({name: suites[name].runtime for name in labs}, {name: False for name in labs})
        self.assertTrue(all(suite.runtime for name, suite in suites.items() if name not in labs))                # every other suite runs a player
        self.assertEqual(pe.PRYING_EYES.publish_dir, pe.EXE.parent)

    def test_every_suite_says_what_it_is_and_what_needs_nothing_is_what_the_gate_runs(self):
        suites = pe.PRYING_EYES.suites
        self.assertTrue(all(suite.kind and suite.about for suite in suites.values()))
        self.assertEqual({name for name, suite in suites.items() if suite.runtime}, {name for name, suite in suites.items() if "runtime" in suite.needs})   # the flag and the word agree
        self.assertEqual({name: suite.hazards for name, suite in suites.items() if suite.hazards}, {"uitest": ("registry", "screen"), "displaytest": ("registry",)})
        gate = [name for name, suite in suites.items() if not suite.needs]
        self.assertEqual(gate, ["weapon-state", "survival", "economy", "saves", "track", "desert", "plan"])       # the rule labs, in the order of the run summary
        self.assertEqual({suites[name].kind for name in gate}, {"lab"})
        out = io.StringIO()
        with contextlib.redirect_stdout(out): code = engine.main(pe.PRYING_EYES, ["suites", "--json"])
        listing = json.loads(out.getvalue())
        self.assertEqual((code, listing["schemaVersion"], [item["name"] for item in listing["suites"]]), (0, 1, list(suites)))
        self.assertEqual({item["name"]: item["needs"] for item in listing["suites"]}["gunplaytest"], ["runtime"])
        self.assertEqual([item["name"] for item in listing["suites"] if not item["needs"]], gate)

    def test_the_gate_workflow_asks_the_studio_and_names_no_lab_and_no_suite(self):
        workflow = (ROOT / ".github/workflows/gate.yml").read_text(encoding="utf-8")
        self.assertIn("studio.py suites --json", workflow)
        self.assertEqual([name for name in re.findall(r"studio\.py test (\S+)", workflow) if not name.startswith("$")], [])      # each suite is run by the name it is listed with
        labs = sorted(path.name for path in (ROOT / "tools").glob("*lab") if path.is_dir())
        self.assertTrue(labs)
        self.assertEqual([lab for lab in labs if lab in workflow], [])

    @unittest.skipUnless(shutil.which("git"), "git is what says which paths .gitignore ignores")
    def test_a_lab_nobody_has_written_yet_needs_no_line_in_gitignore(self):
        """The patterns for the labs cover every tools/*lab folder: its project stays tracked in spite of the global *.csproj, its build folders do not."""
        with tempfile.TemporaryDirectory() as folder:
            repo = Path(folder)
            subprocess.run(["git", "init", "-q"], cwd=repo, check=True)
            shutil.copyfile(ROOT / ".gitignore", repo / ".gitignore")
            def ignored(path):          # exit 0: ignored, 1: not; a pattern that re-includes a path counts as not ignored
                return subprocess.run(["git", "check-ignore", "-q", "--", path], cwd=repo).returncode == 0
            real = sorted(path.relative_to(ROOT).as_posix() for path in (ROOT / "tools").glob("*lab/*.csproj"))
            self.assertTrue(real)
            for project in real + ["tools/newlab/NewLab.csproj", "tools/newlab/Program.cs"]: self.assertFalse(ignored(project), project)
            for derived in ("tools/newlab/bin/Release/net8.0/NewLab.dll", "tools/newlab/obj/project.assets.json", "tools/planlab/bin/Release/net8.0/PlanLab.dll"):
                self.assertTrue(ignored(derived), derived)
            self.assertTrue(ignored("tools/scratch/Scratch.csproj"))                        # a project that is not a lab stays out of git

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
                         "unity/PryingEyes/Packages/com.pryingeyes.weapons/Runtime/WeaponState.cs",
                         "tools/desertlab/Program.cs", "tools/desertlab/DesertLab.csproj", "tools/fixtures/plan-fingerprint.json",
                         "tools/planlab/Fingerprint.cs", "unity/PryingEyes/Assets/_PryingEyes/Scripts/Coop/CoopWire.cs",
                         "tools/player_tests.json", "tools/player_log.py", "tools/coop/run_all.ps1", "tools/coop/run_split.ps1"} <= listing)       # a job on a suite of the player is bound to these
        plain = {Path(p).relative_to(ROOT).as_posix() for p in pe.PRYING_EYES.tool_sources("test")}
        self.assertNotIn("tools/asset_graph.py", plain); self.assertNotIn("tools/studio_gallery.py", plain)


# ---- the suites that run inside the player: invented logs and a job that runs nothing -----------------------------------------------------

def log_of(suite, ok=(), failed=(), result=True, coop=None):
    """What a test of the game prints: a line for each check, then the result line (left out when `result` is false). `coop` is the process
    that speaks ("ospite" or "ospitato"): the lines are then CoopTest's, with the colon, the detail in square brackets and the tail."""
    lines = ["Initialize engine version: 6000.6.7f1", "[PryingEyes] avvio"]
    who = f"{suite} {coop}:" if coop else suite
    if coop:
        lines += [f"[PryingEyes] {who} ok       {name}   [rilevato {index}]" for index, name in enumerate(ok)]
        lines += [f"[PryingEyes] {who} FALLITO  {name}   [atteso 1000, trovato 1500]" for name in failed]
    else:
        lines += [f"[PryingEyes] {who} ok {name} (rilevato {index})" for index, name in enumerate(ok)]
        lines += [f"[PryingEyes] {who} FALLITO {name} (atteso 1000, trovato 1500)" for name in failed]
    if result:
        lines.append(f"[PryingEyes] {who} RISULTATO {'FALLITA' if failed else 'SUPERATA'}: {len(ok)}/{len(ok) + len(failed)}{' verifiche ok' if coop else ''}")
    return "\n".join(lines) + "\n"


def plays(log=None, result=None, files=None, fail=None):
    """What the process does when the job runs it: leaves its log where -logFile says, a result.json (a dict, or the text of a broken one) in
    -testout, `files` (name -> text) in the log folder of a script (-LogDir), and then fails like Job.run does (`fail` is an error or its text)."""
    def play(argv):
        if log is not None: Path(argv[argv.index("-logFile") + 1]).write_text(log, encoding="utf-8")
        if result is not None:
            (Path(argv[argv.index("-testout") + 1]) / "result.json").write_text(result if isinstance(result, str) else json.dumps(result), encoding="utf-8")
        for name, text in (files or {}).items(): (Path(argv[argv.index("-LogDir") + 1]) / name).write_text(text, encoding="utf-8")
        if fail: raise fail if isinstance(fail, BaseException) else RuntimeError(fail)
    return play


class PlayerJob(RecordingJob):
    """A job that runs nothing: `play(argv)` is what the process would do, `timeouts` what each step was given, `events` the order of things."""
    def __init__(self, folder, play=None, events=None):
        super().__init__(folder); self.play, self.timeouts, self.events = play, {}, [] if events is None else events
    def run(self, label, command, cwd=None, timeout=900):
        argv = [str(part) for part in command]
        self.events.append("run"); self.steps.append((label, argv)); self.timeouts[label] = timeout
        if self.play: self.play(argv)
        return self.folder / (label + ".log")


class PlayerSuites(unittest.TestCase):
    """The profile's suites of the player: each entry of tools/player_tests.json run as the card of T06 says."""
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory(); self.addCleanup(self.folder.cleanup)
        self.job_folder = Path(self.folder.name) / "job"; self.job_folder.mkdir()
        self.exe = Path(self.folder.name) / "build/PryingEyes.exe"; self.exe.parent.mkdir(); self.exe.write_bytes(b"")
        self.running, self.events = [], []                            # the PryingEyes.exe processes of the machine, and the order of things
        patcher = patch.object(pe, "processes_named", side_effect=lambda *names: list(self.running)); patcher.start(); self.addCleanup(patcher.stop)

    def entry(self, name="survivaltest", **changes):
        """An entry of the manifest as it is, but for the number of checks that a run has confirmed (the tests give their own)."""
        return {**{key: value for key, value in pe.PLAYER_ENTRIES[name].items() if key != "checks"}, **changes}

    def fresh(self, play):
        """A job in an empty folder, as every real job has: what an earlier run of the test left is gone."""
        shutil.rmtree(self.job_folder); self.job_folder.mkdir()
        return PlayerJob(self.job_folder, play, self.events)

    def go(self, play, entry=None, runtime=None):
        job = self.fresh(play)
        pe.player_state(job, entry or self.entry(), runtime or self.exe)
        return job

    def fails(self, play, code, entry=None, runtime=None):
        """Runs a suite that must fail with `code`; returns the job and the message."""
        job = self.fresh(play)
        with self.assertRaises(RuntimeError) as raised: pe.player_state(job, entry or self.entry(), runtime or self.exe)
        self.assertRegex(str(raised.exception), f"^{code}: ")
        return job, str(raised.exception)

    # ---- what the registry is made of

    def test_every_entry_of_the_manifest_is_a_suite_that_needs_what_the_entry_says(self):
        manifest = json.loads(pe.PLAYER_TESTS.read_text(encoding="utf-8-sig"))["suites"]
        suites = pe.PRYING_EYES.suites
        self.assertEqual([entry["name"] for entry in manifest], [name for name, suite in suites.items() if suite.kind == "player"])
        for entry in manifest:
            suite = suites[entry["name"]]
            self.assertEqual((suite.runtime, suite.needs, suite.hazards), (True, tuple(entry["needs"]), tuple(entry["hazards"])), entry["name"])
            self.assertIn(entry["page"], suite.about)
        self.assertFalse({"-logFile", "-report", "-testout"} & {flag for entry in manifest for flag in entry.get("args", [])})     # the runner adds these three

    def test_the_default_build_is_the_verification_build_not_the_one_the_studio_publishes(self):
        self.assertEqual(pe.PRYING_EYES.runtime, pe.AUDIT)
        self.assertEqual(pe.AUDIT, pe.UNITY / "Builds/Audit/PryingEyes.exe")
        self.assertNotEqual(pe.AUDIT.parent, pe.PRYING_EYES.publish_dir)
        self.assertEqual(engine.arguments(pe.PRYING_EYES).parse_args(["test", "gunplaytest"]).runtime, pe.AUDIT)

    def test_gunplay_and_gunplaytest_are_one_suite(self):
        self.assertNotIn("gunplay", pe.PRYING_EYES.suites)                                            # there is no second name for the same check
        suite, entry = pe.PRYING_EYES.suites["gunplaytest"], pe.PLAYER_ENTRIES["gunplaytest"]
        self.assertEqual((suite.kind, suite.needs, suite.hazards, suite.runtime), ("player", ("runtime",), (), True))
        self.assertEqual(entry["args"], ["-batchmode", "-autotest", "-nofire", "-gunplaytest", "-seed", "1000", "-nographics"])     # tools/check_gunplay.py's own line
        with patch.dict(LEFT_BEHIND, {"gunplaytest": ("gunplaytest-player.log", log_of("gunplaytest", ok=["a"]))}):
            job = RecordingJob(self.job_folder); pe.native_gunplay(job, self.exe)                     # what asset build does, with the build that it made
        self.assertEqual(job.steps[0][1][:len(entry["args"]) + 1], [str(self.exe), *entry["args"]])

    def test_the_settings_key_is_the_one_the_split_screen_script_saves(self):
        self.assertIn("'" + pe.SETTINGS_KEY + "'", (ROOT / "tools/coop/run_split.ps1").read_text(encoding="utf-8-sig"))

    # ---- the command, and what each way to end means

    def test_the_command_is_the_manifests_arguments_then_where_the_run_leaves_its_files(self):
        entry = self.entry()
        job = self.go(plays(log_of("survivaltest", ok=["a"])), entry)
        (label, command), = job.steps
        self.assertEqual(label, "survivaltest")
        self.assertEqual(command, [str(self.exe), *entry["args"], "-logFile", str(self.job_folder / "survivaltest-player.log"),
                                   "-report", str(self.job_folder / "survivaltest-telemetry.txt"), "-testout", str(self.job_folder / "testout")])
        self.assertEqual(job.timeouts, {"survivaltest": entry["timeout"]})
        self.assertTrue((self.job_folder / "testout").is_dir())

    def test_a_log_that_says_it_passed_passes_and_every_check_is_a_finding(self):
        job = self.go(plays(log_of("survivaltest", ok=["la candela", "il budget", "la fine"])))
        findings = job.report["findings"]
        self.assertEqual([(f["code"], f["subject"], f["observed"], f["passed"]) for f in findings],
                         [("survivaltest:1", "la candela", "rilevato 0", True), ("survivaltest:2", "il budget", "rilevato 1", True), ("survivaltest:3", "la fine", "rilevato 2", True)])
        log = str(self.job_folder / "survivaltest-player.log")
        self.assertTrue(all(f["evidence"] == log for f in findings))
        self.assertEqual(job.report["player"], {"suite": "survivaltest", "verdictSource": "log-line", "checks": 3, "passed": 3, "declared": None, "exceptions": 0, "logs": [log]})
        self.assertEqual(job.report["artifacts"], [log])

    def test_a_failed_line_fails_names_the_check_and_the_report_keeps_every_check(self):
        job, message = self.fails(plays(log_of("survivaltest", ok=["a", "b"], failed=["la leva costa 1000"]), fail="STEP_FAILED: survivaltest; exit 1; log"), "PLAYER_CHECK_FAILED")
        self.assertIn("1 of 3 checks failed", message); self.assertIn("la leva costa 1000", message)
        self.assertEqual([f["passed"] for f in job.report["findings"]], [True, True, False])
        self.assertEqual(job.report["findings"][2]["observed"], "atteso 1000, trovato 1500")

    def test_a_result_line_that_says_passed_over_a_failed_line_does_not_pass(self):
        text = log_of("survivaltest", ok=["a"], failed=["b"]).replace("FALLITA: 1/2", "SUPERATA: 2/2")      # a lie the lines give away
        self.fails(plays(text), "PLAYER_CHECK_FAILED")

    def test_a_run_that_leaves_no_result_fails_whatever_else_it_left(self):
        _, message = self.fails(plays(log_of("survivaltest", ok=["a"], result=False)), "PLAYER_RESULT_MISSING")      # it stopped before the result line
        self.assertIn("survivaltest-player.log", message)
        self.assertIn("[PryingEyes] survivaltest RISULTATO", message); self.assertIn("ported to the harness", message)      # what it looked for, and what to do about it
        self.fails(plays(), "PLAYER_RESULT_MISSING")                                                            # it never started: no log at all
        _, crash = self.fails(plays("Crash!\n", fail="STEP_FAILED: survivaltest; exit -1073741819; log"), "PLAYER_RESULT_MISSING")
        self.assertIn("exit -1073741819", crash)                                                                # and says how the process ended
        self.fails(plays(log_of("chartest", ok=["a"])), "PLAYER_RESULT_MISSING")                                # the result of another suite is no answer

    def test_a_suite_of_a_family_reads_the_lines_of_the_label_its_entry_gives(self):
        family = self.entry("pistamenulicence")                                                  # prints `pistamenutest`, like its brothers
        self.assertEqual(family["label"], "pistamenutest")
        job = self.go(plays(log_of("pistamenutest", ok=["a", "b"])), family)
        self.assertEqual((job.report["player"]["suite"], job.report["player"]["checks"]), ("pistamenulicence", 2))
        self.assertEqual([f["code"] for f in job.report["findings"]], ["pistamenulicence:1", "pistamenulicence:2"])      # the findings carry the suite's name
        _, message = self.fails(plays(log_of("pistamenulicence", ok=["a"])), "PLAYER_RESULT_MISSING", family)           # its own name is not what it prints
        self.assertIn("[PryingEyes] pistamenutest RISULTATO", message)
        self.fails(plays(log_of("pistatest", ok=["a"])), "PLAYER_RESULT_MISSING", family)                                # nor is another family's
        harness = {"schemaVersion": 1, "suite": "pistamenutest", "ok": True, "checks": [{"name": "a", "ok": True}]}
        self.assertEqual(self.go(plays(log_of("x", ok=["a"]), harness), family).report["player"]["verdictSource"], "result.json")   # the label is the identity of a result.json too

    def test_fewer_checks_than_the_manifest_declares_fail_and_as_many_or_more_do_not(self):
        entry = self.entry(checks=4)
        job, message = self.fails(plays(log_of("survivaltest", ok=["a", "b", "c"])), "PLAYER_CHECKS_FEWER", entry)
        self.assertIn("ran 3 checks", message); self.assertIn("says 4", message)
        self.assertEqual(job.report["player"]["declared"], 4)
        self.go(plays(log_of("survivaltest", ok=list("abcd"))), entry)
        self.go(plays(log_of("survivaltest", ok=list("abcde"))), entry)

    def test_an_exception_in_the_log_fails_but_the_renderers_noise_does_not(self):
        noise = "NullReferenceException: Object reference not set\n  at UnityEngine.Rendering.Universal.UniversalRenderPipeline.Render ()\n"
        self.go(plays(log_of("survivaltest", ok=["a"]) + noise))
        real = "InvalidOperationException: la scena non c'e'\n  at PryingEyes.Foo.Bar ()\n"
        job, message = self.fails(plays(log_of("survivaltest", ok=["a"]) + noise + real), "PLAYER_EXCEPTION")
        self.assertIn("InvalidOperationException", message); self.assertNotIn("NullReference", message)
        self.assertEqual(job.report["player"]["exceptions"], 1)

    def test_result_json_is_read_first_and_the_log_line_is_only_the_fallback(self):
        result = {"schemaVersion": 1, "suite": "survivaltest", "ok": True, "exceptions": 0, "seconds": 3.5,
                  "checks": [{"name": "a", "ok": True, "detail": "1"}, {"name": "b", "ok": True}]}
        job = self.go(plays(log_of("survivaltest", failed=["x"]), result))                    # the log says failed, the file says passed: the file wins
        self.assertEqual((job.report["player"]["verdictSource"], job.report["player"]["checks"]), ("result.json", 2))
        self.assertEqual([(f["code"], f["subject"], f["observed"]) for f in job.report["findings"]], [("survivaltest:1", "a", "1"), ("survivaltest:2", "b", "")])
        written = str(self.job_folder / "testout/result.json")
        self.assertIn(written, job.report["artifacts"]); self.assertTrue(all(f["evidence"] == written for f in job.report["findings"]))
        bad = {**result, "ok": False, "checks": [{"name": "a", "ok": True}, {"name": "b", "ok": False, "detail": "punti 1500 -> 1500"}]}
        job, message = self.fails(plays(log_of("survivaltest", ok=["a", "b"]), bad), "PLAYER_CHECK_FAILED")     # the log says passed, the file says failed
        self.assertIn("(b)", message); self.assertEqual(job.report["findings"][1]["observed"], "punti 1500 -> 1500")

    def test_a_result_json_that_cannot_be_trusted_is_not_replaced_by_the_log(self):
        good = {"schemaVersion": 1, "suite": "survivaltest", "ok": True, "checks": [{"name": "a", "ok": True}]}
        for broken in ({**good, "suite": "traptest"}, {**good, "schemaVersion": 2}, {**good, "ok": "yes"}, {**good, "checks": [{"name": "a"}]}, "{not json", "[]"):
            _, message = self.fails(plays(log_of("survivaltest", ok=["a"]), broken), "PLAYER_RESULT_INVALID")
            self.assertIn("survivaltest:", message)

    def test_a_timeout_and_an_exit_code_are_not_taken_for_a_failed_check(self):
        entry = self.entry()
        _, message = self.fails(plays(log_of("survivaltest", ok=["a"], result=False), fail="STEP_TIMEOUT: survivaltest"), "PLAYER_TIMEOUT")
        self.assertIn(f"within {entry['timeout']} s", message)
        self.fails(plays(log_of("survivaltest", ok=["a"]), fail="STEP_FAILED: survivaltest; exit 1; log"), "STEP_FAILED")     # passed, yet it quit with 1
        with self.assertRaises(engine.JobCancelled): self.go(plays(fail=engine.JobCancelled("JOB_CANCELLED: test")))        # not the process's own: untouched

    # ---- what a suite refuses to start

    def test_a_build_that_is_not_there_is_named_and_nothing_starts(self):
        job, message = self.fails(plays(), "PLAYER_RUNTIME_MISSING", runtime=Path(self.folder.name) / "nowhere/PryingEyes.exe")
        self.assertIn("nowhere", message); self.assertEqual(job.steps, [])

    def test_a_run_that_draws_is_refused_while_another_player_runs_and_one_that_does_not_is_not(self):
        self.running = [{"ProcessId": 4242, "ExecutablePath": r"C:\games\other\PryingEyes.exe", "CommandLine": ""}]
        args = [flag for flag in self.entry()["args"] if flag != "-nographics"]
        draws = self.entry(needs=["runtime", "gpu"], args=args)
        job, message = self.fails(plays(log_of("survivaltest", ok=["a"])), "PLAYER_GPU_BUSY", draws)
        self.assertIn("4242", message); self.assertEqual(job.steps, [])                              # nothing was started
        self.go(plays(log_of("survivaltest", ok=["a"])))                                               # windowless: another player is no obstacle
        self.running = []
        self.go(plays(log_of("survivaltest", ok=["a"])), draws)                                        # nobody else: it runs

    def test_two_copies_of_one_build_are_refused(self):
        self.running = [{"ProcessId": 7, "ExecutablePath": str(self.exe).upper(), "CommandLine": ""}]
        job, message = self.fails(plays(), "PLAYER_IN_USE")
        self.assertIn("7", message); self.assertEqual(job.steps, [])

    @unittest.skipUnless(os.name == "nt", "8.3 names are Windows'")
    def test_a_build_that_is_running_under_its_short_name_is_the_same_build(self):
        """The CI runner's temporary folder is C:\\Users\\RUNNER~1\\...: the same file has two spellings, and Windows may report either."""
        import ctypes
        buffer = ctypes.create_unicode_buffer(1024)
        size = ctypes.windll.kernel32.GetShortPathNameW(str(self.exe), buffer, len(buffer))
        short = buffer.value if 0 < size < len(buffer) else str(self.exe)
        if short.casefold() == str(self.exe.resolve()).casefold(): self.skipTest("this volume gives no 8.3 names")
        self.running = [{"ProcessId": 8, "ExecutablePath": short, "CommandLine": ""}]
        self.fails(plays(), "PLAYER_IN_USE")                                                    # named by its long path
        self.running = [{"ProcessId": 9, "ExecutablePath": str(self.exe.resolve()), "CommandLine": ""}]
        self.fails(plays(), "PLAYER_IN_USE", runtime=Path(short))                               # the other way round

    def test_a_suite_with_hazards_is_refused_until_every_one_is_allowed(self):
        for name, hazards in (("uitest", ["registry", "screen"]), ("displaytest", ["registry"])):
            refuse = lambda allow: engine.refuse_unallowed_hazards(pe.PRYING_EYES, argparse.Namespace(suite=name, allow=allow))
            with self.assertRaisesRegex(RuntimeError, f"^SUITE_HAZARD_NOT_ALLOWED: {name} declares {', '.join(hazards)}"): refuse([])
            if len(hazards) > 1:
                with self.assertRaisesRegex(RuntimeError, "^SUITE_HAZARD_NOT_ALLOWED"): refuse(hazards[:1])
            refuse(hazards)

    def test_the_settings_key_is_exported_before_the_run_and_imported_after_it_whatever_happens(self):
        calls, answers = [], {"export": 0, "import": 0}
        def reg(*arguments):
            calls.append([str(argument) for argument in arguments]); self.events.append("reg " + str(arguments[0]))
            return types.SimpleNamespace(returncode=answers[str(arguments[0])])
        settings = lambda job: [(f["code"], f["passed"]) for f in job.report["findings"] if f["code"].startswith("SETTINGS")]
        entry, backup = self.entry(hazards=["registry"]), str(self.job_folder / "settings-before.reg")
        with patch.object(pe, "reg", reg):
            job = self.go(plays(log_of("survivaltest", ok=["a"])), entry)
            self.assertEqual(self.events, ["reg export", "run", "reg import"])
            self.assertEqual(calls, [["export", pe.SETTINGS_KEY, backup, "/y"], ["import", backup]])
            self.assertEqual(settings(job), [("SETTINGS_RESTORED", True)])
            self.events.clear()
            self.fails(plays(log_of("survivaltest", failed=["a"]), fail="STEP_FAILED: survivaltest; exit 1; log"), "PLAYER_CHECK_FAILED", entry)      # a failed run
            self.assertEqual(self.events, ["reg export", "run", "reg import"])
            self.events.clear()
            with self.assertRaises(engine.JobCancelled): self.go(plays(fail=engine.JobCancelled("JOB_CANCELLED: test")), entry)                    # a cancelled one
            self.assertEqual(self.events, ["reg export", "run", "reg import"])
            self.events.clear(); answers["export"] = 1                                                    # no key yet: nothing to put back
            job = self.go(plays(log_of("survivaltest", ok=["a"])), entry)
            self.assertEqual(self.events, ["reg export", "run"]); self.assertEqual(settings(job), [("SETTINGS_KEY_ABSENT", True)])
            self.events.clear(); answers["export"] = 0; answers["import"] = 1                             # the key cannot be put back: the report and the error say it
            job, message = self.fails(plays(log_of("survivaltest", ok=["a"])), "PLAYER_SETTINGS_NOT_RESTORED", entry)
            self.assertEqual(settings(job), [("SETTINGS_RESTORED", False)])
        self.events.clear()
        with patch.object(pe, "reg", side_effect=AssertionError("a suite without the hazard does not touch the key")):
            self.go(plays(log_of("survivaltest", ok=["a"])))
        self.assertEqual(self.events, ["run"])

    # ---- the suites made of several processes

    def test_an_entry_with_a_runner_calls_its_script_with_the_job_folder_as_log_folder(self):
        powershell = ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File"]
        coop = pe.PLAYER_ENTRIES["coop-a-griglia"]
        files = {"coop_a_griglia_host.txt": log_of("cooptest", ok=["x", "y"], coop="ospite"), "coop_a_griglia_c1.txt": log_of("cooptest", ok=["z"], coop="ospitato")}
        with patch.object(pe, "AUDIT", self.exe):
            job = self.go(plays(files=files), coop)
            (label, command), = job.steps
            self.assertEqual((label, command), ("coop-a-griglia", [*powershell, str(ROOT / "tools/coop/run_all.ps1"), "-Only", "a", "-LogDir", str(self.job_folder)]))
            self.assertEqual(job.timeouts, {"coop-a-griglia": coop["timeout"]})
            self.assertEqual(job.report["player"]["checks"], 3)                                               # the checks of all the processes
            self.assertEqual([f["subject"] for f in job.report["findings"]], ["z", "x", "y"])                 # in the order of the names of the logs: c1, host
            self.assertEqual({Path(f["evidence"]).name for f in job.report["findings"]}, set(files))
            split = pe.PLAYER_ENTRIES["splitdesertotest"]                                                      # a switch is `-Name` alone
            job = self.go(plays(files={"splitdesert3.log": log_of("splitdesertotest", ok=["a"])}), split)
            self.assertEqual(job.steps[0][1][5:], [str(ROOT / "tools/coop/run_split.ps1"), "-Desert", "-LogDir", str(self.job_folder)])
            self.assertEqual(pe.runner_arguments({"Players": 3, "Graphics": False, "Only": "a,c"}), ["-Players", "3", "-Only", "a,c"])

    def test_a_script_that_fails_or_whose_processes_say_they_failed_or_raised_does_not_pass(self):
        coop = pe.PLAYER_ENTRIES["coop-a-griglia"]
        host = lambda *ok, **more: log_of("cooptest", ok=ok, coop="ospite", **more)
        with patch.object(pe, "AUDIT", self.exe):
            self.fails(plays(files={"a_host.txt": host("x", failed=["y"]), "a_c1.txt": log_of("cooptest", ok=["z"], coop="ospitato")}), "PLAYER_CHECK_FAILED", coop)
            self.fails(plays(files={"a_host.txt": host("x") + "ArgumentException: no\n"}), "PLAYER_EXCEPTION", coop)
            self.fails(plays(files={"a_host.txt": host("x")}, fail="STEP_FAILED: coop-a-griglia; exit 1; log"), "STEP_FAILED", coop)
            self.fails(plays(), "PLAYER_RESULT_MISSING", coop)                                                  # a script that exits 0 with no log at all
            self.go(plays(files={"a_host.txt": host("x"), "a_c1.txt": "[PryingEyes] killed on purpose\n"}), coop)   # a process killed on purpose says nothing
            self.fails(plays(files={"a_host.txt": host("x")}), "PLAYER_CHECKS_FEWER", {**coop, "checks": 2})
            pista = pe.PLAYER_ENTRIES["coop-n-pista"]                                                           # PistaCoopTest: no colon, the detail in parentheses
            log = "[PryingEyes] pistacooptest ospite ok la gara parte (3 giri)\n[PryingEyes] pistacooptest ospite RISULTATO SUPERATA: 1/1\n"
            self.assertEqual(self.go(plays(files={"n_host.txt": log}), pista).report["player"]["checks"], 1)

    def test_a_script_runs_the_verification_build_of_its_checkout_so_runtime_cannot_change_it(self):
        job, message = self.fails(plays(), "PLAYER_RUNTIME_FIXED", pe.PLAYER_ENTRIES["seatstest"])               # self.exe is not the verification build
        self.assertIn("run_split.ps1", message); self.assertEqual(job.steps, [])


if __name__ == "__main__": unittest.main()
