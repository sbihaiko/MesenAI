#!/usr/bin/env python3
"""`<bgCellRecord>` — ADR-0236 (F14.11, issue #499): the frame a captured
`<background>` was taken from, one key per 32x30 screen cell.

The C++ side of this grammar is `Core/NES/HdPacks/HdCaptureCellGuard.h`:
`HdCellKeyRecord::ToString` writes it, `HdCellKeyRecord::Parse` reads it, and
`HdCellGuard` is what turns it into the render-time mask. This module is the
Python mirror the tools need. It is a **mirror**, not an owner: `mep_lint`
validates a pack's line with it and `mep_carry` keeps the line tied to the
`<background>` it belongs to, and neither may accept a line the loader would
refuse — the classic drift here is a pack that passes lint and then loses its
guard in the emulator.

The line, in full::

    <bgCellRecord>N|I00000042:1B1A1918|D00112233445566778899AABBCCDDEEFF:1B1A1918;000001...

    <dict>;<cells>

- `<dict>` — the screen's distinct keys, `|`-separated. One entry is one of:
  - `N` — the run time named no tile at that cell (background off for that
    pixel, or the line's leftmost 8 pixels clipped). A key, not an absence:
    the emulator compares it like any other, which is what keeps a clipped
    column drawing the art it was captured with.
  - `I<8 hex index>:<8 hex palette>` — ADR-0172, a CHR ROM game: index +
    palette.
  - `D<32 hex tile data>:<8 hex palette>` — a CHR RAM game: the 16 drawn
    bytes + palette.
- `<cells>` — exactly 960 cell indices, row-major, 32 per row, each written
  with the same number of hex digits. The width is derived from the
  dictionary size by the writer and by the reader alike
  (`cell_index_width`), so the plane needs no separator and no length field:
  a line whose length does not match its dictionary is a parse error rather
  than a silently re-indexed grid — a grid read one cell off is a wrong
  screen, which is the whole class of bug this tag exists to close.

Why the record exists is in the C++ header and in ADR-0236: a `<background>`
is a whole screen of pixels and carried nothing about the frame it came from,
so it drew on every frame its few `tileAtPosition` probes matched, and Ninja
Gaiden's `screen001` froze the HUD on a frame it was never frozen for. The
record is **positional** — a set of keys would pass a live HUD digit that
appears elsewhere on the same capture, which is #499 again.
"""

from __future__ import annotations

import re

TAG = "<bgCellRecord>"
ROWS = 30
COLS = 32
CELL_COUNT = ROWS * COLS  # 960

TAG_RE = re.compile(r"^<bgCellRecord>")
KEY_RE = re.compile(r"^(N|I([0-9A-Fa-f]{8}):([0-9A-Fa-f]{8})|D([0-9A-Fa-f]{32}):([0-9A-Fa-f]{8}))$")


def is_tag_line(line: str) -> bool:
    """True for a line that opens the tag. What follows is `payload`."""
    return bool(TAG_RE.match(line))


def payload(line: str) -> str:
    return line[len(TAG):]


def cell_index_width(key_count: int) -> int:
    """Hex digits per cell index, from the dictionary size — the same rule as
    `HdCellKeyRecord::CellIndexWidth`."""
    width = 2
    while width < 4 and (1 << (4 * width)) < key_count:
        width += 1
    return width


def parse(line_or_payload: str) -> tuple[list[str], str]:
    """`(keys, cells)` for a tag line or a bare payload.

    Raises `ValueError` with the loader's own reason — the loader logs
    `Invalid <bgCellRecord>: <reason>` and drops the record, leaving the
    `<background>` to draw as it did before the tag existed.
    """
    text = payload(line_or_payload) if is_tag_line(line_or_payload) else line_or_payload
    if ";" not in text:
        raise ValueError("missing ';' between the dictionary and the cell grid")
    dictionary, cells = text.split(";", 1)
    if not dictionary:
        raise ValueError("empty key dictionary")
    keys = dictionary.split("|")
    for key in keys:
        if not KEY_RE.match(key):
            raise ValueError(f"invalid key entry {key!r} (expected N, I<8hex>:<8hex> or D<32hex>:<8hex>)")
    if len(keys) > 0x10000:
        raise ValueError("more than 65536 distinct keys")
    width = cell_index_width(len(keys))
    if len(cells) != CELL_COUNT * width:
        raise ValueError(
            f"cell grid is {len(cells)} hex digits, expected {CELL_COUNT * width} "
            f"({CELL_COUNT} cells x {width})")
    for i in range(CELL_COUNT):
        index = int(cells[i * width:(i + 1) * width], 16)
        if index >= len(keys):
            raise ValueError(f"cell {i} names key {index} of {len(keys)}")
    return keys, cells
