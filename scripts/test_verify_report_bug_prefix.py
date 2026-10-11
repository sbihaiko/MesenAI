#!/usr/bin/env python3
"""Fixture tests for scripts/checks/verify_report_bug_prefix.sh (#1807): the check must not keep its own copy of the
marker pattern it enforces, and the pattern it shows must be the one scripts/report-bug.sh runs.

The check exists to keep one rule - the fork-only marker line - identical on both sides that apply it, and it reads
that pattern out of scripts/report-bug.sh instead of restating it. Its own header comment restated it anyway, in
prose, and nothing compared the two: growing the pattern in the helper (and in the CLAUDE.md citation the check
already binds to the helper) left the comment quoting the old one while the check kept passing. `test_check_prints_
the_pattern_the_helper_runs` and `test_helper_pattern_change_reaches_the_check_output` are that drift: the second
copies the tree, changes the pattern in the helper and in CLAUDE.md to one the check has never seen, and requires the
check to report the new one. `test_check_keeps_no_copy_of_the_pattern` is the guard that a copy cannot come back.
"""
import re
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CHECK = ROOT / "scripts" / "checks" / "verify_report_bug_prefix.sh"
HELPER = ROOT / "scripts" / "report-bug.sh"
CLAUDE_MD = ROOT / "CLAUDE.md"

# The helper's marker test, as it reads it: the single-quoted operand of its one `grep -qE ... <<<"$BODY"`.
PATTERN_RE = re.compile(r"""grep -qE '([^']*)' <<<"\$BODY\"""")

# The pattern the helper may be moved to, spelled differently and matching the same bodies: ERE `[ ]` is a space,
# so every marker form the old pattern caught this one catches too.
MOVED = "[ ]"


def helper_pattern(repo):
    """The marker pattern scripts/report-bug.sh executes, read from the script itself."""
    found = PATTERN_RE.findall((repo / "scripts" / "report-bug.sh").read_text())
    if len(found) != 1:
        raise AssertionError(f"expected exactly one marker test in {repo}/scripts/report-bug.sh, found {len(found)}")
    return found[0]


def run_check(path):
    p = subprocess.run(["bash", str(path)], capture_output=True, text=True)
    return p.returncode, p.stdout + p.stderr


def fixture_repo(pattern):
    """A copy of the check, the helper and CLAUDE.md, with the marker pattern replaced by `pattern` throughout."""
    tmp = Path(tempfile.mkdtemp())
    (tmp / "scripts" / "checks").mkdir(parents=True)
    for src, dest in ((CHECK, tmp / "scripts" / "checks" / CHECK.name),
                      (HELPER, tmp / "scripts" / HELPER.name),
                      (CLAUDE_MD, tmp / "CLAUDE.md")):
        dest.write_text(src.read_text().replace(helper_pattern(ROOT), pattern))
    return tmp


class VerifyReportBugPrefix(unittest.TestCase):
    def test_check_prints_the_pattern_the_helper_runs(self):
        """The verdict names the marker pattern the helper runs, read out of the helper, not a copy."""
        pattern = helper_pattern(ROOT)
        rc, out = run_check(CHECK)
        self.assertEqual(rc, 0, f"the check is red at HEAD:\n{out}")
        self.assertIn(pattern, out,
                      "the check's verdict must name the marker pattern it enforced, read from the helper")

    def test_helper_pattern_change_reaches_the_check_output(self):
        """A helper moved to a pattern the check has never seen must reach the check's output, not be papered over."""
        moved = helper_pattern(ROOT).replace(" work", f"{MOVED}work")
        self.assertNotEqual(moved, helper_pattern(ROOT))
        self.tmp = fixture_repo(moved)
        # Registered here, not in `setUp`: a path read before `fixture_repo` made the tree would be `None`.
        self.addCleanup(shutil.rmtree, self.tmp, True)
        rc, out = run_check(self.tmp / "scripts" / "checks" / CHECK.name)
        self.assertEqual(rc, 0, f"the check is red on a tree whose helper runs '{moved}':\n{out}")
        self.assertIn(moved, out, f"the check must report '{moved}', the pattern its helper now runs:\n{out}")

    def test_check_keeps_no_copy_of_the_pattern(self):
        """The check must not restate the pattern: a copy is what goes stale while the check stays green."""
        pattern = helper_pattern(ROOT)
        # `pattern in ...` rather than assertNotIn: the message must name the copy, not reprint the whole check.
        self.assertFalse(pattern in CHECK.read_text(),
                         f"the check restates the marker pattern '{pattern}'; it must read it from the helper")

    def test_the_fixture_directory_is_removed_when_the_case_finishes(self):
        """The cleanup must carry the directory `fixture_repo` made. A cleanup registered with a path read
        before the fixture exists captures `None`, and `shutil.rmtree(None, True)` removes nothing: every run
        of the fixture case would leak a tree under the system temp folder, silently."""
        case = VerifyReportBugPrefix("test_helper_pattern_change_reaches_the_check_output")
        result = unittest.TestResult()
        case.run(result)
        self.assertEqual(result.errors + result.failures, [], "the case is red, so its cleanup proves nothing")
        self.assertFalse(case.tmp.exists(),
                         f"the fixture directory {case.tmp} leaked: the cleanup ran on None, not on the fixture")


if __name__ == "__main__":
    unittest.main(verbosity=2)
