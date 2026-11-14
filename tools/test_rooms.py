"""Production contracts: resize the real recipe, reject blocked exits and stale bindings, stairs and standing places."""
import copy
import json
import tempfile
import unittest
from pathlib import Path
from room_catalog import CATALOG, check_catalog, compile_room, compile_rooms, validate_rooms


def flight(name, x, z, yaw, steps=15, width=1.6, rise=3.0, run=3.75):
    return {"id": name, "kind": "stairs", "material": "concrete", "position": {"x": x, "y": rise / 2, "z": z},
            "size": {"x": width, "y": rise, "z": run}, "yaw": yaw, "collider": True, "steps": steps}


def variant(base, name, edit):
    room = copy.deepcopy(base); room["id"] = "t_" + name; edit(room); return room


def plan_variants(materials, rooms, **size):
    """Plan several recipe variants in one planner run: {room id: {"plan": ...} or {"error": ...}}."""
    with tempfile.TemporaryDirectory() as folder:
        source = Path(folder) / "variants.json"
        source.write_text(json.dumps({"schemaVersion": 1, "materials": materials, "rooms": rooms}))
        return check_catalog(Path(folder) / "out", source, **size)


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

    def test_block_too_small_for_its_room_is_refused_naming_room_and_block(self):
        # The depot's flight keeps a 0.25 m tread only in a block 15 m deep: at 12 m (the smallest depth planned) the room cannot fit.
        layout = {"blocks": [{"id": "Building_1_-1", "width": 25, "depth": 12}]}
        with tempfile.TemporaryDirectory() as folder:
            with self.assertRaisesRegex(ValueError, r"ROOM_DOES_NOT_FIT: room_depot_station on Building_1_-1 at 25 x 12 m: ROOM_STAIRS_SHORT"):
                compile_rooms({"rooms": [{"block": "Building_1_-1", "asset": "room_depot_station"}]}, layout, folder)

    def test_every_room_in_the_catalog_plans_at_its_own_size(self):
        data = validate_rooms()
        with tempfile.TemporaryDirectory() as folder:
            results = check_catalog(folder)
        self.assertEqual({room["id"] for room in data["rooms"]}, set(results))
        for room in data["rooms"]:
            self.assertIn("plan", results[room["id"]], room["id"] + ": " + str(results[room["id"]].get("error")))
            self.assertGreaterEqual(len(results[room["id"]]["plan"]["doors"]), 2)


class RoomFlights(unittest.TestCase):
    """A stairs part is one recipe entry that the planner expands into solid steps a walking agent can climb."""

    @classmethod
    def setUpClass(cls):
        cls.data = validate_rooms(); cls.base = cls.data["rooms"][0]

    def test_flights_expand_into_steps_in_every_direction(self):
        flights = {0: flight("up_z", -5.5, -5.0, 0), 90: flight("up_x", -9.5, -4.8, 90),
                   180: flight("down_z", 5.5, -5.0, 180), 270: flight("down_x", 9.5, -6.2, 270)}
        room = variant(self.base, "flights", lambda r: r["parts"].extend(flights.values()))
        plan = plan_variants(self.data["materials"], [room])["t_flights"]["plan"]
        self.assertEqual(len(plan["parts"]), len(self.base["parts"]) + 4 * 15)
        by_id = {part["id"]: part for part in plan["parts"]}
        axis = {0: ("z", 1), 90: ("x", 1), 180: ("z", -1), 270: ("x", -1)}
        for yaw, source in flights.items():
            steps = [by_id["%s_%02d" % (source["id"], i)] for i in range(1, 16)]
            tops = [s["position"]["y"] + s["size"]["y"] / 2 for s in steps]
            for before, after in zip(tops, tops[1:]):
                self.assertAlmostEqual(after - before, 0.2, places=4)
            self.assertAlmostEqual(tops[0], 0.2, places=4); self.assertAlmostEqual(tops[-1], 3.0, places=4)
            self.assertTrue(all(s["collider"] and s["kind"] == "box" and s["yaw"] == yaw for s in steps))
            name, sign = axis[yaw]
            along = [s["position"][name] for s in steps]
            self.assertTrue(all(sign * (b - a) > 0.24 for a, b in zip(along, along[1:])), "steps climb the wrong way at yaw %d" % yaw)
            floating = [s for s in steps if s["position"]["y"] - s["size"]["y"] / 2 > 0.001]
            self.assertEqual(floating, [], "every step must stand on the floor")

    def test_flights_nobody_could_climb_are_rejected(self):
        def edit(name, **change):
            return variant(self.base, name, lambda r: r["parts"].append({**flight("stair", -5.5, -5.0, 0), **change}))
        rooms = [edit("steep", steps=10), edit("short", steps=20), edit("narrow", size={"x": 1.0, "y": 3.0, "z": 3.75}),
                 edit("solid", collider=False), edit("single", steps=1),
                 variant(self.base, "box_steps", lambda r: r["parts"][0].update(steps=3)),
                 variant(self.base, "door", lambda r: r["parts"].append(flight("stair", 0, 5.0, 0)))]
        results = plan_variants(self.data["materials"], rooms)
        expected = {"t_steep": "ROOM_STAIRS_STEEP", "t_short": "ROOM_STAIRS_SHORT", "t_narrow": "ROOM_STAIRS_NARROW",
                    "t_solid": "ROOM_STAIRS_COLLIDER", "t_single": "ROOM_STAIRS_STEPS", "t_box_steps": "ROOM_PART_INVALID",
                    "t_door": "PASSAGE_BLOCKED"}
        for name, code in expected.items():
            self.assertIn(code, results[name].get("error", ""), name + ": " + str(results[name])[:200])

    def test_schema_keeps_steps_with_stairs_only(self):
        with tempfile.TemporaryDirectory() as folder:
            source = Path(folder) / "schema.json"
            for name, edit in (("missing", lambda r: r["parts"][0].pop("steps")), ("box", lambda r: r["parts"][1].update(steps=3)),
                               ("hollow", lambda r: r["parts"][0].update(collider=False))):
                data = copy.deepcopy(self.data); data["rooms"][0]["parts"].insert(0, flight("stair", -5.5, -5.0, 0))
                edit(data["rooms"][0]); source.write_text(json.dumps(data))
                with self.assertRaisesRegex(ValueError, "ROOM_SCHEMA", msg=name): validate_rooms(source)
            data = copy.deepcopy(self.data); data["rooms"][0]["parts"].insert(0, flight("stair", -5.5, -5.0, 0))
            source.write_text(json.dumps(data)); validate_rooms(source)


