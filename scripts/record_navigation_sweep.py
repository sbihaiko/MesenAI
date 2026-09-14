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

1. Reads the profile and builds a plan. Every cheat is validated against
   ADR-0184 s1 *here*, before a single process starts: the `AAAA:VV[:CC]` form
   only, and `AAAA < 0x0800`. `headless_record`'s own `parseRamCheat()` is the
   second gate; a plan that would be refused there is refused here first, so an
   artist never watches ten good sessions run and the eleventh die.
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
4. Optionally measures coverage against a reference pack (`--reference`), per
   session and for the union, using the same metric the ADR reports: the
   fraction of the reference pack's *distinct tile-data strings* that the
   recording also holds.

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

REPO_ROOT = Path(__file__).resolve().parent.parent
RECORDER = REPO_ROOT / "scripts" / "headless_record"
NTSC_FRAME_RATE = 60.0988  # HeadlessInputScript::NtscFrameRate

ADR = "ADR-0184 (amended 2026-09-14)"


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


# --- coverage ----------------------------------------------------------------
def tile_data_keys(hires: Path) -> set:
    """The distinct tile-data strings a pack definition holds.

    `artist_cover.py` owns this metric and this parser - the unit is
    `tileData`, not `(tileData, palette)`, because "the same tile under a
    stage's other palette is a different `hires.txt` key, so keys undercount
    figures the artist repaints per stage" (`artist_cover.py:8-10`), and it is
    the unit ADR-0184's amendment quotes. Its `parse()` is imported rather than
    reimplemented so the sweep's acceptance number cannot drift from the tool
    the ADR was measured with; Contra's reference pack has 3404 distinct
    tile-data strings there, and the amendment's 64.6% - 53.8% = 367 tiles is
    10.8% of exactly that.

    Run `artist_cover.py` itself for the full report (per-image rows, the
    sprite/background split, the `$30` gate); this is the headline line only.
    """
    if not hires.exists():
        return set()
    sys.path.insert(0, str(REPO_ROOT / "scripts"))
    import artist_cover  # noqa: E402  - same folder, stdlib only
    return {data for _, data, _, _ in artist_cover.parse(str(hires))}


def find_pack_hires(session_dir: Path) -> Path:
    """The bootstrap pack a session wrote.

    `MepPackManager::GetSiblingFolder` puts it beside the ROM, at
    `<rom stem>/auto/textures/hires.txt`.
    """
    for pattern in ("*/auto/textures/hires.txt", "*/auto/hires.txt"):
        hits = sorted(session_dir.glob(pattern))
        if hits:
            return hits[0]
    hits = sorted(session_dir.rglob("hires.txt"))
    return hits[0] if hits else session_dir / "hires.txt"


# --- the plan ----------------------------------------------------------------
def build_plan(profile: dict, prof_dir: Path, rom: Path, out: Path,
               states: Path, seconds: float, only: set) -> list:
    nav = profile["navigation"]
    addr = nav["address"].upper()
    defaults = profile.get("defaults", {})
    entry = prof_dir / defaults.get("entry", "")
    body = prof_dir / defaults.get("body", "")
    sessions = []

    for item in nav.get("values", []):
        name = item["name"]
        if only and name not in only:
            continue
        cheat = validate_ram_cheat(f"{addr}:{item['value'].upper()}")
        sdir = out / name
        sessions.append({
            "name": name,
            "kind": "navigation",
            "title": item.get("title", ""),
            "cheat": cheat,
            "dir": sdir,
            "entry": entry,
            "body": body,
            "state": None,
            "input": None,
            "seconds": seconds,
            "note": (
                f"navigation pass ({ADR} s2, third row): RAM cheat {cheat} pinned "
                f"for the whole run - ${addr} is {profile['game']}'s "
                f"{nav.get('label', 'navigation selector')}, value 0x{item['value'].upper()}"
                f"{' = ' + item['title'] if item.get('title') else ''}. "
                f"Source: {nav['source']} "
                f"Recorded {seconds:g} s from power-on with {entry.name} + "
                f"{body.name} repeated. The selector is pinned for the whole run, "
                "so the stage-clear transition is never recorded."
            ),
        })

    for room in profile.get("rooms", []):
        name = room["name"]
        if only and name not in only:
            continue
        state = (states / room["state"]) if states else None
        sessions.append({
            "name": name,
            "kind": "room",
            "title": room.get("title", ""),
            "cheat": None,
            "dir": out / name,
            "entry": None,
            "body": None,
            "state": state,
            "input": prof_dir / room["input"],
            "seconds": seconds,
            "note": (
                f"clean pass ({ADR} s2, first row): no cheat. ${addr} selects a "
                "level and not a room inside it, so this room is entered from the "
                f"save state {room['state']} minted by the F9.22 chain and played "
                f"by {room['input']}."
            ),
        })

    for s in sessions:
        s["prefix"] = s["dir"] / "rec"
    return sessions


