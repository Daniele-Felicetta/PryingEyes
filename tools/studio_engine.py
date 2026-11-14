"""Project-agnostic production studio engine.

A production run is a Job: one report.json holding the exact sources it ran against, every step's command, log and exit
code, findings in code/observed/expected form, and the evidence artifacts. Around jobs the engine gives every project:
a cross-process lock so two runs never share one resource, a source-hash guard that fails a run whose inputs changed while it
waited or ran, cancellation, a persistent FIFO queue, and the command line.

The engine names no project. Content types, checks, tools and paths come from a Project: the profile module that sits next
to this file is the real one, tools/fixtures/toy_project.py the smallest possible. tools/check_suite.py fails if this module
(or the process and queue modules it builds on) starts naming a game, a tool or a kind of content.
"""
import argparse
from contextlib import contextmanager
from dataclasses import dataclass
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time
import types
import uuid

from studio_process import OwnedProcess, atomic_json, file_lock, process_identity
from studio_queue import Queue, TERMINAL, new_id, valid_id

# The engine's own files are part of every job's sources: changing the tool invalidates what it verified.
ENGINE_FILES = tuple(Path(__file__).with_name(name) for name in ("studio_engine.py", "studio_process.py", "studio_queue.py"))


class JobCancelled(Exception): pass


class ContentType:
    """One kind of content a project produces (a catalog of entries). Subclass it, set `name`, override what applies."""
    name = ""

    def load(self, strict=True):
        """Catalog data. strict validates schema and sources; not strict only parses (cheap, for read-only commands)."""
        raise NotImplementedError
    def entries(self, data): raise NotImplementedError
    def entry_id(self, entry): raise NotImplementedError
    def summary(self, entry): return self.entry_id(entry)           # one line of `catalog list`
    def files(self): return ()                                      # catalog, schema, validators, generators: every job is bound to them
    def entry_files(self, entry): return ()                         # extra sources of a single entry
    def inspect(self, entry): return entry
    def preview(self, ctx, entry): raise RuntimeError("PREVIEW_NOT_SUPPORTED: " + self.name)
    def build(self, ctx, entry):
        """Produce the entry into ctx.build_dir and return the produced artifact (what after_build verifies)."""
        raise RuntimeError("BUILD_NOT_SUPPORTED: " + self.name)


@dataclass(frozen=True)
class Suite:
    """A named check a project can run on its own (`test NAME`). run(ctx, runtime); `runtime` suites accept --runtime PATH."""
    run: object
    runtime: bool = False


@dataclass
class Context:
    """What content types and suites receive while they run: the job to record steps in, the project, its tools."""
    job: object
    project: object
    tools: dict
    data: object = None           # catalog data of the content type being produced
    build_dir: Path = None


class Project:
    """Everything the engine needs to know about one project. Subclass, set the attributes, override the hooks that apply."""
    name = "project"
    root = Path(".")              # sources, graphs and evidence are all relative to it
    entry_script = None           # script that runs this project's engine (reproduce commands, background queue workers)
    default_output = None         # job store unless STUDIO_OUTPUT (or the constructor) says otherwise
    default_subject = "all"       # report subject of commands that name no asset
    content_types = ()            # ContentType instances; entry ids are unique across all of them
    suites = {}                   # name -> Suite
    runtime = None                # default of `test --runtime`
    publish_dir = None            # `asset build --publish` copies the verified build here...
    published_artifact = None     # ...and records this file as the published entry point
    graph_scope = "declared dependencies"
    description = "Production studio"

    def __init__(self, output=None):
        self.default_output = Path(self.default_output).resolve() if self.default_output else None
        self.output = Path(output or os.environ.get("STUDIO_OUTPUT") or self.default_output).resolve()
        self.kinds = {kind.name: kind for kind in self.content_types}

    def tools(self): return {}                            # name -> executable; resolved lazily so tests need no installed tools
    def lock_resource(self): return self.root             # what two jobs must never touch at once
    def ensure_free(self):                                # raise RuntimeError("PROJECT_IN_USE: ...") when someone else holds the resource
        pass
    def tool_sources(self, command): return ()            # the project's own tooling files, hashed into jobs of `command`
    def doctor(self, catalogs): raise NotImplementedError   # check the environment; return the summary line
    def before_build(self, ctx): self.ensure_free()       # runs under the lock, before any entry is produced
    def after_build(self, ctx, built): pass               # checks against the produced build, before it can be published
    def graph(self, catalogs): raise RuntimeError("GRAPH_NOT_SUPPORTED: " + self.name)
    def evidence_stores(self): return ()                  # extra folders of past reports for the gallery
    def gallery(self, target, catalogs, expected, stores): raise RuntimeError("GALLERY_NOT_SUPPORTED: " + self.name)


