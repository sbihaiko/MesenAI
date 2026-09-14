#!/usr/bin/env python3
"""Headless suite for artist_cover.py — the "measure before you paint" number.

Everything here runs on *synthetic* packs written to a temp dir: a reference
`hires.txt` and one or more recorder `auto/` folders, hand-written so the right
answer is known by counting. No pack, no ROM, no emulator, no network.

What the suite pins down (#225):

  * the namespace rule, taken from the emulator's own
    `HdPackLoader::ReadTileData` — a `tileData` of 32+ hex chars is a CHR RAM
    pattern, anything shorter is a CHR ROM bank index;
  * the refusal: a reference keyed in a namespace the recording cannot produce
    exits non-zero and names the cause, instead of printing a confident 0%;
  * the IPS reader that supplies the evidence for that message — the bytes the
    pack's own `<patch>` writes into the iNES header, including the RLE record
    form;
  * that the refusal does *not* fire on a legitimate low-but-nonzero coverage,
    nor on a normal reference, which must still measure exactly as before;
  * the partial-mismatch warning, which keeps unreachable reference tiles from
    reading as merely unseen;
  * the <patch> caveat (#231): a reference built for a patched ROM whose keys
    share the recording's shape is *measured*, not refused, but the patch and
    its target sha1 are named above the tables and the summary line carries the
    caveat — and a patch-less reference gets no such warning at all.

Standard library only, no pytest, no emulator.
"""
import io
import json
import sys
import tempfile
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import artist_cover as C  # noqa: E402

_FAILURES = []


def check(cond, name, detail=""):
    if cond:
        print(f"ok   {name}")
    else:
        print(f"FAIL {name}: {detail}")
        _FAILURES.append(name)


# --- fixtures ---------------------------------------------------------------


def pattern(n: int) -> str:
    """A CHR RAM tileData key: 16 bytes, 32 hex chars, distinct per n."""
    return f"{n:032X}"


def index(n: int) -> str:
    """A CHR ROM tileData key: a short bank index, as the loader reads it."""
    return f"{n:X}"


def palette(n: int) -> str:
    return f"{n:08X}"


def manifest(path: Path, images, *, patches=(), scale=4):
    """Write a `hires.txt`. `images` is [(name, [tileData, ...]), ...]."""
    path.parent.mkdir(parents=True, exist_ok=True)
    out = ["<ver>108", f"<scale>{scale}"]
    for name, _tiles in images:
        out.append(f"<img>{name}")
    for f, sha1 in patches:
        out.append(f"<patch>{f},{sha1}")
    for i, (_name, tiles) in enumerate(images):
        for t in tiles:
            out.append(f"<tile>{i},{t},{palette(0)},0,0,0,N")
    path.write_text("\n".join(out) + "\n", encoding="utf-8")


def recorded(root: Path, tiles, *, sprites=()):
    """Write a recorder `auto/` folder holding those tileData keys."""
    tex = root / "textures"
    manifest(tex / "hires.txt", [("auto.png", list(tiles))])
    sheets = tex / "sheets"
    sheets.mkdir(parents=True, exist_ok=True)
    (sheets / "spr0.json").write_text(
        json.dumps({"cells": [{"tiles": [{"tile": t} for t in sprites]}]}), encoding="utf-8")
    return root


def ips(path: Path, records):
    """Write an IPS file. A record is (offset, bytes) or (offset, run, byte)."""
    blob = b"PATCH"
    for rec in records:
        if len(rec) == 2:
            off, data = rec
            blob += off.to_bytes(3, "big") + len(data).to_bytes(2, "big") + bytes(data)
        else:
            off, run, byte = rec
            blob += (off.to_bytes(3, "big") + (0).to_bytes(2, "big")
                     + run.to_bytes(2, "big") + bytes([byte]))
    path.write_bytes(blob + b"EOF")
    return path


def run(argv):
    """Run main(), returning (rc, stdout, stderr)."""
    out, err = io.StringIO(), io.StringIO()
    with redirect_stdout(out), redirect_stderr(err):
        rc = C.main(["artist_cover.py"] + [str(a) for a in argv])
    return rc, out.getvalue(), err.getvalue()


# --- the namespace rule -----------------------------------------------------


