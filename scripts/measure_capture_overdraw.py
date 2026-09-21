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

Usage:
  measure_capture_overdraw.py <baseline.png> <render.png> [--label L]
  measure_capture_overdraw.py --sweep DIR --baseline-prefix none \\
      --render-prefix post [--json out.json]

In sweep mode DIR holds one subdirectory per timestamp, named `<prefix>-<n>`,
each containing the run's screenshot anywhere below it -- the layout
`scripts/headless_record` leaves behind. Baseline runs are the no-pack ones.

Trap worth repeating: a pack installed at `mesen-home/HdPacks/<stem>/` is not
found by `headless_record`; the sibling convention `<romdir>/<stem>/auto` is.
A render that comes back at native 256x240 was produced with no pack loaded,
and this tool refuses it rather than reporting a misleading zero.
"""
import argparse
import json
import re
import sys
from pathlib import Path

import numpy as np
from PIL import Image

CELL = 8
COLS, ROWS = 32, 30
NATIVE = (COLS * CELL, ROWS * CELL)


def _colour_counts(path: Path, scale: int) -> np.ndarray:
    """Distinct colours per 8x8 NES cell, for an image at `scale`x native."""
    rgb = np.asarray(Image.open(path).convert("RGB"), dtype=np.uint32)
    packed = (rgb[:, :, 0] << 16) | (rgb[:, :, 1] << 8) | rgb[:, :, 2]
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


def measure(baseline: Path, render: Path) -> dict:
    """Compare one unpacked frame against one rendered frame."""
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
    return {
        "detail_cells": int(detail.sum()),
        "erased_cells": int(erased.sum()),
        "erased_at": [[int(x), int(y)] for y, x in zip(ys, xs)],
    }


def _screenshot_in(folder: Path) -> Path:
    # Only the run's Screenshots folder: a with-pack run also leaves the pack
    # itself below `folder`, which is hundreds of PNGs that are not frames.
    shots = sorted(p for p in folder.rglob("*.png") if p.parent.name == "Screenshots")
    if not shots:
        raise FileNotFoundError(f"{folder}: no screenshot under a Screenshots folder")
    if len(shots) > 1:
        raise ValueError(f"{folder}: {len(shots)} screenshots, expected one")
    return shots[0]


def _sweep(root: Path, baseline_prefix: str, render_prefix: str) -> list[dict]:
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
        row = measure(_screenshot_in(pair[baseline_prefix]), _screenshot_in(pair[render_prefix]))
        row["label"] = str(stamp)
        rows.append(row)
    if not rows:
        raise FileNotFoundError(
            f"{root}: no `{baseline_prefix}-<n>` / `{render_prefix}-<n>` pair"
        )
    return rows


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("baseline", nargs="?", type=Path)
    ap.add_argument("render", nargs="?", type=Path)
    ap.add_argument("--label", default="frame")
    ap.add_argument("--sweep", type=Path)
    ap.add_argument("--baseline-prefix", default="none")
    ap.add_argument("--render-prefix", default="post")
    ap.add_argument("--json", type=Path)
    args = ap.parse_args(argv)

    if args.sweep:
        rows = _sweep(args.sweep, args.baseline_prefix, args.render_prefix)
    elif args.baseline and args.render:
        row = measure(args.baseline, args.render)
        row["label"] = args.label
        rows = [row]
    else:
        ap.error("give either two images or --sweep DIR")

    for row in rows:
        flag = "  <-- erased" if row["erased_cells"] else ""
        print(f"{row['label']:>6}  detail={row['detail_cells']:4d}  "
              f"erased={row['erased_cells']:4d}{flag}")
    total = sum(r["erased_cells"] for r in rows)
    bad = sum(1 for r in rows if r["erased_cells"])
    print(f"\n{bad} of {len(rows)} frames lose ROM content; {total} erased cells in total")
    if args.json:
        args.json.write_text(json.dumps(rows, indent=2) + "\n")
        print(f"wrote {args.json}")
    return 1 if total else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
