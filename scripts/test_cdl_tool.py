"""Headless suite for the CDL reader (`cdl_tool.py`).

Every fixture here is built in memory from the format itself - an iNES header
and a flag array - so the suite needs no ROM, no emulator and no committed
binary. What it pins down is the part a wrong answer would be silent about:
the header is parsed rather than assumed, a CDL from another ROM never merges
into a union, the CHR block splits off the ROM's header (and does not exist at
all for a CHR RAM game), a region ends exactly where the classification
changes, and `strip` blanks the half the Core blanks.

Run:  python3 scripts/test_cdl_tool.py
"""

import io
import sys
import tempfile
from contextlib import redirect_stdout
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import cdl_tool as C  # noqa: E402

_FAILURES = []


def check(cond, name, detail=""):
    if cond:
        print(f"ok   {name}")
    else:
        print(f"FAIL {name}: {detail}")
        _FAILURES.append(name)


# --------------------------------------------------------------------------
# Fixtures
# --------------------------------------------------------------------------

def ines(prg_banks=2, chr_banks=1, fill=0xEA):
    """A minimal iNES file: header, then PRG, then CHR ROM (none when
    `chr_banks` is 0, which is how a CHR RAM game declares itself)."""
    header = bytearray(16)
    header[:4] = b"NES\x1a"
    header[4] = prg_banks
    header[5] = chr_banks
    prg = bytes([fill]) * (prg_banks * C.PRG_BANK_SIZE)
    chr_rom = bytes([0x5A]) * (chr_banks * C.CHR_BANK_SIZE)
    return bytes(header) + prg + chr_rom


def cdl_bytes(flags, crc=0x1234ABCD, header=True):
    body = bytes(flags)
    if not header:
        return body
    return C.MAGIC + crc.to_bytes(4, "little") + body


def write(td, name, data):
    path = Path(td) / name
    path.write_bytes(data)
    return str(path)


def run(argv):
    """Run the CLI, returning (exit code, stdout)."""
    buffer = io.StringIO()
    with redirect_stdout(buffer):
        code = C.main(argv)
    return code, buffer.getvalue()


# --------------------------------------------------------------------------
# Header parsing
# --------------------------------------------------------------------------

def test_the_header_is_parsed_not_assumed():
    with tempfile.TemporaryDirectory() as td:
        path = write(td, "a.cdl", cdl_bytes([C.CODE] * 10, crc=0xDEADBEEF))
        cdl = C.Cdl.load(path)
        check(cdl.has_header and cdl.crc32 == 0xDEADBEEF and len(cdl) == 10,
              "CDLv2 magic, little-endian CRC32 and the flag array all parse",
              f"{cdl.has_header} {cdl.crc32:#x} {len(cdl)}")


def test_a_headerless_file_is_read_as_legacy_rather_than_rejected():
    with tempfile.TemporaryDirectory() as td:
        path = write(td, "old.cdl", cdl_bytes([C.DATA] * 32, header=False))
        cdl = C.Cdl.load(path)
        check(not cdl.has_header and cdl.crc32 is None and len(cdl) == 32,
              "a CRC-less legacy file loads as-is, the way the Core loads it")


def test_an_empty_or_truncated_file_is_rejected():
    with tempfile.TemporaryDirectory() as td:
        empty = write(td, "empty.cdl", b"")
        stub = write(td, "stub.cdl", b"CDLv2\x01\x02")
        short = write(td, "short.cdl", b"\x01\x02\x03")
        rejected = 0
        for path in (empty, stub, short):
            try:
                C.Cdl.load(path)
            except C.CdlError:
                rejected += 1
        check(rejected == 3,
              "an empty file, a truncated CDLv2 header and a sub-header stub "
              "are all rejected", f"{rejected}/3 rejected")


def test_a_non_ines_rom_is_rejected():
    with tempfile.TemporaryDirectory() as td:
        path = write(td, "not.nes", b"UNIF" + bytes(64))
        try:
            C.Rom.load(path)
            check(False, "a non-iNES ROM is rejected", "it loaded")
        except C.CdlError as error:
            check("not an iNES file" in str(error),
                  "a non-iNES ROM is rejected by name", str(error))


