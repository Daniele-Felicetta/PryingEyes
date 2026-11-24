"""Prying Eyes profile for the production studio engine (tools/studio_engine.py).

Everything that knows this game lives here: its content (weapons from Blender recipes, furnished rooms), the checks that
prove it (the suites that run inside the built player, .NET labs for the weapon and survival rules, the city recipe) and the
Unity/Blender pipeline that builds it. The engine supplies jobs, locking, source guards, the queue and the command line.
"""
from contextlib import contextmanager
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

import player_log
from asset_graph import AssetGraph, recipe_sources
from content_catalog import ROOT, CATALOG, SCHEMA, find_asset, read_catalog, validate_catalog
from room_catalog import CATALOG as ROOMS, SCHEMA as ROOM_SCHEMA, validate_rooms
from studio_engine import ContentType, Project, Suite

UNITY = ROOT / "unity/PryingEyes"
CITY = UNITY / "Assets/_PryingEyes/City/citta.json"
EXE = UNITY / "Builds/CityPreview/PryingEyes.exe"                   # where the studio publishes the city prototype
AUDIT = UNITY / "Builds/Audit/PryingEyes.exe"                       # the verification build: where agents build and run checks (AGENTS.md rule 11)
PLAN_FIXTURE = ROOT / "tools/fixtures/plan-fingerprint.json"
PLAN_UPDATE = "dotnet run --project tools/planlab -c Release -- fingerprint --update tools/fixtures/plan-fingerprint.json"
PLAYER_TESTS = ROOT / "tools/player_tests.json"                     # one entry for each suite that runs inside the player (tools/player_tests.schema.json)
SETTINGS_KEY = r"HKCU\Software\DefaultCompany\PryingEyes"           # where the player keeps its settings; the maintainer's game reads the same key
POWERSHELL = ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File"]


def executables():
    version = (UNITY / "ProjectSettings/ProjectVersion.txt").read_text().splitlines()[0].split(": ")[1]
    return (Path(os.environ.get("BLENDER_EXE") or shutil.which("blender") or "C:/Program Files/Blender Foundation/Blender 4.5/blender.exe"),
            Path(os.environ.get("UNITY_EDITOR_EXE") or f"C:/Program Files/Unity/Hub/Editor/{version}/Editor/Unity.exe"))


def processes_named(*names):
    """The running processes with these names, as Windows describes them: ProcessId, CommandLine, ExecutablePath. None elsewhere, where no Windows player runs."""
    if os.name != "nt": return []
    wanted = " OR ".join(f"name = '{name}'" for name in names)
    script = f"Get-CimInstance Win32_Process -Filter \"{wanted}\" | Select-Object ProcessId,CommandLine,ExecutablePath | ConvertTo-Json -Compress"
    result = subprocess.run(["powershell", "-NoProfile", "-Command", script], capture_output=True, text=True, check=True)
    processes = json.loads(result.stdout or "[]")
    return processes if isinstance(processes, list) else [processes]


def check_editor_closed():
    for process in processes_named("Unity.exe", "PryingEyes.exe"):
        command = (process.get("CommandLine") or "").replace("\\", "/").lower()
        path = (process.get("ExecutablePath") or "").replace("\\", "/").lower()
        if ("unity.exe" in path and str(UNITY).replace("\\", "/").lower() in command) or path == EXE.as_posix().lower():
            raise RuntimeError(f"PROJECT_IN_USE: process {process['ProcessId']}; close it before building")


# ---- the suites that run inside the built player: one for each entry of tools/player_tests.json ----------------------------------------

def player_entries(path=PLAYER_TESTS):
    """The in-player suites by name, as the manifest lists them (tools/test_player_tests.py keeps the file true)."""
    return {entry["name"]: entry for entry in json.loads(Path(path).read_text(encoding="utf-8-sig"))["suites"]}


PLAYER_ENTRIES = player_entries()


def shown(path):
    """A path as the messages name it: relative to the repository when it is inside."""
    path = Path(path)
    return path.relative_to(ROOT).as_posix() if path.is_relative_to(ROOT) else str(path)


def reg(*arguments):
    """The Windows `reg` tool, as a function so that the tests can stand in for it."""
    return subprocess.run(["reg", *map(str, arguments)], capture_output=True, text=True)


