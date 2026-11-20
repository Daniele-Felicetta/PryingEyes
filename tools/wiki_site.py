"""The Prying Eyes wiki: Markdown pages that are checked, and the readers built from them.

wiki/**/*.md        the pages: a front matter block, then Markdown in English. The source of truth.
wiki/img/           the images the pages show: thumbnails made by tools/wiki_images.py from its manifest.
wiki/concepts.json  the concept map: concepts, each with a page, and labelled relations between them.

Built from those, committed and checked for staleness:
  wiki/catalog.md       every page by class and kind: the table of contents for people and agents
  wiki/graph.json       every page with its links in and out, image and sources: for tools and agents
  wiki/concept-map.md   the Mermaid diagram of concepts.json, between its two markers
  wiki/site/            the static site for people: open wiki/site/index.html (no server, no network)

Usage:
  python tools/wiki_site.py            regenerate the generated files and the site
  python tools/wiki_site.py --check    exit 1 on a broken link or anchor, a missing image or source, bad front matter, an
                                       orphan page, a missing "Standard and desert" section, or a generated file out of date

Front matter: one `key: value` per line between two lines of `---`.
  title     required; equal to the first heading of the page
  kind      required; one of KINDS below (the kind fixes the folder and the class)
  mode      standard | desert | both | none (hub pages: none)
  summary   required; one line, shown in the catalogue and in search
  image     required on every page but the hubs: a file under wiki/, shown by the body as well
  caption   optional: the caption of the image on the site
  sources   optional: comma-separated repository paths, each must exist
  tests     optional: the flags or scripts that check what the page says
"""
from __future__ import annotations

import argparse
import html
import json
import os
import re
import shutil
import sys
from collections import defaultdict, deque
from pathlib import Path
from urllib.parse import unquote

ROOT = Path(__file__).resolve().parents[1]
ASSETS = Path(__file__).resolve().parent / "wiki_assets"
REPO_URL = "https://github.com/Daniele-Felicetta/PryingEyes"
REPO_BRANCH = "preview"
MAP_BEGIN = "<!-- concept-map:begin -->"
MAP_END = "<!-- concept-map:end -->"
HUB_PAGES = ("README.md", "glossary.md", "standard-vs-desert.md", "concept-map.md", "catalog.md")
MODES = ("standard", "desert", "both", "none")
MODE_LABEL = {"standard": "Standard", "desert": "Desert", "both": "Both modes", "none": ""}
SECTION_TITLE = "Standard and desert"

# kind -> (class, folder). The folder of a page is fixed by its kind; the class groups the kinds in the catalogue and the site.
KINDS = {
    "hub": ("hub", ""),
    "mode": ("mode", "modes"),
    "zone": ("structure", "zones"),
    "branch": ("structure", "branches"),
    "level": ("structure", "levels"),
    "room-role": ("structure", "rooms"),
    "gate": ("structure", "gates"),
    "paid-door": ("structure", "doors"),
    "work": ("structure", "works"),
    "stall": ("structure", "stalls"),
    "region": ("structure", "regions"),
    "place": ("structure", "places"),
    "stack": ("structure", "stacks"),
    "landmark": ("structure", "landmarks"),
    "camp": ("structure", "camps"),
    "survivor": ("entity", "survivors"),
    "enemy": ("entity", "enemies"),
    "creature": ("entity", "creatures"),
    "weapon": ("object", "weapons"),
    "grade": ("object", "grades"),
    "throwable": ("object", "throwables"),
    "collectible": ("object", "collectibles"),
    "perk": ("object", "perks"),
    "grimoire": ("object", "grimoires"),
    "spell": ("object", "spells"),
    "seal": ("object", "seals"),
    "resource": ("object", "resources"),
    "blessing": ("object", "blessings"),
    "prop": ("object", "props"),
    "hazard": ("object", "hazards"),
    "trap": ("object", "traps"),
    "pickup": ("object", "pickups"),
    "power-up": ("object", "power-ups"),
    "vehicle": ("object", "vehicles"),
    "system": ("system", "systems"),
    "cycle": ("system", "cycles"),
    "trial": ("system", "trials"),
    "pipeline": ("pipeline", "pipeline"),
    "package": ("pipeline", "packages"),
    "project": ("project", "projects"),
}
KIND_LABEL = {
    "hub": "Guides", "mode": "Modes", "zone": "Zones", "branch": "Branches", "level": "Levels", "room-role": "Room roles",
    "gate": "Gates", "paid-door": "Paid doors", "work": "Cathedral works", "stall": "Market stalls", "region": "Regions",
    "place": "Places", "stack": "Towers and catacombs", "landmark": "Landmarks", "camp": "Camps", "survivor": "Survivors",
    "enemy": "Enemies", "creature": "Creatures", "weapon": "Weapons", "grade": "Weapon grades", "throwable": "Throwables",
    "collectible": "Collectibles", "perk": "Perks", "grimoire": "Grimoires", "spell": "Spells", "seal": "Seals",
    "resource": "Resources", "blessing": "Blessings", "prop": "Props", "hazard": "Hazards", "trap": "Traps",
    "pickup": "Pickups", "power-up": "Power-ups", "vehicle": "Vehicles", "system": "Systems", "cycle": "Cycles",
    "trial": "Trials", "pipeline": "Pipeline", "package": "Packages", "project": "Projects and history",
}
CLASS_ORDER = ("hub", "mode", "structure", "entity", "object", "system", "pipeline", "project")
CLASS_TITLE = {
    "hub": "Guides", "mode": "Modes", "structure": "Structures", "entity": "Entities", "object": "Objects",
    "system": "Systems", "pipeline": "Pipeline and tools", "project": "Projects and history",
}
# Classes whose pages must say what changes between the standard mode and the desert: a "## Standard and desert" section.
SECTION_CLASSES = ("mode", "structure", "entity", "object", "system")
MIN_OUT_LINKS = 2
# Repository paths written in code spans are checked only where the CI checkout (tools/wiki_site.py, gate.yml) has them.
PATH_PREFIXES = ("tools/", "docs/", "wiki/", "modelli/", "unity/")
CHECKED_SUFFIXES = (".cs", ".py", ".md", ".json", ".ps1", ".csproj")
# Generated files the pages name in code spans: compare() checks them once built, so check_pages must not look for them before the first build.
GENERATED_OUTPUTS = ("wiki/catalog.md", "wiki/concept-map.md", "wiki/graph.json")
IMAGE_SUFFIXES = (".jpg", ".jpeg", ".png", ".webp", ".gif")
MACHINE_PATH = re.compile(r"[A-Za-z]:[\\/]+Users[\\/]|/Users/[\w.-]+/|/home/[\w.-]+/")
REPLACEMENT_CHARACTER = chr(0xFFFD)
CONCEPT_ID = re.compile(r"^[a-z][a-z0-9_]*$")

