#!/usr/bin/env python3
"""Import a legacy plain HD pack as a MEP project (ADR-0198 §1).

A legacy pack is `hires.txt` plus PNGs (ADR-0005): the fork loads them
unchanged, but none of its tools can *edit* one, because `mep_build.py build`
regenerates `hires.txt` **from** sheets and a pack that has no sheets has no
way in. This tool is the missing step. It reads the pack's own manifest, cuts
every `<tile>` it names out of the PNG the rule points at, and writes the
sheets plus the `auto/` key source `mep_build.py build` reads.

ADR-0198 §1 fixes the acceptance test, and `verify` below is that test:
`build` on the imported project regenerates a manifest whose rule set, keyed by
(tileData, palette, condition), equals the input's, and whose every referenced
pixel is identical. Rule order may differ; nothing else may.

Scope: a pack that ships a `<patch>` keys its `<tile>` lines against the
*patched* ROM, so it is imported **against the patched ROM** (ADR-0198 §3,
option (a)) and needs `--rom <the stock dump>`: the `<patch>` line whose sha1
names that dump is applied in memory (`mep_patch.py`, mirroring
`IpsPatcher`), the project's `<supportedRom>` becomes the patched ROM's hash,
the IPS is carried beside both manifests, and the `<patch>` lines are carried
with the same `/`-separated path the IPS was copied to (a Windows `\` token
would never resolve on macOS/Linux — `mep_patch.emitted_line`) and their sha1
untouched, so the fork matches them exactly as it does today (ADR-0145 (3)).
Without `--rom`, or with a dump none of the `<patch>` lines names, the import
is refused. What this does **not** buy is printed on every such import: the
project lives in the patched ROM's key namespace, and a recording made on the
stock ROM does not land there.

Posture, kept from the ADRs this slice sits on:

- stdlib only (ADR-0165): zlib/struct decode the PNG bytes, no Pillow.
- The project is written next to the user's copy and never into a repository
  (ADR-0198 Consequences). Nothing here resolves a repo path.
- A rule this tool cannot classify is **refused with its line cited**, never
  dropped: `HdPackLoader` ignores an unknown tag, and an ignored tag is a rule
  the import would lose without saying so.
- A rule whose fields `mep_build.py build` would *rewrite* is refused too.
  That is the `_index_token` case below, and it is the one place where a
  faithful import needs more than copying text.
- A legacy pack animates by keying one `(tileData, palette)` pattern at
  several crops, one per condition. Each of those rules gets a cell of its
  own, pinned with `exactCondition` to the condition it carried (ADR-0198 §1
  over ADR-0197 §1's per-cell condition), so no key is emitted twice and no
  precedence rule decides which crop an imported key draws from. See
  `_plan_cells`.

Two representations, and why the tool has to know the difference:

- A **data-keyed** pack (CHR RAM) writes 32 hex characters in `<tile>`'s
  tileData field; the key is those bytes.
- An **index-keyed** pack (CHR ROM, and the upstream packs of §2) writes a
  CHR index, read as decimal below `<ver>103` and as hex at 103+
  (`HdPackLoader::ReadTileData`). `build` always re-emits that field through
  `_index_token` (ADR-0172), in the emulator's 2/4/6/8-digit hex width, and it
  looks a rule's trailing fields up in the key source **by that hex form**. So
  an index-keyed import writes `<ver>103` and hex tokens: the parsed key is
  identical to the input's, and without both halves the lookup misses and
  every rule silently loses its condition and its brightness. That rewrite is
  refused when the pack carries a construct the loader reads differently at
  103 (`_ver_sensitive_reason`).

A third command reads a community pack as *facts about the ROM* instead of as
art (ADR-0210 §3), which is a different question from importing it: no PNG of
that pack is opened, and no `<condition>` line is read. See `read_index`.

Usage:
  python3 mep_import.py <legacy pack folder> --out <project folder> [--force] [--rom X.nes]
  python3 mep_import.py verify <legacy pack folder> <project folder> [--strict]
  python3 mep_import.py index <their hires.txt> --pack <our auto/> --rom X.nes
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import struct
import sys
import zlib
from datetime import datetime, timezone
from pathlib import Path

import mep_build
import mep_patch

# The sheet geometry every imported sheet uses: a 16-cell-wide grid of 8px
# logical cells, no gutter — the shape the recorder's contact sheets have
# (SheetRender), so an imported sheet reads like a recorded one.
_SHEET_COLUMNS = 16
_HEX_DATA_RE = re.compile(r"^[0-9A-F]{32}$")
_HEX_PAL_RE = re.compile(r"^[0-9A-F]{8}$")
_HEX_TOKEN_RE = re.compile(r"^[0-9A-F]+$")
_HEX40_RE = re.compile(r"^[0-9A-Fa-f]{40}$")
_TAG_RE = re.compile(r"^(\[[^\]]*\])?(<[A-Za-z]+>)")
# A line whose start looks like a tag: what an unknown tag is refused for.
_TAG_START_RE = re.compile(r"^(\[[^\]]*\])?<")
_IMG_RE = re.compile(r"^(\[[^\]]*\])?<img>")
_BG_RE = re.compile(r"^(\[[^\]]*\])?<background>")
# The only two tags whose `[...]` prefix the loader gives a meaning.
_PREFIXED_TAGS = frozenset({"<tile>", "<background>"})
# HdPackLoader's own dispatch list — every tag it reads, and no other. Anything
# else is refused (never dropped). `<patch>` is read by `mep_patch.patch_lines`
# and needs `--rom` at import time (ADR-0198 §3); the index read (ADR-0210 §3)
# still refuses it by name.
_KNOWN_TAGS = frozenset(
    {"<tile>", "<background>", "<condition>", "<img>", "<addition>", "<fallback>",
     "<bgm>", "<sfx>", "<patch>"}
    | {f"<{t}>" for t in mep_build._HEADER_TAGS})
# The version at which the loader reads a short tileData field as hex.
# Below it the field is decimal (ReadTileData), so a hex-token key source
# would be read as a different key.
_HEX_INDEX_VER = 103
# Condition types whose parse the 100/101/102 -> 103 step changes.
_MEMORY_COND_TYPES = ("memoryCheck", "ppuMemoryCheck",
                      "memoryCheckConstant", "ppuMemoryCheckConstant")


class PackError(Exception):
    """Fatal, with a message a human can act on."""


# --- PNG --------------------------------------------------------------------
#
# mep_build's decoder reads 8-bit RGB/RGBA only, and a legacy pack very
# commonly keys its art in 8-bit *palette* PNGs (79 of Ninja Gaiden's 299).
# Rather than widen that decoder — it guards the sheet pipeline's own pixels —
# this one reads all four of the 8-bit color types and always hands back RGBA.

class Image:
    __slots__ = ("width", "height", "px")

    def __init__(self, width: int, height: int, px: bytearray | None = None):
        self.width = width
        self.height = height
        self.px = px if px is not None else bytearray(width * height * 4)

    def block(self, x: int, y: int, size: int):
        """The `size`x`size` RGBA block at (x, y), or None when it falls
        outside the image."""
        if x < 0 or y < 0 or x + size > self.width or y + size > self.height:
            return None
        out = bytearray()
        stride = self.width * 4
        for row in range(size):
            off = (y + row) * stride + x * 4
            out += self.px[off:off + size * 4]
        return bytes(out)

    def paste(self, block: bytes, x: int, y: int, size: int):
        stride = self.width * 4
        for row in range(size):
            dst = (y + row) * stride + x * 4
            self.px[dst:dst + size * 4] = block[row * size * 4:(row + 1) * size * 4]

    def downscale(self, n: int) -> "Image":
        """Nearest neighbour, dropping every Nth pixel — how the recorder
        writes a sheet's `*.orig.png` twin at 1x (F5.4d)."""
        if n == 1:
            return Image(self.width, self.height, bytearray(self.px))
        w, h = self.width // n, self.height // n
        out = Image(w, h)
        for y in range(h):
            dst = y * w * 4
            src = y * n * self.width * 4
            for x in range(w):
                o = src + x * n * 4
                out.px[dst + x * 4:dst + x * 4 + 4] = self.px[o:o + 4]
        return out


def read_png(path: Path) -> Image:
    """Decode an 8-bit, non-interlaced PNG of color type 0/2/3/6 into RGBA."""
    try:
        data = path.read_bytes()
    except OSError as e:
        raise PackError(f"{path}: cannot be read ({e})") from e
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise PackError(f"{path}: not a PNG")
    pos, width, height, color, palette, trns, idat = 8, 0, 0, None, b"", b"", bytearray()
    while pos + 8 <= len(data):
        length = struct.unpack(">I", data[pos:pos + 4])[0]
        tag, body = data[pos + 4:pos + 8], data[pos + 8:pos + 8 + length]
        if tag == b"IHDR":
            width, height, depth, color, _c, _f, interlace = struct.unpack(">IIBBBBB", body[:13])
            if depth != 8 or interlace != 0 or color not in (0, 2, 3, 6):
                raise PackError(f"{path}: only 8-bit, non-interlaced grey/RGB/palette/RGBA PNGs "
                                f"are importable (got depth {depth}, color type {color})")
        elif tag == b"PLTE":
            palette = body
        elif tag == b"tRNS":
            trns = body
        elif tag == b"IDAT":
            idat += body
        elif tag == b"IEND":
            break
        pos += 12 + length
    if color is None:
        raise PackError(f"{path}: no IHDR")
    channels = {0: 1, 2: 3, 3: 1, 6: 4}[color]
    try:
        raw = zlib.decompress(bytes(idat))
    except zlib.error as e:
        raise PackError(f"{path}: IDAT does not decompress ({e})") from e
    stride = width * channels
    if len(raw) < height * (stride + 1):
        raise PackError(f"{path}: truncated image data")
    scan = _unfilter(raw, width, height, channels)
    return Image(width, height, _to_rgba(scan, width, height, color, palette, trns))


def _unfilter(raw: bytes, width: int, height: int, channels: int) -> bytearray:
    """Reverse the per-scanline filters of a PNG (RFC 2083 §6)."""
    stride = width * channels
    out = bytearray(height * stride)
    prev = bytes(stride)
    src = 0
    for y in range(height):
        ft = raw[src]
        src += 1
        line = bytearray(raw[src:src + stride])
        src += stride
        if ft == 1:
            for i in range(channels, stride):
                line[i] = (line[i] + line[i - channels]) & 0xFF
        elif ft == 2:
            for i in range(stride):
                line[i] = (line[i] + prev[i]) & 0xFF
        elif ft == 3:
            for i in range(stride):
                left = line[i - channels] if i >= channels else 0
                line[i] = (line[i] + ((left + prev[i]) >> 1)) & 0xFF
        elif ft == 4:
            for i in range(stride):
                a = line[i - channels] if i >= channels else 0
                b = prev[i]
                c = prev[i - channels] if i >= channels else 0
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                line[i] = (line[i] + (a if pa <= pb and pa <= pc else (b if pb <= pc else c))) & 0xFF
        elif ft != 0:
            raise PackError(f"invalid PNG scanline filter {ft}")
        out[y * stride:(y + 1) * stride] = line
        prev = line
    return out