def test_shape_follows_the_emulators_own_width_rule():
    check(C.shape(pattern(1)) == "chr-ram-pattern", "32 hex chars is a CHR RAM pattern")
    check(C.shape("A" * 40) == "chr-ram-pattern", "over 32 hex chars is still a pattern")
    check(C.shape("A55") == "chr-rom-index", "a short key is a CHR ROM index")
    check(C.shape("14FB") == "chr-rom-index", "a four-digit key is still an index",
          "the widest real index (128 KB CHR ROM = 0x2000 tiles) is four digits")
    check(C.shape("A" * 31) == "chr-rom-index", "31 hex chars is below the loader's threshold")


# --- the IPS reader ---------------------------------------------------------


def test_the_ips_reader_recovers_the_header_bytes_a_patch_writes():
    with tempfile.TemporaryDirectory() as d:
        p = ips(Path(d) / "a.ips", [(4, b"\x10\x10\x13"), (0x1234, b"\xEA" * 4)])
        w = C.ips_header_writes(p)
    check(w == {4: 0x10, 5: 0x10, 6: 0x13}, "the iNES header window is read, later records are not",
          f"got {w}")


def test_the_ips_reader_finds_a_header_record_that_comes_after_a_body_record():
    # IPS records are not required to be ordered by offset: the emulator's own
    # patcher collects them all and applies them in stream order
    # (IpsPatcher::PatchBuffer). Stopping at the first offset past the header
    # window would report "no header evidence" for a patch that does write one.
    with tempfile.TemporaryDirectory() as d:
        p = ips(Path(d) / "unsorted.ips", [(0x1234, b"\xEA" * 8), (4, b"\x10\x10\x13")])
        w = C.ips_header_writes(p)
    check(w == {4: 0x10, 5: 0x10, 6: 0x13},
          "a header record after a body record is still read", f"got {w}")


def test_the_ips_reader_handles_an_rle_record_and_rejects_a_non_ips():
    with tempfile.TemporaryDirectory() as d:
        p = ips(Path(d) / "rle.ips", [(5, 2, 0x20)])
        w = C.ips_header_writes(p)
        check(w == {5: 0x20, 6: 0x20}, "an RLE record expands into the header", f"got {w}")
        junk = Path(d) / "not.ips"
        junk.write_bytes(b"BPS1nonsense")
        check(C.ips_header_writes(junk) is None, "a non-IPS file is reported as unreadable")
        check(C.ips_header_writes(Path(d) / "absent.ips") is None, "a missing patch file is None")


def test_a_patch_that_misses_the_header_yields_no_header_evidence():
    with tempfile.TemporaryDirectory() as d:
        p = ips(Path(d) / "late.ips", [(0x20, b"\x01\x02")])
        w = C.ips_header_writes(p)
    check(w == {}, "a patch that starts past byte 16 writes nothing into the header", f"got {w}")


def test_the_patch_directive_parser_reads_file_and_target_sha1():
    with tempfile.TemporaryDirectory() as d:
        m = Path(d) / "hires.txt"
        manifest(m, [("a.png", [index(1)])],
                 patches=[("mmm.ips", "ECF39EC5"), ("mmm.ips", "20800599")])
        got = C.patches(m)
    check(got == [("mmm.ips", "ecf39ec5"), ("mmm.ips", "20800599")],
          "every <patch> line is read, sha1 lowercased", f"got {got}")


# --- (a) a foreign-namespace reference is refused ----------------------------


def test_a_patch_bearing_reference_against_a_chr_ram_recording_is_refused():
    with tempfile.TemporaryDirectory() as d:
        d = Path(d)
        # The Metroid shape (#225): the pack is built for a patched ROM whose
        # header byte 5 turns CHR RAM into 128 KB of CHR ROM, so it keys tiles
        # by bank index while our recording of the stock ROM keys by pattern.
        ref = d / "ref" / "hires.txt"
        manifest(ref, [("suit.png", [index(0xA55), index(0x14FB)]),
                       ("door.png", [index(0x800)])],
                 patches=[("mmm.ips", "ecf39ec5")])
        ips(d / "ref" / "mmm.ips", [(4, b"\x10\x10\x13")])
        rec = recorded(d / "rec" / "auto", [pattern(1), pattern(2)])

        rc, out, err = run([ref, rec])
    check(rc != 0, "a foreign-namespace reference exits non-zero", f"rc={rc}")
    check("0/" not in out and not out.strip(), "nothing is printed on stdout — no 0% table",
          f"stdout was {out[:200]!r}")
    for needle, why in [
        ("different namespaces", "names the cause"),
        ("CHR ROM bank index", "names the reference's key shape"),
        ("CHR RAM pattern", "names the recording's key shape"),
        ("ReadTileData", "cites the emulator rule that splits them"),
        ("empty by construction", "says why they cannot intersect"),
        ("mmm.ips", "names the patch it read"),
        ("offset 5 = 0x10", "quotes the header byte it actually read"),
        ("128 KB of CHR ROM", "decodes that byte"),
        ("What to do", "tells the artist what to do instead"),
    ]:
        check(needle in err, f"the refusal {why}", f"missing {needle!r} in {err[:400]!r}")


