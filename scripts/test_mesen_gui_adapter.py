#!/usr/bin/env python3
"""Checks for scripts/gui_test/mesen_gui_adapter.py fixture placement (#1183).

The app resolves its home from a settings.json next to its executable
(ConfigManager.DefaultPortableFolder); HOME is ignored on macOS. So a session
must launch a cloned binary folder seeded with the run's settings.json, and
`files` fixtures must land under that folder. A `rom` fixture the app cannot be
handed is a FixtureError, never silently ignored (ADR-0272 item 6)."""
import hashlib
import os
import stat
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent / "gui_test"))
import mesen_gui_adapter as adapter  # noqa: E402


def fake_app(root):
    app = Path(root) / "app"
    app.mkdir()
    exe = app / "UI"
    exe.write_text("#!/bin/sh\nexit 0\n")
    exe.chmod(exe.stat().st_mode | stat.S_IXUSR)
    (app / "MesenCore.dylib").write_bytes(b"core")
    return exe


class PlaceFixtures(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        self.exe = fake_app(self.root)
        self.work = self.root / "work"
        self.work.mkdir()

    def tearDown(self):
        self.tmp.cleanup()

    def test_launched_binary_dir_holds_the_runs_settings_json(self):
        launched = adapter.resolve_fixtures({}, self.work, self.exe)
        self.assertNotEqual(launched.parent, self.exe.parent)
        self.assertTrue((launched.parent / "settings.json").is_file())
        self.assertTrue((launched.parent / "MesenCore.dylib").is_file())
        self.assertFalse((self.exe.parent / "settings.json").exists(), "the source app folder must stay untouched")

    def test_files_fixtures_land_in_the_portable_home(self):
        src = self.root / "a.txt"
        src.write_text("x")
        launched = adapter.resolve_fixtures({"files": [{"src": str(src), "dest": "Saves/a.txt"}]}, self.work, self.exe)
        self.assertEqual((launched.parent / "Saves" / "a.txt").read_text(), "x")

    def test_rom_fixture_is_refused_not_ignored(self):
        rom = self.root / "g.nes"
        rom.write_bytes(b"NES\x1a" + b"\0" * 12 + b"abc")
        sha = hashlib.sha1(b"abc").hexdigest()
        with self.assertRaisesRegex(adapter.FixtureError, "not yet placeable"):
            adapter.resolve_fixtures({"rom": {"path": str(rom), "sha1": sha}}, self.work, self.exe)

    def test_launch_runs_from_the_clone_even_when_the_app_dies(self):
        a = adapter.MesenGuiAdapter(binary=str(self.exe), workdir=self.work, connect_timeout=1)
        with self.assertRaises(adapter.Unavailable):
            a.launch({})
        self.assertTrue((self.work / "app" / "settings.json").is_file())


if __name__ == "__main__":
    unittest.main()
