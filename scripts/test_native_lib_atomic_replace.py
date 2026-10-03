"""The build must replace the native core with a new inode, never in place.

Issue #628: on macOS arm64 `make ui` copied InteropDLL/obj.<rid>/MesenCore.dylib
onto bin/<rid>/Release/MesenCore.dylib with a plain `cp`, which rewrites the
existing file (same inode). When a process still had the old image mapped (a
test host, a running emulator, a build node), the kernel keeps the code
signature it validated for that vnode; every later process that loads the
rewritten file is SIGKILLed (exit 137) on the first page it touches, even after
the holder exits and although `codesign -v` reports the file as valid. The next
`make headless-ui-tests` died before its first test. Writing to a temp file in
the same directory and renaming it over the destination gives a fresh vnode and
loads fine.

Cases:
  1. scripts/replace_file_atomic.sh replaces the destination with a new inode,
     same bytes, no temp file left behind, works when the destination does not
     exist yet, and fails without touching anything when the source is missing.
  2. Every place the build drops a native library onto an existing path (the
     makefile `ui` recipe, scripts/build_app_macos.sh, scripts/release_macos.sh)
     goes through that helper rather than a bare `cp`.
  3. macOS only (needs cc): a dylib mapped by a live process and replaced
     through the helper still loads in a new process. The plain-`cp` control is
     printed, not asserted, since it depends on the kernel's behavior.
"""
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
HELPER = ROOT / "scripts" / "replace_file_atomic.sh"

_results = []


def check(cond, name, detail=""):
    """Record a condition's truth value and print its result with failure details."""
    _results.append(bool(cond))
    print(("PASS " if cond else "FAIL ") + name + ("" if cond else f"  {detail}"))


def run_helper(src, dst):
    """Run the replacement helper and return its result with captured text output."""
    return subprocess.run(["bash", str(HELPER), str(src), str(dst)],
                          capture_output=True, text=True)


def test_helper_behavior():
    """Verify new-inode replacement, creation, cleanup, and missing-source safety."""
    if not HELPER.is_file():
        check(False, "helper exists", f"{HELPER.relative_to(ROOT)} is missing")
        return
    with tempfile.TemporaryDirectory() as tmp:
        tmp = Path(tmp)
        src, dst = tmp / "new.bin", tmp / "out" / "lib.bin"
        dst.parent.mkdir()
        src.write_bytes(b"new image")
        dst.write_bytes(b"old image")
        old_inode = dst.stat().st_ino
        res = run_helper(src, dst)
        check(res.returncode == 0, "helper exits 0", res.stderr)
        check(dst.read_bytes() == b"new image", "destination holds the new bytes")
        check(dst.stat().st_ino != old_inode, "destination is a new inode",
              f"inode {old_inode} reused")
        check(sorted(p.name for p in dst.parent.iterdir()) == ["lib.bin"],
              "no temp file left behind", str(list(dst.parent.iterdir())))

        fresh = tmp / "out" / "fresh.bin"
        res = run_helper(src, fresh)
        check(res.returncode == 0 and fresh.read_bytes() == b"new image",
              "helper creates a missing destination", res.stderr)

        res = run_helper(tmp / "missing.bin", dst)
        check(res.returncode != 0, "helper fails on a missing source")
        check(dst.read_bytes() == b"new image", "a failed run leaves the destination alone")
        check(sorted(p.name for p in dst.parent.iterdir()) == ["fresh.bin", "lib.bin"],
              "a failed run leaves no temp file", str(list(dst.parent.iterdir())))


def recipe(makefile_text, target):
    """Return the recipe lines (tab-indented) of a makefile target."""
    lines = makefile_text.splitlines()
    out, inside = [], False
    for line in lines:
        if re.match(rf"^{re.escape(target)}\s*:", line):
            inside = True
            continue
        if inside:
            if line.startswith("\t"):
                out.append(line.strip())
            elif line.strip() == "" or line.startswith("#"):
                continue
            else:
                break
    return out


# A native library destination: the core under its make/shell variable or any
# literal .dylib/.so name.
NATIVE_DEST = re.compile(r"(SHAREDLIB\)?\}?\"?|\.dylib\"?|\.so\"?)\s*$")


