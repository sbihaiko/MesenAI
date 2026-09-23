#!/usr/bin/env python3
"""The patched-ROM half of a legacy pack import (ADR-0198 §3, option (a)).

A legacy pack that ships a `<patch>` keys its `<tile>` lines against the ROM
**after** the patch: for seven of the fifteen accepted packs the IPS turns CHR
RAM into CHR ROM, and the keys are bank indices that only exist once the patch
has run. ADR-0198 §3 decided that such a pack is imported *against the patched
ROM*: the project's `<supportedRom>` is the patched ROM's hash, the IPS is
carried as a project asset, and the fork keeps matching it exactly (ADR-0145
(3)) — no Core change, no runtime dual-namespace lookup.

This module is the part of that decision that has to read bytes: which
`<patch>` line names the ROM at hand, what the ROM becomes once the IPS is
applied, and the two hashes of each. It mirrors the emulator on every point it
touches, so that a project written here behaves the way the pack it came from
already does:

- **Which line applies.** `NesConsole` keys `<patch>` lines by the sha1 of the
  ROM they were made for and looks the loaded ROM up by its whole-file sha1
  first, then by its No-Intro sha1 (ADR-0044). The same two lookups, in the
  same order, decide here. No entry for either hash is a refusal — an IPS has
  no checksum of its own, so applying it to another dump is silently-accepted
  garbage (ADR-0145 (3)).
- **How the IPS is applied.** `IpsPatcher::PatchBuffer`, record for record:
  `PATCH` magic, 3-byte offset / 2-byte length records, RLE when the length is
  0, records applied in stream order (not sorted), the output grown to the
  furthest write, and the optional 3-byte truncate offset after `EOF`.
- **Which hash is which.** `<supportedRom>` is the whole-file sha1 —
  `HdPackBuilder` writes `RomFile.GetSha1Hash()` of the running, patched ROM —
  while `pack.json`'s `targets[]` carry the No-Intro body hash (ADR-0003,
  ADR-0039). The two are never compared across forms (ADR-0211).

Stdlib only (ADR-0165). Nothing here writes a file: `resolve` returns a plan,
and `mep_import.py` decides what to do with it.
"""
from __future__ import annotations

import hashlib
import re
from pathlib import Path

# `<patch>file,sha1` — the file name may itself contain a comma (real packs
# do), so the sha1 is the *last* token and the name is everything before it.
# The same read mep_lint makes.
PATCH_LINE_RE = re.compile(r"^<patch>(.*),\s*([0-9A-Fa-f]{40})\s*$")


class PatchError(Exception):
    """Fatal, with a message a human can act on."""


class PatchLine:
    """One `<patch>` line: the file it names, the ROM sha1 it was made for,
    and the manifest line it came from."""

    __slots__ = ("file", "sha1", "line")

    def __init__(self, file: str, sha1: str, line: int):
        self.file, self.sha1, self.line = file, sha1.upper(), line


class PatchPlan:
    """What `resolve` found out: the stock ROM's two hashes, the `<patch>`
    line that names it, and the patched ROM's two hashes."""

    __slots__ = ("rom", "entries", "matched", "matched_by", "stock_whole", "stock_no_intro",
                 "patched_whole", "patched_no_intro", "stock_size", "patched_size",
                 "stock_chr_units", "patched_chr_units", "records", "ips_files")

    def __init__(self, **kw):
        for k in self.__slots__:
            setattr(self, k, kw.get(k))

    @property
    def adds_chr_rom(self) -> bool:
        """True when the IPS turns a CHR RAM cartridge into CHR ROM — the case
        whose `<tile>` keys (bank indices) the stock ROM can never produce."""
        return self.stock_chr_units == 0 and self.patched_chr_units > 0


def patch_lines(lines: list[str]) -> list[PatchLine]:
    """Every `<patch>` line of a manifest, in file order, 1-based line numbers.
    A `<patch>` whose fields do not parse is refused with its line cited: the
    loader's `checkConstraint` fails the whole pack on it."""
    out = []
    for i, raw in enumerate(lines):
        s = raw.strip()
        if not s.startswith("<patch>"):
            continue
        m = PATCH_LINE_RE.match(s)
        if not m:
            raise PatchError(f"line {i + 1}: <patch> needs file,sha1 (40 hex): {s[:60]!r}")
        out.append(PatchLine(m.group(1).strip(), m.group(2), i + 1))
    return out


