#!/usr/bin/env python3
"""Fixture tests for scripts/checks/verify_gui_test_render.py: it must go red on a
stale view, a missing or edited vendored renderer, and an unknown format, never skip."""
import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CHECK = ROOT / "scripts" / "checks" / "verify_gui_test_render.py"
VENDORED = Path("scripts/vendor/agent_squad/gui_test_render.py")
PROCESS = Path("docs/validation/process")
PILOT = "play-pad-only.gui-test"


def run_check(repo):
    p = subprocess.run([sys.executable, str(CHECK), "--repo", str(repo)], capture_output=True, text=True)
    return p.returncode, p.stdout + p.stderr


class VerifyGuiTestRender(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.tmp, True)
        (self.tmp / VENDORED).parent.mkdir(parents=True)
        shutil.copy(ROOT / VENDORED, self.tmp / VENDORED)
        (self.tmp / PROCESS).mkdir(parents=True)
        for ext in ("json", "md"):
            shutil.copy(ROOT / PROCESS / f"{PILOT}.{ext}", self.tmp / PROCESS / f"{PILOT}.{ext}")

    def test_committed_tree_is_green(self):
        code, out = run_check(ROOT)
        self.assertEqual(code, 0, out)

    def test_fixture_copy_is_green(self):
        code, out = run_check(self.tmp)
        self.assertEqual(code, 0, out)

    def test_stale_view_is_red(self):
        md = self.tmp / PROCESS / f"{PILOT}.md"
        md.write_text(md.read_text(encoding="utf-8").replace("Open a ROM", "Open a game", 1), encoding="utf-8")
        code, out = run_check(self.tmp)
        self.assertEqual(code, 1, out)
        self.assertIn("differs from its render", out)

    def test_missing_view_is_red(self):
        (self.tmp / PROCESS / f"{PILOT}.md").unlink()
        code, out = run_check(self.tmp)
        self.assertEqual(code, 1, out)
        self.assertIn("no committed view", out)

    def test_missing_vendored_renderer_is_red(self):
        (self.tmp / VENDORED).unlink()
        code, out = run_check(self.tmp)
        self.assertEqual(code, 1, out)
        self.assertIn("vendored renderer is missing", out)

    def test_edited_vendored_renderer_is_red(self):
        v = self.tmp / VENDORED
        v.chmod(0o644)
        v.write_text(v.read_text(encoding="utf-8") + "\n# local edit\n", encoding="utf-8")
        code, out = run_check(self.tmp)
        self.assertEqual(code, 1, out)
        self.assertIn("sha256", out)

    def test_unknown_format_is_red(self):
        j = self.tmp / PROCESS / f"{PILOT}.json"
        d = json.loads(j.read_text(encoding="utf-8"))
        d["format"] = "gui-test/9"
        j.write_text(json.dumps(d), encoding="utf-8")
        code, out = run_check(self.tmp)
        self.assertEqual(code, 1, out)
        self.assertIn("unknown format", out)

    def test_malformed_script_is_red_not_a_traceback(self):
        """One error line per drift, never a traceback.

        A script the renderer cannot walk at all (`batches` not a list) is as much a
        failure as a stale view: the check reports it and exits 1, it does not raise
        out of its own reader.
        """
        j = self.tmp / PROCESS / f"{PILOT}.json"
        d = json.loads(j.read_text(encoding="utf-8"))
        d["batches"] = 3
        j.write_text(json.dumps(d), encoding="utf-8")
        code, out = run_check(self.tmp)
        self.assertEqual(code, 1, out)
        self.assertNotIn("Traceback", out)
        self.assertIn("unrenderable", out)

    def test_malformed_header_is_red_not_a_traceback(self):
        """The header is parsed, not trusted.

        A header line with no `key: value` must be reported as a drift - one error
        line - and never raise out of the reader (a traceback is not a verdict).
        """
        v = self.tmp / VENDORED
        v.chmod(0o644)
        text = v.read_text(encoding="utf-8")
        v.write_text(text.replace("# sha256: ", "# sha256 ", 1), encoding="utf-8")
        code, out = run_check(self.tmp)
        self.assertEqual(code, 1, out)
        self.assertNotIn("Traceback", out)
        self.assertIn("sha256", out)

    def test_no_script_at_all_is_red(self):
        (self.tmp / PROCESS / f"{PILOT}.json").unlink()
        code, out = run_check(self.tmp)
        self.assertEqual(code, 1, out)
        self.assertIn("no *.gui-test.json", out)


