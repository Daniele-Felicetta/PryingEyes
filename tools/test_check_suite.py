"""The suite's boundary guard: the real repository passes it, and every kind of violation is reported."""
import json
from pathlib import Path
import tempfile
import unittest

import check_suite
from check_suite import ROOT, check

ASMDEF = {"name": "Rules", "references": [], "noEngineReferences": True}
SOURCE = 'namespace Rules { class Counter { string note = "UnityEngine in a string is fine"; /* PryingEyes in a comment too */ } }'
LAB = '<Project><ItemGroup><Compile Include="..\\..\\unity\\PryingEyes\\Packages\\com.example.rules\\Runtime\\Rules.cs" /></ItemGroup></Project>'


def build(root):
    """A minimal valid suite: one package, the lab that compiles it, the project that consumes it, the engine modules."""
    package = root / "unity/PryingEyes/Packages/com.example.rules"
    (package / "Runtime").mkdir(parents=True)
    (package / "package.json").write_text(json.dumps({"name": "com.example.rules"}), encoding="utf-8")
    (package / "README.md").write_text("# Rules\n\nrules", encoding="utf-8")
    (package / "Runtime/Rules.asmdef").write_text(json.dumps(ASMDEF), encoding="utf-8")
    (package / "Runtime/Rules.cs").write_text(SOURCE, encoding="utf-8")
    (root / "tools/lab").mkdir(parents=True)
    (root / "tools/lab/Lab.csproj").write_text(LAB, encoding="utf-8")
    (root / "unity/LayoutDemo/Packages").mkdir(parents=True)
    consumer = {"dependencies": {"com.example.rules": "file:../../PryingEyes/Packages/com.example.rules"}}
    (root / "unity/LayoutDemo/Packages/manifest.json").write_text(json.dumps(consumer), encoding="utf-8")
    for name in check_suite.ENGINE_MODULES: (root / "tools" / name).write_text("def run(job):\n    return job\n", encoding="utf-8")
    return package


class SuiteBoundaries(unittest.TestCase):
    def test_repository_respects_its_own_boundaries(self):
        self.assertEqual(check(ROOT), [])

    def test_valid_minimal_suite_passes(self):
        with tempfile.TemporaryDirectory() as folder:
            build(Path(folder)); self.assertEqual(check(folder), [])

    def test_every_violation_is_reported_with_its_code(self):
        def write(path, text): path.write_text(text, encoding="utf-8")
        flaws = {
            "PACKAGE_FORBIDDEN_NAME": lambda r, p: write(p / "Runtime/Rules.cs", "using UnityEngine; " + SOURCE),
            "PACKAGE_ENGINE_REFERENCES": lambda r, p: write(p / "Runtime/Rules.asmdef", json.dumps({**ASMDEF, "noEngineReferences": False})),
            "PACKAGE_ASSEMBLY_REFERENCES": lambda r, p: write(p / "Runtime/Rules.asmdef", json.dumps({**ASMDEF, "references": ["GameCode"]})),
            "PACKAGE_ASMDEF_MISSING": lambda r, p: (p / "Runtime/Rules.asmdef").unlink(),
            "PACKAGE_README_MISSING": lambda r, p: (p / "README.md").unlink(),
            "PACKAGE_NAME": lambda r, p: write(p / "package.json", json.dumps({"name": "com.example.other"})),
            "PACKAGE_NOT_COMPILED_OUTSIDE_UNITY": lambda r, p: write(r / "tools/lab/Lab.csproj", "<Project />"),
            "PACKAGE_NOT_CONSUMED": lambda r, p: write(r / "unity/LayoutDemo/Packages/manifest.json", json.dumps({"dependencies": {}})),
            "ENGINE_NAMES_A_PROJECT": lambda r, p: write(r / "tools/studio_engine.py", "def run(job):\n    return load_weapon(job)\n"),
            "ENGINE_MODULE_MISSING": lambda r, p: (r / "tools/studio_queue.py").unlink(),
        }
        for code, damage in flaws.items():
            with self.subTest(code=code), tempfile.TemporaryDirectory() as folder:
                root = Path(folder); damage(root, build(root))
                self.assertIn(code, {item["code"] for item in check(root)})

    def test_a_copied_source_is_not_the_compiled_one(self):
        # A lab that compiles a copy of the file proves nothing about the package: the guard must look at the real path.
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); package = build(root)
            (root / "tools/lab/Rules.cs").write_text(SOURCE, encoding="utf-8")
            (root / "tools/lab/Lab.csproj").write_text('<Project><ItemGroup><Compile Include="Rules.cs" /></ItemGroup></Project>', encoding="utf-8")
            self.assertEqual([item["code"] for item in check(root)], ["PACKAGE_NOT_COMPILED_OUTSIDE_UNITY"])
            self.assertEqual(check(root)[0]["path"], str((package / "Runtime/Rules.cs").relative_to(root)).replace("\\", "/"))


