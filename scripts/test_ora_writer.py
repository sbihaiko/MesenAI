"""Headless suite for the layered `.ora` beside every kit surface (ADR-0220, F12.11).

What is tested is the contract, section by section:

* §2 — the container: `mimetype` first and stored, the member order, and the
  same inputs giving the same bytes twice;
* §3 — the layers: five with a `context`, four without, the flags per layer,
  `paint` empty and the topmost visible layer, `orig` the twin's pixels, the
  `context` band clipped outside every cell with the twin grown identically;
* §4 — the sentinel: asserted absent from the artwork before writing, the
  `palettes` band crossing the first cell row in sentinel, and `mep_lint`
  refusing an export that carries it, naming the cell (`index`, `(x, y)`);
* §5 — write-only: a source-level scan that no module under `scripts/` opens
  a `.ora` for reading and that the writer exposes no read entry point;
* the generators: a composed sheet and a CHR page both land with their `.ora`.

Run:  python3 scripts/test_ora_writer.py
"""

import io
import json
import re
import sys
import tempfile
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import mep_lint  # noqa: E402
import mep_sentinel  # noqa: E402
import ora_writer as W  # noqa: E402
from sheet_repaint import Image  # noqa: E402

_FAILURES = []
SCRIPTS = Path(__file__).resolve().parent


def check(cond, name, detail=""):
    if cond:
        print(f"ok   {name}")
    else:
        print(f"FAIL {name}: {detail}")
        _FAILURES.append(name)


# --- fixtures -----------------------------------------------------------------

UNIT, GUTTER, SCALE = 8, 1, 2


def _twin(cols=2, rows=1, colour=(0x40, 0x80, 0xC0, 0xFF)):
    """A 1x composed-sheet twin: `cols` x `rows` cells of `UNIT` with a 1 px gutter."""
    img = Image(cols * (UNIT + GUTTER) + GUTTER, rows * (UNIT + GUTTER) + GUTTER)
    rects = []
    for r in range(rows):
        for c in range(cols):
            x, y = GUTTER + c * (UNIT + GUTTER), GUTTER + r * (UNIT + GUTTER)
            for yy in range(y, y + UNIT):
                for xx in range(x, x + UNIT):
                    img.set(xx, yy, colour)
            rects.append({"index": r * cols + c, "x": x, "y": y, "w": UNIT, "h": UNIT})
    return img, rects


def _surface(**kw):
    orig, rects1x = _twin()
    painted = orig.upscale(SCALE)
    rects = [{**r, "x": r["x"] * SCALE, "y": r["y"] * SCALE,
              "w": r["w"] * SCALE, "h": r["h"] * SCALE} for r in rects1x]
    return W.build_surface(painted, orig, rects, **kw), rects


def _context():
    ctx = Image(40, 12)
    for y in range(12):
        for x in range(40):
            ctx.set(x, y, (0x20, 0xA0, 0x20, 0xFF))
    return ctx


def _members(ora: bytes):
    with zipfile.ZipFile(io.BytesIO(ora)) as z:
        return z.infolist(), {i.filename: z.read(i.filename) for i in z.infolist()}


def _layers(stack_xml: bytes):
    root = ET.fromstring(stack_xml)
    return root, [dict(el.attrib) for el in root.find("stack")]


def _decode(png: bytes) -> Image:
    bm = mep_sentinel.decode_png(png)
    img = Image(bm.width, bm.height)
    img.px = bytearray(bm.raw)
    return img


def _pixels_in(img: Image, rect):
    for yy in range(rect["y"], rect["y"] + rect["h"]):
        for xx in range(rect["x"], rect["x"] + rect["w"]):
            yield img.get(xx, yy)


# --- §2 the container ------------------------------------------------------------

