import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest
from studio_process import file_lock, process_identity
from studio_queue import Queue
from studio_engine import project_lock_path
from fixtures.toy_project import ToyProject

TOOLS = Path(__file__).resolve().parent


def until(check, timeout=15):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        try:
            result = check()
            if result: return result
        except (FileNotFoundError, json.JSONDecodeError): pass
        time.sleep(.05)
    raise AssertionError("Timed out waiting for observed process/job state")


class QueueLifecycle(unittest.TestCase):
    def worker(self, folder):
        stream = (folder / ("worker-" + str(time.time_ns()) + ".log")).open("w")
        child = subprocess.Popen([sys.executable, TOOLS / "fixtures/studio_queue_worker.py", folder], stdout=stream, stderr=subprocess.STDOUT)
        stream.close()
        self.addCleanup(lambda: child.poll() is None and (child.kill(), child.wait()))
        return child

    def children(self, folder, identity):
        path = folder / identity / "children.json"
        data = until(lambda: json.loads(path.read_text()) if path.exists() else None)
        return [(pid, process_identity(pid)) for pid in data.values() if pid is not None]

    def assert_stopped(self, children):
        for pid, token in children:
            self.assertIsNotNone(token)
            until(lambda: process_identity(pid) != token)

    def test_persistent_fifo_queued_cancel_and_worker_exclusion(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory); queue = Queue(folder)
            first, second, third = [queue.submit(["complete"], {}) for _ in range(3)]
            queue.cancel(second)
            worker = self.worker(folder)
            until(lambda: queue.get(first)["state"] == "running")
            competitor = self.worker(folder); self.assertNotEqual(competitor.wait(10), 0)
            self.assertEqual(worker.wait(15), 0)
            rows = Queue(folder).status()
            self.assertEqual([r["state"] for r in rows], ["complete", "cancelled", "complete"])
            events = [json.loads(line) for line in (folder / "events.jsonl").read_text().splitlines()]
            self.assertEqual([(e[0], e[1]) for e in events], [(first, "start"), (first, "end"), (third, "start"), (third, "end")])
            self.assertGreaterEqual(events[2][2], events[1][2])
            self.assertFalse((folder / second / "children.json").exists())

    def test_running_cancel_stops_nested_children_but_not_unrelated_process(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory); queue = Queue(folder); identity = queue.submit(["nested"], {})
            unrelated = subprocess.Popen([sys.executable, "-c", "import time;time.sleep(30)"])
            try:
                worker = self.worker(folder); children = self.children(folder, identity)
                queue.cancel(identity); self.assertEqual(worker.wait(15), 0)
                self.assertEqual(queue.get(identity)["state"], "cancelled")
                self.assert_stopped(children); self.assertIsNone(unrelated.poll())
            finally:
                if unrelated.poll() is None: unrelated.terminate(); unrelated.wait()

    def test_timeout_also_stops_grandchildren(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory); queue = Queue(folder); identity = queue.submit(["timeout"], {})
            worker = self.worker(folder); children = self.children(folder, identity)
            self.assertEqual(worker.wait(15), 0); self.assert_stopped(children)
            self.assertEqual(queue.get(identity)["state"], "failed")
            self.assertEqual(queue.report(identity)["steps"][0]["error"], "STEP_TIMEOUT")

    def test_abrupt_worker_exit_is_observed_and_remaining_queue_resumes(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory); queue = Queue(folder)
            identity = queue.submit(["nested"], {}); following = queue.submit(["complete"], {})
            worker = self.worker(folder); children = self.children(folder, identity)
            worker.kill(); worker.wait(); self.assert_stopped(children)
            self.assertEqual(queue.status(identity)[0]["state"], "interrupted")
            self.assertEqual(queue.get(following)["state"], "queued")
            resumed = self.worker(folder); self.assertEqual(resumed.wait(15), 0)
            self.assertEqual(queue.get(identity)["state"], "interrupted")
            self.assertEqual(queue.get(following)["state"], "complete")

    def test_waiting_for_project_lock_is_cancellable_without_starting_work(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory); queue = Queue(folder); identity = queue.submit(["complete"], {})
            with file_lock(project_lock_path(ToyProject(folder))):
                worker = self.worker(folder)
                until(lambda: queue.report(identity).get("state") == "waiting")
                queue.cancel(identity); self.assertEqual(worker.wait(15), 0)
                self.assertFalse((folder / identity / "children.json").exists())
                self.assertEqual(queue.get(identity)["state"], "cancelled")

    def test_cli_rejects_stale_submitted_sources_before_any_step(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory); queue = Queue(folder); identity = queue.submit(["test", "weapon-state"], {})
            result = subprocess.run([sys.executable, TOOLS / "studio.py", "queue", "run"],
                env={**os.environ, "STUDIO_OUTPUT": str(folder)}, capture_output=True, text=True, timeout=30)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertEqual(queue.get(identity)["state"], "failed")
            report = queue.report(identity)
            self.assertEqual(report["findings"][0]["code"], "QUEUED_SOURCES_CHANGED")
            self.assertEqual(report["steps"], [])

    def test_completed_report_survives_worker_crash_before_queue_acknowledgement(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory); queue = Queue(folder); identity = queue.submit(["finish_crash"], {})
            worker = self.worker(folder); self.assertEqual(worker.wait(15), 9)
            self.assertEqual(queue.get(identity)["state"], "running")
            self.assertEqual(queue.status(identity)[0]["state"], "complete")

    def test_reused_pid_token_is_not_treated_as_the_recorded_owner(self):
        with tempfile.TemporaryDirectory() as directory:
            queue = Queue(directory); identity = queue.submit(["complete"], {})
            with queue.db() as db:
                db.execute("UPDATE jobs SET state='running',owner_pid=?,owner_token='different-birth' WHERE id=?", (os.getpid(), identity))
            self.assertEqual(queue.status(identity)[0]["state"], "interrupted")
            self.assertIsNotNone(process_identity(os.getpid()))

    def test_cli_cancel_and_invalid_queue_requests(self):
        with tempfile.TemporaryDirectory() as directory:
            queue = Queue(directory); identity = queue.submit(["test", "weapon-state"], {})
            env = {**os.environ, "STUDIO_OUTPUT": directory}
            def cli(*args):
                result = subprocess.run([sys.executable, TOOLS / "studio.py", *args], env=env, capture_output=True, text=True, timeout=15)
                return result.returncode, json.loads(result.stdout)
            code, result = cli("job", "cancel", identity)
            self.assertEqual(code, 0); self.assertEqual(result["result"]["state"], "cancelled")
            self.assertEqual(result["result"]["cancel_requested"], 1)
            self.assertEqual(cli("queue", "status", identity)[1]["result"][0]["state"], "cancelled")
            code, result = cli("queue", "add", "queue", "run")
            self.assertEqual(code, 1); self.assertEqual(result["findings"][0]["code"], "QUEUE_OPERATION_NOT_SUPPORTED")
            code, result = cli("job", "cancel", "../outside")
            self.assertEqual(code, 1); self.assertEqual(result["findings"][0]["code"], "JOB_ID_INVALID")


if __name__ == "__main__": unittest.main()
