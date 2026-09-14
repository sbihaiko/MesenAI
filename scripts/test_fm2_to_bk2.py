"""Headless suite for fm2_to_bk2.py — the FCEUX fm2 -> BizHawk bk2 transcoder.

Everything here runs on hand-built movies and hand-built two-byte "ROMs", so
every answer is checkable by eye. What the suite pins down:

  * the button permutation, in **both** directions, on a frame where every one
    of the eight buttons is distinguishable from every other — the failure this
    tool exists to avoid is a movie that plays and then desyncs, and only a
    per-button check catches a transposed pair;
  * the console column, on a soft-reset frame and a hard-reset frame;
  * a port declared `0`, which is an empty field and contributes no column;
  * every refusal, asserting the message names the offending thing by name;
  * the ROM check's one trap: an fm2 checksum is the MD5 of the ROM *without*
    its 16-byte iNES header, so a ROM whose header-stripped MD5 matches must be
    accepted even though its whole-file MD5 does not;
  * the shape of the output zip — entry names, and no trailing pipe on an input
    log row;
  * the row offset — the fm2's power-on row is dropped, because our Core spends
    one poll before the first frame runs, and a one-for-one log plays every
    input one frame late. That is the desync this suite missed once, so it is
    checked in both directions and a dropped row holding input is a refusal.

Standard library only, no pytest, no emulator.

Run:  python3 scripts/test_fm2_to_bk2.py
"""

import base64
import hashlib
import sys
import tempfile
import zipfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import fm2_to_bk2 as M  # noqa: E402

_FAILURES = []


def check(cond, name, detail=""):
    if cond:
        print(f"ok   {name}")
    else:
        print(f"FAIL {name}: {detail}")
        _FAILURES.append(name)


# --- fixtures ---------------------------------------------------------------


def make_rom(path: Path, body: bytes = b"\x01\x02\x03\x04" * 64, header=True) -> bytes:
    """A fake ROM. `body` is the PRG/CHR image the fm2 checksum is taken over;
    the 16-byte iNES header in front of it is what a naive checker would
    wrongly include."""
    data = (b"NES\x1a" + b"\x02\x01" + bytes(10) if header else b"") + body
    path.write_bytes(data)
    return body


def checksum_of(body: bytes) -> str:
    return "base64:" + base64.b64encode(hashlib.md5(body).digest()).decode("ascii")


DEFAULT_HEADER = {
    "version": "3",
    "emuVersion": "20601",
    "rerecordCount": "1234",
    "palFlag": "0",
    "romFilename": r"C:\fceux 2.2.3\roms\Some Game (USA).nes",
    "guid": "DEADBEEF-0000-1111-2222-333344445555",
    "fourscore": "0",
    "port0": "1",
    "port1": "1",
    "port2": "0",
    "FDS": "0",
    "NewPPU": "0",
    "RAMInitOption": "0",
    "RAMInitSeed": "0",
}


def write_fm2(path: Path, body: bytes, frames, extra=None, drop=(), comments=("author tester",)):
    """An fm2 whose header matches `body`'s MD5, plus the given frame lines."""
    header = dict(DEFAULT_HEADER)
    header["romChecksum"] = checksum_of(body)
    header.update(extra or {})
    for key in drop:
        header.pop(key, None)
    lines = [f"{k} {v}" for k, v in header.items()]
    lines += [f"comment {c}" for c in comments]
    lines += list(frames)
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return path


def blank_frame(n=1):
    return ["|0|........|........||"] * n


def convert_ok(td: Path, frames, **kw):
    """Convert a movie built from `frames` and hand back (stats, rows).

    A blank power-on frame goes in front of `frames`, because the converter
    drops the fm2's first row (see "The power-on row is dropped" in the tool's
    docstring). With it, `rows[i]` is still `frames[i]` for every case below."""
    rom = td / "game.nes"
    body = make_rom(rom)
    fm2 = write_fm2(td / "m.fm2", body, blank_frame(1) + list(frames), **kw)
    out = td / "m.bk2"
    stats = M.convert(fm2, rom, out)
    with zipfile.ZipFile(out) as zf:
        log = zf.read("Input Log.txt").decode("utf-8").splitlines()
    return stats, [r for r in log if r.startswith("|")], out


