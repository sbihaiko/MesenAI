#!/usr/bin/env python3
"""#1255: a GUI test run does not take the front from whoever is using the machine.

The blocking acceptance criterion is about the machine's frontmost application,
not about the code that is supposed to keep it there: `ShowActivated = false`
and the accessory activation policy are the mechanism, and a test that stubs the
policy cannot tell whether AppKit honours it, or whether the process ever asked.
This module reads the real answer - `NSWorkspace.frontmostApplication` - and
compares it before and after a real run.

Two halves:

- `FrontmostReader` (always runs): the reader names the application in front,
  and refuses a platform that has no such notion instead of guessing.
- `RealRunKeepsTheFront` (opt-in, macOS only): launches the built application
  through the real adapter, twice, and asserts the application that was in front
  is still in front after each launch and after each driven step.

The opt-in switch is `MESEN_GUI_FOCUS_E2E=1`; the application is the one
`MESEN_GUI_BINARY` names, the same variable the adapter reads. Without the
switch the second half skips - it needs a built application, which the unit-test
run does not produce - and the criterion it covers is stated, not implied.
"""
import os
import shutil
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts" / "gui_test"))

import macos_frontmost as frontmost  # noqa: E402
import mesen_gui_adapter as adapter  # noqa: E402

OPT_IN = "MESEN_GUI_FOCUS_E2E"
BINARY = "MESEN_GUI_BINARY"
FRESH = {"settings": {"profile": "fresh"}}


class FrontmostReader(unittest.TestCase):
    """The instrument itself: a real answer, or an honest refusal."""

    def test_a_read_names_the_application_that_is_in_front(self):
        if sys.platform != "darwin":
            self.skipTest("the frontmost application is a macOS notion")
        first = frontmost.frontmost_identifier()
        self.assertIsInstance(first, str)
        self.assertTrue(first, "an application is in front, so the reader has a name for it")
        # Two reads a moment apart agree: the reader is not answering a fresh
        # object every time, and nothing about it is random.
        self.assertEqual(frontmost.frontmost_identifier(), first)

    def test_a_platform_with_no_frontmost_application_is_refused_never_guessed(self):
        # The guard is the platform, so it is exercised on every platform by
        # moving the platform: `MESEN_GUI_WINDOW=any` runs (Linux, CI) must fail
        # this check loudly rather than pass it with an invented name.
        with mock.patch.object(frontmost.sys, "platform", "linux"), mock.patch.object(frontmost, "_handle", None):
            with self.assertRaises(frontmost.Unsupported) as ctx:
                frontmost.frontmost_identifier()
            self.assertIn("linux", str(ctx.exception))


class AppKitHonoursThePolicyTheRunAsksFor(unittest.TestCase):
    """The call the application makes, made for real.

    The accessory policy is the mechanism the whole criterion rests on, and a
    stubbed policy cannot tell a correct P/Invoke from a wrong one: a wrong
    selector or a wrong argument width leaves the policy where it was and every
    test still passes. This asks AppKit itself, on a process whose application
    has already finished launching - the state the emulator sets it in."""

    @unittest.skipUnless(sys.platform == "darwin", "an activation policy is a macOS notion")
    def test_appkit_keeps_the_accessory_policy_set_after_the_application_launched(self):
        self.assertNotEqual(frontmost.activation_policy(), frontmost.ACCESSORY,
                            "a process starts as Regular: there is something to change")
        self.assertEqual(frontmost.set_activation_policy(frontmost.ACCESSORY), frontmost.ACCESSORY,
                         "AppKit did not keep the accessory policy the run asks for")
        self.assertEqual(frontmost.activation_policy(), frontmost.ACCESSORY)
        # Deliberately not put back: AppKit answers NO to leaving the accessory
        # policy (observed, not assumed - `set_activation_policy(REGULAR)` raises
        # Unsupported), which is why the run asks for it once, before its first
        # window, and never asks again. This process is the test's own.


class RealRunKeepsTheFront(unittest.TestCase):
    """A real launch, a real window, and the real frontmost application."""

    def setUp(self):
        if os.environ.get(OPT_IN) != "1":
            self.skipTest(f"opt-in: set {OPT_IN}=1 (and {BINARY}=<built application>) to run the real-binary focus check")
        if sys.platform != "darwin":
            self.skipTest("the frontmost application is a macOS notion")
        self.binary = os.environ.get(BINARY, "")
        if not self.binary:
            self.fail(f"{OPT_IN}=1 needs {BINARY} to name the built application")
        if not Path(self.binary).is_file():
            self.fail(f"{BINARY}={self.binary} is not a file")
        self.tmp = Path(tempfile.mkdtemp(prefix="focus1255-", dir="/tmp"))
        self.addCleanup(lambda: shutil.rmtree(self.tmp, True))

    def launch(self, name):
        return adapter.MesenGuiAdapter(binary=self.binary, workdir=self.tmp / name, connect_timeout=120).launch(FRESH)

    def assert_front_kept(self, before, when):
        self.assertEqual(frontmost.frontmost_identifier(), before,
                         f"{when} took the front from {before!r}")

    def test_a_run_never_takes_the_front_from_the_application_that_is_in_front(self):
        before = frontmost.frontmost_identifier()
        session = self.launch("first")
        try:
            # The launch is the step that opens the window this run lives in.
            self.assert_front_kept(before, "the launch")
            self.assertEqual(session.wait("ui.screen == play.home", 900), "met")
            self.assert_front_kept(before, "waiting for the home")
            # The launch returned, so the adapter read the run's window off the
            # hook and found it on the primary display - a run that opened no
            # window at all is refused there.
            session.inject("pad.press", {"button": "Up", "ticks": 4})
            # `wait` answers "timeout" rather than raising, so an unchecked wait
            # would let this test pass on a run whose driven step never landed -
            # and the focus claim for a driven step needs the step to have
            # happened.
            self.assertEqual(session.wait("ui.focused == play.home.open-rom", 300), "met",
                             "the driven step did not reach the focused state")
            self.assert_front_kept(before, "a driven step")
        finally:
            self.assertEqual(session.teardown(), [])

        # A second run opens a window of its own while the first one is gone: the
        # window coming up must not take the front either, which is the same claim
        # for a window opened later than the launch.
        second = self.launch("second")
        try:
            self.assert_front_kept(before, "a second launch")
            self.assertEqual(second.wait("ui.screen == play.home", 900), "met")
            self.assert_front_kept(before, "the second run's home")
        finally:
            self.assertEqual(second.teardown(), [])


if __name__ == "__main__":
    unittest.main()