FENCE = re.compile(r"^\s*```")
INLINE_CODE = re.compile(r"`([^`\n]+)`")
IMAGE = re.compile(r"!\[([^\]\n]*)\]\(([^)\s]+)\)")
LINK = re.compile(r"(?<!!)\[([^\]\n]+)\]\(([^)\s]+)\)")
BOLD = re.compile(r"\*\*([^*\n]+?)\*\*")
ITALIC = re.compile(r"(?<![\w*])\*([^*\s][^*\n]*?)\*(?![\w*])")
HEADING = re.compile(r"^(#{1,6})\s+(.+?)\s*#*\s*$")


def finding(code: str, path: str, observed: str, expected: str) -> dict:
    return {"code": code, "path": path, "observed": observed, "expected": expected}


def slugify(text: str) -> str:
    """The anchor GitHub gives a heading: lower case, punctuation dropped, spaces turned into hyphens."""
    text = re.sub(r"!?\[([^\]]*)\]\([^)]*\)", r"\1", text)
    text = text.replace("`", "").replace("*", "").strip().lower()
    return re.sub(r"[^\w\- ]", "", text).replace(" ", "-")


def parse_front_matter(text: str) -> tuple[dict, str]:
    """(meta, body) of a page. Raises ValueError when the front matter block is missing or malformed."""
    if not text.startswith("---\n"):
        raise ValueError("the page does not start with a front matter block ('---')")
    end = text.find("\n---\n", 4)
    if end < 0:
        raise ValueError("the front matter block is not closed by '---'")
    meta = {}
    for line in text[4:end].splitlines():
        if not line.strip():
            continue
        key, sep, value = line.partition(":")
        if not sep or line[:1] in (" ", "\t") or not key.strip():
            raise ValueError(f"not a 'key: value' line: {line!r}")
        meta[key.strip()] = value.strip()
    return meta, text[end + 5:]


def tagged_lines(body: str) -> list[tuple[bool, str]]:
    """(in_code, line) for every line: True inside a fenced code block (the fence lines count as code)."""
    result, fenced = [], False
    for line in body.splitlines():
        if FENCE.match(line):
            fenced = not fenced
            result.append((True, line))
        else:
            result.append((fenced, line))
    return result


def headings_of(body: str) -> list[tuple[int, str, str]]:
    """(level, text, anchor) of every heading outside code, numbered the way GitHub numbers repeated titles."""
    result, seen = [], {}
    for in_code, line in tagged_lines(body):
        match = HEADING.match(line) if not in_code else None
        if not match:
            continue
        slug = slugify(match.group(2))
        count = seen.get(slug, 0)
        seen[slug] = count + 1
        result.append((len(match.group(1)), match.group(2), slug if count == 0 else f"{slug}-{count}"))
    return result


def link_targets(body: str) -> list[str]:
    """Targets of the [text](target) links and ![alt](image) images written outside code."""
    found = []
    for in_code, line in tagged_lines(body):
        if in_code:
            continue
        plain = INLINE_CODE.sub("", line)
        found += [m.group(2) for m in LINK.finditer(plain)]
        found += [m.group(2) for m in IMAGE.finditer(plain)]
    return found


def code_paths(body: str) -> list[str]:
    """Inline code spans that name a repository file this check can verify in the CI checkout."""
    found = []
    for in_code, line in tagged_lines(body):
        if in_code:
            continue
        for span in INLINE_CODE.findall(line):
            if span.startswith(PATH_PREFIXES) and " " not in span and span.endswith(CHECKED_SUFFIXES) and "*" not in span:
                found.append(span)
    return found


class Page:
    def __init__(self, rel: str, meta: dict, body: str):
        self.rel = rel
        self.meta = meta
        self.body = body
        self.kind = meta.get("kind", "")
        self.cls = KINDS[self.kind][0] if self.kind in KINDS else ""
        self.title = meta.get("title", "")
        self.mode = meta.get("mode", "none" if self.kind == "hub" else "")
        self.summary = meta.get("summary", "")
        self.image = meta.get("image", "")
        self.caption = meta.get("caption", "")
        self.sources = [s.strip() for s in meta.get("sources", "").split(",") if s.strip()]
        self.tests = meta.get("tests", "")
        self.headings = headings_of(body)
        self.links = link_targets(body)

    @property
    def id(self) -> str:
        return self.rel[:-3]


def load_pages(wiki: Path) -> tuple[dict, list]:
    """Every Markdown page under wiki/ (the generated site excluded), parsed. Returns ({rel: Page}, findings)."""
    pages, findings = {}, []
    for path in sorted(wiki.rglob("*.md")):
        rel = path.relative_to(wiki).as_posix()
        if rel.startswith("site/"):
            continue
        try:
            text = path.read_text(encoding="utf-8")
        except UnicodeDecodeError:
            findings.append(finding("WIKI_NOT_UTF8", f"wiki/{rel}", "not valid UTF-8", "UTF-8 text"))
            continue
        try:
            meta, body = parse_front_matter(text)
        except ValueError as error:
            findings.append(finding("WIKI_FRONT_MATTER", f"wiki/{rel}", str(error), "a front matter block with title, kind and summary"))
            continue
        pages[rel] = Page(rel, meta, body)
    return pages, findings


