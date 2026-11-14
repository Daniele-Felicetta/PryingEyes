"""The trailer edit as data: a consistent timeline (cuts on the beat, captions on real segments), take folders that resolve in order,
and recorder-accurate shot times. Hermetic: no clips, no player, no ffmpeg."""
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parent / "trailer"))
import make_trailer as MT

FPS = MT.FPS


def whole(x):
    return min(x % 1.0, 1.0 - x % 1.0) < 1e-6


class Timeline(unittest.TestCase):
    def test_clips_are_unique_and_the_title_closes_the_film(self):
        names = [s.clip for s in MT.SEGMENTS]
        self.assertEqual(len(names), len(set(names)))
        self.assertEqual(MT.SEGMENTS[-1].act, "t")
        self.assertEqual([s.act for s in MT.SEGMENTS if s.act == "t"], ["t"])

    def test_acts_come_in_order_and_dissolves_stay_in_the_opening(self):
        order = "".join(s.act for s in MT.SEGMENTS)
        self.assertRegex(order, r"^a+b+t$")
        for s in MT.SEGMENTS:
            if s.act != "a":
                self.assertEqual(s.xf, 0.0, s.clip)
        self.assertEqual(MT.SEGMENTS[0].xf, 0.0)

    def test_cuts_after_the_opening_fall_on_the_beat(self):
        cuts = [s for s in MT.SEGMENTS if s.act == "b"]
        pos, total = MT.layout(MT.SEGMENTS)
        first = pos[cuts[0].clip]
        for s in cuts:
            self.assertTrue(whole(s.dur / MT.BEAT), f"{s.clip} is not a whole number of beats")
            self.assertTrue(whole((pos[s.clip] - first) / MT.BEAT), f"{s.clip} does not start on the beat")

    def test_every_cut_and_dissolve_lands_on_a_frame(self):
        pos, total = MT.layout(MT.SEGMENTS)
        for s in MT.SEGMENTS:
            for t in (pos[s.clip], s.dur, s.xf, s.ss):
                self.assertAlmostEqual(t * FPS, round(t * FPS), places=6, msg=f"{s.clip}: {t}")
        self.assertAlmostEqual(total * FPS, round(total * FPS), places=6)

    def test_dissolves_overlap_the_previous_segment_and_nothing_else(self):
        pos, total = MT.layout(MT.SEGMENTS)
        for prev, s in zip(MT.SEGMENTS, MT.SEGMENTS[1:]):
            self.assertAlmostEqual(pos[s.clip], pos[prev.clip] + prev.dur - s.xf, places=9)
            self.assertLess(s.xf, prev.dur)
            self.assertLess(s.xf, s.dur)

    def test_captions_sit_on_real_segments_inside_the_film(self):
        pos, total = MT.layout(MT.SEGMENTS)
        names = {s.clip for s in MT.SEGMENTS}
        for kind, text, sub, clip, off, length in MT.CAPTIONS:
            self.assertIn(clip, names)
            self.assertIn(kind, ("center", "label", "weapon"))
            self.assertGreaterEqual(off, 0.0)
            self.assertLessEqual(pos[clip] + off + length, total, text)
            self.assertGreater(length, 1.0, f"{text}: too short to read")

    def test_weapon_captions_name_the_clip_they_belong_to(self):
        for kind, text, sub, clip, off, length in MT.CAPTIONS:
            if kind == "weapon":
                self.assertEqual(text, clip)


class Takes(unittest.TestCase):
    def folder(self, root, name, clips, dur=6.0):
        f = root / name
        f.mkdir()
        for c in clips:
            (f / f"{c}.mp4").write_bytes(b"x")
        Path(str(f) + ".json").write_text(json.dumps({"shots": [{"name": c, "dur": dur, "tag": name} for c in clips]}), encoding="utf-8")
        return f

    def test_the_first_folder_that_has_a_clip_wins(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            a = self.folder(root, "reshoot", ["one"])
            b = self.folder(root, "full", ["one", "two"])
            takes = MT.Takes([a, b])
            self.assertEqual(takes.find("one")[1]["tag"], "reshoot")
            self.assertEqual(takes.find("two")[1]["tag"], "full")
            with self.assertRaises(FileNotFoundError):
                takes.find("three")

    def test_a_segment_longer_than_its_clip_is_refused(self):
        with tempfile.TemporaryDirectory() as tmp:
            takes = MT.Takes([self.folder(Path(tmp), "t", ["one"], dur=3.0)])
            takes.shots([MT.Seg("one", 0.5, 2.5)])
            with self.assertRaises(ValueError):
                takes.shots([MT.Seg("one", 0.5, 2.6)])


@unittest.skipUnless(all(importlib.util.find_spec(m) for m in ("numpy", "scipy", "soundfile", "pyloudnorm")), "score dependencies not installed")
class ShotTimes(unittest.TestCase):
    def test_burst_shots_follow_the_recorders_rule(self):
        import trailer_score as SC
        burst = {"t": 0.8, "kind": "burst", "v": 2.0, "i": 14}
        times = SC.shot_times(burst, FPS)
        self.assertEqual(len(times), 28)
        self.assertEqual(times[0], 0.8)
        for k, t in enumerate(times):
            self.assertAlmostEqual(t * FPS, round(t * FPS), places=6)
            self.assertGreaterEqual(t, 0.8 + k / 14 - 1e-9)
            self.assertLess(t, 0.8 + k / 14 + 1.0 / FPS + 1e-9)
        self.assertEqual(SC.shot_times({"t": 1.3, "kind": "fire", "v": 0.0, "i": 0}, FPS), [1.3])


if __name__ == "__main__":
    unittest.main()
