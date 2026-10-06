#!/usr/bin/env python3
"""Unit tests for scripts/checks/verify_pressed_keys_contract.py.

The guard exists because `UI.Tests/Input/PressedKeysTests.cs` runs against
stand-ins for the native export: a native loop that stopped at the old literal,
or one that answered `min(size, capacity)` instead of `size`, passes every case
in that file. A guard that cannot fail on those is worth nothing, so each one
below is fed to the checker as text and asserted to be reported - and the real
tree is asserted to pass.

Usage:
  python3 scripts/test_verify_pressed_keys_contract.py
"""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
CHECK_PATH = REPO_ROOT / "scripts" / "checks" / "verify_pressed_keys_contract.py"

NATIVE_OK = """
	DllExport int32_t __stdcall GetPressedKeys(uint16_t* keyBuffer, int32_t capacity)
	{
		if(capacity < 0) {
			capacity = 0;
		}
		vector<uint16_t> pressedKeys = KeyManager::GetPressedKeys();
		for(size_t i = 0; i < pressedKeys.size() && i < (size_t)capacity; i++) {
			keyBuffer[i] = pressedKeys[i];
		}
		return (int32_t)pressedKeys.size();
	}
"""

CSHARP_OK = """
		[DllImport(DllPath, EntryPoint = "GetPressedKeys")] private static extern Int32 GetPressedKeysWrapper(IntPtr keyBuffer, Int32 capacity);
		public static unsafe List<UInt16> GetPressedKeys()
		{
			return PressedKeys.Read((UInt16[] keyBuffer) => {
				fixed(UInt16* ptr = keyBuffer) {
					return InputApi.GetPressedKeysWrapper((IntPtr)ptr, keyBuffer.Length);
				}
			});
		}
"""

LOGIC_OK = """
	public const int Capacity = 32;
	public const int MaxKeys = 4096;
	public static List<ushort> Read(Func<ushort[], int> fill)
"""

CASES = [
    #(what the drift is, the file it is in, the text, the words the report must carry)
    (
        "the export goes back to void",
        "native",
        NATIVE_OK.replace("DllExport int32_t ", "DllExport void "),
        ["returns void"],
    ),
    (
        "the export answers how many slots fit, not the set's size",
        "native",
        NATIVE_OK.replace("return (int32_t)pressedKeys.size();", "return (int32_t)min(pressedKeys.size(), (size_t)capacity);"),
        ["no longer ends in"],
    ),
    (
        "the copy loop stops at a literal again",
        "native",
        NATIVE_OK.replace("i < (size_t)capacity", "i < 3"),
        ["no longer stops at the caller's"],
    ),
    (
        "the host declares the wrapper as void",
        "csharp",
        CSHARP_OK.replace("extern Int32 GetPressedKeysWrapper", "extern void GetPressedKeysWrapper"),
        ["returns `void`"],
    ),
    (
        "the host stops going through Read",
        "csharp",
        CSHARP_OK.replace("return PressedKeys.Read((UInt16[] keyBuffer) => {", "return PressedKeys.Decode(new UInt16[PressedKeys.Capacity]);")
        .replace("""				fixed(UInt16* ptr = keyBuffer) {
					return InputApi.GetPressedKeysWrapper((IntPtr)ptr, keyBuffer.Length);
				}""", ""),
        ["no longer goes through"],
    ),
    (
        "the logic layer loses the ceiling",
        "logic",
        LOGIC_OK.replace("	public const int MaxKeys = 4096;\n", ""),
        ["MaxKeys"],
    ),
]


def load_checker():
    spec = importlib.util.spec_from_file_location("verify_pressed_keys_contract", CHECK_PATH)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


def check(checker, which: str, native: str, csharp: str, logic: str) -> list[str]:
    errors: list[str] = []
    if which == "native":
        checker.check_native(native, errors)
    elif which == "csharp":
        checker.check_csharp(csharp, errors)
    else:
        checker.check_logic(logic, errors)
    return errors


def main() -> int:
    checker = load_checker()
    failures = 0

    for name, which, text, expected in CASES:
        native = text if which == "native" else NATIVE_OK
        csharp = text if which == "csharp" else CSHARP_OK
        logic = text if which == "logic" else LOGIC_OK
        errors = check(checker, which, native, csharp, logic)
        joined = " | ".join(errors)
        if not errors:
            print(f"FAIL: the guard stayed silent on: {name}", file=sys.stderr)
            failures += 1
        elif not any(word in joined for word in expected):
            print(f"FAIL: {name} was reported, but not in the words expected: {joined}", file=sys.stderr)
            failures += 1
        else:
            print(f"ok   the guard reports: {name}")

    #The positive control: the checker reports nothing on texts that hold the
    #contract, so a guard that simply always failed would be caught here.
    errors = check(checker, "native", NATIVE_OK, "", "") + check(checker, "csharp", "", CSHARP_OK, "") + check(checker, "logic", "", "", LOGIC_OK)
    if errors:
        print(f"FAIL: the guard reports the contract-holding text as broken: {errors}", file=sys.stderr)
        failures += 1
    else:
        print("ok   the guard is silent on text that holds the contract")

    #And the tree it guards, read from disk.
    real_errors: list[str] = []
    checker.check_native(CHECK_PATH.parents[2].joinpath("InteropDLL", "InputApiWrapper.cpp").read_text(encoding="utf-8"), real_errors)
    checker.check_csharp(CHECK_PATH.parents[2].joinpath("UI", "Interop", "InputApi.cs").read_text(encoding="utf-8"), real_errors)
    checker.check_logic(CHECK_PATH.parents[2].joinpath("UI", "Logic", "PressedKeys.cs").read_text(encoding="utf-8"), real_errors)
    if real_errors:
        print(f"FAIL: the repo's own files do not hold the contract: {real_errors}", file=sys.stderr)
        failures += 1
    else:
        print("ok   the repo's own three files hold the contract")

    if failures:
        print(f"FAIL: {failures} case(s) did not hold")
        return 1
    print(f"PASS: {len(CASES) + 2} case(s) - the guard fails on every drift it claims to catch")
    return 0


if __name__ == "__main__":
    sys.exit(main())