def resolve(page_rel: str, target: str, wiki: Path, repo: Path) -> tuple[str, str]:
    """What a link target points at: ('external', url), ('anchor', name), ('wiki', path-in-wiki[#name]),
    ('repo', path-in-repository[#name]) or ('outside', target) when it leaves the repository."""
    if target.startswith(("http://", "https://", "mailto:")):
        return "external", target
    path_part, _, anchor = unquote(target).partition("#")
    if not path_part:
        return "anchor", anchor
    destination = ((wiki / page_rel).parent / path_part).resolve()
    suffix = f"#{anchor}" if anchor else ""
    for base, kind in ((wiki.resolve(), "wiki"), (repo.resolve(), "repo")):
        try:
            return kind, destination.relative_to(base).as_posix() + suffix
        except ValueError:
            continue
    return "outside", target


def check_pages(pages: dict, wiki: Path, repo: Path) -> list:
    findings = []
    for rel, page in sorted(pages.items()):
        where = f"wiki/{rel}"
        if REPLACEMENT_CHARACTER in page.body or REPLACEMENT_CHARACTER in page.title:
            findings.append(finding("WIKI_REPLACEMENT_CHARACTER", where, "contains U+FFFD", "the real character"))
        machine = MACHINE_PATH.search(page.body + page.title)
        if machine:
            findings.append(finding("WIKI_MACHINE_PATH", where, f"names {machine[0]!r}", "paths relative to the repository root"))
        if not page.title or not page.summary:
            findings.append(finding("WIKI_FRONT_MATTER", where, "title or summary missing", "title and summary"))
        if page.kind not in KINDS:
            findings.append(finding("WIKI_KIND", where, f"kind {page.kind!r}", "one of the kinds in tools/wiki_site.py"))
            continue
        expected_folder = KINDS[page.kind][1]
        actual_folder = rel.split("/")[0] if "/" in rel else ""
        if page.kind == "hub":
            if "/" in rel or rel not in HUB_PAGES:
                findings.append(finding("WIKI_HUB", where, "a hub page outside the list", "one of " + ", ".join(HUB_PAGES) + ", at the root of wiki/"))
        elif actual_folder != expected_folder:
            findings.append(finding("WIKI_FOLDER", where, f"kind {page.kind!r} in folder {actual_folder!r}", f"folder {expected_folder!r}"))
        if page.mode not in MODES:
            findings.append(finding("WIKI_MODE", where, f"mode {page.mode!r}", "standard, desert, both or none"))
        first = next((h for h in page.headings if h[0] == 1), None)
        if first is None or first[1] != page.title:
            findings.append(finding("WIKI_TITLE", where, f"first heading is {first[1] if first else None!r}", f"'# {page.title}' first"))
        if page.kind != "hub":
            findings += check_image(page, wiki, where)
        if page.cls in SECTION_CLASSES and not any(h[1].startswith(SECTION_TITLE) and h[0] == 2 for h in page.headings):
            findings.append(finding("WIKI_SECTION_MISSING", where, f"no '## {SECTION_TITLE}' section",
                                    "what changes between the standard mode and the desert, even when nothing does"))
        for source in page.sources:
            if "*" in source or not (repo / source).exists():
                findings.append(finding("WIKI_SOURCE_MISSING", where, f"source {source!r}", "a repository path that exists"))
        for path in code_paths(page.body):
            if path not in GENERATED_OUTPUTS and not (repo / path).exists():
                findings.append(finding("WIKI_PATH_MISSING", where, f"names `{path}`", "a repository path that exists"))
        findings += check_links(rel, page, pages, wiki, repo)
    return findings


def check_image(page: Page, wiki: Path, where: str) -> list:
    if not page.image:
        return [finding("WIKI_IMAGE_MISSING", where, "no image in the front matter", "image: a file under wiki/")]
    image = (wiki / page.image).resolve()
    if not image.is_file() or not page.image.lower().endswith(IMAGE_SUFFIXES):
        return [finding("WIKI_IMAGE_MISSING", where, f"image {page.image} not found", "an image file under wiki/")]
    shown = [(page_dir(page, wiki) / t).resolve() for t in link_targets(page.body) if t.lower().endswith(IMAGE_SUFFIXES)]
    if image not in shown:
        return [finding("WIKI_IMAGE_NOT_SHOWN", where, "the body does not show the front matter image", f"![...] to {page.image}")]
    return []


def page_dir(page: Page, wiki: Path) -> Path:
    return (wiki / page.rel).parent


def check_links(rel: str, page: Page, pages: dict, wiki: Path, repo: Path) -> list:
    findings, where = [], f"wiki/{rel}"
    anchors = {h[2] for h in page.headings}
    for target in link_targets(page.body):
        kind, value = resolve(rel, target, wiki, repo)
        if kind == "outside":
            findings.append(finding("WIKI_LINK_OUTSIDE", where, f"links to {target}", "a path inside the repository"))
        elif kind == "anchor":
            if value.lower() not in anchors:
                findings.append(finding("WIKI_ANCHOR_BROKEN", where, f"links to #{value}", "a heading of this page"))
        elif kind == "wiki":
            path_part, _, anchor = value.partition("#")
            if path_part.endswith(".md"):
                if path_part not in pages:
                    findings.append(finding("WIKI_LINK_BROKEN", where, f"links to {target}", "an existing page"))
                elif anchor and anchor.lower() not in {h[2] for h in pages[path_part].headings}:
                    findings.append(finding("WIKI_ANCHOR_BROKEN", where, f"links to {target}", "a heading of the target page"))
            elif not (wiki / path_part).is_file():
                findings.append(finding("WIKI_LINK_BROKEN", where, f"links to {target}", "an existing file under wiki/"))
        elif kind == "repo":
            if not (repo / value.partition("#")[0]).exists():
                findings.append(finding("WIKI_LINK_BROKEN", where, f"links to {target}", "an existing repository path"))
    return findings


