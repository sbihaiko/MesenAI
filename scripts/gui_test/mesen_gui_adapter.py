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
counts the hook's ticks; teardown() reports what it could not restore.

Window placement (#1255): a run's window belongs on the PRIMARY display - the
built-in one on a laptop - never on an external monitor, and a run never takes
the keyboard focus from whoever is using the machine. Every launch writes a
`MainWindow` entry (1100x700 at 40,60, not maximized) into the clone's
settings.json beside `Preferences`, the application opens its windows without
activating, and after the hook connects this adapter reads the window's own
frame from the hook's state (`window` and `windows`) and FAILS the launch while
any of them sits outside the primary display's working area; a step that opens a
window off the primary display fails its own state read the same way.
The one escape hatch is the environment variable `MESEN_GUI_WINDOW`: the exact
value `any` means "do not enforce placement", for a CI, Linux or headless runner
where there is no primary-display notion. This adapter reads it, and so does the
application; unset (or `primary`) means primary-display placement, and any other
value is an error rather than a silent fallback on both sides - this adapter
refuses it before a clone, a process or a socket exists, and a run started
without the adapter has it refused at hook startup (TestHookWiring.Start, exit
2). Whatever starts a run only has to pass the variable through unchanged."""
import hashlib
import json
import os
import re
import shutil
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
ASK_TIMEOUT = 30  # seconds a single hook answer may take before the step fails (ADR-0272 item 4: no wait may hang the run)
WAIT_HOST_TIMEOUT = 60  # host-time watchdog of a wait(), whatever the tick counter does
PRESS_GRACE = 10  # the key manager registers a moment after the window opens: a first press may see "unknown button"
WINDOW_ENV = "MESEN_GUI_WINDOW"
WINDOW_MODES = ("primary", "any")
# The window every launch pins into the clone's settings.json (MainWindowConfig, UI/Config/BaseWindowConfig.cs).
MAIN_WINDOW_SIZE = {"Width": 1100, "Height": 700}
MAIN_WINDOW_LOCATION = {"X": 40, "Y": 60}


class AdapterError(Exception):
    pass


class FixtureError(AdapterError):
    """A fixture the script needs is missing or cannot be placed: the run fails."""


class Unavailable(AdapterError):
    """The application did not open its hook."""


class UnknownId(AdapterError):
    """A check named an id the application does not have: failed (unknown id)."""


def window_mode(env=None):
    """The window mode of a run: "primary" (the default) or "any".

    Unset and the explicit "primary" both mean primary-display placement. "any"
    is the documented escape hatch for a runner with no primary display. Anything
    else is an error, never a silent fallback: a typo must not read as the
    permissive mode, and a run started without this adapter has the same value
    refused by the application at hook startup (TestHookWiring.Start)."""
    value = (os.environ if env is None else env).get(WINDOW_ENV)
    if value in (None, "", "primary"):
        return "primary"
    if value == "any":
        return "any"
    raise AdapterError(f"{WINDOW_ENV}={value!r} is not a window mode (expected one of: {', '.join(WINDOW_MODES)})")


def contained_in(area, rect):
    """True when rect [x, y, w, h] sits entirely inside area [x, y, w, h]."""
    return (rect[0] >= area[0] and rect[1] >= area[1]
            and rect[0] + rect[2] <= area[0] + area[2]
            and rect[1] + rect[3] <= area[1] + area[3])


def _sha1_of_rom(path):
    data = Path(path).read_bytes()
    if data[:4] == b"NES\x1a":  # No-Intro hashes the NES ROM without its iNES header (ADR-0003)
        data = data[16:]
    return hashlib.sha1(data).hexdigest()


def _clone_app_folder(binary, dest):
    """Clone the app folder (copy-on-write where the filesystem has it) and return the cloned binary."""
    src = Path(binary).resolve().parent
    if dest.exists():
        shutil.rmtree(dest)
    if sys.platform == "darwin" and subprocess.run(["cp", "-c", "-R", str(src), str(dest)], capture_output=True).returncode == 0:
        pass
    else:
        if dest.exists():
            shutil.rmtree(dest)
        shutil.copytree(src, dest, symlinks=True)
    return dest / Path(binary).name


def resolve_fixtures(fixtures, workdir, binary):
    """Place every fixture and return the cloned binary the app runs from.

    HOME does not isolate the app (.NET resolves ApplicationData natively on
    macOS); the one override is a settings.json next to the executable
    (ConfigManager.DefaultPortableFolder), so the session runs from a clone of
    the app folder seeded with a fresh one, and `files` land in that folder. A `rom`
    lands in `<clone>/library/`, which the seeded settings.json names as LibraryFolders."""
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
    launched = _clone_app_folder(binary, Path(workdir) / "app")
    home = launched.parent
    # A settings.json that exists without a UiMode key is the upgrade path (Advanced), which leaves Play Home; a
    # missing file is the fresh-unzip path (Player) but the clone must carry one, so the key is written.
    # LibraryFolders stays absent without a rom, so the first run starts at Home.
    # SingleInstance (PreferencesConfig.SingleInstance, default true) takes a machine-wide mutex: beside any other Mesen a
    # launch would hand its arguments over and exit 0 before the hook comes up (#1220), so a test launch turns it off.
    # MainWindow (MainWindowConfig -> BaseWindowConfig) pins the run's window at a
    # known size and position before the application opens it (#1255): a window the
    # window manager would otherwise place on an external monitor starts at 40,60,
    # whose whole 1100x700 is inside a laptop's working area.
    settings = {
        "Preferences": {"UiMode": "Player", "SingleInstance": False},
        "MainWindow": {
            "WindowSize": dict(MAIN_WINDOW_SIZE),
            "WindowLocation": dict(MAIN_WINDOW_LOCATION),
            "WindowIsMaximized": False,
        },
    }
    if rom is not None:
        # Only through the profile, never argv (ADR-0272 item 6): the ROM is a library tile.
        library = home / "library"
        library.mkdir(parents=True, exist_ok=True)
        (library / path.name).write_bytes(path.read_bytes())
        settings["Preferences"]["LibraryFolders"] = [str(library)]  # PreferencesConfig.LibraryFolders, PascalCase keys
    (home / "settings.json").write_text(json.dumps(settings, indent=2) + "\n")
    for item in fixtures.get("files") or []:
        src = Path(item.get("src", ""))
        if not src.is_file():
            raise FixtureError(f"file fixture missing: {src}")
        rel = item.get("dest")
        if not rel or Path(rel).is_absolute() or ".." in Path(rel).parts:
            raise FixtureError(f"file fixture destination must be a relative path inside the app home: {rel!r}")
        dest = home / rel
        dest.parent.mkdir(parents=True, exist_ok=True)
        dest.write_bytes(src.read_bytes())
    return launched


class MesenGuiAdapter:
    def __init__(self, binary=None, workdir=None, connect_timeout=30, out_dir=None):
        self.binary = binary or os.environ.get("MESEN_GUI_BINARY", "")
        # out_dir: the fork's supervised worker names the run's output folder; it is the workdir unless one is given.
        self.workdir = Path(workdir) if workdir else (Path(out_dir) if out_dir else None)
        self.out_dir = Path(out_dir) if out_dir else None
        self.connect_timeout = connect_timeout

    def capabilities(self):
        return {"actions": list(ACTIONS), "checks": list(CHECKS), "input_families": ["pad"], "variants": {"window.mode": ["windowed"]}}

    def launch(self, fixtures):
        mode = window_mode()  # a bad value fails before a clone, a process or a socket exists
        if sys.platform == "win32":
            raise Unavailable("the hook's Windows named pipe has no adapter transport yet")
        if not self.binary:
            raise Unavailable("no application binary: pass binary= or set MESEN_GUI_BINARY")
        workdir = self.workdir or Path(os.environ.get("TMPDIR", "/tmp")) / f"mesen-gui-{os.getpid()}"
        workdir.mkdir(parents=True, exist_ok=True)
        binary = resolve_fixtures(fixtures, workdir, self.binary)  # before anything starts
        endpoint = str(workdir / "hook.sock")
        if len(endpoint) > 100:
            raise Unavailable(f"socket path too long for a Unix socket: {endpoint}")
        token = os.urandom(8).hex()
        argv = [str(binary), "--test-hook=" + endpoint, "--test-hook-token=" + token]
        env = dict(os.environ)
        log = open(workdir / "app.log", "wb")
        proc = subprocess.Popen(argv, env=env, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
        session = Session(endpoint, token, proc, workdir, argv=argv, env=env, log=log, window_mode=mode)
        try:
            session.connect(self.connect_timeout)
            session.require_placement()
        except BaseException:
            session.close()
            raise
        return session


class Session:
    def __init__(self, endpoint, token, process, workdir, argv=None, env=None, log=None, window_mode="primary"):
        self.endpoint = endpoint
        self.window_mode = window_mode
        self.token = token
        self.process = process
        self.workdir = Path(workdir)
        self.argv = argv or []
        self.env = env or {}
        self._log = log
        self._sock = None
        self._file = None
        self._id = 0
        self.press_grace = PRESS_GRACE
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
        sock.settimeout(ASK_TIMEOUT)
        self._sock = sock
        self._file = sock.makefile("rw", encoding="utf-8", newline="\n")
        hello = self._ask("hello")
        if hello.get("hook") != 1:
            raise Unavailable(f"unsupported hook version {hello.get('hook')!r}")

    def require_placement(self):
        """Fail the launch when an application window's frame is not fully inside the primary display's working area (#1255)."""
        self._refuse_off_primary(self._ask("state"))

    def placement_error(self, state):
        """The message for the first window outside the primary display, or None.

        The application opens its windows without activating and pins the main one
        at 40,60, but the window manager still decides where a window lands when
        the run is not the one it thinks about; this is where a run on an external
        monitor is refused instead of being measured. A hook that reports no
        primary display at all (a runner where the notion does not apply) is an
        error rather than a pass: silence must not read as "inside".

        Two measurements, both taken from the hook rather than assumed here:

        - The area is the primary display's WORKING area - the display without the
          menu bar - which is what the application places a window inside
          (UI/Windows/TestHookWiring.KeepOnPrimaryDisplay). Checking against the
          full bounds would be looser than the placement it polices: a window the
          application itself would have pulled back down would read as "inside".
          The bounds are the fallback for a hook that reports no working area.
        - The rectangle is the window's FRAME - title bar and borders included -
          which is what the window manager puts on the display and what has to fit
          on it. The position and size the hook also reports are the client area,
          and they are the fallback for a platform that reports no frame."""
        if self.window_mode != "primary":
            return None
        window = state.get("window") or {}
        area = window.get("primaryWorkingArea") or window.get("primaryBounds")
        if not area:
            return (f"the hook reported no primary display (state.window.primaryWorkingArea / primaryBounds): "
                    f"set {WINDOW_ENV}=any on a runner where no primary display applies")
        entries = state.get("windows") or [{"id": "main", "position": window.get("position"), "size": window.get("size"),
                                            "frame": window.get("frame")}]
        for entry in entries:
            rect = entry.get("frame")
            if rect is None:
                position, size = entry.get("position"), entry.get("size")
                if position is None or size is None:
                    return f"the hook reported no position/size for window {entry.get('id')!r} (state.windows)"
                rect = [position[0], position[1], size[0], size[1]]
            if not contained_in(area, rect):
                return (f"window {entry.get('id')!r} at ({rect[0]},{rect[1]}) size {rect[2]}x{rect[3]} is not fully inside "
                        f"the primary display {area}: a GUI test run opens its windows on the primary (built-in) display "
                        f"only; set {WINDOW_ENV}=any for a runner where no primary display applies")
        return None

    def _refuse_off_primary(self, state):
        error = self.placement_error(state)
        if error is not None:
            raise AdapterError(error)

    def log_text(self):
        try:
            return (self.workdir / "app.log").read_text(errors="replace")
        except OSError:
            return ""

    def _ask(self, op, **fields):
        self._id += 1
        self._file.write(json.dumps({"id": self._id, "token": self.token, "op": op, **fields}) + "\n")
        self._file.flush()
        try:
            line = self._file.readline()
        except (TimeoutError, socket.timeout):
            raise AdapterError(f"{op}: the hook did not answer within {ASK_TIMEOUT}s")
        if not line:
            raise AdapterError("the hook closed the connection (process crash?)")
        answer = json.loads(line)
        if not answer.get("ok"):
            raise AdapterError(f"{op}: {answer.get('error')}")
        return answer

    def inject(self, action, args):
        if action not in ACTIONS:
            raise AdapterError(f"action {action!r} is not advertised by mesen-gui")
        deadline = time.monotonic() + self.press_grace
        while True:
            try:
                self._ask("inject", action=action, args=args)
                return
            except AdapterError as ex:
                # Right after launch the backend's key lookup answers nothing yet; that is not a wrong button.
                if "unknown button" not in str(ex) or time.monotonic() > deadline:
                    raise
                time.sleep(0.1)

    def check(self, name, args):
        if name not in CHECKS:
            raise AdapterError(f"check {name!r} is not advertised by mesen-gui")
        state = self._ask("state")
        self._refuse_off_primary(state)  # a dialog opened off the primary display fails the step that opened it
        ids = {}
        for c in state.get("controls", []):  # an id can sit on several controls (first-run vs recents Open ROM): any visible copy counts
            seen = ids.get(c["id"])
            ids[c["id"]] = c if seen is None or (c["visible"] and not seen["visible"]) else seen
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

    def wait(self, condition, timeout_ticks, host_timeout=WAIT_HOST_TIMEOUT):
        """condition None is a tick-only wait: "met" once timeout_ticks have passed. Either way a frozen
        tick counter cannot hang the run: host_timeout seconds end the wait as "timeout"."""
        key = want = None
        if condition is not None:
            match = re.fullmatch(r"(ui\.screen|ui\.focused) == (\S+)", condition.strip())
            if not match:
                raise AdapterError(f"unsupported wait condition {condition!r}")
            key = "screen" if match.group(1) == "ui.screen" else "focus"
            want = match.group(2)
        deadline = time.monotonic() + host_timeout
        start = None
        while True:
            state = self._ask("state")
            self._refuse_off_primary(state)
            if key is not None and state.get(key) == want:
                return "met"
            if start is None:
                start = state["tick"]
            if state["tick"] >= start + timeout_ticks:
                return "met" if key is None else "timeout"
            if time.monotonic() > deadline:
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
