"""Plan and report for the unattended per-ROM recording job (F12.10).

`scripts/record_library.sh` is the job; this module is the part of it that has
to be exact, and therefore the part that is tested. It answers two questions:

* **`plan <roms-dir>`** — for every ROM in a folder, which driver records it,
  and why. Prints JSON on stdout; the shell reads it and does the recording.
* **`report <out-dir>`** — after the job, one row per ROM: driver used,
  retained frames, `seen` %, figures / scenery / maps, the kit's `--verify`
  result, and the ADR-0182 mechanism list where the route set declares one.

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
4. `static` — nothing matched. No recording is possible; a CHR ROM game is
   F12.9's job, and a CHR RAM game has nothing to fall back on at all.

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


def mint_plan(stage_dir):
    """Which `mint-*.txt` mints the state for which recordable stage.

    `.mss` files are never versioned (`.gitignore`: a CHR RAM state carries the
    game's graphics), so a checkout has the scripts and none of the states they
    start from. Recording without minting first runs every stage from power-on
    and keeps a title screen — measured, not supposed: the first F12.10 run kept
    **1 retained frame per stage** on Mega Man 3 that way.

    The naming rule is the one `scripts/stages/README.md` documents by example:
    a mint's `save-state=` is named for the *stage that will use it*
    (`mint-stage1.txt` -> `stage1-run.mss`), and a probe wants the same state
    copied beside it as `<stage>-probe.mss`. So each recordable stage is served
    by the **longest** mint whose name prefixes it — `stage1-water` takes
    `mint-stage1-water` over `mint-stage1`, which is the whole point of having
    both.

    Returns `(pairs, unserved)`: `pairs` is `[(mint_path, [stage names])]` in
    mint order, `unserved` is the recordable stages no mint prefixes. An
    unserved stage is not an error — it may legitimately play from power-on, or
    its state may come from a `.chain.txt` this job does not replay — but it is
    reported, because a stage silently recorded from the title screen is the
    failure this function exists to prevent.
    """
    stage_dir = Path(stage_dir)
    mints = {p.stem[len("mint-"):]: p for p in entry_scripts(stage_dir)}
    served = {name: [] for name in mints}
    unserved = []
    for stage in recordable_stages(stage_dir):
        name = stage.stem
        best = None
        for m in mints:
            if (name == m or name.startswith(m + "-")) and (best is None or len(m) > len(best)):
                best = m
        if best is None:
            unserved.append(name)
        else:
            served[best].append(name)
    pairs = [(mints[m], sorted(served[m])) for m in sorted(mints) if served[m]]
    return pairs, sorted(unserved)


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
        stages = recordable_stages(stage_set["dir"])
        if stages:
            row.update(driver=ROUTES, stages=str(stage_set["dir"]),
                       stageCount=len(stages))
            row["reason"] = (
                f"route set {stage_set['name']}/ declares this No-Intro SHA1; "
                f"{len(stages)} recordable stage(s)")
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
            f"| {_cell(r.get('stageCount') or None)} | {_cell(r.get('retained'))} "
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

    m = sub.add_parser("mints", help="mint script -> stages it serves, NUL-separated")
    m.add_argument("stage_dir")

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
        if args.cmd == "mints":
            pairs, unserved = mint_plan(args.stage_dir)
            for mint, stages in pairs:
                sys.stdout.write(str(mint) + "\0" + ",".join(stages) + "\0")
            if unserved:
                print("unserved: " + ", ".join(unserved), file=sys.stderr)
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
