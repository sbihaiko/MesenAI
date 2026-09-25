#!/usr/bin/env python3
"""mep_add_cell — place a copied MEP sheet cell into a pack (ADR-0216).

    scripts/mep_add_cell.py <pack> --paste
    scripts/mep_add_cell.py <pack> cell.json
    pbpaste | scripts/mep_add_cell.py <pack> -

`Copy as MEP sheet cell` in the debugger viewers puts an **unplaced** cell on
the clipboard — `count` and `tiles[]`, and deliberately no `index`/`x`/`y`
(ADR-0216 OPEN 1(b)). The emulator never opens the artist's tree; this is the
tool that does. It picks the sheet, picks the slot, writes the cell into the
sidecar, and grows the two PNGs when the grid has no free slot.

What it retires, measured over the 2026-09-19 28-ROM cold read
(`docs/validation/f12.2-opus-sweep-2026-09-19.md`, finding 4 — a stop in
every one of the 28 runs, 30 s to 2 min each):

  1. authoring the wrapper (`index`, `x`, `y`, `count`, `context`);
  2. counting `cells[]` against `columns`/`cell`/`gutter` to find a free slot
     — a guessed slot silently repaints whatever lived there;
  3. choosing a sheet, which the copied text cannot say and `context` cannot
     route (every free-form cell of all 30 sweep packs reads `"context":
     "misc"` on `unsorted` and on `misc` alike);
  4. working out the pack's `scale`, stated only in `textures/hires.txt`, to
     know where on the painted PNG that `x,y` lands.

**Which sheet (ADR-0216 OPEN 2(a)).** The free-form one-tile sheet, i.e.
`cell.w == 8` *among the sheets that are not a named figure*. `cell.w` alone
does not identify it: over the 30 sweep packs, `sprite` is 8x8 on 719 sheets
and `sprites` on 28, against `unsorted` on 27, and a background key on a
sprite sheet is the one destination the rule forbids. So `sprite`, `sprites`,
`object`, `font`, `hud` and `map` are excluded first; what is left resolves to
`unsorted` on all 27 packs that ship one, then to the 16x16 free-form sheet
`misc` (21), then to `metatiles` (30).

**Growing (ADR-0216 OPEN 3(a)).** `cells[]` is dense and row-major, so the
free slots are the tail of the last partial row: on `unsorted`, 5 of the 27
packs have none at all and 7 more have exactly one, so "the sheet is full" is
the common case, not the corner. Appending a row changes the logical size the
sidecar describes, and `mep_build` derives the pack's `<scale>` by requiring
`<sheet>.png` to be an exact integer multiple of it while `_EditedProbe`
requires `<sheet>.orig.png` to be exactly 1/N of the PNG. Growing one file and
not the other is the one failure mode in this area that is both silent and
wide (issue #346): the probe goes blind and *every* cell of that sheet counts
as painted. So both images are encoded in memory, written to siblings,
re-decoded, and only then swapped in — and any failure restores the bytes that
were there. A half-grown pack is never written.

**A key another sheet already claims (ADR-0216 OPEN 4(a)).** Reported here, at
paste time, rather than by `build` two steps later; the report names the sheet
and whether that cell was painted, read through `mep_build`'s own
`_EditedProbe` so it cannot disagree with the build.

**The `index` trap.** A whole-cell payload carries the word twice: the cell's
ordinal in its sheet, and — inside `tiles[]` — the tile's absolute CHR index
(ADR-0172 §2). This tool sets the cell's `index` itself and carries
`tiles[].index` through untouched. Neither is ever taken from the other.

Standard library only, like the rest of the tree's pack tools. The PNG codec,
the sidecar loader, the scale rule and the painted-cell probe are imported
from `mep_build` rather than reimplemented, so the placer and the build can
never disagree about geometry.

Exit codes: 0 = placed (or `--dry-run` would have), 1 = refused or fatal,
2 = usage error.
"""

import argparse
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import mep_build  # noqa: E402  — the tree's PNG codec, sheet loader and probe

# ADR-0216 OPEN 2(a): each of these is one named thing, not a surface a loose
# background key may be dropped onto. `map` is a nametable surface, and
# `sprite`/`sprites` are the destination ADR-0178 §6 reserves for flip-baked
# keys.
NAMED_FIGURE_KINDS = ("sprite", "sprites", "object", "font", "hud", "map")

