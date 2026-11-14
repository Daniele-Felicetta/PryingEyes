import tempfile
import unittest
import shutil
import subprocess
import sys
from pathlib import Path
from asset_graph import AssetGraph, recipe_sources
from content_catalog import ROOT, CATALOG
from room_catalog import CATALOG as ROOMS
from studio_pryingeyes import production_graph


class DependencyContracts(unittest.TestCase):
    @classmethod
    def setUpClass(cls): cls.graph = production_graph()

    def test_shared_pistol_recipe_reaches_data_driven_variants(self):
        ids = {a["id"] for a in self.graph.affected("modelli/armi/armi_pistole.py")}
        self.assertTrue({"p9_pieta", "p9_ronda", "p45_argine", "district_station"}.issubset(ids))
        self.assertNotIn("room_workshop_station", ids)

    def test_palette_section_reaches_room_and_level_without_invalidating_weapons(self):
        rows = self.graph.affected(ROOMS, "/materials/1/color")
        ids = {a["id"] for a in rows}
        self.assertEqual(ids, {"room_material_wood", "room_workshop_station", "room_depot_station", "room_offices_station",
                               "district_station"})
        self.assertEqual(next(a for a in rows if a["id"] == "district_station")["via"],
                         ["room_material_wood", "room_workshop_station", "district_station"])

    def test_weapon_json_section_is_scoped(self):
        ids = {a["id"] for a in self.graph.affected(CATALOG, "/weapons/29/definition/damage")}
        self.assertIn("p45_argine", ids); self.assertNotIn("p9_ronda", ids)

    def test_wave_package_change_reaches_city_rules_without_rebuilding_weapon_models(self):
        rows = self.graph.affected("unity/PryingEyes/Packages/com.pryingeyes.survival/Runtime/WaveSchedule.cs")
        self.assertEqual({r["id"] for r in rows}, {"city_survival", "district_station"})

    def test_access_package_change_reaches_only_access_rules_and_level(self):
        rows = self.graph.affected("unity/PryingEyes/Packages/com.pryingeyes.survival/Runtime/AreaAccess.cs")
        self.assertEqual({r["id"] for r in rows}, {"city_access", "district_station"})

    def test_graph_rejects_cycles_unknown_nodes_and_escape_paths(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for nodes in ([{"id": "a", "name": "a", "type": "room", "dependsOn": ["a"]}],
                          [{"id": "a", "name": "a", "type": "room", "dependsOn": ["missing"]}],
                          [{"id": "a", "name": "a", "type": "room", "sources": [{"path": "../escape.py"}]}]):
                with self.assertRaises(ValueError): AssetGraph(nodes, root)
            graph = AssetGraph([{"id": "a", "name": "a", "type": "room", "sources": [{"path": "deleted.py"}]}], root)
            self.assertEqual(graph.affected("deleted.py")[0]["id"], "a")
            with self.assertRaisesRegex(ValueError, "POINTER_INVALID"): graph.affected("deleted.py", "bad")

    def test_local_imports_are_analyzed_without_executing_source(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); package = root / "package"; package.mkdir()
            (package / "recipe.py").write_text("from . import helper\nraise RuntimeError('must not run')\n")
            (package / "helper.py").write_text("value=3\n")
            paths = {s["path"] for s in recipe_sources("package/recipe.py", root)}
            self.assertEqual(paths, {"package/recipe.py", "package/helper.py"})

    def test_graph_import_and_use_outside_the_game_repository(self):
        with tempfile.TemporaryDirectory() as directory:
            shutil.copy2(Path(__file__).with_name("asset_graph.py"), directory)
            result = subprocess.run([sys.executable, "-I", "-c",
                "import sys;sys.path.insert(0,sys.argv[1]);from asset_graph import AssetGraph;"
                "g=AssetGraph([{'id':'independent','type':'room','name':'Independent','sources':[{'path':'recipe.json'}]}],sys.argv[1]);"
                "assert g.affected('recipe.json')[0]['id']=='independent'", directory], capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stderr)


if __name__ == "__main__": unittest.main()