class RoomStandingPlaces(unittest.TestCase):
    """Route points and views are places to stand: on the floor or on top of a solid, never inside furniture or in the air."""

    @classmethod
    def setUpClass(cls):
        cls.data = validate_rooms(); cls.base = cls.data["rooms"][0]

    def test_route_points_must_rest_on_a_surface(self):
        dais = {"id": "dais", "kind": "box", "material": "concrete", "position": {"x": -5.5, "y": 0.5, "z": -5.0},
                "size": {"x": 3, "y": 1, "z": 3}, "yaw": 0, "collider": True}
        def route(*points): return lambda r: r.update(route=[{"x": x, "y": y, "z": z} for x, y, z in points])
        rooms = [variant(self.base, "on_dais", lambda r: (r["parts"].append(dais), route((0, 0, 5.5), (-5.5, 1.0, -5.0))(r))),
                 variant(self.base, "floating", route((0, 0, 5.5), (-5.5, 1.0, -5.0))),
                 variant(self.base, "in_bench", route((0, 0, 5.5), (0, 0, -2.4))),
                 variant(self.base, "bad_height", route((0, 0, 5.5), (0, -1, 0)))]
        results = plan_variants(self.data["materials"], rooms)
        self.assertEqual(results["t_on_dais"]["plan"]["route"][1], {"x": -5.5, "y": 1.0, "z": -5.0})
        self.assertIn("ROOM_STAND_FLOATING: route point 1", results["t_floating"]["error"])
        self.assertIn("ROOM_STAND_BLOCKED: route point 1", results["t_in_bench"]["error"])
        self.assertIn("ROOM_ROUTE_INVALID", results["t_bad_height"]["error"])

    def test_views_are_checked_and_scale_with_the_plan(self):
        def view(name, x, y, z): return {"name": name, "position": {"x": x, "y": y, "z": z}, "target": {"x": 0, "y": 1.0, "z": -2.4}}
        def views(*entries): return lambda r: r.update(views=list(entries))
        good = variant(self.base, "good", views(view("interior", -8, 0, 5.2), view("street", 0, 0, 9.5)))
        rooms = [good, variant(self.base, "air", views(view("interior", -8, 1.5, 5.2))),
                 variant(self.base, "twin", views(view("interior", -8, 0, 5.2), view("interior", 0, 0, 5.2))),
                 variant(self.base, "bench", views(view("interior", 0, 0, -2.4))), variant(self.base, "none", lambda r: r.pop("views", None))]
        results = plan_variants(self.data["materials"], rooms)
        self.assertEqual([v["name"] for v in results["t_good"]["plan"]["views"]], ["interior", "street"])
        self.assertEqual(results["t_none"]["plan"]["views"], [])
        self.assertIn("ROOM_STAND_FLOATING: view interior", results["t_air"]["error"])
        self.assertIn("ROOM_VIEW_INVALID: interior", results["t_twin"]["error"])
        self.assertIn("ROOM_STAND_BLOCKED: view interior", results["t_bench"]["error"])
        scaled = plan_variants(self.data["materials"], [good], width=32, depth=20)["t_good"]["plan"]["views"][0]
        self.assertAlmostEqual(scaled["position"]["x"], -8 * 32 / 25, places=4)
        self.assertAlmostEqual(scaled["target"]["z"], -2.4 * 20 / 15, places=4)
        self.assertAlmostEqual(scaled["target"]["y"], 1.0, places=4)       # heights are never scaled

    def test_quarter_turned_parts_stretch_along_the_plan_axes(self):
        turned = {"id": "turned", "kind": "box", "material": "concrete", "position": {"x": -9, "y": 0.5, "z": -4.0},
                  "size": {"x": 4, "y": 1, "z": 1.5}, "yaw": 90, "collider": True}
        room = variant(self.base, "turned", lambda r: r["parts"].append(turned))
        plan = plan_variants(self.data["materials"], [room], width=32, depth=20)["t_turned"]["plan"]
        part = next(p for p in plan["parts"] if p["id"] == "turned")
        sx, sz = 32 / 25, 20 / 15
        # Turned a quarter, the part's own x runs along the plan's z: it stretches by the depth ratio, and its z by the width ratio.
        self.assertAlmostEqual(part["size"]["x"], 4 * sz, places=4)
        self.assertAlmostEqual(part["size"]["z"], 1.5 * sx, places=4)
        self.assertAlmostEqual(part["position"]["x"], -9 * sx, places=4)


if __name__ == "__main__": unittest.main()
