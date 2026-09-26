#!/usr/bin/env python3
"""Tests for scripts/record_navigation_sweep.py.

ADR-0184's own Consequences ask for this: "The rule is checkable and should be
checked. `AAAA < 0x0800` plus a type allow-list is two comparisons; a test that
feeds a Game Genie code and a `NesCustom` code at `$8000` and asserts both are
refused is cheap, and it is what keeps the rule from decaying into a comment."
The sweep validates ahead of `headless_record` so a bad plan never spawns a
process, which means the rule now lives in two places and both must be pinned.

ADR-0239 §2, §4 and §5 grew the profile (an input selector, per-value scripts,
extra RAM pins, a RAM check) and the report (a session's `new` count and status,
the union against the ROM's own CHR). Those cases are wrapped in `case()`, so a
missing function reports itself as a named failure instead of a traceback that
hides every case after it.

Run: python3 scripts/test_record_navigation_sweep.py
"""
import contextlib
import io
import json
import struct
import sys
import tempfile
import zlib
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import record_navigation_sweep as sweep  # noqa: E402

PASSED = []
FAILED = []


def check(cond, name, detail=None):
    """`detail` is appended only to a failure, where it is what a reader needs."""
    (PASSED if cond else FAILED).append(
        name if cond or detail is None else f"{name} [{detail}]")


def raises(fn, name, needle=None):
    try:
        fn()
    except ValueError as exc:
        check(needle is None or needle in str(exc), name)
        return
    FAILED.append(name + " (no ValueError)")


def case(fn, name):
    """Run one ADR-0239 case; anything raised inside is that case's failure."""
    try:
        fn()
    except ValueError as exc:
        FAILED.append(f"{name}: ValueError: {exc}")
    except Exception as exc:  # noqa: BLE001 - the failure names its own cause
        FAILED.append(f"{name}: {type(exc).__name__}: {exc}")


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


# --- ADR-0239 s2: kind, per-value scripts, extra pins, the RAM check ---------
SCRIPTS = {
    "entry.txt": "300f -\n",
    "body.txt": "60f R\n",
    "alt-entry.txt": "120f S\n",
    "alt-body.txt": "30f A\n",
}


def _scripts(d: Path) -> Path:
    for name, text in SCRIPTS.items():
        (d / name).write_text(text)
    return d


def _plan(profile, prof_dir, tmp, only=None, seconds=120):
    return sweep.build_plan(profile, prof_dir, tmp / "rom.nes", tmp / "out",
                            tmp / "states", seconds, only or set())


def _profile(nav, defaults=None, rooms=None, game="Fake"):
    p = {"game": game, "navigation": nav}
    if defaults is not None:
        p["defaults"] = defaults
    if rooms is not None:
        p["rooms"] = rooms
    return p


def _input_kind_plan():
    """ADR-0239 s2: `kind: "input"` - no address, the value's entry selects."""
    with tempfile.TemporaryDirectory() as td:
        tmp = Path(td)
        prof = _scripts(tmp)
        profile = _profile(
            {"kind": "input", "label": "track select", "source": "manual p.7",
             "values": [{"name": "track1", "title": "Track 1", "entry": "entry.txt"},
                        {"name": "track3", "title": "Track 3", "entry": "alt-entry.txt"}]},
            {"seconds": 120, "entry": "entry.txt", "body": "body.txt"})
        plan = _plan(profile, prof, tmp)
        check(len(plan) == 2, "an input profile plans one session per value")
        check(all(s["cheat"] is None for s in plan),
              "an input selector pins no cheat - the entry script is the selector")
        check(all(s["cheats"] == [] for s in plan), "and carries no cheat list")
        check([s["entry"].name for s in plan] == ["entry.txt", "alt-entry.txt"],
              "each value's own entry script is the one that runs")
        check(all(s["body"].name == "body.txt" for s in plan),
              "the body still comes from defaults")
        check(all("input" in s["note"] and "manual p.7" in s["note"] for s in plan),
              "the note names the input selector and its published source")
        check(all("RAM cheat" not in s["note"] for s in plan),
              "no note claims a RAM pin the profile never declared")
        cmd = sweep.command_for(plan[1], tmp / "rom.nes", {"path": "/x/in.txt"})
        check("input=/x/in.txt" in cmd and not any(a.startswith("cheat=") for a in cmd),
              "the command line runs the generated script and no cheat")


def _kind_is_explicit():
    """`kind` defaults to "ram", and saying it out loud changes nothing."""
    with tempfile.TemporaryDirectory() as td:
        tmp = Path(td)
        prof = _scripts(tmp)
        profile = json.loads(PROFILE.read_text())
        plan = _plan(profile, PROFILE.parent, tmp, seconds=300)
        loud = json.loads(PROFILE.read_text())
        loud["navigation"]["kind"] = "ram"
        check(_plan(loud, PROFILE.parent, tmp, seconds=300) == plan,
              "an explicit kind: ram builds the identical plan")
    raises(lambda: sweep.build_plan(
        {"game": "F", "navigation": {"kind": "teleport", "address": "0030",
                                     "values": [{"value": "01", "name": "s"}]}},
        Path("."), Path("r.nes"), Path("o"), None, 60, set()),
        "an unknown navigation.kind is refused, not ignored", "kind")
    raises(lambda: sweep.build_plan(
        {"game": "F", "navigation": {"label": "l", "values": [{"name": "a"}]}},
        Path("."), Path("r.nes"), Path("o"), None, 60, set()),
        "a ram profile without an address is refused", "address")


def _contra_plan_is_unchanged():
    """ADR-0239 §2 is backward compatible: Contra's sessions are the ones the
    amendment measured, field for field."""
    with tempfile.TemporaryDirectory() as td:
        tmp = Path(td)
        profile = json.loads(PROFILE.read_text())
        plan = _plan(profile, PROFILE.parent, tmp, seconds=300)
        nav0 = {k: plan[0][k] for k in
                ("name", "kind", "title", "cheat", "entry", "body", "state",
                 "input", "seconds", "prefix")}
        check(nav0 == {"name": "stage1", "kind": "navigation", "title": "Jungle",
                       "cheat": "0030:00",
                       "entry": PROFILE.parent / "mint-stage1-30lives.txt",
                       "body": PROFILE.parent / "stage1-run.txt",
                       "state": None, "input": None, "seconds": 300,
                       "prefix": tmp / "out" / "stage1" / "rec"},
              "the stage-1 session keeps every field it had")
        room0 = {k: plan[8][k] for k in
                 ("name", "kind", "title", "cheat", "cheats", "state", "input")}
        check(room0 == {"name": "stage1-boss", "kind": "room",
                        "title": "Stage 1 wall core", "cheat": None, "cheats": [],
                        "state": tmp / "states" / "stage1-boss.mss",
                        "input": PROFILE.parent / "stage1-boss.txt"},
              "the boss room is still a no-cheat session")
        check(all(s["ramCheck"] is None for s in plan),
              "no profile without a ramCheck grows one")


def _per_value_scripts():
    """ADR-0239 §2: values[].entry / values[].body override the defaults."""
    with tempfile.TemporaryDirectory() as td:
        tmp = Path(td)
        prof = _scripts(tmp)
        profile = _profile(
            {"address": "0030", "label": "level", "source": "map",
             "values": [{"value": "01", "name": "a"},
                        {"value": "02", "name": "b", "entry": "alt-entry.txt",
                         "body": "alt-body.txt"}]},
            {"seconds": 120, "entry": "entry.txt", "body": "body.txt"})
        plan = _plan(profile, prof, tmp)
        check(plan[0]["entry"].name == "entry.txt" and plan[0]["body"].name == "body.txt",
              "a value without overrides uses the defaults")
        check(plan[1]["entry"].name == "alt-entry.txt"
              and plan[1]["body"].name == "alt-body.txt",
              "a value overrides entry and body per value")


