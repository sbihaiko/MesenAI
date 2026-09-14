#!/usr/bin/env python3
"""Tests for scripts/record_navigation_sweep.py.

ADR-0184's own Consequences ask for this: "The rule is checkable and should be
checked. `AAAA < 0x0800` plus a type allow-list is two comparisons; a test that
feeds a Game Genie code and a `NesCustom` code at `$8000` and asserts both are
refused is cheap, and it is what keeps the rule from decaying into a comment."
The sweep validates ahead of `headless_record` so a bad plan never spawns a
process, which means the rule now lives in two places and both must be pinned.

Run: python3 scripts/test_record_navigation_sweep.py
"""
import json
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import record_navigation_sweep as sweep  # noqa: E402

PASSED = []
FAILED = []


def check(cond, name):
    (PASSED if cond else FAILED).append(name)


def raises(fn, name, needle=None):
    try:
        fn()
    except ValueError as exc:
        check(needle is None or needle in str(exc), name)
        return
    FAILED.append(name + " (no ValueError)")


# --- ADR-0184 s1 -------------------------------------------------------------
check(sweep.validate_ram_cheat("0030:03") == "0030:03", "NesCustom RAM code accepted")
check(sweep.validate_ram_cheat("0032:99:FF") == "0032:99:FF", "compare byte accepted")
check(sweep.validate_ram_cheat("07FF:01") == "07FF:01", "last RAM address accepted")
raises(lambda: sweep.validate_ram_cheat("SXKVPZAX"),
       "Game Genie letter code refused", "AAAA:VV")
raises(lambda: sweep.validate_ram_cheat("8000:99"),
       "PRG address refused", "$8000")
raises(lambda: sweep.validate_ram_cheat("0800:01"),
       "first address above RAM refused", "$0800")
raises(lambda: sweep.validate_ram_cheat("30:03"), "short address refused")
raises(lambda: sweep.validate_ram_cheat("0030:3"), "short value refused")
raises(lambda: sweep.validate_ram_cheat("00G0:03"), "non-hex refused")

# --- input script arithmetic -------------------------------------------------
with tempfile.TemporaryDirectory() as td:
    tmp = Path(td)
    (tmp / "entry.txt").write_text("# a comment\n\n300f -\n1f T\n")
    (tmp / "body.txt").write_text("60f R\n1s RB\n")  # 60 + round(60.0988)
    check(sweep.script_frames(tmp / "entry.txt") == 301, "entry frames, comments skipped")
    check(sweep.script_frames(tmp / "body.txt") == 120, "'s' step resolved at 60.0988")

    info = sweep.build_sweep_script(tmp / "entry.txt", tmp / "body.txt", 10.0,
                                    tmp / "out.txt")
    # 10 s = 601 frames; 301 of entry leaves 300 to cover with a 120 f body.
    check(info["repeats"] == 3, "body repeated to cover the run")
    check(info["totalFrames"] >= info["targetFrames"], "generated script covers the run")
    text = (tmp / "out.txt").read_text()
    check(text.count("60f R") == 3, "body written once per repeat")
    check(text.count("300f -") == 1, "entry written exactly once")
    check(sweep.script_frames(tmp / "out.txt") == info["totalFrames"],
          "generated script re-parses to its declared length")

# --- the plan ----------------------------------------------------------------
PROFILE = Path(__file__).resolve().parent / "stages" / "contra" / "navigation.json"
with tempfile.TemporaryDirectory() as td:
    tmp = Path(td)
    profile = json.loads(PROFILE.read_text())
    plan = sweep.build_plan(profile, PROFILE.parent, tmp / "rom.nes", tmp / "out",
                            tmp / "states", 300, set())
    check(len(plan) == 11, "Contra profile plans 11 sessions")
    navs = [s for s in plan if s["kind"] == "navigation"]
    rooms = [s for s in plan if s["kind"] == "room"]
    check(len(navs) == 8 and len(rooms) == 3, "8 stages + 3 rooms")
    check([s["cheat"] for s in navs] ==
          [f"0030:0{i}" for i in range(8)], "one cheat per declared value")
    check(all(s["cheat"] is None for s in rooms),
          "a room carries no cheat - $30 selects a level, not a room")
    # ADR-0184: "the code travels with the art, verbatim and with its address"
    check(all(s["cheat"] in s["note"] for s in navs), "every note quotes its cheat verbatim")
    check(all("$0030" in s["note"] or "0030:" in s["note"] for s in navs),
          "every note names the address")
    check(all("DataCrystal" in s["note"] for s in navs),
          "every note cites the published RAM map (ADR-0184 s5)")

    only = sweep.build_plan(profile, PROFILE.parent, tmp / "rom.nes", tmp / "out",
                            tmp / "states", 60, {"stage3", "stage1-boss"})
    check([s["name"] for s in only] == ["stage3", "stage1-boss"], "--only filters the plan")

    # A profile whose address is out of RAM is refused before anything runs.
    bad = json.loads(PROFILE.read_text())
    bad["navigation"]["address"] = "8000"
    raises(lambda: sweep.build_plan(bad, PROFILE.parent, tmp / "rom.nes", tmp / "out",
                                    None, 60, set()),
           "a profile with a PRG address is refused at plan time", "$8000")

# --- coverage ----------------------------------------------------------------
with tempfile.TemporaryDirectory() as td:
    tmp = Path(td)
    hires = tmp / "hires.txt"
    hires.write_text(
        "<ver>106\n<scale>2\n"
        "<condition>c1,memoryCheckConstant,30,==,3\n"
        "<tile>0,AABB,FF202616,0,0,1,N\n"
        "<tile>0,AABB,FF303030,8,0,1,N\n"   # same shape, other palette
        "[c1]<tile>0,CCDD,FF202616,16,0,1,N\n"  # gated rule still counts
        "<img>whatever.png\n")
    keys = sweep.tile_data_keys(hires)
    check(keys == {"AABB", "CCDD"}, "coverage unit is tileData, gates included")
    check(sweep.tile_data_keys(tmp / "nope.txt") == set(), "missing pack is empty, not fatal")

    pack = tmp / "s1" / "Rom" / "auto" / "textures"
    pack.mkdir(parents=True)
    (pack / "hires.txt").write_text("<tile>0,AABB,FF202616,0,0,1,N\n")
    check(sweep.find_pack_hires(tmp / "s1") == pack / "hires.txt",
          "the bootstrap pack is found beside the ROM")

print(f"{len(PASSED)}/{len(PASSED) + len(FAILED)} passed")
for f in FAILED:
    print(f"  FAIL {f}")
sys.exit(1 if FAILED else 0)
