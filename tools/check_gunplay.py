"""Verify Unity gunplay and movement, or render the optics alignment check."""
import argparse
from pathlib import Path
import subprocess
import sys

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--exe", type=Path, default=root / "unity/PryingEyes/Builds/Audit/PryingEyes.exe")
parser.add_argument("--out", type=Path)
parser.add_argument("--optics", action="store_true", help="Render the reticles against the hitscan axis (requires graphics).")
args = parser.parse_args()
suite = "opticstest" if args.optics else "gunplaytest"
args.out = args.out or root / ("unity_logs/optics" if args.optics else "unity_logs/gunplay")
if not args.exe.is_file():
    parser.error(f"Build missing: {args.exe}")
args.out.mkdir(parents=True, exist_ok=True)
log = args.out.resolve() / "player.log"
command = [str(args.exe.resolve()), "-batchmode", "-autotest", "-nofire", "-" + suite,
           "-seed", "1000", "-logFile", str(log)]
command += ["-opticout", str(args.out.resolve())] if args.optics else ["-nographics"]
try:
    result = subprocess.run(command, timeout=180, creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
except subprocess.TimeoutExpired:
    print(f"{suite} timed out after 180 seconds.", flush=True)
    sys.exit(1)
lines = log.read_text(encoding="utf-8", errors="replace").splitlines()
for line in lines:
    if suite in line or "Exception" in line:
        print(line)
passed = any(suite + " RISULTATO SUPERATA:" in line for line in lines)
sys.exit(result.returncode if result.returncode else 0 if passed else 1)
