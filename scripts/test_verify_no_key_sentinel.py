#!/usr/bin/env python3
"""Unit tests for scripts/checks/verify_no_key_sentinel.py.

The guard exists because nothing host-free can fail on #902: the writer is
ObjC++ behind AppKit and `KeyManager.cpp` is not linked into
`make core-unit-tests`, so a stand-in written for either would pass whatever the
real file does. A guard that cannot fail is worth nothing, so each drift below
is fed to the checker as text and asserted to be reported - and the real tree is
asserted to hold the contract.

Usage:
  python3 scripts/test_verify_no_key_sentinel.py
"""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
CHECK_PATH = REPO_ROOT / "scripts" / "checks" / "verify_no_key_sentinel.py"

INTERFACE_OK = """
	//"No key": the value an empty KeyCombination slot has, and the one code a
	//pressed set must never carry.
	static constexpr uint16_t NoKey = 0;

	static constexpr int BaseMouseButtonIndex = 0x200;

	static vector<uint16_t> WithoutNoKey(const vector<uint16_t>& keyCodes)
	{
		vector<uint16_t> keysOut;
		keysOut.reserve(keyCodes.size());
		for(uint16_t keyCode : keyCodes) {
			if(keyCode != NoKey) {
				keysOut.push_back(keyCode);
			}
		}
		return keysOut;
	}
"""

KEY_MANAGER_OK = """
vector<uint16_t> KeyManager::GetPressedKeys()
{
	if(_keyManager != nullptr) {
		return IKeyManager::WithoutNoKey(_keyManager->GetPressedKeys());
	}
	return vector<uint16_t>();
}
"""

MACOS_OK = """
		if([event type] == NSEventTypeFlagsChanged) {
			HandleModifiers((uint32_t) [event modifierFlags]);
		} else {
			uint16_t mappedKeyCode = [event keyCode] >= 128 ? IKeyManager::NoKey : _keyCodeMap[[event keyCode]];
			if(mappedKeyCode != IKeyManager::NoKey) {
				_keyState[mappedKeyCode] = ([event type] == NSEventTypeKeyDown);
			}
		}
"""

CASES = [
    #(what the drift is, the file it is in, the text, the words the report must carry)
    (
        "the sentinel loses its name",
        "interface",
        INTERFACE_OK.replace("	static constexpr uint16_t NoKey = 0;\n", ""),
        ["no `static constexpr uint16_t NoKey = 0;`"],
    ),
    (
        "the filter drops a different code",
        "interface",
        INTERFACE_OK.replace("keyCode != NoKey", "keyCode != BaseMouseButtonIndex"),
        ["no longer keeps the keys"],
    ),
    (
        "the backend's set stops being filtered",
        "key_manager",
        KEY_MANAGER_OK.replace(
            "return IKeyManager::WithoutNoKey(_keyManager->GetPressedKeys());",
            "return _keyManager->GetPressedKeys();",
        ),
        ["no longer runs the backend's answer through"],
    ),
    (
        "macOS maps to a literal 0 again",
        "macos",
        MACOS_OK.replace(">= 128 ? IKeyManager::NoKey :", ">= 128 ? 0 :"),
        ["no longer answers `IKeyManager::NoKey`"],
    ),
    (
        "macOS writes the sentinel's slot unguarded",
        "macos",
        MACOS_OK.replace("if(mappedKeyCode != IKeyManager::NoKey) {", "if(true) {"),
        ["no longer inside an `if(... NoKey ...)`"],
    ),
]


def load_checker():
    spec = importlib.util.spec_from_file_location("verify_no_key_sentinel", CHECK_PATH)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


def check_all(checker, interface: str, key_manager: str, macos: str) -> list[str]:
    errors: list[str] = []
    checker.check_interface(interface, errors)
    checker.check_key_manager(key_manager, errors)
    checker.check_macos(macos, errors)
    return errors


def real_tree_errors(checker) -> list[str]:
    errors: list[str] = []
    checker.check_interface(CHECK_PATH.parents[2].joinpath("Core", "Shared", "Interfaces", "IKeyManager.h").read_text(encoding="utf-8"), errors)
    checker.check_key_manager(CHECK_PATH.parents[2].joinpath("Core", "Shared", "KeyManager.cpp").read_text(encoding="utf-8"), errors)
    checker.check_macos(CHECK_PATH.parents[2].joinpath("MacOS", "MacOSKeyManager.mm").read_text(encoding="utf-8"), errors)
    return errors


def main() -> int:
    checker = load_checker()
    failures = 0

    for name, which, text, expected in CASES:
        texts = {"interface": INTERFACE_OK, "key_manager": KEY_MANAGER_OK, "macos": MACOS_OK}
        texts[which] = text
        case_errors = check_all(checker, texts["interface"], texts["key_manager"], texts["macos"])
        joined = " | ".join(case_errors)
        if not case_errors:
            print(f"FAIL: the guard stayed silent on: {name}", file=sys.stderr)
            failures += 1
        elif not any(word in joined for word in expected):
            print(f"FAIL: {name} was reported, but not in the words expected: {joined}", file=sys.stderr)
            failures += 1
        else:
            print(f"ok   the guard reports: {name}")

    #The positive control: the checker reports nothing on texts that hold the
    #contract, so a guard that simply always failed would be caught here.
    control = check_all(checker, INTERFACE_OK, KEY_MANAGER_OK, MACOS_OK)
    if control:
        print(f"FAIL: the guard reports the contract-holding text as broken: {control}", file=sys.stderr)
        failures += 1
    else:
        print("ok   the guard is silent on text that holds the contract")

    #And the tree it guards, read from disk.
    real_errors = real_tree_errors(checker)
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