def load_catalogs(project, strict=True):
    catalogs = {kind.name: kind.load(strict) for kind in project.content_types}
    seen = {}
    for kind in project.content_types:
        for entry in kind.entries(catalogs[kind.name]):
            identity = kind.entry_id(entry)
            if identity in seen: raise ValueError(f"CONTENT_ID_COLLISION: {identity} in {seen[identity]} and {kind.name}")
            seen[identity] = kind.name
    return catalogs


def resolve(project, catalogs, identity):
    for kind in project.content_types:
        for entry in kind.entries(catalogs[kind.name]):
            if kind.entry_id(entry) == identity: return kind, entry
    raise ValueError(f"ASSET_UNKNOWN: {identity}")


def source_key(project, path):
    """How a source file is named in reports: relative to the project root, or absolute when it lives outside it."""
    path = Path(path)
    return str(path.relative_to(project.root)) if path.is_relative_to(project.root) else str(path)


def source_snapshot(project, args, catalogs):
    """Hashes of every file the job's result depends on, plus the content type and entry the job is about."""
    files = set(ENGINE_FILES) | set(project.tool_sources(args.command))
    for kind in project.content_types: files.update(kind.files())
    kind = entry = None
    if args.command == "asset":
        kind, entry = resolve(project, catalogs, args.id)
        files.update(kind.entry_files(entry))
    return {source_key(project, p): hashlib.sha256(p.read_bytes()).hexdigest() for p in files}, entry, kind


def verify_sources(job, paths):
    if job.source_guard is not None:
        current = job.source_guard(); expected = job.report["sources"]
        if current != expected:
            job.report["sourceChanges"] = sorted(name for name in current.keys() | expected.keys() if current.get(name) != expected.get(name))
            job.save(); raise RuntimeError("SOURCE_CHANGED_DURING_JOB: " + ", ".join(job.report["sourceChanges"][:5]))
    for path in paths:
        if hashlib.sha256(path.read_bytes()).hexdigest() != job.report["sources"][source_key(job.project, path)]:
            raise RuntimeError("SOURCE_CHANGED_DURING_JOB: " + str(path))


def project_lock_path(project, resource=None):
    identity = str((resource or project.lock_resource()).resolve()).replace("\\", "/")
    if os.name == "nt": identity = identity.casefold()
    return project.default_output / "locks" / (hashlib.sha256(identity.encode()).hexdigest()[:24] + ".lock")


@contextmanager
def project_lock(project, job=None):
    if job is not None:
        job.report["state"] = "waiting"; job.save()
    with file_lock(project_lock_path(project), wait=job is not None and job.wait_for_lock,
                   check=job.check_cancel if job is not None else lambda: None):
        if job is not None:
            job.check_cancel()
            verify_sources(job, [project.root / name for name in job.report.get("sources", {})])
            job.report["state"] = "running"; job.save()
        yield


class Job:
    def __init__(self, project, operation, subject, queued=None):
        identity = valid_id(queued["id"]) if queued else new_id()
        self.project = project
        self.folder = project.output / identity; self.folder.mkdir(parents=True, exist_ok=queued is not None)
        self.wait_for_lock = queued is not None
        self.source_guard = None
        self.report = {"schemaVersion": 1, "id": identity, "operation": operation, "subject": subject,
                       "ok": False, "state": "running", "createdAt": datetime.now(timezone.utc).isoformat(),
                       "ownerPid": os.getpid(), "ownerToken": process_identity(os.getpid()),
                       "findings": [], "steps": [], "artifacts": [],
                       "reproduce": [sys.executable, str(project.entry_script), *queued["request"]] if queued else [sys.executable, *sys.argv]}
        if queued: self.report.update(queuedRequest=queued["request"], submittedSources=queued["sources"])
        self.save(); print("STUDIO JOB:", identity, flush=True)

    def save(self):
        self.report["updatedAt"] = datetime.now(timezone.utc).isoformat()
        atomic_json(self.folder / "report.json", self.report)

    def check_cancel(self):
        if (self.folder / "cancel.request").exists():
            self.report["state"] = "cancelling"; self.save()
            raise JobCancelled("JOB_CANCELLED: cancellation requested")

    def run(self, label, command, cwd=None, timeout=900):
        self.check_cancel(); log = self.folder / (label + ".log"); cwd = cwd or self.project.root
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


