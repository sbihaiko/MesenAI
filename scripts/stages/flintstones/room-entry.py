#!/usr/bin/env python3
"""Write (and check) this folder's `mint-room<NN>.txt` entry scripts.

The Flintstones: The Surprise at Dinosaur Peak! is swept at ADR-0239 s1's
first rung - the game's own hidden debug level select, typed as input, no
cheat and no RAM address to invent. Every value's entry script is the same
fourteen-press code, then Start to reveal the two-digit number, then one
Right per step of that number, then Start again to begin the game on it.
Only the number of Rights changes, so the 37 scripts are generated from one
template and checked back against it.

    python3 room-entry.py            # write every mint-room<NN>.txt
    python3 room-entry.py --check    # fail if a file is not what it would write

The values are `VALUE_RANGE`. Selector 12 is one of them: it starts a level
of its own (a green-walled pipe room, and 385 bytes of its final state differ
from value 11's), even though the game's own level data tags it 0B - the same
id value 11's level carries. One value per place, so it stays; see
navigation.json's `_comment` for the measurement.
"""
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent

# Frames per press of the debug code, and the pause the screen needs between
# the code and the Start that reveals the number (measured 2026-09-26 on this
# checkout's dump: a shorter gap left the title screen up, a longer one
# changed nothing, and a press the screen is still reading is dropped).
PRESS, GAP = 6, 6
BOOT = 900          # power-on to the title screen, in frames
REVEAL = 60         # the pause either side of the Start that shows the number
STEP_HOLD = 4       # frames Right is held for one step of the number
STEP_GAP = 12       # idle frames after that step, so one press is one step
START_HOLD = 4      # frames Start is held to begin the game

# Core/Shared/HeadlessInputScript.h: the tokens are UDLRABST, so T is Start
# and S is Select. TCRF's code reads "A, B, Right, A, Down, Down, Down, Down,
# B, Left, Left, B, A, Start" - the last press is Start, which is `T` here.
CODE = ["A", "B", "R", "A", "D", "D", "D", "D", "B", "L", "L", "B", "A", "T"]

# The selector's two-digit number runs 00..99 and wraps at 100, measured with
# runs/f1418/flintstones/probe-steps.py: 100 Right presses leave $03CE = 00
# and 160 leave 3C (= 60), while every count below 100 leaves its own count
# there. It is only *mapped* to a level from 00 to 36: the level loader's own
# byte $0303 follows the number exactly there and stops following it at 37
# (measured 2026-09-26: 37 read level 34, 38 read 19, 40 read 12, 50 read 18,
# 59 read 08, 60 read 19, 63 read 00, 160 read 19 - the number is past the
# end of the game's own level table, so those values re-enter a room an
# in-range value already draws or garble it). 37 values, one per place.
VALUE_RANGE = range(0, 37)


def frames(lines):
    return sum(int(l.split("f", 1)[0]) for l in lines)


def mint_lines(value, extra_comment=()):
    """The entry script for one selector value, comments and all."""
    out = [
        "# The Flintstones - The Surprise at Dinosaur Peak!: power-on to the",
        f"# start of the level the debug level select's number {value} loads",
        f"# (ADR-0239 s1 rung 1 - the game's own selector, by input, so no RAM",
        "# cheat rides with the run).",
        "#",
        "# The code is TCRF's, verbatim: \"Press A, B, Right, A, Down, Down,",
        "# Down, Down, B, Left, Left, B, A, Start at the title screen\"",
        "# (tcrf.net, 'The Flintstones: The Surprise at Dinosaur Peak!',",
        "# Debug Mode). Every press is 6 frames with a 6-frame gap: measured",
        "# 2026-09-26, a press the screen is still reading is dropped and the",
        "# debug screen never appears.",
        "#",
        "# \"After pressing any button, a number will appear which acts as a",
        "# combination sound test/stage select ... press Left or Right to",
        "# change the number ... and Start to begin the game on the selected",
        "# stage\" (the same page). The first Start below reveals the number,",
        "# the second begins the game on it.",
        "#",
        f"# {value} step(s) of `{STEP_HOLD}f R` + `{STEP_GAP}f -` between them: the hold",
        "# is short enough not to repeat and the gap is long enough that the",
        f"# screen has read it, so {value} presses leave the number reading {value:02d}",
        "# (measured 2026-09-26 with runs/f1418/flintstones/probe-steps.py: on",
        "# the number screen $03CE equals the press count for every count below",
        "# 100, and in decimal on the screen).",
    ]
    out += list(extra_comment)
    out += [f"{BOOT}f -"]
    for button in CODE:
        out += [f"{PRESS}f {button}", f"{GAP}f -"]
    out += [f"{REVEAL}f -", f"{START_HOLD}f T", f"{REVEAL}f -"]
    for _ in range(value):
        out += [f"{STEP_HOLD}f R", f"{STEP_GAP}f -"]
    out += [f"{STEP_GAP}f -", f"{START_HOLD}f T"]
    return out


def wanted():
    return {f"mint-room{v:02d}.txt": "\n".join(mint_lines(v)) + "\n"
            for v in VALUE_RANGE}


def main():
    check = "--check" in sys.argv[1:]
    files = wanted()
    bad, wrote = [], []
    for name, text in sorted(files.items()):
        path = HERE / name
        if check:
            if not path.is_file():
                bad.append(f"{name}: missing")
            elif path.read_text() != text:
                bad.append(f"{name}: differs from what room-entry.py writes")
            continue
        path.write_text(text)
        wrote.append(name)
    if check:
        for line in bad:
            print(line)
        print(f"{len(files) - len(bad)}/{len(files)} entry scripts match"
              + ("" if not bad else " - FAIL"))
        return 1 if bad else 0
    print(f"wrote {len(wrote)} entry scripts: {wrote[0]} .. {wrote[-1]}")
    for name in sorted(files):
        lines = [l for l in files[name].splitlines()
                 if l.strip() and not l.startswith("#")]
        print(f"  {name}: {frames(lines)} frames ({frames(lines) / 60.0988:.1f} s)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
