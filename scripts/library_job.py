"""Plan and report for the unattended per-ROM recording job (F12.10).

`scripts/record_library.sh` is the job; this module is the part of it that has
to be exact, and therefore the part that is tested. It answers two questions:

* **`plan <roms-dir>`** — for every ROM in a folder, which driver records it,
  and why. Prints JSON on stdout; the shell reads it and does the recording.
* **`starts <stage-dir>`** — how each route gets the state it starts from
  (a mint, a replayed chain, a probe's copy, power-on) or why it cannot, as
  steps the shell runs; `prune` then drops every route left without one.
* **`report <out-dir>`** — after the job, one row per ROM: driver used,
  retained frames, `seen` %, figures / scenery / maps, the kit's `--verify`
  result, and the ADR-0182 mechanism list where the route set declares one;
  then, per route-driven ROM, one row per route with its own evidence.

Nothing here records or builds anything. It resolves, it reads what the job
left on disk, and it writes Markdown — so the whole of it runs in a temp dir
with no emulator and no ROM, which is what `scripts/test_library_job.py` does.

## Two hashes, and why mixing them breaks the job silently

A ROM has two identities here and they are not interchangeable:

* the **No-Intro SHA1** (ADR-0003) — the SHA1 of exactly the PRG+CHR the iNES
  header declares. This is what a pack's `<supportedRom>` carries and what a
  route set is matched on.
* the **whole-file SHA1** — what a `.bk2` movie's `Header.txt` names. This is
  documented in `scripts/fm2_to_bk2.py`'s `rom_hashes`: *"a bk2 `SHA1` names
  the file"*, and its docstring warns that a checker hashing the wrong one
  "would reject every correct ROM it was ever given".

Comparing a bk2 header against a No-Intro hash never raises: it just never
matches, and every movie-driven ROM quietly falls through to a weaker driver.
So the two are computed separately, named separately, and each comparison uses
the one its source actually carries.

## Driver precedence (PRD F12.10)

1. `routes` — a `scripts/stages/<game>/` set whose `stage-set.json` lists this
   ROM's No-Intro SHA1, and which holds at least one recordable stage.
2. `movie`  — a `.bk2` beside the ROM or under `<stages>/<game>/movies/` whose
   header SHA1 is this ROM's whole-file SHA1.
3. `entry`  — a matched set with an entry script but no recordable stage: one
   power-on run.
4. `static` — nothing matched. No recording is possible; a CHR ROM game gets
   the static kit F12.9 projects over its own CHR (ADR-0219), and a CHR RAM game
   has nothing to fall back on at all.

The resolved driver is recorded per ROM whether or not it succeeds, because the
report's job is to say what the pipeline did, not what it hoped to do.
"""

import argparse
import hashlib
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

# Deliberately the builder's own implementation rather than a second copy: the
# hash this job matches a route set on is the hash `mep_build` writes into
# `<supportedRom>`, and two implementations of that would eventually disagree.
from mep_build import _no_intro_sha1 as no_intro_sha1  # noqa: E402

ROM_EXTS = (".nes", ".gb", ".gbc", ".sms", ".gg", ".sg")
SET_MANIFEST = "stage-set.json"

# Drivers, in the precedence order the PRD row fixes.
ROUTES, MOVIE, ENTRY, STATIC = "routes", "movie", "entry", "static"


class LibraryError(Exception):
    """The job cannot be planned at all (a missing folder, an unreadable set).

    A *ROM* that resolves to nothing is never this: it is a row with driver
    `static` and a reason. Only the job's own inputs raise.
    """


# --- identity ---------------------------------------------------------------


def whole_file_sha1(path):
    """The SHA1 a `.bk2` header names — the file, header and all."""
    return hashlib.sha1(Path(path).read_bytes()).hexdigest().upper()  # noqa: S324


def chr_kind(rom):
    """`"CHR ROM"` or `"CHR RAM"` for an iNES file, else `""`.

    Byte 5 is the CHR bank count, with NES 2.0's high nibble of byte 9 when the
    header declares itself NES 2.0. Zero banks means the game generates its
    pattern data at run time, which is what decides whether F12.9 could ever
    help a ROM that matched no route.
    """
    data = Path(rom).read_bytes()
    if len(data) < 16 or data[:4] != b"NES\x1a":
        return ""
    units = data[5]
    if (data[7] & 0x0C) == 0x08 and (data[9] >> 4) != 0x0F:
        units |= (data[9] >> 4) << 8
    return "CHR ROM" if units else "CHR RAM"


