#!/usr/bin/env python3
"""Fixture tests for scripts/checks/verify_gui_test_adapter_surface.py: it must go red
on every way a committed GUI test script can drift from the surface it claims, and
stay green on the committed pilot.

Issue #1242 criterion 1: the narrowed `play-pad-only` script was produced outside this
repository, so nothing here re-derived it. These cases are the check that keeps it
honest from this side - an unadvertised action or check, a control id the application
does not declare, a fixture profile the adapter does not support, and a `manual` step
claiming a capability the adapter or the application really has (the claim that a
narrowed script goes stale through, once an adapter change lands).
"""
import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CHECK = ROOT / "scripts" / "checks" / "verify_gui_test_adapter_surface.py"
ADAPTER = Path("scripts") / "gui_test" / "mesen_gui_adapter.py"
PILOT = Path("docs") / "validation" / "process" / "play-pad-only.gui-test.json"

# The application side the check reads control ids from: the ids the pilot's automated
# steps use, and nothing else - so an id only a `manual` claim names stays absent.
UI_VIEW = """<UserControl xmlns="https://github.com/avaloniaui">
  <StackPanel>
    <Button AutomationId="play.home" />
    <Button AutomationId="play.home.open-rom" />
    <Button AutomationId="play.library" />
  </StackPanel>
</UserControl>
"""


def run_check(repo):
    p = subprocess.run([sys.executable, str(CHECK), "--repo", str(repo)], capture_output=True, text=True)
    return p.returncode, p.stdout + p.stderr


