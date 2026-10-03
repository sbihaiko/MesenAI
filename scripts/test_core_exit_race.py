"""Process exit must not tear the native core down under a thread still using it.

Issue #621: `make headless-ui-tests` reported every test passed, then the test
host died with SIGSEGV (exit 139) during teardown. The macOS crash report of
that run shows the main thread inside exit() -> __cxa_finalize ->
~unique_ptr<Emulator> -> ~Emulator, while a .NET thread-pool thread (the
MainWindow update-check continuation) was in GetMesenVersion reading the
global `_emu` the destructor had just nulled. The test host never calls
EmuApi.Release(), so the C++ static destructors were the ones deleting the
process-global emulator - under every managed caller still in flight.

Each case below loads the real MesenCore through ctypes in a child process,
initializes it the way the headless tests do (InitDll + InitializeEmu with no
window), keeps one thread calling an export in a loop, and calls the C
library's exit() from the main thread. Before the fix every case exited 139;
the child must now exit 0. The library is a build product (`make core`); when
it is absent the file says so and exits 0. MESEN_CORE_LIB=<path> picks a
specific build (the mutation check runs the unfixed library through it).
"""

import os
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
NAMES = ("MesenCore.dylib", "MesenCore.so")
# Exports a host's background work really calls: the update check reads the
# version, MainWindow's 100 ms timer asks IsPaused, the overlay asks IsRunning.
EXPORTS = ("GetMesenVersion", "IsPaused", "IsRunning")
ROUNDS = 3

CHILD = r"""
import ctypes, sys, tempfile, threading, time
lib = ctypes.CDLL(sys.argv[1])
lib.InitDll()
lib.InitializeEmu(tempfile.mkdtemp().encode(), None, None, True, True, True, True)
export = getattr(lib, sys.argv[2])
export.restype = ctypes.c_uint32
running = threading.Event()
def hammer():
    running.set()
    while True:
        export()
threading.Thread(target=hammer, daemon=True).start()
running.wait()
time.sleep(0.05)
# The C exit(), not sys.exit(): it runs the library's static destructors while
# the hammer thread is inside the core, which is what the test host does.
ctypes.CDLL(None).exit(0)
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


def test_exit_while_a_thread_calls_into_the_core(library):
    for export in EXPORTS:
        codes = []
        for _ in range(ROUNDS):
            with tempfile.TemporaryDirectory() as cwd:
                proc = subprocess.run(
                    [sys.executable, "-c", CHILD, str(library), export],
                    cwd=cwd, capture_output=True, text=True, timeout=60,
                )
            codes.append(proc.returncode)
        check(codes == [0] * ROUNDS,
              f"exit() while a thread loops on {export} exits 0 ({ROUNDS} rounds)",
              f"exit codes {codes} (-11 = SIGSEGV in the static-destructor teardown, a shell's 139)")


def main():
    library = find_library()
    if library is None:
        print("SKIP: MesenCore is not built (make core) and MESEN_CORE_LIB names no file")
        return 0
    test_exit_while_a_thread_calls_into_the_core(library)
    print(f"\n{len(_CHECKS) - len(_FAILURES)}/{len(_CHECKS)} checks passed")
    return 1 if _FAILURES else 0


if __name__ == "__main__":
    sys.exit(main())
