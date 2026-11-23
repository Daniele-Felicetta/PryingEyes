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


GAME = check_suite.GAME
RULES = "namespace Game { public class Rules { public int Twice(int x) { return x * 2; } } }"
PLAYER = "using UnityEngine; namespace Game { public class Player : MonoBehaviour { void Start() { START } } }"
TEST = "#if !PE_DISTRIBUTION_BUILD\nusing UnityEngine;\nnamespace Game { public class PlayerTest : MonoBehaviour { public static void Begin() { } } }\n#endif\n"
GAME_LAB = '<Project><ItemGroup><Compile Include="..\\..\\' + GAME.replace("/", "\\") + '\\Scripts\\Rules.cs" /></ItemGroup></Project>'
UNITY_REFERENCE = '<Reference Include="UnityEngine.CoreModule"><HintPath>C:/Unity/Managed/UnityEngine.CoreModule.dll</HintPath></Reference>'


def write(path, text):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")


def build_game(root):
    """A minimal valid game: a rules file that a lab compiles, a production class, a wrapped test that nothing names, and no exceptions."""
    scripts = root / GAME / "Scripts"
    write(scripts / "Rules.cs", RULES)
    write(scripts / "Player.cs", PLAYER.replace("START", ""))
    write(scripts / "PlayerTest.cs", TEST)
    write(root / "tools/gamelab/GameLab.csproj", GAME_LAB)
    return scripts


def tolerate(root, **rules):
    """Write the baseline: {code: [entries]}."""
    write(root / check_suite.BASELINE, json.dumps({"schemaVersion": 1, **rules}))


def codes(findings):
    return sorted(item["code"] for item in findings)


