#!/usr/bin/env python3
"""Acceptance test for scripts/mep_build.py (PRD F5.4c, ADR-0049 §2).

Builds a synthetic author folder (a 16-column sheet + a key-source
hires.txt + two OGGs) and asserts the whole build/pack/rename cycle:

  * `build` re-points every tile key at the sheet cell that owns it (16px
    crops at scale 2, img index = sheet index), regenerates audio/hires.txt
    with the OGG ids taken from their file names, and lints clean;
  * `pack` writes a correct pack.json (sections from the tree, targets from
    the ROM) and produces a byte-deterministic zip that lints clean;
  * `rename-audio-id` renames an enumerated id across fingerprints.json +
    the midi/bgm/sfx files + the audio/hires.txt references;
  * the failure modes exit 2 with a clear message: no key source, sheet
    cells < tile keys, a non-16-column sheet;
  * the ADR-0153 (F9.4) sheet round-trip: `textures/sheets/*.json` +
    `*.png` are sliced back into per-tile crops — identity is pixel-exact
    (PRD Phase 9 validation test 6), painting one cell touches exactly that
    cell's tiles, a 2x upscaled sheet slices at scale 2, a map slices
    through `placements[]`, null/short `tiles[]` entries are skipped rather
    than fatal, and a non-integer scale is a build error;
  * the ADR-0153 §4 claim rule: a cell claims a tile key only when it was
    painted, measured against its `*.orig.png` twin — so a painted
    `metatiles.png` cell beats an untouched map (PRD test 3) and a painted
    map beats the untouched vocabulary (PRD test 4), with the static kind
    rank as the tie-break when both, or neither, were painted;
  * PRD Phase 10 S10.c: `pack` carries the root `generated` label (MEP-v1
    §3.1 v1.6, ADR-0154 §3) across a rebuild, and the zip's membership is
    documented as it stands — every file under the folder, a non-pack
    `studio/` subfolder included;
  * PRD Phase 10 S10.d: coverage preservation for a repainted pack —
    `check-coverage` passes a fully skinned pack (pixels all differ) and
    fails a pack that lost a cell's tile keys or its sheet;
  * #172: `check-coverage` resolves candidate and baseline from one layout
    rule (the argument is the pack folder, as for `build`), says how to
    obtain a baseline when there is none, and refuses to compare the
    rebuilt manifest against itself;
  * #218: `check-coverage` only compares the half of a baseline that `build`
    re-derives — the keys a `textures/sheets/` image claims. A raw recorder
    manifest (every key pointed at `textures/chr/`) is refused instead of
    reported as a drop, a sheet-derived manifest kept outside the pack is
    read against the pack instead of resolving nothing, and a sheet deleted
    from the pack still fails against such a detached baseline;
  * #173: `build` reports its key count as a delta against the key source,
    says why dropping keys is the designed outcome and where the
    screen-owned cells are repainted, and groups the lint warnings about
    the tool's own sheet geometry instead of burying the rest;
  * #255: a sprite sheet cell with `source` + `mirror: H` stores unflipped
    pixels under the unflipped source key (ADR-0178 — the run time mirrors
    the replacement art itself);
  * #457: an index-keyed (CHR ROM) mirror cell is un-baked like a CHR RAM
    one, sheet and twin in lockstep, a crop an alias shares is un-baked once,
    and a second build leaves it alone;
  * #456: a background key the recording keeps transparent at colour 0 is
    rebuilt transparent there (sheet and twin), an opaque key and the
    artist's paint stay opaque, and a second build is byte-identical, also
    when two keys share a crop (a bank-swapping game's alias); a translucent
    brush pixel in the key source is not the signature, and an RGB sheet
    gains the alpha channel it needs;
  * #256: every `[condition]` rule keeps its unconditional fallback twin in
    the rebuilt `hires.txt`, so a condition miss still shows the painted art;
  * #253: a painted sprite sheet whose cells lose to another sheet fails the
    build with an ownership error instead of a silent all-green success;
  * #329: `build` is idempotent over a pack it un-baked — the `*.orig.png`
    twin is un-baked with the sheet, so a second build exits 0 and leaves the
    pack byte-identical, while #253 still fires for a real paint conflict;
  * #338/#343: a painted cell that reaches nothing says so — the capture
    warning is keyed on the tile (so a freshly pasted cell is visible to it)
    and names only the sheets that were actually painted, and a painted cell
    whose key another crop already owns is named instead of vanishing into a
    "repeat(s)" count;
  * #339/#344: the build says a capture's gate is a few `tileAtPosition`
    probes rather than the whole frame, and a capture deleted from both layers
    is retired — its `<background>` line dropped and counted — instead of
    failing the build;
  * #422: the #338 line also fires for a key a sheet shows (a cell
    `mep_add_cell.py` placed), found in the capture's own `.orig.png`, and
    names every live capture that draws the key, and only those;
  * ADR-0196 (F12.5): a sheet's `additions[]` overflow layer round-trips —
    the `<addition>` tag and the synthetic target's own `<tile>` rule come
    out of the same sheet cell, a rebuild re-emits rather than stacks them,
    and the build refuses a target the sidecar does not mark synthetic, a
    CHR RAM target that is not the reserved key, and a CHR ROM target it
    cannot assert against the ROM's iNES header.

Framework-free, mirroring test_mep_recipe.py's ok()/fail()/main() style.
Wired into `make doc-checks`. Usage: python3 scripts/test_mep_build.py
"""

import hashlib
import json
import shutil
import struct
import subprocess
import sys
import tempfile
import zipfile
import zlib
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
MEP_BUILD = REPO / "scripts" / "mep_build.py"
GEN_ROM = REPO / "scripts" / "gen_synthetic_nrom.py"
PY = sys.executable

FAILED = 0


def ok(msg):
    print(f"PASS: {msg}")


def fail(msg):
    global FAILED
    FAILED = 1
    print(f"FAIL: {msg}")


def png(width, height, rgba=(0xC8, 0x28, 0x28, 0xFF)) -> bytes:
    raw = b"".join(b"\x00" + bytes(rgba) * width for _ in range(height))

    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


# --- ADR-0153 (F9.4) sheet fixtures ----------------------------------------
#
# The C++ emitter (Core/NES/HdPacks/SheetRender.cpp) is the ground truth for
# both the pixels and the sidecar bytes, so the helpers below are a faithful
# Python mirror of RenderTile / RenderMetatile / BuildContactSheet /
# SerializeSheet. Any drift here would make the round-trip test lie.

# Stand-in NES master palette: 64 distinct 0x00RRGGBB entries. The real one
# lives in HdPackBuilder; only its indexing matters to the round-trip.
NES_PALETTE = [((i * 4) << 16) | ((i * 3 + 7) << 8) | (i * 2 + 3) for i in range(64)]


def tile_bytes(shape: int) -> bytes:
    """A distinct, non-degenerate 2bpp pattern per shape id."""
    lo = bytes((shape * 7 + r * 13) & 0xFF for r in range(8))
    hi = bytes((shape * 11 + r * 5) & 0xFF for r in range(8))
    return lo + hi


def tile_hex(shape: int) -> str:
    return tile_bytes(shape).hex().upper()


PAL_WORD = 0x0F162A30
PAL_HEX = f"{PAL_WORD:08X}"


def render_tile(shape: int, pixels, x: int, y: int, width: int, height: int):
    """SheetRender::RenderTile: opaque on all four colour indexes."""
    colors = [NES_PALETTE[(PAL_WORD >> ((3 - c) * 8)) & 0x3F] | 0xFF000000 for c in range(4)]
    data = tile_bytes(shape)
    for row in range(8):
        py = y + row
        if not 0 <= py < height:
            continue
        lo, hi = data[row], data[row + 8]
        for col in range(8):
            px = x + col
            if not 0 <= px < width:
                continue
            bit = 7 - col
            pixels[py][px] = colors[((lo >> bit) & 1) | (((hi >> bit) & 1) << 1)]


def blank(width: int, height: int):
    return [[0] * width for _ in range(height)]


def png_rgba(pixels) -> bytes:
    """Encode 0xAARRGGBB rows as a non-interlaced 8-bit RGBA PNG, filter 0."""
    height, width = len(pixels), len(pixels[0])
    raw = bytearray()
    for row in pixels:
        raw.append(0)
        for p in row:
            raw += bytes(((p >> 16) & 0xFF, (p >> 8) & 0xFF, p & 0xFF, (p >> 24) & 0xFF))

    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(bytes(raw), 9)) + chunk(b"IEND", b""))


def png_read(path: Path):
    """Decode a filter-0 8-bit RGBA PNG back into 0xAARRGGBB rows."""
    data = path.read_bytes()
    assert data[:8] == b"\x89PNG\r\n\x1a\n", path
    pos, idat, width, height = 8, b"", 0, 0
    while pos < len(data):
        length = struct.unpack(">I", data[pos:pos + 4])[0]
        tag = data[pos + 4:pos + 8]
        body = data[pos + 8:pos + 8 + length]
        if tag == b"IHDR":
            width, height, depth, color = struct.unpack(">IIBB", body[:10])
            assert (depth, color) == (8, 6), (depth, color)
        elif tag == b"IDAT":
            idat += body
        pos += 12 + length
    raw = zlib.decompress(idat)
    stride = width * 4
    rows = []
    for y in range(height):
        off = y * (stride + 1)
        assert raw[off] == 0, "test PNGs are written with filter 0 only"
        line = raw[off + 1:off + 1 + stride]
        rows.append([(line[i + 3] << 24) | (line[i] << 16) | (line[i + 1] << 8) | line[i + 2]
                     for i in range(0, stride, 4)])
    return rows


def upscale(pixels, n: int):
    return [[p for p in row for _ in range(n)] for row in pixels for _ in range(n)]


def crop(pixels, x: int, y: int, size: int):
    return [row[x:x + size] for row in pixels[y:y + size]]


def _fixed(v):
    return f"{v:.4f}"


# ADR-0172: a CHR ROM pack's sidecar carries each tile's CHR index next to its
# data. The fixture mints one per shape, far enough from 0 that a build reading
# the wrong field cannot land on the right token by accident.
CHR_INDEX_BASE = 0x40
EMIT_TILE_INDEX = False

# ADR-0178: the recorder bakes a sprite's OAM flips into the shape it records,
# so on a CHR RAM game the sidecar's `tile` is a key the run time never looks
# up and `source` carries the one it does. EMIT_FLIP_SOURCE writes both;
# EMIT_FLIP_BAKED writes the baked key alone, which is a pack recorded before
# the ADR.
EMIT_FLIP_SOURCE = False
EMIT_FLIP_BAKED = False


def flip_hex(data: str) -> str:
    """`data` read as if its OAM horizontal-flip bit had been baked in."""
    b = bytes.fromhex(data)
    return bytes(int(f"{x:08b}"[::-1], 2) for x in b).hex().upper()


def render_tile_hflipped(shape: int, pixels, x: int, y: int, width: int, height: int):
    """RenderTile with the OAM horizontal flip baked into the bitmap — what
    HdBuilderPpu::CaptureOam stores on a mirrored sprite cell."""
    tmp = blank(8, 8)
    render_tile(shape, tmp, 0, 0, 8, 8)
    for row in range(8):
        py = y + row
        if not 0 <= py < height:
            continue
        for col in range(8):
            px = x + col
            if not 0 <= px < width:
                continue
            pixels[py][px] = tmp[row][7 - col]



def _tiles_json(tiles):
    parts = []
    for t in tiles:
        if t is None:
            parts.append("null")
            continue
        idx = f', "index": {CHR_INDEX_BASE + t}' if EMIT_TILE_INDEX else ""
        data = tile_hex(t)
        src = ""
        if EMIT_FLIP_SOURCE or EMIT_FLIP_BAKED:
            data, src = flip_hex(data), data
            src = f', "source": "{src}", "mirror": "H"' if EMIT_FLIP_SOURCE else ""
        parts.append(f'{{ "tile": "{data}", "palette": "{PAL_HEX}"{idx}{src} }}')
    return "[" + ", ".join(parts) + "]"


def serialize_sheet(kind, unit, gutter, columns, sheet, reference, cells,
                    placements=None, mode=None, hud_rows=0, version=1):
    """Byte-shape mirror of SheetRender::SerializeSheet (ADR-0153 §4)."""
    out = ["{\n", f'  "version": {version},\n', f'  "kind": "{kind}",\n',
           f'  "gridUnit": {unit},\n', '  "gridPhase": { "x": 0, "y": 0 },\n',
           f'  "gridConsistency": {{ "chosen": {_fixed(0.83)}, "alt8x8": {_fixed(0.41)} }},\n',
           f'  "cell": {{ "w": {unit}, "h": {unit} }},\n', f'  "gutter": {gutter},\n',
           f'  "columns": {columns},\n', f'  "sheet": "{sheet}",\n',
           f'  "reference": "{reference}",\n']
    if placements is not None:
        out.append(f'  "mode": "{mode or "screen"}",\n')
        out.append(f'  "hudRows": {hud_rows},\n')
        out.append("  \"placements\": [")
        body = [f'{{ "x": {p[0]}, "y": {p[1]}, "cell": {p[2]} }}' for p in placements]
        out.append(("\n    " + ",\n    ".join(body) + "\n  ],\n") if body else "],\n")
    out.append("  \"cells\": [")
    body = []
    for c in cells:
        meta = f', "metatile": {c["metatile"]}' if c.get("metatile") is not None else ""
        alias = ""
        if c.get("aliases"):
            parts = [f'{{ "metatile": {a["metatile"]}, "tiles": {_tiles_json(a["tiles"])} }}'
                     for a in c["aliases"]]
            alias = f', "aliases": [{", ".join(parts)}]'
        body.append(f'{{ "index": {c["index"]}, "x": {c["x"]}, "y": {c["y"]}, "count": {c["count"]}, '
                    f'"context": "{c["context"]}"{meta}{alias}, "label": "", "tiles": {_tiles_json(c["tiles"])} }}')
    out.append(("\n    " + ",\n    ".join(body) + "\n  ]\n") if body else "]\n")
    out.append("}\n")
    return "".join(out)