def _extra_pins():
    """ADR-0239 §2/§3: defaults.cheats and values[].cheats are RAM-only pins
    validated before anything runs, merged into the session and quoted in the
    note (ADR-0184 §2: "the code travels with the art, verbatim and with its
    address")."""
    with tempfile.TemporaryDirectory() as td:
        tmp = Path(td)
        prof = _scripts(tmp)
        profile = _profile(
            {"address": "0030", "label": "level", "source": "map",
             "values": [{"value": "01", "name": "a",
                         "cheats": [{"code": "00A2:FF", "label": "hp",
                                     "source": "TCRF RAM map"}]},
                        {"value": "02", "name": "b"}]},
            {"seconds": 120, "entry": "entry.txt", "body": "body.txt",
             "cheats": [{"code": "002A:09", "label": "lives",
                         "source": "DataCrystal RAM map"}]})
        plan = _plan(profile, prof, tmp)
        check(plan[0]["cheats"] == ["0030:01", "002A:09", "00A2:FF"],
              "the selector first, then defaults.cheats, then the value's")
        check(plan[0]["cheat"] == "0030:01",
              "the selector stays in its own field")
        check(plan[1]["cheats"] == ["0030:02", "002A:09"],
              "a value without its own pins gets the defaults' alone")
        check(all(c in plan[0]["note"] for c in plan[0]["cheats"]),
              "every pin is quoted verbatim in the note, selector included")
        check("lives" in plan[0]["note"] and "DataCrystal RAM map" in plan[0]["note"]
              and "TCRF RAM map" in plan[0]["note"],
              "and with the label and the source that names it (ADR-0184 s5)")
        cmd = sweep.command_for(plan[0], tmp / "rom.nes", {"path": "/x/i.txt"})
        check([a for a in cmd if a.startswith("cheat=")] ==
              ["cheat=0030:01", "cheat=002A:09", "cheat=00A2:FF"],
              "the command line carries every pin, selector first")

        bad = json.loads(json.dumps(profile))
        bad["defaults"]["cheats"] = [{"code": "SXKVPZAX", "label": "x", "source": "s"}]
        raises(lambda: _plan(bad, prof, tmp),
               "a Game Genie pin is refused at plan time", "SXKVPZAX")
        bad = json.loads(json.dumps(profile))
        bad["navigation"]["values"][1]["cheats"] = [
            {"code": "8000:99", "label": "x", "source": "s"}]
        raises(lambda: _plan(bad, prof, tmp),
               "a PRG pin is refused at plan time", "$8000")
        bad = json.loads(json.dumps(profile))
        bad["defaults"]["cheats"] = [{"code": "002A:09", "label": "lives"}]
        raises(lambda: _plan(bad, prof, tmp),
               "a pin without its source is refused (ADR-0184 s5)", "source")


def _ram_check_plan():
    """ADR-0239 §4: a named RAM check is read off the session's final state."""
    with tempfile.TemporaryDirectory() as td:
        tmp = Path(td)
        prof = _scripts(tmp)
        profile = _profile(
            {"address": "0030", "label": "level", "source": "map",
             "values": [{"value": "01", "name": "a",
                         "ramCheck": {"address": "006D", "expect": ["01", "02"]}}]},
            {"seconds": 120, "entry": "entry.txt", "body": "body.txt"})
        plan = _plan(profile, prof, tmp)
        check(plan[0]["ramCheck"] == {"address": "006D", "expect": ["01", "02"]},
              "the plan carries the value's RAM check")
        cmd = sweep.command_for(plan[0], tmp / "rom.nes", {"path": "/x/i.txt"})
        check([a for a in cmd if a.startswith("save-state=")] ==
              [f"save-state={tmp / 'out' / 'a' / 'rec-final.mss'}"],
              "a checked session writes the state the check reads")
        plain = _profile({"address": "0030", "label": "l", "source": "s",
                          "values": [{"value": "01", "name": "a"}]},
                         {"seconds": 120, "entry": "entry.txt", "body": "body.txt"})
        cmd = sweep.command_for(_plan(plain, prof, tmp)[0], tmp / "rom.nes",
                                {"path": "/x/i.txt"})
        check(not any(a.startswith("save-state=") for a in cmd),
              "a session with no check writes no state")

        bad = json.loads(json.dumps(profile))
        bad["navigation"]["values"][0]["ramCheck"]["address"] = "0800"
        raises(lambda: _plan(bad, prof, tmp),
               "a RAM check above $07FF is refused at plan time", "$0800")


def _mss(path, ram):
    """A save state `mss_ram.ram` can read, holding `ram` as internal RAM."""
    key = b"memoryManager.internalRam\0"
    blob = key + struct.pack("<I", len(ram)) + bytes(ram)
    name = b"g.nes"
    data = (b"MSS" + struct.pack("<I", 1) + struct.pack("<I", 4) + struct.pack("<I", 0)
            + bytes(20) + zlib.compress(b"\0" * 64)
            + struct.pack("<I", len(name)) + name
            + b"\0" + struct.pack("<II", len(blob), len(blob)) + blob)
    Path(path).write_bytes(data)


def _missing_scripts_are_named():
    """A plan whose script is not on disk is refused before a process starts."""
    with tempfile.TemporaryDirectory() as td:
        tmp = Path(td)
        prof = _scripts(tmp)
        profile = _profile(
            {"kind": "input", "label": "track", "source": "manual",
             "values": [{"name": "t1", "entry": "entry.txt"},
                        {"name": "t3", "entry": "gone.txt"}]},
            {"seconds": 120, "entry": "entry.txt", "body": "body.txt"},
            rooms=[{"name": "boss", "state": "boss.mss", "input": "gone-too.txt"}])
        plan = _plan(profile, prof, tmp)
        check(sweep.missing_scripts(plan) ==
              [("t3", prof / "gone.txt"), ("boss", prof / "gone-too.txt")],
              "the pre-flight names every session whose script is missing",
              str(sweep.missing_scripts(plan)))
        check(sweep.missing_scripts(plan[:1]) == [],
              "and passes a plan whose scripts are all there")


def _ram_check_reads():
    with tempfile.TemporaryDirectory() as td:
        tmp = Path(td)
        ram = bytearray(2048)
        ram[0x6D] = 3
        state = tmp / "rec-final.mss"
        _mss(state, ram)
        spec = {"address": "006D", "expect": "03"}
        got = sweep.ram_check(spec, state)
        check(got["ok"] is True and got["actual"] == "03",
              "a byte equal to expect passes", str(got))
        got = sweep.ram_check({"address": "006D", "expect": ["01", "03"]}, state)
        check(got["ok"] is True, "a list of admissible values passes on a hit")
        got = sweep.ram_check({"address": "006D", "expect": ["01", "02"]}, state)
        check(got["ok"] is False and got["actual"] == "03",
              "a miss is reported with the byte it read", str(got))
        got = sweep.ram_check(spec, tmp / "missing.mss")
        check(got["ok"] is False and "missing.mss" in got["why"],
              "a state that is not there fails the check and says so", str(got))
        # Measured 2026-09-26: `cheat=0030:05` plays Contra's stage 6 while the
        # final state's $0030 reads 00 - the pin is applied on the CPU read
        # bus. A check on a pinned address therefore carries that in its own
        # result, with the byte the pin substitutes (#546).
        got = sweep.ram_check(spec, state, pinned_addresses={"006D": "03"})
        check("caveat" in got and "read bus" in got["caveat"]
              and got["pinnedValue"] == "03",
              "a check on a pinned address carries the read-bus caveat", str(got))
        got = sweep.ram_check(spec, state, pinned_addresses={"0030": "05"})
        check("caveat" not in got and "pinnedValue" not in got,
              "a check on another address carries none", str(got))
        got = sweep.ram_check(spec, state,
                              pinned_addresses={"0030": "05", "006D": "03"})
        check(got["pinnedValue"] == "03",
              "and the byte reported is the one pinned on the checked address",
              str(got))


