#!/usr/bin/env python3
"""Generate Dr. Mario's `mint-level-NN.txt` entry scripts (F14.17).

The scripts are versioned; this generator is kept beside them so the one thing
that varies - how many times the VIRUS LEVEL cursor is stepped - is readable in
one place instead of twenty-one. Run it from the repository root:

    python3 scripts/stages/drmario/gen-entries.py

and it rewrites every `mint-level-NN.txt` in its own folder. It writes nothing
else.

The menu path it encodes was read off this ROM's own screens on 2026-09-26
(screenshots under runs/f1417/drmario/probe/shots, unversioned):

  boot -> title          "1 PLAYER GAME / 2 PLAYER GAME / (c) 1990 Nintendo"
  Start                  -> the option screen, titled "1 PLAYER GAME", whose
                            three rows are VIRUS LEVEL (a scale from 00 to 20),
                            SPEED (LOW / MED / HI) and MUSIC TYPE (FEVER /
                            CHILL / OFF). The cursor opens on VIRUS LEVEL.
  Right x N              -> the VIRUS LEVEL cursor, one step per press
  Start                  -> the bottle; the level the cursor left is the level
                            the game is played at

Two measurements fix the shape of every press:

  * The cursor CLAMPS at both ends and never wraps: 20 Right presses from 00
    reach 20 and further presses hold it there; three Left presses at 00 leave
    it at 00. So N presses from the default reach level N exactly, with no
    arithmetic and no wrap to worry about.
  * A press is 2 frames of hold followed by 38 frames of release. A held
    direction auto-repeats and walks several steps (measured: a 120-frame hold
    of Right moved the cursor 17 steps), which is deterministic but not what a
    value named `level-07` should depend on. Measured landings for this exact
    pattern: 0, 1, 3, 5, 10, 15 and 20 presses give $0096 = 0, 1, 3, 5, 10,
    15 and 20.
"""
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent

# The 60-frame lead-in is the boot and the title's own delay; the 60 frames
# after Start let the option screen finish drawing before the first press.
BOOT = [(60, "-"), (30, "T"), (60, "-")]
# 2 frames of hold, 38 of release: one cursor step, never an auto-repeat.
STEP = [(2, "R"), (38, "-")]
# Start leaves the option screen; 20 idle frames let the level screen settle so
# the recording's first retained frames are the bottle and not the wipe.
LAUNCH = [(2, "T"), (20, "-")]

HEADER = """# mint-level-{n:02d}.txt - Dr. Mario (1990) (Nintendo) from power-on to the
# bottle at VIRUS LEVEL {n:02d} (F14.17, ADR-0239 rung 1: the game's own
# selector, by input - no RAM cheat).
#
# Title -> Start -> the option screen -> {n:02d} press(es) of Right on the VIRUS
# LEVEL row -> Start. The cursor clamps at 20 and never wraps, and each press is
# 2 frames of hold plus 38 of release so no auto-repeat can add a step the count
# does not name. Rebuild every mint with scripts/stages/drmario/gen-entries.py.
"""


def script(level: int) -> str:
    plan = BOOT + STEP * level + LAUNCH
    return HEADER.format(n=level) + "".join(f"{f}f {b}\n" for f, b in plan)


def main() -> int:
    written = []
    for level in range(21):
        path = HERE / f"mint-level-{level:02d}.txt"
        text = script(level)
        if not path.exists() or path.read_text() != text:
            path.write_text(text)
            written.append(path.name)
    print(f"mint-level-00 .. mint-level-20 in {HERE} "
          f"({'rewrote ' + ', '.join(written) if written else 'all up to date'})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
