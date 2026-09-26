#!/usr/bin/env python3
"""One command that records a whole game's navigation sweep (ADR-0184).

ADR-0184's amendment of 2026-09-14 proved the front: Contra's `$0030` holds the
current level, and pinning it for a run warps the game to a stage without
drawing anything foreign, so such a run may feed **all four** of ADR-0183's
surfaces. Eleven sessions - one per stage plus three bosses - covered 58.9% of
the reference pack's tiles against 53.8% for 77 accumulated blind recordings,
and the union of the two reached 64.6%.

That was eleven hand-typed `headless_record` invocations. This script is the
same eleven sessions as one command, driven by a data file:

    scripts/record_navigation_sweep.py --profile scripts/stages/contra/navigation.json \\
        --rom "<dir>/Contra (1988) (Konami).nes" --out <work>/sweep \\
        --states <dir-with-the-boss-.mss-files> --jobs 4 \\
        --reference "<ref pack>/hires.txt"

A second game is a second profile beside `navigation.json`, not a code change.

What it does, in order:

1. Reads the profile and builds a plan. Every cheat - the selector and every
   extra pin - is validated against ADR-0184 s1 *here*, before a single process
   starts: the `AAAA:VV[:CC]` form only, and `AAAA < 0x0800`. `headless_record`'s
   own `parseRamCheat()` is the second gate; a plan that would be refused there
   is refused here first, so an artist never watches ten good sessions run and
   the eleventh die.
2. Runs one `headless_record` per navigation value, in parallel (`--jobs`).
   Each session gets its own directory with its own hard-linked copy of the ROM
   and therefore its own `mesen-home` (`headless_record.cpp:801` puts the home
   beside the output prefix) and its own bootstrap pack folder, which is why
   parallel sessions cannot collide: nothing is written outside the session
   directory.
3. Records the provenance. ADR-0184's phrase for the obligation is "the code
   travels with the art, verbatim and with its address", so every session
   writes the cheat verbatim into a `notes[]` line, and the sweep writes a
   `notes[]` summary naming the address, its published source and every value
   swept. Those lines are what a kit generator hands to ADR-0183 s3.
4. Measures (ADR-0239 s4, s5). A session must prove it went somewhere: `new` is
   the drawn keys - `(tileData, palette)`, the placeholders excluded - that the
   *baseline* packs do not hold, and a session with none of it is reported
   `did-not-warp` and counts in no total. `unique` is the keys no other session
   of the sweep and no baseline holds, reported beside it and never gating: two
   tracks that share a tileset are both legitimate warps with `unique == 0`. The
   totals are ADR-0194 s4's union - drawn keys *and* distinct tile data - with
   `--baseline` folding the game's existing pack in as *before*, and `--rom-chr`
   adding the denominator that needs no third-party pack: the ROM's own
   non-blank CHR patterns the union names.

Union. Each session keeps its own pack, the shape `record_stages.sh` already
produces, and the surfaces union them downstream: the CHR kit's repeatable
`artist_chr_kit.py --also <pack>` (#199 - "a second recording of the same ROM
donates CHR cells as evidence"), and the coverage number this script prints,
which is a set union over the sessions' tile keys.

The other candidate mechanism does not work and was measured rather than
assumed. `HdPackBuilder` does load an existing `hires.txt` out of its save
folder on start and accumulate into it (`HdPackBuilder.cpp:46-49`), so pointing
several sessions at one folder ought to union them in the builder. It does not,
because the builder never starts a second time:
`MepPackManager::StartBootstrapIfNeeded` (`MepPackManager.cpp:200-210`) clears
`needTextures` as soon as *something already dresses this ROM*, and after the
first session the sibling `auto/` pack is exactly that. Measured 2026-09-14,
two 60 s sessions into one folder: the second returned in 7.1 s against the
first's 16.7 s and the pack's tile count did not move (2173 -> 2173). A shared
folder therefore silently records nothing, which is worse than no union at all,
so this script does not offer one.

The profile's `navigation.kind` is `"ram"` (the default, and the amendment's
mechanism: `address` is required and each value is pinned as `address:value`)
or `"input"`, where the place is reached by the game's own selector - a menu, a
track choice, a password - and the value's own `entry` script is what selects
it, so the session carries no cheat at all (ADR-0239 s1's first rung). Either
way a value may override `defaults.entry`/`defaults.body`, and
`defaults.cheats`/`values[].cheats` add RAM-only pins that ride with the run
(ADR-0239 s3: a lives pin keeps a blind body on the stage instead of recording
GAME OVER). A value may also carry a `ramCheck` - an address and the value(s)
the session's final state must read there - which is what tells a warp that
worked from one that left the game where it was.

`--dry-run` prints the plan, the generated input scripts' lengths and every
command line, and runs nothing.
"""

import argparse
import concurrent.futures
import json
import math
import os
import re
import shutil
import subprocess
import sys
import time
from pathlib import Path

