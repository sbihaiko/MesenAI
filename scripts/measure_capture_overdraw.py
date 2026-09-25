#!/usr/bin/env python3
"""measure_capture_overdraw — counts the cells a pack's render erases.

ADR-0221's acceptance test, as a tool. A `<background>` capture draws frozen
art over a live frame; when the frame has background content the capture does
not carry, that content disappears (issue #339: the ROM draws 7 808
`STARRING` / `LITTLE MAC` pixels and the render has 0).

The metric is per 8x8 NES cell, not per pixel, because a pixel diff between a
packed render and the unpacked frame measures the upscale rather than
correctness (ADR-0221 Consequences). For each cell:

  * `detail`  -- the ROM's own frame has 2 or more distinct colours there, so
    the ROM is drawing something;
  * `erased`  -- `detail` holds and the render's corresponding region is a
    single flat colour, so whatever the ROM drew is gone.

`erased` is the number that must move. It is deliberately conservative: a cell
the artist repainted with any variation at all does not count, so the tool
under-reports rather than inventing regressions.

Two columns under the total (F12.15, ADR-0224 §Decision 4)
----------------------------------------------------------
The total alone misled once: ADR-0221's stop condition "0 erased cells" was
written against it, and the F12.13 trace (ADR-0223, "The fight screen") found
that every cell it flagged on the Punch-Out!! fight frames was a
behind-background sprite painted over by the priority-20 layer, not
background the capture lacked -- a render-order fact no gate rule can reach.
So `erased` is split by *what the ROM was drawing* in the cell, read off the
recording's grid dump (`--grid`, the `MESEN_SHEET_GRID_DUMP` file of the run
the sweep replays):

  * `erased background` -- the retained frame's own background tiles have
    detail in the cell; the capture sits where the ROM's background had
    content. This is the number ADR-0221's options and ADR-0223 are judged on.
  * `erased sprite`     -- the ROM's background is flat there, so the detail
    the flat render buried can only have come from a sprite. ADR-0224's
    renderer change is the fix; no recorder rule is.

The sweep screenshot is located in the grid dump by matching the no-pack frame
against every retained frame's background (the method the F12.13 trace used);
the residual, sprite-covered cells, is reported so a bad match is visible.

Which sprites sat in the cell is read from one of two sources, named in the
output:

  * `ram-line`  -- the retained frame's `M` line (F12.6b, ADR-0197 §3): the
    2 KB of internal RAM, whose shadow OAM at `$0200` carries 64 x (y, tile,
    attribute, x). The attribute's bit 5 is the behind-background flag, so
    this source also yields `behind-bg`, the count of erased sprite cells with
    at least one behind-background sprite over them -- the ADR-0224 prediction.
    It assumes the game keeps its shadow OAM at `$0200`, as most do; a game
    that DMA-s from elsewhere shows up as `unattributed` cells.
  * `oam-dump`  -- the ADR-0222 OAM stream (`oam.txt` beside the grid dump, or
    `--oam`), aligned to the grid by played-frame index since its frame
    numbers are its own. **Its entry `<shape>,<x>,<y>,<pal>` carries no
    attribute byte** (`MesenSheets::OamEntry` is `{Shape, X, Y, Palette}`), so
    it gives sprite presence but not priority: `behind-bg` is `n/a` from this
    source. The tool does not invent the bit; it prefers the `M` line when
    both are present and says so.

Sprite boxes are 8x`--sprite-height` (default 8; the RAM cannot tell 8x16
mode). The split itself does not depend on the sprite source -- it is decided
by the background plane -- the source only cross-checks it: an erased sprite
cell with no sprite box over it is counted `unattributed`.

Usage:
  measure_capture_overdraw.py <baseline.png> <render.png> [--label L]
  measure_capture_overdraw.py --sweep DIR --baseline-prefix none \\
      --render-prefix post [--json out.json]
  ... [--grid grid.txt [--oam oam.txt] [--sprite-height 8|16]]
      [--verdict total|background]

In sweep mode DIR holds one subdirectory per timestamp, named `<prefix>-<n>`,
each containing the run's screenshot anywhere below it -- the layout
`scripts/headless_record` leaves behind. Baseline runs are the no-pack ones.

Exit code: 1 when the chosen `--verdict` column is non-zero over the run.
`total` (default) keeps the ADR-0221 / F12.13 behaviour; `background` is the
F12.15 stop condition (ADR-0224 §4: never stated on the total).

Trap worth repeating: a pack installed at `mesen-home/HdPacks/<stem>/` is not
found by `headless_record`; the sibling convention `<romdir>/<stem>/auto` is.
A render that comes back at native 256x240 was produced with no pack loaded,
and this tool refuses it rather than reporting a misleading zero.
"""
import argparse
import json
import os
import re
import sys
from collections import Counter
from pathlib import Path

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mep_conditions import oam_path_for, parse_oam_dump  # noqa: E402

