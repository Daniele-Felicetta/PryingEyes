"""Walk generated Torre floors with the real player controller and report stuck spots, unreachable doors/candles, soft-locks, falls.

Each process runs the strict bot (-procbot -botstrict, no enemies by default): it walks with the real CharacterController, no
recovery teleports, buys every paid door (the wave cycle, the default; --legacy: lights every candle instead) and takes the lift
through the same selection the F key uses (range, view direction, clear line of sight), for --floors floors starting at --floor.
In the wave cycle the bot walks on the navmesh without the sealed doors, like the enemies do, and pauses after each purchase until
the shutter is up. The last line of a bot log is "ESITO: ok" or "ESITO: FALLITO".
Logs: <out>/nav_<seed>.txt (bot) and nav_<seed>_player.log (Unity). Exit status 0 only when every run ends with "ESITO: ok".
"""
import argparse
import os
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
import re
import subprocess
import sys
import time

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
parser.add_argument("--exe", type=Path, default=root / "unity/PryingEyes/Builds/Audit/PryingEyes.exe")
parser.add_argument("--out", type=Path, default=root / "unity_logs/navigation")
parser.add_argument("--seeds", type=int, nargs="+", default=[1000, 2961, 3046, 777, 114, 4242])
parser.add_argument("--floor", type=int, default=1, help="first floor of each run")
parser.add_argument("--floors", type=int, default=5, help="floors to complete in each run")
parser.add_argument("--scale", type=int, default=4, help="game time multiplier (1..8)")
parser.add_argument("--cap", type=int, default=420, help="game seconds allowed per floor")
parser.add_argument("--jobs", type=int, default=6, help="parallel processes")
parser.add_argument("--timeout", type=int, default=1500, help="real seconds allowed per process")
parser.add_argument("--enemies", action="store_true",
                    help="leave the enemies in (default: empty floors, navigation only); in the wave cycle this turns the waves on (-rounds)")
parser.add_argument("--legacy", action="store_true", help="the old candle cycle (-candles) instead of the wave cycle: the bot lights candles, no paid doors")
parser.add_argument("--oldplan", action="store_true", help="legacy wing-based plans instead of the room grid (implies the candle cycle)")
parser.add_argument("--extra", action="append", default=[], metavar="FLAG",
                    help="extra player argument, e.g. --extra=-botfreeze (negative control: a motionless player must be reported stuck)")
args = parser.parse_args()
if not args.exe.is_file():
    parser.error(f"Build missing: {args.exe}")
args.out.mkdir(parents=True, exist_ok=True)
NOISE = re.compile(r"UnityEngine\.Rendering\.(Universal|RenderGraph)")


def player_exceptions(text):
    """Exceptions in the Unity log, minus the URP render-graph errors that -nographics always prints."""
    lines = text.splitlines()
    found = []
    for i, line in enumerate(lines):
        if re.match(r"^\w*Exception: ", line) and not NOISE.search("\n".join(lines[i:i + 4])):
            found.append(line)
    return found


def run(seed):
    bot_log = args.out.resolve() / f"nav_{seed}.txt"
    player_log = args.out.resolve() / f"nav_{seed}_player.log"
    bot_log.unlink(missing_ok=True)
    command = [str(args.exe.resolve()), "-batchmode", "-nographics", "-autotest", "-procbot", "-botstrict",
               "-seed", str(seed), "-floor", str(args.floor), "-botfloors", str(args.floors), "-botscale", str(args.scale),
               "-botcap", str(args.cap), "-procsecs", str(args.cap * args.floors + 120),
               "-botlog", str(bot_log), "-report", str(args.out.resolve() / f"nav_{seed}_report.txt"),
               "-logFile", str(player_log)]
    if not args.enemies:
        command.append("-noenemies")
    elif not (args.legacy or args.oldplan):
        command.append("-rounds")           # the bot runs without waves unless asked (RoundDirector.Enabled)
    if args.legacy:
        command.append("-candles")
    if args.oldplan:
        command.append("-oldplan")
    command += args.extra
    flags = getattr(subprocess, "CREATE_NO_WINDOW", 0) | getattr(subprocess, "BELOW_NORMAL_PRIORITY_CLASS", 0)
    started = time.time()
    proc = subprocess.Popen(command, creationflags=flags)
    timed_out = False
    try:
        proc.wait(timeout=args.timeout)
    except subprocess.TimeoutExpired:
        timed_out = True
        subprocess.run(["taskkill", "/F", "/T", "/PID", str(proc.pid)], capture_output=True)
    lines = bot_log.read_text(encoding="utf-8", errors="replace").splitlines() if bot_log.is_file() else []
    exceptions = player_exceptions(player_log.read_text(encoding="utf-8", errors="replace")) if player_log.is_file() else []
    verdict = next((l for l in reversed(lines) if l.startswith("ESITO")), None)
    return {"seed": seed, "lines": lines, "verdict": verdict, "timed_out": timed_out, "exceptions": exceptions,
            "seconds": time.time() - started, "code": proc.returncode}


ISSUES = ("INCASTRATO", "VARCO", "IRRAGGIUNGIBILE", "NAVMESH", "NONSELEZIONABILE", "CADUTA", "MORTE", "TEMPO", "SOFTLOCK", "USCITA")
failed = 0
with ThreadPoolExecutor(max_workers=max(1, args.jobs)) as pool:
    results = list(pool.map(run, args.seeds))
for r in results:
    ok = r["code"] == 0 and r["verdict"] is not None and r["verdict"].startswith("ESITO: ok") and not r["timed_out"] and not r["exceptions"]
    floors = [l for l in r["lines"] if l.startswith("PIANO") and "COMPLETATO" in l]
    print(f"seed {r['seed']}: {'OK' if ok else 'FALLITO'} in {r['seconds']:.0f} s, {len(floors)} piani completati, uscita {r['code']}", flush=True)
    for l in r["lines"]:
        if l.startswith("PIANO") or l.startswith(ISSUES):
            print("   " + l)
    if r["verdict"] is None:
        print("   nessuna riga ESITO" + (" (tempo scaduto)" if r["timed_out"] else ""))
    elif not ok:
        print("   " + r["verdict"])
    for e in r["exceptions"][:5]:
        print("   eccezione: " + e)
    failed += 0 if ok else 1
print(f"{len(results) - failed}/{len(results)} esecuzioni riuscite", flush=True)
sys.exit(1 if failed else 0)
