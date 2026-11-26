"""The manifest of the in-player suites (tools/player_tests.json) is kept true by this test.

It is hermetic: it reads the manifest, its schema, the game's C# and the co-op scripts, and runs nothing (no Unity, no player, no network).
Each finding starts with a code; the negative controls at the bottom break a copy of the manifest and look for the code.

  MANIFEST_SCHEMA         the file does not follow tools/player_tests.schema.json
  MANIFEST_DUPLICATE      two entries have the same name
  MANIFEST_SOURCE         `source` is not a C# file of the player
  MANIFEST_FLAG           a flag of `args` or of `photos` is neither Unity's own nor a literal in the code of the player
  MANIFEST_MODE           the game that the flags start is not the declared `mode` (Catacombe.Attive and the mode requests: F7 of the audit)
  MANIFEST_NEEDS          `needs` says gpu for a run that does not draw, or leaves it out for one that does (no -nographics)
  MANIFEST_LABEL          the label a test prints is not the one the entry gives it: `label` names a line the source does not print, repeats the
                          name, or is missing where the source prints another label (studio.py reads the lines of that label)
  MANIFEST_PAGE           `page` is not a page of the repository
  MANIFEST_TEST_UNLISTED  a test file that installs itself at load is neither the source of an entry nor excused under `unlisted`
  MANIFEST_UNLISTED       an `unlisted` entry that is not such a file, or that is also the source of an entry
  MANIFEST_RUNNER         a runner script that is missing or does not take a parameter that the entry gives it
  MANIFEST_SCENARIO       a scenario of tools/coop/run_all.ps1 that no entry runs, or an entry that does not match its scenario
"""
import copy
import json
import re
import sys
import unittest
from pathlib import Path

from jsonschema import Draft202012Validator

sys.path.insert(0, str(Path(__file__).resolve().parent))
import code_census

ROOT = Path(__file__).resolve().parents[1]
MANIFEST = ROOT / "tools/player_tests.json"
SCHEMA = ROOT / "tools/player_tests.schema.json"
GAME = code_census.GAME
COOP_ALL = "tools/coop/run_all.ps1"

UNITY_OWN = frozenset({"-batchmode", "-nographics", "-logFile", "-screen-fullscreen", "-screen-width", "-screen-height"})
FLAG_LITERAL = re.compile(r'"(-[A-Za-z][A-Za-z0-9]*)"')
FLAG_TOKEN = re.compile(r"-[A-Za-z][A-Za-z0-9-]*")
SCENARIO = re.compile(r"^\s{4}([a-z]_[a-z_]+)\s*=\s*@\{ Clients = \d+; H = @\(([^)]*)\); C = @\(([^)]*)\)", re.M)
SCRIPT_FLAG = re.compile(r"'(-[A-Za-z][A-Za-z0-9]*)'")
# the labels of the result lines that a source prints: in one literal, or as `"[PryingEyes] <label> " + res` with `res` holding "RISULTATO ..."
RESULT_LABEL = re.compile(r'\[PryingEyes\] (\w+) RISULTATO|"\[PryingEyes\] (\w+) " \+ (?:res|result)\b')
# a test on the harness prints its Suite: `protected override string Suite { get { return "traptest"; } }`, or `=> "traptest"`
SUITE_LABEL = re.compile(r'override\s+string\s+Suite\s*(?:=>\s*|\{\s*get\s*\{\s*return\s*)"(\w+)"')


class Game:
    """What the checks need to know of the game's code, read once: the flags the player reads, the test files that install themselves."""

    def __init__(self, root=ROOT):
        files, _ = code_census.read_sources(Path(root))
        player = {rel: info for rel, info in files.items() if not rel.startswith("Packages/") and not code_census.is_editor(rel)}
        self.files = {f"{GAME}/{rel}" for rel in player}
        self.flags = {flag for info in player.values() for flag in FLAG_LITERAL.findall(info["text"])}
        self.self_installing = {f"{GAME}/{rel}" for rel, info in player.items()
                                if code_census.is_test(rel) and "RuntimeInitializeOnLoadMethod" in info["code"]}


def flags_of(argv):
    """The flags of a command line: what starts with a dash and is not a number (`-floor -3` has one flag)."""
    return [token for token in argv if FLAG_TOKEN.fullmatch(token)]


