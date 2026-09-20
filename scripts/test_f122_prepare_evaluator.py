#!/usr/bin/env python3
"""Regression test for the 2026-09-19 Sonnet sweep's "coverage-count mismatch"
finding (`docs/validation/f12.2-sonnet-sweep-2026-09-19.md`).

The sweep's index for Contra said "the copy table explains 692/930 of the
frame's 8x8 blocks" while the actual `tilemap-copy/Contra*.txt` held 90 rows
(3 columns of a 32-column nametable); Mike Tyson's Punch-Out!! claimed
"793/960" from 32 rows (one row); Ninja Gaiden claimed "828/960" from 30 rows,
every one of them column 0. All three numbers were true and all three were
misleading: `moment_agreement`'s "blocks explained" matches rendered *pixel
designs*, which repeat across a screen (a brick, the blank tile) far more
often than the dump has rows for, so a near-empty dump can still "explain"
most of the frame.

This does not touch the native emulator or `mep_build.py` - `f122_prepare_
evaluator.py` is sweep/dev tooling, and the fix is entirely inside it:
`dump_coverage()` counts the dump's own rows against the frame's fixed
`FULL_GRID_CELLS`, `one_moment()` gates on it as a third, independent leg, and
the generated `index/<game>.md` states both numbers so a reader is never
pointed at the wrong one again."""

import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import f122_prepare_evaluator as f122  # noqa: E402

FAILED = 0


def ok(msg):
    print(f"PASS: {msg}")


def fail(msg):
    global FAILED
    FAILED = 1
    print(f"FAIL: {msg}")


# A one-tile background design (a solid, unmistakable colour once decoded by
# `tile_pixels`) and a bare NES palette word - shape only matters for this
# file, not what colour it draws as.
SOLID_TILE = "FF" * 8 + "00" * 8
PAL = "0F162A30"


def _row(col, row, tile=SOLID_TILE, palette=PAL):
    return f"{col},{row}\t" + json.dumps({"tile": tile, "palette": palette})


def dump_coverage_tests(tmp):
    # The measured Contra case: 3 columns x 30 rows of a 32x30 grid = 90 rows,
    # 90/960 = 9.4%, nowhere near the pixel-match line the sweep read as the
    # real coverage.
    table = tmp / "contra-shaped.txt"
    rows = [_row(col, row) for col in range(3) for row in range(30)]
    table.write_text("\n".join(rows) + "\n")
    cells, coverage = f122.dump_coverage(table)
    if cells == 90 and coverage == round(90 / 960, 3):
        ok("dump_coverage counts the Contra-shaped 3x30 dump as 90/960")
    else:
        fail(f"dump_coverage on a 90-row table returned {cells}, {coverage}")

    # The measured Ninja Gaiden case: 1 column x 30 rows = 30 rows, the
    # column-0-only truncation this same sweep found.
    table = tmp / "ninja-gaiden-shaped.txt"
    rows = [_row(0, row) for row in range(30)]
    table.write_text("\n".join(rows) + "\n")
    cells, coverage = f122.dump_coverage(table)
    if cells == 30 and coverage == round(30 / 960, 3):
        ok("dump_coverage counts the Ninja-Gaiden-shaped column-0 dump as 30/960")
    else:
        fail(f"dump_coverage on a 30-row column-0 table returned {cells}, {coverage}")

    # A full 32x30 nametable dump, the shape every non-scrolling game in the
    # sweep got.
    table = tmp / "full.txt"
    rows = [_row(col, row) for col in range(32) for row in range(30)]
    table.write_text("\n".join(rows) + "\n")
    cells, coverage = f122.dump_coverage(table)
    if cells == 960 and coverage == 1.0:
        ok("dump_coverage counts a full 32x30 dump as 960/960 (1.0)")
    else:
        fail(f"dump_coverage on a full 960-row table returned {cells}, {coverage}")

    if f122.FULL_GRID_CELLS == 960:
        ok("FULL_GRID_CELLS matches WalkTilemap's fixed 256x240-in-8x8-steps walk (32x30)")
    else:
        fail(f"FULL_GRID_CELLS is {f122.FULL_GRID_CELLS}, expected 960")


def one_moment_gate_tests():
    # Exactly the shape the sweep's own numbers had for Contra: a high
    # pixel-match "explained" ratio (repeats) and plenty of non-blank keys,
    # but a dump that names under a tenth of the frame. Before this fix,
    # `one_moment` (inlined at the call site) only looked at the first two and
    # would have shipped this frame as a clean pass.
    inflated = {"explained": 0.744, "keys_on_frame": 45, "keys_in_table": 49}
    if not f122.one_moment(inflated, coverage=90 / 960):
        ok("one_moment rejects a high pixel-match score backed by a 9% dump (Contra shape)")
    else:
        fail("one_moment passed a 9%-coverage dump on pixel-match numbers alone")

    # Ninja Gaiden's shape: explained 828/960 = 0.8625, still >= MOMENT_BLOCKS.
    inflated_worse = {"explained": 0.8625, "keys_on_frame": 8, "keys_in_table": 8}
    if not f122.one_moment(inflated_worse, coverage=30 / 960):
        ok("one_moment rejects a 86%-explained frame backed by a 3% (column-0) dump (Ninja Gaiden shape)")
    else:
        fail("one_moment passed a 3%-coverage dump on pixel-match numbers alone")

    # A genuine full-coverage pass still passes all three legs.
    full = {"explained": 0.90, "keys_on_frame": 20, "keys_in_table": 25}
    if f122.one_moment(full, coverage=1.0):
        ok("one_moment still passes a frame with full dump coverage")
    else:
        fail("one_moment rejected a frame with full dump coverage")

    # A thin-but-honest frame that fails on keys_on_frame alone (unrelated to
    # this fix) must still fail - the new leg does not paper over the old ones.
    thin = {"explained": 0.90, "keys_on_frame": 1, "keys_in_table": 25}
    if not f122.one_moment(thin, coverage=1.0):
        ok("one_moment still rejects on keys_on_frame regardless of coverage")
    else:
        fail("one_moment ignored keys_on_frame once coverage was full")


def main() -> int:
    import tempfile
    with tempfile.TemporaryDirectory() as tmp:
        dump_coverage_tests(Path(tmp))
    one_moment_gate_tests()
    return 1 if FAILED else 0


if __name__ == "__main__":
    sys.exit(main())
