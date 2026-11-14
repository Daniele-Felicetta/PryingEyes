"""Boundary guard for the reusable production suite.

Reusable code stays reusable only while something fails when it stops being so. Rules checked here:

  packages   every Unity package under unity/PryingEyes/Packages
             - has a package.json whose name is its folder name, and a README
             - compiles into an assembly with noEngineReferences and no assembly references (no game, no UnityEngine)
             - mentions neither the game nor the engine in code (comments and strings are ignored)
             - is compiled by an engine-free .NET lab under tools/ (the same source file, never a copy), so its rules
               run without the editor
             - is consumed by the independent project (unity/LayoutDemo), which is the proof of reuse
  engine     the Python production engine (tools/studio_engine.py, its process and queue modules and the dependency
             graph) names no game, no content type and no tool of this project: a project plugs in through its profile
             (tools/studio_pryingeyes.py for this game)

Usage: python tools/check_suite.py [--root PATH]      Exit 0 = PASS, 1 = findings. Run by tools/ship.ps1 and the unit suite.
"""
import argparse
import json
from pathlib import Path
import re
import sys
from xml.etree import ElementTree

ROOT = Path(__file__).resolve().parents[1]
PACKAGES = "unity/PryingEyes/Packages"
CONSUMER = "unity/LayoutDemo/Packages/manifest.json"
# Names a package must never use in code: the game, the engine, the netcode and UI libraries of this project.
FORBIDDEN_IN_PACKAGES = re.compile(r"\b(UnityEngine|UnityEditor|PryingEyes|FishNet|TMPro)\b")
# Modules that make up the generic engine, and the words that would tie them to one project.
ENGINE_MODULES = ("studio_engine.py", "studio_process.py", "studio_queue.py", "asset_graph.py")
FORBIDDEN_IN_ENGINE = re.compile(r"PryingEyes|prying-eyes|Prying Eyes|weapon|blender|unity|citta|\bcity\b|genera_armi|dotnet", re.I)
STRINGS_AND_COMMENTS = re.compile(r'//[^\n]*|/\*.*?\*/|@"(?:[^"]|"")*"|"(?:\\.|[^"\\\n])*"', re.S)


def finding(code, path, observed, expected):
    return {"code": code, "path": str(path).replace("\\", "/"), "observed": observed, "expected": expected}


def code_only(text):
    return STRINGS_AND_COMMENTS.sub(" ", text)


def compiled_sources(root):
    """Every file some .csproj under tools/ compiles, resolved against that project's folder."""
    result = set()
    for project in (root / "tools").rglob("*.csproj"):
        if any(part in ("bin", "obj") for part in project.parts): continue
        try: tree = ElementTree.parse(project)
        except ElementTree.ParseError: continue
        for item in tree.iter("Compile"):
            include = item.get("Include")
            if include: result.add((project.parent / include.replace("\\", "/")).resolve())
    return result


def consumed_packages(root):
    """Package folders the independent consumer depends on through file: references."""
    manifest = root / CONSUMER
    if not manifest.is_file(): return set()
    dependencies = json.loads(manifest.read_text(encoding="utf-8")).get("dependencies", {})
    return {(manifest.parent / value[5:]).resolve() for value in dependencies.values() if str(value).startswith("file:")}


def check_packages(root):
    findings, compiled, consumed = [], compiled_sources(root), consumed_packages(root)
    folder = root / PACKAGES
    for manifest in sorted(folder.glob("*/package.json")) if folder.is_dir() else []:
        package = manifest.parent; name = package.name
        try: declared = json.loads(manifest.read_text(encoding="utf-8")).get("name")
        except ValueError: declared = None
        if declared != name:
            findings.append(finding("PACKAGE_NAME", manifest.relative_to(root), f"name is {declared!r}", f"name equals the folder name {name!r}"))
        if not (package / "README.md").is_file():
            findings.append(finding("PACKAGE_README_MISSING", package.relative_to(root), "no README.md", "README.md says what the package is for and how to consume it"))
        definitions = sorted(package.rglob("*.asmdef"))
        if not definitions:
            findings.append(finding("PACKAGE_ASMDEF_MISSING", package.relative_to(root), "no assembly definition", "one .asmdef with noEngineReferences"))
        for definition in definitions:
            data = json.loads(definition.read_text(encoding="utf-8"))
            if data.get("noEngineReferences") is not True:
                findings.append(finding("PACKAGE_ENGINE_REFERENCES", definition.relative_to(root), "noEngineReferences is not true", "noEngineReferences: true"))
            if data.get("references"):
                findings.append(finding("PACKAGE_ASSEMBLY_REFERENCES", definition.relative_to(root), f"references {data['references']}", "no assembly references (a package cannot depend on game code)"))
        for source in sorted(package.rglob("*.cs")):
            used = sorted(set(FORBIDDEN_IN_PACKAGES.findall(code_only(source.read_text(encoding="utf-8-sig")))))
            if used:
                findings.append(finding("PACKAGE_FORBIDDEN_NAME", source.relative_to(root), "uses " + ", ".join(used), "no engine or game names: the package must compile without Unity"))
            if source.resolve() not in compiled:
                findings.append(finding("PACKAGE_NOT_COMPILED_OUTSIDE_UNITY", source.relative_to(root), "no .NET lab compiles this file",
                                        "a tools/ lab project includes this exact file so its rules run without the editor"))
        if package.resolve() not in consumed:
            findings.append(finding("PACKAGE_NOT_CONSUMED", package.relative_to(root), f"{CONSUMER} has no file: dependency on it", "the independent project consumes every package"))
    return findings


def check_engine(root):
    findings = []
    for name in ENGINE_MODULES:
        path = root / "tools" / name
        if not path.is_file():
            findings.append(finding("ENGINE_MODULE_MISSING", Path("tools") / name, "file not found", "engine module present"))
            continue
        for number, line in enumerate(path.read_text(encoding="utf-8-sig").splitlines(), 1):
            match = FORBIDDEN_IN_ENGINE.search(line)
            if match:
                findings.append(finding("ENGINE_NAMES_A_PROJECT", f"tools/{name}:{number}", f"mentions {match[0]!r}: {line.strip()[:100]}",
                                        "engine modules know jobs, locks, queues and reports, never one project's content or tools"))
    return findings


def check(root=ROOT):
    root = Path(root).resolve()
    return check_packages(root) + check_engine(root)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--root", type=Path, default=ROOT)
    findings = check(parser.parse_args(argv).root)
    for item in findings:
        print(f"{item['code']}: {item['path']}: {item['observed']} (expected: {item['expected']})")
    print("SUITE:", "FAIL" if findings else "PASS", f"({len(findings)} findings)")
    return 1 if findings else 0


if __name__ == "__main__":
    sys.exit(main())