def build_docs(root):
    """A minimal valid documentation set: a README, the index, and the one page the index lists."""
    (root / "docs/game").mkdir(parents=True)
    (root / "README.md").write_text("# Title\n\nSee [the docs](docs/README.md) and [the page](docs/game/page.md).\n", encoding="utf-8")
    (root / "docs/README.md").write_text("# Docs\n\n- [Page](game/page.md)\n", encoding="utf-8")
    (root / "docs/game/page.md").write_text("# Page\n\n## Section One\n\nBack to the [index](../README.md#docs) and to [here](#section-one).\n", encoding="utf-8")
    return root / "docs/game/page.md"


class Documentation(unittest.TestCase):
    def codes(self, root):
        return {item["code"] for item in check_suite.check_docs(root)}

    def test_valid_documentation_passes_and_is_part_of_the_guard(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); build(root); build_docs(root)
            self.assertEqual(check(root), [])
            (root / "README.md").write_text("# Title\n\n[gone](docs/gone.md)\n", encoding="utf-8")
            self.assertIn("DOC_LINK_BROKEN", {item["code"] for item in check(root)})

    def test_a_tree_without_docs_has_nothing_to_check(self):
        with tempfile.TemporaryDirectory() as folder:
            self.assertEqual(check_suite.check_docs(Path(folder)), [])

    def test_every_violation_is_reported_with_its_code(self):
        def write(path, text): path.write_text(text, encoding="utf-8")
        flaws = {
            "DOC_LINK_BROKEN": lambda r, p: write(r / "README.md", "# T\n\n[gone](docs/gone.md)\n"),
            "DOC_ANCHOR_BROKEN": lambda r, p: write(p, "# Page\n\nSee [x](../README.md#no-such-heading).\n"),
            "DOC_PATH_MISSING": lambda r, p: write(p, "# Page\n\nSee `docs/game/ghost.md`.\n"),
            "DOC_NOT_INDEXED": lambda r, p: write(r / "docs/game/other.md", "# Other\n"),
            "DOC_INDEX_MISSING": lambda r, p: (r / "docs/README.md").unlink(),
            "DOC_TITLE_MISSING": lambda r, p: write(p, "Some text before any title\n\n# Page\n"),
            "DOC_MACHINE_PATH": lambda r, p: write(p, "# Page\n\n    cd " + "C:" + "\\" + "Users" + "\\" + "someone" + "\\" + "game\n"),
            "DOC_REPLACEMENT_CHARACTER": lambda r, p: write(p, "# Page\n\ncaf" + chr(0xFFFD) + "\n"),
            "DOC_NOT_UTF8": lambda r, p: p.write_bytes(b"# Page\n\ncaf\xe9\n"),
        }
        for code, damage in flaws.items():
            with self.subTest(code=code), tempfile.TemporaryDirectory() as folder:
                root = Path(folder); damage(root, build_docs(root))
                self.assertIn(code, self.codes(root))

    def test_what_is_not_a_link_is_not_checked(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); page = build_docs(root)
            page.write_text("# Page\n\n## Same\n\n## Same\n\n```md\n[x](gone.md) `docs/ghost.md`\n```\n\nInline `[y](gone.md)`, "
                            "[web](https://example.com/gone.md), [mail](mailto:a@b.c), [folder](../game) and [again](#same-1).\n", encoding="utf-8")
            self.assertEqual(check_suite.check_docs(root), [])

    def test_anchors_follow_the_github_rules(self):
        self.assertEqual(check_suite.heading_slugs("# A title\n## 3.5 Still in the code, not in the README\n## `Weapon.cs` (notes)\n## Same\n## Same\n"),
                         {"a-title", "35-still-in-the-code-not-in-the-readme", "weaponcs-notes", "same", "same-1"})


class Wiki(unittest.TestCase):
    def test_a_tree_without_a_wiki_has_nothing_to_check(self):
        with tempfile.TemporaryDirectory() as folder:
            self.assertEqual(check_suite.check_wiki(Path(folder)), [])

    def test_a_wiki_without_its_home_page_is_a_finding(self):
        with tempfile.TemporaryDirectory() as folder:
            (Path(folder) / "wiki").mkdir()
            self.assertIn("WIKI_HOME", {item["code"] for item in check(folder)})


if __name__ == "__main__": unittest.main()
