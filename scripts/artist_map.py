#!/usr/bin/env python3
"""artist_map — stage panoramas from a recording, and the way back (artist kit, part `map`).

A fan remaster paints a whole stage as one long strip: the "Contra80s 1.1" pack
ships `Stage1a.png` at 6696x480 (`<scale>2`, i.e. 3348x240 logical — the entire
stage-1 scroll in one image) and `Stage3-Ground-v1b3.png` at 512x4360 for the
vertical waterfall. A recorded pack has nothing of the sort: it emits at most a
handful of 256x240 `textures/backgrounds/screenNNN.png` captures, and a stage
that scrolls cannot be repainted from those. This tool closes that gap:

    scripts/artist_map.py --out DIR --stage NAME --dump grid.txt --pack PACK
                          [--stage ... --dump ... --pack ...]
                          [--scale N] [--names names.json] [--quiet]
    scripts/artist_map.py --slice painted.png --map DIR/map/NAME-000.json
                          --out DIR [--quiet]

Per stage it writes, under `<out>/map/`:

  * `<stage>-NNN.png`     the panorama, one image per stitched region;
  * `<stage>-NNN.orig.png` the untouched 1x reference twin (ADR-0153 §3);
  * `<stage>-NNN.json`     an ADR-0153 v1 sheet sidecar whose `cells[]` name,
    for every 8x8 cell of the panorama, its pixel position and the
    `(tileData, palette)` key the pack's `hires.txt` uses for that tile. The
    panorama is therefore addressable *and* a drop-in `textures/sheets/` sheet:
    copy the three files into a pack and `scripts/mep_build.py build` slices
    them back into `<tile>` rules with no extra machinery.

and one manifest fragment `kit-part-map.json` for the kit assembler.

## Where the panorama comes from

No nametable dump exists and none is needed. The recorder already keeps, per
retained frame, the 32x30 grid of 8x8 background cells that was on screen
(ADR-0153 §5, `MesenSheets::GridFrame`), and `MESEN_SHEET_GRID_DUMP=<file>`
writes that stream out — cells are aligned to the frame's fine x scroll, so a
cell always sits on the world's 8px tile lattice. What the dump does *not*
carry is where the camera was. That is recovered the way
`Core/NES/HdPacks/ScreenStitcher.cpp` recovers it: the best whole-cell shift
between consecutive frames, accumulated. On Contra the recovery is exact — the
camera moves 1px per frame, the dump's fine x counts 7,6,5,...,0 and the shift
lands on +1 exactly when it wraps.

So: record with the dump on, and point this tool at the dump and at the pack
the same run produced.

    MESEN_SHEET_GRID_DUMP=$PWD/grid.txt scripts/headless_record <rom> 60 out \\
        bootstrap hdpack-off input=scripts/stages/contra/stage1-run.txt \\
        state=stage1-run.mss

The dump's line kinds are `F <n>` (a frame, repeated once per collapsed
duplicate), `K <shape> <32 hex tile data> <8 hex palette>`, `P <id> <8 hex
palette>` and `<x> <y> <shape> <palette id>`.

## What it cannot do (read this before trusting a panorama)

  * **Palette is a record, but only on a dump that has the plane.** The grid
    stream keys cells by a palette-agnostic shape (`HdPpuTileInfo::GetKey(true)`)
    and a `K` line can only name the colours a shape was *first* drawn with, so
    a tile a bank switch recoloured used to land on the panorama under the wrong
    palette. F9.24 writes the `GridFrame::Palettes` plane the recorder already
    kept (ADR-0159 amendment) as a fourth field per cell plus `P` lines; this
    tool reads it and places every cell under the colours it really had. A dump
    from an older build has no plane: it still parses, every cell falls back to
    the shape's first-seen palette, and each fall-back whose tile data has rival
    palettes in the pack is marked `"paletteAttributed": true` and makes its
    file `seen: false`.
  * **CHR ROM games are out of scope.** Their `hires.txt` keys by CHR index
    (ADR-0172) and the dump does not carry the index. The tool refuses such a
    pack rather than emitting a panorama that matches nothing.
  * **Coverage is what was walked.** A panorama is exactly as long as the
    recording scrolled; nothing is filled in or guessed.

Python 3, standard library only. PNG decoding is `mep_build._png_pixels` (the
tree's single decoder) and the `Image`/`write_png` pair is `sheet_repaint`'s.

Exit codes: 0 = wrote a kit part, 1 = nothing to do or a fatal error,
2 = usage error.
"""

import argparse
import json
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import asset_names as N  # noqa: E402 — the F12.4 painting-surface name contract
import mep_build  # noqa: E402  — the tree's single PNG decoder
import sheet_repaint  # noqa: E402  — Image / read_png / write_png

Image = sheet_repaint.Image
read_png = sheet_repaint.read_png
write_png = sheet_repaint.write_png

# The recorded grid is the NES screen in 8x8 cells (MesenSheets::kGridRows/Cols).
ROWS, COLS = 30, 32
EMPTY = -1
CELL = 8

# 2C02, the same table as Core/NES/NesDefaultVideoFilter.cpp (copied rather
# than imported: scripts/mep_compare.py owns it but needs numpy and Pillow).
_PAL = [0x666666, 0x002A88, 0x1412A7, 0x3B00A4, 0x5C007E, 0x6E0040, 0x6C0600, 0x561D00,
        0x333500, 0x0B4800, 0x005200, 0x004F08, 0x00404D, 0x000000, 0x000000, 0x000000,
        0xADADAD, 0x155FD9, 0x4240FF, 0x7527FE, 0xA01ACC, 0xB71E7B, 0xB53120, 0x994E00,
        0x6B6D00, 0x388700, 0x0C9300, 0x008F32, 0x007C8D, 0x000000, 0x000000, 0x000000,
        0xFFFEFF, 0x64B0FF, 0x9290FF, 0xC676FF, 0xF36AFF, 0xFE6ECC, 0xFE8170, 0xEA9E22,
        0xBCBE00, 0x88D800, 0x5CE430, 0x45E082, 0x48CDDE, 0x4F4F4F, 0x000000, 0x000000,
        0xFFFEFF, 0xC0DFFF, 0xD3D2FF, 0xE8C8FF, 0xFBC2FF, 0xFEC4EA, 0xFECCC5, 0xF7D8A5,
        0xE4E594, 0xCFEF96, 0xBDF4AB, 0xB3F3CC, 0xB5EBF2, 0xB8B8B8, 0x000000, 0x000000]
NES_PALETTE = [((c >> 16) & 255, (c >> 8) & 255, c & 255) for c in _PAL]

