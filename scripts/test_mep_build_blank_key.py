#!/usr/bin/env python3
"""#464: a sprite tile the NES draws as fully transparent never picks up paint
through a crop it shares with another key.

The Castlevania case: the artist kit's composed sheet `usr017` places the blank
sprite tile (`0000.../FF23340F`) and a spark tile at the same position. Painting
the spark is correct, but `mep_build` then saw the blank key's cell as painted
too, and moved its rules off `hud.png` onto the painted `usr017` crop, so a tile
the NES never draws started drawing magenta.

The rule under test: an all-zero sprite tile (palette key `FF......`, colour 0
only) never claims its key through paint (ADR-0153 §4, amended 2026-09-25), and
among its untouched crops one whose cell nobody painted wins over one whose
cell was painted for another key. A background tile of the same all-zero data
is not blank (colour 0 is the backdrop, which the NES draws), so it keeps the
ordinary rule. Synthetic sheets in a temp dir; no emulator, no ROM.
"""

import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from test_mep_build import parse_hires, png_read, png_rgba  # noqa: E402

MEP_BUILD = HERE / "mep_build.py"
BLANK = "0" * 32
SPARK = "000044EE000000000000000000000000"
BODY = "7FF5E5F5D76E1C087D97A7B7B56A1408"
SPR_PAL = "FF23340F"
BG_PAL = "0F162738"
SPARK_RGB = 0xFFF0C040
BODY_RGB = 0xFF40C0F0
MAGENTA = 0xFFFF00FF
FAILED = []


def check(cond: bool, msg: str, detail: str = ""):
    print(f"{'PASS' if cond else 'FAIL'}: {msg}" + ("" if cond else f": {detail}"))
    if not cond:
        FAILED.append(msg)


def sheet_json(kind: str, stem: str, cells) -> str:
    rows = []
    for i, (x, y, data, pal) in enumerate(cells):
        rows.append(f'{{ "index": {i}, "x": {x}, "y": {y}, "count": 1, "context": "", "label": "", '
                    f'"tiles": [{{ "tile": "{data}", "palette": "{pal}" }}] }}')
    return ('{ "version": 1, "kind": "%s", "gridUnit": 8, "cell": { "w": 8, "h": 8 }, "gutter": 1, '
            '"columns": 2, "sheet": "%s.png", "reference": "%s.orig.png", "cells": [\n  %s\n] }\n'
            % (kind, stem, stem, ",\n  ".join(rows)))


def canvas(fills, w: int = 19, h: int = 19):
    """A 1x sheet, transparent except for the solid 8x8 blocks in `fills`."""
    px = [[0] * w for _ in range(h)]
    for x, y, rgb in fills:
        for yy in range(y, y + 8):
            for xx in range(x, x + 8):
                px[yy][xx] = rgb
    return px


def write_sheet(sheets: Path, stem: str, kind: str, cells, fills, paint=None):
    """The sheet and its 1x `*.orig.png` twin; `paint` repaints 8x8 blocks of
    the sheet only, the way an artist does in an image editor."""
    orig = canvas(fills, h=max(y for _x, y, _d, _p in cells) + 9)  # mep_build's _logical_size
    (sheets / f"{stem}.orig.png").write_bytes(png_rgba(orig))
    painted = [row[:] for row in orig]
    for x, y in paint or ():
        for yy in range(y, y + 8):
            for xx in range(x, x + 8):
                painted[yy][xx] = MAGENTA
    (sheets / f"{stem}.png").write_bytes(png_rgba(painted))
    (sheets / f"{stem}.json").write_text(sheet_json(kind, stem, cells), encoding="utf-8")


