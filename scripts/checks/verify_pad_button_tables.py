#!/usr/bin/env python3
"""The per-backend pad button order must agree across its four copies.

ADR-0255 slice 1 correction: the Play Controller sheet lights each drawn key
from the pad's own `GamepadState.Buttons`, and that bitmask's button order is
*per backend*. The order therefore lives in four places that all have to agree:

  1. the backend's own key table, in source - `vector<string> buttonNames` in
     Windows/WindowsKeyManager.cpp, Linux/LinuxKeyManager.cpp and
     MacOS/MacOSKeyManager.mm (bit i is the name at index i for macOS/evdev;
     Windows' table is 1-based in its key code but its index is still the bit);
  2. Core/Shared/GamepadButtonOrder.h - one `PadButtonBit{Backend, Button, Bit}`
     row per (backend, console button), the core's host-free copy;
  3. UI/Logic/ControllerSheet.cs - `ControllerLivePad._names`, the C# mirror
     whose index IS the bit the sheet reads (`pad.Buttons[bit]`);
  4. UI/Interop/InteropEnums.cs - the C# `GamepadBackend` enum, which crosses
     P/Invoke and must keep the core's values.

The header's own comment claims its rows are "caught against the backends
through that chain". `ControllerSheetPadTests` does read the backends off disk
and does run in CI, but it compares *names by index* through the C# mirror, and
it never reads the enum - so the bit index was only pinned transitively, with
the mirror in between, and the enum not at all. This closes both directly,
host-free and without a core:

  * the three backends' `buttonNames` lists are read straight from source;
  * `ControllerLivePad._names[backend]` must equal that list, index for index;
  * every `PadButtonBit` row's `Bit` must name, in the backend's OWN table, the
    key the row's console button maps to - the console pad is the one
    `SetupButton` (UI/Logic/PlayControllerSetup.cs) names, and the button -> name
    mapping is read from `ControllerLivePad._keyNames` plus `CoreNameOf`'s XInput
    override (so it is not a second copy of the bit tables);
  * the C# `GamepadBackend` enum and `IKeyManager.h`'s must agree name for value;
  * `PadButton` and `SetupButton` must name the same console pad.

The check fails with the file, the backend, the button and both values; it
passes silently with one line. It takes the repo root as argv[1] so the unit
test (scripts/test_verify_pad_button_tables.py) can drive it over a corrupted
copy in a temp dir.

Usage: python3 scripts/checks/verify_pad_button_tables.py [repo root]
Exit 0 on PASS, 1 on any disagreement.
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]

CORE_ORDER = "Core/Shared/GamepadButtonOrder.h"
IKEYMANAGER = "Core/Shared/Interfaces/IKeyManager.h"
INTEROP_ENUMS = "UI/Interop/InteropEnums.cs"
SHEET = "UI/Logic/ControllerSheet.cs"
SETUP = "UI/Logic/PlayControllerSetup.cs"

# The three backends the header carries rows for, and the source file whose
# `buttonNames` table is that backend's own order. DirectInput has no rows (raw
# joystick buttons, no console-pad semantics), so it is not here.
BACKEND_TABLES = (
    ("GameController", "MacOS/MacOSKeyManager.mm"),
    ("XInput", "Windows/WindowsKeyManager.cpp"),
    ("Evdev", "Linux/LinuxKeyManager.cpp"),
)
TABLE_FILE = dict(BACKEND_TABLES)

# The bits GamepadState's consumers show: the sheet mirrors the first 24 of each
# backend's table, and every bit a PadButtonBit row names must land inside it.
MIRROR_WINDOW = 24

ROW_RE = re.compile(
    r"\{\s*GamepadBackend::(\w+)\s*,\s*PadButton::(\w+)\s*,\s*(-?\d+)\s*\}")
BUTTON_NAMES_RE = re.compile(
    r"vector<string>\s+buttonNames\s*=\s*\{(.*?)\}\s*;", re.S)
SHEET_NAMES_RE = re.compile(
    r"\[GamepadBackend\.(\w+)\]\s*=\s*new\[\]\s*\{(.*?)\}", re.S)
KEY_NAMES_RE = re.compile(
    r"_keyNames\s*=\s*new\(\)\s*\{(.*?)\};", re.S)
KEY_NAME_ENTRY_RE = re.compile(r'\[SetupButton\.(\w+)\]\s*=\s*"([^"]*)"')
CORE_NAME_OVERRIDE_RE = re.compile(
    r"button\s*==\s*SetupButton\.(\w+)\s*&&\s*backend\s*==\s*GamepadBackend\.(\w+)"
    r'\s*\?\s*"([^"]*)"\s*:')
COMMENT_RE = re.compile(r"//[^\n]*|/\*.*?\*/", re.S)


def read(root: Path, rel: str):
    path = root / rel
    if not path.is_file():
        return None
    return path.read_text(encoding="utf-8")


def enum_members(text: str, header: str):
    """[(name, value)] of the enum whose opening matches `header`."""
    match = re.search(header + r"[^{]*\{([^}]*)\}", text, re.S)
    if not match:
        return None
    body = COMMENT_RE.sub("", match.group(1))
    members = []
    last = -1
    for part in body.split(","):
        part = part.strip()
        if not part:
            continue
        entry = re.match(r"(\w+)\s*(?:=\s*(\d+))?$", part)
        if not entry:
            continue
        if entry.group(2) is not None:
            last = int(entry.group(2))
        else:
            last += 1
        members.append((entry.group(1), last))
    return members


def check(root: Path):
    """Every disagreement found, as a list of messages. Empty when in sync."""
    failures = []

    core_order = read(root, CORE_ORDER)
    ikeymanager = read(root, IKEYMANAGER)
    interop = read(root, INTEROP_ENUMS)
    sheet = read(root, SHEET)
    setup = read(root, SETUP)
    for rel, text in ((CORE_ORDER, core_order), (IKEYMANAGER, ikeymanager),
                      (INTEROP_ENUMS, interop), (SHEET, sheet), (SETUP, setup)):
        if text is None:
            failures.append(f"{rel}: file not found (the pad order has no source to read)")
    if failures:
        return failures

    # The C# enum crosses P/Invoke, so its member values must be the core's.
    core_enum = enum_members(ikeymanager, r"enum\s+class\s+GamepadBackend\b")
    cs_enum = enum_members(interop, r"public\s+enum\s+GamepadBackend\b")
    if not core_enum:
        failures.append(f"{IKEYMANAGER}: no GamepadBackend enum found")
    if not cs_enum:
        failures.append(f"{INTEROP_ENUMS}: no GamepadBackend enum found")
    if core_enum and cs_enum:
        core_map, cs_map = dict(core_enum), dict(cs_enum)
        for name in sorted(set(core_map) | set(cs_map)):
            left, right = core_map.get(name), cs_map.get(name)
            if left != right:
                shown = lambda v: "absent" if v is None else str(v)
                failures.append(
                    f"{INTEROP_ENUMS} vs {IKEYMANAGER}: GamepadBackend.{name} is "
                    f"{shown(right)} in C#, {shown(left)} in the core")

    # PadButton names the console pad SetupButton draws, member set for member.
    pad_enum = enum_members(core_order, r"enum\s+class\s+PadButton\b")
    setup_enum = enum_members(setup, r"public\s+enum\s+SetupButton\b")
    if not pad_enum:
        failures.append(f"{CORE_ORDER}: no PadButton enum found")
    if not setup_enum:
        failures.append(f"{SETUP}: no SetupButton enum found")
    if pad_enum and setup_enum:
        pad_names = {n for n, _ in pad_enum}
        setup_names = {n for n, _ in setup_enum}
        for name in sorted(pad_names - setup_names):
            failures.append(
                f"{CORE_ORDER}: PadButton.{name} names no SetupButton "
                f"({SETUP}) - the header no longer names the drawn console pad")
        for name in sorted(setup_names - pad_names):
            failures.append(
                f"{CORE_ORDER}: SetupButton.{name} ({SETUP}) has no PadButton row")

    # The backend's own table, and the C# mirror keyed by backend.
    backend_names = {}
    for backend, rel in BACKEND_TABLES:
        text = read(root, rel)
        if text is None:
            failures.append(f"{rel}: file not found (the {backend} key table)")
            continue
        match = BUTTON_NAMES_RE.search(text)
        if not match:
            failures.append(f"{rel}: no `vector<string> buttonNames` table found")
            continue
        backend_names[backend] = re.findall(
            r'"([^"]*)"', COMMENT_RE.sub("", match.group(1)))

    sheet_names = {}
    for backend, body in SHEET_NAMES_RE.findall(sheet):
        sheet_names[backend] = re.findall(r'"([^"]*)"', COMMENT_RE.sub("", body))

    # ControllerLivePad._names[backend] == the backend's own table, index for
    # index. This is what makes a wrong bit in the header visible below.
    for backend, rel in BACKEND_TABLES:
        if backend not in sheet_names or backend not in backend_names:
            continue
        mirror = sheet_names[backend]
        names = backend_names[backend]
        if len(mirror) > len(names):
            failures.append(
                f"{SHEET}: GamepadBackend.{backend} lists {len(mirror)} names but "
                f"{rel} names only {len(names)} buttons")
        for i in range(min(len(mirror), len(names))):
            if mirror[i] != names[i]:
                failures.append(
                    f"{SHEET}: GamepadBackend.{backend} bit {i} is \"{mirror[i]}\" "
                    f"but {rel} names bit {i} \"{names[i]}\"")

    # The console button -> backend name mapping, read from the sheet (not a
    # second copy of the bit tables): _keyNames plus CoreNameOf's per-backend
    # override (XInput calls Select "Back").
    key_names_block = KEY_NAMES_RE.search(sheet)
    key_names = dict(KEY_NAME_ENTRY_RE.findall(key_names_block.group(1))) \
        if key_names_block else {}
    overrides = {(b, k): v for k, b, v in CORE_NAME_OVERRIDE_RE.findall(sheet)}

    def console_name(button: str, backend: str):
        return overrides.get((backend, button), key_names.get(button))

    # Every PadButtonBit row: the name at Bit in the backend's OWN table is the
    # name that row's console button maps to.
    # Comments first: a row that has been commented out is not a row. Read
    # as text, it would be checked as if the table still carried it - and the
    # check would then pass on a header whose table no longer has it, or fail
    # on one that is simply being edited. (Row completeness is a separate
    # question this check does not answer: the evdev D-pad legitimately has
    # no row, so an absent row is not a defect here.)
    rows = ROW_RE.findall(COMMENT_RE.sub("", core_order))
    if not rows:
        failures.append(f"{CORE_ORDER}: no PadButtonBit rows found")
    for backend, button, bit_text in rows:
        bit = int(bit_text)
        if backend not in backend_names:
            failures.append(
                f"{CORE_ORDER}: PadButtonBit row for {backend} has no key table "
                f"to check against")
            continue
        names = backend_names[backend]
        expected = console_name(button, backend)
        if expected is None:
            failures.append(
                f"{CORE_ORDER}: PadButton::{button} has no console name in {SHEET}")
            continue
        if not 0 <= bit < len(names):
            failures.append(
                f"{CORE_ORDER}: {backend}.{button} names bit {bit}, outside "
                f"{TABLE_FILE[backend]}'s {len(names)} buttons")
            continue
        actual = names[bit]
        if actual != expected:
            table = TABLE_FILE[backend]
            failures.append(
                f"{CORE_ORDER}: {backend}.{button} names bit {bit}, which is "
                f"\"{actual}\" in {table}, but the console {button} maps to "
                f"\"{expected}\"")

    return failures


def main(argv):
    root = Path(argv[1]).resolve() if len(argv) > 1 else REPO
    failures = check(root)
    if failures:
        print("FAIL verify_pad_button_tables:")
        for failure in failures:
            print(f"  {failure}")
        return 1
    print("PASS verify_pad_button_tables: the three backends' key tables, "
          "ControllerLivePad._names, GamepadButtonOrder.h and the C# "
          "GamepadBackend enum agree")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
