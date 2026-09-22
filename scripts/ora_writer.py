"""Write the layered OpenRaster (`.ora`) twin of a kit painting surface.

ADR-0220 (F12.11). Beside every surface PNG the kit writes `<name>.ora` — a
zip GIMP, Krita and MyPaint open natively — produced in the same pass and from
the same in-memory canvas as `<name>.png` and `<name>.orig.png`, so the three
files cannot disagree by construction (§1). The container is fixed so that its
validity is a test (§2):

    mimetype                  first entry, ZIP_STORED, exactly `image/openraster`
    stack.xml                 <image version="0.0.5" w h><stack>…
    mergedimage.png           the composite as the file opens (hidden layers hidden)
    Thumbnails/thumbnail.png  mergedimage.png, nearest-neighbour, max side 256
    data/orig.png             layer 1
    data/context.png          layer 2, only when present
    data/paint.png            layer 3
    data/guides.png           layer 4
    data/palettes.png         layer 5

Members are written in that order with a fixed timestamp and fixed
compression, so the same inputs give the same bytes.

The five layers, bottom to top (§3):

| layer      | flags                                     | present                 |
|------------|-------------------------------------------|-------------------------|
| `orig`     | visible, opacity 1.0, edit-locked         | always                  |
| `context`  | visible, opacity 0.5                      | iff a stage context     |
| `paint`    | visible, opacity 1.0 — fully transparent  | always                  |
| `guides`   | hidden, opacity 1.0, edit-locked          | always                  |
| `palettes` | hidden, opacity 1.0, edit-locked          | always                  |

`paint` is the topmost **visible** layer by construction — the two above it
are hidden — which is the strongest statement `stack.xml` can make about the
layer the artist lands on (OpenRaster has no "selected layer" attribute).

Everything this module draws is drawn in one sentinel colour, `#FF00FD` at
alpha 255 (`mep_sentinel.SENTINEL_RGBA`), and the artwork and its twin are
**asserted** free of that colour before anything is written (§4). Captions in
the `palettes` band are knocked *out of* the sentinel background (transparent
glyphs), so that band too is sentinel-only. The `context` band lives outside
the cell grid and is clipped so no pixel of it falls inside a cell rectangle;
the canvas and the `*.orig.png` twin grow together to hold it (§3).

**Write-only (§5).** This module exposes no read entry point: nothing here
opens a `.ora`, and nothing under `scripts/` may — the return path is the flat
PNG the artist exports over the F12.4 name (ADR-0213 §3), and only that.
`mergedimage.png` is a display cache. A source-level test holds this line.

Layer PNGs are encoded by `sheet_repaint.write_png`, the encoder the generators
already share, captured in memory through a duck-typed sink. Python 3, standard
library only (ADR-0165).
"""

import io
import struct
import sys
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import mep_sentinel  # noqa: E402 — the sentinel colour and the per-cell scan
import sheet_repaint  # noqa: E402 — Image / write_png, the shared RGBA canvas
from sheet_repaint import Image  # noqa: E402

SENTINEL = mep_sentinel.SENTINEL_RGBA
MIMETYPE = b"image/openraster"
ORA_VERSION = "0.0.5"
THUMBNAIL_MAX = 256
# Fixed zip timestamp (the DOS epoch) so a re-run is byte-identical (§2).
_ZIP_TIME = (1980, 1, 1, 0, 0, 0)
# Vertical gap between the cell grid and the context band, in 1x pixels.
CONTEXT_GAP = 4
# Diagonal hatch period over a `seen: false` cell, in canvas pixels.
HATCH_STEP = 4

LAYER_ORDER = ("orig", "context", "paint", "guides", "palettes")


class OraError(Exception):
    """Refusal with a message a person can act on — never a silent file."""


# --- a 3x5 pixel font, the only text this module can draw ------------------