def refuse_busy_player(entry, runtime):
    """Two copies of one build fight over their log and their files, and a run that draws is wrong while somebody plays: refuse before starting."""
    running = processes_named("PryingEyes.exe")
    path = lambda process: (process.get("ExecutablePath") or "").replace("\\", "/").casefold()
    same = [process for process in running if path(process) == Path(runtime).resolve().as_posix().casefold()]
    if same:
        raise RuntimeError(f"PLAYER_IN_USE: {runtime} is already running (process {same[0]['ProcessId']}); wait for it to close, or test another build")
    if "gpu" in entry["needs"] and running:
        raise RuntimeError(f"PLAYER_GPU_BUSY: {entry['name']} draws and another PryingEyes.exe is running (process {running[0]['ProcessId']}, "
                           f"{running[0].get('ExecutablePath')}); its result is wrong while somebody plays: run it when no player is open")


@contextmanager
def settings_kept(job, hazards):
    """hazards: registry. The player writes the settings key that the maintainer's game reads, so the key is exported before the run and imported
    after it, whatever happens in between: what tools/coop/run_split.ps1 does for a run with a window. A key that is not there yet is left as
    the run makes it, as that script does. Yields {"restored": bool}; the way it went is a finding of the report."""
    state = {"restored": True}
    if "registry" not in hazards:
        yield state
        return
    backup = job.folder / "settings-before.reg"
    saved = reg("export", SETTINGS_KEY, backup, "/y").returncode == 0
    try:
        yield state
    finally:
        if saved:
            state["restored"] = reg("import", backup).returncode == 0
            job.report["artifacts"].append(str(backup))
        job.report["findings"].append({"code": "SETTINGS_RESTORED" if saved else "SETTINGS_KEY_ABSENT", "subject": SETTINGS_KEY, "passed": state["restored"],
                                       "observed": ("imported again from " if state["restored"] else "could not be imported again from ") + backup.name if saved
                                                   else "no key before the run: there was nothing to put back",
                                       "expected": "the settings as they were before the run", "evidence": str(backup)})
        job.save()


def runner_arguments(parameters):
    """`-Name value` for each parameter that the manifest gives a script; a switch is `-Name` alone, and false leaves it out."""
    arguments = []
    for name, value in parameters.items():
        if value is True: arguments.append(f"-{name}")
        elif value is not False: arguments += [f"-{name}", str(value)]
    return arguments