def _the_caveat_covers_every_address_the_session_pins():
    """#546: the caveat is per address a session *pins*, not per selector.

    Measured 2026-09-26 on Punch-Out: `kind: "input"` declares no
    `navigation.address`, so the `pinned` scalar the recorder passed was always
    None and nine rung-2 sessions - which pin `$0002`/`$0003` through
    `values[].cheats` and check one of them - printed a bare `$0002=03 ok`.
    That same run also shows why the wording matters: the fight loader stores
    back the bank byte it read, so `$0002` reading the pin is the game's own
    write while the pinned `$0001` reads the game's 00, which is what proves
    the harness never wrote memory. Neither reading is "proves nothing".
    """
    with tempfile.TemporaryDirectory() as td:
        tmp = Path(td)
        prof = _scripts(tmp)
        pins = [{"code": "0001:0B", "label": "fight id",
                 "source": "DataCrystal $0001 RAM map"},
                {"code": "0002:03", "label": "fight data bank",
                 "source": "DataCrystal $0002 RAM map"}]
        profile = _profile(
            {"kind": "input", "label": "pass key", "source": "manual p.6",
             "values": [{"name": "sandman", "title": "World - Mr. Sandman",
                         "entry": "entry.txt", "body": "body.txt", "cheats": pins,
                         "ramCheck": {"address": "0002", "expect": "03"}},
                        {"name": "tyson", "title": "World - Mike Tyson",
                         "entry": "entry.txt", "body": "body.txt", "cheats": pins,
                         "ramCheck": {"address": "000B", "expect": "05"}}]},
            {"seconds": 120, "entry": "entry.txt", "body": "body.txt"})
        plan = _plan(profile, prof, tmp)
        check(plan[0]["pinnedAddresses"] == {"0001": "0B", "0002": "03"},
              "an input profile pins every cheats address, selector or not",
              str(plan[0].get("pinnedAddresses")))
        ram = bytearray(2048)
        ram[0x02] = 3
        ram[0x0B] = 5
        for s in plan:
            s["stateOut"].parent.mkdir(parents=True, exist_ok=True)
            _mss(s["stateOut"], ram)
        got = sweep.checked_ram(plan[0])
        check(got["ok"] is True and "caveat" in got,
              "a check on a values[].cheats address carries the caveat (#546)",
              str(got))
        check(got.get("pinnedValue") == "03",
              "and reports the byte the pin substitutes beside the one observed",
              str(got))
        check("read bus" in got["caveat"] and "03" in got["caveat"],
              "the caveat says where a pin acts and names the pinned byte",
              got.get("caveat", ""))
        check("says nothing" not in got["caveat"],
              "and does not claim the check proves nothing where the game may "
              "have stored the byte back", got.get("caveat", ""))
        got = sweep.checked_ram(plan[1])
        check("caveat" not in got and "pinnedValue" not in got,
              "a check on an address the session does not pin carries none",
              str(got))
        # A `kind: "ram"` session pins its selector as a cheat, so the same map
        # covers it and no second rule is needed; a room applies no selector
        # cheat, so it pins nothing at all.
        cplan = _plan(json.loads(PROFILE.read_text()), PROFILE.parent, tmp)
        check(cplan[0]["pinnedAddresses"] == {"0030": "00"},
              "a ram selector arrives through its own cheat",
              str(cplan[0].get("pinnedAddresses")))
        check(cplan[8]["pinnedAddresses"] == {},
              "and a room pins nothing - it applies no selector cheat",
              str(cplan[8].get("pinnedAddresses")))
        # The shape #546 came from. A bare address is iterable, so
        # `address in pinned_addresses` would silently become a substring test.
        raises(lambda: sweep.ram_check({"address": "0002", "expect": "03"},
                                       plan[0]["stateOut"],
                                       pinned_addresses="0002"),
               "a scalar pinned_addresses is refused, not read as a string",
               "pinned_addresses")
        try:
            sweep.ram_check({"address": "0002", "expect": "03"}, plan[0]["stateOut"],
                            pinned="0002")
            FAILED.append("the scalar keyword `pinned` is gone")
        except TypeError:
            check(True, "the scalar keyword `pinned` is gone, loudly")


# --- ADR-0239 s4: a session must prove it went somewhere ---------------------
def _hires(path: Path, ver, rules, flag="N"):
    """A pack definition; `flag` is the `<tile>` rule's defaultTile field."""
    path.parent.mkdir(parents=True, exist_ok=True)
    lines = [f"<ver>{ver}"] if ver is not None else []
    lines += [f"<tile>0,{d},0F001030,0,0,1,{flag}" for d in rules]
    path.write_text("\n".join(lines) + "\n")
    return path


def _result(name, tmp, tiles=None, status="ok"):
    r = {"name": name, "status": status, "hires": None}
    if tiles is not None:
        r["hires"] = str(_hires(tmp / name / "hires.txt", 109, tiles))
    return r


def _the_gate_is_the_baseline_and_unique_only_reads():
    """ADR-0239 §4 as amended 2026-09-26: `new` is the drawn keys the *baseline*
    packs do not hold and it is the gate; `unique` is the keys no other session
    and no baseline holds, and it is reported for reading, not gating - "two
    tracks that share a tileset are both legitimate warps with `unique == 0`, so
    gating on it would drop both"."""
    with tempfile.TemporaryDirectory() as td:
        tmp = Path(td)
        a = _result("a", tmp, ["AAAA"])
        b = _result("b", tmp, ["BBBB"])
        c = _result("c", tmp, ["AAAA"])
        got = sweep.score_sessions([a, b, c], [])
        check(all(r["status"] == "ok" for r in (a, b, c)),
              "with no baseline every session holds keys the baseline does not, "
              "so none is did-not-warp", str([r["status"] for r in (a, b, c)]))
        check([r["new"] for r in (a, b, c)] == [1, 1, 1],
              "new is per session against the baseline, so the pair that "
              "duplicates each other counts too",
              str([r["new"] for r in (a, b, c)]))
        check([r["unique"] for r in (a, b, c)] == [0, 1, 0],
              "unique still reports that only b is held by nobody else",
              str([r["unique"] for r in (a, b, c)]))
        check(got["counted"] == ["a", "b", "c"] and got["didNotWarp"] == [],
              "and unique never gates", str(got))
        check(got["unionHires"] == [Path(a["hires"]), Path(b["hires"]),
                                    Path(c["hires"])],
              "the union is every session that produced a pack (s5: 'every sweep "
              "session')", str(got["unionHires"]))

        # A baseline that already holds AAAA and BBBB: every session records
        # material the baseline has, which is a stage-1 recording under another
        # name, and the union cannot move.
        base = _hires(tmp / "base" / "hires.txt", 109, ["AAAA", "BBBB"])
        got = sweep.score_sessions([a, b, c], [base])
        check(got["counted"] == [] and len(got["didNotWarp"]) == 3,
              "a baseline the sweep never leaves leaves nothing to count", str(got))
        check([r["new"] for r in (a, b, c)] == [0, 0, 0], "new == 0 for all three")

        # And a baseline holding only AAAA: b's key is still new.
        base = _hires(tmp / "base2" / "hires.txt", 109, ["AAAA"])
        got = sweep.score_sessions([a, b, c], [base])
        check(got["counted"] == ["b"] and a["new"] == 0 and c["new"] == 0,
              "a baseline holding one session's keys does not rescue that session")

        # The report prints this table *and* builds the totals from it, so a
        # second pass over the same list has to say the same thing.
        scored_once = [(r["new"], r["unique"], r["status"]) for r in (a, b, c)]
        check(sweep.score_sessions([a, b, c], [base]) == got
              and [(r["new"], r["unique"], r["status"]) for r in (a, b, c)]
              == scored_once,
              "scoring the same sweep twice gives the same answer")