_GLYPHS = {
    "0": ("###", "# #", "# #", "# #", "###"), "1": (" # ", "## ", " # ", " # ", "###"),
    "2": ("###", "  #", "###", "#  ", "###"), "3": ("###", "  #", " ##", "  #", "###"),
    "4": ("# #", "# #", "###", "  #", "  #"), "5": ("###", "#  ", "###", "  #", "###"),
    "6": ("###", "#  ", "###", "# #", "###"), "7": ("###", "  #", " # ", " # ", " # "),
    "8": ("###", "# #", "###", "# #", "###"), "9": ("###", "# #", "###", "  #", "###"),
    "A": (" # ", "# #", "###", "# #", "# #"), "B": ("## ", "# #", "## ", "# #", "## "),
    "C": ("###", "#  ", "#  ", "#  ", "###"), "D": ("## ", "# #", "# #", "# #", "## "),
    "E": ("###", "#  ", "## ", "#  ", "###"), "F": ("###", "#  ", "## ", "#  ", "#  "),
    "G": ("###", "#  ", "# #", "# #", "###"), "H": ("# #", "# #", "###", "# #", "# #"),
    "I": ("###", " # ", " # ", " # ", "###"), "J": ("  #", "  #", "  #", "# #", "###"),
    "K": ("# #", "# #", "## ", "# #", "# #"), "L": ("#  ", "#  ", "#  ", "#  ", "###"),
    "M": ("# #", "###", "###", "# #", "# #"), "N": ("## ", "# #", "# #", "# #", "# #"),
    "O": ("###", "# #", "# #", "# #", "###"), "P": ("###", "# #", "###", "#  ", "#  "),
    "Q": ("###", "# #", "# #", "###", "  #"), "R": ("## ", "# #", "## ", "# #", "# #"),
    "S": ("###", "#  ", "###", "  #", "###"), "T": ("###", " # ", " # ", " # ", " # "),
    "U": ("# #", "# #", "# #", "# #", "###"), "V": ("# #", "# #", "# #", "# #", " # "),
    "W": ("# #", "# #", "###", "###", "# #"), "X": ("# #", "# #", " # ", "# #", "# #"),
    "Y": ("# #", "# #", " # ", " # ", " # "), "Z": ("###", "  #", " # ", "#  ", "###"),
    " ": ("   ", "   ", "   ", "   ", "   "), "-": ("   ", "   ", "###", "   ", "   "),
    "_": ("   ", "   ", "   ", "   ", "###"), ".": ("   ", "   ", "   ", "   ", " # "),
    ":": ("   ", " # ", "   ", " # ", "   "), "=": ("   ", "###", "   ", "###", "   "),
    "/": ("  #", "  #", " # ", "#  ", "#  "), "(": (" # ", "#  ", "#  ", "#  ", " # "),
    ")": (" # ", "  #", "  #", "  #", " # "), "#": ("# #", "###", "# #", "###", "# #"),
    "$": (" ##", "## ", " # ", " ##", "## "), ",": ("   ", "   ", "   ", " # ", "#  "),
    "?": ("###", "  #", " ##", "   ", " # "), "+": ("   ", " # ", "###", " # ", "   "),
}
GLYPH_W, GLYPH_H = 3, 5


def text_width(text: str, scale: int = 1) -> int:
    return (len(text) * (GLYPH_W + 1) - 1) * scale if text else 0


def draw_text(img: Image, x: int, y: int, text: str, rgba, scale: int = 1):
    """Uppercase 3x5 glyphs, each font pixel `scale` canvas pixels; an unknown
    character draws as `?`. Clipped to the image."""
    pen = x
    for ch in text.upper():
        rows = _GLYPHS.get(ch) or _GLYPHS["?"]
        for gy, row in enumerate(rows):
            for gx, bit in enumerate(row):
                if bit != "#":
                    continue
                _fill_rect(img, pen + gx * scale, y + gy * scale, scale, scale, rgba)
        pen += (GLYPH_W + 1) * scale


# --- drawing primitives -------------------------------------------------------

def _fill_rect(img: Image, x: int, y: int, w: int, h: int, rgba):
    x0, y0 = max(0, x), max(0, y)
    x1, y1 = min(img.width, x + w), min(img.height, y + h)
    if x1 <= x0 or y1 <= y0:
        return
    row = bytes(rgba) * (x1 - x0)
    for yy in range(y0, y1):
        o = img.offset(x0, yy)
        img.px[o:o + len(row)] = row


def _outline_rect(img: Image, x: int, y: int, w: int, h: int, rgba):
    _fill_rect(img, x, y, w, 1, rgba)
    _fill_rect(img, x, y + h - 1, w, 1, rgba)
    _fill_rect(img, x, y, 1, h, rgba)
    _fill_rect(img, x + w - 1, y, 1, h, rgba)


def _hatch_rect(img: Image, x: int, y: int, w: int, h: int, rgba, step: int = HATCH_STEP):
    for yy in range(max(0, y), min(img.height, y + h)):
        for xx in range(max(0, x), min(img.width, x + w)):
            if (xx - x + yy - y) % step == 0:
                img.set(xx, yy, rgba)


