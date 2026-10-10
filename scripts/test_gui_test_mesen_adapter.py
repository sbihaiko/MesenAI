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
import time
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
        self.frozen = False  # a stalled UI thread: the hook answers but the tick never moves
        self.unknown_presses = 0  # the first N presses answer like a key manager that is not up yet
        self.extra_controls = []  # controls appended after `known`, e.g. a hidden twin of an id
        #1255: where the run's windows are, and the display they must stay inside. The
        #env knobs let a launched stand-in app report a window the adapter must refuse.
        self.window_position = [int(os.environ.get("FAKE_WINDOW_X", 40)), int(os.environ.get("FAKE_WINDOW_Y", 60))]
        self.window_size = [1100, 700]
        self.primary_bounds = [0, 0, 1512, 982]
        self.primary_working_area = [0, 25, 1512, 945]
        self.windows = []  # dialogs and secondary windows opened during the run
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
            self.tick += 0 if self.frozen else 3
            out.update(
                screen="play.home", dialogs=[], focus=self.focus, tick=self.tick, frames=0,
                controls=[{"id": i, "enabled": True, "visible": True, "focused": i == self.focus} for i in self.known] + self.extra_controls,
                window={
                    "mode": "windowed", "size": self.window_size, "position": self.window_position, "maximized": False,
                    "primaryBounds": self.primary_bounds, "primaryWorkingArea": self.primary_working_area,
                },
                windows=[{"id": "main", "position": self.window_position, "size": self.window_size}] + self.windows,
            )
        elif op == "inject":
            if req["action"] != "pad.press":
                return {"id": req["id"], "ok": False, "error": "unknown action " + req["action"]}
            if self.unknown_presses > 0:
                self.unknown_presses -= 1
                return {"id": req["id"], "ok": False, "error": "unknown button " + req["args"]["button"] + " on pad 1"}
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

    def test_a_tick_only_wait_counts_ticks_and_is_met(self):
        before = self.hook.tick
        self.assertEqual(self.session.wait(None, 9), "met")
        self.assertGreaterEqual(self.hook.tick, before + 9)

    def test_wait_has_a_host_time_deadline_when_the_tick_never_advances(self):
        self.hook.frozen = True
        began = time.monotonic()
        self.assertEqual(self.session.wait("ui.focused == play.home.open-rom", 50, host_timeout=0.3), "timeout")
        self.assertLess(time.monotonic() - began, 5)

    def test_a_tick_only_wait_also_has_the_host_deadline(self):
        self.hook.frozen = True
        self.assertEqual(self.session.wait(None, 50, host_timeout=0.3), "timeout")

    def test_the_first_press_waits_for_the_key_manager_instead_of_failing(self):
        self.hook.unknown_presses = 2
        self.session.inject("pad.press", {"button": "Up", "ticks": 2})
        self.assertEqual(self.session.wait("ui.focused == play.home.open-rom", 120), "met")

    def test_a_button_that_stays_unknown_fails_after_the_grace_period(self):
        self.hook.unknown_presses = 10**6
        self.session.press_grace = 0.3
        with self.assertRaises(adapter.AdapterError):
            self.session.inject("pad.press", {"button": "Nope", "ticks": 2})

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


class WindowMode(unittest.TestCase):
    """#1255: a run's window opens on the primary (built-in) display unless the operator says otherwise."""

    def test_unset_value_declared_as_any_or_empty_are_the_primary_display_only(self):
        self.assertEqual(adapter.window_mode({}), "primary")
        self.assertEqual(adapter.window_mode({"MESEN_GUI_WINDOW": "primary"}), "primary")
        self.assertEqual(adapter.window_mode({"MESEN_GUI_WINDOW": ""}), "primary")

    def test_the_documented_escape_hatch_is_any(self):
        self.assertEqual(adapter.window_mode({"MESEN_GUI_WINDOW": "any"}), "any")

    def test_a_typo_is_an_error_not_the_permissive_mode(self):
        with self.assertRaises(adapter.AdapterError) as ctx:
            adapter.window_mode({"MESEN_GUI_WINDOW": "off"})
        self.assertIn("MESEN_GUI_WINDOW", str(ctx.exception))
        self.assertIn("off", str(ctx.exception))

    def test_the_module_docstring_documents_the_switch(self):
        self.assertIn("MESEN_GUI_WINDOW", adapter.__doc__)


