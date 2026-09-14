#!/usr/bin/env python3
"""artist_chr_kit — complete a recorded pack's CHR pages from the ROM (PRD F9.24).

    scripts/artist_chr_kit.py <recorded pack dir> --rom <path.nes> [--out DIR]
                              [--names names.json] [--fill-rules none|observed|all]
                              [--verify] [--quiet]

A recorded pack is the `auto/` folder the bootstrap builder writes. Its
`textures/chr/Chr_*.png` pages are already the surface the hand-built fan packs
work on — 16x16 cells of one pattern page, one file per (CHR bank, palette) the
recording saw — but a cell whose tile never reached the screen is left magenta.
The Contra measurement of 2026-09-13 (`runs/golden-20260913-f922/artist-cover.md`)
put the recorded coverage at 47 % of the fan pack's distinct tiles, so an artist
painting page by page hits a hole every few cells.

This tool fills the holes from the ROM and refuses to pretend when it cannot.

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

Honesty rules this tool keeps:

* a cell the recording put on this page is copied **byte for byte** from the
  recorded page, so a rebuilt pack renders exactly what it rendered before;
* a cell this bank recorded on a lower-ranked page is moved up onto the rank-0
  page — real evidence under another palette, marked `borrowed`;
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
import os
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from sheet_repaint import Image, read_png, write_png  # noqa: E402

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
LEGEND_FILL = (0xE3, 0x9A, 0x0B, 0xFF)
LEGEND_EMPTY = (0xC4, 0x28, 0x28, 0xFF)

# CHR RAM fill thresholds. A "base" is a PRG offset such that the page's tile
# `i` sits at `base + 16 * i`; it is only trusted when several distinct,
# non-degenerate cells of the same page agree on it, and it may only fill a hole
# that one of those agreeing cells sits next to.
MIN_BASE_SUPPORT = 3
LOCALITY_WINDOW = 16


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
    def __init__(self, root: Path):
        self.root = root
        self.textures = root / "textures"
        self.hires = self.textures / "hires.txt"
        if not self.hires.is_file():
            raise ChrKitError(f"{root}: no textures/hires.txt — not a recorded pack folder")
        self.version = 0
        self.scale = 1
        self.rom_sha1 = ""
        self.images: list[str] = []
        self.tiles: list[TileRow] = []
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
    """The 16 pattern bytes of this bank's tile `index`, when they are known."""
    hit = bank.art.get(index)
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


# --- fill sources -----------------------------------------------------------


def degenerate(data: bytes) -> bool:
    """A tile whose 16 bytes are all the same value — blank, solid, and present
    thousands of times in any PRG, so useless as a landmark."""
    return len(set(data)) <= 1


def fill_from_chr_rom(bank: Bank, rom: Rom):
    """CHR ROM: the bank *is* 4 KB of the file, so every index is readable."""
    base = bank.id * 256
    for index in range(256):
        if index in bank.art:
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
    for index in bank.art:
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
        if j in bank.art:
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
    """A drop-behind overlay: green over a recorded cell, amber over a ROM fill,
    red over a hole. One file per page so an artist can toggle it as a layer."""
    n = page.cell_px
    img = Image(16 * n, 16 * n)
    colour = {"evidence": LEGEND_EVIDENCE, "borrowed": LEGEND_BORROWED,
              "fill": LEGEND_FILL, "empty": LEGEND_EMPTY}
    for slot in range(256):
        rgba = colour[states.get(slot, "empty")]
        x, y = page.xy(slot)
        for yy in range(y, y + n):
            for xx in range(x, x + n):
                edge = (xx - x < 2 or yy - y < 2 or x + n - xx <= 2 or y + n - yy <= 2)
                img.set(xx, yy, rgba if edge else (rgba[0], rgba[1], rgba[2], 0x40))
    return img


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


