#!/usr/bin/env python3
"""generate_no_intro_sha1_table.py — builds scripts/no_intro_sha1.tsv.gz, the
versioned `SHA1 -> console + No-Intro name` table the library looks a ROM up in
(#1038, parent spec #1030).

SOURCE. The No-Intro DATs ("dat" = the console's ROM database) as mirrored by
the libretro-database repository at `metadat/no-intro/`, fetched over HTTPS
from raw.githubusercontent.com (the host is already on the pack allow-list,
ADR-0138 §41). One DAT per console; the console order below is also the order
that decides which console owns a sha1 two DATs both list.

LICENCE. libretro-database carries CC BY-SA 4.0 (`LICENSE`,
https://github.com/libretro/libretro-database/blob/master/LICENSE,
"Attribution-ShareAlike 4.0 International"; read 2026-10-07). The DATs inside it
declare no licence field of their own, and No-Intro states none for them, so the
repository's licence is the one that governs the bytes read here -- the earlier
claim that No-Intro publishes them "for free redistribution" had no source and
is withdrawn. The table this script writes is Adapted Material under that
licence, so it is offered under CC BY-SA 4.0 in turn: its own `#source` and
`#licence` lines carry the attribution, the licence name and its URI, so a copy
that leaves this repository still carries them, and `no_intro_sha1.NOTICE.md`
sits beside it as the human-readable notice. It holds names and hashes only --
no ROM bytes, no artwork, no publisher asset, is redistributed. See ADR-0266.

HASH CONTRACT. Every key is the SHA-1 of the ROM *payload*, not of the file
(ADR-0003, ADR-0039, MEP-v1 §4): for a `.nes` file with the `NES\\x1A` magic,
the 16-byte iNES header and any 512-byte trainer are skipped and the range is
clamped to the header-declared PRG+CHR size; every other supported console
hashes the whole file. No-Intro hashes the same range, which is exactly why the
table can be keyed on it. A ROM with a header or trailing garbage is therefore
hashed with the same rule at lookup time (the library slice, #1032/#1030) or it
will not match -- hashing the raw file silently misses every headered NES dump.

The NES DAT lists each dump TWICE: a `.nes` rom hashed over the whole file, iNES
header included, and its `.unh` twin hashed over the payload. Only the `.unh`
row is a key this app can ever produce, so `select_payload_roms` drops the
headered row and the table stays keyed by payload SHA1 for every console -- a
`.nes` row would be a dead key sitting beside the live one.

TABLE FORMAT (v1), gzipped TSV, one line per ROM, sorted by sha1:
  `#mesen-no-intro-sha1-table<TAB>1`   format version; the C# reader refuses any other
  `#source<TAB>...` / `#licence<TAB>...` / `#hash<TAB>...`
  `#console<TAB><code><TAB><DAT name><TAB><DAT version><TAB><dat sha256>`
  `<sha1><TAB><console code><TAB><No-Intro game name>`
Keys are 40 lowercase hex digits (comparison is case-insensitive on the reading
side); console codes are `nes`, `gb`, `gbc`, `gba`, `sms`, `sg1000`, `gg` -- the
same set as the app's RomConsole. Every key is a payload hash on every console,
so no line has to be interpreted differently per console. `gzip.compress(...,
mtime=0)` keeps the file byte-identical across runs, so a regeneration that
changes nothing is not a diff.

Usage: python3 scripts/generate_no_intro_sha1_table.py [--check]
  --check  do not write; exit 1 if the committed table differs from a fresh build
"""
from __future__ import annotations

import argparse
import dataclasses
import gzip
import hashlib
import re
import sys
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

FORMAT_VERSION = 1
TABLE_FILE_NAME = "no_intro_sha1.tsv.gz"
TABLE_PATH = Path(__file__).resolve().parent / TABLE_FILE_NAME

DAT_BASE_URL = ("https://raw.githubusercontent.com/libretro/libretro-database"
                "/master/metadat/no-intro")

