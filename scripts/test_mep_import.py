#!/usr/bin/env python3
"""Acceptance test for scripts/mep_import.py (PRD F12.7, ADR-0198 §1).

A synthetic legacy pack — `hires.txt` plus PNGs, no sheets, no `<patch>` —
is imported and then rebuilt, and the rebuild is compared against the input
the way the slice's stop rule says: by `(tileData, palette, condition)` and
by pixels. Everything here runs on temp folders; nothing reads the user's
data home or a repository path.

Covers:

  * the stdlib PNG codec: 8-bit RGBA and grey/RGB, the *palette* PNG a
    legacy pack very commonly keys its art in (color type 3, with tRNS),
    an out-of-bounds crop, the 1x `downscale` every sheet's `*.orig.png`
    twin needs, and the refuse-with-a-reason cases (16-bit, truncated,
    not-a-PNG, missing file);
  * a **data-keyed** (CHR RAM) round-trip: the key source is written
    verbatim, `<background>`/`<bgm>` come along, a stray line (a comment
    that lost its `#`) is carried instead of refused, the conditioned rule
    gets ADR-0189 §3's bare twin, and `verify` passes by default while
    `verify --strict` fails on that twin;
  * an **index-keyed** (CHR ROM) round-trip: `<ver>` is raised to 103 and
    each `<tile>` key rewritten to the hex form `_index_token` emits, so
    the parsed key survives the round-trip even though the token *text*
    changes (ADR-0172) — the one place an import is not a copy;
  * refusals, each naming what it refused: `<patch>` without `--rom` (ADR-0198
    §3), an unknown tag, a `[...]` prefix on a tag that takes none, a `<tile>` with
    too few fields, a malformed tileData/palette, a bitmap index out of
    range, a missing `<img>` PNG, a non-integer `<scale>`, a non-empty
    `--out` without `--force`, and — for an index-keyed pack — a construct
    the 103 raise would re-read (`frameRange` at 101, a multi-field
    `<background>`, a 6-field `memoryCheck`);
  * the shape a legacy pack animates with: one `(tileData, palette)` pair
    drawn at several crops, one per condition. Each rule gets its own
    `exactCondition` cell, so the rebuild reproduces the input's rules and
    its pixels — with and without an unconditional rule in the pair;
  * the CLI: the bare-path form, `verify`, and `--force`;
  * the **patched-ROM import** (ADR-0198 §3, option (a)): a pack with `<patch>`
    needs `--rom`; the `<patch>` line whose sha1 is the dump's whole-file or
    No-Intro sha1 is applied in memory (`mep_patch.apply_ips`, mirroring
    `IpsPatcher` — stream order, RLE, growth, truncate), the key source's
    `<supportedRom>` becomes the patched whole-file sha1, the IPS lands beside
    both manifests, the emitted `<patch>` token is the same `/`-separated path
    the IPS was copied to (the canonical spelling; the loader rewrites `\\`
    to `/` itself, so either form loads), build + verify round-trip, and
    `verify` fails when either IPS copy is gone or its bytes differ from the
    source pack's. Refusals: no `--rom`, a
    dump none of the `<patch>` lines names
    (ADR-0145 (3)), a missing or non-IPS patch file, a truncated IPS, and a
    declared `<supportedRom>` that contradicts every hash in play (ADR-0211).
    The limit statement is printed and written, in the wording per key form;
  * the **index read** (F12.12, ADR-0210 §3), which is the other direction:
    their `hires.txt` read as facts about the ROM, never as art. A CHR RAM
    game's new shapes are rendered from the pack's own pattern bytes at the
    pack's scale (`index.png` / `.orig.png` / `.json`, provenance `index`,
    `seen: false`, the other palettes of a shape riding as `aliases`); a CHR
    ROM game takes the palette set and nothing else and says so. Each of the
    three mandatory filters has its own case — the index range against the
    loaded CHR (read the loader's own way, `<ver>` deciding decimal vs hex),
    a `<patch>` pack refused by name, and `<condition>` lines never read — and
    the read never opens a PNG of the input pack (asserted with a trap), never
    touches the F12.8 `unsorted` sheet, and produces a sheet `mep_build.py
    build` slices back into rules with 0 errors.

Framework-free, mirroring scripts/test_mep_build.py's ok()/fail() style.
Wired into `make python-tests` by name. Usage:
python3 scripts/test_mep_import.py
"""

import contextlib
import io
import json
import struct
import sys
import tempfile
import zlib
from pathlib import Path

SCRIPTS = Path(__file__).resolve().parent
sys.path.insert(0, str(SCRIPTS))
import mep_build  # noqa: E402
import mep_import as MI  # noqa: E402
import mep_patch as MP  # noqa: E402

FAILED = 0

HEX_A = "0123456789ABCDEF0123456789ABCDEF"
HEX_B = "FEDCBA9876543210FEDCBA9876543210"
PAL_A = "0F20210F"
PAL_B = "1F2F3F4F"


def ok(msg):
    print(f"PASS: {msg}")


def fail(msg):
    global FAILED
    FAILED = 1
    print(f"FAIL: {msg}")


def chunk(tag, data):
    return (struct.pack(">I", len(data)) + tag + data
            + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))


def png(ihdr, idat, extra=b""):
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr) + extra
            + chunk(b"IDAT", zlib.compress(idat, 9)) + chunk(b"IEND", b""))


def png_rgba(width, height, px):
    """8-bit RGBA (color type 6), filter 0. `px` is rows of (r,g,b,a)."""
    raw = bytearray()
    for row in px:
        raw.append(0)
        for p in row:
            raw += bytes(p)
    return png(struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0), bytes(raw))


def png_palette(width, height, indices, palette, trns=b""):
    """8-bit palette (color type 3) + PLTE (+ tRNS) — the legacy-pack shape."""
    raw = bytearray()
    for row in indices:
        raw.append(0)
        raw += bytes(row)
    extra = chunk(b"PLTE", palette) + (chunk(b"tRNS", trns) if trns else b"")
    return png(struct.pack(">IIBBBBB", width, height, 8, 3, 0, 0, 0), bytes(raw), extra)


def cell_png(cols, rows, scale, tag=0):
    """A picture whose every `8*scale` cell is a distinct flat colour, so a
    crop's pixels identify the cell the crop came from."""
    size = 8 * scale
    px = []
    for y in range(rows * size):
        row = []
        for x in range(cols * size):
            c, r = x // size, y // size
            row.append(((10 + 40 * c + 7 * tag) % 256, (20 + 30 * r) % 256,
                        (30 + 20 * (c + r) + 5 * tag) % 256, 255))
        px.append(row)
    return png_rgba(cols * size, rows * size, px)


