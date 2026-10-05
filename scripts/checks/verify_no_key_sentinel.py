#!/usr/bin/env python3
"""Fail when the "no key" sentinel is handed out as if it were a key.

#902: a key code of 0 is not a key. It is the value an empty KeyCombination
slot has, and the one code a pressed set must never carry. macOS mapped the
sentinel into a pressed set: `MacOSKeyManager`'s table answers 0 for the eight
virtual key codes it has no Mesen key for, as does the arm it keeps for every
code >= 128 (outside the range the table covers at all), and the event handler
recorded that answer - so `GetPressedKeys` reported a key that no key name
resolves to. That write is guarded now, and the sentinel's remaining way in is
`SetKeyState`, which all three backends accept code 0 through. Every reader that
receives it has to know to drop it, and each one handles it differently: the
host filters it in `PressedKeys.Decode`, Lua leaves a nil hole (its key name is
empty, so the push is skipped while the table index still advances, and `ipairs`
then stops at the hole), and `ShortcutKeyHandler` keeps it - where it is the
whole set's non-emptiness and its size, and both are read as "a key is down".

Which *physical* keys reach the mapped write is not something this repo can
settle: Fn is the only table hole that names a key AppKit delivers, and it
arrives as `NSEventTypeFlagsChanged`, which the branch above the mapping takes;
media keys arrive as `NSEventTypeSystemDefined`, and the event mask subscribes
to key down, key up and flags changed only. The sentinel's reachable way in is
`InputApi.SetKeyState`, a host export the backends accept code 0 through. The
write has to be safe for the codes the table does produce either way.

Nothing host-free can express this: the writer is ObjC++ behind AppKit and the
facade is not linked into `make core-unit-tests`, so a stand-in for either would
pass. This is the committed guard for the two sites that have to agree:

  * `IKeyManager` names the sentinel and owns the one filter for it, and
    `KeyManager::GetPressedKeys` returns that filter applied to the backend's
    own answer;
  * `MacOSKeyManager` records nothing at all for a virtual key code it cannot
    name: the mapping answers the sentinel and the write of `_keyState` sits
    inside a guard that tests it.

What this guard is: a shape check. It reads the statement a write sits in, so it
catches the guard being deleted, inverted, or short-circuited (`|| true`) - the
#902 regression and the ways back to it - and it accepts the several shapes the
guard can legitimately take (a spacing change, a comment between the `if` and
the write, Allman braces, a renamed local, an early return). What it cannot do
is prove semantics: a condition that mentions the sentinel but tests something
else passes here. That is `core_unit_tests`' `TestTheNoKeySentinelIsNeverAKey`
for the filter, and review for the writer.

It is a source check, not a runtime one: what it catches is the source drifting
back, not a stale `MesenCore.dylib`.

Usage:
  python3 scripts/checks/verify_no_key_sentinel.py
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
INTERFACE_PATH = REPO_ROOT / "Core" / "Shared" / "Interfaces" / "IKeyManager.h"
KEY_MANAGER_PATH = REPO_ROOT / "Core" / "Shared" / "KeyManager.cpp"
MACOS_PATH = REPO_ROOT / "MacOS" / "MacOSKeyManager.mm"

#The sentinel's name, the filter's signature, and the mapping's true arm.
SENTINEL = re.compile(r"static\s+constexpr\s+uint16_t\s+NoKey\s*=\s*0\s*;")
FILTER_SIGNATURE = re.compile(r"WithoutNoKey\s*\(\s*const\s+vector<uint16_t>\s*&\s*(\w+)\s*\)")
FILTER_NEEDS_SENTINEL = re.compile(r"!=\s*(?:IKeyManager::)?NoKey\b")
#The facade's answer: the filter applied to the backend's own set, and returned.
FACADE_RETURN = re.compile(
    r"return\s+IKeyManager::WithoutNoKey\s*\(\s*_keyManager\s*->\s*GetPressedKeys\s*\(\s*\)\s*\)\s*;"
)
MAPPING = re.compile(
    r"=\s*\[\s*event keyCode\s*\]\s*>=\s*\w+\s*\?\s*(?:IKeyManager::)?NoKey\s*:"
    r"\s*_keyCodeMap\s*\[\s*\[\s*event keyCode\s*\]\s*\]\s*;"
)
#The index, allowing one level of nesting: `mappedKeyCode`, `116`, and
#`[event keyCode] & 0x7F` are all the same shape to the rule below.
WRITE = re.compile(r"_keyState\s*\[((?:[^\[\]]|\[[^\[\]]*\])*)\]\s*=")


def read(path: Path, errors: list[str]) -> str:
    try:
        return path.read_text(encoding="utf-8")
    except OSError as ex:
        errors.append(f"{path.relative_to(REPO_ROOT)} could not be read: {ex}")
        return ""


def code_lines(text: str) -> list[str]:
    """The file's statements: blanks, whole-line comments and comment tails out.

    A guard that a formatter or a comment can defeat is a guard that fails on
    refactors instead of on drift, so the checks below read statements only.
    """
    lines: list[str] = []
    for raw in text.splitlines():
        line = raw.split("//")[0].strip()
        if line and not line.startswith("*") and not line.startswith("/*"):
            lines.append(line)
    return lines


def is_sentinel_guard(line: str) -> bool:
    """`if(<something> != NoKey) {` - the write's own guard, in any spacing."""
    match = re.match(r"if\s*\((.*)\)\s*\{?$", line)
    if match is None:
        return False
    condition = match.group(1)
    if re.search(r"\|\|\s*true|&&\s*false", condition):
        #The shape that keeps the guard's text and drops its meaning.
        return False
    return bool(FILTER_NEEDS_SENTINEL.search(condition))