def refused(td: Path, frames, needle, name, **kw):
    """Assert the conversion is refused and the message names the cause."""
    rom = td / "game.nes"
    body = make_rom(rom)
    fm2 = write_fm2(td / "m.fm2", body, frames, **kw)
    try:
        M.convert(fm2, rom, td / "m.bk2")
        check(False, name, "no refusal at all")
    except M.Fm2Error as e:
        check(needle in str(e), name, f"message was {str(e)!r}")


# --- cases ------------------------------------------------------------------


def test_the_permutation_is_derived_and_is_the_expected_one():
    check(M.PERMUTATION == (3, 2, 1, 0, 4, 5, 6, 7),
          "RLDUTSBA -> UDLRSsBA is source indices 3,2,1,0,4,5,6,7", str(M.PERMUTATION))
    check(M.CORE_LETTERS == "UDLRSsBA",
          "the letters are NesController::GetKeyNames()'s, lowercase s for select",
          M.CORE_LETTERS)
    check(M.CONSOLE_LETTERS == "RP",
          "and the console column is SystemActionManager::GetKeyNames()'s", M.CONSOLE_LETTERS)


def test_every_button_moves_to_its_own_column_and_back():
    """Eight one-button frames: each isolates a single button, so a transposed
    pair — the desync failure — cannot hide behind a coincidence."""
    with tempfile.TemporaryDirectory() as td:
        frames = []
        for i, name in enumerate(M.FM2_ORDER):
            field = "".join(M.FM2_ORDER[j][0] if j == i else "." for j in range(8))
            frames.append(f"|0|{field}|........||")
        stats, rows, _out = convert_ok(Path(td), frames)
        check(stats["frames"] == 8, "one row per frame line", str(stats["frames"]))
        wrong = []
        for i, name in enumerate(M.FM2_ORDER):
            cols = rows[i][1:].split("|")
            got = M.core_pressed(cols[1])
            if got != frozenset({name}):
                wrong.append(f"{name} -> {sorted(got)}")
            # And the second port, untouched, must stay empty.
            if M.core_pressed(cols[2]):
                wrong.append(f"{name} leaked into port1")
        check(not wrong, "each fm2 button lands on exactly its own core column", str(wrong))

        # The other direction, spelled out on the one frame where it is most
        # confusable: fm2 index 0 is Right, core index 0 is Up.
        field = "R......."
        out = M.convert_port(field, 1, 0, True)
        check(out == "...R....", "fm2 position 0 (Right) becomes core position 3", out)
        check(M.convert_port("...U....", 1, 0, True) == "U.......",
              "and fm2 position 3 (Up) becomes core position 0",
              M.convert_port("...U....", 1, 0, True))
        check(M.convert_port("....TS..", 1, 0, True) == "....Ss..",
              "sTart/Select keep their order and pick up the core's letters",
              M.convert_port("....TS..", 1, 0, True))
        check(M.convert_port("......BA", 1, 0, True) == "......BA",
              "B and A are already in place", M.convert_port("......BA", 1, 0, True))
        check(M.convert_port("RLDUTSBA", 1, 0, True) == "UDLRSsBA",
              "an all-pressed frame is the two orders side by side",
              M.convert_port("RLDUTSBA", 1, 0, True))


def test_a_press_is_any_character_that_is_not_a_dot_or_a_space():
    """The fm2 spec says the mnemonic letters are conventional; anything else
    non-blank is a press. FCEUX itself writes spaces in some tools' output."""
    check(M.convert_port("X   ....", 1, 0, True) == "...R....",
          "a non-mnemonic character is a press and a space is not",
          M.convert_port("X   ....", 1, 0, True))
    check(M.fm2_pressed("R L.....") == frozenset({"Right", "Down"}),
          "fm2_pressed reads position, not letter", str(sorted(M.fm2_pressed("R L....."))))


def test_a_port_declared_zero_is_an_empty_field_and_no_column():
    with tempfile.TemporaryDirectory() as td:
        stats, rows, _out = convert_ok(Path(td), blank_frame(3))
        check(stats["ports"] == [True, True, False],
              "port2 0 reads as a declared-but-absent port", str(stats["ports"]))
        cols = rows[0][1:].split("|")
        check(len(cols) == 3, "the row has a console column and two pad columns only", str(cols))
        check(cols == ["..", "........", "........"], "and the empty port contributes nothing",
              str(cols))
        # Eight dots where the header says the port is absent is a malformed
        # line, not a friendly synonym.
        refused(Path(td), ["|0|........|........|........|"],
                "declared `0`", "a non-empty field on a port declared 0 is refused")


