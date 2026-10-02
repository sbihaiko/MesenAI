#!/usr/bin/env python3
"""Parity of scripts/cheat_decoder.py with the Core (ADR-0248 section 3).

The cheat-submitted gate decodes codes in Python because CI has no emulator.
This test is what keeps that port honest: it compiles
scripts/cheat_decode_dump.cpp against the unmodified
Core/Shared/CheatManager.cpp, feeds both decoders the same codes, and requires
the same answer for every one - refused, or the same type, CPU, address,
value, compare, RAM/absolute flags and memory type.

Checks:
  P-1 every one of the 9 829 entries of UI/Dependencies/Internal/CheatDb.Nes.json
      decodes the same in Python and in the Core, each part with the type the
      UI gives the entry (CheatTypeDetector), malformed parts included (both
      must refuse them).
  P-2 a deterministic sample for every ported cheat type (well-formed and
      malformed strings, mixed case) decodes the same - the bundled list is
      NES only, so this is the GB/SMS evidence.

A missing C++ compiler is a failure, not a skip: without the Core build the
parity is unproven. Override the compiler with CXX=...

Usage: python3 scripts/test_cheat_decoder_parity.py
"""
from __future__ import annotations

import json
import os
import random
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

SCRIPTS = Path(__file__).resolve().parent
ROOT = SCRIPTS.parent
sys.path.insert(0, str(SCRIPTS))
import cheat_decoder as cd  # noqa: E402

BUNDLED = ROOT / "UI" / "Dependencies" / "Internal" / "CheatDb.Nes.json"
EXPECTED_ENTRIES = 9829
# The Core sources the harness links: the decoder itself, plus what
# CheatManager.cpp and MessageManager.cpp reach at link time.
SOURCES = [
    "scripts/cheat_decode_dump.cpp",
    "Core/Shared/CheatManager.cpp",
    "Core/Shared/MessageManager.cpp",
    "Utilities/HexUtilities.cpp",
    "Utilities/SimpleLock.cpp",
    "Utilities/FolderUtilities.cpp",
    "Utilities/UTF8Util.cpp",
    "Utilities/Timer.cpp",
]
# No -Werror: this build exists to run the Core's decoder, and a new compiler's
# new warning must not read as a parity failure (core-unit-tests owns -Werror).
FLAGS = ["-std=c++17", "-O1", "-Wall", "-Wno-deprecated-declarations",
         "-I", ".", "-I", "Core", "-I", "Utilities"]

FAILURES = []


def fail(msg):
    FAILURES.append(msg)
    print(f"FAIL: {msg}")


def ok(msg):
    print(f"ok: {msg}")


def build_harness(out_dir):
    cxx = os.environ.get("CXX") or shutil.which("c++") or shutil.which("g++") or shutil.which("clang++")
    if not cxx:
        raise SystemExit("FAIL: no C++ compiler (set CXX): the Core side of the parity cannot be built")
    exe = Path(out_dir) / "cheat_decode_dump"
    cmd = [cxx, *FLAGS, *SOURCES, "-o", str(exe)]
    if sys.platform.startswith("linux"):
        cmd.append("-pthread")
    proc = subprocess.run(cmd, cwd=ROOT, capture_output=True, text=True, check=False)
    if proc.returncode != 0:
        raise SystemExit(f"FAIL: building the parity harness failed:\n{proc.stderr[-4000:]}")
    return exe


def core_decode(exe, pairs):
    """[(cheat_type, code)] -> the harness's line for each, in order."""
    stdin = "".join(f"{t}\t{c}\n" for t, c in pairs)
    proc = subprocess.run([str(exe)], input=stdin, capture_output=True, text=True, check=False)
    if proc.returncode != 0:
        raise SystemExit(f"FAIL: harness exited {proc.returncode}: {proc.stderr[-2000:]}")
    lines = proc.stdout.splitlines()
    if len(lines) != len(pairs):
        raise SystemExit(f"FAIL: harness answered {len(lines)} lines for {len(pairs)} codes")
    return lines


def python_line(cheat_type, code):
    d = cd.decode(cheat_type, code)
    if d is None:
        return "invalid"
    return (f"ok\t{d.type}\t{d.cpu}\t{d.address:X}\t{d.value:X}\t{d.compare}\t"
            f"{int(d.is_ram)}\t{int(d.is_abs)}\t{d.mem_type}")


