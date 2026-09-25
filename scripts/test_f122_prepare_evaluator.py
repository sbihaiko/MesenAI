#!/usr/bin/env python3
"""Regression test for the 2026-09-19 Sonnet sweep's "coverage-count mismatch"
finding (`docs/validation/f12.2-sonnet-sweep-2026-09-19.md`) and for #494.

The second one is a frame the *recorder* had already claimed: a pack's
`textures/backgrounds/screenNNN.png` is an opaque picture of the whole screen
drawn at priority 20, i.e. after the `<tile>` layer (ADR-0050, ADR-0156), so on
a second it matches, every `<tile>` rule under it is invisible and no painted
cell can ever reach the game. `choose_frame` scores a candidate by *counting
probe colours* found on its frame, and that count is fooled: the probe paints
each rule `(255, g, b)` and upscaled art carries pixels that match those by
coincidence - which the capture's own pixels then hide. Tetris 2's F14.2
re-score is the measured case (its 40 s frame is `screen002.png`, its chosen
cell (14,18) is under it, and painting it changed no pixel).

The sweep's index for Contra said "the copy table explains 692/930 of the
frame's 8x8 blocks" while the actual `tilemap-copy/Contra*.txt` held 90 rows
(3 columns of a 32-column nametable); Mike Tyson's Punch-Out!! claimed
"793/960" from 32 rows (one row); Ninja Gaiden claimed "828/960" from 30 rows,
every one of them column 0. All three numbers were true and all three were
misleading: `moment_agreement`'s "blocks explained" matches rendered *pixel
designs*, which repeat across a screen (a brick, the blank tile) far more
often than the dump has rows for, so a near-empty dump can still "explain"
most of the frame.

Neither fix touches the native emulator or `mep_build.py` - `f122_prepare_
evaluator.py` is sweep/dev tooling, and both are entirely inside it:
`dump_coverage()` counts the dump's own rows against the frame's fixed
`FULL_GRID_CELLS`, `one_moment()` gates on it as a third, independent leg, and
the generated `index/<game>.md` states both numbers so a reader is never
pointed at the wrong one again; for #494 the candidate probe repaints each
recorded screen flat, and a candidate whose frame one of them owns is dropped
with the screen's name instead of being ranked on a coincidence."""

import json
import shutil
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import f122_prepare_evaluator as f122  # noqa: E402

FAILED = 0


def ok(msg):
    print(f"PASS: {msg}")


def fail(msg):
    global FAILED
    FAILED = 1
    print(f"FAIL: {msg}")


# A one-tile background design (a solid, unmistakable colour once decoded by
# `tile_pixels`) and a bare NES palette word - shape only matters for this
# file, not what colour it draws as.
SOLID_TILE = "FF" * 8 + "00" * 8
PAL = "0F162A30"


def _row(col, row, tile=SOLID_TILE, palette=PAL):
    return f"{col},{row}\t" + json.dumps({"tile": tile, "palette": palette})