# ADR-0239 s2/s4/s5 split the recorder from the metric: everything host-free
# and countable lives in nav_sweep_metrics.py, and it is re-exported here so a
# caller (and this repo's test) has one entry point.
from nav_sweep_metrics import (  # noqa: F401 - re-exported for callers
    CHR_RAM_NOTE, find_pack_hires, pack_hires, pack_rule_keys,
    pack_seen_rule_keys, pack_seen_tile_data, pack_tile_data,
    ram_check, ram_check_spec,
    rom_chr_coverage, rom_chr_seen_coverage, score_sessions, summary_document,
    tile_data_keys, totals_document,
)

REPO_ROOT = Path(__file__).resolve().parent.parent
RECORDER = REPO_ROOT / "scripts" / "headless_record"
NTSC_FRAME_RATE = 60.0988  # HeadlessInputScript::NtscFrameRate

ADR = "ADR-0184 (amended 2026-09-14)"
ADR_0239 = "ADR-0239"
KINDS = ("ram", "input")


# --- ADR-0184 s1: the cheat rule, checked before anything is launched --------
# "The rule is enforced by the parser, not by discipline. A recording harness
# that accepts a cheat MUST refuse the run - not warn - when a code fails
# either check, and MUST name the offending code."
CHEAT_RE = re.compile(r"^([0-9A-Fa-f]{4}):([0-9A-Fa-f]{2})(?::([0-9A-Fa-f]{2}))?$")


def validate_ram_cheat(code: str) -> str:
    """Return the code unchanged, or raise ValueError naming what is wrong."""
    m = CHEAT_RE.match(code)
    if not m:
        raise ValueError(
            f'refused cheat "{code}": not the NesCustom AAAA:VV[:CC] form. '
            "A Game Genie or Pro Action Rocky letter code is a PRG patch by "
            f"construction and is refused outright ({ADR} s1)."
        )
    addr = int(m.group(1), 16)
    if addr >= 0x0800:
        raise ValueError(
            f'refused cheat "{code}": address ${addr:04X} is outside the NES\'s '
            f"internal RAM ($0000-$07FF) ({ADR} s1)."
        )
    return code


def pin_list(raw, where: str) -> list:
    """ADR-0239 s2/s3: the extra RAM-only pins a session carries.

    Each is `{"code", "label", "source"}`. The code goes through ADR-0184 s1
    here, before anything runs, and the source is required: s5 says "a cheat
    used by a recording comes from a source that names it", so a pin nobody
    published is refused rather than recorded as provenance that says nothing.
    """
    out = []
    for pin in raw or []:
        code = str(pin.get("code", "")).strip()
        source = str(pin.get("source", "")).strip()
        if not source:
            raise ValueError(
                f'{where}: RAM pin "{code}" carries no source. {ADR} s5: a code '
                "that a recording uses comes from a source that names it, and a "
                "pin without one is a code somebody invented."
            )
        out.append({"code": validate_ram_cheat(code).upper(),
                    "label": str(pin.get("label", "")).strip(),
                    "source": source})
    return out


def pins_note(pins: list) -> str:
    """The pin sentences ADR-0184 s2 obliges a kit's `notes[]` line to carry."""
    return "".join(
        f" Extra RAM pin {p['code']}"
        f"{' (' + p['label'] + ')' if p['label'] else ''} pinned for the whole "
        f"run as well: {p['source']}" for p in pins)


# --- input scripts -----------------------------------------------------------
STEP_RE = re.compile(r"^\s*(\d+)\s*([fs])\b", re.IGNORECASE)


def script_frames(path: Path) -> int:
    """Length of an input script in emulated frames.

    Mirrors HeadlessInputScript: blank lines and lines starting with '#' are
    ignored, 'f' is frames and 's' is seconds resolved at the region's nominal
    rate, rounded to nearest.
    """
    total = 0
    for line in path.read_text().splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith("#"):
            continue
        m = STEP_RE.match(stripped)
        if not m:
            continue
        n = int(m.group(1))
        total += n if m.group(2).lower() == "f" else round(n * NTSC_FRAME_RATE)
    return total


def script_body(path: Path) -> list:
    """The script's own lines, comments and blanks dropped."""
    return [l.rstrip() for l in path.read_text().splitlines()
            if l.strip() and not l.strip().startswith("#")]