def _clear_rect(img: Image, x: int, y: int, w: int, h: int):
    _fill_rect(img, x, y, w, h, (0, 0, 0, 0))


def _over(dst: Image, src: Image, opacity: float):
    """`svg:src-over` of `src` at `opacity` onto `dst`, in place. Rows of
    `src` that are entirely zero are skipped, which is what keeps the merged
    image cheap: the only non-empty rows are the context band's."""
    w = min(dst.width, src.width)
    for y in range(min(dst.height, src.height)):
        so = src.offset(0, y)
        row = src.px[so:so + w * 4]
        if not any(row):
            continue
        do = dst.offset(0, y)
        for x in range(w):
            sa = row[x * 4 + 3] * opacity / 255.0
            if sa <= 0:
                continue
            i = do + x * 4
            da = dst.px[i + 3] / 255.0
            oa = sa + da * (1 - sa)
            if oa <= 0:
                continue
            for c in range(3):
                sc, dc = row[x * 4 + c], dst.px[i + c]
                dst.px[i + c] = int(round((sc * sa + dc * da * (1 - sa)) / oa))
            dst.px[i + 3] = int(round(oa * 255))


def _thumbnail(img: Image) -> Image:
    """Nearest-neighbour, max side `THUMBNAIL_MAX` — never a resample (ADR-0154 §7)."""
    k = max(1, -(-max(img.width, img.height) // THUMBNAIL_MAX))
    if k == 1:
        return img.clone()
    w, h = max(1, img.width // k), max(1, img.height // k)
    out = Image(w, h)
    for y in range(h):
        for x in range(w):
            so = img.offset(x * k, y * k)
            do = out.offset(x, y)
            out.px[do:do + 4] = img.px[so:so + 4]
    return out


class _Sink:
    """What `sheet_repaint.write_png` needs of a `Path`: `write_bytes`. Lets the
    shared encoder produce bytes without a temp file and without a second
    encoder in the tree."""

    __slots__ = ("data",)

    def write_bytes(self, data: bytes):
        self.data = data


def encode_png(img: Image) -> bytes:
    sink = _Sink()
    sheet_repaint.write_png(sink, img)
    return sink.data


# --- NES palette swatches -------------------------------------------------------

def nes_swatches(palette_hexes) -> list:
    """`[[(r, g, b), ×4], …]` for `hires.txt` palette strings (`"0F162A30"`),
    one entry per distinct string, in the order given. Lazy import: the table
    is `artist_map.NES_PALETTE`, and `artist_map` imports this module."""
    from artist_map import NES_PALETTE  # noqa: PLC0415 — see docstring
    out, seen = [], set()
    for hx in palette_hexes:
        hx = str(hx or "").strip().upper()
        if len(hx) != 8 or hx in seen:
            continue
        try:
            out.append([NES_PALETTE[int(hx[i:i + 2], 16) & 0x3F] for i in range(0, 8, 2)])
        except ValueError:
            continue
        seen.add(hx)
    return out


# --- the surface --------------------------------------------------------------

class Surface:
    """What one kit surface writes: the (possibly grown) painted canvas, its
    twin, the `.ora` bytes and the layer names in bottom-to-top order."""

    __slots__ = ("painted", "orig", "ora", "layers")

    def __init__(self, painted, orig, ora, layers):
        self.painted = painted
        self.orig = orig
        self.ora = ora
        self.layers = layers


def build_surface(painted: Image, orig: Image, rects, *, captions=(), swatches=(),
                  wildcard: str = None, context: Image = None, font_scale: int = None) -> Surface:
    """Compose the layers for one surface and return them with the `.ora`.

    `painted` is the sheet at the pack's scale; `orig` its twin at 1x or at the
    same scale (the CHR pages' convention) — the integer ratio is the twin
    scale. `rects` are the cell rectangles in **canvas pixels** as
    `{"index", "x", "y", "w", "h", "seen"?}`; `captions` are `(x, y, text)` in
    canvas pixels drawn on `guides`; `swatches` are `[[(r,g,b)×n], …]` for the
    `palettes` band, or empty with `wildcard` naming what the band says
    instead (a static page: `defaultTile = Y`). `context` is the 1x stitched
    map crop around the surface's subject, or None when no cell has a stage
    position (ADR-0220 §3) — when given, the canvas and the twin grow by a
    context band below the grid and the crop is placed there at 1x."""
    if orig.width <= 0 or painted.width % orig.width or painted.height % orig.height:
        raise OraError(f"twin {orig.width}x{orig.height} does not divide the sheet "
                       f"{painted.width}x{painted.height}")
    twin_scale = painted.width // orig.width
    if painted.height // orig.height != twin_scale:
        raise OraError("sheet and twin are not one integer scale apart")
    if mep_sentinel.rgba_has_sentinel(painted.px) or mep_sentinel.rgba_has_sentinel(orig.px):
        # §4: checked absent from what we guard, not argued absent from the hardware.
        raise OraError(f"the artwork or its twin already contains the guide sentinel "
                       f"{mep_sentinel.SENTINEL_HEX}; the .ora guard would be blind — "
                       "amend ADR-0220 §4 with another triplet before generating this kit")
    font_scale = max(1, int(font_scale or twin_scale))

    # Canvas geometry, with the context band when there is one.
    W, H = painted.width, painted.height
    band_top = None
    if context is not None:
        gap = CONTEXT_GAP * twin_scale
        band_h = -(-context.height // twin_scale) * twin_scale
        W = max(W, -(-context.width // twin_scale) * twin_scale)
        band_top = H + gap
        H = band_top + band_h
    grown = Image(W, H)
    grown.paste(painted, 0, 0)
    twin = Image(W // twin_scale, H // twin_scale)
    twin.paste(orig, 0, 0)

    layers = []
    orig_layer = twin.upscale(twin_scale) if twin_scale > 1 else twin.clone()
    layers.append(("orig", orig_layer, "visible", "1.0", True))
    if context is not None:
        ctx = Image(W, H)
        ctx.paste(context.crop(0, 0, min(context.width, W), context.height), 0, band_top)
        for r in rects:   # §3: no pixel of context inside a cell rectangle
            _clear_rect(ctx, r["x"], r["y"], r["w"], r["h"])
        layers.append(("context", ctx, "visible", "0.5", False))
    layers.append(("paint", Image(W, H), "visible", "1.0", False))

    guides = Image(W, H)
    for r in rects:
        _outline_rect(guides, r["x"], r["y"], r["w"], r["h"], SENTINEL)
        if r.get("seen") is False:
            _hatch_rect(guides, r["x"], r["y"], r["w"], r["h"], SENTINEL)
    for cx, cy, text in captions:
        draw_text(guides, cx, cy, str(text), SENTINEL, font_scale)
    layers.append(("guides", guides, "hidden", "1.0", True))

    layers.append(("palettes", _palettes_band(W, H, rects, list(swatches), wildcard, font_scale),
                   "hidden", "1.0", True))

    merged = orig_layer.clone()
    for name, img, vis, opacity, _locked in layers:
        if vis == "visible" and name != "orig":
            _over(merged, img, float(opacity))

    ora = _zip([(name, img, vis, opacity, locked) for name, img, vis, opacity, locked in layers],
               merged, W, H)
    return Surface(grown, twin, ora, [name for name, *_ in layers])


def _palettes_band(W, H, rects, swatches, wildcard, font_scale) -> Image:
    """§4: one cell-tall band on the first cell row, sentinel background, each
    swatch inset one pixel top and bottom and separated by a one-pixel
    sentinel column; palettes separated by three. With no swatches the band
    states `wildcard` as knocked-out glyphs. Every cell the band crosses holds
    exact sentinel pixels, so a visible `palettes` fails the same check as a
    visible `guides`."""
    img = Image(W, H)
    if not rects:
        return img
    first = min(rects, key=lambda r: (r["y"], r["x"]))
    top, band_h = first["y"], first["h"]
    _fill_rect(img, 0, top, W, band_h, SENTINEL)
    if swatches:
        sw = max(2, band_h - 2)
        pen = 1
        for pal in swatches:
            for rgb in pal:
                if pen + sw > W:
                    return img
                _fill_rect(img, pen, top + 1, sw, band_h - 2, (rgb[0], rgb[1], rgb[2], 0xFF))
                pen += sw + 1
            pen += 2
        return img
    text = (wildcard or "no palette observed").upper()
    fs = font_scale
    while fs > 1 and GLYPH_H * fs > band_h - 2:
        fs -= 1
    ty = top + max(1, (band_h - GLYPH_H * fs) // 2)
    pen = 2 * fs
    for ch in text:
        rows = _GLYPHS.get(ch) or _GLYPHS["?"]
        for gy, row in enumerate(rows):
            for gx, bit in enumerate(row):
                if bit == "#":
                    _clear_rect(img, pen + gx * fs, ty + gy * fs, fs, fs)
        pen += (GLYPH_W + 1) * fs
    return img


def _stack_xml(layers, W, H) -> bytes:
    image = ET.Element("image", {"version": ORA_VERSION, "w": str(W), "h": str(H),
                                 "xres": "72", "yres": "72"})
    stack = ET.SubElement(image, "stack")
    for name, _img, vis, opacity, locked in reversed(layers):   # topmost first
        attrs = {"name": name, "src": f"data/{name}.png", "x": "0", "y": "0",
                 "opacity": opacity, "visibility": vis, "composite-op": "svg:src-over"}
        if locked:
            attrs["edit-locked"] = "true"
        ET.SubElement(stack, "layer", attrs)
    return ET.tostring(image, encoding="UTF-8", xml_declaration=True)


def _zip(layers, merged: Image, W, H) -> bytes:
    members = [("mimetype", MIMETYPE, zipfile.ZIP_STORED),
               ("stack.xml", _stack_xml(layers, W, H), zipfile.ZIP_DEFLATED),
               ("mergedimage.png", encode_png(merged), zipfile.ZIP_DEFLATED),
               ("Thumbnails/thumbnail.png", encode_png(_thumbnail(merged)), zipfile.ZIP_DEFLATED)]
    for name, img, *_ in layers:
        members.append((f"data/{name}.png", encode_png(img), zipfile.ZIP_DEFLATED))
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w") as z:
        for name, data, comp in members:
            zi = zipfile.ZipInfo(name, date_time=_ZIP_TIME)
            zi.compress_type = comp
            zi.create_system = 3
            zi.external_attr = 0o644 << 16
            z.writestr(zi, data, compress_type=comp, compresslevel=9 if comp else None)
    return buf.getvalue()


# --- the two writers the generators call ------------------------------------------

def ora_path(png_path: Path) -> Path:
    """`<name>.ora` beside `<name>.png` — the F12.4 stem, another extension (§1)."""
    png_path = Path(png_path)
    return png_path.with_name(png_path.name[:-len(".png")] + ".ora")


def write_surface(out_dir: Path, asset_name: str, painted: Image, orig: Image, rects, **kw) -> Surface:
    """Write `<asset_name>`, its `.orig.png` twin and its `.ora` into `out_dir`
    from one canvas. `asset_name` is the validated F12.4 name (`*.png`)."""
    out_dir = Path(out_dir)
    surface = build_surface(painted, orig, rects, **kw)
    stem = asset_name[:-len(".png")]
    sheet_repaint.write_png(out_dir / asset_name, surface.painted)
    sheet_repaint.write_png(out_dir / f"{stem}.orig.png", surface.orig)
    (out_dir / f"{stem}.ora").write_bytes(surface.ora)
    return surface


def chr_rects(cells, cell_px: int):
    return [{"index": c["index"], "x": c["x"], "y": c["y"], "w": cell_px, "h": cell_px,
             "seen": c.get("seen")} for c in cells]


def write_chr_surface(out_chr: Path, asset_name: str, hd: Image, orig, cells, page):
    """The `artist_chr_kit.py` write pass, one call: the page PNG, and — when
    the page has a twin — the twin and the `.ora`. A recorded page without a
    twin has no `orig` layer to write, so it gets the PNG alone, as before.

    Captions are each cell's tile index in hex (the sidecar's `index`, the
    same number `hires.txt` keys the tile by). Swatches are the palettes the
    recording observed on this page; a page with none (a static page — every
    cell `fill`, `defaultTile = Y`, ADR-0219) says the wildcard instead.

    A recorded page's `empty` cells wear the recorder's unpainted fill
    `0xFFFF00FF`; that is why the sentinel is `#FF00FD` and not `#FF00FF`
    (ADR-0220 §4, amended 2026-09-22) — the §4 assertion below caught the
    collision while F12.11 was being implemented."""
    out_chr = Path(out_chr)
    if orig is None:
        sheet_repaint.write_png(out_chr / asset_name, hd)
        return None
    observed = [c.get("palette") for c in cells
                if c.get("seen") is True or c.get("paletteObserved")]
    swatches = nes_swatches(sorted({p for p in observed if p}))
    captions = [(c["x"] + 1, c["y"] + 1, f"{int(c['index']):02X}") for c in cells
                if c.get("state") not in ("empty",)]
    return write_surface(out_chr, asset_name, hd, orig, chr_rects(cells, page.cell_px),
                         captions=captions, swatches=swatches,
                         wildcard=None if swatches else "defaultTile = Y")
