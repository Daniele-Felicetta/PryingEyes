"""Independent queue consumer used by process/lifecycle integration checks. Runs on the toy project: the engine, not a game."""
import json
import os
from pathlib import Path
import sys
import time
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
sys.path.insert(0, str(Path(__file__).resolve().parent))
import studio_engine as engine
from studio_process import atomic_json
from studio_queue import Queue
from toy_project import ToyProject

project = ToyProject(sys.argv[1], output=sys.argv[1])


def execute(row):
    mode = row["request"][0]; job = engine.Job(project, "fixture", mode, row)
    marker = job.folder / "children.json"
    program = """import json,os,subprocess,sys,time
child=None
if sys.argv[2] in ('nested','timeout'):
 child=subprocess.Popen([sys.executable,'-c','import time; time.sleep(30)'])
open(sys.argv[1],'w').write(json.dumps({'child':os.getpid(),'grandchild':child.pid if child else None}))
time.sleep(30 if child else .5)
"""
    try:
        with engine.project_lock(project, job):
            with (project.output / "events.jsonl").open("a") as log: log.write(json.dumps([row["id"], "start", time.time()]) + "\n")
            job.run("fixture", [sys.executable, "-c", program, marker, mode], timeout=.8 if mode == "timeout" else 60)
            with (project.output / "events.jsonl").open("a") as log: log.write(json.dumps([row["id"], "end", time.time()]) + "\n")
        job.report.update(state="complete", ok=True)
    except engine.JobCancelled:
        job.report.update(state="cancelled", ok=False)
    except Exception as error:
        job.report.update(state="failed", ok=False)
        job.report["findings"].append({"code": str(error).split(":")[0], "passed": False})
    job.save()
    if mode == "finish_crash": os._exit(9)
    return 0 if job.report["ok"] else 1


Queue(project.output).work(execute)
