#!/usr/bin/env python3
"""ADR-0244 (P.9): the in-place pack change stays exact, and the GUI uses it.

Two halves:

  * Static, always run: the UI's `InPlaceReloadResult` (UI/Logic/
    PackChangePolicy.cs) has the values of Core's (Core/Shared/Emulator.h) -
    the byte the `ReloadRomKeepingState` export returns - and the Enhancements
    panel's Textures/Audio/Border toggles and the picker's Apply both go through
    `LoadRomHelper.ApplyPackChange` (the in-place path) and no longer straight to
    a reload or a power cycle.
  * Measured, when the tool is built: `scripts/pack_swap_exactness.py` per
    console, every transition holding the verdict ADR-0244's Status line records
    (all PASS, the negative control FAIL, the ROM-patch transitions
    `patch-restarted`). NES needs Ninja Gaiden and SMS needs Sonic from the
    user's library (never copied into this repo); GB and GBC run on the
    synthetic ROMs `gen_hdpack_test_roms.py` writes. A missing ROM or binary prints `skip` for
    that console and asserts nothing about it.

Run:  python3 scripts/test_pack_swap_exactness.py [--console nes|sms|gb|gbc ...]
"""
import argparse
import json
import re
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
sys.path.insert(0, str(HERE))
import pack_swap_exactness as harness  # noqa: E402

FAILURES = []


def check(condition, message):
    print(("ok   " if condition else "FAIL ") + message)
    if not condition:
        FAILURES.append(message)


def enum_values(text, name):
    body = re.search(r"enum (?:class )?" + name + r"\s*:\s*\w+\s*\{(.*?)\}", text, re.S)
    if not body:
        return {}
    code = re.sub(r"//[^\n]*", "", body.group(1))
    return {k: int(v) for k, v in re.findall(r"(\w+)\s*=\s*(\d+)", code)}


def static_checks():
    core = enum_values((ROOT / "Core/Shared/Emulator.h").read_text(), "InPlaceReloadResult")
    ui = enum_values((ROOT / "UI/Logic/PackChangePolicy.cs").read_text(), "InPlaceReloadResult")
    check(len(core) == 4 and core == ui, f"InPlaceReloadResult matches Core ({core}) and UI ({ui})")
    vm = (ROOT / "UI/ViewModels/MainWindowViewModel.cs").read_text()
    toggle = re.search(r"private bool ToggleLayer\(.*?\n\t\t\}", vm, re.S)
    pick = re.search(r"public void PickPlayerPack\(.*?\n\t\t\}", vm, re.S)
    check(bool(toggle) and "LoadRomHelper.ApplyPackChange(" in toggle.group(0)
          and "LoadRomHelper.ReloadRom()" not in toggle.group(0),
          "ToggleLayer applies through LoadRomHelper.ApplyPackChange")
    check(bool(pick) and "LoadRomHelper.ApplyPackChange(" in pick.group(0)
          and "LoadRomHelper.PowerCycle()" not in pick.group(0),
          "PickPlayerPack applies through LoadRomHelper.ApplyPackChange")
    helper = (ROOT / "UI/Utilities/LoadRomHelper.cs").read_text(encoding="utf-8-sig")
    check("EmuApi.ReloadRomKeepingState()" in helper and "PackChangePolicy.Plan(" in helper
          and "PackChangePolicy.Outcome(" in helper,
          "ApplyPackChange asks PackChangePolicy before and after the native swap")


def measured(console):
    with tempfile.TemporaryDirectory(prefix=f"p9-test-{console}-") as tmp:
        out = Path(tmp) / "result.json"
        code = harness.main(["pack_swap_exactness.py", "--console", console,
                             "--work", str(Path(tmp) / "work"), "--json", str(out)])
        if not out.exists():
            print(f"skip {console}: the harness ran nothing (exit {code})")
            return
        results = json.loads(out.read_text())["results"]
    check(code == 0, f"{console}: the harness exits 0 (exit {code})")
    for r in results:
        check(r["pass"] == r["expected_pass"] and r["outcome"] == r["expected_outcome"],
              f"{console}: {r['name']}: {'PASS' if r['pass'] else 'FAIL'} / {r['outcome']} "
              f"(expected {'PASS' if r['expected_pass'] else 'FAIL'} / {r['expected_outcome']})")
    check(len(results) == len(harness.transitions(harness.PROFILES[console])),
          f"{console}: every transition ran ({len(results)})")


def main(argv):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--console", action="append", choices=sorted(harness.PROFILES))
    a = ap.parse_args(argv[1:])
    static_checks()
    for console in a.console or ["nes", "sms", "gb", "gbc"]:
        measured(console)
    print(f"{len(FAILURES)} failure(s)")
    return 1 if FAILURES else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
