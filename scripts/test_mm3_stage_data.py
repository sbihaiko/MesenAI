#!/usr/bin/env python3
"""The Mega Man 3 stage data files, checked as data.

F14.14/F14.15 owner three files that a search, a Jev question and a coverage
pass will read: `scripts/stages/mm3/ram-map.json` (which addresses the game's
state lives at, and which claims were ruled out), `cheats.json` (the RAM-only
codes, ADR-0184) and `jev-tips.json` (the situation tips and their triggers,
ADR-0238 section 3). None of them has a consumer yet, which is exactly when a
schema drifts: the consumer would then be written against a shape the file no
longer has.

What is checked, and what is deliberately not:

  * every address is four hex digits below $0800 - ADR-0184 section 1's rule
    that a code or a map entry is NES internal RAM and nothing above it, where
    a number is a mirror, a register or the cartridge;
  * a cheat code is the tool's `AAAA:VV[:CC]` form, never a Game Genie letter
    code (a PRG patch by construction, ADR-0184 section 1);
  * a jev tip's trigger names a key that `ram-map.json` actually verifies, or
    one of the run-level keys its own `triggerKeys.run` declares - a tip gated
    on an address nobody measured is skipped at question time, and this is what
    stops it being written in the first place;
  * a tip has its text and at least one https source, because the file's rule
    is "our own words, sources cited";
  * a route script is `<count>f <buttons>` with the buttons a NES pad has
    (ADR-0157 section 1: the count carries its own unit);
  * and, last, both stage sets are put through the *reader*:
    `RamMap.load`, `Tips.load`, every macro a tip asks for against
    `jev_harness.macro_table`, and `Tip.holds`/`Condition.holds` on a known
    state. A data file and its only consumer drift apart while both suites
    pass, and this is the check that fails when they do.

It does NOT check that an address means what the file says - only an experiment
can (see each entry's `evidence`), and the honesty of that column is not
something a test can read.

Run:  python3 scripts/test_mm3_stage_data.py
"""
import json
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
STAGE = HERE / "stages" / "mm3"
RAM_MAP = STAGE / "ram-map.json"
CHEATS = STAGE / "cheats.json"
TIPS = STAGE / "jev-tips.json"
ROUTES = sorted(STAGE.glob("*.txt"))

ADDRESS = re.compile(r"^[0-9A-F]{4}$")
CODE = re.compile(r"^([0-9A-F]{4}):([0-9A-F]{2})(:[0-9A-F]{2})?$")
MACRO_LINE = re.compile(r"^(\d+)f ([UDLRABST]+|-)$")
FPS = 60.0988

_FAILURES = []


def check(cond, name, detail=""):
    if cond:
        print(f"ok   {name}")
    else:
        print(f"FAIL {name}: {detail}")
        _FAILURES.append(name)


def load(path):
    return json.loads(path.read_text())


def in_ram(address, what):
    check(bool(ADDRESS.match(address)), f"{what} is four hex digits", address)
    if ADDRESS.match(address):
        check(int(address, 16) <= 0x7FF,
              f"{what} ${address} is NES internal RAM", "above $07FF")


def check_ram_map():
    doc = load(RAM_MAP)
    for key in ("game", "console", "rom", "verified", "open", "rejected"):
        check(key in doc, f"ram-map.json has {key}", "missing")
    for name, entry in doc["verified"].items():
        check(isinstance(entry, dict) and entry.get("evidence"),
              f"verified.{name} cites the experiment that confirmed it",
              str(entry)[:60])
        address = entry.get("address")
        if address is not None:
            in_ram(address, f"verified.{name}.address")
    #A ruled-out entry must say what ruled it out, or the next reader re-adopts it.
    for name, entry in doc["rejected"].items():
        check(entry.get("ruled_out") and entry.get("evidence"),
              f"rejected.{name} says what ruled it out and on what evidence",
              str(entry)[:60])
    for name, entry in doc["open"].items():
        check(entry.get("method"),
              f"open.{name} names the experiment that would settle it",
              str(entry)[:60])


