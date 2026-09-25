#!/usr/bin/env python3
"""f122_score_panel.py on a pack with a sheet it cannot read (#510).

An evaluator ran `rm -rf metatiles.*` inside a pack's `textures/sheets/` and
left a 0-byte `metatiles.png` behind (F14.2 retest16, Castlevania). Scoring
that pack died in `find_magenta` -> `sheet_repaint.read_png` with a
`RepaintError`, so the traceback replaced the whole panel: not one criterion
was scored, and the damage that caused it was never named.

The scorer's contract is the opposite: a sheet nothing can decode is a fact
*about the pack* - the evaluator truncated or deleted it, and the build and
lint it left behind are scored as damaged - so it is reported as a finding and
the rest of the panel is scored. What is pinned here:

  * a damaged sheet is named as `score  unreadable_sheet: <name>`, once per
    sheet, and the criteria that do not depend on it still score (the magenta
    square on the readable sheet, the round-trip rule in `hires.txt`);
  * a pack whose sheets are *all* unreadable still reports them before it
    refuses the panel, because that is the case where "no magenta square" is
    the damage and not an evaluator who painted nothing;
  * a healthy pack prints no `unreadable_sheet` line at all.

`render()` is stubbed: P14 needs a ROM, a minted save state and the built
`scripts/headless_record` binary, none of which this suite has, and the bug
being pinned is upstream of it. Everything else - `mep_lint` on the fixture,
the sheet scan, the `hires.txt` read - runs for real.

Run:  python3 scripts/test_f122_score_panel.py
"""

import io
import json
import pathlib
import sys
import tempfile
from contextlib import redirect_stdout

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import f122_score_panel as P  # noqa: E402
import sheet_repaint as R  # noqa: E402

_FAILURES = []
PAINT_X = PAINT_Y = 16
PAINT_SIZE = 32
#The evaluator's own cell declares CHR index 215, and `hires.txt` carries the
#same index in hex - the round-trip criterion 5 is read on.
PAINT_INDEX = 215


def check(cond, name, detail=""):
    if cond:
        print(f"ok   {name}")
    else:
        print(f"FAIL {name}: {detail}")
        _FAILURES.append(name)


# --------------------------------------------------------------------------
# Fixtures
# --------------------------------------------------------------------------

def write_painted(path):
    """The sheet the evaluator painted: one 32x32 magenta square on an
    otherwise transparent 64x64 canvas, written by the same encoder the tools
    write sheets with."""
    img = R.Image(64, 64)
    for y in range(PAINT_Y, PAINT_Y + PAINT_SIZE):
        for x in range(PAINT_X, PAINT_X + PAINT_SIZE):
            img.set(x, y, (255, 0, 255, 255))
    R.write_png(path, img)


def write_pack(root, sheet_names):
    """A minimal pack: the rebuilt `hires.txt` with the `<tile>` rule the
    round-trip reads, the painted sheet's sidecar, and one file per name in
    `sheet_names` - written 0 bytes, which is what `rm -rf metatiles.*` left
    behind. Returns the pack folder."""
    pack = pathlib.Path(root) / "pack"
    sheets = pack / "textures/sheets"
    sheets.mkdir(parents=True)
    (pack / "textures/hires.txt").write_text(
        "<ver>106\n"
        f"<tile>sprites.png,0D7,FF0F3015,{PAINT_X},{PAINT_Y},1,N\n")
    (sheets / "sprites.json").write_text(json.dumps({
        "scale": 4,
        "cells": [{"index": 0, "x": PAINT_X // 4, "y": PAINT_Y // 4,
                   "tiles": [{"index": PAINT_INDEX}]}],
    }))
    for name in sheet_names:
        (sheets / name).write_bytes(b"")
    return pack


def run(argv):
    """The CLI on a stubbed `render()`, returning (exit code, stdout). The
    stub returns a painted count so P14's "non-zero magenta" clause passes
    without a ROM; the run's real work is the sheet scan in front of it."""
    P.render = lambda roms, game, pack, out, state: (7, 2, pathlib.Path("/dev/null"))
    buffer = io.StringIO()
    with redirect_stdout(buffer):
        code = P.main(argv)
    return code, buffer.getvalue()


# --------------------------------------------------------------------------
# Cases
# --------------------------------------------------------------------------

def test_a_damaged_sheet_is_a_finding_not_a_traceback():
    with tempfile.TemporaryDirectory() as td:
        pack = write_pack(td, [])
        write_painted(pack / "textures/sheets/sprites.png")
        (pack / "textures/sheets/metatiles.png").write_bytes(b"")
        state = pathlib.Path(td) / "frame.mss"
        state.write_bytes(b"")
        code, out = run(["--game", "zelda", "--pack", str(pack),
                         "--state", str(state), "--out", str(pathlib.Path(td) / "out")])
        check(code == 0, "an unreadable sheet does not change the exit code",
              f"rc={code}\n{out}")
        check("score      unreadable_sheet: metatiles.png" in out,
              "the damaged sheet is reported as a finding", out)
        check("sprites.png" in out and "(16, 16)" in out,
              "the readable sheet is still scanned for the magenta square", out)
        check("roundtrip_line:" in out,
              "criterion 5 is still scored on the pack that is left", out)


def test_a_pack_with_nothing_readable_reports_the_sheets_before_refusing():
    with tempfile.TemporaryDirectory() as td:
        pack = write_pack(td, ["metatiles.png"])
        state = pathlib.Path(td) / "frame.mss"
        state.write_bytes(b"")
        code, out = run(["--game", "zelda", "--pack", str(pack),
                         "--state", str(state), "--out", str(pathlib.Path(td) / "out")])
        check(code == 1, "a pack with no readable sheet still fails the panel",
              f"rc={code}\n{out}")
        check("score      unreadable_sheet: metatiles.png" in out,
              "the sheet that could not be read is named in the failure", out)
        check("no 32x32 magenta square" in out,
              "the refusal is the magenta criterion, not the unreadable one", out)


def test_a_healthy_pack_prints_no_finding():
    with tempfile.TemporaryDirectory() as td:
        pack = write_pack(td, [])
        write_painted(pack / "textures/sheets/sprites.png")
        state = pathlib.Path(td) / "frame.mss"
        state.write_bytes(b"")
        code, out = run(["--game", "zelda", "--pack", str(pack),
                         "--state", str(state), "--out", str(pathlib.Path(td) / "out")])
        check(code == 0, "a healthy pack scores", f"rc={code}\n{out}")
        check("unreadable_sheet" not in out,
              "nothing is reported when every sheet reads", out)


def test_scan_sheets_returns_both_halves():
    with tempfile.TemporaryDirectory() as td:
        pack = write_pack(td, [])
        write_painted(pack / "textures/sheets/sprites.png")
        (pack / "textures/sheets/metatiles.png").write_bytes(b"")
        found, unreadable = P.scan_sheets(pack)
        check(found == [("sprites.png", (PAINT_X, PAINT_Y))],
              "the squares of every readable sheet are returned", f"{found}")
        check(unreadable == ["metatiles.png"],
              "the sheets that could not be decoded are returned by name",
              f"{unreadable}")


def main():
    tests = [
        test_a_damaged_sheet_is_a_finding_not_a_traceback,
        test_a_pack_with_nothing_readable_reports_the_sheets_before_refusing,
        test_a_healthy_pack_prints_no_finding,
        test_scan_sheets_returns_both_halves,
    ]
    for t in tests:
        t()
    print(f"\n{len(tests) - len(_FAILURES)}/{len(tests)} cases passed")
    return 1 if _FAILURES else 0


if __name__ == "__main__":
    sys.exit(main())