def _placeholders_are_not_keys_a_session_can_claim():
    """§4: "Neither count includes the builder's `defaultTile` placeholder
    rules: a bootstrapped CHR ROM pack carries one for every CHR index, so every
    pack of one ROM shares them"."""
    with tempfile.TemporaryDirectory() as td:
        tmp = Path(td)
        only = _hires(tmp / "ph" / "hires.txt", 109, ["0000", "0001", "0002"],
                      flag="Y")
        p = {"name": "p", "status": "ok", "hires": str(only)}
        sweep.score_sessions([p], [])
        check(p["new"] == 0 and p["unique"] == 0 and p["status"] == "did-not-warp",
              "a pack of nothing but placeholders holds nothing, even with no "
              "baseline", str(p))
        check(p["keys"] == 0 and p["seen"] == 0,
              "and the placeholder rules are in neither count", str(p))

        # One written rule beside the same placeholders is one key, and it is
        # new against a baseline that holds only placeholders.
        mixed = tmp / "mx" / "hires.txt"
        mixed.parent.mkdir(parents=True, exist_ok=True)
        mixed.write_text("<ver>109\n"
                         "<tile>0,0000,0F001030,0,0,1,Y\n"
                         "<tile>0,0001,0F001030,8,0,1,N\n")
        m = {"name": "m", "status": "ok", "hires": str(mixed)}
        sweep.score_sessions([m], [_hires(tmp / "ph2" / "hires.txt", 109,
                                          ["0000", "0002", "0003"], flag="Y")])
        check(m["new"] == 1 and m["unique"] == 1 and m["status"] == "ok",
              "the one rule a run wrote is the one key it can claim", str(m))
        check(m["tiles"] == 2 and m["seen"] == 1,
              "while the printed tile columns still count what the pack holds",
              f"tiles={m['tiles']} seen={m['seen']}")


def _the_unit_that_can_move_is_the_drawn_key():
    """§4's gate counts drawn keys, the `(tileData, palette)` cell identity
    ADR-0194 §2 names, over the non-placeholder rules §4's 2026-09-26 amendment
    excludes. Measured on real packs 2026-09-26:

    - tile data over every `<tile>` rule is frozen across the packs of one ROM
      game: the builder writes a `defaultTile` placeholder per CHR index
      (`HdPackBuilder.cpp:139/620`), so every pack of that ROM enumerates the
      whole CHR and the rules a run writes name indices already in it. On
      `runs/f1416/excitebike/rehearsal` all 13 sessions read `newTiles == 0`
      against the baseline while their union gains 1422 drawn keys;
    - the tile data a run *wrote* is the unit ADR-0184's measurement used, but
      it is one cell coarser: it cannot see the same art repainted under
      another palette, which ADR-0194 §2 calls a different cell. Over those 13
      sessions it reads the same 13/13 `new` but only 1/13 `unique` (design 6)
      against 3/13 over drawn keys (track-a2 114, design 23, track-a1 1;
      runs/f1416/code/excitebike-rescore2.txt).

    So the gate is `new` in drawn keys and the tile-data counts are reported
    beside it, for §4's wording to be re-read against the numbers.
    """
    with tempfile.TemporaryDirectory() as td:
        tmp = Path(td)

        def pack(path, written):
            # The placeholders carry the neutral ramp (`HdPackBuilder.cpp:618`),
            # the written rules the palettes the run saw - which is what makes
            # the key the unit that moves.
            lines = ["<ver>109"] + [f"<tile>0,{i:04X},0F001030,0,0,1,Y"
                                    for i in range(16)]
            lines += [f"<tile>0,{d},FF202616,0,0,1,N" for d in written]
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("\n".join(lines) + "\n")
            return path

        base = pack(tmp / "base" / "hires.txt", ["0001"])
        a = {"name": "a", "status": "ok",
             "hires": str(pack(tmp / "a" / "hires.txt", ["0001", "0002"]))}
        sweep.score_sessions([a], [base])
        check(a["newTiles"] == 0,
              "over every rule the session holds nothing new - the CHR "
              "enumeration is identical in both packs", f"newTiles={a['newTiles']}")
        check(a["status"] == "ok" and a["new"] == 1,
              "and its verdict comes from the key, which is not frozen",
              f"{a['status']} new={a['new']}")
        check(a["tiles"] == 16 and a["seen"] == 2 and a["keys"] == 2,
              "every count is reported", f"{a['tiles']} {a['seen']} {a['keys']}")

        # The palette is half the key: a session that drew the same tile data
        # as the baseline in another colour holds a cell the baseline does not.
        x = {"name": "x", "status": "ok",
             "hires": str(_hires(tmp / "x" / "hires.txt", 109, ["AAAA"]))}
        y = _hires(tmp / "y" / "hires.txt", 109, ["AAAA"])
        y.write_text("<ver>109\n<tile>0,AAAA,0F002030,0,0,1,N\n")
        y = {"name": "y", "status": "ok", "hires": str(y)}
        shade = _hires(tmp / "shade" / "hires.txt", 109, ["AAAA"])
        got = sweep.score_sessions([x, y], [shade])
        check(x["new"] == 0 and y["new"] == 1 and got["counted"] == ["y"],
              "the same tile data under another palette is another cell, and "
              "the one that matches the baseline did not warp",
              f"x={x['new']} y={y['new']} {got}")
        check(x["unique"] == 0 and y["unique"] == 1,
              "unique reports y as the one neither the baseline nor the other "
              "session holds", f"x={x['unique']} y={y['unique']}")
        check(x["newTiles"] == y["newTiles"] == 0,
              "while the tile-data reading calls both of them duplicates",
              f"{x['newTiles']} {y['newTiles']}")

        # With one palette across the boards the two units agree, which is the
        # shape ADR-0184's own sweep was measured on.
        plain_base = _hires(tmp / "pbase" / "hires.txt", 109, ["AAAA"])
        plain = _result("p", tmp, ["AAAA", "BBBB"])
        sweep.score_sessions([plain], [plain_base])
        check(plain["new"] == plain["newTiles"] == 1,
              "with one palette the literal count and the verdict agree",
              f"{plain['new']} {plain['newTiles']}")


def _union_units():
    """ADR-0194 s4/ADR-0239 s5: the union reports drawn keys *and* distinct
    tile data, because the same tile under another palette is another key."""
    with tempfile.TemporaryDirectory() as td:
        tmp = Path(td)
        h = _hires(tmp / "p" / "hires.txt", 109, ["AAAA"])
        (tmp / "p" / "hires.txt").write_text(
            "<ver>109\n"
            "<tile>0,AAAA,0F001030,0,0,1,N\n"
            "<tile>0,AAAA,0F002030,8,0,1,N\n"
            "<tile>0,BBBB,0F001030,16,0,1,N\n")
        keys = sweep.pack_rule_keys([h])
        check(len(keys) == 3 and sweep.pack_tile_data([h]) == {"AAAA", "BBBB"},
              "keys count (tileData, palette), tile data counts the string",
              str(sorted(keys)))
        check(len(sweep.pack_rule_keys([])) == 0, "no pack is an empty union")