# The seam the #346 test needs: the two-file swap has to be interruptible at
# the exact instant between the two renames, and nothing else can produce that
# instant on demand. Called instead of `os.replace` everywhere below.
_replace = os.replace


class AddCellError(Exception):
    """Fatal, with a message an artist can act on."""


# --- the clipboard ----------------------------------------------------------


def read_system_clipboard() -> str:
    """The real system clipboard, cross-platform. Each reader is the one the
    platform ships, so nothing has to be installed on macOS or Windows; on
    Linux either of the two usual ones is accepted, X11 first."""
    if sys.platform == "darwin":
        readers = [["pbpaste"]]
    elif sys.platform.startswith("win"):
        readers = [["powershell", "-NoProfile", "-Command", "Get-Clipboard"]]
    else:
        readers = [["xclip", "-selection", "clipboard", "-o"], ["wl-paste", "--no-newline"]]
    tried = []
    for cmd in readers:
        if shutil.which(cmd[0]) is None:
            tried.append(f"{cmd[0]} is not installed")
            continue
        try:
            done = subprocess.run(cmd, capture_output=True, text=True)
        except OSError as err:
            tried.append(f"{cmd[0]}: {err}")
            continue
        if done.returncode != 0:
            tried.append(f"{cmd[0]} exited {done.returncode}: {done.stderr.strip()}")
            continue
        return done.stdout
    raise AddCellError("could not read the system clipboard (" + "; ".join(tried)
                       + "). Pass the copied text as a file argument, or pipe it in with '-'.")


def parse_payload(text: str):
    """The clipboard text as a list of unplaced cells.

    Three shapes are accepted, because all three exist in the wild:

      * `{"count": 1, "tiles": [...]}` — what the action emits today;
      * one such object per line — a 16px-tall sprite is two cells, since
        `_cell_crops` lays a cell's `tiles[]` out row-major 2x2, which would
        put the bottom half of an 8x16 sprite to the *right* of its top half;
      * a bare `tiles[]` entry (`{"tile": ..., "palette": ...}`) — the
        pre-ADR-0216 payload, still what a hand-authored paste carries. It is
        read as a one-tile cell, so an old clipboard is not a dead end.
    """
    cells = []
    for line in text.splitlines():
        line = line.strip().rstrip(",")
        if not line:
            continue
        try:
            doc = json.loads(line)
        except ValueError as err:
            raise AddCellError(f"not a JSON object: {line[:60]!r} ({err})")
        if not isinstance(doc, dict):
            raise AddCellError(f"not a JSON object: {line[:60]!r}")
        if "tiles" in doc:
            tiles = doc.get("tiles")
            count = doc.get("count")
        else:
            tiles, count = [doc], 1
        if not isinstance(tiles, list) or not tiles:
            raise AddCellError("the payload carries no tiles[]")
        for entry in tiles:
            if not isinstance(entry, dict):
                raise AddCellError("a tiles[] entry is not a JSON object")
            data = str(entry.get("tile") or "").strip().upper()
            pal = str(entry.get("palette") or "").strip().upper()
            if not mep_build._HEX_TILE_RE.match(data):
                raise AddCellError(f"'tile' is not 32 uppercase hex characters: {data!r}")
            if not mep_build._HEX_PAL_RE.match(pal):
                raise AddCellError(f"'palette' is not 8 uppercase hex characters: {pal!r}")
        cells.append({"count": count if isinstance(count, int) and count > 0 else len(tiles),
                      "tiles": tiles})
    if not cells:
        raise AddCellError("the clipboard is empty — copy a tile with 'Copy as MEP sheet cell' first")
    return cells


def looks_like_a_sprite_palette(tiles) -> bool:
    """A sprite's color 0 is transparent, so `HdPackCopyHelper` packs it as
    `FF` and every sprite rule in a pack is keyed that way.

    This is a *hint*, not the marker ADR-0216 assumes: the clipboard carries no
    record of which viewer the copy came from, and 131 of the 3 495 cells on
    the sweep's `unsorted` sheets (and 340 of 13 932 on `metatiles`) are keyed
    under an FF-leading palette too. So it gates a refusal that
    `--allow-sprite-palette` can lift, rather than a silent reroute.
    """
    return any(str(t.get("palette") or "").strip().upper().startswith("FF") for t in tiles)


# --- the pack ---------------------------------------------------------------