def mode_of(argv):
    """The game that a command line starts. Catacombe.Attive is false in an automated run unless a flag asks for the catacombs or the
    cathedral; DesertMode.Requested and PistaMode.Requested read their own flags; without -autotest the game starts at the menu."""
    flags = set(argv)
    if flags & {"-deserto", "-desertotest"}: return "desert"
    if flags & {"-cattedrale", "-cattedraletest"}: return "cathedral"
    if "-catacombe" in flags: return "catacombs"
    if "-citytest" in flags: return "city"
    if flags & {"-pista", "-pistatest"}: return "track"
    if not flags & {"-autotest", "-guntest"}: return "menu"
    return "tower"


def parameters(script):
    """The names (lower case: PowerShell does not mind the case) of the parameters of a PowerShell script's param(...) block."""
    script = re.sub(r"(?m)#.*$", "", script)
    start = re.search(r"\bparam\s*\(", script, re.I)
    if not start: return set()
    depth, end = 1, start.end()
    while end < len(script) and depth:
        depth += (script[end] == "(") - (script[end] == ")")
        end += 1
    return {name.lower() for name in re.findall(r"\]\s*\$([A-Za-z]\w*)", script[start.end():end])}


def result_labels(text):
    """The labels that a C# source prints before `RISULTATO`: one that it writes itself, or the name that a PlayerTest gives as its Suite (the base prints it)."""
    return {first or second for first, second in RESULT_LABEL.findall(text)} | set(SUITE_LABEL.findall(text))


def scenarios(root=ROOT):
    """The scenarios of tools/coop/run_all.ps1: key -> the flags of the host and of the guests (the script owns the arguments)."""
    text = (Path(root) / COOP_ALL).read_text(encoding="utf-8-sig")
    return {key: (SCRIPT_FLAG.findall(host), SCRIPT_FLAG.findall(guest)) for key, host, guest in SCENARIO.findall(text)}


def check(manifest, game, root=ROOT):
    """Every way the manifest can be untrue, as "CODE subject: what" lines."""
    root = Path(root)
    findings = []

    def found(code, subject, text):
        findings.append(f"{code} {subject}: {text}")

    schema = json.loads(SCHEMA.read_text(encoding="utf-8-sig"))
    for error in Draft202012Validator(schema).iter_errors(manifest):
        found("MANIFEST_SCHEMA", "/".join(map(str, error.absolute_path)) or "(document)", error.message[:160])
    if findings:
        return findings                 # what follows reads fields that only a valid file is sure to have

    suites, names = manifest["suites"], [suite["name"] for suite in manifest["suites"]]
    for name in sorted({name for name in names if names.count(name) > 1}):
        found("MANIFEST_DUPLICATE", name, "more than one entry has this name")
    for suite in suites:
        name = suite["name"]
        if suite["source"] not in game.files or not (root / suite["source"]).is_file():
            found("MANIFEST_SOURCE", name, f"{suite['source']} is not a C# file of the player")
        if not (root / suite["page"]).is_file():
            found("MANIFEST_PAGE", name, f"{suite['page']} does not exist")
        argv = suite.get("args")
        for flag in flags_of((argv or []) + suite.get("photos", {}).get("args", [])):
            if flag not in UNITY_OWN and flag not in game.flags:
                found("MANIFEST_FLAG", name, f"{flag} is not a literal in the code of the player")
        if argv is not None:
            if mode_of(argv) != suite["mode"]:
                found("MANIFEST_MODE", name, f"these flags start the {mode_of(argv)}, the entry says {suite['mode']}")
            if ("-nographics" not in argv) != ("gpu" in suite["needs"]):
                found("MANIFEST_NEEDS", name, "gpu if and only if the run draws, that is, if -nographics is not in args")
        if argv is not None and (root / suite["source"]).is_file():
            printed, label = result_labels((root / suite["source"]).read_text(encoding="utf-8-sig")), suite.get("label")
            if label == name:
                found("MANIFEST_LABEL", name, "label is the name: leave it out")
            elif label is not None and label not in printed:
                found("MANIFEST_LABEL", name, f"{suite['source']} does not print `[PryingEyes] {label} RISULTATO`")
            elif label is None and len(printed) == 1 and name not in printed:
                found("MANIFEST_LABEL", name, f"{suite['source']} prints `[PryingEyes] {next(iter(printed))} RISULTATO`: say so in `label`")
        runner = suite.get("runner")
        if runner is not None:
            script = root / runner["script"]
            if not script.is_file():
                found("MANIFEST_RUNNER", name, f"{runner['script']} does not exist")
            elif script.suffix == ".ps1":
                known = parameters(script.read_text(encoding="utf-8-sig"))
                for parameter in runner["parameters"]:
                    if parameter.lower() not in known:
                        found("MANIFEST_RUNNER", name, f"{runner['script']} takes no parameter {parameter}")

    scripted = scenarios(root)
    by_letter, claimed = {key[0]: key for key in scripted}, {}
    for suite in suites:
        runner = suite.get("runner")
        if not runner or runner["script"] != COOP_ALL: continue
        key = by_letter.get(runner["parameters"].get("Only"))
        if key is None:
            found("MANIFEST_SCENARIO", suite["name"], f"{COOP_ALL} has no scenario {runner['parameters'].get('Only')!r}")
            continue
        claimed.setdefault(key, []).append(suite["name"])
        host, guest = scripted[key]
        if suite["name"] != "coop-" + key.replace("_", "-"):
            found("MANIFEST_SCENARIO", suite["name"], f"runs the scenario {key}: its name should be coop-{key.replace('_', '-')}")
        for flag in host + guest:
            if flag not in game.flags:
                found("MANIFEST_FLAG", suite["name"], f"{flag} (scenario {key}) is not a literal in the code of the player")
        if mode_of(host + ["-autotest"]) != suite["mode"]:
            found("MANIFEST_MODE", suite["name"], f"the scenario {key} starts the {mode_of(host + ['-autotest'])}, the entry says {suite['mode']}")
    for key in scripted:
        if len(claimed.get(key, [])) != 1:
            found("MANIFEST_SCENARIO", key, f"the scenario is run by {len(claimed.get(key, []))} entries instead of one")

    sources, excused = {suite["source"] for suite in suites}, {entry["file"] for entry in manifest["unlisted"]}
    for file in sorted(game.self_installing - sources - excused):
        found("MANIFEST_TEST_UNLISTED", file, "installs itself at load and is neither the source of an entry nor unlisted")
    for entry in manifest["unlisted"]:
        if entry["file"] not in game.self_installing:
            found("MANIFEST_UNLISTED", entry["file"], "is not a test file that installs itself")
        elif entry["file"] in sources:
            found("MANIFEST_UNLISTED", entry["file"], "is unlisted and also the source of an entry")
    return findings


