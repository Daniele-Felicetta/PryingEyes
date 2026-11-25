"""Boundary guard for the reusable production suite.

Reusable code stays reusable only while something fails when it stops being so. Rules checked here:

  packages   every Unity package under unity/PryingEyes/Packages
             - has a package.json whose name is its folder name, and a README
             - compiles into an assembly with noEngineReferences and no assembly references (no game, no UnityEngine)
             - mentions neither the game nor the engine in code (comments and strings are ignored)
             - is compiled by an engine-free .NET lab under tools/ (the same source file, never a copy), so its rules
               run without the editor
             - is consumed by the independent project (unity/LayoutDemo), which is the proof of reuse
  rules      the game's engine-free rules, unity/PryingEyes/Assets/_PryingEyes/Rules (B2 of docs/engineering/modularization.md)
             - compile into one assembly (PryingEyes.Rules) with noEngineReferences that references no assembly but the
               Production.* packages, so Unity itself refuses an engine reference in these files
             - name no engine, netcode or UI type in code; the game's own names are allowed, which is what makes them rules of this
               game and not a package
             - every file is compiled by an engine-free .NET lab under tools/ (the same source file, never a copy)
             The checking code is the packages' (check_engine_free), with other names allowed and other assemblies referenced.
  engine     the Python production engine (tools/studio_engine.py, its process and queue modules and the dependency
             graph) names no game, no content type and no tool of this project: a project plugs in through its profile
             (tools/studio_pryingeyes.py for this game)
  game       the game's own C# sources (unity/PryingEyes/Assets/_PryingEyes), read through the census (tools/code_census.py)
             - no production file names a test type, and every test file is wrapped in PE_DISTRIBUTION_BUILD, so a
               distribution build can leave the tests out
             - a game file that an engine-free lab compiles lives in Rules/, where the compiler keeps the engine out of it
             - what is wrong today is listed, each with its reason, in tools/fixtures/game-boundaries.json: the list can only
               shrink, an entry whose violation is gone fails (GAME_BASELINE_STALE) until it is deleted, and so does a file
               that is not valid (GAME_BASELINE_INVALID)
  docs       README.md, AGENTS.md, docs/**/*.md and the package READMEs
             - every relative link resolves, anchors included, and every `docs/....md` path named in code spans exists
             - every page under docs/ is listed in docs/README.md, the index
             - every page starts with a title, is valid UTF-8 and holds no replacement character
             - no machine-specific path (a Windows user folder, /home/...): the repository is public and paths are relative to its root
  wiki       the wiki in wiki/, built by tools/wiki_site.py: its pages, links, images and concepts pass, and the files it generates
             (catalogue, concept map, wiki/graph.json, wiki/site) are up to date with the pages

Usage: python tools/check_suite.py [--root PATH]      Exit 0 = PASS, 1 = findings. Run by tools/ship.ps1, the unit suite and CI.
"""
import argparse
import json
from pathlib import Path
import re
import sys
from urllib.parse import unquote
from xml.etree import ElementTree
import code_census  # the census of the game's sources (tools/code_census.py), standard library only
import wiki_site  # the wiki: its generator and its checks (tools/wiki_site.py), standard library only

