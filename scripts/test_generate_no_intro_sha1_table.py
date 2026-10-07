#!/usr/bin/env python3
"""Framework-free checks for scripts/generate_no_intro_sha1_table.py — the
generator of the versioned SHA1 -> (console, No-Intro name) table the library
looks a ROM up in (#1038, parent spec #1030).

Checks:
  AC-1 parse_dat keeps every <rom> sha1 together with its <game> name, skips a
       rom that declares no sha1, and reads the DAT's own version.
  AC-2 a name that would break the TSV (a tab, a newline) is refused instead
       of being written, and a DAT in the XML shape is refused with a message
       naming the shape, not silently parsed as empty.
  AC-3 build_table's output is pinned byte for byte (header lines, the
       `#console` provenance line, rows sorted by sha1) and is deterministic.
       The pin is taken through `dat_source`, so parsing, the `.nes` filter and
       the rendering are pinned as one output rather than row by row.
  AC-4 a sha1 two consoles both claim is written once, for the first console
       in the generator's own order.
  AC-5 the committed scripts/no_intro_sha1.tsv.gz is well formed: format
       version, all seven console codes, 40-lowercase-hex keys, non-empty
       names, and a floor on the entry count a truncated regeneration fails.
  AC-6 the NES DAT's headered `.nes` rom is dropped in favour of its headerless
       `.unh` twin, so the emitted table holds only payload keys.

Usage: python3 scripts/test_generate_no_intro_sha1_table.py
No network: AC-1..AC-4 run on an in-memory fixture DAT, AC-5 on the file
committed in the repo.
"""
from __future__ import annotations

import gzip
import hashlib
import re
import sys
from pathlib import Path

SCRIPTS = Path(__file__).resolve().parent
sys.path.insert(0, str(SCRIPTS))

import generate_no_intro_sha1_table as gen  # noqa: E402

TABLE_PATH = SCRIPTS / gen.TABLE_FILE_NAME

SHA1_A = "1111111111111111111111111111111111111111"
SHA1_B = "2222222222222222222222222222222222222222"
SHA1_C = "3333333333333333333333333333333333333333"
SHA1_D = "4444444444444444444444444444444444444444"
SHA1_HEADERED = "5555555555555555555555555555555555555555"
SHA1_PAYLOAD = "6666666666666666666666666666666666666666"

#A clrmamepro/Logiqx DAT in the shape libretro-database mirrors: one game with
#a single rom, one whose two roms are two sha1s of the same game name, and one
#whose second rom declares no sha1 at all.
#
#The rom names are the ones the real DATs use: a `.unh` (or any non-headered)
#name is a payload rom and is kept, a `.nes` name is a headered dump and is
#dropped. Beta Racer is the one `.nes`/`.unh` pair -- its `.unh` row survives
#and its `.nes` row does not -- so the filter is exercised through the same
#build_table call the pin asserts on, not by a hand-built entry list.
FIXTURE_DAT = """clrmamepro (
\tname "Fixture - Test System"
\tdescription "Fixture - Test System"
\tversion "2020.01.02"
\thomepage "http://example.invalid"
)

game (
\tname "Alpha Quest (USA)"
\trom ( name "Alpha Quest (USA).unh" size 40944 crc 11111111 md5 AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA sha1 %s )
)
game (
\tname "Beta Racer (Europe) (Rev 1)"
\trom ( name "Beta Racer (Europe) (Rev 1).unh" size 65536 crc 22222222 md5 BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB sha1 %s )
\trom ( name "Beta Racer (Europe) (Rev 1) [b].nes" size 65552 crc 33333333 md5 CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC sha1 %s )
)
game (
\tname "Gamma Boy (Japan)"
\trom ( name "Gamma Boy (Japan).gb" size 32768 crc 44444444 md5 DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD sha1 %s )
\trom ( name "Gamma Boy (Japan) [b].gb" size 32768 crc 44444444 md5 DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD )
)
""" % (SHA1_A, SHA1_B, SHA1_C, SHA1_D)

