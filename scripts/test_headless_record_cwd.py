"""headless_record must load the NES game database whatever the caller's cwd.

Issue #477: the recorder copied `UI/Dependencies/MesenNesDB.txt` into its
per-run mesen-home through a path relative to the *working directory*, so a
run launched from anywhere but the repo root logged "[DB] Initialized - 0 games
in DB", skipped the database's board/input overrides, and minted a different
save state (Punch-Out!!: RAM $01FC = 8 instead of 6). None of the recording
scripts cd before calling it, so every state depended on where the user stood.

This case runs the real binary, briefly, from a temp cwd and from the repo
root, each into a fresh output folder, and asserts both runs load the same,
non-empty database. The ROM is a synthetic NROM image (an infinite loop), so
no ROM file is needed. The binary is a build product (`make capture-tool`);
when it is absent the case says so and exits 0, the way the suite's other
environment-bound cases do.
"""

import re
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
BINARY = ROOT / "scripts" / "headless_record"
DB_LINE = re.compile(r"\[DB\] Initialized - (\d+) games in DB")

_FAILURES = []
_CHECKS = []


def check(cond, name, detail=""):
    _CHECKS.append(name)
    print(("ok   " if cond else "FAIL ") + name + ("" if cond else f"\n     {detail}"))
    if not cond:
        _FAILURES.append(name)


def nrom(path):
    """16 KB PRG whose reset vector points at a JMP to itself, 8 KB CHR."""
    prg = bytearray(16384)
    prg[0:3] = b"\x4c\x00\xc0"  # $C000: JMP $C000
    prg[0x3FFA:0x4000] = b"\x00\xc0\x00\xc0\x00\xc0"  # NMI, RESET, IRQ -> $C000
    path.write_bytes(b"NES\x1a\x01\x01" + bytes(10) + bytes(prg) + bytes(8192))


def run_from(cwd, work):
    rom = work / "loop.nes"
    nrom(rom)
    out = work / "out"
    out.mkdir()
    proc = subprocess.run(
        [str(BINARY), str(rom), "0.1", str(out / "run"), "log"],
        cwd=str(cwd), capture_output=True, text=True, timeout=120,
    )
    return proc.returncode, proc.stdout + proc.stderr


def db_count(output):
    match = DB_LINE.search(output)
    return int(match.group(1)) if match else None


def test_the_database_loads_from_a_foreign_cwd_as_from_the_repo_root():
    with tempfile.TemporaryDirectory() as foreign, tempfile.TemporaryDirectory() as a, \
            tempfile.TemporaryDirectory() as b:
        code_foreign, out_foreign = run_from(Path(foreign), Path(a))
        code_root, out_root = run_from(ROOT, Path(b))
    check(code_foreign == 0 and code_root == 0, "both runs exit 0",
          f"foreign={code_foreign} root={code_root}\n{out_foreign[-2000:]}")
    foreign_count = db_count(out_foreign)
    root_count = db_count(out_root)
    check(root_count is not None and root_count > 0,
          "run from the repo root loads a non-empty game DB", f"count={root_count}")
    check(foreign_count is not None and foreign_count > 0,
          "run from a foreign cwd loads a non-empty game DB",
          f"count={foreign_count}; log line: {DB_LINE.search(out_foreign) and DB_LINE.search(out_foreign).group(0)}")
    check(foreign_count == root_count, "both cwds load the same game DB",
          f"foreign={foreign_count} root={root_count}")


def main():
    if not BINARY.exists():
        print(f"SKIP: {BINARY} not built (make capture-tool)")
        return 0
    tests = [
        test_the_database_loads_from_a_foreign_cwd_as_from_the_repo_root,
    ]
    for t in tests:
        t()
    print(f"\n{len(_CHECKS) - len(_FAILURES)}/{len(_CHECKS)} checks passed")
    return 1 if _FAILURES else 0


if __name__ == "__main__":
    sys.exit(main())