ROOT = Path(__file__).resolve().parents[1]
PACKAGES = "unity/PryingEyes/Packages"
DOCS = "docs"
GAME = code_census.GAME
RULES = GAME + "/Rules"
BASELINE = "tools/fixtures/game-boundaries.json"
# The rules about the game's sources: the code of each finding and what the rule expects. The baseline can tolerate a violation of any of them.
GAME_RULES = {
    "GAME_PRODUCTION_NAMES_TEST": "production code never names a test type: a test installs itself, so a distribution build can leave it out",
    "GAME_TEST_NOT_EXCLUDED": "a test file is wrapped in #if !PE_DISTRIBUTION_BUILD, so a release does not ship it",
    "GAME_LAB_FILE_OUTSIDE_RULES": "a game file that an engine-free lab compiles lives in Rules/, where the compiler keeps the engine out of it",
}
MD_FENCE = re.compile(r"^[ \t]*(`{3,}|~{3,}).*?^[ \t]*\1[ \t]*$", re.M | re.S)
MD_INLINE_CODE = re.compile(r"`[^`\n]*`")
MD_LINK = re.compile(r"\[[^\]\n]*\]\(<?([^)\s>]+)>?(?:\s+\"[^\"\n]*\")?\)")
MD_HEADING = re.compile(r"^#{1,6}[ \t]+(.+?)[ \t#]*$", re.M)
MD_DOC_PATH = re.compile(r"`(docs/[\w./-]+\.md)`")
REPLACEMENT_CHARACTER = chr(0xFFFD)  # what a decoder writes for a byte it could not read; spelled so this file holds none itself
MACHINE_PATH = re.compile(r"[A-Za-z]:[\\/]+Users[\\/]|/Users/[\w.-]+/|/home/[\w.-]+/")
CONSUMER = "unity/LayoutDemo/Packages/manifest.json"
# Names a package must never use in code: the game, the engine, the netcode and UI libraries of this project.
FORBIDDEN_IN_PACKAGES = re.compile(r"\b(UnityEngine|UnityEditor|PryingEyes|FishNet|TMPro)\b")
# The game's rules may name the game, and nothing of the engine, the netcode or the UI.
FORBIDDEN_IN_RULES = re.compile(r"\b(UnityEngine|UnityEditor|FishNet|TMPro)\b")
# The two kinds of engine-free assembly the guard knows, B1 and B2. They are checked by the same code (check_engine_free) and differ in what
# they may name and in which assemblies they may reference (a package none, the rules the packages), so each brings its findings' codes.
PACKAGE_KIND = {
    "forbidden": FORBIDDEN_IN_PACKAGES, "references": (),
    "codes": {"asmdef": "PACKAGE_ASMDEF_MISSING", "engine": "PACKAGE_ENGINE_REFERENCES", "references": "PACKAGE_ASSEMBLY_REFERENCES",
              "name": "PACKAGE_FORBIDDEN_NAME", "compiled": "PACKAGE_NOT_COMPILED_OUTSIDE_UNITY"},
    "expected": {"references": "no assembly references (a package cannot depend on game code)", "name": "no engine or game names: the package must compile without Unity"},
}
RULES_KIND = {
    "forbidden": FORBIDDEN_IN_RULES, "references": ("Production.",),
    "codes": {"asmdef": "GAME_RULES_ASMDEF_MISSING", "engine": "GAME_RULES_ENGINE_REFERENCES", "references": "GAME_RULES_ASSEMBLY_REFERENCES",
              "name": "GAME_RULES_FORBIDDEN_NAME", "compiled": "GAME_RULES_NOT_COMPILED_OUTSIDE_UNITY"},
    "expected": {"references": "references only Production.* assemblies (the rules cannot depend on gameplay)",
                 "name": "no engine, netcode or UI names (the game's own are allowed): the rules must compile without Unity"},
}
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


def check_engine_free(root, folder, kind, compiled):
    """What a folder that is one engine-free assembly (a package, B1, or the game's rules, B2) must hold: an assembly definition that keeps the
    engine out and references nothing it may not (the compiler is the first guard), no name that its kind forbids in code (comments and strings
    are ignored), and every source file compiled by an engine-free lab from this very path, so that its rules run without the editor."""
    codes, findings = kind["codes"], []
    definitions = sorted(folder.rglob("*.asmdef"))
    if not definitions:
        findings.append(finding(codes["asmdef"], folder.relative_to(root), "no assembly definition", "one .asmdef with noEngineReferences"))
    for definition in definitions:
        data = json.loads(definition.read_text(encoding="utf-8"))
        if data.get("noEngineReferences") is not True:
            findings.append(finding(codes["engine"], definition.relative_to(root), "noEngineReferences is not true", "noEngineReferences: true"))
        stray = [reference for reference in data.get("references") or [] if not reference.startswith(kind["references"])]
        if stray:
            findings.append(finding(codes["references"], definition.relative_to(root), f"references {stray}", kind["expected"]["references"]))
    for source in sorted(folder.rglob("*.cs")):
        used = sorted(set(kind["forbidden"].findall(code_only(source.read_text(encoding="utf-8-sig")))))
        if used:
            findings.append(finding(codes["name"], source.relative_to(root), "uses " + ", ".join(used), kind["expected"]["name"]))
        if source.resolve() not in compiled:
            findings.append(finding(codes["compiled"], source.relative_to(root), "no .NET lab compiles this file",
                                    "a tools/ lab project includes this exact file so its rules run without the editor"))
    return findings


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
        findings += check_engine_free(root, package, PACKAGE_KIND, compiled)
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


def reaches_unity(project, seen=()):
    """Whether a .NET project refers to a Unity assembly: a Reference to one, in the project, in a props file it imports or in a project it references."""
    try: tree = ElementTree.parse(project)
    except (ElementTree.ParseError, OSError): return False
    for item in tree.iter():
        if item.tag == "Reference":
            if "Unity" in (item.get("Include") or "") + "".join(hint.text or "" for hint in item.iter("HintPath")): return True
        elif item.tag in ("Import", "ProjectReference"):
            other = (project.parent / (item.get("Project") or item.get("Include") or "").replace("\\", "/")).resolve()
            if other.is_file() and other not in seen and reaches_unity(other, seen + (project,)): return True
    return False


