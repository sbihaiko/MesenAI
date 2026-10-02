"""A Stop issued right after LoadRom must end the emulation thread.

Issue #636: full headless runs hung with the caller in Emulator::Stop ->
std::thread::join while the emulation thread kept running frames. LoadRom
clears `_stopFlag` and spawns the thread running Emulator::Run(); Run() used
to clear `_stopFlag` again once it had the run lock, so a Stop() landing
before that line (it sets the flag, then joins) was erased and join() never
returned.

Each case loads the real MesenCore through ctypes in a child process
(InitDll + InitializeEmu with no window, like the headless tests), builds a
synthetic NROM (scripts/gen_synthetic_nrom.py, no game data), and loops
LoadRom -> Stop. A watchdog thread in the child bounds every Stop: one that
has not returned within STOP_TIMEOUT seconds makes the child print `hung`
and exit 3, and the parent also bounds the whole child. A second case checks
the thread still runs frames after LoadRom and after a reload (LoadRom over a
running game, which stops and respawns the thread). The library is a build
product (`make core`); when absent the file says so and exits 0.
MESEN_CORE_LIB=<path> picks a specific build (the mutation check runs the
unfixed library through it).
"""

import os
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "scripts"))
from gen_synthetic_nrom import build_rom  # noqa: E402

NAMES = ("MesenCore.dylib", "MesenCore.so")
ITERATIONS = 200
STOP_TIMEOUT = 5.0
CHILD_TIMEOUT = 120

CHILD = r"""
import ctypes, os, sys, tempfile, threading, time
lib = ctypes.CDLL(sys.argv[1])
rom = sys.argv[2].encode()
mode = sys.argv[3]
iterations = int(sys.argv[4])
stop_timeout = float(sys.argv[5])
lib.InitDll()
lib.InitializeEmu(tempfile.mkdtemp().encode(), None, None, True, True, True, True)
lib.LoadRom.argtypes = [ctypes.c_char_p, ctypes.c_char_p]
lib.LoadRom.restype = ctypes.c_bool
lib.IsRunning.restype = ctypes.c_bool
lib.HeadlessGetFrameCount.restype = ctypes.c_uint32

state = {"i": -1, "deadline": None}
lock = threading.Lock()
def watchdog():
    while True:
        time.sleep(0.05)
        with lock:
            deadline = state["deadline"]
            i = state["i"]
        if deadline is not None and time.monotonic() > deadline:
            print(f"hung: Stop did not return within {stop_timeout} s at iteration {i}", flush=True)
            os._exit(3)
threading.Thread(target=watchdog, daemon=True).start()

def bounded_stop(i):
    with lock:
        state["i"] = i
        state["deadline"] = time.monotonic() + stop_timeout
    lib.Stop()
    with lock:
        state["deadline"] = None

def frames_advance():
    start = lib.HeadlessGetFrameCount()
    end = time.monotonic() + 3.0
    while time.monotonic() < end:
        if lib.HeadlessGetFrameCount() > start + 5:
            return True
        time.sleep(0.01)
    return False

if mode == "stop":
    for i in range(iterations):
        if not lib.LoadRom(rom, None):
            print(f"LoadRom failed at iteration {i}", flush=True)
            os._exit(2)
        bounded_stop(i)
    print(f"ok {iterations}", flush=True)
else:
    ok = lib.LoadRom(rom, None) and lib.IsRunning() and frames_advance()
    ok = ok and lib.LoadRom(rom, None) and lib.IsRunning() and frames_advance()
    bounded_stop(0)
    print("runs" if ok else "does not run", flush=True)
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


def run_child(library, rom, mode):
    with tempfile.TemporaryDirectory() as cwd:
        try:
            proc = subprocess.run(
                [sys.executable, "-c", CHILD, str(library), str(rom), mode,
                 str(ITERATIONS), str(STOP_TIMEOUT)],
                cwd=cwd, capture_output=True, text=True, timeout=CHILD_TIMEOUT,
            )
        except subprocess.TimeoutExpired:
            return None, f"child did not finish within {CHILD_TIMEOUT} s"
    return proc.returncode, (proc.stdout + proc.stderr).strip()


def test_stop_right_after_load_returns(library, rom):
    code, out = run_child(library, rom, "stop")
    check(code == 0 and out.endswith(f"ok {ITERATIONS}"),
          f"LoadRom -> Stop returns in each of {ITERATIONS} iterations",
          f"exit {code}: {out}")


def test_thread_still_runs_after_load_and_reload(library, rom):
    code, out = run_child(library, rom, "run")
    check(code == 0 and out.endswith("runs"),
          "frames advance after LoadRom and after a reload over a running game",
          f"exit {code}: {out}")


def main():
    library = find_library()
    if library is None:
        print("SKIP: MesenCore is not built (make core) and MESEN_CORE_LIB names no file")
        return 0
    with tempfile.TemporaryDirectory() as folder:
        rom = Path(folder) / "synthetic.nes"
        rom.write_bytes(build_rom())
        test_stop_right_after_load_returns(library, rom)
        test_thread_still_runs_after_load_and_reload(library, rom)
    print(f"\n{len(_CHECKS) - len(_FAILURES)}/{len(_CHECKS)} checks passed")
    return 1 if _FAILURES else 0


if __name__ == "__main__":
    sys.exit(main())