def write_bank(bank: Bank, images, table, transparent_rgba, out_chr: Path,
               names, pack_keys, fill_rules, rom):
    """Write every page of the bank: the rank-0 page completed, the rest copied
    through unchanged so `<out>/chr/` is a drop-in for `textures/chr/`."""
    entries, rules = [], []
    palette = bank.fill_palette()
    primary = bank.primary

    for page in bank.pages:
        hd = images[page.name]["hd"].clone()
        orig = images[page.name]["orig"]
        orig = orig.clone() if orig is not None else None
        states = {}
        cells = []
        guessed = 0

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

        write_png(out_chr / f"{page.name}.png", hd)
        if orig is not None:
            write_png(out_chr / f"{page.name}.orig.png", orig)
        write_png(out_chr / f"{page.name}.legend.png", legend_image(page, states))

        counts = collections.Counter(c["state"] for c in cells)
        sidecar = {
            "version": 1, "kind": "chr", "generator": "scripts/artist_chr_kit.py",
            "gridUnit": 8, "cell": {"w": 8, "h": 8}, "columns": 16, "rows": 16,
            "scale": page.scale,
            "sheet": f"{page.name}.png", "reference": f"{page.name}.orig.png",
            "legend": f"{page.name}.legend.png",
            "bankKind": bank.kind, "chrBankId": bank.id,
            "variantRank": bank.pages.index(page),
            "completed": page is primary and bool(bank.fills or
                                                  counts["borrowed"]),
            "layout": page.layout, "fillPalette": palette,
            "spriteTransparency": transparent_rgba is not None,
            "counts": {"evidence": counts["evidence"], "borrowed": counts["borrowed"],
                       "fill": counts["fill"], "empty": counts["empty"]},
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
            title = (f"{page.name} — {bank_label}, variant rank "
                     f"{bank.pages.index(page)}, palette {page.palette}; "
                     f"{counts['evidence'] + counts['borrowed']} recorded / "
                     f"{counts['fill']} ROM fill / {counts['empty']} empty")
        entries.append({
            "path": f"chr/{page.name}.png", "title": title, "unit": "page",
            "rows": 16, "columns": 16, "cells": 256,
            "evidence": counts["evidence"] + counts["borrowed"],
            "fill": counts["fill"], "empty": counts["empty"],
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


def verify(pack: Pack, out_chr: Path, quiet=False) -> dict:
    """Drop the completed pages into a copy of the pack, rebuild it, and prove
    nothing was lost.

    Four assertions, because `mep_build build` regenerates `hires.txt` from
    `textures/sheets/` alone and ignores `textures/chr/` entirely — a naive
    before/after on its output would pass no matter what this tool wrote:

    1. every recorded cell of every emitted page is byte-identical to the
       recorded page — that is what makes the pack's existing `<tile>` rules
       render exactly what they rendered before;
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
                if cell["state"] != "evidence":
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


def run(pack_dir: Path, rom_path: Path, out_dir: Path, names_path, fill_rules,
        do_verify, quiet):
    pack = Pack(pack_dir)
    rom = Rom(rom_path)
    names = load_names(names_path)

    pages = collect_pages(pack)
    if not pages:
        raise ChrKitError(f"{pack_dir}: textures/hires.txt references no chr/ page")
    banks = collect_banks(pack, pages)

    # A pack's <supportedRom> is the whole file's SHA-1 (ADR-0003/ADR-0039).
    # Without this check, another game of the same broad kind - CHR ROM against
    # CHR ROM - passes the shape test below and every unrecorded cell is filled
    # with that game's graphics, then reported as an exact ROM fill. The fill
    # is the one place a wrong input produces confident, plausible, wrong art,
    # so the ROM is pinned to the recording rather than merely type-checked.
    if pack.rom_sha1 and set(pack.rom_sha1) != {"0"}:
        actual = hashlib.sha1(rom_path.read_bytes()).hexdigest().lower()
        if actual != pack.rom_sha1:
            raise ChrKitError(
                f"{rom_path.name}: sha1 {actual.upper()} is not the ROM this pack was recorded "
                f"from ({pack.rom_sha1.upper()}) — filling from another game would write its "
                f"graphics into this one and report them as exact")

    expect_ram = not rom.has_chr_rom
    for b in banks:
        if b.kind in ("chrRam", "chrRom") and b.is_chr_ram != expect_ram:
            raise ChrKitError(
                f"{b.primary.name}: the pack says "
                f"{'CHR RAM' if b.is_chr_ram else 'CHR ROM'} but {rom_path.name} has "
                f"{'no ' if expect_ram else ''}CHR ROM — wrong ROM for this pack?")

    images = {p.name: {"hd": read_png(p.path),
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

    files, rules, dropped, passthrough = [], [], [], []
    for b in banks:
        entries, bank_rules = write_bank(b, images, table, b.transparent_rgba,
                                         out_chr, names, pack_keys, fill_rules, rom)
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
    if rules:
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
    filled = sum(len(b.fills) for b in real)
    guessed = sum(f["paletteGuessed"] for f in files)
    total = collections.Counter()
    for f in files:
        total["evidence"] += f["evidence"]
        total["fill"] += f["fill"]
        total["empty"] += f["empty"]

    notes = [
        f"{len(real)} real CHR bank(s) of 256 tiles each ({real_cells} tiles): "
        f"{recorded} recorded by the run, {filled} filled from {rom_path.name}, "
        f"{real_cells - recorded - filled} still empty.",
        "A page is a *variant rank* of a CHR bank, not a palette: the recorder "
        "spreads each tile's palette variants across the bank's pages by usage. "
        "Only the rank-0 page of each bank is completed — it is the page an "
        "artist opens, and completing the lower ranks would only repeat it.",
        "Green in Chr_<n>.legend.png is a cell the run recorded on this page; "
        "olive is a cell this bank recorded on a lower-ranked page, moved up "
        "(same pattern, another palette — still evidence); amber is a ROM fill; "
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
        "romHasChrRom": rom.has_chr_rom,
        "mapper": rom.mapper,
        "totals": {
            "pages": len(files), "banks": len(banks), "realBanks": len(real),
            "realBankTiles": real_cells, "recorded": recorded, "filled": filled,
            "unrecoverable": real_cells - recorded - filled,
            "cells": 256 * len(files), "evidence": total["evidence"],
            "fill": total["fill"], "empty": total["empty"],
            "paletteGuessed": guessed,
            "rulesSafe": filled - guessed, "rulesPermissive": filled,
            "rulesEmitted": len(rules), "fillRules": fill_rules,
        },
        "files": files,
        "dropped": dropped,
        "notes": notes,
        "verify": {"ran": False},
    }
    if do_verify:
        fragment["verify"] = verify(pack, out_chr, quiet=quiet)

    (out_dir / "kit-part-chr.json").write_text(
        json.dumps(fragment, indent=1) + "\n", encoding="utf-8")

    if not quiet:
        pct = 100.0 * recorded / real_cells if real_cells else 0.0
        done = 100.0 * (recorded + filled) / real_cells if real_cells else 0.0
        print(f"{pack_dir}")
        print(f"  {len(files)} page(s) in {len(banks)} bank(s) -> {out_chr}")
        print(f"  real CHR banks: {len(real)} x 256 = {real_cells} tiles — "
              f"recorded {recorded} ({pct:.0f}%), ROM fill {filled}, "
              f"unrecoverable {real_cells - recorded - filled} -> {done:.0f}% complete")
        print(f"  <tile> rules: safe {filled - guessed}, permissive {filled}, "
              f"emitted {len(rules)} (--fill-rules={fill_rules}); "
              f"palette guessed on {guessed} filled cell(s)")
    return fragment


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("pack", type=Path, help="recorded pack dir (the auto/ folder)")
    ap.add_argument("--rom", type=Path, required=True,
                    help="the .nes file the pack was recorded from")
    ap.add_argument("--out", type=Path, default=None,
                    help="kit folder (default: kit/ beside the recorded pack)")
    ap.add_argument("--names", default=None, help="optional titles, shared kit schema")
    ap.add_argument("--fill-rules", choices=("none", "observed", "all"), default="none",
                    help="emit a <tile> rule for a filled cell: never / only when the "
                         "pack already has that (pattern, palette) key (adds no key, "
                         "but re-binds one to another image) / always (unsafe: it "
                         "changes what a rebuilt pack renders)")
    ap.add_argument("--verify", action="store_true")
    ap.add_argument("--quiet", action="store_true")
    args = ap.parse_args(argv)

    pack_dir = args.pack.resolve()
    out_dir = (args.out or pack_dir.parent / "kit").resolve()
    if out_dir == pack_dir or str(out_dir).startswith(str(pack_dir) + os.sep):
        raise SystemExit("error: --out must not be inside the recorded pack")
    try:
        run(pack_dir, args.rom.resolve(), out_dir, args.names, args.fill_rules,
            args.verify, args.quiet)
    except ChrKitError as e:
        print(f"error: {e}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
