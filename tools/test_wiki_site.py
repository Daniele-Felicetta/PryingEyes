"""Hermetic tests of tools/wiki_site.py: the front matter, the Markdown subset, the checks and the round trip of the generated files.

Each test builds a small wiki in a temporary folder, so nothing here reads the real pages, the images or the game."""
import json
import posixpath
import re
import tempfile
import unittest
from pathlib import Path

import wiki_site as site

PAGE = """---
title: {title}
kind: {kind}
mode: {mode}
summary: A test page.
image: {image}
---

# {title}

![{title}](../{image_rel})

## Standard and desert

Nothing changes between the two modes in this test.

Links: [the home page](../README.md) and [the other page](../{other}).
"""

HOME = """---
title: Home
kind: hub
summary: The home page of the test wiki.
---

# Home

Start at [the weapon](weapons/a.md) and [the other one](weapons/b.md) (see the [catalogue](catalog.md)).
"""


def make_wiki(root: Path, pages: dict, images: tuple = ("img/weapons/a.webp", "img/weapons/b.webp")) -> Path:
    wiki = root / "wiki"
    (wiki / "img" / "weapons").mkdir(parents=True)
    for image in images:
        (wiki / image).write_bytes(b"not really an image, only its name is checked")
    (wiki / "README.md").write_text(HOME, encoding="utf-8")
    (wiki / "catalog.md").write_text("---\ntitle: Catalogue\nkind: hub\nsummary: Every page.\n---\n\n# Catalogue\n", encoding="utf-8")
    (wiki / "concepts.json").write_text(json.dumps({"concepts": [], "relations": []}), encoding="utf-8")
    (wiki / "weapons").mkdir(exist_ok=True)
    for name, title, other in (("a", "Alpha", "weapons/b.md"), ("b", "Beta", "weapons/a.md")):
        text = PAGE.format(title=title, kind="weapon", mode="standard", image=f"img/weapons/{name}.webp", image_rel=f"img/weapons/{name}.webp", other=other)
        (wiki / "weapons" / f"{name}.md").write_text(text, encoding="utf-8")
    return wiki


class FrontMatter(unittest.TestCase):
    def test_parses_keys_and_body(self):
        meta, body = site.parse_front_matter("---\ntitle: T\nkind: weapon\n---\n\n# T\n")
        self.assertEqual(meta, {"title": "T", "kind": "weapon"})
        self.assertTrue(body.startswith("\n# T"))

    def test_rejects_missing_or_open_block(self):
        with self.assertRaises(ValueError):
            site.parse_front_matter("# no front matter\n")
        with self.assertRaises(ValueError):
            site.parse_front_matter("---\ntitle: T\n# never closed\n")
        with self.assertRaises(ValueError):
            site.parse_front_matter("---\nnot a key value line\n---\n")


class Markdown(unittest.TestCase):
    def test_headings_get_github_anchors_and_repeats_are_numbered(self):
        anchors = [h[2] for h in site.headings_of("# Top\n\n## Same\n\n## Same\n\n```\n## inside code\n```\n")]
        self.assertEqual(anchors, ["top", "same", "same-1"])

    def test_renders_lists_tables_code_and_sections(self):
        html_text = site.render_markdown(
            "# T\n\n## Standard and desert\n\nA **bold** and a *soft* and `code`.\n\n- one\n- two\n\n"
            "| a | b |\n| --- | --- |\n| 1 | 2 |\n\n```python\nx = 1 < 2\n```\n", lambda t: t)
        self.assertIn('<section id="standard-and-desert" class="diff">', html_text)
        self.assertIn("<strong>bold</strong>", html_text)
        self.assertIn("<em>soft</em>", html_text)
        self.assertIn("<code>code</code>", html_text)
        self.assertIn("<ul><li>one</li><li>two</li></ul>", html_text)
        self.assertIn("<th>a</th>", html_text)
        self.assertIn("x = 1 &lt; 2", html_text)
        self.assertEqual(html_text.count("<section"), html_text.count("</section>"))

    def test_links_and_images_go_through_the_resolver(self):
        html_text = site.render_markdown("![Pic](img/a.webp)\n\nSee [it](other.md#sec).", lambda t: "X" + t)
        self.assertIn('<img src="Ximg/a.webp" alt="Pic"', html_text)
        self.assertIn('<a href="Xother.md#sec">it</a>', html_text)
        self.assertIn("<figure>", html_text)


