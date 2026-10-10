#!/usr/bin/env python3
"""Contract + e2e tests for the `mesen-gui` GUI test adapter (ADR-0272 item 6, #1183).

The hook is faked by a socket server speaking the real protocol, and the e2e case
launches a stand-in executable that starts that same server from the `--test-hook`
flags, so launch/inject/check/wait/capture/teardown run over a real socket and a
real child process. Fixtures FAIL when missing; nothing here skips."""
import hashlib
import json
import os
import socket
import stat
import sys
import tempfile
import textwrap
import threading
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "scripts" / "gui_test"))

import mesen_gui_adapter as adapter  # noqa: E402
import run_headless_e2e as runner  # noqa: E402

PNG = b"\x89PNG\r\n\x1a\nfake"
TOKEN = "s3"


class FakeHook:
    """The hook's protocol (UI/Logic/TestHook/TestHookProtocol.cs): one JSON line in, one out."""

    def __init__(self, path):
        self.path = path
        self.focus = "play.home.continue"
        self.tick = 0
        self.known = ["play.home", "play.home.continue", "play.home.open-rom"]
        self.requests = []
        self.extra_controls = []  # controls appended after `known`, e.g. a hidden twin of an id
        self.quit = threading.Event()
        self.server = socket.socket(socket.AF_UNIX)
        self.server.bind(str(path))
        self.server.listen(1)
        self.thread = threading.Thread(target=self.serve, daemon=True)
        self.thread.start()

    def serve(self):
        conn, _ = self.server.accept()
        with conn, conn.makefile("rw", encoding="utf-8", newline="\n") as f:
            for line in f:
                req = json.loads(line)
                self.requests.append(req)
                f.write(json.dumps(self.answer(req)) + "\n")
                f.flush()
                if req.get("op") == "quit":
                    self.quit.set()
                    return

    def answer(self, req):
        if req.get("token") != TOKEN:
            return {"id": req.get("id"), "ok": False, "error": "unauthorized"}
        out = {"id": req["id"], "ok": True}
        op = req["op"]
        if op == "hello":
            out.update(hook=1, ops=["hello", "state", "inject", "capture", "quit"], namespaces=["pad", "ui"])
        elif op == "state":
            self.tick += 3
            out.update(
                screen="play.home", dialogs=[], focus=self.focus, tick=self.tick, frames=0,
                controls=[{"id": i, "enabled": True, "visible": True, "focused": i == self.focus} for i in self.known] + self.extra_controls,
            )
        elif op == "inject":
            if req["action"] != "pad.press":
                return {"id": req["id"], "ok": False, "error": "unknown action " + req["action"]}
            if req["args"]["button"] == "Up":
                self.focus = "play.home.open-rom"
        elif op == "capture":
            Path(req["path"]).write_bytes(PNG)
            out.update(path=req["path"], size=[2, 2], sha256=hashlib.sha256(PNG).hexdigest())
        return out


class Base(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp(prefix="g1183-", dir="/tmp"))
        self.addCleanup(lambda: __import__("shutil").rmtree(self.tmp, True))
        self.hook = FakeHook(self.tmp / "h.sock")
        self.addCleanup(self.hook.server.close)
        self.session = adapter.Session(str(self.tmp / "h.sock"), TOKEN, process=None, workdir=self.tmp)
        self.addCleanup(self.session.close)