def find_sheets_dir(target: Path) -> Path:
    """`target` may be the sheets folder, a pack folder, or a game folder with
    an `auto/` sibling (ADR-0147). Most specific first, first hit wins."""
    for cand in (target, target / "sheets", target / "textures" / "sheets",
                 target / "auto" / "textures" / "sheets"):
        if cand.is_dir() and any(p.suffix == ".json" for p in cand.iterdir()):
            return cand
    raise AddCellError(f"no sheets/ folder with sidecar JSON under {target}")


def cell_width(sd) -> int:
    """The sidecar's own `cell.w`, which is what ADR-0216 OPEN 2(a) reads.
    Falls back to `gridUnit`; the two agree on all 1 171 sheets of the 30
    sweep packs, and a sidecar that omits `cell` still has to be placeable."""
    cell = sd.doc.get("cell")
    if isinstance(cell, dict):
        try:
            return int(cell.get("w"))
        except (TypeError, ValueError):
            pass
    return sd.unit


def choose_sheet(docs, wanted: str = ""):
    """ADR-0216 OPEN 2(a): the free-form one-tile sheet, then the 16x16
    free-form sheet, then `metatiles`. A named figure is never a destination."""
    if wanted:
        stem = Path(wanted).stem
        for sd in docs:
            if sd.json_path.stem == stem or sd.png_path.name == wanted:
                return sd
        raise AddCellError(f"--sheet {wanted}: no such sidecar in this pack")
    free_form = [sd for sd in docs if sd.kind not in NAMED_FIGURE_KINDS]
    for sd in free_form:
        if cell_width(sd) == 8 and sd.kind != "metatiles":
            return sd
    for sd in free_form:
        if cell_width(sd) == 16 and sd.kind != "metatiles":
            return sd
    for sd in free_form:
        if sd.kind == "metatiles":
            return sd
    raise AddCellError(
        "this pack has no free-form sheet and no metatiles.json — nothing here takes a loose "
        "background key (Donkey Kong and Zelda II are the two such packs in the 2026-09-19 sweep)")


def grid(sd):
    """(pitch, columns, rows already used). `cells[]` is dense and row-major
    on all 78 free-form sheets of the 30 sweep packs — index == position and
    `x,y` == `gutter + col*pitch, gutter + row*pitch` on every one — so the
    next slot is simply the one after the last cell."""
    pitch = sd.unit + sd.gutter
    used = len(sd.cells)
    rows = (used + sd.columns - 1) // sd.columns
    return pitch, sd.columns, rows


def slot_of(sd, index: int):
    pitch = sd.unit + sd.gutter
    col, row = index % sd.columns, index // sd.columns
    return col, row, sd.gutter + col * pitch, sd.gutter + row * pitch


def prevailing_context(sd) -> str:
    """`context` routes nothing (ADR-0216), but a new cell that does not look
    like its neighbours is a diff nobody can read. Copied off the sheet."""
    seen = {}
    for c in sd.cells:
        if isinstance(c, dict) and isinstance(c.get("context"), str):
            seen[c["context"]] = seen.get(c["context"], 0) + 1
    return max(seen, key=seen.get) if seen else "misc"


def claim_report(docs, scale: int, sheets_dir: Path, tiles):
    """ADR-0216 OPEN 4(a): every sheet that already claims one of these keys,
    and whether that cell was painted. The paint question is answered by
    `mep_build._EditedProbe` itself, so this report and the build's override
    line can never disagree."""
    wanted = {(str(t.get("tile") or "").upper(), str(t.get("palette") or "").upper()) for t in tiles}
    lines = []
    for sd in docs:
        probe = None
        for cell in sd.cells:
            if not isinstance(cell, dict):
                continue
            hits = {(str(t.get("tile") or "").upper(), str(t.get("palette") or "").upper())
                    for t in (cell.get("tiles") or []) if isinstance(t, dict)}
            key = hits & wanted
            if not key:
                continue
            if probe is None:
                probe = mep_build._EditedProbe(sd, scale, sheets_dir)
            try:
                ox, oy = int(cell.get("x", 0)), int(cell.get("y", 0))
            except (TypeError, ValueError):
                ox = oy = 0
            if probe.blind:
                state = f"paint unknown ({probe.reason})"
            else:
                state = "painted" if probe.edited(ox, oy, sd.unit) else "untouched"
            lines.append(f"{sd.json_path.name} cell {cell.get('index')} already claims "
                         f"{sorted(key)[0][0][:8]}.../{sorted(key)[0][1]} ({state})")
    return lines


# --- growing ----------------------------------------------------------------