def player_state(job, entry, runtime):
    """Runs one in-player suite of the manifest and says what happened.

    The player is started with the arguments of the entry and, after them, where to put its log (-logFile), its telemetry (-report) and the
    result.json of the harness (-testout), all in the job's folder; an entry with a `runner` is started through its script, with the job's folder
    as the log folder (the script runs the build of this checkout, so --runtime cannot change it). Each check that the run reports is a
    finding, and report["player"] holds the verdict, where it came from (`result.json`, or `log-line` for a suite that does not write one
    yet) and how many checks ran.

    The suite passes when the process quit with 0 within the entry's timeout, the run says it passed, it ran at least the entry's `checks`
    and its log holds no exception but the renderer's noise. Each way to fail has its own code: PLAYER_RUNTIME_MISSING, PLAYER_RUNTIME_FIXED,
    PLAYER_IN_USE, PLAYER_GPU_BUSY, PLAYER_TIMEOUT, PLAYER_RESULT_INVALID, PLAYER_RESULT_MISSING, PLAYER_CHECK_FAILED, PLAYER_EXCEPTION,
    PLAYER_CHECKS_FEWER, and STEP_FAILED for a process that failed after saying that it passed."""
    name, runner, runtime = entry["name"], entry.get("runner"), Path(runtime)
    if not runtime.is_file():
        raise RuntimeError(f"PLAYER_RUNTIME_MISSING: {runtime}; build it (tools/ship.ps1 builds {shown(AUDIT)}) or name another build with --runtime")
    if runner and runtime.resolve() != AUDIT.resolve():
        raise RuntimeError(f"PLAYER_RUNTIME_FIXED: {runner['script']} always runs {shown(AUDIT)}; --runtime cannot change it")
    refuse_busy_player(entry, runtime)
    testout, player = job.folder / "testout", job.folder / f"{name}-player.log"
    if runner:
        command = [*POWERSHELL, ROOT / runner["script"], *runner_arguments(runner["parameters"]), "-LogDir", job.folder]
    else:
        testout.mkdir(exist_ok=True)
        command = [runtime, *entry["args"], "-logFile", player, "-report", job.folder / f"{name}-telemetry.txt", "-testout", testout]
    error = None
    with settings_kept(job, entry["hazards"]) as settings:
        try:
            job.run(name, command, timeout=entry["timeout"])
        except RuntimeError as failure:
            if not str(failure).startswith(("STEP_FAILED", "STEP_TIMEOUT")): raise
            error = failure                 # what the run left behind is read first: it says more than an exit code
    # the logs that say what happened: the player's own, or all of those that a script left in the folder (not its own output, <name>.log)
    logs = sorted(path for path in job.folder.iterdir() if path.suffix in (".log", ".txt") and path.stem != name) if runner else [player]
    result = testout / "result.json"
    job.report["artifacts"].extend(str(path) for path in [*logs, result] if path.is_file())
    try:
        parts = [(path, player_log.verdict(None if runner else name, player_log.read_text(path), None if runner else result)) for path in logs]
    except ValueError as invalid:
        raise RuntimeError(f"PLAYER_RESULT_INVALID: {name}: {invalid}") from invalid
    said = player_log.combine(part for _, part in parts)
    for number, (path, check) in enumerate(((path, check) for path, part in parts for check in part["checks"]), 1):
        job.report["findings"].append({"code": f"{name}:{number}", "subject": check["name"], "observed": check["detail"], "expected": "", "passed": check["ok"],
                                       "evidence": str(result if said["source"] == "result.json" else path)})
    job.report["player"] = {"suite": name, "verdictSource": said["source"], "checks": said["total"], "passed": said["passed"], "declared": entry.get("checks"),
                            "exceptions": len(said["exceptions"]), "logs": [str(path) for path in logs]}
    job.save()
    where = ", ".join(path.name for path in logs) or "no log"
    if error is not None and str(error).startswith("STEP_TIMEOUT"):
        raise RuntimeError(f"PLAYER_TIMEOUT: {name} gave no result within {entry['timeout']} s ({where})") from error
    if said["source"] == "none":
        raise RuntimeError(f"PLAYER_RESULT_MISSING: {name} wrote no result.json and no result line ({where}); {error or 'the process quit with 0'}")
    if not said["ok"]:
        failed = [check["name"] for check in said["checks"] if not check["ok"]]
        raise RuntimeError(f"PLAYER_CHECK_FAILED: {name}: {said['total'] - said['passed']} of {said['total']} checks failed"
                           + (f" ({'; '.join(failed[:4])}{'; ...' if len(failed) > 4 else ''})" if failed else f" (the {said['source']} says it failed)"))
    if said["exceptions"]:
        raise RuntimeError(f"PLAYER_EXCEPTION: {name}: {len(said['exceptions'])} exception(s) in the log, the first: {said['exceptions'][0][:200]}")
    if entry.get("checks") and said["total"] < entry["checks"]:
        raise RuntimeError(f"PLAYER_CHECKS_FEWER: {name} ran {said['total']} checks and tools/player_tests.json says {entry['checks']}: "
                           f"part of the test did not run, or the manifest is out of date")
    if error is not None:
        raise error
    if not settings["restored"]:
        raise RuntimeError(f"PLAYER_SETTINGS_NOT_RESTORED: {name} passed, but {SETTINGS_KEY} could not be put back from settings-before.reg")


def player_suite(entry):
    """The studio suite of one manifest entry: it needs and can disturb what the entry says, and runs the verification build unless --runtime names another."""
    return Suite(lambda ctx, runtime: player_state(ctx.job, entry, runtime or AUDIT), runtime=True, kind="player",
                 needs=tuple(entry["needs"]), hazards=tuple(entry["hazards"]),
                 about=f"In the player, {entry['mode']} game: what it proves is in {entry['page']}")


def native_gunplay(job, executable=AUDIT):
    """The gunplay check of a build is the manifest's `gunplaytest` suite: after_build runs it against the build that it has just made."""
    player_state(job, PLAYER_ENTRIES["gunplaytest"], executable)


def preview_asset(job, blender, identity):
    job.run("preview", [blender, "-b", "-P", "genera_armi.py", "--", "render", identity, "rapido", "lato", "tre_quarti", "giocatore", "ads"], ROOT / "modelli/armi", 600)
    for image in (ROOT / "modelli/armi/render").glob(identity + "*.png"):
        target = job.folder / image.name; shutil.copy2(image, target); job.report["artifacts"].append(str(target))


def verify_room_materials(job, unity):
    job.run("room-materials", [unity, "-batchmode", "-projectPath", UNITY, "-executeMethod",
        "PryingEyes.EditorTools.CityRoomBuilder.VerifyImportedMaterials", "-quit", "-logFile", job.folder / "room-materials-unity.log"], timeout=180)
    log = job.folder / "room-materials-unity.log"
    if not log.is_file() or "MATERIAL_IMPORT_PASS" not in log.read_text(encoding="utf-8-sig"):
        raise RuntimeError("ROOM_MATERIAL_IMPORT_RESULT_MISSING")
    job.report["artifacts"].append(str(log))


