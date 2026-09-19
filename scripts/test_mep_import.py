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
  * refusals, each naming what it refused: `<patch>` (ADR-0198 §2), an
    unknown tag, a `[...]` prefix on a tag that takes none, a `<tile>` with
    too few fields, a malformed tileData/palette, a bitmap index out of
    range, a missing `<img>` PNG, a non-integer `<scale>`, a non-empty
    `--out` without `--force`, and — for an index-keyed pack — a construct
    the 103 raise would re-read (`frameRange` at 101, a multi-field
    `<background>`, a 6-field `memoryCheck`);
  * the shape a legacy pack animates with: one `(tileData, palette)` pair
    drawn at several crops, one per condition. Each rule gets its own
    `exactCondition` cell, so the rebuild reproduces the input's rules and
    its pixels — with and without an unconditional rule in the pair;
  * the CLI: the bare-path form, `verify`, and `--force`.

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
        ("ADR-0198", "a <patch>", base + ["<patch>fix.ips"], None),
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
    if FAILED:
        print("\nFAILURES")
        sys.exit(1)
    print("\nall checks passed")


if __name__ == "__main__":
    main()