def build_sweep_script(entry: Path, body: Path, seconds: float, dest: Path) -> dict:
    """Write <entry> once, then <body> repeated until the run is covered.

    ADR-0184's own measurement is the reason this exists: Contra's
    `stage1-run.txt` is 3300 frames inside a 300 s run, "so for 82% of the
    recording nothing was pressed and the player simply stood", and repeating
    the body to cover the whole run reached the same panorama a cheat had
    bought. Effective input time is the cheapest lever there is, and it costs
    nothing, so the sweep takes it by default.
    """
    target = int(round(seconds * NTSC_FRAME_RATE))
    entry_lines = script_body(entry)
    entry_frames = script_frames(entry)
    body_lines = script_body(body)
    body_frames = script_frames(body)
    if body_frames <= 0:
        raise ValueError(f"{body} has no timed step")
    repeats = max(1, math.ceil((target - entry_frames) / body_frames))
    out = [
        f"# generated by scripts/record_navigation_sweep.py - do not edit",
        f"# entry: {entry.name} ({entry_frames} f)",
        f"# body:  {body.name} ({body_frames} f) x {repeats}, to cover {target} f "
        f"({seconds:g} s at {NTSC_FRAME_RATE} fps)",
    ]
    out += entry_lines
    for i in range(repeats):
        out.append(f"# --- {body.name} pass {i + 1}/{repeats}")
        out += body_lines
    dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_text("\n".join(out) + "\n")
    return {
        "path": str(dest),
        "entryFrames": entry_frames,
        "bodyFrames": body_frames,
        "repeats": repeats,
        "totalFrames": entry_frames + repeats * body_frames,
        "targetFrames": target,
    }


# --- the plan ----------------------------------------------------------------
def _script(prof_dir: Path, rel: str, what: str, who: str) -> Path:
    """A declared script path, refused at plan time when nothing is declared.

    `prof_dir / ""` is the profile's own folder, so a missing declaration used
    to be read as a *directory* script and died inside `read_text` - after the
    header had been printed and the first session had started.
    """
    if not rel:
        raise ValueError(
            f'{who} declares no {what} script: give it one, or declare '
            f"defaults.{what} ({ADR_0239} s2).")
    return prof_dir / rel


def build_plan(profile: dict, prof_dir: Path, rom: Path, out: Path,
               states: Path, seconds: float, only: set) -> list:
    nav = profile["navigation"]
    kind = nav.get("kind", "ram")
    if kind not in KINDS:
        raise ValueError(
            f'navigation.kind "{kind}" is neither "ram" (a published RAM '
            'selector pinned for the run) nor "input" (the game\'s own '
            f"selector, typed by the value's entry script) ({ADR_0239} s2).")
    addr = ""
    if kind == "ram":
        if not nav.get("address"):
            raise ValueError(
                f'navigation.kind is "ram" and the profile declares no '
                f"navigation.address: a RAM selector is an address and the "
                f"values it is set to ({ADR_0239} s2).")
        addr = nav["address"].upper()
    defaults = profile.get("defaults", {})
    entry = defaults.get("entry", "")
    body = defaults.get("body", "")
    default_pins = pin_list(defaults.get("cheats"), "defaults.cheats")
    sessions = []

    for item in nav.get("values", []):
        name = item["name"]
        if only and name not in only:
            continue
        who = f'navigation value "{name}"'
        sdir = out / name
        s_entry = _script(prof_dir, item.get("entry") or entry, "entry", who)
        s_body = _script(prof_dir, item.get("body") or body, "body", who)
        pins = default_pins + pin_list(item.get("cheats"), f"{who}.cheats")
        checks = item.get("ramCheck")
        check = ram_check_spec(checks, f"{who}.ramCheck") if checks else None
        title = item.get("title", "")
        if kind == "ram":
            if not item.get("value"):
                raise ValueError(
                    f'{who} declares no value: a RAM selector is pinned as '
                    f"address:value ({ADR_0239} s2).")
            cheat = validate_ram_cheat(f"{addr}:{item['value'].upper()}")
            note = (
                f"navigation pass ({ADR} s2, third row): RAM cheat {cheat} pinned "
                f"for the whole run - ${addr} is {profile['game']}'s "
                f"{nav.get('label', 'navigation selector')}, value 0x{item['value'].upper()}"
                f"{' = ' + title if title else ''}. "
                f"Source: {nav['source']} "
                f"Recorded {seconds:g} s from power-on with {s_entry.name} + "
                f"{s_body.name} repeated. The selector is pinned for the whole run, "
                "so the stage-clear transition is never recorded."
            )
            cheats = [cheat] + [p["code"] for p in pins]
        else:
            # ADR-0239 s1's first rung: the game's own selector, as input.
            cheat = None
            note = (
                f"navigation pass ({ADR} s2, first row): the selector is input, "
                f"not a cheat. {title or name} is chosen by {s_entry.name} from "
                f"power-on ({nav.get('label', 'navigation selector')}: "
                f"{nav['source']} Recorded {seconds:g} s with {s_body.name} "
                "repeated. The run never clears the stage, so the stage-clear "
                "transition is never recorded."
            )
            cheats = [p["code"] for p in pins]
        sessions.append({
            "name": name,
            "kind": "navigation",
            "title": title,
            "cheat": cheat,
            "cheats": cheats,
            "dir": sdir,
            "entry": s_entry,
            "body": s_body,
            "state": None,
            "input": None,
            "seconds": seconds,
            "ramCheck": check,
            "selectorAddress": addr or None,
            "pins": pins,
            "note": note + pins_note(pins),
        })

    for room in profile.get("rooms", []):
        name = room["name"]
        if only and name not in only:
            continue
        state = (states / room["state"]) if states else None
        pins = default_pins
        # A level selector picks a level, not a room inside one. With the
        # default kind that is the address; with an input selector there is no
        # address to name, so the sentence names the selector instead.
        why = (f"${addr} selects a level and not a room inside it" if addr else
               f"the {nav.get('label', 'selector')} is chosen by input and "
               "reaches no room inside it")
        head = ("clean pass " + f"({ADR} s2, first row): no selector cheat; the "
                f"run carries {len(pins)} RAM pin(s) the selector does not need."
                if pins else
                f"clean pass ({ADR} s2, first row): no cheat.")
        sessions.append({
            "name": name,
            "kind": "room",
            "title": room.get("title", ""),
            "cheat": None,
            "cheats": [p["code"] for p in pins],
            "dir": out / name,
            "entry": None,
            "body": None,
            "state": state,
            "input": prof_dir / room["input"],
            "seconds": seconds,
            "ramCheck": None,
            "selectorAddress": addr or None,
            "pins": pins,
            "note": (
                f"{head} {why}, so this room is entered from the "
                f"save state {room['state']} minted by the F9.22 chain and played "
                f"by {room['input']}." + pins_note(pins)
            ),
        })

    for s in sessions:
        s["prefix"] = s["dir"] / "rec"
        # ADR-0239 s4: the state a named ramCheck is read off. Written by
        # `save-state=`, which the recorder only writes when the run reached
        # its frame target - an incomplete run leaves no state, and the check
        # reports exactly that instead of passing on a stale file.
        s["stateOut"] = Path(str(s["prefix"]) + "-final.mss")
    return sessions


