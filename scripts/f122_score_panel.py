#!/usr/bin/env python3
"""Score the mechanical half of an F12.2 artist-surface cold read, from the pack

ADR-0214 §4 hands criteria 3, 5 and 6 to a machine even though the run itself
is cold: the evaluator says what it picked and pastes what the viewer copied,
and this reads the result back. It never decides criterion 1 (whether the
action was findable) or criterion 4 (`hires.txt` never opened) - those are
facts about the evaluator and live in the log. This read of the manifest is
the machine's; the evaluator's own criterion 5 comes from the build's
`report:` row (#511), and the two must name the same `<tile>`.

What it does, in the panel script's own step order:

  P12/P13  find the 32x32 magenta square across the pack's sheets, then
           read the rebuilt `textures/hires.txt` and report the `<tile>` whose
           x,y is that crop. Criterion 5 is that line existing at all *and*
           carrying the same tile key the evaluator's own cell declares: the
           key the viewer copied came back out of the build pointing at the
           paint, rather than a different key pointing at the same crop.
  P13      `mep_lint.py` on the pack must exit 0. Criterion 6.
  P14      install the pack at `<rom dir>/mep/`, render the reference frame
           from the dispatcher's own save state, and count magenta pixels.
           Criterion 3 is that count being non-zero on the painted pack and
           zero on an unpainted copy - a count on its own proves nothing, so
           the baseline is measured too.

Usage:
    python3 scripts/f122_score_panel.py --game <game> \
        --pack ~/f12.2-opus-sandbox/games/<game> \
        --state ~/f12.2-opus-sandbox/frames/<game>.mss \
        [--rom <rom path>] [--baseline <unpainted work copy>] \
        [--out runs/f12.2-sweep/score]
"""

import argparse
import json
import pathlib
import shutil
import struct
import subprocess
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import sheet_repaint as _repaint  # noqa: E402  (same folder, stdlib-only)

REPO = pathlib.Path(__file__).resolve().parent.parent
MAGENTA = (255, 0, 255)

#The same two games the panel script bounds, with the frame each is asserted
#at - the frame the dispatcher's `frames/<game>.mss` restores.
GAMES = {
    "zelda": {
        "rom": "The Legend of Zelda (1987) (Nintendo).nes",
        "pack_dir": "The Legend of Zelda (1987) (Nintendo)",
        "folder": "zelda",
    },
    "contra": {
        "rom": "Contra (1988) (Konami).nes",
        "pack_dir": "Contra (1988) (Konami)",
        "folder": "contra",
    },
}

DEFAULT_ROMS = pathlib.Path(
    "/Users/bihaiko/VSCodeProjects/EMULADORES/2. Switch/G3 - Nitendinho/roms")


class ScoreError(Exception):
    """Something the scorer cannot honestly score past."""


def log(step, text):
    print(f"{step:<10} {text}", flush=True)


def png_size(path):
    head = path.read_bytes()[:24]
    return struct.unpack(">II", head[16:24])


def find_magenta(png, size=32):
    """P12's own pixels, found rather than assumed. The evaluator chose where
    to put the square; this reads where it landed, so the crop in `hires.txt`
    and the pixels on screen are compared against the same anchor."""
    img = _repaint.read_png(png)
    hits = []
    for y in range(img.height - size + 1):
        for x in range(img.width - size + 1):
            ok = True
            for row in range(y, y + size, 8):
                for col in range(x, x + size, 8):
                    off = img.offset(col, row)
                    if tuple(img.px[off:off + 3]) != MAGENTA:
                        ok = False
                        break
                if not ok:
                    break
            if ok:
                #Confirm every pixel, not just the sampled grid.
                full = all(tuple(img.px[img.offset(c, r):img.offset(c, r) + 3]) == MAGENTA
                           for r in range(y, y + size)
                           for c in range(x, x + size))
                if full:
                    hits.append((x, y))
    return hits


def scan_sheets(pack):
    """Every sheet the pack carries, the magenta squares on the ones that
    decode, and the names of the ones that do not.

    An unreadable sheet is a fact about the pack rather than a reason to stop:
    a 0-byte `metatiles.png` is what `rm -rf metatiles.*` leaves behind (#510,
    F14.2 retest16), and the panel still has everything the damage did not
    touch. `read_png` refuses a file it cannot decode with `RepaintError` -
    the same error the tool raises for a missing sheet - so that is the one
    caught here; it is returned to the caller to report, never swallowed."""
    found, unreadable = [], []
    for sheet in sorted((pack / "textures/sheets").glob("*.png")):
        if sheet.name.endswith(".orig.png"):
            continue
        try:
            spots = find_magenta(sheet)
        except (_repaint.RepaintError, OSError):
            unreadable.append(sheet.name)
            continue
        for spot in spots:
            found.append((sheet.name, spot))
    return found, unreadable