def test_a_trainer_shifts_the_prg_window():
    with tempfile.TemporaryDirectory() as td:
        raw = bytearray(ines(prg_banks=1, chr_banks=0, fill=0x11))
        raw[6] |= 0x04                       # trainer present
        raw[16:16] = bytes([0x99]) * 512      # the 512-byte trainer
        rom = C.Rom(write(td, "trainer.nes", bytes(raw)), bytes(raw))
        check(rom.prg == bytes([0x11]) * C.PRG_BANK_SIZE,
              "the 512-byte trainer is skipped before the PRG starts")


# --------------------------------------------------------------------------
# The CHR split
# --------------------------------------------------------------------------

def test_the_chr_block_splits_off_the_ines_header():
    with tempfile.TemporaryDirectory() as td:
        rom = C.Rom(write(td, "chrrom.nes", ines(1, 1)), ines(1, 1))
        flags = bytearray([C.CODE]) * C.PRG_BANK_SIZE + bytearray([C.CODE]) * C.CHR_BANK_SIZE
        cdl = C.Cdl(write(td, "a.cdl", cdl_bytes(flags)), flags, 1, True)
        prg, chr_flags = C.split(rom, cdl)
        check(len(prg) == C.PRG_BANK_SIZE and len(chr_flags) == C.CHR_BANK_SIZE,
              "a CHR ROM game splits into a PRG block and a CHR block",
              f"{len(prg)} {len(chr_flags)}")


def test_a_chr_ram_game_has_no_chr_block():
    with tempfile.TemporaryDirectory() as td:
        raw = ines(1, 0)
        rom = C.Rom(write(td, "chrram.nes", raw), raw)
        flags = bytearray([C.CODE]) * C.PRG_BANK_SIZE
        cdl = C.Cdl("x.cdl", flags, 1, True)
        prg, chr_flags = C.split(rom, cdl)
        check(not rom.has_chr_rom and len(prg) == C.PRG_BANK_SIZE
              and chr_flags == bytearray(),
              "a CHR RAM game's CDL is PRG only - the split comes from the "
              "header, not from the file's length")


def test_a_cdl_too_short_for_its_rom_is_rejected():
    with tempfile.TemporaryDirectory() as td:
        raw = ines(2, 1)
        rom = C.Rom(write(td, "big.nes", raw), raw)
        short_prg = C.Cdl("s.cdl", bytearray(100), 1, True)
        missing_chr = C.Cdl("m.cdl", bytearray(2 * C.PRG_BANK_SIZE), 1, True)
        rejected = 0
        for cdl in (short_prg, missing_chr):
            try:
                C.split(rom, cdl)
            except C.CdlError:
                rejected += 1
        check(rejected == 2,
              "a CDL shorter than the PRG, or missing its CHR block, is "
              "rejected rather than silently truncated", f"{rejected}/2")


# --------------------------------------------------------------------------
# report
# --------------------------------------------------------------------------

def test_report_counts_code_data_and_both_the_way_the_core_does():
    flags = bytearray([C.CODE, C.CODE | C.DATA, C.DATA, 0,
                       C.CODE | C.JUMP_TARGET, C.DATA | C.PCM_DATA,
                       C.CODE | C.SUB_ENTRY_POINT])
    stats = C.counts(flags)
    check((stats["code_only"], stats["data_only"], stats["both"],
           stats["untouched"]) == (3, 2, 1, 1),
          "code, data and code+data are disjoint tallies, as in CdlStatistics",
          str(stats))
    check((stats["jump_targets"], stats["sub_entry_points"], stats["pcm_bytes"])
          == (1, 1, 1),
          "JumpTarget, SubEntryPoint and the NES-only PcmData flag are carried")


