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

PROJECT = ROOT / "unity/PryingEyes"
OUTPUT = ROOT / "unity_logs/studio"
EXE = PROJECT / "Builds/CityPreview/PryingEyes.exe"


def executables():
    version = (PROJECT / "ProjectSettings/ProjectVersion.txt").read_text().splitlines()[0].split(": ")[1]
    return (Path(os.environ.get("BLENDER_EXE") or shutil.which("blender") or "C:/Program Files/Blender Foundation/Blender 4.5/blender.exe"),
            Path(os.environ.get("UNITY_EDITOR_EXE") or f"C:/Program Files/Unity/Hub/Editor/{version}/Editor/Unity.exe"))


@contextmanager
def project_lock():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    # ponytail: serialize this project's entire job; split asset/Unity locks when concurrent generation is needed.
    with (OUTPUT / "project.lock").open("a+b") as handle:
        if handle.tell() == 0: handle.write(b"0"); handle.flush()
        handle.seek(0)
        try:
            if os.name == "nt":
                import msvcrt
                msvcrt.locking(handle.fileno(), msvcrt.LK_NBLCK, 1)
            else:
                import fcntl
                fcntl.flock(handle.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
        except OSError as error:
            raise RuntimeError("PROJECT_BUSY: another production job holds this project") from error
        try: yield
        finally:
            handle.seek(0)
            if os.name == "nt": msvcrt.locking(handle.fileno(), msvcrt.LK_UNLCK, 1)
            else: fcntl.flock(handle.fileno(), fcntl.LOCK_UN)


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


class Job:
    def __init__(self, operation, subject):
        self.folder = OUTPUT / (datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ") + "-" + uuid.uuid4().hex[:8])
        self.folder.mkdir(parents=True)
        self.report = {"schemaVersion": 1, "operation": operation, "subject": subject, "ok": False, "state": "running",
                       "findings": [], "steps": [], "artifacts": [], "reproduce": [sys.executable, *sys.argv]}
        self.save()

    def save(self):
        (self.folder / "report.json").write_text(json.dumps(self.report, indent=2), encoding="utf-8")

    def run(self, label, command, cwd=ROOT, timeout=900):
        log = self.folder / (label + ".log")
        print("STUDIO:", label, flush=True)
        started = time.monotonic()
        with log.open("w", encoding="utf-8") as stream:
            child = subprocess.Popen([str(a) for a in command], cwd=cwd, stdout=stream, stderr=subprocess.STDOUT,
                                     creationflags=0x08000000 if os.name == "nt" else 0)
            step = {"name": label, "cwd": str(Path(cwd).resolve()), "command": [str(a) for a in command], "pid": child.pid, "log": str(log)}
            self.report["steps"].append(step); self.save()
            try: code = child.wait(timeout=timeout)
            except subprocess.TimeoutExpired as error:
                child.kill(); child.wait(); step.update(exitCode=child.returncode, error="STEP_TIMEOUT"); self.save()
                raise RuntimeError("STEP_TIMEOUT: " + label) from error
            except KeyboardInterrupt:
                child.kill(); child.wait(); step.update(exitCode=child.returncode, error="JOB_CANCELLED"); self.save(); raise
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


def verify_sources(job, paths):
    for path in paths:
        if hashlib.sha256(path.read_bytes()).hexdigest() != job.report["sources"][str(path.relative_to(ROOT))]:
            raise RuntimeError("SOURCE_CHANGED_DURING_JOB: " + str(path))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    commands.add_parser("doctor")
    catalog = commands.add_parser("catalog"); catalog.add_argument("action", choices=["list"]); catalog.add_argument("--type", default="weapon", choices=["weapon", "room"])
    asset = commands.add_parser("asset"); asset.add_argument("--publish", action="store_true", help="Copy a verified build to CityPreview after all checks"); asset.add_argument("action", choices=["inspect", "preview", "build"]); asset.add_argument("id")
    test = commands.add_parser("test"); test.add_argument("suite", choices=["gunplay"])
    report = commands.add_parser("report"); report.add_argument("action", choices=["latest"])
    args = parser.parse_args()
    if args.command == "report":
        print((Path(json.loads((OUTPUT / "latest.json").read_text())["report"])).read_text()); return 0
    job = Job(args.command + ("." + args.action if hasattr(args, "action") else ""), getattr(args, "id", getattr(args, "type", "weapons")))
    try:
        data = validate_catalog(); room_data = validate_rooms(); blender, unity = executables()
        is_room = args.command == "asset" and any(r["id"] == args.id for r in room_data["rooms"])
        source_files = {CATALOG, SCHEMA, ROOMS, ROOM_SCHEMA, ROOT / "tools/room_catalog.py", ROOT / "tools/fixtures/weapon-prefix-v1.json", PROJECT / "Assets/_PryingEyes/City/citta.json", Path(__file__).resolve(), ROOT / "tools/content_catalog.py", ROOT / "tools/citylab.py"}
        source_files.update((PROJECT / "Assets/_PryingEyes").rglob("*.cs"))
        source_files.update((PROJECT / "Packages/com.pryingeyes.layout").rglob("*.cs"))
        source_files.update((ROOT / "modelli/armi").glob("*.py"))
        if is_room:
            entry = next(r for r in room_data["rooms"] if r["id"] == args.id)
        elif args.command == "asset":
            entry = find_asset(args.id, data); source = entry["source"]
            source_files.update(ROOT / p for p in [source["generator"], source["recipe"]["file"], *source["dependencies"]])
        job.report["sources"] = {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest() for p in source_files}; job.save()
        if args.command == "catalog" and args.type == "room":
            for room in room_data["rooms"]: print(f"{room['id']:28} {room['name']} ({room['width']} x {room['depth']} m)")
        elif args.command == "catalog":
            for entry in data["weapons"]:
                d = entry["definition"]; print(f"{entry['index']:2}  {d['id']:22} {d['name']}")
        elif args.command == "doctor":
            if not blender.is_file() or not unity.is_file(): raise RuntimeError("TOOL_MISSING: configure BLENDER_EXE and UNITY_EDITOR_EXE")
            print(f"STUDIO: {len(data['weapons'])} valid weapon entries; Blender and Unity available")
        elif args.command == "test":
            with project_lock(): native_gunplay(job)
        else:
            if args.action == "inspect": print(json.dumps(entry, ensure_ascii=False, indent=2))
            elif args.action == "preview":
                with project_lock():
                    if is_room:
                        check_editor_closed()
                        job.run("room-preview", [unity, "-batchmode", "-projectPath", PROJECT, "-executeMethod", "PryingEyes.EditorTools.CityRoomBuilder.Preview", "-room", args.id, "-roomout", job.folder, "-quit", "-logFile", job.folder / "unity.log"], timeout=300)
                        job.report["artifacts"].extend(str(p) for p in [job.folder / (args.id + ".png"), job.folder / "plan.json"])
                    else: preview_asset(job, blender, args.id)
            else:
                with project_lock():
                    check_editor_closed()
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
                        job.report["artifacts"].extend(str(p) for p in [job.folder / (args.id + ".png"), job.folder / "plan.json"])
                    native_gunplay(job, executable)
                    native_city(job, executable)
                    if args.publish:
                        verify_sources(job, source_files)
                        check_editor_closed()
                        shutil.copytree(build, EXE.parent, dirs_exist_ok=True)
                        files = [p for p in build.rglob("*") if p.is_file()]
                        for product in files:
                            if hashlib.sha256(product.read_bytes()).digest() != hashlib.sha256((EXE.parent / product.relative_to(build)).read_bytes()).digest():
                                raise RuntimeError("PUBLISH_HASH_MISMATCH: " + str(product))
                        job.report["published"] = {"path": str(EXE), "filesVerified": len(files)}
        verify_sources(job, source_files)
        for artifact in job.report["artifacts"]:
            if not Path(artifact).is_file(): raise RuntimeError("ARTIFACT_MISSING: " + artifact)
        job.report["ok"] = True
    except (Exception, KeyboardInterrupt) as error:
        job.report["findings"].append({"code": "JOB_CANCELLED" if isinstance(error, KeyboardInterrupt) else str(error).split(":")[0], "subject": job.report["subject"], "observed": str(error), "expected": "successful production operation", "passed": False})
    job.report["state"] = "complete" if job.report["ok"] else "failed"; job.save()
    (OUTPUT / "latest.json").write_text(json.dumps({"report": str(job.folder / "report.json")}), encoding="utf-8")
    print("STUDIO:", "PASS" if job.report["ok"] else "FAIL", job.folder / "report.json", flush=True)
    return 0 if job.report["ok"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