def find_roms(roms_dir):
    """Every ROM file directly in `roms_dir`, sorted by name.

    One level only, on purpose. A library folder holds a sibling directory per
    game (extracted archives, save files, a `.mss` or two), and descending into
    those turns a three-ROM bounded input into a forty-ROM job nobody asked for.
    """
    root = Path(roms_dir)
    if not root.is_dir():
        raise LibraryError(f"{roms_dir}: not a directory")
    return sorted((p for p in root.iterdir()
                   if p.is_file() and p.suffix.lower() in ROM_EXTS),
                  key=lambda p: p.name.lower())


# --- the route sets ---------------------------------------------------------


def recordable_stages(stage_dir):
    """The `<stage>.txt` files `record_stages.sh` would actually record.

    Mirrors its filter exactly — `*.chain.txt` is a state-to-state transition
    for `replay_chain.sh` and `mint-*.txt` mints a save state; neither is a
    stage. If this list and that filter ever disagree, the plan promises a
    recording the job does not make.
    """
    out = []
    for p in sorted(Path(stage_dir).glob("*.txt")):
        if p.name.endswith(".chain.txt") or p.name.startswith("mint-"):
            continue
        out.append(p)
    return out


def entry_scripts(stage_dir):
    """The `mint-*.txt` scripts, which play from power-on."""
    return sorted(Path(stage_dir).glob("mint-*.txt"))


# headless_record resolves `Ns` steps and its <seconds> argument at this rate
# (ADR-0157 section 1; a mint runs without the `pal` flag).
NTSC_FPS = 60.0988


def script_frames(path):
    """A headless_record input script's length in frames.

    Same arithmetic as HeadlessInputScript::Parse and `replay_chain.sh`: `Nf`
    is N frames, `Ns` is round(N * fps); `#` comments and blank lines are
    skipped, and only the first token (the duration) of a line counts, so a
    `<port1>|<port2>` line is one step. Raises ValueError on a bare number.
    """
    total = 0
    for line in Path(path).read_text(encoding="utf-8").splitlines():
        tok = line.split()[0] if line.split() else ""
        if not tok or tok.startswith("#"):
            continue
        unit, n = tok[-1], tok[:-1]
        if unit == "f":
            total += int(n)
        elif unit == "s":
            total += int(float(n) * NTSC_FPS + 0.5)
        else:
            raise ValueError(f"{path}: bad duration {tok!r} - write <count>f or <count>s")
    return total


def mint_seconds_for_frames(frames):
    """The whole seconds a mint of `frames` frames runs for (#465).

    headless_record writes `save-state=` only when the run reaches its frame
    target, round(seconds * fps), so a mint run for the batch's <seconds>
    saved every state at frame 3607. This is the smallest whole number of
    seconds whose target covers the script - the duration the hand-mint
    procedures in scripts/stages/ use (Castlevania 320 f -> 6 s, Punch-Out!!
    1990 f -> 34 s), so the job's state is the one the routes were authored on.
    """
    s = max(1, int(frames / NTSC_FPS))
    while int(s * NTSC_FPS + 0.5) < frames:  # std::round, for s > 0
        s += 1
    return s


CHAIN_SUFFIX = ".chain.txt"
PROBE_SUFFIX = "-probe"
NAVIGATION = "navigation.json"
POWER_ON = "power-on"


def _chains(stage_dir):
    """`{target: [(source, path)]}` for every `<a>-to-<b>.chain.txt`.

    A name holding `-to-` more than once is split at the first one whose two
    halves are both non-empty; no versioned chain is ambiguous, and a chain that
    cannot be split is simply not a transition this job knows how to replay.
    """
    out = {}
    for p in sorted(Path(stage_dir).glob("*" + CHAIN_SUFFIX)):
        name = p.name[:-len(CHAIN_SUFFIX)]
        a, sep, b = name.partition("-to-")
        if sep and a and b:
            out.setdefault(b, []).append((a, p))
    return out


