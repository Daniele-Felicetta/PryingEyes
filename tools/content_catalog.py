"""Authoritative production data, shared by the CLI and Blender's recipe registry."""
import json
import re
import math
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CATALOG = ROOT / "unity/PryingEyes/Assets/_PryingEyes/Resources/Production/weapons.json"
SCHEMA = CATALOG.with_name("weapons.schema.json")


def read_catalog(path=CATALOG):
    def invalid_number(value):
        raise ValueError(f"NONFINITE_NUMBER: {value}")
    data = json.loads(Path(path).read_text(encoding="utf-8-sig"), parse_constant=invalid_number)
    def finite(value):
        if isinstance(value, float) and not math.isfinite(value): raise ValueError("NONFINITE_NUMBER")
        if isinstance(value, dict):
            for child in value.values(): finite(child)
        elif isinstance(value, list):
            for child in value: finite(child)
    finite(data)
    if not isinstance(data, dict) or data.get("schemaVersion") != 1 or not isinstance(data.get("weapons"), list) or not data["weapons"]:
        raise ValueError("CATALOG_SCHEMA")
    ids = set()
    for index, entry in enumerate(data["weapons"]):
        if not isinstance(entry, dict) or not isinstance(entry.get("definition"), dict) or not isinstance(entry["definition"].get("id"), str):
            raise ValueError("WEAPON_DEFINITION_MISSING")
        identity = entry["definition"]["id"]
        if entry["index"] != index or identity in ids or not re.fullmatch(r"[a-z][a-z0-9_]+", identity):
            raise ValueError(f"WEAPON_ID_OR_INDEX: {identity}")
        ids.add(identity)
    return data


def validate_catalog(path=CATALOG):
    from jsonschema import Draft202012Validator
    data = read_catalog(path)
    schema = json.loads(SCHEMA.read_text(encoding="utf-8"))
    Draft202012Validator.check_schema(schema)
    error = next(Draft202012Validator(schema).iter_errors(data), None)
    if error is not None:
        raise ValueError("CATALOG_SCHEMA: " + ".".join(str(p) for p in error.absolute_path) + ": " + error.message)
    prefix = json.loads((ROOT / "tools/fixtures/weapon-prefix-v1.json").read_text(encoding="utf-8"))
    for expected in prefix["definitions"]:
        index = expected["index"]
        if index >= len(data["weapons"]) or data["weapons"][index]["definition"]["id"] != expected["id"]:
            raise ValueError(f"WEAPON_STABLE_INDEX: {index} must remain {expected['id']}")
    for entry in data["weapons"]:
        definition = entry["definition"]
        if definition["reserve"] > definition["reserveMax"]:
            raise ValueError(f"RESERVE_CAP: {definition['id']}")
        for name in (entry["source"]["recipe"]["file"], *entry["source"]["dependencies"]):
            source = (ROOT / name).resolve()
            if not source.is_relative_to(ROOT) or not source.is_file():
                raise ValueError(f"SOURCE_MISSING: {name}")
    return data


def find_asset(identity, data=None):
    for entry in (data or read_catalog())["weapons"]:
        if entry["definition"]["id"] == identity:
            return entry
    raise ValueError(f"ASSET_UNKNOWN: {identity}")