#A game the NES DAT lists twice: one `.nes` rom hashed over the whole file
#(iNES header included) and its `.unh` twin hashed over the payload. The two
#hashes differ on purpose, so an assertion on them can actually fail.
FIXTURE_DUPLICATE_DAT = """clrmamepro (
\tname "Fixture - Test System"
\tversion "2020.01.02"
)

game (
\tname "Delta Dungeon (World)"
\trom ( name "Delta Dungeon (World).nes" size 40976 crc EEEEEEEE md5 EEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEE sha1 %s )
)
game (
\tname "Delta Dungeon (World)"
\trom ( name "Delta Dungeon (World).unh" size 40960 crc FFFFFFFF md5 FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF sha1 %s )
)
""" % (SHA1_HEADERED, SHA1_PAYLOAD)


def test_a_headered_nes_rom_is_dropped_in_favour_of_its_payload_twin():
    source = gen.dat_source("nes", "Fixture - Test System",
                            FIXTURE_DUPLICATE_DAT.encode("utf-8"))
    table = gzip.decompress(gen.build_table([source])).decode("utf-8")
    rows = [line.split("\t") for line in table.splitlines() if not line.startswith("#")]
    assert rows == [[SHA1_PAYLOAD.lower(), "nes", "Delta Dungeon (World)"]], rows


def test_parse_dat_keeps_every_rom_sha1_with_its_game_name():
    version, entries = gen.parse_dat(FIXTURE_DAT)
    assert version == "2020.01.02", version
    assert entries == (
        (SHA1_A, "Alpha Quest (USA)", "Alpha Quest (USA).unh"),
        (SHA1_B, "Beta Racer (Europe) (Rev 1)",
         "Beta Racer (Europe) (Rev 1).unh"),
        (SHA1_C, "Beta Racer (Europe) (Rev 1)",
         "Beta Racer (Europe) (Rev 1) [b].nes"),
        (SHA1_D, "Gamma Boy (Japan)", "Gamma Boy (Japan).gb"),
    ), entries


def test_a_rom_without_a_sha1_is_skipped_not_guessed():
    _, entries = gen.parse_dat(FIXTURE_DAT)
    assert [sha1 for sha1, _, _ in entries].count(SHA1_D) == 1, entries


def test_a_tab_or_newline_in_a_name_is_refused():
    broken = FIXTURE_DAT.replace("Alpha Quest (USA)\"", "Alpha\tQuest (USA)\"")
    try:
        gen.parse_dat(broken)
    except ValueError as error:
        assert "tab" in str(error), error
    else:
        raise AssertionError("a name holding a tab was accepted")


def test_an_xml_shaped_dat_is_refused_by_name():
    xml = FIXTURE_DAT.replace("game (\n\tname \"Alpha Quest (USA)\"",
                              "<game name=\"Alpha Quest (USA)\"")
    try:
        gen.parse_dat(xml)
    except ValueError as error:
        assert "clrmamepro" in str(error), error
    else:
        raise AssertionError("an XML-shaped DAT was accepted")


def _fixture_source(code="nes", entries=None):
    return gen.DatSource(
        code=code,
        dat_name="Fixture - Test System",
        version="2020.01.02",
        sha256=hashlib.sha256(FIXTURE_DAT.encode("utf-8")).hexdigest(),
        entries=entries if entries is not None else (
            (SHA1_B, "Beta Racer (Europe) (Rev 1)"),
            (SHA1_A, "Alpha Quest (USA)"),
        ),
    )