def publish(project, job, build):
    """Copy a verified build to the project's publish folder and prove every file arrived byte for byte."""
    if project.publish_dir is None: raise RuntimeError("PUBLISH_NOT_SUPPORTED: " + project.name)
    job.check_cancel()
    verify_sources(job, [project.root / name for name in job.report["sources"]])
    project.ensure_free()
    shutil.copytree(build, project.publish_dir, dirs_exist_ok=True)
    files = [p for p in build.rglob("*") if p.is_file()]
    for product in files:
        if hashlib.sha256(product.read_bytes()).digest() != hashlib.sha256((project.publish_dir / product.relative_to(build)).read_bytes()).digest():
            raise RuntimeError("PUBLISH_HASH_MISMATCH: " + str(product))
    job.report["published"] = {"path": str(project.published_artifact or project.publish_dir), "filesVerified": len(files)}


def arguments(project):
    kinds = list(project.kinds)
    parser = argparse.ArgumentParser(description=project.description)
    commands = parser.add_subparsers(dest="command", required=True)
    commands.add_parser("doctor")
    catalog = commands.add_parser("catalog"); catalog.add_argument("action", choices=["list", "affected"]); catalog.add_argument("--type", default=kinds[0], choices=kinds)
    catalog.add_argument("path", nargs="?"); catalog.add_argument("--pointer", help="Optional JSON pointer within the changed file")
    asset = commands.add_parser("asset"); asset.add_argument("--publish", action="store_true", help="Copy a verified build to the project's publish folder after all checks"); asset.add_argument("action", choices=["inspect", "preview", "build"]); asset.add_argument("id")
    test = commands.add_parser("test"); test.add_argument("suite", choices=list(project.suites))
    test.add_argument("--runtime", type=Path, default=project.runtime, help="Verified native build to test (suites that need one)")
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


def execute(project, args, queued=None):
    job = Job(project, args.command + ("." + args.action if hasattr(args, "action") else ""),
              getattr(args, "id", getattr(args, "type", project.default_subject)), queued)
    interrupted = cancelled = False
    try:
        job.check_cancel()
        strict = not (args.command == "catalog" and args.action == "affected")
        catalogs = load_catalogs(project, strict)
        tools = project.tools()
        sources, entry, kind = source_snapshot(project, args, catalogs)
        job.source_guard = lambda: source_snapshot(project, args, load_catalogs(project, strict))[0]
        job.report["sources"] = sources; job.save()
        if queued and sources != queued["sources"]:
            job.report["sourceChanges"] = sorted(name for name in sources.keys() | queued["sources"].keys() if sources.get(name) != queued["sources"].get(name))
            raise RuntimeError("QUEUED_SOURCES_CHANGED: resubmit against current sources")
        ctx = Context(job, project, tools, catalogs[kind.name] if kind else None)
        if args.command == "catalog" and args.action == "affected":
            if not args.path: raise ValueError("DEPENDENCY_PATH_REQUIRED")
            affected = project.graph(catalogs).affected(args.path, args.pointer)
            result = {"schemaVersion": 1, "operation": "catalog.affected", "ok": True, "scope": project.graph_scope,
                      "path": args.path, "pointer": args.pointer, "affected": affected}
            target = job.folder / "affected.json"; atomic_json(target, result); job.report["artifacts"].append(str(target))
            print(json.dumps(result, ensure_ascii=False, indent=2))
        elif args.command == "catalog":
            listed = project.kinds[args.type]
            for item in listed.entries(catalogs[listed.name]): print(listed.summary(item))
        elif args.command == "doctor":
            print("STUDIO:", project.doctor(catalogs))
        elif args.command == "test":
            with project_lock(project, job):
                suite = project.suites[args.suite]
                suite.run(ctx, args.runtime.resolve() if args.runtime else None)
        elif args.command == "gallery":
            expected = source_snapshot(project, types.SimpleNamespace(command="test"), catalogs)[0]
            stores = {p for p in (project.output, project.default_output, *args.store, *project.evidence_stores()) if p}
            target = (args.out or job.folder / "gallery").resolve()
            manifest = project.gallery(target, catalogs, expected, stores)
            job.report["artifacts"].extend(str(target / name) for name in ("index.html", "manifest.json"))
            print(f"STUDIO GALLERY: {len(manifest['assets'])} contents, {len(manifest['runs'])} recorded/reference runs; {target / 'index.html'}")
        elif args.action == "inspect": print(json.dumps(kind.inspect(entry), ensure_ascii=False, indent=2))
        elif args.action == "preview":
            with project_lock(project, job): kind.preview(ctx, entry)
        else:
            with project_lock(project, job):
                project.before_build(ctx)
                ctx.build_dir = job.folder / "build"
                built = kind.build(ctx, entry)
                project.after_build(ctx, built)
                if args.publish: publish(project, job, ctx.build_dir)
        verify_sources(job, [project.root / name for name in job.report["sources"]])
        for artifact in job.report["artifacts"]:
            if not Path(artifact).is_file(): raise RuntimeError("ARTIFACT_MISSING: " + artifact)
        job.check_cancel(); job.report["ok"] = True
    except (Exception, KeyboardInterrupt) as error:
        interrupted = isinstance(error, KeyboardInterrupt)
        cancelled = isinstance(error, (KeyboardInterrupt, JobCancelled))
        job.report["findings"].append({"code": "JOB_CANCELLED" if cancelled else str(error).split(":")[0], "subject": job.report["subject"], "observed": str(error), "expected": "successful production operation", "passed": False})
    job.report["state"] = "complete" if job.report["ok"] else "cancelled" if cancelled else "failed"; job.save()
    atomic_json(project.output / "latest.json", {"report": str(job.folder / "report.json")})
    print("STUDIO:", "PASS" if job.report["ok"] else "FAIL", job.folder / "report.json", flush=True)
    return 130 if interrupted else 0 if job.report["ok"] else 1


