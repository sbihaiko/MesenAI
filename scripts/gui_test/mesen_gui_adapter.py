#!/usr/bin/env python3
"""The `mesen-gui` target adapter of the GUI test squad (ADR-0272 item 6, #1183).

The runner, the script format and the dashboard live in the agent-squad fork
(item 6, P1-A); this repository owns the emulator side: this adapter and the
in-app hook it speaks to (UI/Logic/TestHook, a line-per-JSON-object protocol over
a local Unix socket). Pad only: the hook implements `pad.press` and the `ui.*`
snapshot, and capabilities() advertises exactly that.

Contract, per ADR-0272 item 6: launch() resolves fixtures first and FAILS on a
missing one, never skips; check() is an objective read of the hook's snapshot and
an unknown id is UnknownId, never False; wait() returns "met" or "timeout" and
counts the hook's ticks; teardown() reports what it could not restore."""
import hashlib
import json
import os
import re
import socket
import subprocess
import sys
import time
from pathlib import Path

HEADLESS_E2E_CASE = "GuiTestHookTests.GuiTestHook_e2e_a_runner_drives_the_Home_over_the_socket"
ACTIONS = ["pad.press"]
CHECKS = ["ui.screen", "ui.focused", "ui.visible", "ui.dialogs"]
PROFILES = ["fresh"]
FIXTURE_KINDS = {"settings", "rom", "files", "note"}
LISTENING = "test hook listening on"


class AdapterError(Exception):
    pass


class FixtureError(AdapterError):
    """A fixture the script needs is missing or cannot be placed: the run fails."""


class Unavailable(AdapterError):
    """The application did not open its hook."""


class UnknownId(AdapterError):
    """A check named an id the application does not have: failed (unknown id)."""


def _sha1_of_rom(path):
    data = Path(path).read_bytes()
    if data[:4] == b"NES\x1a":  # No-Intro hashes the NES ROM without its iNES header (ADR-0003)
        data = data[16:]
    return hashlib.sha1(data).hexdigest()


def resolve_fixtures(fixtures, workdir):
    """Place every fixture under workdir and return the HOME the app runs with."""
    unknown = sorted(set(fixtures) - FIXTURE_KINDS)
    if unknown:
        raise FixtureError("fixture kind not supported by mesen-gui: " + ", ".join(unknown))
    profile = (fixtures.get("settings") or {}).get("profile", "fresh")
    if profile not in PROFILES:
        raise FixtureError(f"settings profile {profile!r} is not supported by mesen-gui (supported: {', '.join(PROFILES)})")
    rom = fixtures.get("rom")
    if rom is not None:
        path = Path(rom.get("path", ""))
        if not path.is_file():
            raise FixtureError(f"rom fixture missing: {path}")
        want = str(rom.get("sha1", "")).lower()
        if not re.fullmatch(r"[0-9a-f]{40}", want):
            raise FixtureError(f"rom fixture has no usable No-Intro sha1: {rom.get('sha1')!r}")
        have = _sha1_of_rom(path)
        if have != want:
            raise FixtureError(f"rom fixture {path} sha1 {have} is not the expected {want}")
    home = Path(workdir) / "home"
    home.mkdir(parents=True, exist_ok=True)
    for item in fixtures.get("files") or []:
        src = Path(item.get("src", ""))
        if not src.is_file():
            raise FixtureError(f"file fixture missing: {src}")
        dest = home / item["dest"]
        dest.parent.mkdir(parents=True, exist_ok=True)
        dest.write_bytes(src.read_bytes())
    return home