class AdapterContract(Base):
    def test_capabilities_are_pad_only_and_name_only_what_is_implemented(self):
        caps = adapter.MesenGuiAdapter().capabilities()
        self.assertEqual(caps["actions"], ["pad.press"])
        self.assertEqual(caps["input_families"], ["pad"])
        self.assertEqual(set(caps["checks"]), set(adapter.CHECKS))
        self.assertEqual(caps["variants"], {"window.mode": ["windowed"]})

    def test_every_advertised_action_and_check_runs(self):
        caps = adapter.MesenGuiAdapter().capabilities()
        for action in caps["actions"]:
            self.session.inject(action, {"button": "Up", "ticks": 2})
        self.assertTrue(self.session.check("ui.screen", {"is": "play.home"})["passed"])
        self.assertTrue(self.session.check("ui.focused", {"is": "play.home.open-rom"})["passed"])
        self.assertTrue(self.session.check("ui.visible", {"is": "play.home.continue"})["passed"])

    def test_a_duplicated_id_is_visible_when_any_copy_is_visible(self):
        # First-run Home: the Primary Open ROM is on screen, the hidden Secondary (PlayHomeWithRecents) comes later in the tree.
        self.hook.known = ["play.home", "play.home.open-rom"]
        self.hook.extra_controls = [{"id": "play.home.open-rom", "enabled": True, "visible": False, "focused": False}]
        result = self.session.check("ui.visible", {"is": "play.home.open-rom"})
        self.assertEqual(result, {"passed": True, "observed": True})

    def test_a_duplicated_id_that_is_hidden_everywhere_is_not_visible(self):
        self.hook.known = ["play.home"]
        self.hook.extra_controls = [{"id": "play.home.open-rom", "enabled": True, "visible": False, "focused": False}] * 2
        self.assertFalse(self.session.check("ui.visible", {"is": "play.home.open-rom"})["passed"])
        self.assertTrue(self.session.check("ui.dialogs", {"is": []})["passed"])

    def test_inject_carries_the_token_and_is_acknowledged(self):
        self.session.inject("pad.press", {"button": "Up", "ticks": 2})
        self.assertEqual({r["token"] for r in self.hook.requests}, {TOKEN})

    def test_an_unadvertised_action_is_refused_before_the_socket(self):
        with self.assertRaises(adapter.AdapterError):
            self.session.inject("pad.chord", {"buttons": ["A"]})
        self.assertEqual([r for r in self.hook.requests if r["op"] == "inject"], [])

    def test_a_check_naming_an_unknown_id_is_unknown_id_never_false(self):
        with self.assertRaises(adapter.UnknownId):
            self.session.check("ui.focused", {"is": "play.home.renamed"})
        with self.assertRaises(adapter.UnknownId):
            self.session.check("ui.visible", {"is": "nope"})

    def test_a_failed_check_is_false_not_an_error(self):
        result = self.session.check("ui.focused", {"is": "play.home.open-rom"})
        self.assertFalse(result["passed"])
        self.assertEqual(result["observed"], "play.home.continue")

    def test_wait_is_met_when_the_condition_holds_in_ticks(self):
        self.session.inject("pad.press", {"button": "Up", "ticks": 2})
        self.assertEqual(self.session.wait("ui.focused == play.home.open-rom", 120), "met")

    def test_wait_times_out_without_raising_and_counts_ticks_not_seconds(self):
        self.assertEqual(self.session.wait("ui.focused == play.home.open-rom", 9), "timeout")
        self.assertLessEqual(self.hook.tick, 9 + 3 * 2)

    def test_capture_is_a_png_with_path_size_and_sha256(self):
        shot = self.session.capture(self.tmp / "a.png")
        self.assertEqual(Path(shot["path"]).read_bytes(), PNG)
        self.assertEqual(shot["size"], [2, 2])
        self.assertEqual(shot["sha256"], hashlib.sha256(PNG).hexdigest())

    def test_a_capture_whose_hash_disagrees_with_the_file_is_an_error(self):
        orig = self.hook.answer
        self.hook.answer = lambda r: {**orig(r), "sha256": "0" * 64} if r["op"] == "capture" else orig(r)
        with self.assertRaises(adapter.AdapterError):
            self.session.capture(self.tmp / "b.png")

    def test_teardown_sends_quit_and_reports_nothing_unrestored(self):
        self.assertEqual(self.session.teardown(), [])
        self.assertTrue(self.hook.quit.wait(5))