class GameRules(unittest.TestCase):
    def run_in_a_game(self, damage):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); damage(root, build_game(root))
            return check_suite.check_game(root)

    def test_a_tree_without_the_game_has_nothing_to_check(self):
        with tempfile.TemporaryDirectory() as folder:
            self.assertEqual(check_suite.check_game(Path(folder)), [])

    def test_a_valid_minimal_game_passes_and_is_part_of_the_guard(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); build(root); scripts = build_game(root)
            self.assertEqual(check_suite.check_game(root), [])
            self.assertEqual(check(root), [])
            write(scripts / "Player.cs", PLAYER.replace("START", "PlayerTest.Begin();"))
            self.assertEqual(codes(check(root)), ["GAME_PRODUCTION_NAMES_TEST"])

    def test_every_violation_is_reported_with_its_code(self):
        flaws = {
            "GAME_PRODUCTION_NAMES_TEST": lambda r, s: write(s / "Player.cs", PLAYER.replace("START", "PlayerTest.Begin();")),
            "GAME_TEST_NOT_EXCLUDED": lambda r, s: write(s / "PlayerTest.cs", TEST.replace("#if !PE_DISTRIBUTION_BUILD", "").replace("#endif", "")),
            "GAME_PURE_FILE_NAMES_ENGINE": lambda r, s: write(s / "Rules.cs", "using UnityEngine; " + RULES),
            "GAME_BASELINE_STALE": lambda r, s: tolerate(r, GAME_TEST_NOT_EXCLUDED=[{"file": "Scripts/PlayerTest.cs", "reason": "fixed since"}]),
            "GAME_BASELINE_INVALID": lambda r, s: write(r / check_suite.BASELINE, "{not json"),
        }
        for code, damage in flaws.items():
            with self.subTest(code=code):
                found = self.run_in_a_game(damage)
                self.assertEqual(codes(found), [code])
                self.assertTrue(found[0]["path"].startswith((GAME + "/Scripts/", check_suite.BASELINE)), found[0]["path"])

    def test_a_finding_names_the_file_and_what_it_does(self):
        found = self.run_in_a_game(lambda r, s: write(s / "Player.cs", PLAYER.replace("START", "PlayerTest.Begin();")))
        self.assertEqual((found[0]["path"], found[0]["observed"]), (GAME + "/Scripts/Player.cs", "names the test type PlayerTest"))
        found = self.run_in_a_game(lambda r, s: write(s / "Rules.cs", "using UnityEngine; using UnityEditor; " + RULES))
        self.assertEqual(found[0]["observed"], "compiled by gamelab and names UnityEditor and UnityEngine")

    def test_the_baseline_tolerates_a_violation_until_it_is_fixed(self):
        entry = {"file": "Scripts/Player.cs", "names": "PlayerTest", "reason": "Player starts the test that the harness will start"}
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); scripts = build_game(root)
            write(scripts / "Player.cs", PLAYER.replace("START", "PlayerTest.Begin();"))
            self.assertEqual(codes(check_suite.check_game(root)), ["GAME_PRODUCTION_NAMES_TEST"])
            tolerate(root, GAME_PRODUCTION_NAMES_TEST=[entry])
            self.assertEqual(check_suite.check_game(root), [])
            tolerate(root, GAME_PRODUCTION_NAMES_TEST=[])      # removing an entry whose violation still exists fails
            self.assertEqual(codes(check_suite.check_game(root)), ["GAME_PRODUCTION_NAMES_TEST"])
            tolerate(root, GAME_PRODUCTION_NAMES_TEST=[entry])
            write(scripts / "Player.cs", PLAYER.replace("START", ""))      # fixing the violation and leaving the entry fails
            found = check_suite.check_game(root)
            self.assertEqual(codes(found), ["GAME_BASELINE_STALE"])
            self.assertIn("Scripts/Player.cs (PlayerTest)", found[0]["observed"])

    def test_an_entry_covers_the_test_type_it_names_and_no_other(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); scripts = build_game(root)
            write(scripts / "Player.cs", PLAYER.replace("START", "PlayerTest.Begin();"))
            tolerate(root, GAME_PRODUCTION_NAMES_TEST=[{"file": "Scripts/Player.cs", "names": "OtherTest", "reason": "an old one"}])
            self.assertEqual(codes(check_suite.check_game(root)), ["GAME_BASELINE_STALE", "GAME_PRODUCTION_NAMES_TEST"])

    def test_an_entry_covers_one_file_and_a_new_unwrapped_test_still_fails(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); scripts = build_game(root)
            unwrapped = TEST.replace("#if !PE_DISTRIBUTION_BUILD", "").replace("#endif", "")
            write(scripts / "PlayerTest.cs", unwrapped); write(scripts / "OtherTest.cs", unwrapped.replace("PlayerTest", "OtherTest"))
            tolerate(root, GAME_TEST_NOT_EXCLUDED=[{"file": "Scripts/PlayerTest.cs", "reason": "older than the wrapper"}])
            found = check_suite.check_game(root)
            self.assertEqual([(item["code"], item["path"]) for item in found], [("GAME_TEST_NOT_EXCLUDED", GAME + "/Scripts/OtherTest.cs")])

    def test_a_baseline_that_is_not_valid_tolerates_nothing(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); scripts = build_game(root)
            write(scripts / "PlayerTest.cs", TEST.replace("#if !PE_DISTRIBUTION_BUILD", "").replace("#endif", ""))
            flaws = {
                "no reason": {"GAME_TEST_NOT_EXCLUDED": [{"file": "Scripts/PlayerTest.cs"}]},
                "an empty reason": {"GAME_TEST_NOT_EXCLUDED": [{"file": "Scripts/PlayerTest.cs", "reason": "  "}]},
                "not an entry": {"GAME_TEST_NOT_EXCLUDED": ["Scripts/PlayerTest.cs"]},
                "not a list": {"GAME_TEST_NOT_EXCLUDED": {"file": "Scripts/PlayerTest.cs", "reason": "x"}},
                "a rule that does not exist": {"GAME_TEST_NOT_EXCLUDDED": [{"file": "Scripts/PlayerTest.cs", "reason": "a typo"}]},
            }
            for name, rules in flaws.items():
                with self.subTest(name):
                    tolerate(root, **rules)
                    self.assertEqual(codes(check_suite.check_game(root)), ["GAME_BASELINE_INVALID", "GAME_TEST_NOT_EXCLUDED"])
            write(root / check_suite.BASELINE, "[1, 2]")
            self.assertEqual(codes(check_suite.check_game(root)), ["GAME_BASELINE_INVALID", "GAME_TEST_NOT_EXCLUDED"])
            tolerate(root, GAME_PRODUCTION_NAMES_TEST=[{"file": "Scripts/Player.cs", "reason": "no type is said"}])      # a rule that needs `names`
            self.assertEqual(codes(check_suite.check_game(root)), ["GAME_BASELINE_INVALID", "GAME_TEST_NOT_EXCLUDED"])

    def test_a_project_that_reaches_unity_is_a_compile_check_and_not_a_lab(self):
        variants = {
            "a lab": ("", ["GAME_PURE_FILE_NAMES_ENGINE"]),
            "a reference to a Unity assembly": (f"<ItemGroup>{UNITY_REFERENCE}</ItemGroup>", []),
            "an import of a file that holds one": ('<Import Project="Unity.props" />', []),
            "a reference to a project that holds one": ('<ItemGroup><ProjectReference Include="Unity.csproj" /></ItemGroup>', []),
        }
        for name, (extra, expected) in variants.items():
            with self.subTest(name), tempfile.TemporaryDirectory() as folder:
                root = Path(folder); scripts = build_game(root)
                write(scripts / "Rules.cs", "using UnityEngine; " + RULES)
                write(root / "tools/gamelab/GameLab.csproj", GAME_LAB.replace("<ItemGroup>", extra + "<ItemGroup>", 1))
                write(root / "tools/gamelab/Unity.props", f"<Project><ItemGroup>{UNITY_REFERENCE}</ItemGroup></Project>")
                write(root / "tools/gamelab/Unity.csproj", f"<Project><ItemGroup>{UNITY_REFERENCE}</ItemGroup></Project>")
                self.assertEqual(codes(check_suite.check_game(root)), expected)

    def test_imports_that_refer_to_each_other_end(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            write(root / "a.csproj", '<Project><Import Project="b.props" /></Project>')
            write(root / "b.props", '<Project><Import Project="a.csproj" /></Project>')
            self.assertFalse(check_suite.reaches_unity(root / "a.csproj"))
            write(root / "b.props", f'<Project><Import Project="a.csproj" /><ItemGroup>{UNITY_REFERENCE}</ItemGroup></Project>')
            self.assertTrue(check_suite.reaches_unity(root / "a.csproj"))

    def test_a_lab_that_takes_files_by_pattern_or_from_a_build_folder(self):
        folder_of_the_game = "..\\..\\" + GAME.replace("/", "\\") + "\\Scripts\\"
        patterns = {"a name with *": folder_of_the_game + "R*.cs", "a name with **": "..\\..\\" + GAME.replace("/", "\\") + "\\**\\R*.cs"}
        for name, pattern in patterns.items():
            with self.subTest(name), tempfile.TemporaryDirectory() as folder:
                root = Path(folder); scripts = build_game(root)
                write(scripts / "Rules.cs", "using UnityEngine; " + RULES)
                write(root / "tools/gamelab/GameLab.csproj", GAME_LAB.replace(folder_of_the_game + "Rules.cs", pattern))
                self.assertEqual([item["path"] for item in check_suite.check_game(root)], [GAME + "/Scripts/Rules.cs"])
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); scripts = build_game(root)
            write(scripts / "Rules.cs", "using UnityEngine; " + RULES)
            write(root / "tools/gamelab/GameLab.csproj", "<Project />")
            write(root / "tools/gamelab/obj/Copy.csproj", GAME_LAB.replace('"..\\..\\', '"..\\..\\..\\'))      # what a build leaves behind is no project of the repository
            self.assertEqual(check_suite.check_game(root), [])
            write(root / "tools/gamelab/Copy.csproj", GAME_LAB)      # the same project, outside bin and obj, is one
            self.assertEqual(codes(check_suite.check_game(root)), ["GAME_PURE_FILE_NAMES_ENGINE"])


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
