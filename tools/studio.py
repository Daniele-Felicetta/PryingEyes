"""Production entry point: doctor, catalog list, asset inspect/build, test gunplay, report latest."""
import argparse
from contextlib import contextmanager
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time
import uuid

from content_catalog import ROOT, CATALOG, SCHEMA, find_asset, validate_catalog
from room_catalog import CATALOG as ROOMS, SCHEMA as ROOM_SCHEMA, validate_rooms
from studio_process import OwnedProcess, atomic_json, file_lock, process_identity
from studio_queue import Queue, TERMINAL, new_id, valid_id

PROJECT = ROOT / "unity/PryingEyes"
DEFAULT_OUTPUT = ROOT / "unity_logs/studio"
OUTPUT = Path(os.environ.get("STUDIO_OUTPUT") or DEFAULT_OUTPUT).resolve()
EXE = PROJECT / "Builds/CityPreview/PryingEyes.exe"


def executables():
    version = (PROJECT / "ProjectSettings/ProjectVersion.txt").read_text().splitlines()[0].split(": ")[1]
    return (Path(os.environ.get("BLENDER_EXE") or shutil.which("blender") or "C:/Program Files/Blender Foundation/Blender 4.5/blender.exe"),
            Path(os.environ.get("UNITY_EDITOR_EXE") or f"C:/Program Files/Unity/Hub/Editor/{version}/Editor/Unity.exe"))


@contextmanager
def project_lock(job=None):
    if job is not None:
        job.report["state"] = "waiting"; job.save()
    with file_lock(project_lock_path(), wait=job is not None and job.wait_for_lock,
                   check=job.check_cancel if job is not None else lambda: None):
        if job is not None:
            job.check_cancel()
            verify_sources(job, [ROOT / name for name in job.report.get("sources", {})])
            job.report["state"] = "running"; job.save()
        yield


def project_lock_path(project=None):
    identity = str((project or PROJECT).resolve()).replace("\\", "/")
    if os.name == "nt": identity = identity.casefold()
    return DEFAULT_OUTPUT / "locks" / (hashlib.sha256(identity.encode()).hexdigest()[:24] + ".lock")


def check_editor_closed():
    if os.name != "nt": return
    script = "Get-CimInstance Win32_Process -Filter \"name = 'Unity.exe' OR name = 'PryingEyes.exe'\" | Select-Object ProcessId,CommandLine,ExecutablePath | ConvertTo-Json -Compress"
    result = subprocess.run(["powershell", "-NoProfile", "-Command", script], capture_output=True, text=True, check=True)
    processes = json.loads(result.stdout or "[]")
    for process in processes if isinstance(processes, list) else [processes]:
        command = (process.get("CommandLine") or "").replace("\\", "/").lower()
        path = (process.get("ExecutablePath") or "").replace("\\", "/").lower()
        if ("unity.exe" in path and str(PROJECT).replace("\\", "/").lower() in command) or path == EXE.as_posix().lower():
            raise RuntimeError(f"PROJECT_IN_USE: process {process['ProcessId']}; close it before building")


class JobCancelled(Exception): pass