def test_a_foreign_namespace_is_refused_even_without_a_readable_patch():
    # The key shape is the evidence; the patch only explains it. A reference
    # that ships no patch we can read must still be refused, not measured.
    with tempfile.TemporaryDirectory() as d:
        d = Path(d)
        ref = d / "ref" / "hires.txt"
        manifest(ref, [("a.png", [index(1), index(2)])])
        rec = recorded(d / "rec" / "auto", [pattern(1)])
        rc, out, err = run([ref, rec])
    check(rc != 0, "no patch, still refused", f"rc={rc}")
    check("different namespaces" in err, "the refusal still names the cause")
    check("iNES header" not in err, "no header evidence is claimed when there is no patch")


def test_a_declared_patch_that_cannot_be_read_is_reported_as_such():
    with tempfile.TemporaryDirectory() as d:
        d = Path(d)
        ref = d / "ref" / "hires.txt"
        manifest(ref, [("a.png", [index(1)])], patches=[("gone.ips", "deadbeef")])
        rec = recorded(d / "rec" / "auto", [pattern(1)])
        rc, _out, err = run([ref, rec])
    check(rc != 0, "an unreadable patch does not turn the refusal off")
    check("could not read" in err, "the message says the patch was unreadable, and claims no bytes",
          err[:300])


def test_a_readable_patch_that_leaves_the_header_alone_is_not_called_unreadable():
    # The Zelda II "Revamp" shape (#231): a real patch we parse fine whose 267
    # records all start past byte 16. Saying "we could not read it" would be a
    # false claim about a file we just read.
    with tempfile.TemporaryDirectory() as d:
        d = Path(d)
        ref = d / "ref" / "hires.txt"
        manifest(ref, [("a.png", [index(1)])], patches=[("revamp.ips", "08fa60f2")])
        ips(d / "ref" / "revamp.ips", [(0x0DE4, b"\xEA" * 4), (0x1234, b"\x00" * 3)])
        rec = recorded(d / "rec" / "auto", [pattern(1)])
        rc, _out, err = run([ref, rec])
    check(rc != 0, "the refusal still happens")
    check("could not read" not in err, "it does not claim a patch it read was unreadable", err[:300])
    check("revamp.ips" in err and "does not rewrite the iNES header" in err,
          "it names the patch and says what it actually does", err[:300])


def test_the_mirror_case_is_refused_too():
    # A CHR RAM reference against a CHR ROM recording is the same defect.
    with tempfile.TemporaryDirectory() as d:
        d = Path(d)
        ref = d / "ref" / "hires.txt"
        manifest(ref, [("a.png", [pattern(1), pattern(2)])])
        rec = recorded(d / "rec" / "auto", [index(1), index(2)])
        rc, out, err = run([ref, rec])
    check(rc != 0 and "different namespaces" in err, "pattern reference vs index recording refused",
          f"rc={rc} err={err[:200]!r}")


def test_an_empty_side_is_refused_with_its_own_message():
    with tempfile.TemporaryDirectory() as d:
        d = Path(d)
        ref = d / "ref" / "hires.txt"
        manifest(ref, [("a.png", [pattern(1)])])
        empty = recorded(d / "rec" / "auto", [])
        rc, _out, err = run([ref, empty])
        check(rc != 0 and "no <tile> rules" in err, "an empty recording is refused, not 0%",
              f"rc={rc} err={err[:200]!r}")

        bare = d / "bare" / "hires.txt"
        manifest(bare, [])
        rc, _out, err = run([bare, recorded(d / "rec2" / "auto", [pattern(1)])])
        check(rc != 0 and "no <tile> rules" in err, "an empty reference is refused, not 0%",
              f"rc={rc} err={err[:200]!r}")


# --- (b) a normal reference still measures ----------------------------------