def read_roundtrip(hires, x, y, key):
    """Every `<tile>` line whose x,y is the painted crop, and whether the key
    the evaluator pasted is among them.

    `key` is the CHR index the evaluator's own cell declares, as a decimal
    string, or "" when the pack is byte-keyed (CHR RAM) and has no index to
    check. `mep_build` writes `<tile>image,tileKey,palette,x,y,...` and the
    tile key is the CHR index in hex when the pack is index-keyed
    (ADR-0172) and the 32-hex tile bytes when it is not. Field 0 is the image
    the crop comes from, not the key, so reading the wrong field finds the
    wrong answer on a pack that works - which is what a first attempt here
    did, on Excitebike, against a rule carrying `0183` for index 387."""
    lines = [l for l in hires.read_text().splitlines()
             if "<tile>" in l and f",{x},{y}," in l]
    if not key:
        return lines, list(lines)
    #Parsed as a number, not as text: the packs write the index in hex
    #unpadded in some sources and padded in others - Bubble Bobble's own rule
    #for index 38 reads `26`, Excitebike's for 387 reads `0183` - so comparing
    #strings fails on a pack that is correct. `int(..., 16)` reads both, and a
    #byte-keyed rule (32 hex characters) is not a number and is skipped.
    want = int(key)
    named = []
    for line in lines:
        fields = line.split("<tile>")[1].split(",")
        if len(fields) < 2:
            continue
        try:
            if int(fields[1], 16) == want:
                named.append(line)
        except ValueError:
            continue
    return lines, named


def cell_index_at(pack, sheet_name, x, y):
    """The CHR index the evaluator's own cell declares for the painted crop,
    or [] when the pack keys by tile bytes and carries no index.

    Only the sheet the square was painted on is read. Every sheet numbers its
    own cells from the same origin, so two sheets routinely carry a cell at
    the same `x,y` - Excitebike's `sprites.json` and `unsorted.json` both have
    one at `28,73` - and a search across sheets returns whichever the glob
    reached first. The sidecar's `x,y` are in `.orig.png` 1x units and the
    crop is in the painted PNG's units, so the cell is found by scaling."""
    sheet = pack / "textures/sheets" / (pathlib.Path(sheet_name).stem + ".json")
    try:
        doc = json.loads(sheet.read_text())
    except (OSError, ValueError):
        return []
    scale = doc.get("scale") or 4
    hits = []
    for cell in doc.get("cells", []):
        if cell.get("x") != x // scale or cell.get("y") != y // scale:
            continue
        for tile in cell.get("tiles", []):
            if "index" in tile:
                hits.append(str(tile["index"]))
    return hits


def render(roms, game, pack, out, state):
    """P14: install, reopen the ROM at the reference frame, count magenta.

    `game` may come from GAMES or from the CLI overrides - the sweep's 28 ROMs
    are not in that table, and a scorer that only knows two games cannot score
    them."""
    rom_path = game["rom_path"]
    pack_dir = game["pack_dir_path"]
    mep = pack_dir / "mep"
    aside = pack_dir / "mep.scorer-aside"
    if aside.exists():
        raise ScoreError(f"{aside} already exists - a previous scoring run did "
                         "not clean up")
    if mep.exists():
        mep.rename(aside)
    try:
        shutil.copytree(pack, mep)
        #The tool's `mesen-home` is a sibling of the output prefix, not a child
        #of it (`home = outDir / "mesen-home"`, outDir being the prefix's own
        #folder), and it is shared by every run that passes a prefix in the same
        #folder. One folder per game, emptied first, so the screenshot read back
        #is this run's and not the other game's.
        run_dir = out / game["folder"]
        shutil.rmtree(run_dir, ignore_errors=True)
        run_dir.mkdir(parents=True, exist_ok=True)
        prefix = run_dir / "check"
        proc = subprocess.run(
            ["caffeinate", "-dimsu", str(REPO / "scripts/headless_record"),
             str(rom_path), "0", str(prefix), "screenshot", "log",
             f"state={state}"],
            cwd=str(REPO), capture_output=True, text=True, timeout=1800)
        if proc.returncode != 0:
            raise ScoreError(f"headless_record exited {proc.returncode}\n"
                             f"{proc.stdout[-1500:]}\n{proc.stderr[-1500:]}")
        tiles = None
        for line in proc.stdout.splitlines():
            if "LoadHdPack:" in line and "tiles=" in line:
                tiles = int(line.split("tiles=")[1].split()[0])
        shots = sorted((run_dir / "mesen-home/Screenshots").glob("*.png"))
        if not shots:
            raise ScoreError("headless_record took no screenshot")
        img = _repaint.read_png(shots[-1])
        count = sum(1 for i in range(0, len(img.px), 4)
                    if tuple(img.px[i:i + 3]) == MAGENTA)
        return count, tiles, shots[-1]
    finally:
        shutil.rmtree(mep, ignore_errors=True)
        if aside.exists():
            aside.rename(mep)