def test_a_reset_frame_reaches_the_console_column():
    with tempfile.TemporaryDirectory() as td:
        frames = ["|0|........|........||",
                  "|1|........|........||",
                  "|2|........|........||",
                  "|3|........|........||"]
        stats, rows, _out = convert_ok(Path(td), frames)
        cols = [r[1:].split("|")[0] for r in rows]
        check(cols == ["..", "R.", ".P", "RP"],
              "fm2 command bit 1 is Reset, bit 2 is Power, in SystemActionManager order", str(cols))
        check(stats["framesWithCommand"] == 3, "and the three command frames are counted",
              str(stats["framesWithCommand"]))
        check(stats["resetFrames"] == [1, 2, 3], "by frame index", str(stats["resetFrames"]))
        check(stats["framesWithInput"] == 0, "a command is not a button press",
              str(stats["framesWithInput"]))


def test_a_command_bit_with_no_column_is_reported_not_swallowed():
    with tempfile.TemporaryDirectory() as td:
        stats, _rows, out = convert_ok(Path(td), ["|16|........|........||"])
        check(stats["extraCommands"] == {M.CMD_VS_COIN: [0]},
              "a VS coin has no column in our poll order and is recorded", str(stats["extraCommands"]))
        with zipfile.ZipFile(out) as zf:
            comments = zf.read("Comments.txt").decode("utf-8")
        check("bit 16" in comments, "and it is written into Comments.txt", comments[-300:])


def test_the_output_zip_holds_the_three_entries_and_no_trailing_pipe():
    with tempfile.TemporaryDirectory() as td:
        stats, rows, out = convert_ok(Path(td), blank_frame(5))
        with zipfile.ZipFile(out) as zf:
            names = zf.namelist()
            header = zf.read("Header.txt").decode("utf-8")
            log = zf.read("Input Log.txt").decode("utf-8").splitlines()
        check(sorted(names) == ["Comments.txt", "Header.txt", "Input Log.txt"],
              "the zip holds Header.txt, Input Log.txt and the provenance record", str(names))
        check(log[0].startswith("LogKey:"), "the input log opens with a LogKey: line", log[0])
        check(not log[0].startswith("|"),
              "which our Core skips, because it does not start with a pipe", log[0])
        check(not any(r.endswith("|") for r in rows),
              "no row ends with a pipe — StringUtilities::Split would make it a fourth column",
              rows[0])
        check("Platform NES" in header, "Header.txt names the platform BizHawkMovie::Play reads",
              header)
        check("StartsFromSavestate False" in header, "and says it starts from power-on", header)
        check("Author tester" in header, "the author comes off the fm2's `comment author` line",
              header)
        check("GameName Some Game (USA)" in header,
              "and the game name off the romFilename's basename", header)
        rom_sha1 = hashlib.sha1((Path(td) / "game.nes").read_bytes()).hexdigest().upper()
        check(f"SHA1 {rom_sha1}" in header,
              "SHA1 is the ROM's *whole-file* hash, header included", header)
        check(stats["frames"] == len(rows) == 5, "one row per frame", f"{stats['frames']}/{len(rows)}")


def test_comments_txt_carries_what_the_conversion_could_not():
    with tempfile.TemporaryDirectory() as td:
        _stats, _rows, out = convert_ok(Path(td), blank_frame(2), extra={"NewPPU": "1",
                                                                        "RAMInitOption": "2",
                                                                        "RAMInitSeed": "99"})
        with zipfile.ZipFile(out) as zf:
            comments = zf.read("Comments.txt").decode("utf-8")
        missing = [k for k in M.SYNC_KEYS if k not in comments]
        check(not missing, "every sync-relevant header key is recorded", str(missing))
        check("NewPPU 1" in comments and "RAMInitSeed 99" in comments,
              "with the value the fm2 carried, not a default", comments)
        check(DEFAULT_HEADER["guid"] in comments, "so is the guid", comments)
        check("m.fm2" in comments, "and the source file name", comments)


def test_pal_is_reported_but_not_refused():
    with tempfile.TemporaryDirectory() as td:
        stats, _rows, out = convert_ok(Path(td), blank_frame(2), extra={"palFlag": "1"})
        check(stats["pal"], "palFlag 1 is carried in the statistics", str(stats["pal"]))
        with zipfile.ZipFile(out) as zf:
            comments = zf.read("Comments.txt").decode("utf-8")
        check("palFlag 1" in comments and "PAL" in comments,
              "and written into the output prominently — the caller sets the region by hand",
              comments)


