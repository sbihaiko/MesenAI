#!/usr/bin/env python3
"""Framework-free teeth for scripts/checks/verify_pad_button_tables.py.

The check reads six source files off disk, so it is driven over a temp tree
that copies the real ones and corrupts exactly one copy per case. A check whose
failure mode was never exercised is not a check (the repo's rule for these
guards), so each case asserts the checker exits non-zero AND says what it saw:

  1. the control - the unmodified copies pass;
  2. a swapped bit - GamepadButtonOrder.h names a bit the backend's own table
     fills with another button (the defect that reached main in #817);
  3. a renamed backend button - WindowsKeyManager.cpp's "Back" becomes "Bk";
  4. a reordered mirror - ControllerLivePad._names' XInput row swaps Up/Down;
  5. a renumbered enum - InteropEnums.cs moves Evdev off the core's value;
  6. a commented-out row with a wrong bit - the table is the code, not the text
     around it, so this one must PASS.

Usage: python3 scripts/test_verify_pad_button_tables.py
"""
from __future__ import annotations

import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
CHECK = REPO / "scripts" / "checks" / "verify_pad_button_tables.py"

# Everything the checker reads, relative to the repo root.
FILES = (
    "Core/Shared/GamepadButtonOrder.h",
    "Core/Shared/Interfaces/IKeyManager.h",
    "UI/Interop/InteropEnums.cs",
    "UI/Logic/ControllerSheet.cs",
    "UI/Logic/PlayControllerSetup.cs",
    "MacOS/MacOSKeyManager.mm",
    "Windows/WindowsKeyManager.cpp",
    "Linux/LinuxKeyManager.cpp",
)

FAILURES = []


def fail(msg):
    FAILURES.append(msg)
    print(f"FAIL: {msg}")


def ok(msg):
    print(f"PASS: {msg}")


def build(root):
    for rel in FILES:
        dst = root / rel
        dst.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(REPO / rel, dst)


def run(root):
    return subprocess.run(
        [sys.executable, str(CHECK), str(root)], capture_output=True, text=True)


def edit(root, rel, pattern, replacement):
    """Rewrite one file, refusing to run when the pattern did not match."""
    path = root / rel
    text = path.read_text(encoding="utf-8")
    new = re.sub(pattern, replacement, text, count=1, flags=re.S)
    if new == text:
        raise AssertionError(f"{rel}: corruption pattern {pattern!r} matched nothing")
    path.write_text(new, encoding="utf-8")


def case(name, mutate, wanted):
    with tempfile.TemporaryDirectory() as tmp:
        root = Path(tmp)
        build(root)
        if mutate is not None:
            mutate(root)
        proc = run(root)
    combined = proc.stdout + proc.stderr
    if proc.returncode == 0:
        fail(f"{name}: the checker accepted a corrupted tree (exit 0): {combined.strip()}")
        return
    for needle in wanted:
        if needle not in combined:
            fail(f"{name}: exit {proc.returncode} but the message omits {needle!r}: {combined.strip()}")
            return
    ok(f"{name}: rejected with a message naming {', '.join(wanted)}")


def main():
    # 1. The control: the copies as they ship agree, so the checker is not
    #    rejecting everything.
    with tempfile.TemporaryDirectory() as tmp:
        root = Path(tmp)
        build(root)
        proc = run(root)
    if proc.returncode != 0 or "PASS verify_pad_button_tables" not in proc.stdout:
        fail(f"the unmodified copies must pass, got exit {proc.returncode}: "
             f"{(proc.stdout + proc.stderr).strip()}")
    else:
        ok("the unmodified copies pass (the control)")

    # 2. A swapped bit: XInput's A row names the D-pad Up bit the Windows table
    #    fills with "Up", while the console A maps to "A".
    case("a swapped bit in GamepadButtonOrder.h",
         lambda root: edit(root, "Core/Shared/GamepadButtonOrder.h",
                           r"GamepadBackend::XInput, PadButton::A, 12",
                           "GamepadBackend::XInput, PadButton::A, 0"),
         ["GamepadButtonOrder.h", "XInput.A", '"Up"', '"A"'])

    # 3. A renamed backend button: WindowsKeyManager.cpp's "Back" is XInput's bit
    #    5, which the console Select maps to.
    case("a renamed backend button in WindowsKeyManager.cpp",
         lambda root: edit(root, "Windows/WindowsKeyManager.cpp",
                           r'"Back",', '"Bk",'),
         ["XInput.Select", '"Bk"', '"Back"'])

    # 4. A reordered mirror: ControllerLivePad._names' XInput row swaps Up/Down,
    #    so bit 0 no longer names the backend's bit 0.
    case("a reordered ControllerLivePad._names",
         lambda root: edit(root, "UI/Logic/ControllerSheet.cs",
                           r'"Up", "Down", "Left", "Right", "Start", "Back"',
                           '"Down", "Up", "Left", "Right", "Start", "Back"'),
         ["ControllerSheet.cs", "XInput", '"Down"', '"Up"'])

    # 5. A renumbered enum: the C# GamepadBackend moves Evdev off the core's 3.
    case("a renumbered GamepadBackend enum",
         lambda root: edit(root, "UI/Interop/InteropEnums.cs",
                           r"Evdev = 3,", "Evdev = 9,"),
         ["InteropEnums.cs", "GamepadBackend.Evdev"])

    # 6. A commented-out row is not a row. The mutation both comments the row out
    #    and gives it a bit the backend's table fills with another button, so a
    #    checker that read the comment as live would reject this tree: the table
    #    is the code, not the text around it. Row *completeness* is a different
    #    question and deliberately not answered here (the evdev D-pad has no row
    #    and never will), so this case asserts the parse rather than coverage.
    with tempfile.TemporaryDirectory() as tmp:
        root = Path(tmp)
        build(root)
        edit(root, "Core/Shared/GamepadButtonOrder.h",
             r"(\t*)\{ GamepadBackend::XInput, PadButton::A, 12 \},",
             r"\1// { GamepadBackend::XInput, PadButton::A, 0 },")
        proc = run(root)
    if proc.returncode != 0:
        fail("a commented-out row must not be read as a row, got exit "
             f"{proc.returncode}: {(proc.stdout + proc.stderr).strip()}")
    else:
        ok("a commented-out row is not part of the table")

    if FAILURES:
        print(f"\n{len(FAILURES)} failure(s)")
        sys.exit(1)
    print("\nall checks passed")


if __name__ == "__main__":
    main()