def lab_sources(root):
    """{source file: lab folders}: what the engine-free labs compile (a name with * or ** takes every file it matches).

    A lab compiles sources and reaches no Unity assembly. A project that does is a compile check against the editor's own libraries: it
    exists to compile engine code, so it proves nothing about what runs without one and is left out."""
    result = {}
    for project in sorted((root / "tools").rglob("*.csproj")):
        if any(part in ("bin", "obj") for part in project.relative_to(root).parts) or reaches_unity(project): continue
        try: tree = ElementTree.parse(project)
        except ElementTree.ParseError: continue
        lab = project.relative_to(root / "tools").parts[0]
        for item in tree.iter("Compile"):
            include = (item.get("Include") or "").replace("\\", "/")
            if not include or "$(" in include: continue
            parts = include.split("/")
            fixed = next((number for number, part in enumerate(parts) if "*" in part or "?" in part), len(parts))
            base = (project.parent / "/".join(parts[:fixed])).resolve()
            for path in [base] if fixed == len(parts) else sorted(base.glob("/".join(parts[fixed:]))) if base.is_dir() else []:
                result.setdefault(path.resolve(), set()).add(lab)
    return result


def check_rules(root):
    """B2, the game's engine-free rules in Rules/: the checks of a package (check_engine_free), with the game's own names allowed and the
    Production.* packages as the only assemblies to reference. A tree without the folder has nothing to check."""
    root = Path(root).resolve()
    folder = root / RULES
    return check_engine_free(root, folder, RULES_KIND, set(lab_sources(root))) if folder.is_dir() else []


def read_baseline(root):
    """The exceptions the guard tolerates, {code: {(file, name): reason}}, and the findings about the fixture itself.

    Files are relative to the game folder. An entry of GAME_PRODUCTION_NAMES_TEST also says which test type the file names."""
    tolerated, findings = {code: {} for code in GAME_RULES}, []
    path = root / BASELINE
    if not path.is_file(): return tolerated, findings

    def invalid(observed):
        findings.append(finding("GAME_BASELINE_INVALID", BASELINE, observed, "a JSON object whose entries each name a file and say why it is tolerated"))

    try: data = json.loads(path.read_text(encoding="utf-8"))
    except ValueError as error: invalid(f"not valid JSON: {error}"); return tolerated, findings
    if not isinstance(data, dict): invalid("not a JSON object"); return tolerated, findings
    for key in sorted(set(data) - set(GAME_RULES) - {"schemaVersion", "about"}): invalid(f"unknown key {key!r}: the rules are " + ", ".join(GAME_RULES))
    for code in GAME_RULES:
        fields = ("file", "reason") + (("names",) if code == "GAME_PRODUCTION_NAMES_TEST" else ())
        entries = data.get(code, [])
        if not isinstance(entries, list): invalid(f"{code} is not a list"); continue
        for number, entry in enumerate(entries, 1):
            if isinstance(entry, dict) and all(isinstance(entry.get(field), str) and entry[field].strip() for field in fields):
                tolerated[code][(entry["file"], entry.get("names", ""))] = entry["reason"]
            else: invalid(f"entry {number} of {code} lacks one of: " + ", ".join(fields))
    return tolerated, findings


def check_game(root):
    """Rules about the game's own sources, from the census: tests stay out of production code and out of releases, and what an
    engine-free lab compiles stays engine-free. A tree without the game has nothing to check."""
    root = Path(root).resolve()
    game = root / GAME
    if not game.is_dir(): return []
    data, (tolerated, findings) = code_census.census(root), read_baseline(root)
    outside = {}      # game files that a lab compiles from outside Rules/: what compiles them, and the engine names they use if there are any
    for path, labs in lab_sources(root).items():
        if game in path.parents and root / RULES not in path.parents and path.is_file():
            used = sorted({match[0] for match in code_census.ENGINE.finditer(code_census.blank(path.read_text(encoding="utf-8-sig", errors="replace")))})
            outside[path.relative_to(game).as_posix()] = f"compiled by {', '.join(sorted(labs))}" + (f" and names {' and '.join(used)}" if used else "")
    observed = {
        "GAME_PRODUCTION_NAMES_TEST": {(rel, name): f"names the test type {name}" for rel, names in data["tests"]["namedByProductionCode"].items() for name in names},
        "GAME_TEST_NOT_EXCLUDED": {(rel, ""): "a test file with no PE_DISTRIBUTION_BUILD in it" for rel in data["tests"]["notExcludedFromDistribution"]},
        "GAME_LAB_FILE_OUTSIDE_RULES": {(rel, ""): what for rel, what in outside.items()},
    }
    for code, expected in GAME_RULES.items():
        for (rel, name), what in sorted(observed[code].items()):
            if (rel, name) not in tolerated[code]: findings.append(finding(code, f"{GAME}/{rel}", what, expected))
        for rel, name in sorted(set(tolerated[code]) - set(observed[code])):
            findings.append(finding("GAME_BASELINE_STALE", BASELINE, f"{code} still lists {rel}{f' ({name})' if name else ''}, which no longer breaks the rule",
                                    "delete the entry in the commit that fixes the violation"))
    return findings