def is_sentinel_early_return(line: str) -> bool:
    """`if(<something> == NoKey) return nil;` above the write.

    The other shape the same rule takes: leave before the write rather than wrap
    it. The statement has to carry the test and the bail-out to count.
    """
    if "return" not in line and "continue" not in line:
        return False
    return bool(re.search(r"==\s*(?:IKeyManager::)?NoKey\b", line))


def check_interface(text: str, errors: list[str]) -> None:
    if not text:
        return
    if not SENTINEL.search(text):
        errors.append(
            "Core/Shared/Interfaces/IKeyManager.h: no `static constexpr uint16_t NoKey = 0;` - "
            "the sentinel has no name, so every site that has to leave it alone spells it `0` "
            "and reads as an ordinary key code (#902)"
        )
    signature = FILTER_SIGNATURE.search(text)
    if signature is None:
        errors.append(
            "Core/Shared/Interfaces/IKeyManager.h: no `WithoutNoKey(const vector<uint16_t>&)` - "
            "the one filter for the sentinel, which is what keeps the rule out of each reader"
        )
    elif not FILTER_NEEDS_SENTINEL.search(text):
        #A token check on purpose: that the filter drops exactly the sentinel,
        #keeps every other code and keeps the backend's order is
        #core_unit_tests' TestTheNoKeySentinelIsNeverAKey, which can run it.
        errors.append(
            "Core/Shared/Interfaces/IKeyManager.h: WithoutNoKey no longer compares against "
            "`NoKey` - it drops some other code, or none (#902)"
        )


def check_key_manager(text: str, errors: list[str]) -> None:
    if not text:
        return
    if not any(FACADE_RETURN.search(line) for line in code_lines(text)):
        errors.append(
            "Core/Shared/KeyManager.cpp: GetPressedKeys no longer *returns* "
            "`IKeyManager::WithoutNoKey(_keyManager->GetPressedKeys())` - the sentinel reaches "
            "the host, Lua's getPressedKeys and ShortcutKeyHandler again (#902). A call whose "
            "result is discarded reads as the fix and is not one"
        )


def event_handler(lines: list[str]) -> list[str]:
    """The key-event monitor's own block, and nothing else.

    `_keyState` is written from three places, and only one of them is fed by the
    key table: the modifier branch writes fixed codes, and `SetKeyState` is the
    host export that writes whatever code it is handed - a sentinel that reaches
    the set through it is the facade's filter to stop, not this write's. The
    guard reads the block between `addLocalMonitorForEventsMatchingMask` and its
    closing `}];`.
    """
    start = next((i for i, line in enumerate(lines) if "addLocalMonitorForEventsMatchingMask" in line), None)
    if start is None:
        return []
    for end in range(start, len(lines)):
        if lines[end].startswith("}];"):
            return lines[start:end + 1]
    return lines[start:]


def check_macos(text: str, errors: list[str]) -> None:
    if not text:
        return
    handler = event_handler(code_lines(text))
    if not handler:
        errors.append(
            "MacOS/MacOSKeyManager.mm: no `addLocalMonitorForEventsMatchingMask` handler - the "
            "key-event monitor is built somewhere else now, and this guard reads that block"
        )
        return
    if not any(MAPPING.search(line) for line in handler):
        errors.append(
            "MacOS/MacOSKeyManager.mm: the key-code mapping is gone from the event handler, or no "
            "longer answers `IKeyManager::NoKey` for a virtual key code outside the table - a "
            "code the table cannot name would be recorded as a key again (#902)"
        )

    writes = [(index, WRITE.search(line).group(1)) for index, line in enumerate(handler) if WRITE.search(line)]
    if not writes:
        errors.append(
            "MacOS/MacOSKeyManager.mm: no `_keyState[...] = ...` write in the event handler - the "
            "key state is written somewhere else now, and this guard reads that write"
        )
    for index, key in writes:
        if key.isdigit():
            #A constant index - the modifier branch's own writes. It cannot come
            #from the table, but it can still be the sentinel spelled outright.
            if int(key) == 0:
                errors.append(
                    "MacOS/MacOSKeyManager.mm: `_keyState[0] = ...` writes the sentinel's own slot "
                    "- that is the pressed set's key that is not a key (#902)"
                )
            continue
        #Allman braces put the `{` between the test and its body.
        test_line = index - 1
        while test_line > 0 and handler[test_line] == "{":
            test_line -= 1
        guarded = test_line >= 0 and is_sentinel_guard(handler[test_line])
        #The early-return shape: the sentinel test is above, not around.
        early = any(is_sentinel_early_return(line) for line in handler[max(0, index - 3):index])
        if not guarded and not early:
            errors.append(
                "MacOS/MacOSKeyManager.mm: `_keyState[%s] = ...` is not guarded by a test of "
                "`NoKey` - neither inside an `if(... != NoKey)`, nor behind an early return for "
                "`== NoKey`. A virtual key code with no Mesen key writes `_keyState[0]` again, "
                "and GetPressedKeys reports a key that is not there (#902)" % key
            )


def main() -> int:
    errors: list[str] = []
    check_interface(read(INTERFACE_PATH, errors), errors)
    check_key_manager(read(KEY_MANAGER_PATH, errors), errors)
    check_macos(read(MACOS_PATH, errors), errors)

    if errors:
        for err in errors:
            print(f"FAIL: {err}", file=sys.stderr)
        return 1

    print(
        "PASS: the \"no key\" sentinel is named in IKeyManager, filtered out of every backend's "
        "pressed set by KeyManager::GetPressedKeys, and never recorded by MacOSKeyManager (#902)"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