def job_control(project, args):
    output = project.output
    if args.action == "list":
        return [{"id": p.parent.name, "state": json.loads(p.read_text(encoding="utf-8")).get("state"), "report": str(p)}
                for p in sorted(output.glob("*/report.json"))]
    if args.id is None: raise ValueError("JOB_ID_REQUIRED")
    queue = Queue(output)
    try: queue.get(args.id)
    except ValueError as error:
        if not str(error).startswith("JOB_UNKNOWN"): raise
    else:
        if args.action == "cancel": queue.cancel(args.id)
        return queue.status(args.id)[0]
    path = output / valid_id(args.id) / "report.json"
    if not path.is_file(): raise ValueError("JOB_UNKNOWN: " + args.id)
    report = json.loads(path.read_text(encoding="utf-8"))
    if report.get("state") not in TERMINAL and report.get("ownerPid") and process_identity(report["ownerPid"]) != report.get("ownerToken"):
        report.update(state="interrupted", ok=False); atomic_json(path, report)
    if args.action == "cancel" and report.get("state") not in TERMINAL:
        (path.parent / "cancel.request").touch()
        report["cancelRequested"] = True
    return report


def main(project, argv=None):
    parser = arguments(project); args = parser.parse_args(argv)
    if args.command == "report":
        print(Path(json.loads((project.output / "latest.json").read_text())["report"]).read_text()); return 0
    if args.command not in ("queue", "job"): return execute(project, args)
    try:
        exit_code = 0
        if args.command == "job": result = job_control(project, args)
        else:
            queue = Queue(project.output)
            if args.action == "add":
                request = parser.parse_args(args.arguments)
                if request.command not in ("asset", "test"): raise ValueError("QUEUE_OPERATION_NOT_SUPPORTED")
                sources = source_snapshot(project, request, load_catalogs(project, True))[0]
                command = ["asset", request.action, request.id] + (["--publish"] if request.publish else []) if request.command == "asset" else \
                    ["test", request.suite] + (["--runtime", str(request.runtime.resolve())] if project.suites[request.suite].runtime and request.runtime else [])
                result = {"id": queue.submit(command, sources)}
            elif args.action == "status": result = queue.status(args.id)
            elif args.action == "cancel":
                queue.cancel(args.id); result = queue.status(args.id)[0]
            elif args.background:
                log = project.output / ("queue-worker-" + uuid.uuid4().hex[:8] + ".log")
                with log.open("w", encoding="utf-8") as stream:
                    worker = subprocess.Popen([sys.executable, str(project.entry_script), "queue", "run"], cwd=project.root,
                        stdin=subprocess.DEVNULL, stdout=stream, stderr=subprocess.STDOUT,
                        creationflags=0x08000000 | 0x00000200 if os.name == "nt" else 0,
                        start_new_session=os.name != "nt")
                result = {"workerPid": worker.pid, "log": str(log)}
            else:
                exit_code = queue.work(lambda row: execute(project, parser.parse_args(row["request"]), row))
                result = queue.status()
        print(json.dumps({"schemaVersion": 1, "operation": args.command + "." + args.action, "ok": True, "result": result}, indent=2))
        return exit_code
    except (ValueError, RuntimeError, OSError) as error:
        print(json.dumps({"schemaVersion": 1, "operation": args.command + "." + args.action, "ok": False,
            "findings": [{"code": str(error).split(":")[0], "observed": str(error), "expected": "valid coordinator operation", "passed": False}]}, indent=2))
        return 1
