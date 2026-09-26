#!/usr/bin/env python3
"""Acceptance test for scripts/mep_add_cell.py (ADR-0216, PRD Phase 12 F12.2).

ADR-0216 answers four questions about `Copy as MEP sheet cell`; three of them
are this file's subject, and the fourth (1(b) — the clipboard carries the cell
unplaced) is what makes the placer exist at all. What is asserted here:

  * **OPEN 1(b), the payload.** The placer reads `{"count": n, "tiles": [...]}`
    with no `index`/`x`/`y`, one such object per line; it also still reads a
    bare `tiles[]` entry, which is the pre-ADR clipboard and what a
    hand-authored paste carries, so an old copy is not a dead end.

  * **OPEN 2(a), the destination.** The free-form one-tile sheet, chosen by
    `cell.w == 8` *among the sheets that are not a named figure*. The ADR's own
    correction is the case that matters: `cell.w` alone does not identify it,
    because over the 30 packs the 2026-09-19 sweep left in
    `runs/f12.2-sweep/packs/` a `sprite` sheet is 8x8 on 719 sheets and
    `sprites` on 28, against `unsorted` on 27. A fixture here therefore ships
    all four 8x8 kinds at once, and the test fails if the key lands on a
    sprite sheet — the one destination the rule forbids, since a background
    key put there never reaches the background (ADR-0178 §6).

  * **OPEN 3(a), growing.** The free slots of a sheet are the ones no cell's
    `x,y` claims, and the placer takes the first of them in row-major order:
    on a dense `cells[]` that is the tail of the last partial row, which is the
    arithmetic the ADR states, and on a sparse one it is the first hole. The
    2026-09-19 measurement behind "`cells[]` is dense and row-major on all 78
    free-form sheets" does not survive the 16-game F14.2 retest of 2026-09-25
    (#503): Mario Bros.'s `unsorted` holds 30 cells at indices 0..29 while rows
    1 and 2 carry only columns 0..2 (the sidecar as the repro copies it, before
    the paste), so the slot after the last cell — the
    dense `len(cells)`, `x1,y55` — is already taken by cell 20, and pasting
    there grew `unsorted.png` 184x256 -> 184x292 against a logical height of 64
    and made `build` exit 2. A **hole** in a row the sidecar describes is a
    free slot like any other and needs no growth at all (ADR-0216 OPEN 3: "a
    free slot of the existing last row changes neither file"); only when every
    described slot is claimed does the sheet gain a row, and then the logical
    size and both images move together by exactly one row pitch.
    `sparse_slot_tests` is the case that fails on the pre-#503 rule.

    Appending a row changes the logical size the sidecar describes, and
    `mep_build` pins the pack's `<scale>` on `<sheet>.png` being an exact
    integer multiple of it while `_EditedProbe` needs `<sheet>.orig.png` to be
    exactly 1/N of the PNG.

    That is why the interesting tests here are the failures. **Issue #346**: a
    half-grown pack is the one failure mode in this area that is both silent
    and wide — grow only the `.png` and the probe goes blind, every cell of
    that sheet counts as painted, and the static `_SHEET_RANK` silently decides
    precedence for cells the artist never touched. So two cases prove that a
    failed grow leaves the pack *byte-identical*: a twin that cannot be read,
    which is caught before anything is written, and a rename that fails between
    the two files, which is the only instant at which the pack can be half
    written and the only one that needs a rollback.

  * **OPEN 4(a), a key another sheet already claims.** Reported at paste time
    rather than by `build` two steps later, naming the sheet and whether that
    cell was painted — answered through `mep_build._EditedProbe` itself, so the
    report cannot disagree with the build's own override line.

  * **The `index` trap.** A whole-cell payload carries the word twice: the
    cell's ordinal in its sheet, and inside `tiles[]` the tile's absolute CHR
    index (ADR-0172 §2). The placer sets the first and carries the second
    through untouched; the test pins both, with values that differ.

  * **The round trip.** ADR-0153 §4's shared-schema requirement: the same cell
    document must go back through `mep_build build`, which is run here for
    real over a grown pack and must exit 0 with the new key in `hires.txt`.

Standard library only; synthetic packs in a temp dir. No emulator, no ROM, no
network.
"""

