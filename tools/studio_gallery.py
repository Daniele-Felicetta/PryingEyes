"""Read-only, portable visual evidence browser. Generation never launches Unity/Blender or validates old evidence as new."""
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import shutil
from PIL import Image
from studio_process import atomic_json

WEB = Path(__file__).with_name("studio_web")
JOB_ID = re.compile(r"\d{8}T\d{6}Z-[a-f0-9]{8}$")
VIEWS = {"lato": "Lato", "34": "Tre quarti", "giocatore": "In mano", "ads": "Mira", "isolated": "Stanza isolata",
         "interior": "Interno nel gioco", "exterior": "Esterno nel gioco"}


def safe_file(path, root):
    try:
        path = (root / path).resolve()
        return path if path.is_relative_to(root) and path.is_file() else None
    except (ValueError, TypeError, OSError): return None


def bundle(assets, graph, reports, output, root, expected_sources=None, loose=None):
    root, output = Path(root).resolve(), Path(output).resolve()
    if not output.is_relative_to(root): raise ValueError("GALLERY_OUTPUT_OUTSIDE_ROOT")
    output.mkdir(parents=True, exist_ok=True)
    data = {"schemaVersion": 1, "generatedAt": datetime.now(timezone.utc).isoformat(), "assets": assets,
            "runs": {}, "nodes": json.loads(json.dumps(list(graph.nodes.values()))), "impacts": {}, "warnings": [], "views": VIEWS}
    for node in data["nodes"]:
        for source in node.get("sources", []):
            for field, pointer in (("impactKey", source.get("pointer")), ("wholeFileKey", None)):
                key = hashlib.sha256(json.dumps([source["path"], pointer], ensure_ascii=False).encode()).hexdigest()[:16]
                source[field] = key
                if key not in data["impacts"]: data["impacts"][key] = graph.affected(source["path"], pointer)
    by_id = {asset["id"]: asset for asset in assets}; hashes = {}
    for asset in assets: asset["runs"] = []

    def copy(path, category, suffix=None):
        digest = hashlib.sha256(path.read_bytes()).hexdigest()
        relative = category + "/" + digest[:24] + (suffix or path.suffix.lower())
        target = output / relative
        if not target.resolve().is_relative_to(output): raise ValueError("GALLERY_LINK_OUTSIDE_BUNDLE")
        target.parent.mkdir(parents=True, exist_ok=True)
        if not target.exists() or hashlib.sha256(target.read_bytes()).hexdigest() != digest: shutil.copy2(path, target)
        return relative

    def image(path):
        if path is None or path.suffix.lower() not in (".png", ".jpg", ".jpeg", ".webp"): return None
        try:
            with Image.open(path) as picture: size = picture.size; picture.verify()
        except (OSError, ValueError): return None
        return {"url": copy(path, "images"), "width": size[0], "height": size[1]}

    def images(folder):
        result = {}
        # Only known filenames in a job directory: no arbitrary file URLs from report contents.
        for identity in by_id:
            candidates = [(folder / (identity + "_" + view + ".png"), view) for view in ("lato", "34", "giocatore", "ads")]
            candidates += [(folder / (identity + ".png"), "isolated")]
            candidates += [(folder / "city-runtime" / (identity + "_" + view + ".png"), view) for view in ("interior", "exterior")]
            for path, view in candidates:
                item = image(safe_file(path, root))
                if item: result.setdefault(identity, {})[view] = item
        return result

    def changed(recorded, expected):
        if not isinstance(recorded, dict) or not recorded or expected is None: return None
        changes = []
        for name in recorded.keys() | expected.keys():
            path = safe_file(name, root)
            if path not in hashes: hashes[path] = hashlib.sha256(path.read_bytes()).hexdigest() if path else None
            if name not in recorded or name not in expected or hashes[path] != recorded.get(name): changes.append(name)
        return sorted(changes)

    for path in sorted(set(map(Path, reports))):
        path = safe_file(path, root)
        if path is None: continue
        try:
            report = json.loads(path.read_text(encoding="utf-8-sig"))
            if not isinstance(report, dict) or report.get("schemaVersion") != 1: continue
            subject = report.get("subject")
            if report.get("operation") == "queue" and isinstance(subject, list) and len(subject) >= 3 and subject[0] == "asset": subject = subject[2]
            pics = images(path.parent)
            if subject not in by_id and not pics: continue
            if report.get("operation") not in ("asset.build", "asset.preview", "queue"): continue
            key = hashlib.sha256(str(path.relative_to(root)).encode()).hexdigest()[:16]
            state = report.get("state", "complete" if report.get("ok") else "failed")
            if state == "complete" and report.get("ok") is not True: state = "failed"
            source_changes = changed(report.get("sources"), expected_sources)
            findings = [f for f in report.get("findings", []) if f.get("passed") is False]
            metrics = []
            state_file = safe_file(path.parent / "weapon-state.json", root)
            if state_file:
                mechanical = json.loads(state_file.read_text())
                checks = mechanical.get("findings", [])
                metrics.append({"label": "Meccanica armi", "value": f"{sum(f.get('passed') is True for f in checks)}/{len(checks)}"})
            gunlog = safe_file(path.parent / "gunplay-player.log", root)
            if gunlog:
                match = re.search(r"gunplaytest RISULTATO \w+: (\d+/\d+)", gunlog.read_text(encoding="utf-8-sig", errors="replace"))
                if match: metrics.append({"label": "Gunplay", "value": match[1]})
            native_file = safe_file(path.parent / "city-runtime/run.json", root)
            build_match = None
            if native_file:
                native = json.loads(native_file.read_text(encoding="utf-8-sig"))
                checks = native.get("native", {}).get("findings", [])
                metrics.append({"label": "Città", "value": f"{sum(f.get('passed') is True for f in checks)}/{len(checks)}"})
                build_match = all(native.get(k) is True for k in ("recipeMatchesBuild", "layoutMatchesBuild", "roomsMatchBuild"))
                if not native.get("ok") and native.get("error"):
                    findings.append({"code": native["error"], "observed": "build o risultato non corrispondente", "expected": "build verificata", "passed": False})
            logs = []
            for step in report.get("steps", []):
                evidence = safe_file(step.get("log", ""), root)
                if evidence: logs.append({"name": step.get("name", "registro"), "url": copy(evidence, "logs", ".txt")})
            run = {"key": key, "id": path.parent.name, "store": path.parent.parent.name, "subject": subject, "state": state,
                "date": report.get("createdAt", path.parent.name[:16]), "images": pics, "metrics": metrics,
                "seconds": round(sum(s.get("seconds", 0) for s in report.get("steps", [])), 1),
                "findings": findings[:60], "failureCount": len(findings), "sourceChanges": source_changes,
                "sourceCount": len(report.get("sources", {})), "buildMatches": build_match,
                "report": copy(path, "reports", ".json"), "logs": logs, "reproduce": report.get("reproduce", [])}
            data["runs"][key] = run
            for identity, asset in by_id.items():
                if identity == subject or identity in pics: asset["runs"].append(key)
        except (OSError, ValueError, TypeError, KeyError, AttributeError) as error:
            data["warnings"].append({"report": str(path.relative_to(root)), "error": str(error)})

    if loose:
        for identity, asset in by_id.items():
            pics = {}
            for view in ("lato", "34", "giocatore", "ads"):
                item = image(safe_file(Path(loose) / (identity + "_" + view + ".png"), root))
                if item: pics[view] = item
            if pics:
                key = "reference-" + identity
                data["runs"][key] = {"key": key, "id": "Riferimento senza rapporto", "state": "unverified", "date": "",
                    "subject": identity, "images": {identity: pics}, "metrics": [], "findings": [], "failureCount": 0,
                    "seconds": None, "sourceChanges": None, "sourceCount": 0, "buildMatches": None, "logs": [], "reproduce": []}
                asset["runs"].append(key)
    for asset in assets:
        # Job IDs use the same UTC timestamp format across direct and queued reports.
        asset["runs"].sort(key=lambda key: data["runs"][key]["id"] if data["runs"][key]["state"] != "unverified" else "", reverse=True)
    atomic_json(output / "manifest.json", data)
    payload = json.dumps(data, ensure_ascii=False).replace("<", "\\u003c").replace(">", "\\u003e").replace("&", "\\u0026").replace("\u2028", "\\u2028").replace("\u2029", "\\u2029")
    html = (WEB / "index.html").read_text(encoding="utf-8")
    fragments = {"STYLE": (WEB / "gallery.css").read_text(encoding="utf-8"), "DATA": payload,
                 "SCRIPT": (WEB / "gallery.js").read_text(encoding="utf-8")}
    html = re.sub(r"__(STYLE|DATA|SCRIPT)__", lambda match: fragments[match[1]], html)
    (output / "index.html").write_text(html, encoding="utf-8")
    return data