def make_folder(root: Path, name: str, keys) -> Path:
    folder = root / name
    sheets = folder / "textures" / "sheets"
    sheets.mkdir(parents=True)
    lines = ["<ver>107", "<scale>1", "<system>nes",
             "<supportedRom>2A4E126D0286BEA0BF503C80A12352C57539F76B", "<img>old.png"]
    lines += [f"<tile>0,{data},{pal},0,0,1,N" for data, pal in keys]
    (folder / "textures" / "hires.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
    return folder


def build(folder: Path):
    p = subprocess.run([sys.executable, str(MEP_BUILD), "build", str(folder)], capture_output=True, text=True)
    return p.returncode, (p.stdout + p.stderr).strip()


def owner(folder: Path, data: str, pal: str):
    """(sheet file, magenta pixels in the 8x8 crop) the built rule points at."""
    imgs, tiles = parse_hires(folder / "textures" / "hires.txt")
    got = tiles.get((data, pal))
    if got is None:
        return None, -1
    rel = imgs[got[0]]
    px = png_read(folder / "textures" / "sheets" / Path(rel).name)
    x, y = got[1], got[2]
    return Path(rel).name, sum(1 for row in px[y:y + 8] for p in row[x:x + 8] if p == MAGENTA)


def usr017_case(root: Path):
    """The issue as filed: blank on hud.png, blank + spark sharing one crop on
    usr017.png, the spark painted."""
    folder = make_folder(root, "usr017", [(BLANK, SPR_PAL), (SPARK, SPR_PAL)])
    sheets = folder / "textures" / "sheets"
    write_sheet(sheets, "hud", "hud", [(1, 1, BLANK, SPR_PAL)], [])
    write_sheet(sheets, "usr017", "sprite", [(1, 10, BLANK, SPR_PAL), (1, 10, SPARK, SPR_PAL)],
                [(1, 10, SPARK_RGB)], paint=[(1, 10)])
    code, out = build(folder)
    check(code == 0, "#464: the build with a painted spark sharing the blank tile's crop passes", out)
    sheet, magenta = owner(folder, BLANK, SPR_PAL)
    check(sheet == "hud.png" and magenta == 0,
          "#464: the blank sprite key keeps its hud.png crop instead of the spark's painted usr017 crop",
          f"blank key -> {sheet}, {magenta} magenta px")
    sheet, magenta = owner(folder, SPARK, SPR_PAL)
    check(sheet == "usr017.png" and magenta == 64, "#464: the painted spark still reaches the screen",
          f"spark key -> {sheet}, {magenta} magenta px")
    check("#253" not in out, "#464: the blank key's shared painted crop is not reported as lost paint (#253)", out)


def unpainted_crop_case(root: Path):
    """The blank key is on two kit sheets only, both sprite rank. The painted
    one sorts later, so the plain tie-break would pick it; the unpainted crop
    must win."""
    folder = make_folder(root, "unpainted", [(BLANK, SPR_PAL), (SPARK, SPR_PAL)])
    sheets = folder / "textures" / "sheets"
    write_sheet(sheets, "usr016", "sprite", [(1, 1, BLANK, SPR_PAL)], [])
    write_sheet(sheets, "usr017", "sprite", [(1, 10, BLANK, SPR_PAL), (1, 10, SPARK, SPR_PAL)],
                [(1, 10, SPARK_RGB)], paint=[(1, 10)])
    code, out = build(folder)
    check(code == 0, "#464: the build passes when the blank key lives on two sprite sheets", out)
    sheet, magenta = owner(folder, BLANK, SPR_PAL)
    check(sheet == "usr016.png" and magenta == 0,
          "#464: among untouched crops, the blank key takes the one whose cell nobody painted",
          f"blank key -> {sheet}, {magenta} magenta px")


def same_sheet_repeat_case(root: Path):
    """The blank key twice on one sheet: first in the crop it shares with the
    painted spark, then alone. The later, unpainted crop must own the key."""
    folder = make_folder(root, "repeat", [(BLANK, SPR_PAL), (SPARK, SPR_PAL)])
    sheets = folder / "textures" / "sheets"
    write_sheet(sheets, "usr017", "sprite",
                [(1, 1, BLANK, SPR_PAL), (1, 1, SPARK, SPR_PAL), (1, 10, BLANK, SPR_PAL)],
                [(1, 1, SPARK_RGB)], paint=[(1, 1)])
    code, out = build(folder)
    check(code == 0, "#464: the build passes with the blank key repeated on one sheet", out)
    imgs, tiles = parse_hires(folder / "textures" / "hires.txt")
    got = tiles.get((BLANK, SPR_PAL))
    check(got is not None and (got[1], got[2]) == (1, 10),
          "#464: on one sheet, the blank key's unpainted repeat wins over the shared painted crop",
          f"blank key -> {got and (imgs[got[0]], got[1], got[2])}")


def guard_cases(root: Path):
    """What must not change: a painted non-blank sprite tile still claims its
    key over hud.png, and an all-zero *background* tile (colour 0 is the
    backdrop the NES draws) painted on its sheet still claims its key."""
    folder = make_folder(root, "guards", [(BODY, SPR_PAL), (BLANK, BG_PAL)])
    sheets = folder / "textures" / "sheets"
    write_sheet(sheets, "hud", "hud", [(1, 1, BODY, SPR_PAL)], [(1, 1, BODY_RGB)])
    write_sheet(sheets, "usr001", "sprite", [(1, 1, BODY, SPR_PAL)], [(1, 1, BODY_RGB)], paint=[(1, 1)])
    write_sheet(sheets, "font", "font", [(1, 1, BLANK, BG_PAL)], [])
    write_sheet(sheets, "metatiles", "metatiles", [(1, 1, BLANK, BG_PAL)], [], paint=[(1, 1)])
    code, out = build(folder)
    check(code == 0, "guard: the build passes", out)
    sheet, magenta = owner(folder, BODY, SPR_PAL)
    check(sheet == "usr001.png" and magenta == 64,
          "guard: a painted non-blank sprite cell still beats the untouched hud.png cell",
          f"body key -> {sheet}, {magenta} magenta px")
    sheet, magenta = owner(folder, BLANK, BG_PAL)
    check(sheet == "metatiles.png" and magenta == 64,
          "guard: a painted all-zero background tile still claims its key (the backdrop is drawn)",
          f"background blank key -> {sheet}, {magenta} magenta px")


def legacy_guard_case(root: Path):
    """A legacy 16-column sheet (ADR-0049, no twin, every cell counts as
    painted) still loses to a painted ADR-0153 sheet: the new "unpainted cell
    first" tie-break must not read a twin-less cell as unpainted."""
    folder = make_folder(root, "legacy", [(BODY, SPR_PAL)])
    sheets = folder / "textures" / "sheets"
    (sheets / "legacy.png").write_bytes(png_rgba(canvas([(0, 0, BODY_RGB)], w=128, h=8)))
    write_sheet(sheets, "usr001", "sprite", [(1, 1, BODY, SPR_PAL)], [(1, 1, BODY_RGB)], paint=[(1, 1)])
    code, out = build(folder)
    check(code == 0, "guard: the build with a legacy sheet passes", out)
    sheet, magenta = owner(folder, BODY, SPR_PAL)
    check(sheet == "usr001.png" and magenta == 64,
          "guard: a painted ADR-0153 cell still beats the legacy 16-column sheet",
          f"body key -> {sheet}, {magenta} magenta px")


def main() -> int:
    root = Path(tempfile.mkdtemp(prefix="mep-build-blank-"))
    try:
        usr017_case(root)
        unpainted_crop_case(root)
        same_sheet_repeat_case(root)
        guard_cases(root)
        legacy_guard_case(root)
    finally:
        shutil.rmtree(root, ignore_errors=True)
    print(f"{len(FAILED)} failure(s)")
    return 1 if FAILED else 0


if __name__ == "__main__":
    sys.exit(main())
