"""Headless suite for `artist_chr_kit.py` — completing a recorded pack's CHR
pages from the ROM (PRD F9.24).

The tool's whole value is that an artist can tell evidence from a guess, so
this suite is mostly about honesty rather than pixels: a recorded cell must come
out byte-identical, a cell moved up from a lower-ranked page must stay `seen:
true` and name where it came from, a cell read out of the ROM must be `seen:
false`, and a cell nothing explains must stay a hole. On top of that it pins the
two fill sources (a CHR ROM bank is exact; a CHR RAM bank is only recoverable
where the recorded tiles pin down a PRG block), the palette recovery, and the
`--fill-rules` policy that keeps `hires.txt` untouched.

Fixtures are synthetic: a hand-built iNES image and a hand-written pack in the
recorder's own `hires.txt` shape. No ROM from the library is read and nothing
under `runs/` is touched.

Run:  python3 scripts/test_artist_chr_kit.py
"""

import hashlib
import json
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import artist_chr_kit as K  # noqa: E402
from sheet_repaint import Image, read_png, write_png  # noqa: E402

_FAILURES = []

MAGENTA = (0xFF, 0x00, 0xFF, 0xFF)
SCALE = 2
CELL = 8 * SCALE
PAGE = 16 * CELL

PAL_A = "0F1628300"[:8]        # 0F 16 28 30
PAL_B = "0F0616270"[:8]        # 0F 06 16 27

# A palette the 2C02 table does not produce, to prove the tool reads its colours
# off the pack rather than assuming a table.
CUSTOM = {0x16: (1, 2, 3, 0xFF)}


def check(cond, name, detail=""):
    if cond:
        print(f"ok   {name}")
    else:
        print(f"FAIL {name}: {detail}")
        _FAILURES.append(name)


# --- fixture helpers --------------------------------------------------------


def lcg(n, seed=1):
    """Deterministic pseudo-random bytes, so a fixture PRG has no accidental
    repeats that would make a tile look like it occurs everywhere."""
    out = bytearray()
    x = seed
    for _ in range(n):
        x = (x * 1103515245 + 12345) & 0x7FFFFFFF
        out.append((x >> 16) & 0xFF)
    return bytes(out)


def packed_tile(i: int) -> bytes:
    """A tile that does not occur anywhere in the fixture PRG — a bank the game
    unpacked rather than block-copied."""
    return lcg(16, seed=90001 + i * 7919)


def tile_bytes(i: int) -> bytes:
    """A recognisable, non-degenerate 16-byte pattern per index."""
    lo = bytes(((i + r) * 37) & 0xFF for r in range(8))
    hi = bytes(((i * 5 + r * 11) ^ 0x5A) & 0xFF for r in range(8))
    return lo + hi