def _to_rgba(scan: bytearray, width: int, height: int, color: int,
             palette: bytes, trns: bytes) -> bytearray:
    """Widen the unfiltered scanlines to RGBA. Per channel through
    `bytes.translate` / extended-slice assignment, which are C-level: a
    7682px-wide scanline is why this is not a per-pixel loop."""
    stride = width * {0: 1, 2: 3, 3: 1, 6: 4}[color]
    out = bytearray(width * height * 4)
    for y in range(height):
        src = scan[y * stride:(y + 1) * stride]
        row = bytearray(width * 4)
        if color == 6:
            row[:] = src
        elif color == 2:
            row[0::4] = src[0::3]
            row[1::4] = src[1::3]
            row[2::4] = src[2::3]
            row[3::4] = b"\xff" * width
        elif color == 0:
            row[0::4] = src
            row[1::4] = src
            row[2::4] = src
            row[3::4] = b"\xff" * width
        else:
            # An editor writes only the entries it uses, so pad to 256 for the
            # direct-index translate — after checking that no pixel names an
            # entry the file does not declare, which would read as black. A
            # tRNS shorter than the palette leaves the rest opaque (RFC 2083
            # §11.3.2, and libspng's RGBA8 expansion — the decoder the
            # emulator's own loader uses), so it pads with 0xFF, never 0.
            entries = len(palette) // 3
            if entries == 0:
                raise PackError("palette PNG without a PLTE chunk")
            if src and max(src) >= entries:
                raise PackError(f"palette PNG uses index {max(src)} but its PLTE declares "
                                f"only {entries} entries")
            alpha = trns.ljust(256, b"\xff")[:256] if trns else b"\xff" * 256
            row[0::4] = src.translate(bytes(palette[0::3]).ljust(256, b"\x00"))
            row[1::4] = src.translate(bytes(palette[1::3]).ljust(256, b"\x00"))
            row[2::4] = src.translate(bytes(palette[2::3]).ljust(256, b"\x00"))
            row[3::4] = src.translate(alpha)
        out[y * width * 4:(y + 1) * width * 4] = row
    return out


def write_png(path: Path, img: Image):
    """8-bit RGBA, filter 0, one IDAT — the shape the C++ side and the test
    fixtures write, so a round-trip stays byte-comparable."""
    raw = bytearray()
    stride = img.width * 4
    for y in range(img.height):
        raw.append(0)
        raw += img.px[y * stride:(y + 1) * stride]

    def chunk(tag: bytes, data: bytes) -> bytes:
        return (struct.pack(">I", len(data)) + tag + data
                + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))

    path.write_bytes(
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", struct.pack(">IIBBBBB", img.width, img.height, 8, 6, 0, 0, 0))
        + chunk(b"IDAT", zlib.compress(bytes(raw), 9))
        + chunk(b"IEND", b""))


# --- the legacy pack --------------------------------------------------------

class Rule:
    """One `<tile>` line: its condition prefix, the fields the loader reads
    and the line it came from."""

    __slots__ = ("cond", "bitmap", "token", "palette", "x", "y", "rest", "line")

    def __init__(self, cond, bitmap, token, palette, x, y, rest, line):
        self.cond, self.bitmap = cond, bitmap
        self.token, self.palette = token, palette
        self.x, self.y, self.rest, self.line = x, y, rest, line

    def parsed_index(self, ver: int) -> int:
        """The CHR index this rule names, read the loader's own way: decimal
        below `<ver>103`, hex at 103+ (HdPackLoader::ReadTileData)."""
        return int(self.token, 16) if ver >= _HEX_INDEX_VER else int(self.token, 10)


class Pack:
    """A parsed legacy pack: its manifest lines plus what the import needs."""

    def __init__(self, folder: Path, hires: Path):
        self.folder = folder
        self.hires = hires
        self.lines = hires.read_text(encoding="utf-8", errors="replace").splitlines()
        # mep_build's splitter is the manifest's only parser in this tree; the
        # import reads the same header/tile/audio/body split it does, and adds
        # the <img> list (which build drops, because it regenerates it).
        self.header, tiles, self.audio, self.body = mep_build._parse_source(self.lines)
        self.imgs = [ln.strip()[5:].strip() for ln in self.lines if _IMG_RE.match(ln.strip())]
        self.ver = self._int_tag("<ver>", 100)
        self.scale = self._int_tag("<scale>", 1)
        self.supported_rom = next((h[len("<supportedRom>"):].strip() for h in self.header
                                   if h.startswith("<supportedRom>")), None)
        try:
            self.patches = mep_patch.patch_lines(self.lines)
        except mep_patch.PatchError as e:
            raise PackError(f"{hires}:{e}") from e
        self.index_keyed = any(len(raw.split(",")[1].strip()) < 32
                               for _cond, raw in tiles if len(raw.split(",")) >= 2)
        self._tile_lines = {}
        self._validate_tags()
        self.rules = [self._rule(cond, raw, n) for n, (cond, raw) in enumerate(tiles)]

    def _rule(self, cond: str, raw: str, n: int) -> Rule:
        line = self._tile_lines.get(n, 0)
        f = [x.strip() for x in raw.split(",")]
        if len(f) < 6:
            raise PackError(f"{self.hires}:{line}: <tile> has only {len(f)} fields, "
                            "6 are required (bitmap,tileData,palette,x,y,brightness,defaultTile)")
        try:
            bitmap, x, y = int(f[0]), int(f[3]), int(f[4])
        except ValueError as e:
            raise PackError(f"{self.hires}:{line}: <tile> field is not a number: {e}") from e
        token = f[1].upper()
        if not _HEX_TOKEN_RE.match(token):
            raise PackError(f"{self.hires}:{line}: <tile> tileData {f[1]!r} is neither 32 hex "
                            "characters nor a hex CHR index")
        if not _HEX_PAL_RE.match(f[2].upper()):
            raise PackError(f"{self.hires}:{line}: <tile> palette {f[2]!r} is not 8 hex "
                            "characters; mep_build keys a crop by that exact form")
        return Rule(cond, bitmap, token, f[2].upper(), x, y, f[5:], line)

    def _int_tag(self, tag: str, default: int) -> int:
        for h in self.header:
            if h.startswith(tag):
                try:
                    return int(h[len(tag):].strip())
                except ValueError as e:
                    raise PackError(f"{self.hires}: <{tag[1:-1]}> is not a number: {e}") from e
        return default

    def _validate_tags(self):
        """Every non-comment line must be a tag HdPackLoader knows. An unknown
        one is refused: the loader skips it silently, so importing it would
        lose whatever it said (ADR-0198 Consequences).

        A line that is not a tag *attempt* — no `<` where a tag would start —
        is not a rule at all (the loader skips it whole), so it is carried
        through verbatim rather than refused; refusing it would turn a typo in
        a comment into an unimportable pack. Contra80s has one (` Background 2
        - second 2 seconds`, a comment that lost its `#`)."""
        self.strays = []
        for i, raw in enumerate(self.lines):
            s = raw.strip()
            if not s or s.startswith("#"):
                continue
            m = _TAG_RE.match(s)
            if not m:
                if _TAG_START_RE.match(s):
                    raise PackError(f"{self.hires}:{i + 1}: unknown tag; refusing it rather than "
                                    f"dropping whatever it says (ADR-0198): {s[:60]!r}")
                self.strays.append(i + 1)
                continue
            tag = m.group(2)
            if tag not in _KNOWN_TAGS:
                raise PackError(f"{self.hires}:{i + 1}: unknown tag {tag}; refusing it rather "
                                "than dropping whatever it says (ADR-0198)")
            if m.group(1) and tag not in _PREFIXED_TAGS:
                # HdPackLoader strips a `[...]` prefix off *every* line before
                # it dispatches, but only `<tile>` and `<background>` give it a
                # meaning. On any other tag the prefix is text the loader
                # ignores and mep_build's splitter does not, so the line would
                # move between the header, the body and the condition block on
                # the way through — refused rather than reordered.
                raise PackError(f"{self.hires}:{i + 1}: {tag} with a condition prefix; only "
                                "<tile> and <background> take one")
            if tag == "<tile>":
                self._tile_lines[len(self._tile_lines)] = i + 1


def open_pack(folder: Path) -> Pack:
    for cand in (folder / "hires.txt", folder / "textures" / "hires.txt"):
        if cand.is_file():
            return Pack(folder, cand)
    raise PackError(f"{folder}: no hires.txt found (a legacy pack is a folder with hires.txt)")


# --- key source --------------------------------------------------------------

def ver_sensitive_reason(pack: Pack) -> str | None:
    """Why raising this pack's `<ver>` to 103 would change how the loader reads
    a line, or None when the raise is provably equivalent.

    The raise exists so an index-keyed manifest can be written in the hex form
    `mep_build.py build` re-emits (ADR-0172's `_index_token`). Below 103 the
    loader reads a short tileData field as decimal — the one gate the raise is
    for, and the one it compensates exactly. The gates below are the others it
    would cross, and each is a real interpretation change, so the import
    refuses instead of making it."""
    if pack.ver >= _HEX_INDEX_VER:
        return None
    for i, raw in enumerate(pack.lines):
        s = raw.strip()
        if not s or s.startswith("#"):
            continue
        m = _TAG_RE.match(s)
        if not m:
            continue
        args = [x.strip() for x in s[m.end():].split(",")]
        if m.group(2) == "<background>" and len(args) > 2:
            return (f"line {i + 1}: a <background> with {len(args)} fields is rejected below "
                    "<ver>101 (checkConstraint) and read as a scroll layer at 103+")
        if m.group(2) != "<condition>":
            continue
        ctype = args[1] if len(args) > 1 else ""
        if ctype in _MEMORY_COND_TYPES and len(args) > 5:
            return (f"line {i + 1}: a {ctype} with 6+ fields ignores its 6th below <ver>103 and "
                    "reads it as the mask at 103+")
        if ctype == "frameRange" and pack.ver == 101:
            return (f"line {i + 1}: frameRange operands are hex at <ver>101 and decimal at 102+, "
                    "so 101 -> 103 would re-read them")
    return None