def score(args):
    #The two bounded games keep their own entries so the dated log's commands
    #stay reproducible; anything else names its ROM and pack folder outright.
    game = dict(GAMES.get(args.game, {}))
    if args.rom is not None:
        game["rom_path"] = args.rom.expanduser().resolve()
        game["pack_dir_path"] = game["rom_path"].parent / game["rom_path"].stem
    else:
        game["rom_path"] = args.roms / game["rom"]
        game["pack_dir_path"] = args.roms / game["pack_dir"]
    game.setdefault("folder", args.game)
    pack = args.pack.expanduser().resolve()
    state = args.state.expanduser().resolve()
    if not (pack / "textures/hires.txt").is_file():
        raise ScoreError(f"{pack} has no textures/hires.txt - the evaluator "
                         "never built it")
    if not state.is_file():
        raise ScoreError(f"{state} does not exist - mint it with "
                         "`headless_record ... save-state=` at the frame "
                         "frames/<game>.png shows")

    report = {}

    #P13 - the build the evaluator ran must still hold, and lint must be clean.
    proc = subprocess.run(["python3", str(REPO / "scripts/mep_lint.py"), str(pack)],
                          cwd=str(REPO), capture_output=True, text=True, timeout=900)
    report["lint"] = proc.returncode

    #Every sheet, not `misc.png`: the free-form sheet is `unsorted.png` in some
    #packs (Super Mario Bros. 3's is), and the copy action's target is decided by
    #the cell's own `context`, not by a filename this scorer can guess.
    found, unreadable = scan_sheets(pack)
    #Reported where they are found rather than through `report`, so the finding
    #survives the ScoreError below: on a pack whose paint sat on the sheet that
    #was destroyed, "no magenta square" is the damage, and a reader who is not
    #told a sheet could not be read reads it as an evaluator who painted
    #nothing.
    for name in unreadable:
        log("score", f"unreadable_sheet: {name}")
    report["magenta_blocks"] = found
    if not found:
        raise ScoreError(f"no 32x32 magenta square on any sheet of {pack} - the "
                         "evaluator painted nothing, so criteria 3 and 5 cannot "
                         "be scored")
    #One square is the panel script's shape, not the protocol's. Metroid's run
    #pasted six cells to prove the blocker was not one bad key, and a scorer
    #that insisted on exactly one would call that pack unscoreable when it is
    #the better evidence. The first square by name is the one the round-trip is
    #read against; the magenta pixel count below covers all of them.
    report["magenta_squares"] = len(found)

    sheet_name, (x, y) = sorted(found)[0]
    report["painted_sheet"] = sheet_name
    report["painted_crop"] = (x, y)
    keys = cell_index_at(pack, sheet_name, x, y)
    report["cell_index_at_crop"] = keys
    lines, matched = read_roundtrip(pack / "textures/hires.txt", x, y, keys[0] if keys else "")
    report["tiles_at_crop"] = lines
    if not lines:
        raise ScoreError(f"no <tile> in hires.txt points at the painted crop "
                         f"{x},{y} - criterion 5 fails: the build did not carry "
                         "the paint into a rule")
    if keys and not matched:
        raise ScoreError(f"the evaluator's cell declares index {keys[0]}, but no "
                         f"<tile> at the painted crop carries it (found "
                         f"{[l.split('<tile>')[1].split(',')[1] for l in lines]}) "
                         "- the paint reached a rule, but not that key")
    report["roundtrip_line"] = (matched or lines)[0]

    #P14 - the frame has to actually draw it, and the unpainted pack has to not.
    painted, tiles, shot = render(args.roms, game, pack, args.out, state)
    report["painted_magenta"] = painted
    report["tiles_loaded"] = tiles
    report["screenshot"] = str(shot)
    if args.baseline is not None:
        base = args.baseline.expanduser().resolve()
        before, _, _ = render(args.roms, game, base, args.out / "baseline", state)
        report["baseline_magenta"] = before
        if before != 0:
            raise ScoreError(f"the unpainted pack already renders {before} "
                             "magenta pixels - the count proves nothing")
    if painted == 0:
        raise ScoreError("the painted pack renders no magenta at the reference "
                         "frame: criterion 3 fails")
    return report


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--game", required=True,
                        help="a name from GAMES (zelda, contra), or any label when "
                             "--rom is given - it names the output folder")
    parser.add_argument("--rom", type=pathlib.Path,
                        help="the ROM itself, for a game not in GAMES; the pack folder "
                             "is then its path without the extension")
    parser.add_argument("--pack", type=pathlib.Path, required=True,
                        help="the pack the evaluator built, inside its sandbox")
    parser.add_argument("--state", type=pathlib.Path, required=True,
                        help="the .mss the dispatcher minted at the reference frame")
    parser.add_argument("--baseline", type=pathlib.Path,
                        help="the unpainted work copy, to prove the count means "
                             "something")
    parser.add_argument("--roms", type=pathlib.Path, default=DEFAULT_ROMS)
    parser.add_argument("--out", type=pathlib.Path,
                        default=REPO / "runs/f12.2-panel")
    args = parser.parse_args(argv)
    args.out.mkdir(parents=True, exist_ok=True)
    try:
        report = score(args)
    except ScoreError as ex:
        log("FAIL", str(ex))
        return 1
    for key, value in report.items():
        log("score", f"{key}: {value}")
    log("", "criteria 3, 5 and 6 pass on this pack. Criterion 1, 4, 7 and 8 are "
            "the log's, not this script's.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