class PilotScriptRules(unittest.TestCase):
    """Conversion rules the pilot script must keep (ADR-0268 D6, ADR-0271 D3, ADR-0272 item 7).

    #1242 narrowed the pilot to what `mesen-gui` advertises: a step the adapter cannot be asked to run is
    `manual` and carries no `action`/`wait`/`check` (ADR-0272 item 7, and the format validator refuses an
    unadvertised action wherever it appears), so a rule that reads a press or a check reads it off the steps
    that still run, and asserts over the step itself what the narrowing has to keep: it is still there, and it
    says which capability it is missing. `verify_gui_test_scenario_coverage.py` keeps the case, this keeps the
    rule.
    """

    @classmethod
    def setUpClass(cls):
        cls.doc = json.loads((ROOT / PROCESS / f"{PILOT}.json").read_text())
        cls.batches = {b["id"]: b for b in cls.doc["batches"]}
        cls.steps = {s["id"]: s for b in cls.doc["batches"] for s in b["steps"]}

    def test_window_mode_is_a_variant_not_an_action(self):
        self.assertNotIn("window.mode", self.doc["requires"]["actions"])
        for b in self.doc["batches"]:
            for s in b.get("setup", []) + b["steps"] + b.get("teardown", []):
                self.assertNotEqual(s.get("action", {}).get("name"), "window.mode", s["id"])
        self.assertIn("window.mode", self.doc["variants"])

    def test_launch_focus_is_checked_without_a_press(self):
        step = self.steps["launch.open-rom-focused"]
        self.assertNotIn("action", step)
        self.assertEqual(step["check"]["args"]["is"], "play.home.open-rom")

    def test_containment_steps_run_after_a_game(self):
        ids = [s["id"] for s in self.batches["home-after-game"]["steps"]]
        self.assertTrue(any(i.startswith("after-game.contained-up") for i in ids))
        self.assertTrue(any(i.startswith("after-game.contained-sideways") for i in ids))
        self.assertFalse([i for i in self.batches["home"]["steps"] if i["id"].startswith("home.contained")])
        for sid in ("after-game.contained-sideways-left", "after-game.contained-sideways-right"):
            self.assertIn(sid, self.steps, f"the case must survive the narrowing: {sid}")
        sideways = [s for s in self.batches["home-after-game"]["steps"]
                    if s["id"].startswith("after-game.contained-sideways")]
        for s in sideways:
            if "action" in s:
                self.assertIn(s["action"]["args"]["button"], {"Left", "Right"}, s["id"])
            else:
                self.assertEqual(s["mode"], "manual", s["id"])

    def test_navigation_is_setup_and_the_goal_it_needs_is_named(self):
        """ADR-0271 section 3: navigation is setup, never the behavior under test.

        `mesen-gui` advertises no `nav.goal`, so the batch whose entry was a goal cannot run its own setup
        and is `manual` here - the setup still says which action it would have run."""
        for s in self.steps.values():
            self.assertNotEqual((s.get("action") or {}).get("name"), "nav.goal", s["id"])
        setup = self.batches["home-after-game"]["setup"][0]
        self.assertEqual(setup["mode"], "manual")
        self.assertIn("needs action nav.goal", setup["expect"])
        self.assertNotEqual(setup["precondition"], "game == loaded")

    def test_recent_is_reached_through_favorites(self):
        ids = [s["id"] for s in self.batches["home-after-game"]["steps"]]
        self.assertLess(ids.index("after-game.order-favorites"), ids.index("after-game.order-recent"))
        recent = self.steps["after-game.order-recent"]
        if "check" in recent:
            self.assertEqual(recent["check"]["name"], "ui.focused")
            self.assertEqual(recent["check"]["args"]["is"], "play.home.recent")
        else:
            # `play.home.recent` is not an AutomationId the application declares, so an automated check on it
            # would be `failed (unknown id)`, never a measurement: the step is manual and names the id.
            self.assertEqual(recent["mode"], "manual")
            self.assertIn("play.home.recent", recent["expect"])


if __name__ == "__main__":
    unittest.main()
