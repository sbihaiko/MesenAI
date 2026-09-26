#!/usr/bin/env python3
"""Build the evaluator sandbox for one ROM: frame, state, tilemap, copy table.

The F12.2 dispatcher script
(`docs/validation/f12.2-copy-sheet-cell-panel-script.md`, S1-S7) was written
for two games whose routes already existed and whose reference frame had
already been measured. This is the same thing for a ROM with no route at all,
which is 25 of the 30 in the library: the frame has to be *found* before it can
be handed over, and the only honest way to find one is to ask the emulator
whether the pack's rules reach the screen at that frame.

The probe below does that. It builds the pack once, then repaints in the sheet
PNGs every crop the built `hires.txt` already names, each with a colour that
encodes which rule it is, installs that as `mep/` beside the ROM, and renders
the candidate seconds. A colour on screen is proof that rule is drawn there.

Painting the *cells of a sidecar* instead is the obvious first attempt and
does not work: only 295 of Tetris's 1483 sidecar tiles become `<tile>` lines,
so most of the paint lands on art no rule reads. That was measured, not
guessed.

The frame then has to be handed over clean - the probe's colours are not what
the evaluator should see - so the pack is installed unpainted at the chosen
second and the state is minted there.

One moment
----------
The four per-game inputs - the frame PNG, the save state, the tilemap image and
the copy table - must all describe **one screen**: the background the player is
looking at on that frame has to be the background the tilemap dumps and the copy
table keys. A panel whose two pictures disagree measures the disagreement, not
the feature: the evaluator picks a shape off one picture, pastes the key the
other picture answers with, builds, renders, and sees nothing - and the run
scores the feature as broken when the dispatcher handed over two screens.

The 2026-09-19 sweep paid for this twice. Bubble Bobble's tilemap holds the whole
"Bubble Bobble" logo and the frame the same state renders holds none of it: that
run built a throwaway pack with one cell per distinct key of the copy table, 195
of them in 195 colours, and found exactly **2** on the rendered frame, both the
blank tile. Tetris named the same thing from the other side, its tilemap reading
`LINES-001` / `SCORE 000085` against a frame reading `LINES-000` / `SCORE 000000`.

The two are not different *moments* - the screenshot and the `.mss` come from the
same parked frame of the same run, and the scan restores that very state. What
differs is whether the nametable is on the screen at all. The frame was chosen by
"the most `<tile>` rules are drawn here", counting every rule, and a rule drawn
from a *sprite* sheet scores exactly like one drawn from a background sheet. On
Bubble Bobble the 23 rules that won the frame are the attract mode's bubbles:
59 917 of the frame's 61 440 pixels are the backdrop, the background layer draws
nothing, and the logo sitting in VRAM is never fetched. So:

- `choose_frame` scores **background** rules only (`sheet_kind` reads each
  `<img>`'s sidecar `kind`), because the cold read is a background read, and it
  renders each candidate second a second time with no pack over it to ask whether
  the background layer draws anything there at all. A rule count cannot answer
  that on its own: the recording's `chr/` pages hold the sprite tiles too, so on
  Bubble Bobble's black frame 23 "background" rules are drawn and every one of
  them is a bubble;
- that count is also fooled from the other side, and since #494 the probe paints
  the pack's **recorded screens** flat as well as its rules. A
  `textures/backgrounds/screenNNN.png` is drawn at priority 20, i.e. *after* the
  `<tile>` layer, and the recorder writes it opaque and the size of the screen
  (ADR-0050, ADR-0156), so on a second it matches, the frame *is* that PNG and
  every `<tile>` rule on it is invisible - no painted cell can reach the game
  there. The colour count cannot see that on its own: the rule colours are
  `(255, g, b)` and upscaled art carries pixels that match them by coincidence,
  which the capture's own pixels then hide. Tetris 2's F14.2 re-score is the
  measured case - a 40 s frame that is `screen002.png` came back "drawing 3
  background rule(s)", the panel's cell (14,18) was under the capture, and
  painting it changed no pixel (`docs/validation/`
  `f14.2-rescore-after-431-gauntlet-tetris2-2026-09-24.md`). A candidate whose
  frame a recorded screen owns is dropped, and a game whose every sampled second
  is owned is refused by name instead of handed over;
- after the scan, `moment_agreement` measures the claim instead of asserting it:
  it re-renders the state with the texture layer off, redraws every copy-table
  cell in the emulator's own colours, and counts how many of the frame's 8x8
  blocks a copy-table key explains. The number is in `report.json` and in the
  per-game index the evaluator reads, and a frame that fails the gate is
  re-chosen from the next-best candidate rather than shipped;
- that count can lie by itself, though - it matches *pixel designs*, and a
  repeating one (a brick, the blank tile) can "explain" most of a frame from a
  near-empty dump. The 2026-09-19 Sonnet sweep read Contra's "692/930 blocks
  explained" and Ninja Gaiden's "828/960" as if they meant the dump was that
  complete, then found the actual `tilemap-copy/*.txt` held 90 and 30 rows.
  `dump_coverage` is the honest number - the dump's own row count over the
  frame's `FULL_GRID_CELLS` - logged and gated on (`MOMENT_CELLS`) apart from
  the pixel-match figure, and never conflated with it in the index again;
- `draw_tilemap` dims every tile whose key is not on the frame, so "this shape is
  not on the screen you were given" is visible in the picture rather than a round
  trip the evaluator pays for;
- `visible_window` answers the other half of the same question positionally: the
  screen is a window into the four nametables, so the frame's non-blank blocks
  vote for the cell the screen's top-left sits at and the tilemap gets a cyan box
  around the part it shows. Zelda II's 2026-09-19 run picked row 0 - twelve rows
  above the window - and built, linted and rendered it before anything said so;
- the scan walks all four nametables and keeps what the frame drew (#421), and
  it checks palettes against the baseline pack the evaluator paints, installed
  as `mep/` with the recording's `auto/` wildcards set aside (#420);
- the frame is handed over twice. Tetris's halves were both right and came from
  different layers: the frame PNG is rendered with the pack installed, and a
  captured `<background>` frozen at a neighbouring moment paints an old scoreboard
  over it, while the tilemap reads the live PPU. `capture_drift` measures that in
  pixels and `frames/<game>-screen.png` is the same state with the texture layer
  off - the picture the copy table actually answers for.

Usage:
    python3 scripts/f122_prepare_evaluator.py --game "<rom stem>" \
        [--sweep runs/f12.2-sweep] [--sandbox ~/f12.2-sweep-sandbox]
        [--candidates 40,20,60,30,10,50]
"""

import argparse
import json
import pathlib
import re
import shutil
import subprocess
import sys

REPO = pathlib.Path(__file__).resolve().parent.parent
sys.path.insert(0, str(REPO / "scripts"))
import sheet_repaint as _repaint  # noqa: E402
from artist_map import NES_PALETTE  # noqa: E402