def dump_coverage_tests(tmp):
    # The measured Contra case: 3 columns x 30 rows of a 32x30 grid = 90 rows,
    # 90/960 = 9.4%, nowhere near the pixel-match line the sweep read as the
    # real coverage.
    table = tmp / "contra-shaped.txt"
    rows = [_row(col, row) for col in range(3) for row in range(30)]
    table.write_text("\n".join(rows) + "\n")
    cells, coverage = f122.dump_coverage(table)
    if cells == 90 and coverage == round(90 / 960, 3):
        ok("dump_coverage counts the Contra-shaped 3x30 dump as 90/960")
    else:
        fail(f"dump_coverage on a 90-row table returned {cells}, {coverage}")

    # The measured Ninja Gaiden case: 1 column x 30 rows = 30 rows, the
    # column-0-only truncation this same sweep found.
    table = tmp / "ninja-gaiden-shaped.txt"
    rows = [_row(0, row) for row in range(30)]
    table.write_text("\n".join(rows) + "\n")
    cells, coverage = f122.dump_coverage(table)
    if cells == 30 and coverage == round(30 / 960, 3):
        ok("dump_coverage counts the Ninja-Gaiden-shaped column-0 dump as 30/960")
    else:
        fail(f"dump_coverage on a 30-row column-0 table returned {cells}, {coverage}")

    # A full 32x30 nametable dump, the shape every non-scrolling game in the
    # sweep got.
    table = tmp / "full.txt"
    rows = [_row(col, row) for col in range(32) for row in range(30)]
    table.write_text("\n".join(rows) + "\n")
    cells, coverage = f122.dump_coverage(table)
    if cells == 960 and coverage == 1.0:
        ok("dump_coverage counts a full 32x30 dump as 960/960 (1.0)")
    else:
        fail(f"dump_coverage on a full 960-row table returned {cells}, {coverage}")

    if f122.FULL_GRID_CELLS == 960:
        ok("FULL_GRID_CELLS is the 256x240 screen in 8x8 cells (32x30)")
    else:
        fail(f"FULL_GRID_CELLS is {f122.FULL_GRID_CELLS}, expected 960")

    # #421: the walk now covers all four nametables and keeps what the frame
    # drew, and a scanline fetches 33 columns for the fine-X shift, so an
    # unscrolled frame names nametable 0 (960 cells) plus column 0 of nametable
    # 1 (30 cells) - measured on Bubble Bobble, Gauntlet and Tetris 2. The share
    # handed to the evaluator is still "of the screen", so it stops at 1.0.
    table = tmp / "fetched-33-columns.txt"
    rows = [_row(col, row) for col in range(33) for row in range(30)]
    table.write_text("\n".join(rows) + "\n")
    cells, coverage = f122.dump_coverage(table)
    if cells == 990 and coverage == 1.0:
        ok("dump_coverage caps a 33-column (fine-X fetch) table at 1.0 of the screen")
    else:
        fail(f"dump_coverage on a 990-row table returned {cells}, {coverage}")


def one_moment_gate_tests():
    # Exactly the shape the sweep's own numbers had for Contra: a high
    # pixel-match "explained" ratio (repeats) and plenty of non-blank keys,
    # but a dump that names under a tenth of the frame. Before this fix,
    # `one_moment` (inlined at the call site) only looked at the first two and
    # would have shipped this frame as a clean pass.
    inflated = {"explained": 0.744, "keys_on_frame": 45, "keys_in_table": 49}
    if not f122.one_moment(inflated, coverage=90 / 960):
        ok("one_moment rejects a high pixel-match score backed by a 9% dump (Contra shape)")
    else:
        fail("one_moment passed a 9%-coverage dump on pixel-match numbers alone")

    # Ninja Gaiden's shape: explained 828/960 = 0.8625, still >= MOMENT_BLOCKS.
    inflated_worse = {"explained": 0.8625, "keys_on_frame": 8, "keys_in_table": 8}
    if not f122.one_moment(inflated_worse, coverage=30 / 960):
        ok("one_moment rejects a 86%-explained frame backed by a 3% (column-0) dump (Ninja Gaiden shape)")
    else:
        fail("one_moment passed a 3%-coverage dump on pixel-match numbers alone")

    # A genuine full-coverage pass still passes all three legs.
    full = {"explained": 0.90, "keys_on_frame": 20, "keys_in_table": 25}
    if f122.one_moment(full, coverage=1.0):
        ok("one_moment still passes a frame with full dump coverage")
    else:
        fail("one_moment rejected a frame with full dump coverage")

    # A thin-but-honest frame that fails on keys_on_frame alone (unrelated to
    # this fix) must still fail - the new leg does not paper over the old ones.
    thin = {"explained": 0.90, "keys_on_frame": 1, "keys_in_table": 25}
    if not f122.one_moment(thin, coverage=1.0):
        ok("one_moment still rejects on keys_on_frame regardless of coverage")
    else:
        fail("one_moment ignored keys_on_frame once coverage was full")


