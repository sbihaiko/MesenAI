#!/usr/bin/env python3
"""Read Mesen Code/Data Logger (`.cdl`) files for NES ROMs and turn them into
something a human can act on.

The point is to disassemble only the part of a ROM a run actually used. Mesen's
debugger writes one flag byte per ROM byte; this tool parses that file against
the ROM's own iNES header, reports coverage, merges the files from several runs,
and prints the contiguous regions a person (or a disassembler) should look at.

File format, read off `Core/Debugger/CodeDataLogger.cpp`:

    "CDLv2" (5 bytes) + ROM CRC32 (4 bytes, little-endian) + one flag byte per
    ROM byte.

A file that does not start with `CDLv2` is an older CRC-less CDL; the Core loads
it as-is and so does this tool, with a warning and no CRC to compare. For the
NES the flag array is the **PRG** CDL followed by a **CHR ROM** CDL
(`NesCodeDataLogger::InternalSaveCdlFile`); a CHR RAM game has no CHR block, so
the split comes from the ROM's iNES header, never from the file's length. The
CRC32 in the header is taken over the PRG ROM only (`NesDebugger.cpp`).

Flags (`Core/Debugger/DebugTypes.h`, `Core/NES/Debugger/NesDebugger.h`):
Code 0x01, Data 0x02, JumpTarget 0x04, SubEntryPoint 0x08, PcmData 0x80. In the
CHR block, `Code` means the PPU fetched that byte, i.e. the tile was drawn.

Usage:
    scripts/cdl_tool.py report  <rom.nes> <file.cdl>
    scripts/cdl_tool.py union   <out.cdl> <in1.cdl> <in2.cdl> [...]
    scripts/cdl_tool.py regions <rom.nes> <file.cdl> [--min-size N] [--top N]
                                [--chr] [--json]
    scripts/cdl_tool.py strip   <rom.nes> <file.cdl> <out.bin>
                                --keep used|unused
"""

import argparse
import json
import sys
from pathlib import Path

MAGIC = b"CDLv2"
HEADER_SIZE = 9  # "CDLv2" + 4-byte CRC32, CodeDataLogger::HeaderSize

CODE = 0x01
DATA = 0x02
JUMP_TARGET = 0x04
SUB_ENTRY_POINT = 0x08
PCM_DATA = 0x80

PRG_BANK_SIZE = 16384
CHR_BANK_SIZE = 8192

# What the region table says, and what it deliberately does not say.
REGIONS_CAVEAT = (
    "Classification is evidence of access, not of meaning: a byte is `code` "
    "because the CPU executed it and `data` because something read it. "
    "Nothing here names anything - a large `data` run is a candidate asset "
    "table, and only a human reading the bytes can say what it holds."
)


class CdlError(Exception):
    """Anything that makes a ROM or a CDL file unusable for this tool."""


# --------------------------------------------------------------------------
# Inputs
# --------------------------------------------------------------------------

class Rom:
    """An iNES ROM, read only for its header - the PRG and CHR sizes are what
    split the CDL file. The bytes are kept because `strip` writes them back
    out; nothing here ever writes a ROM into the repository."""

    def __init__(self, path, data):
        self.path = Path(path)
        if len(data) < 16 or data[:4] != b"NES\x1a":
            raise CdlError(f"{path}: not an iNES file (no 'NES\\x1a' header)")
        self.prg_size = data[4] * PRG_BANK_SIZE
        self.chr_size = data[5] * CHR_BANK_SIZE
        if self.prg_size == 0:
            raise CdlError(f"{path}: iNES header declares no PRG ROM")
        self.mapper = (data[6] >> 4) | (data[7] & 0xF0)
        offset = 16 + (512 if data[6] & 0x04 else 0)
        self.prg = data[offset:offset + self.prg_size]
        self.chr = data[offset + self.prg_size:offset + self.prg_size + self.chr_size]
        if len(self.prg) < self.prg_size:
            raise CdlError(
                f"{path}: iNES header declares {self.prg_size} PRG bytes but the "
                f"file only holds {len(self.prg)}")
        # A CHR RAM game declares 0 CHR banks; its tiles live in PRG or in RAM,
        # so the CDL file carries no CHR block at all.
        self.has_chr_rom = self.chr_size > 0

    @classmethod
    def load(cls, path):
        return cls(path, Path(path).read_bytes())


