import contextlib
import io
import tempfile
import unittest
from pathlib import Path

import room_variety as rv

CAMERA = ("[PryingEyes] CENSUS tema=Albergo tipo=Normale stanza=Camera area=45 oggetti=3 copertura=0.080 quote=1 "
          "w=7.2 d=6.3 arredi=arr_letto:0.5:2.1:0|arr_comodino:0.2:1.0:0|arr_scrivania:5.0:3.0:270")
OLD_CAMERA = "[PryingEyes] CENSUS tema=Albergo tipo=Normale stanza=Camera area=45 oggetti=3 copertura=0.080 quote=1"


def camera(x: float) -> str:
    """A camera whose bed sits x metres from the corner: same pieces, different place."""
    return (f"[PryingEyes] CENSUS tema=Albergo tipo=Normale stanza=Camera area=45 oggetti=2 copertura=0.050 quote=1 "
            f"w=7.2 d=6.3 arredi=arr_letto:{x}:2.1:0|arr_comodino:0.2:1.0:0")


class ParseTest(unittest.TestCase):
    def test_reads_kind_coverage_and_props(self):
        room = rv.parse_line(CAMERA)
        self.assertEqual(room.kind, "Camera")
        self.assertAlmostEqual(room.coverage, 0.08)
        self.assertEqual([p.name for p in room.props], ["arr_letto", "arr_comodino", "arr_scrivania"])
        self.assertEqual(room.props[2].facing, 270.0)

    def test_other_lines_are_ignored(self):
        self.assertIsNone(rv.parse_line("[PryingEyes] COVER tema=Albergo stanza=Candela"))
        self.assertIsNone(rv.parse_line(""))

    def test_line_without_layout_counts_for_coverage_only(self):
        room = rv.parse_line(OLD_CAMERA)
        self.assertIsNone(room.props)
        self.assertAlmostEqual(room.coverage, 0.08)

    def test_malformed_prop_raises(self):
        with self.assertRaises(ValueError):
            rv.parse_line(CAMERA.replace("arr_comodino:0.2:1.0:0", "arr_comodino:0.2"))


class LayoutTest(unittest.TestCase):
    def test_same_content_is_one_composition(self):
        kinds = rv.summarize([rv.parse_line(camera(2.0)), rv.parse_line(camera(3.1))])
        self.assertEqual(len(kinds["Camera"].compositions), 1)
        self.assertEqual(kinds["Camera"].top_composition_share, 1.0)
        self.assertEqual(len(kinds["Camera"].layouts), 2)

    def test_layout_is_quantized_to_the_metre_and_the_quarter_turn(self):
        near = rv.layout([rv.Prop("a", 0.4, 2.0, 88.0)])
        same = rv.layout([rv.Prop("a", 0.3, 2.2, 92.0)])
        self.assertEqual(near, same)
        self.assertNotEqual(near, rv.layout([rv.Prop("a", 1.6, 2.0, 88.0)]))
        self.assertEqual(rv.layout([rv.Prop("a", 0.0, 0.0, 0.0)]), rv.layout([rv.Prop("a", 0.0, 0.0, 360.0)]))

    def test_rooms_without_furniture_count_for_coverage_only(self):
        kinds = rv.summarize([rv.Room("Ascensore", 0.0, []), rv.Room("Ascensore", 0.0, [])])
        self.assertEqual(kinds["Ascensore"].rooms, 2)
        self.assertIsNone(kinds["Ascensore"].top_composition_share)
        self.assertEqual(rv.failing_kinds(kinds, max_top=0.5, min_rooms=1), [])

    def test_aligned_share_counts_facings_within_three_degrees_of_an_axis(self):
        room = rv.Room("Camera", 0.0, [rv.Prop("a", 0, 0, 0.0), rv.Prop("a", 0, 0, 2.5),
                                       rv.Prop("a", 0, 0, 45.0), rv.Prop("a", 0, 0, 359.0)])
        self.assertAlmostEqual(rv.summarize([room])["Camera"].aligned_share, 0.75)


class GateTest(unittest.TestCase):
    def test_only_kinds_with_enough_rooms_can_fail(self):
        rooms = [rv.parse_line(camera(2.0)) for _ in range(12)]
        rooms += [rv.parse_line(CAMERA.replace("stanza=Camera", "stanza=Suite")) for _ in range(3)]
        failing = rv.failing_kinds(rv.summarize(rooms), max_top=0.5, min_rooms=10)
        self.assertEqual(failing, [("Camera", 1.0)])

    def test_main_exit_code_follows_the_gate(self):
        with tempfile.TemporaryDirectory() as tmp:
            log = Path(tmp) / "census.log"
            log.write_text("\n".join(camera(2.0) for _ in range(10)) + "\n", encoding="utf-8")
            with contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(rv.main([str(log)]), 0)
                self.assertEqual(rv.main([str(log), "--max-top", "0.5"]), 1)
                self.assertEqual(rv.main([str(log), "--max-top", "1.0"]), 0)

    def test_main_stops_on_a_malformed_line(self):
        with tempfile.TemporaryDirectory() as tmp:
            log = Path(tmp) / "census.log"
            log.write_text(CAMERA.replace("arr_comodino:0.2:1.0:0", "arr_comodino:0.2") + "\n", encoding="utf-8")
            with contextlib.redirect_stderr(io.StringIO()):
                self.assertEqual(rv.main([str(log)]), 2)


if __name__ == "__main__":
    unittest.main()