def link_graph(pages: dict, wiki: Path, repo: Path) -> dict:
    """rel -> sorted list of the wiki pages it links to."""
    graph = {}
    for rel, page in pages.items():
        targets = set()
        for target in link_targets(page.body):
            kind, value = resolve(rel, target, wiki, repo)
            path_part = value.partition("#")[0]
            if kind == "wiki" and path_part in pages and path_part != rel:
                targets.add(path_part)
        graph[rel] = sorted(targets)
    return graph


def check_graph(pages: dict, graph: dict) -> list:
    """Every page is reached from the home page, and every page but the hubs links to at least MIN_OUT_LINKS pages."""
    seen, queue = {"README.md"}, deque(["README.md"])
    while queue:
        for nxt in graph.get(queue.popleft(), []):
            if nxt not in seen:
                seen.add(nxt)
                queue.append(nxt)
    findings = []
    for rel, page in sorted(pages.items()):
        if rel not in seen:
            findings.append(finding("WIKI_ORPHAN", f"wiki/{rel}", "no path of links leads here from wiki/README.md", "a link from another page"))
        if page.kind != "hub" and len(graph[rel]) < MIN_OUT_LINKS:
            findings.append(finding("WIKI_TOO_FEW_LINKS", f"wiki/{rel}", f"links to {len(graph[rel])} page(s)", f"at least {MIN_OUT_LINKS}"))
    return findings


def check_images_used(pages: dict, wiki: Path) -> list:
    used = set()
    for page in pages.values():
        if page.image:
            used.add((wiki / page.image).resolve())
        for target in link_targets(page.body):
            used.add((page_dir(page, wiki) / target.partition("#")[0]).resolve())
    findings = []
    image_dir = wiki / "img"
    if image_dir.is_dir():
        for image in sorted(p for p in image_dir.rglob("*") if p.is_file()):
            if image.resolve() not in used:
                findings.append(finding("WIKI_IMAGE_UNUSED", f"wiki/{image.relative_to(wiki).as_posix()}",
                                        "no page shows it", "a page that shows it, or remove it"))
    return findings


def load_concepts(wiki: Path, pages: dict) -> tuple[list, list, list]:
    """(concepts, relations, findings) from wiki/concepts.json."""
    path = wiki / "concepts.json"
    if not path.is_file():
        return [], [], [finding("WIKI_CONCEPTS", "wiki/concepts.json", "missing", "the concept map data")]
    data = json.loads(path.read_text(encoding="utf-8"))
    concepts, relations = data.get("concepts", []), data.get("relations", [])
    findings, ids = [], [c.get("id", "") for c in concepts]
    if len(set(ids)) != len(ids):
        findings.append(finding("WIKI_CONCEPTS", "wiki/concepts.json", "repeated concept ids", "unique ids"))
    for concept in concepts:
        if not CONCEPT_ID.match(concept.get("id", "")):
            findings.append(finding("WIKI_CONCEPTS", "wiki/concepts.json", f"id {concept.get('id')!r}", "lower case letters, digits, underscores"))
        if concept.get("page") not in pages:
            findings.append(finding("WIKI_CONCEPTS", "wiki/concepts.json", f"concept {concept.get('id')} points at {concept.get('page')}", "an existing page"))
    for relation in relations:
        for end in ("from", "to"):
            if relation.get(end) not in ids:
                findings.append(finding("WIKI_CONCEPTS", "wiki/concepts.json", f"relation to unknown concept {relation.get(end)!r}", "a concept id"))
    return concepts, relations, findings


# ---------------------------------------------------------------- Markdown to HTML (the subset the pages use)

def attr(text: str) -> str:
    return html.escape(text, quote=True)


def inline(text: str, href) -> str:
    codes = []

    def keep(match):
        codes.append(match.group(1))
        return f"\x00{len(codes) - 1}\x00"

    text = INLINE_CODE.sub(keep, text)
    text = html.escape(text, quote=False)
    text = IMAGE.sub(lambda m: f'<img src="{attr(href(m.group(2)))}" alt="{attr(m.group(1))}" loading="lazy">', text)
    text = LINK.sub(lambda m: f'<a href="{attr(href(m.group(2)))}">{m.group(1)}</a>', text)
    text = BOLD.sub(r"<strong>\1</strong>", text)
    text = ITALIC.sub(r"<em>\1</em>", text)
    return re.sub("\x00(\\d+)\x00", lambda m: "<code>" + html.escape(codes[int(m.group(1))], quote=False) + "</code>", text)