def discover_sessions(out: Path, only=None):
    """The sessions a previous sweep left in `out`, as `score_sessions` reads them.

    `--rescore` exists because §4's gate can be amended - it was, on
    2026-09-26 - without re-recording anything: the packs on disk carry
    everything §4 and §5 need, and re-running five games' sessions to re-read a
    verdict would spend the whole capture budget again.

    Returns `(sessions, empty)`. A directory that holds no pack is named in
    `empty`, never scored: that is the shape a crashed or skipped session
    leaves, and `new == 0` would call it `did-not-warp`, which is a claim about
    what the run recorded. A `--out` that is not a directory is refused, for the
    same reason a `--baseline` that resolves to nothing is (ADR-0184 §1).
    """
    out = Path(out)
    if not out.is_dir():
        raise ValueError(f"--rescore: {out} is not a directory to read sessions from")
    sessions, empty = [], []
    for d in sorted(p for p in out.iterdir() if p.is_dir()):
        if only and d.name not in only:
            continue
        hires = find_pack_hires(d)
        if hires.is_file():
            sessions.append({"name": d.name, "status": "ok", "hires": str(hires)})
        else:
            empty.append(d.name)
    return sessions, empty


def missing_scripts(sessions: list) -> list:
    """The declared scripts a plan needs and does not have (ADR-0184 s1).

    `headless_record` writes a pack from a script it can read; one it cannot is
    a session that dies after its neighbours have run. The cheat rule is
    refused at plan time for exactly that reason, so a script path is too - and
    the path is named, because "no such file" without the file is the message
    that costs an hour.
    """
    out = []
    for s in sessions:
        for script in (s["entry"], s["body"], s["input"]):
            if script and not script.exists():
                out.append((s["name"], script))
    return out


def command_for(session: dict, rom_in_dir: Path, script_info) -> list:
    args = [str(RECORDER), str(rom_in_dir), f"{session['seconds']:g}",
            str(session["prefix"]), "bootstrap", "hdpack-off"]
    if session["kind"] == "navigation":
        args.append(f"input={script_info['path']}")
    else:
        args.append(f"input={session['input']}")
        if session["state"]:
            args.append(f"state={session['state']}")
    for cheat in session["cheats"]:
        args.append(f"cheat={cheat}")
    if session["ramCheck"]:
        args.append(f"save-state={session['stateOut']}")
    return args