class Cdl:
    """One parsed CDL file: the flag array plus the CRC32 its header claims."""

    def __init__(self, path, flags, crc32, has_header):
        self.path = str(path)
        self.flags = flags
        self.crc32 = crc32          # None for a header-less legacy file
        self.has_header = has_header

    def __len__(self):
        return len(self.flags)

    @classmethod
    def load(cls, path):
        data = Path(path).read_bytes()
        if not data:
            raise CdlError(f"{path}: empty file, not a CDL")
        if data[:5] == MAGIC:
            if len(data) < HEADER_SIZE:
                raise CdlError(
                    f"{path}: truncated CDLv2 header ({len(data)} bytes, "
                    f"{HEADER_SIZE} needed)")
            crc = int.from_bytes(data[5:9], "little")
            return cls(path, bytearray(data[HEADER_SIZE:]), crc, True)
        if len(data) < HEADER_SIZE:
            raise CdlError(f"{path}: too short to be a CDL file ({len(data)} bytes)")
        # The Core accepts an older CRC-less file rather than lose the data.
        print(f"[warning] {path}: no CDLv2 header, reading as a legacy CRC-less "
              f"CDL file", file=sys.stderr)
        return cls(path, bytearray(data), None, False)

    def save(self, path):
        crc = self.crc32 or 0
        with open(path, "wb") as handle:
            handle.write(MAGIC)
            handle.write(crc.to_bytes(4, "little"))
            handle.write(bytes(self.flags))


def split(rom, cdl):
    """Split a CDL's flag array into (PRG flags, CHR flags) using the ROM's
    header. The CHR block starts at `_memSize`, the PRG size."""
    if len(cdl) < rom.prg_size:
        raise CdlError(
            f"{cdl.path}: holds {len(cdl)} flag bytes, too few for the "
            f"{rom.prg_size}-byte PRG of {rom.path.name}")
    prg = cdl.flags[:rom.prg_size]
    chr_flags = bytearray()
    if rom.has_chr_rom:
        chr_flags = cdl.flags[rom.prg_size:rom.prg_size + rom.chr_size]
        if len(chr_flags) < rom.chr_size:
            raise CdlError(
                f"{cdl.path}: {rom.path.name} has {rom.chr_size} CHR ROM bytes "
                f"but the file only carries {len(chr_flags)} flag bytes past "
                f"the PRG block")
    extra = len(cdl) - rom.prg_size - (rom.chr_size if rom.has_chr_rom else 0)
    if extra > 0:
        print(f"[warning] {cdl.path}: {extra} trailing flag bytes past the "
              f"PRG{'+CHR' if rom.has_chr_rom else ''} block, ignored",
              file=sys.stderr)
    return prg, chr_flags


# --------------------------------------------------------------------------
# Counting
# --------------------------------------------------------------------------

def counts(flags):
    """Per-flag tallies over one block, in the shape `CdlStatistics` uses:
    `data` excludes the bytes that are also code, `both` holds those."""
    code = data = both = jump = sub = pcm = 0
    for value in flags:
        is_code = bool(value & CODE)
        is_data = bool(value & DATA)
        if is_code and is_data:
            both += 1
        elif is_code:
            code += 1
        elif is_data:
            data += 1
        if value & JUMP_TARGET:
            jump += 1
        if value & SUB_ENTRY_POINT:
            sub += 1
        if value & PCM_DATA:
            pcm += 1
    return {
        "total": len(flags),
        "code_only": code,
        "data_only": data,
        "both": both,
        "touched": code + data + both,
        "untouched": len(flags) - code - data - both,
        "jump_targets": jump,
        "sub_entry_points": sub,
        "pcm_bytes": pcm,
    }


def pct(part, whole):
    return 0.0 if not whole else 100.0 * part / whole


def bank_rows(flags, bank_size):
    """One row per bank: which banks a run never entered is the actionable
    number, so an untouched bank must be visible as such."""
    rows = []
    for index, start in enumerate(range(0, len(flags), bank_size)):
        block = flags[start:start + bank_size]
        stats = counts(block)
        stats["bank"] = index
        stats["start"] = start
        stats["end"] = start + len(block) - 1
        rows.append(stats)
    return rows


# --------------------------------------------------------------------------
# Regions
# --------------------------------------------------------------------------