def native_city(job, executable):
    runtime = job.folder / "city-runtime/run.json"
    job.report["artifacts"].append(str(runtime)); job.save()
    try:
        # citylab.py bounds the player run itself (120 s plus 80 s per bound room); this is only the outer backstop.
        job.run("city", [sys.executable, ROOT / "tools/citylab.py", "--require-assets", "--runtime", executable, "--report", job.folder / "city.json"], timeout=900)
    finally:
        if runtime.is_file():
            result = json.loads(runtime.read_text(encoding="utf-8"))
            job.report["findings"].extend({**finding, "evidence": str(runtime)} for finding in result.get("native", {}).get("findings", []))
            job.save()


def weapon_state(job):
    report = job.folder / "weapon-state.json"
    result = None
    job.report["artifacts"].append(str(report)); job.save()
    try:
        job.run("weapon-state", ["dotnet", "run", "-c", "Release", "--project", ROOT / "tools/weaponslab", "--", CATALOG, report], timeout=180)
    finally:
        if report.is_file():
            result = json.loads(report.read_text())
            job.report["findings"].extend({**finding, "evidence": str(report)} for finding in result["findings"])
            job.save()
    if result is None or not result["ok"]: raise RuntimeError("WEAPON_STATE_FAILED_OR_REPORT_MISSING")


def survival_state(job):
    report = job.folder / "survival-state.json"
    job.report["artifacts"].append(str(report)); job.save()
    try:
        job.run("survival-state", ["dotnet", "run", "-c", "Release", "--project", ROOT / "tools/survivallab", "--", report], timeout=180)
    finally:
        if report.is_file():
            result = json.loads(report.read_text(encoding="utf-8"))
            job.report["findings"].extend({**finding, "evidence": str(report)} for finding in result["findings"]); job.save()
    if not report.is_file() or not result["ok"]: raise RuntimeError("SURVIVAL_STATE_FAILED_OR_REPORT_MISSING")


def economy_state(job):
    """The economy rules lab: the reusable ledger/construction/market/loot/expedition package and the game's cathedral catalog."""
    report = job.folder / "economy-state.json"
    job.report["artifacts"].append(str(report)); job.save()
    result = None
    try:
        job.run("economy-state", ["dotnet", "run", "-c", "Release", "--project", ROOT / "tools/economylab", "--", report], timeout=180)
    finally:
        if report.is_file():
            result = json.loads(report.read_text(encoding="utf-8"))
            job.report["findings"].extend({**finding, "evidence": str(report)} for finding in result["findings"]); job.save()
    if result is None or not result["ok"]: raise RuntimeError("ECONOMY_STATE_FAILED_OR_REPORT_MISSING")


def saves_state(job):
    """The save rules lab: the reusable JSON/format/migration/store package and the game's character schema (record, levels, old-file import)."""
    report = job.folder / "saves-state.json"
    job.report["artifacts"].append(str(report)); job.save()
    result = None
    try:
        job.run("saves-state", ["dotnet", "run", "-c", "Release", "--project", ROOT / "tools/saveslab", "--", report], timeout=180)
    finally:
        if report.is_file():
            result = json.loads(report.read_text(encoding="utf-8"))
            job.report["findings"].extend({**finding, "evidence": str(report)} for finding in result["findings"]); job.save()
    if result is None or not result["ok"]: raise RuntimeError("SAVES_STATE_FAILED_OR_REPORT_MISSING")


def track_state(job):
    """The track rules lab: the motorcycle physics, the circuit generator, the rider AI and the race rules (the cooperative race included) of the track mode, compiled from the game's own files."""
    report = job.folder / "track-state.json"
    job.report["artifacts"].append(str(report)); job.save()
    result = None
    try:
        job.run("track-state", ["dotnet", "run", "-c", "Release", "--project", ROOT / "tools/tracklab", "--", report], timeout=300)
    finally:
        if report.is_file():
            result = json.loads(report.read_text(encoding="utf-8"))
            job.report["findings"].extend({**finding, "evidence": str(report)} for finding in result["findings"]); job.save()
    if result is None or not result["ok"]: raise RuntimeError("TRACK_STATE_FAILED_OR_REPORT_MISSING")