def test_container_members_in_order_with_mimetype_first_and_stored():
    surface, _ = _surface(context=_context(), swatches=[[(1, 2, 3)] * 4])
    infos, data = _members(surface.ora)
    names = [i.filename for i in infos]
    check(names == ["mimetype", "stack.xml", "mergedimage.png", "Thumbnails/thumbnail.png",
                    "data/orig.png", "data/context.png", "data/paint.png",
                    "data/guides.png", "data/palettes.png"],
          "members in the §2 order", str(names))
    check(infos[0].filename == "mimetype" and infos[0].compress_type == zipfile.ZIP_STORED,
          "mimetype is the first entry and ZIP_STORED")
    check(data["mimetype"] == b"image/openraster", "mimetype is exactly image/openraster",
          repr(data["mimetype"]))
    check(all(i.compress_type == zipfile.ZIP_DEFLATED for i in infos[1:]),
          "every other member is deflated")
    check(all(i.date_time == (1980, 1, 1, 0, 0, 0) for i in infos), "fixed timestamps")
    root, layers = _layers(data["stack.xml"])
    check(root.tag == "image" and root.get("version") == "0.0.5",
          "stack.xml is an <image version=0.0.5>")
    check(root.get("w") == str(surface.painted.width) and root.get("h") == str(surface.painted.height),
          "image w/h are the canvas size", f"{root.attrib} vs {surface.painted.width}x{surface.painted.height}")
    check(all(name in data for name in ("mergedimage.png", "Thumbnails/thumbnail.png")),
          "mergedimage and thumbnail present")
    thumb = _decode(data["Thumbnails/thumbnail.png"])
    check(max(thumb.width, thumb.height) <= 256, "thumbnail max side is 256")


def test_same_inputs_give_the_same_bytes():
    a, _ = _surface(context=_context(), captions=[(0, 0, "usr000")], swatches=[[(9, 9, 9)] * 4])
    b, _ = _surface(context=_context(), captions=[(0, 0, "usr000")], swatches=[[(9, 9, 9)] * 4])
    check(a.ora == b.ora, "a re-run is byte-identical")
    buf = io.BytesIO(a.ora)
    with zipfile.ZipFile(buf) as z:
        check(z.testzip() is None, "the zip round-trips through zipfile unchanged")


# --- §3 the layers -----------------------------------------------------------------

_FLAGS = {
    "orig": ("visible", "1.0", "true"),
    "context": ("visible", "0.5", None),
    "paint": ("visible", "1.0", None),
    "guides": ("hidden", "1.0", "true"),
    "palettes": ("hidden", "1.0", "true"),
}


def _check_flags(layers, expected_names, label):
    bottom_up = list(reversed(layers))
    check([l["name"] for l in bottom_up] == expected_names,
          f"{label}: layer names bottom to top", str([l["name"] for l in bottom_up]))
    for l in bottom_up:
        vis, op, locked = _FLAGS[l["name"]]
        ok = (l.get("visibility") == vis and l.get("opacity") == op
              and l.get("edit-locked") == locked and l.get("src") == f"data/{l['name']}.png"
              and l.get("composite-op") == "svg:src-over")
        check(ok, f"{label}: {l['name']} flags", str(l))
    visible = [l["name"] for l in bottom_up if l["visibility"] == "visible"]
    check(visible[-1] == "paint", f"{label}: paint is the topmost visible layer", str(visible))


def test_five_layers_with_context_four_without():
    with_ctx, _ = _surface(context=_context())
    without, _ = _surface()
    _, data = _members(with_ctx.ora)
    _check_flags(_layers(data["stack.xml"])[1], ["orig", "context", "paint", "guides", "palettes"], "recorded")
    _, data = _members(without.ora)
    _check_flags(_layers(data["stack.xml"])[1], ["orig", "paint", "guides", "palettes"], "static")
    check("data/context.png" not in data, "no context member without a stage position")


def test_paint_is_fully_transparent_and_orig_is_the_twin():
    surface, rects = _surface()
    _, data = _members(surface.ora)
    paint = _decode(data["data/paint.png"])
    check(not any(paint.px), "paint has zero alpha everywhere")
    orig_layer = _decode(data["data/orig.png"])
    check(orig_layer.px == surface.orig.upscale(SCALE).px, "orig layer is the twin's pixels, upscaled")
    check(surface.painted.width == orig_layer.width and surface.painted.height == orig_layer.height,
          "every layer shares the canvas size")