def no_intro_sha1(data: bytes, suffix: str) -> str:
    """MEP-v1 §4 over bytes already in memory — the twin of
    `mep_build._no_intro_sha1(path)`, which this module cannot call because the
    patched ROM never touches the disk. iNES: skip the 16-byte header and a
    512-byte trainer, clamp to the PRG+CHR size the header declares (ADR-0044);
    SNES copier header; everything else whole. 40 uppercase hex digits."""
    end = len(data)
    offset = 0
    if suffix == ".nes" and data[:4] == b"NES\x1a" and len(data) >= 16:
        offset = 16 + (512 if data[6] & 0x04 else 0)
        prg_units = data[4]
        chr_units = data[5]
        if (data[7] & 0x0C) == 0x08 and (data[9] & 0x0F) != 0x0F and (data[9] >> 4) != 0x0F:
            prg_units |= (data[9] & 0x0F) << 8
            chr_units |= (data[9] >> 4) << 8
        declared = offset + prg_units * 0x4000 + chr_units * 0x2000
        if offset < declared < end:
            end = declared
    elif suffix in {".sfc", ".smc", ".swc", ".fig", ".bs", ".st"} and len(data) % 1024 == 512:
        offset = 512
    return hashlib.sha1(data[offset:end]).hexdigest().upper()  # noqa: S324 - No-Intro identity hash is SHA-1 by contract (ADR-0003/ADR-0039)


def whole_file_sha1(data: bytes) -> str:
    """`VirtualFile::GetSha1Hash` — the file as it is, header included."""
    return hashlib.sha1(data).hexdigest().upper()  # noqa: S324 - the loader's own <patch>/<supportedRom> key


def chr_units(data: bytes) -> int:
    """iNES header byte 5 (plus the NES 2.0 MSBs): 8 KiB CHR ROM units. 0 is a
    CHR RAM cartridge, whose `<tile>` keys are 16 pattern bytes."""
    if len(data) < 16 or data[:4] != b"NES\x1a":
        return 0
    units = data[5]
    if (data[7] & 0x0C) == 0x08 and (data[9] >> 4) != 0x0F:
        units |= (data[9] >> 4) << 8
    return units


def apply_ips(rom: bytes, ips: bytes) -> tuple[bytes, int]:
    """`IpsPatcher::PatchBuffer`, in Python: (patched bytes, record count).

    Records are applied in stream order — the emulator does not sort them, so
    a later record overwriting an earlier one is the author's intent and is
    kept. The output grows to the furthest write (an IPS that appends CHR ROM
    to a CHR RAM cartridge does exactly this), and a 3-byte offset after
    `EOF`, when present, truncates it. Anything the format does not allow is a
    refusal with the byte offset cited: the loader would have produced a
    silently wrong ROM from it."""
    if ips[:5] != b"PATCH":
        raise PatchError("not an IPS file (no 'PATCH' magic); ADR-0198 §3 admits an IPS, and a "
                         "BPS/UPS patch is not handled by this import")
    records = []
    pos = 5
    truncate = None
    size = len(rom)
    while True:
        if pos + 3 > len(ips):
            raise PatchError(f"IPS truncated at byte {pos}: no EOF marker")
        if ips[pos:pos + 3] == b"EOF":
            if len(ips) >= pos + 6:
                truncate = int.from_bytes(ips[pos + 3:pos + 6], "big")
            break
        address = int.from_bytes(ips[pos:pos + 3], "big")
        pos += 3
        if pos + 2 > len(ips):
            raise PatchError(f"IPS truncated at byte {pos}: record has no length")
        length = int.from_bytes(ips[pos:pos + 2], "big")
        pos += 2
        if length == 0:
            if pos + 3 > len(ips):
                raise PatchError(f"IPS truncated at byte {pos}: RLE record has no run")
            run = int.from_bytes(ips[pos:pos + 2], "big")
            data = bytes((ips[pos + 2],)) * run
            pos += 3
        else:
            if pos + length > len(ips):
                raise PatchError(f"IPS truncated at byte {pos}: record needs {length} bytes")
            data = ips[pos:pos + length]
            pos += length
        records.append((address, data))
        size = max(size, address + len(data))
    out = bytearray(size)
    out[:len(rom)] = rom
    for address, data in records:
        out[address:address + len(data)] = data
    if truncate is not None and len(out) > truncate:
        del out[truncate:]
    return bytes(out), len(records)


