"""The guide sentinel colour and the per-cell scan that catches a wrong export.

ADR-0220 §4 (F12.11). The kit's `.ora` carries two drawn layers — `guides` and
`palettes` — in one sentinel colour, `#FF00FD` at alpha 255 (amended 2026-09-22:
`#FF00FF` collided with the recorder's own unpainted-cell fill `0xFFFF00FF`;
see ADR-0220 §4). Both are hidden
for export; an artist who leaves one visible ships grid lines or swatches into
the flat PNG, and `mep_build._EditedProbe` would read every touched cell as
painted art. This module is the one place the sentinel is spelled, and the one
scan both gates run:

* `ora_writer` asserts the sentinel absent from the artwork and its twin
  **before** writing (the value is checked absent, never argued absent);
* `mep_lint` scans every cell rectangle of every sheet it can pair with a
  sidecar and fails the pack naming the cell (`index` and `(x, y)`).

Exact equality, no tolerance (ADR-0220 §4): an approximate match would fire on
dark magenta art and pass on a blended grid, the opposite of both.

The scan reads the flat sheet PNG and its sidecar and nothing else — it never
opens a `.ora` (ADR-0220 §5). Python 3, standard library only; the PNG decoder
is `mep_build._png_pixels`, the tree's single decoder.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import mep_build  # noqa: E402 — `_png_pixels`, the tree's single PNG decoder

SENTINEL_RGBA = (0xFF, 0x00, 0xFD, 0xFF)
SENTINEL_HEX = "#FF00FD"
_SENTINEL_RGBA_BYTES = bytes(SENTINEL_RGBA)
_SENTINEL_RGB_BYTES = bytes(SENTINEL_RGBA[:3])


class _BytesPath:
    """Duck-typed stand-in for `Path` so `mep_build._png_pixels` decodes bytes
    a zip member yielded without a temp file."""

    def __init__(self, data: bytes):
        self._data = data

    def read_bytes(self) -> bytes:
        return self._data


def decode_png(data: bytes):
    """`mep_build._Bitmap` for an 8-bit RGB/RGBA PNG held in memory, or None."""
    return mep_build._png_pixels(_BytesPath(data))


def rgba_has_sentinel(px: bytes) -> bool:
    """True when a flat RGBA buffer holds one exact sentinel pixel."""
    needle = _SENTINEL_RGBA_BYTES
    i = px.find(needle)
    while i != -1:
        if i % 4 == 0:
            return True
        i = px.find(needle, i + 1)
    return False


def band_has_sentinel(band: bytes, channels: int) -> bool:
    """True when one scanline band of `channels` bytes per pixel holds the
    sentinel. An RGB export has no alpha to compare, so `FF00FD` alone counts:
    a flat PNG that dropped alpha still carries the grid it should not."""
    needle = _SENTINEL_RGBA_BYTES if channels == 4 else _SENTINEL_RGB_BYTES
    i = band.find(needle)
    while i != -1:
        if i % channels == 0:
            return True
        i = band.find(needle, i + 1)
    return False


def cell_has_sentinel(bitmap, x: int, y: int, w: int, h: int) -> bool:
    """Scan one cell rectangle (pixel coordinates, clipped to the image)."""
    x0, y0 = max(0, x), max(0, y)
    x1, y1 = min(bitmap.width, x + w), min(bitmap.height, y + h)
    if x1 <= x0 or y1 <= y0:
        return False
    for row in range(y0, y1):
        if band_has_sentinel(bitmap.band(row, x0, x1), bitmap.channels):
            return True
    return False


def cell_rects(doc: dict, pack_scale: int):
    """`[(index, x, y, w, h)]` in **pixels of the sheet PNG** for a sidecar.

    Two coordinate conventions exist in the tree and this is where they meet:
    a composed / recorder sheet (`sheets/*.json`) keeps `cells[].x/y` at 1x and
    the PNG is at the pack's `<scale>`; a CHR page sidecar (`kind: "chr"`,
    `scale` field) keeps them already scaled, the way `Page.xy` writes them.
    `(x, y)` in the returned tuple is the sidecar's own value, because that is
    the number a person finds in the file."""
    kind = str(doc.get("kind") or "")
    cell = doc.get("cell") if isinstance(doc.get("cell"), dict) else {}
    unit = int(doc.get("gridUnit") or 8)
    cw = int(cell.get("w") or unit)
    ch = int(cell.get("h") or unit)
    if kind == "chr":
        s = max(1, int(doc.get("scale") or 1))
        factor = 1
    else:
        s = max(1, int(pack_scale or 1))
        factor = s
    out = []
    for c in doc.get("cells") or []:
        if not isinstance(c, dict):
            continue
        try:
            x, y = int(c["x"]), int(c["y"])
        except (KeyError, TypeError, ValueError):
            continue
        out.append((c.get("index"), x, y, x * factor, y * factor, cw * s, ch * s))
    return out


def scan_sidecar(doc: dict, bitmap, pack_scale: int):
    """Every cell of `doc` whose rectangle on `bitmap` holds the sentinel, as
    `[(index, x, y)]` in the sidecar's own coordinates."""
    hits = []
    for index, x, y, px, py, w, h in cell_rects(doc, pack_scale):
        if cell_has_sentinel(bitmap, px, py, w, h):
            hits.append((index, x, y))
    return hits
