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
import wiki_site  # the wiki: its generator and its checks (tools/wiki_site.py), standard library only

ROOT = Path(__file__).resolve().parents[1]
PACKAGES = "unity/PryingEyes/Packages"
DOCS = "docs"
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
    return check_packages(root) + check_engine(root) + check_docs(root) + check_wiki(root)


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