class Checks(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.repo = Path(self.tmp.name)

    def tearDown(self):
        self.tmp.cleanup()

    def codes(self, outputs_and_findings):
        return sorted(f["code"] for f in outputs_and_findings[1])

    def test_a_good_wiki_has_no_findings_and_builds(self):
        make_wiki(self.repo, {})
        outputs, findings = site.build(self.repo)
        self.assertEqual(findings, [])
        self.assertIn("wiki/graph.json", outputs)
        self.assertIn("wiki/site/weapons/a.html", outputs)
        self.assertIn("wiki/site/index.html", outputs)

    def test_every_backlink_resolves_from_its_own_folder(self):
        make_wiki(self.repo, {})
        outputs, _ = site.build(self.repo)
        for name, text in outputs.items():
            if not name.endswith(".html") or '<section class="backlinks">' not in text:
                continue
            section = text.split('<section class="backlinks">', 1)[1].split("</section>", 1)[0]
            for href in re.findall(r'href="([^"]+)"', section):
                target = posixpath.normpath(posixpath.join(posixpath.dirname(name), href))
                self.assertIn(target, outputs, f"{name} links back to {href}")

    def test_missing_image_and_unused_image(self):
        wiki = make_wiki(self.repo, {}, images=("img/weapons/a.webp", "img/weapons/b.webp", "img/weapons/spare.webp"))
        (wiki / "weapons" / "b.md").write_text(PAGE.format(title="Beta", kind="weapon", mode="standard", image="img/weapons/gone.webp",
                                                           image_rel="img/weapons/gone.webp", other="weapons/a.md"), encoding="utf-8")
        codes = self.codes(site.build(self.repo))
        self.assertIn("WIKI_IMAGE_MISSING", codes)
        self.assertIn("WIKI_IMAGE_UNUSED", codes)

    def test_broken_link_and_missing_section(self):
        wiki = make_wiki(self.repo, {})
        text = (wiki / "weapons" / "a.md").read_text(encoding="utf-8")
        (wiki / "weapons" / "a.md").write_text(text.replace("## Standard and desert", "## Something else") + "\nSee [nowhere](../nope.md).\n",
                                               encoding="utf-8")
        codes = self.codes(site.build(self.repo))
        self.assertIn("WIKI_SECTION_MISSING", codes)
        self.assertIn("WIKI_LINK_BROKEN", codes)

    def test_orphan_and_too_few_links(self):
        wiki = make_wiki(self.repo, {})
        (wiki / "weapons" / "c.md").write_text(PAGE.format(title="Gamma", kind="weapon", mode="desert", image="img/weapons/a.webp",
                                                           image_rel="img/weapons/a.webp", other="weapons/a.md"), encoding="utf-8")
        codes = self.codes(site.build(self.repo))
        self.assertIn("WIKI_ORPHAN", codes)
        self.assertNotIn("WIKI_TOO_FEW_LINKS", codes)

    def test_a_page_with_one_outgoing_link_is_flagged(self):
        wiki = make_wiki(self.repo, {})
        (wiki / "weapons" / "a.md").write_text(PAGE.format(title="Alpha", kind="weapon", mode="standard", image="img/weapons/a.webp",
                                                           image_rel="img/weapons/a.webp", other="weapons/a.md").replace(
            "[the home page](../README.md) and ", ""), encoding="utf-8")
        codes = self.codes(site.build(self.repo))
        self.assertIn("WIKI_TOO_FEW_LINKS", codes)

    def test_wrong_folder_and_unknown_kind(self):
        wiki = make_wiki(self.repo, {})
        (wiki / "weapons" / "d.md").write_text(PAGE.format(title="Delta", kind="prop", mode="standard", image="img/weapons/a.webp",
                                                           image_rel="img/weapons/a.webp", other="weapons/a.md"), encoding="utf-8")
        codes = self.codes(site.build(self.repo))
        self.assertIn("WIKI_FOLDER", codes)

    def test_source_path_must_exist(self):
        wiki = make_wiki(self.repo, {})
        text = (wiki / "weapons" / "a.md").read_text(encoding="utf-8").replace("summary: A test page.",
                                                                              "summary: A test page.\nsources: tools/missing.py")
        (wiki / "weapons" / "a.md").write_text(text, encoding="utf-8")
        self.assertIn("WIKI_SOURCE_MISSING", self.codes(site.build(self.repo)))


class RoundTrip(unittest.TestCase):
    def test_write_then_compare_is_clean_and_an_edit_is_stale(self):
        with tempfile.TemporaryDirectory() as tmp:
            repo = Path(tmp)
            make_wiki(repo, {})
            outputs, findings = site.build(repo)
            self.assertEqual(findings, [])
            site.write(repo, outputs)
            self.assertEqual(site.compare(repo, outputs), [])
            (repo / "wiki" / "graph.json").write_text("{}\n", encoding="utf-8")
            stale = [f["path"] for f in site.compare(repo, outputs)]
            self.assertIn("wiki/graph.json", stale)
            (repo / "wiki" / "site" / "stray.html").write_text("x", encoding="utf-8")
            self.assertIn("wiki/site/stray.html", [f["path"] for f in site.compare(repo, outputs)])

    def test_layout_places_every_concept_even_with_a_cycle(self):
        concepts = [{"id": c, "label": c, "page": "README.md"} for c in ("a", "b", "c")]
        relations = [{"from": "a", "to": "b", "label": "x"}, {"from": "b", "to": "c", "label": "y"}, {"from": "c", "to": "a", "label": "z"}]
        coords, width, height = site.layout(concepts, relations)
        self.assertEqual(set(coords), {"a", "b", "c"})
        self.assertGreater(width, 0)
        self.assertGreater(height, 0)


if __name__ == "__main__":
    unittest.main()