def check_cheats():
    doc = load(CHEATS)
    check(doc.get("game") == "Mega Man 3", "cheats.json names the game")
    codes = doc.get("cheats")
    check(isinstance(codes, list) and codes, "cheats.json carries codes")
    for entry in codes or []:
        code = entry.get("code", "")
        match = CODE.match(code)
        check(bool(match), f"cheat {code!r} is the tool's AAAA:VV[:CC] form",
              "a Game Genie letter code is a PRG patch and is refused")
        if match:
            in_ram(match.group(1), f"cheat {code}")
        for key in ("label", "source", "what_it_does", "verified", "evidence"):
            check(entry.get(key), f"cheat {code} carries {key}", "missing")
        #A code that does not hold is the interesting case, and it must say so
        #rather than be dropped: the finding is why the coverage pass is shaped
        #the way it is.
        if entry.get("verified") != "yes":
            check("not" in entry.get("status", ""),
                  f"cheat {code} that does not hold says so in `status`",
                  entry.get("status", "missing"))


def check_tips():
    doc = load(TIPS)
    #The shape is shared with the other stage sets, so a tip file that drifts
    #from `f14.14-tips-1` is a file the harness cannot read at all.
    check(doc.get("schema") == "f14.14-tips-1",
          "jev-tips.json declares the shared f14.14-tips-1 schema",
          str(doc.get("schema")))
    siblings = sorted((STAGE.parent).glob("*/jev-tips.json"))
    for sibling in siblings:
        check(load(sibling).get("schema") == doc.get("schema"),
              f"{sibling.parent.name}/jev-tips.json declares the same schema",
              "the sets would need one reader each")
    check(doc.get("ramMap", "").endswith("ram-map.json"),
          "jev-tips.json points at the ram map its triggers are checked against")
    ram_map = load(RAM_MAP)
    verified = set(ram_map["verified"])
    known = verified | set(doc.get("runKeys", {}))
    macros = set(doc.get("macroVocabulary", {}))
    for tip in doc.get("tips", []):
        ident = tip.get("id", "?")
        check(tip.get("tip"), f"tip {ident} carries its text")
        check(bool(tip.get("sources")), f"tip {ident} cites at least one source")
        for source in tip.get("sources", []):
            check(source.startswith("https://"),
                  f"tip {ident} source is https", source)
        check(tip.get("macro") in macros,
              f"tip {ident} asks for a declared macro {tip.get('macro')!r}",
              f"declared: {sorted(macros)}")
        when = tip.get("when") or []
        check(bool(when), f"tip {ident} has a `when`")
        for clause in when:
            field = clause.get("field")
            check(field in known,
                  f"tip {ident} `when` field {field!r} is a RAM or run key",
                  f"known: {sorted(known)}")
            if field in verified:
                entry = ram_map["verified"][field]
                #A field a trigger may gate on is one the map can *read*: an
                #address, or an `expr` over fields it reads (Mega Man 3's abs_x
                #is the page, the camera and the player's column - a gate on the
                #wrapping byte would drift across a wrap, which is the bug the
                #monotone field exists to prevent). Only an entry that is
                #neither - a number the map records as not being RAM - is
                #unreadable at question time.
                check(entry.get("address") is not None or entry.get("expr"),
                      f"tip {ident} gates on {field!r}, which the map can read",
                      f"an entry with neither an address nor an expr is not read "
                      f"at question time: {json.dumps(entry)[:120]}")
            bounded = [k for k in clause if k in ("min", "max")]
            check(bool(bounded) or "value" in clause,
                  f"tip {ident} clause on {field!r} bounds or pins the value",
                  str(clause))
            if "min" in clause and "max" in clause:
                check(clause["min"] <= clause["max"],
                      f"tip {ident} range on {field!r} is ordered", str(clause))