def run_session(session: dict, rom: Path, dry: bool) -> dict:
    sdir = session["dir"]
    sdir.mkdir(parents=True, exist_ok=True)
    rom_in_dir = sdir / rom.name
    script_info = None
    if session["kind"] == "navigation":
        script_info = build_sweep_script(
            session["entry"], session["body"], session["seconds"],
            Path(str(session["prefix"]) + "-input.txt"))
    cmd = command_for(session, rom_in_dir, script_info)
    result = {
        "name": session["name"],
        "kind": session["kind"],
        "title": session["title"],
        "cheat": session["cheat"],
        "cheats": list(session["cheats"]),
        "seconds": session["seconds"],
        "dir": str(sdir),
        "command": cmd,
        "inputScript": script_info,
        "note": session["note"],
        "ramCheck": None,
    }
    if dry:
        result["status"] = "dry-run"
        return result

    if session["kind"] == "room" and session["state"] and not session["state"].exists():
        result["status"] = "skipped"
        result["why"] = (f"save state not found: {session['state']} - a room needs "
                         "its state; pass --states at the folder that holds it "
                         "(.mss files are never versioned)")
        return result

    # The builder writes beside the ROM, so every session needs its own copy.
    if not rom_in_dir.exists():
        try:
            os.link(rom, rom_in_dir)
        except OSError:
            shutil.copy2(rom, rom_in_dir)
    # A leftover pack, or the `.bootstrap` marker that says "already done",
    # would make the bootstrap skip the build - MepPackManager.cpp:200-210
    # refuses to dress a ROM something already dresses. A session is always a
    # fresh pack, as in record_stages.sh.
    (sdir / ".bootstrap").unlink(missing_ok=True)
    shutil.rmtree(sdir / rom.stem, ignore_errors=True)

    started = time.time()
    with open(sdir / "rec_stdout.log", "w") as log:
        proc = subprocess.run(cmd, stdout=log, stderr=subprocess.STDOUT)
    result["wallSeconds"] = round(time.time() - started, 1)
    result["exit"] = proc.returncode
    result["status"] = "ok" if proc.returncode == 0 else "failed"
    hires = find_pack_hires(sdir)
    if hires.exists():
        result["hires"] = str(hires)
        # what artist_cover.py and artist_chr_kit.py --also take
        result["pack"] = str(hires.parent.parent)
    else:
        result["hires"] = None
        result["pack"] = None
    if session["ramCheck"]:
        result["ramCheck"] = ram_check(session["ramCheck"], session["stateOut"],
                                       pinned=session["selectorAddress"])
    return result


def _rom_chr_line(row, seen) -> str:
    """§5.2's row, with the row that can move beside it.

    A pack the recorder wrote carries a `defaultTile` placeholder for every CHR
    index, so §5.2 read literally is 100% for every CHR ROM game (measured
    2026-09-26 on all four of the F14.16 baselines). The bracketed figure is
    that row; the leading one counts the rules a run really wrote, which is
    what a reader means by coverage.
    """
    if row is None:
        return "--  (needs --rom-chr)"
    if row["status"] == CHR_RAM_NOTE:
        return CHR_RAM_NOTE
    return (f"{seen['named']}/{seen['patterns']} ({seen['pct']:.1f}%) written"
            f"  [every rule: {row['pct']:.1f}%]")