class Job:
    def __init__(self, operation, subject, queued=None):
        identity = valid_id(queued["id"]) if queued else new_id()
        self.folder = OUTPUT / identity; self.folder.mkdir(parents=True, exist_ok=queued is not None)
        self.wait_for_lock = queued is not None
        self.source_guard = None
        self.report = {"schemaVersion": 1, "id": identity, "operation": operation, "subject": subject,
                       "ok": False, "state": "running", "createdAt": datetime.now(timezone.utc).isoformat(),
                       "ownerPid": os.getpid(), "ownerToken": process_identity(os.getpid()),
                       "findings": [], "steps": [], "artifacts": [],
                       "reproduce": [sys.executable, str(Path(__file__).resolve()), *queued["request"]] if queued else [sys.executable, *sys.argv]}
        if queued: self.report.update(queuedRequest=queued["request"], submittedSources=queued["sources"])
        self.save(); print("STUDIO JOB:", identity, flush=True)

    def save(self):
        self.report["updatedAt"] = datetime.now(timezone.utc).isoformat()
        atomic_json(self.folder / "report.json", self.report)

    def check_cancel(self):
        if (self.folder / "cancel.request").exists():
            self.report["state"] = "cancelling"; self.save()
            raise JobCancelled("JOB_CANCELLED: cancellation requested")

    def run(self, label, command, cwd=ROOT, timeout=900):
        self.check_cancel(); log = self.folder / (label + ".log")
        print("STUDIO:", label, flush=True); started = time.monotonic()
        with log.open("w", encoding="utf-8") as stream:
            child = OwnedProcess(command, cwd, stream)
            step = {"name": label, "cwd": str(Path(cwd).resolve()), "command": list(map(str, command)),
                    "pid": child.pid, "processRole": "owned-tree supervisor", "log": str(log)}
            self.report["steps"].append(step)
            try:
                self.save()
                while True:
                    self.check_cancel()
                    remaining = timeout - (time.monotonic() - started)
                    if remaining <= 0: raise TimeoutError("STEP_TIMEOUT: " + label)
                    try: code = child.wait(timeout=min(.2, remaining)); break
                    except subprocess.TimeoutExpired: pass
            except (TimeoutError, JobCancelled, KeyboardInterrupt) as error:
                child.stop(); step.update(exitCode=child.returncode,
                    error="STEP_TIMEOUT" if isinstance(error, TimeoutError) else "JOB_CANCELLED")
                self.save()
                if isinstance(error, TimeoutError): raise RuntimeError("STEP_TIMEOUT: " + label) from error
                raise
            finally: child.close()
        step.update(exitCode=code, seconds=round(time.monotonic() - started, 2)); self.save()
        if code != 0: raise RuntimeError(f"STEP_FAILED: {label}; exit {code}; {log}")
        return log


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
    job.run("room-materials", [unity, "-batchmode", "-projectPath", PROJECT, "-executeMethod",
        "PryingEyes.EditorTools.CityRoomBuilder.VerifyImportedMaterials", "-quit", "-logFile", job.folder / "room-materials-unity.log"], timeout=180)
    log = job.folder / "room-materials-unity.log"
    if not log.is_file() or "MATERIAL_IMPORT_PASS" not in log.read_text(encoding="utf-8-sig"):
        raise RuntimeError("ROOM_MATERIAL_IMPORT_RESULT_MISSING")
    job.report["artifacts"].append(str(log))


def native_city(job, executable):
    runtime = job.folder / "city-runtime/run.json"
    job.report["artifacts"].append(str(runtime)); job.save()
    try:
        job.run("city", [sys.executable, ROOT / "tools/citylab.py", "--runtime", executable, "--report", job.folder / "city.json"], timeout=300)
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


def verify_sources(job, paths):
    if job.source_guard is not None:
        current = job.source_guard(); expected = job.report["sources"]
        if current != expected:
            job.report["sourceChanges"] = sorted(name for name in current.keys() | expected.keys() if current.get(name) != expected.get(name))
            job.save(); raise RuntimeError("SOURCE_CHANGED_DURING_JOB: " + ", ".join(job.report["sourceChanges"][:5]))
    for path in paths:
        if hashlib.sha256(path.read_bytes()).hexdigest() != job.report["sources"][str(path.relative_to(ROOT))]:
            raise RuntimeError("SOURCE_CHANGED_DURING_JOB: " + str(path))


def arguments():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    commands.add_parser("doctor")
    catalog = commands.add_parser("catalog"); catalog.add_argument("action", choices=["list", "affected"]); catalog.add_argument("--type", default="weapon", choices=["weapon", "room"])
    catalog.add_argument("path", nargs="?"); catalog.add_argument("--pointer", help="Optional JSON pointer within the changed file")
    asset = commands.add_parser("asset"); asset.add_argument("--publish", action="store_true", help="Copy a verified build to CityPreview after all checks"); asset.add_argument("action", choices=["inspect", "preview", "build"]); asset.add_argument("id")
    test = commands.add_parser("test"); test.add_argument("suite", choices=["gunplay", "weapon-state"])
    test.add_argument("--runtime", type=Path, default=EXE, help="Verified native build to test (gunplay only)")
    report = commands.add_parser("report"); report.add_argument("action", choices=["latest"])
    gallery = commands.add_parser("gallery"); gallery.add_argument("action", choices=["build"])
    gallery.add_argument("--out", type=Path); gallery.add_argument("--store", type=Path, action="append", default=[])
    queue = commands.add_parser("queue"); operations = queue.add_subparsers(dest="action", required=True)
    add = operations.add_parser("add"); add.add_argument("arguments", nargs=argparse.REMAINDER)
    run = operations.add_parser("run"); run.add_argument("--background", action="store_true")
    status = operations.add_parser("status"); status.add_argument("id", nargs="?")
    cancel = operations.add_parser("cancel"); cancel.add_argument("id")
    jobs = commands.add_parser("job"); jobs.add_argument("action", choices=["list", "status", "cancel"]); jobs.add_argument("id", nargs="?")
    return parser