def test_report_names_an_untouched_bank():
    with tempfile.TemporaryDirectory() as td:
        raw = ines(2, 0)
        rom_path = write(td, "two.nes", raw)
        flags = bytearray([C.CODE]) * C.PRG_BANK_SIZE + bytearray(C.PRG_BANK_SIZE)
        cdl_path = write(td, "two.cdl", cdl_bytes(flags))
        code, out = run(["report", rom_path, cdl_path])
        check(code == 0 and "never entered" in out and "CHR RAM" in out,
              "the per-bank breakdown says which bank the run never entered",
              out)


def test_report_states_the_drawn_chr_bytes():
    with tempfile.TemporaryDirectory() as td:
        raw = ines(1, 1)
        rom_path = write(td, "chr.nes", raw)
        chr_flags = bytearray([C.CODE]) * 2048 + bytearray(C.CHR_BANK_SIZE - 2048)
        flags = bytearray(C.PRG_BANK_SIZE) + chr_flags
        cdl_path = write(td, "chr.cdl", cdl_bytes(flags))
        code, out = run(["report", rom_path, cdl_path])
        check(code == 0 and "2048 of 8192 bytes drawn" in out,
              "a CHR ROM game reports drawn-vs-total CHR bytes", out)


# --------------------------------------------------------------------------
# union
# --------------------------------------------------------------------------

def test_union_is_a_bitwise_or_across_runs():
    with tempfile.TemporaryDirectory() as td:
        a = write(td, "a.cdl", cdl_bytes([C.CODE, 0, C.DATA, 0]))
        b = write(td, "b.cdl", cdl_bytes([0, C.DATA, C.CODE, 0]))
        out_path = str(Path(td) / "u.cdl")
        code, _ = run(["union", out_path, a, b])
        merged = C.Cdl.load(out_path)
        check(code == 0 and list(merged.flags) ==
              [C.CODE, C.DATA, C.CODE | C.DATA, 0],
              "coverage is the union of the runs, never one of them",
              str(list(merged.flags)))
        check(merged.has_header and merged.crc32 == 0x1234ABCD,
              "the union keeps the CRC32 header of the ROM it came from")


def test_union_refuses_two_roms_and_names_both():
    with tempfile.TemporaryDirectory() as td:
        a = write(td, "a.cdl", cdl_bytes([C.CODE] * 4, crc=0xAAAAAAAA))
        b = write(td, "b.cdl", cdl_bytes([C.CODE] * 4, crc=0xBBBBBBBB))
        out_path = str(Path(td) / "u.cdl")
        buffer = io.StringIO()
        with redirect_stdout(buffer):
            code = C.main(["union", out_path, a, b])
        check(code == 1 and not Path(out_path).exists(),
              "a CRC mismatch fails the union and writes nothing")
        try:
            C.cmd_union(C.build_parser().parse_args(["union", out_path, a, b]))
            message = ""
        except C.CdlError as error:
            message = str(error)
        check("0xAAAAAAAA" in message and "0xBBBBBBBB" in message
              and "a.cdl" in message and "b.cdl" in message,
              "the refusal names both files and both CRCs", message)


def test_union_refuses_files_of_different_sizes():
    with tempfile.TemporaryDirectory() as td:
        a = write(td, "a.cdl", cdl_bytes([C.CODE] * 4))
        b = write(td, "b.cdl", cdl_bytes([C.CODE] * 8))
        code, _ = run(["union", str(Path(td) / "u.cdl"), a, b])
        check(code == 1, "two CDL files of different lengths do not merge")


# --------------------------------------------------------------------------
# regions
# --------------------------------------------------------------------------

def test_a_region_ends_exactly_where_the_class_changes():
    flags = bytearray([C.CODE] * 4 + [C.DATA] * 6 + [0] * 2 + [C.CODE | C.DATA] * 3)
    found = C.regions(flags, C.PRG_BANK_SIZE)
    shape = [(r["start"], r["end"], r["size"], r["class"]) for r in found]
    check(shape == [(0, 3, 4, "code"), (4, 9, 6, "data"),
                    (10, 11, 2, "unused"), (12, 14, 3, "code+data")],
          "runs coalesce and break at both boundaries, with no byte lost",
          str(shape))


