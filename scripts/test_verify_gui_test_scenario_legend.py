#!/usr/bin/env python3
"""Fixture tests for scripts/checks/verify_gui_test_scenario_legend.py (#1764): a published scenario's legend must
name the record span its areas hold, derived from the map's layout, and the check must go red on the range the
published body carried before the area migration.

`PUBLISHED_BEFORE_THE_MIGRATION` is #1286's own legend, captured from that issue's body on 2026-10-11 - the line
that still said `#1288-#1387` while the map's 16 areas held #1288-#1787, so a reader who followed it missed the 194
records the migration created. It is the fixture this check exists to reject.
"""
import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CHECK = ROOT / "scripts" / "checks" / "verify_gui_test_scenario_legend.py"
MAP = Path("docs/validation/process/play-pad-only.gui-test.issues.json")
ADR = "docs/adr/gui/0275-a-gui-test-scenario-records-each-run-as-its-own-issue-with-one-emoji-row-per-case.md"

# The legend #1286's body carried when the debt was filed. Kept verbatim: the check is red on this line today.
PUBLISHED_BEFORE_THE_MIGRATION = (
    "<sub>Legend: ✅ passed · ❌ failed · \U0001f7e1 needs review · \U0001f464 manual "
    "· ⚪ not run. The run issues are this scenario's sub-issues; the case records (#1288–#1387) "
    "are linked from the Cases table below. Format: ADR-0275 (proposed).</sub>")

# The same line, with the span the areas of the synthetic map below hold: #100-#130 over 2 areas.
FIXTURE_LEGEND = (
    "<sub>Legend: ✅ passed · ❌ failed · \U0001f7e1 needs review · \U0001f464 manual "
    "· ⚪ not run. The run issues are sub-issues of each area's monthly Runs container; "
    "the case records (#100–#130) live in the 2 areas below, linked from each area's Cases table. "
    "Format: ADR-0275 (accepted).</sub>")

# A well-formed line for a map that carries no areas and no records: every token is valid, and it verifies nothing.
EMPTY_AREAS_LEGEND = (
    "<sub>Legend: ✅ passed · ❌ failed · \U0001f7e1 needs review · \U0001f464 manual "
    "· ⚪ not run. The run issues are sub-issues of each area's monthly Runs container; "
    "the case records live in the 0 areas below, linked from each area's Cases table. "
    "Format: ADR-0275 (accepted).</sub>")


def fixture_map(legend=None):
    """A two-area map. Its records are #100, #105 and #130, so the span a legend must name is #100-#130."""
    doc = {
        "layout": "areas",
        "parent": 1286,
        "boarded": True,
        "areas": {
            "home": {"number": 1531, "containers": [{"number": 1532, "title": "Cases"}], "runs": {}},
            "pause": {"number": 1541, "containers": [{"number": 1542, "title": "Cases"}], "runs": {}},
        },
        "cases": {
            "HOME-01": {"area": "home", "container": 1532, "number": 100},
            "HOME-02": {"area": "home", "container": 1532, "number": 105},
            "PAU-01": {"area": "pause", "container": 1542, "number": 130},
        },
    }
    if legend is not None:
        doc["legend"] = legend
    return doc


def run_check(repo):
    p = subprocess.run([sys.executable, str(CHECK), "--repo", str(repo)], capture_output=True, text=True)
    return p.returncode, p.stdout + p.stderr


class VerifyGuiTestScenarioLegend(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.tmp, True)
        self.process = self.tmp / "docs" / "validation" / "process"
        self.process.mkdir(parents=True)
        adr = self.tmp / ADR
        adr.parent.mkdir(parents=True)
        shutil.copy(ROOT / ADR, adr)  # the fixture's status token comes from the real register, never a literal

    def write_map(self, doc):
        (self.process / MAP.name).write_text(json.dumps(doc), encoding="utf-8")

    def test_the_committed_map_s_legend_names_the_span_its_areas_hold(self):
        # The published legend is the only thing a reader follows to the records, and it is committed beside the
        # record map: the span and the area count in it are the areas' own, not a range typed at migration time.
        # Asserting that against the map is the check's job, so the test runs the check over ROOT: a migration that
        # moves the areas and leaves the line saying `#1288–#1787` and `16 areas` is red here.
        code, out = run_check(ROOT)
        self.assertEqual((code, out), (0, ""))

    def test_the_legend_the_published_body_carried_before_the_migration_is_rejected(self):
        self.write_map(fixture_map(PUBLISHED_BEFORE_THE_MIGRATION))
        code, out = run_check(self.tmp)
        self.assertEqual(code, 1)
        self.assertIn("#1288–#1387", out)
        self.assertIn("#100–#130", out)

    def test_a_legend_naming_the_span_the_areas_hold_passes(self):
        self.write_map(fixture_map(FIXTURE_LEGEND))
        code, out = run_check(self.tmp)
        self.assertEqual((code, out), (0, ""))

    def test_a_map_with_areas_and_no_legend_is_a_failure(self):
        self.write_map(fixture_map())
        code, out = run_check(self.tmp)
        self.assertEqual(code, 1)
        self.assertIn("carries no legend", out)

    def test_a_legend_naming_an_area_count_the_map_does_not_have_is_a_failure(self):
        self.write_map(fixture_map(FIXTURE_LEGEND.replace("the 2 areas below", "the 3 areas below")))
        code, out = run_check(self.tmp)
        self.assertEqual(code, 1)
        self.assertIn("3 areas", out)

    def test_a_legend_naming_a_status_the_adr_no_longer_carries_is_a_failure(self):
        self.write_map(fixture_map(FIXTURE_LEGEND.replace("(accepted)", "(proposed)")))
        code, out = run_check(self.tmp)
        self.assertEqual(code, 1)
        self.assertIn("proposed", out)

    def test_a_map_with_areas_and_no_cases_is_a_failure(self):
        # With no records the span the legend names has nothing to be checked against, so a line that still carries
        # the range the flat layout had would pass by being unverifiable. Rejected, not skipped.
        doc = fixture_map(FIXTURE_LEGEND)
        doc["cases"] = {}
        self.write_map(doc)
        code, out = run_check(self.tmp)
        self.assertEqual(code, 1)
        self.assertIn("carries no cases", out)

    def test_a_map_laid_out_as_areas_with_no_areas_is_a_failure(self):
        # Every token of this line is well formed - 0 areas below is what an empty `areas` object holds - so counting
        # a map that carries no areas as verified is a check that checks nothing, one indent deeper.
        doc = fixture_map(EMPTY_AREAS_LEGEND)
        doc["areas"] = {}
        doc["cases"] = {}
        self.write_map(doc)
        code, out = run_check(self.tmp)
        self.assertEqual(code, 1)
        self.assertIn("carries no areas", out)

    def test_a_repo_with_no_area_map_is_a_check_that_checks_nothing(self):
        code, out = run_check(self.tmp)
        self.assertEqual(code, 1)
        self.assertIn("nothing to verify", out)


if __name__ == "__main__":
    unittest.main()
