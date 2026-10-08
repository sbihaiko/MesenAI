"""A load starts the game running; a reload keeps the player's pause.

Issue #783: a game opened while the emulator was paused inherited the Core's
pause flag, so it drew exactly one frame and then parked - a flat picture, the
Core's own pause icon, and no Player overlay, because the overlay is an Esc menu
rather than a reaction to the flag. Nothing on the load path cleared it.

Two halves of one rule, both asserted at the Core boundary:

- open   - park a running game, then LoadRom a different one. The new game must
           be unpaused AND must keep advancing frames. The frame check is the
           point: Emulator::Run draws a frame *before* it looks at the flag, and
           _pauseOnNextFrame re-sets it on the next one, so "not paused right
           now" is satisfied even by a game that is about to park.
- reload - park a running game, then ReloadRomKeepingState (ADR-0244's in-place
           pack change, which reaches LoadRom through ReloadRom). The game must
           still be paused. This is the half the fix could have broken, and it
           is what the Player's load card and ADR-0244 rely on.

And the third case, which is issue #787: the "run single frame" shortcut, the
other route that arms the same one-shot. Holding it makes the Core re-arm the
pause every 50 ms, and until ReleaseShortcut was exported nothing on the host
could stop that - so the route could not be driven from a test at all, and the
`_pauseOnNextFrame` half of the fix above went in unpinned.

Each case loads the real MesenCore through ctypes in a child process (InitDll +
InitializeEmu with no window, like the headless tests) and builds a synthetic
NROM (scripts/gen_synthetic_nrom.py, no game data). The two ROMs differ by one
byte in PRG space that is never executed - the game is a JMP-to-self - so they
are distinct files without being distinct behavior.

The library is a build product (`make core`); when absent the file says so and
exits 0. MESEN_CORE_LIB=<path> picks a specific build, which is how the
mutation check runs the unfixed library and requires the `open` case to fail.
"""

import ctypes
import os
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "scripts"))
from gen_synthetic_nrom import build_rom  # noqa: E402

NAMES = ("MesenCore.dylib", "MesenCore.so")
CHILD_TIMEOUT = 120
FRAMES = 10
SETTLE = 3.0
HOLD = 3.0


def enum_value(name):
    """The ordinal of `name` in `EmulatorShortcut` (Core/Shared/SettingTypes.h).

    Read rather than hard-coded: the value crosses the ABI as a struct field, and
    a reordered enum would otherwise have this test press a different shortcut
    while still passing.
    """
    header = (ROOT / "Core/Shared/SettingTypes.h").read_text()
    body = header.split("enum class EmulatorShortcut", 1)[1].split("};", 1)[0]
    entries = []
    for line in body.splitlines():
        line = line.split("//")[0].strip()
        if not line or line == "{":
            continue
        entries.append(line.rstrip(","))
    if name not in entries:
        raise SystemExit(f"EmulatorShortcut::{name} is not in Core/Shared/SettingTypes.h")
    return entries.index(name)


CHILD = r"""
import ctypes, os, sys, tempfile, time
lib = ctypes.CDLL(sys.argv[1])
rom_a = sys.argv[2].encode()
rom_b = sys.argv[3].encode()
mode = sys.argv[4]
frames = int(sys.argv[5])
settle = float(sys.argv[6])
hold = float(sys.argv[7])
run_single_frame = int(sys.argv[8])

class ExecuteShortcutParams(ctypes.Structure):
    _fields_ = [("Shortcut", ctypes.c_int), ("Param", ctypes.c_uint32), ("ParamPtr", ctypes.c_void_p)]

lib.InitDll()
lib.InitializeEmu(tempfile.mkdtemp().encode(), None, None, True, True, True, True)
lib.LoadRom.argtypes = [ctypes.c_char_p, ctypes.c_char_p]
lib.LoadRom.restype = ctypes.c_bool
lib.IsRunning.restype = ctypes.c_bool
lib.IsPaused.restype = ctypes.c_bool
lib.HeadlessGetFrameCount.restype = ctypes.c_uint32
lib.ReloadRomKeepingState.restype = ctypes.c_uint8
lib.ExecuteShortcut.argtypes = [ExecuteShortcutParams]

def count():
    return lib.HeadlessGetFrameCount()

def advance(n, start=None, window=None):
    start = count() if start is None else start
    end = time.monotonic() + (settle if window is None else window)
    while time.monotonic() < end:
        if count() > start + n:
            return True
        time.sleep(0.01)
    return False

def park():
    # A running game with its pause flag set - the state the reporter left behind.
    if not lib.LoadRom(rom_a, None) or not lib.IsRunning():
        return "the first game did not load"
    if not advance(5):
        return "the first game never ran"
    lib.Pause()
    end = time.monotonic() + settle
    while time.monotonic() < end:
        if lib.IsPaused():
            return None
        time.sleep(0.01)
    return "the core never reported the first game as paused"

def open_case():
    problem = park()
    if problem:
        return problem
    if not lib.LoadRom(rom_b, None):
        return "the second game did not load"
    start = count()
    if not advance(frames, start):
        return ("the opened game advanced %d frame(s) and stopped: the pause survived the load"
                % (count() - start))
    if lib.IsPaused():
        return "the opened game still reports as paused"
    return "runs"

def reload_case():
    problem = park()
    if problem:
        return problem
    result = lib.ReloadRomKeepingState()
    if result == 2:
        return "the in-place reload was refused"
    time.sleep(0.3)
    if not lib.IsPaused():
        return "the reload dropped the player's pause (result %d)" % result
    return "stays paused"

def shortcut_case():
    # Resolved here rather than at import: on a core without the export the other
    # two cases must still report for themselves instead of dying with this one.
    # "Did you mean: ExecuteShortcut?" is the whole of #787.
    lib.ReleaseShortcut.argtypes = [ExecuteShortcutParams]

    if not lib.LoadRom(rom_a, None) or not lib.IsRunning():
        return "the game did not load"
    if not advance(5):
        return "the game never ran"

    params = ExecuteShortcutParams()
    params.Shortcut = run_single_frame
    params.Param = 0
    params.ParamPtr = None

    # Held. _needRepeat re-arms the one-shot every 50 ms once the 500 ms delay is
    # past, so the game keeps drawing exactly one frame per repeat.
    lib.ExecuteShortcut(params)
    start = count()
    if not advance(2, start, hold):
        return ("a held run-single-frame stopped stepping after %d frame(s): nothing re-armed the pause"
                % (count() - start))

    # Released. Nothing may step the game after this - if the release is missing
    # or a no-op, _needRepeat keeps re-arming and the frames keep coming.
    lib.ReleaseShortcut(params)
    time.sleep(0.25)
    frozen = count()
    time.sleep(0.5)
    if count() != frozen:
        return ("the released shortcut kept stepping the game: %d -> %d frames"
                % (frozen, count()))
    return "released"

if mode == "open":
    print(open_case(), flush=True)
elif mode == "reload":
    print(reload_case(), flush=True)
else:
    print(shortcut_case(), flush=True)
os._exit(0)
"""