def source_snapshot(args, data, rooms):
    is_room = args.command == "asset" and any(r["id"] == args.id for r in rooms["rooms"])
    source_files = {CATALOG, SCHEMA, ROOMS, ROOM_SCHEMA, ROOT / "tools/room_catalog.py", ROOT / "tools/fixtures/weapon-prefix-v1.json", PROJECT / "Assets/_PryingEyes/City/citta.json", Path(__file__).resolve(), ROOT / "tools/content_catalog.py", ROOT / "tools/citylab.py"}
    source_files.update((PROJECT / "Assets/_PryingEyes").rglob("*.cs"))
    for extension in ("*.shader", "*.hlsl"):
        source_files.update((PROJECT / "Assets/_PryingEyes").rglob(extension))
    source_files.update((PROJECT / "Packages/com.pryingeyes.layout").rglob("*.cs"))
    source_files.update((PROJECT / "Packages/com.pryingeyes.weapons").rglob("*.cs"))
    source_files.update(PROJECT / "Packages" / name for name in ("manifest.json", "packages-lock.json"))
    for package in (PROJECT / "Packages").glob("com.pryingeyes.*"):
        source_files.update(package.rglob("*.asmdef")); source_files.add(package / "package.json")
    for folder in (ROOT / "tools/planlab", ROOT / "tools/weaponslab"):
        source_files.update(folder.glob("*.cs*"))
    source_files.update((ROOT / "modelli/armi").glob("*.py"))
    if is_room:
        entry = next(r for r in rooms["rooms"] if r["id"] == args.id)
    elif args.command == "asset":
        entry = find_asset(args.id, data); source = entry["source"]
        source_files.update(ROOT / p for p in [source["generator"], source["recipe"]["file"], *source["dependencies"]])
    source_files.update(ROOT / "tools" / name for name in ("studio_process.py", "studio_queue.py"))
    if args.command in ("catalog", "gallery"): source_files.add(ROOT / "tools/asset_graph.py")
    if args.command == "gallery":
        source_files.add(ROOT / "tools/studio_gallery.py"); source_files.update(p for p in (ROOT / "tools/studio_web").glob("*") if p.is_file())
    sources = {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest() for p in source_files}
    return sources, entry if args.command == "asset" else None, is_room


