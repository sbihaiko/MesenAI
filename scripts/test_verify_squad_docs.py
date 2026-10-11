#!/usr/bin/env python3
"""Fixture tests for scripts/checks/verify_squad_docs.py (#1825), the check that asserts three properties of
docs/squad/ (ADR-free tooling: every record listed in the folder index, no en-GB spelling, a Status line that agrees
with its own body about whether the fix is live).

The review of 5c72391c5 found the check passing things it promised to catch, each in the same shape: the rule asked a
question weaker than the one its verdict claimed to have answered.

  - The index rule was a raw substring test for a backticked file name anywhere in README.md, so a record named only
    in a sentence ("see `land-tip-not-final-state.md`") passed as listed while the index listed nothing. The rule now
    asks for a matching index bullet.
  - The en-US scan read the four records and skipped README.md, so an en-GB word in the one file every reader opens
    first passed while the verdict said "all en-US". README.md is scanned too.
  - The en-GB rule was a finite list, so `ageing` (and every word the list never named) passed the "No en-GB
    spelling" gate. The rule now reads the productive GB->US families with the en-US words of the same shape listed as
    exceptions, and the verdict names the rule set it ran instead of promising a dictionary it does not have.

`test_en_us_words_of_a_gb_shape_are_not_flagged` is the other half of that: a rule that fires on `raised`, `four` or
`analyses` would redden innocent docs, which is worse than the miss it replaced.

Run: python3 scripts/test_verify_squad_docs.py
"""
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CHECK = ROOT / "scripts" / "checks" / "verify_squad_docs.py"
MAKEFILE = ROOT / "makefile"

LISTED = "- `{name}` - a record.\n"
PROSE_ONLY = "The record `{name}` covers it.\n"


def run_check(root):
    """The check's verdict over `root`, as (exit code, stdout + stderr)."""
    p = subprocess.run([sys.executable, str(CHECK), str(root)], capture_output=True, text=True)
    return p.returncode, p.stdout + p.stderr


def squad_tree(self, readme, **records):
    """A throwaway repo root holding docs/squad/ with `readme` as its README.md and one file per `records` entry."""
    tmp = Path(tempfile.mkdtemp(prefix="squad-docs-"))
    self.addCleanup(shutil.rmtree, tmp, ignore_errors=True)
    squad = tmp / "docs" / "squad"
    squad.mkdir(parents=True)
    (squad / "README.md").write_text(readme, encoding="utf-8")
    for name, text in records.items():
        (squad / f"{name}.md").write_text(text, encoding="utf-8")
    return tmp