class MesenGuiAdapter:
    def __init__(self, binary=None, workdir=None, connect_timeout=30):
        self.binary = binary or os.environ.get("MESEN_GUI_BINARY", "")
        self.workdir = Path(workdir) if workdir else None
        self.connect_timeout = connect_timeout

    def capabilities(self):
        return {"actions": list(ACTIONS), "checks": list(CHECKS), "input_families": ["pad"], "variants": {"window.mode": ["windowed"]}}

    def launch(self, fixtures):
        if sys.platform == "win32":
            raise Unavailable("the hook's Windows named pipe has no adapter transport yet")
        if not self.binary:
            raise Unavailable("no application binary: pass binary= or set MESEN_GUI_BINARY")
        workdir = self.workdir or Path(os.environ.get("TMPDIR", "/tmp")) / f"mesen-gui-{os.getpid()}"
        workdir.mkdir(parents=True, exist_ok=True)
        home = resolve_fixtures(fixtures, workdir)  # before anything starts
        endpoint = str(workdir / "hook.sock")
        if len(endpoint) > 100:
            raise Unavailable(f"socket path too long for a Unix socket: {endpoint}")
        token = os.urandom(8).hex()
        argv = [self.binary, "--test-hook=" + endpoint, "--test-hook-token=" + token]
        env = dict(os.environ, HOME=str(home))
        log = open(workdir / "app.log", "wb")
        proc = subprocess.Popen(argv, env=env, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
        session = Session(endpoint, token, proc, workdir, argv=argv, env=env, log=log)
        try:
            session.connect(self.connect_timeout)
        except BaseException:
            session.close()
            raise
        return session


class Session:
    def __init__(self, endpoint, token, process, workdir, argv=None, env=None, log=None):
        self.endpoint = endpoint
        self.token = token
        self.process = process
        self.workdir = Path(workdir)
        self.argv = argv or []
        self.env = env or {}
        self._log = log
        self._sock = None
        self._file = None
        self._id = 0
        if process is None:
            self.connect(5)

    def connect(self, timeout):
        deadline = time.monotonic() + timeout
        while True:
            if self.process is not None and self.process.poll() is not None:
                raise Unavailable(f"application exited ({self.process.returncode}) before opening its hook: {self.log_text()[-400:]}")
            sock = socket.socket(socket.AF_UNIX)
            try:
                sock.connect(self.endpoint)
                break
            except OSError:
                sock.close()
                if time.monotonic() > deadline:
                    raise Unavailable("the hook did not answer on " + self.endpoint)
                time.sleep(0.05)
        self._sock = sock
        self._file = sock.makefile("rw", encoding="utf-8", newline="\n")
        hello = self._ask("hello")
        if hello.get("hook") != 1:
            raise Unavailable(f"unsupported hook version {hello.get('hook')!r}")

    def log_text(self):
        try:
            return (self.workdir / "app.log").read_text(errors="replace")
        except OSError:
            return ""

    def _ask(self, op, **fields):
        self._id += 1
        self._file.write(json.dumps({"id": self._id, "token": self.token, "op": op, **fields}) + "\n")
        self._file.flush()
        line = self._file.readline()
        if not line:
            raise AdapterError("the hook closed the connection (process crash?)")
        answer = json.loads(line)
        if not answer.get("ok"):
            raise AdapterError(f"{op}: {answer.get('error')}")
        return answer

    def inject(self, action, args):
        if action not in ACTIONS:
            raise AdapterError(f"action {action!r} is not advertised by mesen-gui")
        self._ask("inject", action=action, args=args)

    def check(self, name, args):
        if name not in CHECKS:
            raise AdapterError(f"check {name!r} is not advertised by mesen-gui")
        state = self._ask("state")
        ids = {c["id"]: c for c in state.get("controls", [])}
        if name == "ui.dialogs":
            observed = state.get("dialogs", [])
            return {"passed": observed == args["is"], "observed": observed}
        if name == "ui.screen":
            observed = state.get("screen")
            return {"passed": observed == args["is"], "observed": observed}
        if name == "ui.focused":
            observed = state.get("focus")
            if "within" in args:
                want = args["within"]
                self._known(ids, want)
                return {"passed": observed is not None and (observed == want or observed.startswith(want + ".")), "observed": observed}
            self._known(ids, args["is"])
            return {"passed": observed == args["is"], "observed": observed}
        self._known(ids, args["is"])  # ui.visible
        return {"passed": bool(ids[args["is"]]["visible"]), "observed": ids[args["is"]]["visible"]}

    @staticmethod
    def _known(ids, ident):
        if ident not in ids:
            raise UnknownId(ident)

    def wait(self, condition, timeout_ticks):
        match = re.fullmatch(r"(ui\.screen|ui\.focused) == (\S+)", condition.strip())
        if not match:
            raise AdapterError(f"unsupported wait condition {condition!r}")
        key = "screen" if match.group(1) == "ui.screen" else "focus"
        start = None
        while True:
            state = self._ask("state")
            if state.get(key) == match.group(2):
                return "met"
            if start is None:
                start = state["tick"]
            if state["tick"] >= start + timeout_ticks:
                return "timeout"
            time.sleep(0.02)

    def capture(self, path):
        answer = self._ask("capture", path=str(path))
        data = Path(answer["path"]).read_bytes()
        if hashlib.sha256(data).hexdigest() != answer["sha256"]:
            raise AdapterError("capture sha256 does not match the file on disk")
        return {"path": answer["path"], "size": answer["size"], "sha256": answer["sha256"]}

    def teardown(self):
        """Quit the application; returns what could not be restored (a crash is reported, not raised)."""
        left = []
        try:
            if self._file is not None:
                self._ask("quit")
        except (AdapterError, OSError, ValueError) as ex:
            left.append("quit not acknowledged: " + str(ex))
        self.close()
        if self.process is not None and self.process.returncode not in (0, None):
            left.append(f"application exited {self.process.returncode}")
        return left

    def close(self):
        for closer in (lambda: self._file and self._file.close(), lambda: self._sock and self._sock.close()):
            try:
                closer()
            except OSError:
                pass
        self._file = self._sock = None
        if self.process is not None:
            try:
                self.process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                self.process.kill()
                self.process.wait()
        if self._log is not None:
            self._log.close()
            self._log = None