def main() -> int:
    ap = argparse.ArgumentParser(
        description="Record a whole navigation sweep as one command (ADR-0184, "
                    "ADR-0239 s2/s4/s5).")
    ap.add_argument("--profile", type=Path, default=None,
                    help="game profile, e.g. scripts/stages/contra/navigation.json "
                         "(with --rescore it only names the game and supplies the "
                         "default --seconds)")
    ap.add_argument("--rom", type=Path, default=None)
    ap.add_argument("--out", type=Path, required=True)
    ap.add_argument("--states", type=Path, default=None,
                    help="folder holding the rooms' .mss files (never versioned)")
    ap.add_argument("--seconds", type=float, default=None,
                    help="per-session capture length; default from the profile")
    ap.add_argument("--jobs", type=int, default=4,
                    help="parallel sessions (each writes only inside its own dir)")
    ap.add_argument("--only", default=None,
                    help="comma-separated session names to run")
    ap.add_argument("--reference", type=Path, default=None,
                    help="reference pack hires.txt (or its folder) to measure "
                         "coverage against")
    ap.add_argument("--baseline", type=Path, action="append", default=[],
                    help="an existing pack to union with the sweep as the "
                         "'before' side: a pack dir (the auto/ folder, or one "
                         "holding textures/hires.txt), a hires.txt or a whole "
                         "recording dir; repeatable (ADR-0239 s5)")
    ap.add_argument("--rom-chr", action="store_true",
                    help="add the ROM's own non-blank CHR patterns as the "
                         "denominator that needs no third-party pack (ADR-0239 "
                         "s5.2); a CHR RAM game reports n/a")
    ap.add_argument("--summary", type=Path, default=None,
                    help="write the machine-readable per-session + totals JSON")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--rescore", action="store_true",
                    help="score the packs a previous sweep already wrote under "
                         "--out instead of recording anything: no ROM, no "
                         "emulator, no session rerun (ADR-0239 s4/s5)")
    args = ap.parse_args()

    profile = json.loads(args.profile.read_text()) if args.profile else {}
    if not args.rescore and not args.profile:
        ap.error("--profile is required unless --rescore reads an existing sweep")
    if not args.rescore and not args.rom:
        ap.error("--rom is required unless --rescore reads an existing sweep")
    if args.rom_chr and not args.rom:
        ap.error("--rom-chr needs --rom: the denominator is the ROM's own CHR")
    prof_dir = args.profile.resolve().parent if args.profile else Path.cwd()
    seconds = args.seconds if args.seconds is not None else \
        profile.get("defaults", {}).get("seconds", 300)
    only = set(x.strip() for x in args.only.split(",")) if args.only else set()
    # A dry run launches nothing, so there is nothing to parallelise: it goes
    # through the sequential branch, which is the one that prints each session's
    # full command. Left parallel, --dry-run printed "dry-run in None s" (there
    # is no wall clock to report) and printed no command at all - the one thing
    # it exists to show.
    jobs = 1 if (args.dry_run or args.rescore) else max(1, args.jobs)

    if not args.dry_run and not args.rescore and not RECORDER.exists():
        print(f"missing {RECORDER} - run: make capture-tool", file=sys.stderr)
        return 2
    if args.rom and not args.rom.exists():
        print(f"ROM not found: {args.rom}", file=sys.stderr)
        return 2
    # ADR-0184's "refuse, do not warn": a --baseline that resolves to nothing
    # is not an empty union, it is a wrong 'before' - every session would come
    # out with `new` equal to its whole pack.
    baseline_hires = []
    for b in args.baseline:
        h = pack_hires(b)
        if not h.is_file():
            print(f"--baseline {b} holds no pack definition (looked for {h})",
                  file=sys.stderr)
            return 2
        baseline_hires.append(h)
    reference = pack_hires(args.reference) if args.reference else None

    results = []
    if args.rescore:
        # Nothing is planned, nothing runs: the packs a previous sweep wrote are
        # the input, which is what lets an amended §4 rule be re-read without
        # spending the capture budget again.
        try:
            results, empty = discover_sessions(args.out, only)
        except ValueError as exc:
            print(exc, file=sys.stderr)
            return 2
        if not results:
            print(f"--rescore: no session pack under {args.out} - point --out at "
                  "the folder a sweep wrote its sessions into", file=sys.stderr)
            return 2
        print(f"== {args.out}: rescoring {len(results)} session(s) already on disk "
              f"({', '.join(r['name'] for r in results)})")
        if empty:
            print("   no pack, not scored: " + ", ".join(empty))
    else:
        try:
            sessions = build_plan(profile, prof_dir, args.rom.resolve(),
                                  args.out.resolve(), args.states.resolve()
                                  if args.states else None, seconds, only)
        except ValueError as exc:  # ADR-0184 s1 - refuse, do not warn
            print(exc, file=sys.stderr)
            return 2
        if not sessions:
            print("no session matched --only", file=sys.stderr)
            return 2
        if not args.dry_run:
            missing = missing_scripts(sessions)
            if missing:
                name, script = missing[0]
                print(f"session {name} needs {script}, which is not there - a "
                      "profile's entry/body/room scripts are read relative to the "
                      "profile's own folder", file=sys.stderr)
                return 2

        nav = profile["navigation"]
        kind = nav.get("kind", "ram")
        print(f"== {profile['game']} navigation sweep: {len(sessions)} sessions, "
              f"{seconds:g} s each, jobs={jobs}")
        if kind == "ram":
            print(f"   address ${nav['address'].upper()} ({nav.get('label', '')}) - "
                  f"{nav['source']}")
        else:
            print(f"   input selector ({nav.get('label', '')}) - {nav['source']}")
        for s in sessions:
            print(f"   {s['name']:<14} {s['kind']:<10} "
                  f"{'cheat=' + ','.join(s['cheats']) if s['cheats'] else 'no cheat':<20} "
                  f"{s['title']}")

        args.out.mkdir(parents=True, exist_ok=True)
        if jobs == 1:
            for s in sessions:
                r = run_session(s, args.rom.resolve(), args.dry_run)
                results.append(r)
                print(f"-- {r['name']}: {r['status']}"
                      f"{'' if args.dry_run else ' in ' + str(r.get('wallSeconds')) + ' s'}")
                if args.dry_run:
                    print("   " + " ".join(f'"{a}"' if " " in a else a
                                           for a in r["command"]))
        else:
            with concurrent.futures.ThreadPoolExecutor(max_workers=jobs) as pool:
                futures = {pool.submit(run_session, s, args.rom.resolve(), args.dry_run): s
                           for s in sessions}
                for fut in concurrent.futures.as_completed(futures):
                    r = fut.result()
                    results.append(r)
                    print(f"-- {r['name']}: {r['status']} in {r.get('wallSeconds')} s")
            results.sort(key=lambda r: [s["name"] for s in sessions].index(r["name"]))

    # --- ADR-0239 s4: did each session go somewhere? --------------------------
    scored = {"counted": [], "didNotWarp": [], "unionHires": []}
    totals = None
    after_hires = list(baseline_hires)
    if not args.dry_run:
        scored = score_sessions(results, baseline_hires)
        after_hires = list(baseline_hires) + scored["unionHires"]
        totals = totals_document(baseline_hires, after_hires,
                                 args.rom.resolve() if args.rom_chr else None,
                                 reference)
        print("\n== sessions (ADR-0239 s4: a session must prove it went somewhere)")
        print(f"   {'name':<16}{'status':<14}{'tiles':>6}{'keys':>7}"
              f"{'new':>6}{'unq':>6}  RAM check")
        for r in results:
            check = r.get("ramCheck")
            line = (f"${check['address']}={check['actual'] or '--'} "
                    f"{'ok' if check['ok'] else 'FAIL: ' + check['why']}"
                    if check else "")
            print(f"   {r['name']:<16}{r['status']:<14}"
                  f"{r.get('tiles', 0):>6}"
                  f"{'' if r.get('keys') is None else r['keys']:>7}"
                  f"{'' if r.get('new') is None else r['new']:>6}"
                  f"{'' if r.get('unique') is None else r['unique']:>6}  {line}")
        # One line, always true, because the difference between `new` and §4
        # read as tile-data strings is invisible in this table and decides four
        # of the five games in the F14.16 sweep.
        print("   new = drawn keys, (tileData, palette) - the cell identity "
              "ADR-0194 s2 names, the\n   background placeholders excluded - that "
              "the baseline packs do not hold; it is the gate\n   (`new == 0` = "
              "did-not-warp). unique = the same keys that no other session and\n"
              "   no baseline holds: reported, never a gate, because two tracks "
              "that share a tileset\n   are both legitimate warps with unique == 0."
              + ("" if baseline_hires else "  No --baseline given,\n   so every "
                 "session's whole pack counts as new.") + "\n   `tiles` counts "
              "distinct tile data over every rule, which on a CHR ROM game is\n"
              "   the same set for every session (the builder writes a "
              "`defaultTile` placeholder\n   per CHR index) - see newTiles/seen "
              "in --summary.")
        for r in results:
            if (r.get("ramCheck") or {}).get("caveat"):
                print(f"   ! {r['name']}: {r['ramCheck']['caveat']}")
        if scored["didNotWarp"]:
            # §4's "counts in no total" is about the count; §5's union is "every
            # sweep session", and on this rule a did-not-warp session holds
            # nothing the baseline does not, so it cannot inflate the union.
            print("   did-not-warp (not counted; nothing the baseline lacks, so "
                  "the union is the same with or without it): "
                  + ", ".join(scored["didNotWarp"]))

        # --- ADR-0239 s5: the union, against two denominators ----------------
        before, after = totals["before"], totals["after"]
        print(f"\n== coverage ({ADR_0239} s5): drawn keys and distinct tile data")
        for label, row in (("before", before), ("after", after)):
            print(f"   {label:<8}{row['keys']:>6} keys {row['tileData']:>6} tile data"
                  f"   romChr {_rom_chr_line(row['romChr'], row['romChrSeen'])}")
        print(f"   gained  {after['keys'] - before['keys']:>+6} keys "
              f"{after['tileData'] - before['tileData']:>+6} tile data "
              + (f"over {len(baseline_hires)} baseline pack(s)" if baseline_hires
                 else "(no --baseline given: before is empty)"))
        # Measured 2026-09-26 (Castlevania, CHR RAM): the placeholders are not
        # a small constant - they are 2581 distinct PRG-scan patterns, the same
        # in every pack of the ROM - so an every-rule column is that scan plus
        # a rounding error and hides the warps. The written rows are the ones
        # §5.1 asks for; the every-rule counts stay beside them.
        print(f"   the union columns are the rules a run *wrote*; over every "
              f"rule they read\n   {before['keysAll']} -> {after['keysAll']} keys "
              f"and {before['tileDataAll']} -> {after['tileDataAll']} tile data, "
              "which on a CHR RAM\n   game is the builder's PRG scan plus a "
              "rounding error. Raised as a defect by the\n   F14.16 coordinator "
              "2026-09-26; see nav_sweep_metrics.totals_document.")

    # --- coverage against a reference pack (ADR-0184's own metric) -----------
    coverage = None
    if reference and not args.dry_run:
        ref = reference
        ref_keys = tile_data_keys(ref)
        union_keys = set()
        per_session = []
        for r in results:
            if not r.get("hires"):
                continue
            # Drawn rows: a pattern the record never painted is not a pattern
            # the recording holds, and the placeholder scan paints thousands of
            # bytes nobody drew (nav_sweep_metrics.totals_document).
            keys = pack_seen_tile_data([Path(r["hires"])])
            union_keys |= keys
            hit = len(keys & ref_keys)
            per_session.append({
                "name": r["name"], "tiles": len(keys), "ofReference": hit,
                "pct": round(100.0 * hit / len(ref_keys), 1) if ref_keys else 0.0,
            })
        base_keys = pack_seen_tile_data(baseline_hires)
        sweep_hit = len(union_keys & ref_keys)
        coverage = {
            "reference": str(ref),
            "referenceTiles": len(ref_keys),
            "perSession": per_session,
            "sweepUnionTiles": len(union_keys),
            "sweepOfReference": sweep_hit,
            "sweepPct": round(100.0 * sweep_hit / len(ref_keys), 1) if ref_keys else 0.0,
        }
        if base_keys:
            both = len((union_keys | base_keys) & ref_keys)
            coverage["baselineOfReference"] = len(base_keys & ref_keys)
            coverage["baselinePct"] = round(100.0 * len(base_keys & ref_keys) / len(ref_keys), 1)
            coverage["unionWithBaselineOfReference"] = both
            coverage["unionWithBaselinePct"] = round(100.0 * both / len(ref_keys), 1)
            coverage["gainedOverBaseline"] = both - len(base_keys & ref_keys)

        print(f"\n== coverage against {ref} ({len(ref_keys)} distinct tile-data strings)")
        for p in per_session:
            print(f"   {p['name']:<14} {p['tiles']:>5} tiles  "
                  f"{p['ofReference']:>5} of reference  {p['pct']:>5.1f}%")
        print(f"   {'SWEEP UNION':<14} {len(union_keys):>5} tiles  "
              f"{sweep_hit:>5} of reference  {coverage['sweepPct']:>5.1f}%")
        if base_keys:
            print(f"   {'baseline':<14} {len(base_keys):>5} tiles  "
                  f"{coverage['baselineOfReference']:>5} of reference  "
                  f"{coverage['baselinePct']:>5.1f}%")
            print(f"   {'UNION + base':<14} {'':>5}        "
                  f"{coverage['unionWithBaselineOfReference']:>5} of reference  "
                  f"{coverage['unionWithBaselinePct']:>5.1f}%  "
                  f"(+{coverage['gainedOverBaseline']} tiles the baseline never held)")

    # --- notes[] (ADR-0183 s3, ADR-0184: the code travels with the art) ------
    # A rescore wrote no art and ran no session, so there is nothing to hand a
    # kit generator: the packs it read are the sweep's, and their notes are in
    # that sweep's own sweep.json.
    if args.rescore:
        doc = summary_document(profile.get("game", args.out.name),
                               str(args.rom or ""), seconds, results, totals)
        if args.summary:
            args.summary.parent.mkdir(parents=True, exist_ok=True)
            args.summary.write_text(json.dumps(doc, indent=2) + "\n")
            print(f"wrote {args.summary}")
        elif args.out.is_dir():
            dest = args.out / "rescore.json"
            dest.write_text(json.dumps(doc, indent=2) + "\n")
            print(f"wrote {dest}")
        return 0

    if kind == "ram":
        notes = [
            f"{profile['game']} navigation sweep, {ADR}: RAM address "
            f"${nav['address'].upper()} ({nav.get('label', '')}), values swept "
            + ", ".join(swept) + ". " + nav["source"],
        ]
    else:
        notes = [
            f"{profile['game']} navigation sweep, {ADR}: input selector "
            f"({nav.get('label', '')}), places swept " + ", ".join(picked)
            + ". No cheat: the profile's own entry scripts select the place "
            "(ADR-0239 s1, first rung). " + nav["source"],
        ]
    notes += [f"{r['name']}: {r['note']}" for r in results]
    print("\n== notes[] (ADR-0183 s3 - hand these to the kit generators verbatim)")
    for n in notes:
        print(f"   - {n}")

    summary = {
        "adr": ADR,
        "game": profile["game"],
        "profile": str(args.profile),
        "rom": str(args.rom),
        "seconds": seconds,
        "jobs": jobs,
        "navigation": nav,
        "cheatsSwept": swept,
        "cheatsPinned": sorted({c for r in results for c in r["cheats"]}),
        "baseline": [str(h) for h in baseline_hires],
        "sessions": results,
        # The after-side of the union is Paths because that is what a caller
        # wants; json needs the strings.
        "scored": dict(scored, unionHires=[str(h) for h in scored["unionHires"]]),
        "coverage": coverage,
        "totals": totals,
        "notes": notes,
    }
    (args.out / "sweep.json").write_text(json.dumps(summary, indent=2) + "\n")
    print(f"\nwrote {args.out / 'sweep.json'}")

    if args.summary:
        doc = summary_document(profile["game"], args.rom, seconds, results, totals)
        args.summary.parent.mkdir(parents=True, exist_ok=True)
        args.summary.write_text(json.dumps(doc, indent=2) + "\n")
        print(f"wrote {args.summary}")

    failed = [r for r in results if r["status"] == "failed"]
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