def test_the_rom_check_hashes_the_post_header_bytes():
    """The trap. An fm2 romChecksum is the MD5 FCEUX computes over the PRG/CHR
    image, *not* over the file — a checker that hashed the whole file would
    reject every correct ROM. The fixture makes the two hashes differ by
    construction."""
    with tempfile.TemporaryDirectory() as td:
        rom = Path(td) / "game.nes"
        body = make_rom(rom)
        whole = hashlib.md5(rom.read_bytes()).hexdigest()
        stripped = hashlib.md5(body).hexdigest()
        check(whole != stripped, "the fixture's two MD5s really do differ", f"{whole} {stripped}")
        sha1, md5, cut = M.rom_hashes(rom)
        check(md5 == stripped, "rom_hashes hashes the bytes after the 16-byte iNES header", md5)
        check(cut == 16, "and says so", str(cut))
        check(sha1 == hashlib.sha1(rom.read_bytes()).hexdigest(),
              "while the SHA1 it reports is the whole file's", sha1)
        # And the end-to-end path accepts it.
        fm2 = write_fm2(Path(td) / "m.fm2", body, blank_frame(2))
        stats = M.convert(fm2, rom, Path(td) / "m.bk2")
        check(stats["frames"] == 1, "a ROM matching post-header is accepted end to end",
              str(stats))
        # A headerless ROM (the image itself) hashes the same way.
        bare = Path(td) / "bare.nes"
        make_rom(bare, body, header=False)
        _s, md5b, cutb = M.rom_hashes(bare)
        check(md5b == stripped and cutb == 0,
              "a ROM with no iNES header is hashed whole, which is the same image", md5b)


def test_a_wrong_rom_is_refused_by_name():
    with tempfile.TemporaryDirectory() as td:
        rom = Path(td) / "game.nes"
        make_rom(rom)
        fm2 = write_fm2(Path(td) / "m.fm2", b"a different image entirely", blank_frame(1))
        try:
            M.convert(fm2, rom, Path(td) / "m.bk2")
            check(False, "a ROM whose checksum does not match is refused")
        except M.Fm2Error as e:
            msg = str(e)
            check("romChecksum mismatch" in msg and "iNES header stripped" in msg,
                  "a ROM whose checksum does not match is refused, saying which hash was compared",
                  msg)


def test_each_unconvertible_header_is_refused_by_name():
    with tempfile.TemporaryDirectory() as td:
        t = Path(td)
        refused(t, blank_frame(1), "savestate", "a movie starting from a savestate is refused",
                extra={"savestate": "0102ff"})
        refused(t, blank_frame(1), "FDS 1", "an FDS movie is refused", extra={"FDS": "1"})
        refused(t, blank_frame(1), "binary 1", "a binary input log is refused", extra={"binary": "1"})
        refused(t, blank_frame(1), "fourscore 1", "a fourscore movie is refused",
                extra={"fourscore": "1"})
        refused(t, blank_frame(1), "romChecksum", "an fm2 with no romChecksum is refused",
                drop=("romChecksum",))


def test_a_malformed_frame_line_is_refused_by_name():
    with tempfile.TemporaryDirectory() as td:
        t = Path(td)
        refused(t, ["|0|........||"], "field(s) between the pipes",
                "a line with too few fields is refused")
        refused(t, ["|0|........|........|||"], "field(s) between the pipes",
                "a line with too many fields is refused")
        refused(t, ["|0|.......|........||"], "7 character(s) wide",
                "a short gamepad field is refused, naming its width")
        refused(t, ["|0|.........|........||"], "9 character(s) wide",
                "so is a long one")
        refused(t, ["|x|........|........||"], "not a decimal number",
                "a non-numeric command field is refused")
        refused(t, ["|0|........|........|"], "field(s) between the pipes",
                "a line missing the empty port2 field is refused")
        # A line with no leading pipe is, by the fm2 grammar, still a header
        # line — the input log starts at the *first* line beginning with `|`.
        # So it never reaches the frame parser, and what the file-level refusal
        # names is the consequence: this fm2 has no frames.
        refused(t, ["0|........|........||"], "no frame lines at all",
                "a line with no leading pipe is a header line, so the movie reads as frameless")
        # The frame parser's own guard, which the API can still hit.
        try:
            M.split_frame("0|..|..|", 7, 3)
            check(False, "split_frame refuses a line with no leading pipe")
        except M.Fm2Error as e:
            check("does not start with `|`" in str(e),
                  "split_frame refuses a line with no leading pipe", str(e))


