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

    def test_no_script_at_all_is_red(self):
        (self.tmp / PROCESS / f"{PILOT}.json").unlink()
        code, out = run_check(self.tmp)
        self.assertEqual(code, 1, out)
        self.assertIn("no *.gui-test.json", out)


if __name__ == "__main__":
    unittest.main()