def desert_state(job):
    """The desert rules lab: the ground, the regions and the sites, the clock and the weather of the desert mode, and the building and stack generators, compiled from the game's own files."""
    report = job.folder / "desert-state.json"
    job.report["artifacts"].append(str(report)); job.save()
    result = None
    try:
        job.run("desert-state", ["dotnet", "run", "-c", "Release", "--project", ROOT / "tools/desertlab", "--", "--report", report], timeout=180)
    finally:
        if report.is_file():
            result = json.loads(report.read_text(encoding="utf-8"))
            job.report["findings"].extend({**finding, "evidence": str(report)} for finding in result["findings"]); job.save()
    if result is None or not result["ok"]: raise RuntimeError("DESERT_STATE_FAILED_OR_REPORT_MISSING")


def plan_findings(computed, fixture):
    """The findings of the floor generator's fingerprint: one per plan, the hash computed now (observed) against the one in the fixture (expected), and one for
    CoopProtocol.Revision, the one read from CoopWire.cs now (observed) against the one the fixture was made at (expected)."""
    wanted = {(p["layout"], p["seed"], p["floor"]): p["sha256"] for p in fixture["plans"]}
    name = lambda key: f"{key[0]} seed {key[1]} floor {key[2]}"
    findings = [{"code": "PLAN_FINGERPRINT", "subject": name(key), "passed": plan["sha256"] == wanted.get(key), "observed": plan["sha256"], "expected": wanted.get(key, "")}
                for plan in computed["plans"] for key in [(plan["layout"], plan["seed"], plan["floor"])]]
    made = {(p["layout"], p["seed"], p["floor"]) for p in computed["plans"]}
    findings.extend({"code": "PLAN_FINGERPRINT", "subject": name(key), "passed": False, "observed": "", "expected": digest} for key, digest in wanted.items() if key not in made)
    findings.append({"code": "PLAN_REVISION", "subject": "CoopProtocol.Revision", "passed": computed["revision"] == fixture["revision"],
                     "observed": computed["revision"], "expected": fixture["revision"]})
    return findings


def plan_state(job, fixture=PLAN_FIXTURE):
    """The fingerprint of the floor generator: the plan lab hashes what `dump` writes for 48 plans (the game's layout and the lab's default) and the hashes are
    compared with the committed fixture. In co-op every player builds the floor from the seed, so a change that moves a hash must raise CoopProtocol.Revision."""
    if not fixture.is_file(): raise RuntimeError(f"PLAN_FIXTURE_MISSING: {fixture}; make it with `{PLAN_UPDATE}`")
    report = job.folder / "plan-state.json"
    job.report["artifacts"].append(str(report)); job.save()
    findings = []
    try:
        job.run("plan-state", ["dotnet", "run", "-c", "Release", "--project", ROOT / "tools/planlab", "--", "fingerprint", "--report", report], timeout=180)
    finally:
        if report.is_file():
            findings = plan_findings(json.loads(report.read_text(encoding="utf-8")), json.loads(fixture.read_text(encoding="utf-8")))
            job.report["findings"].extend({**finding, "evidence": str(report)} for finding in findings); job.save()
    if not findings: raise RuntimeError("PLAN_STATE_FAILED_OR_REPORT_MISSING")
    plans = [f for f in findings if f["code"] == "PLAN_FINGERPRINT"]
    changed = [f["subject"] for f in plans if not f["passed"]]
    if changed:
        raise RuntimeError(f"PLAN_FINGERPRINT_CHANGED: {len(changed)} of {len(plans)} plans differ from {fixture.name} ({'; '.join(changed[:6])}{'; ...' if len(changed) > 6 else ''}). "
                           f"If the generator is meant to change, raise CoopProtocol.Revision in CoopWire.cs and run `{PLAN_UPDATE}` (docs/engineering/testing.md)")
    if not findings[-1]["passed"]:
        raise RuntimeError(f"PLAN_REVISION_STALE: CoopWire.cs says {findings[-1]['observed']} and {fixture.name} was made at {findings[-1]['expected']}; the plans are the same: run `{PLAN_UPDATE}`")


def unity_build(ctx, method, asset_args):
    """The editor step both content types share: build the player for one entry into ctx.build_dir."""
    ctx.job.run("unity", [ctx.tools["unity"], "-batchmode", "-projectPath", UNITY, "-executeMethod", method, *asset_args,
                          "-buildOut", ctx.build_dir, "-quit", "-logFile", ctx.job.folder / "unity.log"], timeout=1200)
    return ctx.build_dir / "PryingEyes.exe"