def test_a_bad_checksum_encoding_is_refused_by_name():
    try:
        M.decode_rom_checksum("base64:not!valid!")
        check(False, "an unparseable romChecksum is refused")
    except M.Fm2Error as e:
        check("base64" in str(e), "an unparseable romChecksum is refused", str(e))
    try:
        M.decode_rom_checksum("base64:AAAA")
        check(False, "a romChecksum of the wrong length is refused")
    except M.Fm2Error as e:
        check("not the 16" in str(e), "a romChecksum of the wrong length is refused", str(e))
    hexed = "d3f453931146e95b04a31647de80fdab"
    check(M.decode_rom_checksum(hexed) == hexed, "a bare hex checksum is accepted too")


def test_verify_round_trips_and_main_exits_zero():
    with tempfile.TemporaryDirectory() as td:
        t = Path(td)
        rom = t / "game.nes"
        body = make_rom(rom)
        frames = blank_frame(1) + ["|0|RLDUTSBA|........||",
                                   "|1|....T...|......BA||"] + blank_frame(3)
        fm2 = write_fm2(t / "m.fm2", body, frames)
        rc = M.main([str(fm2), "--rom", str(rom), "--verify", "--quiet"])
        check(rc == 0, "--verify exits 0 on a clean conversion", str(rc))
        check(not (t / "m.bk2").exists(), "and writes no output file")
        out = t / "out.bk2"
        rc = M.main([str(fm2), "--rom", str(rom), "-o", str(out), "--quiet"])
        check(rc == 0 and out.is_file(), "a real conversion exits 0 and writes the bk2", str(rc))
        stats = {"ports": [True, True, False]}
        res = M.round_trip(fm2, out, stats["ports"])
        check(res["mismatches"] == [], "the round trip finds no mismatch", str(res["mismatches"]))
        check(res["frames"] == 5, "over every frame", str(res["frames"]))
        check(res["trailingPipe"] is False, "and no row carries a trailing pipe")


def test_round_trip_catches_a_deliberately_broken_permutation():
    """The suite's own safety net: if round_trip cannot see a wrong column, its
    passing above proves nothing."""
    with tempfile.TemporaryDirectory() as td:
        t = Path(td)
        rom = t / "game.nes"
        body = make_rom(rom)
        fm2 = write_fm2(t / "m.fm2", body, blank_frame(1) + ["|0|R.......|........||"])
        out = t / "bad.bk2"
        with zipfile.ZipFile(out, "w") as zf:
            zf.writestr("Header.txt", "Platform NES\n")
            # Right ended up in the Up column: the classic transposition.
            zf.writestr("Input Log.txt", "LogKey:#Reset|Power\n|..|U.......|........\n")
        res = M.round_trip(fm2, out, [True, True, False])
        check(len(res["mismatches"]) == 1, "a swapped button is caught", str(res["mismatches"]))


def test_the_real_zelda_movie_converts_when_it_is_present():
    """Not a fixture: the actual TASVideos publication, if it has been fetched.
    Skipped silently when it has not, so the suite stays runnable anywhere."""
    root = Path(__file__).resolve().parent.parent
    fm2 = root / ".cache" / "tas" / "chatterbox-legendofzelda-allitems.fm2"
    rom = root / "roms" / "Zelda.nes"
    if not fm2.is_file() or not rom.is_file():
        print("ok   (skipped) the real Zelda movie is not fetched here")
        return
    _sha1, md5, cut = M.rom_hashes(rom)
    check(cut == 16 and md5 == "d3f453931146e95b04a31647de80fdab",
          "roms/Zelda.nes hashes to the MD5 the fm2 names, header stripped", md5)
    with tempfile.TemporaryDirectory() as td:
        stats = M.convert(fm2, rom, Path(td) / "z.bk2")
        check(stats["frames"] == 114912,
              "all 114913 frames convert, less the dropped power-on row", str(stats["frames"]))
        res = M.round_trip(fm2, Path(td) / "z.bk2", stats["ports"])
        check(res["mismatches"] == [], "and the whole movie round-trips",
              str(res["mismatches"][:3]))