# --- ADR-0239 s5: the ROM's own CHR as the denominator ----------------------
BLANK = bytes(16)
P1 = bytes([0x0F] * 16)
P2 = bytes([0xF0] * 16)


def _rom(path, tiles=None, chr_ram=False, trainer=False):
    """A minimal iNES file: one PRG bank and a full 8 KB CHR ROM."""
    tiles = dict(tiles or {})
    chr_data = b"".join(tiles.get(i, BLANK) for i in range(512))
    header = (b"NES\x1a"
              + bytes([1, 0 if chr_ram else 1, 0x04 if trainer else 0x00, 0x00])
              + bytes(8))
    path.write_bytes(header + (b"\xAA" * 512 if trainer else b"")
                     + bytes(16384) + chr_data)
    return path


def _rom_chr():
    with tempfile.TemporaryDirectory() as td:
        tmp = Path(td)
        rom = _rom(tmp / "g.nes", {1: P1, 2: P1, 3: P2})
        # distinct non-blank patterns: P1 (indices 1 and 2) and P2 (index 3).
        pack = _hires(tmp / "p" / "hires.txt", 109, ["01", "03"])
        got = sweep.rom_chr_coverage([pack], rom)
        check(got["patterns"] == 2 and got["named"] == 2 and got["pct"] == 100.0,
              "the denominator is the ROM's distinct non-blank patterns", str(got))
        # A rule naming index 2 names the *same* pattern as index 1, so two
        # rules over one pattern are still one covered pattern.
        pack = _hires(tmp / "q" / "hires.txt", 109, ["01", "02"])
        got = sweep.rom_chr_coverage([pack], rom)
        check((got["named"], got["pct"]) == (1, 50.0),
              "a duplicate pattern under another index counts once", str(got))
        # Blank indices are no denominator and no numerator.
        pack = _hires(tmp / "r" / "hires.txt", 109, ["00", "01"])
        got = sweep.rom_chr_coverage([pack], rom)
        check((got["patterns"], got["named"]) == (2, 1),
              "a blank pattern is neither counted nor named", str(got))
        # <ver> below 103 reads the field as decimal (HdPackLoader::ReadTileData).
        # Only index 16 holds art, so the token "10" tells the two readings
        # apart: decimal it is index 10 (blank), hex it is index 16 (P2).
        rom2 = _rom(tmp / "g2.nes", {16: P2})
        pack = _hires(tmp / "d" / "hires.txt", 102, ["10"])
        got = sweep.rom_chr_coverage([pack], rom2)
        check(got["named"] == 0, "below <ver>103 the token 10 is decimal index 10",
              str(got))
        pack = _hires(tmp / "e" / "hires.txt", 109, ["10"])
        got = sweep.rom_chr_coverage([pack], rom2)
        check(got["named"] == 1, "at 103+ the same token is hex index 16", str(got))
        pack = _hires(tmp / "f" / "hires.txt", None, ["10"])
        got = sweep.rom_chr_coverage([pack], rom2)
        check(got["named"] == 0, "a pack with no <ver> reads decimal too", str(got))
        # The trainer shifts PRG and CHR by 512 bytes.
        rom3 = _rom(tmp / "g3.nes", {1: P1}, trainer=True)
        pack = _hires(tmp / "t" / "hires.txt", 109, ["01"])
        check(sweep.rom_chr_coverage([pack], rom3)["named"] == 1,
              "a 512-byte trainer is skipped before PRG and CHR")
        # A 32-hex tileData is a CHR RAM pattern; it still names a pattern the
        # ROM's CHR holds, and it never names one it does not.
        pack = _hires(tmp / "h" / "hires.txt", 109, [P1.hex().upper()])
        check(sweep.rom_chr_coverage([pack], rom)["named"] == 1,
              "a 32-hex rule names a pattern by value")
        pack = _hires(tmp / "i" / "hires.txt", 109, [(b"\x0A" * 16).hex().upper()])
        check(sweep.rom_chr_coverage([pack], rom)["named"] == 0,
              "and a pattern the ROM's CHR does not hold names nothing")
        # A CHR RAM game has no fixed denominator.
        got = sweep.rom_chr_coverage([pack], _rom(tmp / "ram.nes", chr_ram=True))
        check(got["pct"] is None and got["status"] == "n/a (CHR RAM)",
              "a CHR RAM game reports n/a, not 0%", str(got))
        # Measured 2026-09-26: a bootstrapped pack carries a `defaultTile`
        # placeholder for *every* CHR index (512 on Excitebike, 8192 on the
        # other three), so §5.2 read literally is 100% for any pack the
        # recorder wrote. The row over the rules a run really wrote is the one
        # that can move, and it is reported beside it.
        placeholders = _hires(tmp / "y" / "hires.txt", 109, ["00", "01", "02", "03"],
                              flag="Y")
        written = _hires(tmp / "z" / "hires.txt", 109, ["01"])
        check(sweep.rom_chr_coverage([placeholders], rom)["pct"] == 100.0,
              "a placeholder rule still names its pattern (the literal row)")
        check(sweep.rom_chr_seen_coverage([placeholders], rom)["named"] == 0,
              "and the row over written rules counts none of them")
        check(sweep.rom_chr_seen_coverage([written], rom)["named"] == 1,
              "a written rule is what the seen row counts")


def _pack_hires_resolution():
    """--baseline takes a pack dir (the `auto/` folder, or one holding
    textures/hires.txt), a recording's session dir, or the file itself."""
    with tempfile.TemporaryDirectory() as td:
        tmp = Path(td)
        direct = _hires(tmp / "pack" / "hires.txt", 109, ["AAAA"])
        tex = _hires(tmp / "auto" / "textures" / "hires.txt", 109, ["AAAA"])
        sess = _hires(tmp / "before" / "Rom" / "auto" / "textures" / "hires.txt",
                      109, ["AAAA"])
        check(sweep.pack_hires(direct) == direct, "a hires.txt resolves to itself")
        check(sweep.pack_hires(direct.parent) == direct,
              "a pack folder holding hires.txt resolves")
        check(sweep.pack_hires(tex.parent.parent) == tex,
              "an auto/ folder holding textures/hires.txt resolves")
        check(sweep.pack_hires(sess.parent.parent) == sess,
              "and so does the auto/ folder of a real recording")
        check(sweep.pack_hires(tmp / "before") == sess,
              "a session folder resolves through find_pack_hires")