_TILE_RE = re.compile(r"^(\[[^\]]*\])?<tile>(.*)$")
_IMG_RE = re.compile(r"^<img>(.*)$")
_SCALE_RE = re.compile(r"^<scale>(\d+)")

# A step whose best shift scores below this is not the same place any more, and
# starts a new region instead of welding two places together (ADR-0153 §6's cut
# rule, same constant as ScreenStitcher's kMinMatch).
MIN_MATCH = 0.5
# Once a candidate shift scores this well there is nothing better to find, so
# the search stops. Keeps a 3600-frame recording to a couple of seconds.
GOOD_ENOUGH = 0.97
# Search window. A camera never moves half a screen between two retained frames.
MAX_DX, MAX_DY = 12, 8
# Below this many comparable cells a frame is a fade, a menu or a blank and
# says nothing about where the camera is.
MIN_CELLS = 100
# A region has to be bigger than the screen in at least one axis to be worth a
# file: a one-screen "panorama" is a copy of the backgrounds/screenNNN.png the
# recorder already writes (ADR-0153 6), and it tells an artist nothing. Contra's
# base stages (2 and 4) do not scroll at all and are correctly refused here.
MIN_REGION_CELLS = COLS + 4


class MapError(Exception):
    pass


# --- the recorded grid stream ----------------------------------------------


# The palette id the recorder writes when its id space ran out: "no evidence"
# (MesenSheets::kUnknownPalette).
UNKNOWN_PALETTE = 0xFF


class GridFrame:
    __slots__ = ("index", "repeat", "fine", "rows", "pals")

    def __init__(self, index):
        self.index = index
        self.repeat = 1
        self.fine = 0
        self.rows = [[EMPTY] * COLS for _ in range(ROWS)]
        # The ADR-0159 palette plane, parallel to `rows`. Shape ids wildcard
        # the palette, so this is the only record of the colours a cell really
        # had; a dump written before F9.24 has no fourth field and every cell
        # stays UNKNOWN_PALETTE, which reads as "fall back to the shape's own".
        self.pals = [[UNKNOWN_PALETTE] * COLS for _ in range(ROWS)]


def parse_grid_dump(path: Path):
    """(frames, shapes, palettes) from a MESEN_SHEET_GRID_DUMP file.

    Five line kinds (HdPackBuilder::WriteGridDump): `F <n>` opens a frame and is
    repeated once per collapsed duplicate, `K <id> <32 hex tile data> <8 hex
    palette>` interns a shape the first time it is drawn, `P <id> <8 hex
    palette>` interns a palette word, `M <4096 hex>` carries the frame's
    internal RAM (F12.6b, read by mep_conditions.py and skipped here), and
    `<x> <y> <shape> [<palette id>]` places a cell. `x` is `col * 8 + fineX`, so `x & 7` recovers the frame's
    fine scroll and `(x - fineX) // 8` its column.

    The fourth cell field and the `P` lines are F9.24's palette plane. A dump
    written by an older build has neither; it parses unchanged and every cell
    falls back to the shape's own first-seen palette."""
    frames = []
    shapes = {}
    palettes = {}
    cur = None
    last = None
    with path.open("r", encoding="utf-8", errors="replace") as fh:
        for line in fh:
            head = line[0]
            if head == "F":
                n = int(line[2:])
                if last is not None and n == last:
                    cur.repeat += 1
                    continue
                cur = GridFrame(n)
                last = n
                frames.append(cur)
            elif head == "K":
                parts = line.split()
                shapes[int(parts[1])] = (parts[2].upper(), parts[3].upper())
            elif head == "P":
                parts = line.split()
                palettes[int(parts[1])] = parts[2].upper()
            elif head == "M":
                # F12.6b (ADR-0197 §3): the retained RAM window of the frame.
                # A map is drawn from tiles, not from memory, so this reader
                # skips it — but it must skip it *by name*, or the line falls
                # through to the cell parser and the whole dump fails.
                continue
            elif cur is not None:
                parts = line.split()
                x = int(parts[0])
                fine = x & 7
                cur.fine = fine
                col = (x - fine) // CELL
                row = int(parts[1]) // CELL
                if 0 <= row < ROWS and 0 <= col < COLS:
                    cur.rows[row][col] = int(parts[2])
                    if len(parts) > 3:
                        cur.pals[row][col] = int(parts[3])
    if not frames:
        raise MapError(f"{path}: no grid frames — was MESEN_SHEET_GRID_DUMP set on a run that reached gameplay?")
    return frames, shapes, palettes


# --- stitching --------------------------------------------------------------


def score_shift(a: GridFrame, b: GridFrame, dx: int, dy: int, rows):
    """Agreement of `b(c, r)` with `a(c + dx, r + dy)` over `rows`, counting
    only cells where at least one side is drawn. Returns (score, compared)."""
    hit = 0
    tot = 0
    arows = a.rows
    brows = b.rows
    for r in rows:
        ra = r + dy
        if ra < 0 or ra >= ROWS:
            continue
        ar = arows[ra]
        br = brows[r]
        if dx >= 0:
            seg_a = ar[dx:]
            seg_b = br[:COLS - dx]
        else:
            seg_a = ar[:COLS + dx]
            seg_b = br[-dx:]
        for u, v in zip(seg_a, seg_b):
            if u == EMPTY and v == EMPTY:
                continue
            tot += 1
            if u == v:
                hit += 1
    return (hit / tot if tot else 0.0), tot


def _candidates(prev_dx, prev_dy):
    """Shifts to try, nearest to the last accepted one first. A camera keeps
    doing what it was doing, so the first or second candidate almost always
    wins and the full window is never walked."""
    seen = set()
    order = []
    for radius in range(0, max(MAX_DX, MAX_DY) + 1):
        for dy in range(prev_dy - radius, prev_dy + radius + 1):
            for dx in range(prev_dx - radius, prev_dx + radius + 1):
                if abs(dx) > MAX_DX or abs(dy) > MAX_DY:
                    continue
                if max(abs(dx - prev_dx), abs(dy - prev_dy)) != radius:
                    continue
                if (dx, dy) in seen:
                    continue
                seen.add((dx, dy))
                order.append((dx, dy))
    return order


def best_shift(a: GridFrame, b: GridFrame, rows, prev_dx=0, prev_dy=0):
    best = (0, 0, -1.0)
    for dx, dy in _candidates(prev_dx, prev_dy):
        s, tot = score_shift(a, b, dx, dy, rows)
        if tot < MIN_CELLS:
            continue
        if s > best[2]:
            best = (dx, dy, s)
            if s >= GOOD_ENOUGH:
                break
    return best


