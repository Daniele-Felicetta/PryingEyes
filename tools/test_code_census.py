"""The code census: what it counts as a reference, an assembly, a pure file, a flag and a test, on a small invented tree."""
import contextlib
import io
import json
from pathlib import Path
import tempfile
import unittest

import code_census
from code_census import blank, census, components

GAME = "unity/PryingEyes/Assets/_PryingEyes"
SOURCES = {
    # the root folder: names Beta (a type of Proc) and its own test, reads a flag, keeps a mutable static
    GAME + "/Scripts/Alpha.cs": """
        using UnityEngine;
        namespace Game {
            public class Alpha : MonoBehaviour {
                public static Alpha I;
                static int count = 0;
                const int Limit = 3;
                Proc.Beta beta;                      // qualified by a namespace: a reference
                void Start() { if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-alphatest") >= 0) AlphaTest.Begin(); }
                static bool HasArg(string a) { return false; }
            }
        }""",
    # none of these is a reference to Beta or to Pure: a comment, a string, a member access, a field, a parameter and an
    # enum member with that name, a type nested in the file itself
    GAME + "/Scripts/Gamma.cs": """
        using UnityEngine;
        namespace Game {
            public class Gamma {
                // Beta in a comment
                string note = "Beta in a string";
                int Read(Holder holder, int Beta) { return holder.Beta; }
            }
            public class Holder { public int Beta; public float Pure = 1f; }
            public enum Kind { Beta, Pure }
            public class Delta { enum Pure { None } }
        }""",
    GAME + "/Scripts/AlphaTest.cs": """
        using UnityEngine;
        namespace Game {
            public class AlphaTest : MonoBehaviour {
                [RuntimeInitializeOnLoadMethod] static void Install() { }
                public static void Begin() { }
                void Check(string name, bool ok) { Debug.Log("unity_logs/alpha"); }
            }
        }""",
    GAME + "/Scripts/Proc/Beta.cs": """
        using UnityEngine;
        namespace Game.Proc { public partial class Beta { public Alpha owner; Pure rules; } }""",
    GAME + "/Scripts/Deserto/Beta.Sand.cs": """
        using UnityEngine;
        namespace Game.Proc { public partial class Beta { float sand; } }""",
    GAME + "/Scripts/Proc/Pure.cs": """
        using System;
        namespace Game.Proc { public sealed class Pure { public int Twice(int x) { return x * 2; } } }""",
    GAME + "/Editor/Tool.cs": """
        using UnityEditor;
        namespace Game.EditorTools { public static class Tool { public static void Run() { var a = typeof(Alpha); string f = "-toolout"; } } }""",
    # a package cannot see the game, so a name it shares with a game type is not a reference
    "unity/PryingEyes/Packages/com.example.rules/Runtime/Rule.cs": """
        namespace Rules { public sealed class Rule { public int Alpha; public Rule Next(Rule Gamma) { return Gamma; } } }""",
}
ASMDEF = {"name": "Rules", "references": [], "noEngineReferences": True}
LAB = '<Project><ItemGroup><Compile Include="..\\..\\unity\\PryingEyes\\Assets\\_PryingEyes\\Scripts\\Proc\\Pure.cs" /></ItemGroup></Project>'


def build(root):
    for name, text in SOURCES.items():
        path = root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")
    (root / "unity/PryingEyes/Packages/com.example.rules/Runtime/Rules.asmdef").write_text(json.dumps(ASMDEF), encoding="utf-8")
    (root / "tools/purelab").mkdir(parents=True)
    (root / "tools/purelab/PureLab.csproj").write_text(LAB, encoding="utf-8")


class Blanking(unittest.TestCase):
    def test_comments_strings_and_characters_become_spaces_of_the_same_length(self):
        code = 'int a = 1; // Beta\nstring s = "Beta \\" still"; char c = \'B\'; /* Beta\n more */ var v = @"x ""Beta"" y"; Beta b;'
        blanked = blank(code)
        self.assertEqual(len(blanked), len(code))
        self.assertEqual(blanked.count("\n"), code.count("\n"))
        self.assertEqual(blanked.count("Beta"), 1)
        self.assertTrue(blanked.rstrip().endswith("Beta b;"))