def _rescore_reads_the_packs_a_sweep_left():
    """`--rescore`: §4's gate can be amended without re-recording anything.

    It was amended on 2026-09-26, and re-running five games' 120 s sessions to
    re-read a verdict from packs already on disk would spend the whole budget
    again. A session is any directory of `--out` holding a bootstrap pack; one
    that holds none is named, not silently scored - that is the shape a crashed
    session leaves, and calling it `did-not-warp` would be a false claim.
    """
    with tempfile.TemporaryDirectory() as td:
        tmp = Path(td)
        out = tmp / "sweep"
        _hires(out / "s1" / "Fake" / "auto" / "textures" / "hires.txt",
               109, ["AAAA", "BBBB"])
        _hires(out / "s2" / "Fake" / "auto" / "textures" / "hires.txt",
               109, ["AAAA"])
        (out / "s3").mkdir(parents=True)
        found, empty = sweep.discover_sessions(out)
        check([r["name"] for r in found] == ["s1", "s2"],
              "every directory holding a pack is a session, in name order",
              str([r["name"] for r in found]))
        check(empty == ["s3"], "and one that holds none is named", str(empty))
        check(all(r["status"] == "ok" for r in found),
              "a pack on disk is a session that ran")
        check(found[0]["hires"].endswith("s1/Fake/auto/textures/hires.txt"),
              "the pack is resolved through find_pack_hires", found[0]["hires"])
        check(sweep.discover_sessions(out, {"s2"})[0][0]["name"] == "s2",
              "--only filters the rescore too")
        raises(lambda: sweep.discover_sessions(tmp / "nope"),
               "a --out that is not there is refused, not read as zero sessions",
               "nope")

        base = _hires(tmp / "base" / "hires.txt", 109, ["AAAA"])
        scored = sweep.score_sessions(found, [base])
        check(scored["counted"] == ["s1"] and scored["didNotWarp"] == ["s2"],
              "and the rescued packs score exactly as the run's own did", str(scored))


def _main_rc(*argv):
    """`main()`'s `(exit code, stdout)` for one command line, SystemExit included.

    argparse answers a missing required argument with SystemExit, which is not
    an `Exception` - raised inside a case it would abort the run and hide every
    case after it instead of failing one. stdout is captured because `main()`
    prints whole report tables and a test that leaks them buries its own
    failures.
    """
    before, buf = sys.argv, io.StringIO()
    sys.argv = ["record_navigation_sweep.py", *argv]
    try:
        with contextlib.redirect_stdout(buf):
            return sweep.main(), buf.getvalue()
    except SystemExit as exc:
        return exc.code, buf.getvalue()
    finally:
        sys.argv = before


def _rescore_cli():
    """The mode runs with no ROM and no emulator binary: nothing executes."""
    with tempfile.TemporaryDirectory() as td:
        tmp = Path(td)
        base = _hires(tmp / "base" / "hires.txt", 109, ["AAAA"])
        _hires(tmp / "sweep" / "s1" / "Fake" / "auto" / "textures" / "hires.txt",
               109, ["AAAA", "BBBB"])
        rc, out = _main_rc("--rescore", "--out", str(tmp / "sweep"),
                           "--baseline", str(base), "--summary", str(tmp / "s.json"))
        check(rc == 0, "rescore exits 0 without --profile and --rom", str(rc))
        check("new = drawn keys" in out and "rescoring 1 session" in out,
              "and it prints the same §4 table a run does",
              [ln for ln in out.splitlines() if ln.strip()][:3])
        doc = json.loads((tmp / "s.json").read_text())
        check(doc["game"] == "sweep", "the game defaults to the --out folder name",
              doc["game"])
        check(doc["sessions"][0]["name"] == "s1" and doc["sessions"][0]["new"] == 1,
              "and its row is scored", str(doc["sessions"][0]))
        check(doc["totals"]["before"]["keys"] == 1
              and doc["totals"]["after"]["keys"] == 2,
              "before is the baseline, after is the union", str(doc["totals"]))

        # A --baseline that resolves to nothing is refused here too.
        rc, _ = _main_rc("--rescore", "--out", str(tmp / "sweep"),
                         "--baseline", str(tmp / "gone"))
        check(rc == 2, "the §5 refusal holds in rescore mode", str(rc))

        # A session directory with no pack is named, and only a --out with no
        # pack at all is refused.
        (tmp / "sweep" / "gone").mkdir()
        rc, out = _main_rc("--rescore", "--out", str(tmp / "sweep"),
                           "--summary", str(tmp / "s2.json"))
        check(rc == 0 and "no pack, not scored: gone" in out,
              "a session dir holding no pack is named, not scored as did-not-warp",
              out.splitlines()[1] if len(out.splitlines()) > 1 else out)
        doc = json.loads((tmp / "s2.json").read_text())
        check([s["name"] for s in doc["sessions"]] == ["s1"],
              "and it is in no row and in no total", str(doc["sessions"]))
        (tmp / "none").mkdir()
        rc, _ = _main_rc("--rescore", "--out", str(tmp / "none"))
        check(rc == 2, "a --out holding no session pack is refused", str(rc))


def _the_union_row_is_the_rules_a_run_wrote():
    """ADR-0239 §5.1's row is what the run *drew*, not every rule the pack holds.

    Measured 2026-09-26 on Castlevania (CHR RAM): the bootstrap builder writes
    one `defaultTile` placeholder per PRG-scan pattern - 2581 of them, all
    distinct, identical in every pack of that ROM - so a union row over every
    rule is the placeholder set plus a rounding error, and the reference
    coverage it feeds is mostly the scan. The CHR ROM games have the same
    defect in another shape (the placeholders enumerate the CHR indices, so
    `newTiles` read 0 for all 13 Excitebike sessions). §4's amendment already
    excluded them from `new`/`unique`; §5.1's union needs the same exclusion.
    """
    with tempfile.TemporaryDirectory() as td:
        tmp = Path(td)
        # The builder's placeholders: distinct, and the same in both packs.
        ph = "".join(f"<tile>0,{i:032X},0F001030,0,0,1,Y\n" for i in range(40))
        base = tmp / "base" / "hires.txt"
        base.parent.mkdir(parents=True)
        base.write_text("<ver>109\n" + ph + "<tile>0,01,0F001030,0,0,1,N\n")
        sess = tmp / "s" / "hires.txt"
        sess.parent.mkdir(parents=True)
        sess.write_text("<ver>109\n" + ph
                        + "<tile>0,02,0F001030,0,0,1,N\n"
                        + "<tile>0,03,0F001030,0,0,1,N\n"
                        + "<tile>0,04,0F001030,0,0,1,N\n")
        ref = _hires(tmp / "ref" / "hires.txt", 109, ["03"])
        # §5.3 is scored at the CHR pattern (#545), so the reference row needs
        # the ROM's own CHR: a written rule names index 3, and index 3 is P3.
        rom = _rom(tmp / "g.nes", {1: P1, 2: P2, 3: P3, 4: P4})

        t = sweep.totals_document([base], [base, sess], rom, ref)
        check(t["before"]["tileData"] == 1 and t["after"]["tileData"] == 4,
              "the union's distinct tile data counts the rules the run wrote",
              str(t["before"]) + " " + str(t["after"]))
        check(t["before"]["tileDataAll"] == 41 and t["after"]["tileDataAll"] == 44,
              "and the every-rule count is still reported beside it, so a pack "
              "whose placeholders dominate is visible and not hidden",
              str(t["after"]))
        check(t["before"]["keys"] == 1 and t["after"]["keys"] == 4,
              "drawn keys are the drawn unit too", str(t["after"]))
        check(t["after"]["keysAll"] == 44, "with the every-rule count beside it",
              str(t["after"]))
        check(t["reference"]["after"]["tiles"] == 1,
              "and the reference hit is measured over the drawn rows: only the "
              "rule naming index 3 is both drawn and painted, the 40 "
              "placeholders are neither",
              str(t["reference"]))
        check(t["reference"]["afterAll"]["tiles"] == 1,
              "the every-rule hit rides along", str(t["reference"]))


# --- #545: the reference is scored at the pattern, not at the string ---------
P3 = bytes([0xAA] * 16)
P4 = bytes([0x55] * 16)


