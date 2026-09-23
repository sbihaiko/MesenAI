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

# `<patch>file,sha1`, read the way `HdPackLoader` reads it: the text after the
# tag is split on **every** comma and each token trimmed (`StringUtilities::
# Split(lineContent.substr(7), ',')` + `TrimTokens`), then `ProcessPatchTag`
# requires at least two tokens, takes tokens[0] as the file and tokens[1] as
# the sha1, and requires tokens[1] to be exactly 40 characters. A file name
# with a comma therefore never registers at runtime: tokens[1] is the tail of
# the name, not the hash (and from `<ver>109` on, three or more tokens fail
# the line outright — `checkConstraintEx`). There is no older-`<ver>` form
# without a sha1: `tokens.size() >= 2` is checked unconditionally.
SHA1_RE = re.compile(r"^[0-9A-Fa-f]{40}$")
_CONDITIONED_PATCH_RE = re.compile(r"^\[[^\]]*\]<patch>")
# A Windows drive (`C:` or `C:\`) or a UNC prefix — absolute on the platform
# the pack may have been written on, so refused everywhere (ADR-0006).
_WIN_ABSOLUTE_RE = re.compile(r"^(?:[A-Za-z]:|\\\\|//)")


class PatchError(Exception):
    """Fatal, with a message a human can act on."""


def safe_relative(name: str, line: int) -> str:
    """The pack-relative path a `<patch>` file name denotes, in the form this
    tool uses everywhere (`/`-separated), or a refusal.

    Backslashes are separators: Windows-authored packs write `sub\\fix.ips`,
    and the loader agrees on every platform — `HdPackLoader::LoadPack`
    replaces every `\\` with `/` on each manifest line before any tag is
    parsed (commit 9615330b, 2026-08-27), so `sub\\fix.ips` and `sub/fix.ips`
    are the same token at runtime. The `/` form is what this tool uses as its
    canonical spelling (`rel`) because every repo tool can `exists()` it
    without re-implementing the loader's rewrite, and because `mep_build`
    already normalizes `<img>`/`<background>`/audio names the same way; it
    is a portability choice for tooling, not a runtime necessity. What is
    refused, before any
    byte is read or written (ADR-0006, the MEI trust model's zip-traversal
    rule, applied to a folder pack): an absolute path (POSIX, drive letter or
    UNC), a `..` component anywhere, and an empty name. The loader itself
    would happily `CombinePath` a `../x.ips` — which is exactly why the tool,
    which *writes* under `--out`, must not."""
    rel = name.strip().replace("\\", "/")
    if not rel:
        raise PatchError(f"line {line}: <patch> names no file")
    if rel.startswith("/") or _WIN_ABSOLUTE_RE.match(rel):
        raise PatchError(f"line {line}: <patch> file {name!r} is an absolute path; a patch must "
                         "live inside the pack folder (ADR-0006)")
    parts = [p for p in rel.split("/") if p not in ("", ".")]
    if ".." in parts:
        raise PatchError(f"line {line}: <patch> file {name!r} escapes the pack folder ('..' "
                         "component); a patch must live inside the pack folder (ADR-0006)")
    if not parts:
        raise PatchError(f"line {line}: <patch> names no file")
    return "/".join(parts)


def contained(path: Path, root: Path) -> bool:
    """True when `path`, symlinks followed, stays beneath `root`. Both are
    resolved non-strictly so a destination that does not exist yet counts."""
    try:
        path.resolve().relative_to(root.resolve())
    except ValueError:
        return False
    return True


def loader_source(folder: Path, rel: str) -> str | None:
    """The pack file `HdPackLoader::ResolvePackRelativePath` opens for `rel`:
    the exact path when it exists, otherwise the one file whose
    `/`-separated, lowercased pack-relative name equals `rel` lowercased
    (`IndexPackFiles` + `_packFilesByLower`). A Windows-authored pack whose
    manifest says `Fix.ips` beside a `fix.ips` loads on every host, so the
    import must find it too. When two files fold to the same name the loader's
    map keeps whichever the folder walk yields last; that walk order is not
    specified, so an ambiguous fold is `None` here rather than a guess.
    `None` also when nothing matches."""
    if (folder / rel).is_file():
        return rel
    want = rel.lower()
    hits = [f.relative_to(folder).as_posix() for f in folder.rglob("*")
            if f.is_file() and f.relative_to(folder).as_posix().lower() == want]
    return hits[0] if len(hits) == 1 else None


