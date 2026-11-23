"""Prying Eyes profile for the production studio engine (tools/studio_engine.py).

Everything that knows this game lives here: its content (weapons from Blender recipes, furnished rooms), the checks that
prove it (gunplay in the native build, .NET labs for the weapon and survival rules, the city recipe) and the Unity/Blender
pipeline that builds it. The engine supplies jobs, locking, source guards, the queue and the command line.
"""
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

from asset_graph import AssetGraph, recipe_sources
from content_catalog import ROOT, CATALOG, SCHEMA, find_asset, read_catalog, validate_catalog
from room_catalog import CATALOG as ROOMS, SCHEMA as ROOM_SCHEMA, validate_rooms
from studio_engine import ContentType, Project, Suite

UNITY = ROOT / "unity/PryingEyes"
CITY = UNITY / "Assets/_PryingEyes/City/citta.json"
EXE = UNITY / "Builds/CityPreview/PryingEyes.exe"
PLAN_FIXTURE = ROOT / "tools/fixtures/plan-fingerprint.json"
PLAN_UPDATE = "dotnet run --project tools/planlab -c Release -- fingerprint --update tools/fixtures/plan-fingerprint.json"


def executables():
    version = (UNITY / "ProjectSettings/ProjectVersion.txt").read_text().splitlines()[0].split(": ")[1]
    return (Path(os.environ.get("BLENDER_EXE") or shutil.which("blender") or "C:/Program Files/Blender Foundation/Blender 4.5/blender.exe"),
            Path(os.environ.get("UNITY_EDITOR_EXE") or f"C:/Program Files/Unity/Hub/Editor/{version}/Editor/Unity.exe"))


def check_editor_closed():
    if os.name != "nt": return
    script = "Get-CimInstance Win32_Process -Filter \"name = 'Unity.exe' OR name = 'PryingEyes.exe'\" | Select-Object ProcessId,CommandLine,ExecutablePath | ConvertTo-Json -Compress"
    result = subprocess.run(["powershell", "-NoProfile", "-Command", script], capture_output=True, text=True, check=True)
    processes = json.loads(result.stdout or "[]")
    for process in processes if isinstance(processes, list) else [processes]:
        command = (process.get("CommandLine") or "").replace("\\", "/").lower()
        path = (process.get("ExecutablePath") or "").replace("\\", "/").lower()
        if ("unity.exe" in path and str(UNITY).replace("\\", "/").lower() in command) or path == EXE.as_posix().lower():
            raise RuntimeError(f"PROJECT_IN_USE: process {process['ProcessId']}; close it before building")


def native_gunplay(job, executable=EXE):
    log = job.run("gunplay", [executable, "-batchmode", "-autotest", "-nofire", "-gunplaytest", "-report", job.folder / "telemetry.txt",
                                "-logFile", job.folder / "gunplay-player.log"], timeout=180)
    player_log = job.folder / "gunplay-player.log"
    if not player_log.is_file() or "gunplaytest RISULTATO SUPERATA" not in player_log.read_text(encoding="utf-8-sig"):
        raise RuntimeError(f"NATIVE_RESULT_MISSING: {player_log}")
    job.report["artifacts"].append(str(player_log))


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
    suites = {"gunplay": Suite(lambda ctx, runtime: native_gunplay(ctx.job, runtime), runtime=True, kind="player", needs=("runtime",),
                               about="Weapons and movement in the verified native build (-gunplaytest)"),
              "weapon-state": Suite(lambda ctx, runtime: weapon_state(ctx.job), kind="lab", seconds=3,
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
                            about="The floor generator: its plans against the committed hashes, and the protocol revision")}
    runtime = EXE
    publish_dir = EXE.parent
    published_artifact = EXE
    graph_scope = "declared dependencies and local model-recipe imports; not the complete Unity graph"

    def tools(self):
        blender, unity = executables()
        return {"blender": blender, "unity": unity}

    def lock_resource(self): return UNITY       # one editor per Unity project: jobs on it run one at a time
    def ensure_free(self): check_editor_closed()

    def tool_sources(self, command):
        files = {CITY, PLAN_FIXTURE, ROOT / "tools/studio.py", ROOT / "tools/studio_pryingeyes.py", ROOT / "tools/citylab.py"}
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