def test_a_flag_inside_a_run_does_not_break_it():
    flags = bytearray([C.CODE, C.CODE | C.JUMP_TARGET, C.CODE | C.SUB_ENTRY_POINT,
                       C.CODE])
    found = C.regions(flags, C.PRG_BANK_SIZE)
    check(len(found) == 1 and found[0]["jump_targets"] == 1
          and found[0]["sub_entry_points"] == 1,
          "a jump target inside a code run is counted, not treated as a break",
          str(found))


def test_the_first_and_last_byte_each_belong_to_a_region():
    flags = bytearray([C.DATA] + [C.CODE] * 3 + [C.DATA])
    found = C.regions(flags, C.PRG_BANK_SIZE)
    check(found[0]["start"] == 0 and found[-1]["end"] == len(flags) - 1
          and sum(r["size"] for r in found) == len(flags),
          "coalescing covers the array end to end")


def test_an_empty_block_yields_no_region():
    check(C.regions(bytearray(), C.PRG_BANK_SIZE) == [],
          "an empty block yields no region rather than a zero-size one")


def test_a_region_spanning_two_banks_says_so():
    flags = bytearray([C.CODE]) * (C.PRG_BANK_SIZE + 100)
    found = C.regions(flags, C.PRG_BANK_SIZE)
    check(len(found) == 1 and found[0]["bank"] == 0 and found[0]["bank_span"] == 2,
          "a run crossing a bank boundary stays one run and records the span",
          str(found))


def test_candidates_are_data_only_runs_biggest_first():
    flags = (bytearray([C.DATA] * 50) + bytearray([C.CODE] * 10)
             + bytearray([C.DATA] * 200) + bytearray([C.CODE | C.DATA] * 300))
    top = C.candidates(C.regions(flags, C.PRG_BANK_SIZE), 16)
    check([r["size"] for r in top] == [200, 50],
          "candidate asset tables are the data-only runs, biggest first - the "
          "300-byte code+data run is not one", str([r["size"] for r in top]))


def test_regions_output_says_it_names_nothing():
    with tempfile.TemporaryDirectory() as td:
        raw = ines(1, 0)
        rom_path = write(td, "r.nes", raw)
        flags = bytearray([C.CODE] * 1000 + [C.DATA] * 3000
                          + [0] * (C.PRG_BANK_SIZE - 4000))
        cdl_path = write(td, "r.cdl", cdl_bytes(flags))
        code, out = run(["regions", rom_path, cdl_path])
        unwrapped = " ".join(out.split())
        check(code == 0 and "not of meaning" in unwrapped
              and "Nothing here names anything" in unwrapped,
              "the region table states that access is not meaning (ADR-0183)",
              out[:400])
        check("0x0003E8" in out, "region bounds print as ROM offsets", out[:400])


def test_regions_json_carries_the_same_rows():
    import json
    with tempfile.TemporaryDirectory() as td:
        raw = ines(1, 0)
        rom_path = write(td, "r.nes", raw)
        flags = bytearray([C.DATA] * 4096 + [0] * (C.PRG_BANK_SIZE - 4096))
        cdl_path = write(td, "r.cdl", cdl_bytes(flags))
        code, out = run(["regions", rom_path, cdl_path, "--json"])
        payload = json.loads(out)
        check(code == 0 and payload["candidate_asset_tables"][0]["size"] == 4096
              and payload["caveat"],
              "--json emits the same rows plus the caveat", out[:200])


# --------------------------------------------------------------------------
# strip
# --------------------------------------------------------------------------

