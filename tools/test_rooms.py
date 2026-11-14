"""Production contracts: resize the real recipe, reject blocked exits and stale bindings."""
import copy
import json
import tempfile
import unittest
from pathlib import Path
from room_catalog import CATALOG, compile_room, compile_rooms, validate_rooms


class RoomContracts(unittest.TestCase):
    def test_real_recipe_resize_and_blocked_exit(self):
        data = validate_rooms(); room = data["rooms"][0]
        with tempfile.TemporaryDirectory() as folder:
            for width, depth in ((25, 15), (20, 12), (32, 20)):
                plan = compile_room(room["id"], folder, width, depth)
                self.assertEqual(len(plan["parts"]), len(room["parts"]))
                self.assertEqual(len(plan["doors"]), 2)
                self.assertEqual((plan["width"], plan["depth"]), (width, depth))
            blocked = copy.deepcopy(data)
            blocked["rooms"][0]["parts"][0]["position"] = {"x": 0, "y": .55, "z": 6}
            source = Path(folder) / "blocked.json"; source.write_text(json.dumps(blocked))
            with self.assertRaisesRegex(ValueError, "PASSAGE_BLOCKED"):
                compile_room(room["id"], folder, catalog=source)

    def test_schema_rejects_wrong_material_and_unknown_fields(self):
        with tempfile.TemporaryDirectory() as folder:
            source = Path(folder) / "bad.json"
            for change in ("material", "unknown", "nonfinite"):
                data = json.loads(CATALOG.read_text()); part = data["rooms"][0]["parts"][0]
                if change == "material": part["material"] = "missing"
                elif change == "unknown": part["colider"] = True
                else: part["position"]["x"] = float("nan")
                source.write_text(json.dumps(data))
                with self.assertRaises(ValueError): validate_rooms(source)

    def test_bindings_cannot_silently_ignore_or_duplicate_rooms(self):
        with tempfile.TemporaryDirectory() as folder:
            binding = {"block": "unknown", "asset": "room_workshop_station"}
            with self.assertRaisesRegex(ValueError, "ROOM_BINDING"):
                compile_rooms({"rooms": [binding]}, {"blocks": []}, folder)


if __name__ == "__main__": unittest.main()