def check_harness_reads_the_files():
    """The same files through the *code* that consumes them.

    Checking a data file as data is not enough: `scripts/jev_harness.py` is the
    only reader, and the two can drift apart while both suites pass. This loads
    each stage set's `ram-map.json` through `RamMap.load` and its
    `jev-tips.json` through `Tips.load`, resolves every macro a tip asks for
    against the harness's own table, and puts a state through
    `Tip.holds`/`Condition.holds` - which is where a `value` clause that never
    became `equals`, or an address the reader silently drops, shows up.
    """
    sys.path.insert(0, str(HERE))
    import jev_harness  # noqa: PLC0415 - the reader under test

    table = jev_harness.macro_table(15)
    for game, state, expected in (
            ("mm3", {"abs_x": 200, "stage": "snake-man"}, ["big-snakey"]),
            ("ninjagaiden", {"abs_x": 987, "camera_x": 859, "hp": 16,
                             "enemies_near": 0, "stage": 0}, ["wall-pin"])):
        stage = HERE / "stages" / game
        document = load(stage / "jev-tips.json")
        ram = jev_harness.RamMap.load(stage / "ram-map.json")
        tips = jev_harness.load_tips(stage / "jev-tips.json", fields=ram.fields)
        check(bool(ram.fields) and ram.progress in ram.fields,
              f"{game}: the ram map loads through RamMap.load with a progress field",
              f"{ram.progress} of {sorted(ram.fields)}")
        check(len(tips.tips) == len(document["tips"]),
              f"{game}: every tip in the file loads through Tips.load",
              f"{len(tips.tips)} of {len(document['tips'])}")
        declared = [name for name in document.get("macroVocabulary", {})
                    if not name.startswith("_")]
        for name in declared + [tip.macro for tip in tips.tips if tip.macro]:
            check(f"{name}_15" in table,
                  f"{game}: the macro {name} a tip asks for is one the harness plays",
                  f"not in {sorted(table)}")
        check([tip.id for tip in tips.matching(state)] == expected,
              f"{game}: the tips that hold on a known state are the expected ones",
              str([tip.id for tip in tips.matching(state)]))
        #A tip is gated, never always-on: a state with no fields at all (the
        #shape a proposal arrives in) must turn every one of them off.
        for tip in tips.tips:
            check(not tip.conditions or not tip.holds({}),
                  f"{game}: tip {tip.id} does not hold on a state with no fields",
                  str(tip.conditions))