def command_for(session: dict, rom_in_dir: Path, script_info) -> list:
    args = [str(RECORDER), str(rom_in_dir), f"{session['seconds']:g}",
            str(session["prefix"]), "bootstrap", "hdpack-off"]
    if session["kind"] == "navigation":
        args.append(f"input={script_info['path']}")
        args.append(f"cheat={session['cheat']}")
    else:
        args.append(f"input={session['input']}")
        if session["state"]:
            args.append(f"state={session['state']}")
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
        "seconds": session["seconds"],
        "dir": str(sdir),
        "command": cmd,
        "inputScript": script_info,
        "note": session["note"],
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
    return result


def main() -> int:
    ap = argparse.ArgumentParser(
        description="Record a whole navigation sweep as one command (ADR-0184).")
    ap.add_argument("--profile", type=Path, required=True,
                    help="game profile, e.g. scripts/stages/contra/navigation.json")
    ap.add_argument("--rom", type=Path, required=True)
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
                    help="an existing recording's hires.txt (or folder) to union "
                         "with the sweep for the second coverage number; repeatable")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    profile = json.loads(args.profile.read_text())
    prof_dir = args.profile.resolve().parent
    seconds = args.seconds if args.seconds is not None else \
        profile.get("defaults", {}).get("seconds", 300)
    only = set(x.strip() for x in args.only.split(",")) if args.only else set()
    # A dry run launches nothing, so there is nothing to parallelise: it goes
    # through the sequential branch, which is the one that prints each session's
    # full command. Left parallel, --dry-run printed "dry-run in None s" (there
    # is no wall clock to report) and printed no command at all - the one thing
    # it exists to show.
    jobs = 1 if args.dry_run else max(1, args.jobs)

    if not args.dry_run and not RECORDER.exists():
        print(f"missing {RECORDER} - run: make capture-tool", file=sys.stderr)
        return 2
    if not args.rom.exists():
        print(f"ROM not found: {args.rom}", file=sys.stderr)
        return 2

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

    nav = profile["navigation"]
    swept = [s["cheat"] for s in sessions if s["cheat"]]
    print(f"== {profile['game']} navigation sweep: {len(sessions)} sessions, "
          f"{seconds:g} s each, jobs={jobs}")
    print(f"   address ${nav['address'].upper()} ({nav.get('label', '')}) - "
          f"{nav['source']}")
    for s in sessions:
        print(f"   {s['name']:<14} {s['kind']:<10} "
              f"{'cheat=' + s['cheat'] if s['cheat'] else 'no cheat':<18} "
              f"{s['title']}")

    args.out.mkdir(parents=True, exist_ok=True)
    results = []
    if jobs == 1:
        for s in sessions:
            r = run_session(s, args.rom.resolve(), args.dry_run)
            results.append(r)
            print(f"-- {r['name']}: {r['status']}"
                  f"{'' if args.dry_run else ' in ' + str(r.get('wallSeconds')) + ' s'}")
            if args.dry_run:
                print("   " + " ".join(f'"{a}"' if " " in a else a for a in r["command"]))
    else:
        with concurrent.futures.ThreadPoolExecutor(max_workers=jobs) as pool:
            futures = {pool.submit(run_session, s, args.rom.resolve(), args.dry_run): s
                       for s in sessions}
            for fut in concurrent.futures.as_completed(futures):
                r = fut.result()
                results.append(r)
                print(f"-- {r['name']}: {r['status']} in {r.get('wallSeconds')} s")
        results.sort(key=lambda r: [s["name"] for s in sessions].index(r["name"]))

    # --- coverage ------------------------------------------------------------
    coverage = None
    if args.reference and not args.dry_run:
        ref = args.reference
        if ref.is_dir():
            ref = ref / "hires.txt"
        ref_keys = tile_data_keys(ref)
        union_keys = set()
        per_session = []
        for r in results:
            if not r.get("hires"):
                continue
            keys = tile_data_keys(Path(r["hires"]))
            union_keys |= keys
            hit = len(keys & ref_keys)
            per_session.append({
                "name": r["name"], "tiles": len(keys), "ofReference": hit,
                "pct": round(100.0 * hit / len(ref_keys), 1) if ref_keys else 0.0,
            })
        base_keys = set()
        for b in args.baseline:
            p = Path(b)
            if p.is_dir():
                p = p / "hires.txt"
            base_keys |= tile_data_keys(p)
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
    notes = [
        f"{profile['game']} navigation sweep, {ADR}: RAM address "
        f"${nav['address'].upper()} ({nav.get('label', '')}), values swept "
        + ", ".join(v for v in swept) + ". " + nav["source"],
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
        "sessions": results,
        "coverage": coverage,
        "notes": notes,
    }
    (args.out / "sweep.json").write_text(json.dumps(summary, indent=2) + "\n")
    print(f"\nwrote {args.out / 'sweep.json'}")

    failed = [r for r in results if r["status"] == "failed"]
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
