#!/usr/bin/env python3
"""Fail when the pressed-key read drifts across the two languages it is written in.

#895: the host read three pressed keys because a literal `3` was written twice -
once as `new UInt16[3]` in `UI/Interop/InputApi.cs`, once as `i < 3` in
`InteropDLL/InputApiWrapper.cpp`'s copy loop - with nothing keeping the two in
step. The fix moved the sizing into `UI/Logic/PressedKeys.cs` (dual-compiled by
`UI.Tests`) and made the native export answer the size of the set it holds, so
the host can grow instead of truncating.

The C# half is pinned by `UI.Tests/Input/PressedKeysTests.cs`, but those tests
run against stand-ins for the export: a native loop that stopped at 3, or that
answered `min(size, capacity)` instead of `size`, passes every one of them. This
is the committed guard for the other half. It reads the two files and asserts the
contract they have to agree on:

  * the export RETURNS the set's size (`int32_t`, ending in
    `return (int32_t)pressedKeys.size();`) rather than being `void`;
  * its copy loop stops at the caller's `capacity`;
  * the caller declares the same return type in its `DllImport` and passes the
    buffer's own length as the capacity;
  * the caller reaches the export through `PressedKeys.Read`, the only path that
    grows the buffer - a direct call that decodes one buffer would drop the tail
    again, silently, which is the whole bug.

It is a source check, not a runtime one: a stale `MesenCore.dylib` is invisible
here (the symbol name does not encode the return type), and rebuilding the core
is what keeps a build in step. What this catches is the source drifting back.

Usage:
  python3 scripts/checks/verify_pressed_keys_contract.py
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
CSHARP_PATH = REPO_ROOT / "UI" / "Interop" / "InputApi.cs"
NATIVE_PATH = REPO_ROOT / "InteropDLL" / "InputApiWrapper.cpp"
LOGIC_PATH = REPO_ROOT / "UI" / "Logic" / "PressedKeys.cs"


def read(path: Path, errors: list[str]) -> str:
    try:
        return path.read_text(encoding="utf-8")
    except OSError as ex:
        errors.append(f"{path.relative_to(REPO_ROOT)} could not be read: {ex}")
        return ""


def check_native(text: str, errors: list[str]) -> None:
    if not text:
        return
    #The export's signature: a return value, and the capacity as a parameter.
    match = re.search(
        r"DllExport\s+(\w+)\s+__stdcall\s+GetPressedKeys\s*\(\s*uint16_t\s*\*\s*\w+\s*,\s*int32_t\s+capacity\s*\)",
        text,
    )
    if match is None:
        errors.append(
            "InteropDLL/InputApiWrapper.cpp: no "
            "`DllExport <type> __stdcall GetPressedKeys(uint16_t* ..., int32_t capacity)` "
            "export - the item of the contract the host binds to by name"
        )
    elif match.group(1) == "void":
        errors.append(
            "InteropDLL/InputApiWrapper.cpp: GetPressedKeys returns void again - the host "
            "reads the return value as the size of the set, so a void export hands it "
            "whatever the register held (#895)"
        )
    #The answer the growth depends on: the size of the set, not the number of
    #slots that fit.
    if "return (int32_t)pressedKeys.size();" not in text:
        errors.append(
            "InteropDLL/InputApiWrapper.cpp: GetPressedKeys no longer ends in "
            "`return (int32_t)pressedKeys.size();` - answering `min(size, capacity)` (or "
            "nothing) makes a set larger than the buffer silently truncated again, and "
            "every case in UI.Tests/Input/PressedKeysTests.cs still passes"
        )
    if "i < (size_t)capacity" not in text:
        errors.append(
            "InteropDLL/InputApiWrapper.cpp: the copy loop no longer stops at the caller's "
            "`capacity` - a literal bound here is the two-language drift #895 was"
        )


def check_csharp(text: str, errors: list[str]) -> None:
    if not text:
        return
    match = re.search(
        r'\[DllImport\(DllPath,\s*EntryPoint\s*=\s*"GetPressedKeys"\)\]\s*'
        r"private\s+static\s+extern\s+(\w+)\s+GetPressedKeysWrapper"
        r"\(\s*IntPtr\s+\w+\s*,\s*Int32\s+\w+\s*\)",
        text,
    )
    if match is None:
        errors.append(
            'UI/Interop/InputApi.cs: no `[DllImport(DllPath, EntryPoint = "GetPressedKeys")]` '
            "wrapper taking (IntPtr, Int32) - the native export and the host stopped "
            "agreeing on the call"
        )
    elif match.group(1) != "Int32":
        errors.append(
            f"UI/Interop/InputApi.cs: GetPressedKeysWrapper returns `{match.group(1)}`, but "
            "the export answers an int32_t size of the set - a `void` here discards it and "
            "the growth never happens (#895)"
        )
    if "PressedKeys.Read(" not in text:
        errors.append(
            "UI/Interop/InputApi.cs: the pressed-key read no longer goes through "
            "PressedKeys.Read - a direct call decodes the first buffer only, which is the "
            "silent truncation #895 is about"
        )
    if "keyBuffer.Length" not in text:
        errors.append(
            "UI/Interop/InputApi.cs: the wrapper is no longer called with the buffer's own "
            "length as the capacity"
        )


def check_logic(text: str, errors: list[str]) -> None:
    if not text:
        return
    for constant in ("public const int Capacity =", "public const int MaxKeys ="):
        if constant not in text:
            errors.append(
                f"UI/Logic/PressedKeys.cs: `{constant} ...` is gone - the first-ask size and "
                "the allocation ceiling are the two numbers the read is built on"
            )
    if "Func<ushort[], int> fill" not in text:
        errors.append(
            "UI/Logic/PressedKeys.cs: Read no longer takes the fill as a "
            "`Func<ushort[], int>` - its return value is the size of the set, which is what "
            "the growth is sized from"
        )


def main() -> int:
    errors: list[str] = []
    check_native(read(NATIVE_PATH, errors), errors)
    check_csharp(read(CSHARP_PATH, errors), errors)
    check_logic(read(LOGIC_PATH, errors), errors)

    if errors:
        for err in errors:
            print(f"FAIL: {err}", file=sys.stderr)
        return 1

    print(
        "PASS: the pressed-key read agrees across PressedKeys.cs, InputApi.cs and "
        "InputApiWrapper.cpp (the export answers the set's size, the loop stops at the "
        "caller's capacity, and the host grows through PressedKeys.Read)"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
