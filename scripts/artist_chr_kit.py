#!/usr/bin/env python3
"""artist_chr_kit — complete a recorded pack's CHR pages from the ROM (PRD F9.24).

    scripts/artist_chr_kit.py <recorded pack dir> --rom <path.nes> [--out DIR]
                              [--also <other recorded pack dir>]...
                              [--names names.json] [--fill-rules none|observed|all]
                              [--verify] [--quiet]
    scripts/artist_chr_kit.py <empty or missing dir> --rom <path.nes> --static
                              [--out DIR] [--scale N] [--names names.json] [--quiet]

A recorded pack is the `auto/` folder the bootstrap builder writes. Its
`textures/chr/Chr_*.png` pages are already the surface the hand-built fan packs
work on — 16x16 cells of one pattern page, one file per (CHR bank, palette) the
recording saw — but a cell whose tile never reached the screen is left magenta.
The Contra measurement of 2026-09-13 (`runs/golden-20260913-f922/artist-cover.md`)
put the recorded coverage at 47 % of the fan pack's distinct tiles, so an artist
painting page by page hits a hole every few cells.

This tool fills the holes from the ROM and refuses to pretend when it cannot.

`--static` (ADR-0219, PRD F12.9) is the degenerate case of exactly that: there is
no recording at all, so every cell is a hole and every hole is filled from the
ROM. It runs on a **CHR ROM** game and only there — the bank *is* 4 KB of the
file, so the shape half of the kit needs no play session — and it produces the
pattern pages alone: no figure, no scenery, no stage map, because nothing was
observed. Every cell comes out `fill` / `seen: false` and every emitted `<tile>`
rule carries `defaultTile = Y`, the palette wildcard ADR-0210 measured. A CHR RAM
game is refused, with a pointer to the third-party key index (ADR-0210 §3,
F12.12) that is the only static source of shape for those. The tool never starts
an emulator, reads a `.mss`, a route set or a movie on any path.

A page is not one palette. `HdPackBuilder::SaveHdPack` spreads each tile's
palette variants across the pages of its CHR bank by usage, so `Chr_<b>_0` holds
each index's most-used variant, `Chr_<b>_1` the second-most, and so on. The unit
of completeness is therefore the **bank**, and only its rank-0 page is completed.

Per bank kind:

* **CHR ROM** (`Chr_<bank>_<n>.png`, iNES header byte 5 > 0) — the bank *is* 4 KB
  of the file, so every one of its 256 tiles is readable statically and the page
  comes out complete.
* **CHR RAM** (`Chr_<n>.png`) — the pattern table is filled at run time from PRG
  data, packed or not. A tile is recoverable only where the bank's *recorded*
  tiles pin down a contiguous PRG block (`offset = base + 16 * index`) that also
  covers the hole. Where the game unpacks its graphics, nothing pins down and the
  cell stays a hole: the bytes are not in the file in tile form.
* The recorder's own synthetic pages are left alone and named as such — the PRG
  scan it already writes (`HdPackBuilder::AddPrgScanTiles`, bank ids
  `0x504247xx`) and its blank-tile bucket (`Chr_FFFFFFFF_*`).

A stage is recorded more than once (ADR-0184 §2: a clean pass and a coverage pass
under a RAM-only cheat), and neither run dominates the other — an immortal run
travels further, a mortal one sees the banks that death, respawn and GAME OVER
load. `--also <pack>` therefore takes **another recording of the same ROM as
additional evidence**. It is evidence, not a second source of truth (ADR-0183
§1): the pack named on the command line stays the pack the kit is for, its own
recorded cells always win, and a donated cell only ever fills a hole. Passing
that same pack (or any recording twice) adds no evidence, so the repeat is
ignored and named in `notes[]` rather than refused — a caller that builds the
list as "the whole set, and then also the whole set" gets the kit it meant.

**Order of preference per cell of a bank's rank-0 page**, in this order and no
other (`write_bank` implements it as one if/elif chain):

1. this pack's own cell recorded on this page — `evidence`;
2. a cell this bank recorded on a lower-ranked page — `borrowed`, still this
   pack's evidence, another palette rank of it;
3. a cell another recording of the same ROM recorded — `donated`, `seen: true`
   because a real run really drew it, but not this pack's own evidence, so the
   sidecar names the run it came from and the legend gives it its own colour;
4. a cell read statically out of the ROM — `fill`, `seen: false`;
5. nothing — `empty`, a hole this tool refuses to paint.

A recorded pack keys a cell by (pattern, palette), so one drawing comes back
once per palette the run saw it wearing — and a screen fade gives every tile on
screen a key per step of the fade. Where two of those keys are the same picture
at another brightness, the kit **folds** one onto the other: the artist paints
one cell, the renderer rebuilds the rest from the `<tile>` row's Brightness
column, and the recording keeps every key it had. Folding is a statement about
colour only, decided off the NES palette (`compute_folds`), and a palette that
changes any painted entry's hue is never folded — two colourways of one enemy
are two pictures, not one (ADR-0183 §3).

Honesty rules this tool keeps:

* a cell the recording put on this page is copied **byte for byte** from the
  recorded page, so a rebuilt pack renders exactly what it rendered before;
* a cell folded onto another is still that byte-for-byte copy — folding changes
  what the kit *says* about a cell, never its pixels — and its sidecar entry
  names the cell it folds into, the Brightness that rebuilds it and the hue
  drift that costs;
* a cell this bank recorded on a lower-ranked page is moved up onto the rank-0
  page — real evidence under another palette, marked `borrowed`;
* a cell another recording of the same ROM recorded is pasted from that run's
  own page, marked `donated`, blue in the legend, and carries the donor's path,
  page and slot in `Chr_<n>.json`; a donor is refused unless its
  `<supportedRom>` sha1 is present and equal to this pack's;
* a cell filled from the ROM is rendered nearest-neighbour (the recorder's own
  pages go through a smoothing filter, so the fill is visibly crisper) and is
  marked `seen: false` in `Chr_<n>.json`, amber in `Chr_<n>.legend.png`, and
  counted in the manifest fragment;
* a filled cell's *palette* is a guess — see `--fill-rules` — and by default no
  `hires.txt` rule is emitted for it at all.

Output follows the shared kit contract
(`runs/golden-20260913-f922/artist-kit-contract.md`): pages under `<out>/chr/`,
one manifest fragment `<out>/kit-part-chr.json`. The tool never writes inside the
recorded pack and never copies ROM bytes into the repository.

Python 3, standard library only.
"""

from __future__ import annotations

import argparse
import collections
import hashlib
import json
import math
import os
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import asset_names as N  # noqa: E402 — the F12.4 painting-surface name contract
from sheet_repaint import Image, read_png, write_png  # noqa: E402
import ora_writer  # noqa: E402 — ADR-0220: the layered .ora beside every page

SCRIPT_DIR = Path(__file__).resolve().parent

# The 2C02 table of Core/NES/NesDefaultVideoFilter.cpp, ARGB. Only ever used for
# a palette index that no cell of the pack exhibits; everything else is read off
# the recorded pages, which keeps a custom palette working for free.
DEFAULT_PALETTE_ARGB = [
    0xFF666666, 0xFF002A88, 0xFF1412A7, 0xFF3B00A4, 0xFF5C007E, 0xFF6E0040,
    0xFF6C0600, 0xFF561D00, 0xFF333500, 0xFF0B4800, 0xFF005200, 0xFF004F08,
    0xFF00404D, 0xFF000000, 0xFF000000, 0xFF000000, 0xFFADADAD, 0xFF155FD9,
    0xFF4240FF, 0xFF7527FE, 0xFFA01ACC, 0xFFB71E7B, 0xFFB53120, 0xFF994E00,
    0xFF6B6D00, 0xFF388700, 0xFF0C9300, 0xFF008F32, 0xFF007C8D, 0xFF000000,
    0xFF000000, 0xFF000000, 0xFFFFFEFF, 0xFF64B0FF, 0xFF9290FF, 0xFFC676FF,
    0xFFF36AFF, 0xFFFE6ECC, 0xFFFE8170, 0xFFEA9E22, 0xFFBCBE00, 0xFF88D800,
    0xFF5CE430, 0xFF45E082, 0xFF48CDDE, 0xFF4F4F4F, 0xFF000000, 0xFF000000,
    0xFFFFFEFF, 0xFFC0DFFF, 0xFFD3D2FF, 0xFFE8C8FF, 0xFFFBC2FF, 0xFFFEC4EA,
    0xFFFECCC5, 0xFFF7D8A5, 0xFFE4E594, 0xFFCFEF96, 0xFFBDF4AB, 0xFFB3F3CC,
    0xFFB5EBF2, 0xFFB8B8B8, 0xFF000000, 0xFF000000,
]

# Legend washes, RGBA. Painted over the page at 40 % so the art stays readable.
LEGEND_EVIDENCE = (0x2E, 0xA0, 0x43, 0xFF)
LEGEND_BORROWED = (0x8A, 0xA0, 0x26, 0xFF)
# A donated cell is evidence, but not this pack's: blue reads as neither the
# green/olive family (this pack saw it) nor the amber one (nobody saw it).
LEGEND_DONATED = (0x2F, 0x81, 0xF7, 0xFF)
# A folded cell is evidence the artist does not have to paint: the same
# picture at another brightness, reconstructed from the cell it folds into.
LEGEND_FOLDED = (0x6E, 0x40, 0xC9, 0xFF)
LEGEND_FILL = (0xE3, 0x9A, 0x0B, 0xFF)
LEGEND_EMPTY = (0xC4, 0x28, 0x28, 0xFF)

# CHR RAM fill thresholds. A "base" is a PRG offset such that the page's tile
# `i` sits at `base + 16 * i`; it is only trusted when several distinct,
# non-degenerate cells of the same page agree on it, and it may only fill a hole
# that one of those agreeing cells sits next to.
MIN_BASE_SUPPORT = 3
LOCALITY_WINDOW = 16

# `<ver>` a static manifest declares: BaseHdNesPack::CurrentVersion, the number
# the recorder writes (HdNesPack.h). A static pack is read by the same loader as
# a recorded one, so it declares the same version.
PACK_VERSION = 109

# The palette a static fill is rendered under. It is a guess and it does not
# have to be a good one: every static rule carries `defaultTile = Y`, which is
# the per-rule palette wildcard (ADR-0210), so the shape matches whatever
# colours the game puts it under. `Bank.fill_palette()` returns this same value
# when a bank has no recorded cell to vote, which is every bank here.
STATIC_FILL_PALETTE = "0F001030"

# First line of a static manifest, and of the `hires.txt` a pages-only build
# writes from it. It is what lets `mep_build.py build` tell "this pack is its
# own pages" from "this pack has a recording", so it never overwrites a
# recorded manifest; `scripts/mep_build.py` reads the same constant.
PAGES_ONLY_MARK = "# mep-pages-only 1"

# The colour the recorder leaves an unpainted cell (`0xFFFF00FF`, ARGB). A
# static page starts as this and is then written cell by cell; any pixel still
# wearing it would be a cell the ROM could not supply, which cannot happen on a
# CHR ROM bank and is why the static kit reports 0 `empty`.
UNPAINTED_RGBA = (0xFF, 0x00, 0xFF, 0xFF)


class ChrKitError(Exception):
    pass


# --- pack reading -----------------------------------------------------------


TILE_RE = re.compile(r"^(?:\[(?P<cond>[^\]]*)\]\s*)?<tile>(?P<body>.*)$")


