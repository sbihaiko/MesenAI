#!/usr/bin/env python3
"""Unit tests for scripts/checks/verify_no_key_sentinel.py.

The guard exists because nothing host-free can fail on #902: the writer is
ObjC++ behind AppKit and `KeyManager.cpp` is not linked into
`make core-unit-tests`, so a stand-in written for either would pass whatever the
real file does. A guard that cannot fail is worth nothing - and a guard that
cannot *pass* is worth less, because it fails on every refactor and teaches
everyone to route around it. So this file feeds the checker both ways:

  * each drift below must be reported, in the words it claims to report it in;
  * each refactor below must stay silent - the guard reads statements, so a
    spacing change, a comment between the `if` and the write, Allman braces, a
    renamed local, an unqualified `NoKey` or the early-return form are all the
    same rule to it;
  * and the repo's own three files must hold the contract.

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

#The handler as it is written: the monitor, the modifier branch (whose writes
#index by a constant and are not the table's), the mapping, and the guarded
#write. Both of the guard's arms read this block.
MACOS_OK = """
	NSEventMask eventMask = NSEventMaskKeyDown | NSEventMaskKeyUp | NSEventMaskFlagsChanged;

	_eventMonitor = [NSEvent addLocalMonitorForEventsMatchingMask:eventMask handler:^ NSEvent* (NSEvent* event) {
		if([event type] == NSEventTypeFlagsChanged) {
			HandleModifiers((uint32_t) [event modifierFlags]);
		} else {
			uint16_t mappedKeyCode = [event keyCode] >= 128 ? IKeyManager::NoKey : _keyCodeMap[[event keyCode]];
			if(mappedKeyCode != IKeyManager::NoKey) {
				_keyState[mappedKeyCode] = ([event type] == NSEventTypeKeyDown);
			}
		}

		return nil;
	}];

	_connectObserver = [[NSNotificationCenter defaultCenter] addObserverForName:GCControllerDidConnectNotification object:nil queue:nil usingBlock:^ void (NSNotification* notification) {
		AddController(controller);
	}];