def build_key_source(pack: Pack, normalize: bool, supported_rom: str | None = None) -> list[str]:
    """The manifest `mep_build.py build` reads as its key source: the input's
    own lines, with tileData rewritten to the form build looks up.

    `supported_rom`, when given, is the patched ROM's whole-file sha1 (ADR-0198
    §3): it replaces the pack's own `<supportedRom>` line, or is inserted right
    after `<scale>` when the pack declared none. Nothing else in the header
    moves.

    Every `<patch>` line is re-emitted as `mep_patch.emitted_line`: the file
    token becomes the normalized `/`-separated path the IPS is copied to
    (`_copy_patches`), the sha1 stays. The runtime on macOS/Linux resolves the
    token verbatim (`HdPackLoader::ResolvePackRelativePath` + `IndexPackFiles`,
    which indexes `/` names), so a Windows `sub\\fix.ips` carried as written
    would be copied to `sub/fix.ips` and never applied."""
    out = []
    n = -1
    placed = supported_rom is None
    patches = iter(pack.patches)
    for raw in pack.lines:
        s = raw.strip()
        if supported_rom is not None and s.startswith("<supportedRom>"):
            if not placed:
                out.append(f"<supportedRom>{supported_rom}")
                placed = True
            continue
        if s.startswith("<patch>"):
            # `pack.patches` is `mep_patch.patch_lines(pack.lines)`: one entry
            # per `<patch>` line, in file order, so the two walk in step.
            out.append(mep_patch.emitted_line(next(patches)))
            continue
        m = mep_build._TILE_RE.match(s)
        if not normalize or not m or s.startswith("#"):
            out.append(_raise_ver(raw, pack.ver) if normalize else raw)
            if not placed and s.startswith("<scale>"):
                out.append(f"<supportedRom>{supported_rom}")
                placed = True
            continue
        n += 1
        rule = pack.rules[n]
        f = [x.strip() for x in m.group(2).split(",")]
        f[1] = mep_build._index_token(rule.parsed_index(pack.ver)) if pack.index_keyed else f[1].upper()
        out.append(f"{m.group(1) or ''}<tile>{','.join(f)}")
    if not placed:
        # No <scale> line to anchor on: the header is whatever came first.
        out.insert(0, f"<supportedRom>{supported_rom}")
    return out


def _raise_ver(raw: str, ver: int) -> str:
    """Raise `<ver>` to 103 so the hex tokens are read as hex — never lower it:
    a `<ver>108` pack keeps 108, or its `<addition>` lines and 106+ `<background>`
    fields would be refused by the loader (found on Metroid #148, 2026-09-22)."""
    s = raw.strip()
    return f"<ver>{max(ver, _HEX_INDEX_VER)}" if s.startswith("<ver>") else raw


def lookup_attrs(lines: list[str]) -> dict:
    """The key source's `(data, palette) -> [(condition, trailing fields)]`, in
    mep_build's own shape (`cmd_build`): the lookup an import has to satisfy."""
    _header, tiles, _audio, _body = mep_build._parse_source(lines)
    attrs = {}
    for cond, raw in tiles:
        f = [x.strip() for x in raw.split(",")]
        if len(f) >= 6:
            attrs.setdefault((f[1].upper(), f[2].upper()), []).append((cond, f[5:]))
    return attrs


def emitted_attrs(pack: Pack, attrs: dict, normalize: bool, split: set = frozenset()) -> tuple:
    """What `build` will emit per rule, checked against what the input says.

    Returns (twin_additions, problems). A problem is a rule whose fields build
    would replace with its default — that is the `_index_token` lookup missing,
    and it is refused rather than imported, because the whole point of the
    slice is that the rule set does not change.

    `split` is the set of `(tileData, palette)` patterns `_plan_cells` gave one
    exact cell per rule: those emit exactly the rules the input had, so the
    ADR-0189 §3 twin is neither added nor counted for them."""
    twins, problems = set(), []
    for rule in pack.rules:
        key = (mep_build._index_token(rule.parsed_index(pack.ver))
               if (normalize and pack.index_keyed) else rule.token)
        got = attrs.get((key, rule.palette))
        if got is None:
            if rule.cond or rule.rest != ["1", "N"]:
                problems.append(rule)
            continue
        if ((rule.token, rule.palette) not in split
                and any(c for c, _r in got) and "" not in {c for c, _r in got}):
            twins.add((key, rule.palette))
    return twins, problems


# --- the import -------------------------------------------------------------

def import_pack(src: Path, out: Path, force: bool, rom: Path | None = None) -> dict:
    pack = open_pack(src)
    if not pack.rules:
        raise PackError(f"{pack.hires}: no <tile> entries to import")
    if not 1 <= pack.scale <= 10:
        raise PackError(f"{pack.hires}: <scale>{pack.scale} is outside the loader's 1..10")
    patch = _resolve_patch(pack, rom)
    if patch:
        _patch_destinations(patch, out)  # refuse before anything is written

    normalize = pack.index_keyed
    if normalize:
        reason = ver_sensitive_reason(pack)
        if reason:
            raise PackError(
                f"{pack.hires}: this pack is index-keyed (CHR ROM) and its <tile> keys must be "
                f"rewritten to the hex form mep_build re-emits, but {reason} — the rewrite would "
                "change how the loader reads that line, so ADR-0198 §1 refuses it rather than "
                "import a pack whose keys or conditions shift")
    key_lines = build_key_source(pack, normalize, patch.patched_whole if patch else None)
    attrs = lookup_attrs(key_lines)
    # The whole read side first — every <img> decodes, every cell's PNG name
    # is usable, every planned crop fits its image — because a refusal must
    # not leave a half-written project behind: the retry would then hit the
    # non-empty--out guard instead of the real error.
    cells, split = _plan_cells(pack)
    twins, problems = emitted_attrs(pack, attrs, normalize, {p for p, _c in split})
    if problems:
        first = problems[0]
        raise PackError(
            f"{pack.hires}:{first.line}: mep_build would rewrite this rule's fields (its key is "
            f"{first.token}/{first.palette}, which the key source does not carry in the form build "
            "looks up) — the import refuses rather than drop a condition or a brightness "
            "(ADR-0198 §1)")

    sources = _read_sources(pack, cells)
    if out.exists() and any(out.iterdir()) and not force:
        raise PackError(f"{out} exists and is not empty; pass --force to write into it")
    sheets_dir = out / "textures" / "sheets"
    auto_dir = out / "auto" / "textures"
    sheets_dir.mkdir(parents=True, exist_ok=True)
    auto_dir.mkdir(parents=True, exist_ok=True)
    (auto_dir / "hires.txt").write_text("\n".join(key_lines) + "\n", encoding="utf-8")

    sheets = _write_sheets(pack, cells, sources, sheets_dir)
    _copy_images(pack, auto_dir)
    backgrounds = _copy_backgrounds(pack, auto_dir)
    audio = _copy_audio(pack, out)
    patched = _copy_patches(pack, patch, out) if patch else None
    (out / "IMPORT.md").write_text(
        _import_note(pack, sheets, sum(len(v) for v in cells.values()), len(twins), split,
                     normalize, patched),
        encoding="utf-8")

    return {
        "src": pack.hires,
        "strays": pack.strays,
        "rules": len(pack.rules),
        "cells": sum(len(v) for v in cells.values()),
        "sheets": sheets,
        "images": len(cells),
        "twins": len(twins),
        "split": split,
        "keyed": "index" if pack.index_keyed else "data",
        "ver": pack.ver,
        "normalized": normalize,
        "backgrounds": backgrounds,
        "audio": audio,
        "patch": patched,
        "out": out,
    }


def _resolve_patch(pack: Pack, rom: Path | None):
    """ADR-0198 §3: a pack with `<patch>` is imported against the patched ROM,
    which needs the stock dump at hand; without it the import is refused,
    naming the section. A pack without `<patch>` ignores `--rom`, and says so:
    a plain pack's namespace is the stock ROM's already."""
    if not pack.patches:
        if rom is not None:
            print(f"note: {pack.hires} has no <patch>; --rom {rom.name} was not needed and was "
                  "not read", file=sys.stderr)
        return None
    first = pack.patches[0]
    if rom is None:
        raise PackError(
            f"{pack.hires}:{first.line}: this pack ships a <patch>, so its <tile> keys are keyed "
            "against the ROM *after* the patch. ADR-0198 §3 imports it against that patched "
            "ROM, which needs the stock dump the <patch> line was made for: pass --rom <dump>. "
            f"The pack names {len(pack.patches)} <patch> target(s): "
            f"{', '.join(sorted({p.sha1 for p in pack.patches}))}")
    try:
        return mep_patch.resolve(pack.lines, pack.hires.parent, rom, pack.supported_rom)
    except mep_patch.PatchError as e:
        raise PackError(f"{pack.hires}: {e}") from e


def _patch_destinations(plan, out: Path) -> list[tuple[str, list[Path]]]:
    """Where each IPS lands, and the check that it lands *inside* the project:
    `(rel, [auto/textures/rel, textures/rel])` per IPS. `mep_patch` already
    refused absolute paths, `..` components and files outside the source pack
    (ADR-0006); this is the destination half of the same rule, so a `<patch>`
    name that would make `layer / rel` resolve outside its layer (a symlinked
    sub-folder in an `--out` reused with `--force`, for instance) is refused
    naming the manifest line, before `import_pack` writes anything."""
    line_of = {}
    for e in plan.entries:
        line_of.setdefault(e.rel, e.line)
    layers = (out / "auto" / "textures", out / "textures")
    result = []
    for rel in plan.ips_files:
        dsts = [layer / rel for layer in layers]
        for dst in dsts:
            # Against the project root, not the layer: a layer that is itself
            # a symlink resolves *with* its target and would contain anything.
            if not mep_patch.contained(dst, out):
                raise PackError(f"line {line_of[rel]}: <patch> file {rel!r} would be written "
                                f"outside {out} — refused, nothing written (ADR-0006)")
        result.append((rel, dsts))
    return result