class WindowPlacement(Base):
    """#1255: no window of the run may sit outside the primary display, and the step that opens one there fails."""

    def test_a_window_inside_the_primary_display_is_a_pass(self):
        self.assertTrue(self.session.check("ui.screen", {"is": "play.home"})["passed"])

    #An external monitor left of the built-in one: x is negative, so a window the OS
    #put there is outside the primary display's bounds.
    def test_the_main_window_on_an_external_display_fails_the_read(self):
        self.hook.window_position = [-1200, 60]
        with self.assertRaises(adapter.AdapterError) as ctx:
            self.session.check("ui.screen", {"is": "play.home"})
        self.assertIn("primary display", str(ctx.exception))
        self.assertIn("-1200", str(ctx.exception))

    def test_a_window_half_off_the_right_edge_fails_the_read(self):
        self.hook.window_position = [900, 60]  # 900 + 1100 > 1512
        with self.assertRaises(adapter.AdapterError):
            self.session.check("ui.screen", {"is": "play.home"})

    def test_a_dialog_opened_off_the_primary_display_fails_the_step(self):
        self.session.check("ui.screen", {"is": "play.home"})
        self.hook.windows.append({"id": "play.controller-sheet", "position": [1600, 100], "size": [800, 600]})
        with self.assertRaises(adapter.AdapterError) as ctx:
            self.session.wait("ui.screen == play.home", 8)
        self.assertIn("play.controller-sheet", str(ctx.exception))

    def test_a_dialog_that_stays_on_the_primary_display_does_not_fail_the_step(self):
        self.hook.windows.append({"id": "play.controller-sheet", "position": [200, 120], "size": [800, 600]})
        self.assertEqual(self.session.wait("ui.screen == play.home", 8), "met")

    def test_the_escape_hatch_lets_a_window_sit_anywhere(self):
        self.session.window_mode = "any"
        self.hook.window_position = [-1200, 60]
        self.assertTrue(self.session.check("ui.screen", {"is": "play.home"})["passed"])

    def test_a_hook_that_reports_no_primary_display_is_an_error_not_a_pass(self):
        self.hook.primary_bounds = None
        with self.assertRaises(adapter.AdapterError) as ctx:
            self.session.check("ui.screen", {"is": "play.home"})
        self.assertIn("primary", str(ctx.exception))


class WindowVariablePassThrough(unittest.TestCase):
    """#1255: the adapter reads MESEN_GUI_WINDOW itself, so a runner only has to not swallow it."""

    def test_the_runner_passes_the_variable_to_the_dotnet_process(self):
        calls = []
        real = runner.subprocess.Popen

        class Fake:
            def __init__(self, argv, **kwargs):
                calls.append(kwargs.get("env") or {})
                raise SystemExit  # the run never reaches a real dotnet

        os.environ["MESEN_GUI_WINDOW"] = "any"
        self.addCleanup(lambda: os.environ.pop("MESEN_GUI_WINDOW", None))
        runner.subprocess.Popen = Fake
        self.addCleanup(lambda: setattr(runner.subprocess, "Popen", real))
        with self.assertRaises(SystemExit):
            runner.run("osx-arm64", "/nonexistent/MesenCore.dylib")
        self.assertTrue(calls and calls[0].get("MESEN_GUI_WINDOW") == "any", calls)


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


# A fake app that models UI/Utilities/SingleInstance: a machine-wide lock that a second process hands its
# arguments to and then exits 0, unless the settings.json next to the executable turns Preferences.SingleInstance off.
SINGLE_INSTANCE_APP = FAKE_APP.replace("hook = t.FakeHook", textwrap.dedent("""\
    import fcntl, json, os
    settings = json.loads((Path(sys.argv[0]).resolve().parent / "settings.json").read_text())
    if settings.get("Preferences", dict()).get("SingleInstance", True):
        lock = open(os.environ["FAKE_MUTEX"], "w")
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except OSError:
            sys.exit(0)
    hook = t.FakeHook"""))