FAKE_APP = textwrap.dedent('''\
    #!{python}
    import sys
    sys.path.insert(0, {tests!r})
    import test_gui_test_mesen_adapter as t
    from pathlib import Path
    args = sys.argv[1:]
    endpoint = [a.split("=", 1)[1] for a in args if a.startswith("--test-hook=")][0]
    token = [a.split("=", 1)[1] for a in args if a.startswith("--test-hook-token=")][0]
    t.TOKEN = token
    hook = t.FakeHook(Path(endpoint))
    print("test hook listening on " + endpoint, file=sys.stderr, flush=True)
    hook.quit.wait(30)
''')


class AdapterE2E(unittest.TestCase):
    """launch -> inject -> wait -> check -> capture -> teardown against a real child process."""

    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp(prefix="g1183e-", dir="/tmp"))
        self.addCleanup(lambda: __import__("shutil").rmtree(self.tmp, True))
        (self.tmp / "appdir").mkdir()  # the app folder is cloned into self.tmp/run, so it cannot be self.tmp itself
        self.app = self.tmp / "appdir" / "fake-app"
        self.app.write_text(FAKE_APP.format(python=sys.executable, tests=str(ROOT / "scripts")))
        self.app.chmod(self.app.stat().st_mode | stat.S_IXUSR)
        self.rom = self.tmp / "Contra (USA).nes"
        self.rom.write_bytes(b"rom")
        # A fresh profile has no rom fixture; the rom tests add self.rom_fixture themselves.
        self.fixtures = {"settings": {"profile": "fresh"}}
        self.rom_fixture = {"path": str(self.rom), "sha1": hashlib.sha1(b"rom").hexdigest()}

    def launch(self, fixtures=None):
        return adapter.MesenGuiAdapter(binary=str(self.app), workdir=self.tmp / "run").launch(fixtures or self.fixtures)

    def test_three_pilot_steps_run_with_a_screenshot_each(self):
        session = self.launch()
        try:
            steps = []
            steps.append(("launch.first-run-home", session.wait("ui.screen == play.home", 120), session.check("ui.screen", {"is": "play.home"})))
            session.inject("pad.press", {"button": "Up", "ticks": 4})
            steps.append(("home.focus-holds-up", session.wait("ui.focused == play.home.open-rom", 120), session.check("ui.focused", {"is": "play.home.open-rom"})))
            steps.append(("home.no-dialog", session.wait("ui.screen == play.home", 4), session.check("ui.dialogs", {"is": []})))
            for i, (_, waited, check) in enumerate(steps):
                self.assertEqual(waited, "met")
                self.assertTrue(check["passed"])
                shot = session.capture(self.tmp / f"step{i}.png")
                self.assertTrue(Path(shot["path"]).exists())
        finally:
            self.assertEqual(session.teardown(), [])

    def test_the_run_gets_a_fresh_home_and_the_hook_flags(self):
        session = self.launch()
        try:
            self.assertIn("--test-hook=", " ".join(session.argv))
            # HOME does not isolate the app; the fresh profile is a settings.json next to the cloned binary.
            clone = Path(session.argv[0])
            self.assertEqual(clone.parent, self.tmp / "run" / "app")
            self.assertEqual((clone.parent / "settings.json").read_text(), "{}\n")
            self.assertTrue(session.log_text().startswith("test hook listening on"))
        finally:
            session.teardown()

    def test_a_missing_rom_fails_launch_and_starts_nothing(self):
        self.rom.unlink()
        self.fixtures["rom"] = self.rom_fixture
        with self.assertRaises(adapter.FixtureError) as ctx:
            self.launch()
        self.assertIn("rom", str(ctx.exception))
        self.assertFalse((self.tmp / "run" / "hook.sock").exists())

    def test_a_rom_fixture_is_placed_in_the_clone_and_written_as_the_library_folder(self):
        self.fixtures["rom"] = self.rom_fixture
        session = self.launch()
        try:
            home = Path(session.argv[0]).parent
            self.assertEqual((home / "library" / self.rom.name).read_bytes(), b"rom")
            settings = json.loads((home / "settings.json").read_text())
            self.assertEqual(settings["Preferences"]["LibraryFolders"], [str(home / "library")])
            self.assertFalse(any("rom" in a.lower() or ".nes" in a for a in session.argv[1:]))
        finally:
            session.teardown()

    def test_no_rom_fixture_leaves_library_folders_absent(self):
        session = self.launch()
        try:
            home = Path(session.argv[0]).parent
            self.assertFalse((home / "library").exists())
            self.assertNotIn("LibraryFolders", (home / "settings.json").read_text())
        finally:
            session.teardown()

    def test_a_rom_with_the_wrong_sha1_fails_launch(self):
        self.fixtures["rom"] = {**self.rom_fixture, "sha1": "0" * 40}
        with self.assertRaises(adapter.FixtureError):
            self.launch()

    def test_a_placeholder_sha1_is_not_a_pass(self):
        self.fixtures["rom"] = {**self.rom_fixture, "sha1": "<no-intro sha1 per ADR-0003, filled when the adapter lands>"}
        with self.assertRaises(adapter.FixtureError):
            self.launch()

    def test_an_unsupported_settings_profile_fails_instead_of_being_ignored(self):
        self.fixtures["settings"] = {"profile": "history-one-favorite"}
        with self.assertRaises(adapter.FixtureError):
            self.launch()

    def test_an_unknown_fixture_kind_fails(self):
        with self.assertRaises(adapter.FixtureError):
            self.launch({**self.fixtures, "pack": {"path": "x"}})

    def test_a_binary_that_never_opens_the_hook_is_unavailable(self):
        (self.tmp / "quietdir").mkdir()
        quiet = self.tmp / "quietdir" / "quiet"
        quiet.write_text("#!/bin/sh\nexit 3\n")
        quiet.chmod(0o755)
        with self.assertRaises(adapter.Unavailable):
            adapter.MesenGuiAdapter(binary=str(quiet), workdir=self.tmp / "q", connect_timeout=2).launch(self.fixtures)