def _the_two_dialects_meet_at_the_pattern_they_name():
    """#545: two packs of one ROM write the `<tile>` field in different bases.

    A community pack is `<ver>100` - a decimal, unpadded CHR index ('<tile>0,1,
    FF072235,...') - and a bootstrapped pack is `<ver>109` - hex, padded
    ('<tile>0,1000,0F25300F,...') - so keying the comparison on the string it
    read ({'1','10','100',...} against {'00','01','0100',...}) intersects by
    accident and the figure is a constant. Measured on Ninja Gaiden:
    1003/7382 = 13.6 % for the baseline, for each of the 21 sessions and for
    the union - not 0 %, so §5.3's "not printed as 0 %" guard does not see it.
    Each rule names a 16-byte CHR pattern either way, which is the identity
    `_named_pattern` reads and the one §5.3 is asking for: on the same packs
    the pattern reads 535/6208 (8.6 %) -> 2705/6208 (43.6 %).
    """
    with tempfile.TemporaryDirectory() as td:
        tmp = Path(td)
        # Index 1 holds P1, index 10 P2, index 16 P3: the token "10" is a
        # different index in each dialect, which is what the fix is about.
        rom = _rom(tmp / "g.nes", {1: P1, 10: P2, 16: P3})
        ref = _hires(tmp / "ref" / "hires.txt", 100, ["1", "10"])
        base = _hires(tmp / "base" / "hires.txt", 109, ["01", "0A"])

        t = sweep.totals_document([base], [base], rom, ref)
        check(t["reference"]["tileData"] == 2,
              "the denominator is the reference pack's own distinct patterns",
              str(t["reference"]))
        check(t["reference"]["before"]["tiles"] == 2
              and t["reference"]["before"]["pct"] == 100.0,
              "and a pack writing the same two tiles in the other dialect holds "
              "both of them - the raw strings {'1','10'} and {'01','0A'} share "
              "nothing", str(t["reference"]))

        # The same reference figure for either dialect: what is compared is the
        # pattern, so which spelling the pack uses cannot move the number.
        hex_ref = _hires(tmp / "hexref" / "hires.txt", 109, ["01", "0A"])
        figures = ("tileData", "tileDataAll", "before", "after",
                   "beforeAll", "afterAll")
        check([sweep.totals_document([base], [base], rom, hex_ref)["reference"][k]
               for k in figures]
              == [sweep.totals_document([base], [base], rom, ref)["reference"][k]
                  for k in figures],
              "a reference pack written at the other <ver> gives the same figures")

        # The other direction, and the one a string comparison gets *wrong*: a
        # token that matches is not a tile that matches.
        ref10 = _hires(tmp / "ref10" / "hires.txt", 100, ["10"])
        hex10 = _hires(tmp / "hex10" / "hires.txt", 109, ["10"])
        t = sweep.totals_document([hex10], [hex10], rom, ref10)
        check(t["reference"]["before"]["tiles"] == 0,
              '"10" is index 10 in one dialect and index 16 in the other, so a '
              "rule that names another tile is not a hit", str(t["reference"]))
        check(t["reference"]["tileData"] == 1,
              "and the denominator is one pattern, not zero: the reference "
              "names index 10 whatever the compared pack does", str(t["reference"]))

        # §5.2 is untouched: it already reads the pattern, so the same two
        # packs give the same CHR coverage in either dialect.
        check(sweep.rom_chr_seen_coverage([base], rom)
              == sweep.rom_chr_seen_coverage([_hires(tmp / "dec" / "hires.txt",
                                                     102, ["1", "10"])], rom),
              "the ROM CHR row reads the same for both dialects, as it did")


def _an_index_needs_the_rom_and_a_pattern_does_not():
    """`pack_named_patterns`: what an identity needs to be resolved.

    An index becomes a pattern only through the ROM's own CHR (ADR-0239 §5.2),
    so asking for the identity of an index-form pack with no ROM is refused
    rather than scored as an empty set - an empty set reads as 0 % coverage,
    the figure §5.3 forbids. A rule that carries its 16 bytes names its pattern
    by value (`HdPackLoader::ReadTileData`: 32+ hex digits is a CHR RAM
    pattern), so a CHR RAM pack needs no ROM at all.
    """
    with tempfile.TemporaryDirectory() as td:
        tmp = Path(td)
        rom = _rom(tmp / "g.nes", {1: P1, 10: P2})
        by_index = _hires(tmp / "i" / "hires.txt", 109, ["01", "0A"])
        by_value = _hires(tmp / "v" / "hires.txt", 109, [P1.hex().upper()])
        check(sweep.pack_named_patterns([by_index], rom) == {P1, P2},
              "an index-form pack resolves through the ROM's CHR",
              str(sweep.pack_named_patterns([by_index], rom)))
        check(sweep.pack_named_patterns([by_value], None) == {P1},
              "a 32-hex rule names its pattern by value, with no ROM")
        raises(lambda: sweep.pack_named_patterns([by_index], None),
               "an index-form pack with no ROM is refused, not scored empty",
               "needs the ROM")
        # The placeholders are the unit §5.1 drops, here too.
        with_ph = _hires(tmp / "p" / "hires.txt", 109, ["01", "0A"], flag="Y")
        check(sweep.pack_named_patterns([with_ph], rom) == set(),
              "a defaultTile placeholder names no pattern for §5.1's row")
        check(sweep.pack_named_patterns([with_ph], rom, seen_only=False) == {P1, P2},
              "and names it for the every-rule row")


def _a_different_ver_base_is_provenance_not_a_gate():
    """§5.3, amended: two dialects are noted, never refused.

    The comparison no longer cares which base the field is written in, so what
    a reader needs is that the two files are not the same dialect - a note. The
    refusal is §5.3's own (#225: a pack keyed for a patched ROM) and it is
    untouched.
    """
    with tempfile.TemporaryDirectory() as td:
        tmp = Path(td)
        rom = _rom(tmp / "g.nes", {1: P1, 10: P2})
        ref = _hires(tmp / "ref" / "hires.txt", 100, ["1", "10"])
        base = _hires(tmp / "base" / "hires.txt", 109, ["01"])

        note = sweep.version_note(ref, [base])
        check("100" in note and "109" in note,
              "the note names both bases", note)
        check(sweep.version_note(base, [base]) == "",
              "the same dialect carries no note")
        check(sweep.version_note(base, []) == "",
              "and nothing to compare against carries none either")
        t = sweep.totals_document([base], [base], rom, ref)
        check(t["reference"]["note"] == note
              and t["reference"]["versions"] == {"reference": 100, "compared": [109]},
              "the totals carry the same note beside the figure",
              str(t["reference"].get("versions")))