class ConcurrentLaunch(unittest.TestCase):
    """#1220: a test launch never depends on being the only emulator instance on the machine."""

    def test_two_hook_enabled_instances_both_open_their_hook(self):
        tmp = Path(tempfile.mkdtemp(prefix="g1220-", dir="/tmp"))
        self.addCleanup(lambda: __import__("shutil").rmtree(tmp, True))
        (tmp / "appdir").mkdir()
        app = tmp / "appdir" / "fake-app"
        app.write_text(SINGLE_INSTANCE_APP.format(python=sys.executable, tests=str(ROOT / "scripts")))
        app.chmod(app.stat().st_mode | stat.S_IXUSR)
        old = os.environ.get("FAKE_MUTEX")
        os.environ["FAKE_MUTEX"] = str(tmp / "mutex")
        self.addCleanup(lambda: os.environ.pop("FAKE_MUTEX") if old is None else os.environ.__setitem__("FAKE_MUTEX", old))
        sessions = []
        try:
            for name in ("a", "b"):
                sessions.append(adapter.MesenGuiAdapter(binary=str(app), workdir=tmp / name, connect_timeout=10).launch({}))
            self.assertEqual(len(sessions), 2)
        finally:
            for session in sessions:
                session.teardown()


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
            settings = json.loads((clone.parent / "settings.json").read_text())
            self.assertEqual(settings["Preferences"], {"UiMode": "Player", "SingleInstance": False})
            #1255: every launch pins the main window, so the OS cannot open the run
            #on an external monitor before the hook can say where it landed.
            self.assertEqual(settings["MainWindow"], {
                "WindowSize": {"Width": 1100, "Height": 700},
                "WindowLocation": {"X": 40, "Y": 60},
                "WindowIsMaximized": False,
            })
            self.assertTrue(session.log_text().startswith("test hook listening on"))
        finally:
            session.teardown()

    def env(self, **values):
        old = {k: os.environ.get(k) for k in values}
        os.environ.update(values)

        def restore():
            for key, value in old.items():
                os.environ.pop(key, None) if value is None else os.environ.__setitem__(key, value)

        self.addCleanup(restore)

    #1255: the adapter reads the variable itself, so it only has to reach the launched process unchanged.
    def test_the_window_variable_reaches_the_launched_process_unchanged(self):
        self.env(MESEN_GUI_WINDOW="any")
        session = self.launch()
        try:
            self.assertEqual(session.env.get("MESEN_GUI_WINDOW"), "any")
        finally:
            session.teardown()

    def test_a_window_off_the_primary_display_fails_the_launch(self):
        self.env(FAKE_WINDOW_X="-1200")
        with self.assertRaises(adapter.AdapterError) as ctx:
            self.launch()
        self.assertIn("primary display", str(ctx.exception))

    def test_the_escape_hatch_lets_a_run_on_another_monitor_start(self):
        self.env(MESEN_GUI_WINDOW="any", FAKE_WINDOW_X="-1200")
        session = self.launch()
        try:
            self.assertTrue(session.check("ui.screen", {"is": "play.home"})["passed"])
        finally:
            self.assertEqual(session.teardown(), [])

    def test_an_invalid_window_value_fails_the_launch_before_anything_starts(self):
        self.env(MESEN_GUI_WINDOW="off")
        with self.assertRaises(adapter.AdapterError) as ctx:
            self.launch()
        self.assertIn("MESEN_GUI_WINDOW", str(ctx.exception))
        self.assertFalse((self.tmp / "run" / "app").exists(), "the app was cloned before the switch was read")
        self.assertFalse((self.tmp / "run" / "hook.sock").exists())

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