void MacOSKeyManager::HandleModifiers(uint32_t flags)
{
	_keyState[116] = (flags & NX_DEVICELSHIFTKEYMASK) != 0;
	_keyState[70] = (flags & NX_DEVICELCMDKEYMASK) != 0;
}
"""

MAPPING = "uint16_t mappedKeyCode = [event keyCode] >= 128 ? IKeyManager::NoKey : _keyCodeMap[[event keyCode]];"
GUARD = "if(mappedKeyCode != IKeyManager::NoKey) {"
WRITE_LINE = "_keyState[mappedKeyCode] = ([event type] == NSEventTypeKeyDown);"

#(what the drift is, the file it is in, the text, the words the report must carry)
CASES = [
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
        ["no longer compares against"],
    ),
    (
        "the backend's set stops being filtered",
        "key_manager",
        KEY_MANAGER_OK.replace(
            "return IKeyManager::WithoutNoKey(_keyManager->GetPressedKeys());",
            "return _keyManager->GetPressedKeys();",
        ),
        ["no longer *returns*"],
    ),
    (
        "the filter is called and its result discarded",
        "key_manager",
        KEY_MANAGER_OK.replace(
            "return IKeyManager::WithoutNoKey(_keyManager->GetPressedKeys());",
            "IKeyManager::WithoutNoKey(_keyManager->GetPressedKeys());\n\t\treturn _keyManager->GetPressedKeys();",
        ),
        ["no longer *returns*"],
    ),
    (
        "macOS maps to a literal 0 again",
        "macos",
        MACOS_OK.replace(">= 128 ? IKeyManager::NoKey :", ">= 128 ? 0 :"),
        ["the key-code mapping is gone"],
    ),
    (
        "macOS writes the sentinel's slot unguarded",
        "macos",
        MACOS_OK.replace(GUARD + "\n", "").replace("\t\t\t}", "\t\t\t", 1),
        ["is not guarded by a test of"],
    ),
    (
        "the guard is inverted",
        "macos",
        MACOS_OK.replace(GUARD, "if(mappedKeyCode == IKeyManager::NoKey || true) {"),
        ["is not guarded by a test of"],
    ),
    (
        "the guard is short-circuited",
        "macos",
        MACOS_OK.replace(GUARD, "if(mappedKeyCode != IKeyManager::NoKey || true) {"),
        ["is not guarded by a test of"],
    ),
    (
        "the sentinel's own slot is written by a constant",
        "macos",
        MACOS_OK.replace(WRITE_LINE, "_keyState[0] = ([event type] == NSEventTypeKeyDown);"),
        ["writes the sentinel's own slot"],
    ),
    (
        "a second, unguarded write joins the handler",
        "macos",
        MACOS_OK.replace(WRITE_LINE + "\n\t\t\t}", WRITE_LINE + "\n\t\t\t}\n\t\t\t_keyState[[event keyCode] & 0x7F] = true;"),
        ["is not guarded by a test of"],
    ),
    (
        "the event handler moves out of this file",
        "macos",
        MACOS_OK.replace("addLocalMonitorForEventsMatchingMask", "addSomewhereElseEntirely"),
        ["no `addLocalMonitorForEventsMatchingMask` handler"],
    ),
]

#The other half of a guard's job: the same rule written differently. A report on
#any of these is a false positive, and a guard that has them gets deleted.
CLEAN_CASES = [
    (
        "the guard is written with a space before the parenthesis",
        "macos",
        MACOS_OK.replace(GUARD, "if (mappedKeyCode != IKeyManager::NoKey) {"),
    ),
    (
        "a comment sits between the guard and the write",
        "macos",
        MACOS_OK.replace(WRITE_LINE, "//the sentinel is not a key\n\t\t\t" + WRITE_LINE),
    ),
    (
        "the sentinel is tested with an early return instead",
        "macos",
        MACOS_OK.replace(GUARD + "\n\t\t\t\t" + WRITE_LINE + "\n\t\t\t}",
                         "if(mappedKeyCode == IKeyManager::NoKey) { return nil; }\n\t\t\t" + WRITE_LINE),
    ),
    (
        "the local is renamed",
        "macos",
        MACOS_OK.replace("mappedKeyCode", "code"),
    ),
    (
        "the sentinel is spelled unqualified",
        "macos",
        MACOS_OK.replace("IKeyManager::NoKey", "NoKey"),
    ),
    (
        "the brace is on its own line",
        "macos",
        MACOS_OK.replace(GUARD, "if(mappedKeyCode != IKeyManager::NoKey)\n\t\t\t{"),
    ),
    (
        "the filter renames its loop variable",
        "interface",
        INTERFACE_OK.replace("keyCode", "code"),
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


def with_text(which: str, text: str) -> tuple[str, str, str]:
    texts = {"interface": INTERFACE_OK, "key_manager": KEY_MANAGER_OK, "macos": MACOS_OK}
    texts[which] = text
    return texts["interface"], texts["key_manager"], texts["macos"]


def real_tree_errors(checker) -> list[str]:
    root = CHECK_PATH.parents[2]
    errors: list[str] = []
    checker.check_interface(root.joinpath("Core", "Shared", "Interfaces", "IKeyManager.h").read_text(encoding="utf-8"), errors)
    checker.check_key_manager(root.joinpath("Core", "Shared", "KeyManager.cpp").read_text(encoding="utf-8"), errors)
    checker.check_macos(root.joinpath("MacOS", "MacOSKeyManager.mm").read_text(encoding="utf-8"), errors)
    return errors


def main() -> int:
    checker = load_checker()
    failures = 0

    for name, which, text, expected in CASES:
        errors = check_all(checker, *with_text(which, text))
        joined = " | ".join(errors)
        if not errors:
            print(f"FAIL: the guard stayed silent on: {name}", file=sys.stderr)
            failures += 1
        elif not any(word in joined for word in expected):
            print(f"FAIL: {name} was reported, but not in the words expected: {joined}", file=sys.stderr)
            failures += 1
        else:
            print(f"ok   the guard reports: {name}")

    for name, which, text in CLEAN_CASES:
        errors = check_all(checker, *with_text(which, text))
        if errors:
            print(f"FAIL: the guard reports a rewrite of the same rule: {name} - {errors}", file=sys.stderr)
            failures += 1
        else:
            print(f"ok   the guard stays silent on: {name}")

    #The positive control: nothing reported on the texts that hold the contract,
    #so a checker that simply always failed would be caught in the loop above.
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
    print(f"PASS: {len(CASES) + len(CLEAN_CASES) + 2} case(s) - the guard fails on every drift it claims to catch, and on no rewrite")
    return 0


if __name__ == "__main__":
    sys.exit(main())
