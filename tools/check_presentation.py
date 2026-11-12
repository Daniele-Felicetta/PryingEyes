"""Exercise the real Unity menus, player settings, touch input and party HUD."""
import argparse
from pathlib import Path
import subprocess
import sys

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--exe", type=Path, default=root / "unity/PryingEyes/Builds/Audit/PryingEyes.exe")
parser.add_argument("--out", type=Path)
parser.add_argument("--width", type=int, default=1280)
parser.add_argument("--height", type=int, default=720)
parser.add_argument("--shots", action="store_true", help="Render menu and party HUD screenshots (requires graphics).")
args = parser.parse_args()
if args.width < 320 or args.height < 180:
    parser.error("Use a resolution of at least 320x180.")
args.out = args.out or root / ("unity_logs/presentation" if (args.width, args.height) == (1280, 720)
                               else f"unity_logs/presentation-{args.width}x{args.height}")
if not args.exe.is_file():
    parser.error(f"Build missing: {args.exe}")
args.out.mkdir(parents=True, exist_ok=True)
command = [str(args.exe.resolve()), "-batchmode", "-uitest", "-seed", "1000",
           "-screen-fullscreen", "0", "-screen-width", str(args.width), "-screen-height", str(args.height),
           "-uiout", str(args.out.resolve()), "-logFile", str((args.out / "player.log").resolve())]
command += ["-uishots"] if args.shots else ["-nographics"]
try:
    result = subprocess.run(command, timeout=180, creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
except subprocess.TimeoutExpired:
    print("Presentation check timed out after 180 seconds.", flush=True)
    sys.exit(1)
lines = (args.out / "player.log").read_text(encoding="utf-8", errors="replace").splitlines()
for line in lines:
    if "uitest" in line or "Exception" in line:
        print(line)
passed = any("uitest RISULTATO SUPERATA:" in line for line in lines)
sys.exit(result.returncode if result.returncode else 0 if passed else 1)
