"""tools/player_log.py: what a run of the player leaves behind, read from invented logs and result.json files. Hermetic: nothing is run."""
import json
from pathlib import Path
import tempfile
import unittest

import player_log

ROOT = Path(__file__).resolve().parents[1]
NOISE = "NullReferenceException: Object reference not set\n  at UnityEngine.Rendering.Universal.UniversalRenderPipeline.Render ()\n"
LOG = ("Initialize engine version: 6000.6.7f1\n"
       "[PryingEyes] survivaltest ok la candela si accende (3 candele)\n"
       "[PryingEyes] survivaltest FALLITO la leva costa 1000 (punti 1500)\n"
       "[PryingEyes] survivaltest ok il nome (con parentesi) ha un dettaglio (7)\n"
       "[PryingEyes] survivaltest ok senza dettaglio\n"
       "[PryingEyes] survivaltest ok due (gruppi) (di parentesi (annidate))\n"
       "[PryingEyes] chartest ok un altro test (1)\n"
       "[PryingEyes] survivaltest RISULTATO FALLITA: 3/5\n")


class PlayerLog(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory(); self.addCleanup(self.folder.cleanup)
        self.root = Path(self.folder.name)

    def result(self, data, name="result.json"):
        path = self.root / name
        path.write_text(data if isinstance(data, str) else json.dumps(data), encoding="utf-8")
        return path

    # ---- the exceptions of a log

    def test_an_exception_counts_unless_the_renderers_noise_is_in_it_or_in_the_next_three_lines(self):
        self.assertEqual(player_log.exceptions("ArgumentException: no\nfine\n"), ["ArgumentException: no"])
        self.assertEqual(player_log.exceptions(NOISE), [])
        self.assertEqual(player_log.exceptions("InvalidOperationException: x\nat A\nat B\nat UnityEngine.Rendering.RenderGraphModule.X ()\n"), [])      # the 4th line
        self.assertEqual(len(player_log.exceptions("InvalidOperationException: x\nat A\nat B\nat C\nat UnityEngine.Rendering.RenderGraphModule.X ()\n")), 1)   # the 5th is too far
        self.assertEqual(player_log.exceptions("not an Exception: line\n  ArgumentException: indented\n"), [])                                       # starts the line, no spaces before
        self.assertEqual(player_log.exceptions(""), [])

    def test_check_navigation_uses_this_filter_and_defines_none_of_its_own(self):
        script = (ROOT / "tools/check_navigation.py").read_text(encoding="utf-8-sig")
        self.assertIn("from player_log import exceptions as player_exceptions", script)
        self.assertNotIn("NOISE", script); self.assertNotIn("import re", script)

    # ---- the lines

    def test_check_lines_give_the_name_and_the_detail_when_there_is_one_to_find(self):
        found = player_log.check_lines(LOG, "survivaltest")
        self.assertEqual([(c["name"], c["ok"], c["detail"]) for c in found], [
            ("la candela si accende", True, "3 candele"), ("la leva costa 1000", False, "punti 1500"),
            ("il nome (con parentesi) ha un dettaglio", True, "7"), ("senza dettaglio", True, ""),
            ("due (gruppi) (di parentesi (annidate))", True, "")])                 # parentheses inside the detail: the whole text is the name
        self.assertEqual([c["suite"] for c in player_log.check_lines(LOG)].count("chartest"), 1)           # without a suite: every one
        self.assertEqual(player_log.check_lines("[PryingEyes] survivaltest RISULTATO SUPERATA: 1/1\nrandom ok line (1)\n"), [])

    def test_the_result_line_is_the_last_one_of_the_suite(self):
        self.assertEqual(player_log.result_lines(LOG, "survivaltest"), [{"suite": "survivaltest", "ok": False, "passed": 3, "total": 5}])
        self.assertEqual(player_log.result_lines(LOG, "chartest"), [])
        two = "[PryingEyes] a RISULTATO FALLITA: 1/2\r\n[PryingEyes] a RISULTATO SUPERATA: 2/2\r\n"
        self.assertEqual([line["ok"] for line in player_log.result_lines(two, "a")], [False, True])
        self.assertEqual(player_log.result_lines("[PryingEyes] a RISULTATO SUPERATA 2/2\n[PryingEyes] a RISULTATO SUPERATA: due\n"), [])    # not the format

    # ---- result.json

    def test_a_result_json_is_read_as_the_checks_it_holds(self):
        good = {"schemaVersion": 1, "suite": "traptest", "ok": True, "exceptions": 0, "seconds": 4.1, "version": "0.9.4", "args": ["-x"],
                "checks": [{"name": "a", "ok": True, "detail": "1"}, {"name": "b", "ok": False}]}
        self.assertEqual(player_log.read_result(self.result(good), "traptest"), {"ok": True, "exceptions": 0, "checks": [
            {"name": "a", "ok": True, "detail": "1"}, {"name": "b", "ok": False, "detail": ""}]})
        self.assertIsNone(player_log.read_result(self.root / "nothing.json", "traptest"))

    def test_a_result_json_that_cannot_be_trusted_says_why(self):
        good = {"schemaVersion": 1, "suite": "traptest", "ok": True, "checks": [{"name": "a", "ok": True}]}
        for broken, why in (("{not json", "not JSON"), ("[]", "not an object"), ({**good, "schemaVersion": 2}, "schemaVersion 2"),
                            ({**good, "suite": "other"}, "'other'"), ({**good, "ok": "yes"}, "lacks ok"), ({**good, "checks": "a"}, "lacks ok"),
                            ({**good, "checks": [{"ok": True}]}, "lacks ok")):
            with self.assertRaisesRegex(ValueError, why): player_log.read_result(self.result(broken), "traptest")

    # ---- the verdict

    def test_the_verdict_comes_from_result_json_when_it_exists_and_from_the_log_otherwise(self):
        good = {"schemaVersion": 1, "suite": "survivaltest", "ok": True, "checks": [{"name": "a", "ok": True}, {"name": "b", "ok": True}]}
        harness = player_log.verdict("survivaltest", LOG, self.result(good))                       # the file wins over a log that says failed
        self.assertEqual((harness["source"], harness["ok"], harness["total"], harness["passed"]), ("result.json", True, 2, 2))
        self.assertEqual(harness["exceptions"], [])
        lines = player_log.verdict("survivaltest", LOG + NOISE, self.root / "nothing.json")
        self.assertEqual((lines["source"], lines["ok"], lines["total"], lines["passed"], len(lines["checks"])), ("log-line", False, 5, 3, 5))
        self.assertEqual(lines["exceptions"], [])                                                  # the noise is not the test's
        self.assertEqual(player_log.verdict("survivaltest", LOG)["source"], "log-line")            # no file named at all
        failed = player_log.verdict("survivaltest", LOG, self.result({**good, "ok": False}))       # the file says failed
        self.assertFalse(failed["ok"])
        lying = player_log.verdict("survivaltest", LOG, self.result({**good, "checks": [{"name": "a", "ok": True}, {"name": "b", "ok": False}]}))
        self.assertFalse(lying["ok"])                                                                # ok: true over a failed check does not pass

    def test_a_log_without_a_result_is_no_verdict_and_a_failed_line_over_a_passing_result_is_not_a_pass(self):
        nothing = player_log.verdict("survivaltest", "Initialize engine\n[PryingEyes] survivaltest ok a (1)\n")
        self.assertEqual((nothing["source"], nothing["ok"], nothing["total"]), ("none", False, 0))
        self.assertEqual(nothing["checks"], [])
        liar = "[PryingEyes] s FALLITO x (1)\n[PryingEyes] s RISULTATO SUPERATA: 1/1\n"
        self.assertFalse(player_log.verdict("s", liar)["ok"])
        silent = "[PryingEyes] s FALLITO x\n[PryingEyes] s RISULTATO FALLITA: 17/18\n"                  # a test that prints only its failures: the total is the line's
        said = player_log.verdict("s", silent)
        self.assertEqual((said["total"], said["passed"], len(said["checks"])), (18, 17, 1))
        passed = player_log.verdict("s", "[PryingEyes] s RISULTATO SUPERATA: 18/18\n")
        self.assertEqual((passed["ok"], passed["total"], passed["checks"]), (True, 18, []))

    def test_the_verdict_of_a_run_made_of_several_processes_adds_them_up(self):
        host = player_log.verdict(None, "[PryingEyes] cooptest ok a (1)\n[PryingEyes] cooptest RISULTATO SUPERATA: 1/1\n")
        guest = player_log.verdict(None, "[PryingEyes] cooptest ok b (1)\n[PryingEyes] cooptest ok c (1)\n[PryingEyes] cooptest RISULTATO SUPERATA: 2/2\n")
        killed = player_log.verdict(None, "[PryingEyes] killed on purpose\nArgumentException: boom\n")
        self.assertIs(player_log.combine([host]), host)                                              # one process alone is itself
        together = player_log.combine([host, guest, killed])
        self.assertEqual((together["source"], together["ok"], together["total"], together["passed"]), ("log-line", True, 3, 3))
        self.assertEqual([c["name"] for c in together["checks"]], ["a", "b", "c"]); self.assertEqual(together["exceptions"], ["ArgumentException: boom"])
        bad = player_log.verdict(None, "[PryingEyes] cooptest FALLITO x (1)\n[PryingEyes] cooptest RISULTATO FALLITA: 0/1\n")
        self.assertFalse(player_log.combine([host, bad])["ok"])
        self.assertEqual(player_log.combine([killed, killed])["source"], "none")                      # nobody said anything
        self.assertEqual(player_log.combine([])["source"], "none")

    def test_a_log_that_is_not_there_reads_as_empty_and_one_that_is_reads_as_utf8(self):
        self.assertEqual(player_log.read_text(self.root / "absent.log"), "")
        path = self.root / "player.log"; path.write_bytes("﻿[PryingEyes] a ok perché (1)\n".encode("utf-8"))
        self.assertEqual(player_log.check_lines(player_log.read_text(path))[0]["name"], "perché")


if __name__ == "__main__":
    unittest.main()