import json
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import mep_add_cell  # noqa: E402
import mep_build  # noqa: E402

REPO = Path(__file__).resolve().parent.parent
MEP_BUILD = REPO / "scripts" / "mep_build.py"
PY = sys.executable

FAILED = 0


def ok(msg):
    print(f"PASS: {msg}")


def fail(msg):
    global FAILED
    FAILED = 1
    print(f"FAIL: {msg}")


# --- fixtures ---------------------------------------------------------------

PAL = "0F162A30"
SPRITE_PAL = "FF162A30"


def tile_hex(shape: int) -> str:
    """A distinct 16-byte 2bpp pattern per shape id, as 32 uppercase hex."""
    return bytes(((shape * 7 + r * 13) & 0xFF) for r in range(8)).hex().upper() + \
        bytes(((shape * 11 + r * 5) & 0xFF) for r in range(8)).hex().upper()


def solid(width, height, channels, value):
    return mep_build._Bitmap(width, height, channels,
                             bytearray(bytes([value] * channels) * width * height))


def write_sheet(sheets: Path, stem: str, kind: str, unit: int, gutter: int, columns: int,
                shapes, scale: int = 1, palette: str = PAL, chr_index: bool = True,
                paint_cell: int = -1, slots=None):
    """One ADR-0153 v1 sidecar plus its PNG and pixel-exact `*.orig.png` twin.

    `shapes` is one shape id per cell; the cell grid is dense row-major, which
    is how a generated free-form sheet is laid out. `slots` overrides that with
    one `(col, row)` per cell, which is how a **recorded** sheet comes out: its
    `cells[]` skips slots, so `len(cells)` does not name a free one (#503). The
    PNG still spans every row the sidecar describes. `paint_cell` differs one
    cell from the twin, i.e. makes `_EditedProbe` call it painted."""
    pitch = unit + gutter
    if slots is None:
        slots = [(i % columns, i // columns) for i in range(len(shapes))]
    rows = max(row for _col, row in slots) + 1
    lw, lh = columns * pitch + gutter, rows * pitch + gutter
    per = 4 if unit >= 16 else 1
    cells = []
    for i, (shape, (col, row)) in enumerate(zip(shapes, slots)):
        tiles = [{"tile": tile_hex(shape + k), "palette": palette} for k in range(per)]
        if chr_index:
            for k, t in enumerate(tiles):
                # Deliberately far from any cell ordinal: ADR-0216's one trap is
                # the two `index` fields being confused for one another.
                t["index"] = 400 + shape * 4 + k
        cells.append({"index": i, "x": gutter + col * pitch,
                      "y": gutter + row * pitch, "count": per,
                      "context": "misc", "label": "", "tiles": tiles})
    doc = {"version": 1, "kind": kind, "gridUnit": unit, "gridPhase": {"x": 0, "y": 0},
           "cell": {"w": unit, "h": unit}, "gutter": gutter, "columns": columns,
           "sheet": f"{stem}.png", "reference": f"{stem}.orig.png", "cells": cells}
    (sheets / f"{stem}.json").write_text(json.dumps(doc, indent=2) + "\n", encoding="utf-8")

    orig = solid(lw, lh, 4, 0x20)
    sheet = solid(lw * scale, lh * scale, 4, 0x20)
    if paint_cell >= 0:
        c = cells[paint_cell]
        off = (c["y"] * scale * sheet.width + c["x"] * scale) * 4
        sheet.raw[off:off + 4] = b"\xff\x00\x00\xff"
    mep_build._png_write(sheets / f"{stem}.orig.png", orig)
    mep_build._png_write(sheets / f"{stem}.png", sheet)
    return doc


def make_pack(root: Path, sheets_spec, scale: int = 1):
    """A pack folder: `textures/hires.txt` as the key source plus the sheets."""
    textures = root / "textures"
    (textures / "sheets").mkdir(parents=True)
    docs = []
    for spec in sheets_spec:
        docs.append(write_sheet(textures / "sheets", scale=scale, **spec))
    lines = ["<ver>106\n", f"<scale>{scale}\n"]
    seen = set()
    for doc in docs:
        for cell in doc["cells"]:
            for t in cell["tiles"]:
                key = (t["tile"], t["palette"])
                if key in seen:
                    continue
                seen.add(key)
                lines.append(f"<tile>0,{t['tile']},{t['palette']},0,0,1,N,0,0\n")
    (textures / "hires.txt").write_text("".join(lines), encoding="utf-8")
    return root


def snapshot(root: Path):
    return {p.relative_to(root).as_posix(): p.read_bytes() for p in sorted(root.rglob("*")) if p.is_file()}


def run(pack: Path, payload, *extra):
    """The placer as a library call, so a failure is an exception with a
    message rather than an exit code with a string."""
    with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False) as fh:
        fh.write(payload if isinstance(payload, str) else json.dumps(payload))
        src = fh.name
    try:
        return mep_add_cell.main([str(pack), src, *extra])
    finally:
        os.unlink(src)


CELL_PAYLOAD = {"count": 1, "tiles": [{"tile": tile_hex(90), "palette": PAL, "index": 486}]}


def main() -> int:
    with tempfile.TemporaryDirectory() as tmp:
        tmp = Path(tmp)
        payload_tests()
        destination_tests(tmp)
        free_slot_tests(tmp)
        sparse_slot_tests(tmp)
        grow_tests(tmp)
        half_write_tests(tmp)
        claim_tests(tmp)
        sprite_palette_tests(tmp)
    return 1 if FAILED else 0


# --- OPEN 1(b): what the clipboard carries ----------------------------------


def payload_tests():
    cells = mep_add_cell.parse_payload(json.dumps(CELL_PAYLOAD))
    if len(cells) == 1 and cells[0]["count"] == 1 and cells[0]["tiles"][0]["index"] == 486:
        ok("the unplaced cell payload (`count` + `tiles[]`, no index/x/y) parses")
    else:
        fail(f"cell payload parsed as {cells}")

    # The pre-ADR-0216 clipboard, and what a hand-authored paste carries.
    bare = mep_add_cell.parse_payload('{"tile": "%s", "palette": "%s"}' % (tile_hex(3), PAL))
    if len(bare) == 1 and bare[0]["count"] == 1 and bare[0]["tiles"][0]["tile"] == tile_hex(3):
        ok("a bare tiles[] entry still reads as a one-tile cell")
    else:
        fail(f"bare entry parsed as {bare}")

    # A 16px-tall sprite is two cells, not one with two tiles: `_cell_crops`
    # lays a cell's tiles[] out row-major 2x2, so tiles[1] would land to the
    # RIGHT of tiles[0], not below it.
    two = mep_add_cell.parse_payload(json.dumps(CELL_PAYLOAD) + "\n" + json.dumps(CELL_PAYLOAD))
    if len(two) == 2:
        ok("two lines are two cells")
    else:
        fail(f"two lines parsed as {len(two)} cell(s)")

    for bad, why in [('{"count": 1, "tiles": [{"tile": "AB", "palette": "%s"}]}' % PAL, "short tile"),
                     ('{"count": 1, "tiles": [{"tile": "%s", "palette": "XX"}]}' % tile_hex(1), "bad palette"),
                     ("", "empty clipboard"),
                     ("not json", "not JSON")]:
        try:
            mep_add_cell.parse_payload(bad)
            fail(f"a {why} payload was accepted")
        except mep_add_cell.AddCellError:
            ok(f"a {why} payload is refused")


# --- OPEN 2(a): which sheet ---------------------------------------------------


def destination_tests(tmp: Path):
    # All four 8x8 kinds at once. This is the ADR's own correction: picking by
    # `cell.w == 8` without excluding named figures lands on a sprite sheet.
    full = make_pack(tmp / "dest-all", [
        dict(stem="metatiles", kind="metatiles", unit=16, gutter=1, columns=2, shapes=[0, 4]),
        dict(stem="misc", kind="misc", unit=16, gutter=1, columns=2, shapes=[8, 12]),
        dict(stem="spr000", kind="sprite", unit=8, gutter=1, columns=2, shapes=[16, 17]),
        dict(stem="sprites", kind="sprites", unit=8, gutter=1, columns=2, shapes=[18, 19]),
        dict(stem="unsorted", kind="unsorted", unit=8, gutter=1, columns=2, shapes=[20, 21, 22]),
    ])
    if run(full, CELL_PAYLOAD) != 0:
        fail("the placer refused a pack with an unsorted sheet")
    else:
        landed = [p.name for p in sorted((full / "textures" / "sheets").glob("*.json"))
                  if tile_hex(90) in p.read_text()]
        if landed == ["unsorted.json"]:
            ok("with sprite, sprites, misc, metatiles and unsorted all present, the key lands on unsorted")
        else:
            fail(f"the key landed on {landed}, not ['unsorted.json']")

    no_unsorted = make_pack(tmp / "dest-misc", [
        dict(stem="metatiles", kind="metatiles", unit=16, gutter=1, columns=2, shapes=[0, 4]),
        dict(stem="misc", kind="misc", unit=16, gutter=1, columns=2, shapes=[8, 12]),
        dict(stem="spr000", kind="sprite", unit=8, gutter=1, columns=2, shapes=[16, 17]),
    ])
    run(no_unsorted, CELL_PAYLOAD)
    landed = [p.name for p in sorted((no_unsorted / "textures" / "sheets").glob("*.json"))
              if tile_hex(90) in p.read_text()]
    if landed == ["misc.json"]:
        ok("with no unsorted sheet, the key falls back to the 16x16 free-form sheet")
    else:
        fail(f"without unsorted the key landed on {landed}, not ['misc.json']")

    # Donkey Kong and Zelda II in the sweep: metatiles + named figures only.
    meta_only = make_pack(tmp / "dest-meta", [
        dict(stem="metatiles", kind="metatiles", unit=16, gutter=1, columns=2, shapes=[0, 4]),
        dict(stem="obj000", kind="object", unit=16, gutter=1, columns=2, shapes=[8, 12]),
        dict(stem="spr000", kind="sprite", unit=8, gutter=1, columns=2, shapes=[16, 17]),
    ])
    run(meta_only, CELL_PAYLOAD)
    landed = [p.name for p in sorted((meta_only / "textures" / "sheets").glob("*.json"))
              if tile_hex(90) in p.read_text()]
    if landed == ["metatiles.json"]:
        ok("with neither free-form sheet, the key falls back to metatiles")
    else:
        fail(f"with metatiles only the key landed on {landed}")

    figures = make_pack(tmp / "dest-none", [
        dict(stem="obj000", kind="object", unit=16, gutter=1, columns=2, shapes=[8, 12]),
        dict(stem="spr000", kind="sprite", unit=8, gutter=1, columns=2, shapes=[16, 17]),
    ])
    before = snapshot(figures)
    if run(figures, CELL_PAYLOAD) == 1 and snapshot(figures) == before:
        ok("a pack of named figures only is refused, and nothing is written")
    else:
        fail("a pack with no free-form sheet and no metatiles was written to")


# --- the cheap case: a free slot in the last row -----------------------------


def free_slot_tests(tmp: Path):
    pack = make_pack(tmp / "free", [
        dict(stem="unsorted", kind="unsorted", unit=8, gutter=1, columns=4, shapes=[20, 21, 22]),
    ], scale=2)
    sheets = pack / "textures" / "sheets"
    pngs = {p.name: p.read_bytes() for p in sheets.glob("*.png")}
    if run(pack, CELL_PAYLOAD) != 0:
        fail("the placer refused a sheet with a free slot")
        return
    doc = json.loads((sheets / "unsorted.json").read_text())
    cell = doc["cells"][-1]
    # columns 4, unit 8, gutter 1 -> pitch 9; cell 3 is col 3, row 0.
    want = {"index": 3, "x": 1 + 3 * 9, "y": 1, "count": 1, "context": "misc"}
    if all(cell.get(k) == v for k, v in want.items()):
        ok("a free slot in the last row gets the cell at its computed x,y with the next ordinal")
    else:
        fail(f"the new cell is {cell}, expected {want} plus tiles[]")
    if cell["tiles"] == CELL_PAYLOAD["tiles"]:
        ok("tiles[].index (the CHR index, ADR-0172 §2) is carried through untouched, "
           "while the cell's own index is its ordinal")
    else:
        fail(f"tiles[] was rewritten: {cell['tiles']}")
    # #511: the marker `mep_build` reads to report this cell's rule by name,
    # before (and after) the artist paints it.
    if cell.get("addedBy") == "mep_add_cell":
        ok("the placed cell carries `addedBy: mep_add_cell`, so the build reports the rule it produced (#511)")
    else:
        fail(f"the placed cell carries no addedBy marker: {cell.get('addedBy')!r}")
    if {p.name: p.read_bytes() for p in sheets.glob("*.png")} == pngs:
        ok("filling a free slot rewrites no PNG at all")
    else:
        fail("filling a free slot touched a PNG")

    # --dry-run writes nothing.
    before = snapshot(pack)
    if run(pack, CELL_PAYLOAD, "--dry-run") == 0 and snapshot(pack) == before:
        ok("--dry-run reports the placement and writes nothing")
    else:
        fail("--dry-run wrote to the pack")


# --- #503: a recorded sidecar is not dense ------------------------------------


def sparse_slot_tests(tmp: Path):
    """The slot is the first the described grid leaves unclaimed, not
    `len(cells)`. Columns 2, unit 8, gutter 1 -> pitch 9, and four cells at
    (col 0,row 2), (col 1,row 0), (col 1,row 1), (col 1,row 2). The grid below
    is drawn by position, not by `cell.index`; the digit in each slot is the
    cell's index — cell 0 sits in the bottom-left corner, cells 1..3 down the
    right column — and `.` is the slot no cell claims:

      .  1        <- rows 0 and 1 of column 0 are the holes
      .  2
      0  3

    `len(cells)` is 4, so the dense slot index 4 is (col 0, row 2) = x1,y19 —
    cell 0's own slot — and it is also the first index of a row the old rule
    called new, so the placer grew `unsorted.png` by a row that the lowest cell
    (y19) does not describe: the logical height stays 28 while the PNG gains 9
    rows of pixels, and `build` exits 2 with #503's own size error. The holes
    are in rows `_logical_size` already describes, which is what makes them
    free without growing anything (y19 + 8 + gutter 1 = 28, the twin's height).
    """
    pack = make_pack(tmp / "sparse", [
        dict(stem="unsorted", kind="unsorted", unit=8, gutter=1, columns=2, shapes=[20, 21, 22, 23],
             slots=[(0, 2), (1, 0), (1, 1), (1, 2)]),
    ], scale=2)
    sheets = pack / "textures" / "sheets"
    before_cells = json.loads((sheets / "unsorted.json").read_text())["cells"]
    pngs = {p.name: p.read_bytes() for p in sheets.glob("*.png")}
    if run(pack, CELL_PAYLOAD) != 0:
        fail("#503: the placer refused a sheet with a free hole in it")
        return
    after = json.loads((sheets / "unsorted.json").read_text())["cells"]
    new = after[-1]
    taken = {(c["x"], c["y"]) for c in before_cells}
    if (new.get("x"), new.get("y")) not in taken:
        ok("#503: the pasted cell lands on an x,y no existing cell occupies")
    else:
        held = [c["index"] for c in before_cells if (c["x"], c["y"]) == (new.get("x"), new.get("y"))]
        fail(f"#503: the pasted cell landed on x{new.get('x')},y{new.get('y')} — the slot of "
             f"cell {held} (the dense `len(cells)` slot), not the free hole")
    if (new.get("x"), new.get("y")) == (1, 1) and new.get("index") == 4:
        ok("the free slot is the first unclaimed one in row-major order, and the new cell's "
           "ordinal is still the length of cells[]")
    else:
        fail(f"#503: the new cell is at x{new.get('x')},y{new.get('y')} index {new.get('index')}, "
             "expected the hole x1,y1 at index 4")
    if after[:4] == before_cells:
        ok("#503: no existing cell was moved, re-indexed or overwritten")
    else:
        fail(f"#503: the existing cells changed: {after[:4]} against {before_cells}")
    if {p.name: p.read_bytes() for p in sheets.glob("*.png")} == pngs:
        ok("a hole in a described row needs no grow: neither PNG is rewritten (ADR-0216 OPEN 3(a): "
           "a free slot changes neither file)")
    else:
        fail("#503: filling a hole in a described row grew a PNG")
    done = subprocess.run([PY, str(MEP_BUILD), "build", str(pack)], capture_output=True, text=True)
    hires = (pack / "textures" / "hires.txt").read_text()
    crop = [ln for ln in hires.splitlines() if tile_hex(90) in ln and ln.startswith("<tile>")]
    if done.returncode == 0 and tile_hex(90) in hires:
        ok("mep_build build re-reads the pack the hole was filled in and carries the key "
           "(exit 0, the size error of #503 is gone)")
    else:
        fail(f"build exited {done.returncode}; new key in hires.txt: {tile_hex(90) in hires}\n"
             f"{(done.stdout + done.stderr)[-1500:]}")
    # The cell has to reach the screen where it was pasted, not merely build:
    # x1,y1 at scale 2 is the 2,2 crop.
    if crop and f",2,2," in crop[0]:
        ok("#503: the placed key is carried at the hole's own crop (x1,y1 -> 2,2 at scale 2)")
    else:
        fail(f"#503: the placed key's <tile> line reads {crop}")

    # A grid whose every slot is claimed still grows, and grows by exactly one
    # whole row pitch in both files — the cells are not in row-major order,
    # which is the only difference from `grow_tests` above.
    full = make_pack(tmp / "sparse-full", [
        dict(stem="unsorted", kind="unsorted", unit=8, gutter=1, columns=2, shapes=[20, 21, 22, 23],
             slots=[(1, 0), (1, 1), (0, 0), (0, 1)]),
    ], scale=2)
    full_sheets = full / "textures" / "sheets"
    before_png = mep_build._png_pixels(full_sheets / "unsorted.png")
    before_ref = mep_build._png_pixels(full_sheets / "unsorted.orig.png")
    if run(full, CELL_PAYLOAD) != 0:
        fail("#503: the placer refused a full sheet whose cells are out of order")
        return
    after_png = mep_build._png_pixels(full_sheets / "unsorted.png")
    after_ref = mep_build._png_pixels(full_sheets / "unsorted.orig.png")
    added = json.loads((full_sheets / "unsorted.json").read_text())["cells"][-1]
    if (after_ref.height == before_ref.height + 9 and after_png.height == before_png.height + 18
            and (added.get("x"), added.get("y")) == (1, 19)):
        ok("a grid with no unclaimed slot grows by exactly one row pitch, in both files, at "
           "x1,y19")
    else:
        fail(f"#503: full sheet grew to {after_png.width}x{after_png.height} / "
             f"{after_ref.width}x{after_ref.height} with the cell at "
             f"x{added.get('x')},y{added.get('y')}")
    done = subprocess.run([PY, str(MEP_BUILD), "build", str(full)], capture_output=True, text=True)
    if done.returncode == 0:
        ok("the grown sparse sheet builds: the logical size moved with both images")
    else:
        fail(f"build exited {done.returncode} on the grown sparse sheet:\n"
             f"{(done.stdout + done.stderr)[-1500:]}")


# --- OPEN 3(a): growing -------------------------------------------------------
def grow_tests(tmp: Path):
    # columns 2 with 4 cells: the grid is exactly full, which is the state 5 of
    # the sweep's 27 unsorted sheets are already in.
    pack = make_pack(tmp / "grow", [
        dict(stem="unsorted", kind="unsorted", unit=8, gutter=1, columns=2, shapes=[20, 21, 22, 23]),
    ], scale=3)
    sheets = pack / "textures" / "sheets"
    before_png = mep_build._png_pixels(sheets / "unsorted.png")
    before_ref = mep_build._png_pixels(sheets / "unsorted.orig.png")
    if run(pack, CELL_PAYLOAD) != 0:
        fail("the placer refused to grow a full sheet")
        return
    after_png = mep_build._png_pixels(sheets / "unsorted.png")
    after_ref = mep_build._png_pixels(sheets / "unsorted.orig.png")
    pitch = 9
    if (after_ref.height == before_ref.height + pitch
            and after_png.height == before_png.height + pitch * 3
            and after_png.width == before_png.width and after_ref.width == before_ref.width):
        ok("a full sheet grows by exactly one row, in both files, at the pack's scale")
    else:
        fail(f"grown to {after_png.width}x{after_png.height} / {after_ref.width}x{after_ref.height} "
             f"from {before_png.width}x{before_png.height} / {before_ref.width}x{before_ref.height}")

    # The whole point of growing them together (#346): the probe must still be
    # able to see, and must still say the untouched cells are untouched.
    docs, _ = mep_build._load_sheet_docs(sheets)
    sd = next(d for d in docs if d.kind == "unsorted")
    scale = mep_build._sheet_scale(docs)
    probe = mep_build._EditedProbe(sd, scale, sheets)
    if scale == 3 and not probe.blind and not probe.edited(1, 1, 8):
        ok("after the grow the pack's scale still resolves and _EditedProbe is not blind (#346)")
    else:
        fail(f"scale {scale}, probe blind={probe.blind} ({probe.reason}), "
             f"cell 0 edited={None if probe.blind else probe.edited(1, 1, 8)}")

    # ADR-0153 §4: the same cell document has to round-trip through the build.
    done = subprocess.run([PY, str(MEP_BUILD), "build", str(pack)], capture_output=True, text=True)
    hires = (pack / "textures" / "hires.txt").read_text()
    if done.returncode == 0 and tile_hex(90) in hires:
        ok("mep_build build re-reads the grown pack and carries the placed key into hires.txt")
    else:
        fail(f"build exited {done.returncode}; new key in hires.txt: {tile_hex(90) in hires}\n"
             f"{(done.stdout + done.stderr)[-1500:]}")


# --- issue #346: a failed grow leaves the pack untouched ----------------------


def half_write_tests(tmp: Path):
    def full_pack(name):
        return make_pack(tmp / name, [
            dict(stem="unsorted", kind="unsorted", unit=8, gutter=1, columns=2,
                 shapes=[20, 21, 22, 23]),
        ], scale=2)

    # (1) The twin cannot be read. Caught before a byte is written, because a
    # sheet grown without its twin blinds the probe for every cell it holds.
    pack = full_pack("half-noref")
    (pack / "textures" / "sheets" / "unsorted.orig.png").write_bytes(b"not a png")
    before = snapshot(pack)
    code = run(pack, CELL_PAYLOAD)
    if code == 1 and snapshot(pack) == before:
        ok("a grow with an unreadable *.orig.png twin is refused and the pack is byte-identical (#346)")
    else:
        fail(f"unreadable twin: exit {code}, pack changed={snapshot(pack) != before}")

    # (2) The rename fails between the two files. This is the only instant at
    # which the pack can be half written, and the only one that needs the
    # rollback; nothing but an injected failure can produce it on demand.
    pack = full_pack("half-replace")
    before = snapshot(pack)
    real = mep_add_cell._replace
    calls = []

    def replace_failing_on_the_second(src, dst):
        calls.append(dst)
        if len(calls) == 2:
            raise OSError("injected failure between the two renames (#346)")
        return real(src, dst)

    mep_add_cell._replace = replace_failing_on_the_second
    try:
        code = run(pack, CELL_PAYLOAD)
    finally:
        mep_add_cell._replace = real
    after = snapshot(pack)
    if code == 1 and len(calls) == 2 and after == before:
        ok("a rename that fails between the two PNGs rolls both back; the pack is byte-identical (#346)")
    else:
        changed = sorted(k for k in set(before) | set(after) if before.get(k) != after.get(k))
        fail(f"half write: exit {code}, {len(calls)} rename(s), changed {changed}")
    leftovers = sorted(p.name for p in (pack / "textures" / "sheets").glob("*.tmp"))
    if not leftovers:
        ok("a failed grow leaves no temporary file behind")
    else:
        fail(f"temporaries left behind: {leftovers}")


# --- OPEN 4(a): a key another sheet already claims ---------------------------


def claim_tests(tmp: Path):
    pack = make_pack(tmp / "claim", [
        # metatiles cell 1 is painted, so the report has to say so; shape 24
        # is its first tile, and that is the key the payload below carries.
        dict(stem="metatiles", kind="metatiles", unit=16, gutter=1, columns=2,
             shapes=[20, 24], paint_cell=1),
        dict(stem="unsorted", kind="unsorted", unit=8, gutter=1, columns=4, shapes=[40, 41]),
    ])
    payload = {"count": 1, "tiles": [{"tile": tile_hex(24), "palette": PAL, "index": 400 + 24 * 4}]}
    with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False) as fh:
        fh.write(json.dumps(payload))
        src = fh.name
    done = subprocess.run([PY, str(REPO / "scripts" / "mep_add_cell.py"), str(pack), src],
                          capture_output=True, text=True)
    os.unlink(src)
    out = done.stdout
    if done.returncode == 0 and "metatiles.json cell 1 already claims" in out and "(painted)" in out:
        ok("a key another sheet already claims is reported at paste time, with its paint state")
    else:
        fail(f"claim report missing: exit {done.returncode}\n{out}{done.stderr}")