class TileRow:
    """One `<tile>` row of a recorded `hires.txt`."""

    __slots__ = ("image", "tile_data", "tile_index", "palette", "x", "y",
                 "brightness", "default_tile", "chr_bank_id", "conditions",
                 "line_no")

    def __init__(self, **kw):
        for k in self.__slots__:
            setattr(self, k, kw.get(k))

    @property
    def key(self):
        """The `hires.txt` lookup key: pattern identity plus palette."""
        if self.tile_data is not None:
            return (self.tile_data, self.palette)
        return (f"#{self.tile_index}", self.palette)


class Pack:
    def __init__(self, root: Path, static: "Rom | None" = None, scale: int = 1,
                 rom_sha1: str = ""):
        self.root = root
        self.textures = root / "textures"
        self.hires = self.textures / "hires.txt"
        self.version = PACK_VERSION
        self.scale = 1
        self.rom_sha1 = ""
        self.images: list[str] = []
        self.tiles: list[TileRow] = []
        self.static = static is not None
        if self.static:
            # ADR-0219: the ROM is the whole input and the folder is an output
            # location, so there is nothing to parse. One page per 4 KB bank of
            # the file, rank 0, in bank order; no `<tile>` row exists yet
            # because no cell was ever recorded.
            self.scale = scale
            self.rom_sha1 = rom_sha1
            self.images = [f"chr/{static_page_name(b)}.png"
                           for b in range(static.chr_tile_count // 256)]
            return
        self.version = 0
        if not self.hires.is_file():
            raise ChrKitError(f"{root}: no textures/hires.txt — not a recorded pack folder")
        self._parse()

    def _parse(self):
        for line_no, raw in enumerate(self.hires.read_text(encoding="utf-8", errors="replace").splitlines(), 1):
            line = raw.strip()
            if not line or line.startswith("//"):
                continue
            if line.startswith("<ver>"):
                self.version = int(line[5:].strip())
            elif line.startswith("<scale>"):
                self.scale = int(line[7:].strip())
            elif line.startswith("<supportedRom>"):
                self.rom_sha1 = line[14:].strip().lower()
            elif line.startswith("<img>"):
                self.images.append(line[5:].strip())
            else:
                m = TILE_RE.match(line)
                if m:
                    self.tiles.append(self._tile(m, line_no))

    def _tile(self, m, line_no) -> TileRow:
        tok = [t.strip() for t in m.group("body").split(",")]
        image = int(tok[0])
        data, pal = tok[1], tok[2].upper()
        tile_data = tile_index = None
        if len(data) >= 32:
            tile_data = data.upper()
        else:
            tile_index = int(data, 16)
        x, y = int(tok[3]), int(tok[4])
        brightness = tok[5] if len(tok) > 5 else "1"
        default_tile = tok[6] if len(tok) > 6 else "N"
        chr_bank_id = None
        if tile_data is not None:
            if len(tok) > 7:
                chr_bank_id = int(tok[7])
            if len(tok) > 8:
                tile_index = int(tok[8])
        else:
            chr_bank_id = tile_index // 256
        return TileRow(image=image, tile_data=tile_data, tile_index=tile_index,
                       palette=pal, x=x, y=y, brightness=brightness,
                       default_tile=default_tile, chr_bank_id=chr_bank_id,
                       conditions=m.group("cond"), line_no=line_no)

    def key_set(self):
        return {t.key for t in self.tiles}


# --- ROM reading ------------------------------------------------------------


class Rom:
    """An iNES file, split into PRG and CHR. Bytes stay in memory; nothing is
    written back out — a ROM must never land in the repository."""

    def __init__(self, path: Path):
        data = path.read_bytes()
        if data[:4] != b"NES\x1a":
            raise ChrKitError(f"{path}: not an iNES file")
        self.path = path
        prg_size = data[4] * 16384
        chr_size = data[5] * 8192
        self.mapper = (data[6] >> 4) | (data[7] & 0xF0)
        off = 16 + (512 if data[6] & 0x04 else 0)
        self.prg = data[off:off + prg_size]
        self.chr = data[off + prg_size:off + prg_size + chr_size]
        self.has_chr_rom = chr_size > 0
        self._prg_index = None

    @property
    def chr_tile_count(self):
        return len(self.chr) // 16

    def chr_tile(self, index: int):
        o = index * 16
        if o + 16 > len(self.chr):
            return None
        return self.chr[o:o + 16]

    def prg_index(self):
        """Every 16-byte window of the PRG, mapped to the offsets it occurs at.

        Unaligned on purpose: a CHR RAM game's tile blocks start wherever the
        assembler put them, and the recorded cells are what tells us where."""
        if self._prg_index is None:
            idx = collections.defaultdict(list)
            prg = self.prg
            for o in range(0, len(prg) - 15):
                idx[prg[o:o + 16]].append(o)
            self._prg_index = idx
        return self._prg_index




# --- pages and banks --------------------------------------------------------


# HdPackBuilder::AddPrgScanTiles tags its synthetic 256-tile pages
# `0x50524700 + n` ("PRG"); the builder's blank-tile bucket is 0xFFFFFFFF.
PRG_SCAN_BANK_MASK = 0xFFFFFF00
PRG_SCAN_BANK_TAG = 0x50524700


def large_sprite_slot(index: int) -> int:
    """HdPackBuilder::DrawTile's 8x16-sprite shuffle, so a page laid out that
    way is still read cell-for-tile."""
    row, col = index // 16, index % 16
    new_col = col // 2 + (8 if (row & 1) else 0)
    new_row = (row & 0xFE) + (1 if (col & 1) else 0)
    return new_row * 16 + new_col


LARGE_SPRITE_SLOT_TO_INDEX = {large_sprite_slot(i): i for i in range(256)}


class Page:
    """One `Chr_*.png` — 16x16 cells of one CHR bank.

    Not one palette: `SaveHdPack` redistributes a tile's palette variants across
    the pages of its bank by usage, so `Chr_<b>_0` holds each index's most-used
    variant, `Chr_<b>_1` the second-most, and so on. A page is therefore a
    *variant rank* of a bank, and completeness is a property of the bank."""

    __slots__ = ("name", "path", "image_index", "scale", "cell_px", "rows",
                 "palette", "chr_bank_id", "is_chr_ram", "layout", "bank_mixed")

    def __init__(self, name, path, image_index, scale):
        self.name = name
        self.path = path
        self.image_index = image_index
        self.scale = scale
        self.cell_px = 8 * scale
        self.rows: dict[int, TileRow] = {}    # slot -> recorded row
        self.palette = None
        self.chr_bank_id = None
        self.is_chr_ram = False
        self.layout = None                    # "identity" | "largeSprites" | None
        self.bank_mixed = False

    def slot_of_row(self, row: TileRow) -> int:
        return (row.y // self.cell_px) * 16 + (row.x // self.cell_px)

    def index_of_slot(self, slot: int) -> int:
        return LARGE_SPRITE_SLOT_TO_INDEX[slot] if self.layout == "largeSprites" else slot

    def slot_of_index(self, index: int) -> int:
        return large_sprite_slot(index) if self.layout == "largeSprites" else index

    def xy(self, slot: int):
        return (slot % 16) * self.cell_px, (slot // 16) * self.cell_px


def _fit_layout(page: Page):
    """Cells carry their tile index; check it against the two layouts the
    builder can emit and refuse the page when neither fits."""
    known = [(slot, row.tile_index % 256) for slot, row in page.rows.items()
             if row.tile_index is not None and row.tile_index >= 0]
    if not known:
        return "identity"
    if all(slot == index for slot, index in known):
        return "identity"
    if all(LARGE_SPRITE_SLOT_TO_INDEX[slot] == index for slot, index in known):
        return "largeSprites"
    return None


def collect_pages(pack: Pack) -> list[Page]:
    by_image = collections.defaultdict(list)
    for t in pack.tiles:
        by_image[t.image].append(t)

    pages = []
    for image_index, rel in enumerate(pack.images):
        if not rel.startswith("chr/"):
            continue
        rows = by_image.get(image_index, [])
        if not rows:
            continue
        page = Page(Path(rel).stem, pack.textures / rel, image_index, pack.scale)
        if not page.path.is_file():
            continue
        for r in rows:
            page.rows[page.slot_of_row(r)] = r
        page.is_chr_ram = rows[0].tile_data is not None
        page.palette = collections.Counter(r.palette for r in rows).most_common(1)[0][0]
        # A handful of a page's cells can carry a neighbouring bank's id (the
        # builder dedupes identical patterns across banks), so the page's bank
        # is the majority, not a unanimous vote.
        votes = collections.Counter(r.chr_bank_id for r in rows if r.chr_bank_id is not None)
        page.chr_bank_id = votes.most_common(1)[0][0] if votes else None
        page.bank_mixed = len(votes) > 1
        page.layout = _fit_layout(page)
        pages.append(page)
    return pages


def static_page_name(bank_id: int) -> str:
    """The page name the recorder would have written for this CHR ROM bank.

    `HdPackBuilder::SaveHdPack` names a CHR ROM page `Chr_<HexUtilities::ToHex(
    bankId)>_<rank>.png`, and that helper widens by bytes: 2 hex digits up to
    0xFF, then 4, then 6, then 8. A static kit has only rank 0 — a rank is a
    palette variant and no palette was ever seen."""
    for width, top in ((2, 0xFF), (4, 0xFFFF), (6, 0xFFFFFF)):
        if bank_id <= top:
            return f"Chr_{bank_id:0{width}X}_0"
    return f"Chr_{bank_id:08X}_0"


def static_pages(pack: Pack) -> list[Page]:
    """One `Page` per 4 KB CHR ROM bank, with no recorded row on any of them.

    Every field `collect_pages` reads off the recording is decided here instead,
    and each one is a statement about the ROM rather than about a run: the
    layout is `identity` because a static page is laid out in bank order (the
    `largeSprites` shuffle is something the *builder* does to what it saw), the
    bank id is the bank's own number, and the page carries no palette of its own
    (ADR-0219 §"Nothing is claimed to be seen")."""
    pages = []
    for image_index, rel in enumerate(pack.images):
        page = Page(Path(rel).stem, None, image_index, pack.scale)
        page.is_chr_ram = False
        page.palette = STATIC_FILL_PALETTE
        page.chr_bank_id = image_index
        page.layout = "identity"
        pages.append(page)
    return pages


def static_images(pages: list[Page]) -> dict:
    """The blank canvas each static page is drawn onto.

    A recorded page arrives as a PNG the recorder wrote; there is none here, so
    the page starts as the recorder's own unpainted colour and every one of its
    256 cells is then written by the ROM fill. `orig` is the same canvas rather
    than `None`, so the twin ADR-0153 §3 requires comes out identical to the
    sheet — which is exactly what an untouched reference means here."""
    out = {}
    for page in pages:
        size = 16 * page.cell_px
        # Built as one buffer rather than pixel by pixel: a 32-bank ROM at
        # scale 4 is 16.7 M pixels, and the per-pixel form spent 9 of the
        # slice's 10-second budget writing a colour that is about to be
        # overwritten.
        row = bytes(UNPAINTED_RGBA) * size
        out[page.name] = {role: Image(size, size, bytearray(row * size))
                          for role in ("hd", "orig")}
    return out


class Bank:
    """Every page of one CHR bank, plus what is known about its 256 tiles."""

    def __init__(self, bank_id, pages: list[Page]):
        self.id = bank_id
        # Rank 0 is the page holding each index's most-used variant, which is
        # both the fullest page and the one an artist opens first.
        self.pages = sorted(pages, key=lambda p: (-len(p.rows), p.name))
        self.primary = self.pages[0]
        self.is_chr_ram = self.primary.is_chr_ram
        self.layout = self.primary.layout
        self.kind = self._kind()
        # index -> (page, slot) of the recorded art, best rank first
        self.art: dict[int, tuple] = {}
        for page in self.pages:
            for slot in page.rows:
                self.art.setdefault(page.index_of_slot(slot), (page, slot))
        # index -> donation dict, from a second recording of the same ROM. Only
        # ever set for an index this pack's own `art` does not hold.
        self.donated: dict[int, dict] = {}
        # False when the pack records no CHR bank hash and the partition was
        # recovered structurally: two such banks of two different recordings
        # cannot be matched to each other, so they are never donated to.
        self.identity_known = True
        self.fills: dict[int, dict] = {}
        self.notes: list[str] = []
        self.transparent_rgba = None    # RGBA of colour 0 on a sprite bank

    def _kind(self):
        if self.primary.name.startswith("Chr_FFFFFFFF"):
            return "blankBucket"
        if self.id is None or self.layout is None:
            return "unreadable"
        if (self.id & PRG_SCAN_BANK_MASK) == PRG_SCAN_BANK_TAG:
            return "prgScan"
        return "chrRam" if self.is_chr_ram else "chrRom"

    def fill_palette(self):
        """The palette a ROM fill is rendered under.

        Rule: the most common palette among the *recorded* cells of this bank's
        rank-0 page — the page a fill lands on, and the palette that page mostly
        shows. It is a guess and is recorded as one: the pattern comes from the
        ROM, the colours come from what this page was seen wearing."""
        votes = collections.Counter()
        for page in self.pages:
            weight = 4 if page is self.primary else 1
            for row in page.rows.values():
                votes[row.palette] += weight
        return votes.most_common(1)[0][0] if votes else "0F001030"


def bank_identity_unknown(pages) -> bool:
    """A CHR RAM pack records a content hash per bank. Packs recorded before the
    builder populated it carry 0 on every real page, and then two different
    pattern tables are indistinguishable — such pages must not be pooled. The
    builder's own synthetic PRG-scan pages always carry their 0x504247xx tag,
    so they are not evidence either way."""
    real = [p for p in pages if p.is_chr_ram
            and ((p.chr_bank_id or 0) & PRG_SCAN_BANK_MASK) != PRG_SCAN_BANK_TAG]
    return bool(real) and not any(p.chr_bank_id for p in real)


def _regroup_without_hashes(pages):
    """Recover the bank partition of a pack whose CHR bank hashes are all zero.

    Two pages belong to the same CHR RAM bank only if they never disagree about
    a tile index: within a bank a page is a *palette variant rank*, so the same
    index always carries the same 16 pattern bytes. A page that contradicts a
    group at any index was recorded while a different bank was in the pattern
    table, and starts a group of its own. Fullest page first, so the rank-0
    pages seed the groups."""
    groups = []
    for page in sorted(pages, key=lambda p: (-len(p.rows), p.name)):
        art = {page.index_of_slot(slot): page.rows[slot].tile_data
               for slot in page.rows}
        for g in groups:
            if all(g["art"].get(i, d) == d for i, d in art.items()):
                g["art"].update(art)
                g["pages"].append(page)
                break
        else:
            groups.append({"art": dict(art), "pages": [page]})
    return [g["pages"] for g in groups]


def collect_banks(pack: Pack, pages: list[Page]) -> list[Bank]:
    unknown = bank_identity_unknown(pages)
    groups = collections.defaultdict(list)
    homeless = []
    for p in pages:
        # The blank-tile bucket borrows a real bank's ids; keep it on its own.
        if p.chr_bank_id is None or p.name.startswith("Chr_FFFFFFFF"):
            groups["?" + p.name].append(p)
        elif unknown and p.is_chr_ram and not p.chr_bank_id:
            homeless.append(p)
        else:
            groups[p.chr_bank_id].append(p)

    banks = []
    for k, v in groups.items():
        banks.append(Bank(v[0].chr_bank_id if isinstance(k, str) else k, v))
    for v in _regroup_without_hashes(homeless):
        bank = Bank(0, v)
        bank.identity_known = False
        bank.notes.append(
            "this pack records no CHR bank hash (an older recorder); this bank is "
            "the largest set of pages that never disagree about a tile index, "
            "which is what a bank's palette-variant pages look like")
        banks.append(bank)
    banks.sort(key=lambda b: (b.kind != "chrRom" and b.kind != "chrRam",
                              -len(b.art), b.primary.name))
    return banks


# --- palette recovery -------------------------------------------------------


def bitpairs(data: bytes):
    """The 8x8 grid of 2-bit colour numbers of a 16-byte NES tile."""
    out = []
    for y in range(8):
        lo, hi = data[y], data[y + 8]
        out.append([((lo >> (7 - x)) & 1) | (((hi >> (7 - x)) & 1) << 1) for x in range(8)])
    return out


def palette_indices(palette_hex: str):
    p = int(palette_hex, 16)
    return [(p >> ((3 - c) * 8)) & 0x3F for c in range(4)]


class PaletteTable:
    """NES palette index -> the exact RGBA the recorder wrote.

    Read off the recorded cells rather than assumed, so a pack built with a
    custom palette (or a different channel order) completes correctly. The 2C02
    table is the fallback for an index the pack never exhibits; its channel
    order is fitted against the observations."""

    def __init__(self):
        self.observed: dict[int, tuple] = {}
        self.order_hits = 0
        self._votes = collections.defaultdict(collections.Counter)
        self._order = None

    def observe(self, index: int, rgba: tuple):
        self._votes[index][rgba] += 1

    def finish(self):
        self.observed = {i: c.most_common(1)[0][0] for i, c in self._votes.items()}
        self._order = self._fit_order()

    def _fit_order(self):
        orders = {
            "rgba": lambda v: ((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF, (v >> 24) & 0xFF),
            "bgra": lambda v: (v & 0xFF, (v >> 8) & 0xFF, (v >> 16) & 0xFF, (v >> 24) & 0xFF),
        }
        best, best_hits = "rgba", -1
        for name, f in orders.items():
            hits = sum(1 for i, rgba in self.observed.items()
                       if f(DEFAULT_PALETTE_ARGB[i & 0x3F]) == rgba)
            if hits > best_hits:
                best, best_hits = name, hits
        self.order_hits = best_hits
        self.order_name = best
        return orders[best]

    def rgba(self, index: int):
        if index in self.observed:
            return self.observed[index]
        return self._order(DEFAULT_PALETTE_ARGB[index & 0x3F])


def _pattern(row: TileRow):
    return bytes.fromhex(row.tile_data) if row.tile_data else None


def pattern_of(bank: Bank, index: int, rom: Rom):
    """The 16 pattern bytes of this bank's tile `index`, when they are known.

    Falls back to a donated cell (`--also`): a second run of the same ROM having
    drawn that index is evidence about this bank's contents just as much as this
    run's own cell is, so it may anchor a PRG block like any other."""
    hit = bank.art.get(index)
    if hit is None:
        donation = bank.donated.get(index)
        hit = (donation["page"], donation["slot"]) if donation else None
    if hit is not None:
        page, slot = hit
        row = page.rows[slot]
        if row.tile_data:
            return bytes.fromhex(row.tile_data)
        return rom.chr_tile(row.tile_index)
    return None


def learn_palette(banks, images, rom: Rom) -> PaletteTable:
    """Walk every recorded cell's reference pixels and record, per NES palette
    index, the RGBA the recorder used — and per bank, whether colour 0 is drawn
    transparent (a sprite bank) or opaque."""
    table = PaletteTable()
    for bank in banks:
        votes = collections.Counter()
        transparent_rgba = None
        for page in bank.pages:
            orig = images[page.name]["orig"]
            if orig is None:
                continue
            s = page.scale
            for slot, row in page.rows.items():
                # CHR ROM rows carry an index, not the bytes; resolve it.
                data = _pattern(row) or (rom.chr_tile(row.tile_index)
                                         if row.tile_index is not None else None)
                if data is None:
                    continue
                pal = palette_indices(row.palette)
                grid = bitpairs(data)
                ox, oy = page.xy(slot)
                for ty in range(8):
                    for tx in range(8):
                        rgba = orig.get(ox + tx * s, oy + ty * s)
                        c = grid[ty][tx]
                        if c == 0:
                            votes[rgba[3] == 0] += 1
                            if rgba[3] == 0:
                                transparent_rgba = rgba
                                continue
                        table.observe(pal[c], rgba)
        # Mixed or opaque: fill opaque. An opaque fill can only ever be too
        # solid; a wrongly transparent one punches a hole in a background, the
        # one failure an artist cannot see coming.
        bank.transparent_rgba = transparent_rgba if votes[True] > votes[False] else None
    table.finish()
    return table


# --- second recordings (--also) ---------------------------------------------


def _real_sha1(h) -> bool:
    """A `<supportedRom>` that actually identifies a ROM. A pack recorded before
    the builder filled the field in carries 40 zeros, which identifies nothing."""
    return bool(h) and len(h) == 40 and set(h) != {"0"}


def donor_paths(also, pack_dir: Path) -> tuple[list[Path], list[str]]:
    """Resolve `--also` into the recordings it actually names.

    A caller that builds the list as "the whole set, and then also the whole
    set" hands back the pack it passed positionally, and a list assembled from
    several sources can repeat one. Neither adds a recording: the primary pack
    is already the pack the kit is for (ADR-0183 §1), and of two entries naming
    one folder the first is the one that donates (`attach_donors` pairs each
    bank with the first donor that has it). So a repeat is dropped rather than
    refused — refusing it named no way out and read as if the pack were invalid
    — and the manifest says which argument was ignored (#275)."""
    # Resolved on both sides: `run()` may be called with a path that still holds
    # a `..` or sits behind a symlink (/var against /private/var on macOS), and a
    # repeat that survives on a string difference is a repeat the pack gets
    # twice.
    primary = pack_dir.resolve()
    paths, notes, seen = [], [], {primary}
    for p in also:
        path = Path(p).resolve()
        if path not in seen:
            seen.add(path)
            paths.append(path)
        elif path == primary:
            notes.append(
                f"--also {path}: the pack itself, and the positional pack is the one "
                "this kit is for — a redundant argument, ignored.")
        else:
            notes.append(
                f"--also {path}: listed more than once, and the first one listed is the "
                "one that donates — the repeat is ignored.")
    return paths, notes


class Donor:
    """A second recording of the same ROM, read only as evidence for holes.

    Never a second source of truth (ADR-0183 §1): a donor is not merged into the
    pack, contributes no `hires.txt` rule, and cannot displace a cell the primary
    pack recorded itself. It is refused outright unless it is provably the same
    game — the same check the ROM itself gets, applied between the two packs.
    Being the primary pack itself is not one of those refusals: `donor_paths`
    has already taken it out of the list."""

    def __init__(self, path: Path, primary: Pack, primary_path: Path):
        self.path = path
        self.label = str(path)
        self.pack = Pack(path)
        if not _real_sha1(primary.rom_sha1) or not _real_sha1(self.pack.rom_sha1) \
                or primary.rom_sha1 != self.pack.rom_sha1:
            raise ChrKitError(
                f"--also {path}: recorded from sha1 "
                f"{(self.pack.rom_sha1 or '(none)').upper()}, while {primary_path} was "
                f"recorded from sha1 {(primary.rom_sha1 or '(none)').upper()} — a second "
                "recording is only evidence about this pack when it is provably the same "
                "ROM, and a pack with no <supportedRom> proves nothing")
        if self.pack.scale != primary.scale:
            raise ChrKitError(
                f"--also {path}: recorded at scale {self.pack.scale}, this pack at scale "
                f"{primary.scale} — a donated cell is pasted from the donor's own page, so "
                "the two must share a cell size")
        self.pages = collect_pages(self.pack)
        self.banks = collect_banks(self.pack, self.pages)
        self.by_id = {}
        for b in self.banks:
            if b.kind in ("chrRom", "chrRam") and b.identity_known and b.id is not None:
                self.by_id.setdefault(b.id, b)
        self.cells = 0
        self._images = {}

    def image(self, page: Page, which: str):
        key = (page.name, which)
        if key not in self._images:
            if which == "hd":
                self._images[key] = read_png(page.path)
            else:
                ref = page.path.parent / (page.name + ".orig.png")
                self._images[key] = read_png(ref) if ref.is_file() else None
        return self._images[key]


def attach_donors(banks: list[Bank], donors: list[Donor]) -> list[str]:
    """Fill each real bank's holes with cells a donor run recorded for the same
    CHR bank.

    Banks are paired by their CHR bank id, which is a content hash of the bank on
    a CHR RAM game and the bank number on a CHR ROM one — either way it is a
    property of the ROM, so it is the same id in every recording of it. A bank
    whose identity the pack does not record (all-zero hashes, partition recovered
    structurally) is never paired: two structurally-recovered groups of two
    different runs are not known to be the same pattern table.

    The first `--also` that has a cell wins, so donor order on the command line
    is the donor precedence. Nothing here can touch an index `bank.art` holds."""
    notes = []
    paired = set()
    for bank in banks:
        if bank.kind not in ("chrRom", "chrRam"):
            continue
        if not bank.identity_known or bank.id is None:
            if donors:
                bank.notes.append(
                    "this bank's identity is not recorded, so no --also recording could "
                    "be paired with it — a donated cell needs a bank both runs agree on")
            continue
        for donor in donors:
            src = donor.by_id.get(bank.id)
            if src is None or src.is_chr_ram != bank.is_chr_ram:
                continue
            paired.add((donor.label, bank.id))
            for index in sorted(src.art):
                if index in bank.art or index in bank.donated:
                    continue
                page, slot = src.art[index]
                bank.donated[index] = {"donor": donor, "page": page, "slot": slot}
                donor.cells += 1
    for donor in donors:
        missing = sorted(bid for bid in donor.by_id if (donor.label, bid) not in paired)
        if missing:
            notes.append(
                f"{donor.label}: {len(missing)} CHR bank(s) of that recording "
                f"({', '.join(str(b) for b in missing)}) have no counterpart in this pack "
                "and were ignored — it visited pattern tables this run never loaded")
    return notes


# --- fill sources -----------------------------------------------------------


def degenerate(data: bytes) -> bool:
    """A tile whose 16 bytes are all the same value — blank, solid, and present
    thousands of times in any PRG, so useless as a landmark."""
    return len(set(data)) <= 1


def fill_from_chr_rom(bank: Bank, rom: Rom):
    """CHR ROM: the bank *is* 4 KB of the file, so every index is readable."""
    base = bank.id * 256
    for index in range(256):
        if index in bank.art or index in bank.donated:
            continue
        data = rom.chr_tile(base + index)
        if data is None:
            continue
        bank.fills[index] = {"data": data, "origin": "chrRom",
                             "romTileIndex": base + index}


def fill_from_prg(bank: Bank, rom: Rom, stats):
    """CHR RAM: the recorded cells have to point at the PRG block they came from
    before any hole beside them can be read out of it.

    A *base* is an offset `b` with `PRG[b + 16*i : +16] == tile i` for the bank's
    recorded tiles `i`. Bases are seeded from where each non-degenerate recorded
    tile occurs in the PRG, then scored by how many other recorded tiles they
    explain. A hole `j` is filled from the best-supported base whose supporting
    tiles sit within `LOCALITY_WINDOW` indices of it: a block copy is
    contiguous, so a base vouched for a few cells away is evidence about `j`,
    while the same base vouched for at the far end of the bank is not.

    Where a game *unpacks* its graphics, no base reaches the threshold and the
    bank stays mostly empty. That is the honest answer, not a failure to try."""
    seen = {}
    for index in list(bank.art) + list(bank.donated):
        data = pattern_of(bank, index, rom)
        if data:
            seen[index] = data
    nondegenerate = {i: d for i, d in seen.items() if not degenerate(d)}
    if not nondegenerate:
        bank.notes.append("no distinguishable recorded tile — nothing to anchor a PRG block on")
        return

    index_map = rom.prg_index()
    prg = rom.prg
    candidates = set()
    for i, data in nondegenerate.items():
        offsets = index_map.get(data, ())
        if len(offsets) > 64:      # too common to be a landmark
            continue
        for o in offsets:
            b = o - 16 * i
            if 0 <= b <= len(prg) - 16:
                candidates.add(b)

    trusted = []
    for b in candidates:
        support = [i for i, d in nondegenerate.items()
                   if 0 <= b + 16 * i <= len(prg) - 16 and prg[b + 16 * i:b + 16 * i + 16] == d]
        if len(support) >= MIN_BASE_SUPPORT:
            trusted.append((b, sorted(support)))
    stats["bases"] += len(trusted)
    if not trusted:
        bank.notes.append(
            f"no PRG block explains this bank ({len(nondegenerate)} recorded tiles tried) — "
            "the game unpacks its graphics")
        return

    for j in range(256):
        if j in bank.art or j in bank.donated:
            continue
        best = None
        for b, support in trusted:
            o = b + 16 * j
            if not (0 <= o <= len(prg) - 16):
                continue
            distance = min(abs(i - j) for i in support)
            if distance > LOCALITY_WINDOW:
                continue
            score = (distance, -len(support))
            if best is None or score < best[0]:
                best = (score, b, o, support)
        if best is None:
            continue
        _, b, o, support = best
        data = prg[o:o + 16]
        rival = any(0 <= b2 + 16 * j <= len(prg) - 16
                    and prg[b2 + 16 * j:b2 + 16 * j + 16] != data
                    and min(abs(i - j) for i in s2) <= LOCALITY_WINDOW
                    for b2, s2 in trusted if b2 != b)
        if rival:
            stats["ambiguous"] += 1
        bank.fills[j] = {"data": data, "origin": "prg", "romOffset": o,
                         "baseOffset": b, "baseSupport": len(support),
                         "ambiguous": rival}


# --- rendering --------------------------------------------------------------


def render_cell(data: bytes, palette_hex: str, table: PaletteTable,
                transparent_rgba, scale: int) -> Image:
    """An 8x8 tile at the page's scale, nearest neighbour.

    The recorder runs its own cells through a smoothing filter; a fill is a
    guess, and leaving it crisp both avoids inventing edges the ROM does not
    have and makes a fill recognisable at a glance next to recorded art."""
    pal = palette_indices(palette_hex)
    grid = bitpairs(data)
    tile = Image(8, 8)
    for y in range(8):
        for x in range(8):
            c = grid[y][x]
            tile.set(x, y, transparent_rgba if (c == 0 and transparent_rgba is not None)
                     else table.rgba(pal[c]))
    return tile.upscale(scale)


def legend_image(page: Page, states: dict) -> Image:
    """A drop-behind overlay: green over a recorded cell, olive over one moved up
    from a lower-ranked page, blue over one donated by another recording, violet
    over one folded onto another cell (same picture, another brightness - do not
    paint it), amber over a ROM fill, red over a hole. One file per page so an
    artist can toggle it as a layer."""
    n = page.cell_px
    img = Image(16 * n, 16 * n)
    colour = {"evidence": LEGEND_EVIDENCE, "borrowed": LEGEND_BORROWED,
              "donated": LEGEND_DONATED, "folded": LEGEND_FOLDED,
              "fill": LEGEND_FILL, "empty": LEGEND_EMPTY}
    for slot in range(256):
        rgba = colour[states.get(slot, "empty")]
        x, y = page.xy(slot)
        for yy in range(y, y + n):
            for xx in range(x, x + n):
                edge = (xx - x < 2 or yy - y < 2 or x + n - xx <= 2 or y + n - yy <= 2)
                img.set(xx, yy, rgba if edge else (rgba[0], rgba[1], rgba[2], 0x40))
    return img


# --- palette folding (F9.27 follow-up) --------------------------------------

# A recorded pack keys a cell by (pattern, palette), so one drawing comes back
# once per palette the run saw it wearing. On the TAS Zelda recording that is
# 4712 cells for 1642 distinct patterns, because a screen fade gives every tile
# on screen a key per step of the fade.
#
# `HdPackLoader` has carried a per-`<tile>` **Brightness** since version 105
# (`HdPackLoader.cpp:513`, applied by `HdNesPack::AdjustBrightness`), and the
# builder has never written anything but 255 (`HdPackBuilder.cpp:337/391/464`).
# So the renderer can already reconstruct a fade step from one painted cell,
# and this module decides which cells that is true of.
#
# THE RULE, read off the NES palette and never off the picture:
#
#   NES colour byte c: row = c >> 4, hue = c & 0x0F. The four rows of one hue
#   column ARE the console's brightness ramp for that colour
#   ($2C -> $1C -> $0C -> $0F). Ten indices render pure black in the 2C02 table
#   ($0D-$0F, $1D-$1F, $2E, $2F, $3E, $3F); a black entry carries no hue and is
#   a wildcard.
#
#   Only the palette indices the tile ACTUALLY PAINTS are compared - a pattern
#   that paints colours 0 and 3 does not care what 1 and 2 hold.
#
#   INERT  - the two palettes render the pattern to identical RGB. No judgement
#            at all; the cells are the same picture.
#   FADE   - every painted entry keeps its hue and none gets brighter, i.e. the
#            game moved those colours down their own ramps.
#   Neither - a painted entry changed hue. A different picture. Collapsing a
#            green enemy onto a red one would destroy evidence (ADR-0183 3),
#            so it is never folded, however close the two happen to render.
#
# The rule is a precondition, not the decision. `AdjustBrightness` is a single
# RGB multiplier, and the one thing a multiplier can never do is change hue, so
# each candidate is also measured: the least-squares multiplier that takes the
# base to the variant, and the largest hue angle between them. Anything past
# HUE_DRIFT_GATE_DEG is left as its own cell. Both numbers are written into the
# cell's sidecar entry, and the per-page distribution into `fold.driftHistogram`,
# so the gate is retunable from the data a kit already carries rather than from
# another recording run.
#
# Measured on the corpus (2026-09-14), fraction of the rows-over-patterns gap
# this removes: Contra 77 %, Mega Man 3 58 %, Gauntlet 57 %, Ninja Gaiden 53 %,
# Zelda TAS 44 %, Castlevania 17 %. Zelda TAS: 4712 cells -> 3372, and the
# deepest CHR slot goes from 35 variants to 16.
#
# What this does NOT do is collapse a pattern to one cell. 1642 is Zelda's
# *pattern* count, not its picture count: its most-repeated patterns carry ~7
# hue families (grey, olive $x8, green $xB, cyan $xC, red $x6, blue $x2, brown
# $x7), each a 3-5 step ramp. 30 keys become 7 cells, not 1.

HUE_DRIFT_GATE_DEG = 25.0


def _nes_rgb(c):
    v = DEFAULT_PALETTE_ARGB[c & 0x3F]
    return ((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF)


def _is_black(c):
    return _nes_rgb(c) == (0, 0, 0)


def _hue(c):
    return c & 0x0F


def _level(c):
    """0 for black, else the palette row + 1 - the rung of the hue's ramp."""
    return 0 if _is_black(c) else (c >> 4) + 1


def _luma(c):
    r, g, b = _nes_rgb(c)
    return 0.299 * r + 0.587 * g + 0.114 * b


def palette_entries(palette: str):
    """`"0F0B1B2B"` -> the four NES colour indices, colour 0 first."""
    v = int(palette, 16)
    return [(v >> ((3 - k) * 8)) & 0x3F for k in range(4)]


def painted_indices(tile_data: str):
    """Which of the four palette indices the 8x8 pattern actually paints.

    `tile_data` is the 32 hex characters a CHR RAM `<tile>` row carries. A CHR
    ROM row carries only an index, and then the caller has to assume all four -
    which is the conservative direction: more entries have to agree on hue, so
    fewer cells fold."""
    d = [int(tile_data[i * 2:i * 2 + 2], 16) for i in range(16)]
    used = set()
    for i in range(8):
        lo, hi = d[i], d[i + 8]
        for j in range(8):
            used.add(((lo >> (7 - j)) & 1) | (((hi >> (7 - j)) & 1) << 1))
    return sorted(used)


def fade_related(base, other, used) -> bool:
    """`other` is `base` further down the same ramps: no painted entry changed
    hue, none got brighter, and at least one moved."""
    moved = False
    for k in used:
        a, b = base[k], other[k]
        if _is_black(a) and _is_black(b):
            continue
        if _is_black(a) or _is_black(b):
            moved = True
            continue
        if _hue(a) != _hue(b):
            return False
        if _level(b) > _level(a):
            return False
        if _level(b) < _level(a):
            moved = True
    return moved


def fold_measure(base, other, used):
    """(brightness, max hue drift in degrees) for folding `other` onto `base`.

    `brightness` is the least-squares single multiplier, in the 0..1 units the
    `<tile>` column uses (1 = the loader's 255). The drift is the largest angle
    between a painted entry's two RGB vectors - the part of the residual a
    multiplier can never remove. Black entries carry no direction and are
    skipped."""
    num = den = 0.0
    for k in used:
        a, b = _nes_rgb(base[k]), _nes_rgb(other[k])
        for i in range(3):
            num += a[i] * b[i]
            den += a[i] * a[i]
    brightness = 0.0 if den == 0 else num / den
    drift = 0.0
    for k in used:
        a, b = _nes_rgb(base[k]), _nes_rgb(other[k])
        na = math.sqrt(sum(x * x for x in a))
        nb = math.sqrt(sum(x * x for x in b))
        if na == 0 or nb == 0:
            continue
        cos = sum(x * y for x, y in zip(a, b)) / (na * nb)
        drift = max(drift, math.degrees(math.acos(max(-1.0, min(1.0, cos)))))
    return max(0.0, min(4.0, brightness)), drift


def compute_folds(pages, gate: float = HUE_DRIFT_GATE_DEG):
    """Fold every page's palette variants of one pattern onto one painted cell.

    Pack-wide, not per bank: a pack whose CHR bank hashes are all zero has its
    pages regrouped structurally (`_regroup_without_hashes`), so the variants of
    one pattern routinely sit in different Bank objects. The key this folds is
    (pattern, palette), which is global.

    Returns `(folded, bases, stats)`:
      folded[(page name, slot)] = {"kind", "brightness", "drift", "base*"}
      bases[(page name, slot)]  = [that cell's folded entries]
    """
    groups = collections.defaultdict(list)
    for page in pages:
        for slot, row in page.rows.items():
            ident = row.tile_data or f"#{page.index_of_slot(slot)}"
            groups[ident].append((page, slot, row))

    folded, bases = {}, collections.defaultdict(list)
    stats = {"inert": 0, "fade": 0, "kept": 0, "refused": 0,
             "driftHistogram": collections.Counter()}

    for ident, members in groups.items():
        data = members[0][2].tile_data
        used = painted_indices(data) if data else [0, 1, 2, 3]
        if not used:
            used = [0]
        # One member per palette, brightest first; a member of the fullest page
        # wins a tie, so the cell an artist opens first tends to be the base.
        by_palette = {}
        for page, slot, row in members:
            by_palette.setdefault(row.palette, (page, slot, row))
        order = sorted(
            by_palette.values(),
            key=lambda m: (-sum(_luma(palette_entries(m[2].palette)[k]) for k in used),
                           -len(m[0].rows), m[0].name, m[1]))
        if len(order) < 2:
            stats["kept"] += 1
            continue

        # INERT first: palettes that render this pattern to identical RGB are
        # the same picture, and folding them is not a judgement.
        reps, inert_of = [], collections.defaultdict(list)
        seen = {}
        for m in order:
            sig = tuple(_nes_rgb(palette_entries(m[2].palette)[k]) for k in used)
            at = seen.get(sig)
            if at is None:
                seen[sig] = len(reps)
                reps.append(m)
            else:
                inert_of[at].append(m)

        # FADE: greedy from the brightest representative.
        taken = [False] * len(reps)
        for i, base in enumerate(reps):
            if taken[i]:
                continue
            taken[i] = True
            stats["kept"] += 1
            bkey = (base[0].name, base[1])
            base_pal = palette_entries(base[2].palette)

            def _fold(member, kind, brightness, drift):
                entry = {"kind": kind, "palette": member[2].palette,
                         "brightness": round(brightness, 4), "drift": round(drift, 1),
                         "basePage": base[0].name, "baseSlot": base[1],
                         "basePalette": base[2].palette}
                folded[(member[0].name, member[1])] = entry
                bases[bkey].append(entry)
                stats[kind] += 1
                stats["driftHistogram"][int(drift) // 5 * 5] += 1

            for m in inert_of[i]:
                _fold(m, "inert", 1.0, 0.0)
            for j in range(i + 1, len(reps)):
                if taken[j]:
                    continue
                other = reps[j]
                other_pal = palette_entries(other[2].palette)
                if not fade_related(base_pal, other_pal, used):
                    continue
                brightness, drift = fold_measure(base_pal, other_pal, used)
                if drift > gate:
                    # Same ramps, but too much of the residual is hue for a
                    # multiplier to carry. Left as its own cell, and counted.
                    stats["refused"] += 1
                    continue
                taken[j] = True
                _fold(other, "fade", brightness, drift)
                for m in inert_of[j]:
                    _fold(m, "inert", brightness, drift)
    return folded, bases, stats


# --- writing ----------------------------------------------------------------


def _fill_rule(page: Page, index: int, palette: str, data: bytes,
               is_chr_ram: bool, bank_id) -> str:
    """A `<tile>` row for a filled cell, in the recorder's own column order.

    Written to `<out>/chr/fill-rules.hires.txt` and never into the recorded
    pack: see the module docstring and `--fill-rules`."""
    x, y = page.xy(page.slot_of_index(index))
    if is_chr_ram:
        return (f"<tile>{page.image_index},{data.hex().upper()},{palette},"
                f"{x},{y},1,N,{bank_id},{index}")
    return (f"<tile>{page.image_index},{bank_id * 256 + index:02X},{palette},"
            f"{x},{y},1,Y")


def _fold_block(cells):
    """What this page's folds did, in the page's own sidecar.

    The drift histogram is per page rather than only per pack on purpose: how
    loose the fold got is a property of the game (measured worst drift is 0 deg
    on Contra and 45 deg on Gauntlet), so whoever opens a Gauntlet kit sees it
    there instead of inferring it from a global constant."""
    folded = [c for c in cells if c["state"] == "folded"]
    absorbed = [f for c in cells for f in c.get("folds") or []]
    hist = collections.Counter()
    for f in folded:
        hist[int(f["drift"]) // 5 * 5] += 1
    return {
        "gateDegrees": HUE_DRIFT_GATE_DEG,
        "foldedAway": len(folded),
        "inert": sum(1 for f in folded if f["kind"] == "inert"),
        "fade": sum(1 for f in folded if f["kind"] == "fade"),
        "absorbedHere": len(absorbed),
        "maxDrift": max((f["drift"] for f in folded), default=0.0),
        "driftHistogram": {str(k): v for k, v in sorted(hist.items())},
    }


def write_bank(bank: Bank, images, table, transparent_rgba, out_chr: Path,
               names, pack_keys, fill_rules, rom, donors=(), folds=None):
    """Write every page of the bank: the rank-0 page completed, the rest copied
    through unchanged so `<out>/chr/` is a drop-in for `textures/chr/`."""
    entries, rules = [], []
    palette = bank.fill_palette()
    primary = bank.primary
    # The `donated` counters are only emitted when the run was given a second
    # recording at all, so a run without `--also` writes exactly the bytes it
    # wrote before this feature existed.
    with_donors = bool(donors)
    # (pattern, palette) cells this pack folds onto another cell, and the folds
    # each surviving cell absorbed - computed pack-wide by `compute_folds`.
    folded_cells, fold_bases = folds if folds else ({}, {})

    for page in bank.pages:
        hd = images[page.name]["hd"].clone()
        orig = images[page.name]["orig"]
        orig = orig.clone() if orig is not None else None
        states = {}
        cells = []
        guessed = 0

        # ADR-0183 §3, in one chain and in this order: this pack's own recorded
        # cell, then a cell this bank recorded on a lower-ranked page, then a
        # cell another recording of the same ROM recorded, then a ROM fill, then
        # nothing. Evidence before inference, and this pack's evidence first.
        for slot in range(256):
            index = page.index_of_slot(slot)
            x, y = page.xy(slot)
            entry = {"slot": slot, "index": index, "x": x, "y": y}
            if slot in page.rows:
                row = page.rows[slot]
                entry.update(state="evidence", seen=True, palette=row.palette)
                if row.tile_data:
                    entry["tileData"] = row.tile_data
                else:
                    entry["tileIndex"] = row.tile_index
                states[slot] = "evidence"
                fold = folded_cells.get((page.name, slot))
                if fold is not None:
                    # Still this pack's evidence, and its pixels are untouched:
                    # what changes is that the artist does not have to paint it.
                    # The cell it folds into carries the picture, and the
                    # renderer rebuilds this one with Brightness (ADR-0183 3:
                    # the recording keeps every key either way).
                    entry.update(state="folded", **fold)
                    states[slot] = "folded"
                absorbed = fold_bases.get((page.name, slot))
                if absorbed:
                    entry["folds"] = absorbed
            elif page is primary and index in bank.art:
                # Recorded art of this very bank and index, sitting on a
                # lower-ranked page under another palette. Real evidence, moved
                # onto the page an artist opens; the pattern is certain.
                src_page, src_slot = bank.art[index]
                sx, sy = src_page.xy(src_slot)
                hd.paste(images[src_page.name]["hd"].crop(sx, sy, page.cell_px, page.cell_px), x, y)
                if orig is not None and images[src_page.name]["orig"] is not None:
                    orig.paste(images[src_page.name]["orig"].crop(sx, sy, page.cell_px, page.cell_px), x, y)
                row = src_page.rows[src_slot]
                entry.update(state="borrowed", seen=True, palette=row.palette,
                             sourcePage=src_page.name, sourceSlot=src_slot)
                if row.tile_data:
                    entry["tileData"] = row.tile_data
                else:
                    entry["tileIndex"] = row.tile_index
                states[slot] = "borrowed"
            elif page is primary and index in bank.donated:
                # Another recording of the same ROM drew this tile. A real run
                # really rendered it, so `seen` stays true — but it is not this
                # pack's evidence, so the donor is named here and the cell gets
                # its own legend colour.
                donation = bank.donated[index]
                donor, src_page, src_slot = (donation["donor"], donation["page"],
                                             donation["slot"])
                sx, sy = src_page.xy(src_slot)
                hd.paste(donor.image(src_page, "hd").crop(sx, sy, page.cell_px, page.cell_px), x, y)
                src_orig = donor.image(src_page, "orig")
                if orig is not None and src_orig is not None:
                    orig.paste(src_orig.crop(sx, sy, page.cell_px, page.cell_px), x, y)
                row = src_page.rows[src_slot]
                entry.update(state="donated", seen=True, palette=row.palette,
                             sourcePack=donor.label, sourcePage=src_page.name,
                             sourceSlot=src_slot)
                if row.tile_data:
                    entry["tileData"] = row.tile_data
                else:
                    entry["tileIndex"] = row.tile_index
                states[slot] = "donated"
            elif page is primary and index in bank.fills:
                fill = bank.fills[index]
                data = fill["data"]
                cell = render_cell(data, palette, table, transparent_rgba, page.scale)
                hd.paste(cell, x, y)
                if orig is not None:
                    orig.paste(cell, x, y)
                key = ((data.hex().upper(), palette) if bank.is_chr_ram
                       else (f"#{bank.id * 256 + index}", palette))
                observed = key in pack_keys
                if not observed:
                    guessed += 1
                if fill_rules == "all" or (fill_rules == "observed" and observed):
                    rules.append(_fill_rule(page, index, palette, data,
                                            bank.is_chr_ram, bank.id))
                entry.update(state="fill", seen=False, palette=palette,
                             paletteObserved=observed, origin=fill["origin"],
                             tileData=data.hex().upper())
                if fill["origin"] == "prg":
                    entry.update(romOffset=fill["romOffset"],
                                 baseOffset=fill["baseOffset"],
                                 baseSupport=fill["baseSupport"])
                    if fill["ambiguous"]:
                        entry["ambiguous"] = True
                else:
                    entry["tileIndex"] = fill["romTileIndex"]
                states[slot] = "fill"
            else:
                entry.update(state="empty", seen=False)
                states[slot] = "empty"
            cells.append(entry)

        ora_writer.write_chr_surface(  # page PNG + twin + layered .ora, one pass (ADR-0220 §1)
            out_chr, N.require_asset_name(page.name + N.SURFACE_EXT, __name__), hd, orig, cells, page)
        write_png(out_chr / f"{page.name}.legend.png", legend_image(page, states))

        counts = collections.Counter(c["state"] for c in cells)
        cell_counts = {"evidence": counts["evidence"], "borrowed": counts["borrowed"],
                       "folded": counts["folded"],
                       "fill": counts["fill"], "empty": counts["empty"]}
        if with_donors:
            cell_counts["donated"] = counts["donated"]
        sidecar = {
            "version": 1, "kind": "chr", "generator": "scripts/artist_chr_kit.py",
            "gridUnit": 8, "cell": {"w": 8, "h": 8}, "columns": 16, "rows": 16,
            "scale": page.scale,
            "sheet": f"{page.name}.png", "reference": f"{page.name}.orig.png",
            "legend": f"{page.name}.legend.png",
            "bankKind": bank.kind, "chrBankId": bank.id,
            "variantRank": bank.pages.index(page),
            "completed": page is primary and bool(bank.fills or counts["borrowed"]
                                                  or counts["donated"]),
            "layout": page.layout, "fillPalette": palette,
            "spriteTransparency": transparent_rgba is not None,
            "counts": cell_counts,
            "fold": _fold_block(cells),
            "paletteGuessed": guessed,
            "notes": list(bank.notes) if page is primary else [],
            "cells": cells,
        }
        (out_chr / f"{page.name}.json").write_text(
            json.dumps(sidecar, indent=1) + "\n", encoding="utf-8")

        title = names.get("pages", {}).get(page.name)
        if not title:
            bank_label = "blank-tile bucket" if bank.kind == "blankBucket" else (
                "PRG scan page" if bank.kind == "prgScan" else f"CHR bank {bank.id}")
            donated_label = f"{counts['donated']} donated / " if counts["donated"] else ""
            folded_label = (f"{counts['folded']} folded onto another cell / "
                            if counts["folded"] else "")
            title = (f"{page.name} — {bank_label}, variant rank "
                     f"{bank.pages.index(page)}, palette {page.palette}; "
                     f"{counts['evidence'] + counts['borrowed']} to paint / "
                     f"{folded_label}{donated_label}{counts['fill']} ROM fill / "
                     f"{counts['empty']} empty")
        entry_extra = {"donated": counts["donated"]} if with_donors else {}
        if counts["folded"]:
            entry_extra["folded"] = counts["folded"]
        entries.append({
            "path": f"chr/{page.name}.png", "title": title, "unit": "page",
            "rows": 16, "columns": 16, "cells": 256,
            "evidence": counts["evidence"] + counts["borrowed"],
            "fill": counts["fill"], "empty": counts["empty"],
            **entry_extra,
            "seen": counts["fill"] == 0,
            "reference": f"chr/{page.name}.orig.png",
            "legend": f"chr/{page.name}.legend.png",
            "sidecar": f"chr/{page.name}.json",
            "bankKind": bank.kind, "chrBankId": bank.id,
            "variantRank": bank.pages.index(page),
            "paletteGuessed": guessed,
        })
    return entries, rules


# --- verify -----------------------------------------------------------------


DEFAULT_STATIC_SCALE = 4


def static_notes(rom_path: Path, cells: int, filled: int, rules: int) -> list:
    """What a static kit has to say for itself, in the order it has to say it.

    The first note is the one that matters: no play session happened, so the
    pages are shape without evidence. Everything a recorded kit says about
    green/olive/blue cells is omitted rather than reworded — there are none."""
    return [
        f"NOTHING ON THESE PAGES WAS SEEN IN PLAY. They were projected over "
        f"{rom_path.name}'s own CHR with no recording at all (ADR-0219, PRD F12.9): "
        f"all {cells} cell(s) are `fill` and `seen: false` in the sidecars, and "
        "there is no figure sheet, no scenery sheet and no stage map, because "
        "those come from what a run observed and nothing was observed.",
        f"{filled} of {cells} cell(s) were read out of the file. A CHR ROM bank "
        "*is* 4 KB of the ROM, so the shape of every tile is exact — what is "
        "missing is not accuracy, it is organisation.",
        f"The colours are not. Every one of the {rules} <tile> row(s) in "
        "chr/fill-rules.hires.txt carries defaultTile=Y, the per-rule palette "
        "wildcard (ADR-0210): the shape matches whatever palette the game puts it "
        f"under, and the {STATIC_FILL_PALETTE} the page is *drawn* in is a "
        "placeholder for reading, never a claim.",
        "A fill is rendered nearest-neighbour: the recorder smooths its own cells "
        "and these were never recorded, so they are deliberately crisp.",
        "This kit is a folder, not a pack, and nothing installs it. To paint: edit "
        "a page, copy chr/ into a pack folder's textures/ and run "
        "`python3 scripts/mep_build.py build <pack>` — the build reads "
        "chr/fill-rules.hires.txt as the manifest and rebuilds textures/hires.txt "
        "from it.",
        "Recording the game later never becomes pointless: a run contributes the "
        "palettes a static page can only wildcard, plus the figures, scenery and "
        "maps this kit does not have. A recorded cell always wins over a fill.",
    ]


def verify_static(pack: Pack, out_chr: Path, rom: Rom, quiet=False) -> dict:
    """The static substitute for `verify`'s round-trip, and why it differs.

    ADR-0183 §4 accepts a surface when a rebuilt pack loses and invents no key
    *against the recording it came from*. A static kit has no recording, so
    "unchanged" is vacuous and faking it would be worse than useless. What
    survives of §4 is the half that is checkable here (ADR-0219, F12.9 stop
    condition 2): the pages-only pack builds with 0 errors and the rebuilt
    manifest carries exactly one `<tile>` rule per tile of the ROM's CHR, every
    one of them the `Y` wildcard."""
    result = {"ran": True, "errors": 0, "rules": 0, "expectedRules": rom.chr_tile_count,
              "wildcard": 0, "built": False}
    with tempfile.TemporaryDirectory(prefix="artist-chr-static-verify-") as tmp:
        target = Path(tmp) / "pack" / "textures" / "chr"
        target.parent.mkdir(parents=True)
        shutil.copytree(out_chr, target)
        proc = subprocess.run(
            [sys.executable, str(SCRIPT_DIR / "mep_build.py"), "build", str(target.parent.parent)],
            capture_output=True, text=True)
        result["built"] = proc.returncode == 0
        result["errors"] = sum(1 for ln in (proc.stdout + proc.stderr).splitlines()
                               if ln.startswith("error:"))
        if not result["built"] and not result["errors"]:
            # A non-zero exit with no `error:` line is still a failure, and
            # reporting 0 errors beside it would read as a pass.
            result["errors"] = 1
        built = target.parent / "hires.txt"
        if built.is_file():
            rows = [ln for ln in built.read_text().splitlines() if ln.startswith("<tile>")]
            result["rules"] = len(rows)
            result["wildcard"] = sum(1 for ln in rows if ln.rstrip().endswith(",Y"))
        if not quiet:
            print(f"  verify: build {'ok' if result['built'] else 'FAILED'}, "
                  f"{result['errors']} error(s), {result['rules']} <tile> rule(s) "
                  f"of {result['expectedRules']} expected, "
                  f"{result['wildcard']} carrying defaultTile=Y")
    return result


def verify(pack: Pack, out_chr: Path, quiet=False) -> dict:
    """Drop the completed pages into a copy of the pack, rebuild it, and prove
    nothing was lost.

    Four assertions, because `mep_build build` regenerates `hires.txt` from
    `textures/sheets/` alone and ignores `textures/chr/` entirely — a naive
    before/after on its output would pass no matter what this tool wrote:

    1. every recorded cell of every emitted page is byte-identical to the
       recorded page — that is what makes the pack's existing `<tile>` rules
       render exactly what they rendered before. A folded cell is checked the
       same way: folding changes what the kit *says* about a cell, never its
       pixels, so a pack rebuilt from an unpainted kit is unchanged;
    2. the patched pack's key set is a superset of the original's, nothing lost,
       every added key one of the `--fill-rules` rows;
    3. `mep_build build` runs on the patched copy with 0 errors;
    4. its output is byte-identical to a build of the untouched copy.
    """
    result = {"ran": True, "errors": 0, "keys_before": 0, "keys_after": 0,
              "lost": 0, "added": 0, "cells_checked": 0, "cells_differing": 0}
    with tempfile.TemporaryDirectory(prefix="artist-chr-verify-") as tmp:
        tmp = Path(tmp)
        base, cand = tmp / "base", tmp / "cand"
        shutil.copytree(pack.root, base)
        shutil.copytree(pack.root, cand)

        for png in sorted(out_chr.glob("Chr_*.png")):
            if png.name.endswith(".orig.png") or png.name.endswith(".legend.png"):
                continue
            original = pack.textures / "chr" / png.name
            if not original.is_file():
                continue
            a, b = read_png(original), read_png(png)
            if (a.width, a.height) != (b.width, b.height):
                raise ChrKitError(f"{png.name}: size changed")
            sidecar = json.loads((out_chr / (png.name[:-4] + ".json")).read_text())
            n = sidecar["scale"] * 8
            for cell in sidecar["cells"]:
                if cell["state"] not in ("evidence", "folded"):
                    continue
                result["cells_checked"] += 1
                x, y = cell["x"], cell["y"]
                same = all(a.px[a.offset(x, y + r):a.offset(x, y + r) + n * 4]
                           == b.px[b.offset(x, y + r):b.offset(x, y + r) + n * 4]
                           for r in range(n))
                if not same:
                    result["cells_differing"] += 1
            shutil.copy2(png, cand / "textures" / "chr" / png.name)
            ref = out_chr / (png.name[:-4] + ".orig.png")
            if ref.is_file():
                shutil.copy2(ref, cand / "textures" / "chr" / ref.name)
        if result["cells_differing"]:
            raise ChrKitError(
                f"{result['cells_differing']} recorded cell(s) changed — refusing to report success")

        rules = out_chr / "fill-rules.hires.txt"
        if rules.is_file():
            rows = [ln for ln in rules.read_text().splitlines() if ln.startswith("<tile>")]
            if rows:
                target = cand / "textures" / "hires.txt"
                target.write_text(target.read_text() + "\n".join(rows) + "\n", encoding="utf-8")

        before, after = pack.key_set(), Pack(cand).key_set()
        result.update(keys_before=len(before), keys_after=len(after),
                      lost=len(before - after), added=len(after - before))
        if result["lost"]:
            raise ChrKitError(f"{result['lost']} key(s) lost")

        built = {}
        for name, folder in (("base", base), ("cand", cand)):
            proc = subprocess.run(
                [sys.executable, str(SCRIPT_DIR / "mep_build.py"), "build", str(folder), "--quiet"],
                capture_output=True, text=True)
            errors = 0
            for line in proc.stdout.splitlines():
                m = re.search(r"(\d+) error\(s\)", line)
                if m:
                    errors = int(m.group(1))
            built[name] = (proc.returncode, errors)
            if name == "cand":
                result["errors"] = errors
                result["buildReturnCode"] = proc.returncode
                result["baselineReturnCode"] = built["base"][0]
                # `build` exits non-zero on the pack's pre-existing warnings, so
                # the assertion is "0 errors, and no worse than the untouched
                # pack", not "exit 0".
                if errors or proc.returncode != built["base"][0]:
                    raise ChrKitError(f"mep_build build regressed: rc={proc.returncode} "
                                      f"(baseline {built['base'][0]}), {errors} error(s) "
                                      f"(baseline {built['base'][1]})\n{proc.stdout[-2000:]}")
        result["rebuildIdentical"] = (
            (base / "textures" / "hires.txt").read_bytes()
            == (cand / "textures" / "hires.txt").read_bytes())
        if not quiet:
            print(f"verify: {result['cells_checked']} recorded cell(s) byte-identical; "
                  f"keys {result['keys_before']} -> {result['keys_after']} "
                  f"(lost {result['lost']}, added {result['added']}); "
                  f"mep_build build: {result['errors']} error(s); "
                  f"rebuilt hires.txt identical to the untouched pack's: "
                  f"{result['rebuildIdentical']}")
    return result


# --- main -------------------------------------------------------------------


def load_names(path):
    if not path:
        return {}
    data = json.loads(Path(path).read_text(encoding="utf-8"))
    # The shared schema names subjects, poses and cycles; a pattern page is none
    # of those, so a title is only ever taken from an explicit `pages` map. A
    # name is never invented here.
    return {"pages": data.get("pages", {})}


_PASSTHROUGH_WHY = {
    "prgScan": "they are the recorder's own PRG scan (synthetic bank 0x{id:08X}), "
               "already ROM-sourced",
    "blankBucket": "they are the builder's blank-tile bucket, not a pattern page",
    "unreadable": "their cell layout matches neither of the builder's",
}


def static_input(pack_dir: Path, rom: Rom, rom_path: Path, scale: int, also) -> Pack:
    """The three refusals of the static path, then the Pack the ROM defines.

    Each refusal is the ADR's, not a convenience: a CHR RAM game has no static
    source of shape here at all (its recovery pins PRG blocks against *recorded*
    tiles, and there are none), `--also` presupposes a primary with cells to
    donate to, and a folder that already holds a recording is a recorded pack —
    running the static path over it would replace evidence with inference, which
    is the one thing ADR-0183 §3 forbids."""
    if not rom.has_chr_rom:
        raise ChrKitError(
            f"{rom_path.name}: a CHR RAM game has no CHR in the file, so a static kit "
            "has nothing to read — its pattern tables are built at run time from PRG "
            "data, and pinning a PRG block needs recorded tiles. The only static source "
            "of shape for these games is a third-party key index read as facts: "
            "`scripts/mep_import.py index` (ADR-0210 §3, PRD F12.12)")
    if also:
        raise ChrKitError(
            "--also is another recording used as evidence for a recorded pack's holes; "
            "--static has no recording for it to attach to")
    if (pack_dir / "textures" / "hires.txt").is_file():
        raise ChrKitError(
            f"{pack_dir}: this folder holds a recording (textures/hires.txt) — run "
            "without --static to complete it from the ROM. A static kit is what a kit "
            "looks like when there is no recording at all")
    rom_sha1 = hashlib.sha1(rom_path.read_bytes()).hexdigest().lower()
    pack = Pack(pack_dir, static=rom, scale=scale, rom_sha1=rom_sha1)
    if not pack.images:
        raise ChrKitError(f"{rom_path.name}: CHR ROM smaller than one 4 KB bank")
    return pack


def run(pack_dir: Path, rom_path: Path, out_dir: Path, names_path, fill_rules,
        do_verify, quiet, also=(), static=False, scale=DEFAULT_STATIC_SCALE):
    rom = Rom(rom_path)
    if static:
        pack = static_input(pack_dir, rom, rom_path, scale, also)
    else:
        pack = Pack(pack_dir)
    names = load_names(names_path)

    pages = static_pages(pack) if static else collect_pages(pack)
    if not pages:
        raise ChrKitError(f"{pack_dir}: textures/hires.txt references no chr/ page")
    banks = collect_banks(pack, pages)

    # A pack's <supportedRom> is the whole file's SHA-1 (ADR-0003/ADR-0039).
    # Without this check, another game of the same broad kind - CHR ROM against
    # CHR ROM - passes the shape test below and every unrecorded cell is filled
    # with that game's graphics, then reported as an exact ROM fill. The fill
    # is the one place a wrong input produces confident, plausible, wrong art,
    # so the ROM is pinned to the recording rather than merely type-checked.
    # On the static path there is no recording to pin against, so this guard has
    # no anchor and the caller's --rom is the whole of the input (ADR-0219
    # Consequences). The substitute is that the manifest records the ROM's own
    # SHA-1 and every cell says `seen: false`: the art is the named ROM's, and
    # the kit never claims it is any particular game's.
    if not pack.static and pack.rom_sha1 and set(pack.rom_sha1) != {"0"}:
        actual = hashlib.sha1(rom_path.read_bytes()).hexdigest().lower()
        if actual != pack.rom_sha1:
            raise ChrKitError(
                f"{rom_path.name}: sha1 {actual.upper()} is not the ROM this pack was recorded "
                f"from ({pack.rom_sha1.upper()}) — filling from another game would write its "
                f"graphics into this one and report them as exact")

    # Static pages are derived from this very ROM, so the pack cannot disagree
    # with it about CHR kind; the check below is about a recorded pack paired
    # with the wrong file.
    expect_ram = not rom.has_chr_rom
    for b in ([] if pack.static else banks):
        if b.kind in ("chrRam", "chrRom") and b.is_chr_ram != expect_ram:
            raise ChrKitError(
                f"{b.primary.name}: the pack says "
                f"{'CHR RAM' if b.is_chr_ram else 'CHR ROM'} but {rom_path.name} has "
                f"{'no ' if expect_ram else ''}CHR ROM — wrong ROM for this pack?")

    # Second recordings of the same ROM, as evidence for this pack's holes only.
    # A repeat of the positional pack adds no recording, so it is dropped and
    # reported in notes[] rather than refused (#275).
    also_paths, ignored_also = donor_paths(also, pack_dir)
    donors = [Donor(p, pack, pack_dir) for p in also_paths]
    donor_notes = attach_donors(banks, donors)

    images = static_images(pages) if pack.static else {
        p.name: {"hd": read_png(p.path),
                 "orig": (read_png(p.path.parent / (p.name + ".orig.png"))
                          if (p.path.parent / (p.name + ".orig.png")).is_file() else None)}
        for p in pages}
    table = learn_palette(banks, images, rom)

    stats = collections.Counter()
    for b in banks:
        if b.kind == "chrRom":
            fill_from_chr_rom(b, rom)
        elif b.kind == "chrRam":
            fill_from_prg(b, rom, stats)

    out_chr = out_dir / "chr"
    out_chr.mkdir(parents=True, exist_ok=True)
    pack_keys = pack.key_set()

    # Pack-wide, before any page is written: the variants of one pattern sit in
    # different Bank objects whenever the pack's CHR bank hashes are all zero.
    fold_map, fold_bases, fold_stats = compute_folds(pages)
    folds = (fold_map, fold_bases)

    files, rules, dropped, passthrough = [], [], [], []
    for b in banks:
        entries, bank_rules = write_bank(b, images, table, b.transparent_rgba,
                                         out_chr, names, pack_keys, fill_rules, rom,
                                         donors, folds)
        files += entries
        rules += bank_rules
        # These pages are in the kit, copied through untouched — they are not
        # completed from the ROM, which is a different statement. `dropped[]`
        # is read as "left out of the kit", so saying it there would tell the
        # artist a file sitting in chr/ is absent.
        if b.kind in _PASSTHROUGH_WHY:
            passthrough.append((f"chr/{b.primary.name}.png",
                                _PASSTHROUGH_WHY[b.kind].format(id=b.id)))

    rules_path = out_chr / "fill-rules.hires.txt"
    if rules and pack.static:
        # On the static path these rows are not a suggestion beside a recording:
        # they are the whole manifest, one per tile in the file, every one of
        # them `defaultTile = Y`. So the file carries the header and the <img>
        # lines a build needs, and `mep_build.py build` reads it as the key
        # source of a pages-only pack (ADR-0219, PRD F12.9 (2)).
        head = [f"<ver>{PACK_VERSION}", f"<scale>{pack.scale}",
                f"<supportedRom>{pack.rom_sha1.upper()}"]
        head += [f"<img>{rel}" for rel in pack.images]
        rules_path.write_text(
            PAGES_ONLY_MARK + "\n"
            f"# The manifest of a static kit: one <tile> row per tile of "
            f"{rom_path.name}'s own CHR,\n"
            "# every one of them defaultTile=Y (the palette wildcard) and every one of\n"
            "# them `seen: false` in the sidecars. Nothing here was observed in play.\n"
            + "\n".join(head + rules) + "\n", encoding="utf-8")
    elif rules:
        rules_path.write_text(
            f"// <tile> rows for ROM-filled cells, --fill-rules={fill_rules}.\n"
            "// NOT part of the drop-in: appending these to a pack's textures/hires.txt\n"
            "// makes the renderer use a guess. See scripts/artist_chr_kit.py.\n"
            + "\n".join(rules) + "\n", encoding="utf-8")
    elif rules_path.is_file():
        rules_path.unlink()

    real = [b for b in banks if b.kind in ("chrRom", "chrRam")]
    real_cells = 256 * len(real)
    recorded = sum(len(b.art) for b in real)
    donated = sum(len(b.donated) for b in real)
    filled = sum(len(b.fills) for b in real)
    guessed = sum(f["paletteGuessed"] for f in files)
    total = collections.Counter()
    for f in files:
        total["evidence"] += f["evidence"]
        total["donated"] += f.get("donated", 0)
        total["folded"] += f.get("folded", 0)
        total["fill"] += f["fill"]
        total["empty"] += f["empty"]

    donated_clause = (f"{donated} donated by {len(donors)} other recording(s), "
                      if donors else "")
    notes = static_notes(rom_path, real_cells, filled, len(rules)) if pack.static else [
        f"{len(real)} real CHR bank(s) of 256 tiles each ({real_cells} tiles): "
        f"{recorded} recorded by the run, {donated_clause}{filled} filled from "
        f"{rom_path.name}, {real_cells - recorded - donated - filled} still empty.",
        "A page is a *variant rank* of a CHR bank, not a palette: the recorder "
        "spreads each tile's palette variants across the bank's pages by usage. "
        "Only the rank-0 page of each bank is completed — it is the page an "
        "artist opens, and completing the lower ranks would only repeat it.",
        "Green in Chr_<n>.legend.png is a cell the run recorded on this page; "
        "olive is a cell this bank recorded on a lower-ranked page, moved up "
        "(same pattern, another palette — still evidence); violet is a cell "
        "folded onto another cell, which is the one to paint; amber is a ROM fill; "
        "red is a cell nothing could fill. Every non-green cell is spelled out "
        "in Chr_<n>.json, and a ROM fill is `seen: false` there.",
        "A ROM fill is rendered nearest-neighbour under the bank's most-recorded "
        "palette; the recorder smooths its own cells, so a fill is visibly "
        "crisper. The pattern is the ROM's; the palette is a guess.",
        f"{guessed} filled cell(s) wear a (pattern, palette) the pack never "
        f"recorded. --fill-rules={fill_rules} emitted {len(rules)} <tile> row(s), "
        "into chr/fill-rules.hires.txt and never into the pack. The safe count "
        f"(palette already observed for that pattern) is {len(rules) if fill_rules == 'observed' else '-'}"
        f"; the permissive count (a rule per fill) would be {filled}. A rule with "
        "a wrong palette never matches and is harmless, but one that does match "
        "would put a nearest-neighbour guess on screen in place of the pack's "
        "filtered art, and a sprite/background misread would punch a transparent "
        "hole — so hires.txt is left exactly as recorded.",
    ]
    notes.extend(ignored_also)
    if donors:
        notes.append(
            "Blue in Chr_<n>.legend.png is a cell **another recording of this ROM** "
            "drew and this one never did. It is `seen: true` — a real run really "
            "rendered it — but it is not this pack's own evidence, so its cell in "
            "Chr_<n>.json names the recording, page and slot it was taken from. This "
            "pack stays the pack the kit is for: a donated cell only ever fills a hole "
            "and never displaces a cell this run recorded (ADR-0183 §1/§3).")
        for d in donors:
            notes.append(
                f"Second recording: {d.label} — {d.cells} cell(s) of this kit come from "
                f"it. Same ROM (sha1 {d.pack.rom_sha1.upper()}), banks paired by CHR bank "
                "id. It contributes no <tile> rule: its cells are evidence about the ROM, "
                "not keys this pack ever rendered.")
        notes.extend(donor_notes)
    if fold_stats["inert"] + fold_stats["fade"]:
        notes.append(
            f"{fold_stats['inert'] + fold_stats['fade']} cell(s) are violet in the "
            "legend: **do not paint them**. A recorded pack keys a cell by "
            "(pattern, palette), so one drawing comes back once per palette the run "
            "saw it wearing — and a screen fade gives every tile on screen a key per "
            "step of the fade. Where two keys are the same picture at another "
            f"brightness, the kit folds one onto the other: {fold_stats['inert']} "
            "render identically and "
            f"{fold_stats['fade']} are steps of the same ramp, leaving "
            f"{fold_stats['kept']} cell(s) to paint. Each folded cell names the cell "
            "it folds into, the Brightness that rebuilds it and the hue drift that "
            "costs, in its Chr_<n>.json entry.")
        notes.append(
            "A fold is only ever a *brightness* relation, decided off the NES "
            "palette: every painted entry keeps its hue column and none gets "
            "brighter. A palette that changes any painted entry's hue is a "
            f"different picture and is never folded, and {fold_stats['refused']} "
            "candidate(s) that did keep their hues were still refused because more "
            f"than {HUE_DRIFT_GATE_DEG:.0f} deg of the residual was hue, which a "
            "single multiplier cannot carry. Two colourways of one enemy stay two "
            "cells (ADR-0183 §3); nothing is folded that the renderer cannot "
            "reconstruct.")
    if not rom.has_chr_rom:
        notes.append(
            "This is a CHR RAM game: the pattern tables are built at run time "
            "from PRG data. A cell is only fillable where the bank's recorded "
            "tiles pin down a contiguous PRG block that also covers the hole. "
            "Where the game unpacks its graphics the bytes are not in the file "
            "in tile form and the cell stays red — no tool can paint it without "
            "playing the game to the moment that loads it.")
        if stats["ambiguous"]:
            notes.append(f"{stats['ambiguous']} filled cell(s) had more than one PRG "
                         "block that could explain them; the nearest-supported one "
                         "was used and the cell is marked `ambiguous` in its sidecar.")
    if passthrough:
        by_why = {}
        for path, why in passthrough:
            by_why.setdefault(why, []).append(path.split("/")[-1])
        for why, pages in by_why.items():
            notes.append(f"{len(pages)} page(s) in this kit are copied through "
                         f"untouched and were not completed from the ROM — {why}: "
                         + ", ".join(sorted(pages)) + ". They are here to paint; "
                         "they simply have no holes a ROM fill could close.")
    for b in banks:
        notes.extend(f"{b.primary.name} (bank {b.id}): {note}" for note in b.notes)

    fragment = {
        "part": "chr",
        "generator": "scripts/artist_chr_kit.py",
        "pack": str(pack_dir),
        "rom": rom_path.name,
        # Only on the static path, so a recorded run writes the bytes it wrote
        # before this flag existed. `static` is what ARTIST.md keys its first
        # line off, and `romSha1` is the whole of what identifies the input when
        # there is no recording to pin against (ADR-0219).
        **({"static": True, "romSha1": pack.rom_sha1.upper()} if pack.static else {}),
        "romHasChrRom": rom.has_chr_rom,
        "mapper": rom.mapper,
        "totals": {
            "pages": len(files), "banks": len(banks), "realBanks": len(real),
            "realBankTiles": real_cells, "recorded": recorded, "filled": filled,
            "unrecoverable": real_cells - recorded - donated - filled,
            **({"donated": donated} if donors else {}),
            "cells": 256 * len(files), "evidence": total["evidence"],
            **({"donatedCells": total["donated"]} if donors else {}),
            "fill": total["fill"], "empty": total["empty"],
            "folded": total["folded"],
            "paletteGuessed": guessed,
            "rulesSafe": filled - guessed, "rulesPermissive": filled,
            "rulesEmitted": len(rules), "fillRules": fill_rules,
        },
        "fold": {
            # Counted over the cells that sit on this pack's chr/ pages - the
            # surface this tool owns - not over every key in the manifest, so
            # `cellsOnChrPages == foldedAway + cellsToPaint` always holds.
            "gateDegrees": HUE_DRIFT_GATE_DEG,
            "cellsOnChrPages": fold_stats["inert"] + fold_stats["fade"] + fold_stats["kept"],
            "foldedAway": fold_stats["inert"] + fold_stats["fade"],
            "inert": fold_stats["inert"], "fade": fold_stats["fade"],
            "cellsToPaint": fold_stats["kept"],
            "refusedOverGate": fold_stats["refused"],
            "driftHistogram": {str(k): v for k, v in
                               sorted(fold_stats["driftHistogram"].items())},
        },
        "files": files,
        # Only present when the run was given one, so a run without `--also`
        # writes the same bytes it wrote before this feature existed.
        **({"donors": [{"pack": d.label, "romSha1": d.pack.rom_sha1.upper(),
                        "cells": d.cells} for d in donors]} if donors else {}),
        "dropped": dropped,
        "notes": notes,
        "verify": {"ran": False},
    }
    if do_verify:
        fragment["verify"] = (verify_static(pack, out_chr, rom, quiet=quiet) if pack.static
                              else verify(pack, out_chr, quiet=quiet))

    (out_dir / "kit-part-chr.json").write_text(
        json.dumps(fragment, indent=1) + "\n", encoding="utf-8")

    if not quiet and pack.static:
        print(f"{rom_path.name} — static, no recording")
        print(f"  {len(files)} page(s) in {len(banks)} bank(s) -> {out_chr}")
        print(f"  {real_cells} cell(s), all fill / seen: false; "
              f"{len(rules)} <tile> rule(s), all defaultTile=Y")
    elif not quiet:
        pct = 100.0 * recorded / real_cells if real_cells else 0.0
        done = 100.0 * (recorded + donated + filled) / real_cells if real_cells else 0.0
        print(f"{pack_dir}")
        print(f"  {len(files)} page(s) in {len(banks)} bank(s) -> {out_chr}")
        donated_clause = f"donated {donated}, " if donors else ""
        print(f"  real CHR banks: {len(real)} x 256 = {real_cells} tiles — "
              f"recorded {recorded} ({pct:.0f}%), {donated_clause}ROM fill {filled}, "
              f"unrecoverable {real_cells - recorded - donated - filled} -> "
              f"{done:.0f}% complete")
        for d in donors:
            print(f"  --also {d.label}: {d.cells} cell(s) donated")
        print(f"  <tile> rules: safe {filled - guessed}, permissive {filled}, "
              f"emitted {len(rules)} (--fill-rules={fill_rules}); "
              f"palette guessed on {guessed} filled cell(s)")
    return fragment


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("pack", type=Path,
                    help="recorded pack dir (the auto/ folder); with --static, a missing "
                         "or empty folder used only to place the kit beside")
    ap.add_argument("--rom", type=Path, required=True,
                    help="the .nes file the pack was recorded from")
    ap.add_argument("--out", type=Path, default=None,
                    help="kit folder (default: kit/ beside the recorded pack)")
    # `--also` and not `--secondary`/`--merge`: it reads at the call site as
    # "this pack, and also that recording", it is repeatable without implying a
    # rank, and it does not suggest a union pack — which ADR-0183 §1 forbids.
    ap.add_argument("--also", action="append", default=[], metavar="PACK",
                    help="another recorded pack of the SAME ROM, used as evidence for "
                         "cells this pack never recorded (repeatable; first one wins). "
                         "Its cells never displace this pack's own. The pack itself, or "
                         "a recording already listed, adds nothing and is ignored.")
    ap.add_argument("--names", default=None, help="optional titles, shared kit schema")
    ap.add_argument("--fill-rules", choices=("none", "observed", "all"), default="none",
                    help="emit a <tile> rule for a filled cell: never / only when the "
                         "pack already has that (pattern, palette) key (adds no key, "
                         "but re-binds one to another image) / always (unsafe: it "
                         "changes what a rebuilt pack renders)")
    # ADR-0219 / PRD F12.9. Not a mode of the recorded path with the recording
    # left out: it is the same projection over the one source that is available
    # before any play session, and it says so on every cell it writes.
    ap.add_argument("--static", action="store_true",
                    help="no recording: project every page over the ROM's own CHR "
                         "(CHR ROM games only). Every cell is a fill, seen: false, and "
                         "the kit has no figure, scenery or map surface")
    ap.add_argument("--scale", type=int, default=DEFAULT_STATIC_SCALE,
                    help=f"--static only: the upscale the pages are drawn at "
                         f"(default {DEFAULT_STATIC_SCALE}, the recorder's own). A "
                         "recorded run takes its scale from the recording instead")
    ap.add_argument("--verify", action="store_true")
    ap.add_argument("--quiet", action="store_true")
    args = ap.parse_args(argv)
    if args.scale < 1 or args.scale > 10:
        raise SystemExit("error: --scale out of range (1..10)")

    pack_dir = args.pack.resolve()
    out_dir = (args.out or pack_dir.parent / "kit").resolve()
    if out_dir == pack_dir or str(out_dir).startswith(str(pack_dir) + os.sep):
        raise SystemExit("error: --out must not be inside the recorded pack")
    # With no recording there is no key that was ever observed, so "emit a rule
    # only for an observed (pattern, palette)" would emit nothing at all and the
    # kit would have no manifest. A static rule is safe for the opposite reason
    # to a recorded one: it is the Y wildcard, so it cannot re-bind a key the
    # pack rendered differently — there is no such key.
    fill_rules = "all" if args.static else args.fill_rules
    try:
        run(pack_dir, args.rom.resolve(), out_dir, args.names, fill_rules,
            args.verify, args.quiet, args.also, static=args.static, scale=args.scale)
    except ChrKitError as e:
        print(f"error: {e}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