class HeadlessRunnerVerdict(unittest.TestCase):
    """run_headless_e2e.py reads the summary line: a skipped case must never read as a pass."""

    @staticmethod
    def line(failed, passed, skipped):
        word = "Passed" if not failed else "Failed"
        return f"{word}!  - Failed:     {failed}, Passed:     {passed}, Skipped:     {skipped}, Total:     {failed + passed + skipped}, Duration: 3 s"

    def test_exactly_one_passed_and_none_skipped_is_a_pass(self):
        self.assertEqual(runner.verdict(self.line(0, 1, 0)), [])

    def test_a_skipped_case_is_a_failure(self):
        self.assertTrue(runner.verdict(self.line(0, 0, 1)))

    def test_a_failed_case_is_a_failure(self):
        self.assertTrue(runner.verdict(self.line(1, 0, 0)))

    def test_a_filter_that_matches_nothing_is_a_failure(self):
        self.assertTrue(runner.verdict("No test matches the given testcase filter"))

    def test_the_case_name_matches_the_adapters_headless_case(self):
        self.assertEqual("GuiTestHookTests." + runner.CASE, adapter.HEADLESS_E2E_CASE)
        self.assertIn(runner.CASE, (ROOT / "UI.HeadlessTests" / "GuiTestHookTests.cs").read_text())


class HeadlessWiring(unittest.TestCase):
    def test_the_hook_e2e_case_is_still_in_ui_headless_tests(self):
        src = (ROOT / "UI.HeadlessTests" / "GuiTestHookTests.cs").read_text()
        self.assertIn("GuiTestHook_e2e_a_runner_drives_the_Home_over_the_socket", src)
        self.assertEqual(adapter.HEADLESS_E2E_CASE, "GuiTestHookTests.GuiTestHook_e2e_a_runner_drives_the_Home_over_the_socket")


if __name__ == "__main__":
    unittest.main()