class Weapons(ContentType):
    """Weapons: one catalog entry holds the gameplay definition and the Blender recipe that models it."""
    name = "weapon"

    def load(self, strict=True): return validate_catalog() if strict else read_catalog()
    def entries(self, data): return data["weapons"]
    def entry_id(self, entry): return entry["definition"]["id"]
    def summary(self, entry): d = entry["definition"]; return f"{entry['index']:2}  {d['id']:22} {d['name']}"
    def files(self): return [CATALOG, SCHEMA, ROOT / "tools/fixtures/weapon-prefix-v1.json", ROOT / "tools/content_catalog.py", *(ROOT / "modelli/armi").glob("*.py")]
    def entry_files(self, entry):
        source = entry["source"]
        return [ROOT / p for p in [source["generator"], source["recipe"]["file"], *source["dependencies"]]]

    def preview(self, ctx, entry): preview_asset(ctx.job, ctx.tools["blender"], self.entry_id(entry))

    def build(self, ctx, entry):
        job, blender, identity = ctx.job, ctx.tools["blender"], self.entry_id(entry)
        identities = [identity]
        # the city shops sell weapons whose family models must exist before the player is built
        for shop in json.loads(CITY.read_text())["shopWeapons"]:
            if find_asset(shop, ctx.data)["source"].get("family") and not (ROOT / f"modelli/armi/export/{shop}.fbx").is_file() and shop not in identities:
                identities.append(shop)
        for name in identities:
            job.run("bake-" + name, [blender, "-b", "-P", "genera_armi.py", "--", "bake", name], ROOT / "modelli/armi", 1800)
            job.run("pack-" + name, [sys.executable, "pack_tex.py", name], ROOT / "modelli/armi")
        preview_asset(job, blender, identity)
        return unity_build(ctx, "PryingEyes.EditorTools.ProductionCatalog.BuildAsset", ["-asset", identity])


class Rooms(ContentType):
    """Furnished rooms: a recipe of parts and materials planned by the layout package and built by the editor."""
    name = "room"

    def load(self, strict=True): return validate_rooms()
    def entries(self, data): return data["rooms"]
    def entry_id(self, entry): return entry["id"]
    def summary(self, room): return f"{room['id']:28} {room['name']} ({room['width']} x {room['depth']} m)"
    def files(self): return [ROOMS, ROOM_SCHEMA, ROOT / "tools/room_catalog.py"]

    def preview(self, ctx, entry):
        job, identity = ctx.job, entry["id"]
        check_editor_closed()
        job.run("room-preview", [ctx.tools["unity"], "-batchmode", "-projectPath", UNITY, "-executeMethod", "PryingEyes.EditorTools.CityRoomBuilder.Preview",
                                 "-room", identity, "-roomout", job.folder, "-quit", "-logFile", job.folder / "unity.log"], timeout=300)
        verify_room_materials(job, ctx.tools["unity"])
        job.report["artifacts"].extend(str(p) for p in [job.folder / (identity + ".png"), job.folder / "plan.json"])

    def build(self, ctx, entry):
        job, identity = ctx.job, entry["id"]
        if not any(b["asset"] == identity for b in json.loads(CITY.read_text()).get("rooms", [])):
            raise RuntimeError("ROOM_NOT_IN_CITY: bind the room recipe to a city block before integration testing")
        built = unity_build(ctx, "PryingEyes.EditorTools.CityRoomBuilder.BuildAsset", ["-room", identity, "-roomout", job.folder])
        verify_room_materials(job, ctx.tools["unity"])
        job.report["artifacts"].extend(str(p) for p in [job.folder / (identity + ".png"), job.folder / "plan.json"])
        return built


