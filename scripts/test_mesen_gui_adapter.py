#!/usr/bin/env python3
"""Checks for scripts/gui_test/mesen_gui_adapter.py fixture placement (#1183).

The app resolves its home from a settings.json next to its executable
(ConfigManager.DefaultPortableFolder); HOME is ignored on macOS. So a session
must launch a cloned binary folder seeded with the run's settings.json, and
`files` fixtures must land under that folder. A `rom` fixture reaches the app
only through the profile: copied into `<clone>/library/` and written as
`Preferences.LibraryFolders`, never on argv (ADR-0272 item 6)."""
import hashlib
import json
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

    def test_rom_fixture_is_placed_as_a_library_folder(self):
        rom = self.root / "g.nes"
        rom.write_bytes(b"NES\x1a" + b"\0" * 12 + b"abc")
        sha = hashlib.sha1(b"abc").hexdigest()
        launched = adapter.resolve_fixtures({"rom": {"path": str(rom), "sha1": sha}}, self.work, self.exe)
        library = launched.parent / "library"
        self.assertEqual((library / "g.nes").read_bytes(), rom.read_bytes())
        settings = json.loads((launched.parent / "settings.json").read_text())
        self.assertEqual(settings["Preferences"]["LibraryFolders"], [str(library)])

    def test_no_rom_fixture_keeps_library_folders_absent(self):
        launched = adapter.resolve_fixtures({}, self.work, self.exe)
        self.assertNotIn("LibraryFolders", (launched.parent / "settings.json").read_text())

    def test_the_fresh_profile_starts_in_player_mode_so_the_app_stays_on_play_home(self):
        # An existing settings.json without UiMode is the upgrade path (Advanced), which leaves Play Home.
        launched = adapter.resolve_fixtures({}, self.work, self.exe)
        settings = json.loads((launched.parent / "settings.json").read_text())
        self.assertEqual(settings["Preferences"]["UiMode"], "Player")

    def test_a_rom_fixture_keeps_player_mode_next_to_the_library_folder(self):
        rom = self.root / "g.nes"
        rom.write_bytes(b"abc")
        launched = adapter.resolve_fixtures({"rom": {"path": str(rom), "sha1": hashlib.sha1(b"abc").hexdigest()}}, self.work, self.exe)
        settings = json.loads((launched.parent / "settings.json").read_text())
        self.assertEqual(settings["Preferences"]["UiMode"], "Player")

    def test_the_seeded_settings_turn_single_instance_off(self):
        # SingleInstance takes a machine-wide mutex: with it on, a launch beside any other Mesen exits 0 (#1220).
        launched = adapter.resolve_fixtures({}, self.work, self.exe)
        settings = json.loads((launched.parent / "settings.json").read_text())
        self.assertIs(settings["Preferences"]["SingleInstance"], False)

    def test_a_files_destination_outside_the_home_is_refused(self):
        src = self.root / "a.txt"
        src.write_text("x")
        for dest in ("../escape.txt", "/tmp/escape-1183.txt", "a/../../escape.txt"):
            with self.subTest(dest=dest):
                with self.assertRaises(adapter.FixtureError):
                    adapter.resolve_fixtures({"files": [{"src": str(src), "dest": dest}]}, self.work, self.exe)
        self.assertFalse((self.work / "escape.txt").exists())
        self.assertFalse(Path("/tmp/escape-1183.txt").exists())

    def test_a_files_item_without_a_destination_is_a_fixture_error(self):
        src = self.root / "a.txt"
        src.write_text("x")
        with self.assertRaises(adapter.FixtureError):
            adapter.resolve_fixtures({"files": [{"src": str(src)}]}, self.work, self.exe)

    def test_rom_fixture_with_the_wrong_sha1_is_refused(self):
        rom = self.root / "g.nes"
        rom.write_bytes(b"abc")
        with self.assertRaises(adapter.FixtureError):
            adapter.resolve_fixtures({"rom": {"path": str(rom), "sha1": "0" * 40}}, self.work, self.exe)
        self.assertFalse((self.work / "app" / "library").exists())

    def test_launch_runs_from_the_clone_even_when_the_app_dies(self):
        a = adapter.MesenGuiAdapter(binary=str(self.exe), workdir=self.work, connect_timeout=1)
        with self.assertRaises(adapter.Unavailable):
            a.launch({})
        self.assertTrue((self.work / "app" / "settings.json").is_file())


if __name__ == "__main__":
    unittest.main()