def test_context_band_is_outside_every_cell_and_the_twin_grows_with_it():
    plain, rects = _surface()
    grown, _ = _surface(context=_context())
    check(grown.painted.height > plain.painted.height, "the canvas grew a context band")
    check(grown.painted.width % SCALE == 0 and grown.painted.height % SCALE == 0,
          "the grown canvas is still a whole multiple of the scale")
    check(grown.orig.width * SCALE == grown.painted.width
          and grown.orig.height * SCALE == grown.painted.height,
          "the twin grew identically (sheet = twin x scale)")
    check(grown.orig.px[:len(plain.orig.px)] == plain.orig.px[:len(plain.orig.px)]
          or grown.orig.crop(0, 0, plain.orig.width, plain.orig.height).px == plain.orig.px,
          "the twin's cells are untouched by the growth")
    _, data = _members(grown.ora)
    ctx = _decode(data["data/context.png"])
    inside = [p for r in rects for p in _pixels_in(ctx, r) if p[3]]
    check(not inside, "no context pixel falls inside a cell rectangle", f"{len(inside)} pixels")
    band = [ctx.get(x, y) for y in range(plain.painted.height, ctx.height) for x in range(ctx.width) if ctx.get(x, y)[3]]
    check(bool(band), "the band holds the 1x crop")
    merged = _decode(data["mergedimage.png"])
    check(not mep_sentinel.rgba_has_sentinel(merged.px), "the merged image never shows the sentinel")
    band_alpha = {merged.get(x, y)[3] for y in range(plain.painted.height, merged.height)
                  for x in range(merged.width) if ctx.get(x, y)[3]}
    check(band_alpha and band_alpha <= {127, 128}, "the context band composes at 50 percent over an empty twin",
          str(band_alpha))


# --- §4 the sentinel -----------------------------------------------------------------

def test_sentinel_in_the_artwork_stops_generation():
    orig, rects1x = _twin()
    painted = orig.upscale(SCALE)
    painted.set(3, 3, mep_sentinel.SENTINEL_RGBA)
    rects = [{**r, "x": r["x"] * SCALE, "y": r["y"] * SCALE, "w": r["w"] * SCALE, "h": r["h"] * SCALE}
             for r in rects1x]
    try:
        W.build_surface(painted, orig, rects)
    except W.OraError as exc:
        check("#FF00FD" in str(exc), "a sentinel pixel in the artwork is refused loudly", str(exc))
    else:
        check(False, "a sentinel pixel in the artwork is refused loudly", "no error")
    twin_hit, _ = _twin(colour=mep_sentinel.SENTINEL_RGBA)
    try:
        W.build_surface(twin_hit.upscale(SCALE), twin_hit, rects)
    except W.OraError:
        check(True, "a sentinel pixel in the twin is refused too")
    else:
        check(False, "a sentinel pixel in the twin is refused too", "no error")


def test_guides_and_palettes_are_sentinel_framed_and_cross_the_first_row():
    surface, rects = _surface(captions=[(2, 2, "USR000 P1")], swatches=[[(10, 20, 30), (40, 50, 60)]])
    _, data = _members(surface.ora)
    guides = _decode(data["data/guides.png"])
    drawn = {p for p in (guides.get(x, y) for y in range(guides.height) for x in range(guides.width)) if p[3]}
    check(drawn == {mep_sentinel.SENTINEL_RGBA}, "guides is sentinel-only", str(drawn))
    check(all(any(p == mep_sentinel.SENTINEL_RGBA for p in _pixels_in(guides, r)) for r in rects),
          "the grid outline reaches every cell")
    palettes = _decode(data["data/palettes.png"])
    first_row = [r for r in rects if r["y"] == rects[0]["y"]]
    check(all(any(p == mep_sentinel.SENTINEL_RGBA for p in _pixels_in(palettes, r)) for r in first_row),
          "every first-row cell the palettes band crosses holds exact sentinel pixels")
    colours = {p[:3] for p in (palettes.get(x, y) for y in range(palettes.height) for x in range(palettes.width)) if p[3]}
    check((10, 20, 30) in colours and (40, 50, 60) in colours, "the swatches are drawn in the band")
    wild, _ = _surface(wildcard="defaultTile = Y")
    _, data = _members(wild.ora)
    band = _decode(data["data/palettes.png"])
    y0 = rects[0]["y"]
    holes = sum(1 for y in range(y0, y0 + rects[0]["h"]) for x in range(band.width) if not band.get(x, y)[3])
    check(0 < holes < band.width * rects[0]["h"], "a static band states the wildcard as knocked-out glyphs")