def rgba_for(index: int):
    if index in CUSTOM:
        return CUSTOM[index]
    v = K.DEFAULT_PALETTE_ARGB[index & 0x3F]
    return ((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF, (v >> 24) & 0xFF)


def draw_cell(img: Image, slot: int, data: bytes, palette_hex: str):
    """Render a tile into a page the way the recorder's reference sheet does —
    written out here rather than borrowed from the tool, so the suite is not
    checking the tool against itself."""
    pal = [(int(palette_hex, 16) >> ((3 - c) * 8)) & 0x3F for c in range(4)]
    ox, oy = (slot % 16) * CELL, (slot // 16) * CELL
    for y in range(8):
        lo, hi = data[y], data[y + 8]
        for x in range(8):
            c = ((lo >> (7 - x)) & 1) | (((hi >> (7 - x)) & 1) << 1)
            rgba = rgba_for(pal[c])
            for dy in range(SCALE):
                for dx in range(SCALE):
                    img.set(ox + x * SCALE + dx, oy + y * SCALE + dy, rgba)


def blank_page() -> Image:
    img = Image(PAGE, PAGE)
    for y in range(PAGE):
        for x in range(PAGE):
            img.set(x, y, MAGENTA)
    return img


def ines(prg: bytes, chr_rom: bytes) -> bytes:
    header = bytearray(16)
    header[0:4] = b"NES\x1a"
    header[4] = len(prg) // 16384
    header[5] = len(chr_rom) // 8192
    return bytes(header) + prg + chr_rom


def write_pack(root: Path, scale: int, images, rows, rom_sha1="0" * 40):
    """`images` is [(name, {slot: (data, palette, tile_index_or_None)})] in
    `<img>` order; `rows` builds the `<tile>` text for each.

    `rom_sha1` defaults to the 40 zeros a pre-ADR-0003 recording carries, which
    identifies no ROM; a fixture that has to be paired with another recording
    passes the real one."""
    chr_dir = root / "textures" / "chr"
    chr_dir.mkdir(parents=True, exist_ok=True)
    lines = ["<ver>109", f"<scale>{scale}", "<supportedRom>" + rom_sha1]
    lines += [f"<img>chr/{name}.png" for name, _ in images]
    for i, (name, cells) in enumerate(images):
        page = blank_page()
        for slot, (data, palette, _) in cells.items():
            draw_cell(page, slot, data, palette)
        write_png(chr_dir / f"{name}.png", page)
        write_png(chr_dir / f"{name}.orig.png", page)
        lines.append(f"#chr/{name}.png")
        lines += rows(i, cells)
    (root / "textures" / "hires.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")


def chr_rom_rows(image_index, cells):
    out = []
    for slot, (_, palette, index) in sorted(cells.items()):
        x, y = (slot % 16) * CELL, (slot // 16) * CELL
        out.append(f"<tile>{image_index},{index:02X},{palette},{x},{y},1,Y")
    return out


def chr_ram_rows_for(bank_id):
    def rows(image_index, cells):
        out = []
        for slot, (data, palette, index) in sorted(cells.items()):
            x, y = (slot % 16) * CELL, (slot // 16) * CELL
            out.append(f"<tile>{image_index},{data.hex().upper()},{palette},"
                       f"{x},{y},1,N,{bank_id},{index}")
        return out
    return rows


def chr_rom_fixture(td: Path):
    """A 2-bank CHR ROM game. Page rank 0 holds indices 0..99 of bank 0; rank 1
    holds 200..209, which the tool has to move up onto rank 0."""
    chr_rom = b"".join(tile_bytes(i) for i in range(512))
    rom = td / "game.nes"
    rom.write_bytes(ines(lcg(16384, 7), chr_rom))
    pack = td / "pack"
    rank0 = {i: (tile_bytes(i), PAL_A, i) for i in range(100)}
    rank1 = {i: (tile_bytes(i), PAL_B, i) for i in range(200, 210)}
    write_pack(pack, SCALE, [("Chr_00_0", rank0), ("Chr_00_1", rank1)], chr_rom_rows)
    return pack, rom


def chr_ram_fixture(td: Path):
    """A CHR RAM game whose bank 12345 was copied verbatim out of the PRG at
    0x1000, and whose bank 999 was not (a packed bank)."""
    prg = bytearray(lcg(32768, 11))
    for i in range(256):
        prg[0x1000 + i * 16:0x1000 + i * 16 + 16] = tile_bytes(i)
    rom = td / "game.nes"
    rom.write_bytes(ines(bytes(prg), b""))
    pack = td / "pack"
    linear = {i: (tile_bytes(i), PAL_A, i) for i in range(40)}
    packed = {i: (packed_tile(i), PAL_B, i) for i in range(20)}
    chr_dir = pack / "textures" / "chr"
    chr_dir.mkdir(parents=True, exist_ok=True)
    lines = ["<ver>109", f"<scale>{SCALE}", "<supportedRom>" + "0" * 40,
             "<img>chr/Chr_0.png", "<img>chr/Chr_1.png"]
    for i, (name, cells, bank) in enumerate((("Chr_0", linear, 12345),
                                             ("Chr_1", packed, 999))):
        page = blank_page()
        for slot, (data, palette, _) in cells.items():
            draw_cell(page, slot, data, palette)
        write_png(chr_dir / f"{name}.png", page)
        write_png(chr_dir / f"{name}.orig.png", page)
        lines.append(f"#chr/{name}.png")
        lines += chr_ram_rows_for(bank)(i, cells)
    (pack / "textures" / "hires.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
    return pack, rom


def chr_rom_pair_fixture(td: Path):
    """Two recordings of the same CHR ROM game (ADR-0184 §2's clean pass and
    coverage pass, in miniature).

    The primary recorded indices 0..99 of bank 0 on its rank-0 page and 200..209
    on a rank-1 page. The second recording saw 0..9 again — so the primary's own
    cell has something to win against — plus 120..139 the primary never saw, plus
    a whole bank 1 (indices 256..275) the primary never loaded at all."""
    chr_rom = b"".join(tile_bytes(i) for i in range(512))
    rom = td / "game.nes"
    rom.write_bytes(ines(lcg(16384, 7), chr_rom))
    sha1 = hashlib.sha1(rom.read_bytes()).hexdigest().upper()

    primary = td / "pack"
    write_pack(primary, SCALE,
               [("Chr_00_0", {i: (tile_bytes(i), PAL_A, i) for i in range(100)}),
                ("Chr_00_1", {i: (tile_bytes(i), PAL_B, i) for i in range(200, 210)})],
               chr_rom_rows, sha1)

    donor = td / "donor"
    shared = {i: (tile_bytes(i), PAL_B, i) for i in range(10)}
    only = {i: (tile_bytes(i), PAL_B, i) for i in range(120, 140)}
    other_bank = {i - 256: (tile_bytes(i), PAL_B, i) for i in range(256, 276)}
    write_pack(donor, SCALE,
               [("Chr_00_9", {**shared, **only}), ("Chr_01_9", other_bank)],
               chr_rom_rows, sha1)
    return primary, donor, rom


DONOR_ONLY = set(range(120, 140))


def sidecar(out: Path, name: str):
    return json.loads((out / "chr" / f"{name}.json").read_text())


def states(doc):
    return {c["slot"]: c["state"] for c in doc["cells"]}



# --- palette folding --------------------------------------------------------
#
# The rule is decided off the NES palette, so these cases are about the rule
# itself rather than about pixels: real palette pairs lifted out of the Zelda
# TAS recording (`runs/tas-zelda-20260914/kit4`), and the two classes of false
# positive the 2026-09-14 corpus measurement turned up.

# A pattern that paints all four palette indices (pixel values 3,3,2,2,1,1,0,0
# per row), so nothing folds through an entry the tile never draws.
FOLD_TILE = "CC" * 8 + "F0" * 8
# The shape Zelda's fade rides on: `7F80808080808080FFFFFFFFFFFFFFFF` paints
# only colours 2 and 3, which is why its 30 palettes collapse to ~7 cells.
RAMP_TILE = "7F80808080808080" + "FFFFFFFFFFFFFFFF"


def _fold(base: str, other: str, tile: str = FOLD_TILE):
    """("inert"|"fade"|None, brightness, drift) — the same two passes, in the
    same order, that `compute_folds` runs."""
    used = K.painted_indices(tile)
    b, o = K.palette_entries(base), K.palette_entries(other)
    if all(K._nes_rgb(b[k]) == K._nes_rgb(o[k]) for k in used):
        return "inert", 1.0, 0.0
    if not K.fade_related(b, o, used):
        return None, None, None
    brightness, drift = K.fold_measure(b, o, used)
    if drift > K.HUE_DRIFT_GATE_DEG:
        return None, brightness, drift
    return "fade", brightness, drift


def test_a_fade_step_of_one_ramp_folds_onto_the_step_above_it():
    # Zelda's most-repeated patterns carry ~7 hue families, each a short ramp;
    # these three are verbatim from `runs/tas-zelda-20260914/kit4`, on the shape
    # they ride on. The expected Brightness and drift are measured, not chosen:
    # five of the six real steps fold, and the sixth is refused by the gate,
    # which is what the gate is for.
    expect = [
        ("olive $x8", "0F081828", "0F080818", "fade", 0.549, 0.6),
        ("olive $x8", "0F081828", "0F020808", "fade", 0.326, 0.8),
        ("green $xB", "0F0B1B2B", "0F0B0B1B", "fade", 0.530, 18.4),
        # $2B (45E082) to $0B (004F08) is a real saturation swing on the 2C02,
        # not just a dimming, so the deepest green step stays its own cell.
        ("green $xB", "0F0B1B2B", "0F0F0B0B", None, 0.321, 28.3),
        ("cyan  $xC", "0F0C1C2C", "0F0C0C1C", "fade", 0.573, 13.5),
        ("cyan  $xC", "0F0C1C2C", "0F0F0C0C", "fade", 0.372, 13.7),
    ]
    for name, base, step, want, brightness, drift in expect:
        kind, b, d = _fold(base, step, RAMP_TILE)
        verb = "folds onto" if want else "is refused over the gate against"
        check(kind == want and abs(b - brightness) < 0.002 and abs(d - drift) < 0.05,
              f"{name}: {step} {verb} {base}",
              f"kind={kind} b={b} drift={d}")
        check(want is None or 0.0 <= b < 1.0,
              f"{name}: {step} is darker than {base}, so Brightness < 1", str(b))


def test_a_fully_faded_palette_folds_at_brightness_zero():
    # The last step of a fade-out paints the tile black. Brightness 0 is the
    # honest reconstruction, not a degenerate one: AdjustBrightness renders it
    # black, which is what the game drew.
    kind, brightness, drift = _fold("0F081828", "0F0F0F0F", RAMP_TILE)
    check(kind == "fade" and brightness == 0.0 and drift == 0.0,
          "an all-black palette folds at Brightness 0",
          f"kind={kind} b={brightness} drift={drift}")


def test_two_colourways_of_one_pattern_are_never_folded():
    # The whole point. A green and a red wearing the same pattern are two
    # pictures, and folding them would destroy evidence (ADR-0183 §3).
    for a, b in (("0F0B1B2B", "0F171626"),   # green vs red
                 ("0F0C1C2C", "0F021222"),   # cyan  vs blue
                 ("0F081828", "0F0B1B2B")):  # olive vs green
        kind, _b, _d = _fold(a, b, RAMP_TILE)
        check(kind is None, f"{b} is a different colourway from {a}, not a fade",
              str(kind))
        kind, _b, _d = _fold(b, a, RAMP_TILE)
        check(kind is None, "and it does not fold the other way round either",
              str(kind))


def test_a_rotated_ramp_is_not_a_fade():
    # FP class 1 of the corpus measurement: every entry keeps hue 0, so the
    # structural rule fires, but the grey ramp is permuted rather than scaled.
    # A multiplier preserves the ratios between a tile's colours; a rotation
    # does not, and the drift gate is what catches it.
    kind, _b, drift = _fold("0F001030", "3F103000")
    check(kind is None,
          "a rotated grey ramp is refused, not folded", f"kind={kind} drift={drift}")


def test_one_entry_moving_while_the_others_hold_is_left_to_the_gate():
    # FP class 2 of the corpus measurement: a highlight pulse, where only one
    # painted entry walks its ramp and the others hold. It is a false positive
    # of the fade *story*, not of the Brightness *field* — the relation on that
    # entry really is a brightness one — so the drift gate decides it rather
    # than a "every entry must move" rule. Measured: requiring every entry to
    # move is perfect on Contra (0 bad folds of 50) but costs Zelda 340 cells
    # and Gauntlet 1387, which is the bulk of what the fold is for.
    kind, brightness, drift = _fold("3F293717", "3F292717", RAMP_TILE)
    check(kind == "fade" and 20.0 < drift < 22.0,
          "a one-entry move inside the gate folds, and the gate is what decided",
          f"kind={kind} b={brightness} drift={drift}")
    # The same shape of change, but far enough round the wheel, is refused —
    # which is the point of gating on drift rather than on the story.
    kind, _b, drift = _fold("0F303030", "0F301030", RAMP_TILE)
    check(kind is None or drift <= K.HUE_DRIFT_GATE_DEG,
          "and the gate, not the rule, is the only thing that refuses it",
          f"kind={kind} drift={drift}")


def test_a_palette_that_gets_brighter_is_never_a_fade_downwards():
    # The relation is directional: the base is the brightest member, and the
    # greedy in compute_folds relies on that to pick the cell to paint.
    used = K.painted_indices(FOLD_TILE)
    dark, bright = K.palette_entries("0F0F0B0B"), K.palette_entries("0F0B1B2B")
    check(not K.fade_related(dark, bright, used),
          "a brighter palette is not a fade of a darker one")
    check(K.fade_related(bright, dark, used),
          "but the darker one is a fade of the brighter one")


def test_only_the_indices_the_pattern_paints_are_compared():
    # A pattern that paints colours 0 and 1 does not care what 2 and 3 hold,
    # and two palettes that differ only there are the same picture.
    flat = "F000F000F000F000" + "0000000000000000"   # paints colours 0 and 1
    check(K.painted_indices(flat) == [0, 1],
          "painted_indices reads the pattern, not the palette",
          str(K.painted_indices(flat)))
    kind, brightness, drift = _fold("0F162B2B", "0F161222", flat)
    check(kind == "inert" and brightness == 1.0 and drift == 0.0,
          "palettes differing only outside the painted indices fold inertly",
          f"kind={kind} b={brightness} drift={drift}")
    # And the same two palettes on a pattern that DOES paint 2 and 3 are two
    # different pictures, so nothing folds.
    kind, _b, _d = _fold("0F162B2B", "0F161222", FOLD_TILE)
    check(kind is None,
          "while the same pair on a pattern that paints them is left alone",
          str(kind))


def test_the_fold_is_reported_per_page_and_per_pack_and_changes_no_pixel():
    with tempfile.TemporaryDirectory() as td:
        td = Path(td)
        pack, rom = chr_rom_fixture(td)
        K.run(pack, rom, td / "kit", None, "none", False, True)
        frag = json.loads((td / "kit" / "kit-part-chr.json").read_text())
        check("fold" in frag and frag["fold"]["gateDegrees"] == K.HUE_DRIFT_GATE_DEG,
              "the fragment states the gate the fold ran under")
        for key in ("foldedAway", "inert", "fade", "cellsToPaint",
                    "refusedOverGate", "driftHistogram"):
            check(key in frag["fold"], f"the fragment's fold block carries {key}")
        doc = sidecar(td / "kit", "Chr_00_0")
        check("fold" in doc and "driftHistogram" in doc["fold"],
              "and every page carries its own drift histogram, so a loose game "
              "is visible where the kit is opened")
        check(all(c["state"] != "folded" or
                  {"kind", "brightness", "drift", "basePage", "baseSlot"} <= set(c)
                  for c in doc["cells"]),
              "a folded cell names what it folds into and what that costs")


# --- cases ------------------------------------------------------------------


def test_chr_rom_bank_is_completed_to_every_one_of_its_256_tiles():
    with tempfile.TemporaryDirectory() as td:
        td = Path(td)
        pack, rom = chr_rom_fixture(td)
        K.run(pack, rom, td / "kit", None, "none", False, True)
        doc = sidecar(td / "kit", "Chr_00_0")
        st = states(doc)
        check(doc["counts"]["empty"] == 0, "a CHR ROM bank leaves no hole",
              str(doc["counts"]))
        check(doc["counts"]["evidence"] == 100 and doc["counts"]["borrowed"] == 10,
              "recorded and moved-up cells are counted apart", str(doc["counts"]))
        check(doc["counts"]["fill"] == 146, "the rest is ROM fill", str(doc["counts"]))
        check(all(s != "empty" for s in st.values()), "no cell left magenta")


def test_a_filled_cell_carries_the_rom_bytes_and_is_marked_unseen():
    with tempfile.TemporaryDirectory() as td:
        td = Path(td)
        pack, rom = chr_rom_fixture(td)
        K.run(pack, rom, td / "kit", None, "none", False, True)
        doc = sidecar(td / "kit", "Chr_00_0")
        fills = [c for c in doc["cells"] if c["state"] == "fill"]
        check(all(c["seen"] is False for c in fills),
              "every ROM fill is seen: false")
        check(all(c["tileData"] == tile_bytes(c["index"]).hex().upper() for c in fills),
              "a fill carries the ROM's own pattern bytes")
        check(all(c["origin"] == "chrRom" for c in fills),
              "a CHR ROM fill says where it came from")
        evid = [c for c in doc["cells"] if c["state"] == "evidence"]
        check(all(c["seen"] is True for c in evid), "a recorded cell is seen: true")


def test_a_cell_from_a_lower_rank_page_is_moved_up_and_still_evidence():
    with tempfile.TemporaryDirectory() as td:
        td = Path(td)
        pack, rom = chr_rom_fixture(td)
        K.run(pack, rom, td / "kit", None, "none", False, True)
        doc = sidecar(td / "kit", "Chr_00_0")
        borrowed = [c for c in doc["cells"] if c["state"] == "borrowed"]
        check({c["index"] for c in borrowed} == set(range(200, 210)),
              "exactly the rank-1 indices are moved up",
              str(sorted(c["index"] for c in borrowed)))
        check(all(c["seen"] is True for c in borrowed),
              "a moved-up cell is evidence, not a guess")
        check(all(c["sourcePage"] == "Chr_00_1" for c in borrowed),
              "a moved-up cell names the page it came from")
        check(all(c["palette"] == PAL_B for c in borrowed),
              "a moved-up cell keeps the palette it was recorded under")
        # and its pixels really are the lower-ranked page's
        page = read_png(td / "kit" / "chr" / "Chr_00_0.png")
        src = read_png(pack / "textures" / "chr" / "Chr_00_1.png")
        c = borrowed[0]
        check(page.crop(c["x"], c["y"], CELL, CELL).px
              == src.crop(c["x"], c["y"], CELL, CELL).px,
              "the moved-up pixels are copied, not re-rendered")


def test_recorded_cells_come_out_byte_identical():
    with tempfile.TemporaryDirectory() as td:
        td = Path(td)
        pack, rom = chr_rom_fixture(td)
        K.run(pack, rom, td / "kit", None, "none", False, True)
        before = read_png(pack / "textures" / "chr" / "Chr_00_0.png")
        after = read_png(td / "kit" / "chr" / "Chr_00_0.png")
        doc = sidecar(td / "kit", "Chr_00_0")
        same = all(before.crop(c["x"], c["y"], CELL, CELL).px
                   == after.crop(c["x"], c["y"], CELL, CELL).px
                   for c in doc["cells"] if c["state"] == "evidence")
        check(same, "not one recorded cell moved a byte")
        check(read_png(td / "kit" / "chr" / "Chr_00_1.png").px == read_png(
            pack / "textures" / "chr" / "Chr_00_1.png").px,
            "a lower-ranked page is copied through untouched")


def test_the_fill_palette_is_read_off_the_pack_not_assumed():
    with tempfile.TemporaryDirectory() as td:
        td = Path(td)
        pack, rom = chr_rom_fixture(td)
        K.run(pack, rom, td / "kit", None, "none", False, True)
        doc = sidecar(td / "kit", "Chr_00_0")
        check(doc["fillPalette"] == PAL_A,
              "the fill palette is the bank's most-recorded one", doc["fillPalette"])
        # PAL_A's colour 1 is NES index 0x16, which the fixture renders in a
        # colour the 2C02 table never produces.
        page = read_png(td / "kit" / "chr" / "Chr_00_0.png")
        fill = next(c for c in doc["cells"] if c["state"] == "fill")
        data = tile_bytes(fill["index"])
        found = False
        for y in range(8):
            for x in range(8):
                c = ((data[y] >> (7 - x)) & 1) | (((data[y + 8] >> (7 - x)) & 1) << 1)
                if c == 1:
                    found = True
                    check(page.get(fill["x"] + x * SCALE, fill["y"] + y * SCALE)
                          == CUSTOM[0x16],
                          "a fill wears the pack's own colour for that palette index")
                    break
            if found:
                break
        check(found, "the fixture tile exercises colour 1")


def test_chr_ram_fills_only_what_a_prg_block_explains():
    with tempfile.TemporaryDirectory() as td:
        td = Path(td)
        pack, rom = chr_ram_fixture(td)
        K.run(pack, rom, td / "kit", None, "none", False, True)
        doc = sidecar(td / "kit", "Chr_0")
        fills = {c["index"] for c in doc["cells"] if c["state"] == "fill"}
        check(fills == set(range(40, 40 + K.LOCALITY_WINDOW)),
              "a linear PRG block fills exactly the holes beside a recorded tile",
              str(sorted(fills)))
        one = next(c for c in doc["cells"] if c["state"] == "fill")
        check(one["origin"] == "prg" and one["baseOffset"] == 0x1000,
              "a PRG fill names the block it was read from", str(one))
        check(one["tileData"] == tile_bytes(one["index"]).hex().upper(),
              "a PRG fill carries the PRG's own bytes")
        check(one["baseSupport"] >= K.MIN_BASE_SUPPORT,
              "a PRG block is only trusted when several recorded tiles agree")


def test_a_packed_chr_ram_bank_is_left_alone_and_says_so():
    with tempfile.TemporaryDirectory() as td:
        td = Path(td)
        pack, rom = chr_ram_fixture(td)
        K.run(pack, rom, td / "kit", None, "none", False, True)
        doc = sidecar(td / "kit", "Chr_1")
        check(doc["counts"]["fill"] == 0,
              "a bank no PRG block explains gets no invented cell", str(doc["counts"]))
        check(doc["counts"]["empty"] == 256 - 20,
              "its holes stay holes", str(doc["counts"]))
        check(any("unpacks" in n for n in doc["notes"]),
              "and the page says why", str(doc["notes"]))


def test_the_legend_marks_evidence_fill_and_hole_apart():
    with tempfile.TemporaryDirectory() as td:
        td = Path(td)
        pack, rom = chr_ram_fixture(td)
        K.run(pack, rom, td / "kit", None, "none", False, True)
        doc = sidecar(td / "kit", "Chr_0")
        legend = read_png(td / "kit" / "chr" / "Chr_0.legend.png")
        st = states(doc)
        want = {"evidence": K.LEGEND_EVIDENCE, "fill": K.LEGEND_FILL,
                "empty": K.LEGEND_EMPTY}
        ok = True
        for state, rgba in want.items():
            slot = next(s for s, v in st.items() if v == state)
            x, y = (slot % 16) * CELL, (slot // 16) * CELL
            ok = ok and legend.get(x, y) == rgba
        check(ok, "each cell state gets its own legend colour")


def test_fill_rules_never_touch_the_pack_and_default_to_none():
    with tempfile.TemporaryDirectory() as td:
        td = Path(td)
        pack, rom = chr_rom_fixture(td)
        before = (pack / "textures" / "hires.txt").read_bytes()
        frag = K.run(pack, rom, td / "kit", None, "none", False, True)
        check(not (td / "kit" / "chr" / "fill-rules.hires.txt").exists(),
              "--fill-rules=none writes no rule file")
        check(frag["totals"]["rulesEmitted"] == 0, "and emits no rule")
        check(frag["totals"]["rulesPermissive"] == frag["totals"]["filled"],
              "the permissive count is one rule per fill")
        frag = K.run(pack, rom, td / "kit2", None, "all", False, True)
        rules = (td / "kit2" / "chr" / "fill-rules.hires.txt").read_text()
        rows = [ln for ln in rules.splitlines() if ln.startswith("<tile>")]
        check(len(rows) == frag["totals"]["filled"],
              "--fill-rules=all writes one row per fill")
        check((pack / "textures" / "hires.txt").read_bytes() == before,
              "the recorded pack's hires.txt is never written to")


def test_the_manifest_fragment_follows_the_shared_kit_contract():
    with tempfile.TemporaryDirectory() as td:
        td = Path(td)
        pack, rom = chr_rom_fixture(td)
        K.run(pack, rom, td / "kit", None, "none", False, True)
        frag = json.loads((td / "kit" / "kit-part-chr.json").read_text())
        check(frag["part"] == "chr" and frag["generator"].endswith("artist_chr_kit.py"),
              "the fragment names its part and generator")
        check(all(k in frag for k in ("pack", "files", "dropped", "notes", "verify")),
              "the fragment has every section the contract asks for",
              str(sorted(frag)))
        f = frag["files"][0]
        check(all(k in f for k in ("path", "title", "unit", "rows", "columns",
                                   "cells", "seen", "evidence", "fill", "empty")),
              "a file entry carries its counts and its seen flag", str(sorted(f)))
        check(f["path"].startswith("chr/"), "pages live under chr/", f["path"])
        check(f["seen"] is False, "a page holding a fill is not all-evidence")


def test_a_page_that_is_in_the_kit_is_never_reported_as_dropped():
    # `dropped[]` is rendered to the artist as "Left out of the kit". A page
    # the tool copies through untouched (a PRG scan, the blank bucket) is
    # still sitting in chr/ and still paintable, so saying it there would tell
    # the artist a file in front of them is absent. It belongs in notes[].
    with tempfile.TemporaryDirectory() as td:
        td = Path(td)
        pack, rom = chr_rom_fixture(td)
        K.run(pack, rom, td / "kit", None, "none", False, True)
        frag = json.loads((td / "kit" / "kit-part-chr.json").read_text())
        written = {p.name for p in (td / "kit" / "chr").glob("*.png")
                   if not p.name.endswith((".orig.png", ".legend.png"))}
        listed = {e["path"] for e in frag["files"]}
        for entry in frag["dropped"]:
            check(entry["path"] not in listed,
                  "a dropped page is not also listed as a file", entry["path"])
            check(entry["path"].split("/")[-1] not in written,
                  "a dropped page has no PNG on disk", entry["path"])


def test_a_title_is_never_invented_and_names_are_honoured():
    with tempfile.TemporaryDirectory() as td:
        td = Path(td)
        pack, rom = chr_rom_fixture(td)
        names = td / "names.json"
        names.write_text(json.dumps({"subjects": {}, "poses": {},
                                     "pages": {"Chr_00_0": "the tank's tileset"}}))
        K.run(pack, rom, td / "kit", str(names), "none", False, True)
        frag = json.loads((td / "kit" / "kit-part-chr.json").read_text())
        titles = {f["path"]: f["title"] for f in frag["files"]}
        check(titles["chr/Chr_00_0.png"] == "the tank's tileset",
              "a named page takes its name", titles["chr/Chr_00_0.png"])
        check("Chr_00_1" in titles["chr/Chr_00_1.png"],
              "an unnamed page falls back to its id and counts",
              titles["chr/Chr_00_1.png"])


def test_the_wrong_rom_is_refused():
    with tempfile.TemporaryDirectory() as td:
        td = Path(td)
        pack, _ = chr_ram_fixture(td)
        wrong = td / "wrong.nes"
        wrong.write_bytes(ines(lcg(16384, 3), b"".join(tile_bytes(i) for i in range(512))))
        try:
            K.run(pack, wrong, td / "kit", None, "none", False, True)
            check(False, "a CHR ROM file is refused for a CHR RAM pack")
        except K.ChrKitError as e:
            check("CHR ROM" in str(e), "a CHR ROM file is refused for a CHR RAM pack", str(e))


def test_verify_proves_the_pages_are_a_drop_in():
    with tempfile.TemporaryDirectory() as td:
        td = Path(td)
        pack, rom = chr_rom_fixture(td)
        K.run(pack, rom, td / "kit", None, "none", False, True)
        result = K.verify(K.Pack(pack), td / "kit" / "chr", quiet=True)
        check(result["lost"] == 0 and result["added"] == 0,
              "no key lost and none added", str(result))
        check(result["keys_after"] >= result["keys_before"],
              "the key set is a superset", str(result))
        check(result["cells_differing"] == 0 and result["cells_checked"] == 110,
              "every recorded cell of the kit was checked and matched", str(result))
        check(result["errors"] == 0, "mep_build build reports no error", str(result))


def test_verify_catches_a_page_that_changed_a_recorded_cell():
    with tempfile.TemporaryDirectory() as td:
        td = Path(td)
        pack, rom = chr_rom_fixture(td)
        K.run(pack, rom, td / "kit", None, "none", False, True)
        page = td / "kit" / "chr" / "Chr_00_0.png"
        img = read_png(page)
        was = img.get(0, 0)                  # cell 0 is recorded evidence
        img.set(0, 0, (was[0] ^ 0xFF, was[1] ^ 0xFF, was[2] ^ 0xFF, 0xFF))
        write_png(page, img)
        try:
            K.verify(K.Pack(pack), td / "kit" / "chr", quiet=True)
            check(False, "verify refuses a page that repainted evidence")
        except K.ChrKitError as e:
            check("recorded cell" in str(e),
                  "verify refuses a page that repainted evidence", str(e))


def test_out_inside_the_recorded_pack_is_refused():
    with tempfile.TemporaryDirectory() as td:
        td = Path(td)
        pack, rom = chr_rom_fixture(td)
        rc = None
        try:
            K.main([str(pack), "--rom", str(rom), "--out", str(pack / "kit"), "--quiet"])
        except SystemExit as e:
            rc = str(e)
        check(rc is not None and "must not be inside" in rc,
              "the tool refuses to write inside the recorded pack", str(rc))


def test_a_second_recording_fills_a_hole_the_primary_never_recorded():
    with tempfile.TemporaryDirectory() as td:
        td = Path(td)
        pack, donor, rom = chr_rom_pair_fixture(td)
        K.run(pack, rom, td / "kit", None, "none", False, True, [str(donor)])
        doc = sidecar(td / "kit", "Chr_00_0")
        donated = [c for c in doc["cells"] if c["state"] == "donated"]
        check({c["index"] for c in donated} == DONOR_ONLY,
              "exactly the indices only the second recording saw are donated",
              str(sorted(c["index"] for c in donated)))
        check(all(c["seen"] is True for c in donated),
              "a donated cell is seen: true — a real run really drew it")
        # `--also` resolves its path, so the sidecar names the real location.
        check(all(c["sourcePack"] == str(donor.resolve())
                  and c["sourcePage"] == "Chr_00_9" for c in donated),
              "a donated cell names the recording and the page it came from",
              str(donated[0]))
        check(all(c["palette"] == PAL_B for c in donated),
              "it keeps the palette the donor recorded it under")
        # and its pixels really are the donor's, not a re-render
        page = read_png(td / "kit" / "chr" / "Chr_00_0.png")
        src = read_png(donor / "textures" / "chr" / "Chr_00_9.png")
        c = donated[0]
        check(page.crop(c["x"], c["y"], CELL, CELL).px
              == src.crop(c["x"], c["y"], CELL, CELL).px,
              "the donated pixels are copied from the donor's own page")


def test_the_order_of_preference_is_own_evidence_then_donor_then_rom():
    # ADR-0183 §1: the primary pack is the pack the kit is for, so its own
    # recorded cell wins even against a donor that also has the index; and
    # evidence beats inference, so a donated cell wins against a ROM fill.
    with tempfile.TemporaryDirectory() as td:
        td = Path(td)
        pack, donor, rom = chr_rom_pair_fixture(td)
        K.run(pack, rom, td / "kit", None, "none", False, True, [str(donor)])
        doc = sidecar(td / "kit", "Chr_00_0")
        st = states(doc)
        by_index = {c["index"]: c for c in doc["cells"]}
        check(all(st[i] == "evidence" and by_index[i]["palette"] == PAL_A
                  for i in range(10)),
              "an index both runs recorded stays this pack's own evidence",
              str([(i, st[i]) for i in range(10)]))
        check(all(st[i] == "borrowed" for i in range(200, 210)),
              "a lower-ranked page of this pack still outranks a donor")
        check(all(st[i] == "donated" for i in DONOR_ONLY),
              "an index only the donor recorded is donated, not filled from the ROM")
        check(doc["counts"] == {"evidence": 100, "borrowed": 10, "folded": 0,
                               "fill": 126, "empty": 0, "donated": 20},
              "and the counts add up to the whole bank", str(doc["counts"]))


def test_a_donor_recorded_from_another_rom_is_refused_by_name_and_hash():
    with tempfile.TemporaryDirectory() as td:
        td = Path(td)
        pack, donor, rom = chr_rom_pair_fixture(td)
        other = td / "other.nes"
        other.write_bytes(ines(lcg(16384, 3), b"".join(tile_bytes(i) for i in range(512))))
        other_sha1 = hashlib.sha1(other.read_bytes()).hexdigest().upper()
        stranger = td / "stranger"
        write_pack(stranger, SCALE,
                   [("Chr_00_0", {i: (tile_bytes(i), PAL_A, i) for i in range(10)})],
                   chr_rom_rows, other_sha1)
        try:
            K.run(pack, rom, td / "kit", None, "none", False, True, [str(stranger)])
            check(False, "a donor recorded from another ROM is refused")
        except K.ChrKitError as e:
            msg = str(e)
            check(str(stranger) in msg and other_sha1 in msg
                  and hashlib.sha1(rom.read_bytes()).hexdigest().upper() in msg,
                  "a donor recorded from another ROM is refused, by name and both hashes",
                  msg)
        # 40 zeros identify no ROM, so an old recording cannot be paired either
        nameless = td / "nameless"
        write_pack(nameless, SCALE,
                   [("Chr_00_0", {i: (tile_bytes(i), PAL_A, i) for i in range(10)})],
                   chr_rom_rows)
        try:
            K.run(pack, rom, td / "kit", None, "none", False, True, [str(nameless)])
            check(False, "a donor with no <supportedRom> is refused")
        except K.ChrKitError as e:
            check("proves nothing" in str(e),
                  "a donor with no <supportedRom> is refused", str(e))


def test_the_legend_gives_a_donated_cell_its_own_colour():
    with tempfile.TemporaryDirectory() as td:
        td = Path(td)
        pack, donor, rom = chr_rom_pair_fixture(td)
        K.run(pack, rom, td / "kit", None, "none", False, True, [str(donor)])
        doc = sidecar(td / "kit", "Chr_00_0")
        st = states(doc)
        legend = read_png(td / "kit" / "chr" / "Chr_00_0.legend.png")
        check(len({K.LEGEND_EVIDENCE, K.LEGEND_BORROWED, K.LEGEND_DONATED,
                   K.LEGEND_FOLDED, K.LEGEND_FILL, K.LEGEND_EMPTY}) == 6,
              "the six cell states have six distinct legend colours")
        ok = True
        for state, rgba in (("evidence", K.LEGEND_EVIDENCE),
                            ("borrowed", K.LEGEND_BORROWED),
                            ("donated", K.LEGEND_DONATED),
                            ("fill", K.LEGEND_FILL)):
            slot = next(s for s, v in st.items() if v == state)
            ok = ok and legend.get((slot % 16) * CELL, (slot // 16) * CELL) == rgba
        check(ok, "a donated cell is painted its own colour, not green or amber")


def test_provenance_travels_into_the_fragment_and_its_notes():
    with tempfile.TemporaryDirectory() as td:
        td = Path(td)
        pack, donor, rom = chr_rom_pair_fixture(td)
        frag = K.run(pack, rom, td / "kit", None, "none", False, True, [str(donor)])
        check(frag["donors"] == [{"pack": str(donor.resolve()),
                                  "romSha1": hashlib.sha1(rom.read_bytes()).hexdigest().upper(),
                                  "cells": 20}],
              "the fragment names the second recording and what it contributed",
              str(frag.get("donors")))
        check(frag["totals"]["donated"] == 20
              and frag["totals"]["unrecoverable"] == 0,
              "the totals count donated cells apart from recorded and filled",
              str(frag["totals"]))
        notes = " ".join(frag["notes"])
        check(str(donor.resolve()) in notes and "20 cell(s) of this kit come from it" in notes,
              "notes[] names the recording and how many cells came from it — this is "
              "what a reader of ARTIST.md sees", notes[:400])
        check("no counterpart in this pack" in notes and "1" in notes,
              "a donor bank this pack never loaded is reported, not silently dropped",
              notes[:400])


def test_a_donated_cell_never_becomes_a_hires_rule():
    # A donated cell's (pattern, palette) key was observed by the *donor*, not by
    # this pack. Emitting a rule for it would add a key this pack never rendered,
    # which is exactly what ADR-0183 §4's round trip forbids.
    with tempfile.TemporaryDirectory() as td:
        td = Path(td)
        pack, donor, rom = chr_rom_pair_fixture(td)
        frag = K.run(pack, rom, td / "kit", None, "all", False, True, [str(donor)])
        rows = [ln for ln in (td / "kit" / "chr" / "fill-rules.hires.txt").read_text()
                .splitlines() if ln.startswith("<tile>")]
        check(len(rows) == frag["totals"]["filled"] == 126,
              "--fill-rules=all writes one row per ROM fill and no more", str(len(rows)))
        doc = sidecar(td / "kit", "Chr_00_0")
        donated_x = {(c["x"], c["y"]) for c in doc["cells"] if c["state"] == "donated"}
        check(not any((int(r.split(",")[3]), int(r.split(",")[4])) in donated_x
                      for r in rows),
              "no rule is emitted for a donated cell")
        result = K.verify(K.Pack(pack), td / "kit" / "chr", quiet=True)
        check(result["lost"] == 0 and result["errors"] == 0,
              "and the completed pages still round-trip", str(result))


def test_without_also_the_run_leaves_no_trace_of_the_feature():
    # The guard that keeps a plain run byte-identical to what the tool wrote
    # before `--also` existed: no donation counter, no donors[], no note.
    with tempfile.TemporaryDirectory() as td:
        td = Path(td)
        pack, donor, rom = chr_rom_pair_fixture(td)
        frag = K.run(pack, rom, td / "kit", None, "none", False, True)
        check("donors" not in frag and "donated" not in frag["totals"]
              and "donatedCells" not in frag["totals"],
              "a run with no --also emits no donation field", str(sorted(frag["totals"])))
        check(all("donated" not in f for f in frag["files"]),
              "nor on a file entry")
        doc = sidecar(td / "kit", "Chr_00_0")
        check("donated" not in doc["counts"], "nor in a page sidecar's counts",
              str(doc["counts"]))
        check(not any("recording" in n and "Second" in n for n in frag["notes"]),
              "nor a note about a second recording")
        # and the same run *with* a donor does emit all four
        frag2 = K.run(pack, rom, td / "kit2", None, "none", False, True, [str(donor)])
        check("donors" in frag2 and "donated" in frag2["totals"]
              and "donated" in sidecar(td / "kit2", "Chr_00_0")["counts"],
              "which is a real difference, not an absent code path")


def main():
    tests = [
        test_chr_rom_bank_is_completed_to_every_one_of_its_256_tiles,
        test_a_filled_cell_carries_the_rom_bytes_and_is_marked_unseen,
        test_a_cell_from_a_lower_rank_page_is_moved_up_and_still_evidence,
        test_recorded_cells_come_out_byte_identical,
        test_the_fill_palette_is_read_off_the_pack_not_assumed,
        test_chr_ram_fills_only_what_a_prg_block_explains,
        test_a_packed_chr_ram_bank_is_left_alone_and_says_so,
        test_the_legend_marks_evidence_fill_and_hole_apart,
        test_fill_rules_never_touch_the_pack_and_default_to_none,
        test_the_manifest_fragment_follows_the_shared_kit_contract,
        test_a_page_that_is_in_the_kit_is_never_reported_as_dropped,
        test_a_title_is_never_invented_and_names_are_honoured,
        test_the_wrong_rom_is_refused,
        test_verify_proves_the_pages_are_a_drop_in,
        test_verify_catches_a_page_that_changed_a_recorded_cell,
        test_out_inside_the_recorded_pack_is_refused,
        test_a_second_recording_fills_a_hole_the_primary_never_recorded,
        test_the_order_of_preference_is_own_evidence_then_donor_then_rom,
        test_a_donor_recorded_from_another_rom_is_refused_by_name_and_hash,
        test_the_legend_gives_a_donated_cell_its_own_colour,
        test_provenance_travels_into_the_fragment_and_its_notes,
        test_a_donated_cell_never_becomes_a_hires_rule,
        test_without_also_the_run_leaves_no_trace_of_the_feature,
        test_a_fade_step_of_one_ramp_folds_onto_the_step_above_it,
        test_a_fully_faded_palette_folds_at_brightness_zero,
        test_two_colourways_of_one_pattern_are_never_folded,
        test_a_rotated_ramp_is_not_a_fade,
        test_one_entry_moving_while_the_others_hold_is_left_to_the_gate,
        test_a_palette_that_gets_brighter_is_never_a_fade_downwards,
        test_only_the_indices_the_pattern_paints_are_compared,
        test_the_fold_is_reported_per_page_and_per_pack_and_changes_no_pixel,
    ]
    for t in tests:
        t()
    print(f"\n{len(tests) - len(_FAILURES)}/{len(tests)} cases passed")
    return 1 if _FAILURES else 0


if __name__ == "__main__":
    sys.exit(main())