CELL = 8
COLS, ROWS = 32, 30
NATIVE = (COLS * CELL, ROWS * CELL)
SHADOW_OAM = 0x200
BEHIND_BG_BIT = 0x20

# The 2C02 default palette, `Core/NES/NesDefaultVideoFilter.cpp`; headless
# screenshots are rendered with it, which is what makes the frame match exact.
_PALETTE_HEX = (
    "666666 002A88 1412A7 3B00A4 5C007E 6E0040 6C0600 561D00 333500 0B4800 005200 004F08 00404D 000000 000000 000000 "
    "ADADAD 155FD9 4240FF 7527FE A01ACC B71E7B B53120 994E00 6B6D00 388700 0C9300 008F32 007C8D 000000 000000 000000 "
    "FFFEFF 64B0FF 9290FF C676FF F36AFF FE6ECC FE8170 EA9E22 BCBE00 88D800 5CE430 45E082 48CDDE 4F4F4F 000000 000000 "
    "FFFEFF C0DFFF D3D2FF E8C8FF FBC2FF FEC4EA FECCC5 F7D8A5 E4E594 CFEF96 BDF4AB B3F3CC B5EBF2 B8B8B8 000000 000000")
PALETTE = np.array([[int(v[0:2], 16), int(v[2:4], 16), int(v[4:6], 16)] for v in _PALETTE_HEX.split()],
                   dtype=np.uint8)


def _colour_counts(path: Path, scale: int) -> np.ndarray:
    """Distinct colours per 8x8 NES cell, for an image at `scale`x native."""
    rgb = np.asarray(Image.open(path).convert("RGB"), dtype=np.uint32)
    return _cell_colour_counts((rgb[:, :, 0] << 16) | (rgb[:, :, 1] << 8) | rgb[:, :, 2], scale)


def _cell_colour_counts(packed: np.ndarray, scale: int) -> np.ndarray:
    side = CELL * scale
    counts = np.empty((ROWS, COLS), dtype=np.int32)
    for cy in range(ROWS):
        for cx in range(COLS):
            block = packed[cy * side:(cy + 1) * side, cx * side:(cx + 1) * side]
            counts[cy, cx] = np.unique(block).size
    return counts


def _scale_of(path: Path, *, expect_native: bool) -> int:
    width, height = Image.open(path).size
    if width % NATIVE[0] or height % NATIVE[1] or width // NATIVE[0] != height // NATIVE[1]:
        raise ValueError(f"{path}: {width}x{height} is not a whole multiple of 256x240")
    scale = width // NATIVE[0]
    if expect_native and scale != 1:
        raise ValueError(f"{path}: baseline must be the unpacked frame, got {scale}x")
    return scale


def measure(baseline: Path, render: Path, recording: "Recording | None" = None) -> dict:
    """Compare one unpacked frame against one rendered frame.

    With a `Recording`, the erased cells are split into background and sprite
    loss (ADR-0224 §4); without one the row carries only the total.
    """
    render_scale = _scale_of(render, expect_native=False)
    if render_scale == 1:
        raise ValueError(
            f"{render}: came back at native 256x240, so no pack was loaded -- "
            "check the run's `LoadHdPack` line before trusting any comparison"
        )
    rom = _colour_counts(baseline, _scale_of(baseline, expect_native=True))
    out = _colour_counts(render, render_scale)
    detail = rom >= 2
    erased = detail & (out == 1)
    ys, xs = np.nonzero(erased)
    row = {
        "detail_cells": int(detail.sum()),
        "erased_cells": int(erased.sum()),
        "erased_at": [[int(x), int(y)] for y, x in zip(ys, xs)],
    }
    if recording is not None:
        row.update(recording.split(np.asarray(Image.open(baseline).convert("RGB")), erased))
    return row


# ---------------------------------------------------------------- grid dump


class _GridFrame:
    __slots__ = ("idx", "repeat", "fine", "cells", "pals", "ram")

    def __init__(self, idx: int):
        self.idx = idx
        self.repeat = 0
        self.fine = 0
        self.cells = np.full((ROWS, COLS), -1, dtype=np.int32)
        self.pals = np.full((ROWS, COLS), -1, dtype=np.int32)
        self.ram = None  # bytes of $0000-$07FF when the dump has an `M` line