class VerifyGuiTestAdapterSurface(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.tmp, True)
        (self.tmp / ADAPTER).parent.mkdir(parents=True)
        shutil.copy(ROOT / ADAPTER, self.tmp / ADAPTER)
        (self.tmp / PILOT).parent.mkdir(parents=True)
        shutil.copy(ROOT / PILOT, self.tmp / PILOT)
        ui = self.tmp / "UI" / "Views"
        ui.mkdir(parents=True)
        (ui / "PilotViews.axaml").write_text(UI_VIEW, encoding="utf-8")

    def pilot(self):
        return json.loads((self.tmp / PILOT).read_text(encoding="utf-8"))

    def write(self, doc):
        (self.tmp / PILOT).write_text(json.dumps(doc, indent=2) + "\n", encoding="utf-8")

    def batch(self, doc, bid):
        return [b for b in doc["batches"] if b["id"] == bid][0]

    def test_committed_tree_is_green(self):
        code, out = run_check(ROOT)
        self.assertEqual(code, 0, out)

    def test_fixture_copy_is_green(self):
        code, out = run_check(self.tmp)
        self.assertEqual(code, 0, out)

    def test_unadvertised_action_on_an_automated_step_is_red(self):
        doc = self.pilot()
        step = [s for s in self.batch(doc, "home")["steps"] if s["id"] == "home.focus-holds-up"][0]
        step["action"]["name"] = "pad.chord"
        self.write(doc)
        code, out = run_check(self.tmp)
        self.assertEqual(code, 1, out)
        self.assertIn("adapter does not advertise action pad.chord", out)

    def test_unknown_control_id_on_an_automated_step_is_red(self):
        doc = self.pilot()
        step = [s for s in self.batch(doc, "home")["steps"] if s["id"] == "home.focus-holds-up"][0]
        step["check"]["args"]["is"] = "play.home.gone"
        self.write(doc)
        code, out = run_check(self.tmp)
        self.assertEqual(code, 1, out)
        self.assertIn("play.home.gone", out)
        self.assertIn("declares as an AutomationId", out)

    def test_unsupported_settings_profile_is_red(self):
        doc = self.pilot()
        doc["fixtures"]["settings"]["profile"] = "warm"
        self.write(doc)
        code, out = run_check(self.tmp)
        self.assertEqual(code, 1, out)
        self.assertIn("settings profile 'warm'", out)

    def test_manual_step_claiming_an_advertised_action_is_red(self):
        doc = self.pilot()
        step = [s for s in self.batch(doc, "home")["steps"] if s["mode"] == "manual"][0]
        step["expect"] = "Something is judged by eye. (manual: needs action pad.press, which mesen-gui does not advertise)"
        self.write(doc)
        code, out = run_check(self.tmp)
        self.assertEqual(code, 1, out)
        self.assertIn("pad.press", out)
        self.assertIn("stale manual claim", out)

    def test_manual_step_claiming_a_control_the_app_has_is_red(self):
        doc = self.pilot()
        step = [s for s in self.batch(doc, "home")["steps"] if s["mode"] == "manual"][0]
        step["expect"] = "Something is judged by eye. (manual: needs control play.home.open-rom, which mesen-gui does not advertise)"
        self.write(doc)
        code, out = run_check(self.tmp)
        self.assertEqual(code, 1, out)
        self.assertIn("play.home.open-rom", out)
        self.assertIn("stale manual claim", out)

    def test_no_control_id_anywhere_is_red(self):
        (self.tmp / "UI" / "Views" / "PilotViews.axaml").unlink()
        code, out = run_check(self.tmp)
        self.assertEqual(code, 1, out)
        self.assertIn("no AutomationId", out)

    def test_unknown_control_id_in_a_wait_is_red(self):
        """A `wait.check` names a control id too.

        `ui.visible` is a check the pilot's own steps wait on, so a wait over
        `ui.focused`/`ui.screen`/`ui.visible` must have its id read like any other -
        otherwise an unknown id in a wait is the one id form the rule misses.
        """
        doc = self.pilot()
        step = [s for s in self.batch(doc, "home")["steps"] if s["id"] == "home.focus-holds-up"][0]
        step["wait"] = {"check": "ui.visible == play.home.gone", "ticks": 4}
        self.write(doc)
        code, out = run_check(self.tmp)
        self.assertEqual(code, 1, out)
        self.assertIn("play.home.gone", out)

    def test_a_compound_precondition_is_red(self):
        # #1242, from the real run: the supervised runner reads everything before " == " as the
        # check name, so a compound precondition asks the adapter for a check that does not
        # exist and the whole batch dies with `UnknownId: ...`. One comparison, or prose.
        doc = self.pilot()
        step = [s for s in self.batch(doc, "library")["steps"] if s["id"] == "lib.up-back-to-row-one"][0]
        step["precondition"] = "ui.focused == play.library.tile and fixture rom"
        self.write(doc)
        code, out = run_check(self.tmp)
        self.assertEqual(code, 1, out)
        self.assertIn("not one", out)

    def test_a_manual_step_whose_precondition_is_a_check_is_red(self):
        # #1242, from the real run: the supervised runner evaluates every step's precondition,
        # then does not run a manual one. A manual step's start state is reached by hand, so the
        # adapter cannot be asked to confirm it: `ui.focused == play.library.search` made the
        # runner raise `UnknownId` and killed the whole library batch.
        doc = self.pilot()
        step = [s for s in self.batch(doc, "library")["steps"] if s["id"] == "lib.up-back-to-row-one"][0]
        step["precondition"] = "ui.focused == play.home.open-rom"
        self.write(doc)
        code, out = run_check(self.tmp)
        self.assertEqual(code, 1, out)
        self.assertIn("is manual but its precondition", out)

    def test_malformed_check_args_do_not_crash_the_check(self):
        """One error line per drift, never a traceback.

        A `check.args` that is not a dict names no id this check can read, so it must
        be skipped - the format validator is what rejects the script, and this check
        must still exit cleanly instead of raising out of its own reader.
        """
        doc = self.pilot()
        step = [s for s in self.batch(doc, "home")["steps"] if s["id"] == "home.focus-holds-up"][0]
        step["check"] = {"name": "ui.focused", "args": ["is", "play.home.open-rom"]}
        self.write(doc)
        code, out = run_check(self.tmp)
        self.assertNotIn("Traceback", out)
        self.assertEqual(code, 0, out)

    def test_no_script_at_all_is_red(self):
        (self.tmp / PILOT).unlink()
        code, out = run_check(self.tmp)
        self.assertEqual(code, 1, out)
        self.assertIn("no *.gui-test.json", out)


if __name__ == "__main__":
    unittest.main()