def codes(findings):
    return {finding.split()[0] for finding in findings}


class PlayerManifest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.game = Game()
        cls.manifest = json.loads(MANIFEST.read_text(encoding="utf-8-sig"))

    def broken(self, change):
        """The findings for a copy of the manifest after `change(copy)`."""
        manifest = copy.deepcopy(self.manifest)
        change(manifest)
        return check(manifest, self.game)

    @staticmethod
    def entry(manifest, name):
        return next(suite for suite in manifest["suites"] if suite["name"] == name)

    def assertFound(self, code, findings, text=""):
        self.assertTrue(any(f.startswith(code) and text in f for f in findings), f"{code} {text!r} not in {findings}")

    # ---- the manifest as it is

    def test_the_manifest_is_true(self):
        findings = check(self.manifest, self.game)
        self.assertEqual([], findings, "\n" + "\n".join(findings))

    def test_the_schema_is_a_schema(self):
        Draft202012Validator.check_schema(json.loads(SCHEMA.read_text(encoding="utf-8-sig")))

    def test_the_census_counts_the_same_files_that_install_themselves(self):
        # the census gives the number and not the list: this test derives the list with the census's own functions and checks the number
        self.assertEqual(len(self.game.self_installing), code_census.census(ROOT)["tests"]["selfInstalling"])

    # ---- negative controls: a broken copy must be found, and by the right code

    def test_a_file_that_does_not_follow_the_schema_is_found(self):
        findings = self.broken(lambda m: self.entry(m, "traptest").pop("page"))
        self.assertEqual({"MANIFEST_SCHEMA"}, codes(findings))
        both = self.broken(lambda m: self.entry(m, "traptest").update(runner={"script": "tools/coop/run_split.ps1", "parameters": {}}))     # args and runner
        self.assertFound("MANIFEST_SCHEMA", both)
        self.assertFound("MANIFEST_SCHEMA", self.broken(lambda m: self.entry(m, "traptest").update(checks=0)))
        self.assertFound("MANIFEST_SCHEMA", self.broken(lambda m: self.entry(m, "traptest").update(mode="moon")))

    def test_a_name_used_twice_is_found(self):
        first = self.manifest["suites"][0]["name"]
        self.assertFound("MANIFEST_DUPLICATE", self.broken(lambda m: m["suites"][1].update(name=first)), first)

    def test_a_source_that_is_not_a_file_of_the_player_is_found(self):
        gone = f"{GAME}/Scripts/NoSuchTest.cs"
        self.assertFound("MANIFEST_SOURCE", self.broken(lambda m: self.entry(m, "traptest").update(source=gone)), "traptest")
        self.assertFound("MANIFEST_SOURCE", self.broken(lambda m: self.entry(m, "traptest").update(source=f"{GAME}/Editor/ProcCheck.cs")), "traptest")

    def test_a_flag_that_the_code_does_not_read_is_found(self):
        self.assertFound("MANIFEST_FLAG", self.broken(lambda m: self.entry(m, "traptest")["args"].append("-nosuchflag")), "-nosuchflag")
        self.assertFound("MANIFEST_FLAG", self.broken(lambda m: self.entry(m, "traptest")["photos"]["args"].append("-nosuchshots")), "-nosuchshots")

    def test_unitys_own_flags_and_values_are_not_flags_of_the_game(self):
        self.assertEqual([], flags_of(["1000", "-3", "unity_logs/x", "custode"]))
        self.assertEqual(["-screen-width", "-seed"], flags_of(["-screen-width", "1280", "-seed", "-5"]))
        self.assertEqual([], self.broken(lambda m: self.entry(m, "displaytest")["args"].extend(["-logFile", "x.log"])))

    def test_a_mode_that_the_flags_do_not_start_is_found(self):
        self.assertFound("MANIFEST_MODE", self.broken(lambda m: self.entry(m, "traptest").update(mode="desert")), "traptest")
        self.assertFound("MANIFEST_MODE", self.broken(lambda m: self.entry(m, "traptest")["args"].append("-catacombe")), "catacombs")
        self.assertFound("MANIFEST_MODE", self.broken(lambda m: self.entry(m, "desertotest").update(mode="tower")), "desertotest")

    def test_the_game_a_command_line_starts(self):
        for argv, mode in ((["-autotest"], "tower"), (["-autotest", "-catacombe"], "catacombs"), (["-autotest", "-cattedrale"], "cathedral"),
                           (["-autotest", "-cattedraletest"], "cathedral"), (["-autotest", "-desertotest"], "desert"), (["-autotest", "-deserto", "-catacombe"], "desert"),
                           (["-autotest", "-citytest"], "city"), (["-autotest", "-pistatest"], "track"), (["-batchmode", "-pistatest"], "track"),
                           (["-batchmode", "-padtest"], "menu"), (["-batchmode", "-guntest"], "tower"), ([], "menu")):
            self.assertEqual(mode, mode_of(argv), argv)

    def test_gpu_goes_with_a_run_that_draws(self):
        def draws(manifest):
            args = self.entry(manifest, "traptest")["args"]
            args.remove("-nographics")
        self.assertFound("MANIFEST_NEEDS", self.broken(draws), "traptest")
        self.assertFound("MANIFEST_NEEDS", self.broken(lambda m: self.entry(m, "traptest")["needs"].append("gpu")), "traptest")

    def test_the_label_that_a_test_prints_is_the_one_the_entry_gives(self):
        self.assertFound("MANIFEST_LABEL", self.broken(lambda m: self.entry(m, "pistamenulicence").pop("label")), "pistamenutest")   # the source prints another label
        self.assertFound("MANIFEST_LABEL", self.broken(lambda m: self.entry(m, "pistamenulicence").update(label="pistatest")), "does not print")
        self.assertFound("MANIFEST_LABEL", self.broken(lambda m: self.entry(m, "chartest").update(label="chartest")), "leave it out")
        self.assertFound("MANIFEST_SCHEMA", self.broken(lambda m: self.entry(m, "chartest").update(label="Char Test")))
        self.assertEqual({"pistatest", "pistamenutest"},
                         {suite["label"] for suite in self.manifest["suites"] if "label" in suite})
        self.assertEqual({"pistamenutest"}, result_labels('Debug.Log($"[PryingEyes] pistamenutest RISULTATO {x}"); Debug.Log("[PryingEyes] other " + res);') - {"other"})
        self.assertEqual({"magiatest"}, result_labels('Debug.Log("[PryingEyes] magiatest " + res);'))
        self.assertEqual({"traptest"}, result_labels("class TrapTest : PlayerTest { protected override string Suite => \"traptest\"; }"))
        self.assertEqual({"pistamenutest"}, result_labels("class PistaMenuTest : PlayerTest { protected override string Suite { get { return \"pistamenutest\"; } } }"))

    def test_a_page_that_does_not_exist_is_found(self):
        self.assertFound("MANIFEST_PAGE", self.broken(lambda m: self.entry(m, "traptest").update(page="docs/game/no-such-page.md")), "traptest")

    def test_a_test_that_installs_itself_must_be_listed_or_excused(self):
        trap = f"{GAME}/Scripts/TrapTest.cs"
        lost = self.broken(lambda m: m["suites"].remove(self.entry(m, "traptest")))
        self.assertFound("MANIFEST_TEST_UNLISTED", lost, "TrapTest.cs")
        excused = self.broken(lambda m: (m["suites"].remove(self.entry(m, "traptest")), m["unlisted"].append({"file": trap, "reason": "a reason"})))
        self.assertEqual([], excused)

    def test_an_excuse_must_still_apply(self):
        partial = f"{GAME}/Scripts/Stealth/StealthTest.Fase2.cs"                 # a test file that does not install itself
        self.assertFound("MANIFEST_UNLISTED", self.broken(lambda m: m["unlisted"].append({"file": partial, "reason": "a reason"})), "StealthTest.Fase2.cs")
        trap = f"{GAME}/Scripts/TrapTest.cs"                                      # excused and also a source
        self.assertFound("MANIFEST_UNLISTED", self.broken(lambda m: m["unlisted"].append({"file": trap, "reason": "a reason"})), "TrapTest.cs")

    def test_a_runner_must_exist_and_take_the_parameters_it_is_given(self):
        split = lambda m: self.entry(m, "seatstest")["runner"]
        self.assertFound("MANIFEST_RUNNER", self.broken(lambda m: split(m).update(script="tools/coop/no_such_script.ps1")), "does not exist")
        self.assertFound("MANIFEST_RUNNER", self.broken(lambda m: split(m)["parameters"].update(NoSuchParameter=True)), "NoSuchParameter")
        self.assertEqual([], self.broken(lambda m: split(m)["parameters"].update(players=2)))              # PowerShell ignores the case

    def test_the_parameters_of_a_param_block(self):
        script = "# comment (with parentheses\nparam(\n  [string]$Exe = (Join-Path $PSScriptRoot 'x'),\n  [ValidateRange(2, 4)][int]$Players = 3,   # (aside)\n  [switch]$Desert\n)\n$later = [int]$Other"
        self.assertEqual({"exe", "players", "desert"}, parameters(script))
        self.assertEqual({"only", "seed", "timeoutsec", "logdir", "port"}, parameters((ROOT / COOP_ALL).read_text(encoding="utf-8-sig")))
        self.assertEqual(set(), parameters("Write-Output 1"))

    def test_every_scenario_of_the_coop_script_has_exactly_one_entry(self):
        scripted = scenarios()
        self.assertGreaterEqual(len(scripted), 17)
        gone = self.broken(lambda m: m["suites"].remove(self.entry(m, "coop-a-griglia")))
        self.assertFound("MANIFEST_SCENARIO", gone, "a_griglia")
        twice = self.broken(lambda m: self.entry(m, "coop-b-ali")["runner"]["parameters"].update(Only="a"))
        self.assertFound("MANIFEST_SCENARIO", twice, "a_griglia")
        renamed = self.broken(lambda m: self.entry(m, "coop-a-griglia").update(name="coop-griglia"))
        self.assertFound("MANIFEST_SCENARIO", renamed, "coop-a-griglia")
        unknown = self.broken(lambda m: self.entry(m, "coop-a-griglia")["runner"]["parameters"].update(Only="z"))
        self.assertFound("MANIFEST_SCENARIO", unknown, "'z'")

    def test_a_scenario_that_starts_another_game_than_its_entry_says_is_found(self):
        self.assertFound("MANIFEST_MODE", self.broken(lambda m: self.entry(m, "coop-j-deserto").update(mode="tower")), "coop-j-deserto")
        self.assertFound("MANIFEST_MODE", self.broken(lambda m: self.entry(m, "coop-n-pista").update(mode="tower")), "coop-n-pista")


if __name__ == "__main__":
    unittest.main()