class VerifySquadDocs(unittest.TestCase):
    def test_index_needs_a_bullet_not_a_prose_mention(self):
        """A record named in README prose, with no index bullet, is not listed: the reader cannot discover it."""
        root = squad_tree(self, "# Squad operations\n\nThe record `prose-only.md` explains the outage.\n",
                          **{"prose-only": "# A record\n"})
        rc, out = run_check(root)
        self.assertNotEqual(rc, 0, "a prose mention passed as an index entry while the index lists nothing")
        self.assertIn("prose-only.md", out, f"the verdict must name the unlisted record:\n{out}")

    def test_index_bullet_lists_the_record(self):
        """An index bullet naming the record is what the rule asks for, and it passes."""
        root = squad_tree(self, "# Squad operations\n\n" + LISTED.format(name="listed.md"),
                          **{"listed": "# A record\n"})
        rc, out = run_check(root)
        self.assertEqual(rc, 0, f"a listed record was reported as unlisted:\n{out}")

    def test_readme_is_scanned_for_en_gb_too(self):
        """README.md is a versioned en-US doc in the folder the check owns, so an en-GB word in it fails the run."""
        readme = ("# Squad operations\n\nA note on colour handling.\n\n"
                  + LISTED.format(name="record.md"))
        root = squad_tree(self, readme, record="# A record\n")
        rc, out = run_check(root)
        self.assertNotEqual(rc, 0, "an en-GB word in docs/squad/README.md passed the en-US rule")
        self.assertIn("README.md", out, f"the verdict must name README.md as the file that carries it:\n{out}")

    def test_gb_words_the_list_never_named_are_caught(self):
        """The listed spellings are not the rule: `ageing` and the -ise/-isation family are en-GB without being enumerated."""
        for word in ("ageing", "marginalised", "organisational"):
            root = squad_tree(self, "# Squad operations\n\n" + LISTED.format(name="record.md"),
                              record=f"# A record\n\n{word} is what the log spelled.\n")
            rc, out = run_check(root)
            self.assertNotEqual(rc, 0, f"'{word}' is en-GB and the check passed it")
            self.assertIn(word, out, f"the verdict must name '{word}':\n{out}")

    def test_en_us_words_of_a_gb_shape_are_not_flagged(self):
        """The families carry en-US exceptions: a rule that fires on these reddens docs that are already correct."""
        body = ("# A record\n\nWe raised four points, surprise at the analysis and its analyses, telling your hourly "
                "reader otherwise, precise and promised, praised, television, advertise, exercised, compromise, "
                "merchandise, expertise, treatise, paradise, likewise.\n")
        root = squad_tree(self, "# Squad operations\n\n" + LISTED.format(name="record.md"), record=body)
        rc, out = run_check(root)
        self.assertEqual(rc, 0, f"en-US words were reported as en-GB:\n{out}")

    def test_status_block_still_must_agree_with_the_body(self):
        """Rule (c) survives the rewrite: a Status line reading as done over a body that says the fix is unmerged fails."""
        body = ("# A record\n\nStatus: fixed in the fork.\n\n"
                "The fix is not merged into the live `local/next`.\n")
        root = squad_tree(self, "# Squad operations\n\n" + LISTED.format(name="record.md"), record=body)
        rc, out = run_check(root)
        self.assertNotEqual(rc, 0, "a Status line reading as done passed over a body that says the fix is unmerged")
        self.assertIn("Status", out, f"the verdict must name the Status line:\n{out}")

    def test_status_block_may_declare_the_fix_pending(self):
        """The same record with a pending marker in its Status block passes: the rule asks for agreement, not for a phrase."""
        body = ("# A record\n\nStatus: pending deployment, the fix is ready in the fork.\n\n"
                "The fix is not merged into the live `local/next`.\n")
        root = squad_tree(self, "# Squad operations\n\n" + LISTED.format(name="record.md"), record=body)
        rc, out = run_check(root)
        self.assertEqual(rc, 0, f"a Status block declaring the fix pending was reported:\n{out}")

    def test_the_repo_tree_passes(self):
        """The folder this repository ships passes its own check, README.md included in the en-US scan."""
        rc, out = run_check(ROOT)
        self.assertEqual(rc, 0, f"docs/squad/ fails its own check at HEAD:\n{out}")
        self.assertIn("README.md", out, "the verdict must say README.md was scanned, or a reader cannot tell")

    def test_check_is_wired_into_doc_checks(self):
        """The check is durable only while doc-checks runs it: an unwired check gates nothing."""
        self.assertIn("scripts/checks/verify_squad_docs.py", MAKEFILE.read_text(encoding="utf-8"),
                      "makefile no longer runs scripts/checks/verify_squad_docs.py")

    def test_check_is_documented_in_the_owning_tree(self):
        """docs/AGENTS.md owns docs/ and states the Verification contract every doc change is gated by."""
        agents = (ROOT / "docs" / "AGENTS.md").read_text(encoding="utf-8")
        self.assertIn("verify_squad_docs.py", agents,
                      "docs/AGENTS.md does not document the check that gates docs/squad/")


def main():
    result = unittest.main(exit=False, verbosity=2).result
    print(f"\n{result.testsRun - len(result.failures) - len(result.errors)}/{result.testsRun} passed")
    sys.exit(0 if result.wasSuccessful() else 1)


if __name__ == "__main__":
    main()