def parse_grid_dump(path: Path):
    """Retained frames, shapes and palettes of a `MESEN_SHEET_GRID_DUMP` file.

    `F <n>` opens a retained frame (repeated once per played frame), `M <hex>`
    is its 2 KB RAM window, `K`/`P` intern shapes and palette words, and a
    cell line is `<x> <y> <shape> [<pal>]` with `x` carrying the fine scroll.
    """
    frames, shapes, palettes = [], {}, {}
    cur, last = None, None
    with Path(path).open("r", encoding="ascii", errors="replace") as fh:
        for line in fh:
            head = line[0]
            if head == "F":
                n = int(line[2:])
                if last is not None and n == last:
                    cur.repeat += 1
                else:
                    cur = _GridFrame(n)
                    cur.repeat = 1
                    last = n
                    frames.append(cur)
            elif head == "K":
                _, sid, tile, pal = line.split()
                shapes[int(sid)] = (bytes.fromhex(tile), int(pal, 16))
            elif head == "P":
                _, pid, pal = line.split()
                palettes[int(pid)] = int(pal, 16)
            elif head == "M":
                if cur is not None:
                    cur.ram = bytes.fromhex(line[2:].strip())
            elif cur is not None:
                parts = line.split()
                x, y, sid = int(parts[0]), int(parts[1]), int(parts[2])
                cur.fine = x & 7
                col, row = (x - cur.fine) // CELL, y // CELL
                if 0 <= row < ROWS and 0 <= col < COLS:
                    cur.cells[row, col] = sid
                    cur.pals[row, col] = int(parts[3]) if len(parts) > 3 else -1
    return frames, shapes, palettes


def _decode_tile(tile: bytes) -> np.ndarray:
    lo = np.frombuffer(tile[:8], dtype=np.uint8)[:, None]
    hi = np.frombuffer(tile[8:16], dtype=np.uint8)[:, None]
    shift = np.arange(7, -1, -1, dtype=np.uint8)[None, :]
    return ((lo >> shift) & 1) | (((hi >> shift) & 1) << 1)


def background_plane(frame: _GridFrame, shapes: dict, palettes: dict) -> np.ndarray:
    """240x256 plane of NES colour numbers for the frame's background alone."""
    plane = np.full((NATIVE[1], NATIVE[0]), -1, dtype=np.int16)
    backdrops = Counter()
    for r in range(ROWS):
        for c in range(COLS):
            sid = frame.cells[r, c]
            if sid < 0 or sid not in shapes:
                continue
            pid = frame.pals[r, c]
            word = palettes[pid] if pid in palettes else shapes[sid][1]
            cols = np.array([(word >> 24) & 0x3F, (word >> 16) & 0x3F, (word >> 8) & 0x3F, word & 0x3F],
                            dtype=np.int16)
            backdrops[int(cols[0])] += 1
            x0, y0 = c * CELL + frame.fine, r * CELL
            w = min(CELL, NATIVE[0] - x0)
            plane[y0:y0 + CELL, x0:x0 + w] = cols[_decode_tile(shapes[sid][0])][:, :w]
    if backdrops:
        plane[plane < 0] = backdrops.most_common(1)[0][0]
    else:
        plane[plane < 0] = 0x0F
    return plane