def grown(bmp, extra_rows: int):
    """`bmp` with `extra_rows` more scanlines, filled with its own top-left
    pixel — which on every generated sheet is the gutter, i.e. exactly the
    background a new row's margin already has."""
    fill = bytes(bmp.raw[:bmp.channels])
    out = mep_build._Bitmap(bmp.width, bmp.height + extra_rows, bmp.channels,
                            bytearray(bmp.raw) + (fill * bmp.width) * extra_rows)
    return out


def write_pair(png_path: Path, png_bmp, ref_path: Path, ref_bmp):
    """Rewrite `<sheet>.png` and `<sheet>.orig.png` together, or leave both
    exactly as they were (ADR-0216 OPEN 3(a), issue #346).

    Encode both, write both to siblings, decode both back and check the size
    and channel count survived, and only then swap them in. The window between
    the two swaps is the one a crash could halve, so the original bytes are
    held until both are through and put back if the second one is not."""
    temps = [(png_path, png_bmp, png_path.with_name(png_path.name + ".mep-add-cell.tmp")),
             (ref_path, ref_bmp, ref_path.with_name(ref_path.name + ".mep-add-cell.tmp"))]
    backups = {}
    try:
        for dest, bmp, tmp in temps:
            mep_build._png_write(tmp, bmp)
            back = mep_build._png_pixels(tmp)
            if back is None or back.width != bmp.width or back.height != bmp.height or back.channels != bmp.channels:
                raise AddCellError(f"{dest.name}: the grown image did not read back as "
                                   f"{bmp.width}x{bmp.height} — nothing was written")
            backups[dest] = dest.read_bytes()
        done = []
        try:
            for dest, _bmp, tmp in temps:
                _replace(tmp, dest)
                done.append(dest)
        except Exception:
            for dest in done:
                dest.write_bytes(backups[dest])
            raise
    finally:
        for _dest, _bmp, tmp in temps:
            if tmp.exists():
                tmp.unlink()
    return backups


# --- placing ----------------------------------------------------------------


