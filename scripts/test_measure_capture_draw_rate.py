#!/usr/bin/env python3
"""Unit tests for scripts/measure_capture_draw_rate.py (F12.13 / ADR-0221).

Synthetic pack + grid dump, no emulator. What needs a real recording -- that
the gate this tool says fires is the gate HdNesPack reads -- is the F12.13
validation log, which runs it on the library re-record.

Usage: python3 scripts/test_measure_capture_draw_rate.py
"""
import os
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import measure_capture_draw_rate as m  # noqa: E402

FAILURES = []


def check(condition, message):
    if condition:
        print(f"  ok   {message}")
    else:
        print(f"  FAIL {message}")
        FAILURES.append(message)


GLYPH = "3C7EFFFFFFFF7E3C" + "0000000000000000"
FLAT = "00" * 16
OTHER = "FF" * 8 + "00" * 8


def fake_rom(path: Path, tiles: list[str]) -> None:
    # iNES header: 1 x 16 KB PRG, 1 x 8 KB CHR, no trainer. CHR tile i = tiles[i].
    header = b"NES\x1a" + bytes([1, 1, 0, 0]) + bytes(8)
    prg = bytes(16384)
    chr_ = b"".join(bytes.fromhex(t) for t in tiles)
    chr_ += bytes(8192 - len(chr_))
    path.write_bytes(header + prg + chr_)


def grid(frames: list[dict]) -> str:
    """frames: [{(x, y): (shape, pal)}]; shapes 1=GLYPH pal 0x0F302207, 2=FLAT, 3=OTHER."""
    out = ["K 1 " + GLYPH + " 0F302207", "K 2 " + FLAT + " 0F302207", "K 3 " + OTHER + " 0F302207",
           "P 0 0F302207", "P 1 0F0F0F0F"]
    lines = []
    for f in frames:
        lines.append("F 0")
        lines.append("M " + "00" * 4)
        for (x, y), (s, p) in f.items():
            lines.append(f"{x} {y} {s} {p}")
    return "\n".join(out + lines) + "\n"


def run(hires: str, dump: str, rom_tiles=None) -> dict:
    with tempfile.TemporaryDirectory() as d:
        h = Path(d, "hires.txt")
        h.write_text(hires)
        g = Path(d, "grid.txt")
        g.write_text(dump)
        chr_rom = None
        if rom_tiles is not None:
            r = Path(d, "x.nes")
            fake_rom(r, rom_tiles)
            chr_rom = m.read_chr(r)
        bgs, problems = m.parse_pack(h, chr_rom)
        result = m.score(bgs, g)
        result["problems"] = problems
        return result


def test_chr_ram_gate_counts_the_frames_it_fires_on():
    hires = "\n".join([
        f"<condition>s1_A,tileAtPosition,8,16,{GLYPH},0F302207",
        f"<condition>s1_B,tileAtPosition,80,16,{GLYPH},0F302207",
        "[s1_A&s1_B]<background>backgrounds/screen001.png,1,0,0,20",
    ])
    both = {(8, 16): (1, 0), (80, 16): (1, 0)}
    one = {(8, 16): (1, 0), (80, 16): (2, 0)}
    r = run(hires, grid([both, both, one, {}]))
    check(r["frames_played"] == 4, "four F lines are four played frames")
    check(r["rows"][0]["frames_fired"] == 2, "the gate fires only where every probe matches")
    check(abs(r["rows"][0]["rate"] - 0.5) < 1e-9, "rate is fired / played")
    check(r["captures_never_firing"] == 0 and not r["problems"], "a CHR RAM pack needs no ROM")


def test_chr_rom_probe_resolves_the_index_through_the_rom():
    hires = "\n".join([
        "<condition>s1_A,tileAtPosition,8,16,01,0F302207",
        "[s1_A]<background>backgrounds/screen001.png,1,0,0,20",
    ])
    r = run(hires, grid([{(8, 16): (1, 0)}, {(8, 16): (3, 0)}]), rom_tiles=[FLAT, GLYPH, OTHER])
    check(r["rows"][0]["frames_fired"] == 1 and not r["problems"],
          "a CHR ROM probe matches the cell whose shape has the ROM's bytes at that index")
    r = run(hires, grid([{(8, 16): (1, 0)}]))
    check(r["problems"] and r["rows"][0]["frames_fired"] == 0,
          "a CHR ROM pack without --rom is reported as undecodable, not scored as zero")


def test_palette_is_part_of_the_gate():
    hires = "\n".join([
        f"<condition>s1_A,tileAtPosition,8,16,{GLYPH},0F302207",
        "[s1_A]<background>backgrounds/screen001.png,1,0,0,20",
    ])
    r = run(hires, grid([{(8, 16): (1, 0)}, {(8, 16): (1, 1)}]))
    check(r["rows"][0]["frames_fired"] == 1, "the same tile under another palette word does not fire the gate")
    hires_y = hires.replace("0F302207", "0F302207,Y")
    r = run(hires_y, grid([{(8, 16): (1, 0)}, {(8, 16): (1, 1)}]))
    check(r["rows"][0]["frames_fired"] == 2, "an IgnorePalette (,Y) probe matches any palette")


def test_repeated_frames_count_as_played_frames():
    hires = "\n".join([
        f"<condition>s1_A,tileAtPosition,8,16,{GLYPH},0F302207",
        "[s1_A]<background>backgrounds/screen001.png,1,0,0,20",
    ])
    f = {(8, 16): (1, 0)}
    r = run(hires, grid([f, f, f, {}, {}]))
    check(r["frames_played"] == 5 and r["rows"][0]["frames_fired"] == 3,
          "the dump re-emits a held frame once per repeat and each one is a played frame")


def test_unscored_condition_kinds_are_reported():
    hires = "\n".join([
        "<condition>s1_A,memoryCheck,0,1,2,3",
        "[s1_A]<background>backgrounds/screen001.png,1,0,0,20",
    ])
    r = run(hires, grid([{}]))
    check(r["captures"] == 1 and r["captures_scored"] == 0 and r["problems"],
          "a background gated on a condition kind this tool does not score is counted, not scored, and reported")


if __name__ == "__main__":
    for name, fn in sorted(globals().items()):
        if name.startswith("test_") and callable(fn):
            print(name)
            fn()
    if FAILURES:
        print(f"{len(FAILURES)} failure(s)")
        sys.exit(1)
    print("all passed")
