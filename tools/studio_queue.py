"""Persistent FIFO coordinator. The caller supplies requests, source snapshots and execution."""
from contextlib import contextmanager
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import re
import sqlite3
import uuid
from studio_process import atomic_json, file_lock, process_identity

TERMINAL = {"complete", "failed", "cancelled", "interrupted"}
def now(): return datetime.now(timezone.utc).isoformat()
def new_id(): return datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ") + "-" + uuid.uuid4().hex[:8]
def valid_id(identity):
    if not re.fullmatch(r"\d{8}T\d{6}Z-[a-f0-9]{8}", identity): raise ValueError("JOB_ID_INVALID")
    return identity


class Queue:
    def __init__(self, output):
        self.output = Path(output); self.output.mkdir(parents=True, exist_ok=True)
        with self.db() as db:
            db.execute("""CREATE TABLE IF NOT EXISTS jobs (
                sequence INTEGER PRIMARY KEY AUTOINCREMENT, id TEXT UNIQUE NOT NULL, request TEXT NOT NULL,
                sources TEXT NOT NULL, state TEXT NOT NULL, created TEXT NOT NULL, updated TEXT NOT NULL,
                owner_pid INTEGER, owner_token TEXT, cancel_requested INTEGER NOT NULL DEFAULT 0)""")

    @contextmanager
    def db(self):
        connection = sqlite3.connect(self.output / "queue.sqlite3", timeout=10, isolation_level=None)
        connection.row_factory = sqlite3.Row
        try: yield connection
        finally: connection.close()

    def submit(self, request, sources):
        identity = new_id(); stamp = now()
        folder = self.output / identity; folder.mkdir()
        atomic_json(folder / "report.json", {"schemaVersion": 1, "id": identity, "operation": "queue", "subject": request,
            "state": "queued", "ok": False, "findings": [], "steps": [], "artifacts": [], "created": stamp})
        with self.db() as db:
            db.execute("INSERT INTO jobs(id,request,sources,state,created,updated) VALUES(?,?,?,'queued',?,?)",
                       (identity, json.dumps(request), json.dumps(sources), stamp, stamp))
        return identity

    def get(self, identity):
        with self.db() as db: row = db.execute("SELECT * FROM jobs WHERE id=?", (valid_id(identity),)).fetchone()
        if row is None: raise ValueError("JOB_UNKNOWN: " + identity)
        result = dict(row); result["request"] = json.loads(result["request"]); result["sources"] = json.loads(result["sources"])
        return result

    def report(self, identity):
        path = self.output / valid_id(identity) / "report.json"
        return json.loads(path.read_text(encoding="utf-8")) if path.is_file() else {}

    def cancel(self, identity):
        with self.db() as db:
            db.execute("BEGIN IMMEDIATE")
            row = db.execute("SELECT state FROM jobs WHERE id=?", (valid_id(identity),)).fetchone()
            if row is None: db.rollback(); raise ValueError("JOB_UNKNOWN: " + identity)
            state = row["state"]
            if state not in TERMINAL:
                # Signal before releasing the transaction: a worker can never start an already cancelled request.
                (self.output / identity / "cancel.request").touch()
                db.execute("UPDATE jobs SET cancel_requested=1,state=?,updated=? WHERE id=?",
                           ("cancelled" if state == "queued" else state, now(), identity))
            db.commit()
        if state == "queued":
            report = self.report(identity); report.update(state="cancelled", updated=now())
            atomic_json(self.output / identity / "report.json", report)
        return self.get(identity)

    def recover(self):
        with self.db() as db: rows = db.execute("SELECT * FROM jobs WHERE state='running'").fetchall()
        for row in rows:
            if process_identity(row["owner_pid"]) == row["owner_token"]: continue
            report = self.report(row["id"]); state = report.get("state")
            if state not in TERMINAL:
                state = "interrupted"; report.update(state=state, ok=False, updated=now())
                report.setdefault("findings", []).append({"code": "OWNER_PROCESS_EXITED", "subject": row["id"], "passed": False,
                    "observed": "recorded worker process no longer exists", "expected": "worker completes the job or handles cancellation"})
                atomic_json(self.output / row["id"] / "report.json", report)
            with self.db() as db:
                db.execute("UPDATE jobs SET state=?,updated=? WHERE id=? AND state='running'", (state, now(), row["id"]))

    def status(self, identity=None):
        self.recover()
        with self.db() as db:
            rows = db.execute("SELECT id FROM jobs ORDER BY sequence").fetchall()
        items = [self.get(identity)] if identity else [self.get(row["id"]) for row in rows]
        for item in items:
            item["sourceCount"] = len(item.pop("sources"))
            item["report"] = str(self.output / item["id"] / "report.json")
            item["executionState"] = self.report(item["id"]).get("state", item["state"])
        return items

    def work(self, execute):
        # ponytail: one project-wide FIFO worker; generation can get a separate pool when real throughput requires it.
        with file_lock(self.output / "queue.worker.lock"):
            self.recover()
            while True:
                with self.db() as db:
                    db.execute("BEGIN IMMEDIATE")
                    row = db.execute("SELECT id FROM jobs WHERE state='queued' ORDER BY sequence LIMIT 1").fetchone()
                    if row is None: db.commit(); break
                    identity = row["id"]
                    db.execute("UPDATE jobs SET state='running',owner_pid=?,owner_token=?,updated=? WHERE id=?",
                               (os.getpid(), process_identity(os.getpid()), now(), identity)); db.commit()
                code = 1
                try: code = execute(self.get(identity))
                except KeyboardInterrupt:
                    self.cancel(identity); raise
                except Exception as error:
                    report = self.report(identity); report.update(state="failed", ok=False)
                    report.setdefault("findings", []).append({"code": "QUEUE_EXECUTION_EXCEPTION", "subject": identity,
                        "passed": False, "observed": str(error), "expected": "completed execution"})
                    atomic_json(self.output / identity / "report.json", report)
                state = self.report(identity).get("state", "failed")
                if state not in TERMINAL: state = "failed"
                with self.db() as db: db.execute("UPDATE jobs SET state=?,updated=? WHERE id=?", (state, now(), identity))
                if code == 130: return 130  # Ctrl+C stops the drain; untouched requests remain queued.
        return 0