class PatchLine:
    """One `<patch>` line: the file it names (verbatim, and normalized as
    `rel`), the ROM sha1 it was made for, and the manifest line it came from."""

    __slots__ = ("file", "rel", "sha1", "line")

    def __init__(self, file: str, sha1: str, line: int):
        self.file, self.sha1, self.line = file, sha1.upper(), line
        self.rel = safe_relative(file, line)


def emitted_line(entry: PatchLine) -> str:
    """The `<patch>` line a project carries: the **normalized** relative path
    (`rel`, `/`-separated — the path the IPS is copied to) and the sha1 as the
    loader keys it (uppercase). The loader would accept the source's `\\`
    token just as well (it rewrites `\\` to `/` before parsing, see
    `safe_relative`); the `/` form is the canonical one so that every repo
    tool can resolve the path without re-implementing that rewrite, the way
    `mep_build` normalizes `<background>`. `verify` compares the built
    manifest's token to `rel` after the same `\\`→`/` rewrite, so either
    spelling of the same path passes and only a genuinely different path
    fails."""
    return f"<patch>{entry.rel},{entry.sha1}"


class PatchPlan:
    """What `resolve` found out: the stock ROM's two hashes, the `<patch>`
    line that names it, and the patched ROM's two hashes."""

    __slots__ = ("rom", "entries", "matched", "matched_by", "stock_whole", "stock_no_intro",
                 "patched_whole", "patched_no_intro", "stock_size", "patched_size",
                 "stock_chr_units", "patched_chr_units", "records", "ips_files", "sources")

    def __init__(self, **kw):
        for k in self.__slots__:
            setattr(self, k, kw.get(k))

    @property
    def adds_chr_rom(self) -> bool:
        """True when the IPS turns a CHR RAM cartridge into CHR ROM — the case
        whose `<tile>` keys (bank indices) the stock ROM can never produce."""
        return self.stock_chr_units == 0 and self.patched_chr_units > 0


