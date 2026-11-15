"""Accept real display preview/confirm/rollback behavior, restoring Windows display preferences afterwards.

Requires a built Windows player and a desktop; run with the player's other instances closed.
This is an explicit acceptance check, not part of hermetic unit discovery.
"""
import argparse
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--exe", type=Path, default=ROOT / "unity/PryingEyes/Builds/Audit/PryingEyes.exe")
    parser.add_argument("--out", type=Path, default=ROOT / "unity_logs/display-check")
    args = parser.parse_args()
    if sys.platform != "win32":
        parser.error("This acceptance check requires Windows.")
    if not args.exe.is_file():
        parser.error(f"Build missing: {args.exe}")
    import winreg
    key_path = r"Software\DefaultCompany\PryingEyes"
    def display_value(name):
        return name.startswith(("Screenmanager ", "display_", "UnitySelectMonitor"))

    def values(key):
        result = {}
        for i in range(winreg.QueryInfoKey(key)[1]):
            name, value, kind = winreg.EnumValue(key, i)
            if display_value(name):
                result[name] = (value, kind)
        return result

    args.out.mkdir(parents=True, exist_ok=True)
    log = (args.out / "player.log").resolve()
    log.unlink(missing_ok=True)
    with winreg.CreateKeyEx(winreg.HKEY_CURRENT_USER, key_path, 0, winreg.KEY_READ | winreg.KEY_WRITE) as key:
        previous = values(key)
        try:
            process = subprocess.run([str(args.exe.resolve()), "-batchmode", "-displaytest", "-nowaves", "-noenemies",
                                      "-screen-fullscreen", "0", "-screen-width", "1280", "-screen-height", "720",
                                      "-logFile", str(log)], timeout=90, creationflags=subprocess.CREATE_NO_WINDOW)
        finally:
            for name in values(key):
                if name not in previous:
                    winreg.DeleteValue(key, name)
            for name, (value, kind) in previous.items():
                winreg.SetValueEx(key, name, 0, kind, value)
            if values(key) != previous:
                raise RuntimeError("Display preferences were not restored")
            print("DISPLAY PREFS: restored", flush=True)
    text = log.read_text(encoding="utf-8", errors="replace")
    for line in text.splitlines():
        if line.startswith("DISPLAYTEST"):
            print(line)
    return int(process.returncode != 0 or "DISPLAYTEST RESULT PASS" not in text)


if __name__ == "__main__":
    sys.exit(main())
