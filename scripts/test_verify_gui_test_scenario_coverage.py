#!/usr/bin/env python3
"""Fixture tests for scripts/checks/verify_gui_test_scenario_coverage.py (#1242): it must go red on a lost
case and on a step naming a case the scenario does not define, must read a case whose cell carries a tag,
and must never skip - a tree with no script/scenario pair fails."""
import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CHECK = ROOT / "scripts" / "checks" / "verify_gui_test_scenario_coverage.py"
PROCESS = Path("docs/validation/process")

SCENARIO = """# A scenario

### 1. Home

| ID | Precondition | Pad-only steps | Expected | [W] | [F] | Notes |
|---|---|---|---|---|---|---|
| HOME-01 | fresh | look | home is up | | — | |
| P4-04 **(pass 3)** | W-P4 | switch pad | the footer re-reads | | | |
"""


def _step(sid, precondition="", expect=""):
    return {"id": sid, "role": "under-test", "precondition": precondition, "expect": expect,
            "severity": "major", "mode": "manual"}


def _script(*steps):
    return {"format": "gui-test/1", "name": "play-pad-only", "target": "mesen-gui",
            "requires": {"actions": [], "checks": []}, "variants": {}, "fixtures": {},
            "batches": [{"id": "home", "setup": [], "teardown": [], "steps": list(steps)}]}


def run_check(repo):
    p = subprocess.run([sys.executable, str(CHECK), "--repo", str(repo)], capture_output=True, text=True)
    return p.returncode, p.stdout + p.stderr


class VerifyGuiTestScenarioCoverage(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.tmp, True)
        (self.tmp / PROCESS).mkdir(parents=True)
        (self.tmp / PROCESS / "play-pad-only-test-script.md").write_text(SCENARIO, encoding="utf-8")

    def write_script(self, *steps):
        (self.tmp / PROCESS / "play-pad-only.gui-test.json").write_text(
            json.dumps(_script(*steps), indent=2), encoding="utf-8")

    def test_a_scenario_fully_referenced_by_a_step_id_precondition_or_expect_passes(self):
        self.write_script(_step("home.first", precondition="HOME-01", expect="home is up"),
                          _step("P4-04", expect="the footer re-reads"))
        code, out = run_check(self.tmp)
        self.assertEqual((code, out), (0, ""))

    def test_a_case_no_step_references_is_a_lost_case(self):
        self.write_script(_step("home.first", precondition="HOME-01"))
        code, out = run_check(self.tmp)
        self.assertEqual(code, 1)
        self.assertIn("case P4-04 of play-pad-only-test-script.md is in no step", out)

    def test_a_step_naming_a_case_the_scenario_does_not_define_is_a_typo(self):
        self.write_script(_step("home.first", precondition="HOME-01", expect="P4-04 and HOME-09"))
        code, out = run_check(self.tmp)
        self.assertEqual(code, 1)
        self.assertIn("a step names case HOME-09, which play-pad-only-test-script.md does not define", out)

    def test_a_script_with_no_scenario_beside_it_makes_the_whole_run_red(self):
        (self.tmp / PROCESS / "play-pad-only-test-script.md").unlink()
        self.write_script(_step("home.first"))
        code, out = run_check(self.tmp)
        self.assertEqual(code, 1)
        self.assertIn("nothing to verify", out)

    def test_a_scenario_md_with_no_id_table_is_a_check_that_checks_nothing(self):
        (self.tmp / PROCESS / "play-pad-only-test-script.md").write_text("# A scenario\n\nprose only\n", encoding="utf-8")
        self.write_script(_step("home.first"))
        code, out = run_check(self.tmp)
        self.assertEqual(code, 1)
        self.assertIn("no case id found", out)


if __name__ == "__main__":
    unittest.main()