_FAILURES = []
_CHECKS = []


def check(cond, name, detail=""):
    _CHECKS.append(name)
    print(("ok   " if cond else "FAIL ") + name + ("" if cond else f"\n     {detail}"))
    if not cond:
        _FAILURES.append(name)


def find_library():
    from_env = os.environ.get("MESEN_CORE_LIB", "")
    if from_env:
        return Path(from_env) if Path(from_env).is_file() else None
    interop = ROOT / "InteropDLL"
    if not interop.is_dir():
        return None
    for folder in sorted(interop.glob("obj.*")):
        for name in NAMES:
            if (folder / name).is_file():
                return folder / name
    return None


def run_child(library, rom_a, rom_b, mode):
    with tempfile.TemporaryDirectory() as cwd:
        try:
            proc = subprocess.run(
                [sys.executable, "-c", CHILD, str(library), str(rom_a), str(rom_b),
                 mode, str(FRAMES), str(SETTLE), str(HOLD), str(enum_value("RunSingleFrame"))],
                cwd=cwd, capture_output=True, text=True, timeout=CHILD_TIMEOUT,
            )
        except subprocess.TimeoutExpired:
            return None, f"child did not finish within {CHILD_TIMEOUT} s"
    return proc.returncode, (proc.stdout + proc.stderr).strip()


def test_open_runs_and_keeps_running(library, rom_a, rom_b):
    code, out = run_child(library, rom_a, rom_b, "open")
    check(code == 0 and out.endswith("runs"),
          f"a game opened while paused runs, and keeps running past {FRAMES} frames",
          f"exit {code}: {out}")


def test_reload_keeps_the_pause(library, rom_a, rom_b):
    code, out = run_child(library, rom_a, rom_b, "reload")
    check(code == 0 and out.endswith("stays paused"),
          "a reload of a paused game keeps it paused (ADR-0244)",
          f"exit {code}: {out}")


def test_shortcut_release_stops_the_stepping(library, rom_a, rom_b):
    code, out = run_child(library, rom_a, rom_b, "shortcut")
    check(code == 0 and out.endswith("released"),
          "releasing the run-single-frame shortcut stops it stepping the game (#787)",
          f"exit {code}: {out}")


def main():
    library = find_library()
    if library is None:
        print("SKIP: MesenCore is not built (make core) and MESEN_CORE_LIB names no file")
        return 0
    with tempfile.TemporaryDirectory() as folder:
        rom_a = Path(folder) / "synthetic-a.nes"
        rom_b = Path(folder) / "synthetic-b.nes"
        rom_a.write_bytes(build_rom())
        # One byte of PRG, in the NOP run the game never reaches (it jumps to
        # itself at $8000), so the second ROM is a different file that behaves
        # identically.
        other = bytearray(build_rom())
        other[16 + 0x100] = 0x00
        rom_b.write_bytes(bytes(other))
        test_open_runs_and_keeps_running(library, rom_a, rom_b)
        test_reload_keeps_the_pause(library, rom_a, rom_b)
        test_shortcut_release_stops_the_stepping(library, rom_a, rom_b)
    print(f"\n{len(_CHECKS) - len(_FAILURES)}/{len(_CHECKS)} checks passed")
    return 1 if _FAILURES else 0


if __name__ == "__main__":
    sys.exit(main())