def test_a_normal_chr_ram_reference_measures_and_does_not_regress():
    with tempfile.TemporaryDirectory() as d:
        d = Path(d)
        ref = d / "ref" / "hires.txt"
        # 4 tiles on a sprite image, 4 on a background image, 2 on an unseen one.
        manifest(ref, [("hero.png", [pattern(n) for n in range(4)]),
                       ("wall.png", [pattern(n) for n in range(10, 14)]),
                       ("boss.png", [pattern(n) for n in range(90, 92)])])
        rec = recorded(d / "rec" / "auto",
                       [pattern(n) for n in (0, 1, 2, 10, 11)],
                       sprites=[pattern(n) for n in (0, 1, 2)])
        rc, out, err = run([ref, rec])
    check(rc == 0, "a same-namespace measurement still exits 0", f"rc={rc} err={err[:200]!r}")
    check(not err.strip(), "and prints no warning", err[:200])
    check("artist tileData on screen in some state: 5/10" in out,
          "the coverage number is the plain intersection", out[:300])
    check("| hero.png | sprite |" in out, "a majority-on-sheet image reads as sprite", out[:600])
    check("| wall.png | background |" in out, "an off-sheet image reads as background", out[:600])
    check("| boss.png | unseen |" in out, "an unreached image still reads as unseen", out[:600])


def test_a_chr_rom_reference_against_a_chr_rom_recording_measures():
    # The refusal is about a *mismatch*, never about indices as such.
    with tempfile.TemporaryDirectory() as d:
        d = Path(d)
        ref = d / "ref" / "hires.txt"
        manifest(ref, [("a.png", [index(n) for n in range(8)])])
        rec = recorded(d / "rec" / "auto", [index(n) for n in range(3)])
        rc, out, err = run([ref, rec])
    check(rc == 0, "an index/index comparison measures normally", f"rc={rc} err={err[:200]!r}")
    check("artist tileData on screen in some state: 3/8" in out, "and counts the real overlap",
          out[:300])


def test_a_patch_alone_does_not_refuse_a_measurable_reference():
    # <patch> is a guess, not evidence: plenty of patches leave the mapper
    # alone. Only a key shape the recording cannot produce refuses.
    with tempfile.TemporaryDirectory() as d:
        d = Path(d)
        ref = d / "ref" / "hires.txt"
        manifest(ref, [("a.png", [pattern(n) for n in range(4)])],
                 patches=[("tweak.ips", "abc123")])
        ips(d / "ref" / "tweak.ips", [(0x4000, b"\xEA\xEA")])
        rec = recorded(d / "rec" / "auto", [pattern(0), pattern(1)])
        rc, out, err = run([ref, rec])
    check(rc == 0, "a patch-bearing but same-namespace reference is measured, not refused",
          f"rc={rc} err={err[:200]!r}")
    check("artist tileData on screen in some state: 2/4" in out, "with the true number", out[:300])


# --- (c) a low-but-nonzero coverage is not a refusal -------------------------


def test_a_legitimately_low_coverage_is_reported_not_refused():
    with tempfile.TemporaryDirectory() as d:
        d = Path(d)
        ref = d / "ref" / "hires.txt"
        manifest(ref, [(f"img{i}.png", [pattern(i * 100 + n) for n in range(10)])
                       for i in range(10)])
        # One tile of one image, out of a hundred: 1%, and honest.
        rec = recorded(d / "rec" / "auto", [pattern(0)] + [pattern(9000 + n) for n in range(5)])
        rc, out, err = run([ref, rec])
    check(rc == 0, "1% coverage exits 0", f"rc={rc} err={err[:300]!r}")
    check("artist tileData on screen in some state: 1/100" in out, "and is reported as 1/100",
          out[:300])
    check("unseen     images   9" in out, "the nine untouched images read as unseen", out[:400])


def test_one_shared_tile_is_enough_to_measure():
    # The floor of "measurable" is a single key in a shape both sides use —
    # the refusal must not creep up into a coverage threshold.
    with tempfile.TemporaryDirectory() as d:
        d = Path(d)
        ref = d / "ref" / "hires.txt"
        manifest(ref, [("a.png", [pattern(n) for n in range(50)])])
        rec = recorded(d / "rec" / "auto", [pattern(0)])
        rc, out, err = run([ref, rec])
    check(rc == 0 and "in some state: 1/50" in out, "a single shared tile still measures",
          f"rc={rc} out={out[:200]!r}")


# --- the partial mismatch ---------------------------------------------------