def _row_is_flat(frame: GridFrame, r: int) -> bool:
    """True when row `r` carries no directional evidence at all: every drawn
    cell in it is the same shape (the common case is a row of the blank tile,
    which is a shape like any other — only an undrawn cell is EMPTY).

    Such a row matches itself under *every* shift, so the unshifted and the
    shifted comparison can only be separated by where the drawn/EMPTY boundary
    falls. On a 32-column row that is worth one cell out of 32, which is how
    Castlevania's blank margin above its status bar scored 31/32 unshifted
    against a perfect 1.0 shifted and voted "moving" (issue #221)."""
    seen = None
    for v in frame.rows[r]:
        if v == EMPTY:
            continue
        if seen is None:
            seen = v
        elif v != seen:
            return False
    return True


def _leading_band(verdict):
    """How many rows the screen-fixed band at the start of `verdict` covers.

    The band runs over rows that voted "fixed" *and* over rows that abstained
    (`None`), but it always ends on a fixed row. So a blank margin is absorbed
    into a HUD that sits below it, and is left in the panorama when there is no
    such HUD underneath — a row with no evidence never invents a band, it only
    stops breaking one."""
    n = 0
    end = 0
    while n < len(verdict) and verdict[n] is not False:
        n += 1
        if verdict[n - 1]:
            end = n
    return end


def hud_bands(frames, rows_all):
    """(top, bottom): the screen-fixed row bands, found by asking each row
    whether it agrees better with "the camera did not move" than with the shift
    the rest of the screen claims. A HUD is the only thing that answers yes on
    every scrolling step, and it must not be smeared across the panorama.

    Only a *contiguous* band at the top and at the bottom is honoured, the same
    shape the recorder's own `HudRows`/`HudBottomRows` have, so a row of sky
    that happens to be uniform never punches a hole in the middle of a stage.
    A row that answered neither way — it abstained on every step, or its votes
    tied — is contiguous with either side rather than "moving" (see
    `_leading_band`)."""
    fixed = [0] * ROWS
    moving = [0] * ROWS
    steps = 0
    prev_dx = 0
    for i in range(1, len(frames)):
        dx, _dy, s = best_shift(frames[i - 1], frames[i], rows_all, prev_dx, 0)
        if s < MIN_MATCH or dx == 0:
            continue
        prev_dx = dx
        steps += 1
        if steps > 400:
            break
        for r in range(ROWS):
            if _row_is_flat(frames[i - 1], r) and _row_is_flat(frames[i], r):
                continue
            s0, t0 = score_shift(frames[i - 1], frames[i], 0, 0, (r,))
            sd, td = score_shift(frames[i - 1], frames[i], dx, 0, (r,))
            if t0 < 4 or td < 4:
                continue
            if s0 > sd:
                fixed[r] += 1
            elif sd > s0:
                moving[r] += 1
    if not steps:
        return 0, 0
    # Tri-state: a row only votes when it saw evidence, so a tie — including
    # the 0-0 of a row that abstained on every step — is `None`, "no answer",
    # and not the "moving" the two-way test used to read it as.
    verdict = [None if fixed[r] == moving[r] else fixed[r] > moving[r] for r in range(ROWS)]
    top = _leading_band(verdict)
    bottom = _leading_band(verdict[::-1][:ROWS - top])
    return top, bottom


class Region:
    """One stitched stretch of world: every (world row, world col) the camera
    covered, and how often each `(shape, palette)` was seen there.

    Keeping the tally rather than the first sighting is what makes the strip
    look like the stage. Measured on Contra's waterfall (2026-09-13, 8704
    positions): the *first* variant a position is seen with holds for a median
    0.02 of that position's sightings, the most-seen one for 0.98 — the player
    enters a scrolling stage, so the frame that first covers a world position is
    almost always a transitional one. Both are evidence; the majority is the
    one an artist recognises as the stage."""

    def __init__(self):
        self.cells = {}
        self.frames = 0

    def see(self, key, variant):
        tally = self.cells.get(key)
        if tally is None:
            self.cells[key] = {variant: 1}
        else:
            tally[variant] = tally.get(variant, 0) + 1

    def clear(self):
        self.cells.clear()
        self.frames = 0

    def winner(self, key):
        """The most-seen variant at a position; ties go to the first sighting,
        since dicts keep insertion order."""
        best = None
        best_n = -1
        for variant, n in self.cells[key].items():
            if n > best_n:
                best, best_n = variant, n
        return best

    @property
    def conflicting(self):
        """Positions that ever showed more than one thing — a door that opened,
        a wall that was destroyed, water that animates."""
        return [k for k, t in self.cells.items() if len(t) > 1]

    @property
    def agreements(self):
        """Re-sightings that confirmed the variant the panorama kept."""
        total = 0
        for k, t in self.cells.items():
            total += max(t.values()) - 1
        return total

    @property
    def conflicts(self):
        """Sightings that disagreed with the variant the panorama kept."""
        total = 0
        for t in self.cells.values():
            total += sum(t.values()) - max(t.values())
        return total

    @property
    def bounds(self):
        ys = [k[0] for k in self.cells]
        xs = [k[1] for k in self.cells]
        return min(ys), min(xs), max(ys), max(xs)


def stitch(frames, hud_top, hud_bottom, quiet=False):
    """Accumulate the per-step shift into world coordinates and drop every
    drawn playfield cell at the world position it was seen at. A step whose
    best shift cannot beat MIN_MATCH is a cut: it starts a new region rather
    than leaving a gap in the old one (ADR-0153 §6).

    De-duplication is by world position, never by frame, so a stretch the
    player walked twice is written once. What a position keeps is the variant
    it was seen with most often (Region); every sighting that disagreed is
    counted, never silently dropped."""
    rows = tuple(range(hud_top, ROWS - hud_bottom))
    regions = []
    cur = Region()
    regions.append(cur)
    ox = oy = 0
    prev_dx = prev_dy = 0

    def place(frame):
        cur.frames += 1
        for r in rows:
            row = frame.rows[r]
            pals = frame.pals[r]
            wy = oy + r
            for c in range(COLS):
                sid = row[c]
                if sid == EMPTY:
                    continue
                # A cell is its drawing *and* its colours: a tile a bank switch
                # recoloured is a different piece of scenery, and collapsing the
                # two would put one of them on the panorama under the other's
                # palette (ADR-0159 amendment).
                cur.see((wy, ox + c), (sid, pals[c]))

    place(frames[0])
    for i in range(1, len(frames)):
        dx, dy, s = best_shift(frames[i - 1], frames[i], rows, prev_dx, prev_dy)
        if s < MIN_MATCH:
            if len(cur.cells) >= MIN_REGION_CELLS:
                cur = Region()
                regions.append(cur)
            else:
                cur.clear()
            ox = oy = 0
            prev_dx = prev_dy = 0
            place(frames[i])
            continue
        prev_dx, prev_dy = dx, dy
        ox += dx
        oy += dy
        place(frames[i])
    kept = []
    for r in regions:
        if len(r.cells) < MIN_REGION_CELLS:
            continue
        y0, x0, y1, x1 = r.bounds
        if (x1 - x0 + 1) <= COLS and (y1 - y0 + 1) <= ROWS:
            # Never scrolled: the recorder's own screen capture already is this.
            continue
        kept.append(r)
    return kept