def _room_states(stage_dir):
    """The states `navigation.json`'s `rooms[]` say a room is entered from.

    ADR-0184 (amended 2026-09-14) and that file's own comment: a room the level
    selector cannot reach "is its own session, entered from the save state that
    is already in its room". So `stage1-boss` starts at the wall, not at the
    stage-1 entry its name happens to share a prefix with (issue #408).
    """
    doc = _read_json(Path(stage_dir) / NAVIGATION)
    rooms = doc.get("rooms") if isinstance(doc, dict) else None
    out = set()
    for room in rooms if isinstance(rooms, list) else []:
        state = room.get("state") if isinstance(room, dict) else None
        if isinstance(state, str) and state.endswith(".mss"):
            out.add(state[:-len(".mss")])
    return out


def start_plan(stage_dir):
    """How each recordable route gets the state it starts from (#407, #408).

    A route `<stage>.txt` plays *from* `<stage>.mss` (`scripts/stages/README.md`),
    and a checkout versions no state, so the job must produce each one or not
    record the route. The sources, in the README's own terms:

    * **its own state** — a state a `.chain.txt` produces, or one
      `navigation.json` names as a room's entry. Only an exact
      `mint-<stage>.txt`, or replaying the chain from a state this job can
      itself produce, yields it. A mint that merely shares a prefix does not:
      that is how `stage1-boss` came to record the stage-1 entry (#408).
    * **a probe** — `<stage>-probe` starts from `<stage>`'s state, copied.
    * **a mint** — the longest `mint-*.txt` whose name prefixes the route
      (`mint-stage1.txt` -> `stage1-run`, `stage1-long`, `stage1-probe`).
    * **power-on** — only in a set that ships no mint at all, whose routes boot
      the game themselves (Metroid's `stage1-run`). In a set that mints its
      states, a route nothing produces is **skipped and reported**: run from
      power-on it records the attract demo and reads like a recording (#407).

    Returns `{"steps": [...], "routes": {route: {...}}}`. A step is
    `{"op": "mint", "script", "state", "seconds"}` (the run's duration, from
    `mint_seconds_for_frames`; "" when the script cannot be read), `{"op": "chain", "script", "from",
    "state"}` or `{"op": "copy", "from", "state"}`, in an order where every
    `from` precedes its use. A route entry holds `start` (`mint <file>`, `chain
    <file>`, `copy of <state>`, `power-on`, or `None` when skipped) and
    `reason`.
    """
    stage_dir = Path(stage_dir)
    routes = [p.stem for p in recordable_stages(stage_dir)]
    mints = {p.stem[len("mint-"):]: p for p in entry_scripts(stage_dir)}
    chains = _chains(stage_dir)
    own = set(chains) | _room_states(stage_dir)
    known = set(routes) | own | {a for srcs in chains.values() for a, _ in srcs}
    memo, visiting = {}, set()

    def resolve(name):
        """`(producer, reason)`; producer is None when nothing yields it."""
        if name in memo:
            return memo[name]
        if name in visiting:
            return None, f"{name}.mss depends on itself through its chains"
        visiting.add(name)
        res = _resolve(name)
        visiting.discard(name)
        memo[name] = res
        return res

    def _resolve(name):
        if name in own:
            if name in mints:
                return ("mint", mints[name]), ""
            why = []
            for src, path in chains.get(name, []):
                prod, reason = resolve(src)
                if prod:
                    return ("chain", path, src), ""
                why.append(f"{path.name} starts from {src}.mss; {reason}")
            if not why:
                why.append("navigation.json enters this room from its own "
                           "state and no mint or chain produces it")
            return None, "; ".join(why)
        if name.endswith(PROBE_SUFFIX) and name[:-len(PROBE_SUFFIX)] in known:
            base = name[:-len(PROBE_SUFFIX)]
            prod, reason = resolve(base)
            return (("copy", base), "") if prod else (None, f"a probe starts from {base}.mss; {reason}")
        best = max((m for m in mints if name == m or name.startswith(m + "-")),
                   key=len, default=None)
        if best is not None:
            return ("mint", mints[best]), ""
        return None, (f"no mint-*.txt or .chain.txt produces {name}.mss")

    # A set that ships no mint, no chain and no room state has no way to produce
    # a state at all: its routes are their own entry scripts.
    self_booting = not mints and not own
    out_routes = {}
    for r in routes:
        prod, reason = resolve(r)
        if prod is None and self_booting:
            out_routes[r] = {"start": POWER_ON, "reason": (
                "the set ships no mint or chain, so its routes boot the game themselves")}
        else:
            out_routes[r] = {"start": None, "reason": reason}

    # Steps: one run per mint (for the first state it yields), a copy for every
    # other state that mint yields, then chains and probe copies once their
    # source exists.
    steps, done, minted = [], set(), {}

    def emit(name):
        if name in done:
            return
        prod = resolve(name)[0]
        if prod[0] == "mint" and prod[1] in minted:
            steps.append({"op": "copy", "from": minted[prod[1]], "state": name})
        elif prod[0] == "mint":
            minted[prod[1]] = name
            try:
                secs = mint_seconds_for_frames(script_frames(prod[1]))
            except (OSError, ValueError):
                secs = ""  # the shell refuses the mint rather than guess (#465)
            steps.append({"op": "mint", "script": str(prod[1]), "state": name,
                          "seconds": secs})
        elif prod[0] == "chain":
            emit(prod[2])
            steps.append({"op": "chain", "script": str(prod[1]), "from": prod[2], "state": name})
        else:
            emit(prod[1])
            steps.append({"op": "copy", "from": prod[1], "state": name})
        done.add(name)

    for r in routes:
        prod = resolve(r)[0]
        if prod is None:
            continue
        emit(r)
        if prod[0] == "copy":
            out_routes[r]["start"] = f"copy of {prod[1]}.mss"
        else:
            out_routes[r]["start"] = f"{prod[0]} {prod[1].name}"
    return {"steps": steps, "routes": out_routes}


