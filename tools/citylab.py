"""Validate and preview the city recipe without launching Unity.

Usage: python tools/citylab.py [--out unity_logs/city_preview.svg]
The SVG marks entrances, weapon panels, and enemy spawns. Exit 1 on invalid layout.
"""
import argparse
import hashlib
import sys
import json
import math
import os
import re
import shutil
import subprocess
from pathlib import Path
from content_catalog import validate_catalog
from room_catalog import compile_rooms

ROOT = Path(__file__).resolve().parents[1]
RECIPE = ROOT / "unity/PryingEyes/Assets/_PryingEyes/City/citta.json"


def missing_assets(weapons, families, root=ROOT):
    """Blender outputs Unity needs before it can import the family weapons among `weapons`.

    This is production readiness, not recipe validity: it depends on the files one machine has generated.
    """
    suffixes = ("_base.jpg", "_mask.png", "_normal.jpg")
    return [file for weapon in weapons if weapon in families
            for file in (root / f"modelli/armi/export/{weapon}.fbx", *(root / f"modelli/armi/tex/{weapon}{suffix}" for suffix in suffixes))
            if not file.is_file()]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--recipe", type=Path, default=RECIPE)
    parser.add_argument("--runtime", type=Path, help="Run the native city acceptance test with this executable")
    parser.add_argument("--timeout", type=float, default=None,
                        help="Native test timeout in seconds (default: 120 plus 80 per bound room: each room walks its route and photographs its views in real time)")
    parser.add_argument("--out", type=Path, default=ROOT / "unity_logs/city_preview.svg")
    parser.add_argument("--report", type=Path, default=ROOT / "unity_logs/city_report.json")
    parser.add_argument("--build-assets", action="store_true", help="Bake and pack the city weapons before validation")
    parser.add_argument("--require-assets", action="store_true",
                        help="Fail when a family weapon's generated model or textures are missing (needs this machine's Blender outputs)")
    args = parser.parse_args()
    try:
        r = json.loads(args.recipe.read_text(encoding="utf-8"))
        required = ("halfWidth", "halfDepth", "roadHalfWidth", "seed", "blocksX", "blocksZ", "district", "shopWeapons")
        if not isinstance(r, dict) or any(key not in r for key in required):
            raise ValueError("Recipe must contain district, seed, bounds, block counts and shopWeapons")
        if any(isinstance(r[key], bool) or not isinstance(r[key], (int, float)) or not math.isfinite(r[key])
               for key in ("halfWidth", "halfDepth", "roadHalfWidth")):
            raise ValueError("District dimensions must be finite numbers")
        if not isinstance(r["shopWeapons"], list) or any(not isinstance(id, str) for id in r["shopWeapons"]):
            raise ValueError("shopWeapons must be an array of weapon IDs")
    except (OSError, ValueError, TypeError) as error:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps({"schemaVersion": 1, "operation": "city.validate", "ok": False,
            "findings": [{"code": "RECIPE_INVALID", "subject": str(args.recipe), "observed": str(error),
                          "expected": "valid city recipe", "passed": False}]}, indent=2), encoding="utf-8")
        print("CITY INVALID:", error)
        return 1
    catalog = validate_catalog()
    families = {e["definition"]["id"] for e in catalog["weapons"] if e["source"].get("family")}
    if args.build_assets:
        blender = os.environ.get("BLENDER_EXE") or shutil.which("blender")
        if not blender and os.name == "nt":
            blender = r"C:\Program Files\Blender Foundation\Blender 4.5\blender.exe"
        if not blender or not Path(blender).is_file():
            parser.error("Blender 4.5 not found: set BLENDER_EXE")
        source = ROOT / "modelli/armi"
        for identity in r["shopWeapons"]:
            if identity in families:
                subprocess.run([blender, "-b", "-P", "genera_armi.py", "--", "bake", identity], cwd=source, check=True)
                subprocess.run([sys.executable, "pack_tex.py", identity], cwd=source, check=True)
    w, d, road = (float(r[k]) for k in ("halfWidth", "halfDepth", "roadHalfWidth"))
    errors = []
    if w < 25 or d < 22 or road < 4 or r["blocksX"] != 2 or r["blocksZ"] != 2:
        errors.append("District bounds, road clearance or 2x2 block count invalid")
    shops = r.get("shopWeapons", [])
    if len(shops) != 3 or len(set(shops)) != 3:
        errors.append("Expected three different weapon IDs")
    registered = {e["definition"]["id"] for e in catalog["weapons"]}
    for weapon in shops:
        if weapon not in registered:
            errors.append(f"Weapon is not registered: {weapon}")
    if args.require_assets or args.build_assets:
        errors.extend(f"Asset missing: {file}" for file in missing_assets(shops, families))

    layout_file = args.report.parent / "city_layout.json"
    layout_file.parent.mkdir(parents=True, exist_ok=True)
    if layout_file.exists():
        layout_file.unlink()
    try:
        layout_run = subprocess.run(["dotnet", "run", "-c", "Release", "--project", str(ROOT / "tools/planlab"),
            "--", "city", str(args.recipe.resolve()), str(layout_file.resolve())], cwd=ROOT, capture_output=True, text=True, timeout=90)
        if layout_run.returncode:
            errors.append(layout_run.stderr.strip() or layout_run.stdout.strip() or "Layout planner failed")
        else:
            layout = json.loads(layout_file.read_text(encoding="utf-8-sig"))
            rooms = compile_rooms(r, layout, args.report.parent / "rooms")
    except (OSError, ValueError, TypeError, subprocess.TimeoutExpired) as error:
        errors.append(str(error))
    if errors:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps({"schemaVersion": 1, "operation": "city.validate", "ok": False, "errors": errors,
            "findings": [{"code": "LAYOUT_INVALID", "subject": r["district"], "observed": error, "expected": "valid city layout", "passed": False} for error in errors]}, indent=2), encoding="utf-8")
        for error in errors:
            print("CITY INVALID:", error)
        return 1

    scale = 10
    width, height = 2 * w * scale, 2 * d * scale

    def xy(x, z):
        return (x + w) * scale, (d - z) * scale

    def rect(x, z, rw, rd, fill, stroke="none"):
        px, py = xy(x - rw / 2, z + rd / 2)
        return f'<rect x="{px:.1f}" y="{py:.1f}" width="{rw * scale:.1f}" height="{rd * scale:.1f}" fill="{fill}" stroke="{stroke}" stroke-width="2"/>'

    svg = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{width:.0f}" height="{height:.0f}" viewBox="0 0 {width:.0f} {height:.0f}">',
           f'<rect width="100%" height="100%" fill="#31343b"/>',
           rect(0, 0, road * 2, d * 2, "#51545b"), rect(0, 0, w * 2, road * 2, "#51545b")]
    for block in layout["blocks"]:
        pavement, building, entrance = (block[key] for key in ("pavement", "building", "entrance"))
        svg.append(rect(pavement["x"], pavement["z"], block["pavementWidth"], block["pavementDepth"], "#82827e"))
        svg.append(rect(building["x"], building["z"], block["width"], block["depth"], "#765048", "#cca879"))
        ex, ez = xy(entrance["x"], entrance["z"])
        svg.append(f'<circle cx="{ex:.1f}" cy="{ez:.1f}" r="8" fill="#5ce0a8"/>')
    for panel, label in zip(layout["panels"], shops):
        px, py = xy(panel["position"]["x"], panel["position"]["z"])
        svg.append(f'<circle cx="{px:.1f}" cy="{py:.1f}" r="7" fill="#ffcf54"/>')
        svg.append(f'<text x="{px + 10:.1f}" y="{py - 6:.1f}" fill="white" font-size="12">{label}</text>')
    for point in layout["spawns"]:
        px, py = xy(point["x"], point["z"])
        svg.append(f'<circle cx="{px:.1f}" cy="{py:.1f}" r="6" fill="#e86d74"/>')
    svg.append('</svg>')
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text("\n".join(svg), encoding="utf-8")
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps({"schemaVersion": 1, "operation": "city.validate", "ok": True, "recipeSha256": hashlib.sha256(args.recipe.read_bytes()).hexdigest(), "seed": r["seed"], "buildings": 4,
                                      "entrances": 4, "weapons": shops, "spawns": 4,
                                      "preview": str(args.out), "layout": str(layout_file), "rooms": rooms}, indent=2), encoding="utf-8")
    print(f"CITY OK: {r['district']}; seed={r['seed']}; 4 buildings, 4 entrances, 3 weapons, 4 spawns; {args.out}")
    if args.runtime:
        timeout = args.timeout if args.timeout is not None else 120 + 80 * len(rooms or [])
        return run_native(args.runtime.resolve(), args.report.parent / "city-runtime", timeout, args.recipe, layout, rooms)
    return 0


