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

Scope: a pack that ships a `<patch>` is **refused**, naming ADR-0198 §2/§3 —
its `<tile>` keys are bank indices of the *patched* ROM, so importing it as a
plain project would write the wrong namespace silently. Plain packs first
(§2); the patched-ROM import is a follow-up slice (§3 option (a)).

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

Usage:
  python3 mep_import.py <legacy pack folder> --out <project folder> [--force]
  python3 mep_import.py verify <legacy pack folder> <project folder> [--strict]
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

# The sheet geometry every imported sheet uses: a 16-cell-wide grid of 8px
# logical cells, no gutter — the shape the recorder's contact sheets have
# (SheetRender), so an imported sheet reads like a recorded one.
_SHEET_COLUMNS = 16
_HEX_DATA_RE = re.compile(r"^[0-9A-F]{32}$")
_HEX_PAL_RE = re.compile(r"^[0-9A-F]{8}$")
_HEX_TOKEN_RE = re.compile(r"^[0-9A-F]+$")
_TAG_RE = re.compile(r"^(\[[^\]]*\])?(<[A-Za-z]+>)")
# A line whose start looks like a tag: what an unknown tag is refused for.
_TAG_START_RE = re.compile(r"^(\[[^\]]*\])?<")
_IMG_RE = re.compile(r"^(\[[^\]]*\])?<img>")
_BG_RE = re.compile(r"^(\[[^\]]*\])?<background>")
# The only two tags whose `[...]` prefix the loader gives a meaning.
_PREFIXED_TAGS = frozenset({"<tile>", "<background>"})
# HdPackLoader's own dispatch list — every tag it reads, and no other. Anything
# else is refused (never dropped); `<patch>` is in the list and is refused
# separately, by name, per ADR-0198 §2.
_KNOWN_TAGS = frozenset(
    {"<tile>", "<background>", "<condition>", "<img>", "<addition>", "<fallback>",
     "<bgm>", "<sfx>"}
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
            if tag == "<patch>":
                raise PackError(
                    f"{self.hires}:{i + 1}: this pack ships a <patch>. Its <tile> keys are bank "
                    "indices of the patched ROM, a namespace the stock-ROM tools never meet, so "
                    "ADR-0198 §2 refuses the import until §3's patched-ROM slice lands (see "
                    "docs/adr/0198-*.md). Import a pack without <patch> first.")
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


def build_key_source(pack: Pack, normalize: bool) -> list[str]:
    """The manifest `mep_build.py build` reads as its key source: the input's
    own lines, with tileData rewritten to the form build looks up."""
    out = []
    n = -1
    for raw in pack.lines:
        s = raw.strip()
        m = mep_build._TILE_RE.match(s)
        if not normalize or not m or s.startswith("#"):
            out.append(_raise_ver(raw) if normalize else raw)
            continue
        n += 1
        rule = pack.rules[n]
        f = [x.strip() for x in m.group(2).split(",")]
        f[1] = mep_build._index_token(rule.parsed_index(pack.ver)) if pack.index_keyed else f[1].upper()
        out.append(f"{m.group(1) or ''}<tile>{','.join(f)}")
    return out


def _raise_ver(raw: str) -> str:
    s = raw.strip()
    return f"<ver>{_HEX_INDEX_VER}" if s.startswith("<ver>") else raw


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

def import_pack(src: Path, out: Path, force: bool) -> dict:
    pack = open_pack(src)
    if not pack.rules:
        raise PackError(f"{pack.hires}: no <tile> entries to import")
    if not 1 <= pack.scale <= 10:
        raise PackError(f"{pack.hires}: <scale>{pack.scale} is outside the loader's 1..10")

    normalize = pack.index_keyed
    if normalize:
        reason = ver_sensitive_reason(pack)
        if reason:
            raise PackError(
                f"{pack.hires}: this pack is index-keyed (CHR ROM) and its <tile> keys must be "
                f"rewritten to the hex form mep_build re-emits, but {reason} — the rewrite would "
                "change how the loader reads that line, so ADR-0198 §1 refuses it rather than "
                "import a pack whose keys or conditions shift")
    key_lines = build_key_source(pack, normalize)
    attrs = lookup_attrs(key_lines)
    # The whole read side first — every <img> decodes, every cell's PNG name
    # is usable, every planned crop fits its image — because a refusal must
    # not leave a half-written project behind: the retry would then hit the
    # non-empty--out guard instead of the real error.
    plan, split = _plan_cells(pack)
    twins, problems = emitted_attrs(pack, attrs, normalize, {p for p, _c in split})
    if problems:
        first = problems[0]
        raise PackError(
            f"{pack.hires}:{first.line}: mep_build would rewrite this rule's fields (its key is "
            f"{first.token}/{first.palette}, which the key source does not carry in the form build "
            "looks up) — the import refuses rather than drop a condition or a brightness "
            "(ADR-0198 §1)")

    sources = _read_sources(pack, plan)
    if out.exists() and any(out.iterdir()) and not force:
        raise PackError(f"{out} exists and is not empty; pass --force to write into it")
    sheets_dir = out / "textures" / "sheets"
    auto_dir = out / "auto" / "textures"
    sheets_dir.mkdir(parents=True, exist_ok=True)
    auto_dir.mkdir(parents=True, exist_ok=True)
    (auto_dir / "hires.txt").write_text("\n".join(key_lines) + "\n", encoding="utf-8")

    sheets = _write_sheets(pack, plan, sources, sheets_dir)
    _copy_images(pack, auto_dir)
    backgrounds = _copy_backgrounds(pack, auto_dir)
    audio = _copy_audio(pack, out)
    (out / "IMPORT.md").write_text(
        _import_note(pack, sheets, sum(len(v) for v in plan.values()), len(twins), split, normalize),
        encoding="utf-8")

    return {
        "src": pack.hires,
        "strays": pack.strays,
        "rules": len(pack.rules),
        "cells": sum(len(v) for v in plan.values()),
        "sheets": sheets,
        "images": len(plan),
        "twins": len(twins),
        "split": split,
        "keyed": "index" if pack.index_keyed else "data",
        "ver": pack.ver,
        "normalized": normalize,
        "backgrounds": backgrounds,
        "audio": audio,
        "out": out,
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
                 normalize: bool) -> str:
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
    return "\n".join(lines)


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
    # there verbatim — so the built side is read from both manifests.
    in_rest = set(source.body) | set(source.audio)
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
    failures = 0
    for name, items in (("missing from the import", sorted(missing)),
                        ("unexpected in the import", sorted(extra)),
                        ("carried line missing", rest_missing),
                        ("unexpected carried line", rest_extra),
                        ("pixels differ", pixel_bad)):
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


def _token_as_built(rule: Rule, ver: int) -> str:
    return rule.token if len(rule.token) >= 32 else mep_build._index_token(rule.parsed_index(ver))


# --- CLI ---------------------------------------------------------------------

def main(argv=None) -> int:
    p = argparse.ArgumentParser(
        prog="mep_import.py",
        description="Import a legacy plain HD pack (hires.txt + PNGs, no <patch>) as a MEP "
                    "project, per ADR-0198 §1.")
    sub = p.add_subparsers(dest="cmd")
    imp = sub.add_parser("import", help="write the project (default)")
    imp.add_argument("pack", help="the legacy pack folder (holding hires.txt)")
    imp.add_argument("--out", required=True, help="the project folder to write")
    imp.add_argument("--force", action="store_true", help="write into a non-empty --out")
    ver = sub.add_parser("verify", help="check a rebuilt project against the input (ADR-0198 §1)")
    ver.add_argument("pack")
    ver.add_argument("project")
    ver.add_argument("--strict", action="store_true",
                     help="a difference is a difference: fail on the ADR-0189 §3 twins too")

    argv = list(sys.argv[1:] if argv is None else argv)
    if argv and argv[0] not in ("import", "verify", "-h", "--help"):
        argv.insert(0, "import")
    args = p.parse_args(argv)
    try:
        if args.cmd == "verify":
            return verify_pack(Path(args.pack).resolve(), Path(args.project).resolve(),
                               args.strict)
        summary = import_pack(Path(args.pack).resolve(), Path(args.out).resolve(), args.force)
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
    print(f"  next: python3 mep_build.py build {summary['out']}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