def load_stage_sets(stages_root):
    """Every `scripts/stages/<game>/` that declares which ROM it was authored
    against, keyed by No-Intro SHA1.

    A set with no `stage-set.json` is not an error and not a match: we will not
    guess that a folder called `zelda/` belongs to a given dump. Guessing is how
    a pack ends up recorded against the wrong ROM (issue #314).
    """
    root = Path(stages_root)
    if not root.is_dir():
        raise LibraryError(f"{stages_root}: not a directory")
    sets, undeclared = {}, []
    for d in sorted(p for p in root.iterdir() if p.is_dir()):
        manifest = d / SET_MANIFEST
        if not manifest.is_file():
            undeclared.append(d.name)
            continue
        try:
            doc = json.loads(manifest.read_text(encoding="utf-8"))
        except (OSError, ValueError) as exc:
            raise LibraryError(f"{manifest}: {exc}") from exc
        hashes = ((doc.get("rom") or {}).get("noIntroSha1")) or []
        if isinstance(hashes, str):
            hashes = [hashes]
        if not hashes:
            raise LibraryError(
                f"{manifest}: declares no rom.noIntroSha1, so it can never "
                "match a ROM. Remove the file or fill it in.")
        entry = {
            "name": d.name,
            "dir": d,
            "game": doc.get("game") or d.name,
            "mechanisms": doc.get("mechanisms") or [],
            "note": doc.get("note") or "",
        }
        for h in hashes:
            sets[str(h).strip().upper()] = entry
    return sets, undeclared


# --- the movies -------------------------------------------------------------


def bk2_rom_sha1(path):
    """The whole-file SHA1 a `.bk2` header names, or `None`.

    A zip we cannot read, or one with no `SHA1` line, is not a match and not an
    error — the folder beside a ROM is the user's, and it may hold anything.
    """
    import zipfile
    try:
        with zipfile.ZipFile(path) as z:
            name = next((n for n in z.namelist() if n.lower() == "header.txt"), None)
            if name is None:
                return None
            for line in z.read(name).decode("utf-8", "replace").splitlines():
                parts = line.split()
                if len(parts) == 2 and parts[0].upper() == "SHA1":
                    return parts[1].strip().upper()
    except (OSError, zipfile.BadZipFile, KeyError):
        return None
    return None


def find_movie(rom, whole_sha1, stage_set):
    """A `.bk2` for this exact dump, beside the ROM or in the set's `movies/`."""
    places = [Path(rom).parent]
    if stage_set:
        places.append(Path(stage_set["dir"]) / "movies")
    for place in places:
        if not place.is_dir():
            continue
        for cand in sorted(place.glob("*.bk2")):
            if bk2_rom_sha1(cand) == whole_sha1:
                return cand
    return None


# --- the plan ---------------------------------------------------------------