# --- the pack the recording produced ---------------------------------------


class Pack:
    """The recorded pack's tile art, addressed by `(tileData, palette)`.

    Art is lifted from the pack's own `chr/Chr_N.orig.png` — the F5.4d 1:1
    reference twin, which holds the tile nearest-upscaled to `<scale>` — so a
    panorama cell is the pack's own pixels, not a re-render that might differ
    by a rounding rule."""

    def __init__(self, folder: Path):
        self.folder = folder
        manifest = folder / "textures" / "hires.txt"
        if not manifest.is_file():
            raise MapError(f"{folder}: no textures/hires.txt — this is not a recorded pack")
        self.scale = 1
        self.imgs = []
        self.tiles = {}
        self.index_keyed = False
        for raw in manifest.read_text(encoding="utf-8", errors="replace").splitlines():
            line = raw.strip()
            m = _SCALE_RE.match(line)
            if m:
                self.scale = int(m.group(1))
                continue
            m = _IMG_RE.match(line)
            if m:
                self.imgs.append(m.group(1).strip())
                continue
            m = _TILE_RE.match(line)
            if not m:
                continue
            f = [t.strip() for t in m.group(2).split(",")]
            if len(f) < 6:
                continue
            if len(f[1]) < 32:
                self.index_keyed = True
                continue
            self.tiles.setdefault((f[1].upper(), f[2].upper()), (int(f[0]), int(f[3]), int(f[4])))
        if self.index_keyed:
            raise MapError(
                f"{folder}: this pack keys its tiles by CHR index (ADR-0172) and the grid dump "
                "carries no index — a panorama built from it would match nothing. "
                "CHR ROM games need a recorder change before this tool can serve them.")
        if not self.tiles:
            raise MapError(f"{folder}: textures/hires.txt has no <tile> entries")
        self._sheets = {}
        self._crops = {}

    @property
    def keys(self):
        return set(self.tiles)

    def _sheet(self, idx: int) -> Image:
        img = self._sheets.get(idx)
        if img is not None:
            return img
        rel = self.imgs[idx] if idx < len(self.imgs) else None
        if rel is None:
            raise MapError(f"{self.folder}: <tile> names image #{idx}, which has no <img> line")
        base = self.folder / "textures" / rel
        ref = base.with_name(base.name[:-4] + ".orig.png")
        path = ref if ref.is_file() else base
        img = read_png(path)
        self._sheets[idx] = img
        return img

    def crop_1x(self, key):
        """The tile's 8x8 art at 1x as an Image, or None when the pack has no
        such key."""
        cached = self._crops.get(key)
        if cached is not None:
            return cached
        where = self.tiles.get(key)
        if where is None:
            return None
        idx, x, y = where
        sheet = self._sheet(idx)
        n = self.scale
        out = Image(CELL, CELL)
        for j in range(CELL):
            sy = y + j * n
            for i in range(CELL):
                src = sheet.offset(x + i * n, sy)
                dst = out.offset(i, j)
                out.px[dst:dst + 4] = sheet.px[src:src + 4]
        self._crops[key] = out
        return out


def render_tile(tile_hex: str, pal_hex: str) -> Image:
    """An 8x8 NES tile from its 16 bytes of 2bpp data and its four palette
    indexes — the fallback for a key the pack never wrote art for."""
    data = bytes.fromhex(tile_hex)
    colors = [NES_PALETTE[int(pal_hex[i:i + 2], 16) & 0x3F] for i in range(0, 8, 2)]
    img = Image(CELL, CELL)
    for y in range(CELL):
        lo, hi = data[y], data[y + 8]
        for x in range(CELL):
            bit = 7 - x
            r, g, b = colors[((lo >> bit) & 1) | (((hi >> bit) & 1) << 1)]
            off = img.offset(x, y)
            img.px[off:off + 4] = bytes((r, g, b, 0xFF))
    return img


# --- emitting ---------------------------------------------------------------


def multi_palette_shapes(pack: Pack, shapes, used):
    """Shape ids whose colour is an attribution rather than a record.

    The dump interns a shape palette-agnostically and prints only the first
    palette it was seen with, so a tile the game drew under two palettes lands
    in the panorama under one of them. The candidates are the tile data the
    pack holds under more than one palette — but `hires.txt` is the whole ROM's
    tiles, sprites included, and a background tile that happens to share its
    16 bytes with a sprite tile is not ambiguous *on the background*. So a
    palette only counts when some cell of this recording's own background
    actually used it (`used`), which is what keeps the figure a measurement
    rather than an upper bound."""
    by_data = {}
    for data, pal in pack.tiles:
        if pal in used:
            by_data.setdefault(data, set()).add(pal)
    return {sid for sid, (data, _pal) in shapes.items() if len(by_data.get(data, ())) > 1}


def build_panorama(region: Region, shapes, palettes, pack: Pack, scale: int):
    """(image, orig, cells, stats) for one region."""
    y0, x0, y1, x1 = region.bounds
    w = (x1 - x0 + 1) * CELL
    h = (y1 - y0 + 1) * CELL
    orig = Image(w, h)
    cells = []
    chosen = {k: region.winner(k) for k in region.cells}
    used = {shapes[sid][1] for sid, _pid in chosen.values() if sid in shapes}
    ambiguous_ids = multi_palette_shapes(pack, shapes, used)
    stats = {"cells": 0, "from_pack": 0, "rendered": 0, "ambiguous": 0, "unknown_shape": 0}
    for (wy, wx), (sid, pid) in sorted(chosen.items()):
        key = shapes.get(sid)
        if key is None:
            stats["unknown_shape"] += 1
            continue
        # The palette plane is a record; the shape's own palette is only the
        # first one it was ever drawn with, so falling back to it is an
        # attribution and is marked as one.
        attributed = pid not in palettes
        if not attributed:
            key = (key[0], palettes[pid])
        art = pack.crop_1x(key)
        if art is None:
            art = render_tile(*key)
            stats["rendered"] += 1
        else:
            stats["from_pack"] += 1
        px, py = (wx - x0) * CELL, (wy - y0) * CELL
        orig.paste(art, px, py)
        cell = {"index": len(cells), "x": px, "y": py,
                "tiles": [{"tile": key[0], "palette": key[1]}]}
        if attributed and sid in ambiguous_ids:
            cell["paletteAttributed"] = True
            stats["ambiguous"] += 1
        cells.append(cell)
        stats["cells"] += 1
    painted = orig.upscale(scale) if scale > 1 else orig.clone()
    return painted, orig, cells, stats


