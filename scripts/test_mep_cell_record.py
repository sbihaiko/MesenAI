#!/usr/bin/env python3
"""ADR-0236 (F14.11, #499) `<bgCellRecord>`: the grammar `mep_cell_record`
mirrors out of `Core/NES/HdPacks/HdCaptureCellGuard.h`, what `mep_lint` says
about a line HdPackLoader would refuse or mis-bind, and the way `mep_carry`
keeps the line tied to the `<background>` it belongs to across a rebuild.

The failure this file exists to catch is drift: a pack that passes lint and
then loses its guard in the emulator, or a rebuild that carries the record
onto a `<background>` it was never written for. Both are silent in a running
emulator - the symptom is a frozen HUD, which is what #499 was.

Usage: python3 scripts/test_mep_cell_record.py
"""
from __future__ import annotations

import sys
import tempfile
from pathlib import Path

SCRIPTS = Path(__file__).resolve().parent
sys.path.insert(0, str(SCRIPTS))
import mep_carry  # noqa: E402
import mep_cell_record  # noqa: E402
import mep_lint  # noqa: E402

FAILURES: list[str] = []
PNG_1x1 = bytes.fromhex(
    "89504e470d0a1a0a0000000d49484452000000010000000108060000001f15c489"
    "0000000d49444154789c63f8ffff3f0005fe02fea72d3e4a0000000049454e44ae426082"
)
KEY_A = "I00000042:1B1A1918"
KEY_B = "D" + "00112233445566778899AABBCCDDEEFF" + ":1B1A1918"


def check(cond, msg, extra=""):
    print(("PASS: " if cond else "FAIL: ") + msg + (f" -- {extra}" if extra and not cond else ""))
    if not cond:
        FAILURES.append(msg)


def line(keys, cells=None, tag=True):
    """The line `HdCellKeyRecord::ToString` writes: dictionary, `;`, then 960
    cell indices each `cell_index_width(len(keys))` hex digits wide."""
    cells = cells if cells is not None else [0] * mep_cell_record.CELL_COUNT
    width = mep_cell_record.cell_index_width(len(keys))
    body = "|".join(keys) + ";" + "".join(f"{c:0{width}X}" for c in cells)
    return (mep_cell_record.TAG if tag else "") + body


# ---------------------------------------------------------------- grammar --
def grammar_tests():
    """`mep_cell_record.parse` accepts exactly what `HdCellKeyRecord::Parse`
    accepts. The width rule is the sharp one: a reader that disagreed with the
    writer about it would not fail loudly, it would read every cell one off and
    gate the wrong screen - #499 again with no crash to point at."""
    check(mep_cell_record.cell_index_width(2) == 2, "a 2-key dictionary indexes cells in 1 byte")
    check(mep_cell_record.cell_index_width(256) == 2, "256 keys still fit 2 hex digits")
    check(mep_cell_record.cell_index_width(257) == 3, "257 keys need 3 hex digits")
    check(mep_cell_record.cell_index_width(4096) == 3, "4096 keys still fit 3")
    check(mep_cell_record.cell_index_width(4097) == 4, "4097 keys need 4")

    text = line([KEY_A, "N"])
    keys, cells = mep_cell_record.parse(text)
    check(keys == [KEY_A, "N"] and len(cells) == mep_cell_record.CELL_COUNT * 2,
          "a well-formed line parses to its dictionary and a 960-cell plane",
          f"{keys!r} {len(cells)}")
    check(mep_cell_record.parse(text[len(mep_cell_record.TAG):])[0] == keys,
          "the loader's own payload form (no tag) parses identically")

    text = line([KEY_A, KEY_B, "N"], cells=[2] + [1] * 959)
    keys, cells = mep_cell_record.parse(text)
    check(len(keys) == 3 and cells[:2] == "02", "the cell plane is read at the dictionary's width", cells[:4])
    check(mep_cell_record.parse(line([KEY_B]))[0] == [KEY_B],
          "a CHR RAM key (D + 32 hex digits) is a key")

    for bad, why in (
        (mep_cell_record.TAG + "N" + "0" * 1920, "a line with no ';'"),
        (mep_cell_record.TAG + ";" + "0" * 3840, "an empty dictionary"),
        (mep_cell_record.TAG + "I0000004:1B1A1918;" + "0" * 1920, "a short key body"),
        (mep_cell_record.TAG + "Q00000042:1B1A1918;" + "0" * 1920, "an unknown key kind"),
        (mep_cell_record.TAG + KEY_A + ";" + "0" * 1918, "a short cell plane"),
        (mep_cell_record.TAG + "|".join(f"I{i:08X}:1B1A1918" for i in range(257)) + ";" + "0" * 1920,
         "a plane at the wrong width for its dictionary"),
    ):
        try:
            mep_cell_record.parse(bad)
            check(False, f"parse refuses {why}")
        except ValueError as err:
            check(True, f"parse refuses {why} ({err})")

    try:
        mep_cell_record.parse(line([KEY_A, "N"], cells=[2] + [0] * 959))
        check(False, "parse refuses a cell that names a key past the dictionary")
    except ValueError as err:
        check("names key 2 of 2" in str(err), "parse refuses a cell past the dictionary, naming it", str(err))

    # The message a pack author sees has to be the one the loader logs, or a
    # lint error and an emulator log cannot be lined up at all.
    check(mep_cell_record.is_tag_line(line([KEY_A])), "the tag opener is recognized")
    check(not mep_cell_record.is_tag_line("<background>backgrounds/screen001.png,1"),
          "a <background> line is not a record")