def render_markdown(body: str, href, caption: str = "") -> str:
    """HTML of the Markdown of one page. Each level-2 heading opens a <section> that holds the text up to the next one."""
    out, para, items, table, quote = [], [], [], [], []
    seen, section_open, index = {}, False, 0
    lines = body.splitlines()

    def flush():
        if para:
            text = " ".join(para).strip()
            single = IMAGE.fullmatch(text)
            if single:
                out.append(f'<figure><img src="{attr(href(single.group(2)))}" alt="{attr(single.group(1))}" loading="lazy">'
                           f"<figcaption>{html.escape(caption or single.group(1), quote=False)}</figcaption></figure>")
            else:
                out.append("<p>" + inline(text, href) + "</p>")
            para.clear()
        if items:
            tag = items[0][0]
            out.append(f"<{tag}>" + "".join("<li>" + inline(t, href) + "</li>" for _, t in items) + f"</{tag}>")
            items.clear()
        if table:
            rows = [[c.strip() for c in row.strip().strip("|").split("|")] for row in table]
            head, rest = rows[0], rows[2:]
            cells = "".join(f"<th>{inline(c, href)}</th>" for c in head)
            trs = "".join("<tr>" + "".join(f"<td>{inline(c, href)}</td>" for c in row) + "</tr>" for row in rest)
            out.append(f'<div class="table"><table><thead><tr>{cells}</tr></thead><tbody>{trs}</tbody></table></div>')
            table.clear()
        if quote:
            out.append("<blockquote><p>" + inline(" ".join(quote), href) + "</p></blockquote>")
            quote.clear()

    while index < len(lines):
        line = lines[index]
        index += 1
        if FENCE.match(line):
            flush()
            language = line.strip().strip("`").strip()
            code = []
            while index < len(lines) and not FENCE.match(lines[index]):
                code.append(lines[index])
                index += 1
            index += 1
            cls = f' class="language-{attr(language)}"' if language else ""
            out.append(f"<pre><code{cls}>{html.escape(chr(10).join(code), quote=False)}</code></pre>")
            continue
        if not line.strip():
            flush()
            continue
        heading = HEADING.match(line)
        if heading:
            flush()
            level, text = len(heading.group(1)), heading.group(2)
            slug = slugify(text)
            count = seen.get(slug, 0)
            seen[slug] = count + 1
            anchor = slug if count == 0 else f"{slug}-{count}"
            if level == 2:
                if section_open:
                    out.append("</section>")
                css = ' class="diff"' if text.startswith(SECTION_TITLE) else ""
                out.append(f'<section id="{anchor}"{css}>')
                section_open = True
            out.append(f'<h{level} id="{anchor}">{inline(text, href)}</h{level}>')
            continue
        if re.match(r"^(\*\*\*|---|___)\s*$", line):
            flush()
            out.append("<hr>")
            continue
        if line.startswith(">"):
            flush()
            quote.append(line[1:].strip())
            continue
        bullet = re.match(r"^\s*[-*]\s+(.*)$", line)
        ordered = re.match(r"^\s*\d+\.\s+(.*)$", line)
        if bullet or ordered:
            tag = "ul" if bullet else "ol"
            if items and items[0][0] != tag:
                flush()
            if para:
                flush()
            items.append((tag, (bullet or ordered).group(1)))
            continue
        if items and line.startswith("  "):
            tag, text = items[-1]
            items[-1] = (tag, text + " " + line.strip())
            continue
        if line.startswith("|"):
            if para:
                flush()
            table.append(line)
            continue
        if table or items or quote:
            flush()
        para.append(line.strip())
    flush()
    if section_open:
        out.append("</section>")
    return "\n".join(out)


# ---------------------------------------------------------------- generated files

def catalog_body(pages: dict) -> str:
    """The body of wiki/catalog.md: every page but the catalogue itself, by class and kind."""
    lines = ["# Catalogue", "", "Generated by `tools/wiki_site.py`: do not edit it by hand. Every page of the wiki, with its mode",
             "(standard, desert or both) and its one-line summary. Pages are linked from here and from [the home page](README.md).", ""]
    total = sum(1 for rel in pages if rel != "catalog.md")
    lines += [f"{total} pages.", ""]
    for cls in CLASS_ORDER:
        members = [p for rel, p in pages.items() if p.cls == cls and rel != "catalog.md"]
        if not members:
            continue
        lines += [f"## {CLASS_TITLE[cls]}", ""]
        for kind in KINDS:
            group = sorted((p for p in members if p.kind == kind), key=lambda p: p.title.lower())
            if not group:
                continue
            if KIND_LABEL[kind] != CLASS_TITLE[cls]:
                lines += [f"### {KIND_LABEL[kind]}", ""]
            lines += ["| Page | Mode | Summary |", "| --- | --- | --- |"]
            for page in group:
                mode = MODE_LABEL.get(page.mode, "")
                lines.append(f"| [{page.title}]({page.rel}) | {mode} | {page.summary.replace('|', '/')} |")
            lines.append("")
    return "\n".join(lines).rstrip() + "\n"


def graph_document(pages: dict, graph: dict, concepts: list, relations: list) -> str:
    inbound = defaultdict(list)
    for rel, targets in graph.items():
        for target in targets:
            inbound[target].append(rel)
    nodes = []
    for rel in sorted(pages):
        page = pages[rel]
        nodes.append({
            "id": page.id, "path": f"wiki/{rel}", "title": page.title, "kind": page.kind, "class": page.cls,
            "mode": page.mode, "summary": page.summary, "image": f"wiki/{page.image}" if page.image else "",
            "sources": page.sources, "tests": page.tests,
            "links": [t[:-3] for t in graph[rel]], "linked_from": sorted(r[:-3] for r in inbound[rel]),
        })
    document = {"schema": 1, "repository": REPO_URL, "branch": REPO_BRANCH, "pages": nodes,
                "concepts": concepts, "relations": relations}
    return json.dumps(document, indent=1, ensure_ascii=False) + "\n"


def mermaid_block(concepts: list, relations: list) -> str:
    lines = ["```mermaid", "flowchart LR"]
    for concept in concepts:
        label = concept["label"].replace('"', "'")
        lines.append(f'  {concept["id"]}["{label}"]')
    for relation in relations:
        label = relation["label"].replace('"', "'")
        lines.append(f'  {relation["from"]} -- "{label}" --> {relation["to"]}')
    lines.append("```")
    return "\n".join(lines)


