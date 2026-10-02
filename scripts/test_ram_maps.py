#!/usr/bin/env python3
"""Every `scripts/stages/<game>/ram-map.json`, checked as data and, when the
ROM is here, re-measured on the pinned dump (F14.19, ADR-0242 Q2).

A RAM map is what `scripts/jev_harness.py` turns into the state a search
steers by and a Jev question is asked over: position, camera, room, HP. The
two maps F14.14/F14.15 shipped (`mm3/`, `ninjagaiden/`) are checked by
`scripts/test_mm3_stage_data.py`; this file checks every map, and adds the
part F14.19 needs: a map written for a game with no committed route carries a
`verification` block, and each of its fields is re-read on the dump.

What is checked on every map, with no ROM:

  * it loads through `jev_harness.RamMap.load` - the only reader - with a
    `progress` field (and a `screen` field, when one is named) among its
    fields, and every address below $0800 (ADR-0184 section 1);
  * a map that pins a ROM pins one its own stage set declares, so a map can
    never describe a different dump than the routes beside it.

What is checked on a map with a `verification` block, with no ROM:

  * every checkpoint plays a valid `<count>f <buttons>` script, starts from a
    `mint-*.txt` the folder ships, and has a unique id;
  * every expectation names a field of the map and is either an exact value
    (`equals`) or a relation (`gt`, `lt`, `eq`, `ne`) to an *earlier*
    checkpoint;
  * every field of the map is expected at two checkpoints at least, and at
    least one of those expectations is a relation - a field is verified by
    watching it move (or hold) between two reads, never by one read alone.

And with the ROM and the built `scripts/headless_record`:

  * the mint is played from power-on for the smallest whole number of seconds
    that covers it (`library_job.mint_seconds_for_frames`, #465), the state is
    loaded into one `step_emu` session, every checkpoint's input is played in
    order (a checkpoint naming its own `mint` starts again from that state),
    and the fields are read through `RamMap.read` - the harness's own read -
    and compared with the expectations.

A missing ROM or binary is a `skip` line naming what is missing, and the file
still exits 0 (the way `scripts/test_step_emu_rom.py` skips), so a CI image
with neither stays green without pretending it measured anything. The ROM is
looked up by its No-Intro SHA1 in `$MESENCE_ROMS` (default: the user's library
folder), never by name, because two dumps of one title can share a name.

Run:  python3 scripts/test_ram_maps.py [--print]
      --print also prints every field at every checkpoint (how the expected
      values were read off the dump in the first place).
"""
import json
import os
import re
import subprocess
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
STAGES = HERE / "stages"
BINARY = HERE / "headless_record"
ROMS = Path(os.environ.get(
    "MESENCE_ROMS",
    "/Users/bihaiko/VSCodeProjects/EMULADORES/2. Switch/G3 - Nitendinho/roms"))
sys.path.insert(0, str(HERE))
import jev_harness  # noqa: E402 - the reader under test
import library_job  # noqa: E402 - the mint arithmetic the job uses

MACRO_LINE = re.compile(r"^(\d+)f ([UDLRABST]+|-)$")
RELATIONS = ("gt", "lt", "eq", "ne")
_FAILURES = []


def check(cond, name, detail=""):
    if cond:
        print(f"ok   {name}")
    else:
        print(f"FAIL {name}: {detail}")
        _FAILURES.append(name)


def load(path):
    return json.loads(Path(path).read_text(encoding="utf-8"))


def maps():
    return sorted(STAGES.glob("*/ram-map.json"))


def check_every_map_loads():
    found = maps()
    check(len(found) >= 4, "the stage sets carry at least four RAM maps",
          str([path.parent.name for path in found]))
    for path in found:
        game = path.parent.name
        try:
            ram = jev_harness.RamMap.load(path)
        except jev_harness.RamMapError as error:
            check(False, f"{game}: the map loads through RamMap.load", str(error))
            continue
        check(ram.progress in ram.fields,
              f"{game}: progress {ram.progress!r} is a field of the map")
        if ram.screen:
            check(ram.screen in ram.fields,
                  f"{game}: screen {ram.screen!r} is a field of the map")
        for field in ram.fields.values():
            for address in (field.address, field.address2, field.range_start,
                            field.range_end):
                if address is not None:
                    check(0 <= address <= 0x7FF,
                          f"{game}: {field.name} ${address:04X} is NES internal RAM")
        document = load(path)
        pins = (document.get("rom") or {}).get("noIntroSha1") or []
        if pins:
            declared = load(path.parent / "stage-set.json")["rom"]["noIntroSha1"]
            check(set(pins) <= set(declared),
                  f"{game}: the map's ROM pin is one the stage set declares",
                  f"{pins} vs {declared}")


def script_lines(lines):
    return [line for line in lines if line.strip() and not line.startswith("#")]