def production_graph(weapons=None, rooms=None):
    """What depends on what: weapons on their recipes and materials, rooms on their parts, the district on everything it uses."""
    weapons = weapons or read_catalog(); rooms = rooms or validate_rooms()
    rel = lambda p: str(p.relative_to(ROOT)).replace("\\", "/")
    nodes = [{"id": "weapon_material_library", "type": "material-library", "name": "Materiali delle armi",
              "sources": [{"path": "modelli/armi/mats.py", "reason": "shared material generator"}], "dependsOn": []}]
    for entry in weapons["weapons"]:
        d, source = entry["definition"], entry["source"]
        sources = [{"path": rel(CATALOG), "pointer": "/weapons/" + str(entry["index"]), "reason": "catalog definition and recipe parameters"},
                   {"path": rel(SCHEMA), "reason": "catalog contract"}, {"path": source["generator"], "reason": "generator dispatcher"},
                   *recipe_sources(source["recipe"]["file"], ROOT), *({"path": p, "reason": "declared production dependency"} for p in source["dependencies"])]
        nodes.append({"id": d["id"], "name": d["name"], "type": "weapon", "sources": sources,
                      "dependsOn": ["weapon_material_library"], "buildCommand": ["python", "tools/studio.py", "asset", "build", d["id"]]})
    for index, material in enumerate(rooms["materials"]):
        nodes.append({"id": "room_material_" + material["id"], "name": material["id"], "type": "room-material", "dependsOn": [],
            "sources": [{"path": rel(ROOMS), "pointer": "/materials/" + str(index), "reason": "room palette"}]})
    for index, room in enumerate(rooms["rooms"]):
        materials = {p["material"] for p in room["parts"]} | {"concrete"}
        nodes.append({"id": room["id"], "name": room["name"], "type": "room", "dependsOn": sorted("room_material_" + m for m in materials),
            "sources": [{"path": rel(ROOMS), "pointer": "/rooms/" + str(index), "reason": "room recipe"},
                {"path": rel(ROOM_SCHEMA), "reason": "room contract"},
                *({"path": p, "reason": "room planner/builder"} for p in ["unity/PryingEyes/Packages/com.pryingeyes.layout/Runtime/RoomPlan.cs",
                    "unity/PryingEyes/Assets/_PryingEyes/Editor/CityRoomBuilder.cs"])],
            "buildCommand": ["python", "tools/studio.py", "asset", "build", room["id"]]})
    city = json.loads(CITY.read_text(encoding="utf-8-sig"))
    nodes.append({"id": "city_survival", "name": "Progressione delle ondate urbane", "type": "rules", "dependsOn": [],
        "sources": [{"path": rel(CITY), "pointer": "/survival", "reason": "city balance profile"},
            {"path": "unity/PryingEyes/Assets/_PryingEyes/Scripts/CityWaves.cs", "reason": "city wave integration"},
            {"path": "unity/PryingEyes/Packages/com.pryingeyes.survival/Runtime/WaveSchedule.cs", "reason": "reusable wave scheduler"}]})
    nodes.append({"id": "city_access", "name": "Accessi urbani a pagamento", "type": "rules", "dependsOn": [],
        "sources": [{"path": rel(CITY), "pointer": "/areas", "reason": "city access prices and identities"},
            {"path": "unity/PryingEyes/Assets/_PryingEyes/Scripts/CityAccess.cs", "reason": "city physical access integration"},
            {"path": "unity/PryingEyes/Packages/com.pryingeyes.survival/Runtime/AreaAccess.cs", "reason": "reusable access state"}]})
    nodes.append({"id": "district_station", "name": city["district"], "type": "level", "sources": [{"path": rel(CITY), "reason": "district recipe"}],
        "dependsOn": sorted(set(city["shopWeapons"]) | {b["asset"] for b in city.get("rooms", [])} | {"city_survival", "city_access"})})
    return AssetGraph(nodes, ROOT)