def layout(concepts: list, relations: list) -> tuple[dict, int, int]:
    """Layered positions for the concept map: rank by longest path (cycles broken where a depth-first walk closes one)."""
    ids = [c["id"] for c in concepts]
    out = defaultdict(list)
    for r in relations:
        out[r["from"]].append(r["to"])
    state, back = {}, set()

    def walk(node):
        state[node] = 1
        for nxt in out[node]:
            if state.get(nxt) == 1:
                back.add((node, nxt))
            elif nxt not in state:
                walk(nxt)
        state[node] = 2

    for node in ids:
        if node not in state:
            walk(node)
    forward = [(r["from"], r["to"]) for r in relations if (r["from"], r["to"]) not in back]
    rank = {i: 0 for i in ids}
    for _ in range(len(ids) + 1):
        changed = False
        for a, b in forward:
            if rank[b] < rank[a] + 1:
                rank[b] = rank[a] + 1
                changed = True
        if not changed:
            break
    layers = defaultdict(list)
    for i in ids:
        layers[rank[i]].append(i)
    position = {i: k for layer in layers.values() for k, i in enumerate(layer)}
    for _ in range(3):  # barycentre sweeps, left to right and back
        for depth in sorted(layers)[1:] + sorted(layers)[-2::-1]:
            neighbours = {i: [position[a] for a, b in forward if b == i] + [position[b] for a, b in forward if a == i]
                          for i in layers[depth]}
            layers[depth].sort(key=lambda i: (sum(neighbours[i]) / len(neighbours[i]) if neighbours[i] else position[i], position[i]))
            for k, i in enumerate(layers[depth]):
                position[i] = k
    coords, column_w, row_h, box_w, box_h = {}, 280, 92, 220, 56
    tallest = max(len(v) for v in layers.values()) if layers else 1
    for depth, members in layers.items():
        top = 40 + (tallest - len(members)) * row_h / 2
        for k, i in enumerate(members):
            coords[i] = (40 + depth * column_w, top + k * row_h, box_w, box_h)
    width = 80 + (max(layers) + 1) * column_w if layers else 200
    height = 80 + tallest * row_h
    return coords, width, height


def wrap(label: str, width: int = 26) -> list[str]:
    words, lines, current = label.split(), [], ""
    for word in words:
        if current and len(current) + 1 + len(word) > width:
            lines.append(current)
            current = word
        else:
            current = f"{current} {word}".strip()
    if current:
        lines.append(current)
    return lines or [label]


def concept_svg(concepts: list, relations: list, pages: dict, site_href) -> str:
    coords, width, height = layout(concepts, relations)
    parts = [f'<svg class="conceptmap" viewBox="0 0 {width} {height}" role="img" aria-labelledby="map-title">',
             '<title id="map-title">Concept map of Prying Eyes</title>',
             '<defs><marker id="arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">'
             '<path d="M 0 0 L 10 5 L 0 10 z" class="arrowhead"/></marker></defs>']
    by_id = {c["id"]: c for c in concepts}
    for r in relations:
        x1, y1, w1, h1 = coords[r["from"]]
        x2, y2, w2, h2 = coords[r["to"]]
        sx, sy = x1 + w1, y1 + h1 / 2
        ex, ey = x2, y2 + h2 / 2
        if ex < sx:  # a relation that runs backwards: leave from the bottom, arrive from the top
            sx, sy, ex, ey = x1 + w1 / 2, y1 + h1, x2 + w2 / 2, y2
        mx = (sx + ex) / 2
        my = (sy + ey) / 2
        parts.append(f'<path class="edge" d="M {sx:.0f} {sy:.0f} C {mx:.0f} {sy:.0f}, {mx:.0f} {ey:.0f}, {ex:.0f} {ey:.0f}" marker-end="url(#arrow)"/>')
        parts.append(f'<text class="edge-label" x="{mx:.0f}" y="{my - 4:.0f}" text-anchor="middle">{html.escape(r["label"])}</text>')
    for c in concepts:
        x, y, w, h = coords[c["id"]]
        href = site_href(c["page"])
        lines = wrap(c["label"])
        text = "".join(f'<tspan x="{x + w / 2:.0f}" dy="{"1.1em" if k == 0 else "1.15em"}">{html.escape(line)}</tspan>'
                       for k, line in enumerate(lines))
        offset = -(len(lines) - 1) * 9
        parts.append(f'<a href="{attr(href)}"><g class="node"><rect x="{x:.0f}" y="{y:.0f}" width="{w}" height="{h}" rx="8"/>'
                     f'<text x="{x + w / 2:.0f}" y="{y + h / 2 + offset:.0f}" text-anchor="middle">{text}</text></g></a>')
    parts.append("</svg>")
    return "\n".join(parts)


def mermaid_replace(text: str, block: str) -> str:
    if MAP_BEGIN not in text or MAP_END not in text:
        raise ValueError("wiki/concept-map.md lacks its markers")
    head, _, rest = text.partition(MAP_BEGIN)
    _, _, tail = rest.partition(MAP_END)
    return f"{head}{MAP_BEGIN}\n{block}\n{MAP_END}{tail}"


# ---------------------------------------------------------------- the site

def site_path(rel: str) -> str:
    """Where a page lands inside wiki/site/."""
    return "index.html" if rel == "README.md" else rel[:-3] + ".html"