def bare_copies(lines):
    """Return cp, ditto, or install commands targeting a native library path."""
    return [l for l in lines
            if re.match(r"^(cp|ditto|install)\b", l) and NATIVE_DEST.search(l)]


def test_build_uses_the_helper():
    """Check build recipes and macOS packaging scripts for in-place library copies."""
    make = (ROOT / "makefile").read_text()
    ui = recipe(make, "ui")
    check(ui, "makefile has a ui recipe")
    check(not bare_copies(ui), "makefile ui recipe has no bare cp onto the core",
          str(bare_copies(ui)))
    check(any("replace_file_atomic.sh" in l and "SHAREDLIB" in l for l in ui),
          "makefile ui recipe installs the core through the helper", str(ui))
    # Any recipe, not just `ui`: the core's link rule also drops a copy at
    # bin/pgohelperlib.so for the PGO helper.
    every = [l.strip() for l in make.splitlines() if l.startswith("\t")]
    check(not bare_copies(every), "no makefile recipe has a bare cp onto a native library",
          str(bare_copies(every)))
    for script in ("build_app_macos.sh", "release_macos.sh"):
        lines = [l.strip() for l in (ROOT / "scripts" / script).read_text().splitlines()]
        check(not bare_copies(lines), f"{script} has no bare cp onto a native library",
              str(bare_copies(lines)))


def build_dylib(cc, src_text, out):
    """Write C source beside out and compile a dylib, raising on compiler failure."""
    c = out.with_suffix(".c")
    c.write_text(src_text)
    subprocess.run([cc, "-dynamiclib", str(c), "-o", str(out)], check=True,
                   capture_output=True)


LOAD = "import ctypes,sys; sys.exit(0 if ctypes.CDLL(sys.argv[1]).f() == 2 else 1)"
HOLD = "import ctypes,sys,time; ctypes.CDLL(sys.argv[1]).f(); print('up', flush=True); time.sleep(30)"


def replace_while_held(tmp, name, replace):
    """Replace a mapped dylib and return a new loader's exit code, cleaning up the holder."""
    lib = tmp / f"{name}.dylib"
    shutil.copyfile(tmp / "a.dylib", lib)
    holder = subprocess.Popen([sys.executable, "-c", HOLD, str(lib)],
                              stdout=subprocess.PIPE, text=True)
    try:
        holder.stdout.readline()
        replace(tmp / "b.dylib", lib)
        return subprocess.run([sys.executable, "-c", LOAD, str(lib)]).returncode
    finally:
        holder.kill()
        holder.wait()


def test_kernel_scenario():
    """On macOS with a compiler, verify held-dylib replacement and report a cp control."""
    cc = shutil.which("cc") or shutil.which("clang")
    if sys.platform != "darwin" or not cc or not HELPER.is_file():
        print("SKIP kernel scenario (macOS + cc + helper only)")
        return
    with tempfile.TemporaryDirectory() as tmp:
        tmp = Path(tmp)
        build_dylib(cc, "int f(void){return 1;}", tmp / "a.dylib")
        build_dylib(cc, "int f(void){return 2;} int g(void){return 3;}", tmp / "b.dylib")
        rc = replace_while_held(tmp, "helper", lambda s, d: run_helper(s, d).check_returncode())
        check(rc == 0, "a held dylib replaced by the helper loads in a new process",
              f"exit {rc}")
        rc = replace_while_held(tmp, "plaincp", lambda s, d: subprocess.run(["cp", str(s), str(d)], check=True))
        print(f"INFO control: the same dylib overwritten by plain cp loads with exit {rc}"
              " (-9 = SIGKILL, the bug)")


def main():
    """Run the checks, print their summary, and return 1 if any recorded check fails."""
    test_helper_behavior()
    test_build_uses_the_helper()
    test_kernel_scenario()
    passed = sum(_results)
    print(f"{passed}/{len(_results)} passed")
    return 0 if passed == len(_results) else 1


if __name__ == "__main__":
    sys.exit(main())