def check_verification_schema(path):
    """The `verification` block's shape; returns the block, or None."""
    game = path.parent.name
    document = load(path)
    block = document.get("verification")
    if block is None:
        return None
    fields = jev_harness.RamMap.load(path).fields
    check(bool((document.get("rom") or {}).get("noIntroSha1")),
          f"{game}: a verified map pins the dump it was verified on")
    checkpoints = block.get("checkpoints") or []
    check(len(checkpoints) >= 2, f"{game}: at least two checkpoints",
          str(len(checkpoints)))
    seen, coverage = [], {name: [] for name in fields}
    for index, point in enumerate(checkpoints):
        ident = point.get("id")
        check(isinstance(ident, str) and ident and ident not in seen,
              f"{game}: checkpoint {index} has a unique id", str(ident))
        mint = point.get("mint") or (block.get("mint") if index == 0 else None)
        if index == 0:
            check(bool(mint), f"{game}: the first checkpoint names its mint")
        if mint:
            check(mint.startswith("mint-") and (path.parent / mint).is_file(),
                  f"{game}: {ident} starts from a mint the folder ships", mint)
        lines = script_lines(point.get("input") or [])
        check(bool(lines), f"{game}: {ident} plays some input")
        for line in lines:
            check(bool(MACRO_LINE.match(line)),
                  f"{game}: {ident} input {line!r} is `<count>f <buttons>`")
        for name, expectation in (point.get("expect") or {}).items():
            check(name in fields, f"{game}: {ident} expects a field of the map",
                  f"{name!r} not in {sorted(fields)}")
            keys = set(expectation)
            check(len(keys) == 1 and keys <= {"equals", *RELATIONS},
                  f"{game}: {ident}.{name} is one `equals` or one relation",
                  str(expectation))
            for relation in keys & set(RELATIONS):
                check(expectation[relation] in seen,
                      f"{game}: {ident}.{name} {relation} names an earlier checkpoint",
                      str(expectation[relation]))
            if name in coverage:
                coverage[name].append(keys)
        seen.append(ident)
    for name, expectations in coverage.items():
        check(len(expectations) >= 2,
              f"{game}: {name} is expected at two checkpoints at least",
              f"{len(expectations)}")
        check(any(keys & set(RELATIONS) for keys in expectations),
              f"{game}: {name} is related to another checkpoint, not read once",
              str(expectations))
    return block


def find_rom(pins):
    """The library file whose No-Intro SHA1 is one of `pins`, or None."""
    if not ROMS.is_dir():
        return None
    wanted = {pin.upper() for pin in pins}
    for candidate in sorted(ROMS.glob("*.nes")):
        if library_job.no_intro_sha1(candidate).upper() in wanted:
            return candidate
    return None


def mint_state(rom, script, out_dir):
    seconds = library_job.mint_seconds_for_frames(library_job.script_frames(script))
    state = out_dir / f"{script.stem}.mss"
    done = subprocess.run(
        [str(BINARY), str(rom), str(seconds), str(out_dir / "mint" / script.stem),
         "mep-off", "hdpack-off", f"input={script}", f"save-state={state}"],
        capture_output=True, text=True)
    if done.returncode != 0 or not state.is_file():
        raise RuntimeError(f"mint {script.name} failed: {done.stdout[-800:]}"
                           f"{done.stderr[-800:]}")
    return state


def holds(relation, value, other):
    return {"gt": value > other, "lt": value < other,
            "eq": value == other, "ne": value != other}[relation]


def measure(path, block, show):
    """Plays the checkpoints on the dump and compares; True when it ran."""
    import step_emu  # noqa: PLC0415 - only needed with a ROM

    game = path.parent.name
    pins = load(path)["rom"]["noIntroSha1"]
    rom = find_rom(pins)
    if rom is None:
        print(f"skip {game}: no ROM with No-Intro SHA1 {pins[0]} in {ROMS} "
              f"(set MESENCE_ROMS to the folder holding it)")
        return False
    ram = jev_harness.RamMap.load(path)
    readings = {}
    with tempfile.TemporaryDirectory(prefix=f"f1419-{game}-") as tmp:
        tmp = Path(tmp)
        states = {}
        with step_emu.StepEmu(rom, work=tmp / "session") as emu:
            for index, point in enumerate(block["checkpoints"]):
                mint = point.get("mint") or (block["mint"] if index == 0 else None)
                if mint:
                    if mint not in states:
                        states[mint] = mint_state(rom, path.parent / mint, tmp)
                    emu.load_file(states[mint])
                lines = script_lines(point["input"])
                frames = sum(int(MACRO_LINE.match(line).group(1)) for line in lines)
                emu.load_script("\n".join(lines) + "\n")
                emu.run_exact(frames)
                state = ram.read(emu)
                readings[point["id"]] = state
                if show:
                    print(f"     {game} {point['id']}: "
                          + ", ".join(f"{name}={state[name]}" for name in ram.fields))
                for name, expectation in point.get("expect", {}).items():
                    (kind, target), = expectation.items()
                    value = state[name]
                    if kind == "equals":
                        check(value == target,
                              f"{game}: at {point['id']} {name} reads {target}",
                              f"read {value}")
                    else:
                        other = readings[target][name]
                        check(holds(kind, value, other),
                              f"{game}: {name} at {point['id']} {kind} {target}",
                              f"{value} vs {other}")
    return True


def main():
    show = "--print" in sys.argv[1:]
    check_every_map_loads()
    blocks = [(path, check_verification_schema(path)) for path in maps()]
    blocks = [(path, block) for path, block in blocks if block]
    check(len(blocks) >= 2,
          "at least two maps carry a verification block (ADR-0242 Q2)",
          str([path.parent.name for path, _ in blocks]))
    if _FAILURES:
        print(f"\n{len(_FAILURES)} failure(s) before any ROM was read")
        return 1
    if not BINARY.exists():
        print(f"skip: {BINARY} is not built (run `make capture-tool`), so no "
              "map was re-measured on its dump")
    else:
        for path, block in blocks:
            measure(path, block, show)
    print(f"\n{len(_FAILURES)} failure(s)")
    return 1 if _FAILURES else 0


if __name__ == "__main__":
    sys.exit(main())