def _copy_patches(pack: Pack, plan, out: Path) -> dict:
    """The IPS beside **both** manifests: `auto/textures/` so the key-source
    layer is whole (its `<patch>` lines cite the file), and `textures/` because
    `build` carries the `<patch>` lines into `textures/hires.txt` and copies
    nothing but backgrounds up — the loader resolves the patch from the
    manifest's own folder (`HdPackLoader::ProcessPatchTag`), and a manifest
    whose patch is not beside it fails to load whole."""
    copied = 0
    for rel, dsts in _patch_destinations(plan, out):
        # Read the file the loader would open (a case-folded `Fix.ips` ->
        # `fix.ips`), write it under the emitted name, so the project's token
        # resolves exactly on every host.
        data = (pack.hires.parent / plan.sources[rel]).read_bytes()
        for dst in dsts:
            dst.parent.mkdir(parents=True, exist_ok=True)
            dst.write_bytes(data)
        copied += 1
    return {
        "rom": plan.rom,
        "matched_line": plan.matched.line,
        "matched_file": plan.matched.file,
        "matched_rel": plan.matched.rel,
        "matched_by": plan.matched_by,
        "entries": len(plan.entries),
        "files": copied,
        "records": plan.records,
        "stock_whole": plan.stock_whole,
        "stock_no_intro": plan.stock_no_intro,
        "patched_whole": plan.patched_whole,
        "patched_no_intro": plan.patched_no_intro,
        "stock_size": plan.stock_size,
        "patched_size": plan.patched_size,
        "stock_chr_units": plan.stock_chr_units,
        "patched_chr_units": plan.patched_chr_units,
        "adds_chr_rom": plan.adds_chr_rom,
        "declared_supported_rom": pack.supported_rom,
        "note": mep_patch.namespace_note(plan, pack.index_keyed),
    }


def _plan_cells(pack: Pack) -> tuple:
    """`bitmap -> [(x, y, [entry, ...], condition), ...]` plus the patterns that
    had to be split across several cells.

    Two kinds of cell, and the second is what makes the pixel half of ADR-0198
    §1 hold.

    A **pattern** — a `(tileData, palette)` pair — that the manifest only ever
    draws at one crop is carried by an *inherit* cell: it names the pair and
    lets `mep_build.py build` take the pair's conditions from the key source,
    exactly as a recorded pack does. Several pairs drawn at the same crop share
    one cell through its `aliases`, so the crop's pixels are stored once.

    A pattern the manifest keys at **several** crops cannot be carried that
    way: build takes a pair's conditions from the key source as a whole, so a
    single cell would draw every one of them from the one crop it names.
    Contra80s animates Norris exactly like that (592 of its 7836 patterns) and
    Super Mario Bros. 430 of 2076. Each such rule therefore gets its own
    *exact* cell at its own crop, carrying the one condition that rule had —
    the per-cell condition ADR-0197 §1 opened, pinned by `exactCondition` so
    that build emits that rule and nothing else. The unconditional rule, when
    the pack has one, is an exact cell too, with an empty condition; 586 of
    Contra80s' 592 split patterns draw it at a crop of its own.

    No key is then emitted twice, so no precedence rule decides which crop an
    imported key draws from: every rule of the input has one cell, and one only.
    """
    patterns: dict = {}
    for rule in pack.rules:
        if rule.bitmap >= len(pack.imgs):
            raise PackError(f"{pack.hires}:{rule.line}: bitmap index {rule.bitmap} is out of "
                            f"range ({len(pack.imgs)} <img> line(s))")
        patterns.setdefault((rule.token, rule.palette), {}).setdefault(
            rule.cond, (rule.bitmap, rule.x, rule.y))

    plan: dict = {}
    at: dict = {}
    split: list = []
    exact: list = []
    for pattern, by_cond in patterns.items():
        entry = (pattern[0], pattern[1],
                 _index_of(pattern[0], pack.ver) if pack.index_keyed else None)
        if len(set(by_cond.values())) == 1:
            pos = next(iter(by_cond.values()))
            cell = at.get(pos)
            if cell is None:
                cell = (pos[1], pos[2], [], None)
                at[pos] = cell
                plan.setdefault(pos[0], []).append(cell)
            if entry not in cell[2]:
                cell[2].append(entry)
            continue
        split.append((pattern, sorted(by_cond)))
        for cond, pos in by_cond.items():
            exact.append((pos[0], (pos[1], pos[2], [entry], (cond[1:-1] if cond else "", True))))
    # The exact cells last: they are the pack's animated frames, and a reader
    # opening the sheet meets its plain art first.
    for bitmap, cell in exact:
        plan.setdefault(bitmap, []).append(cell)
    return plan, split


def _index_of(token: str, ver: int) -> int:
    return int(token, 16) if ver >= _HEX_INDEX_VER else int(token, 10)


def _read_sources(pack: Pack, plan: dict) -> dict:
    """Decode every `<img>` the manifest names and check every planned crop
    against its image. The whole read side of the import, so that a pack
    which cannot be imported is refused *before* anything is written."""
    cache: dict = {}
    for bitmap in range(len(pack.imgs)):
        _source_image(pack, bitmap, cache)
    size = 8 * pack.scale
    for bitmap, cells in plan.items():
        rel = pack.imgs[bitmap].replace("\\", "/")
        name = Path(rel).name
        if not name.lower().endswith(".png") or name.lower().endswith(".orig.png"):
            raise PackError(f"{pack.hires}: <img> {pack.imgs[bitmap]!r} cannot name an imported "
                            "sheet (a sheet PNG must end in .png and not in .orig.png)")
        img = _source_image(pack, bitmap, cache)
        for x, y, _entries, _cond in cells:
            if img.block(x, y, size) is None:
                raise PackError(f"{pack.hires}: a <tile> at ({x},{y}) in {rel} does not fit that "
                                f"{img.width}x{img.height} PNG")
    return cache


