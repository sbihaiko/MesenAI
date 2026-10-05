#!/usr/bin/env python3
"""Fail when the "no key" sentinel is handed out as if it were a key.

#902: a key code of 0 is not a key. It is the value an empty KeyCombination
slot has, and the one code a pressed set must never carry. macOS produces it
anyway - `MacOSKeyManager` maps a virtual key code to a Mesen key code and then
records the answer, and its table answers 0 for every code it has no Mesen key
for (eight holes, and every code >= 128, which is outside the range it covers
at all) - so `GetPressedKeys` reports a key that no key name resolves to. Every
reader that receives it has to know to drop it, and each one drops it
differently: the host filters it in `PressedKeys.Decode`, Lua drops it by
accident (its key name is empty), and `ShortcutKeyHandler` keeps it - where it
is the whole set's non-emptiness and its size, and both are read as "a key is
down".

Nothing host-free can express this: the writer is ObjC++ behind AppKit and the
facade is not linked into `make core-unit-tests`, so a stand-in for either would
pass. This is the committed guard for the three sites that have to agree:

  * `IKeyManager` names the sentinel and owns the one filter for it;
  * `KeyManager::GetPressedKeys` runs every backend's answer through that
    filter - the one place the set leaves the backend, and the one place macOS's
    sentinel and a `SetKeyState(NoKey, true)` from the host both become nothing;
  * `MacOSKeyManager` records nothing at all for a virtual key code it cannot
    name, instead of recording "no key" under the sentinel's slot.

The single-key probe (`IsKeyPressed`) is not guarded here: all three backends
already answer false for the sentinel on their own, so there is nothing for this
guard to keep in step.

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


def read(path: Path, errors: list[str]) -> str:
    try:
        return path.read_text(encoding="utf-8")
    except OSError as ex:
        errors.append(f"{path.relative_to(REPO_ROOT)} could not be read: {ex}")
        return ""


def check_interface(text: str, errors: list[str]) -> None:
    if not text:
        return
    if not re.search(r"static\s+constexpr\s+uint16_t\s+NoKey\s*=\s*0\s*;", text):
        errors.append(
            "Core/Shared/Interfaces/IKeyManager.h: no `static constexpr uint16_t NoKey = 0;` - "
            "the sentinel has no name, so every site that has to leave it alone spells it `0` "
            "and reads as an ordinary key code (#902)"
        )
    if not re.search(r"WithoutNoKey\s*\(\s*const\s+vector<uint16_t>\s*&\s*\w+\s*\)", text):
        errors.append(
            "Core/Shared/Interfaces/IKeyManager.h: no `WithoutNoKey(const vector<uint16_t>&)` - "
            "the one filter for the sentinel, which is what keeps the rule out of each reader"
        )
    #The filter's own rule: it compares against the sentinel, so a rewrite that
    #dropped a *different* code (or none) is caught here.
    if "WithoutNoKey" in text and "keyCode != NoKey" not in text:
        errors.append(
            "Core/Shared/Interfaces/IKeyManager.h: WithoutNoKey no longer keeps the keys "
            "`keyCode != NoKey` - it drops the wrong code, or none (#902)"
        )


def check_key_manager(text: str, errors: list[str]) -> None:
    if not text:
        return
    if not re.search(r"WithoutNoKey\s*\(\s*_keyManager\s*->\s*GetPressedKeys\s*\(\s*\)\s*\)", text):
        errors.append(
            "Core/Shared/KeyManager.cpp: GetPressedKeys no longer runs the backend's answer "
            "through IKeyManager::WithoutNoKey - the sentinel reaches the host, Lua's "
            "getPressedKeys and ShortcutKeyHandler again (#902)"
        )


def check_macos(text: str, errors: list[str]) -> None:
    if not text:
        return
    #The mapping: a virtual key code with no Mesen key is the sentinel, not a
    #literal `0` that reads as one more key code.
    if not re.search(r">=\s*128\s*\?\s*IKeyManager::NoKey\s*:", text):
        errors.append(
            "MacOS/MacOSKeyManager.mm: the key-code mapping no longer answers "
            "`IKeyManager::NoKey` for a virtual key code >= 128 - media and brightness keys "
            "would be recorded as a key again (#902)"
        )
    #The write driven by that mapping has to be guarded by the sentinel check.
    lines = text.splitlines()
    writes = [i for i, line in enumerate(lines) if "_keyState[mappedKeyCode]" in line]
    if not writes:
        errors.append(
            "MacOS/MacOSKeyManager.mm: no `_keyState[mappedKeyCode] = ...` write - the event "
            "handler's key state is written somewhere else now, and this guard reads that write"
        )
    for index in writes:
        guard = next((lines[i] for i in range(index - 1, -1, -1) if lines[i].strip()), "")
        if not (guard.strip().startswith("if(") and "NoKey" in guard):
            errors.append(
                "MacOS/MacOSKeyManager.mm: `_keyState[mappedKeyCode] = ...` is no longer inside "
                "an `if(... NoKey ...)` - a virtual key code with no Mesen key writes "
                "`_keyState[0]` again, and GetPressedKeys reports a key that is not there (#902)"
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
        "pressed set, and never recorded by MacOSKeyManager (#902)"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