def heading_slugs(text):
    """The anchors GitHub gives the headings of a page (fenced code excluded, repeats numbered)."""
    slugs, seen = set(), {}
    for match in MD_HEADING.finditer(MD_FENCE.sub("", text)):
        title = re.sub(r"!?\[([^\]]*)\]\([^)]*\)", r"\1", match[1])
        slug = re.sub(r"[^\w\- ]", "", title.replace("`", "").replace("*", "").lower().strip()).replace(" ", "-")
        count = seen.get(slug, 0)
        seen[slug] = count + 1
        slugs.add(slug if count == 0 else f"{slug}-{count}")
    return slugs


def doc_links(text):
    """Targets of the [text](target) links outside fenced and inline code."""
    return MD_LINK.findall(MD_INLINE_CODE.sub("", MD_FENCE.sub("", text)))


def check_docs(root):
    docs = root / DOCS
    if not docs.is_dir(): return []
    pages = [root / name for name in ("README.md", "AGENTS.md") if (root / name).is_file()]
    pages += sorted(docs.rglob("*.md")) + sorted((root / PACKAGES).glob("*/README.md"))
    findings, texts, slugs = [], {}, {}
    for page in pages:
        rel = page.relative_to(root)
        try: text = page.read_text(encoding="utf-8")
        except UnicodeDecodeError:
            findings.append(finding("DOC_NOT_UTF8", rel, "the file is not valid UTF-8", "UTF-8 text")); continue
        texts[page.resolve()] = text
        if REPLACEMENT_CHARACTER in text:
            findings.append(finding("DOC_REPLACEMENT_CHARACTER", rel, "contains U+FFFD, a letter that was lost when the file was saved", "the real character"))
        first = next((line for line in text.splitlines() if line.strip()), "")
        if not first.startswith("# "):
            findings.append(finding("DOC_TITLE_MISSING", rel, f"starts with {first[:40]!r}", "a first line '# Title'"))
        machine = MACHINE_PATH.search(text)
        if machine:
            findings.append(finding("DOC_MACHINE_PATH", rel, f"names {machine[0]!r}", "paths relative to the repository root, never one machine's"))
    for page in pages:
        text = texts.get(page.resolve())
        if text is None: continue
        rel = page.relative_to(root)
        for target in doc_links(text):
            if target.startswith(("http://", "https://", "mailto:")): continue
            path_part, _, anchor = unquote(target).partition("#")
            destination = page if not path_part else (root / path_part.lstrip("/") if path_part.startswith("/") else page.parent / path_part).resolve()
            if not destination.exists():
                findings.append(finding("DOC_LINK_BROKEN", rel, f"links to {target}", "a path that exists")); continue
            if anchor and destination.suffix == ".md" and destination.resolve() in texts:
                if destination.resolve() not in slugs: slugs[destination.resolve()] = heading_slugs(texts[destination.resolve()])
                if anchor.lower() not in slugs[destination.resolve()]:
                    findings.append(finding("DOC_ANCHOR_BROKEN", rel, f"links to {target}", "a heading of the target page"))
        for mentioned in sorted(set(MD_DOC_PATH.findall(MD_FENCE.sub("", text)))):
            if not (root / mentioned).is_file():
                findings.append(finding("DOC_PATH_MISSING", rel, f"names `{mentioned}`", "a page that exists"))
    index = (docs / "README.md").resolve()
    if index not in texts:
        return findings + [finding("DOC_INDEX_MISSING", Path(DOCS) / "README.md", "no index page", "docs/README.md lists every page")]
    listed = {(index.parent / unquote(t).partition("#")[0]).resolve() for t in doc_links(texts[index]) if not t.startswith(("http://", "https://", "#"))}
    for page in sorted(docs.rglob("*.md")):
        if page.resolve() != index and page.resolve() not in listed:
            findings.append(finding("DOC_NOT_INDEXED", page.relative_to(root), "not linked from docs/README.md", "every page is listed in the index"))
    return findings


def check_wiki(root):
    """The wiki in wiki/, checked and generated by tools/wiki_site.py. A tree without one has nothing to check."""
    root = Path(root)
    if not (root / "wiki").is_dir(): return []
    outputs, findings = wiki_site.build(root)
    return findings + wiki_site.compare(root, outputs)


def check(root=ROOT):
    root = Path(root).resolve()
    return check_packages(root) + check_rules(root) + check_engine(root) + check_game(root) + check_docs(root) + check_wiki(root)


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