def plan_rom(rom, sets, seconds):
    """One ROM's row of the plan: the driver, why, and what the shell runs."""
    rom = Path(rom)
    sha1 = no_intro_sha1(rom)
    whole = whole_file_sha1(rom)
    stage_set = sets.get(sha1)
    row = {
        "rom": str(rom),
        "name": rom.stem,
        "noIntroSha1": sha1,
        "wholeFileSha1": whole,
        "chr": chr_kind(rom),
        "seconds": seconds,
        "driver": STATIC,
        "reason": "",
        "stages": None,
        "stageCount": 0,
        "movie": None,
        "entry": None,
        "mechanisms": [],
        "game": rom.stem,
    }

    movie = find_movie(rom, whole, stage_set)

    if stage_set:
        row["game"] = stage_set["game"]
        row["mechanisms"] = stage_set["mechanisms"]
        starts = start_plan(stage_set["dir"])["routes"]
        stages = [r for r, v in starts.items() if v["start"]]
        skipped = sorted(set(starts) - set(stages))
        if stages:
            row.update(driver=ROUTES, stages=str(stage_set["dir"]),
                       stageCount=len(stages), routesSkipped=skipped)
            row["reason"] = (
                f"route set {stage_set['name']}/ declares this No-Intro SHA1; "
                f"{len(stages)} recordable stage(s)")
            if skipped:
                row["reason"] += (f"; {len(skipped)} route(s) skipped, their start "
                                  "state cannot be produced from a checkout")
            if movie:
                # Worth saying out loud: precedence was exercised, not assumed.
                row["reason"] += f"; a movie also matched ({movie.name}) and (a) wins"
                row["movie"] = str(movie)
            return row
        entries = entry_scripts(stage_set["dir"])
        if movie:
            row.update(driver=MOVIE, movie=str(movie))
            row["reason"] = f"no recordable stage in {stage_set['name']}/; movie {movie.name} matches this dump"
            return row
        if entries:
            row.update(driver=ENTRY, entry=str(entries[0]))
            row["reason"] = (
                f"route set {stage_set['name']}/ has an entry script but no "
                "recordable stage; one power-on run")
            return row
        row["reason"] = f"route set {stage_set['name']}/ holds no script to play"
        return row

    if movie:
        row.update(driver=MOVIE, movie=str(movie))
        row["reason"] = f"no route set declares this dump; movie {movie.name} matches it"
        return row

    row["reason"] = ("no route set declares this No-Intro SHA1 and no .bk2 "
                     "matches this dump")
    return row


def build_plan(roms_dir, stages_root, seconds):
    sets, undeclared = load_stage_sets(stages_root)
    roms = find_roms(roms_dir)
    return {
        "romsDir": str(Path(roms_dir).resolve()),
        "stagesDir": str(Path(stages_root).resolve()),
        "seconds": seconds,
        "undeclaredStageSets": undeclared,
        "roms": [plan_rom(r, sets, seconds) for r in roms],
    }


# --- reading back what the job left ----------------------------------------