# --- the sprite-sourced key ---------------------------------------------------


def sprite_palette_tests(tmp: Path):
    # ADR-0216 says the placer refuses a sprite-sourced key (ADR-0178 §6). The
    # clipboard carries no record of which viewer the copy came from, so the
    # only signal in the payload is the palette: a sprite's transparent color 0
    # is packed as FF. That is a hint, not a marker - 131 of the 3 495 cells on
    # the sweep's unsorted sheets are keyed under an FF-leading palette too - so
    # it gates a refusal an explicit flag can lift, never a silent reroute.
    pack = make_pack(tmp / "sprite-pal", [
        dict(stem="unsorted", kind="unsorted", unit=8, gutter=1, columns=4, shapes=[20, 21]),
    ])
    payload = {"count": 1, "tiles": [{"tile": tile_hex(90), "palette": SPRITE_PAL, "index": 486}]}
    before = snapshot(pack)
    if run(pack, payload) == 1 and snapshot(pack) == before:
        ok("an FF-leading (sprite) palette is refused rather than routed to a background sheet")
    else:
        fail("a sprite-palette key was placed on a background sheet")
    if run(pack, payload, "--allow-sprite-palette") == 0 and \
            tile_hex(90) in (pack / "textures" / "sheets" / "unsorted.json").read_text():
        ok("--allow-sprite-palette lifts the refusal for the measured background minority")
    else:
        fail("--allow-sprite-palette did not place the key")


if __name__ == "__main__":
    sys.exit(main())