def execute(args, queued=None):
    job = Job(args.command + ("." + args.action if hasattr(args, "action") else ""), getattr(args, "id", getattr(args, "type", "weapons")), queued)
    interrupted = cancelled = False
    try:
        job.check_cancel()
        from content_catalog import read_catalog
        catalog_reader = read_catalog if args.command == "catalog" and args.action == "affected" else validate_catalog
        data = catalog_reader()
        room_data = validate_rooms(); blender, unity = executables()
        sources, entry, is_room = source_snapshot(args, data, room_data)
        job.source_guard = lambda: source_snapshot(args, catalog_reader(), validate_rooms())[0]
        job.report["sources"] = sources; job.save()
        if queued and sources != queued["sources"]:
            job.report["sourceChanges"] = sorted(name for name in sources.keys() | queued["sources"].keys() if sources.get(name) != queued["sources"].get(name))
            raise RuntimeError("QUEUED_SOURCES_CHANGED: resubmit against current sources")
        if args.command == "catalog" and args.action == "affected":
            from asset_graph import production_graph
            if not args.path: raise ValueError("DEPENDENCY_PATH_REQUIRED")
            graph = production_graph(data, room_data); affected = graph.affected(args.path, args.pointer)
            result = {"schemaVersion": 1, "operation": "catalog.affected", "ok": True,
                "scope": "declared dependencies and local model-recipe imports; not the complete Unity graph", "path": args.path,
                "pointer": args.pointer, "affected": affected}
            target = job.folder / "affected.json"; atomic_json(target, result); job.report["artifacts"].append(str(target))
            print(json.dumps(result, ensure_ascii=False, indent=2))
        elif args.command == "catalog" and args.type == "room":
            for room in room_data["rooms"]: print(f"{room['id']:28} {room['name']} ({room['width']} x {room['depth']} m)")
        elif args.command == "catalog":
            for entry in data["weapons"]:
                d = entry["definition"]; print(f"{entry['index']:2}  {d['id']:22} {d['name']}")
        elif args.command == "doctor":
            if not blender.is_file() or not unity.is_file() or not shutil.which("dotnet"):
                raise RuntimeError("TOOL_MISSING: configure Blender, Unity and dotnet")
            print(f"STUDIO: {len(data['weapons'])} valid weapon entries; Blender, Unity and dotnet available")
        elif args.command == "test":
            with project_lock(job):
                if args.suite == "weapon-state": weapon_state(job)
                else: native_gunplay(job, args.runtime.resolve())
        elif args.command == "gallery":
            from asset_graph import production_graph
            from studio_gallery import build_gallery
            graph = production_graph(data, room_data)
            expected, _, _ = source_snapshot(argparse.Namespace(command="test", suite="weapon-state"), data, room_data)
            stores = {OUTPUT, DEFAULT_OUTPUT, *args.store}
            stores.update(p for p in (ROOT / "unity_logs").iterdir() if p.is_dir())
            target = (args.out or job.folder / "gallery").resolve()
            manifest = build_gallery(target, graph, data, room_data, expected, stores)
            job.report["artifacts"].extend(str(target / name) for name in ("index.html", "manifest.json"))
            print(f"STUDIO GALLERY: {len(manifest['assets'])} contents, {len(manifest['runs'])} recorded/reference runs; {target / 'index.html'}")
        else:
            if args.action == "inspect": print(json.dumps(entry, ensure_ascii=False, indent=2))
            elif args.action == "preview":
                with project_lock(job):
                    if is_room:
                        check_editor_closed()
                        job.run("room-preview", [unity, "-batchmode", "-projectPath", PROJECT, "-executeMethod", "PryingEyes.EditorTools.CityRoomBuilder.Preview", "-room", args.id, "-roomout", job.folder, "-quit", "-logFile", job.folder / "unity.log"], timeout=300)
                        verify_room_materials(job, unity)
                        job.report["artifacts"].extend(str(p) for p in [job.folder / (args.id + ".png"), job.folder / "plan.json"])
                    else: preview_asset(job, blender, args.id)
            else:
                with project_lock(job):
                    check_editor_closed()
                    weapon_state(job)
                    build = job.folder / "build"; executable = build / "PryingEyes.exe"
                    if is_room:
                        recipe = json.loads((PROJECT / "Assets/_PryingEyes/City/citta.json").read_text())
                        if not any(b["asset"] == args.id for b in recipe.get("rooms", [])):
                            raise RuntimeError("ROOM_NOT_IN_CITY: bind the room recipe to a city block before integration testing")
                        method = "PryingEyes.EditorTools.CityRoomBuilder.BuildAsset"
                        asset_args = ["-room", args.id, "-roomout", job.folder]
                    else:
                        identities = [args.id]
                        recipe = json.loads((PROJECT / "Assets/_PryingEyes/City/citta.json").read_text())
                        for identity in recipe["shopWeapons"]:
                            candidate = find_asset(identity, data)
                            if candidate["source"].get("family") and not (ROOT / f"modelli/armi/export/{identity}.fbx").is_file() and identity not in identities:
                                identities.append(identity)
                        for identity in identities:
                            job.run("bake-" + identity, [blender, "-b", "-P", "genera_armi.py", "--", "bake", identity], ROOT / "modelli/armi", 1800)
                            job.run("pack-" + identity, [sys.executable, "pack_tex.py", identity], ROOT / "modelli/armi")
                        preview_asset(job, blender, args.id)
                        method = "PryingEyes.EditorTools.ProductionCatalog.BuildAsset"
                        asset_args = ["-asset", args.id]
                    job.run("unity", [unity, "-batchmode", "-projectPath", PROJECT, "-executeMethod", method, *asset_args, "-buildOut", build, "-quit", "-logFile", job.folder / "unity.log"], timeout=1200)
                    if is_room:
                        verify_room_materials(job, unity)
                        job.report["artifacts"].extend(str(p) for p in [job.folder / (args.id + ".png"), job.folder / "plan.json"])
                    native_gunplay(job, executable)
                    native_city(job, executable)
                    if args.publish:
                        job.check_cancel()
                        verify_sources(job, [ROOT / name for name in job.report["sources"]])
                        check_editor_closed()
                        shutil.copytree(build, EXE.parent, dirs_exist_ok=True)
                        files = [p for p in build.rglob("*") if p.is_file()]
                        for product in files:
                            if hashlib.sha256(product.read_bytes()).digest() != hashlib.sha256((EXE.parent / product.relative_to(build)).read_bytes()).digest():
                                raise RuntimeError("PUBLISH_HASH_MISMATCH: " + str(product))
                        job.report["published"] = {"path": str(EXE), "filesVerified": len(files)}
        verify_sources(job, [ROOT / name for name in job.report["sources"]])
        for artifact in job.report["artifacts"]:
            if not Path(artifact).is_file(): raise RuntimeError("ARTIFACT_MISSING: " + artifact)
        job.check_cancel(); job.report["ok"] = True
    except (Exception, KeyboardInterrupt) as error:
        interrupted = isinstance(error, KeyboardInterrupt)
        cancelled = isinstance(error, (KeyboardInterrupt, JobCancelled))
        job.report["findings"].append({"code": "JOB_CANCELLED" if cancelled else str(error).split(":")[0], "subject": job.report["subject"], "observed": str(error), "expected": "successful production operation", "passed": False})
    job.report["state"] = "complete" if job.report["ok"] else "cancelled" if cancelled else "failed"; job.save()
    atomic_json(OUTPUT / "latest.json", {"report": str(job.folder / "report.json")})
    print("STUDIO:", "PASS" if job.report["ok"] else "FAIL", job.folder / "report.json", flush=True)
    return 130 if interrupted else 0 if job.report["ok"] else 1