# ------------------------------------------------------------------- lint --
def _lint(hires: str) -> mep_lint.Report:
    with tempfile.TemporaryDirectory() as td:
        root = Path(td)
        (root / "textures").mkdir()
        (root / "textures" / "hires.txt").write_text(hires)
        (root / "textures" / "tiles.png").write_bytes(PNG_1x1)
        (root / "textures" / "backgrounds").mkdir()
        (root / "textures" / "backgrounds" / "screen001.png").write_bytes(PNG_1x1)
        rep = mep_lint.Report()
        mep_lint.lint_nes_hires(mep_lint.Source(root), "textures/hires.txt", rep)
        return rep


def _msgs(rep, level=None):
    return [m for lv, _, m in rep.items if level is None or lv == level]


BG = "<background>backgrounds/screen001.png,1,0,0,20"


def lint_tests():
    base = "<ver>106\n<scale>1\n<img>tiles.png\n"

    rep = _lint(base + BG + "\n" + line([KEY_A, "N"]) + "\n")
    check(not any("unknown tag" in m for m in _msgs(rep)),
          "the record is not an unknown tag", str(_msgs(rep)))
    check(not rep.errors, "a record under its <background> is not an error", str(_msgs(rep, "error")))

    rep = _lint(base)
    check(not any("bgCellRecord" in m for m in _msgs(rep)),
          "its absence is never reported - every pack written before the tag has none (ADR-0236 §3)",
          str(_msgs(rep)))

    # Above the <background> instead of under it: the loader's `_lastBackground`
    # is set by the <background> line, so a record *before* one binds to nothing
    # and is dropped - and the capture then draws on every frame its probes
    # match, which is #499 itself.
    rep = _lint(base + line([KEY_A, "N"]) + "\n" + BG + "\n")
    check(any("bgCellRecord" in m and "directly under" in m for m in _msgs(rep, "error")),
          "a record that does not follow a <background> is an error", str(_msgs(rep, "error")))

    # A comment in between: HdPackLoader does not skip `#` lines, it only
    # resets on every line it reads, so the binding is broken there too.
    rep = _lint(base + BG + "\n# the frame it was captured from\n" + line([KEY_A, "N"]) + "\n")
    check(any("directly under" in m for m in _msgs(rep, "error")),
          "a comment between the two breaks the binding, the way the loader reads it",
          str(_msgs(rep, "error")))

    rep = _lint(base + BG + "\n" + mep_cell_record.TAG + KEY_A + ";" + "0" * 1918 + "\n")
    errs = _msgs(rep, "error")
    check(any("invalid <bgCellRecord>" in m and "expected" in m for m in errs),
          "a malformed record is an error quoting the reader's own reason", str(errs))

    check("bgCellRecord" in mep_lint.NES_TAGS and "bgCellRecord" not in mep_lint.GBSMS_TAGS,
          "the tag is NES-only: in NES_TAGS, not in GBSMS_TAGS")


# ------------------------------------------------------------------ carry --
def _carry(body: list, with_png: bool) -> tuple[list, str]:
    with tempfile.TemporaryDirectory() as td:
        root = Path(td)
        (root / "auto" / "textures" / "backgrounds").mkdir(parents=True)
        if with_png:
            (root / "auto" / "textures" / "backgrounds" / "screen001.png").write_bytes(PNG_1x1)
        (root / "textures").mkdir()
        import io
        from contextlib import redirect_stdout
        buf = io.StringIO()
        with redirect_stdout(buf):
            out = mep_carry.carry_backgrounds(root, root / "textures", body)
        return out, buf.getvalue()


def carry_tests():
    rec = line([KEY_A, "N"])

    out, _ = _carry([BG, rec, "<tile>0,1,2"], with_png=True)
    check(out == [BG, rec, "<tile>0,1,2"],
          "the record is carried verbatim, directly under the <background> it was written for", str(out))

    # The artist deleted the PNG: #344's way out of a capture. The record has to
    # go with the line, or the rebuild hands it to whichever <background> ends
    # up above it - a guard for the wrong screen, silently.
    out, log = _carry([BG, rec, "<tile>0,1,2"], with_png=False)
    check(out == ["<tile>0,1,2"], "a retired capture takes its record with it", str(out))

    out, log = _carry([rec, BG, "<tile>0,1,2"], with_png=True)
    check(out == [BG, "<tile>0,1,2"], "an orphan record is dropped, not carried", str(out))
    check("dropped 1 <bgCellRecord> line(s)" in log,
          "the build says it dropped a record, once, in the warning the artist skims", log)


def main() -> int:
    grammar_tests()
    lint_tests()
    carry_tests()
    print("ALL PASS" if not FAILURES else f"{len(FAILURES)} FAILURE(S)")
    return 1 if FAILURES else 0


if __name__ == "__main__":
    sys.exit(main())