def classify(value):
    if value & CODE and value & DATA:
        return "code+data"
    if value & CODE:
        return "code"
    if value & DATA:
        return "data"
    return "unused"


def regions(flags, bank_size, block="prg"):
    """Coalesce the flag array into contiguous runs of one classification.

    Only the class coalesces - a jump target inside a code run does not break
    it, it is counted. A run that spans banks stays one run (the mapper decides
    what a bank means at run time); the `bank` column names the bank it starts
    in and marks the span.
    """
    out = []
    if not flags:
        return out
    start = 0
    current = classify(flags[0])
    for index in range(1, len(flags) + 1):
        kind = classify(flags[index]) if index < len(flags) else None
        if kind != current:
            out.append(_region(flags, start, index - 1, current, bank_size, block))
            start = index
            current = kind
    return out


def _region(flags, start, end, kind, bank_size, block):
    window = flags[start:end + 1]
    first_bank = start // bank_size
    last_bank = end // bank_size
    return {
        "block": block,
        "start": start,
        "end": end,
        "size": end - start + 1,
        "class": kind,
        "bank": first_bank,
        "bank_span": last_bank - first_bank + 1,
        "jump_targets": sum(1 for v in window if v & JUMP_TARGET),
        "sub_entry_points": sum(1 for v in window if v & SUB_ENTRY_POINT),
        "pcm_bytes": sum(1 for v in window if v & PCM_DATA),
    }


def candidates(all_regions, min_size):
    """The payoff: `data` runs no code overlaps, biggest first. By construction
    a run classified `data` holds no executed byte at all."""
    picked = [r for r in all_regions if r["class"] == "data" and r["size"] >= min_size]
    picked.sort(key=lambda r: (-r["size"], r["start"]))
    return picked


# --------------------------------------------------------------------------
# Output
# --------------------------------------------------------------------------

def table(rows, columns):
    """A fixed-width table. `columns` is a list of (header, key, align)."""
    widths = []
    for header, key, _ in columns:
        widths.append(max(len(header), *(len(str(r[key])) for r in rows)) if rows
                      else len(header))
    lines = ["  ".join(h.ljust(w) if a == "<" else h.rjust(w)
                       for (h, _, a), w in zip(columns, widths)).rstrip()]
    lines.append("  ".join("-" * w for w in widths))
    for row in rows:
        lines.append("  ".join(
            str(row[k]).ljust(w) if a == "<" else str(row[k]).rjust(w)
            for (_, k, a), w in zip(columns, widths)).rstrip())
    return "\n".join(lines)


def hex_addr(value):
    return f"0x{value:06X}"


# --------------------------------------------------------------------------
# Subcommands
# --------------------------------------------------------------------------