def _read_json(path):
    try:
        return json.loads(Path(path).read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None


def retained_frames(rom_out):
    """Retained OAM frames, summed over the run folders under one ROM's output.

    Read out of the recorder's own log line — `poses: N silhouettes from M
    retained OAM frames` — because that is the number the recorder itself
    reports, rather than one we recompute and could get differently.
    """
    total = 0
    for log in sorted(Path(rom_out).rglob("mesen-home/mesen.log")):
        try:
            text = log.read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue
        for line in text.splitlines():
            if "retained OAM frames" in line:
                words = line.split()
                for i, w in enumerate(words):
                    if w == "retained" and i >= 1 and words[i - 1].isdigit():
                        total += int(words[i - 1])
                        break
    return total


def kit_counts(kit_dir):
    """What an assembled kit reports about itself, read off its own fragments.

    Never recomputed: a report that disagrees with the kit it describes is
    worse than no report. Each `kit-part-<name>.json` carries `files[]` (one
    surface each, with `cells` and a `seen` flag) and a `verify` block.

    Returns a dict with `figures` / `scenery` / `maps` / `chrPages` surface
    counts, `seen` — **the share of cells that live on a surface the run
    actually saw**, which is a stricter reading than counting surfaces, since
    one unseen CHR page hides 256 cells and one unseen sprite sheet hides two
    — and `verify`, the summed error count across the parts that ran.

    A missing value stays `None` and prints as `-`. A kit the job never
    produced must not read as a kit with zero of everything.
    """
    kit = Path(kit_dir)
    parts = {"sprites": "figures", "background": "scenery",
             "map": "maps", "chr": "chrPages"}
    out = {v: None for v in parts.values()}
    out["seen"] = None
    out["verify"] = None
    seen_cells = total_cells = 0
    errors = 0
    ran_any = False
    for frag in sorted(kit.glob("kit-part-*.json")):
        doc = _read_json(frag)
        if not isinstance(doc, dict):
            continue
        key = parts.get(str(doc.get("part") or ""))
        files = doc.get("files")
        if not isinstance(files, list):
            continue
        if key:
            out[key] = len(files)
        for f in files:
            if not isinstance(f, dict):
                continue
            cells = f.get("cells")
            cells = cells if isinstance(cells, int) else 0
            total_cells += cells
            if f.get("seen"):
                seen_cells += cells
        verify = doc.get("verify")
        if isinstance(verify, dict) and verify.get("ran"):
            ran_any = True
            n = verify.get("errors")
            errors += n if isinstance(n, int) else 0
    if total_cells:
        out["seen"] = round(100.0 * seen_cells / total_cells, 1)
    if ran_any:
        out["verify"] = errors
    return out


def _cell(v):
    return "-" if v is None else str(v)


def _stages_cell(r):
    n = _cell(r.get("stageCount") or None)
    skipped = r.get("routesSkipped") or []
    return f"{n} (+{len(skipped)} skipped)" if skipped else n


def _route_sections(results):
    """One table per route-driven ROM: how each route started and what it kept.

    `seen %` cannot show a route that recorded the wrong thing (#409), so this
    is where a mis-started route is visible: its start, the stage its start
    state is in, its own retained frames, and any route whose recording is
    byte-identical to it.
    """
    out = []
    for r in results:
        routes = r.get("routes")
        if not isinstance(routes, list) or not routes:
            continue
        out += [f"## Routes: {r.get('name', '?')}", "",
                "| route | start | stage at start | retained frames | silhouettes | same recording as |",
                "|---|---|---|---|---|---|"]
        for x in routes:
            if not x.get("start"):
                continue
            out.append(
                f"| {x['route']} | {x['start']} | {_cell(x.get('stageAtStart'))} "
                f"| {_cell(x.get('retained'))} | {_cell(x.get('silhouettes'))} "
                f"| {', '.join(x.get('sameAs') or []) or '-'} |")
        skipped = [x for x in routes if not x.get("start")]
        if skipped:
            out += ["", f"Not recorded ({len(skipped)}) — no start state the job can produce, "
                    "so a run would start at power-on and record the attract demo:", ""]
            out += [f"- **{x['route']}** — {x.get('reason') or 'no reason recorded'}" for x in skipped]
        out.append("")
    return out


def render_report(results, title="Library recording job"):
    """The Markdown the job writes as `library-report.md`."""
    lines = [
        f"# {title}",
        "",
        "One row per ROM. Generated by `scripts/record_library.sh` (F12.10);",
        "every number is read off what the job left on disk, never recomputed.",
        "A ROM that could not be recorded is a **row**, not a missing row.",
        "",
        "| ROM | driver | status | stages | retained frames | seen % | figures | scenery | maps | kit --verify |",
        "|---|---|---|---|---|---|---|---|---|---|",
    ]
    for r in results:
        lines.append(
            f"| {r.get('name','?')} | `{r.get('driver','?')}` | {r.get('status','?')} "
            f"| {_stages_cell(r)} | {_cell(r.get('retained'))} "
            f"| {_cell(r.get('seen'))} | {_cell(r.get('figures'))} "
            f"| {_cell(r.get('scenery'))} | {_cell(r.get('maps'))} "
            f"| {_cell(r.get('verify'))} |")

    lines += [
        "",
        "`seen %` is the share of **cells** on a surface the run actually saw,",
        "not the share of surfaces: one unseen CHR page hides 256 cells and one",
        "unseen sprite sheet hides two. `maps` is always `-` — a panorama needs a",
        "per-stage grid dump of about 190 MB, which this job deliberately does not",
        "request (`scripts/artist_map.py` builds one on demand).",
        "",
    ]
    lines += _route_sections(results)
    lines += [
        "## Why each ROM got the driver it did",
        "",
    ]
    for r in results:
        lines.append(f"- **{r.get('name','?')}** — `{r.get('driver','?')}`: {r.get('reason','')}")
        lines.append(f"  - No-Intro SHA1 `{r.get('noIntroSha1','?')}`, {r.get('chr') or 'unknown CHR'}")
        if r.get("note"):
            lines.append(f"  - {r['note']}")

    with_mech = [r for r in results if r.get("mechanisms")]
    lines += ["", "## Mechanisms the route sets declare (ADR-0182)", ""]
    if with_mech:
        for r in with_mech:
            lines.append(f"- **{r['name']}**: " + ", ".join(r["mechanisms"]))
    else:
        lines.append("No route set in this run declares a mechanism list.")
    others = [r["name"] for r in results
              if r.get("driver") == ROUTES and not r.get("mechanisms")]
    if others:
        lines.append("")
        lines.append(
            "Recorded from routes but declaring no mechanism list: "
            + ", ".join(others)
            + ". ADR-0182 judges coverage by mechanisms, so an undeclared set "
              "is not evidence of coverage — it is a set nobody has described "
              "yet.")
    return "\n".join(lines) + "\n"


STARTS_FILE = "starts.json"


def prune_unstarted(stage_dir, starts_path):
    """Drop every route whose start state does not exist, and say why.

    Runs after the job has executed `start_plan`'s steps, so it catches both a
    route the plan could never start and one whose mint or chain failed at run
    time. `record_stages.sh` records a `<stage>.txt` with no `.mss` from
    power-on, which is the #407 failure, so the script itself is removed from
    the job's working copy — never from the repository. Returns the number of
    routes dropped.
    """
    stage_dir = Path(stage_dir)
    doc = _read_json(starts_path) or {}
    routes = doc.get("routes") or {}
    dropped = 0
    for name, info in routes.items():
        if info.get("start") == POWER_ON:
            continue
        if info.get("start") and (stage_dir / f"{name}.mss").is_file():
            continue
        if info.get("start"):
            info["reason"] = f"{info['start']} did not write {name}.mss (see mint.log)"
            info["start"] = None
        script = stage_dir / f"{name}.txt"
        if script.is_file():
            script.unlink()
            dropped += 1
    Path(starts_path).write_text(json.dumps(doc, indent=2), encoding="utf-8")
    return dropped


def _poses_line(log):
    """`(silhouettes, retained)` from the recorder's own `poses:` line."""
    try:
        text = Path(log).read_text(encoding="utf-8", errors="replace")
    except OSError:
        return None, None
    found = (None, None)
    for line in text.splitlines():
        words = line.split()
        if "poses:" in words and "retained" in words:
            i, j = words.index("poses:"), words.index("retained")
            if i + 1 < len(words) and words[i + 1].isdigit() and words[j - 1].isdigit():
                found = (int(words[i + 1]), int(words[j - 1]))
    return found


def _stage_probe(stage_dir):
    """`(address, {value: name})` from the set's `navigation.json`, or None.

    That file's `navigation.address` is the published RAM byte holding the
    current level (Contra `$0030`, ADR-0184 §5), which is the one reading that
    says which stage a state is in rather than what its name hopes.
    """
    nav = (_read_json(Path(stage_dir) / NAVIGATION) or {}).get("navigation")
    if not isinstance(nav, dict):
        return None
    try:
        addr = int(str(nav.get("address")), 16)
        names = {int(str(v["value"]), 16): str(v["name"]) for v in nav.get("values") or []}
    except (KeyError, TypeError, ValueError):
        return None
    return addr, names


def route_evidence(rom_out):
    """Per-route evidence that each route played the stage it is named for (#409).

    `seen %` is counted per surface and a CHR page is one surface, so on Contra
    nine routes recording the attract demo left it at 87.6 either way. What a
    recording already gives per route is read here instead: how it started
    (`starts.json`), the recorder's retained frames and silhouettes, the stage
    the start state is in by the set's RAM probe when it has one, and which
    routes recorded **byte-identical** tile sets (`hires.txt`) — nine different
    routes with one recording is the attract demo, not nine stages.
    """
    import hashlib as _h
    rom_out = Path(rom_out)
    routes = ((_read_json(rom_out / STARTS_FILE) or {}).get("routes")) or {}
    work = rom_out / "stages-src"
    probe = _stage_probe(work)
    rows, prints = [], {}
    for name in sorted(routes):
        info = routes[name]
        d = rom_out / "stages" / name
        sil, ret = _poses_line(d / "mesen-home" / "mesen.log")
        hires = sorted(d.glob("*/auto/textures/hires.txt"))
        fp = _h.sha1(hires[0].read_bytes()).hexdigest() if hires else None  # noqa: S324
        at_start = None
        state = work / f"{name}.mss"
        if probe and info.get("start") and info.get("start") != POWER_ON and state.is_file():
            try:
                import mss_ram
                v = mss_ram.ram(state)[probe[0]]
                at_start = f"{probe[1].get(v, '?')} (${probe[0]:04X}={v:02X})"
            except (OSError, ValueError, IndexError, KeyError):
                at_start = None
        rows.append({"route": name, "start": info.get("start"), "reason": info.get("reason") or "",
                     "retained": ret, "silhouettes": sil, "stageAtStart": at_start, "_fp": fp})
        if fp and info.get("start"):
            prints.setdefault(fp, []).append(name)
    for r in rows:
        fp = r.pop("_fp")
        r["sameAs"] = [n for n in prints.get(fp, []) if n != r["route"]] if fp else []
    return rows


def collect_row(plan_path, index, results_path, rom_out, status):
    """Fold one ROM's outcome into `results.json`, appending as the job goes.

    Written after each ROM rather than at the end: a job that dies on ROM six
    should still leave the five rows it earned, and a report of five honest
    rows beats a crash with none.
    """
    plan = _read_json(plan_path) or {}
    rows = plan.get("roms") or []
    if not 0 <= index < len(rows):
        return 2
    row = dict(rows[index])
    row["status"] = status
    row["retained"] = retained_frames(rom_out) or None
    row.update(kit_counts(Path(rom_out) / "kit"))
    if row.get("driver") == ROUTES:
        routes = route_evidence(rom_out)
        if routes:
            # What the job actually ran, which a failed mint can make less than
            # what the plan promised.
            row["routes"] = routes
            row["stageCount"] = sum(1 for x in routes if x["start"])
            row["routesSkipped"] = [x["route"] for x in routes if not x["start"]]
    existing = _read_json(results_path)
    existing = existing if isinstance(existing, list) else []
    existing.append(row)
    Path(results_path).write_text(json.dumps(existing, indent=2), encoding="utf-8")
    return 0


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    sub = ap.add_subparsers(dest="cmd", required=True)

    p = sub.add_parser("plan", help="resolve a driver per ROM; prints JSON")
    p.add_argument("roms_dir")
    p.add_argument("--stages", default=str(Path(__file__).resolve().parent / "stages"))
    p.add_argument("--seconds", type=int, default=60)

    m = sub.add_parser("starts", help="start-state steps, NUL-separated; routes to --json")
    m.add_argument("stage_dir")
    m.add_argument("--json", required=True, help="where to write the per-route starts")

    q = sub.add_parser("prune", help="drop routes whose start state does not exist")
    q.add_argument("stage_dir")
    q.add_argument("starts")

    c = sub.add_parser("collect", help="append one ROM's result row to results.json")
    c.add_argument("plan")
    c.add_argument("index", type=int)
    c.add_argument("results")
    c.add_argument("rom_out")
    c.add_argument("status")

    r = sub.add_parser("report", help="render library-report.md from results.json")
    r.add_argument("results", help="the job's results.json")
    r.add_argument("--out", default=None)
    r.add_argument("--title", default="Library recording job")

    args = ap.parse_args(argv)
    try:
        if args.cmd == "plan":
            print(json.dumps(build_plan(args.roms_dir, args.stages, args.seconds),
                             indent=2))
            return 0
        if args.cmd == "starts":
            sp = start_plan(args.stage_dir)
            Path(args.json).write_text(json.dumps({"routes": sp["routes"]}, indent=2),
                                       encoding="utf-8")
            for st in sp["steps"]:
                for k in ("op", "script", "from", "state", "seconds"):
                    sys.stdout.write(str(st.get(k, "")) + "\0")
            return 0
        if args.cmd == "prune":
            n = prune_unstarted(args.stage_dir, args.starts)
            print(f"{n} route(s) not recorded: no start state", file=sys.stderr)
            return 0
        if args.cmd == "collect":
            return collect_row(args.plan, args.index, args.results,
                               args.rom_out, args.status)
        results = _read_json(args.results)
        if not isinstance(results, list):
            print(f"error: {args.results} is not a list of result rows", file=sys.stderr)
            return 2
        text = render_report(results, args.title)
        if args.out:
            Path(args.out).write_text(text, encoding="utf-8")
        else:
            sys.stdout.write(text)
        return 0
    except LibraryError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