class PryingEyes(Project):
    name = "prying-eyes"
    description = "Prying Eyes production studio: weapons and furnished rooms from recipe to verified native build."
    root = ROOT
    entry_script = ROOT / "tools/studio.py"
    default_output = ROOT / "unity_logs/studio"
    default_subject = "weapons"
    content_types = (Weapons(), Rooms())
    # The registry of what can be run: `python tools/studio.py suites` prints it. A suite that needs nothing (no editor, no built player) is a
    # check of the CI gate, which runs every one of them by asking the studio: a new lab is one entry here and its project under tools/.
    # The suites that run inside the built player are not written here: each entry of tools/player_tests.json is one (`gunplaytest` among them).
    suites = {"weapon-state": Suite(lambda ctx, runtime: weapon_state(ctx.job), kind="lab", seconds=3,
                                    about="The rules of a weapon (ammunition, reload, cadence) against the frozen reload algorithm, for every weapon"),
              "survival": Suite(lambda ctx, runtime: survival_state(ctx.job), kind="lab", seconds=3,
                                about="The waves of the wave cycle: budget of enemies, pace of the spawns, limit of the living"),
              "economy": Suite(lambda ctx, runtime: economy_state(ctx.job), kind="lab", seconds=3,
                               about="The cathedral's money: ledger, constructions, market exchanges, loot and what you keep"),
              "saves": Suite(lambda ctx, runtime: saves_state(ctx.job), kind="lab", seconds=3,
                             about="Save documents, their migrations, and the character and run files"),
              "track": Suite(lambda ctx, runtime: track_state(ctx.job), kind="lab", seconds=70,
                             about="The track mode: bikes, circuits, races, replays, licences, championships and career"),
              "desert": Suite(lambda ctx, runtime: desert_state(ctx.job), kind="lab", seconds=20,
                              about="The desert's pure code: noise, clock, terrain, buildings, world, map, weather and grading"),
              "plan": Suite(lambda ctx, runtime: plan_state(ctx.job), kind="lab", seconds=3,
                            about="The floor generator: its plans against the committed hashes, and the protocol revision"),
              **{name: player_suite(entry) for name, entry in PLAYER_ENTRIES.items()}}
    runtime = AUDIT                 # what `test --runtime` defaults to: the verification build, not what the studio publishes (publish_dir)
    publish_dir = EXE.parent
    published_artifact = EXE
    graph_scope = "declared dependencies and local model-recipe imports; not the complete Unity graph"

    def tools(self):
        blender, unity = executables()
        return {"blender": blender, "unity": unity}

    def lock_resource(self): return UNITY       # one editor per Unity project: jobs on it run one at a time
    def ensure_free(self): check_editor_closed()

    def tool_sources(self, command):
        files = {CITY, PLAN_FIXTURE, PLAYER_TESTS, ROOT / "tools/studio.py", ROOT / "tools/studio_pryingeyes.py", ROOT / "tools/player_log.py", ROOT / "tools/citylab.py"}
        files.update((ROOT / "tools/coop").glob("*.ps1"))        # the scripts that start the suites made of several processes
        assets = UNITY / "Assets/_PryingEyes"
        files.update(assets.rglob("*.cs"))
        for extension in ("*.shader", "*.hlsl"): files.update(assets.rglob(extension))
        files.update(UNITY / "Packages" / name for name in ("manifest.json", "packages-lock.json"))
        for package in (UNITY / "Packages").glob("com.pryingeyes.*"):
            files.update(package.rglob("*.cs")); files.update(package.rglob("*.asmdef")); files.add(package / "package.json")
        for folder in (ROOT / "tools").glob("*lab"):            # every .NET lab: a new one needs no line here
            if folder.is_dir(): files.update(folder.glob("*.cs*"))
        if command in ("catalog", "gallery"): files.add(ROOT / "tools/asset_graph.py")
        if command == "gallery":
            files.add(ROOT / "tools/studio_gallery.py"); files.update(p for p in (ROOT / "tools/studio_web").glob("*") if p.is_file())
        return files

    def doctor(self, catalogs):
        tools = self.tools()
        if not tools["blender"].is_file() or not tools["unity"].is_file() or not shutil.which("dotnet"):
            raise RuntimeError("TOOL_MISSING: configure Blender, Unity and dotnet")
        return f"{len(catalogs['weapon']['weapons'])} valid weapon entries; Blender, Unity and dotnet available"

    def before_build(self, ctx):
        super().before_build(ctx)
        weapon_state(ctx.job); survival_state(ctx.job); economy_state(ctx.job); saves_state(ctx.job); track_state(ctx.job)

    def after_build(self, ctx, built):
        native_gunplay(ctx.job, built); native_city(ctx.job, built)

    def graph(self, catalogs): return production_graph(catalogs["weapon"], catalogs["room"])

    def evidence_stores(self):
        logs = ROOT / "unity_logs"
        return [p for p in logs.iterdir() if p.is_dir()] if logs.is_dir() else []

    def gallery(self, target, catalogs, expected, stores):
        from studio_gallery import JOB_ID, bundle
        assets = []
        for entry in catalogs["weapon"]["weapons"]:
            d = entry["definition"]
            assets.append({"id": d["id"], "name": d["name"], "type": "weapon", "category": d["cls"],
                "family": entry["source"].get("family", ""), "tags": [*d["modes"], d["style"]],
                "stats": [{"label": "Danno", "value": str(d["damage"])}, {"label": "Colpi/min", "value": str(d["rpm"])},
                          {"label": "Caricatore", "value": str(d["mag"])}, {"label": "Riserva", "value": str(d["reserveMax"])}]})
        for room in catalogs["room"]["rooms"]:
            assets.append({"id": room["id"], "name": room["name"], "type": "room", "category": "Ambiente", "family": "",
                "tags": [], "stats": [{"label": "Dimensioni", "value": f"{room['width']} × {room['depth']} m"},
                    {"label": "Elementi", "value": str(len(room["parts"]))}, {"label": "Uscite", "value": str(len(room["doors"]))}]})
        reports = [p for store in stores for p in Path(store).glob("*/report.json") if JOB_ID.fullmatch(p.parent.name)]
        return bundle(assets, self.graph(catalogs), reports, target, root=ROOT, expected_sources=expected, loose=ROOT / "modelli/armi/render")


PRYING_EYES = PryingEyes()
