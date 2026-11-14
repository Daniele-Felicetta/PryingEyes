"""Room source validation and plans produced by the reusable C# layout package."""
import hashlib
import json
import subprocess
from pathlib import Path
from jsonschema import Draft202012Validator
from content_catalog import ROOT

CATALOG = ROOT / "unity/PryingEyes/Assets/_PryingEyes/Resources/Production/rooms.json"
SCHEMA = CATALOG.with_name("rooms.schema.json")


def validate_rooms(path=CATALOG):
    data = json.loads(Path(path).read_text(encoding="utf-8-sig"),
                      parse_constant=lambda value: (_ for _ in ()).throw(ValueError("ROOM_NONFINITE: " + value)))
    schema = json.loads(SCHEMA.read_text())
    Draft202012Validator.check_schema(schema)
    error = next(Draft202012Validator(schema).iter_errors(data), None)
    if error:
        raise ValueError("ROOM_SCHEMA: " + ".".join(map(str, error.absolute_path)) + ": " + error.message)
    for entries in (data["materials"], data["rooms"], *(r["parts"] for r in data["rooms"])):
        if len({entry["id"] for entry in entries}) != len(entries): raise ValueError("ROOM_DUPLICATE_ID")
    materials = {m["id"] for m in data["materials"]}
    if "concrete" not in materials: raise ValueError("ROOM_FLOOR_MATERIAL_MISSING: concrete")
    for room in data["rooms"]:
        for part in room["parts"]:
            if part["material"] not in materials: raise ValueError("ROOM_MATERIAL_MISSING: " + part["id"])
    return data


def compile_room(identity, folder, width=None, depth=None, catalog=CATALOG):
    folder = Path(folder); folder.mkdir(parents=True, exist_ok=True)
    output = folder / (identity + ".plan.json")
    command = ["dotnet", "run", "-c", "Release", "--project", str(ROOT / "tools/planlab"),
               "--", "room", str(catalog), identity, str(output)]
    if width is not None: command.extend([str(width), str(depth)])
    run = subprocess.run(command, cwd=ROOT, capture_output=True, text=True, timeout=90)
    if run.returncode: raise ValueError(run.stderr.strip() or run.stdout.strip() or "ROOM_PLANNER_FAILED")
    return json.loads(output.read_text(encoding="utf-8-sig"))


def check_catalog(folder, catalog=CATALOG, width=None, depth=None):
    """Plan every room of a catalog in one planner run: {room id: {"plan": ...} or {"error": message}}.

    One process for the whole catalog keeps the cost of checking many variants (or every real room) at a few seconds."""
    folder = Path(folder); folder.mkdir(parents=True, exist_ok=True)
    output = folder / "catalog.plans.json"
    command = ["dotnet", "run", "-c", "Release", "--project", str(ROOT / "tools/planlab"),
               "--", "room", str(catalog), "*", str(output)]
    if width is not None: command.extend([str(width), str(depth)])
    run = subprocess.run(command, cwd=ROOT, capture_output=True, text=True, timeout=120)
    if not output.is_file(): raise ValueError(run.stderr.strip() or run.stdout.strip() or "ROOM_PLANNER_FAILED")
    return json.loads(output.read_text(encoding="utf-8-sig"))


def compile_rooms(recipe, layout, folder):
    catalog = validate_rooms(); known = {r["id"] for r in catalog["rooms"]}
    blocks = {b["id"]: b for b in layout["blocks"]}; seen = set(); result = []
    bindings = recipe.get("rooms", [])
    if not isinstance(bindings, list): raise ValueError("ROOM_BINDINGS_INVALID")
    for binding in bindings:
        if not isinstance(binding, dict) or set(binding) != {"block", "asset"}: raise ValueError("ROOM_BINDING_INVALID")
        block, identity = binding["block"], binding["asset"]
        if not isinstance(block, str) or not isinstance(identity, str) or block not in blocks or block in seen or identity not in known:
            raise ValueError("ROOM_BINDING_UNKNOWN_OR_DUPLICATE: " + str(binding))
        seen.add(block); b = blocks[block]
        try: plan = compile_room(identity, Path(folder) / block, b["width"], b["depth"])
        except ValueError as error:      # say which room and block, so a too-small district points at the room that cannot fit
            raise ValueError("ROOM_DOES_NOT_FIT: %s on %s at %g x %g m: %s" % (identity, block, b["width"], b["depth"], error)) from None
        result.append({"id": identity, "block": block, "sourceSha256": hashlib.sha256(CATALOG.read_bytes()).hexdigest(), "plan": plan})
    return sorted(result, key=lambda entry: entry["block"])