def sidecar(name: str, cells, columns: int, scale: int, mode: str, stats, region: Region,
            pack_scale: int = 1):
    return {
        "version": 1,
        # An ADR-0153 v1 sheet kind mep_build.py already slices through
        # `cells[]`; "map" is reserved for the recorder's own stitched maps,
        # which resolve through metatiles.json instead.
        "kind": "misc",
        "gridUnit": CELL,
        "gridPhase": {"x": 0, "y": 0},
        "hasGrid": True,
        "cell": {"w": CELL, "h": CELL},
        "gutter": 0,
        "columns": columns,
        "routedCells": 0,
        "sheet": f"{name}.png",
        "reference": f"{name}.orig.png",
        "cells": cells,
        # Everything below is artist_map's own, ignored by mep_build.
        "panorama": {
            "generator": "scripts/artist_map.py",
            "scale": scale,
            # The pack's own <scale>. Every sheet of a pack shares one
            # (MEP-v1 2.1), so a sliced panorama must come out at this size
            # whatever size the artist chose to paint at. --slice reads it.
            "packScale": pack_scale,
            "orientation": mode,
            "framesStitched": region.frames,
            "positionsConfirmed": region.agreements,
            "positionsDisagreeing": len(region.conflicting),
            "sightingsDisagreeing": region.conflicts,
            "cellsFromPackArt": stats["from_pack"],
            "cellsRenderedFromKey": stats["rendered"],
            "cellsPaletteAttributed": stats["ambiguous"],
        },
    }


def orientation_of(region: Region) -> str:
    y0, x0, y1, x1 = region.bounds
    return "horizontal" if (x1 - x0) >= (y1 - y0) else "vertical"


def title_for(names, stage: str, idx: int, region: Region, w: int, h: int) -> str:
    """A caption, never an id, and never invented: `--names` may name a stage,
    otherwise the title is the stage id and the measurement."""
    named = None
    if names:
        stages = names.get("stages")
        if isinstance(stages, dict):
            entry = stages.get(stage)
            if isinstance(entry, dict):
                named = entry.get("name")
            elif isinstance(entry, str):
                named = entry
    what = named or stage
    return f"{what} — {orientation_of(region)} panorama, {w}x{h} at 1x, region {idx}"


# --- the slicer -------------------------------------------------------------