def test_hatch_covers_a_seen_false_cell():
    orig, rects1x = _twin()
    painted = orig.upscale(SCALE)
    rects = [{**r, "x": r["x"] * SCALE, "y": r["y"] * SCALE, "w": r["w"] * SCALE, "h": r["h"] * SCALE,
              "seen": (r["index"] == 1) and False if r["index"] == 1 else None} for r in rects1x]
    rects[1]["seen"] = False
    surface = W.build_surface(painted, orig, rects)
    _, data = _members(surface.ora)
    guides = _decode(data["data/guides.png"])
    interior = lambda r: [guides.get(x, y) for y in range(r["y"] + 2, r["y"] + r["h"] - 2)  # noqa: E731
                          for x in range(r["x"] + 2, r["x"] + r["w"] - 2)]
    check(any(p[3] for p in interior(rects[1])), "an unseen cell is hatched inside")
    check(not any(p[3] for p in interior(rects[0])), "a seen cell has only its outline")


def _lint_pack(root: Path, sheet_png: Image, doc: dict, kind_dir="sheets"):
    tex = root / "textures"
    (tex / kind_dir).mkdir(parents=True)
    (tex / "hires.txt").write_text(
        "<ver>106\n<scale>2\n<supportedRom>" + "0" * 40 + "\n<img>usr000.png\n", encoding="utf-8")
    W.sheet_repaint.write_png(tex / kind_dir / doc["sheet"], sheet_png)
    (tex / kind_dir / (doc["sheet"][:-4] + ".json")).write_text(json.dumps(doc), encoding="utf-8")
    rep = mep_lint.Report()
    mep_lint.lint_sheet_sentinels(mep_lint.Source(root), "textures/hires.txt", rep)
    return [(lvl, where, msg) for lvl, where, msg in rep.items if lvl == "error"]