#(console code, DAT display name) -- the DAT names are the repository's own file
#names, so a rename there is a loud 404 here rather than a silently empty table.
CONSOLES: tuple[tuple[str, str], ...] = (
    ("nes", "Nintendo - Nintendo Entertainment System"),
    ("gb", "Nintendo - Game Boy"),
    ("gbc", "Nintendo - Game Boy Color"),
    ("gba", "Nintendo - Game Boy Advance"),
    ("sms", "Sega - Master System - Mark III"),
    ("sg1000", "Sega - SG-1000"),
    ("gg", "Sega - Game Gear"),
)
CONSOLE_CODES: tuple[str, ...] = tuple(code for code, _ in CONSOLES)

_SHA1 = re.compile(r"\bsha1\s+([0-9a-fA-F]{40})\b")
_GAME_NAME = re.compile(r'^\s*name\s+"([^"]*)"', re.M)
_ROM_BLOCK = re.compile(r"^\s*rom\s*\(", re.M)
_DAT_VERSION = re.compile(r'^\s*version\s+"([^"]*)"', re.M)
_GAME_BLOCK = re.compile(r"^game\s*\(", re.M)
_XML_SHAPE = re.compile(r"<(?:datafile|game)\b")

#Rom file names whose bytes the app never hashes whole. No-Intro's NES DAT
#carries each dump as a headered `.nes` rom and a headerless `.unh` rom, of the
#same game and with different hashes; ADR-0003/ADR-0039 hash the `.unh` bytes.
HEADERED_ROM_SUFFIXES: frozenset[str] = frozenset({".nes"})


@dataclasses.dataclass(frozen=True)
class DatSource:
    """One console's DAT: its identity, the provenance of the bytes it came
    from, and the (sha1, game name) pairs it declares."""
    code: str
    dat_name: str
    version: str
    sha256: str
    entries: tuple[tuple[str, str], ...]


def dat_url(dat_name: str) -> str:
    return f"{DAT_BASE_URL}/{urllib.parse.quote(dat_name)}.dat"


def parse_dat(text: str) -> tuple[str, tuple[tuple[str, str, str], ...]]:
    """-> (DAT version, ((sha1, game name, rom name), ...)) in file order.

    The game name is the DAT's `<game> name`, never the rom's file name: the
    player is shown a game, and the rom name carries dump-level noise
    ("(Unl)", "[b]") the game name does not. The rom name is kept anyway,
    because it is the only thing that says whether the hashed bytes include an
    iNES header -- `select_payload_roms` reads it. It is None for a rom entry
    that declares none (`<rom>` names are optional in the format)."""
    if _XML_SHAPE.search(text):
        raise ValueError(
            "this DAT is in the XML shape (<datafile>/<game>), not the "
            "clrmamepro shape this parser reads - update parse_dat")
    version_match = _DAT_VERSION.search(text)
    if version_match is None:
        raise ValueError("no clrmamepro `version \"...\"` line in this DAT")

    entries: list[tuple[str, str, str]] = []
    for block in _GAME_BLOCK.split(text)[1:]:
        name_match = _GAME_NAME.search(block)
        if name_match is None:
            continue
        name = name_match.group(1)
        if "\t" in name or "\n" in name or "\r" in name:
            raise ValueError(f"game name holds a tab or a newline: {name!r}")
        for rom in _ROM_BLOCK.split(block)[1:]:
            #A rom declares its own `name` in the same shape a game does.
            rom_match = _GAME_NAME.search(rom)
            rom_name = rom_match.group(1) if rom_match is not None else None
            for sha1 in _SHA1.findall(rom):
                #A rom that declares no sha1 is skipped, never completed with a
                #guess: a wrong key would hand the player another game's title.
                entries.append((sha1.upper(), name, rom_name))
    return version_match.group(1), tuple(entries)


def select_payload_roms(
        entries: tuple[tuple[str, str, str], ...]) -> tuple[tuple[str, str], ...]:
    """-> the (sha1, game name) rows whose hash the app can reproduce.

    Drops a rom whose file name marks it as a headered dump
    (`HEADERED_ROM_SUFFIXES`), because the app hashes the payload
    (ADR-0003, ADR-0039) and a headered row is a key no lookup ever produces.
    A rom whose name the DAT does not declare is kept: dropping what cannot be
    classified would lose a game the table could still match."""
    return tuple((sha1, name) for sha1, name, rom_name in entries
                 if rom_name is None
                 or Path(rom_name).suffix.lower() not in HEADERED_ROM_SUFFIXES)