def test_strip_keep_used_blanks_the_untouched_bytes():
    with tempfile.TemporaryDirectory() as td:
        raw = ines(1, 0, fill=0xAB)
        rom_path = write(td, "s.nes", raw)
        flags = bytearray([C.CODE] * 16 + [0] * (C.PRG_BANK_SIZE - 16))
        cdl_path = write(td, "s.cdl", cdl_bytes(flags))
        out_path = str(Path(td) / "used.bin")
        code, _ = run(["strip", rom_path, cdl_path, out_path, "--keep", "used"])
        data = Path(out_path).read_bytes()
        check(code == 0 and len(data) == C.PRG_BANK_SIZE
              and data[:16] == bytes([0xAB]) * 16
              and set(data[16:]) == {0},
              "--keep used mirrors StripUnused: flagged bytes survive, the "
              "rest go to zero")


def test_strip_keep_unused_blanks_the_touched_bytes():
    with tempfile.TemporaryDirectory() as td:
        raw = ines(1, 0, fill=0xAB)
        rom_path = write(td, "s.nes", raw)
        flags = bytearray([C.CODE] * 16 + [0] * (C.PRG_BANK_SIZE - 16))
        cdl_path = write(td, "s.cdl", cdl_bytes(flags))
        out_path = str(Path(td) / "unused.bin")
        code, _ = run(["strip", rom_path, cdl_path, out_path, "--keep", "unused"])
        data = Path(out_path).read_bytes()
        check(code == 0 and set(data[:16]) == {0}
              and data[16:] == bytes([0xAB]) * (C.PRG_BANK_SIZE - 16),
              "--keep unused mirrors StripUsed: the touched bytes go to zero")


def test_strip_covers_the_chr_block_too():
    with tempfile.TemporaryDirectory() as td:
        raw = ines(1, 1, fill=0xAB)
        rom_path = write(td, "s.nes", raw)
        flags = (bytearray(C.PRG_BANK_SIZE)
                 + bytearray([C.CODE] * 32 + [0] * (C.CHR_BANK_SIZE - 32)))
        cdl_path = write(td, "s.cdl", cdl_bytes(flags))
        out_path = str(Path(td) / "used.bin")
        run(["strip", rom_path, cdl_path, out_path, "--keep", "used"])
        data = Path(out_path).read_bytes()
        chr_part = data[C.PRG_BANK_SIZE:]
        check(len(data) == C.PRG_BANK_SIZE + C.CHR_BANK_SIZE
              and set(data[:C.PRG_BANK_SIZE]) == {0}
              and chr_part[:32] == bytes([0x5A]) * 32
              and set(chr_part[32:]) == {0},
              "the CHR block is stripped with the same rule, at offset _memSize")


def main():
    tests = [
        test_the_header_is_parsed_not_assumed,
        test_a_headerless_file_is_read_as_legacy_rather_than_rejected,
        test_an_empty_or_truncated_file_is_rejected,
        test_a_non_ines_rom_is_rejected,
        test_a_trainer_shifts_the_prg_window,
        test_the_chr_block_splits_off_the_ines_header,
        test_a_chr_ram_game_has_no_chr_block,
        test_a_cdl_too_short_for_its_rom_is_rejected,
        test_report_counts_code_data_and_both_the_way_the_core_does,
        test_report_names_an_untouched_bank,
        test_report_states_the_drawn_chr_bytes,
        test_union_is_a_bitwise_or_across_runs,
        test_union_refuses_two_roms_and_names_both,
        test_union_refuses_files_of_different_sizes,
        test_a_region_ends_exactly_where_the_class_changes,
        test_a_flag_inside_a_run_does_not_break_it,
        test_the_first_and_last_byte_each_belong_to_a_region,
        test_an_empty_block_yields_no_region,
        test_a_region_spanning_two_banks_says_so,
        test_candidates_are_data_only_runs_biggest_first,
        test_regions_output_says_it_names_nothing,
        test_regions_json_carries_the_same_rows,
        test_strip_keep_used_blanks_the_untouched_bytes,
        test_strip_keep_unused_blanks_the_touched_bytes,
        test_strip_covers_the_chr_block_too,
    ]
    for t in tests:
        t()
    print(f"\n{len(tests) - len(_FAILURES)}/{len(tests)} cases passed")
    return 1 if _FAILURES else 0


if __name__ == "__main__":
    sys.exit(main())