def bundled_pairs():
    games = json.loads(BUNDLED.read_text(encoding="utf-8-sig"))["games"]
    entries = []
    for game in games:
        for cheat in game["cheats"]:
            code = cheat["code"]
            t = cd.bundled_type(code)
            entries.append([(t, part) for part in cd.bundled_parts(code)])
    return entries


def sample_pairs(seed=0x248):
    rng = random.Random(seed)
    hexd = "0123456789abcdefABCDEF"
    gg = cd._GG_LETTERS + cd._GG_LETTERS.lower()
    junk = hexd + "GHKLNOPSTUVXYZ:-+ "

    def rand(alphabet, n):
        return "".join(rng.choice(alphabet) for _ in range(n))

    shaped = {
        cd.NES_GAME_GENIE: lambda: rand(gg, rng.choice((6, 8))),
        cd.NES_PRO_ACTION_ROCKY: lambda: rand(hexd, 8),
        cd.NES_CUSTOM: lambda: rand(hexd, 4) + ":" + rand(hexd, 2) + (":" + rand(hexd, 2) if rng.random() < 0.5 else ""),
        cd.GB_GAME_GENIE: lambda: rand(hexd, 3) + "-" + rand(hexd, 3) + ("-" + rand(hexd, 3) if rng.random() < 0.5 else ""),
        # The first byte decides the GameShark branch: 01, 80-8F and the rest.
        cd.GB_GAME_SHARK: lambda: rng.choice(("01", "80", "81", "8F", "90", "00", rand(hexd, 2))) + rand(hexd, 6),
        cd.SMS_PRO_ACTION_REPLAY: lambda: rand(hexd, 8),
        cd.SMS_GAME_GENIE: lambda: rand(hexd, 3) + "-" + rand(hexd, 3) + ("-" + rand(hexd, 3) if rng.random() < 0.5 else ""),
    }
    pairs = []
    for t, make in shaped.items():
        for _ in range(1500):
            pairs.append((t, make()))
        for _ in range(500):
            pairs.append((t, rand(junk, rng.randint(0, 18))))
        # Every shape of every other type, so a type never accepts a sibling's code.
        for other in shaped.values():
            for _ in range(40):
                pairs.append((t, other()))
    return pairs


def main():
    with tempfile.TemporaryDirectory() as tmp:
        exe = build_harness(tmp)

        entries = bundled_pairs()
        if len(entries) != EXPECTED_ENTRIES:
            fail(f"the bundled list has {len(entries)} entries, expected {EXPECTED_ENTRIES}")
        flat = [p for e in entries for p in e]
        core = core_decode(exe, flat)
        i = 0
        agree = 0
        decodable = 0
        mismatches = []
        for entry in entries:
            same = True
            all_ok = bool(entry)
            for t, code in entry:
                py = python_line(t, code)
                if py != core[i]:
                    same = False
                    mismatches.append((t, code, py, core[i]))
                all_ok = all_ok and core[i] != "invalid"
                i += 1
            agree += same
            decodable += all_ok
        for t, code, py, c in mismatches[:10]:
            fail(f"bundled code {code!r} (type {t}): python {py!r} != core {c!r}")
        if agree == len(entries):
            ok(f"P-1 {agree}/{len(entries)} bundled entries ({len(flat)} parts) decode identically "
               f"in Python and the Core; {decodable} decode in full, {len(entries) - decodable} are refused by both")
        else:
            fail(f"P-1 only {agree}/{len(entries)} bundled entries agree")

        sample = sample_pairs()
        core = core_decode(exe, sample)
        bad = [(t, c, python_line(t, c), k) for (t, c), k in zip(sample, core) if python_line(t, c) != k]
        for t, code, py, c in bad[:10]:
            fail(f"sample code {code!r} (type {t}): python {py!r} != core {c!r}")
        accepted = sum(1 for k in core if k != "invalid")
        if not bad:
            ok(f"P-2 {len(sample)}/{len(sample)} sampled codes over {len(cd.TYPE_NAMES)} types agree "
               f"({accepted} decoded, {len(sample) - accepted} refused by both)")

    if FAILURES:
        print(f"{len(FAILURES)} failure(s)")
        return 1
    print("all parity checks passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