def test_a_partly_foreign_reference_measures_but_says_what_is_unreachable():
    with tempfile.TemporaryDirectory() as d:
        d = Path(d)
        ref = d / "ref" / "hires.txt"
        manifest(ref, [("ram.png", [pattern(n) for n in range(4)]),
                       ("rom.png", [index(n) for n in range(6)])])
        rec = recorded(d / "rec" / "auto", [pattern(0), pattern(1)])
        rc, out, err = run([ref, rec])
    check(rc == 0, "a partial overlap still measures", f"rc={rc} err={err[:200]!r}")
    check("warning: 6/10 reference tileData are CHR ROM bank index" in err,
          "and warns how much of the reference the recording could never hold", err[:300])
    check("in some state: 2/10" in out, "the printed number is unchanged", out[:300])


# --- the <patch> caveat (#231) ----------------------------------------------
#
# A <patch> that survives the namespace check (its keys share the recording's
# shape) is not a refusal, but the pack is still built for a patched ROM. The
# tool must say so above the tables and on the summary line rather than letting
# the figures read as coverage — and must stay silent when there is no patch.


def test_a_patch_bearing_reference_that_shares_the_shape_warns_and_still_measures():
    # Zelda II / Revamp (#231): a CHR ROM reference whose keys are the same
    # shape as a CHR ROM recording's passes the namespace check, so the numbers
    # ship. They compare two builds, and the patch that causes it must be named.
    with tempfile.TemporaryDirectory() as d:
        d = Path(d)
        ref = d / "ref" / "hires.txt"
        manifest(ref, [("a.png", [index(n) for n in range(4)])],
                 patches=[("revamp.ips", "08fa60f2")])
        ips(d / "ref" / "revamp.ips", [(4, b"\x10\x10\x13")])
        rec = recorded(d / "rec" / "auto", [index(0), index(1)])
        rc, out, err = run([ref, rec])
    check(rc == 0, "a same-shape patch-bearing reference still measures", f"rc={rc}")
    check("warning:" in err and "revamp.ips" in err and "08fa60f2" in err,
          "the caveat names the patch and its target sha1", err[:400])
    check("built for a patched ROM" in err, "and says the reference targets a patched ROM")
    check("128 KB of CHR ROM" in err and "byte 5" in err,
          "and decodes the header byte the patch rewrites", err[:500])
    check("[caveat:" in out and "not coverage" in out,
          "the summary line carries the caveat, not a bare number", out[:400])
    check("artist tileData on screen in some state: 2/4" in out,
          "the measurement itself is unchanged", out[:400])


def test_a_patch_that_leaves_the_header_alone_does_not_claim_it_changed():
    # The Zelda II Revamp shape for real: a patch we read fine whose records all
    # start past the header. It is still a caveat, but claiming byte 5 moved (or
    # that the namespaces provably differ) would be a false statement.
    with tempfile.TemporaryDirectory() as d:
        d = Path(d)
        ref = d / "ref" / "hires.txt"
        manifest(ref, [("a.png", [index(n) for n in range(4)])],
                 patches=[("tweak.ips", "abc123")])
        ips(d / "ref" / "tweak.ips", [(0x40, b"\xEA\xEA")])
        rec = recorded(d / "rec" / "auto", [index(0)])
        rc, out, err = run([ref, rec])
    check(rc == 0, "it still measures", f"rc={rc}")
    check("does not rewrite the iNES header" in err and "byte 5 is unchanged" in err,
          "it says the header was left alone and byte 5 is unchanged", err[:400])
    check("provably" not in err, "it claims no namespace divergence it did not see", err[:400])
    check("[caveat:" in out, "the summary still carries the caveat", out[:400])


def test_a_reference_without_a_patch_gets_no_caveat():
    # The honesty half: the caveat must fire on <patch> alone, never on a normal
    # reference, or "this compares two builds" would be noise the artist learns
    # to ignore.
    with tempfile.TemporaryDirectory() as d:
        d = Path(d)
        ref = d / "ref" / "hires.txt"
        manifest(ref, [("a.png", [index(n) for n in range(4)])])
        rec = recorded(d / "rec" / "auto", [index(0)])
        rc, out, err = run([ref, rec])
    check(rc == 0, "a patch-less reference measures", f"rc={rc}")
    check(err.strip() == "", "and prints no warning at all", err[:300])
    check("[caveat:" not in out, "and its summary line is a bare measurement", out[:400])