def _cells_touched(boxes, height: int) -> np.ndarray:
    """30x32 mask of the cells any (x, y) 8x`height` box overlaps."""
    mask = np.zeros((ROWS, COLS), dtype=bool)
    for x, y in boxes:
        for cy in range(y // CELL, min(ROWS, (y + height - 1) // CELL + 1)):
            for cx in range(x // CELL, min(COLS, (x + CELL - 1) // CELL + 1)):
                if 0 <= cy and 0 <= cx:
                    mask[cy, cx] = True
    return mask


def shadow_oam_sprites(ram: bytes):
    """(x, y, attr) of every on-screen sprite in the shadow OAM at `$0200`."""
    out = []
    for i in range(64):
        y, _tile, attr, x = ram[SHADOW_OAM + i * 4:SHADOW_OAM + i * 4 + 4]
        if y >= 0xEF:
            continue
        out.append((x, y + 1, attr))
    return out


class Recording:
    """The grid dump (and OAM source) of the run a sweep replays."""

    def __init__(self, grid: Path, oam: "Path | None" = None, sprite_height: int = 8):
        self.grid = Path(grid)
        self.sprite_height = sprite_height
        self.frames, self.shapes, self.palettes = parse_grid_dump(self.grid)
        if not self.frames:
            raise ValueError(f"{self.grid}: no retained frame")
        self._planes = [background_plane(f, self.shapes, self.palettes) for f in self.frames]
        self._rgb = [PALETTE[p] for p in self._planes]
        self._oam_by_played = None
        self.has_ram = any(f.ram is not None and len(f.ram) > SHADOW_OAM + 256 for f in self.frames)
        oam = Path(oam) if oam else oam_path_for(self.grid)
        self.oam_path = oam if oam.is_file() else None
        if self.oam_path and not self.has_ram:
            self._load_oam()
        self.sprite_source = "ram-line" if self.has_ram else ("oam-dump" if self.oam_path else "none")

    def _load_oam(self):
        frames, _shapes, _has_visibility = parse_oam_dump(self.oam_path)
        self._oam_by_played = []
        for f in frames:
            # ADR-0234 appends two fields to an entry, so index rather than
            # unpack: the boxes are the first three whatever the dump's age.
            boxes = [(e[1], e[2]) for e in f.entries]
            self._oam_by_played.extend([boxes] * max(1, f.repeat))

    def locate(self, shot_rgb: np.ndarray):
        """Index of the retained frame whose background matches `shot_rgb` best,
        and the count of cells that still differ (sprite-covered cells)."""
        best, best_miss = 0, ROWS * COLS + 1
        for i, rgb in enumerate(self._rgb):
            eq = np.all(shot_rgb == rgb, axis=2).reshape(ROWS, CELL, COLS, CELL).all(axis=(1, 3))
            miss = int((~eq).sum())
            if miss < best_miss:
                best, best_miss = i, miss
                if miss == 0:
                    break
        return best, best_miss

    def _sprites_for(self, index: int):
        """(front_mask, behind_mask | None) for retained frame `index`."""
        frame = self.frames[index]
        if self.has_ram:
            if frame.ram is None or len(frame.ram) < SHADOW_OAM + 256:
                return np.zeros((ROWS, COLS), dtype=bool), np.zeros((ROWS, COLS), dtype=bool)
            sprites = shadow_oam_sprites(frame.ram)
            any_mask = _cells_touched([(x, y) for x, y, _a in sprites], self.sprite_height)
            behind = _cells_touched([(x, y) for x, y, a in sprites if a & BEHIND_BG_BIT], self.sprite_height)
            return any_mask, behind
        if self._oam_by_played is not None:
            played = sum(f.repeat for f in self.frames[:index])
            boxes = self._oam_by_played[played] if played < len(self._oam_by_played) else []
            return _cells_touched(boxes, self.sprite_height), None
        return None, None

    def split(self, shot_rgb: np.ndarray, erased: np.ndarray) -> dict:
        index, residual = self.locate(shot_rgb)
        bg_detail = _cell_colour_counts(self._planes[index].astype(np.uint32), 1) >= 2
        erased_bg = erased & bg_detail
        erased_sprite = erased & ~bg_detail
        sprite_mask, behind_mask = self._sprites_for(index)
        out = {
            "retained_frame": int(self.frames[index].idx),
            "frame_match_residual_cells": residual,
            "sprite_source": self.sprite_source,
            "erased_background_cells": int(erased_bg.sum()),
            "erased_sprite_cells": int(erased_sprite.sum()),
            "erased_sprite_behind_bg_cells": None,
            "erased_sprite_unattributed_cells": None,
        }
        if sprite_mask is not None:
            out["erased_sprite_unattributed_cells"] = int((erased_sprite & ~sprite_mask).sum())
        if behind_mask is not None:
            out["erased_sprite_behind_bg_cells"] = int((erased_sprite & behind_mask).sum())
        return out


# ---------------------------------------------------------------- sweep / CLI


def _screenshot_in(folder: Path) -> Path:
    # Only the run's Screenshots folder: a with-pack run also leaves the pack
    # itself below `folder`, which is hundreds of PNGs that are not frames.
    shots = sorted(p for p in folder.rglob("*.png") if p.parent.name == "Screenshots")
    if not shots:
        raise FileNotFoundError(f"{folder}: no screenshot under a Screenshots folder")
    if len(shots) > 1:
        raise ValueError(f"{folder}: {len(shots)} screenshots, expected one")
    return shots[0]


def _sweep(root: Path, baseline_prefix: str, render_prefix: str, recording=None) -> list[dict]:
    stamps = {}
    for child in root.iterdir():
        match = re.fullmatch(rf"({re.escape(baseline_prefix)}|{re.escape(render_prefix)})-(\d+)", child.name)
        if child.is_dir() and match:
            stamps.setdefault(int(match.group(2)), {})[match.group(1)] = child
    rows = []
    for stamp in sorted(stamps):
        pair = stamps[stamp]
        if baseline_prefix not in pair or render_prefix not in pair:
            continue
        row = measure(_screenshot_in(pair[baseline_prefix]), _screenshot_in(pair[render_prefix]), recording)
        row["label"] = str(stamp)
        rows.append(row)
    if not rows:
        raise FileNotFoundError(
            f"{root}: no `{baseline_prefix}-<n>` / `{render_prefix}-<n>` pair"
        )
    return rows


def _fmt(value) -> str:
    return " n/a" if value is None else f"{value:4d}"


def _print_rows(rows: list[dict], split: bool) -> None:
    for row in rows:
        flag = "  <-- erased" if row["erased_cells"] else ""
        line = f"{row['label']:>6}  detail={row['detail_cells']:4d}  erased={row['erased_cells']:4d}"
        if split:
            line += (f"  background={_fmt(row['erased_background_cells'])}"
                     f"  sprite={_fmt(row['erased_sprite_cells'])}"
                     f"  behind-bg={_fmt(row['erased_sprite_behind_bg_cells'])}"
                     f"  [frame {row['retained_frame']}, residual {row['frame_match_residual_cells']}]")
        print(line + flag)


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("baseline", nargs="?", type=Path)
    ap.add_argument("render", nargs="?", type=Path)
    ap.add_argument("--label", default="frame")
    ap.add_argument("--sweep", type=Path)
    ap.add_argument("--baseline-prefix", default="none")
    ap.add_argument("--render-prefix", default="post")
    ap.add_argument("--json", type=Path)
    ap.add_argument("--grid", type=Path, help="grid dump of the recording the sweep replays (splits erased)")
    ap.add_argument("--oam", type=Path, help="ADR-0222 OAM dump (default: oam.txt beside --grid)")
    ap.add_argument("--sprite-height", type=int, choices=(8, 16), default=8)
    ap.add_argument("--verdict", choices=("total", "background"), default="total",
                    help="which column drives the exit code (ADR-0224 §4: F12.15 uses `background`)")
    args = ap.parse_args(argv)

    recording = Recording(args.grid, args.oam, args.sprite_height) if args.grid else None
    if recording is not None:
        print(f"sprite source: {recording.sprite_source}"
              + ("  (OAM dump carries no attribute byte: behind-bg is n/a)"
                 if recording.sprite_source == "oam-dump" else "")
              + ("  (no `M` line and no OAM dump: sprite cells are inferred from the background plane only)"
                 if recording.sprite_source == "none" else ""))
    if args.verdict == "background" and recording is None:
        ap.error("--verdict background needs --grid")

    if args.sweep:
        rows = _sweep(args.sweep, args.baseline_prefix, args.render_prefix, recording)
    elif args.baseline and args.render:
        row = measure(args.baseline, args.render, recording)
        row["label"] = args.label
        rows = [row]
    else:
        ap.error("give either two images or --sweep DIR")

    _print_rows(rows, recording is not None)
    total = sum(r["erased_cells"] for r in rows)
    bad = sum(1 for r in rows if r["erased_cells"])
    print(f"\n{bad} of {len(rows)} frames lose ROM content; {total} erased cells in total")
    verdict = total
    if recording is not None:
        bg = sum(r["erased_background_cells"] for r in rows)
        sp = sum(r["erased_sprite_cells"] for r in rows)
        behind = [r["erased_sprite_behind_bg_cells"] for r in rows]
        unattr = [r["erased_sprite_unattributed_cells"] for r in rows]
        print(f"  erased background: {bg}   erased sprite: {sp}"
              f"   behind-bg sprite: {_fmt(None if any(b is None for b in behind) else sum(behind)).strip()}"
              f"   unattributed: {_fmt(None if any(u is None for u in unattr) else sum(unattr)).strip()}")
        if args.verdict == "background":
            verdict = bg
    if args.json:
        args.json.write_text(json.dumps(rows, indent=2) + "\n")
        print(f"wrote {args.json}")
    return 1 if verdict else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