def same_layout(expected, observed):
    if isinstance(expected, dict):
        return isinstance(observed, dict) and expected.keys() == observed.keys() and all(same_layout(v, observed[k]) for k, v in expected.items())
    if isinstance(expected, list):
        return isinstance(observed, list) and len(expected) == len(observed) and all(same_layout(a, b) for a, b in zip(expected, observed))
    if isinstance(expected, (float, int)) and not isinstance(expected, bool):
        return isinstance(observed, (float, int)) and math.isclose(expected, observed, rel_tol=0, abs_tol=0.00001)
    return expected == observed


def run_native(exe, output, timeout, recipe, layout=None, rooms=None):
    """Keep binary identity and process outcome beside the native findings and screenshots."""
    output = output.resolve(); output.mkdir(parents=True, exist_ok=True)
    command = [str(exe), "-batchmode", "-autotest", "-nofire", "-citytest", "-cityshots",
               "-screen-width", "1280", "-screen-height", "720", "-cityout", str(output),
               "-report", str(output / "telemetry.txt"), "-logFile", str(output / "player.log")]
    report_path = output / "report.json"
    if report_path.exists():
        report_path.unlink()  # A stale passing report must never validate a failed or timed-out process.
    result = {"schemaVersion": 1, "operation": "city.runtime", "ok": False,
              "command": command, "requestedRecipeSha256": hashlib.sha256(recipe.read_bytes()).hexdigest()}
    try:
        result["exeSha256"] = hashlib.sha256(exe.read_bytes()).hexdigest()
        assembly = exe.parent / "PryingEyes_Data/Managed/Assembly-CSharp.dll"
        result["assemblySha256"] = hashlib.sha256(assembly.read_bytes()).hexdigest()
        child = subprocess.run(command, cwd=ROOT, timeout=timeout, creationflags=0x08000000 if os.name == "nt" else 0)
        result["exitCode"] = child.returncode
        if report_path.is_file():
            result["native"] = json.loads(report_path.read_text(encoding="utf-8"))
            result["recipeMatchesBuild"] = result["native"].get("recipeSha256") == result["requestedRecipeSha256"]
            result["layoutMatchesBuild"] = layout is not None and same_layout(layout, result["native"].get("layout"))
            result["roomsMatchBuild"] = rooms is not None and same_layout(rooms, sorted(result["native"].get("rooms", []), key=lambda r: r["block"]))
            result["survivalMatchesBuild"] = same_layout(json.loads(recipe.read_text(encoding="utf-8")).get("survival"), result["native"].get("survival"))
            result["accessMatchesBuild"] = same_layout(json.loads(recipe.read_text(encoding="utf-8")).get("areas"), result["native"].get("areas"))
            result["ok"] = child.returncode == 0 and result["native"].get("ok") is True and all(result[k] for k in ("recipeMatchesBuild", "layoutMatchesBuild", "roomsMatchBuild", "survivalMatchesBuild", "accessMatchesBuild"))
            if not all(result[k] for k in ("recipeMatchesBuild", "layoutMatchesBuild", "roomsMatchBuild", "survivalMatchesBuild", "accessMatchesBuild")):
                result["error"] = "BUILD_LAYOUT_OR_RECIPE_MISMATCH"
        else:
            result["error"] = "RUNTIME_REPORT_MISSING"
    except subprocess.TimeoutExpired:
        result["error"] = "RUNTIME_TIMEOUT"
    except (OSError, ValueError) as error:
        result["error"] = str(error)
    (output / "run.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    print("CITY RUNTIME:", "PASS" if result["ok"] else "FAIL", output / "run.json")
    return 0 if result["ok"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