class Site:
    def __init__(self, wiki: Path, repo: Path, pages: dict, graph: dict):
        self.wiki, self.repo, self.pages, self.graph = wiki, repo, pages, graph
        self.site_dir = wiki / "site"
        self.inbound = defaultdict(list)
        for rel, targets in graph.items():
            for target in targets:
                self.inbound[target].append(rel)

    def href(self, from_rel: str, target: str) -> str:
        """The URL a link in page from_rel uses in the site."""
        kind, value = resolve(from_rel, target, self.wiki, self.repo)
        here = (self.site_dir / site_path(from_rel)).parent
        if kind == "external":
            return value
        if kind == "anchor":
            return "#" + value
        if kind == "wiki":
            path_part, _, anchor = value.partition("#")
            destination = self.site_dir / site_path(path_part) if path_part.endswith(".md") else self.wiki / path_part
            url = os.path.relpath(destination, here).replace("\\", "/")
            return url + (f"#{anchor}" if anchor else "")
        if kind == "repo":
            path_part, _, anchor = value.partition("#")
            kind_word = "tree" if (self.repo / path_part).is_dir() else "blob"
            return f"{REPO_URL}/{kind_word}/{REPO_BRANCH}/{path_part}" + (f"#{anchor}" if anchor else "")
        return target

    def page_url(self, from_rel: str, to_rel: str) -> str:
        """The URL of the page to_rel (a path from the wiki root) as seen from the page from_rel."""
        here = (self.site_dir / site_path(from_rel)).parent
        return os.path.relpath(self.site_dir / site_path(to_rel), here).replace("\\", "/")

    def root_of(self, rel: str) -> str:
        return "../" * site_path(rel).count("/")

    def page_html(self, page: Page, nav_links: str, extra: str = "") -> str:
        root = self.root_of(page.rel)
        body = render_markdown(page.body, lambda t: self.href(page.rel, t), page.caption) + extra
        inbound = sorted(self.inbound.get(page.rel, []), key=lambda r: self.pages[r].title.lower())
        backlinks = "".join(f'<li><a href="{attr(self.page_url(page.rel, r))}">{html.escape(self.pages[r].title)}</a></li>' for r in inbound)
        badge = MODE_LABEL.get(page.mode, "")
        badge_html = f'<span class="badge badge-{attr(page.mode)}">{html.escape(badge)}</span>' if badge else ""
        meta = []
        if page.tests:
            meta.append(f"<dt>Checks</dt><dd><code>{html.escape(page.tests)}</code></dd>")
        if page.sources:
            links = " · ".join(f'<a href="{attr(self.repo_link(s))}">{html.escape(s)}</a>' for s in page.sources)
            meta.append(f"<dt>Sources</dt><dd>{links}</dd>")
        meta_html = f'<dl class="facts">{"".join(meta)}</dl>' if meta else ""
        crumbs = f'<a href="{root}index.html">Home</a>'
        if page.kind != "hub":
            crumbs += f' › <a href="{root}catalog.html">{html.escape(CLASS_TITLE.get(page.cls, page.cls))}</a>'
            if KIND_LABEL.get(page.kind, page.kind) != CLASS_TITLE.get(page.cls, page.cls):
                crumbs += f' › {html.escape(KIND_LABEL.get(page.kind, page.kind))}'
        return (
            "<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n"
            "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n"
            f"<title>{html.escape(page.title)} · Prying Eyes wiki</title>\n"
            f"<meta name=\"description\" content=\"{attr(page.summary)}\">\n"
            f"<link rel=\"stylesheet\" href=\"{root}assets/wiki.css\">\n"
            f"<script src=\"{root}assets/search-index.js\" defer></script>\n"
            f"<script src=\"{root}assets/wiki.js\" defer></script>\n</head>\n"
            f'<body data-root="{root}" data-current="{attr(page.rel)}">\n'
            "<header class=\"top\"><a class=\"brand\" href=\"" + root + "index.html\">Prying Eyes · wiki</a>"
            f"<a href=\"{root}concept-map.html\">Concept map</a><a href=\"{root}catalog.html\">Catalogue</a>"
            f"<a href=\"{root}standard-vs-desert.html\">Standard vs desert</a>"
            "<div class=\"search\"><input id=\"q\" type=\"search\" placeholder=\"Search the wiki\" aria-label=\"Search the wiki\" autocomplete=\"off\">"
            "<div id=\"results\" class=\"results\" hidden></div></div></header>\n"
            "<div class=\"layout\">\n"
            "<aside class=\"side\"><div class=\"filter\" role=\"group\" aria-label=\"Mode\">"
            "<button type=\"button\" data-mode-filter=\"all\" aria-pressed=\"true\">All</button>"
            "<button type=\"button\" data-mode-filter=\"standard\" aria-pressed=\"false\">Standard</button>"
            "<button type=\"button\" data-mode-filter=\"desert\" aria-pressed=\"false\">Desert</button></div>"
            f"<nav id=\"nav\" aria-label=\"Pages\">{nav_links}</nav></aside>\n"
            "<main class=\"main\">\n"
            f"<p class=\"crumbs\">{crumbs}</p>\n"
            f"<div class=\"title-row\">{badge_html}</div>\n"
            f"<article class=\"prose\">\n{body}\n</article>\n{meta_html}\n"
            f"<section class=\"backlinks\"><h2>Linked from</h2>"
            f"{'<ul>' + backlinks + '</ul>' if backlinks else '<p>No page links here yet.</p>'}</section>\n"
            "</main>\n</div>\n"
            "<footer>Generated from the Markdown in wiki/ by tools/wiki_site.py. Edit the pages, not this site. "
            f"<a href=\"{REPO_URL}\">Repository</a></footer>\n</body>\n</html>\n"
        )

    def repo_link(self, path: str) -> str:
        kind_word = "tree" if (self.repo / path).is_dir() else "blob"
        return f"{REPO_URL}/{kind_word}/{REPO_BRANCH}/{path}"


def nav_html(pages: dict) -> str:
    """The static list of pages used when scripts are off; wiki.js rebuilds it with mode filters."""
    parts = []
    for cls in CLASS_ORDER:
        members = [p for p in pages.values() if p.cls == cls]
        if members:
            parts.append(f"<h3>{html.escape(CLASS_TITLE[cls])}</h3><ul>")
            parts += [f'<li><a href="#">{html.escape(p.title)}</a></li>' for p in sorted(members, key=lambda p: p.title.lower())]
            parts.append("</ul>")
    return "".join(parts)


def search_index(pages: dict) -> str:
    entries = []
    for rel in sorted(pages):
        page = pages[rel]
        entries.append({"t": page.title, "s": page.summary, "u": site_path(rel), "c": page.cls,
                        "k": KIND_LABEL.get(page.kind, page.kind), "m": page.mode, "p": rel})
    return "window.WIKI_SEARCH = " + json.dumps(entries, ensure_ascii=False, indent=0) + ";\n"