def job_control(args):
    if args.action == "list":
        return [{"id": p.parent.name, "state": json.loads(p.read_text(encoding="utf-8")).get("state"), "report": str(p)}
                for p in sorted(OUTPUT.glob("*/report.json"))]
    if args.id is None: raise ValueError("JOB_ID_REQUIRED")
    queue = Queue(OUTPUT)
    try: queue.get(args.id)
    except ValueError as error:
        if not str(error).startswith("JOB_UNKNOWN"): raise
    else:
        if args.action == "cancel": queue.cancel(args.id)
        return queue.status(args.id)[0]
    path = OUTPUT / valid_id(args.id) / "report.json"
    if not path.is_file(): raise ValueError("JOB_UNKNOWN: " + args.id)
    report = json.loads(path.read_text(encoding="utf-8"))
    if report.get("state") not in TERMINAL and report.get("ownerPid") and process_identity(report["ownerPid"]) != report.get("ownerToken"):
        report.update(state="interrupted", ok=False); atomic_json(path, report)
    if args.action == "cancel" and report.get("state") not in TERMINAL:
        (path.parent / "cancel.request").touch()
        report["cancelRequested"] = True
    return report


def main(argv=None):
    parser = arguments(); args = parser.parse_args(argv)
    if args.command == "report":
        print(Path(json.loads((OUTPUT / "latest.json").read_text())["report"]).read_text()); return 0
    if args.command not in ("queue", "job"): return execute(args)
    try:
        exit_code = 0
        if args.command == "job": result = job_control(args)
        else:
            queue = Queue(OUTPUT)
            if args.action == "add":
                request = parser.parse_args(args.arguments)
                if request.command not in ("asset", "test"): raise ValueError("QUEUE_OPERATION_NOT_SUPPORTED")
                sources, _, _ = source_snapshot(request, validate_catalog(), validate_rooms())
                command = ["asset", request.action, request.id] + (["--publish"] if request.publish else []) if request.command == "asset" else \
                    ["test", request.suite] + (["--runtime", str(request.runtime.resolve())] if request.suite == "gunplay" else [])
                result = {"id": queue.submit(command, sources)}
            elif args.action == "status": result = queue.status(args.id)
            elif args.action == "cancel":
                queue.cancel(args.id); result = queue.status(args.id)[0]
            elif args.background:
                log = OUTPUT / ("queue-worker-" + uuid.uuid4().hex[:8] + ".log")
                with log.open("w", encoding="utf-8") as stream:
                    worker = subprocess.Popen([sys.executable, str(Path(__file__).resolve()), "queue", "run"], cwd=ROOT,
                        stdin=subprocess.DEVNULL, stdout=stream, stderr=subprocess.STDOUT,
                        creationflags=0x08000000 | 0x00000200 if os.name == "nt" else 0,
                        start_new_session=os.name != "nt")
                result = {"workerPid": worker.pid, "log": str(log)}
            else:
                exit_code = queue.work(lambda row: execute(parser.parse_args(row["request"]), row))
                result = queue.status()
        print(json.dumps({"schemaVersion": 1, "operation": args.command + "." + args.action, "ok": True, "result": result}, indent=2))
        return exit_code
    except (ValueError, RuntimeError, OSError) as error:
        print(json.dumps({"schemaVersion": 1, "operation": args.command + "." + args.action, "ok": False,
            "findings": [{"code": str(error).split(":")[0], "observed": str(error), "expected": "valid coordinator operation", "passed": False}]}, indent=2))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