class HeadlessRunnerRealCall(unittest.TestCase):
    """run() really spawns `dotnet`: a stand-in on PATH proves the exit code, the timeout and the skip rule end to end."""

    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp(prefix="g1183r-", dir="/tmp"))
        self.addCleanup(lambda: __import__("shutil").rmtree(self.tmp, True))
        self.lib = self.tmp / "core.lib"
        self.lib.write_bytes(b"x")
        old = dict(os.environ)
        self.addCleanup(lambda: (os.environ.clear(), os.environ.update(old)))
        os.environ["PATH"] = str(self.tmp) + os.pathsep + os.environ["PATH"]
        os.environ["MESEN_CORE_LIB"] = str(self.lib)

    def dotnet(self, body, code=0):
        exe = self.tmp / "dotnet"
        exe.write_text("#!/bin/sh\n" + body + f"\nexit {code}\n")
        exe.chmod(0o755)

    def summary(self, failed, passed, skipped):
        return HeadlessRunnerVerdict.line(failed, passed, skipped)

    def test_a_passing_summary_exits_zero(self):
        self.dotnet(f"echo '{self.summary(0, 1, 0)}'")
        self.assertEqual(runner.main(["x"]), 0)

    def test_a_passing_summary_with_a_failing_exit_code_fails(self):
        self.dotnet(f"echo '{self.summary(0, 1, 0)}'", code=1)
        self.assertEqual(runner.main(["x"]), 1)

    def test_a_skipped_case_fails_the_call(self):
        self.dotnet(f"echo '{self.summary(0, 0, 1)}'")
        self.assertEqual(runner.main(["x"]), 1)

    def test_a_hung_dotnet_is_killed_and_fails(self):
        self.dotnet("sleep 30")
        runner.TIMEOUT_SECONDS, old = 0.5, runner.TIMEOUT_SECONDS
        self.addCleanup(lambda: setattr(runner, "TIMEOUT_SECONDS", old))
        began = time.monotonic()
        self.assertEqual(runner.main(["x"]), 1)
        self.assertLess(time.monotonic() - began, 10)

    def test_a_hung_grandchild_is_killed_with_the_process_group(self):
        pidfile = self.tmp / "grandchild.pid"
        self.dotnet(f"sleep 30 &\necho $! > {pidfile}\nwait")
        runner.TIMEOUT_SECONDS, old = 0.5, runner.TIMEOUT_SECONDS
        self.addCleanup(lambda: setattr(runner, "TIMEOUT_SECONDS", old))
        began = time.monotonic()
        self.assertEqual(runner.main(["x"]), 1)
        self.assertLess(time.monotonic() - began, 10)
        pid = int(pidfile.read_text())
        for _ in range(50):
            try:
                os.kill(pid, 0)
            except ProcessLookupError:
                return
            time.sleep(0.1)
        os.kill(pid, 9)
        self.fail("the grandchild outlived the timeout")

    def test_without_the_core_library_the_call_fails(self):
        del os.environ["MESEN_CORE_LIB"]
        self.assertEqual(runner.main(["x"]), 2)


class HeadlessWiring(unittest.TestCase):
    def test_the_hook_e2e_case_is_still_in_ui_headless_tests(self):
        src = (ROOT / "UI.HeadlessTests" / "GuiTestHookTests.cs").read_text()
        self.assertIn("GuiTestHook_e2e_a_runner_drives_the_Home_over_the_socket", src)
        self.assertEqual(adapter.HEADLESS_E2E_CASE, "GuiTestHookTests.GuiTestHook_e2e_a_runner_drives_the_Home_over_the_socket")

    def test_ci_runs_the_real_case_through_the_runner_against_the_built_core(self):
        """Grepping the name proves nothing (a skipped case is green): a CI job that builds the core must call the runner."""
        wf = (ROOT / ".github" / "workflows" / "render-gate.yml").read_text()
        step = wf.split("run_headless_e2e.py")
        self.assertGreater(len(step), 1, "no CI step runs scripts/gui_test/run_headless_e2e.py")
        self.assertIn("MESEN_CORE_LIB: ${{ github.workspace }}/InteropDLL/obj.linux-x64/MesenCore.so", step[0].rsplit("- name:", 1)[1])
        self.assertIn("linux-x64", step[1].split("\n", 1)[0])
        self.assertIn("'scripts/gui_test/**'", wf.split("jobs:")[0], "a runner change must trigger the gate")


if __name__ == "__main__":
    unittest.main()