def resolve(lines: list[str], folder: Path, rom: Path,
            declared_supported_rom: str | None = None) -> PatchPlan:
    """Which `<patch>` line names `rom`, and what `rom` becomes.

    Refusals, each naming what it refused:

    - no `<patch>` line at all (the caller should not be here);
    - a `<patch>` file that is not in the pack — the loader's `checkConstraint`
      fails the whole pack on a missing patch, so every line's file must exist;
    - no line whose sha1 is the ROM's whole-file or No-Intro sha1 — the IPS was
      made for another dump, and IPS does not relax (ADR-0145 (3));
    - a file that is not an IPS, or an IPS the format does not allow;
    - a `<supportedRom>` the pack declares that is none of: the ROM's two
      hashes, the patched ROM's two hashes, or one of the pack's own `<patch>`
      targets — the pack says it is for another ROM (ADR-0211 rules 4–6).
    """
    entries = patch_lines(lines)
    if not entries:
        raise PatchError("no <patch> line — nothing to resolve")
    for e in entries:
        if not (folder / e.file.replace("\\", "/")).is_file():
            raise PatchError(f"line {e.line}: <patch> file {e.file!r} is not in {folder} — "
                             "HdPackLoader fails the whole pack on a missing patch, so the "
                             "import refuses rather than write a project that cannot load")
    try:
        stock = rom.read_bytes()
    except OSError as exc:
        raise PatchError(f"{rom}: cannot be read ({exc})") from exc
    suffix = rom.suffix.lower()
    stock_whole = whole_file_sha1(stock)
    stock_no_intro = no_intro_sha1(stock, suffix)
    matched, matched_by = None, None
    for form, digest in (("whole-file", stock_whole), ("No-Intro", stock_no_intro)):
        matched = next((e for e in entries if e.sha1 == digest), None)
        if matched:
            matched_by = form
            break
    if matched is None:
        declared = ", ".join(sorted({e.sha1 for e in entries}))
        raise PatchError(
            f"none of the pack's <patch> line(s) names this ROM: {rom.name} is "
            f"{stock_whole} (whole-file) / {stock_no_intro} (No-Intro), the pack's {len(entries)} "
            f"<patch> line(s) name {declared}. The IPS was made for another dump, and an IPS "
            "carries no checksum of its own, so applying it here would be silently wrong — "
            "ADR-0145 (3): IPS does not relax. Pass the dump the pack was made for")
    ips_path = folder / matched.file.replace("\\", "/")
    patched, records = apply_ips(stock, ips_path.read_bytes())
    plan = PatchPlan(
        rom=rom, entries=entries, matched=matched, matched_by=matched_by,
        stock_whole=stock_whole, stock_no_intro=stock_no_intro,
        patched_whole=whole_file_sha1(patched), patched_no_intro=no_intro_sha1(patched, suffix),
        stock_size=len(stock), patched_size=len(patched),
        stock_chr_units=chr_units(stock), patched_chr_units=chr_units(patched),
        records=records,
        ips_files=sorted({e.file.replace("\\", "/") for e in entries}))
    if declared_supported_rom:
        want = declared_supported_rom.strip().upper()
        allowed = {stock_whole, stock_no_intro, plan.patched_whole, plan.patched_no_intro}
        allowed |= {e.sha1 for e in entries}
        if want not in allowed:
            raise PatchError(
                f"the pack declares <supportedRom>{want}, which is neither {rom.name} "
                f"({stock_whole} / {stock_no_intro}), nor the ROM its IPS produces "
                f"({plan.patched_whole} / {plan.patched_no_intro}), nor one of its own <patch> "
                "targets — the pack says it is for another ROM (ADR-0211 rules 4–6), so the "
                "import refuses rather than re-key it")
    return plan


def namespace_note(plan: PatchPlan, index_keyed: bool) -> list[str]:
    """What a patched-ROM import does **not** buy, in the words ADR-0198 §3
    asks the tool to print on every such import."""
    lines = [
        f"This project lives in the patched ROM's key namespace: <supportedRom> is "
        f"{plan.patched_whole}, the ROM {plan.matched.file} produces from {plan.rom.name}, "
        f"not {plan.stock_whole}, the stock dump."]
    if plan.adds_chr_rom and index_keyed:
        lines.append(
            f"The IPS turns CHR RAM into {plan.patched_chr_units * 8} KiB of CHR ROM, and the "
            "pack's <tile> keys are bank indices of that CHR ROM. A recording this fork makes on "
            "the stock ROM writes 16-byte pattern keys, so the two namespaces never meet: the "
            "recording -> artist kit -> sheet loop (ADR-0183) does not connect to this project. "
            "Its author can paint and lint here, not record (ADR-0198 §3).")
    elif index_keyed:
        lines.append(
            "The pack keys <tile> by CHR index. A recording made on the stock ROM keys the same "
            "way only if the stock ROM is CHR ROM too; a recording made on any other dump lands "
            "in another namespace and does not reach this project (ADR-0198 §3).")
    else:
        lines.append(
            "The pack keys <tile> by 16 pattern bytes, which the stock ROM also produces — but a "
            "recording made on the stock ROM is keyed to the stock ROM's hash, not to this "
            "project's <supportedRom>, so it does not land here without re-keying (ADR-0198 §3).")
    return lines