def capture_owned_tests(tmp):
    """#494: a second a recorded screen owns cannot host a panel.

    The fixture is Tetris 2's shape in miniature: one `<tile>` rule whose key
    the panel would paste from the cell the capture covers, one outside it, and
    a recorded screen 64x32 at scale 4 - the frame's top-left 2x1 cells. Two
    candidate seconds are offered: 40 s, whose frame is that screen (with one
    stray pixel of a rule's own probe colour on it, the coincidence the colour
    count reads as "that rule is drawn here"), and 20 s, whose frame draws a
    rule for real. `subprocess.run` is replaced, so no emulator runs: the
    "Core" below is the two facts the probe asks it for - a recorded screen's
    painted PNG is what the frame shows where it lands, and a `<tile>` rule is
    its crop's colour."""
    romdir = tmp / "romdir"
    rom = romdir / "Game (1993) (Nintendo).nes"
    (romdir / rom.stem).mkdir(parents=True)
    rom.write_bytes(b"NES\x1a")
    pack = tmp / "pack"
    (pack / "textures/sheets").mkdir(parents=True)
    (pack / "textures/backgrounds").mkdir(parents=True)
    _repaint = f122._repaint
    _repaint.write_png(pack / "textures/sheets/unsorted.png",
                       _repaint.Image(128, 128, bytearray(b"\x10\x20\x30\xff" * (128 * 128))))
    _repaint.write_png(pack / "textures/backgrounds/screen001.png",
                       _repaint.Image(64, 32, bytearray(b"\x00\x00\x00\xff" * (64 * 32))))
    (pack / "textures/hires.txt").write_text(
        "<ver>109\n<scale>4\n"
        "<img>sheets/unsorted.png\n"
        "<tile>0,23,0F262A12,0,0,1,N\n"
        "<tile>0,23,0F2A3036,64,64,1,N\n"
        "<condition>screen001_A,tileAtPosition,0,0,23,0F262A12\n"
        "[screen001_A]<background>backgrounds/screen001.png,1,0,0,20\n")
    out = tmp / "prepare"
    out.mkdir()
    sibling = romdir / rom.stem

    def painted(rel, x, y):
        """The colour `paint_probe` gave one crop of the probe pack it installed."""
        img = _repaint.read_png(sibling / "mep/textures" / rel)
        off = img.offset(x, y)
        return bytes(img.px[off:off + 3])

    def paint(target, x0, y0, x1, y1, rgb):
        for y in range(y0, y1):
            for x in range(x0, x1):
                off = target.offset(x, y)
                target.px[off:off + 4] = bytes(rgb) + b"\xff"

    def fake_run(cmd, **kwargs):
        seconds = int(cmd[4])
        shots = Path(cmd[5]).parent / "mesen-home/Screenshots"
        shots.mkdir(parents=True, exist_ok=True)
        if "hdpack-off" in cmd:
            #Not one colour, or the probe would call the frame a blank screen.
            img = _repaint.Image(256, 240, bytearray(b"".join(
                bytes((x % 256, 0x30, 0x30, 0xff)) for y in range(240) for x in range(256))))
        else:
            img = _repaint.Image(1024, 960, bytearray(b"\x30\x30\x30\xff" * (1024 * 960)))
            if seconds == 40:
                cap = _repaint.read_png(sibling / "mep/textures/backgrounds/screen001.png")
                for y in range(cap.height):
                    for x in range(cap.width):
                        src, off = cap.offset(x, y), img.offset(x, y)
                        img.px[off:off + 4] = cap.px[src:src + 4]
                #The rule at crop 0,0 is under the capture: the game drew it and
                #the screen PNG hid it, and this one pixel is all a colour count
                #can still find of it.
                paint(img, 200, 200, 201, 201, painted("sheets/unsorted.png", 0, 0))
            else:
                paint(img, 5 * 32, 5 * 32, 6 * 32, 6 * 32,
                      painted("sheets/unsorted.png", 64, 64))
        _repaint.write_png(shots / "shot.png", img)

        class Done:
            returncode = 0
            stdout = ""
            stderr = ""
        return Done()

    real_run = f122.subprocess.run
    f122.subprocess.run = fake_run
    try:
        ranked = f122.choose_frame(rom, pack, out, [40, 20])
    finally:
        f122.subprocess.run = real_run
    picked = [r[0] for r in ranked]
    if picked == [20]:
        ok("#494: choose_frame drops the second a recorded screen owns and keeps the next-best")
    else:
        fail(f"#494: choose_frame ranked {picked}, expected [20] - 40 s is "
             "backgrounds/screen001.png and every cell it covers is invisible to a paint")

    #A game whose every sampled second is owned is handed over by nobody: the
    #panel has no frame to be won on, and saying so is the only honest answer.
    f122.subprocess.run = fake_run
    try:
        f122.choose_frame(rom, pack, out, [40])
        fail("#494: choose_frame handed over a frame a recorded screen owns")
    except f122.PrepareError as ex:
        if "screen001" in str(ex):
            ok("#494: a game whose candidates are all owned is refused, naming the screen")
        else:
            fail(f"#494: the refusal does not name the recorded screen: {ex}")
    finally:
        f122.subprocess.run = real_run