def test_the_power_on_row_is_dropped_and_nothing_else_is():
    """The desync this suite exists to catch, and the one it missed once.

    Converting the fm2's rows one-for-one plays every input exactly one frame
    late on our Core: `Emulator::LoadRom` polls once after the movie's
    PowerCycle, before the first frame runs, so poll counter 0 is spent before
    the game can act on it. Measured against the real Zelda publication, the
    one-for-one movie walks Link into the first cave but never picks up the
    sword, and the run is dead inside a minute; dropping the first row alone
    keeps it in sync past 600 emulated seconds."""
    check(M.FM2_POWER_ON_ROWS == 1, "exactly one row is dropped", str(M.FM2_POWER_ON_ROWS))
    with tempfile.TemporaryDirectory() as td:
        t = Path(td)
        rom = t / "game.nes"
        body = make_rom(rom)
        # Four distinguishable frames behind a blank power-on row. What must
        # come out is frames 1..4, in order, with frame 0 gone.
        frames = ["|0|........|........||",
                  "|0|R.......|........||",
                  "|0|.L......|........||",
                  "|0|..D.....|........||",
                  "|0|...U....|........||"]
        fm2 = write_fm2(t / "m.fm2", body, frames)
        out = t / "m.bk2"
        stats = M.convert(fm2, rom, out)
        check(stats["frames"] == 4 and stats["droppedRows"] == 1,
              "five fm2 frames become four input log rows",
              f"{stats['frames']} rows, {stats['droppedRows']} dropped")
        with zipfile.ZipFile(out) as zf:
            rows = [r for r in zf.read("Input Log.txt").decode("utf-8").splitlines()
                    if r.startswith("|")]
        got = [sorted(M.core_pressed(r[1:].split("|")[1])) for r in rows]
        check(got == [["Right"], ["Left"], ["Down"], ["Up"]],
              "row 0 is fm2 frame 1, not fm2 frame 0", str(got))
        res = M.round_trip(fm2, out, stats["ports"])
        check(res["mismatches"] == [], "and the round trip agrees on the offset",
              str(res["mismatches"]))
        # The round trip is what would catch a regression, so prove it can:
        # re-check the same file against the wrong offset.
        for wrong in (0, 2):
            res = M.round_trip(fm2, out, stats["ports"], dropped=wrong)
            check(res["mismatches"] != [],
                  f"a conversion that dropped {wrong} row(s) is caught by the round trip",
                  str(res["mismatches"]))


def test_a_power_on_row_that_holds_input_is_refused_not_silently_lost():
    with tempfile.TemporaryDirectory() as td:
        t = Path(td)
        refused(t, ["|0|....T...|........||"] + blank_frame(2), "Start",
                "a first frame holding a button is refused, naming the button")
        refused(t, ["|1|........|........||"] + blank_frame(2), "command bits 1",
                "a first frame carrying a reset is refused too")
        refused(t, blank_frame(1), "no input left to play",
                "an fm2 whose only frame is the power-on row has nothing to convert")


def main():
    tests = [
        test_the_permutation_is_derived_and_is_the_expected_one,
        test_every_button_moves_to_its_own_column_and_back,
        test_a_press_is_any_character_that_is_not_a_dot_or_a_space,
        test_a_port_declared_zero_is_an_empty_field_and_no_column,
        test_a_reset_frame_reaches_the_console_column,
        test_a_command_bit_with_no_column_is_reported_not_swallowed,
        test_the_output_zip_holds_the_three_entries_and_no_trailing_pipe,
        test_comments_txt_carries_what_the_conversion_could_not,
        test_pal_is_reported_but_not_refused,
        test_the_rom_check_hashes_the_post_header_bytes,
        test_a_wrong_rom_is_refused_by_name,
        test_each_unconvertible_header_is_refused_by_name,
        test_a_malformed_frame_line_is_refused_by_name,
        test_a_bad_checksum_encoding_is_refused_by_name,
        test_verify_round_trips_and_main_exits_zero,
        test_round_trip_catches_a_deliberately_broken_permutation,
        test_the_power_on_row_is_dropped_and_nothing_else_is,
        test_a_power_on_row_that_holds_input_is_refused_not_silently_lost,
        test_the_real_zelda_movie_converts_when_it_is_present,
    ]
    for t in tests:
        t()
    print(f"\n{len(tests) - len(_FAILURES)}/{len(tests)} case group(s) passed")
    return 1 if _FAILURES else 0


if __name__ == "__main__":
    sys.exit(main())
