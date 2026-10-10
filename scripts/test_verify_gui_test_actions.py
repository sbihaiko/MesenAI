#!/usr/bin/env python3
"""Fixture tests for scripts/checks/verify_gui_test_actions.py: it must go red on a
batch that opens with a pad press and stay green on the committed pilot.

GitHub issue #1232: the `home` batch opened with `pad.press Up` on an application
that had just launched, so the press reached a home that had not placed its ring
yet (measured on the real app: the ring lands 8-16 ticks after launch) and the
step's check read `None` four ticks later. A batch opens at a fresh launch and
cannot press a ring it has not seen; the actor before the first press has to
observe (`launch.open-rom-focused` already does, the `home` batch did not).
"""
import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CHECK = ROOT / "scripts" / "checks" / "verify_gui_test_actions.py"
PILOT = Path("docs") / "validation" / "process" / "play-pad-only.gui-test.json"


def run_check(repo):
    p = subprocess.run([sys.executable, str(CHECK), "--repo", str(repo)], capture_output=True, text=True)
    return p.returncode, p.stdout + p.stderr


class VerifyGuiTestActions(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.tmp, True)
        (self.tmp / PILOT).parent.mkdir(parents=True)
        shutil.copy(ROOT / PILOT, self.tmp / PILOT)

    def doc(self):
        return json.loads((self.tmp / PILOT).read_text(encoding="utf-8"))

    def write(self, doc):
        (self.tmp / PILOT).write_text(json.dumps(doc, indent=2) + "\n", encoding="utf-8")

    def committed(self):
        return json.loads((ROOT / PILOT).read_text(encoding="utf-8"))

    def batch(self, doc, batch_id):
        return next(b for b in doc["batches"] if b["id"] == batch_id)

    def first_automated(self, batch):
        for step in batch.get("setup", []) + batch.get("steps", []):
            if step.get("mode") == "automated":
                return step
        return None

    def test_committed_tree_is_green(self):
        code, out = run_check(ROOT)
        self.assertEqual(code, 0, out)

    def test_fixture_copy_is_green(self):
        code, out = run_check(self.tmp)
        self.assertEqual(code, 0, out)

    def test_batch_opening_with_a_pad_press_is_red(self):
        doc = self.doc()
        self.batch(doc, "home")["setup"] = []
        self.write(doc)
        code, out = run_check(self.tmp)
        self.assertEqual(code, 1, out)
        self.assertIn("home.focus-holds-up", out)
        self.assertIn("presses the pad", out)

    def test_batch_opening_with_a_chord_is_red(self):
        # #1242: the pilot's `pause` steps are all `manual` now (the chord they need is not advertised), so
        # the chord is put on the step that opens a batch once that batch's setup is taken away - the same
        # mutation, on a step that really does press.
        doc = self.doc()
        self.batch(doc, "home")["setup"] = []
        self.first_automated(self.batch(doc, "home"))["action"] = {"name": "pad.chord", "args": {"ticks": 2}}
        self.write(doc)
        code, out = run_check(self.tmp)
        self.assertEqual(code, 1, out)
        self.assertIn("home.focus-holds-up", out)
        self.assertIn("pad.chord", out)

    def test_committed_home_batch_observes_the_ring_before_its_first_press(self):
        home = self.batch(self.committed(), "home")
        first = self.first_automated(home)
        self.assertNotIn("action", first)
        self.assertEqual(first["wait"]["check"], "ui.focused == play.home.open-rom")
        press = next(s for s in home["steps"] if (s.get("action") or {}).get("name") == "pad.press")
        self.assertEqual(press["id"], "home.focus-holds-up")


if __name__ == "__main__":
    unittest.main()