def scan_pack_tests(tmp):
    """#420: the scan checks palettes against the pack the evaluator paints.

    The copy asks the *loaded* pack which palettes it keys a tile under
    (`NesPackTilePalette`, #342). The F14.2 scan ran with the recording's
    bootstrap `auto/` beside the ROM and no `mep/`, so the loaded pack was
    `auto/`, whose 8 192 `defaultTile=Y` rules are palette wildcards: Zelda II's
    tile 28,0 copied with the live `0F301C15` while the baseline pack the
    evaluator paints keys that tile only as `0F2B0F00`, and stale keys with no
    baseline rule at all (Tetris 2, The Flintstones) passed. So while the scan
    runs, the ROM's sibling folder must hold the baseline pack as `mep/` and no
    `auto/`, and afterwards it must be exactly as it was.

    `subprocess.run` is replaced, so no emulator runs: what is asserted is the
    folder the scan's core would load its pack from."""
    romdir = tmp / "romdir"
    rom = romdir / "Game (1988).nes"
    sibling = romdir / rom.stem
    (sibling / "auto/textures").mkdir(parents=True)
    rom.write_bytes(b"NES\x1a")
    (sibling / "auto/textures/hires.txt").write_text(
        "<ver>109\n<tile>0,178,0F301C15,0,0,1,Y\n")
    (sibling / ".bootstrap").write_text("")
    pack = tmp / "baseline"
    (pack / "textures").mkdir(parents=True)
    (pack / "textures/hires.txt").write_text("<ver>109\n<tile>0,178,0F2B0F00,0,0,1,N\n")
    (pack / "pack.json").write_text("{}")
    state = tmp / "state.mss"
    state.write_bytes(b"MSS")
    table = tmp / "table.txt"

    seen = {}

    def fake_run(cmd, **kwargs):
        seen["cmd"] = cmd
        seen["env"] = kwargs.get("env") or {}
        mep = sibling / "mep/textures/hires.txt"
        seen["mep"] = mep.read_text() if mep.is_file() else None
        seen["auto"] = (sibling / "auto").exists()
        table.write_text("0,0\t" + json.dumps({"tile": SOLID_TILE, "palette": PAL}) + "\n")

        class Done:
            returncode = 0
            stdout = ""
            stderr = ""
        return Done()

    real_run = f122.subprocess.run
    f122.subprocess.run = fake_run
    try:
        f122.scan(rom, pack, state, table)
    finally:
        f122.subprocess.run = real_run

    if seen.get("mep") == (pack / "textures/hires.txt").read_text():
        ok("#420: the scan runs with the baseline pack installed as mep/ beside the ROM")
    else:
        fail(f"#420: the scan ran with mep/textures/hires.txt = {seen.get('mep')!r}, "
             "not the baseline pack the evaluator paints")
    if seen.get("auto") is False:
        ok("#420: the bootstrap auto/ (defaultTile=Y wildcards) is out of the way during the scan")
    else:
        fail("#420: the bootstrap auto/ was beside the ROM during the scan, so its "
             "defaultTile=Y wildcards accept any live palette")
    if seen.get("env", {}).get("MESEN_F122_COPY_STATE") == str(state):
        ok("#420: the scan still restores the dispatcher's state")
    else:
        fail("#420: the scan lost MESEN_F122_COPY_STATE")
    after = sorted(p.name for p in sibling.iterdir())
    if after == [".bootstrap", "auto"] and \
            (sibling / "auto/textures/hires.txt").read_text().endswith(",Y\n"):
        ok("#420: the sibling folder is restored exactly (auto/ back, no mep/ left behind)")
    else:
        fail(f"#420: after the scan the sibling folder holds {after}")

    # A mep/ already beside the ROM (a previous run's leftover) is the
    # evaluator's or another run's; the scan swaps it out and puts it back.
    (sibling / "mep/textures").mkdir(parents=True)
    (sibling / "mep/textures/hires.txt").write_text("leftover\n")
    f122.subprocess.run = fake_run
    try:
        f122.scan(rom, pack, state, table)
    finally:
        f122.subprocess.run = real_run
    if seen.get("mep") == (pack / "textures/hires.txt").read_text() and \
            (sibling / "mep/textures/hires.txt").read_text() == "leftover\n":
        ok("#420: a mep/ already beside the ROM is set aside for the scan and restored")
    else:
        fail("#420: a pre-existing mep/ was scanned against or lost")

    # Measured on Super Mario Bros. (E2E, 2026-09-24): with auto/ set aside the
    # core's bootstrap starts an audio recorder on ROM load and writes
    # auto/audio/.recorder into the empty slot, so putting the real auto/ back
    # by rename failed with "Directory not empty" and left it parked. Whatever
    # the scan's own run wrote there is a by-product of the scan.
    shutil.rmtree(sibling / "mep")

    def recording_run(cmd, **kwargs):
        (sibling / "auto/audio").mkdir(parents=True)
        (sibling / "auto/audio/.recorder").write_text("")
        return fake_run(cmd, **kwargs)

    f122.subprocess.run = recording_run
    try:
        f122.scan(rom, pack, state, table)
        raised = None
    except OSError as ex:
        raised = ex
    finally:
        f122.subprocess.run = real_run
    after = sorted(p.name for p in sibling.iterdir())
    if raised is None and after == [".bootstrap", "auto"] and \
            (sibling / "auto/textures/hires.txt").is_file() and \
            not (sibling / "auto/audio").exists():
        ok("#420: an auto/ the core recreated during the scan is dropped and the real one restored")
    else:
        fail(f"#420: after a scan whose core recreated auto/ the sibling holds {after} ({raised})")
        for leftover in ("auto.scan-aside", "mep.scan-aside"):
            if (sibling / leftover).exists():
                shutil.rmtree(sibling / "auto", ignore_errors=True)
                (sibling / leftover).rename(sibling / leftover.split(".")[0])

    # The scan failing (no table written) still restores the folder.

    def failing_run(cmd, **kwargs):
        if table.exists():
            table.unlink()

        class Done:
            returncode = 1
            stdout = "boom"
            stderr = ""
        return Done()

    f122.subprocess.run = failing_run
    try:
        f122.scan(rom, pack, state, table)
        fail("#420: a scan that wrote no table did not raise")
    except f122.PrepareError:
        pass
    finally:
        f122.subprocess.run = real_run
    after = sorted(p.name for p in sibling.iterdir())
    if after == [".bootstrap", "auto"]:
        ok("#420: a failed scan still restores the sibling folder")
    else:
        fail(f"#420: after a failed scan the sibling folder holds {after}")


def main() -> int:
    import tempfile
    with tempfile.TemporaryDirectory() as tmp:
        dump_coverage_tests(Path(tmp))
    with tempfile.TemporaryDirectory() as tmp:
        scan_pack_tests(Path(tmp))
    with tempfile.TemporaryDirectory() as tmp:
        capture_owned_tests(Path(tmp))
    one_moment_gate_tests()
    return 1 if FAILED else 0


if __name__ == "__main__":
    sys.exit(main())