TILE = re.compile(r"^<tile>(\d+),([0-9A-Fa-f]+),([0-9A-Fa-f]+),(\d+),(\d+),")
IMG = re.compile(r"^<img>(.+)$")

#A sheet whose cells are sprite shapes never draws the background, so a rule of
#its is no evidence that the nametable is on screen. `kind` is the sidecar's own
#word for what the surface holds; the name test is the fallback for the
#recorder's own layer, whose `chr/` and `backgrounds/` images carry no sidecar.
SPRITE_KINDS = {"sprite", "sprites", "poses"}

#A frame passes the one-moment gate when the copy table explains this much of
#the screen and names this many non-blank shapes on it. Both are needed: a black
#attract frame is 97% "explained" by one blank key, and a frame with three
#non-blank keys in a corner is not a frame an artist can pick a shape off.
MOMENT_BLOCKS = 0.50
MOMENT_KEYS = 4
#The screen is 256x240, so this many 8x8 cells are on it. Since #421 the scan
#walks all four nametables the Tilemap Viewer shows (512x480, `WalkPositions` in
#CopyAsMepSheetCellTests.cs) and the copy itself refuses every cell no visible
#scanline fetched, so a table holds the cells *this frame drew*, from whichever
#nametable drew them, in the viewer's own coordinates (columns 0-63, rows 0-59).
#A scanline fetches 33 columns for the fine-X shift, so a full table can name a
#few more than this; `dump_coverage` caps the share at 1.0.
FULL_GRID_CELLS = (256 // 8) * (240 // 8)
#A third leg of the same gate, and the one the 2026-09-19 Sonnet sweep's
#"coverage-count mismatch" finding was missing. `explained` in `moment_agreement`
#counts *pixel-pattern* repeats - a handful of table rows can "explain" most of
#the screen when their designs (a brick, the blank tile) tile across it - so it
#stayed high on Contra (692/930, from a 90-row dump) and Ninja Gaiden (828/960,
#from 30 rows, every one of them column 0) while the actual dump the evaluator
#pastes from covered under a tenth of the frame. This checks the dump itself:
#the fraction of the 960 positions the copy action actually named something for,
#with no repeats to inflate it.
MOMENT_CELLS = 0.50
#How many candidate seconds may be minted and scanned before the game is handed
#over with its measurement and a warning. A scan is the expensive step, so this
#is deliberately small.
MOMENT_TRIES = 2

#A screen that is this much one colour has no background layer on it: the PPU
#can draw at most 64 sprite tiles of the frame's 960, so everything else is the
#backdrop. Bubble Bobble's 2026-09-19 frame is 59 917 of 61 440 pixels black,
#with the whole title logo sitting unfetched in the nametable behind it. Counting
#rules cannot see this - the recording's `chr/` pages carry the sprite tiles too,
#so a bubble drawn from one scores exactly like a wall.
BLANK_SCREEN = 0.85

#A frame is worth a cold read when several rules are drawn on it; below this
#the screen is mostly art the pack does not cover, and the evaluator would be
#picking shapes the tool cannot repaint. Not a hard gate - a thin frame is
#still recorded, with the count in the report - because refusing to run at all
#is a worse answer than a stated one.
THIN = 3
#Stop looking once a frame draws this many rules: 30 ROMs x 6 renders is not a
#budget worth spending to improve a frame that is already good.
GOOD = 12


class PrepareError(Exception):
    """Something the dispatcher cannot honestly work around."""


def log(step, text):
    print(f"{step:<10} {text}", flush=True)


def safe_name(stem):
    return "".join(c if (c.isalnum() or c in "._-") else "_" for c in stem)


def build(pack):
    proc = subprocess.run(["python3", str(REPO / "scripts/mep_build.py"), "build", str(pack)],
                          cwd=str(REPO), capture_output=True, text=True, timeout=1800)
    errors = [l for l in proc.stdout.splitlines() if l.startswith("error:")]
    return proc.returncode, errors


def rules_of(pack):
    """Every `<tile>` line the built pack names, as (image, key, palette, x, y)."""
    hires = (pack / "textures/hires.txt").read_text().splitlines()
    images = [IMG.match(l).group(1) for l in hires if IMG.match(l)]
    out = []
    for line in hires:
        m = TILE.match(line)
        if m:
            out.append((int(m.group(1)), m.group(2), m.group(3),
                        int(m.group(4)), int(m.group(5))))
    return images, out


def sheet_kind(pack, rel):
    """What the surface behind an `<img>` holds, as its own sidecar says."""
    path = pathlib.Path(rel.lstrip("/"))
    sidecar = pack / "textures" / path.parent / (path.stem + ".json")
    if sidecar.is_file():
        try:
            return str(json.loads(sidecar.read_text()).get("kind", ""))
        except ValueError:
            pass
    stem = path.stem
    return "sprite" if (stem.startswith("spr") or stem == "sprites") else ""


def paint_probe(pack, work, block=32, background_only=True):
    """Repaint every crop a rule names, each in its own colour, and every
    recorded screen flat.

    `background_only` drops the sprite sheets: the cold read is a background
    read, and a frame carried entirely by sprite rules is a frame whose
    nametable - the thing the tilemap and the copy table describe - is not on
    the screen. See *One moment* above.

    The recorded screens are repainted too, one shade each, because a
    `<background>` at priority 20 is drawn after the `<tile>` layer and the
    recorder writes it opaque and the size of a whole screen: on a second it
    matches, the frame is that PNG and every rule under it is invisible
    (ADR-0050, ADR-0156, #494). Painting them flat does both halves of that -
    a rule behind a screen is not counted as drawn at all, and
    `capture_owned` can say which cells the screen owns - where leaving their
    art in place leaves a rule colour findable by coincidence on a frame no
    rule reaches. Returns the rule colours, the rule count, and
    `{probe colour: name}` for the screens."""
    images, rules = rules_of(pack)
    if background_only:
        sprites = {i for i, rel in enumerate(images)
                   if sheet_kind(pack, rel) in SPRITE_KINDS}
        rules = [r for r in rules if r[0] not in sprites]
    shutil.rmtree(work, ignore_errors=True)
    shutil.copytree(pack, work)

    per_image = {}
    for idx, key, pal, x, y in rules:
        per_image.setdefault(idx, []).append((x, y, key))

    colors = {}
    for idx, entries in sorted(per_image.items()):
        rel = images[idx].lstrip("/") if idx < len(images) else ""
        png = work / "textures" / rel
        if not png.exists():
            png = work / "textures" / pathlib.Path(rel).name
        if not png.exists():
            raise PrepareError(f"rule image {idx} ('{rel}') is not in {work}/textures/")
        img = _repaint.read_png(png)
        for n, (x, y, key) in enumerate(entries):
            i = (idx * 4096 + n) & 0xFFFF
            rgba = (255, (i >> 8) & 0xFF, i & 0xFF, 255)
            colors[rgba[:3]] = key
            for row in range(y, min(y + block, img.height)):
                for col in range(x, min(x + block, img.width)):
                    off = img.offset(col, row)
                    img.px[off:off + 4] = bytes(rgba)
        _repaint.write_png(png, img)

    #One shade per screen. `(254, g, 254)` is no NES palette entry and no blend
    #of two of them, and its red channel is 254 where every rule above carries
    #255, so a pixel of one can only be the screen it was painted on. A pack
    #with more than 128 recorded screens repeats a shade and costs the log its
    #name, not the answer: what is measured is the pixels, not the label.
    captures = {}
    for n, png in enumerate(sorted((work / "textures/backgrounds").glob("*.png"))):
        rgb = (254, n & 0x7F, 254)
        captures[rgb] = f"backgrounds/{png.name}"
        img = _repaint.read_png(png)
        for i in range(0, len(img.px), 4):
            img.px[i:i + 4] = bytes(rgb) + b"\xff"
        _repaint.write_png(png, img)
    return colors, len(rules), captures


def render(rom, seconds, prefix, load=None, save=None, flags=()):
    """`load` restores a .mss, `save` mints one - different flags on purpose.

    `state=` opens the run at a saved frame, `save-state=` writes one at the end
    of the run. Minting with `state=` produces a run that plays the whole way
    and saves nothing, which is what this dispatcher did first."""
    cmd = ["caffeinate", "-dimsu", str(REPO / "scripts/headless_record"),
           str(rom), str(seconds), str(prefix), "screenshot", "log", *flags]
    if load is not None:
        cmd.append(f"state={load}")
    if save is not None:
        cmd.append(f"save-state={save}")
    proc = subprocess.run(cmd, cwd=str(REPO), capture_output=True, text=True, timeout=1800)
    if proc.returncode != 0:
        raise PrepareError(f"headless_record exited {proc.returncode}\n{proc.stdout[-800:]}")
    return proc.stdout


def backdrop_share(shot):
    """The largest share of the screen one colour holds, 0..1."""
    img = _repaint.read_png(shot)
    counts = {}
    for i in range(0, len(img.px), 4):
        rgb = bytes(img.px[i:i + 3])
        counts[rgb] = counts.get(rgb, 0) + 1
    return max(counts.values()) / (img.width * img.height)


def count_colors(shot, colors):
    img = _repaint.read_png(shot)
    counts = {}
    for i in range(0, len(img.px), 4):
        rgb = tuple(img.px[i:i + 3])
        if rgb in colors:
            counts[rgb] = counts.get(rgb, 0) + 1
    return counts


def capture_owned(shot, captures):
    """Which cells of a rendered frame a recorded screen owns (#494).

    `captures` is `paint_probe`'s map of the flat shade each recorded screen of
    the pack was repainted in, so a pixel of one of those shades on the frame
    can only be that screen's own pixels. A cell is owned when a screen is the
    *majority* of it: a cell a screen only clips still shows most of the
    painted `<tile>` rule, which is what the panel's criterion 3 counts, and
    the majority is also what stops a stray pixel of art that happens to match
    a shade from costing a candidate its frame.

    Returns `{name: {(col, row), ...}}` in screen cells (0-31, 0-29)."""
    if not captures:
        return {}  #a pack with no recorded screens has nothing to own a frame
    img = _repaint.read_png(shot)
    want = {bytes(rgb): name for rgb, name in captures.items()}
    scale = max(1, img.width // 256) * 8
    owned = {}
    for cy in range(0, img.height - scale + 1, scale):
        for cx in range(0, img.width - scale + 1, scale):
            seen = {}
            for y in range(cy, cy + scale):
                base = y * img.width * 4
                for x in range(cx, cx + scale):
                    off = base + x * 4
                    rgb = bytes(img.px[off:off + 3])
                    if rgb in want:
                        seen[rgb] = seen.get(rgb, 0) + 1
            for rgb, n in seen.items():
                if n * 2 > scale * scale:
                    owned.setdefault(want[rgb], set()).add((cx // scale, cy // scale))
    return owned


def choose_frame(rom, pack, out, candidates):
    """S6's missing half: which second of the recording draws the pack at all.

    Both layers are repainted, not just the human one. The recording is the
    `auto/` layer and it supplies most of what is *drawn*: on Bubble Bobble the
    built `mep/` pack names 234 `<tile>` rules while the runtime reports 2296,
    because `build` only emits a rule for the cells its sheets claim. Painting
    the human pack alone therefore paints art the screen never fetches - it
    found nothing on Bubble Bobble at any second, while the title screen was
    plainly HD.

    Sprite rules are not painted (`paint_probe`'s `background_only`), so what a
    candidate second is scored on is how much *background* the pack draws there.
    Returns the candidates that drew anything, best first, so a frame that then
    fails the one-moment gate can be replaced by the runner-up instead of
    re-probing. A candidate a recorded screen owns is not among them: that
    screen is the frame, it is drawn after every `<tile>` rule, and a panel
    handed over on it cannot be won (#494)."""
    colors, rules, captures = paint_probe(pack, out / "probe-pack")
    auto = rom.parent / rom.stem / "auto"
    auto_colors, auto_rules, auto_captures = ({}, 0, {})
    if auto.is_dir():
        auto_colors, auto_rules, auto_captures = paint_probe(auto, out / "probe-auto")
        for c, k in auto_colors.items():
            colors.setdefault(c, f"auto:{k}")
        #The auto layer's screens are never drawn while the human pack is
        #installed - the loader keeps them out under a human layer - and its
        #shades repeat the pack's, so `setdefault` keeps the pack's name. The
        #shade still has to be painted flat: it is art otherwise.
        for c, name in auto_captures.items():
            captures.setdefault(c, f"auto:{name}")
    log("probe", f"{rules} background rule(s) repainted in the pack, {auto_rules} "
                 f"in the recording's auto layer, {len(captures)} recorded screen(s) "
                 f"flattened")
    mep = rom.parent / rom.stem / "mep"
    aside = rom.parent / rom.stem / "auto.probe-aside"
    if aside.exists() or mep.exists():
        raise PrepareError(f"{mep if mep.exists() else aside} is in the way - a "
                           "previous probe did not clean up")

    ranked, owned_seconds = [], []
    try:
        if auto.is_dir():
            auto.rename(aside)
            shutil.copytree(out / "probe-auto", auto)
        for t in candidates:
            shutil.rmtree(mep, ignore_errors=True)
            shutil.copytree(out / "probe-pack", mep)
            run = out / f"probe-t{t}"
            shutil.rmtree(run, ignore_errors=True)
            run.mkdir(parents=True)
            render(rom, t, run / "p")
            shots = sorted((run / "mesen-home/Screenshots").glob("*.png"))
            if not shots:
                log("probe", f"t={t}s: no screenshot")
                continue
            owned = capture_owned(shots[-1], captures)
            if owned:
                #The frame is a recorded screen, whole. It is opaque and drawn
                #after the `<tile>` layer, so nothing painted on this second can
                #reach the display, and the rules it hides are exactly the ones
                #a colour count cannot see are gone (#494).
                names = ", ".join(sorted(owned))
                log("probe", f"t={t}s: {names} owns "
                             f"{sum(len(c) for c in owned.values())} cell(s) of this "
                             "frame - no painted cell can reach the game here")
                owned_seconds.append(f"{t}s is {names}")
                continue
            counts = count_colors(shots[-1], colors)
            #The same second with no pack over it, to ask the other half of the
            #question: is the background layer drawing anything at all there?
            bare = out / f"probe-n{t}"
            shutil.rmtree(bare, ignore_errors=True)
            bare.mkdir(parents=True)
            render(rom, t, bare / "p", flags=("hdpack-off",))
            plain = sorted((bare / "mesen-home/Screenshots").glob("*.png"))
            share = backdrop_share(plain[-1]) if plain else 1.0
            on_screen = share < BLANK_SCREEN
            log("probe", f"t={t}s: {len(counts)} background rule(s) drawn, "
                         f"{sum(counts.values())} px, "
                         f"{share:.1%} of the screen is one colour"
                         f"{'' if on_screen else ' - no background layer here'}")
            if counts:
                ranked.append((t, len(counts), shots[-1], on_screen))
            if any(r[3] and r[1] >= GOOD for r in ranked):
                break
    finally:
        shutil.rmtree(mep, ignore_errors=True)
        if aside.exists():
            shutil.rmtree(auto, ignore_errors=True)
            aside.rename(auto)

    if not ranked:
        raise PrepareError(
            "no candidate second draws a single background <tile> rule, from the "
            "rebuilt pack or from the recording's own auto layer - the screen is "
            "drawn by a captured whole-screen <background>, which overrides every "
            "<tile> rule, so no painted cell could ever show there"
            #Named apart: a screen that owns a frame is a different answer from
            #a frame the pack simply does not cover, and the artist can retire
            #that screen or paint it (#344).
            + (f" - and the pack's own recorded screen owns a sampled second: "
               f"{'; '.join(owned_seconds)}" if owned_seconds else ""))
    #A frame with a background on it beats a frame with more rules on it, always:
    #the rules of a blank screen are all sprite rules wearing a background's name.
    ranked.sort(key=lambda r: (not r[3], -r[1]))
    return ranked


def mint(rom, pack, out, seconds, frames):
    """S6: install the pack unpainted, screenshot and save-state at the frame."""
    mep = rom.parent / rom.stem / "mep"
    shutil.rmtree(mep, ignore_errors=True)
    shutil.copytree(pack, mep)
    run = out / "frame"
    shutil.rmtree(run, ignore_errors=True)
    run.mkdir(parents=True)
    state = frames / f"{safe_name(rom.stem)}.mss"
    out_text = render(rom, seconds, run / "p", save=state)
    if not state.is_file():
        raise PrepareError(
            f"the render finished but {state} was not written - a run that missed "
            f"its target saves nothing:\n{out_text[-600:]}")
    shots = sorted((run / "mesen-home/Screenshots").glob("*.png"))
    if not shots:
        raise PrepareError("headless_record took no screenshot")
    shutil.copy(shots[-1], frames / f"{safe_name(rom.stem)}.png")
    shutil.rmtree(mep, ignore_errors=True)
    return state, shots[-1]


def table_rows(table):
    """`col,row<TAB><json>` per line, as the scan writes it.

    ADR-0216 changed the copy action's own JSON from a bare `{"tile": ...,
    "palette": ...}` to a "sheet cell" wrapper, `{"count": 1, "tiles": [{...}]}`
    (`scripts/mep_add_cell.py`'s docstring names both shapes it accepts). This
    dispatcher predates that change and only ever reasoned about one 8x8 tile
    per cell, so a background rule's copy is unwrapped to its first (and, for
    every rule this dispatcher paints, only) `tiles[]` entry here, once, so
    every caller below keeps seeing the bare shape."""
    rows = []
    for line in table.read_text().splitlines():
        if "\t" not in line:
            continue
        pos, text = line.split("\t", 1)
        try:
            doc = json.loads(text)
        except ValueError:
            continue
        if isinstance(doc, dict) and "tiles" in doc:
            tiles = doc.get("tiles") or []
            doc = tiles[0] if tiles and isinstance(tiles[0], dict) else {}
        col, row = (int(v) for v in pos.split(","))
        rows.append((col, row, doc))
    return rows


def dump_coverage(table):
    """How much of the paused frame the copy action actually named a cell for.

    `WalkTilemap` (`CopyAsMepSheetCellTests.cs`) walks every cell of all four
    nametables and keeps the ones the copy answered for; a cell the frame drew
    is missing from `table` when the copy refused it (`NesDrawnTileResolver`'s
    `BanksDisagree`, or the pack keying it under several drawable palettes,
    none of them live - #342/#420; since ADR-0215's 2026-09-24 amendment a
    tile the pack holds no rule for copies with the live palette). Before
    #421 the walk covered nametable $2000 only, so a scrolled game lost the
    whole part of its screen that another nametable drew. This is the raw row
    count over the screen's `FULL_GRID_CELLS`, with no repetition to inflate
    it - the number an evaluator's own count of the dump file would get -
    capped at 1.0 because a scanline fetches a 33rd column for the fine-X
    shift.

    Do not confuse this with `moment_agreement`'s "blocks explained": that one
    matches rendered *pixel patterns*, so a handful of rows whose design (a
    brick, the blank tile) repeats across the screen can "explain" most of it
    while naming almost none of the frame. The 2026-09-19 Sonnet sweep read
    the two as the same number and called the gap a defect - Contra's index
    claimed "692/930 blocks explained" from a 90-row dump (9% of the frame),
    Mike Tyson's Punch-Out!! claimed "793/960" from 32 rows (3%), and Ninja
    Gaiden claimed "828/960" from 30 rows (3%, every one of them column 0)."""
    cells = len(table_rows(table))
    return cells, min(1.0, round(cells / FULL_GRID_CELLS, 3)) if FULL_GRID_CELLS else 0.0


def tile_pixels(doc):
    """The 8x8 the PPU draws for one copy-table cell, as 24 bytes per row.

    The palette word is four NES colour indices, and `NES_PALETTE` is the table
    the emulator's own screenshots come out in - measured, not assumed: every
    one of the 11 distinct colours in a `hdpack-off` render of Bubble Bobble's
    state is an entry of it. So a tile can be compared to the screen by bytes."""
    data = bytes.fromhex(doc["tile"])
    if len(data) != 16:
        return None
    entries = [NES_PALETTE[int(doc["palette"][i:i + 2], 16) & 0x3F]
               for i in range(0, 8, 2)]
    rows = []
    for y in range(8):
        row = bytearray()
        for x in range(8):
            bit = 7 - x
            idx = ((data[y] >> bit) & 1) | (((data[8 + y] >> bit) & 1) << 1)
            row += bytes(entries[idx])
        rows.append(bytes(row))
    return b"".join(rows)


def moment_agreement(shot, table):
    """Does the copy table describe the screen the state renders?

    `shot` is the state re-rendered with the texture layer off, so it is the
    game's own 256x240 output at exactly the frame the scan read. Every distinct
    copy-table cell is drawn in the emulator's colours and looked up by bytes
    over the screen's 8x8 blocks, at each of the 64 fine-scroll alignments; the
    best alignment wins. Blank cells are counted apart, because a black attract
    frame is explained end to end by one blank key and explains nothing."""
    img = _repaint.read_png(shot)
    rgb = bytearray()
    for i in range(0, len(img.px), 4):
        rgb += img.px[i:i + 3]
    stride = img.width * 3

    tiles = {}
    keys = set()
    for _, _, doc in table_rows(table):
        pixels = tile_pixels(doc)
        if pixels is None:
            continue
        key = f"{doc['tile']}/{doc['palette']}"
        keys.add(key)
        tiles[pixels] = (key, len(set(pixels[i:i + 3] for i in range(0, 192, 3))) == 1)

    best = (0, set(), (0, 0), 0, set())
    for fy in range(8):
        for fx in range(8):
            hit, seen, total, all_seen = 0, set(), 0, set()
            for by in range(fy, img.height - 7, 8):
                base = by * stride + fx * 3
                for bx in range(0, img.width - 7 - fx, 8):
                    total += 1
                    off = base + bx * 3
                    block = b"".join(rgb[off + r * stride:off + r * stride + 24]
                                     for r in range(8))
                    found = tiles.get(block)
                    if found is not None:
                        hit += 1
                        all_seen.add(found[0])
                        if not found[1]:
                            seen.add(found[0])
            if hit > best[0]:
                best = (hit, seen, (fx, fy), total, all_seen)
    hit, seen, fine, total, all_seen = best
    return {
        "blocks": f"{hit}/{total}",
        "explained": round(hit / total, 3) if total else 0.0,
        "keys_on_frame": len(seen),
        "keys_in_table": len(keys),
        "fine_scroll": f"{fine[0]},{fine[1]}",
        #Every key found on the frame, blank ones included: the dimming on the
        #tilemap says "this tile is not on your screen", and a blank tile that is
        #on it must not be marked as if it were the Zelda II case.
        "drawn": all_seen,
    }


def one_moment(agreement, coverage):
    """The one-moment gate, all three legs: pixel-match, non-blank keys, and
    the dump's own coverage of the frame (`MOMENT_CELLS` - see `dump_coverage`).
    One function so the loop's break and the final report can never disagree
    about what passed."""
    return (agreement["explained"] >= MOMENT_BLOCKS
            and agreement["keys_on_frame"] >= MOMENT_KEYS
            and coverage >= MOMENT_CELLS)


def capture_drift(rom, pack, state, out, frame_png):
    """How much of the handed-over frame is a frozen capture, not this moment.

    A pack's `textures/backgrounds/screenNNN.png` is gated on three probe tiles
    (ADR-0050, ADR-0156), so a capture frozen at a neighbouring moment can win on
    this frame and repaint it. Tetris's sweep run is the measured case: with the
    captures in place its frame reads `LINES-000` / `SCORE 000000` while the PPU
    state - the thing the tilemap dumps - reads `LINES-001` / `SCORE 000085`.
    The pack is rendered again with the capture folder removed and nothing else
    changed; the engine drops the dangling `<background>` entries at load, so the
    difference is exactly what the captures painted."""
    mep = rom.parent / rom.stem / "mep"
    bare = out / "probe-nobg"
    shutil.rmtree(bare, ignore_errors=True)
    shutil.copytree(pack, bare)
    shutil.rmtree(bare / "textures/backgrounds", ignore_errors=True)
    shutil.rmtree(mep, ignore_errors=True)
    shutil.copytree(bare, mep)
    run = out / "nobg"
    shutil.rmtree(run, ignore_errors=True)
    run.mkdir(parents=True)
    try:
        render(rom, 0, run / "p", load=state)
        shots = sorted((run / "mesen-home/Screenshots").glob("*.png"))
        if not shots:
            raise PrepareError("the capture-free render took no screenshot")
        a = _repaint.read_png(shots[-1])
        b = _repaint.read_png(frame_png)
    finally:
        shutil.rmtree(mep, ignore_errors=True)
    if (a.width, a.height) != (b.width, b.height):
        return "size mismatch"
    differing = sum(1 for i in range(0, len(a.px), 4)
                    if a.px[i:i + 3] != b.px[i:i + 3])
    return f"{differing}/{a.width * a.height}"


def render_screen(rom, state, out):
    """The state's own picture, with no pack over it: 256x240, one frame, no play.

    `0` seconds is the whole point - `state=` with a length would play on from
    the saved frame, and the picture would be a later moment than the scan's."""
    run = out / "screen"
    shutil.rmtree(run, ignore_errors=True)
    run.mkdir(parents=True)
    render(rom, 0, run / "p", load=state, flags=("hdpack-off",))
    shots = sorted((run / "mesen-home/Screenshots").glob("*.png"))
    if not shots:
        raise PrepareError("the state rendered no screenshot")
    return shots[-1]


def scan(rom, pack, state, table):
    """S7: the copy action's answer for every tile of that frame.

    The copy checks each tile's palette against the pack the core has loaded
    (`NesPackTilePalette`, #342), so the pack beside the ROM during the scan
    decides which keys the table offers. That has to be `pack`, the baseline
    the evaluator's work copy is made from, and nothing else (#420). The
    recording's bootstrap `auto/` is set aside: its `defaultTile=Y` rules are
    palette wildcards, and under them Zelda II's tile 28,0 copied with the live
    `0F301C15` although the baseline keys that tile only as `0F2B0F00`, while
    stale keys with no baseline rule at all went through on Tetris 2 and The
    Flintstones. A `mep/` already there is set aside too, and both come back
    whatever the scan does."""
    sibling = rom.parent / rom.stem
    mep = sibling / "mep"
    aside = [(sibling / "auto", sibling / "auto.scan-aside"),
             (mep, sibling / "mep.scan-aside")]
    for _, parked in aside:
        if parked.exists():
            raise PrepareError(f"{parked} is in the way - a previous scan did not clean up")
    env = dict(**{k: v for k, v in __import__("os").environ.items()})
    env["MESEN_F122_COPY_SCAN"] = str(rom)
    env["MESEN_F122_COPY_STATE"] = str(state)
    env["MESEN_F122_COPY_OUT"] = str(table)
    try:
        for live, parked in aside:
            if live.exists():
                live.rename(parked)
        shutil.copytree(pack, mep)
        proc = subprocess.run(
            ["caffeinate", "-dimsu", "/Library/Developer/CommandLineTools/usr/bin/make",
             "headless-ui-tests",
             'CXX=/Library/Developer/CommandLineTools/usr/bin/clang++ -isysroot '
             '/Library/Developer/CommandLineTools/SDKs/MacOSX.sdk'],
            cwd=str(REPO), capture_output=True, text=True, timeout=3600, env=env)
    finally:
        shutil.rmtree(mep, ignore_errors=True)
        for live, parked in aside:
            if parked.exists():
                #With auto/ gone the core's bootstrap can start recording into
                #a fresh one on ROM load (Super Mario Bros.: auto/audio/.recorder),
                #so whatever the scan's own run left in the slot is dropped.
                shutil.rmtree(live, ignore_errors=True)
                parked.rename(live)
    if not table.is_file():
        raise PrepareError(f"the scan wrote no table\n{proc.stdout[-1200:]}\n{proc.stderr[-800:]}")
    return len(table.read_text().splitlines())


#3x5 digits, one string per digit, one string per row. Small enough to read
#beside a tile and big enough not to be mistaken for the art.
DIGITS = {
    "0": ("111", "101", "101", "101", "111"),
    "1": ("010", "110", "010", "010", "111"),
    "2": ("111", "001", "111", "100", "111"),
    "3": ("111", "001", "111", "001", "111"),
    "4": ("101", "101", "111", "001", "001"),
    "5": ("111", "100", "111", "001", "111"),
    "6": ("111", "100", "111", "101", "111"),
    "7": ("111", "001", "010", "010", "010"),
    "8": ("111", "101", "111", "101", "111"),
    "9": ("111", "101", "111", "001", "111"),
}


def stamp(buf, width, height, x, y, text, rgb, weight=1):
    """`weight` is the pixel size of one stroke: the 3x5 font at 1 px a stroke
    is what four of the 28 sweep runs said they could not read."""
    for ch in text:
        rows = DIGITS.get(ch)
        if rows is None:
            x += 4 * weight
            continue
        for ry, row in enumerate(rows):
            for rx, bit in enumerate(row):
                if bit != "1":
                    continue
                for dy in range(weight):
                    for dx in range(weight):
                        px_, py_ = x + rx * weight + dx, y + ry * weight + dy
                        if 0 <= px_ < width and 0 <= py_ < height:
                            off = (py_ * width + px_) * 4
                            buf[off:off + 4] = bytes(rgb) + b"\xff"
        x += 4 * weight


#A window is only drawn when this share of the frame's non-blank 8x8 blocks
#agree on one offset. Below it the picture would be asserting a scroll it does
#not know, which is worse than the image that says nothing - the whole reason
#this exists is that two sweep runs derived the offset by eye and one built,
#linted and rendered a pack against a row that was never on screen.
WINDOW_AGREEMENT = 0.6
WINDOW_BLOCKS = 8


def visible_window(shot, table):
    """Where on the four-nametable tilemap the frame's top-left sits.

    The screen is a 32x30 window into the nametables, and the dump holds the
    cells it drew in the Tilemap Viewer's coordinates (#421), so the window can
    straddle two or four nametables. Zelda II's 2026-09-19 run picked row 0 of a
    one-nametable dump, built it, linted it, rendered it and got a
    byte-identical frame: that row was 12 rows above the window. A window that
    wraps from the right-hand nametables back to column 0 splits its votes and
    may not be measured.

    Every non-blank 8x8 block of the screen is looked up among the table's tiles,
    at each of the 64 fine-scroll alignments, and votes for the `(col, row)` the
    screen's top-left block would sit at. The winning offset is the window's
    origin. Returns `None` when the vote is not decisive - a frame drawn from
    CHR the dump does not describe has no window to mark."""
    rows = table_rows(table)
    if not rows:
        return None
    at = {}
    blank = set()
    for col, row, doc in rows:
        pixels = tile_pixels(doc)
        if pixels is None:
            continue
        at.setdefault(pixels, []).append((col, row))
        if len(set(pixels[i:i + 3] for i in range(0, 192, 3))) == 1:
            blank.add(pixels)

    img = _repaint.read_png(shot)
    rgb = bytearray()
    for i in range(0, len(img.px), 4):
        rgb += img.px[i:i + 3]
    stride = img.width * 3

    best = None
    for fy in range(8):
        for fx in range(8):
            votes, matched, gy = {}, 0, 0
            gx = 0
            for by in range(fy, img.height - 7, 8):
                base = by * stride + fx * 3
                gx = 0
                for bx in range(0, img.width - 7 - fx, 8):
                    off = base + bx * 3
                    block = b"".join(rgb[off + r * stride:off + r * stride + 24]
                                     for r in range(8))
                    found = at.get(block)
                    if found is not None and block not in blank:
                        matched += 1
                        for col, row in found:
                            key = (col - gx, row - gy)
                            votes[key] = votes.get(key, 0) + 1
                    gx += 1
                gy += 1
            if not votes:
                continue
            (col0, row0), hit = max(votes.items(), key=lambda kv: kv[1])
            if best is None or hit > best["agreeing"]:
                best = {"col": col0, "row": row0, "cols": gx, "rows": gy,
                        "agreeing": hit, "matched": matched,
                        "fine_scroll": f"{fx},{fy}"}
    if best is None or best["agreeing"] < WINDOW_BLOCKS:
        return None
    if best["agreeing"] < WINDOW_AGREEMENT * best["matched"]:
        return None
    return best


#Six screen pixels per tile pixel, so a tile is 48 px and the `col,row` labels
#are drawn 3 px to a stroke. At scale 3 the labels were 3x5 px of single pixels
#and four sweep runs abandoned the image for the copy table; the whole point of
#the picture is that a person can find a shape in it and read its coordinates.
TILEMAP_SCALE = 6


def draw_tilemap(table, png, scale=TILEMAP_SCALE, cell=8, drawn=None, window=None):
    """The viewer's own view, drawn from the table: it cannot be opened here.

    Each cell is the tile's 16 bytes as a 2bpp bitmap - the same bytes the
    `<tile>` key is made of, which is why the table is enough to draw it. Only
    tiles the action answered for are drawn; that is the table's own shape.

    `drawn` is the set of keys `moment_agreement` found on the frame. A tile
    outside it is dimmed and backed in dark red: it is in the nametable and not
    on the screen the evaluator was given - scrolled off, or from the other
    nametable - and picking it costs a whole build-and-render round trip to find
    out (Zelda II, 2026-09-19).

    `window` is `visible_window`'s answer, drawn as a cyan box around the part of
    the tilemap the frame shows. The picture is laid out like the Tilemap
    Viewer's four nametables (#421), so the box is the only thing in it that
    states the scroll."""
    rows = table_rows(table)
    if not rows:
        raise PrepareError(f"{table} holds no parseable cell")

    cols = max(c for c, _, _ in rows) + 1
    lines_ = max(r for _, r, _ in rows) + 1
    px = cell * scale
    width = cols * px + 1
    height = lines_ * px + 1
    buf = bytearray()
    for _ in range(width * height):
        buf += b"\x20\x20\x20\xff"

    def put(x, y, rgb):
        if 0 <= x < width and 0 <= y < height:
            off = (y * width + x) * 4
            buf[off:off + 4] = bytes(rgb) + b"\xff"

    for col, row, doc in rows:
        data = bytes.fromhex(doc["tile"])
        if len(data) != 16:
            continue
        off_frame = drawn is not None and f"{doc['tile']}/{doc['palette']}" not in drawn
        if off_frame:
            for y in range(px):
                for x in range(px):
                    put(col * px + x, row * px + y, (48, 22, 22))
        for y in range(8):
            for x in range(8):
                #NES bitplanes are stored a row at a time, MSB first: pixel x of
                #row y is bit (7 - x) of data[y] and data[8 + y].
                bit = 7 - x
                lo = (data[y] >> bit) & 1
                hi = (data[8 + y] >> bit) & 1
                idx = lo | (hi << 1)
                if idx == 0:
                    continue
                shade = (40, 90, 160, 230)[idx]
                if off_frame:
                    shade //= 3
                for dy in range(scale):
                    for dx in range(scale):
                        put(col * px + x * scale + dx, row * px + y * scale + dy,
                            (shade, shade, shade))
    #A brighter line every fourth tile, and the number itself, because a reader
    #who counts grid squares to find `12,8` is one line off more often than not
    #- the round-1 dispatcher defect was exactly this, in the other direction.
    for n in range(0, cols + 1, 4):
        for y in range(height):
            put(min(n * px, width - 1), y, (150, 150, 150))
    for n in range(0, lines_ + 1, 4):
        for x in range(width):
            put(x, min(n * px, height - 1), (150, 150, 150))
    for x in range(0, width, px):
        for y in range(height):
            put(x, y, (90, 90, 90))
    for y in range(0, height, px):
        for x in range(width):
            put(x, y, (90, 90, 90))
    weight = max(1, scale // 2)
    for n in range(0, cols, 4):
        stamp(buf, width, height, n * px + 3, 3, str(n), (255, 230, 90), weight)
    for n in range(0, lines_, 4):
        stamp(buf, width, height, 3, n * px + 3, str(n), (255, 230, 90), weight)

    #The window last, over the grid and the labels: a line of it that a grid
    #line could cover is a line the reader would not trust.
    if window:
        left = max(0, window["col"]) * px
        top = max(0, window["row"]) * px
        right = min(cols, window["col"] + window["cols"]) * px
        bottom = min(lines_, window["row"] + window["rows"]) * px
        edge = max(2, scale // 2)
        #Only a side the window really ends on gets a line. Contra's 2026-09-19
        #frame starts 22 cells left of this nametable, and a line drawn down its
        #col 0 would read as "the window starts here", which is the mistake the
        #box exists to prevent.
        for t in range(edge):
            for x in range(left, right):
                if window["row"] >= 0:
                    put(x, top + t, (60, 230, 255))
                if window["row"] + window["rows"] <= lines_:
                    put(x, bottom - 1 - t, (60, 230, 255))
            for y in range(top, bottom):
                if window["col"] >= 0:
                    put(left + t, y, (60, 230, 255))
                if window["col"] + window["cols"] <= cols:
                    put(right - 1 - t, y, (60, 230, 255))

    _repaint.write_png(png, _repaint.Image(width, height, buf))
    return cols, lines_, len(rows)


def labels_dump(path):
    return path.is_file() and "Copy as MEP sheet cell" in path.read_text()


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--game", required=True, help="the ROM's stem, e.g. 'Tetris (1989) (Nintendo)'")
    parser.add_argument("--sweep", type=pathlib.Path, default=REPO / "runs/f12.2-sweep")
    parser.add_argument("--sandbox", type=pathlib.Path,
                        default=pathlib.Path.home() / "f12.2-opus-sandbox")
    parser.add_argument("--candidates", default="40,20,60,30,10,50")
    parser.add_argument("--repo-roms", type=pathlib.Path,
                        help="where the sweep's isolated ROM copies live")
    args = parser.parse_args(argv)

    safe = safe_name(args.game)
    romdir = (args.repo_roms or (args.sweep / "roms")) / safe
    rom = romdir / f"{args.game}.nes"
    if not rom.is_file():
        raise PrepareError(f"{rom} does not exist - run the classification pass first")
    pack = args.sweep / "packs" / safe
    if not (pack / "textures/hires.txt").is_file():
        raise PrepareError(f"{pack} is not a recorded pack")

    out = args.sweep / "prepare" / safe
    frames = args.sandbox / "frames"
    frames.mkdir(parents=True, exist_ok=True)
    (args.sandbox / "tilemaps").mkdir(parents=True, exist_ok=True)
    out.mkdir(parents=True, exist_ok=True)

    report = {}
    ranked = choose_frame(rom, pack, out,
                          [int(x) for x in args.candidates.split(",")])
    table = args.sandbox / "tilemap-copy" / f"{safe}.txt"
    table.parent.mkdir(parents=True, exist_ok=True)

    #Mint, scan, then *measure* whether the two pictures are one screen, and
    #take the next-best candidate when they are not. The gate is checked after
    #the scan because the copy table is what the frame is compared against;
    #everything before it is the cheap half.
    agreement, seconds, drawn, state, coverage = None, None, None, None, 0.0
    for attempt, (candidate, rules, _, _) in enumerate(ranked[:MOMENT_TRIES], 1):
        seconds, drawn = candidate, rules
        log("frame", f"{rom.stem} at {seconds}s draws {drawn} background rule(s)")
        state, shot = mint(rom, pack, out, seconds, frames)
        log("mint", f"{state.name} + {shot.name}")
        scan(rom, pack, state, table)
        report["cells"], coverage = dump_coverage(table)
        report["dump_coverage"] = coverage
        log("scan", f"{report['cells']} tile(s) answered in {table.name} "
                    f"({report['cells']}/{FULL_GRID_CELLS} of the frame, {coverage:.1%})")
        screen = render_screen(rom, state, out)
        shutil.copy(screen, frames / f"{safe}-screen.png")
        agreement = moment_agreement(screen, table)
        #`agreement['blocks']`/`explained` match rendered pixel *patterns*, so a
        #handful of rows whose design repeats across the screen (a brick, the
        #blank tile) can score high while the dump itself - `coverage` - names
        #almost none of the frame (see `dump_coverage`). Logged apart, on
        #purpose, so the two are never read as the same claim again.
        log("moment", f"{agreement['blocks']} block(s) of the frame explained by the "
                      f"copy table, {agreement['keys_on_frame']} non-blank key(s) of "
                      f"{agreement['keys_in_table']} on screen, fine scroll "
                      f"{agreement['fine_scroll']}")
        if one_moment(agreement, coverage):
            break
        if attempt < min(MOMENT_TRIES, len(ranked)):
            log("moment", "the tilemap and the frame are not one screen at this "
                          "second - taking the next-best candidate")
    report["frame_seconds"] = seconds
    report["rules_drawn"] = drawn
    report["thin"] = drawn < THIN
    report["state"] = str(state)
    report["moment"] = {k: v for k, v in agreement.items() if k != "drawn"}
    report["capture_drift"] = capture_drift(rom, pack, state, out,
                                            frames / f"{safe}.png")
    log("moment", f"captured screens repaint {report['capture_drift']} pixel(s) of "
                  f"the handed-over frame")
    report["one_moment"] = one_moment(agreement, coverage)
    if report["dump_coverage"] < MOMENT_CELLS:
        log("warn", f"the copy table names only {report['cells']}/{FULL_GRID_CELLS} "
                    f"({coverage:.1%}) of the frame's positions - the copy refused "
                    "the rest (several drawable palettes in the baseline pack, "
                    "none live, or CHR banks that disagree), so the pixel-match 'blocks explained' figure above "
                    "overstates what the evaluator can actually paste from")
    if not report["one_moment"]:
        log("warn", "no candidate second puts this pack's nametable on the screen "
                    "- the panel is handed over with the measurement above, and a "
                    "run that finds nothing to recolour is measuring that")

    window = visible_window(frames / f"{safe}-screen.png", table)
    report["window"] = window
    if window:
        log("window", f"the frame's window into the nametables starts at cell "
                      f"{window['col']},{window['row']} "
                      f"({window['agreeing']} of {window['matched']} non-blank "
                      f"block(s) of the frame agree)")
    else:
        log("warn", "no scroll offset explains the frame - the tilemap is handed "
                    "over without a window box, and the evaluator has only the "
                    "dimming to tell an off-screen tile from an on-screen one")

    cols, lines_, drawn_cells = draw_tilemap(
        table, args.sandbox / "tilemaps" / f"{safe}.png", drawn=agreement["drawn"],
        window=window)
    report["tilemap"] = f"{cols}x{lines_} cells, {drawn_cells} drawn"
    log("tilemap", report["tilemap"])

    work = args.sandbox / "games" / safe
    shutil.rmtree(work, ignore_errors=True)
    shutil.copytree(pack, work)
    report["work_copy"] = str(work)
    log("copy", f"work copy at {work}")

    #The briefing names the goal, never the path - but the paths still have to
    #reach the evaluator, and the 2026-09-19 Fable runs each lost a stop to a
    #path written in prose that did not match the tool. So the dispatcher writes
    #them down, per game, from what it actually produced.
    index = args.sandbox / "index" / f"{safe}.md"
    index.parent.mkdir(parents=True, exist_ok=True)
    index.write_text(
        f"# {rom.stem} - your inputs\n\n"
        "Written by the dispatcher from what it actually produced. Every path is\n"
        "absolute and correct as of the run that wrote this file.\n\n"
        f"- ROM: `{rom}`\n"
        f"- Frame, as the player sees it: `{frames / (safe + '.png')}`\n"
        f"- The same frame with the pack's texture layer off, which is the screen "
        f"the tilemap and the copy table describe: "
        f"`{frames / (safe + '-screen.png')}`. The pack's captured screens repaint "
        f"{report['capture_drift']} pixel(s) of the frame above; where the two "
        f"pictures differ, this one is the moment.\n"
        f"- That frame as a save state: `{state}`\n"
        f"- The same frame's tilemap: `{args.sandbox / 'tilemaps' / (safe + '.png')}`\n"
        f"- What the copy action returns per tile: `{table}`\n"
        f"- Sheet-cell work copy: `{work}`\n"
        f"- The pack folder beside the ROM, which the render step reads: "
        f"`{rom.parent / rom.stem}`\n"
        f"- Reference frame: {seconds} s of the recording, drawing {drawn} of the "
        f"pack's background tile rules\n"
        f"- One screen, measured: the copy table explains {agreement['blocks']} of "
        f"the frame's 8x8 blocks and {agreement['keys_on_frame']} of its "
        f"{agreement['keys_in_table']} keys are non-blank shapes on that frame. "
        f"A tile dimmed and backed in dark red on the tilemap image is in the "
        f"nametable and **not** on this screen - do not pick one.\n"
        f"- Coverage: the copy table itself names {report['cells']} of the frame's "
        f"{FULL_GRID_CELLS} positions ({report['dump_coverage']:.1%}). This is a "
        f"different number from the 'blocks explained' line above, on purpose - "
        f"that one counts a *pixel design* wherever it repeats (a brick, the blank "
        f"tile, tiled across the whole screen), so it can read high while this "
        f"count, the one the tilemap picture and the dump file actually cover, "
        f"stays small where the copy refuses most of the frame. Trust this number for what you can "
        f"paste; the tilemap picture is `{cols}x{lines_}` cells wide/tall for the "
        f"same reason.\n"
        + (f"- The visible window: the tilemap is laid out like the emulator's four "
           f"nametables (columns 0-63, rows 0-59) and the screen is a "
           f"{window['cols']}x{window['rows']} window into them. The cyan box on "
           f"the tilemap is the part the frame shows; its top-left is cell "
           f"`{window['col']},{window['row']}`. Pick inside the box.\n"
           if window else
           "- The visible window could not be measured on this frame, so the "
           "tilemap carries no box. It is laid out like the emulator's four "
           "nametables (columns 0-63, rows 0-59) and holds only the cells this "
           "frame drew, so every tile on it that is not dimmed is on your "
           "screen.\n"),
        encoding="utf-8")
    report["index"] = str(index)
    log("index", str(index))

    labels = args.sandbox / "tilemap-menu-labels.txt"
    report["labels_present"] = labels_dump(labels)
    if not report["labels_present"]:
        log("warn", f"{labels} is missing or lacks the sheet-cell entry")

    (out / "report.json").write_text(json.dumps(report, indent=2) + "\n")
    for key, value in report.items():
        log("report", f"{key}: {value}")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except PrepareError as ex:
        log("FAIL", str(ex))
        sys.exit(1)