def cut_painted(map_json: Path, painted_png: Path, out_dir: Path, quiet=False):
    """Cut a painted panorama back into per-tile art and write the drop-in
    sheet a recorded pack needs.

    One tile key sits at many panorama positions, and an artist may well paint
    two of them differently — a brick under a shadow and the same brick in the
    open are one `(tileData, palette)` key and the pack can hold exactly one
    art for it. The rule is **first occurrence wins**, reading the panorama in
    (y, x) order, and every later position whose art disagrees is reported by
    position. Nothing is averaged and nothing is silently dropped."""
    doc = json.loads(map_json.read_text(encoding="utf-8"))
    cells = doc.get("cells") or []
    if not cells:
        raise MapError(f"{map_json}: no cells[] to slice")
    orig_path = map_json.with_name(str(doc.get("reference") or (map_json.stem + ".orig.png")))
    if not orig_path.is_file():
        raise MapError(f"{orig_path}: the untouched reference twin is missing; it is what says which cells were painted")
    painted = read_png(painted_png)
    orig = read_png(orig_path)
    if painted.width % orig.width or painted.height % orig.height \
            or painted.width // orig.width != painted.height // orig.height:
        raise MapError(
            f"{painted_png}: {painted.width}x{painted.height} is not a whole-factor upscale of the "
            f"{orig.width}x{orig.height} panorama — resize by 1x, 2x, 3x, ...")
    n = painted.width // orig.width

    # Every sheet of a pack shares one <scale> (MEP-v1 2.1), so the sheet this
    # writes has to come out at the pack's scale whatever scale the artist
    # chose to paint at. The panorama is emitted at 1x on purpose - it is a
    # picture of a stage, and an artist wants to paint it at its own size -
    # so painting it at 1x and dropping the result into a 4x pack used to fail
    # the build with "painted at 1x while metatiles.png is at 4x". Measured by
    # following ARTIST.md literally; the instructions were not wrong about what
    # to run, they were wrong that any whole multiple would do.
    pack_scale = int((doc.get("panorama") or {}).get("packScale") or n)
    if pack_scale % n:
        raise MapError(
            f"{painted_png}: painted at {n}x, but the pack this panorama came from is at "
            f"{pack_scale}x and {n} does not divide {pack_scale} — paint at 1x, or at a "
            f"whole factor of {pack_scale}x")
    grow = pack_scale // n
    span = CELL * pack_scale

    winners = {}
    order = []
    conflicts = []
    unpainted = 0
    for c in sorted(cells, key=lambda c: (c.get("y", 0), c.get("x", 0))):
        tiles = c.get("tiles") or []
        if not tiles:
            continue
        key = (str(tiles[0].get("tile", "")).upper(), str(tiles[0].get("palette", "")).upper())
        x, y = int(c["x"]), int(c["y"])
        art = painted.crop(x * n, y * n, CELL * n, CELL * n)
        ref = orig.crop(x, y, CELL, CELL).upscale(n)
        touched = art.px != ref.px
        if not touched:
            unpainted += 1
        if key not in winners:
            winners[key] = (art, ref, (x, y), touched)
            order.append(key)
        elif winners[key][0].px != art.px:
            conflicts.append({"tile": key[0], "palette": key[1],
                              "winner": {"x": winners[key][2][0], "y": winners[key][2][1]},
                              "disagrees": {"x": x, "y": y},
                              # Both painted, and differently: a real artistic
                              # disagreement the pack cannot express. Only one
                              # painted: the artist repainted a single instance
                              # of a key that sits in many places, and the
                              # winner's art will show at all of them.
                              "bothPainted": bool(touched and winners[key][3])})

    columns = 16
    rows = (len(order) + columns - 1) // columns
    sheet = Image(columns * span, rows * span)
    ref_sheet = Image(columns * CELL, rows * CELL)
    out_cells = []
    for i, key in enumerate(order):
        art, ref, _where, _touched = winners[key]
        cx, cy = (i % columns) * span, (i // columns) * span
        sheet.paste(art.upscale(grow) if grow > 1 else art, cx, cy)
        small = Image(CELL, CELL)
        for j in range(CELL):
            for k in range(CELL):
                src = ref.offset(k * n, j * n)
                dst = small.offset(k, j)
                small.px[dst:dst + 4] = ref.px[src:src + 4]
        ref_sheet.paste(small, (i % columns) * CELL, (i // columns) * CELL)
        out_cells.append({"index": i, "x": (i % columns) * CELL, "y": (i // columns) * CELL,
                          "tiles": [{"tile": key[0], "palette": key[1]}]})

    name = N.require_asset_name(
        "pano-" + N.sanitize_asset_stem(map_json.stem, fallback="map") + N.SURFACE_EXT,
        "artist_map.py slice")[:-len(N.SURFACE_EXT)]
    sheets = out_dir / "sheets"
    sheets.mkdir(parents=True, exist_ok=True)
    write_png(sheets / f"{name}.png", sheet)
    write_png(sheets / f"{name}.orig.png", ref_sheet)
    (sheets / f"{name}.json").write_text(json.dumps({
        "version": 1, "kind": "misc", "gridUnit": CELL, "gridPhase": {"x": 0, "y": 0},
        "hasGrid": True, "cell": {"w": CELL, "h": CELL}, "gutter": 0, "columns": columns,
        "routedCells": 0, "sheet": f"{name}.png", "reference": f"{name}.orig.png",
        "cells": out_cells,
    }, indent=1) + "\n", encoding="utf-8")

    if not quiet:
        grown = f", written at the pack's {pack_scale}x" if grow > 1 else ""
        print(f"slice: {len(order)} distinct tile key(s) from {len(cells)} panorama cell(s) "
              f"at {n}x{grown}")
        print(f"slice: {unpainted} cell(s) still match the reference (never painted)")
        both = sum(1 for c in conflicts if c["bothPainted"])
        print(f"slice: {len(conflicts)} position(s) disagree with the first occurrence of their key "
              f"- {both} because both were painted differently (a disagreement the pack cannot "
              f"express), {len(conflicts) - both} because only one instance of a repeated key was "
              f"painted (the painted one wins everywhere)")
        for c in conflicts[:20]:
            print(f"  conflict {c['tile']} {c['palette']}: "
                  f"({c['disagrees']['x']},{c['disagrees']['y']}) differs from "
                  f"({c['winner']['x']},{c['winner']['y']})")
        if len(conflicts) > 20:
            print(f"  ... {len(conflicts) - 20} more")
        print(f"slice: wrote sheets/{name}.png + .orig.png + .json — copy the three into a pack's "
              "textures/sheets/ and run scripts/mep_build.py build")
    return {"keys": len(order), "cells": len(cells), "conflicts": conflicts, "unpainted": unpainted,
            "conflictsBothPainted": sum(1 for c in conflicts if c["bothPainted"]),
            "sheet": f"sheets/{name}.png"}


# --- the round trip ---------------------------------------------------------


def verify(pack_dir: Path, map_dir: Path, stems, quiet=False):
    """The acceptance test: does painting a panorama round-trip into pack rules?

    Three questions, answered on a *temporary copy* of the pack — a recorded
    pack under `runs/` is never touched:

      1. is every panorama cell's art byte-identical to the art the pack
         already holds for that cell's `(tileData, palette)` key? That is what
         says the sidecar addresses the strip correctly: a transposed
         coordinate or a wrong stride shows up here and nowhere else;
      2. does `scripts/mep_build.py build` accept the panorama as a sheet?
      3. is the manifest's key set unchanged afterwards — nothing lost, nothing
         invented?
    """
    pack = Pack(pack_dir)
    before = pack.keys
    checked = matched = 0
    for stem in stems:
        doc = json.loads((map_dir / f"{stem}.json").read_text(encoding="utf-8"))
        orig = read_png(map_dir / f"{stem}.orig.png")
        for c in doc.get("cells") or []:
            t = (c.get("tiles") or [{}])[0]
            key = (str(t.get("tile", "")).upper(), str(t.get("palette", "")).upper())
            art = pack.crop_1x(key)
            if art is None:
                continue
            checked += 1
            if orig.crop(int(c["x"]), int(c["y"]), CELL, CELL).px == art.px:
                matched += 1

    def rebuild(with_panorama: bool):
        """`mep_build.py build` on a throwaway copy of the pack, with and
        without the panorama, and the tile key set it produced."""
        with tempfile.TemporaryDirectory(prefix="artist_map_verify.") as tmp:
            work = Path(tmp) / "pack"
            shutil.copytree(pack_dir, work)
            sheets = work / "textures" / "sheets"
            sheets.mkdir(parents=True, exist_ok=True)
            if with_panorama:
                for stem in stems:
                    doc = json.loads((map_dir / f"{stem}.json").read_text(encoding="utf-8"))
                    orig = read_png(map_dir / f"{stem}.orig.png")
                    name = f"pano-{stem}"
                    doc["sheet"] = f"{name}.png"
                    doc["reference"] = f"{name}.orig.png"
                    # A pack has one <scale> for every <img> (MEP-v1 2.1), so the
                    # panorama joins it at the pack's scale whatever the artist
                    # chose to paint at.
                    write_png(sheets / f"{name}.png",
                              orig.upscale(pack.scale) if pack.scale > 1 else orig.clone())
                    write_png(sheets / f"{name}.orig.png", orig)
                    (sheets / f"{name}.json").write_text(json.dumps(doc, indent=1) + "\n", encoding="utf-8")
            proc = subprocess.run(
                [sys.executable, str(Path(__file__).resolve().parent / "mep_build.py"), "build",
                 str(work), "--source", str(pack_dir / "textures" / "hires.txt"), "--quiet"],
                capture_output=True, text=True)
            keys = set()
            rebuilt = work / "textures" / "hires.txt"
            if rebuilt.is_file():
                for raw in rebuilt.read_text(encoding="utf-8", errors="replace").splitlines():
                    m = _TILE_RE.match(raw.strip())
                    if not m:
                        continue
                    f = [t.strip() for t in m.group(2).split(",")]
                    if len(f) >= 6:
                        keys.add((f[1].upper(), f[2].upper()))
            return proc, keys

    base_proc, baseline = rebuild(False)
    proc, after = rebuild(True)
    lost = sorted(baseline - after)
    added = sorted(after - baseline)
    result = {
        "ran": True,
        "errors": proc.returncode + base_proc.returncode,
        "cellsChecked": checked,
        "cellsByteIdentical": matched,
        # The recorder manifest exports every CHR tile as a palette-agnostic
        # defaultTile (ADR-0043) and a rebuild carries only what the sheets
        # carry, so the honest comparison is rebuild-without-panorama against
        # rebuild-with-panorama. The recorder's own total is kept beside it.
        "keys_source": len(before),
        "keys_before": len(baseline),
        "keys_after": len(after),
        "lost": len(lost),
        "added": len(added),
        # A panorama routes keys the pack's own manifest already holds to art
        # for the first time, so `added` here is a gain rather than an
        # invention - `notInPack` is what would catch an invented one, and it
        # is reported separately per stage. The assembler needs to know that,
        # or it would have to call every map fragment FAILED or call every
        # invented key "passed".
        "addedAreFromSource": all(k in pack.keys for k in added),
    }
    if not quiet:
        print(f"verify: {matched}/{checked} panorama cell(s) byte-identical to the pack's own tile art")
        print(f"verify: mep_build.py build exit {proc.returncode} (baseline {base_proc.returncode}); "
              f"keys {len(baseline)} without the panorama -> {len(after)} with it "
              f"(lost {len(lost)}, added {len(added)}; the recorder manifest holds {len(before)})")
        if proc.returncode or base_proc.returncode:
            sys.stderr.write(proc.stdout[-4000:])
            sys.stderr.write(proc.stderr[-4000:])
    return result


# --- driver -----------------------------------------------------------------


def generate(stage: str, dump: Path, pack_dir: Path, out_dir: Path, scale: int, names, quiet: bool):
    frames, shapes, palettes = parse_grid_dump(dump)
    pack = Pack(pack_dir)
    rows_all = tuple(range(ROWS))
    top, bottom = hud_bands(frames, rows_all)
    regions = stitch(frames, top, bottom, quiet)
    if not regions:
        raise MapError(
            f"{stage}: the recording never scrolled past one screen, so there is no panorama to "
            "paint - the recorder's own textures/backgrounds/screenNNN.png already is this stage's "
            "surface. Contra's base stages (2 and 4) are like this by design.")
    map_dir = out_dir / "map"
    map_dir.mkdir(parents=True, exist_ok=True)
    # `stage` comes from the command line and ends up in a file name the
    # artist's paint program has to export back onto, so it goes through the
    # F12.4 contract first (ADR-0213 section 3). The caption keeps the name the
    # operator typed; only the file name is rewritten, and `renamedStage` below
    # records it when the two differ.
    safe_stage = N.sanitize_asset_stem(stage, fallback="stage")
    files = []
    stems = []
    dump_keys = set()
    for region in regions:
        for k in region.cells:
            sid, pid = region.winner(k)
            if sid in shapes:
                dump_keys.add((shapes[sid][0], palettes.get(pid, shapes[sid][1])))
    for i, region in enumerate(sorted(regions, key=lambda r: -len(r.cells))):
        painted, orig, cells, stats = build_panorama(region, shapes, palettes, pack, scale)
        name = N.require_asset_name(
            f"{safe_stage}-{i:03d}" + N.SURFACE_EXT, "artist_map.py")[:-len(N.SURFACE_EXT)]
        stems.append(name)
        write_png(map_dir / f"{name}.png", painted)
        write_png(map_dir / f"{name}.orig.png", orig)
        columns = orig.width // CELL
        doc = sidecar(name, cells, columns, scale, orientation_of(region), stats, region,
                      pack.scale)
        (map_dir / f"{name}.json").write_text(json.dumps(doc, indent=1) + "\n", encoding="utf-8")
        files.append({
            "path": f"map/{name}.png",
            "title": title_for(names, stage, i, region, orig.width, orig.height),
            "unit": "panorama", "rows": orig.height // CELL, "columns": columns,
            "cells": stats["cells"],
            # A cell is drawn only where the camera actually was; nothing is
            # filled in. `seen` is false only when some cell's *palette* is an
            # attribution rather than a record (see the module docstring).
            "seen": stats["ambiguous"] == 0,
            "ids": [stage],
        })
        if not quiet:
            print(f"{stage}: region {i} {orig.width}x{orig.height} at 1x, "
                  f"{stats['cells']} cells, {orientation_of(region)}, "
                  f"{region.frames} frames stitched, "
                  f"{region.agreements} re-sightings agreed with what was kept, "
                  f"{len(region.conflicting)} of {stats['cells']} positions ever showed "
                  f"something else ({region.conflicts} such sightings), "
                  f"{stats['from_pack']} cells from pack art, {stats['rendered']} rendered from the key, "
                  f"{stats['ambiguous']} palette-attributed")
    covered = dump_keys & pack.keys
    if not quiet:
        print(f"{stage}: HUD bands {top} top / {bottom} bottom rows excluded")
        print(f"{stage}: coverage {len(covered)} of the pack's {len(pack.tiles)} tile key(s) "
              f"({100.0 * len(covered) / len(pack.tiles):.1f}%); "
              f"{len(dump_keys - pack.keys)} panorama key(s) the pack does not hold")
    return files, stems, {
        "stage": stage,
        # Only present when the file name had to be rewritten, so a reader who
        # never hits the case never has to wonder what it means.
        **({"renamedStage": safe_stage} if safe_stage != stage else {}),
        "hudTop": top, "hudBottom": bottom,
        "regions": len(regions),
        "packKeys": len(pack.tiles), "panoramaKeys": len(dump_keys),
        "covered": len(covered), "notInPack": len(dump_keys - pack.keys),
        "conflicts": sum(len(r.conflicting) for r in regions),
        "conflictSightings": sum(r.conflicts for r in regions),
        "confirmed": sum(r.agreements for r in regions),
    }


def _merge_verify(checks):
    """One `verify` block for the kit fragment, summing the per-pack runs."""
    if not checks:
        return {"ran": False}
    out = {"ran": True}
    for field in ("errors", "cellsChecked", "cellsByteIdentical", "keys_source",
                  "keys_before", "keys_after", "lost", "added"):
        out[field] = sum(c.get(field, 0) for c in checks)
    out["addedAreFromSource"] = all(c.get("addedAreFromSource", False) for c in checks)
    return out


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(
        prog="artist_map.py", description="Stage panoramas from a recording, and the way back.",
        formatter_class=argparse.RawDescriptionHelpFormatter, epilog=__doc__)
    ap.add_argument("--out", required=True, help="kit folder to write into")
    ap.add_argument("--stage", action="append", default=[], metavar="NAME")
    ap.add_argument("--dump", action="append", default=[], metavar="GRID.TXT",
                    help="a MESEN_SHEET_GRID_DUMP file, one per --stage")
    ap.add_argument("--pack", action="append", default=[], metavar="DIR",
                    help="the recorded pack the same run produced, one per --stage")
    ap.add_argument("--scale", type=int, default=1, help="upscale of the painting surface (default 1)")
    ap.add_argument("--names", metavar="FILE.JSON", help="captions for the stages (titles only)")
    ap.add_argument("--slice", metavar="PAINTED.PNG", help="cut a painted panorama back into pack sheets")
    ap.add_argument("--map", metavar="MAP.JSON", help="the sidecar --slice's panorama was generated with")
    ap.add_argument("--verify", action="store_true",
                    help="round-trip the panorama through mep_build.py on a temporary copy of the pack")
    ap.add_argument("--quiet", action="store_true")
    args = ap.parse_args(argv)

    out_dir = Path(args.out)
    if args.scale < 1 or args.scale > 10:
        print(f"error: --scale {args.scale} out of range (1..10)", file=sys.stderr)
        return 2

    if args.slice:
        if not args.map:
            print("error: --slice needs --map <the sidecar the panorama was generated with>", file=sys.stderr)
            return 2
        try:
            cut_painted(Path(args.map), Path(args.slice), out_dir, args.quiet)
        except (MapError, sheet_repaint.RepaintError, OSError, ValueError) as e:
            print(f"error: {e}", file=sys.stderr)
            return 1
        return 0

    if not args.stage or len(args.stage) != len(args.dump) or len(args.stage) != len(args.pack):
        print("error: pass --stage, --dump and --pack the same number of times", file=sys.stderr)
        return 2

    names = None
    if args.names:
        try:
            names = json.loads(Path(args.names).read_text(encoding="utf-8"))
        except (OSError, ValueError) as e:
            print(f"error: --names {args.names}: {e}", file=sys.stderr)
            return 2

    files = []
    notes = []
    packs = []
    checks = []
    for stage, dump, pack in zip(args.stage, args.dump, args.pack):
        try:
            got, stems, stats = generate(stage, Path(dump), Path(pack), out_dir, args.scale, names, args.quiet)
        except (MapError, sheet_repaint.RepaintError, OSError, ValueError) as e:
            print(f"error: {stage}: {e}", file=sys.stderr)
            return 1
        files += got
        packs.append(str(Path(pack)))
        if args.verify:
            try:
                checks.append(verify(Path(pack), out_dir / "map", stems, args.quiet))
            except (MapError, sheet_repaint.RepaintError, OSError, ValueError) as e:
                print(f"error: {stage}: verify: {e}", file=sys.stderr)
                return 1
        notes.append(
            f"{stage}: {stats['regions']} panorama region(s) stitched from the recorded grid stream; "
            f"HUD rows {stats['hudTop']} top / {stats['hudBottom']} bottom excluded. "
            f"{stats['confirmed']} world positions were re-seen and agreed, "
            f"{stats['conflicts']} positions ever showed something else "
        f"({stats['conflictSightings']} such sightings; the most-seen variant wins, "
            f"the rest are the moving parts of the stage - doors, lifts, destroyed walls). "
            f"The panorama covers {stats['covered']} of the pack's {stats['packKeys']} tile keys; "
            f"{stats['notInPack']} key(s) it shows are not in the pack at all.")
    notes.append(
        "Paint a panorama in any editor at its own 1x size, or at any whole multiple of it that "
        "divides the pack's scale, then run "
        "`scripts/artist_map.py --slice <painted>.png --map map/<name>.json --out <kit>` — "
        "it cuts the strip back into a textures/sheets/ drop-in, written at the pack's own scale "
        "whatever size you painted at, because every sheet of a pack shares one. One tile key sits at many "
        "panorama positions and the pack can hold one art per key: the first occurrence in "
        "(y, x) order wins and every disagreeing position is printed.")
    notes.append(
        "What a world position shows is the variant (drawing plus palette) the recording showed "
        "there most often, ties going to the first sighting. Nothing is filled or guessed: every "
        "cell on a panorama is a cell the camera actually covered. The majority rule matters "
        "because on a scrolling stage the frame that first covers a position is almost always a "
        "transitional one - measured on Contra's waterfall, the first variant holds for a median "
        "0.02 of a position's sightings and the most-seen one for 0.98.")
    notes.append(
        "The recorded grid stream keys cells by a palette-agnostic shape, so their real colours "
        "come from the per-cell palette plane F9.24 added to the dump. A dump written before that "
        "has no plane: its cells fall back to the shape's first-seen colours, carry "
        "\"paletteAttributed\": true and make their file seen: false.")

    out_dir.mkdir(parents=True, exist_ok=True)
    fragment = {
        "part": "map",
        "generator": "scripts/artist_map.py",
        # The contract's `pack` is one recorded pack; a run that stitched
        # several stages names the first and lists them all beside it.
        "pack": packs[0],
        "packs": packs,
        "files": files,
        "dropped": [],
        "notes": notes,
        "verify": _merge_verify(checks),
    }
    (out_dir / "kit-part-map.json").write_text(json.dumps(fragment, indent=1) + "\n", encoding="utf-8")
    if not args.quiet:
        print(f"wrote {out_dir / 'kit-part-map.json'} ({len(files)} panorama(s))")

    # --verify is the acceptance gate, so its result has to reach the exit
    # code: a rebuild error, a lost key or a cell that stopped matching the
    # pack's own art used to be printed and then exit 0, which automation
    # cannot enforce. An *added* key is not a failure here - the panorama
    # routes keys the pack's manifest already holds to art for the first time,
    # and `notInPack` is what would catch an invented one.
    v = fragment["verify"]
    if v.get("ran"):
        bad = (v.get("errors", 0) or v.get("lost", 0)
               or v.get("cellsChecked", 0) != v.get("cellsByteIdentical", 0))
        if bad:
            print(f"verify: FAILED - {v.get('errors', 0)} rebuild error(s), "
                  f"{v.get('lost', 0)} key(s) lost, "
                  f"{v.get('cellsChecked', 0) - v.get('cellsByteIdentical', 0)} cell(s) no longer "
                  "byte-identical to the pack's art", file=sys.stderr)
            return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