def patch_lines(lines: list[str]) -> list[PatchLine]:
    """Every `<patch>` line of a manifest, in file order, 1-based line numbers,
    tokenized exactly as `HdPackLoader::ProcessPatchTag` tokenizes it (see
    `SHA1_RE`). A line the loader would not register is refused with its line
    cited, rather than imported into a project whose IPS the runtime ignores:
    fewer than two tokens ("Patch tag requires more parameters"), more than
    two (a comma in the file name — tokens[1] is then not the hash, and from
    `<ver>109` the loader fails the line on the count alone), or a second
    token that is not a 40-hex sha1. Path rules: `safe_relative`."""
    out = []
    for i, raw in enumerate(lines):
        s = raw.strip()
        line = raw.rstrip("\r\n")
        # `HdPackLoader::LoadPack` dispatches on the raw line: `<patch>` must
        # start at column 0, or right after a `[condition]` prefix it strips.
        # An indented `<patch>` is ignored at runtime and a conditioned one is
        # applied unconditionally; carrying either would change which ROM the
        # project runs (PR #385 review), so both are refused, line cited.
        if _CONDITIONED_PATCH_RE.match(line):
            raise PatchError(f"line {i + 1}: <patch> behind a [condition] prefix: {s[:60]!r}. "
                             "HdPackLoader strips the condition and applies the patch "
                             "unconditionally; drop the prefix in the source pack")
        if not s.startswith("<patch>"):
            continue
        if not line.startswith("<patch>"):
            raise PatchError(f"line {i + 1}: indented <patch> line: {s[:60]!r}. HdPackLoader "
                             "only reads a tag at column 0, so the source pack never applies "
                             "this patch; remove the indentation (to apply it) or the line")
        tokens = [t.strip() for t in s[len("<patch>"):].split(",")]
        if len(tokens) < 2:
            raise PatchError(f"line {i + 1}: <patch> needs file,sha1 (40 hex) — the loader "
                             f"never registers a patch line with one field: {s[:60]!r}")
        if len(tokens) > 2:
            raise PatchError(
                f"line {i + 1}: <patch> has {len(tokens)} comma-separated fields, not 2: "
                f"{s[:60]!r}. HdPackLoader splits the line on every comma and reads the second "
                "field as the sha1, so a file name with a comma is never registered at runtime "
                "(and <ver>109+ fails the line outright). Rename the patch file")
        if not SHA1_RE.match(tokens[1]):
            raise PatchError(f"line {i + 1}: <patch> needs file,sha1 (40 hex): {s[:60]!r}")
        out.append(PatchLine(tokens[0], tokens[1], i + 1))
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
      fails the whole pack on a missing patch, so every line's file must exist,
      found the way the loader finds it (`loader_source`: exact, then
      case-folded); `plan.sources` maps each normalized name to the file read;
    - a `<patch>` file that resolves (symlinks followed) outside the pack
      folder, on top of the `safe_relative` rules already applied when the
      lines were parsed (ADR-0006);
    - no line whose sha1 is the ROM's whole-file or No-Intro sha1 — the IPS was
      made for another dump, and IPS does not relax (ADR-0145 (3)); when
      several lines name the same sha1 the **last** one wins, as at runtime
      (`HdPackLoader::ProcessPatchTag` assigns `PatchesByHash[sha1]` per line,
      so each later line overwrites the earlier);
    - a file that is not an IPS, or an IPS the format does not allow;
    - a `<supportedRom>` the pack declares that is none of: the ROM's two
      hashes, the patched ROM's two hashes, or one of the pack's own `<patch>`
      targets — the pack says it is for another ROM (ADR-0211 rules 4–6).
    """
    entries = patch_lines(lines)
    if not entries:
        raise PatchError("no <patch> line — nothing to resolve")
    sources = {}
    for e in entries:
        if not contained(folder / e.rel, folder):
            raise PatchError(f"line {e.line}: <patch> file {e.file!r} resolves outside the pack "
                             f"folder {folder} — a patch must live inside the pack (ADR-0006)")
        src = loader_source(folder, e.rel)
        if src is None:
            raise PatchError(f"line {e.line}: <patch> file {e.file!r} is not in {folder} (nor "
                             "under any case-folded spelling) — HdPackLoader fails the whole "
                             "pack on a missing patch, so the import refuses rather than write a "
                             "project that cannot load")
        if not contained(folder / src, folder):
            raise PatchError(f"line {e.line}: <patch> file {e.file!r} resolves outside the pack "
                             f"folder {folder} — a patch must live inside the pack (ADR-0006)")
        sources[e.rel] = src
    # Every carried IPS, not only the one `--rom` selects, must be a valid IPS
    # (PR #385 review): each is copied into the project and applied by the
    # runtime to whichever ROM its sha1 names, so a malformed one would give a
    # wrong ROM there. Parsed with the same reader, over an empty buffer.
    for e in entries:
        try:
            apply_ips(b"", (folder / sources[e.rel]).read_bytes())
        except PatchError as exc:
            raise PatchError(f"line {e.line}: <patch> file {e.file!r}: {exc}") from exc
    try:
        stock = rom.read_bytes()
    except OSError as exc:
        raise PatchError(f"{rom}: cannot be read ({exc})") from exc
    suffix = rom.suffix.lower()
    stock_whole = whole_file_sha1(stock)
    stock_no_intro = no_intro_sha1(stock, suffix)
    matched, matched_by = None, None
    for form, digest in (("whole-file", stock_whole), ("No-Intro", stock_no_intro)):
        # Last entry wins for a repeated sha1: `HdPackLoader::ProcessPatchTag`
        # does `_data->PatchesByHash[tokens[1]] = ...` once per line, in file
        # order, so the runtime keeps the last assignment.
        matched = next((e for e in reversed(entries) if e.sha1 == digest), None)
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
    ips_path = folder / sources[matched.rel]
    patched, records = apply_ips(stock, ips_path.read_bytes())
    plan = PatchPlan(
        rom=rom, entries=entries, matched=matched, matched_by=matched_by,
        stock_whole=stock_whole, stock_no_intro=stock_no_intro,
        patched_whole=whole_file_sha1(patched), patched_no_intro=no_intro_sha1(patched, suffix),
        stock_size=len(stock), patched_size=len(patched),
        stock_chr_units=chr_units(stock), patched_chr_units=chr_units(patched),
        records=records,
        ips_files=sorted({e.rel for e in entries}), sources=sources)
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