def test_a_declared_patch_we_cannot_read_still_warns_before_measuring():
    with tempfile.TemporaryDirectory() as d:
        d = Path(d)
        ref = d / "ref" / "hires.txt"
        manifest(ref, [("a.png", [index(n) for n in range(4)])],
                 patches=[("gone.ips", "deadbeef")])
        rec = recorded(d / "rec" / "auto", [index(0)])
        rc, out, err = run([ref, rec])
    check(rc == 0, "an unreadable patch does not refuse when the shapes match", f"rc={rc}")
    check("gone.ips" in err and "could not be read" in err,
          "the caveat says the patch is unreadable", err[:400])
    check("[caveat:" in out, "the summary still carries the caveat", out[:400])


# --- the per-state table's labels -------------------------------------------


def test_the_state_label_is_the_folder_two_levels_above_auto():
    got = C.state_labels(["/runs/a/stage1-run/Contra/auto", "/runs/a/stage2-base/Contra/auto/"])
    check(got == ["stage1-run", "stage2-base"], "distinct states keep their short names", f"{got}")


def test_two_runs_of_the_same_state_get_distinct_labels():
    # Both packs are real and distinct; collapsing them onto one label lost a
    # row of the per-state table and made "only this state" wrong.
    got = C.state_labels(["/runs/golden-a/contra/stage2-base/Contra/auto",
                          "/runs/golden-b/by-stage/stage2-base/Contra/auto"])
    check(len(set(got)) == 2, "a repeated state name is disambiguated, not collapsed", f"{got}")
    check(all(g.endswith("stage2-base") for g in got), "and the state is still readable", f"{got}")


def test_two_same_named_states_both_reach_the_per_state_table():
    with tempfile.TemporaryDirectory() as d:
        d = Path(d)
        ref = d / "ref" / "hires.txt"
        manifest(ref, [("a.png", [pattern(n) for n in range(10)])])
        a = recorded(d / "runA" / "stage1" / "Rom" / "auto", [pattern(0), pattern(1)])
        b = recorded(d / "runB" / "stage1" / "Rom" / "auto", [pattern(2), pattern(3)])
        rc, out, err = run([ref, a, b])
    check(rc == 0, "both packs measure", f"rc={rc} err={err[:200]!r}")
    check("recorded: 2 packs" in out, "both packs are counted, not one", out[:300])
    rows = [l for l in out.splitlines() if l.startswith("| ") and "stage1" in l]
    check(len(rows) == 2, "the per-state table has a row for each", f"{rows}")
    check(all("| 2 | 2 |" in r for r in rows),
          "and each keeps the two tiles only it exhibited", f"{rows}")


def main():
    tests = [
        test_shape_follows_the_emulators_own_width_rule,
        test_the_ips_reader_recovers_the_header_bytes_a_patch_writes,
        test_the_ips_reader_finds_a_header_record_that_comes_after_a_body_record,
        test_the_ips_reader_handles_an_rle_record_and_rejects_a_non_ips,
        test_a_patch_that_misses_the_header_yields_no_header_evidence,
        test_the_patch_directive_parser_reads_file_and_target_sha1,
        test_a_patch_bearing_reference_against_a_chr_ram_recording_is_refused,
        test_a_foreign_namespace_is_refused_even_without_a_readable_patch,
        test_a_declared_patch_that_cannot_be_read_is_reported_as_such,
        test_a_readable_patch_that_leaves_the_header_alone_is_not_called_unreadable,
        test_the_mirror_case_is_refused_too,
        test_an_empty_side_is_refused_with_its_own_message,
        test_a_normal_chr_ram_reference_measures_and_does_not_regress,
        test_a_chr_rom_reference_against_a_chr_rom_recording_measures,
        test_a_patch_alone_does_not_refuse_a_measurable_reference,
        test_a_legitimately_low_coverage_is_reported_not_refused,
        test_one_shared_tile_is_enough_to_measure,
        test_a_partly_foreign_reference_measures_but_says_what_is_unreachable,
        test_a_patch_bearing_reference_that_shares_the_shape_warns_and_still_measures,
        test_a_patch_that_leaves_the_header_alone_does_not_claim_it_changed,
        test_a_reference_without_a_patch_gets_no_caveat,
        test_a_declared_patch_we_cannot_read_still_warns_before_measuring,
        test_the_state_label_is_the_folder_two_levels_above_auto,
        test_two_runs_of_the_same_state_get_distinct_labels,
        test_two_same_named_states_both_reach_the_per_state_table,
    ]
    for t in tests:
        t()
    print(f"\n{len(tests) - len(_FAILURES)}/{len(tests)} case group(s) passed")
    return 1 if _FAILURES else 0


if __name__ == "__main__":
    sys.exit(main())