def dat_source(code: str, dat_name: str, raw: bytes) -> DatSource:
    """Parse one fetched DAT into the record the table is built from, keeping
    only the payload rows."""
    version, entries = parse_dat(raw.decode("utf-8", errors="replace"))
    return DatSource(code, dat_name, version,
                     hashlib.sha256(raw).hexdigest(),
                     select_payload_roms(entries))


def build_table(sources: list[DatSource]) -> bytes:
    """Render and gzip the versioned table. Byte-identical for equal input."""
    lines = [
        f"#mesen-no-intro-sha1-table\t{FORMAT_VERSION}",
        "#source\tNo-Intro DATs, mirrored by libretro-database at metadat/no-intro "
        "(https://github.com/libretro/libretro-database)",
        "#licence\tCC BY-SA 4.0 -- libretro-database's own licence "
        "(https://github.com/libretro/libretro-database/blob/master/LICENSE); the "
        "DATs are No-Intro's data files, mirrored there. This table is Adapted "
        "Material (7 systems, rows reduced to payload sha1 + console + game name, "
        "the NES DAT's headered .nes rows dropped) and is offered under the same "
        "licence. Attribution and the licence's URI travel with it; see ADR-0266 "
        "and scripts/no_intro_sha1.NOTICE.md. Names and hashes only -- no ROM "
        "bytes, no artwork",
        "#hash\tSHA-1 of the ROM payload, never of the file: for .nes, the bytes "
        "after the 16-byte iNES header and any 512-byte trainer, clamped to the "
        "header-declared PRG+CHR size (ADR-0003, ADR-0039). The NES DAT's headered "
        ".nes roms are dropped; only their headerless .unh twins are listed, so "
        "every key is a payload hash",
    ]
    for source in sources:
        lines.append(f"#console\t{source.code}\t{source.dat_name}\t"
                     f"{source.version}\t{source.sha256}")

    rows: dict[str, tuple[str, str]] = {}
    for source in sources:
        for sha1, name in source.entries:
            key = sha1.lower()
            #First console in CONSOLES order wins a sha1 two DATs both list;
            #build_table is fed in that order by main().
            rows.setdefault(key, (source.code, name))
    for sha1 in sorted(rows):
        code, name = rows[sha1]
        lines.append(f"{sha1}\t{code}\t{name}")

    data = ("\n".join(lines) + "\n").encode("utf-8")
    return gzip.compress(data, compresslevel=9, mtime=0)


def fetch_dat(dat_name: str, timeout: float = 120.0) -> bytes:
    url = dat_url(dat_name)
    print(f"fetching {url}", file=sys.stderr)
    try:
        with urllib.request.urlopen(url, timeout=timeout) as response:  # noqa: S310 - https literal
            return response.read()
    except urllib.error.URLError as error:
        raise SystemExit(f"FAIL: could not download {url}: {error}") from error


def collect() -> list[DatSource]:
    sources: list[DatSource] = []
    for code, dat_name in CONSOLES:
        raw = fetch_dat(dat_name)
        source = dat_source(code, dat_name, raw)
        sources.append(source)
        print(f"  {code}: {len(source.entries)} roms (dat {source.version})",
              file=sys.stderr)
    return sources


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--check", action="store_true",
                        help="do not write; fail if the committed table differs")
    args = parser.parse_args(argv)

    table = build_table(collect())
    if args.check:
        committed = TABLE_PATH.read_bytes() if TABLE_PATH.is_file() else b""
        if committed != table:
            print(f"FAIL: {TABLE_PATH} is stale - regenerate it", file=sys.stderr)
            return 1
        print("OK: the committed table matches a fresh build")
        return 0

    TABLE_PATH.write_bytes(table)
    rows = sum(1 for line in gzip.decompress(table).decode("utf-8").splitlines()
               if not line.startswith("#"))
    print(f"wrote {TABLE_PATH} ({len(table)} bytes gzipped, {rows} roms)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