def _the_rescore_scores_the_reference_at_the_pattern():
    """`--reference` end to end: per-session, union and union+baseline lines.

    The packs are #545's shape - a `<ver>100` reference against `<ver>109`
    sessions - so the string comparison this replaces reads 0.0 % on every
    line. Only the runs on disk are read: no ROM is emulated and no session is
    re-recorded.
    """
    with tempfile.TemporaryDirectory() as td:
        tmp = Path(td)
        rom = _rom(tmp / "g.nes", {1: P1, 10: P2, 16: P3})
        ref = _hires(tmp / "ref" / "hires.txt", 100, ["1", "10"])
        base = _hires(tmp / "base" / "hires.txt", 109, ["01"])
        _hires(tmp / "sweep" / "s1" / "Fake" / "auto" / "textures" / "hires.txt",
               109, ["0A"])
        _hires(tmp / "sweep" / "s2" / "Fake" / "auto" / "textures" / "hires.txt",
               109, ["10"])

        rc, out = _main_rc("--rescore", "--out", str(tmp / "sweep"),
                           "--baseline", str(base), "--rom", str(rom),
                           "--reference", str(ref),
                           "--summary", str(tmp / "s.json"))
        check(rc == 0, "rescore with a reference exits 0", str(rc))
        check("2 distinct CHR patterns" in out,
              "the reference line says what its denominator is",
              [ln for ln in out.splitlines() if "coverage against" in ln])
        check(any("SWEEP UNION" in ln and "1 of reference" in ln and "50.0%" in ln
                  for ln in out.splitlines()),
              "the union line is the pattern hit: s2's '10' is index 16, not "
              "the reference's index 10, so only s1's P2 is held",
              [ln for ln in out.splitlines() if "SWEEP UNION" in ln])
        check(any("UNION + base" in ln and "2 of reference" in ln and "100.0%" in ln
                  for ln in out.splitlines()),
              "and with the baseline's P1 the reference is fully held",
              [ln for ln in out.splitlines() if "UNION + base" in ln])
        check(any("<ver>100" in ln and "<ver>109" in ln for ln in out.splitlines()),
              "the two dialects are noted, not refused",
              [ln for ln in out.splitlines() if "<ver>" in ln])
        doc = json.loads((tmp / "s.json").read_text())
        check(doc["totals"]["reference"]["before"] == {"tiles": 1, "pct": 50.0}
              and doc["totals"]["reference"]["after"] == {"tiles": 2, "pct": 100.0},
              "and the totals carry the same figure", str(doc["totals"]["reference"]))

        # The identity is the pattern, so a reference with no ROM to resolve an
        # index through is refused before anything is printed as 0 %.
        rc, _ = _main_rc("--rescore", "--out", str(tmp / "sweep"),
                         "--baseline", str(base), "--reference", str(ref))
        check(rc == 2, "--reference without --rom is refused, not scored at 0 %",
              str(rc))


def _summary_document():
    with tempfile.TemporaryDirectory() as td:
        tmp = Path(td)
        base = _hires(tmp / "base" / "hires.txt", 109, ["01"])
        a = _result("a", tmp, ["01", "02"])
        b = _result("b", tmp, ["01"])
        sweep.score_sessions([a, b], [base])
        a["ramCheck"] = {"address": "006D", "expect": ["01"], "actual": "01",
                         "ok": True, "why": ""}
        before = sweep.totals_document([base], [base], None)
        check(before["before"] == {"keys": 1, "keysAll": 1, "tileData": 1,
                                   "tileDataAll": 1, "romChr": None,
                                   "romChrSeen": None}
              and "reference" not in before,
              "before is the baseline's own count", str(before))
        after = sweep.totals_document([base], [base, Path(a["hires"])],
                                     _rom(tmp / "g.nes", {1: P1, 2: P2}))
        check(after["after"]["tileData"] == 2 and after["after"]["keys"] == 2,
              "after is the union", str(after))
        check(after["after"]["romChr"]["named"] == 2,
              "and carries the ROM CHR row", str(after["after"]["romChr"]))
        doc = sweep.summary_document("Fake", "g.nes", 120, [a, b], after)
        check([s["name"] for s in doc["sessions"]] == ["a", "b"],
              "the summary lists every session")
        check(doc["sessions"][0] == {"name": "a", "status": "ok", "tiles": 2,
                                     "new": 1, "unique": 1,
                                     "ramCheck": a["ramCheck"],
                                     "keys": 2, "seen": 2, "newTiles": 1},
              "a session row is name/status/tiles/new/unique/ramCheck, plus "
              "every count `new` was read from", str(doc["sessions"][0]))
        check(doc["sessions"][1]["status"] == "did-not-warp"
              and doc["sessions"][1]["new"] == 0
              and doc["sessions"][1]["unique"] == 0,
              "a session the baseline already covers is did-not-warp, and "
              "unique does not change its verdict")
        check(doc["totals"] is after, "the totals ride along unchanged")
        check(doc["game"] == "Fake" and doc["rom"] == "g.nes" and doc["seconds"] == 120,
              "the document names the game, the ROM and the budget")


# --- the notes a run hands a kit generator, for both selector kinds ---------
# A snapshot of the F14.16 branch shipped `main()` with `swept` (the RAM
# branch) and `picked` (the input branch) used but never assigned, so a real
# sweep aborted with `NameError` *after* paying for every capture, at the
# notes[] block - the last thing a run writes before `sweep.json`. `--rescore`
# and `--dry-run` are the only cheap ways to reach it, and `--dry-run` reaches
# it without spawning the emulator.
def _the_run_notes_are_built_for_both_selector_kinds():
    """ADR-0183 §3: the notes[] a kit generator reads, for rung 1 and rung 2."""
    with tempfile.TemporaryDirectory() as td:
        tmp = Path(td)
        rom = tmp / "Fake.nes"
        rom.write_bytes(b"NES\x1a" + bytes(12))
        for kind in ("ram", "input"):
            prof = tmp / kind
            prof.mkdir()
            (prof / "mint.txt").write_text("10f S\n")
            (prof / "body.txt").write_text("20f R\n")
            value = {"name": "one", "title": "One",
                     "entry": "mint.txt", "body": "body.txt"}
            if kind == "ram":
                value["value"] = "01"
            nav = {"kind": kind, "label": "selector",
                   "source": "a published map", "values": [value]}
            if kind == "ram":
                nav["address"] = "006D"
            doc = {"game": "Fake", "navigation": nav,
                   "defaults": {"seconds": 1}}
            (prof / "navigation.json").write_text(json.dumps(doc))
            rc, out = _main_rc("--profile", str(prof / "navigation.json"),
                               "--rom", str(rom), "--out", str(tmp / ("out-" + kind)),
                               "--dry-run")
            check(rc == 0, f"a dry run of a kind:{kind} profile exits 0", str(rc))
            check("== notes[]" in out,
                  f"and a kind:{kind} run reaches the notes[] block, which is "
                  "where a real sweep aborted after spending its captures",
                  out[-500:])
            check("[Fake] navigation sweep" in out or "Fake navigation sweep" in out,
                  f"the kind:{kind} note names the game", out[-500:])
            if kind == "ram":
                check("values swept 006D:01" in out,
                      "the RAM note lists the selector cheats the sweep pinned",
                      out[-500:])
            else:
                check("places swept One" in out,
                      "and the input note lists the places the entry scripts "
                      "selected, by title", out[-500:])


for _fn in (_input_kind_plan, _kind_is_explicit, _contra_plan_is_unchanged,
            _per_value_scripts, _extra_pins, _ram_check_plan,
            _missing_scripts_are_named, _ram_check_reads,
            _the_caveat_covers_every_address_the_session_pins,
            _the_gate_is_the_baseline_and_unique_only_reads,
            _placeholders_are_not_keys_a_session_can_claim,
            _the_unit_that_can_move_is_the_drawn_key,
            _union_units, _rom_chr, _pack_hires_resolution,
            _rescore_reads_the_packs_a_sweep_left, _rescore_cli,
            _the_union_row_is_the_rules_a_run_wrote,
            _the_two_dialects_meet_at_the_pattern_they_name,
            _an_index_needs_the_rom_and_a_pattern_does_not,
            _a_different_ver_base_is_provenance_not_a_gate,
            _the_rescore_scores_the_reference_at_the_pattern,
            _summary_document, _the_run_notes_are_built_for_both_selector_kinds):
    case(_fn, _fn.__name__.lstrip("_"))

print(f"{len(PASSED)}/{len(PASSED) + len(FAILED)} passed")
for f in FAILED:
    print(f"  FAIL {f}")
sys.exit(1 if FAILED else 0)