def contact_sheet(unit, gutter, columns, vocab_cells):
    """BuildContactSheet: `columns` cells per row, gutter around and between,
    transparent everywhere a tile did not draw. Returns (cells, 1x pixels)."""
    stride = unit + gutter
    rows = (len(vocab_cells) + columns - 1) // columns
    width, height = columns * stride + gutter, rows * stride + gutter
    pixels = blank(width, height)
    cells = []
    per = 4 if unit >= 16 else 1
    for i, cell in enumerate(vocab_cells):
        x = gutter + (i % columns) * stride
        y = gutter + (i // columns) * stride
        for k in range(per):
            shape = cell["tiles"][k] if k < len(cell["tiles"]) else None
            if shape is None:
                continue
            render_tile(shape, pixels, x + (k % 2) * 8, y + (k // 2) * 8, width, height)
        cells.append(dict(cell, index=i, x=x, y=y))
    return cells, pixels


def hflipped_contact_sheet(unit, gutter, columns, vocab_cells):
    """`contact_sheet` with the OAM horizontal flip baked into every tile —
    what HdBuilderPpu::CaptureOam stored on a mirrored sprite cell, i.e. the
    pixels of a pack recorded before ADR-0178. Returns (cells, 1x pixels)."""
    stride = unit + gutter
    rows = (len(vocab_cells) + columns - 1) // columns
    width, height = columns * stride + gutter, rows * stride + gutter
    pixels = blank(width, height)
    cells = []
    for i, cell in enumerate(vocab_cells):
        x = gutter + (i % columns) * stride
        y = gutter + (i // columns) * stride
        for k in range(4):
            shape = cell["tiles"][k] if k < len(cell["tiles"]) else None
            if shape is None:
                continue
            render_tile_hflipped(shape, pixels, x + (k % 2) * 8, y + (k // 2) * 8, width, height)
        cells.append(dict(cell, index=i, x=x, y=y))
    return cells, pixels


def map_sheet(unit, placements, vocab_cells, width, height):
    """RenderMap: every placement at its map-pixel origin, no gutter."""
    pixels = blank(width, height)
    per = 4 if unit >= 16 else 1
    for px, py, idx in placements:
        for k in range(per):
            tiles = vocab_cells[idx]["tiles"]
            shape = tiles[k] if k < len(tiles) else None
            if shape is None:
                continue
            render_tile(shape, pixels, px + (k % 2) * 8, py + (k // 2) * 8, width, height)
    return pixels


def write_pair(sheets: Path, stem: str, pixels, scale: int):
    """The sheet at `scale` plus its pixel-exact 1x `*.orig.png` twin — the
    F5.4d `_writeReferences` convention the edited-cell test measures against."""
    (sheets / f"{stem}.png").write_bytes(png_rgba(upscale(pixels, scale) if scale > 1 else pixels))
    (sheets / f"{stem}.orig.png").write_bytes(png_rgba(pixels))


def parse_hires(path: Path):
    """(img list, {(tileData, palette): (img index, x, y, fields)})."""
    imgs, tiles = [], {}
    for line in path.read_text(encoding="utf-8").splitlines():
        s = line.strip()
        if s.startswith("<img>"):
            imgs.append(s[5:].strip())
        elif "<tile>" in s and not s.startswith("#"):
            f = [t.strip() for t in s.split("<tile>", 1)[1].split(",")]
            tiles[(f[1].upper(), f[2].upper())] = (int(f[0]), int(f[3]), int(f[4]), f)
    return imgs, tiles


def make_sheet_folder(root: Path, name: str, scale: int = 1, chr_rom: bool = False,
                      sidecar_index: bool = True, flip_baked: bool = False,
                      sidecar_source: bool = True, sprite_sheet: bool = False):
    """An ADR-0153 author folder: a metatile vocabulary of 6 cells, a stitched
    map over cells 0..3, and an object over cells 0..1.

    `chr_rom` writes the key source in the index form a CHR ROM game's
    manifest uses (ADR-0172); `sidecar_index` is what the sheet sidecars
    record, so the two can be set apart to reproduce a pack recorded before
    that ADR. `sprite_sheet` adds a `"kind": "sprite"` sheet beside the
    background ones — the only kind whose crops can carry a baked OAM flip
    (issue #196)."""
    global EMIT_TILE_INDEX, EMIT_FLIP_SOURCE, EMIT_FLIP_BAKED
    EMIT_TILE_INDEX = chr_rom and sidecar_index
    # ADR-0178: `flip_baked` bakes a horizontal flip into every sidecar key;
    # `sidecar_source` decides whether the unflipped twin rides along, i.e.
    # whether the pack was recorded before or after the ADR.
    EMIT_FLIP_SOURCE = flip_baked and sidecar_source
    EMIT_FLIP_BAKED = flip_baked and not sidecar_source
    folder = root / name
    sheets = folder / "textures" / "sheets"
    sheets.mkdir(parents=True)
    unit, gutter, columns = 16, 1, 3
    # Cells 4 and 5 exercise the "no art" paths: a null in the middle of
    # tiles[] (the entries after it must NOT shift up) and a short tiles[].
    vocab = [
        {"count": 431, "context": "scene", "metatile": 0, "tiles": [0, 1, 2, 3]},
        {"count": 300, "context": "scene", "metatile": 1, "tiles": [4, 5, 6, 7]},
        {"count": 120, "context": "scene", "metatile": 2, "tiles": [8, 9, 10, 11]},
        {"count": 90, "context": "scene", "metatile": 3, "tiles": [12, 13, 14, 15]},
        {"count": 12, "context": "scene", "metatile": 4, "tiles": [16, None, 17, None]},
        {"count": 3, "context": "scene", "metatile": 5, "tiles": [18, 19]},
    ]
    cells, pixels = contact_sheet(unit, gutter, columns, vocab)
    write_pair(sheets, "metatiles", pixels, scale)
    (sheets / "metatiles.json").write_text(
        serialize_sheet("metatiles", unit, gutter, columns, "metatiles.png", "metatiles.orig.png", cells),
        encoding="utf-8")

    placements = [(0, 0, 0), (16, 0, 1), (0, 16, 2), (16, 16, 3)]
    write_pair(sheets, "map-000", map_sheet(unit, placements, vocab, 32, 32), scale)
    (sheets / "map-000.json").write_text(
        serialize_sheet("map", unit, 0, 1, "map-000.png", "map-000.orig.png", [],
                        placements=placements, mode="screen"),
        encoding="utf-8")

    obj_cells, obj_pixels = contact_sheet(unit, gutter, 2, vocab[:2])
    write_pair(sheets, "obj000", obj_pixels, scale)
    (sheets / "obj000.json").write_text(
        serialize_sheet("object", unit, gutter, 2, "obj000.png", "obj000.orig.png", obj_cells),
        encoding="utf-8")

    if sprite_sheet:
        # HdPackBuilder::WriteSpriteSheets — the OAM half of the pack. Same
        # geometry as the object sheet, so the two differ only in `kind`.
        spr_cells, spr_pixels = contact_sheet(unit, gutter, 2, vocab[:2])
        write_pair(sheets, "spr000", spr_pixels, scale)
        (sheets / "spr000.json").write_text(
            serialize_sheet("sprite", unit, gutter, 2, "spr000.png", "spr000.orig.png", spr_cells),
            encoding="utf-8")

    # Key source: only cells 0..1 are known keys, and tile 0 carries
    # non-default brightness/defaultTile that the build must carry over.
    lines = ["<ver>107", "<scale>2", "<system>nes",
             "<supportedRom>2A4E126D0286BEA0BF503C80A12352C57539F76B", "<img>old.png"]
    for shape in range(8):
        extra = "0.5,Y" if shape == 0 else "1,N"
        key = f"{CHR_INDEX_BASE + shape:02X}" if chr_rom else tile_hex(shape)
        lines.append(f"<tile>0,{key},{PAL_HEX},0,0,{extra}")
    (folder / "textures" / "hires.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
    EMIT_TILE_INDEX = False
    return folder, vocab, cells


def assert_pixel_exact(folder: Path, tiles, scale: int, label: str):
    """PRD Phase 9 validation test 6: every emitted crop must be byte-identical
    to what SheetRender::RenderMetatile drew for that key."""
    sheets = folder / "textures" / "sheets"
    cache = {}
    span = 8 * scale
    for shape in range(20):
        key = (tile_hex(shape), PAL_HEX)
        if key not in tiles:
            continue
        img_index, x, y, _f = tiles[key]
        imgs, _ = parse_hires(folder / "textures" / "hires.txt")
        rel = imgs[img_index]
        if rel not in cache:
            cache[rel] = png_read(sheets / Path(rel).name)
        got = crop(cache[rel], x, y, span)
        want_1x = blank(8, 8)
        render_tile(shape, want_1x, 0, 0, 8, 8)
        want = upscale(want_1x, scale) if scale > 1 else want_1x
        if got != want:
            fail(f"{label}: crop for shape {shape} at {rel}({x},{y}) is not pixel-identical to RenderMetatile")
            return False
    ok(f"{label}: every sliced crop is pixel-identical to what the builder drew")
    return True


def paint(folder: Path, png_name: str, x: int, y: int, size: int, color: int = 0xFFA020F0):
    """Paint a `size`x`size` block into a sheet, leaving its `*.orig.png` twin
    untouched — exactly what an artist does in an image editor."""
    path = folder / "textures" / "sheets" / png_name
    px = png_read(path)
    for py in range(y, min(y + size, len(px))):
        for pxx in range(x, min(x + size, len(px[0]))):
            px[py][pxx] = color
    path.write_bytes(png_rgba(px))


def sheet_fold_tests(root: Path):
    """ADR-0230 Decision item 2 (F14.9): a sidecar tile entry's `folds` are
    palettes the cell reproduces exactly at one Brightness. Build emits one
    exact defaultTile=N rule per fold, on the cell's own crop, painted or not,
    and a sidecar without the field builds as it always did."""
    def with_folds(folder, folds_by_shape):
        # SerializeSheet lists a shape's folds on its entry in every sheet.
        for path in sorted((folder / "textures" / "sheets").glob("*.json")):
            doc = json.loads(path.read_text(encoding="utf-8"))
            for cell in doc.get("cells") or []:
                for entry in cell.get("tiles") or []:
                    for shape, folds in folds_by_shape.items():
                        if entry and entry.get("tile") == tile_hex(shape):
                            entry["folds"] = folds
            path.write_text(json.dumps(doc, indent=1), encoding="utf-8")

    folder, _v, _c = make_sheet_folder(root, "sheet-folds")
    plain_folder, _v, _c = make_sheet_folder(root, "sheet-folds-plain")
    with_folds(folder, {0: [{"palette": "0f0f0f0f", "brightness": 0}],
                        5: [{"palette": "0F162A0F", "brightness": 1}, {"palette": "0F062A30", "brightness": 0.75}]})
    if run("build", str(folder)) is None or run("build", str(plain_folder)) is None:
        return
    hires = folder / "textures" / "hires.txt"
    first = hires.read_bytes()
    _imgs, tiles = parse_hires(hires)
    base, fold = tiles.get((tile_hex(0), PAL_HEX)), tiles.get((tile_hex(0), "0F0F0F0F"))
    if not fold or fold[:3] != base[:3] or fold[3][5:7] != ["0", "N"]:
        fail(f"a fold is not an exact Brightness rule on its cell's crop: {fold} vs {base}")
    else:
        ok("ADR-0230: a fold emits a defaultTile=N rule at its Brightness on the cell's own crop")
    inert, dim = tiles.get((tile_hex(5), "0F162A0F")), tiles.get((tile_hex(5), "0F062A30"))
    if not inert or not dim or inert[3][5] != "1" or dim[3][5] != "0.75" or inert[:3] != tiles[(tile_hex(5), PAL_HEX)][:3]:
        fail(f"every fold of an entry gets its own rule: {inert} {dim}")
    else:
        ok("ADR-0230: every fold of an entry gets its own rule, in the palette it names")
    if tiles[(tile_hex(0), PAL_HEX)][3][5:7] != ["0.5", "Y"]:
        fail(f"a fold changed its base key's own rule: {tiles[(tile_hex(0), PAL_HEX)]}")
    else:
        ok("ADR-0230: the base key keeps the fields the key source carries")
    _p, plain = parse_hires(plain_folder / "textures" / "hires.txt")
    if set(tiles) - set(plain) != {(tile_hex(0), "0F0F0F0F"), (tile_hex(5), "0F162A0F"), (tile_hex(5), "0F062A30")}:
        fail(f"folds added more than their own keys: {sorted(set(tiles) ^ set(plain))}")
    else:
        ok("ADR-0230: a sidecar without folds builds exactly the keys it always did")
    if run("build", str(folder)) is None or hires.read_bytes() != first:
        fail("a second build of a pack with folds is not byte-identical")
    else:
        ok("ADR-0230: a second build of a pack with folds is byte-identical")

    bad, _v, _c = make_sheet_folder(root, "sheet-folds-bad")
    with_folds(bad, {0: [{"palette": PAL_HEX, "brightness": 1}]})
    out = run("build", str(bad), expect=1)
    if out is None or "repeats the entry's own palette" not in out:
        fail(f"a fold naming the entry's own palette was not refused by the lint gate: {out}")
    else:
        ok("ADR-0230: a malformed fold fails the lint gate instead of vanishing")


def edited_precedence_tests(root: Path):
    """ADR-0153 §4: a cell claims a tile key only when it was actually painted,
    measured against the `*.orig.png` twin. This is what lets PRD Phase 9
    validation tests 3 (paint a bush in metatiles.png) and 4 (paint a seam in
    map-NNN.png) both work — no static order can satisfy both."""
    shapes = (8, 9, 10, 11)  # metatile 2: on metatiles.png AND on map-000.png

    def owners(folder):
        imgs, tiles = parse_hires(folder / "textures" / "hires.txt")
        return {imgs[tiles[(tile_hex(s), PAL_HEX)][0]] for s in shapes}

    # PRD test 3: paint one metatile cell; the untouched map must not win.
    a, _v, cells = make_sheet_folder(root, "edited-metatiles")
    paint(a, "metatiles.png", cells[2]["x"], cells[2]["y"], 16)
    out = run("build", str(a))
    if out is None:
        return
    if owners(a) != {"sheets/metatiles.png"}:
        fail(f"a painted metatile cell lost to an untouched map: {owners(a)}")
    elif "(painted)" not in out:
        fail(f"the painted cell's win over the untouched map was not logged:\n{out}")
    else:
        ok("PRD test 3: a painted metatiles.png cell beats an untouched map covering the same key")

    # PRD test 4: paint the map; the untouched vocabulary must not win.
    b, _v, _c = make_sheet_folder(root, "edited-map")
    paint(b, "map-000.png", 0, 16, 16)
    out = run("build", str(b))
    if out is None:
        return
    if owners(b) != {"sheets/map-000.png"}:
        fail(f"a painted map lost to the untouched metatile vocabulary: {owners(b)}")
    elif "(untouched)" not in out:
        fail(f"the untouched vocabulary's loss was not logged:\n{out}")
    else:
        ok("PRD test 4: a painted map-NNN.png region beats the untouched metatile vocabulary")

    # Both painted: the static rank breaks the tie (map > metatiles), logged.
    c, _v, c_cells = make_sheet_folder(root, "edited-both")
    paint(c, "metatiles.png", c_cells[2]["x"], c_cells[2]["y"], 16)
    paint(c, "map-000.png", 0, 16, 16, color=0xFF20A0F0)
    out = run("build", str(c))
    if out is None:
        return
    if owners(c) != {"sheets/map-000.png"}:
        fail(f"two painted sheets did not fall back to the static rank: {owners(c)}")
    elif "(precedence)" not in out:
        fail(f"the static-rank override was not logged:\n{out}")
    else:
        ok("both painted: the static rank decides (map > metatiles) and the override is logged")

    # A sheet with no reference twin has nothing to diff against: every cell
    # counts as painted, and the static rank keeps deciding.
    d, _v, _c = make_sheet_folder(root, "edited-blind")
    (d / "textures" / "sheets" / "map-000.orig.png").unlink()
    out = run("build", str(d))
    if out is None:
        return
    if owners(d) != {"sheets/map-000.png"}:
        fail(f"a twin-less sheet did not fall back to the static rank: {owners(d)}")
    elif "every cell counts as painted" not in out:
        fail(f"a missing reference twin was not reported:\n{out}")
    else:
        ok("a sheet with no *.orig.png twin counts as fully painted and keeps its static rank")

    # #346: a twin that exists but is the wrong size is not "no evidence" —
    # the pair is meant to grow together, so a mismatch proves a half-grown
    # grow rather than an absent one. This must refuse, not go blind.
    # Reproduce it the way a real grow happens: a 7th cell added to the
    # vocabulary spills contact_sheet into a new row, so metatiles.png and
    # its sidecar both grow — but *.orig.png is left at the old, smaller size.
    e, vocab_e, _c = make_sheet_folder(root, "edited-half-grown")
    sheets_e = e / "textures" / "sheets"
    grown_vocab = vocab_e + [{"count": 1, "context": "scene", "metatile": 6, "tiles": [0]}]
    grown_cells, grown_pixels = contact_sheet(16, 1, 3, grown_vocab)
    (sheets_e / "metatiles.png").write_bytes(png_rgba(grown_pixels))
    (sheets_e / "metatiles.json").write_text(
        serialize_sheet("metatiles", 16, 1, 3, "metatiles.png", "metatiles.orig.png", grown_cells),
        encoding="utf-8")
    out = run("build", str(e), expect=2)
    if out is None:
        return
    if "#346" not in out or "must grow together" not in out:
        fail(f"a half-grown sheet/twin pair was not refused with the #346 reason:\n{out}")
    else:
        ok("#346: a sheet grown without its *.orig.png twin refuses the build instead of going blind")


def screen_residency_tests(root: Path):
    """ADR-0156 (F9.9): the cells a captured screen owns leave `metatiles.png`,
    so the sidecar stops listing them and no sheet claims their tile keys any
    more. Two things have to hold for that to be safe: the build must not
    break, and the `<background>` line plus its PNG - the surface those cells
    were routed *to* - must come through the rebuild intact."""
    folder, vocab, cells = make_sheet_folder(root, "screen-resident")
    sheets = folder / "textures" / "sheets"
    unit, gutter, columns = 16, 1, 3

    # Cell 5 is the routed one: metatiles-only, so nothing else can claim its
    # keys. The builder would not have drawn it at all; dropping it from the
    # sidecar is the same thing as far as the slicing contract goes.
    kept = [c for c in cells if c["index"] != 5]
    (sheets / "metatiles.json").write_text(
        serialize_sheet("metatiles", unit, gutter, columns, "metatiles.png",
                        "metatiles.orig.png", kept),
        encoding="utf-8")

    # The screen the cell was routed to, as the emulator writes it: a
    # condition-gated <background> in the body, PNG under auto/textures only.
    hires = folder / "textures" / "hires.txt"
    bg_line = "[screen001_A&screen001_B]<background>backgrounds/screen001.png,1,0,0,20"
    hires.write_text(hires.read_text(encoding="utf-8") + bg_line + "\n", encoding="utf-8")
    auto_bg = folder / "auto" / "textures" / "backgrounds"
    auto_bg.mkdir(parents=True)
    (auto_bg / "screen001.png").write_bytes(png(256, 240))

    out = run("build", str(folder))
    if out is None:
        return
    _imgs, tiles = parse_hires(hires)
    routed = [s for s in (18, 19) if (tile_hex(s), PAL_HEX) in tiles]
    if routed:
        fail(f"a routed cell still claims tile keys {routed} after the rebuild")
    else:
        ok("ADR-0156: a cell routed to the screen surface claims no tile key")
    if (tile_hex(0), PAL_HEX) not in tiles:
        fail("routing one cell off metatiles.png took the rest of the sheet with it")
    else:
        ok("ADR-0156: the cells that stayed on the sheet still round-trip")
    body = [ln.strip() for ln in hires.read_text(encoding="utf-8").splitlines()]
    if bg_line not in body:
        fail("the <background> line the cells were routed to did not survive the rebuild")
    elif not (folder / "textures" / "backgrounds" / "screen001.png").is_file():
        fail("the captured screen was not copied up into textures/")
    else:
        ok("ADR-0156: the captured screen survives the rebuild, line and PNG")

    # Issue #170: a capture draws above every <tile>, so on the scenes it
    # covers the sheets are not the surface an artist paints. The build says so
    # - without it, repainting metatiles.png and seeing no change in game has
    # no explanation anywhere.
    #
    # "and only there" is load-bearing, and was added after the 2026-09-19 cold
    # read: the old wording said a capture draws over every <tile> on the scenes
    # it covers, the evaluator read that as "my paint will never show here",
    # and it showed. A warning that overstates its scope is a defect of its own,
    # so the message has to name the frames each capture owns, not just that
    # captures exist.
    if ("captured screen(s)" not in out or "backgrounds/screen001.png" not in out
            or "and only there" not in out):
        fail(f"the build does not scope the captured screen to the frames it owns:\n{out}")
    else:
        ok("the build points at backgrounds/screen001.png and scopes it to the frames it owns")

    # #339: the capture's gate is a handful of `tileAtPosition` probes, not the
    # whole frame. Measured on the F12.2 sweep's Mike Tyson's Punch-Out!!: the
    # frame the dispatcher minted renders with the game's own STARRING /
    # LITTLE MAC text erased, and a pixelwise compare against the pack's ten
    # captures finds no exact match at all - the closest, screen003.png, is
    # 16 047 pixels away, and its whole gate is three <condition> lines
    # (screen003_A/_B/_C, tileAtPosition). So the run time picks a capture on a
    # frame it was not frozen for.
    #
    # Decision, recorded here rather than as a new rule: the precedence is NOT
    # weakened. ADR-0050 and ADR-0156 chose it deliberately, the build cannot
    # evaluate a tileAtPosition probe (it has no PPU and no ROM state), and
    # tightening the gate is the recorder's job in Core/NES/HdPacks, not the
    # packer's. What the build owes the artist is (a) to stop implying the gate
    # is exact, and (b) to give a way out - which is #344's retirement. Both
    # are in the one message below; the guide says the same thing in prose.
    if "tileAtPosition probes" not in out or "#339" not in out:
        fail(f"the build does not say the capture's gate is approximate:\n{out}")
    else:
        ok("#339: the build says a capture is gated on probes, not on the whole frame")

    # #344: a capture is retired by deleting its PNG. Before this, the build
    # carried the <background> line over from the previous hires.txt and never
    # re-derived it, so the artist had no exit: keeping the capture made the
    # repaint a no-op, deleting it made the build fail with one
    # `<background> ... does not exist` per file (Tennis (1984): 5 errors,
    # exit 1, on a pack the engine itself loads fine - HdPackLoader drops a
    # dangling entry at load). Now the line is dropped and counted.
    (folder / "textures" / "backgrounds" / "screen001.png").unlink()
    (auto_bg / "screen001.png").unlink()
    out = run("build", str(folder))
    if out is None:
        return
    body = [ln.strip() for ln in hires.read_text(encoding="utf-8").splitlines()]
    if bg_line in body:
        fail("a capture deleted from both layers is still referenced by the rebuilt manifest")
    elif "retired 1 captured screen(s)" not in out or "#344" not in out:
        fail(f"the retirement was silent:\n{out}")
    else:
        ok("#344: deleting the capture retires it — the <background> line is dropped, and counted")
    if run("build", str(folder)) is None:
        fail("#344: a second build over a retired capture is not clean")
    else:
        ok("#344: retirement is idempotent — a rebuild neither re-adds nor re-reports it")


def windows_path_carry_tests(root: Path):
    """#381: a carried line whose file name uses `\\` resolves the way
    HdPackLoader resolves it — the loader rewrites every `\\` to `/` before it
    parses a tag, so `Backdrops\\shot.png` is `Backdrops/shot.png` to the
    emulator on every OS. The build read the raw text, so on POSIX the name was
    one literal file, never found, and the line was "retired" with an info log
    (702 `<background>` lines on the Mega Man (USA) community pack). The
    `<bgm>`/`<sfx>` seed filter had the same read."""
    folder = make_author_folder(root, name="win-paths")
    hires = folder / "textures" / "hires.txt"
    kept = "<background>Backdrops\\shot.png,1,0,0,20"
    gone = "[c]<background>Backdrops\\gone.png,1,0,0,20"
    hires.write_text(hires.read_text(encoding="utf-8")
                     + "<condition>c,memoryCheck,30,=,3\n" + kept + "\n" + gone + "\n"
                     + "<bgm>0,7,Music\\theme.ogg\n", encoding="utf-8")
    (folder / "auto" / "textures" / "Backdrops").mkdir(parents=True)
    (folder / "auto" / "textures" / "Backdrops" / "shot.png").write_bytes(png(256, 240))
    (folder / "audio" / "Music").mkdir()
    (folder / "audio" / "Music" / "theme.ogg").write_bytes(b"")

    out = run("build", str(folder))
    if out is None:
        return
    body = [ln.strip() for ln in hires.read_text(encoding="utf-8").splitlines()]
    if "<background>Backdrops/shot.png,1,0,0,20" not in body:
        fail(f"a backslash <background> whose PNG exists did not survive the rebuild:\n{out}")
    elif not (folder / "textures" / "Backdrops" / "shot.png").is_file():
        fail("the backslash-named background was not copied up into textures/")
    elif any("\\" in ln for ln in body):
        fail("the rebuilt manifest still carries a backslash path")
    else:
        ok("#381: a Windows-style <background> resolves, is copied up, and is emitted as the "
           "loader reads it")
    if gone in body or "Backdrops/gone.png" in "\n".join(body):
        fail("a backslash <background> with no PNG in either layer was not retired")
    elif "warning: retired 1 captured screen(s)" not in out or "1 <background> line(s) dropped" not in out:
        fail(f"the retirement is not a counted warning:\n{out}")
    else:
        ok("#381: retiring a carried line is a warning that counts the lines dropped")
    audio = (folder / "audio" / "hires.txt").read_text(encoding="utf-8")
    if "<bgm>0,7,Music/theme.ogg" not in audio or "Music\\theme.ogg" in audio:
        fail(f"a backslash <bgm> seed ref was not kept under its normalized name:\n{audio}")
    else:
        ok("#381: a Windows-style <bgm> seed reference resolves and is emitted with '/'")


def muted_paint_tests(root: Path):
    """#338 and #343: a painted cell that reaches nothing must say so.

    Both defects come out of the 2026-09-19 F12.2 cold read, and both are the
    same artist question - "did the cell I just painted reach the screen?" -
    answered by silence.

    #343, measured on Dr. Mario (1990) (Nintendo): appending a cell to
    `misc.json` whose key an earlier crop of the same sheet already owns, and
    painting it magenta, left the rebuilt `textures/hires.txt` byte-identical
    to the previous build. The entire signal was one info line moving from
    `19 crop(s) repeat a tile key` to `20 crop(s) repeat a tile key` - no key
    named, no mention of paint, no change to the `tile keys: ...` delta line,
    exit 0, lint 0.

    #338, measured on Pac-Man (1984) (Namco): the build warned about 19 sheets
    and stayed silent about `unsorted.png`, the one the evaluator had edited.
    `screen_shadowed_cells()` keyed on `adjacency.json`'s `metatile` cell id,
    which a freshly pasted cell does not carry, so the check was blind to
    exactly the edit that needed judging - while claiming, of 19 other sheets,
    that "painting this sheet will not show in game". Keying on the tile finds
    the edited sheet and only it: 1 warning, naming `sheets/unsorted.png`."""
    # #343. The last cell of metatiles.json is re-pointed at cell 0's keys —
    # the artist pasting a key that is already on the sheet. Both cells are
    # painted, so the later one cannot win and emits no <tile> of its own.
    folder, _vocab, cells = make_sheet_folder(root, "muted-repeat")
    sheets = folder / "textures" / "sheets"
    unit, gutter, columns = 16, 1, 3
    twin = [dict(c) for c in cells]
    twin[5]["tiles"] = list(twin[0]["tiles"])
    (sheets / "metatiles.json").write_text(
        serialize_sheet("metatiles", unit, gutter, columns, "metatiles.png",
                        "metatiles.orig.png", twin), encoding="utf-8")
    paint(folder, "metatiles.png", twin[0]["x"], twin[0]["y"], 16)
    paint(folder, "metatiles.png", twin[5]["x"], twin[5]["y"], 16)
    out = run("build", str(folder))
    if out is None:
        return
    if "(#343)" not in out or tile_hex(0) not in out:
        fail(f"a painted cell whose key was already claimed was dropped without a word:\n{out}")
    else:
        ok("#343: a painted cell that emits no <tile> is named, with the key and the owner")

    # #338. A screen-resident node that carries tile keys and no `cell` id -
    # the shape the old lookup could not see - plus a painted cell holding one
    # of those keys.
    folder, _vocab, _cells = make_sheet_folder(root, "muted-shadow")
    sheets = folder / "textures" / "sheets"
    node = ('{"tile": "%s", "palette": "%s"}' % (tile_hex(0), PAL_HEX))
    (sheets / "adjacency.json").write_text(
        '{"version": 1, "kind": "adjacency", "background": {"nodes": ['
        '{"count": 9, "screens": [{"screen": "screen007", "x": 0, "y": 0}], '
        f'"tiles": [{node}]}}], "edges": []}}}}', encoding="utf-8")
    # The capture has to be on disk: a capture the artist retired (#344) must
    # stop warning about itself, so the report only counts the ones still there.
    (folder / "textures" / "backgrounds").mkdir(parents=True)
    (folder / "textures" / "backgrounds" / "screen007.png").write_bytes(png(256, 240))
    paint(folder, "metatiles.png", 1, 1, 16)
    out = run("build", str(folder))
    if out is None:
        return
    if "(#338)" not in out or "backgrounds/screen007.png" not in out:
        fail(f"a painted cell a capture also draws was not reported:\n{out}")
    elif "sheets/metatiles.png" not in out.split("(#338)")[0].rsplit("warning:", 1)[-1]:
        fail(f"the capture warning does not name the sheet that was painted:\n{out}")
    else:
        ok("#338: the capture warning is keyed on the tile and names the painted sheet")

    # And it stays quiet about the sheets nobody painted - the 19-vs-1 of the
    # Pac-Man measurement. map-000.png and obj000.png hold the same key and are
    # untouched, so they must not be warned about.
    shadow = [ln for ln in out.splitlines() if "(#338)" in ln]
    if len(shadow) != 1:
        fail(f"the capture warning fired for {len(shadow)} sheet(s), expected only the painted one")
    else:
        ok("#338: untouched sheets holding the same key are not warned about")


# `Core/NES/NesDefaultVideoFilter.cpp`'s 2C02 row for the four colours of
# PAL_HEX (0F 16 2A 30): the palette a recorded `screenNNN.orig.png` is drawn
# with. The sheet fixtures above use a stand-in palette on purpose; a capture
# has to carry the real colours, or it would not be the recorder's.
CAPTURE_RGB = (0x000000, 0xB53120, 0x5CE430, 0xFFFEFF)


def capture_png(shapes, scale: int = 1) -> bytes:
    """A `screenNNN.orig.png`: a 256x240 backdrop with each (shape, x, y) of
    `shapes` drawn in the 2C02 colours of PAL_HEX, nearest-upscaled by `scale`
    exactly as `HdPackBuilder::CaptureScreen` writes the twin."""
    pixels = [[0xFF101010] * 256 for _ in range(240)]
    for shape, x, y in shapes:
        data = tile_bytes(shape)
        for row in range(8):
            lo, hi = data[row], data[row + 8]
            for col in range(8):
                bit = 7 - col
                c = ((lo >> bit) & 1) | (((hi >> bit) & 1) << 1)
                pixels[y + row][x + col] = 0xFF000000 | CAPTURE_RGB[c]
    return png_rgba(upscale(pixels, scale) if scale > 1 else pixels)


def add_unsorted_cell(folder: Path, shape: int, chr_rom: bool):
    """Give the pack an `unsorted` sheet, place `shape` on it with
    `scripts/mep_add_cell.py` exactly as a reader pastes a copy line, and paint
    the slot it reports. Returns the placer's output, or None on failure."""
    global EMIT_TILE_INDEX
    sheets = folder / "textures" / "sheets"
    EMIT_TILE_INDEX = chr_rom
    cells, pixels = contact_sheet(8, 1, 5, [{"count": 4, "context": "misc", "tiles": [18]}])
    write_pair(sheets, "unsorted", pixels, 1)
    (sheets / "unsorted.json").write_text(
        serialize_sheet("unsorted", 8, 1, 5, "unsorted.png", "unsorted.orig.png", cells), encoding="utf-8")
    EMIT_TILE_INDEX = False
    tile = {"tile": tile_hex(shape), "palette": PAL_HEX}
    if chr_rom:
        tile["index"] = CHR_INDEX_BASE + shape
    (folder.parent / f"{folder.name}-cell.json").write_text(json.dumps({"count": 1, "tiles": [tile]}), encoding="utf-8")
    p = subprocess.run([PY, str(REPO / "scripts" / "mep_add_cell.py"), str(folder), str(folder.parent / f"{folder.name}-cell.json")],
                       capture_output=True, text=True)
    out = p.stdout + p.stderr
    at = [ln for ln in out.splitlines() if "painted at" in ln]
    if p.returncode != 0 or not at:
        fail(f"mep_add_cell.py could not place the fixture cell (exit {p.returncode}):\n{out}")
        return None
    x, y = (int(v) for v in at[0].split("painted at", 1)[1].split("on", 1)[0].split(","))
    paint(folder, "unsorted.png", x, y, 8)
    return out


def capture_shadow_tests(root: Path):
    """#422: the #338 warning, for a cell `mep_add_cell.py` placed. Its key is
    also on `metatiles.png`, so the recorder never marked it screen-resident
    and `adjacency.json` carries no `screens[]` for it (ADR-0156 §1: a node a
    sheet shows carries none) — yet a live capture draws it. The F14.2 cold
    read hit this on 10 of 27 runs; the Mario Bros. repro is tile 403 under
    `screen002`. Which capture draws the key is read off the capture's own
    `.orig.png`, the only evidence a pack on disk has for a sheet-shown key."""
    shape = 5  # on metatiles cell 1 (tiles 4..7), so the placer reports a claim
    folder, _vocab, _cells = make_sheet_folder(root, "shadow-added-cell", chr_rom=True)
    sheets = folder / "textures" / "sheets"
    node = '{"tile": "%s", "palette": "%s", "index": %d}' % (tile_hex(shape), PAL_HEX, CHR_INDEX_BASE + shape)
    (sheets / "adjacency.json").write_text(
        '{"version": 1, "kind": "adjacency", "background": {"nodes": ['
        f'{{"cell": 1, "count": 68, "context": "scene", "tiles": [{node}]}}], "edges": []}}}}',
        encoding="utf-8")
    backgrounds = folder / "textures" / "backgrounds"
    backgrounds.mkdir(parents=True)
    for name, shapes in (("screen001", [(9, 64, 64)]), ("screen002", [(shape, 0, 72), (shape, 8, 72)])):
        (backgrounds / f"{name}.png").write_bytes(png(256, 240))
        (backgrounds / f"{name}.orig.png").write_bytes(capture_png(shapes))
    if add_unsorted_cell(folder, shape, chr_rom=True) is None:
        return
    out = run("build", str(folder))
    if out is None:
        return
    shadow = [ln for ln in out.splitlines() if "(#338)" in ln]
    if not shadow or "sheets/unsorted.png" not in shadow[0] or "backgrounds/screen002.png" not in shadow[0]:
        fail(f"#422: a cell mep_add_cell.py placed and a capture draws got no (#338) line naming it:\n{out}")
    else:
        ok("#422: an added-and-painted cell a capture draws gets its (#338) line, naming the capture")
    if len(shadow) != 1 or "backgrounds/screen001.png" in "".join(shadow):
        fail(f"#422: the (#338) line names a capture that does not draw the key, or fired twice: {shadow}")
    else:
        ok("#422: the capture that does not draw the key is not named")

    # Ice Climber / Pac-Man: build named a capture other than the one covering
    # the frame. Here the adjacency provenance points at screen003 for the key,
    # and screen004 (2x, fine x scroll 3 read off its tileAtPosition probe, rows
    # one line down as Ice Climber draws them) also draws it; screen001 does
    # not. Every capture that draws it is named.
    folder, _vocab, _cells = make_sheet_folder(root, "shadow-which-capture")
    sheets = folder / "textures" / "sheets"
    node = '{"tile": "%s", "palette": "%s"}' % (tile_hex(shape), PAL_HEX)
    (sheets / "adjacency.json").write_text(
        '{"version": 1, "kind": "adjacency", "background": {"nodes": ['
        '{"count": 2, "screens": [{"screen": "screen003", "x": 0, "y": 0}], '
        f'"tiles": [{node}]}}], "edges": []}}}}', encoding="utf-8")
    backgrounds = folder / "textures" / "backgrounds"
    backgrounds.mkdir(parents=True)
    for name, shapes in (("screen001", [(9, 64, 64)]), ("screen003", []), ("screen004", [(shape, 3 + 80, 17)])):
        (backgrounds / f"{name}.png").write_bytes(png(512, 480))
        (backgrounds / f"{name}.orig.png").write_bytes(capture_png(shapes, 2))
    source = folder / "textures" / "hires.txt"
    source.write_text(source.read_text(encoding="utf-8")
                      + f"<condition>screen004_A,tileAtPosition,3,16,{tile_hex(9)},{PAL_HEX}\n", encoding="utf-8")
    if add_unsorted_cell(folder, shape, chr_rom=False) is None:
        return
    out = run("build", str(folder))
    if out is None:
        return
    shadow = "".join(ln for ln in out.splitlines() if "(#338)" in ln)
    if "backgrounds/screen004.png" not in shadow:
        fail(f"#422: the capture that draws the painted key (screen004) is not named:\n{out}")
    elif "backgrounds/screen001.png" in shadow:
        fail(f"#422: a capture that does not draw the key is named: {shadow}")
    elif "backgrounds/screen003.png" not in shadow:
        fail(f"#422: the recorder's own screens[] provenance was dropped: {shadow}")
    else:
        ok("#422: the (#338) line names every capture that draws the key, and only those")


def sheet_alias_tests(root: Path):
    """ADR-0153 §3 alias pass (F9.7): a cell absorbs the vocabulary entries that
    render to the same pixels, and the build must emit a <tile> for every one of
    them from the single painted crop. Without the fan-out a bank-swapped
    duplicate would keep its old art and the two copies would drift apart on
    screen - which is exactly what happened when each was painted by hand."""
    folder, vocab, cells = make_sheet_folder(root, "sheets-alias")
    sheets = folder / "textures" / "sheets"
    # Cell 4 absorbs two entries that render identically under other keys. It is
    # deliberately a metatiles-only cell: cells 0..3 also appear on the map and
    # object sheets, where the §4 precedence rule would resolve the canonical
    # key from a higher-ranked sheet than the alias, and the comparison below
    # would be measuring precedence rather than the fan-out.
    cells[4]["aliases"] = [{"metatile": 6, "tiles": [40, None, 41, None]},
                           {"metatile": 7, "tiles": [44, None, 45, None]}]
    (sheets / "metatiles.json").write_text(
        serialize_sheet("metatiles", 16, 1, 3, "metatiles.png", "metatiles.orig.png", cells),
        encoding="utf-8")

    out = run("build", str(folder))
    if out is None:
        return
    _imgs, tiles = parse_hires(folder / "textures" / "hires.txt")
    # The 20 keys of the base fixture plus 2 aliases x 2 resolved tiles.
    if len(tiles) != 24:
        fail(f"alias fan-out emitted {len(tiles)} keys, expected 24")
    else:
        ok("an aliased cell emits a <tile> for its own keys and for every alias")

    canonical = tiles.get((tile_hex(16), PAL_HEX))
    absorbed = [tiles.get((tile_hex(40), PAL_HEX)), tiles.get((tile_hex(44), PAL_HEX))]
    if canonical is None or any(a is None for a in absorbed):
        fail("an alias key is missing from the built hires.txt")
    elif not all(a[:3] == canonical[:3] for a in absorbed):
        fail("an alias was not painted from the canonical cell's crop")
    else:
        ok("every alias takes its art from the canonical cell's crop, so they cannot drift")


def map_full_vocabulary_tests(root: Path):
    """#416: a map placement names an absolute vocabulary index, and that is
    not a `metatiles.json` cell (ADR-0164 §1). A recording places entries that
    sit inside another cell's `aliases[]`, on `misc.json`, or on no sheet at
    all because a captured screen owns them (ADR-0156). The build used to look
    the index up in `metatiles.json` only and fall back to *array position*:
    past the end the crop was dropped with a warning (Castlevania: 3), and
    inside it the crop was emitted under another cell's keys without a word
    (Castlevania: 22). Every such placement has to resolve to its own keys."""
    folder, vocab, cells = make_sheet_folder(root, "map-full-vocab")
    sheets = folder / "textures" / "sheets"
    # Vocabulary entry 2 is routed to a captured screen: off metatiles.json,
    # so position 2 of the array now holds entry 3. Entry 7 is an alias
    # absorbed by cell 4, entry 8 is a misc cell.
    kept = [dict(c, index=i) for i, c in enumerate(c for c in cells if c["metatile"] != 2)]
    kept[3]["aliases"] = [{"metatile": 7, "tiles": [40, 41, 42, 43]}]
    (sheets / "metatiles.json").write_text(
        serialize_sheet("metatiles", 16, 1, 3, "metatiles.png", "metatiles.orig.png", kept),
        encoding="utf-8")
    misc_vocab = [{"count": 1, "context": "misc", "metatile": 8, "tiles": [30, 31, 32, 33]}]
    misc_cells, misc_pixels = contact_sheet(16, 1, 1, misc_vocab)
    write_pair(sheets, "misc", misc_pixels, 1)
    (sheets / "misc.json").write_text(
        serialize_sheet("misc", 16, 1, 1, "misc.png", "misc.orig.png", misc_cells), encoding="utf-8")
    entries = {i: {"tiles": v["tiles"]} for i, v in enumerate(vocab)}
    entries.update({7: {"tiles": [40, 41, 42, 43]}, 8: {"tiles": [30, 31, 32, 33]}})
    nodes = ",\n      ".join(f'{{ "cell": {i}, "count": 1, "context": "scene", "tiles": {_tiles_json(e["tiles"])} }}'
                             for i, e in sorted(entries.items()))
    adjacency = sheets / "adjacency.json"
    adjacency.write_text('{\n  "version": 1,\n  "kind": "adjacency",\n  "background": {\n'
                         f'    "vocabulary": "metatiles.json",\n    "nodes": [\n      {nodes}\n    ],\n'
                         '    "edges": []\n  }\n}\n', encoding="utf-8")
    placements = [(0, 0, 5), (16, 0, 7), (0, 16, 2), (16, 16, 8)]
    write_pair(sheets, "map-000", map_sheet(16, placements, entries, 32, 32), 1)
    map_json = sheets / "map-000.json"
    map_json.write_text(serialize_sheet("map", 16, 0, 1, "map-000.png", "map-000.orig.png", [],
                                        placements=placements, mode="continuous"), encoding="utf-8")

    out = run("build", str(folder))
    if out is None:
        return
    imgs, tiles = parse_hires(folder / "textures" / "hires.txt")

    def source(shape):
        hit = tiles.get((tile_hex(shape), PAL_HEX))
        return (imgs[hit[0]], hit[1], hit[2]) if hit else None

    if "do not resolve" in out:
        fail(f"#416: a placement of the recording's own vocabulary did not resolve:\n{out}")
    else:
        ok("#416: every placement resolves, aliases, misc and routed entries included")
    if source(40) != ("sheets/map-000.png", 16, 0) or source(43) != ("sheets/map-000.png", 24, 8):
        fail(f"#416: the alias placement was not sliced from the map: {source(40)}, {source(43)}")
    else:
        ok("#416: a placement of an alias entry is sliced from the map under the alias's keys")
    if source(30) != ("sheets/map-000.png", 16, 16):
        fail(f"#416: the misc placement was not sliced from the map: {source(30)}")
    else:
        ok("#416: a placement of a misc entry is sliced from the map under its own keys")
    if source(8) != ("sheets/map-000.png", 0, 16) or source(11) != ("sheets/map-000.png", 8, 24):
        fail(f"#416: the routed entry's placement did not resolve through adjacency.json: {source(8)}")
    elif (source(12) or ("",))[0] != "sheets/metatiles.png":
        fail(f"#416: entry 3's keys were taken from entry 2's map crop by array position: {source(12)}")
    else:
        ok("#416: a routed entry resolves through adjacency.json, never by array position")

    # An index nothing names is still skipped, and the warning says which.
    map_json.write_text(serialize_sheet("map", 16, 0, 1, "map-000.png", "map-000.orig.png", [],
                                        placements=placements + [(0, 0, 99)], mode="continuous"),
                        encoding="utf-8")
    out = run("build", str(folder))
    if out is None or "1 placement(s) do not resolve" not in out or "cell 99" not in out:
        fail(f"#416: an index no sidecar names was not reported by index:\n{out}")
    else:
        ok("#416: an index no sidecar names is skipped, and the warning names it")

    # A pack recorded before adjacency.json (ADR-0164) still resolves the
    # alias and the misc entry from their sheets. It cannot resolve a routed
    # entry, and must say so rather than borrow the keys of whatever sits at
    # that position of metatiles.json.
    adjacency.unlink()
    out = run("build", str(folder))
    if out is None:
        return
    imgs, tiles = parse_hires(folder / "textures" / "hires.txt")
    if "2 placement(s) do not resolve" not in out or "cell 2, 99" not in out:
        fail(f"#416: without adjacency.json the alias or misc entry stopped resolving from its sheet:\n{out}")
    else:
        ok("#416: without adjacency.json aliases and misc cells still resolve from their sheets")
    if (source(12) or ("",))[0] != "sheets/metatiles.png":
        fail(f"#416: without adjacency.json a routed entry borrowed another cell's keys: {source(12)}\n{out}")
    else:
        ok("#416: without adjacency.json a routed entry is reported, never resolved by array position")

    # A vocabulary whose cells name no `metatile` at all (and no adjacency
    # file) still resolves by array position, the only index it has.
    legacy, _v, legacy_cells = make_sheet_folder(root, "map-legacy-vocab")
    bare = [{k: v for k, v in c.items() if k != "metatile"} for c in legacy_cells]
    (legacy / "textures" / "sheets" / "metatiles.json").write_text(
        serialize_sheet("metatiles", 16, 1, 3, "metatiles.png", "metatiles.orig.png", bare),
        encoding="utf-8")
    out = run("build", str(legacy))
    if out is None:
        return
    imgs, tiles = parse_hires(legacy / "textures" / "hires.txt")
    hit = tiles.get((tile_hex(8), PAL_HEX))
    if "do not resolve" in out or not hit or imgs[hit[0]] != "sheets/map-000.png" or hit[1:3] != (0, 16):
        fail(f"#416: a vocabulary with no metatile ids no longer resolves by position: {hit}\n{out}")
    else:
        ok("#416: a vocabulary with no metatile ids still resolves by array position")


def sheet_round_trip_tests(root: Path):
    # --- identity round-trip, precedence, null/short tiles[] ---
    folder, vocab, cells = make_sheet_folder(root, "sheets-1x")
    out = run("build", str(folder))
    if out is None:
        return
    hires = folder / "textures" / "hires.txt"
    imgs, tiles = parse_hires(hires)
    # 4 cells x 4 + cell 4 (2 tiles) + cell 5 (2 tiles) = 20 distinct keys.
    if len(tiles) != 20:
        fail(f"sheet build emitted {len(tiles)} distinct tile keys, expected 20")
    else:
        ok("sheet build emits one <tile> per resolved 8x8 crop (20 keys)")
    if "sheets/metatiles.orig.png" in imgs or any(".orig.png" in i for i in imgs):
        fail(f"*.orig.png reference twin was sliced: {imgs}")
    else:
        ok("*.orig.png reference twins are never sliced nor emitted")
    if "<scale>1" not in hires.read_text(encoding="utf-8"):
        fail("build did not take <scale> from the 1x sheet art")
    else:
        ok("build takes <scale> from the sheet art when nothing else pins it")

    # Precedence: object (rank 4) > map (3) > metatiles (1).
    def img_of(shape):
        return imgs[tiles[(tile_hex(shape), PAL_HEX)][0]]

    if img_of(0) != "sheets/obj000.png" or img_of(5) != "sheets/obj000.png":
        fail(f"object sheet did not win over map/metatiles: {img_of(0)}, {img_of(5)}")
    elif img_of(8) != "sheets/map-000.png" or img_of(15) != "sheets/map-000.png":
        fail(f"map did not win over the metatile vocabulary: {img_of(8)}, {img_of(15)}")
    elif img_of(16) != "sheets/metatiles.png" or img_of(18) != "sheets/metatiles.png":
        fail(f"metatile-only cells did not stay on metatiles.png: {img_of(16)}, {img_of(18)}")
    else:
        ok("precedence metatiles < map < object holds, overrides are logged")
    if "overrides tile" not in out:
        fail(f"an override was applied without being logged:\n{out}")
    else:
        ok("every precedence override is logged")

    # The map is sliced through placements[], resolved against the sibling
    # metatiles.json vocabulary: metatile 2 is placed at map pixel (0,16), so
    # its four 8x8 tiles land row-major inside that 16x16 cell.
    map_at = {shape: tiles[(tile_hex(shape), PAL_HEX)][1:3] for shape in (8, 9, 10, 11)}
    if map_at != {8: (0, 16), 9: (8, 16), 10: (0, 24), 11: (8, 24)}:
        fail(f"map not sliced through placements[]: {map_at}")
    else:
        ok("a map is sliced through placements[] against the metatile vocabulary")

    # Cell 4 is [shape16, null, shape17, null]: the null must be skipped
    # without shifting shape17 up out of the cell's second row.
    c4 = cells[4]
    if tiles[(tile_hex(16), PAL_HEX)][1:3] != (c4["x"], c4["y"]):
        fail(f"null-skipping shifted the first tile of cell 4: {tiles[(tile_hex(16), PAL_HEX)][1:3]}")
    elif tiles[(tile_hex(17), PAL_HEX)][1:3] != (c4["x"], c4["y"] + 8):
        fail(f"a null tiles[] entry shifted the entries after it: {tiles[(tile_hex(17), PAL_HEX)][1:3]}")
    else:
        ok("a null tiles[] entry is skipped without shifting the entries after it")
    c5 = cells[5]
    if tiles[(tile_hex(19), PAL_HEX)][1:3] != (c5["x"] + 8, c5["y"]):
        fail(f"short tiles[] mis-sliced: {tiles[(tile_hex(19), PAL_HEX)][1:3]}")
    else:
        ok("a short tiles[] is sliced for what it has, not fatal")

    # Key-source attributes carried over; unknown keys default to 1,N.
    if tiles[(tile_hex(0), PAL_HEX)][3][5:7] != ["0.5", "Y"]:
        fail(f"key-source brightness/defaultTile not carried over: {tiles[(tile_hex(0), PAL_HEX)][3]}")
    elif tiles[(tile_hex(16), PAL_HEX)][3][5:7] != ["1", "N"]:
        fail(f"a sheet-only key did not default to 1,N: {tiles[(tile_hex(16), PAL_HEX)][3]}")
    else:
        ok("<tile> attributes carry over from the key source, sheet-only keys default to 1,N")

    # Nothing has been painted yet, so no cell claims anything: the static
    # rank decides and the identity round-trip must stay pixel-exact.
    assert_pixel_exact(folder, tiles, 1, "identity round-trip (nothing painted)")

    # --- painting a cell changes exactly that cell's tiles ---
    sheets_dir = folder / "textures" / "sheets"
    before = {rel: png_read(sheets_dir / Path(rel).name) for rel in imgs}
    painted = png_read(sheets_dir / "metatiles.png")
    for y in range(c4["y"], c4["y"] + 16):
        for x in range(c4["x"], c4["x"] + 16):
            painted[y][x] = 0xFF00FF00
    (sheets_dir / "metatiles.png").write_bytes(png_rgba(painted))
    if run("build", str(folder)) is None:
        return
    imgs2, tiles2 = parse_hires(hires)
    if imgs2 != imgs or set(tiles2) != set(tiles):
        fail("painting a cell changed the manifest's img list or key set")
    else:
        now = {rel: png_read(sheets_dir / Path(rel).name) for rel in imgs2}
        changed = set()
        for key, (idx, x, y, _f) in tiles2.items():
            rel = imgs2[idx]
            if crop(now[rel], x, y, 8) != crop(before[rel], x, y, 8):
                changed.add(key)
        want = {(tile_hex(16), PAL_HEX), (tile_hex(17), PAL_HEX)}
        if changed != want:
            fail(f"painting cell 4 changed {len(changed)} tile(s), expected exactly its 2: {sorted(k[0][:4] for k in changed)}")
        else:
            ok("painting one cell changes exactly the tiles of that cell, nothing else")

    # --- a 2x upscaled sheet slices at scale 2 ---
    up, _v, up_cells = make_sheet_folder(root, "sheets-2x", scale=2)
    if run("build", str(up)) is not None:
        up_imgs, up_tiles = parse_hires(up / "textures" / "hires.txt")
        if "<scale>2" not in (up / "textures" / "hires.txt").read_text(encoding="utf-8"):
            fail("a 2x sheet did not produce <scale>2")
        elif up_tiles[(tile_hex(16), PAL_HEX)][1:3] != (up_cells[4]["x"] * 2, up_cells[4]["y"] * 2):
            fail(f"2x sheet not sliced at scale 2: {up_tiles[(tile_hex(16), PAL_HEX)][1:3]}")
        else:
            ok("an upscaled (2x) sheet is sliced at scale 2")
        assert_pixel_exact(up, up_tiles, 2, "2x round-trip")

    # --- a non-integer ratio is a build error naming both sizes ---
    bad, _v, _c = make_sheet_folder(root, "sheets-bad")
    px = png_read(bad / "textures" / "sheets" / "metatiles.png")
    px = [row + [0] for row in px]  # 52 -> 53 px wide: not a whole multiple
    (bad / "textures" / "sheets" / "metatiles.png").write_bytes(png_rgba(px))
    out = run("build", str(bad), expect=2)
    if out is not None and "metatiles.png" in out and "53x35" in out and "52x35" in out:
        ok("a non-integer sheet scale is an error naming the file and both sizes")
    else:
        fail(f"non-integer sheet scale: {out}")

    # --- a map with no sibling metatiles.json is skipped, not fatal ---
    orphan, _v, _c = make_sheet_folder(root, "sheets-orphan")
    for stem in ("metatiles", "obj000"):
        for suffix in (".json", ".png", ".orig.png"):
            f = orphan / "textures" / "sheets" / f"{stem}{suffix}"
            if f.exists():
                f.unlink()
    # With no non-map sheet left, nothing pins the pack's scale from the art
    # any more and the build falls back to the key source's <scale> (2)
    # (main()'s "else 2" default). That leaves map-000's own pair — written
    # at make_sheet_folder's default 1x — declaring a size the #346 check
    # would now (correctly) refuse as half-grown; drop its twin too so this
    # stays the "no reference" case the orphan test does not care about.
    (orphan / "textures" / "sheets" / "map-000.orig.png").unlink()
    out = run("build", str(orphan))
    if out is not None and "no sibling metatiles.json" in out:
        ok("a map without its metatile vocabulary is skipped with a warning")
    else:
        fail(f"orphan map: {out}")

    # --- an unknown sidecar version is skipped, never a crash ---
    future, _v, _c = make_sheet_folder(root, "sheets-future")
    fj = future / "textures" / "sheets" / "metatiles.json"
    fj.write_text(fj.read_text(encoding="utf-8").replace('"version": 1', '"version": 2'), encoding="utf-8")
    out = run("build", str(future))
    if out is not None and "is not 1" in out:
        ok("an unknown sidecar version is skipped with a clear warning")
    else:
        fail(f"unknown sheet version: {out}")

    # --- the adjacency sidecar (ADR-0164 §2) is skipped silently ---
    # It is not a sheet: no cells[], no sheet PNG, nothing to slice - and a
    # warning on every pack would train users to ignore warnings.
    adj, _v, _c = make_sheet_folder(root, "sheets-adjacency")
    (adj / "textures" / "sheets" / "adjacency.json").write_text(
        '{"version": 1, "kind": "adjacency", "background": {"nodes": [], "edges": []}}',
        encoding="utf-8")
    out = run("build", str(adj))
    warned = "unknown sheet kind 'adjacency'" in out or "names sheet 'adjacency.png'" in out
    if out is not None and not warned and "metatiles.png" in out:
        ok("the adjacency sidecar is skipped silently and the build still slices the sheets")
    else:
        fail(f"adjacency sidecar: {out}")

    edited_precedence_tests(root)
    sheet_fold_tests(root)
    screen_residency_tests(root)
    windows_path_carry_tests(root)
    muted_paint_tests(root)
    capture_shadow_tests(root)
    chr_rom_key_tests(root)
    flip_baked_key_tests(root)
    mirror_h_pixel_key_tests(root)
    mirror_unbake_idempotency_tests(root)
    index_keyed_unflip_tests(root)
    backdrop_transparency_tests(root)
    backdrop_shared_crop_tests(root)
    backdrop_unbake_and_sprite_tests(root)
    index_keyed_alias_unflip_tests(root)
    backdrop_translucent_paint_tests(root)
    backdrop_rgb_sheet_tests(root)
    condition_fallback_twin_tests(root)
    authored_condition_round_trip_tests(root)
    painted_sprite_ownership_tests(root)


def flip_baked_key_tests(root: Path):
    """ADR-0178: a sprite shape is recorded with its OAM flips baked in, and on
    a CHR RAM game hires.txt keys by tile data — so the baked bitmap is a key
    nothing ever looks up and the rebuild has to emit the `source` the sidecar
    carries instead (issue #181)."""
    folder, _v, _c = make_sheet_folder(root, "flip-baked", flip_baked=True)
    out = run("build", str(folder))
    if out is None:
        return
    _imgs, tiles = parse_hires(folder / "textures" / "hires.txt")
    got = {k for k, _p in tiles}
    want = {tile_hex(s) for s in range(20)}
    if got == want:
        ok("ADR-0178: a flip-baked sidecar rebuilds with the unflipped keys the run time looks up")
    else:
        fail(f"flip-baked rebuild keys: missing {sorted(want - got)[:3]}, "
             f"unexpected {sorted(got - want)[:3]}")

    # A pack recorded before the ADR has no `source`. Its baked keys are
    # recognised by the un-flip test and the build fails rather than emitting
    # cells that would render nothing. Only a sprite sheet can carry a baked
    # flip, so that is where the detector has to keep firing.
    legacy, _v, _c = make_sheet_folder(root, "flip-baked-legacy", flip_baked=True,
                                       sidecar_source=False, sprite_sheet=True)
    out = run("build", str(legacy), expect=2)
    # #337: the message has to name the fix that works. "Re-record the pack" is
    # what it said, and re-recording in place reproduces this error every time -
    # the bootstrap loads the existing auto/ layer, so the cells come back.
    if out is not None and "flip-baked tile key" in out and "EMPTY folder" in out:
        ok("ADR-0178: a pack whose sidecars predate the ADR fails the build, naming the fix")
    else:
        fail(f"pre-ADR-0178 pack did not fail with the empty-folder message: {out}")
    named = sorted(line.split(":")[1].strip() for line in (out or "").splitlines()
                   if "flip-baked tile key" in line)
    if named == ["spr000.png"]:
        ok("issue #196: the flip-baked error names the sprite sheet and only the sprite sheet")
    else:
        fail(f"flip-baked error was not scoped to the sprite sheet, named {named}: {out}")

    # Issue #196: the same coincidence on a background sheet is not a baked
    # flip. The NES background has no per-tile flip bit, so a background crop
    # whose mirror happens to be another real tile of the game is ADR-0178's
    # third Consequences bullet — harmless — and must not fail a build that
    # re-recording cannot fix. Measured non-zero on Contra's base stages
    # (`runs/golden-20260913-f922/contra-rerecord-2026-09-13.md`).
    bg, _v, _c = make_sheet_folder(root, "flip-baked-background", flip_baked=True,
                                   sidecar_source=False)
    out = run("build", str(bg))
    if out is not None and "flip-baked tile key" not in out:
        ok("issue #196: a background sheet whose key mirrors a real tile builds clean")
    else:
        fail(f"background sheet still raises the ADR-0178 flip error: {out}")
    if out is not None:
        _imgs, bg_tiles = parse_hires(bg / "textures" / "hires.txt")
        want = {flip_hex(tile_hex(s)) for s in range(20)}
        got = {k for k, _p in bg_tiles}
        if got == want:
            ok("issue #196: the exempt background crops still emit their own keys, none dropped")
        else:
            fail(f"background exemption changed the emitted keys: missing {sorted(want - got)[:3]}, "
                 f"unexpected {sorted(got - want)[:3]}")


def mirror_h_pixel_key_tests(root: Path):
    """#255: build must store unflipped pixels under the unflipped source key.
    The kit shows the artist the baked-flipped bitmap; the run time looks up
    `source` and mirrors the replacement art itself (ADR-0178)."""
    folder = root / "mirror-h-pixels"
    sheets = folder / "textures" / "sheets"
    sheets.mkdir(parents=True)
    # One 8x8 sprite cell: PNG is flip-baked, sidecar carries source + mirror.
    shape = 3
    pixels = blank(8, 8)
    render_tile_hflipped(shape, pixels, 0, 0, 8, 8)
    write_pair(sheets, "spr000", pixels, 1)
    src = tile_hex(shape)
    baked = flip_hex(src)
    (sheets / "spr000.json").write_text(
        serialize_sheet("sprite", 8, 0, 1, "spr000.png", "spr000.orig.png",
                        [{"index": 0, "x": 0, "y": 0, "count": 1, "context": "sprite",
                          "tiles": [shape]}]),
        encoding="utf-8")
    # serialize_sheet uses EMIT_FLIP_* globals; force the sidecar bytes we need.
    (sheets / "spr000.json").write_text(
        "{\n"
        '  "version": 1,\n'
        '  "kind": "sprite",\n'
        '  "gridUnit": 8,\n'
        '  "gridPhase": { "x": 0, "y": 0 },\n'
        '  "gridConsistency": { "chosen": 0.8300, "alt8x8": 0.4100 },\n'
        '  "cell": { "w": 8, "h": 8 },\n'
        '  "gutter": 0,\n'
        '  "columns": 1,\n'
        '  "sheet": "spr000.png",\n'
        '  "reference": "spr000.orig.png",\n'
        '  "cells": [\n'
        f'    {{ "index": 0, "x": 0, "y": 0, "count": 1, "context": "sprite", "label": "", '
        f'"tiles": [{{ "tile": "{baked}", "palette": "{PAL_HEX}", '
        f'"source": "{src}", "mirror": "H" }}] }}\n'
        "  ]\n"
        "}\n",
        encoding="utf-8")
    (folder / "textures" / "hires.txt").write_text(
        "\n".join(["<ver>107", "<scale>1", "<system>nes",
                   "<supportedRom>2A4E126D0286BEA0BF503C80A12352C57539F76B",
                   f"<tile>0,{src},{PAL_HEX},0,0,1,N"]) + "\n",
        encoding="utf-8")
    # Paint so the cell counts as edited (otherwise an untouched crop is fine
    # to leave, but we need the round-trip to rewrite the baked pixels).
    paint(folder, "spr000.png", 0, 0, 8, color=0xFFA020F0)
    # Re-apply the baked flip over the paint so the sheet still looks mirrored
    # the way a real kit cell does — paint() filled a flat colour; rebuild a
    # mirrored pattern the artist would have seen, distinct from the unflipped
    # want.
    px = png_read(sheets / "spr000.png")
    render_tile_hflipped(shape, px, 0, 0, 8, 8)
    # Tint one pixel so edited-probe still sees a difference from orig.
    px[0][0] = 0xFFA020F0
    (sheets / "spr000.png").write_bytes(png_rgba(px))

    out = run("build", str(folder))
    if out is None:
        return
    imgs, tiles = parse_hires(folder / "textures" / "hires.txt")
    if (src, PAL_HEX) not in tiles:
        fail(f"#255: source key missing from rebuilt hires.txt: {sorted(tiles)[:3]}")
        return
    if (baked, PAL_HEX) in tiles:
        fail("#255: baked flip key was still emitted; source should have replaced it")
        return
    img_i, x, y, _f = tiles[(src, PAL_HEX)]
    got = crop(png_read(sheets / Path(imgs[img_i]).name), x, y, 8)
    want = blank(8, 8)
    render_tile(shape, want, 0, 0, 8, 8)
    want[0][7] = 0xFFA020F0  # the tint, un-baked from (0,0) by the H flip
    if got == want:
        ok("#255: mirror-H cell stores unflipped pixels under the source key")
    else:
        fail(f"#255: crop under source key is not the unflipped art "
             f"(first pixel got={got[0][0]:08X} want={want[0][0]:08X})")
        return

    # A second build must not flip again: un-baking mutates the PNG in place,
    # so the sidecar has to drop source/mirror or the next pass restores the
    # baked bitmap under the source key.
    out2 = run("build", str(folder))
    if out2 is None:
        return
    imgs2, tiles2 = parse_hires(folder / "textures" / "hires.txt")
    if (src, PAL_HEX) not in tiles2:
        fail("#255: source key missing after the second build")
        return
    img_i2, x2, y2, _f2 = tiles2[(src, PAL_HEX)]
    got2 = crop(png_read(sheets / Path(imgs2[img_i2]).name), x2, y2, 8)
    if got2 == want:
        ok("#255: a second build leaves the unflipped pixels unflipped (idempotent)")
    else:
        fail(f"#255: second build re-flipped the crop under the source key "
             f"(first pixel got={got2[0][0]:08X} want={want[0][0]:08X})")
    side = json_loads((sheets / "spr000.json").read_text(encoding="utf-8"))
    entry = side["cells"][0]["tiles"][0]
    if entry.get("tile") == src and "source" not in entry and "mirror" not in entry:
        ok("#255: after un-bake the sidecar is a plain unflipped tile entry")
    else:
        fail(f"#255: sidecar still carries mirror fields after un-bake: {entry}")


def condition_fallback_twin_tests(root: Path):
    """#256: each [condition] rule keeps its unconditional fallback twin."""
    folder, _v, cells = make_sheet_folder(root, "cond-fallback", sprite_sheet=True)
    # Key source: one tile under a spriteNearby condition, with its bare twin
    # (recorder order). A second tile under a condition only — build must
    # synthesise the missing twin.
    key0, key1 = tile_hex(0), tile_hex(1)
    # HdPackLoader: <condition>name,spriteNearby,dx,dy,tileData,palette
    lines = ["<ver>107", "<scale>2", "<system>nes",
             "<supportedRom>2A4E126D0286BEA0BF503C80A12352C57539F76B",
             f"<condition>spr000_n0,spriteNearby,0,0,{key0},{PAL_HEX}",
             f"[spr000_n0]<tile>0,{key0},{PAL_HEX},0,0,1,N",
             f"<tile>0,{key0},{PAL_HEX},0,0,1,N",
             f"[spr000_n0]<tile>0,{key1},{PAL_HEX},0,0,1,N"]
    (folder / "textures" / "hires.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
    paint(folder, "spr000.png", cells[0]["x"], cells[0]["y"], 16)
    out = run("build", str(folder))
    if out is None:
        return
    body = (folder / "textures" / "hires.txt").read_text(encoding="utf-8").splitlines()
    tile_lines = [ln for ln in body if "<tile>" in ln and not ln.strip().startswith("#")]
    has_cond = [ln for ln in tile_lines if ln.startswith("[spr000_n0]<tile>") and key0 in ln]
    has_bare = [ln for ln in tile_lines if ln.startswith("<tile>") and key0 in ln]
    has_cond1 = [ln for ln in tile_lines if ln.startswith("[spr000_n0]<tile>") and key1 in ln]
    has_bare1 = [ln for ln in tile_lines if ln.startswith("<tile>") and key1 in ln]
    if has_cond and has_bare:
        ok("#256: conditional rule keeps its recorder-written unconditional twin")
    else:
        fail(f"#256: key0 missing cond/bare twin: cond={has_cond} bare={has_bare}")
    if has_cond1 and has_bare1:
        ok("#256: a conditional-only key source still gets a synthesised bare twin")
    else:
        fail(f"#256: key1 missing synthesised bare twin: cond={has_cond1} bare={has_bare1}")


def authored_condition_round_trip_tests(root: Path):
    """F12.6a / ADR-0197 §1: a condition an artist writes into a sheet reaches
    hires.txt — its definition once, the cell's rule under it, and the bare twin
    behind it — and only the cells that named it are conditioned."""
    folder, _v, cells = make_sheet_folder(root, "authored-cond", sprite_sheet=True)
    sheets = folder / "textures" / "sheets"
    doc_path = sheets / "spr000.json"
    doc = json_loads(doc_path.read_text(encoding="utf-8"))
    key0 = tile_hex(0)
    line = f"<condition>onBridge,tileAtPosition,120,80,{key0},{PAL_HEX}"
    doc["conditions"] = [{"name": "onBridge", "authored": True, "line": line}]
    doc["cells"][0]["condition"] = "onBridge"
    doc_path.write_text(json_dumps(doc), encoding="utf-8")
    # Two cells painted: only the first names the condition, so the second is
    # the control that proves the condition did not spread across the sheet.
    paint(folder, "spr000.png", cells[0]["x"], cells[0]["y"], 16)
    paint(folder, "spr000.png", cells[1]["x"], cells[1]["y"], 16, color=0xFF20A0F0)
    out = run("build", str(folder))
    if out is None:
        return
    body = (folder / "textures" / "hires.txt").read_text(encoding="utf-8").splitlines()
    defs = [ln for ln in body if ln.startswith("<condition>onBridge,")]
    if len(defs) == 1 and defs[0] == line:
        ok("F12.6a: the authored definition reaches hires.txt once, verbatim")
    else:
        fail(f"F12.6a: authored definition not emitted once: {defs}")
    tile_lines = [ln for ln in body if "<tile>" in ln and not ln.strip().startswith("#")]
    cond = [ln for ln in tile_lines if ln.startswith("[onBridge]<tile>") and key0 in ln]
    bare = [ln for ln in tile_lines if ln.startswith("<tile>") and key0 in ln]
    if cond and bare:
        ok("F12.6a: the conditioned cell emits its rule and the bare twin (#256)")
    else:
        fail(f"F12.6a: conditioned cell missing a rule: cond={cond} bare={bare}")
    key1 = tile_hex(cells[1]["tiles"][0])
    # Assert the control cell is really there first: "no conditional rule
    # mentions key1" is also true of a key the build never emitted at all.
    emitted1 = [ln for ln in tile_lines if key1 in ln]
    if key1 == key0 or not emitted1:
        fail(f"F12.6a: the control cell was not emitted, so the leak check is vacuous ({key1})")
    elif not [ln for ln in emitted1 if ln.startswith("[")]:
        ok("F12.6a: a cell that named no condition stays unconditional")
    else:
        fail(f"F12.6a: the condition leaked onto a cell that did not name it ({key1})")
    # The name must exist before it is used: HdPackLoader reads the file top
    # down and drops a rule whose condition it has not seen.
    first_use = next((i for i, ln in enumerate(body) if "[onBridge]" in ln), -1)
    if defs and body.index(defs[0]) < first_use:
        ok("F12.6a: the definition precedes its first use, as HdPackLoader needs")
    else:
        fail("F12.6a: the definition is emitted after the rule that uses it")


def mirror_unbake_idempotency_tests(root: Path):
    """#329: `build` has to be safe to re-run over its own output.

    A pack recorded before ADR-0178 carries sprite cells whose sidecar names
    the unflipped `source` beside `mirror`, with the OAM flip baked into the
    pixels. Build un-bakes those cells in place — and the `*.orig.png` twin is
    the baseline the "was this cell painted?" probe diffs the sheet against,
    so the twin has to be un-baked with it. Left baked, the twin made every
    corrected cell read as painted on the next run, and each one that lost its
    key to a higher-ranked sheet tripped #253: the second build refused,
    naming the tool's own corrections as if they were the artist's paint."""
    global EMIT_FLIP_SOURCE
    folder, _v, _c = make_sheet_folder(root, "unbake-idempotent")
    sheets = folder / "textures" / "sheets"
    vocab = [
        {"count": 431, "context": "scene", "metatile": 0, "tiles": [0, 1, 2, 3]},
        {"count": 300, "context": "scene", "metatile": 1, "tiles": [4, 5, 6, 7]},
    ]
    # Two sprite sheets over the same keys, the way a recorder emits the OAM
    # vocabulary more than once. `sprites` ranks below `spr000` (`_SHEET_RANK`
    # 1 vs 4), so it owns none of the keys whenever both were painted — which
    # is what #253 exists to report, and what the un-bake must not fake.
    for stem, kind in (("sprites", "sprites"), ("spr000", "sprite")):
        cells, pixels = hflipped_contact_sheet(16, 1, 2, vocab)
        write_pair(sheets, stem, pixels, 1)  # sheet and twin both baked
        EMIT_FLIP_SOURCE = True
        (sheets / f"{stem}.json").write_text(
            serialize_sheet(kind, 16, 1, 2, f"{stem}.png", f"{stem}.orig.png", cells),
            encoding="utf-8")
        EMIT_FLIP_SOURCE = False

    def digest():
        return {p.relative_to(folder).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
                for p in sorted(folder.rglob("*")) if p.is_file()}

    if run("build", str(folder)) is None:
        return
    before = digest()
    out2 = run("build", str(folder))
    if out2 is None:
        fail("#329: a second build over its own output must exit 0")
        return
    after = digest()
    touched = sorted(set(before) ^ set(after)) + sorted(k for k in before if before[k] != after.get(k))
    if touched:
        fail(f"#329: the second build rewrote the pack: {touched}")
    else:
        ok("#329: a second build over an un-baked pack exits 0 and changes nothing")

    # Negative: idempotency must not have been bought by weakening #253. Paint
    # both sheets' first cell — the artist's own paint — and `sprites`, whose
    # rank loses, still has to fail the build.
    paint(folder, "sprites.png", 1, 1, 16)
    paint(folder, "spr000.png", 1, 1, 16, color=0xFF20A0F0)
    out3 = run("build", str(folder), expect=1)
    if out3 is not None and "#253" in out3 and "sprites.png" in out3:
        ok("#329: a genuinely painted sprite cell that loses its key still fails #253")
    else:
        fail(f"#329: #253 no longer fires for a real paint conflict: {out3}")


def _one_cell_sidecar(kind: str, stem: str, entries) -> str:
    """A unit-8 sidecar with one cell per `entries` item, laid out in a row at
    x = 8 * i, gutter 0. Each item is the raw JSON of that cell's tile entry."""
    cells = ",\n".join(
        f'    {{ "index": {i}, "x": {8 * i}, "y": 0, "count": 1, "context": "scene", '
        f'"label": "", "tiles": [{e}] }}' for i, e in enumerate(entries))
    return ("{\n" '  "version": 1,\n' f'  "kind": "{kind}",\n' '  "gridUnit": 8,\n'
            '  "gridPhase": { "x": 0, "y": 0 },\n'
            '  "gridConsistency": { "chosen": 0.8300, "alt8x8": 0.4100 },\n'
            '  "cell": { "w": 8, "h": 8 },\n' '  "gutter": 0,\n'
            f'  "columns": {len(entries)},\n' f'  "sheet": "{stem}.png",\n'
            f'  "reference": "{stem}.orig.png",\n' f'  "cells": [\n{cells}\n  ]\n' "}\n")


def index_keyed_unflip_tests(root: Path):
    """#457: on a CHR ROM game (index keys, ADR-0172) a mirrored sprite cell
    must be un-baked exactly like on a CHR RAM game (ADR-0178, #255). The key
    is the index either way, but the run time still mirrors the replacement
    art itself, so a crop left baked is drawn flipped twice: Glass Joe torn."""
    folder = root / "index-keyed-unflip"
    sheets = folder / "textures" / "sheets"
    sheets.mkdir(parents=True)
    shape = 3
    index = CHR_INDEX_BASE + shape
    token = f"{index:02X}"
    pixels = blank(8, 8)
    render_tile_hflipped(shape, pixels, 0, 0, 8, 8)
    write_pair(sheets, "spr000", pixels, 1)  # sheet and twin both baked, as recorded
    src = tile_hex(shape)
    (sheets / "spr000.json").write_text(_one_cell_sidecar("sprite", "spr000", [
        f'{{ "tile": "{flip_hex(src)}", "palette": "{PAL_HEX}", "index": {index}, '
        f'"source": "{src}", "mirror": "H" }}']), encoding="utf-8")
    (folder / "textures" / "hires.txt").write_text(
        "\n".join(["<ver>107", "<scale>1", "<system>nes",
                   "<supportedRom>2A4E126D0286BEA0BF503C80A12352C57539F76B",
                   f"<tile>0,{token},{PAL_HEX},0,0,1,N"]) + "\n", encoding="utf-8")
    want = blank(8, 8)
    render_tile(shape, want, 0, 0, 8, 8)

    def crop_of_key():
        imgs, tiles = parse_hires(folder / "textures" / "hires.txt")
        entry = tiles.get((token, PAL_HEX))
        if entry is None:
            return None
        return crop(png_read(sheets / Path(imgs[entry[0]]).name), entry[1], entry[2], 8)

    if run("build", str(folder)) is None:
        return
    got = crop_of_key()
    if got == want:
        ok("#457: an index-keyed mirror-H sprite cell stores the unflipped art under its index key")
    else:
        mirrored = got == pixels
        fail(f"#457: the crop under index key {token} is not the unflipped art"
             f"{' (it is still the baked mirror)' if mirrored else ''}")
        return
    twin = png_read(sheets / "spr000.orig.png")
    if twin == want:
        ok("#457: the *.orig.png twin is un-baked in lockstep on an index-keyed pack (#329)")
    else:
        fail("#457: the twin still holds the baked mirror, so the next build reads the cell as painted")
    if run("build", str(folder)) is None:
        return
    entry = json_loads((sheets / "spr000.json").read_text(encoding="utf-8"))["cells"][0]["tiles"][0]
    if crop_of_key() == want and "mirror" not in entry and entry.get("index") == index:
        ok("#457: a second build leaves the index-keyed crop unflipped and the sidecar plain (idempotent)")
    else:
        fail(f"#457: the second build re-flipped the crop or kept the mirror field: {entry}")


def _render_bg(shape: int, transparent0: bool):
    """RenderTile at 1x; `transparent0` is what HdPackBuilder::GenerateHdTile
    writes for a background key drawn over a behind-background sprite
    (`TransparencyRequired`): colour index 0 left at alpha 0."""
    out = blank(8, 8)
    render_tile(shape, out, 0, 0, 8, 8)
    if transparent0:
        data = tile_bytes(shape)
        for r in range(8):
            for c in range(8):
                if not ((data[r] >> (7 - c)) & 1 or (data[r + 8] >> (7 - c)) & 1):
                    out[r][c] = 0
    return out


def backdrop_transparency_tests(root: Path):
    """#456: a background key the recording keeps transparent at colour 0 (a
    behind-background sprite was drawn over it, `TransparencyRequired`) has to
    stay transparent in the rebuilt pack. The sheet shows colour 0 as the
    opaque backdrop, so a rebuilt `textures/` layer that copied it painted the
    floor over Glass Joe. Keys the recording draws opaque stay opaque, and a
    colour-0 pixel the artist repainted is paint, not backdrop."""
    folder = root / "backdrop-transparency"
    tex = folder / "textures"
    sheets = tex / "sheets"
    sheets.mkdir(parents=True)
    shapes = [21, 22, 23]  # A: transparent in the recording, B: opaque, C: transparent and painted
    need = [True, False, True]
    # The key source is the recording: its own crops, at its own <scale>.
    source = blank(24, 8)
    for i, s in enumerate(shapes):
        for r, row in enumerate(_render_bg(s, need[i])):
            source[r][8 * i:8 * i + 8] = row
    (tex / "old.png").write_bytes(png_rgba(source))
    (tex / "hires.txt").write_text("\n".join(
        ["<ver>107", "<scale>1", "<system>nes", "<supportedRom>2A4E126D0286BEA0BF503C80A12352C57539F76B",
         "<img>old.png"] + [f"<tile>0,{tile_hex(s)},{PAL_HEX},{8 * i},0,1,N" for i, s in enumerate(shapes)])
        + "\n", encoding="utf-8")
    # The sheet: every colour index opaque, as SheetRender draws a background cell.
    sheet = blank(24, 8)
    for i, s in enumerate(shapes):
        for r, row in enumerate(_render_bg(s, False)):
            sheet[r][8 * i:8 * i + 8] = row
    write_pair(sheets, "metatiles", sheet, 1)
    (sheets / "metatiles.json").write_text(_one_cell_sidecar("metatiles", "metatiles", [
        f'{{ "tile": "{tile_hex(s)}", "palette": "{PAL_HEX}" }}' for s in shapes]), encoding="utf-8")
    # The artist repaints one colour-0 pixel of C (and one ink pixel).
    zero_c = next((r, c) for r in range(8) for c in range(8) if _render_bg(23, True)[r][c] == 0)
    ink_c = next((r, c) for r in range(8) for c in range(8) if _render_bg(23, True)[r][c] != 0)
    painted = png_read(sheets / "metatiles.png")
    for r, c in (zero_c, ink_c):
        painted[r][16 + c] = 0xFFA020F0
    (sheets / "metatiles.png").write_bytes(png_rgba(painted))

    if run("build", str(folder)) is None:
        return
    imgs, tiles = parse_hires(tex / "hires.txt")

    def crop_of(shape):
        e = tiles.get((tile_hex(shape), PAL_HEX))
        return None if e is None else crop(png_read(sheets / Path(imgs[e[0]]).name), e[1], e[2], 8)

    def opaque(px):
        return sum(1 for row in (px or []) for p in row if p >> 24 == 0xFF)

    want_c = _render_bg(23, True)
    for r, c in (zero_c, ink_c):
        want_c[r][c] = 0xFFA020F0
    got_a, got_b, got_c = crop_of(21), crop_of(22), crop_of(23)
    if got_a == _render_bg(21, True):
        ok("#456: a key the recording draws transparent at colour 0 is rebuilt transparent there")
    else:
        fail(f"#456: key A's rebuilt crop is not the recording's transparency "
             f"({opaque(got_a)} of 64 px opaque, want {opaque(_render_bg(21, True))})")
    if got_b == _render_bg(22, False):
        ok("#456: a key the recording draws opaque keeps its opaque backdrop")
    else:
        fail(f"#456: key B (opaque in the recording) changed in the rebuild ({opaque(got_b)} of 64 px opaque)")
    if got_c == want_c:
        ok("#456: a repainted colour-0 pixel stays paint; the untouched backdrop of that key goes transparent")
    else:
        fail(f"#456: key C mixed up the artist's paint and the backdrop "
             f"({opaque(got_c)} of 64 px opaque, want {opaque(want_c)})")
    twin = png_read(sheets / "metatiles.orig.png")
    if crop(twin, 0, 0, 8) == _render_bg(21, True) and crop(twin, 8, 0, 8) == _render_bg(22, False):
        ok("#456: the *.orig.png twin gets the same transparency, so the cell still reads as unpainted")
    else:
        fail("#456: the twin was not updated in lockstep with the sheet")

    def digest():
        return {p.relative_to(folder).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
                for p in sorted(folder.rglob("*")) if p.is_file()}

    before = digest()
    if run("build", str(folder)) is None:
        return
    after = digest()
    changed = sorted(k for k in set(before) | set(after) if before.get(k) != after.get(k))
    if changed:
        fail(f"#456: a second build over its own output rewrote {changed}")
    else:
        ok("#456: a second build over its own output is byte-identical")


def backdrop_shared_crop_tests(root: Path):
    """#456 idempotency on a bank-swapping game: one drawing under two CHR
    indexes is one cell with an alias (F9.7), so the two keys share a crop.
    Only the first key is see-through in the recording; clearing the shared
    crop makes the second see-through in the rebuilt manifest, which the next
    build reads as its key source. So the first build has to clear the second
    key's other crops as well, or the second build rewrites the pack
    (Punch-Out!!: EA and 96 under 1130260F)."""
    folder = root / "backdrop-shared-crop"
    tex = folder / "textures"
    sheets = tex / "sheets"
    sheets.mkdir(parents=True)
    shape, k1, k2 = 21, 0x50, 0x51
    source = blank(16, 8)
    for i, t in enumerate((True, False)):
        for r, row in enumerate(_render_bg(shape, t)):
            source[r][8 * i:8 * i + 8] = row
    (tex / "old.png").write_bytes(png_rgba(source))
    (tex / "hires.txt").write_text("\n".join(
        ["<ver>109", "<scale>1", "<system>nes", "<supportedRom>2A4E126D0286BEA0BF503C80A12352C57539F76B",
         "<img>old.png", f"<tile>0,{k1:02X},{PAL_HEX},0,0,1,N", f"<tile>0,{k2:02X},{PAL_HEX},8,0,1,N"])
        + "\n", encoding="utf-8")
    sheet = blank(16, 8)
    for i in range(2):
        for r, row in enumerate(_render_bg(shape, False)):
            sheet[r][8 * i:8 * i + 8] = row
    write_pair(sheets, "metatiles", sheet, 1)
    entry = '{{ "tile": "{0}", "palette": "{1}", "index": {2} }}'
    side = _one_cell_sidecar("metatiles", "metatiles", [
        entry.format(tile_hex(shape), PAL_HEX, k1), entry.format(tile_hex(shape), PAL_HEX, k2)])
    alias = f'"aliases": [{{ "metatile": 9, "tiles": [{entry.format(tile_hex(shape), PAL_HEX, k2)}] }}], '
    side = side.replace('"context": "scene", "label"', f'"context": "scene", {alias}"label"', 1)
    (sheets / "metatiles.json").write_text(side, encoding="utf-8")

    if run("build", str(folder)) is None:
        return
    got = png_read(sheets / "metatiles.png")
    if crop(got, 8, 0, 8) == _render_bg(shape, True):
        ok("#456: a key that shares a cleared crop has its other crops cleared in the same build")
    else:
        fail("#456: the second key's own cell kept its opaque backdrop after the shared crop was cleared")

    def digest():
        return {p.relative_to(folder).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
                for p in sorted(folder.rglob("*")) if p.is_file()}

    before = digest()
    if run("build", str(folder)) is None:
        return
    after = digest()
    changed = sorted(k for k in set(before) | set(after) if before.get(k) != after.get(k))
    if changed:
        fail(f"#456: with a shared crop, a second build over its own output rewrote {changed}")
    else:
        ok("#456: with a shared crop, a second build over its own output is byte-identical")


def backdrop_unbake_and_sprite_tests(root: Path):
    """#456 with #255: a mirrored cell is un-baked before its colour 0 is
    cleared, so the colour-0 positions are the unflipped tile's - the baked
    tile's would clear ink and leave backdrop, and the next build, reading a
    plain sidecar, would clear again. A sprite sheet is not a background crop
    and is never punched (test_mep_figure's #435 recipe caught both)."""
    folder = root / "backdrop-unbake-sprite"
    tex = folder / "textures"
    sheets = tex / "sheets"
    sheets.mkdir(parents=True)
    bg, spr = 3, 5  # bg is asymmetric: #457 proves its mirror differs
    source = blank(16, 8)
    for i, s in enumerate((bg, spr)):
        for r, row in enumerate(_render_bg(s, True)):
            source[r][8 * i:8 * i + 8] = row
    (tex / "old.png").write_bytes(png_rgba(source))
    (tex / "hires.txt").write_text("\n".join(
        ["<ver>107", "<scale>1", "<system>nes", "<supportedRom>2A4E126D0286BEA0BF503C80A12352C57539F76B",
         "<img>old.png", f"<tile>0,{tile_hex(bg)},{PAL_HEX},0,0,1,N", f"<tile>0,{tile_hex(spr)},{PAL_HEX},8,0,1,N"])
        + "\n", encoding="utf-8")
    baked = blank(8, 8)
    render_tile_hflipped(bg, baked, 0, 0, 8, 8)
    write_pair(sheets, "metatiles", baked, 1)
    (sheets / "metatiles.json").write_text(_one_cell_sidecar("metatiles", "metatiles", [
        f'{{ "tile": "{flip_hex(tile_hex(bg))}", "palette": "{PAL_HEX}", '
        f'"source": "{tile_hex(bg)}", "mirror": "H" }}']), encoding="utf-8")
    sprite = _render_bg(spr, False)
    write_pair(sheets, "spr000", sprite, 1)
    (sheets / "spr000.json").write_text(_one_cell_sidecar("sprite", "spr000", [
        f'{{ "tile": "{tile_hex(spr)}", "palette": "{PAL_HEX}" }}']), encoding="utf-8")

    if run("build", str(folder)) is None:
        return
    if crop(png_read(sheets / "metatiles.png"), 0, 0, 8) == _render_bg(bg, True):
        ok("#456: an un-baked mirror cell clears colour 0 at the unflipped tile's positions")
    else:
        fail("#456: the un-baked mirror cell was punched at the baked tile's colour-0 positions")
    if png_read(sheets / "spr000.png") == sprite:
        ok("#456: a sprite sheet is not a background crop and keeps its pixels")
    else:
        fail("#456: a sprite sheet crop was punched as if it were a background crop")

    def digest():
        return {p.relative_to(folder).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
                for p in sorted(folder.rglob("*")) if p.is_file()}

    before = digest()
    if run("build", str(folder)) is None:
        return
    after = digest()
    changed = sorted(k for k in set(before) | set(after) if before.get(k) != after.get(k))
    if changed:
        fail(f"#456: after an un-bake, a second build over its own output rewrote {changed}")
    else:
        ok("#456: after an un-bake, a second build over its own output is byte-identical")


def index_keyed_alias_unflip_tests(root: Path):
    """#457 (PR #475 review): an index-keyed sprite cell whose alias carries
    the same `source` + `mirror` shares the cell's crop. The crop has to be
    un-baked once: queued twice it was mirrored back to the baked pixels,
    while the sidecar lost both mirror fields, so no later build could fix it."""
    folder = root / "index-keyed-alias-unflip"
    sheets = folder / "textures" / "sheets"
    sheets.mkdir(parents=True)
    shape = 3
    i1, i2 = CHR_INDEX_BASE + shape, CHR_INDEX_BASE + shape + 0x20
    pixels = blank(8, 8)
    render_tile_hflipped(shape, pixels, 0, 0, 8, 8)
    write_pair(sheets, "spr000", pixels, 1)
    src = tile_hex(shape)
    entry = '{{ "tile": "{0}", "palette": "{1}", "index": {2}, "source": "{3}", "mirror": "H" }}'
    side = _one_cell_sidecar("sprite", "spr000", [entry.format(flip_hex(src), PAL_HEX, i1, src)])
    alias = f'"aliases": [{{ "metatile": 9, "tiles": [{entry.format(flip_hex(src), PAL_HEX, i2, src)}] }}], '
    (sheets / "spr000.json").write_text(
        side.replace('"context": "scene", "label"', f'"context": "scene", {alias}"label"', 1), encoding="utf-8")
    (folder / "textures" / "hires.txt").write_text("\n".join(
        ["<ver>107", "<scale>1", "<system>nes", "<supportedRom>2A4E126D0286BEA0BF503C80A12352C57539F76B"]
        + [f"<tile>0,{i:02X},{PAL_HEX},0,0,1,N" for i in (i1, i2)]) + "\n", encoding="utf-8")
    want = blank(8, 8)
    render_tile(shape, want, 0, 0, 8, 8)
    if run("build", str(folder)) is None:
        return
    if png_read(sheets / "spr000.png") == want and png_read(sheets / "spr000.orig.png") == want:
        ok("#457: a crop a cell and its alias share is un-baked once, sheet and twin")
    else:
        baked = png_read(sheets / "spr000.png") == pixels
        fail(f"#457: the shared crop is not the unflipped art{' (still the baked mirror)' if baked else ''}")


def backdrop_translucent_paint_tests(root: Path):
    """#456 (PR #475 review): on a rebuild the key source's `<img>` is the
    artist's own sheet, so a translucent brush pixel is not the recorder's
    `TransparencyRequired` signature (alpha 0 at a colour-0 position). Such a
    key keeps its opaque backdrop."""
    folder = root / "backdrop-translucent-paint"
    tex = folder / "textures"
    sheets = tex / "sheets"
    sheets.mkdir(parents=True)
    shape = 22
    base = _render_bg(shape, False)
    write_pair(sheets, "metatiles", base, 1)
    (sheets / "metatiles.json").write_text(_one_cell_sidecar("metatiles", "metatiles", [
        f'{{ "tile": "{tile_hex(shape)}", "palette": "{PAL_HEX}" }}']), encoding="utf-8")
    zero = _render_bg(shape, True)
    painted = [row[:] for row in base]
    ink = next((r, c) for r in range(8) for c in range(8) if zero[r][c] != 0)
    back = next((r, c) for r in range(8) for c in range(8) if zero[r][c] == 0)
    for r, c in (ink, back):
        painted[r][c] = 0x80A020F0  # a soft brush: half alpha, not the recorder's alpha 0
    (sheets / "metatiles.png").write_bytes(png_rgba(painted))
    (tex / "hires.txt").write_text("\n".join(
        ["<ver>107", "<scale>1", "<system>nes", "<supportedRom>2A4E126D0286BEA0BF503C80A12352C57539F76B",
         "<img>sheets/metatiles.png", f"<tile>0,{tile_hex(shape)},{PAL_HEX},0,0,1,N"]) + "\n", encoding="utf-8")
    if run("build", str(folder)) is None:
        return
    got = png_read(sheets / "metatiles.png")
    if got == painted and png_read(sheets / "metatiles.orig.png") == base:
        ok("#456: a translucent brush pixel in the key source does not make its key see-through")
    else:
        cleared = sum(1 for row in got for p in row if p >> 24 == 0)
        fail(f"#456: a translucent brush pixel was read as the recorder's transparency ({cleared} px cleared)")


def png_rgb(pixels) -> bytes:
    """`png_rgba` without the alpha channel (colour type 2), as an editor saves an opaque sheet."""
    rgba = png_rgba(pixels)
    height, width = len(pixels), len(pixels[0])
    raw = bytearray()
    for row in pixels:
        raw.append(0)
        for p in row:
            raw += bytes(((p >> 16) & 0xFF, (p >> 8) & 0xFF, p & 0xFF))

    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    return (rgba[:8] + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(bytes(raw), 9)) + chunk(b"IEND", b""))


def backdrop_rgb_sheet_tests(root: Path):
    """#456 (PR #475 review): an opaque working sheet saved as 8-bit RGB (no
    alpha channel), with its twin, still gets colour 0 cleared on a
    see-through key - the sheet gains the alpha channel it needs."""
    folder = root / "backdrop-rgb-sheet"
    tex = folder / "textures"
    sheets = tex / "sheets"
    sheets.mkdir(parents=True)
    shape = 21
    (tex / "old.png").write_bytes(png_rgba(_render_bg(shape, True)))
    (tex / "hires.txt").write_text("\n".join(
        ["<ver>107", "<scale>1", "<system>nes", "<supportedRom>2A4E126D0286BEA0BF503C80A12352C57539F76B",
         "<img>old.png", f"<tile>0,{tile_hex(shape)},{PAL_HEX},0,0,1,N"]) + "\n", encoding="utf-8")
    for name in ("metatiles.png", "metatiles.orig.png"):
        (sheets / name).write_bytes(png_rgb(_render_bg(shape, False)))
    (sheets / "metatiles.json").write_text(_one_cell_sidecar("metatiles", "metatiles", [
        f'{{ "tile": "{tile_hex(shape)}", "palette": "{PAL_HEX}" }}']), encoding="utf-8")
    if run("build", str(folder)) is None:
        return
    kinds = [(sheets / n).read_bytes()[25] for n in ("metatiles.png", "metatiles.orig.png")]
    if kinds != [6, 6]:
        fail(f"#456: an RGB sheet kept its opaque colour 0 (PNG colour types {kinds}, want RGBA)")
    elif png_read(sheets / "metatiles.png") == _render_bg(shape, True) == png_read(sheets / "metatiles.orig.png"):
        ok("#456: an RGB sheet and its twin gain alpha and keep colour 0 transparent")
    else:
        fail("#456: an RGB sheet was converted but colour 0 was not cleared")


def painted_sprite_ownership_tests(root: Path):
    """#253: a painted sprite sheet whose cells lose to another sheet fails."""
    folder, _v, _c = make_sheet_folder(root, "painted-owned-elsewhere",
                                       sprite_sheet=True)
    sheets = folder / "textures" / "sheets"
    # Second sprite sheet claiming the same keys as spr000, later name so it
    # wins the same-rank tie when both are painted.
    spr_cells, spr_pixels = contact_sheet(16, 1, 2, [
        {"count": 431, "context": "scene", "metatile": 0, "tiles": [0, 1, 2, 3]},
        {"count": 300, "context": "scene", "metatile": 1, "tiles": [4, 5, 6, 7]},
    ])
    write_pair(sheets, "spr001", spr_pixels, 1)
    (sheets / "spr001.json").write_text(
        serialize_sheet("sprite", 16, 1, 2, "spr001.png", "spr001.orig.png", spr_cells),
        encoding="utf-8")
    paint(folder, "spr000.png", spr_cells[0]["x"], spr_cells[0]["y"], 16)
    paint(folder, "spr001.png", spr_cells[0]["x"], spr_cells[0]["y"], 16, color=0xFF20A0F0)
    out = run("build", str(folder), expect=1)
    if out is not None and "painted tile" in out and "#253" in out and "spr000.png" in out:
        ok("#253: a painted sprite sheet that loses its cells to another sheet fails the build")
    else:
        fail(f"#253: overlapping painted sprite sheets did not fail with ownership error: {out}")


def chr_rom_key_tests(root: Path):
    """ADR-0172: on a CHR ROM game hires.txt keys every tile by its CHR index,
    so the rebuild has to emit the index the sidecar records — emitting the
    32-hex data form instead produces a pack that loads, draws its captures and
    matches no tile at all (issue #170)."""
    folder, _v, _c = make_sheet_folder(root, "chr-rom", chr_rom=True)
    out = run("build", str(folder))
    if out is None:
        return
    _imgs, tiles = parse_hires(folder / "textures" / "hires.txt")
    want = {f"{CHR_INDEX_BASE + s:02X}" for s in range(20)}
    got = {k for k, _p in tiles}
    if got == want:
        ok("ADR-0172: a CHR ROM pack rebuilds with index keys, one per resolved crop")
    else:
        fail(f"CHR ROM rebuild keys: missing {sorted(want - got)}, unexpected {sorted(got - want)}")
    # The key source's own attributes must still reach the tile they belong to,
    # which only works when the carry-over lookup uses the index form too.
    entry = tiles.get((f"{CHR_INDEX_BASE:02X}", PAL_HEX))
    if entry and entry[3][5:] == ["0.5", "Y"]:
        ok("ADR-0172: brightness/defaultTile are carried over across the index form")
    else:
        fail(f"CHR ROM rebuild lost the key source's attributes: {entry}")

    # A pack recorded before the ADR has no index to emit. Failing loudly is
    # the decision: the alternative is a pack that silently renders nothing.
    legacy, _v, _c = make_sheet_folder(root, "chr-rom-legacy", chr_rom=True, sidecar_index=False)
    out = run("build", str(legacy), expect=2)
    if out is not None and "carry no tile index" in out and "EMPTY folder" in out:
        ok("ADR-0172: a CHR ROM pack whose sidecars predate the ADR fails the build, naming the fix")
    else:
        fail(f"pre-ADR-0172 CHR ROM pack did not fail with the empty-folder message: {out}")


def author_overflow(folder: Path, target, anchor, cell: int = 1, offset=(16, -24),
                    synthetic: bool = True, sheet: str = "obj000"):
    """Author an ADR-0196 overflow layer on `sheet` by hand: cell `cell` is
    re-keyed to the synthetic `target` and marked synthetic, and one
    `additions[]` record anchors on `anchor` and points at that cell. This is
    what `compose_engine.export(overflow=...)` writes; doing it by hand here
    keeps the build test independent of the editor."""
    path = folder / "textures" / "sheets" / f"{sheet}.json"
    doc = json_loads(path.read_text(encoding="utf-8"))
    entry = {"tile": target[0], "palette": target[1]}
    if len(target) > 2 and target[2] is not None:
        entry["index"] = target[2]
    for c in doc["cells"]:
        if c["index"] == cell:
            c["tiles"] = [entry]
            if synthetic:
                c["synthetic"] = True
                c["ordinal"] = 1
                c["pose"] = "pose000"
    rec = {"pose": "pose000", "offsetX": offset[0], "offsetY": offset[1], "cell": cell,
           "anchor": {"node": 0, "tile": anchor[0], "palette": anchor[1]}}
    if len(anchor) > 2 and anchor[2] is not None:
        rec["anchor"]["index"] = anchor[2]
    doc["additions"] = [rec]
    path.write_text(json_dumps(doc), encoding="utf-8")


def ines_header(path: Path, chr_units: int):
    """A bare iNES header — all `mep_addition.chr_tile_count` reads."""
    data = bytearray(16)
    data[0:4] = b"NES\x1a"
    data[4] = 2
    data[5] = chr_units
    path.write_bytes(bytes(data))
    return path


def addition_chr_ram_tests(root: Path):
    """ADR-0196 (F12.5): a CHR RAM pack's overflow layer round-trips — the
    sheet cell carries the reserved key, the build emits both the `<tile>` rule
    for it and the `<addition>` that points at it, and the two cannot drift
    because the cell is the only source of the target key."""
    import mep_addition
    target = (mep_addition.chr_ram_target(1), mep_addition.RESERVED_PALETTE)
    anchor = (tile_hex(0), PAL_HEX)
    folder, _v, _c = make_sheet_folder(root, "addition-ram")
    author_overflow(folder, target, anchor)
    out = run("build", str(folder))
    if out is None:
        return
    text = (folder / "textures" / "hires.txt").read_text(encoding="utf-8")
    want = f"<addition>{anchor[0]},{anchor[1]},16,-24,{target[0]},{target[1]}"
    if want in text:
        ok("ADR-0196: the overflow layer emits its <addition> line")
    else:
        fail(f"no <addition> line for the authored overflow:\n{want}")
    _imgs, tiles = parse_hires(folder / "textures" / "hires.txt")
    if target in tiles:
        ok("ADR-0196: the synthetic target is keyed by a <tile> rule of its own sheet")
    else:
        fail(f"the synthetic target {target} got no <tile> rule: {sorted(tiles)[:4]}")
    if "synthetic target key(s)" in out:
        ok("ADR-0196: the build says how many keys no recording observed")
    else:
        fail(f"build summary does not report the synthetic keys: {out}")

    # A rebuild must not stack a second copy of the tag on the carried manifest.
    if run("build", str(folder)) is None:
        return
    again = (folder / "textures" / "hires.txt").read_text(encoding="utf-8")
    if again.count("<addition>") == 1:
        ok("ADR-0196: a rebuild re-emits the tag from the sheets, it does not stack")
    else:
        fail(f"rebuild left {again.count('<addition>')} <addition> lines")


def addition_refusal_tests(root: Path):
    """The two refusals the build owns: a target the sidecar does not mark
    synthetic, and a CHR RAM target that is not ADR-0196 §3's reserved key."""
    import mep_addition
    anchor = (tile_hex(0), PAL_HEX)
    folder, _v, _c = make_sheet_folder(root, "addition-unmarked")
    author_overflow(folder, (mep_addition.chr_ram_target(1), mep_addition.RESERVED_PALETTE),
                    anchor, synthetic=False)
    out = run("build", str(folder), expect=2)
    if out is not None and "not marked synthetic" in out:
        ok("ADR-0196 §4: a target the sidecar does not mark synthetic fails the build")
    else:
        fail(f"an unmarked synthetic target did not fail the build: {out}")

    folder, _v, _c = make_sheet_folder(root, "addition-unreserved")
    author_overflow(folder, (tile_hex(3), PAL_HEX), anchor)
    out = run("build", str(folder), expect=2)
    if out is not None and "reserved" in out:
        ok("ADR-0196 §3: a target that is not the reserved key fails the build")
    else:
        fail(f"an unreserved CHR RAM target did not fail the build: {out}")


def addition_chr_rom_tests(root: Path):
    """On a CHR ROM pack ADR-0196 §3's proof is an assertion against the ROM's
    own iNES header, so the build refuses to make it without the ROM."""
    import mep_addition
    anchor = (tile_hex(0), PAL_HEX, CHR_INDEX_BASE)
    target = (mep_addition.chr_ram_target(1), mep_addition.RESERVED_PALETTE, 8192)
    folder, _v, _c = make_sheet_folder(root, "addition-rom", chr_rom=True)
    author_overflow(folder, target, anchor)
    out = run("build", str(folder), expect=2)
    if out is not None and "--rom" in out:
        ok("ADR-0196 §3: a CHR ROM pack with no ROM cannot assert, and says so")
    else:
        fail(f"a CHR ROM overflow built with no ROM did not name the fix: {out}")

    rom = ines_header(root / "addition-rom.nes", 16)   # 16 x 8 KB = 8192 tiles
    out = run("build", str(folder), "--rom", str(rom))
    if out is None:
        return
    text = (folder / "textures" / "hires.txt").read_text(encoding="utf-8")
    want = f"<addition>{CHR_INDEX_BASE:02X},{PAL_HEX},16,-24,2000,{mep_addition.RESERVED_PALETTE}"
    if want in text:
        ok("ADR-0196 §3: the CHR ROM target is the first index past the ROM's CHR")
    else:
        fail(f"no index-keyed <addition> line:\n{want}")

    inside = ines_header(root / "addition-rom-big.nes", 64)   # 32768 tiles
    out = run("build", str(folder), "--rom", str(inside), expect=2)
    if out is not None and "inside the ROM's CHR" in out:
        ok("ADR-0196 §3: a target inside CHR is refused against the header, not guessed")
    else:
        fail(f"a target inside CHR was not refused: {out}")


def run(*argv, expect=0, cwd=None):
    p = subprocess.run([PY, str(MEP_BUILD), *argv], capture_output=True, text=True, cwd=cwd)
    out = (p.stdout + p.stderr).strip()
    if p.returncode != expect:
        fail(f"mep_build {' '.join(argv)} -> exit {p.returncode}, expected {expect}: {out}")
        return None
    return out


def chr_relegation_test(root: Path):
    """F9.10 (ADR-0160): the bootstrap writes its CHR-order fragments under
    `textures/chr/` and names them that way in `<img>`. The sheet build drops
    every `<img>` of the key source and re-emits its own, so where the
    fragments live must make no difference at all to the built manifest — and
    the built manifest must keep pointing only at `sheets/`, which is the whole
    point of relegating them."""
    old = make_author_folder(root, name="chr-at-root")
    new = make_author_folder(root, name="chr-in-subfolder")
    for folder, img in ((old, "<img>Chr_00_0.png"), (new, "<img>chr/Chr_00_0.png")):
        hires = folder / "textures" / "hires.txt"
        hires.write_text(hires.read_text(encoding="utf-8").replace("<img>old.png", img),
                         encoding="utf-8")
    if run("build", str(old)) is None or run("build", str(new)) is None:
        return
    old_text = (old / "textures" / "hires.txt").read_text(encoding="utf-8")
    new_text = (new / "textures" / "hires.txt").read_text(encoding="utf-8")
    if old_text != new_text:
        fail("moving the CHR-order fragments into chr/ changed the built hires.txt")
    elif [ln for ln in new_text.splitlines() if ln.startswith("<img>")] != ["<img>sheets/objects.png"]:
        fail(f"the built manifest does not reference sheets/ alone:\n{new_text}")
    else:
        ok("F9.10: a key source naming textures/chr/ builds the same manifest, pointing only at sheets/")


def pack_extra_data_tests(root: Path, rom: Path):
    """PRD Phase 10 S10.c: `pack` must carry the root `generated` object of an
    existing pack.json across the rebuild (MEP-v1 §3.1 v1.6, ADR-0154 §3 — the
    label is the disclosure, and losing it on export un-labels the pack), and
    the zip's membership is documented here as it stands: `folder.rglob("*")`
    ships every file under the folder, including a non-pack subfolder. Whether
    `pack` should exclude anything is a decision (an ADR), not a behaviour this
    test invents; what it pins is that the behaviour cannot change silently."""
    folder = make_author_folder(root, name="studio")
    if run("build", str(folder)) is None:
        return
    # A non-pack subfolder, the shape the skin studio would leave behind: a
    # transcript of the session plus a candidate image that never shipped.
    studio = folder / "studio"
    (studio / "candidates").mkdir(parents=True)
    (studio / "transcript.jsonl").write_text('{"turn": 1, "prompt": "chrome knight"}\n', encoding="utf-8")
    (studio / "candidates" / "skin.png").write_bytes(png(16, 16))
    generated = {"by": "sheet_repaint", "backend": "passthrough", "date": "2026-09-09",
                 "scale": 4, "source": "auto/textures/sheets"}
    (folder / "pack.json").write_text(json_dumps({
        "mep": "1.6.0", "name": "S10.c Test", "version": "0.1.0", "license": "CC0-1.0",
        "id": "probe-pack",
        "generated": generated,
        "targets": [{"system": "nes", "sha1": "2A4E126D0286BEA0BF503C80A12352C57539F76B"}],
        "sections": {"textures": {"path": "textures/"}},
    }), encoding="utf-8")

    z = root / "studio.zip"
    if run("pack", str(folder), "--rom", str(rom), "--out", str(z)) is None:
        return
    pj = json_loads((folder / "pack.json").read_text(encoding="utf-8"))
    with zipfile.ZipFile(z) as zf:
        names = zf.namelist()
        zipped = json_loads(zf.read("pack.json").decode("utf-8"))
    if pj.get("generated") != generated:
        fail(f"pack rewrote pack.json without the root `generated` object: {pj.get('generated')}")
    elif zipped.get("generated") != generated:
        fail(f"the zipped pack.json lost the root `generated` object: {zipped.get('generated')}")
    else:
        ok("S10.c: pack carries the root `generated` object across a rebuild, on disk and in the zip")

    # `id` is the pack's product identity (MEP-v1 §3.1, ADR-0140 source (1)):
    # stable across revisions, and the catalog slot the pack competes for.
    # Dropping it on a re-pack sends the pack down the fallback chain, which
    # can move it to a different slot than its author declared.
    if pj.get("id") != "probe-pack" or zipped.get("id") != "probe-pack":
        fail(f"pack dropped the root `id`: on disk {pj.get('id')!r}, in the zip {zipped.get('id')!r}")
    else:
        ok("pack carries the root `id` across a rebuild, on disk and in the zip (ADR-0140)")

    # The key source is a recording (no page of its own here): the first
    # build keeps it as textures/hires.recorded.txt before overwriting it,
    # usable rules or not (ADR-0231 §2, PR #472 review), and it ships.
    want = ["pack.json", "audio/bgm/01.ogg", "audio/hires.txt", "audio/sfx/03.ogg",
            "studio/candidates/skin.png", "studio/transcript.jsonl",
            "textures/hires.recorded.txt", "textures/hires.txt", "textures/sheets/objects.png"]
    if names != want:
        fail(f"pack zip membership changed:\n  got  {names}\n  want {want}")
    else:
        ok("S10.c: pack zips every file under the folder — a non-pack studio/ subfolder ships with it")


def skin(folder: Path) -> int:
    """Repaint every sheet in place, leaving the `*.orig.png` twins alone: each
    pixel keeps its alpha and rotates its RGB channels, so all the art changes
    and no geometry does. That is what a skin tool returns, and why the
    pixel-exact identity round-trip cannot be its check (PRD Phase 10 S10.d)."""
    painted = 0
    for p in sorted((folder / "textures" / "sheets").glob("*.png")):
        if p.name.endswith(".orig.png"):
            continue
        px = png_read(p)
        for row in px:
            for i, v in enumerate(row):
                row[i] = (v & 0xFF000000) | ((v & 0x00FFFF) << 8) | ((v >> 16) & 0xFF)
        p.write_bytes(png_rgba(px))
        painted += 1
    return painted


def drop_cell(folder: Path, cells, index: int):
    """Rewrite metatiles.json without one cell — the way a repaint loses a
    subject: the sheet still builds, that cell's tile keys simply stop being
    claimed by anything (the ADR-0156 rewrite, used here as a defect)."""
    kept = [c for c in cells if c["index"] != index]
    (folder / "textures" / "sheets" / "metatiles.json").write_text(
        serialize_sheet("metatiles", 16, 1, 3, "metatiles.png", "metatiles.orig.png", kept),
        encoding="utf-8")


def coverage_preservation_tests(root: Path):
    """PRD Phase 10 S10.d: the "nothing broken" check for a repainted pack is
    key/coverage preservation, not pixel identity. Three arms: a skinned pack
    passes, a pack that lost a cell's keys fails naming them, and a pack whose
    sheet is gone fails as unresolved."""
    def baseline_of(folder: Path, name: str) -> Path:
        """The recorder's pack, kept aside before the repaint: its own manifest
        and its own PNGs, exactly what `--baseline` points at in practice."""
        copy = root / name
        shutil.copytree(folder, copy)
        return copy / "textures" / "hires.txt"

    # --- pass arm: every pixel repainted, every key kept ---
    good, _v, _c = make_sheet_folder(root, "skin-pass")
    if run("build", str(good)) is None:
        return
    base = baseline_of(good, "skin-pass-baseline")
    before = (good / "textures" / "sheets" / "metatiles.png").read_bytes()
    if skin(good) != 3 or (good / "textures" / "sheets" / "metatiles.png").read_bytes() == before:
        fail("the skin fixture did not actually change the sheet pixels")
        return
    if run("build", str(good)) is None:
        return
    out = run("check-coverage", str(good), "--baseline", str(base))
    if out is None:
        return
    if "every baseline tile key still resolves" not in out or "unchanged (19)" not in out:
        fail(f"S10.d: a fully repainted pack did not pass the coverage check:\n{out}")
    else:
        ok("S10.d: a skinned pack passes — every key resolves, tiles-with-art unchanged, pixels ignored")

    # --- fail arm: one cell dropped from the sidecar, its keys go with it ---
    bad, _v, bad_cells = make_sheet_folder(root, "skin-drop")
    if run("build", str(bad)) is None:
        return
    bad_base = baseline_of(bad, "skin-drop-baseline")
    skin(bad)
    drop_cell(bad, bad_cells, 5)  # metatile 5 is metatiles-only: shapes 18 and 19
    if run("build", str(bad)) is None:
        return
    out = run("check-coverage", str(bad), "--baseline", str(bad_base), expect=1)
    if out is None:
        return
    named = [tile_hex(s) for s in (18, 19) if f"{tile_hex(s)}/{PAL_HEX}" in out]
    if "2 baseline tile key(s) no longer resolve" not in out:
        fail(f"S10.d: a dropped cell did not fail as a dropped key:\n{out}")
    elif len(named) != 2:
        fail(f"S10.d: the failure did not name both dropped keys ({named}):\n{out}")
    elif "tiles-with-art count changed over the baseline's keys: 19 -> 17" not in out:
        fail(f"S10.d: the failure did not report the F5.4d count loss:\n{out}")
    else:
        ok("S10.d: a pack with a dropped key fails, naming the keys and the tiles-with-art loss")

    # --- fail arm: the keys are still declared, but their sheet is gone ---
    gone, _v, _c = make_sheet_folder(root, "skin-gone")
    if run("build", str(gone)) is None:
        return
    gone_base = baseline_of(gone, "skin-gone-baseline")
    skin(gone)
    if run("build", str(gone)) is None:
        return
    (gone / "textures" / "sheets" / "metatiles.png").unlink()
    out = run("check-coverage", str(gone), "--baseline", str(gone_base), expect=1)
    if out is None:
        return
    if "is missing or not a valid PNG" not in out:
        fail(f"S10.d: a missing sheet was not reported as an unresolved key:\n{out}")
    else:
        ok("S10.d: a key whose sheet is missing fails as unresolved, not as a pass")


def check_coverage_baseline_universe_tests(root: Path):
    """#218: the baseline and the rebuild have to describe the same universe.

    `build` re-derives exactly the keys a `textures/sheets/` image claims,
    while the recorder's own manifest keys every CHR tile it saw out of
    `textures/chr/` (ADR-0043). Comparing the two reported a drop on an
    untouched rebuild of a bootstrap pack, and a copy of the post-build
    manifest kept outside the pack resolved nothing and passed vacuously
    (ADR-0189, Consequences). Three arms: the recorder manifest is refused,
    a detached sheet-derived baseline is read against the pack and passes on
    a repaint, and the same detached baseline still fails a deleted sheet —
    the protection this gate exists for. A fourth arm deletes *every* sheet:
    the diagnosis must stay "the art is gone" (exit 1) and not become "this
    baseline is unusable" (exit 2), which is a different, wrong answer."""
    pack, _v, _c = make_sheet_folder(root, "cc-218")
    if run("build", str(pack)) is None:
        return

    # --- the recorder's manifest: every key out of textures/chr/ ---
    rec = root / "cc-218-recorder"
    (rec / "textures" / "chr").mkdir(parents=True)
    (rec / "textures" / "chr" / "Chr_0.png").write_bytes(png(128, 128))
    lines = ["<ver>107", "<scale>1", "<system>nes", "<img>chr/Chr_0.png"]
    lines += [f"<tile>0,{tile_hex(shape)},{PAL_HEX},0,0,1,N" for shape in range(20)]
    (rec / "textures" / "hires.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
    out = run("check-coverage", str(pack), "--baseline", str(rec / "textures" / "hires.txt"), expect=2)
    if out is None:
        return
    if "not sheet-derived" not in out:
        fail(f"#218: the recorder's manifest was not refused as a baseline:\n{out}")
    elif "tiles-with-art count changed" in out or "coverage not preserved" in out:
        fail(f"#218: a non-sheet baseline still reported a drop on an untouched pack:\n{out}")
    else:
        ok("#218: a raw recorder manifest is refused as a baseline, not reported as a drop")

    # --- a sheet-derived baseline kept outside the pack, then a repaint ---
    detached = root / "cc-218-baseline-hires.txt"
    detached.write_bytes((pack / "textures" / "hires.txt").read_bytes())
    skin(pack)
    if run("build", str(pack)) is None:
        return
    out = run("check-coverage", str(pack), "--baseline", str(detached))
    if out is None:
        return
    if "unchanged (19)" not in out or "baseline 20 resolved key(s)" not in out:
        fail(f"#218: a baseline kept outside the pack did not compare its 20 keys:\n{out}")
    else:
        ok("#218: a sheet-derived baseline kept outside the pack is read against the pack, not vacuously")

    # --- and it still catches a real loss: the sheet is gone ---
    (pack / "textures" / "sheets" / "metatiles.png").unlink()
    out = run("check-coverage", str(pack), "--baseline", str(detached), expect=1)
    if out is None:
        return
    if "declared but unresolved" not in out or "metatiles.png" not in out:
        fail(f"#218: a deleted sheet passed against a detached baseline:\n{out}")
    else:
        ok("#218: a deleted sheet still fails against a detached baseline")

    # --- total deletion is the same loss, not a different diagnosis ---
    # Review on #223: `detached` used to be decided by whether any key still
    # resolved, so deleting *every* sheet dropped the baseline into the
    # "nothing was compared" refusal (exit 2, "keep the baseline beside the
    # pack it describes") instead of reporting the loss as exit 1.
    for sheet in sorted((pack / "textures" / "sheets").glob("*.png")):
        sheet.unlink()
    out = run("check-coverage", str(pack), "--baseline", str(detached), expect=1)
    if out is None:
        return
    if "declared but unresolved" not in out:
        fail(f"#218: deleting every sheet did not report the loss:\n{out}")
    elif "not one tile key of" in out or "Keep the baseline manifest with the pack" in out:
        fail(f"#218: deleting every sheet answered with 'relocate the baseline' (exit 2), "
             f"not the coverage loss:\n{out}")
    else:
        ok("#218: deleting every sheet is reported as the coverage loss, not as an unusable baseline")


def check_coverage_layout_tests(root: Path):
    """#172: `check-coverage` resolves both of its paths from one rule — the
    argument is the pack folder, exactly the one `build` takes — and it never
    compares the rebuilt manifest against itself.

    Three arms: the documented layout (the recorder's pack kept nested under
    `auto/`, which is also `build`'s second key source) runs with no
    `--baseline` at all; a pack with no baseline anywhere says how to obtain
    one instead of telling the user to run a `build` they have just run; and a
    `--baseline` that resolves to the candidate is refused rather than passed
    vacuously, which is the guard the F9.18 panel never actually got."""
    pack, _v, _c = make_sheet_folder(root, "cc-layout")
    if run("build", str(pack)) is None:
        return
    # The recorder's manifest and its sheets, kept nested where `build` writing
    # in place cannot reach them.
    shutil.copytree(pack / "textures", pack / "auto" / "textures")
    if run("build", str(pack)) is None:
        return
    out = run("check-coverage", str(pack))
    if out is None:
        return
    if "every baseline tile key still resolves" not in out:
        fail(f"#172: check-coverage on the pack folder did not run against its default baseline:\n{out}")
    else:
        ok("#172: check-coverage takes the pack folder and finds its default baseline there")

    # No baseline anywhere: `build` has run, so "run build first" is advice
    # about a step already taken and names a file that is not the missing one.
    lone, _v, _c = make_sheet_folder(root, "cc-nobaseline")
    if run("build", str(lone)) is None:
        return
    out = run("check-coverage", str(lone), expect=2)
    if out is None:
        return
    if "run `mep_build.py build` first" in out or "before `build` ran" not in out:
        fail(f"#172: the missing-baseline error does not say how to obtain a baseline:\n{out}")
    else:
        ok("#172: a missing baseline says how to obtain one, not `run build first`")

    out = run("check-coverage", str(lone), "--baseline",
              str(lone / "textures" / "hires.txt"), expect=2)
    if out is None:
        return
    if "same file" not in out:
        fail(f"#172: comparing the rebuilt manifest with itself was not refused:\n{out}")
    else:
        ok("#172: baseline == candidate is refused instead of passing vacuously")


def build_summary_tests(root: Path):
    """#173: the build summary must let an artist tell designed narrowing from
    a broken rebuild.

    A rebuild legitimately carries fewer keys than the manifest it read: the
    bootstrap exports every CHR tile as a palette-agnostic defaultTile
    (ADR-0043), the sheets carry only what was routed to them, and the cells a
    captured screen owns are off the sheets entirely (ADR-0156/ADR-0160) —
    ADR-0172 measured that shape and accepted it. So the count is reported as a
    delta against the key source, with the reason, and the lint warnings about
    the tool's own sheet geometry are grouped instead of burying the lines an
    artist can act on."""
    folder, _v, _c = make_sheet_folder(root, "summary")
    # Four keys no sheet carries — the shape of a key the recorder exported and
    # the rebuild cannot route, e.g. a cell a captured screen owns.
    hires = folder / "textures" / "hires.txt"
    hires.write_text(hires.read_text(encoding="utf-8")
                     + "".join(f"<tile>0,{tile_hex(s)},{PAL_HEX},0,0,1,N\n" for s in range(40, 44)),
                     encoding="utf-8")
    out = run("build", str(folder))
    if out is None:
        return
    if "tile keys: 12 in the key source" not in out or "8 carried, 4 dropped" not in out:
        fail(f"#173: build did not report the key count as a delta against its key source:\n{out}")
    else:
        ok("#173: build reports tile keys as a delta against the manifest it read")
    if "expected, not breakage" not in out or "backgrounds/screenNNN.png" not in out:
        fail(f"#173: dropped keys came with no reason and no repaint surface named:\n{out}")
    else:
        ok("#173: a dropped key is explained, with the screen-owned case named")
    if "not a multiple of" in out:
        fail(f"#173: the tool's own sheet-geometry warnings are still inline:\n{out}")
    elif "are about the tool's own output" not in out or "sheets/metatiles.png" not in out:
        fail(f"#173: the tool's own warnings were silenced instead of grouped and named:\n{out}")
    else:
        ok("#173: the tool's own sheet-geometry warnings are grouped, counted and named")


def make_author_folder(root: Path, keys: int = 16, name: str = "author"):
    """A buildable author folder: a 16-column sheet at scale 2 (16px cells),
    a key-source hires.txt, and one bgm + one sfx OGG."""
    folder = root / name
    (folder / "textures" / "sheets").mkdir(parents=True)
    (folder / "audio" / "bgm").mkdir(parents=True)
    (folder / "audio" / "sfx").mkdir(parents=True)
    (folder / "textures" / "sheets" / "objects.png").write_bytes(png(256, 16))  # 16 cells, scale 2
    lines = ["<ver>107", "<scale>2", "<system>nes",
             "<supportedRom>2A4E126D0286BEA0BF503C80A12352C57539F76B", "<img>old.png"]
    lines.extend(f"<tile>0,{k:02X}{'00' * 15},0F001A2C,0,0,1,N" for k in range(keys))
    (folder / "textures" / "hires.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
    (folder / "audio" / "bgm" / "01.ogg").write_bytes(b"")
    (folder / "audio" / "sfx" / "03.ogg").write_bytes(b"")
    return folder



def pages_only_test(root: Path):
    """ADR-0219 / PRD F12.9: a pack that is only CHR pages and their manifest.

    The normal build maps a key source onto `textures/sheets/` cells; there are
    no sheets here and nothing to map, because a static kit's manifest already
    names the page each key is painted on and the crop inside it. What this
    checks is that the path is taken only for a pack that declares itself one,
    that it carries every rule over unchanged, and that it refuses rather than
    overwrites when the pack turns out to hold a recording."""
    folder = root / "pages-only"
    chr_dir = folder / "textures" / "chr"
    chr_dir.mkdir(parents=True)
    scale, cell = 2, 16
    (chr_dir / "Chr_00_0.png").write_bytes(png(16 * cell, 16 * cell))
    rows = [f"<tile>0,{i:02X},{PAL_HEX},{(i % 16) * cell},{(i // 16) * cell},1,Y"
            for i in range(256)]
    manifest = chr_dir / "fill-rules.hires.txt"
    manifest.write_text("\n".join(
        ["# mep-pages-only 1", "<ver>109", f"<scale>{scale}",
         "<supportedRom>" + "A" * 40, "<img>chr/Chr_00_0.png"] + rows) + "\n")

    out = run("build", str(folder))
    if out is None:
        return
    hires = folder / "textures" / "hires.txt"
    text = hires.read_text(encoding="utf-8")
    tiles = [ln for ln in text.splitlines() if ln.startswith("<tile>")]
    if len(tiles) != 256:
        fail(f"pages-only build emitted {len(tiles)} tiles, expected 256")
    elif any(not ln.endswith(",Y") for ln in tiles):
        fail("pages-only build dropped a defaultTile=Y")
    elif tiles != rows:
        fail(f"pages-only build rewrote a rule: {next(a for a, b in zip(tiles, rows) if a != b)}")
    else:
        ok("pages-only build carries every <tile> rule over unchanged, all defaultTile=Y")
    if "<img>chr/Chr_00_0.png" not in text or "<scale>2" not in text:
        fail(f"pages-only manifest lost its <img>/<scale>:\n{text[:300]}")
    else:
        ok("pages-only build keeps the page reference and the declared scale")

    # Idempotent: the second build overwrites the manifest the first one wrote.
    if run("build", str(folder)) is not None and hires.read_text(encoding="utf-8") == text:
        ok("a pages-only build is idempotent")
    else:
        fail("rebuilding a pages-only pack changed its manifest")

    # A recorded manifest is never overwritten by this path.
    hires.write_text("<ver>109\n<scale>2\n<img>chr/Chr_00_0.png\n" + rows[0] + "\n")
    out = run("build", str(folder), expect=2)
    if out is not None and "refusing to overwrite a recorded manifest" in out:
        ok("a pack whose manifest was not built from its pages is refused")
    else:
        fail(f"a recorded manifest was not protected: {out}")
    hires.unlink()

    # A crop outside the page is an error, not a silent mis-slice.
    bad = root / "pages-only-bad"
    shutil.copytree(folder, bad)
    m = bad / "textures" / "chr" / "fill-rules.hires.txt"
    m.write_text(m.read_text().replace(
        f"<tile>0,00,{PAL_HEX},0,0,1,Y", f"<tile>0,00,{PAL_HEX},9000,0,1,Y"))
    out = run("build", str(bad), expect=2)
    if out is not None and "outside" in out:
        ok("a crop outside the page is a build error")
    else:
        fail(f"an out-of-page crop was accepted: {out}")

    # Without the marker the folder is not a pages-only pack at all.
    plain = root / "pages-only-unmarked"
    shutil.copytree(folder, plain)
    m = plain / "textures" / "chr" / "fill-rules.hires.txt"
    m.write_text(m.read_text().replace("# mep-pages-only 1\n", ""))
    out = run("build", str(plain), expect=2)
    if out is not None and "no tile-key source" in out:
        ok("an unmarked fill-rules file is not treated as a manifest")
    else:
        fail(f"an unmarked fill-rules file was picked up as a manifest: {out}")


def main() -> int:
    with tempfile.TemporaryDirectory() as tmp:
        root = Path(tmp)
        folder = make_author_folder(root)

        # --- build ---
        out = run("build", str(folder))
        if out is None:
            return 1
        hires = folder / "textures" / "hires.txt"
        text = hires.read_text(encoding="utf-8")
        tiles = [ln for ln in text.splitlines() if "<tile>" in ln]
        if len(tiles) != 16:
            fail(f"build emitted {len(tiles)} tiles, expected 16")
        else:
            ok("build emitted 16 tiles")
        if "<img>sheets/objects.png" not in text:
            fail("build did not point <img> at the sheet under textures/sheets/")
        else:
            ok("build points <img> at the author sheet")
        # Cell k -> crop (k%16)*16, 0 at scale 2; key k -> tileData first byte k.
        for k, t in enumerate(tiles):
            body = t.split(">", 1)[1]
            f = body.split(",")
            want_x = str((k % 16) * 16)
            want_data = f"{k:02X}{'00' * 15}"
            if f[0] != "0" or f[3] != want_x or f[4] != "0" or f[1].upper() != want_data:
                fail(f"tile {k} not re-pointed: {body}")
                break
        else:
            ok("tile crops re-pointed to sheet cells (scale 2, img 0)")
        audio = (folder / "audio" / "hires.txt").read_text(encoding="utf-8")
        if "<bgm>0,1,bgm/01.ogg" not in audio or "<sfx>0,3,sfx/03.ogg" not in audio:
            fail(f"audio manifest missing expected ids:\n{audio}")
        else:
            ok("audio manifest references the new OGGs with their file-name ids")
        # run(expect=0) above already asserted the lint gate (a non-zero lint
        # exit fails the build); "0 error(s)" in the text would false-match a
        # naive substring search, so rely on the exit code alone.
        ok("build lints clean (lint gate = exit 0)")

        # --- pack determinism + correctness ---
        rom = root / "syn.nes"
        subprocess.run([PY, str(GEN_ROM), str(rom)], check=True, capture_output=True)
        z1, z2 = root / "a.zip", root / "b.zip"
        run("pack", str(folder), "--rom", str(rom), "--name", "F5.4c Test", "--version", "1.0.0", "--out", str(z1))
        run("pack", str(folder), "--rom", str(rom), "--name", "F5.4c Test", "--version", "1.0.0", "--out", str(z2))
        d1, d2 = hashlib.sha256(z1.read_bytes()).hexdigest(), hashlib.sha256(z2.read_bytes()).hexdigest()
        if d1 != d2:
            fail(f"pack not deterministic: {d1} != {d2}")
        else:
            ok(f"pack is byte-deterministic ({d1[:12]}…)")
        with zipfile.ZipFile(z1) as zf:
            names = zf.namelist()
            if names[0] != "pack.json":
                fail(f"pack.json is not the first zip entry: {names[:3]}")
            else:
                ok("pack.json is the first zip entry")
            meta = zf.read("pack.json").decode("utf-8")
            pj = json_loads(meta)
        if pj.get("sections") != {"textures": {"path": "textures/"}, "audio": {"path": "audio/"}}:
            fail(f"pack.json sections wrong: {pj.get('sections')}")
        else:
            ok("pack.json sections derived from the tree")
        if not pj["targets"][0]["sha1"]:
            fail("pack --rom did not fill targets[0].sha1")
        else:
            ok(f"pack --rom computed No-Intro sha1 {pj['targets'][0]['sha1'][:8]}...")

        # --- rename-audio-id (F5.4g Bloco D item 12 id lifecycle) ---
        audio_dir = folder / "audio"
        (audio_dir / "midi").mkdir(exist_ok=True)
        (audio_dir / "midi" / "track01.mid").write_bytes(b"M")
        (audio_dir / "bgm" / "track01.ogg").write_bytes(b"O")
        fp = audio_dir / "fingerprints.json"
        fp.write_text('{\n  "version": 1,\n  "tracks": [\n'
                      '    { "id": "track01", "kind": "bgm", "frames": 10, "midi": "midi/track01.mid", "events": [[0,0,0]] }\n'
                      '  ]\n}\n', encoding="utf-8")
        (audio_dir / "hires.txt").write_text("<ver>107\n<bgm>0,1,bgm/track01.ogg\n", encoding="utf-8")
        run("rename-audio-id", str(folder), "track01", "track02")
        if not (audio_dir / "midi" / "track02.mid").exists() or (audio_dir / "midi" / "track01.mid").exists():
            fail("rename-audio-id did not move midi/track01.mid -> track02.mid")
        else:
            ok("rename-audio-id moved the midi file")
        if not (audio_dir / "bgm" / "track02.ogg").exists() or (audio_dir / "bgm" / "track01.ogg").exists():
            fail("rename-audio-id did not move bgm/track01.ogg -> track02.ogg")
        else:
            ok("rename-audio-id moved the bgm OGG")
        fp_text = fp.read_text(encoding="utf-8")
        if '"id": "track01"' in fp_text or '"midi": "midi/track01.mid"' in fp_text:
            fail("rename-audio-id did not rewrite fingerprints.json id/midi")
        else:
            ok("rename-audio-id rewrote fingerprints.json id + midi path")
        hires_text = (audio_dir / "hires.txt").read_text(encoding="utf-8")
        if "bgm/track01.ogg" in hires_text or "bgm/track02.ogg" not in hires_text:
            fail("rename-audio-id did not rewrite the audio/hires.txt reference")
        else:
            ok("rename-audio-id rewrote the audio/hires.txt reference")

        # --- flat-pack migration: dangling seed refs dropped, ids reclaimed ---
        mig = root / "migrate"
        (mig / "textures" / "sheets").mkdir(parents=True)
        (mig / "audio" / "bgm").mkdir(parents=True)
        (mig / "audio" / "sfx").mkdir(parents=True)
        (mig / "textures" / "sheets" / "a.png").write_bytes(png(256, 16))
        # Key source carries bgm/sfx whose files do NOT exist under audio/:
        # the migration path must drop them and hand the freed ids to the
        # real OGGs, not keep dangling refs or collide.
        (mig / "textures" / "hires.txt").write_text(
            "<ver>107\n<scale>2\n<system>nes\n"
            + "\n".join(f"<tile>0,{k:02X}{'00' * 15},0F001A2C,0,0,1,N" for k in range(16))
            + "\n<bgm>0,0,track01.ogg\n<sfx>0,3,jump.ogg\n", encoding="utf-8")
        (mig / "audio" / "bgm" / "01.ogg").write_bytes(b"")
        (mig / "audio" / "sfx" / "03.ogg").write_bytes(b"")
        run("build", str(mig))
        mig_audio = (mig / "audio" / "hires.txt").read_text(encoding="utf-8")
        if "track01.ogg" in mig_audio or "jump.ogg" in mig_audio:
            fail(f"migration kept dangling seed refs:\n{mig_audio}")
        elif "<bgm>0,1,bgm/01.ogg" not in mig_audio or "<sfx>0,3,sfx/03.ogg" not in mig_audio:
            fail(f"migration did not reclaim the freed ids:\n{mig_audio}")
        elif mig_audio.count("<sfx>") != 1 or mig_audio.count("<bgm>") != 1:
            fail(f"migration produced duplicate track ids:\n{mig_audio}")
        else:
            ok("flat-pack migration drops dangling refs, reclaims ids, no duplicates")

        # --- non-NES packs skip the audio manifest (frozen, ADR-0041) ---
        sms = root / "sms"
        (sms / "textures" / "sheets").mkdir(parents=True)
        (sms / "audio" / "bgm").mkdir(parents=True)
        (sms / "textures" / "sheets" / "a.png").write_bytes(png(256, 16))
        (sms / "textures" / "hires.txt").write_text(
            "<ver>200\n<system>sms\n<scale>2\n"
            + "\n".join(f"<tile>0,{k:02X}{'00' * 15},0F001A2C,0,0,1,N" for k in range(16))
            + "\n", encoding="utf-8")
        (sms / "audio" / "bgm" / "01.ogg").write_bytes(b"")
        out = run("build", str(sms))
        if (sms / "audio" / "hires.txt").exists():
            fail("non-NES build wrote an audio manifest (GB/SMS OGG frozen)")
        else:
            ok("non-NES build skips the audio manifest")

        # --- empty argv is a usage error, not a crash ---
        p = subprocess.run([PY, str(MEP_BUILD)], capture_output=True, text=True)
        if p.returncode != 2 or "usage" not in (p.stdout + p.stderr):
            fail(f"mep_build with no args -> exit {p.returncode}, expected 2 + usage")
        else:
            ok("mep_build with no args prints usage and exits 2")

        # --- failure modes (exit 2) ---
        no_source = root / "no-source"
        (no_source / "textures" / "sheets").mkdir(parents=True)
        (no_source / "textures" / "sheets" / "a.png").write_bytes(png(256, 16))
        out = run("build", str(no_source), expect=2)
        if out is not None and "no tile-key source" in out:
            ok("missing key source fails with guidance")
        else:
            fail(f"missing key source: {out}")

        small = make_author_folder(root, keys=20, name="small")  # 16 cells, 20 keys
        run("build", str(small), expect=2)

        wide = make_author_folder(root, name="wide")
        (wide / "textures" / "sheets" / "objects.png").unlink()
        (wide / "textures" / "sheets" / "objects.png").write_bytes(png(512, 16))  # 32 columns
        run("build", str(wide), expect=2)

        # --- ADR-0153 / F9.4: the artist-legible sheet round-trip ---
        sheet_alias_tests(root)
        map_full_vocabulary_tests(root)
        sheet_round_trip_tests(root)

        # --- F9.10: sheets/ is the front door; chr/ is where the CHR-order
        # fragments went, and the round trip cannot notice the difference ---
        chr_relegation_test(root)

        # --- PRD Phase 10 S10.c/S10.d: the export label + local data, and
        # the coverage rule that replaces pixel identity for a skinned pack ---
        pack_extra_data_tests(root, rom)
        coverage_preservation_tests(root)

        # --- #172 / #173: the two reporting defects the Phase 9 validation
        # panel hit — a coverage check that could not be pointed anywhere, and
        # a build summary that read as damage ---
        check_coverage_layout_tests(root)
        # --- #218: the baseline universe (only what `build` re-derives) ---
        check_coverage_baseline_universe_tests(root)
        build_summary_tests(root)

        # --- ADR-0196 / F12.5: the overflow layer's <addition> tags ---
        addition_chr_ram_tests(root)
        addition_refusal_tests(root)
        addition_chr_rom_tests(root)

        # --- F5.4g item 12: audio_cleanup_suggest reads the probe's log ---
        sug = root / "sug-pack"
        (sug / "auto" / "audio").mkdir(parents=True)
        (sug / "auto" / "audio" / "enumeration.log").write_text(
            "id,kind,audible,frames,last,hash,first-notes,repeat\n"
            "0,bgm,300,300,299,37CEFCA2,\"s2 1\",no\n"
            "1,short,12,300,20,811C9DC5,\"s0 1\",no\n"
            "2,bgm,300,300,298,37CEFCA2,\"s2 1\",yes\n"
        )
        p = subprocess.run([PY, str(REPO / "scripts" / "audio_cleanup_suggest.py"), str(sug)],
                           capture_output=True, text=True)
        out = (p.stdout + p.stderr).strip()
        if p.returncode != 1 or "kind=short" not in out or "repeat of an earlier id" not in out:
            fail(f"audio_cleanup_suggest -> exit {p.returncode}, expected 1 + garbage ids: {out}")
        else:
            ok("audio_cleanup_suggest flags short/repeat/silent ids from the probe's enumeration.log")

        pages_only_test(root)

    return 1 if FAILED else 0


def json_loads(s):
    import json
    return json.loads(s)


def json_dumps(obj) -> str:
    import json
    return json.dumps(obj, indent=2) + "\n"


if __name__ == "__main__":
    sys.exit(main())