class Census(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.folder = tempfile.TemporaryDirectory()
        cls.root = Path(cls.folder.name)
        build(cls.root)
        cls.folders = census(cls.root, by_folder=True)
        cls.groups = census(cls.root)

    @classmethod
    def tearDownClass(cls):
        cls.folder.cleanup()

    def edge(self, result, source, target):
        return next((e["types"] for e in result["edges"] if e["from"] == source and e["to"] == target), None)

    def test_assemblies_follow_the_definitions_and_the_editor_folders(self):
        assemblies = self.folders["assemblies"]
        self.assertEqual(assemblies["Assembly-CSharp"]["files"], 6)
        self.assertEqual(assemblies["Assembly-CSharp-Editor"]["files"], 1)
        self.assertEqual((assemblies["Rules"]["defined"], assemblies["Rules"]["engine"]), (True, False))

    def test_a_reference_is_a_name_used_as_a_type_from_code_that_can_see_it(self):
        self.assertEqual(self.edge(self.folders, "Scripts", "Scripts/Proc"), ["Beta"])            # Alpha only: Gamma never names the type
        self.assertEqual(self.edge(self.folders, "Scripts/Proc", "Scripts"), ["Alpha"])
        self.assertEqual(self.edge(self.folders, "Editor", "Scripts"), ["Alpha"])
        self.assertIsNone(self.edge(self.folders, "Packages/com.example.rules/Runtime", "Scripts"))
        self.assertEqual(self.folders["mutual"], [("Scripts", "Scripts/Proc")])
        self.assertIn(["Scripts", "Scripts/Proc"], self.folders["cycles"])

    def test_fan_in_counts_the_other_files_that_name_a_type(self):
        fan_in = {row["type"]: row["files"] for row in self.folders["fanIn"]}
        self.assertEqual(fan_in["Alpha"], 2)          # Beta.cs and the editor tool; the package's field named Alpha is not one
        self.assertEqual(fan_in["Beta"], 1)

    def test_a_partial_type_spread_over_two_groups_is_reported(self):
        self.assertEqual(self.folders["partials"], [{"type": "Beta", "home": "Scripts/Proc", "files": 2, "lines": 6,
                                                      "groups": {"Scripts/Proc": 1, "Scripts/Deserto": 1}}])

    def test_pure_files_name_no_engine_and_say_which_lab_compiles_them(self):
        pure = {row["file"]: row for row in self.folders["pure"]}
        self.assertEqual(list(pure), ["Scripts/Proc/Pure.cs"])
        self.assertEqual(pure["Scripts/Proc/Pure.cs"]["labs"], ["purelab"])

    def test_tests_are_a_group_of_their_own_and_production_code_naming_them_is_listed(self):
        self.assertEqual(self.groups["groups"]["(tests)"]["files"], 1)
        tests = self.groups["tests"]
        self.assertEqual((tests["files"], tests["selfInstalling"], tests["ownCheckHelper"]), (1, 1, 1))
        self.assertEqual(tests["notExcludedFromDistribution"], ["Scripts/AlphaTest.cs"])
        self.assertEqual(tests["namedByProductionCode"], {"Scripts/Alpha.cs": ["AlphaTest"]})
        self.assertEqual(tests["testOutputLiterals"], 1)

    def test_flags_and_statics_are_counted_outside_tests_and_editor_tools(self):
        flags = self.groups["flags"]
        self.assertEqual((flags["distinct"], flags["runtime"], flags["editorOnly"], flags["namedTest"]), (2, 1, 1, 1))
        self.assertEqual((flags["commandLineReads"], flags["privateHelpers"], flags["productionFiles"]), (1, 1, 1))
        statics = self.groups["statics"]
        self.assertEqual((statics["declarations"], statics["singletons"]), (2, 1))                # I and count; the constant is not one

    def test_the_command_line_prints_every_section_and_writes_the_data(self):
        target = self.root / "census.json"
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            self.assertEqual(code_census.main(["--root", str(self.root), "--json", str(target)]), 0)
        for section in code_census.SECTIONS: self.assertIn("== " + section, out.getvalue())
        written = json.loads(target.read_text(encoding="utf-8"))
        self.assertEqual((written["schemaVersion"], written["flags"]["distinct"]), (1, 2))


class Harness(unittest.TestCase):
    """TestRun is what the game may name in every build, so it is not a test; PlayerTest is the one definition of Check."""
    SOURCES = {
        # production code asks TestRun and is allowed to; Leak names the harness itself and must be found
        "Scripts/Manager.cs": "using UnityEngine; namespace Game { public class Manager : MonoBehaviour { bool Quiet() { return TestRun.Dedicated; } } }",
        "Scripts/Leak.cs": "using UnityEngine; namespace Game { public class Leak : MonoBehaviour { PlayerTest owner; } }",
        "Scripts/Testing/TestRun.cs": "namespace Game { public static class TestRun { public static bool Dedicated; static string Logs = \"unity_logs\"; } }",
        "Scripts/Testing/PlayerTest.cs": "#if !PE_DISTRIBUTION_BUILD\nusing UnityEngine;\nnamespace Game { public abstract class PlayerTest : MonoBehaviour { protected bool Check(string name, bool ok) { return ok; } } }\n#endif\n",
        # a test on the harness has no Check of its own; an old one still has
        "Scripts/BetaTest.cs": "#if !PE_DISTRIBUTION_BUILD\nnamespace Game { public sealed class BetaTest : PlayerTest { [UnityEngine.RuntimeInitializeOnLoadMethod] static void Install() { } } }\n#endif\n",
        "Scripts/OldTest.cs": "#if !PE_DISTRIBUTION_BUILD\nusing UnityEngine;\nnamespace Game { public sealed class OldTest : MonoBehaviour { void Check(string name, bool ok) { } string folder = \"unity_logs/old\"; } }\n#endif\n",
    }

    def test_the_harness_counts_once_and_testrun_is_not_a_test(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            for name, text in self.SOURCES.items():
                path = root / GAME / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(text, encoding="utf-8")
            tests = census(root)["tests"]
        self.assertEqual(tests["files"], 3)                                     # PlayerTest, BetaTest and OldTest; TestRun is none of them
        self.assertEqual((tests["selfInstalling"], tests["ownCheckHelper"]), (1, 1))      # OldTest alone: the harness defines Check for everybody
        self.assertEqual(tests["notExcludedFromDistribution"], [])              # TestRun has no wrapper and needs none
        self.assertEqual(tests["testOutputLiterals"], 1)                        # OldTest's; TestRun is where unity_logs/ is resolved against the checkout
        self.assertEqual(tests["namedByProductionCode"], {"Scripts/Leak.cs": ["PlayerTest"]})     # Manager naming TestRun is not reported


class Components(unittest.TestCase):
    def test_groups_that_reach_each_other_form_one_component(self):
        graph = {"a": {"b"}, "b": {"c"}, "c": {"a"}, "d": {"a"}, "e": set()}
        found = sorted(sorted(c) for c in components(graph, ["a", "b", "c", "d", "e"]))
        self.assertEqual(found, [["a", "b", "c"], ["d"], ["e"]])


if __name__ == "__main__":
    unittest.main()