def write_src(root: Path, lines, files: dict) -> Path:
    src = root / "src"
    src.mkdir(parents=True, exist_ok=True)
    (src / "hires.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
    for rel, data in files.items():
        p = src / rel
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_bytes(data)
    return src


def imported_pack(root: Path, name, lines, files: dict):
    """(src, project) after an import — the fixture every round-trip case
    starts from."""
    src = write_src(root / name, lines, files)
    project = root / f"{name}-proj"
    summary = MI.import_pack(src, project, force=False)
    return src, project, summary


def run_build(project: Path):
    buf = io.StringIO()
    with contextlib.redirect_stdout(buf), contextlib.redirect_stderr(buf):
        rc = mep_build.main(["build", str(project), "--quiet"])
    return rc, buf.getvalue()


def run_verify(src: Path, project: Path, strict=False):
    buf = io.StringIO()
    with contextlib.redirect_stdout(buf), contextlib.redirect_stderr(buf):
        rc = MI.verify_pack(src, project, strict)
    return rc, buf.getvalue()


def expect_error(fn, needle, what):
    try:
        fn()
    except MI.PackError as e:
        if needle.lower() not in str(e).lower():
            fail(f"{what}: refused, but the message does not mention {needle!r}: {e}")
            return
        ok(f"{what}: refused with a message naming {needle!r}")
        return
    fail(f"{what}: expected a refusal, the import went through")


# --- the PNG codec -----------------------------------------------------------

def test_png_codec(root: Path):
    # A palette PNG with tRNS: index 0 transparent, index 1 opaque, index 2
    # past the end of tRNS and therefore opaque (RFC 2083 §11.3.2).
    pal = bytes([255, 0, 0, 0, 255, 0, 0, 0, 255, 9, 9, 9])
    p = root / "pal.png"
    p.write_bytes(png_palette(3, 2, [[0, 1, 2], [3, 0, 1]], pal, trns=bytes([0, 255])))
    img = MI.read_png(p)
    want = [(255, 0, 0, 0), (0, 255, 0, 255), (0, 0, 255, 255)]
    got = [tuple(img.px[(0 * 3 + x) * 4:(0 * 3 + x) * 4 + 4]) for x in range(3)]
    if got != want:
        fail(f"palette PNG decoded to {got}, expected {want}")
    else:
        ok("a palette PNG (color type 3) decodes through PLTE + tRNS to RGBA")

    # 8-bit grey and RGB widen to RGBA too.
    grey = root / "grey.png"
    grey.write_bytes(png(struct.pack(">IIBBBBB", 2, 2, 8, 0, 0, 0, 0),
                         bytes([0, 7, 200, 0, 9, 250])))
    img = MI.read_png(grey)
    if tuple(img.px[:4]) != (7, 7, 7, 255):
        fail(f"grey PNG decoded to {tuple(img.px[:4])}, expected (7,7,7,255)")
    else:
        ok("an 8-bit grey PNG widens to RGBA")

    # A crop outside the image is None, not a short read.
    if img.block(0, 0, 3) is not None or img.block(-1, 0, 1) is not None:
        fail("block() should be None for a crop that does not fit")
    elif img.block(0, 0, 2) != bytes([7, 7, 7, 255, 200, 200, 200, 255,
                                      9, 9, 9, 255, 250, 250, 250, 255]):
        fail(f"block(0,0,2) read {img.block(0, 0, 2)!r}")
    else:
        ok("block() reads a crop and refuses one that does not fit")

    # The 1x twin: dropping every Nth pixel.
    px = [[(1, 1, 1, 255), (2, 2, 2, 255)], [(3, 3, 3, 255), (4, 4, 4, 255)]]
    two = MI.Image(2, 2, bytearray(b for row in px for p in row for b in p))
    one = two.downscale(2)
    if (one.width, one.height) != (1, 1) or tuple(one.px) != (1, 1, 1, 255):
        fail(f"downscale(2) gave {one.width}x{one.height} {tuple(one.px)}")
    else:
        ok("downscale(N) drops every Nth pixel (the *.orig.png twin's rule)")

    # A written PNG reads back pixel for pixel (the sheets are written by it).
    out = root / "rt.png"
    MI.write_png(out, MI.Image(2, 1, bytearray([1, 2, 3, 4, 5, 6, 7, 8])))
    if bytes(MI.read_png(out).px) != bytes([1, 2, 3, 4, 5, 6, 7, 8]):
        fail("write_png -> read_png did not round-trip")
    else:
        ok("write_png -> read_png round-trips pixel for pixel")

    # Refusals: 16-bit depth, a truncated IDAT, and a file that is not PNG.
    deep = root / "deep.png"
    deep.write_bytes(png(struct.pack(">IIBBBBB", 2, 1, 16, 6, 0, 0, 0), bytes(8)))
    expect_error(lambda: MI.read_png(deep), "only 8-bit", "a 16-bit PNG")
    short = root / "short.png"
    short.write_bytes(png(struct.pack(">IIBBBBB", 8, 8, 8, 6, 0, 0, 0), b"\x00" * 4))
    expect_error(lambda: MI.read_png(short), "truncated", "a truncated PNG")
    nope = root / "nope.png"
    nope.write_bytes(b"not a png at all")
    expect_error(lambda: MI.read_png(nope), "not a PNG", "a file that is not a PNG")
    expect_error(lambda: MI.read_png(root / "gone.png"), "cannot be read", "a missing PNG")
    over = root / "over.png"
    over.write_bytes(png_palette(1, 1, [[7]], pal))
    expect_error(lambda: MI.read_png(over), "declares only", "a palette index the PLTE does not declare")


# --- a data-keyed (CHR RAM) pack --------------------------------------------

DATA_LINES = [
    "# a legacy pack",
    "<ver>100",
    "<scale>2",
    "<system>nes",
    "<supportedRom>0000000000000000000000000000000000000000",
    "<condition>C1,spriteAtPosition,8,8," + HEX_B + "," + PAL_B,
    "<img>art.png",
    "<img>art2.png",
    "<tile>0," + HEX_A + "," + PAL_A + ",0,0,1,N",
    "[C1]<tile>1," + HEX_B + "," + PAL_B + ",16,0,1,N",
    "<background>bg.png,1,0,0,20",
    "<bgm>0,1,ogg/loop.ogg",
    " Background 2 - a comment that lost its #",
]
DATA_FILES = {
    "art.png": cell_png(2, 1, 2, tag=0),
    "art2.png": cell_png(2, 1, 2, tag=8),
    "bg.png": cell_png(1, 1, 1),
    "ogg/loop.ogg": b"OggS" + bytes(60),
}


def test_data_keyed(root: Path):
    src, project, summary = imported_pack(root, "data", DATA_LINES, DATA_FILES)
    if summary["keyed"] != "data" or summary["normalized"]:
        fail(f"a 32-hex key must import as data-keyed, got {summary['keyed']}/{summary['normalized']}")
        return
    if summary["rules"] != 2 or summary["twins"] != 1 or summary["split"]:
        fail(f"summary: rules={summary['rules']} twins={summary['twins']} split={summary['split']}")
        return
    if summary["backgrounds"] != 1 or summary["audio"] != 1 or len(summary["strays"]) != 1:
        fail(f"summary: backgrounds={summary['backgrounds']} audio={summary['audio']} "
             f"strays={summary['strays']}")
        return
    ok("a data-keyed pack imports: 2 rules, 1 twin, the background and the OGG along, 1 stray")

    key_source = (project / "auto" / "textures" / "hires.txt").read_text(encoding="utf-8")
    tiles = [ln for ln in key_source.splitlines() if "<tile>" in ln]
    if tiles != ["<tile>0," + HEX_A + "," + PAL_A + ",0,0,1,N",
                 "[C1]<tile>1," + HEX_B + "," + PAL_B + ",16,0,1,N"]:
        fail(f"the key source is not the input's lines verbatim: {tiles}")
        return
    if not (project / "auto" / "textures" / "art.png").is_file():
        fail("the <img> PNGs were not copied beside the key source (lint reads the auto layer too)")
        return
    ok("the key source is the input's own lines, and its art sits beside it")

    sidecar = json.loads((project / "textures" / "sheets" / "art.json").read_text(encoding="utf-8"))
    if sidecar["cells"][0]["tiles"][0]["tile"] != HEX_A or "index" in sidecar["cells"][0]["tiles"][0]:
        fail(f"the sidecar entry should carry the data key and no index: {sidecar['cells'][0]}")
        return
    ok("the sheet sidecar keys a data pack's cell by its 16 data bytes")

    rc, out = run_build(project)
    if rc != 0:
        fail(f"build on the imported project exited {rc}:\n{out}")
        return
    rc, out = run_verify(src, project)
    if rc != 0:
        fail(f"verify failed on a data-keyed import:\n{out}")
        return
    if "1 ADR-0189 §3 twin" not in out:
        fail(f"verify did not name the twin it tolerated:\n{out}")
        return
    ok("build + verify round-trip a data-keyed pack (rule set and pixels)")

    rc, out = run_verify(src, project, strict=True)
    if rc != 1 or "strict" not in out:
        fail(f"verify --strict should fail on the ADR-0189 §3 twin, got {rc}:\n{out}")
        return
    ok("verify --strict treats the ADR-0189 §3 twin as the difference it is")

    # The twin is the conditioned key's unconditional fallback (#256).
    built = (project / "textures" / "hires.txt").read_text(encoding="utf-8")
    conds = [ln.split("<tile>")[0] for ln in built.splitlines() if "<tile>" in ln and HEX_B in ln]
    if sorted(conds) != ["", "[C1]"]:
        fail(f"the rebuilt manifest's rules for the conditioned key are {conds}, expected ['', '[C1]']")
        return
    ok("the rebuilt manifest carries the conditioned rule and its bare fallback")


# --- an index-keyed (CHR ROM) pack ------------------------------------------

INDEX_LINES = [
    "<ver>101",
    "<scale>1",
    "<system>nes",
    "<condition>HPZero,memoryCheck,30,=,3",
    "<img>chr.png",
    "<tile>0,5," + PAL_A + ",0,0,1,N",
    "[HPZero]<tile>0,200," + PAL_B + ",8,0,1,N",
    "<tile>0,200," + PAL_B + ",8,0,1,N",
]
INDEX_FILES = {"chr.png": cell_png(4, 1, 1)}


def test_index_keyed(root: Path):
    src, project, summary = imported_pack(root, "index", INDEX_LINES, INDEX_FILES)
    if summary["keyed"] != "index" or not summary["normalized"] or summary["ver"] != 101:
        fail(f"a short-token pack must import as index-keyed at ver 101: {summary}")
        return
    ok("a short-token pack imports as index-keyed, from <ver>101")

    key_source = (project / "auto" / "textures" / "hires.txt").read_text(encoding="utf-8")
    if "<ver>103" not in key_source:
        fail("the key source does not raise <ver> to 103 (the loader reads a short field as hex there)")
        return
    want = ["<tile>0," + mep_build._index_token(5) + "," + PAL_A + ",0,0,1,N",
            "[HPZero]<tile>0," + mep_build._index_token(200) + "," + PAL_B + ",8,0,1,N",
            "<tile>0," + mep_build._index_token(200) + "," + PAL_B + ",8,0,1,N"]
    tiles = [ln for ln in key_source.splitlines() if "<tile>" in ln]
    if tiles != want:
        fail(f"the key source's tokens are not _index_token's hex form: {tiles}")
        return
    ok("<ver> is raised to 103 and the keys are rewritten to _index_token's hex width (ADR-0172)")

    # A pack already above 103 keeps its own version: lowering <ver>108 to 103
    # made the loader refuse every <addition> line (Metroid #148, 2026-09-22).
    hi_lines = ["<ver>108"] + INDEX_LINES[1:]
    _src, hi_project, hi_summary = imported_pack(root, "index108", hi_lines, INDEX_FILES)
    hi_source = (hi_project / "auto" / "textures" / "hires.txt").read_text(encoding="utf-8")
    if hi_summary["ver"] != 108 or "<ver>108" not in hi_source or "<ver>103" in hi_source:
        fail(f"an index-keyed <ver>108 pack must keep <ver>108 in its key source: {hi_summary}")
        return
    ok("an index-keyed pack above 103 keeps its own <ver> (never lowered)")

    sidecar = json.loads((project / "textures" / "sheets" / "chr.json").read_text(encoding="utf-8"))
    entry = sidecar["cells"][0]["tiles"][0]
    if entry.get("index") != 5 or len(entry.get("tile", "")) != 32:
        fail(f"the sidecar entry should carry index 5 and a 32-hex placeholder: {entry}")
        return
    ok("the sidecar carries the CHR index and a 32-hex placeholder tile")

    rc, out = run_build(project)
    if rc != 0:
        fail(f"build on the imported index-keyed project exited {rc}:\n{out}")
        return
    rc, out = run_verify(src, project, strict=True)
    if rc != 0:
        fail(f"verify failed on an index-keyed import:\n{out}")
        return
    if "tileData *text* changed" not in out or "5 -> 05" not in out:
        fail(f"verify did not report the token-text-only delta (5 -> 05):\n{out}")
        return
    ok("the index-keyed round-trip holds by parsed key and by pixels (--strict too)")


# --- refusals ---------------------------------------------------------------

def test_refusals(root: Path):
    rule = "<tile>0," + HEX_A + "," + PAL_A + ",0,0,1,N"
    base = ["<ver>100", "<scale>2", "<img>art.png", rule]
    art = {"art.png": cell_png(2, 1, 2)}

    def src_with(lines, files=None, name="refuse"):
        return write_src(root / name, lines, dict(art if files is None else files))

    for needle, what, lines, files in [
        ("--rom", "a <patch> without --rom",
         base + ["<patch>fix.ips," + "0" * 40], None),
        ("unknown tag", "an unknown tag", base + ["<frobnicate>1"], None),
        ("only <tile> and <background>", "a prefix on a tag that takes none",
         base + ["[C1]<bgm>0,1,ogg/x.ogg"], None),
        ("6 are required", "a <tile> with 5 fields",
         base[:3] + ["<tile>0," + HEX_A + ",1,2,3"], None),
        ("not a number", "a <tile> with a non-numeric bitmap",
         base[:3] + ["<tile>x," + HEX_A + "," + PAL_A + ",0,0,1,N"], None),
        ("is neither 32 hex", "a <tile> with a malformed tileData",
         base[:3] + ["<tile>0,nope," + PAL_A + ",0,0,1,N"], None),
        ("not 8 hex", "a <tile> with a malformed palette",
         base[:3] + ["<tile>0," + HEX_A + ",zz,0,0,1,N"], None),
        ("out of range", "a bitmap index past the <img> list",
         base + ["<tile>7," + HEX_A + "," + PAL_A + ",0,0,1,N"], None),
        ("does not exist", "an <img> PNG that is not there",
         ["<ver>100", "<scale>2", "<img>nope.png", rule], {}),
        ("outside the loader's", "a <scale> of 11",
         ["<ver>100", "<scale>11", "<img>art.png", rule], None),
        ("no <tile>", "a manifest with no <tile>", ["<ver>100", "<scale>2", "<img>art.png"], None),
    ]:
        src = src_with(lines, files, name="refuse-" + needle.replace(" ", "-")[:20])
        expect_error(lambda s=src: MI.import_pack(s, root / "unused-out", False), needle, what)

    # An index-keyed pack whose <ver> raise would be re-read: refused, naming
    # the line and what changes.
    idx = ["<ver>101", "<scale>1", "<img>art.png", "<tile>0,5," + PAL_A + ",0,0,1,N"]
    cases = [
        ("frameRange", "<condition>F,frameRange,10,20", "frameRange"),
        ("<background> with", "<background>bg.png,1,0,0,20", "a 3-field <background> at 101"),
        ("6th", "<condition>M,memoryCheck,30,=,3,FF", "a 6-field memoryCheck"),
    ]
    for needle, line, what in cases:
        files = {"art.png": cell_png(4, 1, 1), "bg.png": cell_png(1, 1, 1)}
        src = write_src(root / ("vs-" + needle.replace(" ", "")), idx + [line], files)
        expect_error(lambda s=src: MI.import_pack(s, root / "unused-out", False), needle, what)

    # A non-empty --out is refused rather than written into (ADR-0198
    # Consequences: the project lands next to the user's copy).
    src = src_with(base, art, name="nonempty")
    out = root / "nonempty-out"
    out.mkdir(parents=True, exist_ok=True)
    (out / "keep.txt").write_text("mine", encoding="utf-8")
    expect_error(lambda: MI.import_pack(src, out, False), "--force", "a non-empty --out")
    if (out / "keep.txt").read_text(encoding="utf-8") != "mine":
        fail("the refused import wrote into the non-empty output folder anyway")
    else:
        ok("a refused import leaves the output folder untouched")

    # A 16-bit <img> is refused by the codec, with the reason.
    src = write_src(root / "deep-img", ["<ver>100", "<scale>2", "<img>art.png", rule],
                    {"art.png": png(struct.pack(">IIBBBBB", 16, 16, 16, 6, 0, 0, 0), bytes(64))})
    expect_error(lambda: MI.import_pack(src, root / "unused-out", False), "only 8-bit",
                 "a 16-bit <img>")

    # No hires.txt at all.
    empty = root / "empty-src"
    empty.mkdir(parents=True, exist_ok=True)
    expect_error(lambda: MI.open_pack(empty), "no hires.txt", "a folder without a manifest")


# --- the shape the tool cannot express --------------------------------------

SPLIT_LINES = [
    "<ver>100",
    "<scale>1",
    "<system>nes",
    "<condition>C1,tileAtPosition,8,8," + HEX_A + "," + PAL_A,
    "<condition>C2,frameRange,4,2",
    "<img>art.png",
    "<tile>0," + HEX_A + "," + PAL_A + ",0,0,1,N",
    "[C1]<tile>0," + HEX_A + "," + PAL_A + ",8,0,0.5,N",
    "[C2]<tile>0," + HEX_A + "," + PAL_A + ",16,0,1,Y",
]
SPLIT_FILES = {"art.png": cell_png(3, 1, 1)}

# The same shape without the unconditional rule: every crop is conditioned, so
# the rebuild must emit two rules and no bare twin at all.
SPLIT_NO_BARE_LINES = SPLIT_LINES[:6] + SPLIT_LINES[7:]


def test_split_pattern_round_trips(root: Path):
    """Contra80s' shape: one (tileData, palette) pair, three conditions, three
    crops — 592 of its 7836 patterns are drawn that way, and Super Mario Bros.
    430 of 2076. A single cell cannot carry it: build takes a pair's conditions
    from the key source as a whole and would draw all three from one crop. Each
    rule therefore gets its own `exactCondition` cell (ADR-0198 §1 over
    ADR-0197 §1), and the round-trip is exact — pixels included."""
    src, project, summary = imported_pack(root, "split", SPLIT_LINES, SPLIT_FILES)
    if [p for p, _c in summary["split"]] != [(HEX_A, PAL_A)]:
        fail(f"the import should name the split pattern, got {summary['split']}")
        return
    if summary["twins"]:
        fail(f"an exact cell emits the input's own rules, so no twin is due: {summary['twins']}")
        return
    sidecar = json.loads((project / "textures" / "sheets" / "art.json").read_text())
    cells = [(c.get("condition", ""), c.get("exactCondition") is True)
             for c in sidecar["cells"]]
    if sorted(cells) != [("", True), ("C1", True), ("C2", True)]:
        fail(f"each crop should be pinned to the one condition it carried, got {cells}")
        return
    if sidecar.get("conditions"):
        fail("the import must cite the pack's own <condition>, never redefine it in the sheet")
        return
    ok("a pattern drawn at three crops becomes three exactCondition cells citing the pack's own"
       " conditions")

    note = (project / "IMPORT.md").read_text(encoding="utf-8")
    if "exactCondition" not in note or "more than one crop" not in note:
        fail("IMPORT.md does not explain the cells a split pattern produced")
        return
    buf = io.StringIO()
    with contextlib.redirect_stdout(buf):
        rc = MI.main([str(src), "--out", str(root / "split-cli"), "--force"])
    if rc != 0 or "drawn at more than one crop" not in buf.getvalue():
        fail(f"the CLI did not report the split pattern (rc={rc}):\n{buf.getvalue()}")
        return
    ok("IMPORT.md and the CLI both report the split (the bare-path form of `import` too)")

    rc, out = run_build(project)
    if rc != 0:
        fail(f"build on the split-pattern project exited {rc}:\n{out}")
        return
    rc, out = run_verify(src, project, strict=True)
    if rc != 0 or "0 differ" not in out:
        fail(f"a split pattern must round-trip exactly, pixels included (rc={rc}):\n{out}")
        return
    ok("`verify --strict` passes: the rule set and every crop's pixels survive the rebuild")

    built = (project / "textures" / "hires.txt").read_text(encoding="utf-8")
    rules = sorted(ln for ln in built.splitlines() if "<tile>" in ln)
    if not any(ln.endswith(",0.5,N") for ln in rules) or not any(ln.endswith(",1,Y") for ln in rules):
        fail(f"each crop must keep its own brightness/defaultTile fields:\n{rules}")
        return
    ok("an exact cell keeps the trailing fields of the rule it came from, not the defaults")


def test_split_pattern_without_a_bare_rule(root: Path):
    """The other half of the shape: a pattern whose every rule is conditioned.
    ADR-0198 §1 asks for the input's rule set, so no unconditional twin is
    invented here — `verify --strict` is what proves it."""
    src, project, _summary = imported_pack(root, "split2", SPLIT_NO_BARE_LINES, SPLIT_FILES)
    rc, out = run_build(project)
    if rc != 0:
        fail(f"build exited {rc}:\n{out}")
        return
    rc, out = run_verify(src, project, strict=True)
    if rc != 0 or "0 ADR-0189 §3 twin(s) added by build" not in out:
        fail(f"a fully conditioned split pattern must round-trip with no twin (rc={rc}):\n{out}")
        return
    ok("a split pattern with no unconditional rule rebuilds to exactly its two conditioned rules")


# --- the CLI ----------------------------------------------------------------

def test_cli(root: Path):
    src = write_src(root / "cli", DATA_LINES, DATA_FILES)
    project = root / "cli-proj"
    buf = io.StringIO()
    with contextlib.redirect_stdout(buf):
        rc = MI.main(["import", str(src), "--out", str(project)])
    if rc != 0 or not (project / "IMPORT.md").is_file():
        fail(f"`mep_import.py import` exited {rc}:\n{buf.getvalue()}")
        return
    ok("`mep_import.py import <pack> --out <project>` writes the project")

    buf = io.StringIO()
    with contextlib.redirect_stderr(buf):
        rc = MI.main(["import", str(src), "--out", str(project)])
    if rc != 2 or "--force" not in buf.getvalue():
        fail(f"re-importing into a non-empty --out should exit 2 naming --force: {rc}\n{buf.getvalue()}")
        return
    ok("a second import without --force exits 2 naming --force")

    rc, out = run_build(project)
    if rc != 0:
        fail(f"build exited {rc}:\n{out}")
        return
    buf = io.StringIO()
    with contextlib.redirect_stdout(buf), contextlib.redirect_stderr(buf):
        rc = MI.main(["verify", str(src), str(project)])
    if rc != 0 or "OK:" not in buf.getvalue():
        fail(f"`mep_import.py verify` exited {rc}:\n{buf.getvalue()}")
        return
    ok("`mep_import.py verify <pack> <project>` passes on a rebuilt project")


# --- the index read (ADR-0210 §3) -------------------------------------------

PAL_C = "0F162A30"
PAL_D = "1F2F3F4F"


def shape_hex(shape: int) -> str:
    """A distinct 16-byte 2bpp pattern per shape id, as 32 uppercase hex — the
    form a CHR RAM key's `tileData` carries."""
    lo = bytes(((shape * 7 + r * 13) & 0xFF) for r in range(8))
    hi = bytes(((shape * 11 + r * 5) & 0xFF) for r in range(8))
    return (lo + hi).hex().upper()


def write_pack(root: Path, name: str, lines, files: dict = None) -> Path:
    """One pack folder the way a recording lays it out: `textures/hires.txt`
    plus whatever the manifest names, all of it under `textures/`."""
    pack = root / name
    textures = pack / "textures"
    (textures / "sheets").mkdir(parents=True, exist_ok=True)
    (textures / "hires.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
    for rel, data in (files or {}).items():
        p = textures / rel
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_bytes(data)
    return pack


def make_rom(root: Path, name: str, chr_banks: int, prg_banks: int = 1) -> Path:
    """A minimal, valid iNES file. Header byte 5 is what says whether the game
    is CHR ROM (`artist_chr_kit.Rom.has_chr_rom`): 0 -> CHR RAM, N -> N x 8 KB
    of CHR, i.e. N x 512 tiles."""
    rom = root / name
    rom.parent.mkdir(parents=True, exist_ok=True)
    # 4 magic bytes + the 12-byte rest of the iNES header, or the CHR slice
    # comes up short and the tile count with it (511 of 512).
    rom.write_bytes(b"NES\x1a" + bytes([prg_banks, chr_banks] + [0] * 10)
                    + bytes(prg_banks * 16384) + bytes(chr_banks * 8192))
    return rom


def flat(width: int, height: int, value: int = 0x20) -> "mep_build._Bitmap":
    return mep_build._Bitmap(width, height, 4, bytearray(bytes([value] * 4) * width * height))


def write_free_sheet(sheets: Path, stem: str, kind: str, keys, scale: int, columns: int = 2):
    """A contact sheet the recorder would have written: `columns` 8px cells on
    a gutterless grid, at `scale`, with the pixel-exact 1x `*.orig.png` twin
    `mep_build._EditedProbe` diffs against."""
    rows = (len(keys) + columns - 1) // columns
    lw, lh = columns * 8, rows * 8
    cells = [{"index": i, "x": (i % columns) * 8, "y": (i // columns) * 8, "count": 1,
              "tiles": [{"tile": t, "palette": p}]} for i, (t, p) in enumerate(keys)]
    (sheets / f"{stem}.json").write_text(json.dumps(
        {"version": 1, "kind": kind, "gridUnit": 8, "gutter": 0, "columns": columns,
         "cell": {"w": 8, "h": 8}, "sheet": f"{stem}.png", "reference": f"{stem}.orig.png",
         "cells": cells}, indent=1) + "\n", encoding="utf-8")
    mep_build._png_write(sheets / f"{stem}.orig.png", flat(lw, lh))
    mep_build._png_write(sheets / f"{stem}.png", flat(lw * scale, lh * scale))


def ram_recording(root: Path, name: str = "rec", scale: int = 2, shapes=(1, 2),
                  sheet_only=(3,)) -> Path:
    """A CHR RAM recording: a data-keyed manifest plus one F12.8 `unsorted`
    sheet, which the index read must leave byte for byte alone.

    `sheet_only` is the trap that matters most here: shapes the recording's
    sheet carries and its manifest does not (the bounded input has 251 of
    them). They are part of the recording all the same, and a shape the index
    sheet also claimed would be taken off the recorded sheet by the build."""
    pack = write_pack(root, name, ["<ver>109", f"<scale>{scale}"]
                      + [f"<tile>0,{shape_hex(s)},{PAL_C},0,0,1,N,0,0" for s in shapes])
    write_free_sheet(pack / "textures" / "sheets", "unsorted", "unsorted",
                     [(shape_hex(s), PAL_C) for s in tuple(shapes) + tuple(sheet_only)], scale)
    return pack


def index_pack(root: Path, name: str, lines, files: dict = None) -> Path:
    """Their pack: a flat legacy one (`hires.txt` beside its art, ADR-0005),
    holding no art of ours to open."""
    pack = root / name
    pack.mkdir(parents=True, exist_ok=True)
    (pack / "hires.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
    for rel, data in (files or {}).items():
        p = pack / rel
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_bytes(data)
    return pack


def run_index(their: Path, pack: Path, rom: Path, *extra):
    buf = io.StringIO()
    with contextlib.redirect_stdout(buf), contextlib.redirect_stderr(buf):
        rc = MI.main(["index", str(their), "--pack", str(pack), "--rom", str(rom), *extra])
    return rc, buf.getvalue()


def sheet_bytes(pack: Path) -> dict:
    return {p.name: p.read_bytes() for p in sorted((pack / "textures" / "sheets").iterdir())}


def test_index_render(root: Path):
    """The render itself: a key's own 16 bytes through the key's own palette,
    at the pack's scale."""
    table = MI._nes_palette()
    # Row 0 low plane 0x80 = one pixel of colour 1 at x=0; high plane 0x40 =
    # one pixel of colour 2 at x=1. Everything else is colour 0.
    data = bytes([0x80, 0, 0, 0, 0, 0, 0, 0, 0x40, 0, 0, 0, 0, 0, 0, 0])
    pal = [0x0F, 0x19, 0x29, 0x08]
    hex_pal = "".join(f"{c:02X}" for c in pal)
    block = MI.render_pattern(data.hex().upper(), hex_pal, 2)
    want_px = [table[pal[1]], table[pal[2]], table[pal[0]]]
    got_px = [tuple(block[i * 4:i * 4 + 4][:3]) for i in range(3)]
    if got_px != want_px or len(block) != (16 * 16) * 4:
        fail(f"render_pattern gave {got_px} / {len(block)} bytes, expected {want_px} / "
             f"{(16 * 16) * 4} (a 16x16 RGBA cell at scale 2)")
    else:
        ok("render_pattern unpacks 2bpp through the key's own palette at the pack's scale")

    flat_block = MI.render_pattern("00" * 16, hex_pal, 1)
    if len(flat_block) != 8 * 8 * 4 or any(flat_block[i:i + 4] != bytes(table[pal[0]]) + b"\xff"
                                          for i in range(0, len(flat_block), 4)):
        fail("a zero pattern did not render as one flat colour-0 cell")
    else:
        ok("a blank pattern renders as colour 0, opaque (a recorded cell's own convention)")

    # The key's palette decides the colours: the same bytes under another
    # palette is another picture.
    other = MI.render_pattern(data.hex().upper(), "00010203", 1)
    if other == MI.render_pattern(data.hex().upper(), hex_pal, 1):
        fail("two palettes rendered the same picture")
    else:
        ok("the key's palette word decides the cell's colours")


def test_index_chr_ram(root: Path):
    """A CHR RAM game: every shape our recording lacks is rendered, the ones
    it holds are not, and the sheet is what `mep_build` slices back."""
    root = root / "ram"
    pack = ram_recording(root)                       # shapes 1 and 2 are ours, 3 is ours too
    before = sheet_bytes(pack)
    their = index_pack(root, "their", [
        "<ver>100",
        "<scale>2",
        f"<tile>0,{shape_hex(2)},{PAL_C},0,0,1,N",   # in our manifest: no cell
        f"<tile>0,{shape_hex(3)},{PAL_C},0,0,1,N",   # only on our sheet: no cell either
        f"<tile>0,{shape_hex(5)},{PAL_C},0,0,1,N",
        f"<tile>0,{shape_hex(5)},{PAL_D},0,0,1,N",   # same shape, second palette: an alias
        f"<tile>0,{shape_hex(9)},{PAL_D},0,0,1,N",
    ], files={"notart.png": b"this is not a PNG at all"})
    rom = make_rom(root, "ram.nes", chr_banks=0)
    rc, out = run_index(their / "hires.txt", pack, rom)
    if rc != 0:
        fail(f"the index read exited {rc}:\n{out}")
        return
    doc = json.loads((pack / "textures" / "sheets" / "index.json").read_text())
    if [len(doc["cells"]), doc["kind"], doc["source"], doc["seen"], doc["origin"]["shapes"]] \
            != [2, "index", "index", False, 4]:
        fail(f"sidecar reads {len(doc['cells'])} cells, kind {doc['kind']!r}, "
             f"source {doc['source']!r}, seen {doc['seen']}, shapes {doc['origin']['shapes']}")
    else:
        ok("4 distinct shapes in theirs, 2 of them new: 2 cells, kind/source `index`, `seen: false`")

    if [c["tiles"][0]["tile"] for c in doc["cells"]] != [shape_hex(5), shape_hex(9)]:
        fail(f"the wrong shapes got cells: {[c['tiles'][0]['tile'][:8] for c in doc['cells']]}")
    else:
        ok("a shape our recording holds on a sheet (not in its manifest) is not re-rendered: the "
           "index sheet and the recorded sheets are disjoint by construction")

    if [c.get("seen") for c in doc["cells"]] != [False, False]:
        fail(f"cells carry seen {[c.get('seen') for c in doc['cells']]}")
    else:
        ok("every cell carries its own provenance: `index`, `seen: false`")

    aliased = [c for c in doc["cells"] if c.get("aliases")]
    if (len(aliased) != 1 or [c["tiles"][0]["palette"] for c in doc["cells"]] != [PAL_C, PAL_D]
            or aliased[0]["aliases"][0]["tiles"][0]["palette"] != PAL_D):
        fail(f"the second palette of a shape did not ride as an alias: "
             f"{json.dumps(doc['cells'])[:200]}")
    else:
        ok("a shape's other palettes ride as the cell's `aliases`, so no key of theirs is lost")

    # The pixels are the pack's own pattern bytes, at our recording's scale.
    scale = 2
    scaled = mep_build._png_pixels(pack / "textures" / "sheets" / "index.png")
    lw = 2 * 8
    if (scaled.width, scaled.height) != (lw * scale, 8 * scale):
        fail(f"index.png is {scaled.width}x{scaled.height}, expected {lw * scale}x{8 * scale}")
        return
    ok("index.png is the sheet at the recording's own <scale>")
    cell0 = doc["cells"][0]["tiles"][0]
    want = MI.render_pattern(cell0["tile"], cell0["palette"], scale)
    got = bytearray()
    for y in range(8 * scale):
        o = y * scaled.stride
        got += scaled.raw[o:o + 8 * scale * scaled.channels]
    if bytes(got) != want:
        fail("the cell in index.png is not render_pattern's own pixels")
    else:
        ok("each cell holds the shape rendered from its own 16 pattern bytes")

    twin = mep_build._png_pixels(pack / "textures" / "sheets" / "index.orig.png")
    if (twin.width, twin.height) != (lw, 8):
        fail(f"index.orig.png is {twin.width}x{twin.height}, expected the 1x {lw}x8 twin")
    else:
        ok("index.orig.png is the pixel-exact 1x twin the sheet is diffed against")

    if {p.name for p in (pack / "textures" / "sheets").iterdir() if p.name.startswith("index")} \
            != {"index.png", "index.orig.png", "index.json"}:
        fail("the index read wrote something other than the three sheet files")
    else:
        ok("the read writes index.png, index.orig.png and index.json and nothing else")

    # The whole point of the sheet: `build` slices it back into rules, and it
    # takes nothing off a recorded sheet to do it.
    rc, out = run_build(pack)
    if rc != 0 or "0 error(s)" not in out:
        fail(f"build exited {rc} on the pack carrying an index sheet:\n{out}")
        return
    ok("`mep_build.py build` accepts a pack carrying an index sheet, 0 errors")
    if "to sheets/index.png" in out:
        fail(f"the index sheet took a key off a recorded sheet:\n{out}")
    else:
        ok("the build moves no key of a recorded sheet onto the index sheet")
    built = (pack / "textures" / "hires.txt").read_text(encoding="utf-8")
    rows = {}
    for line in built.splitlines():
        if line.startswith("<tile>"):
            f = [x.strip() for x in line[6:].split(",")]
            rows[(f[1].upper(), f[2].upper())] = line
    want_keys = {(shape_hex(5), PAL_C), (shape_hex(5), PAL_D), (shape_hex(9), PAL_D)}
    if set(rows) & want_keys != want_keys:
        fail(f"the rebuild is missing index keys: {sorted(want_keys - set(rows))}")
    elif any(line.startswith("[") for line in rows.values()):
        fail("an index key was emitted with a condition prefix")
    else:
        ok("every index key is in the rebuilt manifest as a bare <tile> rule")

    # F12.8's remainder sheet holds recorded keys and the index sheet holds
    # keys the recording lacks: disjoint by construction, so the first is
    # byte for byte what it was.
    after = sheet_bytes(pack)
    if after["unsorted.png"] != before["unsorted.png"] \
            or after["unsorted.orig.png"] != before["unsorted.orig.png"] \
            or after["unsorted.json"] != before["unsorted.json"]:
        fail("the F12.8 unsorted sheet changed during the index read")
    else:
        ok("the F12.8 `unsorted` sheet is byte for byte unchanged (disjoint by construction)")


def test_index_conditions(root: Path):
    """Filter 3: `<condition>` lines are never read, at any coverage cost."""
    root = root / "cond"
    pack = ram_recording(root)
    their = index_pack(root, "their", [
        "<ver>105",
        "<scale>2",
        # A condition this toolchain would have had to *invent* to import
        # (ADR-0183 §3), and a rule gated on it. The tile is a fact; the gate
        # is the other author's reading of the machine.
        "<condition>alwaysOn,memoryCheck,3,4,5,6",
        "[alwaysOn]<tile>0,%s,%s,0,0,1,N" % (shape_hex(5), PAL_C),
        "<tile>0,%s,%s,0,0,1,N" % (shape_hex(7), PAL_C),
    ])
    rom = make_rom(root, "ram.nes", chr_banks=0)
    plan = MI.index_run(their / "hires.txt", pack, rom, None, False)
    if plan["conditioned_rules"] != 1 or len(plan["cells"]) != 2:
        fail(f"the conditioned rule was not read as a bare key: {plan['conditioned_rules']} "
             f"conditioned rule(s), {len(plan['cells'])} cell(s)")
        return
    doc = json.loads((pack / "textures" / "sheets" / "index.json").read_text()) \
        if (pack / "textures" / "sheets" / "index.json").is_file() else None
    if doc is None:
        fail("no sidecar was written")
    elif any(k in ("condition", "exactCondition", "conditions") for c in doc["cells"] for k in c) \
            or "conditions" in doc:
        fail(f"a condition reached the sidecar: {json.dumps(doc)[:300]}")
    elif [c["tiles"][0]["tile"] for c in doc["cells"]] != [shape_hex(5), shape_hex(7)]:
        fail(f"the conditioned tile's shape was dropped instead of imported bare: "
             f"{[c['tiles'][0]['tile'] for c in doc['cells']]}")
    else:
        ok("a conditioned rule contributes the bare key of the tile it names; no condition is "
           "read, written or cited")

    rc, out = run_build(pack)
    built = (pack / "textures" / "hires.txt").read_text(encoding="utf-8")
    if rc != 0 or "<condition>" in built or "[alwaysOn]" in built:
        fail(f"a condition reached the rebuilt manifest (build exited {rc})")
    else:
        ok("the rebuild carries no <condition> line and no condition prefix for those keys")


def test_index_patch(root: Path):
    """Filter 2: a pack carrying `<patch>` keys a namespace we never meet."""
    root = root / "patch"
    pack = ram_recording(root)
    their = index_pack(root, "their", [
        "<ver>100",
        "<scale>2",
        "<patch>chr-ram-to-rom.ips," + "0" * 40,
        f"<tile>0,{shape_hex(5)},{PAL_C},0,0,1,N",
    ])
    rom = make_rom(root, "ram.nes", chr_banks=0)
    expect_error(lambda: MI.read_index(their / "hires.txt", pack, rom), "ADR-0198",
                 "an index read of a pack carrying <patch>")
    rc, out = run_index(their / "hires.txt", pack, rom)
    if rc != 2 or not (pack / "textures" / "sheets").is_dir() \
            or [p.name for p in (pack / "textures" / "sheets").iterdir() if "index" in p.name]:
        fail(f"the refused read exited {rc} and/or wrote a sheet:\n{out}")
    else:
        ok("the refusal exits 2 and writes nothing into the recording")


def test_index_range(root: Path):
    """Filter 1: the index range against the loaded CHR, read the loader's own
    way — `<ver>` decides whether that field is decimal or hex."""
    root = root / "range"
    rom = make_rom(root, "chrrom.nes", chr_banks=1)      # 8 KB CHR = 512 tiles
    pack = write_pack(root, "rec", ["<ver>109", "<scale>2", "<tile>0,00,0F162A30,0,0,1,N"])
    their = index_pack(root, "their", [
        "<ver>100",                                       # decimal, per ReadTileData
        "<scale>2",
        "<tile>0,1,FF072235,0,0,1,N",
        "<tile>0,511,FF072235,8,0,1,N",
        "<tile>0,512,11073325,0,0,1,N",                   # past the CHR
        "<tile>0,7000,20192233,0,0,1,N",                  # far past it
    ])
    plan = MI.read_index(their / "hires.txt", pack, rom)
    if (plan["shapes"], plan["cells"], plan["in_range_rules"]) != (0, [], 2):
        fail(f"a CHR ROM read planned {plan['shapes']} shape(s), {len(plan['cells'])} cell(s), "
             f"{plan['in_range_rules']} in-range rule(s)")
    elif sorted(plan["palettes"]) != ["FF072235"]:
        fail(f"the palette set took {plan['palettes']} — an out-of-range key's palette is a "
             "claim about another binary")
    elif (plan["dropped"]["out_of_range_rules"], plan["dropped"]["out_of_range_indices"]) != (2, 2):
        fail(f"dropped {plan['dropped']}, expected 2 rules over 2 tile indices")
    else:
        ok("a CHR ROM read takes palettes from in-range keys only and drops the rest by index")

    if plan["chr_tiles"] != 512 or plan["game"] != "chr_rom":
        fail(f"the dump read as {plan['game']} with {plan['chr_tiles']} tiles")
    else:
        ok("the range is the loaded dump's own CHR tile count (8 KB of CHR = 512 tiles)")

    rc, out = run_index(their / "hires.txt", pack, rom)
    if rc != 0 or "palette set is taken" not in out or "dropped as out of range: 2 rule(s)" not in out:
        fail(f"the CHR ROM run did not say what it did (rc {rc}):\n{out}")
    else:
        ok("the CHR ROM run says so: shapes discarded, the palette set taken, what was dropped")
    if [p.name for p in (pack / "textures" / "sheets").iterdir() if "index" in p.name]:
        fail("a CHR ROM run wrote a sheet")
    else:
        ok("a CHR ROM run adds no shape and writes no sheet")

    # The trap this filter is read through: `int(field, 16)` is not a
    # normalisation, it is a reading. The same token, two `<ver>`s.
    dec = write_pack(root, "dec", ["<ver>102", "<scale>2", "<tile>0,200,FF072235,0,0,1,N"])
    hexp = write_pack(root, "hexp", ["<ver>103", "<scale>2", "<tile>0,200,FF072235,0,0,1,N"])
    if (MI.read_index(dec / "textures" / "hires.txt", pack, rom)["palettes"],
            MI.read_index(hexp / "textures" / "hires.txt", pack, rom)["palettes"]) \
            != (["FF072235"], []):
        fail("<ver> did not decide how the index field is read")
    else:
        ok("`200` is tile 200 below <ver>103 (in range) and 0x200 = 512 at 103+ (dropped): the "
           "loader's own reading, not a hex normalisation")


def test_index_opens_no_png(root: Path):
    """Not one PNG of the input pack is opened — the shape comes from the key,
    the art never does. A trap on the decoder says so, and the fixture's `<img>`
    files are made unreadable to make the point twice."""
    root = root / "trap"
    pack = ram_recording(root)
    their = index_pack(root, "their", [
        "<ver>100",
        "<scale>2",
        "<img>ghost.png",                       # does not exist at all
        "<img>notapng.png",                     # exists, and is not a PNG
        f"<tile>0,{shape_hex(5)},{PAL_C},0,0,1,N",
    ], files={"notapng.png": b"\x00\x01\x02 not a PNG"})
    rom = make_rom(root, "ram.nes", chr_banks=0)
    real_read_png = MI.read_png

    def trap(*_a, **_k):
        raise AssertionError("the index read opened a PNG of the input pack")

    MI.read_png = trap
    try:
        plan = MI.read_index(their / "hires.txt", pack, rom)
    except AssertionError as e:
        fail(str(e))
        return
    finally:
        MI.read_png = real_read_png
    if plan["shapes"] != 1:
        fail(f"the read planned {plan['shapes']} shape(s) with no PNG readable")
    else:
        ok("the read never opens a PNG of the input pack (trapped decoder), and does not need "
           "its art to exist")


def test_index_refusals(root: Path):
    """What an index read refuses, and why: the wrong dump, a pack and a
    recording whose namespaces are not the same game."""
    root = root / "refuse"
    ram = make_rom(root, "ram.nes", chr_banks=0)
    chrrom = make_rom(root, "chrrom.nes", chr_banks=1)
    pack = ram_recording(root)
    their = index_pack(root, "their", ["<ver>100", "<scale>2",
                                       f"<tile>0,{shape_hex(5)},{PAL_C},0,0,1,N"])

    junk = root / "junk.nes"
    junk.write_bytes(b"not a rom")
    expect_error(lambda: MI.read_index(their / "hires.txt", pack, junk), "iNES",
                 "an index read against a file that is not an iNES ROM")

    # The recording and the dump must be the same binary (ADR-0210).
    stamped = write_pack(root, "stamped", ["<ver>109", "<scale>2",
                                           "<supportedRom>" + "AB" * 20,
                                           f"<tile>0,{shape_hex(1)},{PAL_C},0,0,1,N"])
    expect_error(lambda: MI.read_index(their / "hires.txt", stamped, ram), "not the dump",
                 "a dump whose SHA1 is not the recording's <supportedRom>")

    empty = write_pack(root, "empty", ["<ver>109", "<scale>2"])
    expect_error(lambda: MI.read_index(their / "hires.txt", empty, ram), "no <tile> entries",
                 "a recording with no tiles to compare against")

    # A CHR ROM dump with a data-keyed recording: the two are not one game.
    expect_error(lambda: MI.read_index(their / "hires.txt", pack, chrrom), "not the same game",
                 "a CHR ROM dump against a data-keyed recording")

    # Index-keyed recording vs data-keyed pack: the namespaces never meet
    # (ADR-0198 §2/§3 — a patched-ROM pack is the usual reason).
    idx_rec = write_pack(root, "idxrec", ["<ver>109", "<scale>2", "<tile>0,00,0F162A30,0,0,1,N"])
    expect_error(lambda: MI.read_index(their / "hires.txt", idx_rec, chrrom), "never meet",
                 "a data-keyed pack against an index-keyed recording")


def test_index_cli(root: Path):
    """The command line the slice names, plus the guard that keeps a painted
    index sheet from being overwritten."""
    root = root / "cli"
    pack = ram_recording(root)
    their = index_pack(root, "their", ["<ver>100", "<scale>2",
                                       f"<tile>0,{shape_hex(5)},{PAL_C},0,0,1,N"])
    rom = make_rom(root, "ram.nes", chr_banks=0)
    report = root / "index-report.json"
    rc, out = run_index(their, pack, rom, "--report", str(report))
    if rc != 0 or "index " not in out or "1 cell(s) rendered" not in out:
        fail(f"`mep_import.py index <folder> --pack <pack> --rom <rom>` exited {rc}:\n{out}")
        return
    ok("`mep_import.py index <their pack> --pack <ours> --rom <dump>` reads either form of path")
    doc = json.loads(report.read_text()) if report.is_file() else None
    if doc is None or doc["shapes"] != 1 or doc["game"] != "chr_ram" or doc["keys"] != 1:
        fail(f"the --report file reads {doc and {k: doc[k] for k in ('shapes', 'game', 'keys')}}")
    else:
        ok("--report writes the run's own JSON: shapes, keys, game, what was dropped")

    buf = io.StringIO()
    with contextlib.redirect_stderr(buf):
        rc = MI.main(["index", str(their), "--pack", str(pack), "--rom", str(rom)])
    if rc != 2 or "--force" not in buf.getvalue():
        fail(f"a second read without --force should exit 2 naming --force: {rc}\n{buf.getvalue()}")
    else:
        ok("a second read refuses to overwrite the index sheet without --force")

    rc, out = run_index(their, pack, rom, "--force")
    if rc != 0:
        fail(f"`--force` did not let the sheet be rewritten: {rc}\n{out}")
    else:
        ok("--force rewrites the index sheet")


# --- the patched-ROM import (ADR-0198 §3, option (a)) -----------------------

def fake_rom(prg_units=1, chr_units=0) -> bytes:
    """A deterministic iNES file: 16-byte header, no trainer, `prg_units` 16
    KiB PRG banks and `chr_units` 8 KiB CHR banks of a byte pattern."""
    header = b"NES\x1a" + bytes((prg_units, chr_units)) + bytes(10)
    body = bytes((i * 7 + 3) & 0xFF for i in range(prg_units * 0x4000 + chr_units * 0x2000))
    return header + body


def ips_record(address, data=b"", rle=None):
    rec = address.to_bytes(3, "big")
    if rle is not None:
        run, value = rle
        return rec + (0).to_bytes(2, "big") + run.to_bytes(2, "big") + bytes((value,))
    return rec + len(data).to_bytes(2, "big") + data


def ips_file(*records, truncate=None) -> bytes:
    out = b"PATCH" + b"".join(records) + b"EOF"
    if truncate is not None:
        out += truncate.to_bytes(3, "big")
    return out


def sha1_hex(data: bytes) -> str:
    import hashlib
    return hashlib.sha1(data).hexdigest().upper()  # noqa: S324 - the loader's own key form


# The hard case of ADR-0198 §3: a CHR RAM cartridge whose IPS declares one 8
# KiB CHR ROM bank (header byte 5) and appends it, plus a small PRG write.
STOCK_ROM = fake_rom(prg_units=1, chr_units=0)
CHR_ROM_IPS = ips_file(
    ips_record(5, b"\x01"),
    ips_record(100, b"MEP!"),
    ips_record(16 + 0x4000, rle=(0x2000, 0xAA)),
)
PATCHED_ROM = (STOCK_ROM[:5] + b"\x01" + STOCK_ROM[6:100] + b"MEP!" + STOCK_ROM[104:]
               + b"\xAA" * 0x2000)


def patched_lines(sha1: str, supported: str | None = None, keyed="index"):
    lines = ["<ver>103", "<scale>1"]
    if supported:
        lines.append(f"<supportedRom>{supported}")
    lines.append("<img>chr.png")
    if keyed == "index":
        lines += ["<tile>0,5," + PAL_A + ",0,0,1,N", "<tile>0,6," + PAL_B + ",8,0,1,N"]
    else:
        lines += ["<tile>0," + HEX_A + "," + PAL_A + ",0,0,1,N",
                  "<tile>0," + HEX_B + "," + PAL_B + ",8,0,1,N"]
    lines.append(f"<patch>fix.ips,{sha1}")
    return lines


def test_apply_ips():
    """`mep_patch.apply_ips` against `IpsPatcher::PatchBuffer`'s rules."""
    patched, records = MP.apply_ips(STOCK_ROM, CHR_ROM_IPS)
    if patched != PATCHED_ROM or records != 3:
        fail(f"apply_ips: {len(patched)} bytes / {records} records, expected "
             f"{len(PATCHED_ROM)} / 3")
    else:
        ok("apply_ips grows the ROM to the furthest write and expands an RLE record")
    if MP.chr_units(STOCK_ROM) != 0 or MP.chr_units(patched) != 1:
        fail(f"chr_units: {MP.chr_units(STOCK_ROM)} -> {MP.chr_units(patched)}")
    else:
        ok("chr_units reads header byte 5 before and after the patch")
    # Stream order, not address order: the later record wins; and the
    # truncate offset after EOF cuts the output.
    ips = ips_file(ips_record(20, b"\x11\x11"), ips_record(19, b"\x22\x22"), truncate=24)
    out, n = MP.apply_ips(bytes(32), ips)
    if out != bytes(19) + b"\x22\x22\x11" + bytes(2) or n != 2:
        fail(f"stream order/truncate: {out.hex()} ({n} records)")
    else:
        ok("apply_ips applies records in stream order and honours the truncate offset")
    for what, blob, needle in (
            ("a BPS", b"BPS1" + bytes(20), "not an IPS"),
            ("a truncated record", b"PATCH" + ips_record(0, b"ab")[:4], "truncated"),
            ("no EOF", b"PATCH" + ips_record(0, b"ab"), "truncated")):
        try:
            MP.apply_ips(STOCK_ROM, blob)
            fail(f"apply_ips accepted {what}")
        except MP.PatchError as e:
            if needle in str(e):
                ok(f"apply_ips refuses {what}, naming {needle!r}")
            else:
                fail(f"apply_ips refused {what} with the wrong reason: {e}")
    # No-Intro over bytes: header and trailing junk excluded, clamped to the
    # declared PRG+CHR (ADR-0044); the whole-file hash sees both.
    junk = STOCK_ROM + bytes(11)
    if MP.no_intro_sha1(junk, ".nes") != MP.no_intro_sha1(STOCK_ROM, ".nes"):
        fail("no_intro_sha1 changed with trailing junk")
    elif MP.whole_file_sha1(junk) == MP.whole_file_sha1(STOCK_ROM):
        fail("whole_file_sha1 ignored trailing junk")
    else:
        ok("no_intro_sha1 clamps to the declared payload while whole_file_sha1 sees the file")


def test_patched_rom(root: Path):
    root = root / "patched"
    rom = root / "Game.nes"
    root.mkdir(parents=True, exist_ok=True)
    rom.write_bytes(STOCK_ROM)
    stock_whole = sha1_hex(STOCK_ROM)
    patched_whole = sha1_hex(PATCHED_ROM)
    files = {"chr.png": cell_png(2, 1, 1), "fix.ips": CHR_ROM_IPS}

    # Without --rom the pack is refused, naming the section and the flag.
    src = write_src(root / "norom", patched_lines(stock_whole, stock_whole), files)
    expect_error(lambda: MI.import_pack(src, root / "norom-out", False), "--rom",
                 "a <patch> pack without --rom")

    # With the stock dump: imported against the patched ROM.
    src = write_src(root / "ok", patched_lines(stock_whole, stock_whole), files)
    project = root / "ok-proj"
    summary = MI.import_pack(src, project, False, rom)
    p = summary["patch"]
    if p is None or p["matched_by"] != "whole-file" or p["matched_line"] != 7:
        fail(f"patch summary: {p}")
        return
    if (p["stock_whole"], p["patched_whole"]) != (stock_whole, patched_whole):
        fail(f"hashes: {p['stock_whole']} / {p['patched_whole']}")
        return
    if not p["adds_chr_rom"] or p["patched_size"] != len(PATCHED_ROM) or p["records"] != 3:
        fail(f"patch facts: {p}")
        return
    ok("a <patch> pack imports against the patched ROM: hashes, size and CHR growth measured")

    key_source = (project / "auto" / "textures" / "hires.txt").read_text(encoding="utf-8")
    sup = [ln for ln in key_source.splitlines() if ln.startswith("<supportedRom>")]
    if sup != [f"<supportedRom>{patched_whole}"]:
        fail(f"key source <supportedRom>: {sup}")
        return
    if f"<patch>fix.ips,{stock_whole}" not in key_source.splitlines():
        fail("the <patch> line was not carried verbatim into the key source")
        return
    if not (project / "auto" / "textures" / "fix.ips").is_file() \
            or not (project / "textures" / "fix.ips").is_file():
        fail("the IPS is not beside both manifests")
        return
    note = (project / "IMPORT.md").read_text(encoding="utf-8")
    if "does not buy" not in note or "patched ROM's key namespace" not in note \
            or "never meet" not in note:
        fail("IMPORT.md does not state the namespace limit")
        return
    if "never meet" not in " ".join(p["note"]):
        fail(f"the CHR RAM -> CHR ROM wording is missing from the printed note: {p['note']}")
        return
    ok("<supportedRom> is the patched hash, the <patch> line and the IPS ride along, the "
       "limit is written")

    rc, out = run_build(project)
    if rc != 0:
        fail(f"build exited {rc}:\n{out}")
        return
    rc, out = run_verify(src, project, strict=True)
    if rc != 0 or "patch: 1 <patch> line(s) carried" not in out \
            or "IPS beside the built manifest: yes" not in out:
        fail(f"verify --strict on a patched-ROM project:\n{out}")
        return
    built = (project / "textures" / "hires.txt").read_text(encoding="utf-8").splitlines()
    if f"<supportedRom>{patched_whole}" not in built or f"<patch>fix.ips,{stock_whole}" not in built:
        fail("the built manifest lost the <supportedRom> or the <patch> line")
        return
    ok("build + verify --strict round-trip a patched-ROM project, IPS and hashes included")

    # PR #385 review: a well-formed but wrong <supportedRom> in the built
    # manifest must fail against the hash the import recorded.
    built_path = project / "textures" / "hires.txt"
    wrong = "0" * 40
    built_path.write_text("\n".join(f"<supportedRom>{wrong}" if ln.startswith("<supportedRom>")
                                     else ln for ln in built) + "\n", encoding="utf-8")
    rc, out = run_verify(src, project)
    if rc != 1 or "is not the patched ROM the import computed" not in out or patched_whole not in out:
        fail(f"verify should fail on a valid-looking but wrong <supportedRom>: {rc}\n{out}")
    else:
        ok("verify fails when the built <supportedRom> is not the patched hash the import recorded")
    built_path.write_text("\n".join(built) + "\n", encoding="utf-8")

    (project / "textures" / "fix.ips").unlink()
    rc, out = run_verify(src, project)
    if rc != 1 or "<patch> file missing" not in out:
        fail(f"verify should fail once the IPS is gone from textures/: {rc}\n{out}")
    else:
        ok("verify fails when the IPS is no longer beside the built manifest")

    # Hash selection: a <patch> keyed by the No-Intro sha1 matches too, in
    # the loader's own second-lookup order.
    stock_no_intro = MP.no_intro_sha1(STOCK_ROM, ".nes")
    src = write_src(root / "nointro", patched_lines(stock_no_intro), files)
    summary = MI.import_pack(src, root / "nointro-proj", False, rom)
    if summary["patch"]["matched_by"] != "No-Intro" or summary["patch"]["declared_supported_rom"]:
        fail(f"No-Intro match: {summary['patch']['matched_by']}")
    else:
        key_source = (root / "nointro-proj" / "auto" / "textures" / "hires.txt").read_text(encoding="utf-8")
        if key_source.splitlines()[2] != f"<supportedRom>{patched_whole}":
            fail(f"a pack without <supportedRom> should get one after <scale>: "
                 f"{key_source.splitlines()[:4]}")
        else:
            ok("a <patch> keyed by the No-Intro sha1 matches, and a missing <supportedRom> is "
               "inserted after <scale>")

    # A data-keyed pack with a code patch: imported, with the softer wording.
    src = write_src(root / "data", patched_lines(stock_whole, keyed="data"), files)
    summary = MI.import_pack(src, root / "data-proj", False, rom)
    if "16 pattern bytes" not in " ".join(summary["patch"]["note"]):
        fail(f"data-keyed wording: {summary['patch']['note']}")
    else:
        ok("a data-keyed <patch> pack imports with the 16-pattern-bytes limit wording")

    # Refusals, each naming what it refused.
    other = "F" * 40
    for needle, what, lines, extra in (
            ("ADR-0145", "an IPS made for another dump", patched_lines(other), {}),
            ("not in", "a <patch> file that is not in the pack",
             patched_lines(stock_whole), {"fix.ips": None}),
            ("not an IPS", "a <patch> file that is a BPS",
             patched_lines(stock_whole), {"fix.ips": b"BPS1" + bytes(20)}),
            ("truncated", "a truncated IPS",
             patched_lines(stock_whole), {"fix.ips": CHR_ROM_IPS[:-5]}),
            ("ADR-0211", "a <supportedRom> that contradicts every hash",
             patched_lines(stock_whole, other), {}),
            ("needs file,sha1", "a <patch> without a sha1",
             patched_lines(stock_whole)[:-1] + ["<patch>fix.ips"], {})):
        fs = dict(files)
        for k, v in extra.items():
            if v is None:
                fs.pop(k)
            else:
                fs[k] = v
        src = write_src(root / ("refuse-" + needle.replace(" ", "-").replace(",", "")), lines, fs)
        expect_error(lambda s=src: MI.import_pack(s, root / "unused", False, rom), needle, what)

    # A declared <supportedRom> equal to the patched hash, or to another
    # <patch> target, is not a contradiction (ADR-0211 rules 5-6).
    for label, declared in (("the patched hash", patched_whole), ("a <patch> target", other)):
        lines = patched_lines(stock_whole, declared) + [f"<patch>fix.ips,{other}"]
        src = write_src(root / ("declared-" + label.replace(" ", "-")), lines, files)
        try:
            MI.import_pack(src, root / ("declared-proj-" + label.replace(" ", "-")), False, rom)
            ok(f"a <supportedRom> equal to {label} is accepted")
        except MI.PackError as e:
            fail(f"a <supportedRom> equal to {label} was refused: {e}")

    # --rom on a plain pack is a note, not an error.
    src = write_src(root / "plain", DATA_LINES, DATA_FILES)
    buf = io.StringIO()
    with contextlib.redirect_stderr(buf):
        summary = MI.import_pack(src, root / "plain-proj", False, rom)
    if summary["patch"] is not None or "was not needed" not in buf.getvalue():
        fail(f"--rom on a plain pack: {summary['patch']} / {buf.getvalue()}")
    else:
        ok("--rom on a pack without <patch> is noted and otherwise ignored")

    # The CLI prints the limit on every such import.
    src = write_src(root / "cli", patched_lines(stock_whole, stock_whole), files)
    buf = io.StringIO()
    with contextlib.redirect_stdout(buf), contextlib.redirect_stderr(buf):
        rc = MI.main(["import", str(src), "--out", str(root / "cli-proj"), "--rom", str(rom)])
    out = buf.getvalue()
    if rc != 0 or "what this does not buy" not in out or patched_whole not in out:
        fail(f"CLI --rom: {rc}\n{out}")
    else:
        ok("`mep_import.py <pack> --out <project> --rom <dump>` prints the hashes and the limit")


def test_patch_hardening(root: Path):
    """PR #385 review (Codex): a `<patch>` name is data the tool must not
    trust — no traversal out of the pack or the project (ADR-0006), the
    loader's own token rules (a comma in the name never registers), and the
    loader's last-wins for a repeated sha1 (`HdPackLoader::ProcessPatchTag`)."""
    root = root / "hardening"
    root.mkdir(parents=True, exist_ok=True)
    rom = root / "Game.nes"
    rom.write_bytes(STOCK_ROM)
    stock_whole = sha1_hex(STOCK_ROM)
    files = {"chr.png": cell_png(2, 1, 1), "fix.ips": CHR_ROM_IPS}

    # Traversal: the payload really exists one level above the pack, so only
    # the path rule (not a missing file) can refuse it. Nothing is written,
    # the payload is untouched, and the message names the manifest line (6).
    payload = root / "payload.ips"
    payload.write_bytes(b"do not touch")
    for label, name in (("../", "../payload.ips"),
                        ("backslash", "..\\payload.ips"),
                        ("dot-slash-dot-dot", "./sub/../../payload.ips"),
                        ("absolute", str(payload)),
                        ("drive letter", "C:\\payload.ips")):
        lines = patched_lines(stock_whole)[:-1] + [f"<patch>{name},{stock_whole}"]
        src = write_src(root / ("trav-" + label.replace("/", "").replace(" ", "-")), lines, files)
        out = root / ("trav-out-" + label.replace("/", "").replace(" ", "-"))
        try:
            MI.import_pack(src, out, False, rom)
        except MI.PackError as e:
            msg = str(e)
            if "line 6" not in msg or "ADR-0006" not in msg:
                fail(f"traversal {label}: refused, but without the line and ADR-0006: {msg}")
                continue
        else:
            fail(f"traversal {label}: <patch>{name} was imported")
            continue
        if out.exists() or payload.read_bytes() != b"do not touch":
            fail(f"traversal {label}: something was written (out exists: {out.exists()})")
            continue
        ok(f"a <patch> path that escapes the pack ({label}) is refused naming line 6, "
           "nothing written")

    # A destination that escapes the project: `--out` reused with `--force`
    # whose `textures/` is a symlink to somewhere else. The source side is
    # clean, so only the destination check can catch it — before any write.
    outside = root / "outside"
    outside.mkdir()
    out = root / "symlinked-out"
    (out / "textures").parent.mkdir(parents=True, exist_ok=True)
    (out / "textures").symlink_to(outside, target_is_directory=True)
    (out / "keep.txt").write_text("x", encoding="utf-8")
    src = write_src(root / "symlinked", patched_lines(stock_whole), files)
    expect_error(lambda: MI.import_pack(src, out, True, rom), "outside",
                 "a <patch> destination that resolves outside its layer")
    if list(outside.iterdir()) or (out / "auto").exists():
        fail("the symlinked destination received a write")
    else:
        ok("the destination check runs before any write")

    # The loader splits on every comma: `<patch>foo,bar.ips,<sha1>` is three
    # tokens, tokens[1] is 'bar.ips', and the IPS is never registered.
    files_comma = dict(files)
    files_comma["foo,bar.ips"] = CHR_ROM_IPS
    lines = patched_lines(stock_whole)[:-1] + [f"<patch>foo,bar.ips,{stock_whole}"]
    src = write_src(root / "comma", lines, files_comma)
    out = root / "comma-out"
    expect_error(lambda: MI.import_pack(src, out, False, rom), "every comma",
                 "a <patch> file name containing a comma")
    if out.exists():
        fail("comma: something was written")
    else:
        ok("a comma <patch> name refuses with nothing written")
    lines_lower = patched_lines(stock_whole)[:-1] + [f"<patch>fix.ips,{stock_whole.lower()}"]
    src = write_src(root / "lowercase", lines_lower, files)
    summary = MI.import_pack(src, root / "lowercase-out", False, rom)
    if summary["patch"]["matched_line"] != 6:
        fail(f"a lowercase sha1 (the loader uppercases) did not match: {summary['patch']}")
    else:
        ok("a lowercase sha1 matches, as the loader uppercases tokens[1]")

    # Duplicate sha1: the loader assigns PatchesByHash[sha1] per line, so the
    # last line wins. b.ips carries one more record than a.ips; the patched
    # hash must be b's.
    other_ips = CHR_ROM_IPS[:-3] + ips_record(200, b"LAST") + b"EOF"
    patched_b = bytearray(PATCHED_ROM)
    patched_b[200:204] = b"LAST"
    files_dup = {"chr.png": cell_png(2, 1, 1), "a.ips": CHR_ROM_IPS, "b.ips": other_ips}
    lines = patched_lines(stock_whole)[:-1] + [f"<patch>a.ips,{stock_whole}",
                                               f"<patch>b.ips,{stock_whole}"]
    src = write_src(root / "dup", lines, files_dup)
    project = root / "dup-out"
    p = MI.import_pack(src, project, False, rom)["patch"]
    if (p["matched_file"], p["matched_line"], p["entries"], p["files"]) != ("b.ips", 7, 2, 2):
        fail(f"duplicate sha1: expected the last line (b.ips, line 7) to win: {p}")
    elif p["patched_whole"] != sha1_hex(bytes(patched_b)) or p["records"] != 4:
        fail(f"duplicate sha1: the patched hash is not b.ips's: {p}")
    elif not (project / "textures" / "a.ips").is_file() \
            or not (project / "textures" / "b.ips").is_file():
        fail("duplicate sha1: both IPS files must still be carried (both lines are)")
    else:
        ok("a repeated <patch> sha1 picks the last line, as HdPackLoader::ProcessPatchTag does")


def test_patch_path_normalization(root: Path):
    """PR #385 review, second and fourth rounds (Codex): the emitted `<patch>`
    token is the normalized `/` path the IPS is copied to — the canonical
    spelling every repo tool resolves — and `verify` looks the file up the
    way the loader does. `HdPackLoader::LoadPack` rewrites `\\` to `/` on
    every manifest line before parsing (commit 9615330b), so a built `\\`
    token whose file sits at the `/` path is runtime-valid on every host and
    must pass; the second round's claim that it "never resolves on
    macOS/Linux" was wrong. Only a token that resolves to no file fails."""
    root = root / "pathnorm"
    root.mkdir(parents=True, exist_ok=True)
    rom = root / "Game.nes"
    rom.write_bytes(STOCK_ROM)
    stock_whole = sha1_hex(STOCK_ROM)
    files = {"chr.png": cell_png(2, 1, 1), "sub/fix.ips": CHR_ROM_IPS}
    lines = patched_lines(stock_whole)[:-1] + [f"<patch>sub\\fix.ips,{stock_whole}"]
    src = write_src(root / "win", lines, files)
    project = root / "win-proj"
    summary = MI.import_pack(src, project, False, rom)
    p = summary["patch"]
    if (p["matched_file"], p["matched_rel"]) != ("sub\\fix.ips", "sub/fix.ips"):
        fail(f"a backslash <patch> name: {p['matched_file']!r} / {p['matched_rel']!r}")
        return
    good, bad = f"<patch>sub/fix.ips,{stock_whole}", f"<patch>sub\\fix.ips,{stock_whole}"
    key_source = (project / "auto" / "textures" / "hires.txt").read_text(encoding="utf-8").splitlines()
    if good not in key_source or bad in key_source:
        fail(f"the key source's <patch> token was not normalized: "
             f"{[ln for ln in key_source if ln.startswith('<patch>')]}")
        return
    if not (project / "auto" / "textures" / "sub" / "fix.ips").is_file() \
            or not (project / "textures" / "sub" / "fix.ips").is_file():
        fail("the IPS did not land at the normalized path beside both manifests")
        return
    note = (project / "IMPORT.md").read_text(encoding="utf-8")
    if "`sub/fix.ips`" not in note:
        fail("IMPORT.md does not name the normalized path the IPS was copied to")
        return
    ok("a Windows `sub\\fix.ips` <patch> is emitted as `sub/fix.ips`, the path the IPS lands at")

    rc, out = run_build(project)
    if rc != 0:
        fail(f"build exited {rc}:\n{out}")
        return
    built_path = project / "textures" / "hires.txt"
    built = built_path.read_text(encoding="utf-8").splitlines()
    if good not in built or bad in built:
        fail(f"build did not carry the normalized <patch> line: "
             f"{[ln for ln in built if ln.startswith('<patch>')]}")
        return
    rc, out = run_verify(src, project)
    if rc != 0 or "IPS beside the built manifest: yes" not in out \
            or "carried line missing" in out:
        fail(f"verify on a normalized nested <patch> path:\n{out}")
        return
    ok("build carries `<patch>sub/fix.ips` and verify passes it against the source's `\\` line")

    # A built `\` token whose file sits at the `/` path: the loader rewrites
    # `\` to `/` before parsing, so this loads on every host and verify must
    # pass it (fourth round; the second round refused it on a wrong premise).
    built_path.write_text("\n".join(bad if ln == good else ln for ln in built) + "\n",
                          encoding="utf-8")
    rc, out = run_verify(src, project)
    if rc != 0 or "IPS beside the built manifest: yes" not in out:
        fail(f"verify should pass a `\\` <patch> token whose file is at the `/` path (the loader "
             f"rewrites `\\` to `/`): {rc}\n{out}")
    else:
        ok("verify passes a built `\\` <patch> token whose file sits at the `/` path (loader-equivalent lookup)")

    # A token whose file is elsewhere is the plain missing-file case.
    built_path.write_text("\n".join(built) + "\n", encoding="utf-8")
    (project / "textures" / "sub" / "fix.ips").rename(project / "textures" / "fix.ips")
    rc, out = run_verify(src, project)
    if rc != 1 or "<patch> file missing beside textures/hires.txt" not in out \
            or "IPS beside the built manifest: NO" not in out:
        fail(f"verify should report a missing file: {rc}\n{out}")
    else:
        ok("a <patch> token whose file moved is reported as missing beside textures/hires.txt")
    (project / "textures" / "fix.ips").rename(project / "textures" / "sub" / "fix.ips")

    # PR #385 review (Codex): existence is not enough — the IPS bytes beside
    # the built manifest and under auto/textures/ must be the source pack's.
    built_ips = project / "textures" / "sub" / "fix.ips"
    auto_ips = project / "auto" / "textures" / "sub" / "fix.ips"
    original = built_ips.read_bytes()
    built_ips.write_bytes(original[:-3])
    rc, out = run_verify(src, project)
    if rc != 1 or "IPS bytes differ from the source pack's: textures/sub/fix.ips" not in out \
            or "IPS beside the built manifest: NO" not in out:
        fail(f"verify should fail on a truncated IPS beside textures/hires.txt: {rc}\n{out}")
    else:
        ok("verify fails when the IPS beside textures/hires.txt is truncated")
    built_ips.write_bytes(original)

    altered = bytearray(original)
    altered[6] ^= 0xFF
    auto_ips.write_bytes(bytes(altered))
    rc, out = run_verify(src, project)
    if rc != 1 or "IPS bytes differ from the source pack's: auto/textures/sub/fix.ips" not in out \
            or "textures/sub/fix.ips" not in out or "IPS beside the built manifest: NO" not in out:
        fail(f"verify should fail on an altered auto/textures IPS: {rc}\n{out}")
    else:
        ok("verify fails when the auto/textures/ IPS differs from the source pack's")
    auto_ips.unlink()
    rc, out = run_verify(src, project)
    if rc != 1 or "<patch> file missing in auto/textures: sub/fix.ips" not in out:
        fail(f"verify should fail when the auto/textures IPS is missing: {rc}\n{out}")
    else:
        ok("verify fails when the auto/textures/ IPS is missing")
    auto_ips.write_bytes(original)

    rc, out = run_verify(src, project)
    if rc != 0 or "IPS beside the built manifest: yes" not in out:
        fail(f"verify should pass again once both IPS copies are intact: {rc}\n{out}")
    else:
        ok("verify passes once both IPS copies are byte-identical to the source pack's again")


def test_patch_case_fold(root: Path):
    """PR #385 review, third round (Codex): `HdPackLoader::
    ResolvePackRelativePath` falls back to a case-insensitive lookup, so a
    Windows-authored pack whose manifest says `sub/fix.ips` beside
    `SUB/Fix.IPS` loads on a case-sensitive host. The import finds the same
    file and writes it under the emitted name. On a case-insensitive file
    system (APFS default) the exact lookup already succeeds; the fold path is
    exercised on Linux CI."""
    root = root / "casefold"
    root.mkdir(parents=True, exist_ok=True)
    rom = root / "Game.nes"
    rom.write_bytes(STOCK_ROM)
    stock_whole = sha1_hex(STOCK_ROM)
    files = {"chr.png": cell_png(2, 1, 1), "SUB/Fix.IPS": CHR_ROM_IPS}
    lines = patched_lines(stock_whole)[:-1] + [f"<patch>sub/fix.ips,{stock_whole}"]
    src = write_src(root / "src", lines, files)
    project = root / "proj"
    summary = MI.import_pack(src, project, False, rom)
    p = summary["patch"]
    if p is None or p["matched_rel"] != "sub/fix.ips" or p["files"] != 1:
        fail(f"a case-folded <patch> file was not resolved: {p}")
        return
    for layer in (project / "auto" / "textures", project / "textures"):
        names = [f.relative_to(layer).as_posix() for f in layer.rglob("*") if f.suffix.lower() == ".ips"]
        if names != ["sub/fix.ips"] or (layer / "sub" / "fix.ips").read_bytes() != CHR_ROM_IPS:
            fail(f"the IPS did not land under the emitted name in {layer}: {names}")
            return
    ok("a <patch> naming `sub/fix.ips` beside `SUB/Fix.IPS` resolves like the loader and lands as `sub/fix.ips`")

    # PR #385 review: a patch named like a generated asset is refused before
    # anything is written, instead of overwriting the sheet/image with IPS bytes.
    # (A patch spelled like the pack's own `<img>` or `hires.txt` cannot be
    # staged here: on a case-insensitive file system it *is* that file. The
    # same rule covers it, by name, in `_generated_layer_paths`.)
    # A file/folder prefix clash is the same refusal: a patch `sheets` is a
    # file where the import creates a folder. (An `<img>`/patch prefix clash
    # cannot exist inside one source folder; the check covers it by name.)
    for name in ("sheets/chr.png", "Sheets/Fix.ips", "sheets"):
        tag = name.replace("/", "_").replace(".", "_")
        lines = patched_lines(stock_whole)[:-1] + [f"<patch>{name},{stock_whole}"]
        src = write_src(root / f"clash-{tag}", lines, {"chr.png": cell_png(2, 1, 1), name: CHR_ROM_IPS})
        dst = root / f"clash-{tag}-out"
        expect_error(lambda: MI.import_pack(src, dst, False, rom), "collides with",
                     f"a <patch> named {name!r}")
        if dst.exists() and any(dst.iterdir()):
            fail(f"a colliding <patch> {name!r} left files behind")
    # PR #385 review: the loader dispatches `<patch>` only at column 0 (or
    # right after a `[condition]` it strips), so an indented line is inert at
    # runtime and a conditioned one is unconditional — both refused.
    for label, line, needle in (("indented", f"  <patch>fix.ips,{stock_whole}", "indented <patch>"),
                                ("conditioned", f"[c]<patch>fix.ips,{stock_whole}", "[condition]")):
        lines = patched_lines(stock_whole)[:-1] + [line]
        srcx = write_src(root / f"col-{label}", lines, {"chr.png": cell_png(2, 1, 1), "fix.ips": CHR_ROM_IPS})
        dst = root / f"col-{label}-out"
        expect_error(lambda: MI.import_pack(srcx, dst, False, rom), needle, f"a {label} <patch> line")
        if dst.exists() and any(dst.iterdir()):
            fail(f"a {label} <patch> line left files behind")

    files, dirs = MI._generated_layer_paths(MI.open_pack(src))
    if not {"hires.txt", "chr.png"} <= files or dirs != ("sheets/",):
        fail(f"_generated_layer_paths misses a generated name: {sorted(files)} {dirs}")
    else:
        ok("_generated_layer_paths reserves hires.txt, every <img> and textures/sheets/")

    probe = root / "probe"
    probe.mkdir()
    (probe / "a.ips").write_bytes(b"x")
    if (probe / "A.IPS").exists():
        ok("case-insensitive file system: the ambiguous-fold refusal is exercised on Linux CI")
        return
    (probe / "A.IPS").write_bytes(b"y")
    if MP.loader_source(probe, "a.Ips") is not None:
        fail("loader_source guessed between two files that fold to the same name")
    else:
        ok("loader_source refuses to guess between two files that fold to the same name")


def main():
    with tempfile.TemporaryDirectory(prefix="test-mep-import-") as tmp:
        root = Path(tmp)
        test_png_codec(root)
        test_data_keyed(root)
        test_index_keyed(root)
        test_refusals(root)
        test_split_pattern_round_trips(root)
        test_split_pattern_without_a_bare_rule(root)
        test_cli(root)
        test_index_render(root)
        test_index_chr_ram(root)
        test_index_conditions(root)
        test_index_patch(root)
        test_index_range(root)
        test_index_opens_no_png(root)
        test_index_refusals(root)
        test_index_cli(root)
        test_apply_ips()
        test_patched_rom(root)
        test_patch_hardening(root)
        test_patch_path_normalization(root)
        test_patch_case_fold(root)
    if FAILED:
        print("\nFAILURES")
        sys.exit(1)
    print("\nall checks passed")


if __name__ == "__main__":
    main()