def place(sheets_dir: Path, docs, sd, payload, scale: int, label: str, dry_run: bool):
    """One cell into one sheet. Returns the report lines."""
    if sd.doc.get("kind") == "map" or sd.doc.get("placements"):
        raise AddCellError(f"{sd.json_path.name} is a map sheet; it places metatiles, not cells")
    pitch, columns, rows_used = grid(sd)
    index = len(sd.cells)
    col, row, x, y = slot_of(sd, index)
    must_grow = row >= rows_used

    out = [f"sheet: {sd.json_path.name} (kind {sd.kind}, {columns} columns, {sd.unit}px cells, "
           f"gutter {sd.gutter}, scale {scale})",
           f"slot: cell index {index} at col {col}, row {row} -> x {x}, y {y} "
           f"(painted at {x * scale}, {y * scale} on {sd.png_path.name})"]

    cell = {"index": index, "x": x, "y": y, "count": payload["count"],
            "context": prevailing_context(sd)}
    if label:
        cell["label"] = label
    # ADR-0216: `tiles[].index` is the tile's absolute CHR index (ADR-0172 §2)
    # and is carried through untouched. The cell's own `index` above is its
    # ordinal in this sheet. They are never derived from one another.
    cell["tiles"] = payload["tiles"]

    ref = str(sd.doc.get("reference") or "").strip()
    ref_path = sheets_dir / ref if ref else None
    if must_grow:
        if ref_path is None or not ref_path.is_file():
            raise AddCellError(
                f"{sd.json_path.name} is full ({len(sd.cells)} cells, {columns} columns, "
                f"{rows_used} rows) and has no readable '{ref or '<none>'}' twin to grow with it. "
                "Growing only the sheet blinds the build's painted-cell probe for the whole "
                "sheet (#346), so nothing is written.")
        sheet_bmp = mep_build._png_pixels(sd.png_path)
        ref_bmp = mep_build._png_pixels(ref_path)
        if sheet_bmp is None or ref_bmp is None:
            raise AddCellError(
                f"{sd.png_path.name} or {ref} is not an 8-bit non-interlaced RGB/RGBA PNG, so the "
                "pair cannot be grown together — nothing was written (#346)")
        if ref_bmp.channels != sheet_bmp.channels or ref_bmp.width * scale != sheet_bmp.width \
                or ref_bmp.height * scale != sheet_bmp.height:
            raise AddCellError(
                f"{ref} is {ref_bmp.width}x{ref_bmp.height}x{ref_bmp.channels}ch against "
                f"{sd.png_path.name}'s {sheet_bmp.width}x{sheet_bmp.height}x{sheet_bmp.channels}ch "
                f"at scale {scale}: the twin does not match the sheet, so growing them together "
                "would not restore the match — nothing was written (#346)")
        out.append(f"grow: {sd.png_path.name} {sheet_bmp.width}x{sheet_bmp.height} -> "
                   f"{sheet_bmp.width}x{sheet_bmp.height + pitch * scale}, {ref} "
                   f"{ref_bmp.width}x{ref_bmp.height} -> {ref_bmp.width}x{ref_bmp.height + pitch}")
    out += claim_report(docs, scale, sheets_dir, payload["tiles"]) or []

    if dry_run:
        out.append("dry run: nothing written")
        return out

    backups = None
    if must_grow:
        backups = write_pair(sd.png_path, grown(sheet_bmp, pitch * scale), ref_path, grown(ref_bmp, pitch))
    doc = sd.doc
    # SheetDoc.cells is `doc.get("cells") or []`, so it IS the document's list
    # whenever the sheet has any cell at all. Appending to the document and
    # re-reading the list back keeps the two from diverging on the one sheet
    # where they are separate objects (a sidecar with an empty cells[]).
    if not isinstance(doc.get("cells"), list):
        doc["cells"] = []
    doc["cells"].append(cell)
    try:
        sd.json_path.write_text(json.dumps(doc, indent=2) + "\n", encoding="utf-8")
    except OSError:
        # The cell is emitted only if both image writes succeeded (OPEN 3(a));
        # the converse has to hold too, or the pack keeps a row no cell uses
        # and every later build fails on the size it derives.
        if backups:
            for dest, data in backups.items():
                dest.write_bytes(data)
        doc["cells"].pop()
        raise
    sd.cells = doc["cells"]
    out.append(f"wrote: {sd.json_path.name} now holds {len(sd.cells)} cells")
    return out


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(
        prog="mep_add_cell.py",
        description="Place an unplaced MEP sheet cell (ADR-0216) into a pack's sheets.")
    ap.add_argument("pack", help="the pack, game or textures/sheets folder")
    ap.add_argument("source", nargs="?", default="",
                    help="the copied text: a file path, or '-' for stdin. Defaults to stdin when "
                         "stdin is not a terminal.")
    ap.add_argument("--paste", action="store_true", help="read the real system clipboard")
    ap.add_argument("--sheet", default="", help="override the destination sidecar")
    ap.add_argument("--label", default="", help="the cell's optional label")
    ap.add_argument("--allow-sprite-palette", action="store_true",
                    help="place a key whose palette leads with FF (see the module docstring)")
    ap.add_argument("--dry-run", action="store_true", help="report the placement, write nothing")
    args = ap.parse_args(argv)

    try:
        if args.paste:
            text = read_system_clipboard()
        elif args.source and args.source != "-":
            text = Path(args.source).read_text(encoding="utf-8")
        elif args.source == "-" or not sys.stdin.isatty():
            text = sys.stdin.read()
        else:
            raise AddCellError("no input: pass --paste, a file path, or pipe the copied text in")

        payloads = parse_payload(text)
        sheets_dir = find_sheets_dir(Path(args.pack))
        docs, _claimed = mep_build._load_sheet_docs(sheets_dir)
        if not docs:
            raise AddCellError(f"{sheets_dir}: no usable ADR-0153 v1 sidecar")
        scale = mep_build._sheet_scale(docs) or 1

        for payload in payloads:
            if looks_like_a_sprite_palette(payload["tiles"]) and not args.allow_sprite_palette:
                raise AddCellError(
                    "this key's palette leads with FF, which is how a sprite's transparent color 0 "
                    "is packed. ADR-0178 §6 allows a flip-baked key only on a sprite sheet, and "
                    "this tool places background keys, so it refuses rather than guess a "
                    "destination. If the key really is a background one, pass "
                    "--allow-sprite-palette.")
            sd = choose_sheet(docs, args.sheet)
            for line in place(sheets_dir, docs, sd, payload, scale, args.label, args.dry_run):
                print(line)
    except (AddCellError, mep_build.BuildError) as err:
        print(f"error: {err}", file=sys.stderr)
        return 1
    except OSError as err:
        print(f"error: {err}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