def check_the_progress_field_is_monotone():
    """`progress` is the monotone level x, and the wrapping bytes are not it.

    The two bytes the map used to steer by are each ONE byte wide and each wraps
    at 256 - `$0025` the camera's scroll and `$0027` the player's x within the
    page - so a search scored on either walks *backwards* across a wrap and
    reports the page's end as reached. The first F14.15 pass had to chain the
    level page by page by hand because of it (`runs/f1415/hunt_mm3.py`).

    What closed it is `$002D`, the level's page (measured 2026-09-26, 7 runs and
    2164 one-frame rows over one session: it moves at a wrap and only at a wrap).
    This check is the arithmetic over the four states of that route, taken from
    their raw bytes: the derived numbers have to be strictly increasing where the
    raw ones are not, and the stall has to read what the measurement says.
    """
    sys.path.insert(0, str(HERE))
    import jev_harness  # noqa: PLC0415 - the reader under test

    ram = jev_harness.RamMap.load(HERE / "stages" / "mm3" / "ram-map.json")
    check(ram.progress == "abs_x" and ram.screen == "camera_x",
          "the map's own progress is the derived abs_x and its screen the derived "
          "camera_x, so a run needs no --progress-field for this game",
          f"{ram.progress} / {ram.screen}")
    for name in ("level_page", "camera_scroll_x", "player_x_low",
                 "player_screen_x", "camera_x", "abs_x"):
        check(name in ram.fields, f"the map carries {name}", str(sorted(ram.fields)))

    document = load(HERE / "stages" / "mm3" / "ram-map.json")
    check("level_x_high_byte" not in document.get("open", {}),
          "the open problem `level_x_high_byte` is closed, not still open",
          str(list(document.get("open", {}))))
    check("002D" in document["verified"]["level_page"]["address"].upper()
          or "0X2D" in document["verified"]["level_page"]["address"].upper(),
          "and it is closed by a measured address, with its evidence beside it",
          str(document["verified"]["level_page"].get("address")))

    #The four states of F14.15's Mega Man 3 route, as raw bytes: (page, scroll,
    #player x within the page). 187/280/529/696 is the level, 315/408/657/824 the
    #player - and the raw player byte reads 59/152/145/56, which is not a route.
    class Raw:
        def __init__(self, page, scroll, player):
            self.ram = bytearray(0x800)
            self.ram[0x2D] = page
            self.ram[0x25] = scroll
            self.ram[0x27] = player
            for address in (0x12, 0xA2, 0xAE, 0x39):
                self.ram[address] = 0

        def read_ram(self, *items):
            return [bytes(self.ram[item[0]:item[1] + 1]) if isinstance(item, tuple)
                    else self.ram[item] for item in items]

    rows = [(0, 187, 59, "page 1, the committed route's end"),
            (1, 24, 152, "page 2 start"),
            (2, 17, 145, "page 3 start"),
            (2, 184, 56, "the page-3 stall F14.15 measures")]
    states = [(what, ram.read(Raw(page, scroll, player)))
              for page, scroll, player, what in rows]
    raw = [state["player_x_low"] for _what, state in states]
    level = [state["camera_x"] for _what, state in states]
    absolute = [state["abs_x"] for _what, state in states]
    check(raw == [59, 152, 145, 56],
          "the wrapping byte is not a route: it falls where the level rises", str(raw))
    check(level == [187, 280, 529, 696] and absolute == [315, 408, 657, 824],
          "the derived level and player x are the measured monotone numbers, in "
          "order", f"{level} / {absolute}")
    check(all(later > earlier for earlier, later in zip(level, level[1:]))
          and all(later > earlier for earlier, later in zip(absolute, absolute[1:])),
          "both are strictly increasing over the route", f"{level} / {absolute}")
    check(states[-1][1]["abs_x"] == 824,
          "and the stall F14.15 measures stands at abs_x 824, page 2 / camera 696",
          str(states[-1][1]["abs_x"]))
    check(int(states[-1][1]["camera_x"] // ram.screen_width) == 2,
          "the screen index a run rewinds against is the level's page",
          str(int(states[-1][1]["camera_x"] // ram.screen_width)))

    #The tips' bands were written for this scale (140..1023, four pages of 256).
    #The stall is the anchor that matters: a tip has to be able to fire there.
    tips = jev_harness.load_tips(HERE / "stages" / "mm3" / "jev-tips.json",
                                 fields=ram.fields)
    at_stall = dict(states[-1][1], stage="snake-man")
    check([tip.id for tip in tips.matching(at_stall)] == ["cloud-platforms"],
          "and a tip's band reaches the stall at last - the first pass had none "
          "that could, because every band starts above the wrapping byte's range",
          str([tip.id for tip in tips.matching(at_stall)]))
    fired = {what: [tip.id for tip in tips.matching(dict(state, stage="snake-man"))]
             for what, state in states}
    check(all(len(ids) <= 1 for ids in fired.values()) and
          len({ids[0] for ids in fired.values() if ids}) == 4,
          "each of the route's four states falls in a band of its own, in order",
          json.dumps(fired))


def check_the_stage_reaches_the_state():
    """The run-level `stage` an mm3 tip is gated on has to come from somewhere.

    All six mm3 tips are gated on `stage` as well as on a position band (they
    are per-boss), and `stage` is not a RAM byte: `ram-map.json` records its
    `stage_id` as `open`, unconfirmed. So a run declares it - `--stage`, or a
    `stage` in the stage set - and the map carries it on every state it reads,
    which is the only way a tip gated on it can ever hold.
    """
    sys.path.insert(0, str(HERE))
    import jev_harness  # noqa: PLC0415 - the reader under test

    stage = HERE / "stages" / "mm3"
    plain = jev_harness.RamMap.load(stage / "ram-map.json")
    tips = jev_harness.load_tips(stage / "jev-tips.json", fields=plain.fields)
    gated = jev_harness.tips_gated_on_stage(tips, plain.fields)
    check(len(gated) == 6 and len(gated) == len(tips.all),
          "every mm3 tip is gated on the run-level `stage`", str(gated))
    #Ninja Gaiden's map verifies a `stage` byte of its own ($006D), so its two
    #stage-naming tips read RAM and fire with or without a declared stage: the
    #note is only about a game whose map has no such byte.
    ng = HERE / "stages" / "ninjagaiden"
    ng_map = jev_harness.RamMap.load(ng / "ram-map.json")
    ng_tips = jev_harness.load_tips(ng / "jev-tips.json", fields=ng_map.fields)
    check("stage" in ng_map.fields
          and jev_harness.tips_gated_on_stage(ng_tips, ng_map.fields) == [],
          "a map with its own `stage` byte gates nothing on the run key",
          f"{sorted(ng_map.fields)[:4]}")

    #The stage set names none today, so `--stage` is the source - and the flag
    #wins over the file when a set does name one.
    named, known = jev_harness.resolve_stage(stage / "stage-set.json", None)
    check(named is None and known == [],
          "mm3's stage set names no stage, so nothing is guessed from it",
          f"{named!r} of {known}")
    named, known = jev_harness.resolve_stage(stage / "stage-set.json", "snake-man")
    check(named == "snake-man", "--stage supplies the run's stage", str(named))

    #And the map hands it to every state it reads, which is where a tip's
    #trigger looks for it.
    ram = jev_harness.RamMap.load(stage / "ram-map.json",
                                  run_keys={"stage": named})
    check(ram.run_keys == {"stage": "snake-man"} and "stage" not in ram.fields,
          "the run key rides on the map and stays out of its RAM fields",
          str(ram.run_keys))
    state = dict.fromkeys(ram.fields, 0)
    state.update(ram.run_keys)
    state["abs_x"] = 200
    check([tip.id for tip in tips.matching(state)] == ["big-snakey"],
          "a tip gated on `stage` holds on a state the map carries it on",
          str(tips.matching(state)))
    check(tips.matching({key: value for key, value in state.items()
                         if key != "stage"}) == [],
          "and does not hold without it - the gate fails closed, as before")


def check_routes():
    for path in ROUTES:
        total = 0
        for number, line in enumerate(path.read_text().splitlines(), 1):
            if not line.strip():
                continue
            match = MACRO_LINE.match(line)
            check(bool(match),
                  f"{path.name}:{number} is `<count>f <buttons>`", line)
            if match:
                total += int(match.group(1))
        check(total > 0, f"{path.name} has frames", str(total))
        #A route is judged by the run it makes: the recorder takes seconds.
        check(0 < total < 60 * FPS * 10,
              f"{path.name} is under ten minutes of emulated play",
              f"{total} frames")


def main():
    check(RAM_MAP.exists() and CHEATS.exists() and TIPS.exists(),
          "the three Mega Man 3 data files exist")
    check_ram_map()
    check_cheats()
    check_tips()
    check_harness_reads_the_files()
    check_the_progress_field_is_monotone()
    check_the_stage_reaches_the_state()
    check_routes()
    print(f"\n{len(_FAILURES)} failure(s)")
    return 1 if _FAILURES else 0


if __name__ == "__main__":
    sys.exit(main())