#Pinned byte for byte: the header carries the format version the C# reader
#checks, the source and the licence the ticket requires the script to record
#(with the ADR-0266 citation so a reader of the artifact can find the record),
#the ADR-0003 byte-range note, and one provenance line per DAT.
#
#The fixture goes through `dat_source` -- parse, the `.nes` filter and the
#rendering -- so this one assertion fails if any of the three regresses. Feeding
#hand-built DatSource entries here would leave `select_payload_roms` and
#`parse_dat`'s field order checked only by the single-row tests above.
def test_build_table_output_is_pinned():
    source = gen.dat_source("nes", "Fixture - Test System",
                            FIXTURE_DAT.encode("utf-8"))
    table = gen.build_table([source])
    expected = "\n".join([
        "#mesen-no-intro-sha1-table\t1",
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
        "#console\tnes\tFixture - Test System\t2020.01.02\t" + source.sha256,
        #Sorted by sha1: A (1111...), B (2222...), then Gamma Boy's D (4444...).
        #SHA1_C is absent -- Beta Racer's `.nes` row was dropped.
        SHA1_A.lower() + "\tnes\tAlpha Quest (USA)",
        SHA1_B.lower() + "\tnes\tBeta Racer (Europe) (Rev 1)",
        SHA1_D.lower() + "\tnes\tGamma Boy (Japan)",
        "",
    ])
    assert gzip.decompress(table).decode("utf-8") == expected


def test_build_table_is_deterministic():
    first = gen.build_table([_fixture_source()])
    second = gen.build_table([_fixture_source()])
    assert first == second, "the gzip container changed between two builds"


def test_a_sha1_two_consoles_claim_is_written_once_for_the_first():
    nes = _fixture_source(entries=((SHA1_A, "Alpha Quest (USA)"),))
    shared = _fixture_source("gb", entries=((SHA1_A, "Alpha Quest (Japan)"),))
    table = gzip.decompress(gen.build_table([nes, shared])).decode("utf-8")
    rows = [line.split("\t") for line in table.splitlines() if not line.startswith("#")]
    assert rows == [[SHA1_A.lower(), "nes", "Alpha Quest (USA)"]], rows


def test_the_committed_table_is_well_formed():
    assert TABLE_PATH.is_file(), f"{TABLE_PATH} is not committed"
    text = gzip.decompress(TABLE_PATH.read_bytes()).decode("utf-8")
    lines = text.splitlines()
    assert lines[0] == "#mesen-no-intro-sha1-table\t%d" % gen.FORMAT_VERSION, lines[0]

    consoles = set()
    counts = {}
    rows = 0
    for line in lines[1:]:
        if line.startswith("#console\t"):
            consoles.add(line.split("\t")[1])
            continue
        if line.startswith("#"):
            continue
        sha1, code, name = line.split("\t")
        assert re.fullmatch(r"[0-9a-f]{40}", sha1), line
        assert code in gen.CONSOLE_CODES, line
        assert name and "\t" not in name, line
        counts[code] = counts.get(code, 0) + 1
        rows += 1

    assert consoles == set(gen.CONSOLE_CODES), consoles
    assert rows == len({line.split("\t")[0] for line in lines if not line.startswith("#")}), \
        "the committed table holds a duplicate sha1"
    assert [line.split("\t")[0] for line in lines
            if not line.startswith("#")] == sorted(line.split("\t")[0] for line in lines
                                                  if not line.startswith("#")), \
        "the committed table is no longer sorted by sha1"
    #A floor per console, so a truncated regeneration fails instead of shipping
    #a table that silently never matches a Game Gear ROM.
    floors = {"nes": 1000, "gb": 400, "gbc": 300, "gba": 800,
              "sms": 200, "sg1000": 40, "gg": 150}
    for code, floor in floors.items():
        assert counts.get(code, 0) >= floor, f"{code}: {counts.get(code, 0)} < {floor}"


def main():
    failures = []
    for name, function in sorted(list(globals().items())):
        if not name.startswith("test_") or not callable(function):
            continue
        try:
            function()
            print(f"PASS: {name}")
        except Exception as error:  # noqa: BLE001 - a failing check is the verdict
            failures.append(name)
            print(f"FAIL: {name}: {error!r}")
    print(f"{len(failures)} failed" if failures else "all checks passed")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
