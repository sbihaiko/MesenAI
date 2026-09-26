#!/usr/bin/env python3
"""Write one Lemmings (NES) entry script per access code in `navigation.json`.

    python3 scripts/stages/lemmings/access-code.py            # write the scripts
    python3 scripts/stages/lemmings/access-code.py --check    # ... and diff them

Why this file exists. An entry script for this game is not typed by hand: the
ACCESS CODE screen wants six letters, each set by holding Up for a fixed count
of frames, and a wrong count is a different letter, which is a session that
records a level nobody asked for. `access-code.py` is the generator, so the
frame counts are arithmetic and the profile's `_accessCode` is the only place a
code is written down (the punchout folder's `pass-key.py` is the precedent).

The screen, measured on this dump 2026-09-26 (probes under
runs/f1417/lemmings/probe, unversioned):

* Title -> Start opens the main menu (START / NEW LEVEL / FUN / music). The
  D-pad moves a pointer and A clicks it, so Right once then A opens
  "ENTER YOUR ACCESS CODE / THEN PUSH START BUTTON". The pointer is already
  parked on START at the menu, so the stage-1 route is one A press with no
  Right at all (`mint-stage1.txt`).
* The code screen shows six characters; the buffer behind them is six ASCII
  bytes at $0054-$0059 in internal RAM, read out of a save state with
  `scripts/mss_ram.py`. The cursor starts on the first character and Right
  moves it; Up increments the character under it and Down decrements it; A and
  B do nothing; Start submits.
* The alphabet is the 21 letters `BCDFGHJKLMNPQRSTVWXYZ` - the vowels are not
  in it, and it is exactly the letter set every one of the ROM's 100 codes is
  drawn from. B is index 0, the value a position starts at, which is why the
  screen opens on `BBBBBB`.
* Up is an autorepeat, and its counter is *not* reset when the button is
  released: holding Up for k frames advances the character by `k // 8`, and
  the remainder carries into the next hold. So a hold of `8 * n` frames
  advances exactly n steps and leaves the counter at zero, which is what makes
  `8 * index` the correct hold for every character after the first. Measured
  both ways: a single hold of 8/16/24/32/48/64/96 frames advanced 1/2/3/4/6/8/12
  steps (B, C, D, F, J, L, Q), and four holds of 10 frames with a 120-frame gap
  advanced 5 steps, not 4 - the leftover 2 frames per hold accumulate.
* Start submits the code and the game answers on the same screen with
  "CODE IS FOR LEVEL n / RATING IS x" (~150 frames later). Start again, after
  the answer is on screen, opens the level's preview ("LEVEL n", the title,
  NUMBER OF LEMMINGS / NUMBER TO BE SAVED / RELEASE RATE / TIME), and a third
  Start begins play. The answer screen times out back to the menu after a few
  seconds, which is why `mint-level-*.txt` presses Start twice more inside the
  same script instead of leaving a gap.

Everything below this line is arithmetic over that measurement.
"""

import argparse
import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent

# The 21 letters the ACCESS CODE screen cycles through, in the order Up walks
# them. B is index 0 - the value every one of the six positions opens on.
ALPHABET = "BCDFGHJKLMNPQRSTVWXYZ"

# Up advances one step per 8 held frames (see the module docstring).
FRAMES_PER_STEP = 8

# The presses that walk from power-on to the ACCESS CODE screen, and the frames
# between them. Every menu press is 10 frames with a 20-120 frame gap: the ROM
# ignores a press that arrives in the frame a pointer move is being read.
HEAD = [
    ("900f -", "the title screen holds for ~900 frames before it takes input"),
    ("10f T", "title -> main menu"),
    ("120f -", "the menu draws"),
    ("10f R", "the pointer moves from START to NEW LEVEL"),
    ("20f -", ""),
    ("10f A", "click NEW LEVEL -> the ACCESS CODE screen"),
    ("120f -", "the code screen draws"),
]

# The presses that turn a submitted code into a running level.
TAIL = [
    ("10f T", "submit the code"),
    ("240f -", "the game answers CODE IS FOR LEVEL n / RATING IS x"),
    ("10f T", "open the level's preview"),
    ("120f -", "the preview draws"),
    ("10f T", "begin play"),
    ("60f -", "the level draws its first frames"),
]


def typing_lines(code: str) -> list:
    """The presses that type `code` on the ACCESS CODE screen."""
    if len(code) != 6:
        raise ValueError(f"access code {code!r} is not six characters")
    lines = []
    for i, ch in enumerate(code):
        if ch not in ALPHABET:
            raise ValueError(f"access code {code!r}: {ch!r} is not in the "
                             f"screen's alphabet ({ALPHABET})")
        if i:
            lines.append("10f R")
            lines.append("20f -")
        steps = ALPHABET.index(ch)
        if steps:
            lines.append(f"{FRAMES_PER_STEP * steps}f U")
            lines.append("20f -")
    return lines


def entry_script(code: str, title: str, level: int) -> str:
    head = [
        f"# mint-level-{level:03d}: power-on -> the ACCESS CODE screen -> the code",
        f"# {code} -> level {level} ({title!r}) running.",
        "#",
        "# Generated by scripts/stages/lemmings/access-code.py - do not edit by",
        "# hand. ADR-0239 s1 rung 1: the game's own selector, typed by input, so",
        "# no RAM cheat rides with the session.",
    ]
    # A comment is a line of its own: headless_record's script parser reads the
    # whole remainder of a step's line as button letters, so "900f -   # why"
    # is refused as the unknown button "#".
    for line, why in HEAD + [(l, "") for l in typing_lines(code)] + TAIL:
        if why:
            head.append(f"# {why}")
        head.append(line)
    return "\n".join(head) + "\n"


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--check", action="store_true",
                    help="write nothing; fail if a script on disk differs")
    args = ap.parse_args()

    profile = json.loads((HERE / "navigation.json").read_text())
    values = profile["navigation"]["values"]
    if not values:
        print("navigation.json has no values", file=sys.stderr)
        return 2

    written, stale = [], []
    for v in values:
        code = v.get("_accessCode")
        if not code:
            print(f'{v["name"]} carries no _accessCode', file=sys.stderr)
            return 2
        level = v["_level"]
        want = entry_script(code, v["_title"], level)
        dest = HERE / v["entry"]
        if args.check:
            if not dest.is_file() or dest.read_text() != want:
                stale.append(dest.name)
            continue
        dest.write_text(want)
        written.append(dest.name)

    if args.check:
        if stale:
            print("stale entry scripts: " + ", ".join(stale), file=sys.stderr)
            return 1
        print(f"ok   {len(values)} entry scripts match navigation.json")
        return 0
    print(f"ok   wrote {len(written)} entry scripts beside navigation.json")
    return 0


if __name__ == "__main__":
    sys.exit(main())