def cmd_report(args):
    rom = Rom.load(args.rom)
    cdl = Cdl.load(args.cdl)
    prg, chr_flags = split(rom, cdl)
    prg_stats = counts(prg)

    print(f"ROM  {rom.path.name}  (mapper {rom.mapper}, "
          f"PRG {rom.prg_size // 1024}KB, "
          f"{'CHR ROM ' + str(rom.chr_size // 1024) + 'KB' if rom.has_chr_rom else 'CHR RAM'})")
    header_note = (f"CRC32 0x{cdl.crc32:08X}" if cdl.has_header
                   else "no header (legacy CRC-less file)")
    print(f"CDL  {Path(cdl.path).name}  ({header_note})")
    print()

    print("PRG coverage")
    rows = [
        {"what": "total", "bytes": prg_stats["total"], "pct": "100.0%"},
        {"what": "code", "bytes": prg_stats["code_only"],
         "pct": f"{pct(prg_stats['code_only'], prg_stats['total']):.1f}%"},
        {"what": "data", "bytes": prg_stats["data_only"],
         "pct": f"{pct(prg_stats['data_only'], prg_stats['total']):.1f}%"},
        {"what": "code+data", "bytes": prg_stats["both"],
         "pct": f"{pct(prg_stats['both'], prg_stats['total']):.1f}%"},
        {"what": "touched", "bytes": prg_stats["touched"],
         "pct": f"{pct(prg_stats['touched'], prg_stats['total']):.1f}%"},
        {"what": "untouched", "bytes": prg_stats["untouched"],
         "pct": f"{pct(prg_stats['untouched'], prg_stats['total']):.1f}%"},
    ]
    print(table(rows, [("class", "what", "<"), ("bytes", "bytes", ">"),
                       ("share", "pct", ">")]))
    print()
    print(f"jump targets     {prg_stats['jump_targets']}")
    print(f"sub entry points {prg_stats['sub_entry_points']}")
    print(f"PCM (DPCM) bytes {prg_stats['pcm_bytes']}")
    print()

    if rom.has_chr_rom:
        chr_stats = counts(chr_flags)
        drawn = chr_stats["code_only"] + chr_stats["both"]
        print(f"CHR ROM  {drawn} of {chr_stats['total']} bytes drawn "
              f"({pct(drawn, chr_stats['total']):.1f}%)")
        print()
    else:
        print("CHR RAM game: the CDL file carries no CHR block.")
        print()

    print(f"PRG banks ({PRG_BANK_SIZE // 1024}KB each)")
    rows = []
    for bank in bank_rows(prg, PRG_BANK_SIZE):
        rows.append({
            "bank": bank["bank"],
            "range": f"{hex_addr(bank['start'])}-{hex_addr(bank['end'])}",
            "code": bank["code_only"] + bank["both"],
            "data": bank["data_only"] + bank["both"],
            "touched": f"{pct(bank['touched'], bank['total']):.1f}%",
            "note": "never entered" if bank["touched"] == 0 else "",
        })
    print(table(rows, [("bank", "bank", ">"), ("range", "range", "<"),
                       ("code", "code", ">"), ("data", "data", ">"),
                       ("touched", "touched", ">"), ("", "note", "<")]))
    return 0


def cmd_union(args):
    loaded = [Cdl.load(p) for p in args.inputs]
    first = loaded[0]
    for other in loaded[1:]:
        if first.crc32 != other.crc32:
            raise CdlError(
                "refusing to merge CDL files from different ROMs: "
                f"{first.path} has CRC32 "
                f"{'0x%08X' % first.crc32 if first.crc32 is not None else 'none (no header)'}"
                f", {other.path} has CRC32 "
                f"{'0x%08X' % other.crc32 if other.crc32 is not None else 'none (no header)'}")
        if len(other) != len(first):
            raise CdlError(
                f"refusing to merge CDL files of different sizes: {first.path} "
                f"has {len(first)} flag bytes, {other.path} has {len(other)}")

    merged = bytearray(first.flags)
    for other in loaded[1:]:
        for index, value in enumerate(other.flags):
            merged[index] |= value

    out = Cdl(args.output, merged, first.crc32, True)
    out.save(args.output)
    before = counts(first.flags)["touched"]
    after = counts(merged)["touched"]
    print(f"merged {len(loaded)} CDL files into {args.output} "
          f"({len(merged)} flag bytes)")
    print(f"touched bytes: {before} in {Path(first.path).name} -> {after} in the union "
          f"(+{after - before})")
    return 0


def cmd_regions(args):
    rom = Rom.load(args.rom)
    cdl = Cdl.load(args.cdl)
    prg, chr_flags = split(rom, cdl)

    found = regions(prg, PRG_BANK_SIZE, "prg")
    if args.chr and rom.has_chr_rom:
        found += regions(chr_flags, CHR_BANK_SIZE, "chr")

    shown = [r for r in found if r["size"] >= args.min_size]
    top = candidates([r for r in found if r["block"] == "prg"], args.min_size)
    if args.top:
        top = top[:args.top]

    if args.json:
        print(json.dumps({
            "rom": rom.path.name,
            "caveat": REGIONS_CAVEAT,
            "regions": shown,
            "candidate_asset_tables": top,
        }, indent=2))
        return 0

    print(f"Regions of {rom.path.name} as seen by {Path(cdl.path).name}")
    print()
    for line in _wrap(REGIONS_CAVEAT, 78):
        print(line)
    print()

    print(f"Candidate asset tables - `data` runs no code overlaps, "
          f"biggest first (PRG, >= {args.min_size} bytes)")
    if top:
        print(table([_row(r) for r in top], _COLUMNS))
    else:
        print(f"  none: no data-only run reaches {args.min_size} bytes")
    print()

    print(f"All regions in address order (>= {args.min_size} bytes)")
    print(table([_row(r) for r in shown], _COLUMNS) if shown
          else f"  none reaches {args.min_size} bytes")
    return 0