def site_pages(pages: dict, site: Site) -> dict:
    """site-relative path -> HTML, for every page."""
    nav = nav_html(pages)
    result = {}
    for rel, page in pages.items():
        result[site_path(rel)] = site.page_html(page, nav)
    return result


def concept_map_page(site: Site, concepts: list, relations: list, pages: dict) -> str:
    """The map page: the hand-written intro of concept-map.md, then the diagram as SVG (not as Mermaid text)."""
    svg = concept_svg(concepts, relations, pages, lambda rel: site.href("concept-map.md", rel))
    page = pages["concept-map.md"]
    intro = Page("concept-map.md", dict(page.meta), page.body.split(MAP_BEGIN)[0])
    note = ('<p class="note">Each box is a page: follow it. Read an arrow as a sentence, for example '
            '"Standard mode – <em>starts in</em> → the cathedral".</p>')
    return site.page_html(intro, nav_html(pages), extra=f'<div class="map-wrap">\n{svg}\n</div>\n{note}')


# ---------------------------------------------------------------- build and check

def build(repo: Path = ROOT) -> tuple[dict, list]:
    """(outputs, findings): outputs maps each generated file (repository-relative, posix) to its text."""
    wiki = repo / "wiki"
    pages, findings = load_pages(wiki)
    if "README.md" not in pages:
        return {}, findings + [finding("WIKI_HOME", "wiki/README.md", "missing", "the home page")]
    findings += check_pages(pages, wiki, repo)
    graph = link_graph(pages, wiki, repo)
    findings += check_graph(pages, graph)
    findings += check_images_used(pages, wiki)
    concepts, relations, concept_findings = load_concepts(wiki, pages)
    findings += concept_findings
    outputs = {}
    if "catalog.md" in pages:
        catalog_text = catalog_body(pages)
        outputs["wiki/catalog.md"] = rewrite_page(wiki / "catalog.md", catalog_text)
        pages["catalog.md"] = Page("catalog.md", pages["catalog.md"].meta, catalog_text)
        graph = link_graph(pages, wiki, repo)
    if "concept-map.md" in pages:
        try:
            block = mermaid_block(concepts, relations)
            text = mermaid_replace((wiki / "concept-map.md").read_text(encoding="utf-8"), block)
            outputs["wiki/concept-map.md"] = text
        except ValueError as error:
            findings.append(finding("WIKI_CONCEPTS", "wiki/concept-map.md", str(error), "the two markers around the diagram"))
    outputs["wiki/graph.json"] = graph_document(pages, graph, concepts, relations)
    site = Site(wiki, repo, pages, graph)
    for name, text in site_pages(pages, site).items():
        outputs[f"wiki/site/{name}"] = text
    if "concept-map.md" in pages:
        # The map page takes the place of the generic page of concept-map.md: its diagram is the SVG, not the Mermaid text.
        outputs[f"wiki/site/{site_path('concept-map.md')}"] = concept_map_page(site, concepts, relations, pages)
    outputs["wiki/site/assets/search-index.js"] = search_index(pages)
    for asset in sorted(ASSETS.glob("*")):
        if asset.is_file():
            outputs[f"wiki/site/assets/{asset.name}"] = asset.read_text(encoding="utf-8")
    return outputs, findings


def rewrite_page(path: Path, generated: str) -> str:
    """The generated catalogue keeps its front matter (the hub page's) and takes the generated body."""
    text = path.read_text(encoding="utf-8")
    meta_end = text.find("\n---\n", 4)
    return text[:meta_end + 5] + "\n" + generated


def compare(repo: Path, outputs: dict) -> list:
    findings = []
    for name, text in sorted(outputs.items()):
        path = repo / name
        current = path.read_text(encoding="utf-8").replace("\r\n", "\n") if path.is_file() else None
        if current != text:
            findings.append(finding("WIKI_STALE", name, "missing" if current is None else "out of date",
                                    "regenerate with python tools/wiki_site.py"))
    site = repo / "wiki" / "site"
    if site.is_dir():
        for path in sorted(p for p in site.rglob("*") if p.is_file()):
            name = path.relative_to(repo).as_posix()
            if name not in outputs:
                findings.append(finding("WIKI_STALE", name, "a file the generator does not make", "remove it: python tools/wiki_site.py"))
    return findings


def write(repo: Path, outputs: dict) -> None:
    site = repo / "wiki" / "site"
    if site.is_dir():
        shutil.rmtree(site)
    for name, text in sorted(outputs.items()):
        path = repo / name
        path.parent.mkdir(parents=True, exist_ok=True)
        with open(path, "w", encoding="utf-8", newline="\n") as handle:
            handle.write(text)


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description="Build and check the Prying Eyes wiki.")
    parser.add_argument("--check", action="store_true", help="check only: exit 1 on any finding")
    parser.add_argument("--root", type=Path, default=ROOT, help="repository root (default: this checkout)")
    args = parser.parse_args(argv)
    repo = args.root.resolve()
    outputs, findings = build(repo)
    if args.check:
        findings += compare(repo, outputs)
        for item in findings:
            print(f"{item['code']}: {item['path']}: {item['observed']} (expected: {item['expected']})")
        print("WIKI:", "FAIL" if findings else "PASS", f"({len(findings)} findings)")
        return 1 if findings else 0
    for item in findings:
        print(f"{item['code']}: {item['path']}: {item['observed']} (expected: {item['expected']})")
    if findings:
        print("WIKI: not written, fix the findings first", f"({len(findings)} findings)")
        return 1
    write(repo, outputs)
    print(f"WIKI: written, {len(outputs)} generated files")
    return 0


if __name__ == "__main__":
    sys.exit(main())