def _write_sheets(pack: Pack, plan: dict, sources: dict, sheets_dir: Path) -> list:
    """One sheet per image the manifest names: the crops relaid on a fresh
    16-column grid, so the output geometry is the tool's own and never the
    input's (a legacy pack puts a tile wherever its author drew it, at any
    pixel offset — 594 of Contra80s' rules start on an odd x)."""
    size = 8 * pack.scale
    written = []
    for bitmap in sorted(plan):
        cells = plan[bitmap]
        if not cells:
            continue
        name = Path(pack.imgs[bitmap].replace("\\", "/")).name
        stem = name[:-4]
        source = _source_image(pack, bitmap, sources)
        columns = min(_SHEET_COLUMNS, len(cells))
        rows = (len(cells) + columns - 1) // columns
        sheet = Image(columns * size, rows * size)
        for i, (x, y, _entries, _cond) in enumerate(cells):
            block = source.block(x, y, size)
            if block is None:
                raise PackError(f"{pack.hires}: a <tile> at ({x},{y}) in {pack.imgs[bitmap]} does "
                                f"not fit that {source.width}x{source.height} PNG")
            sheet.paste(block, (i % columns) * size, (i // columns) * size, size)
        write_png(sheets_dir / f"{stem}.png", sheet)
        write_png(sheets_dir / f"{stem}.orig.png", sheet.downscale(pack.scale))
        (sheets_dir / f"{stem}.json").write_text(
            json.dumps(_sidecar(pack, stem, cells, columns, size), indent=1) + "\n",
            encoding="utf-8")
        written.append(stem)
    return written


def _source_image(pack: Pack, bitmap: int, cache: dict) -> Image:
    rel = pack.imgs[bitmap].replace("\\", "/")
    if rel not in cache:
        path = pack.folder / rel
        if not path.is_file():
            raise PackError(f"{pack.hires}: <img> {rel} does not exist in {pack.folder}")
        cache[rel] = read_png(path)
    return cache[rel]


def _sidecar(pack: Pack, stem: str, cells: list, columns: int, size: int) -> dict:
    """An ADR-0153 v1 sheet sidecar — the fields `mep_build._load_sheet_docs`
    and `_slice_sheet` read, and no field this tool cannot fill honestly:
    `count`/`context` are the recorder's observations, not an import's."""
    out = []
    for i, (x, y, entries, cond) in enumerate(cells):
        # ADR-0172: a CHR-ROM pack's manifest carries the CHR index and not the
        # tile's 16 bytes, so `tile` is a zero-padded placeholder here and
        # `index` is the key. build overrides `tile` with `_index_token(index)`
        # whenever the key source is index-keyed, which this project's always
        # is; the placeholder is never emitted.
        def entry(e):
            token, palette, index = e
            d = {"tile": token if not pack.index_keyed else f"{index:032X}", "palette": palette}
            if pack.index_keyed:
                d["index"] = index
            return d

        cell = {"index": i, "x": (i % columns) * 8, "y": (i // columns) * 8,
                "metatile": i, "tiles": [entry(entries[0])]}
        if len(entries) > 1:
            cell["aliases"] = [{"metatile": i * 1000 + j, "tiles": [entry(e)]}
                               for j, e in enumerate(entries[1:], 1)]
        if cond is not None:
            # ADR-0198 §1 over ADR-0197 §1's per-cell condition: this crop
            # carries exactly the one rule it was cut from. The `<condition>`
            # it names is the *pack's own*, defined in the key source the
            # import copied verbatim, so the sidecar cites it and never
            # redefines it — `conditions` here would be a second, hand-written
            # definition of something the manifest already says.
            name, exact = cond
            if name:
                cell["condition"] = name
            if exact:
                cell["exactCondition"] = True
        out.append(cell)
    return {"version": 1, "kind": "misc", "gridUnit": 8, "gutter": 0,
            "columns": columns, "sheet": f"{stem}.png", "reference": f"{stem}.orig.png",
            "cells": out}


def _copy_images(pack: Pack, auto_dir: Path) -> int:
    """The `<img>` PNGs beside the key source, verbatim.

    `auto/` is a layer in its own right, and the recorder's own projects keep
    it whole: `<rom>/auto/textures/` holds `hires.txt` *and* the art it names,
    so the layer loads as a pack. Copying them keeps the same property here —
    and `mep_lint`, which `build` runs over every layer, reads the layer's own
    `<img>` lines and fails on a manifest whose art is not beside it."""
    n = 0
    for rel in pack.imgs:
        name = rel.replace("\\", "/")
        src = pack.folder / name
        dst = auto_dir / name
        if not src.is_file():
            raise PackError(f"{pack.hires}: <img> {name} does not exist in {pack.folder}")
        dst.parent.mkdir(parents=True, exist_ok=True)
        dst.write_bytes(src.read_bytes())
        n += 1
    return n


def _copy_backgrounds(pack: Pack, auto_dir: Path) -> int:
    """The `<background>` PNGs, into `auto/textures/` under the name the line
    uses: `build` copies them up into `textures/`, which is where the loader
    resolves them from."""
    n = 0
    for raw in pack.body:
        s = raw.strip()
        m = _BG_RE.match(s)
        if not m:
            continue
        name = s[m.end():].split(",")[0].strip().replace("\\", "/")
        if not name:
            continue
        src = pack.folder / name
        dst = auto_dir / name
        if not src.is_file():
            print(f"warning: {pack.hires}: <background> {name} does not exist in {pack.folder}",
                  file=sys.stderr)
            continue
        dst.parent.mkdir(parents=True, exist_ok=True)
        dst.write_bytes(src.read_bytes())
        n += 1
    return n


def _copy_audio(pack: Pack, out: Path) -> int:
    """The `<bgm>`/`<sfx>` OGGs, into `audio/`: build keeps a seed reference
    only when its file is there, so an uncopied track is a dropped rule."""
    n = 0
    for raw in pack.audio:
        kind = "bgm" if mep_build._BGM_RE.match(raw) else "sfx"
        rx = mep_build._BGM_RE if kind == "bgm" else mep_build._SFX_RE
        fields = [f.strip() for f in (rx.match(raw).group(2)).split(",")]
        name = fields[2].replace("\\", "/") if len(fields) >= 3 else ""
        src = pack.folder / name if name else None
        if src is None or not src.is_file():
            print(f"warning: {pack.hires}: <{kind}> {name!r} has no file in "
                  f"{pack.folder}; build would drop the reference", file=sys.stderr)
            continue
        dst = out / "audio" / name
        dst.parent.mkdir(parents=True, exist_ok=True)
        dst.write_bytes(src.read_bytes())
        n += 1
    return n


def _import_note(pack: Pack, sheets: list, cells: int, twins: int, split: list,
                 normalize: bool, patched: dict | None = None) -> str:
    digest = hashlib.sha256(pack.hires.read_bytes()).hexdigest()
    lines = [
        "# Imported from a legacy HD pack",
        "",
        f"Source pack: `{pack.folder}`",
        f"Source manifest: `{pack.hires.name}` sha256 `{digest}`",
        f"Imported: {datetime.now(timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ')} by `scripts/mep_import.py`",
        "",
        f"This project is an ADR-0198 §1 import: {cells} sheet cell(s) over {len(sheets)} "
        f"sheet(s), keyed by "
        + ("CHR index (a CHR ROM pack)." if normalize else "the tile's 16 data bytes."),
        "",
        "```",
        "python3 mep_build.py build .        # regenerate textures/hires.txt from the sheets",
        "python3 mep_build.py pack . --rom <rom>",
        "```",
        "",
        "The sheets are ordinary ADR-0153 sheets: paint a `*/sheets/*.png` and `build`",
        "carries the pixels into the pack. The `*.orig.png` twin beside each sheet is the",
        "pre-import art, and is what tells the builder which cells you repainted.",
        "",
    ]
    if normalize:
        lines += [
            "An index-keyed pack names each tile by its CHR index, and `mep_build.py` writes",
            "that field in hex (ADR-0172). Keep the key source's `<ver>103` as it is: below 103",
            "the loader reads a short tile data field as decimal, so a rebuilt manifest would",
            "look up a different tile. The 32-hex `tile` field in the sidecars is a placeholder",
            "— a legacy manifest carries no CHR bytes — and is never the emitted key.",
            "",
        ]
    if twins:
        lines += [
            f"ADR-0189 §3 gives every conditioned tile a bare twin. {twins} key(s) of the source",
            "pack carried a condition without one, so the first `build` adds them; nothing the",
            "source pack said is lost by it.",
            "",
        ]
    if split:
        keys = sum(len(conds) for _pat, conds in split)
        lines += [
            f"{len(split)} tile pattern(s) of this pack are drawn at more than one crop — the way",
            f"a legacy manifest animates: the same (tile data, palette) under {keys} different",
            "conditions, each pointing at a different piece of art. Each of those rules has a",
            "cell of its own, marked `exactCondition` and naming the pack's own `<condition>`,",
            "so a rebuild emits exactly the rule the crop came from (ADR-0198 §1, on ADR-0197",
            "§1's per-cell condition). Repaint any of those cells and only that frame changes.",
            "",
            "```",
            "python3 mep_import.py verify <legacy pack> .",
            "```",
            "",
        ]
    if patched:
        lines += _patched_note(patched)
    return "\n".join(lines)


def _patched_note(p: dict) -> list:
    """The patched-ROM section of IMPORT.md (ADR-0198 §3): every hash the
    import read or wrote, and what the project does not buy."""
    chr_line = (f"CHR RAM -> {p['patched_chr_units'] * 8} KiB CHR ROM" if p["adds_chr_rom"]
                else f"CHR units {p['stock_chr_units']} -> {p['patched_chr_units']}")
    lines = [
        "## Imported against the patched ROM (ADR-0198 §3, option (a))",
        "",
        f"This pack ships a `<patch>`, so its `<tile>` keys are keyed against the ROM *after*",
        f"the patch. `{p['matched_file']}` (manifest line {p['matched_line']}, matched by the",
        f"{p['matched_by']} sha1) was applied in memory to `{p['rom'].name}`:",
        "",
        "| | whole-file sha1 (`<supportedRom>` form) | No-Intro sha1 (`targets[]` form) | size | CHR |",
        "|---|---|---|---|---|",
        f"| stock `{p['rom'].name}` | `{p['stock_whole']}` | `{p['stock_no_intro']}` | "
        f"{p['stock_size']} | {p['stock_chr_units']} |",
        f"| patched | `{p['patched_whole']}` | `{p['patched_no_intro']}` | {p['patched_size']} | "
        f"{p['patched_chr_units']} |",
        "",
        f"{p['records']} IPS record(s); {chr_line}. The key source's `<supportedRom>` is the",
        "patched whole-file sha1 (the form `HdPackBuilder` writes for the running ROM)"
        + (f"; the pack itself declared `{p['declared_supported_rom']}`." if p["declared_supported_rom"]
           else "; the pack declared none."),
        f"The {p['entries']} `<patch>` line(s) are carried with their sha1 untouched and their file",
        f"token as the `/`-separated path the IPS was copied to (`{p['matched_rel']}`, the form",
        "every platform's loader resolves; a Windows `\\` would never resolve on macOS/Linux), and",
        "the IPS sits beside both manifests, so the fork applies it exactly as it does today — by the stock",
        "ROM's sha1, never on a mismatch (ADR-0145 (3)). `mep_build.py pack . --rom <stock dump>`",
        "writes `targets[]` from the **stock** dump: the MEP matcher runs before the patch",
        "(`Emulator::InternalLoadRom`), so the patched hash is not what it compares against.",
        "",
        "**What this does not buy.**",
        "",
    ]
    lines += [f"- {n}" for n in p["note"]]
    lines.append("")
    return lines


# --- verification (the slice's stop rule) -----------------------------------

def _rules_with_keys(pack: Pack, ver: int, scale: int, base: Path) -> tuple:
    """Map every rule to its loader key and to the pixel block it names.

    The comparison is over content, never over coordinates: the import relayouts
    the crops on purpose. `blocks[key]` keeps every block the manifest names
    under that key; `first[key]` keeps the one the *first* rule naming it names,
    which is the one the run time draws (`GetMatchingTile` walks the rules in
    file order and the rules of one key share their conditions, so the first
    one that passes is the first one written)."""
    keys: dict = {}
    blocks: dict = {}
    first: dict = {}
    tokens: dict = {}
    cache: dict = {}
    for rule in pack.rules:
        if len(rule.token) >= 32:
            key = ("data", rule.token, rule.palette, rule.cond)
        else:
            key = ("index", rule.parsed_index(ver), rule.palette, rule.cond)
        keys[rule] = key
        tokens.setdefault(key, set()).add(rule.token)
        rel = pack.imgs[rule.bitmap].replace("\\", "/") if rule.bitmap < len(pack.imgs) else ""
        if rel not in cache:
            img = base / rel
            cache[rel] = read_png(img) if img.is_file() else None
        img = cache[rel]
        block = img.block(rule.x, rule.y, 8 * scale) if img is not None else None
        digest = None if block is None else hashlib.sha256(block).hexdigest()
        blocks.setdefault(key, set()).add(digest)
        first.setdefault(key, digest)
    return keys, blocks, first, tokens


_IMPORT_PATCHED_RE = re.compile(r"^\| patched \| `([0-9A-Fa-f]{40})`")


def _imported_patched_hashes(project: Path) -> set[str]:
    """The patched ROM's whole-file sha1 as the import recorded it: the
    `| patched |` row of IMPORT.md and the key source's `<supportedRom>`.
    Two records so that a hand edit or a regression in one is caught by the
    other; a set of more than one value is itself a mismatch."""
    found = set()
    note = project / "IMPORT.md"
    if note.is_file():
        for line in note.read_text(encoding="utf-8").splitlines():
            m = _IMPORT_PATCHED_RE.match(line)
            if m:
                found.add(m.group(1).upper())
    key_source = project / "auto" / "textures" / "hires.txt"
    if key_source.is_file():
        rom = Pack(project, key_source).supported_rom
        if rom:
            found.add(rom.strip().upper())
    return found


def verify_pack(src: Path, project: Path, strict: bool) -> int:
    """ADR-0198 §1's acceptance test, run against a project `build` has
    already rebuilt. Returns a process exit code."""
    source = open_pack(src)
    built_path = project / "textures" / "hires.txt"
    if not built_path.is_file():
        print(f"error: {built_path} does not exist — run `mep_build.py build {project}` first",
              file=sys.stderr)
        return 2
    built = Pack(project, built_path)
    in_keys, in_blocks, in_first, in_tokens = _rules_with_keys(
        source, source.ver, source.scale, source.folder)
    out_keys, out_blocks, _out_first, out_tokens = _rules_with_keys(
        built, built.ver, built.scale, built_path.parent)

    ins, outs = set(in_keys.values()), set(out_keys.values())
    # ADR-0189 §3: build gives a conditioned tile a bare twin. Expected, and
    # named rather than hidden — the input's own rules are all still here.
    conditioned = {(k[0], k[1], k[2]) for k in ins if k[3]}
    expected_twins = {k for k in outs - ins
                      if k[3] == "" and (k[0], k[1], k[2]) in conditioned}
    missing = ins - outs
    extra = outs - ins - expected_twins
    # Everything that is not a <tile>: condition definitions, <background>,
    # <addition>, <fallback>, the audio references and the stray lines. build
    # reorders them (conditions are hoisted above the first tile that names
    # one) and rewrites nothing, so the sets must match exactly. The audio
    # references are the one set that moves folder on the way through — build
    # regenerates them into audio/hires.txt and keeps a seed ref whose OGG is
    # there verbatim — so the built side is read from both manifests. The
    # `<patch>` lines are the other exception: the import re-emits them with
    # the normalized path the IPS was copied to (`mep_patch.emitted_line`), so
    # the source side is compared in that same form.
    in_rest = {_patch_as_emitted(s) for s in source.body} | set(source.audio)
    out_rest = set(built.body) | set(built.audio)
    audio_manifest = project / "audio" / "hires.txt"
    if audio_manifest.is_file():
        out_rest |= set(Pack(project, audio_manifest).audio)
    rest_missing, rest_extra = sorted(in_rest - out_rest), sorted(out_rest - in_rest)
    ambiguous = sorted(k for k, v in in_blocks.items() if len(v) > 1)
    # ADR-0198 §1's pixel half, read strictly: the art the built manifest draws
    # for a key must be the art the input's own first rule for that key draws,
    # because that is the rule the run time picks.
    pixel_bad = [key for key in sorted(ins & outs)
                 if out_blocks.get(key, set()) != {in_first[key]}]

    # ADR-0198 §3: a patched-ROM project must still carry the IPS beside the
    # built manifest (the loader fails the whole pack without it) and a
    # <supportedRom> the loader can read — the `<patch>` lines themselves are
    # compared above as carried lines. The token is checked **as written**,
    # the way the macOS/Linux loader resolves it: it must be the normalized
    # `/` path (`p.rel`), and that exact path must exist. A `\` token whose
    # file sits at the `/` path would otherwise pass here and never apply at
    # runtime (PR #385 review).
    patch_token_bad = [p.file for p in built.patches if p.file != p.rel]
    patch_missing = [p.file for p in built.patches
                     if p.file == p.rel and not (built_path.parent / p.rel).is_file()]
    # The built <supportedRom> must be the patched hash the import computed,
    # not merely 40 hex digits (PR #385 review): IMPORT.md's "patched" row is
    # the import's own record, and the key source carries the same value into
    # every rebuild. Either disagreeing with the built manifest fails.
    expected_roms = sorted(_imported_patched_hashes(project)) if built.patches else []
    patch_bad_rom = (bool(built.patches)
                     and (not (built.supported_rom and _HEX40_RE.match(built.supported_rom))
                          or not expected_roms
                          or any(h != built.supported_rom.upper() for h in expected_roms)))

    token_delta = sum(1 for k in ins & outs if in_tokens[k] != out_tokens[k])
    print(f"source {source.hires}: {len(in_keys)} rule(s), {len(ins)} distinct key(s)")
    print(f"built  {built_path}: {len(out_keys)} rule(s), {len(outs)} distinct key(s)")
    print(f"rule set: {len(missing)} missing, {len(extra)} unexpected extra, "
          f"{len(expected_twins)} ADR-0189 §3 twin(s) added by build")
    print(f"pixels: {len(ins & outs) - len(pixel_bad)} key(s) draw exactly the art the source's "
          f"first matching rule draws, {len(pixel_bad)} differ"
          + (f"; {len(ambiguous)} key(s) name more than one crop in the source" if ambiguous else ""))
    print(f"carried: {len(in_rest)} condition/background/addition/fallback/audio line(s) "
          f"({len(rest_missing)} missing, {len(rest_extra)} unexpected extra)")
    if token_delta:
        example = next(k for k in sorted(ins & outs) if in_tokens[k] != out_tokens[k])
        print(f"tokens: {token_delta} key(s) whose tileData *text* changed, e.g. "
              f"{sorted(in_tokens[example])[0]} -> {sorted(out_tokens[example])[0]} — "
              "`_index_token`'s hex width (ADR-0172); the parsed key is unchanged")
    if built.patches:
        print(f"patch: {len(built.patches)} <patch> line(s) carried; <supportedRom> "
              f"{built.supported_rom or '(none)'} "
              + ("(the pack declared none)" if not source.supported_rom
                 else f"(the pack declared {source.supported_rom})")
              + "; IPS beside the built manifest: "
              + ('yes' if not (patch_missing or patch_token_bad) else 'NO'))
    failures = 0
    for name, items in (("missing from the import", sorted(missing)),
                        ("unexpected in the import", sorted(extra)),
                        ("carried line missing", rest_missing),
                        ("unexpected carried line", rest_extra),
                        ("pixels differ", pixel_bad),
                        ("<patch> token is not the normalized /-separated path the IPS was "
                         "copied to (the macOS/Linux loader resolves it as written)",
                         patch_token_bad),
                        ("<patch> file missing beside textures/hires.txt", patch_missing),
                        ("<supportedRom> is not the patched ROM the import computed",
                         [f"{built.supported_rom or '(none)'} (imported: "
                          f"{', '.join(expected_roms) or 'no record'})"]
                         if patch_bad_rom else [])):
        for k in items[:10]:
            print(f"  {name}: {k}")
            failures += 1
    if strict and expected_twins:
        print(f"  strict: {len(expected_twins)} twin(s) are still a difference from the input",
              file=sys.stderr)
        failures += 1
    if failures:
        print(f"FAIL: the import does not round-trip ({failures} finding(s) shown)", file=sys.stderr)
        return 1
    print("OK: build on the imported project regenerates the input's rule set with identical pixels")
    return 0


def _patch_as_emitted(body_line: str) -> str:
    """A source body line in the form the import carries it: a `<patch>` line
    becomes `mep_patch.emitted_line` (normalized path, uppercase sha1); any
    other line is itself. `open_pack` already parsed every `<patch>` line, so
    this cannot raise."""
    if not body_line.startswith("<patch>"):
        return body_line
    return mep_patch.emitted_line(mep_patch.patch_lines([body_line])[0])


def _token_as_built(rule: Rule, ver: int) -> str:
    return rule.token if len(rule.token) >= 32 else mep_build._index_token(rule.parsed_index(ver))


# --- the index read (ADR-0210 §3) -------------------------------------------
#
# A community pack's `hires.txt` read as an **index of facts about the ROM**:
# which tiles exist and which palettes the game puts them under. Not one PNG of
# that pack is opened — the shapes it names are the game's own bytes, not its
# author's art — and not one `<condition>` line is read (ADR-0183 §3: a
# `memoryCheck` is the other author's *reading* of the machine, and this
# toolchain emits observations; importing one would launder a claim into our
# evidence).
#
# The ROM decides which of two things happens, and both are decided from the
# key's own form (`HdPackLoader::ReadTileData`):
#
#   * **CHR RAM** (no CHR in the iNES file) — a `<tile>` key's 32-hex `tileData`
#     *is* the game's 16 pattern bytes, because the art is not in the ROM file
#     in tile form. Every shape our recording does not already hold is rendered
#     from those bytes into `sheets/index.png` / `.orig.png` / `.json`,
#     provenance `index`, `seen: false`: art, but not observed art.
#   * **CHR ROM** — the shape half of a key is a pointer into the ROM's own
#     CHR, which `artist_chr_kit.py` already publishes whole, so a third party
#     cannot add a shape here. Only the **palette set** is taken, and only for
#     indices that exist in the dump; the run says so and writes no sheet.
#
# The three filters are mandatory (ADR-0210 §3), and each one exists because
# the measurement tripped over it:
#
#   1. **Index range** — a key whose `TileIndex` is past the loaded CHR names
#      art this dump does not have. Read the loader's own way, `<ver>` decides
#      whether that field is decimal (<= 102) or hex (103+); reading a
#      `<ver>100` pack as hex is how a legitimate pack gets misread as aimed at
#      another ROM (`int(field, 16)` is not a normalisation, it is a reading).
#   2. **`<patch>`** — refused by name, naming ADR-0198 §2/§3: its keys are bank
#      indices of the patched ROM, a namespace the stock-ROM tools never meet.
#   3. **Conditions** — a `[...]` prefix is dropped, never carried: the tile
#      exists, the condition is the other author's claim about when it draws.

_INDEX_STEM = "index"
# The sheet's own geometry, the one `_write_sheets` uses for an import: a
# 16-cell-wide grid of 8px logical cells, no gutter.
_INDEX_COLUMNS = 16


def _open_manifest(path: Path) -> Pack:
    """A `Pack` from either the manifest itself or the folder holding it."""
    return Pack(path.parent, path) if path.is_file() else open_pack(path)


def _nes_palette() -> list:
    """2C02 RGB, from `artist_map` — the copy the artist tools read, taken on
    import so this tool carries no second colour table."""
    import artist_map
    return artist_map.NES_PALETTE


def _rom_chr_tiles(rom: Path) -> int:
    """The loaded dump's CHR tile count, walked the kit's own way (ADR-0183):
    `artist_chr_kit.Rom` is this tree's iNES reader, and it is also what says
    whether the game is CHR ROM at all (byte 5 > 0)."""
    import artist_chr_kit
    try:
        return artist_chr_kit.Rom(rom).chr_tile_count
    except (OSError, artist_chr_kit.ChrKitError) as e:
        raise PackError(f"{rom}: cannot be read as an iNES ROM ({e})") from e


def _check_supported_rom(pack: Pack, rom: Path) -> str:
    """The recording and the dump must be the same binary or the indices mean
    nothing (ADR-0210 Consequences: the importer "always needs the matching
    dump present", and that is what catches a pack aimed at another ROM)."""
    want = next((h[len("<supportedRom>"):].strip() for h in pack.header
                 if h.startswith("<supportedRom>")), "")
    got = hashlib.sha1(rom.read_bytes()).hexdigest()
    if not want:
        print(f"note: {pack.hires} carries no <supportedRom>; the dump was not checked "
              "against the recording", file=sys.stderr)
        return got
    if want.lower() != got.lower():
        raise PackError(f"{rom} is not the dump {pack.hires} was recorded from "
                        f"(<supportedRom> {want.upper()}, file sha1 {got.upper()}) — the keys "
                        "describe that ROM's tiles, not this one's (ADR-0210 §3)")
    return got


def render_pattern(data_hex: str, pal_hex: str, scale: int) -> bytes:
    """A key's own 16 pattern bytes as one `8*scale` square RGBA cell.

    2bpp NES CHR, the same unpack `artist_map.render_tile` does for a key the
    pack never wrote art for: eight low-plane bytes, then eight high-plane
    bytes, two bits per pixel, looked up in the key's own four palette indices
    (2C02 RGB, read from `artist_map` so this tool carries no second table).

    The upscale is nearest neighbour — `ScaleFilterType::Prescale`, which is
    the recorder's own path (`HdPackBuilder` falls back to it whenever the
    pack's `<scale>` is forced) and which every sampled cell of the bounded
    input's recording is pixel-exact against (448 of 448, measured 2026-09-20).
    Which *smoothing* filter a run used is a recording-time choice the manifest
    does not carry, so a tool that renders a key from bytes cannot always know
    it — and nearest neighbour invents no edge the ROM does not have, which is
    the same reason ADR-0183 §3 gives a ROM-derived kit cell its crisp fill."""
    data = bytes.fromhex(data_hex)
    table = _nes_palette()
    pal = [table[int(pal_hex[i:i + 2], 16) & 0x3F] for i in range(0, 8, 2)]
    block = bytearray()
    for y in range(8):
        lo, hi = data[y], data[y + 8]
        row = bytearray()
        for x in range(8):
            bit = 7 - x
            r, g, b = pal[((lo >> bit) & 1) | (((hi >> bit) & 1) << 1)]
            row += bytes((r, g, b, 0xFF))
        block += bytes(row) * scale
    return bytes(block) * scale


def read_index(their: Path, pack_dir: Path, rom: Path) -> dict:
    """The whole read side: their manifest, our recording and the dump, plus
    every filter, as one plan. Nothing is written here."""
    ours = _open_manifest(pack_dir)
    if not ours.rules:
        raise PackError(f"{ours.hires}: no <tile> entries — there is no recording to compare "
                        "against")
    if not 1 <= ours.scale <= 10:
        raise PackError(f"{ours.hires}: <scale>{ours.scale} is outside the loader's 1..10")
    pack_dir = ours.folder
    theirs = _open_manifest(their)
    if not theirs.rules:
        raise PackError(f"{theirs.hires}: no <tile> entries to read")
    if theirs.patches:
        # Filter 2 (ADR-0210 §3): the keys of a pack with <patch> are bank
        # indices of the patched ROM, a namespace this dump never produces.
        # The import side admits such a pack against the patched ROM (ADR-0198
        # §3); the index read, which is about *this* dump, does not.
        first = theirs.patches[0]
        raise PackError(
            f"{theirs.hires}:{first.line}: this pack ships a <patch>, so its <tile> keys name the "
            "patched ROM's tiles, not this dump's — ADR-0198 §2/§3 keeps it out of the stock-ROM "
            "index read (ADR-0210 §3 filter 2). Import it against the patched ROM with "
            "`mep_import.py <pack> --out <project> --rom <dump>` instead")
    chr_tiles = _rom_chr_tiles(rom)
    rom_sha1 = _check_supported_rom(ours, rom)
    if (chr_tiles > 0) != ours.index_keyed:
        kind = f"a CHR ROM game ({chr_tiles} tiles)" if chr_tiles else "a CHR RAM game"
        raise PackError(
            f"{rom} is {kind}, so its recording keys <tile> by "
            f"{'CHR index' if chr_tiles else 'the tile’s 16 data bytes'}, but {ours.hires} "
            f"keys them by {'CHR index' if ours.index_keyed else '16 data bytes'} — this "
            "recording and this dump are not the same game (ADR-0210 §3)")
    if ours.index_keyed != theirs.index_keyed:
        raise PackError(
            f"{theirs.hires} keys <tile> by "
            f"{'CHR index' if theirs.index_keyed else 'the tile’s 16 data bytes'} while "
            f"{ours.hires} keys them by {'CHR index' if ours.index_keyed else '16 data bytes'}"
            " — the two namespaces never meet: a pack keyed by CHR index against a CHR RAM "
            "recording is a pack for a patched ROM, which ADR-0198 §2/§3 keeps out of the "
            "stock-ROM tools, and the reverse is a pack this dump cannot check")
    plan = {
        "their": theirs.hires,
        "their_sha256": hashlib.sha256(theirs.hires.read_bytes()).hexdigest(),
        "their_rules": len(theirs.rules),
        "their_ver": theirs.ver,
        "their_keyed": "index" if theirs.index_keyed else "data",
        "pack": ours.hires,
        "rom": rom,
        "rom_sha1": rom_sha1,
        "chr_tiles": chr_tiles,
        "game": "chr_rom" if chr_tiles else "chr_ram",
        "scale": ours.scale,
        # A `[...]` prefix is dropped, never carried (filter 3).
        "conditioned_rules": sum(1 for r in theirs.rules if r.cond),
        "ours_rules": len(ours.rules),
        "cells": [],
        "palettes": [],
        "dropped": {},
    }
    if chr_tiles:
        _read_index_palettes(theirs, chr_tiles, plan)
    else:
        _read_index_shapes(ours, theirs, plan)
    return plan


def _recorded_shapes(pack: Pack) -> set:
    """Every 32-hex `tileData` our recording already holds: the keys its
    manifest lists **and** the keys its own sheets carry.

    The manifest alone is not enough, and the bounded input proves it: 251 of
    its sheets' shapes are absent from its manifest (the recorder's sheets keep
    palette variants and shapes a later manifest no longer lists). Excluding
    only the manifest's let the index sheet claim 30 keys a recorded sheet
    already owned, and `mep_build` handed them over — "spr005.png loses tile
    ... to sheets/index.png" on the F12.12 bounded run, 2026-09-20. F12.8's
    sheet and this one are meant to be disjoint by construction (ADR-0209
    Q4(k)), so the exclusion reads what `build` itself emits: every sidecar it
    accepts, every `tiles[]` entry, aliases included, and each entry's
    `source` — the un-flipped form ADR-0178 makes build emit for a sprite crop
    whose recorded bitmap has its OAM flips baked in (five more keys of the
    bounded input are on a recorded sheet only in that form)."""
    shapes = {r.token for r in pack.rules if len(r.token) >= 32}
    docs, _claimed = mep_build._load_sheet_docs(pack.hires.parent / "sheets")
    for sd in docs:
        # This tool's own surface is not part of the recording: excluding it
        # would make a second run report "nothing new" against the first run's
        # own output instead of against what the recorder observed.
        if sd.json_path.stem == _INDEX_STEM:
            continue
        for cell in sd.cells:
            if not isinstance(cell, dict):
                continue
            groups = [cell.get("tiles")]
            groups += [a.get("tiles") for a in cell.get("aliases") or [] if isinstance(a, dict)]
            for group in groups:
                for entry in group or []:
                    if not isinstance(entry, dict):
                        continue
                    for field in ("tile", "source"):
                        token = str(entry.get(field) or "").strip().upper()
                        if len(token) >= 32:
                            shapes.add(token)
    return shapes


def _read_index_shapes(ours: Pack, theirs: Pack, plan: dict):
    """CHR RAM: one cell per shape our recording does not already hold.

    Keyed by shape, the way ADR-0209 measured the gain ("distinct art, palette
    ignored"): two of their keys that name the same 16 bytes are one picture.
    The other palettes of that shape become the cell's `aliases`, which
    `mep_build`'s ADR-0153 §3 pass emits from the same crop — so every key
    their manifest names is reachable, and none of them is drawn twice."""
    ours_shapes = _recorded_shapes(ours)
    shapes: dict = {}
    short = 0
    recorded = 0
    for rule in theirs.rules:
        if len(rule.token) < 32:
            short += 1
            continue
        if rule.token in ours_shapes:
            recorded += 1
            continue
        pals = shapes.setdefault(rule.token, [])
        if rule.palette not in pals:
            pals.append(rule.palette)
    plan["cells"] = [{"tile": shape, "palette": pals[0], "aliases": pals[1:]}
                     for shape, pals in shapes.items()]
    plan["palettes"] = sorted({p for pals in shapes.values() for p in pals})
    plan["dropped"] = {"short_key": short, "already_recorded": recorded}
    plan["our_shapes"] = len(ours_shapes)
    plan["their_shapes"] = len({r.token for r in theirs.rules if len(r.token) >= 32})
    plan["shapes"] = len(shapes)
    # What a rebuild emits: one rule per key of theirs that is new to us.
    plan["keys"] = sum(len(p) for p in shapes.values())


def _read_index_palettes(theirs: Pack, chr_tiles: int, plan: dict):
    """CHR ROM: the shape half is discarded, the palette set is taken.

    **Filter 1, index range**, read the loader's own way (`Rule.parsed_index`):
    a key past the loaded CHR names a tile this dump does not have, so its
    palette is a claim about another binary and is dropped."""
    palettes: list = []
    in_range = 0
    dropped_rules = 0
    dropped_keys = set()
    dropped_idx = set()
    for rule in theirs.rules:
        index = rule.parsed_index(theirs.ver)
        if index >= chr_tiles:
            dropped_rules += 1
            dropped_keys.add((index, rule.palette))
            dropped_idx.add(index)
            continue
        in_range += 1
        if rule.palette not in palettes:
            palettes.append(rule.palette)
    plan["palettes"] = palettes
    plan["in_range_rules"] = in_range
    plan["dropped"] = {"out_of_range_rules": dropped_rules,
                       "out_of_range_keys": len(dropped_keys),
                       "out_of_range_indices": len(dropped_idx)}
    plan["shapes"] = 0
    plan["keys"] = 0


def _write_index_report(plan: dict, out: Path | None):
    """The read's own provenance as JSON: which of their keys was read, what
    was dropped and why. `--report` writes it anywhere; the CHR RAM sheet
    carries the same block in its sidecar, and a CHR ROM run writes no sheet at
    all, so this is the only durable trace of a palette-only read."""
    if out is None:
        return
    doc = {k: v for k, v in plan.items()}
    doc["rom"] = str(plan["rom"])
    doc["pack"] = str(plan["pack"])
    doc["their"] = str(plan["their"])
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(doc, indent=1) + "\n", encoding="utf-8")


def _index_sidecar(plan: dict, columns: int) -> dict:
    """An ADR-0153 v1 sheet sidecar — every field `mep_build._load_sheet_docs`
    and `_slice_sheet` read, plus this source's provenance (ADR-0210: "every
    cell carries its source", and only `recorded` is `seen: true`)."""
    cells = []
    for i, cell in enumerate(plan["cells"]):
        def entry(palette):
            return {"tile": cell["tile"], "palette": palette}
        doc = {"index": i, "metatile": i, "x": (i % columns) * 8, "y": (i // columns) * 8,
               "tiles": [entry(cell["palette"])], "source": "index", "seen": False}
        if cell["aliases"]:
            doc["aliases"] = [{"metatile": i * 1000 + j, "tiles": [entry(p)]}
                              for j, p in enumerate(cell["aliases"], 1)]
        cells.append(doc)
    return {
        "version": 1, "kind": "index", "gridUnit": 8, "gutter": 0,
        "cell": {"w": 8, "h": 8}, "columns": columns,
        "sheet": f"{_INDEX_STEM}.png", "reference": f"{_INDEX_STEM}.orig.png",
        "source": "index", "seen": False,
        "origin": {
            "hires": plan["their"].name, "sha256": plan["their_sha256"],
            "rules": plan["their_rules"], "ver": plan["their_ver"],
            "shapes": plan.get("their_shapes", 0),
            "conditionsRead": 0, "conditionedRules": plan["conditioned_rules"],
            "dropped": plan["dropped"],
        },
        "cells": cells,
    }


def write_index_sheet(plan: dict, force: bool) -> list:
    """Write `sheets/index.png`, `index.orig.png` and `index.json` beside our
    recording's manifest. Refuses to overwrite an existing index sheet without
    `--force`: it is a surface an artist may have painted on."""
    sheets_dir = plan["pack"].parent / "sheets"
    for name in (f"{_INDEX_STEM}.png", f"{_INDEX_STEM}.orig.png", f"{_INDEX_STEM}.json"):
        if (sheets_dir / name).exists() and not force:
            raise PackError(f"{sheets_dir / name} already exists; the artist may have painted it "
                            "— pass --force to overwrite the index sheet")
    scale = plan["scale"]
    cells = plan["cells"]
    size = 8 * scale
    columns = min(_INDEX_COLUMNS, max(1, len(cells)))
    rows = (len(cells) + columns - 1) // columns
    sheet = Image(columns * size, rows * size)
    for i, cell in enumerate(cells):
        sheet.paste(render_pattern(cell["tile"], cell["palette"], scale),
                    (i % columns) * size, (i // columns) * size, size)
    sheets_dir.mkdir(parents=True, exist_ok=True)
    write_png(sheets_dir / f"{_INDEX_STEM}.png", sheet)
    write_png(sheets_dir / f"{_INDEX_STEM}.orig.png", sheet.downscale(scale))
    (sheets_dir / f"{_INDEX_STEM}.json").write_text(
        json.dumps(_index_sidecar(plan, columns), indent=1) + "\n", encoding="utf-8")
    return [f"{_INDEX_STEM}.png", f"{_INDEX_STEM}.orig.png", f"{_INDEX_STEM}.json"]


def index_run(their: Path, pack_dir: Path, rom: Path, report: Path | None, force: bool) -> dict:
    plan = read_index(their, pack_dir, rom)
    plan["written"] = []
    if plan["game"] == "chr_ram" and plan["cells"]:
        plan["written"] = write_index_sheet(plan, force)
    if report is not None:
        _write_index_report(plan, report)
    return plan


def _print_index_summary(plan: dict):
    """What the tool says about what it did — the CHR ROM half of ADR-0210 §3
    is a *statement*, since there is nothing to draw there."""
    dropped = plan["dropped"]
    print(f"index {plan['their']}: {plan['their_rules']} rule(s), "
          f"{plan.get('their_shapes', 0)} distinct 32-hex shape(s)")
    if plan["game"] == "chr_rom":
        print(f"  {plan['rom'].name} is a CHR ROM game ({plan['chr_tiles']} CHR tiles): the shape "
              "half of every key is discarded — artist_chr_kit.py already publishes this ROM's "
              "CHR whole — so only the palette set is taken (ADR-0210 §3)")
        print(f"  {plan['in_range_rules']} of {plan['their_rules']} rule(s) name a tile this dump "
              f"has; {len(plan['palettes'])} palette(s) taken")
        if dropped["out_of_range_rules"]:
            print(f"  dropped as out of range: {dropped['out_of_range_rules']} rule(s), "
                  f"{dropped['out_of_range_indices']} tile index(es) past {plan['chr_tiles']} "
                  f"({dropped['out_of_range_keys']} distinct key(s))")
        else:
            print("  nothing dropped as out of range: every index this pack names exists in the "
                  "dump")
        print("  no sheet written: a CHR ROM game's shapes are already in the ROM, and this pack "
              "adds none")
    elif not plan["cells"]:
        print(f"  no shape here is new: our recording ({plan['ours_rules']} rule(s)) already holds "
              "every 32-hex shape this pack names — no sheet written")
    else:
        print(f"  wrote {', '.join('sheets/' + n for n in plan['written'])}: "
              f"{len(plan['cells'])} cell(s) rendered from the pack's own pattern bytes, "
              f"{plan['keys']} key(s) once built, {len(plan['palettes'])} palette(s)")
    if dropped.get("short_key"):
        print(f"  {dropped['short_key']} rule(s) key a tile by CHR index in a CHR RAM game's "
              "namespace; skipped")
    if plan["conditioned_rules"]:
        print(f"  <condition> lines were never read: {plan['conditioned_rules']} conditioned "
              "rule(s) contributed the bare key of the tile they name, no condition (ADR-0210 §3)")
    print(f"  provenance: index, seen: false — this is the ROM's art, not art anyone observed "
          f"(ADR-0183 §3); {plan['rom'].name} sha1 {plan['rom_sha1'].upper()[:12]}…")


# --- CLI ---------------------------------------------------------------------

def main(argv=None) -> int:
    p = argparse.ArgumentParser(
        prog="mep_import.py",
        description="Import a legacy HD pack (hires.txt + PNGs) as a MEP project, per ADR-0198 "
                    "§1; a pack with <patch> is imported against the patched ROM and needs "
                    "--rom (ADR-0198 §3).")
    sub = p.add_subparsers(dest="cmd")
    imp = sub.add_parser("import", help="write the project (default)")
    imp.add_argument("pack", help="the legacy pack folder (holding hires.txt)")
    imp.add_argument("--out", required=True, help="the project folder to write")
    imp.add_argument("--force", action="store_true", help="write into a non-empty --out")
    imp.add_argument("--rom", help="the stock dump a <patch> line was made for; the IPS is "
                                   "applied in memory and the project keys against the result "
                                   "(ADR-0198 §3). Required for a pack with <patch>")
    ver = sub.add_parser("verify", help="check a rebuilt project against the input (ADR-0198 §1)")
    ver.add_argument("pack")
    ver.add_argument("project")
    ver.add_argument("--strict", action="store_true",
                     help="a difference is a difference: fail on the ADR-0189 §3 twins too")
    idx = sub.add_parser("index", help="read a community pack's hires.txt as facts about the ROM "
                                       "and render the shapes our recording lacks (ADR-0210 §3)")
    idx.add_argument("hires", help="their hires.txt, or the folder holding it")
    idx.add_argument("--pack", required=True,
                     help="our recording's pack folder (the one holding textures/hires.txt)")
    idx.add_argument("--rom", required=True,
                     help="the dump those keys describe; the CHR in it is the range filter")
    idx.add_argument("--report", help="also write this run's JSON report to this path")
    idx.add_argument("--force", action="store_true", help="overwrite an existing index sheet")

    argv = list(sys.argv[1:] if argv is None else argv)
    if argv and argv[0] not in ("import", "verify", "index", "-h", "--help"):
        argv.insert(0, "import")
    args = p.parse_args(argv)
    try:
        if args.cmd == "verify":
            return verify_pack(Path(args.pack).resolve(), Path(args.project).resolve(),
                               args.strict)
        if args.cmd == "index":
            summary = index_run(Path(args.hires).resolve(), Path(args.pack).resolve(),
                                Path(args.rom).resolve(),
                                Path(args.report).resolve() if args.report else None, args.force)
            _print_index_summary(summary)
            return 0
        summary = import_pack(Path(args.pack).resolve(), Path(args.out).resolve(), args.force,
                              Path(args.rom).resolve() if args.rom else None)
    except PackError as e:
        print(f"error: {e}", file=sys.stderr)
        return 2
    print(f"imported {summary['src']} -> {summary['out']}")
    if summary["normalized"]:
        keyed = "keyed by CHR index, hex tokens"
        keyed += ", <ver> raised to 103" if summary["ver"] < _HEX_INDEX_VER else ""
    else:
        keyed = "keyed by the tile's 16 data bytes"
    print(f"  {summary['rules']} rule(s) over {summary['cells']} cell(s) in "
          f"{len(summary['sheets'])} sheet(s) ({keyed})")
    if summary["backgrounds"]:
        print(f"  {summary['backgrounds']} <background> PNG(s) copied into auto/textures/")
    if summary["audio"]:
        print(f"  {summary['audio']} audio file(s) copied into audio/")
    if summary["split"]:
        keys = sum(len(conds) for _pat, conds in summary["split"])
        print(f"  {len(summary['split'])} pattern(s) are drawn at more than one crop "
              f"({keys} rule(s)); each got a cell of its own, pinned to the condition it "
              "carried (ADR-0198 §1)")
    if summary["strays"]:
        print(f"  note: {len(summary['strays'])} line(s) are not a tag and carry no rule "
              f"(the loader skips them too); kept verbatim: line(s) "
              f"{', '.join(str(n) for n in summary['strays'][:5])}")
    if summary["twins"]:
        print(f"  note: {summary['twins']} key(s) get an ADR-0189 §3 bare twin on the first build")
    if summary["patch"]:
        _print_patched(summary["patch"])
    print(f"  next: python3 mep_build.py build {summary['out']}")
    return 0


def _print_patched(p: dict):
    """The patched-ROM block of the import's output, limit included — ADR-0198
    §3 asks for it on every such import, so it is not behind a flag."""
    chr_line = (f"CHR RAM -> {p['patched_chr_units'] * 8} KiB CHR ROM" if p["adds_chr_rom"]
                else f"CHR units {p['stock_chr_units']} -> {p['patched_chr_units']}")
    print(f"  patched ROM (ADR-0198 §3): {p['matched_file']} (line {p['matched_line']}, matched by "
          f"the {p['matched_by']} sha1) applied to {p['rom'].name}: {p['records']} record(s), "
          f"{p['stock_size']} -> {p['patched_size']} bytes, {chr_line}")
    print(f"    stock   whole-file {p['stock_whole']}  No-Intro {p['stock_no_intro']}")
    print(f"    patched whole-file {p['patched_whole']}  No-Intro {p['patched_no_intro']}")
    print(f"    <supportedRom> written: {p['patched_whole']}"
          + (f" (the pack declared {p['declared_supported_rom']})" if p["declared_supported_rom"]
             else " (the pack declared none)")
          + f"; {p['entries']} <patch> line(s) carried (sha1 as written, file token as the "
          f"/-separated path the IPS was copied to), {p['files']} IPS file(s) beside both "
          "manifests")
    print("    what this does not buy:")
    for n in p["note"]:
        print(f"      - {n}")


if __name__ == "__main__":
    sys.exit(main())