_COLUMNS = [("block", "block", "<"), ("start", "start", "<"),
            ("end", "end", "<"), ("size", "size", ">"),
            ("class", "class", "<"), ("bank", "bank", "<"),
            ("jt", "jt", ">"), ("sub", "sub", ">"), ("pcm", "pcm", ">")]


def _row(region):
    bank = str(region["bank"])
    if region["bank_span"] > 1:
        bank = f"{region['bank']}+{region['bank_span'] - 1}"
    return {
        "block": region["block"],
        "start": hex_addr(region["start"]),
        "end": hex_addr(region["end"]),
        "size": region["size"],
        "class": region["class"],
        "bank": bank,
        "jt": region["jump_targets"],
        "sub": region["sub_entry_points"],
        "pcm": region["pcm_bytes"],
    }


def _wrap(text, width):
    lines, line = [], ""
    for word in text.split():
        if line and len(line) + 1 + len(word) > width:
            lines.append(line)
            line = word
        else:
            line = f"{line} {word}".strip()
    if line:
        lines.append(line)
    return lines


def cmd_strip(args):
    """Mirror `CodeDataLogger::StripData`: `--keep used` is the Core's
    `StripUnused` (zero every byte whose flags are 0), `--keep unused` is its
    `StripUsed` (zero every byte whose flags are not 0). Both keep the ROM's
    length - the output is PRG followed by CHR ROM, without the iNES header,
    exactly the buffer the Core strips."""
    rom = Rom.load(args.rom)
    cdl = Cdl.load(args.cdl)
    prg_flags, chr_flags = split(rom, cdl)

    keep_used = args.keep == "used"
    out = bytearray(rom.prg) + bytearray(rom.chr if rom.has_chr_rom else b"")
    all_flags = bytearray(prg_flags) + bytearray(chr_flags)
    zeroed = 0
    for index, flag in enumerate(all_flags):
        if index >= len(out):
            break
        if (flag == 0) if keep_used else (flag != 0):
            if out[index]:
                zeroed += 1
            out[index] = 0
    Path(args.output).write_bytes(bytes(out))
    kept = sum(1 for f in all_flags if (f != 0) == keep_used)
    print(f"wrote {args.output}: {len(out)} bytes, keeping the "
          f"{'touched' if keep_used else 'untouched'} ones "
          f"({kept} flagged bytes kept, {zeroed} non-zero bytes blanked)")
    return 0


# --------------------------------------------------------------------------
# CLI
# --------------------------------------------------------------------------

def build_parser():
    parser = argparse.ArgumentParser(
        prog="cdl_tool.py",
        description="Read Mesen Code/Data Logger files for NES ROMs.")
    sub = parser.add_subparsers(dest="command", required=True)

    report = sub.add_parser("report", help="coverage table for one CDL file")
    report.add_argument("rom")
    report.add_argument("cdl")
    report.set_defaults(func=cmd_report)

    union = sub.add_parser(
        "union", help="bitwise OR of several CDL files (coverage is a union)")
    union.add_argument("output")
    union.add_argument("inputs", nargs="+")
    union.set_defaults(func=cmd_union)

    regions_cmd = sub.add_parser(
        "regions", help="contiguous runs of one classification")
    regions_cmd.add_argument("rom")
    regions_cmd.add_argument("cdl")
    regions_cmd.add_argument("--min-size", type=int, default=16,
                             help="hide runs smaller than this (default 16)")
    regions_cmd.add_argument("--top", type=int, default=20,
                             help="how many candidate asset tables to list "
                                  "(0 for all, default 20)")
    regions_cmd.add_argument("--chr", action="store_true",
                             help="also list the CHR ROM block")
    regions_cmd.add_argument("--json", action="store_true")
    regions_cmd.set_defaults(func=cmd_regions)

    strip = sub.add_parser(
        "strip", help="write the ROM bytes with one half blanked out")
    strip.add_argument("rom")
    strip.add_argument("cdl")
    strip.add_argument("output")
    strip.add_argument("--keep", choices=("used", "unused"), required=True)
    strip.set_defaults(func=cmd_strip)

    return parser


def main(argv=None):
    args = build_parser().parse_args(argv)
    try:
        return args.func(args)
    except CdlError as error:
        print(f"error: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