def test_lint_refuses_a_cell_carrying_the_sentinel_and_names_it():
    orig, rects1x = _twin()
    painted = orig.upscale(2)
    doc = {"version": 1, "kind": "sprite", "gridUnit": UNIT, "gutter": GUTTER, "columns": 2,
           "sheet": "usr000.png", "reference": "usr000.orig.png",
           "cells": [{"index": r["index"], "x": r["x"], "y": r["y"], "tiles": []} for r in rects1x]}
    with tempfile.TemporaryDirectory() as td:
        clean = _lint_pack(Path(td) / "clean", painted, doc)
        check(not clean, "a clean export passes the sentinel check", str(clean))
    bad = painted.clone()
    cell = rects1x[1]
    bad.set(cell["x"] * 2 + 3, cell["y"] * 2 + 3, mep_sentinel.SENTINEL_RGBA)
    with tempfile.TemporaryDirectory() as td:
        errors = _lint_pack(Path(td) / "bad", bad, doc)
        check(len(errors) == 1, "exactly one error for one bad cell", str(errors))
        msg = errors[0][2] if errors else ""
        check(f"cell index {cell['index']} at ({cell['x']}, {cell['y']})" in msg and "#FF00FD" in msg,
              "the error names the cell's index and (x, y)", msg)
        check(errors and errors[0][1] == "textures/sheets/usr000.png", "the error names the sheet",
              str(errors[:1]))
    # A sentinel in the gutter is not a cell and is not an error: the check is per cell.
    gutter = painted.clone()
    gutter.set(0, 0, mep_sentinel.SENTINEL_RGBA)
    with tempfile.TemporaryDirectory() as td:
        check(not _lint_pack(Path(td) / "gutter", gutter, doc), "a sentinel in the gutter is not a cell hit")
    # A CHR page sidecar keeps scaled coordinates (kind chr, scale field).
    page = Image(32, 32)
    page.set(17, 17, mep_sentinel.SENTINEL_RGBA)
    chr_doc = {"version": 1, "kind": "chr", "gridUnit": 8, "cell": {"w": 8, "h": 8}, "columns": 2,
               "rows": 2, "scale": 2, "sheet": "Chr_0_0.png", "reference": "Chr_0_0.orig.png",
               "cells": [{"index": i, "x": (i % 2) * 16, "y": (i // 2) * 16} for i in range(4)]}
    with tempfile.TemporaryDirectory() as td:
        errors = _lint_pack(Path(td) / "chr", page, chr_doc, kind_dir="chr")
        check(len(errors) == 1 and "cell index 3 at (16, 16)" in errors[0][2],
              "a chr page's scaled cell coordinates resolve to the right cell", str(errors))


def test_lint_main_runs_the_check_over_a_pack():
    orig, rects1x = _twin()
    bad = orig.upscale(2)
    bad.set(rects1x[0]["x"] * 2 + 1, rects1x[0]["y"] * 2 + 1, mep_sentinel.SENTINEL_RGBA)
    doc = {"version": 1, "kind": "sprite", "gridUnit": UNIT, "gutter": GUTTER, "columns": 2,
           "sheet": "usr000.png", "reference": "usr000.orig.png",
           "cells": [{"index": r["index"], "x": r["x"], "y": r["y"], "tiles": []} for r in rects1x]}
    with tempfile.TemporaryDirectory() as td:
        root = Path(td) / "pack"
        tex = root / "textures" / "sheets"
        tex.mkdir(parents=True)
        (root / "textures" / "hires.txt").write_text(
            "<ver>106\n<scale>2\n<supportedRom>" + "0" * 40 + "\n<img>usr000.png\n", encoding="utf-8")
        W.sheet_repaint.write_png(tex / "usr000.png", bad)
        (tex / "usr000.json").write_text(json.dumps(doc), encoding="utf-8")
        out = io.StringIO()
        real = sys.stdout
        sys.stdout = out
        try:
            rc = mep_lint.main(["mep_lint.py", str(root)])
        finally:
            sys.stdout = real
        check(rc == 1 and "guide sentinel" in out.getvalue(),
              "mep_lint's main path fails the pack and prints the sentinel error",
              out.getvalue()[-400:])


# --- §5 write-only ---------------------------------------------------------------------

_READ_CALL = re.compile(r"ZipFile\((?![^)]*[\"']w[\"'])|\.read\(|\.open\(|read_bytes\(|extract")


def test_no_module_under_scripts_opens_an_ora_for_reading():
    offenders = []
    for path in sorted(SCRIPTS.glob("*.py")):
        if path.name.startswith("test_"):
            continue
        for n, line in enumerate(path.read_text(encoding="utf-8", errors="replace").splitlines(), 1):
            code = line.split("#", 1)[0]
            if ".ora" in code and _READ_CALL.search(code):
                offenders.append(f"{path.name}:{n}: {line.strip()}")
    check(not offenders, "no line under scripts/ reads a .ora", "\n".join(offenders))
    src = (SCRIPTS / "ora_writer.py").read_text(encoding="utf-8")
    opens = re.findall(r"zipfile\.ZipFile\(([^)]*)\)", src)
    check(opens and all('"w"' in o for o in opens), "every ZipFile the writer opens is opened for writing", str(opens))
    public = [n for n in dir(W) if not n.startswith("_") and callable(getattr(W, n))
              and getattr(getattr(W, n), "__module__", "") == W.__name__]
    readers = [n for n in public if re.match(r"(read|load|open|parse|extract|unpack)", n)]
    check(not readers, "the writer exposes no read entry point", str(readers))
    check(not hasattr(W, "read_ora") and not hasattr(W, "load_ora"), "no read_ora/load_ora anywhere")
    mentions = [p.name for p in SCRIPTS.glob("*.py") if not p.name.startswith("test_")
                and "mergedimage" in p.read_text(encoding="utf-8", errors="replace")]
    check(mentions == ["ora_writer.py"], "mergedimage.png is written by one module and read by none", str(mentions))


# --- the generators ----------------------------------------------------------------------

def test_a_composed_sheet_lands_with_its_ora():
    import test_artist_bg_kit as BG  # noqa: PLC0415 — reuse its recorded-pack fixture
    import artist_bg_kit as K  # noqa: PLC0415
    with tempfile.TemporaryDirectory() as td:
        pack = BG.make_pack(Path(td) / "pack")
        out = Path(td) / "kit"
        K.build_kit(pack, out)
        pngs = sorted(p for p in (out / "sheets").glob("usr*.png") if not p.name.endswith(".orig.png"))
        check(bool(pngs), "the bg kit wrote at least one composed sheet")
        for png in pngs:
            ora = png.with_name(png.name[:-4] + ".ora")
            check(ora.is_file(), f"{png.name} has its .ora beside it")
            if ora.is_file():
                _, data = _members(ora.read_bytes())
                _, layers = _layers(data["stack.xml"])
                check([l["name"] for l in reversed(layers)] == ["orig", "paint", "guides", "palettes"],
                      f"{png.name}: a scenery sheet without stage positions has four layers")
                orig_layer = _decode(data["data/orig.png"])
                twin = W.sheet_repaint.read_png(png.with_name(png.name[:-4] + ".orig.png"))
                sheet = W.sheet_repaint.read_png(png)
                check(orig_layer.width == sheet.width and twin.width * (sheet.width // twin.width) == sheet.width,
                      f"{png.name}: sheet, twin and orig layer agree on size")


class _Page:
    def __init__(self, cell_px):
        self.cell_px = cell_px


def test_a_static_chr_page_has_four_layers_hatched_everywhere_and_the_wildcard():
    cell_px = 8
    hd = Image(16 * cell_px, 16 * cell_px, bytearray(bytes((0x10, 0x10, 0x10, 0xFF)) * 256 * cell_px * cell_px))
    orig = hd.clone()
    cells = [{"slot": s, "index": s, "x": (s % 16) * cell_px, "y": (s // 16) * cell_px,
              "state": "fill", "seen": False, "palette": "0F001020", "paletteObserved": False}
             for s in range(256)]
    with tempfile.TemporaryDirectory() as td:
        out = Path(td)
        surface = W.write_chr_surface(out, "Chr_0.png", hd, orig, cells, _Page(cell_px))
        check((out / "Chr_0.png").is_file() and (out / "Chr_0.orig.png").is_file() and (out / "Chr_0.ora").is_file(),
              "page, twin and .ora written")
        check(surface.layers == ["orig", "paint", "guides", "palettes"], "a static page has four layers",
              str(surface.layers))
        _, data = _members((out / "Chr_0.ora").read_bytes())
        guides = _decode(data["data/guides.png"])
        rects = W.chr_rects(cells, cell_px)
        hatched = sum(1 for r in rects if any(p[3] for p in (guides.get(x, y)
                      for y in range(r["y"] + 2, r["y"] + r["h"] - 2) for x in range(r["x"] + 2, r["x"] + r["w"] - 2))))
        check(hatched == 256, "every seen:false cell is hatched", str(hatched))
        band = _decode(data["data/palettes.png"])
        row0 = [band.get(x, y) for y in range(0, cell_px) for x in range(band.width)]
        check(any(p == mep_sentinel.SENTINEL_RGBA for p in row0) and any(not p[3] for p in row0),
              "the wildcard is stated as knocked-out glyphs on a sentinel band")
        # A recorded page without a twin writes the PNG alone, as before.
        none = W.write_chr_surface(out / "x", "Chr_1.png", hd, None, cells, _Page(cell_px)) if (out / "x").mkdir() is None else None
        check(none is None and (out / "x" / "Chr_1.png").is_file() and not (out / "x" / "Chr_1.ora").exists(),
              "a page with no twin gets no .ora (there is no orig layer to write)")


def test_a_recorded_page_with_empty_cells_gets_its_ora():
    """ADR-0220 §4 as amended 2026-09-22: the recorder's unpainted fill is
    `0xFFFF00FF`, the sentinel is `#FF00FD`, so a recorded page whose `empty`
    cells still wear the fill is an ordinary surface — `.ora` and all."""
    cell_px = 8
    hd = Image(16 * cell_px, 16 * cell_px, bytearray(bytes((0x10, 0x10, 0x10, 0xFF)) * 256 * cell_px * cell_px))
    cells = [{"slot": s, "index": s, "x": (s % 16) * cell_px, "y": (s // 16) * cell_px,
              "state": "evidence", "seen": True, "palette": "0F001020"} for s in range(256)]
    hole = cells[5]
    hole.update(state="empty", seen=False)
    for yy in range(hole["y"], hole["y"] + cell_px):
        for xx in range(hole["x"], hole["x"] + cell_px):
            hd.set(xx, yy, (0xFF, 0x00, 0xFF, 0xFF))   # the recorder's unpainted fill
    orig = hd.clone()
    with tempfile.TemporaryDirectory() as td:
        out = Path(td)
        got = W.write_chr_surface(out, "Chr_2_0.png", hd, orig, cells, _Page(cell_px))
        check(got is not None and (out / "Chr_2_0.ora").is_file(), "a recorded page with an empty cell gets a .ora")
        check(got is not None and got.layers == ["orig", "paint", "guides", "palettes"], "with four layers")
        _, data = _members((out / "Chr_2_0.ora").read_bytes())
        guides = _decode(data["data/guides.png"])
        r = W.chr_rects(cells, cell_px)[5]
        check(any(p[3] for p in (guides.get(x, y) for y in range(r["y"] + 2, r["y"] + r["h"] - 2)
                                  for x in range(r["x"] + 2, r["x"] + r["w"] - 2))),
              "the empty (seen: false) cell is hatched")


def test_lint_scans_a_chr_pages_empty_cells_too():
    page = Image(32, 32)
    for yy in range(16, 32):
        for xx in range(16, 32):
            page.set(xx, yy, mep_sentinel.SENTINEL_RGBA)
    chr_doc = {"version": 1, "kind": "chr", "gridUnit": 8, "cell": {"w": 8, "h": 8}, "columns": 2,
               "rows": 2, "scale": 2, "sheet": "Chr_0_0.png", "reference": "Chr_0_0.orig.png",
               "cells": [{"index": i, "x": (i % 2) * 16, "y": (i // 2) * 16,
                          "state": "empty" if i == 3 else "evidence"} for i in range(4)]}
    with tempfile.TemporaryDirectory() as td:
        errors = _lint_pack(Path(td) / "chr", page, chr_doc, kind_dir="chr")
        check(len(errors) == 1 and "cell index 3 at (16, 16)" in errors[0][2],
              "the sentinel on an empty chr cell is a wrong export like any other", str(errors))
    fill = Image(32, 32)
    for yy in range(16, 32):
        for xx in range(16, 32):
            fill.set(xx, yy, (0xFF, 0x00, 0xFF, 0xFF))
    with tempfile.TemporaryDirectory() as td:
        check(not _lint_pack(Path(td) / "fill", fill, chr_doc, kind_dir="chr"),
              "the recorder's unpainted fill 0xFFFF00FF is not the sentinel and is not flagged")


def test_a_figure_export_lands_with_its_ora():
    """ADR-0220 §1, "beside every surface PNG": `mep_figure.export_figure`
    writes `<id>-figure.ora` from the same canvas — 8 members, `mimetype`
    first, four layers (a figure has no stage position, §3)."""
    import test_mep_figure as TF  # noqa: PLC0415 — its synthetic pack fixture
    import mep_figure as F  # noqa: PLC0415
    import compose_engine as E  # noqa: PLC0415
    with tempfile.TemporaryDirectory() as td:
        pack_dir = TF.make_pack(Path(td) / "pack", with_poses=False)
        out = Path(td) / "figures"
        doc = F.export_figure(E.Pack(pack_dir), "spr000", out)
        ora = out / "spr000-figure.ora"
        check(ora.is_file() and (out / doc["sheet"]).is_file(), "spr000-figure.ora is written beside the figure PNG")
        infos, data = _members(ora.read_bytes())
        check(len(infos) == 8 and infos[0].filename == "mimetype" and infos[0].compress_type == zipfile.ZIP_STORED,
              "8 members, mimetype first and stored", str([i.filename for i in infos]))
        _, layers = _layers(data["stack.xml"])
        _check_flags(layers, ["orig", "paint", "guides", "palettes"], "figure")
        sheet = W.sheet_repaint.read_png(out / doc["sheet"])
        check(_decode(data["data/orig.png"]).width == sheet.width, "the orig layer is the figure's size")
        guides = _decode(data["data/guides.png"])
        check(any(guides.get(x, y) == mep_sentinel.SENTINEL_RGBA for y in range(guides.height) for x in range(guides.width)),
              "guides carry the cell grid and caption in the sentinel")


# --- 2026-09-23 follow-up: captions fit, the palettes band follows first use --------

def test_a_caption_is_fitted_to_the_canvas_never_clipped():
    check(W.fit_text("alpha beta gamma", 10) == ["alpha b..."], "one line: cut with an ellipsis",
          str(W.fit_text("alpha beta gamma", 10)))
    check(W.fit_text("alpha beta gamma", 10, max_lines=2) == ["alpha beta", "gamma"],
          "two lines: greedy word wrap", str(W.fit_text("alpha beta gamma", 10, max_lines=2)))
    check(W.fit_text("abcdefghij", 4, max_lines=3) == ["abcd", "efgh", "ij"],
          "a word longer than a line is split", str(W.fit_text("abcdefghij", 4, max_lines=3)))
    check(W.fit_text("", 10) == [], "empty text draws nothing")
    # A caption wider than the canvas wraps to its right edge and stops at the
    # next cell row below it, so nothing lands past the canvas or under row 1.
    orig, rects1x = _twin(cols=2, rows=2)
    painted = orig.upscale(SCALE)
    rects = [{**r, "x": r["x"] * SCALE, "y": r["y"] * SCALE,
              "w": r["w"] * SCALE, "h": r["h"] * SCALE} for r in rects1x]
    long = "figures no cycle or sequence ordered - 19 of them, seen in 4 frame(s) between them"
    surface = W.build_surface(painted, orig, rects, captions=[(GUTTER * SCALE, GUTTER * SCALE, long)])
    _, data = _members(surface.ora)
    guides = _decode(data["data/guides.png"])
    row1_top = min(r["y"] for r in rects if r["y"] > GUTTER * SCALE)
    lit = [(x, y) for y in range(guides.height) for x in range(guides.width)
           if guides.get(x, y)[3] and not _on_grid(x, y, rects)]
    check(bool(lit), "the caption is drawn")
    check(all(y < row1_top for _, y in lit), "no caption pixel reaches the next cell row",
          f"lowest caption pixel y={max(y for _, y in lit) if lit else None} row1_top={row1_top}")
    plain = Image(60, 20)
    n = W.draw_text(plain, 0, 0, long, mep_sentinel.SENTINEL_RGBA, 1, max_width=40, max_lines=3)
    check(n == 3 and any(plain.get(x, 2 * W.line_height(1))[3] for x in range(40)),
          "the long caption wrapped onto the lines it was allowed", str(n))
    check(all(plain.get(x, y)[3] == 0 for y in range(20) for x in range(40, 60)),
          "draw_text with max_width never draws past it")


def _on_grid(x, y, rects):
    for r in rects:
        inside = r["x"] <= x < r["x"] + r["w"] and r["y"] <= y < r["y"] + r["h"]
        edge = x in (r["x"], r["x"] + r["w"] - 1) or y in (r["y"], r["y"] + r["h"] - 1)
        if inside and edge:
            return True
    return False


def test_palettes_band_follows_first_use_and_labels_each_group():
    sw, labels = W.first_use_palettes([(3, ["FF36160F"]), (0, ["FF20000F", "FF36160F"]), (7, [None, "zz"])])
    check(labels == ["3", "0"], "palettes are listed once, in the order first used, not by hex",
          str(labels))
    check(len(sw) == 2 and sw[0] == W.nes_swatches(["FF36160F"])[0], "the first group is the first cell's palette")
    surface, rects = _surface(swatches=sw, swatch_labels=labels)
    _, data = _members(surface.ora)
    band = _decode(data["data/palettes.png"])
    y0, h = rects[0]["y"], rects[0]["h"]
    first_colour = next(band.get(x, y0 + 1) for x in range(band.width)
                        if band.get(x, y0 + 1)[3] and band.get(x, y0 + 1) != mep_sentinel.SENTINEL_RGBA)
    check(first_colour[:3] == sw[0][0], "the leftmost swatch is the first-used palette's first colour",
          str(first_colour))
    first_x = next(x for x in range(band.width) if band.get(x, y0 + 1)[:3] == sw[0][0])
    holes = sum(1 for y in range(y0, y0 + h) for x in range(first_x) if band.get(x, y)[3] == 0)
    check(holes > 0, "the label is knocked out of the sentinel in front of its group")
    check(all(any(p == mep_sentinel.SENTINEL_RGBA for p in _pixels_in(band, r)) for r in rects if r["y"] == y0),
          "a labelled band still leaves exact sentinel pixels in every first-row cell")


def main():
    for name, fn in sorted(globals().items()):
        if name.startswith("test_") and callable(fn):
            print(f"--- {name}")
            fn()
    total = sum(1 for n in globals() if n.startswith("test_"))
    print(f"\n{total - len(set(_FAILURES)) if not _FAILURES else total} test function(s), {len(_FAILURES)} failed check(s)")
    return 1 if _FAILURES else 0


if __name__ == "__main__":
    sys.exit(main())