def build_gallery(output, graph, weapons, rooms, expected_sources, stores):
    from content_catalog import ROOT
    assets = []
    for entry in weapons["weapons"]:
        d = entry["definition"]
        assets.append({"id": d["id"], "name": d["name"], "type": "weapon", "category": d["cls"],
            "family": entry["source"].get("family", ""), "tags": [*d["modes"], d["style"]],
            "stats": [{"label": "Danno", "value": str(d["damage"])}, {"label": "Colpi/min", "value": str(d["rpm"])},
                      {"label": "Caricatore", "value": str(d["mag"])}, {"label": "Riserva", "value": str(d["reserveMax"])}]})
    for room in rooms["rooms"]:
        assets.append({"id": room["id"], "name": room["name"], "type": "room", "category": "Ambiente", "family": "",
            "tags": [], "stats": [{"label": "Dimensioni", "value": f"{room['width']} × {room['depth']} m"},
                {"label": "Elementi", "value": str(len(room["parts"]))}, {"label": "Uscite", "value": str(len(room["doors"]))}]})
    reports = [p for store in stores for p in Path(store).glob("*/report.json") if JOB_ID.fullmatch(p.parent.name)]
    return bundle(assets, graph, reports, output, root=ROOT, expected_sources=expected_sources, loose=ROOT / "modelli/armi/render")
