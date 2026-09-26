#!/usr/bin/env python3
"""Write the `mint-mountain-NN.txt` entry scripts of the Ice Climber sweep.

ADR-0239 section 1 rung 1: the game's own selector, by input. Ice Climber's
title screen carries the selector as the third row of its menu - `MOUNTAIN
01` - and the D-pad walks it while `START` begins the game. The Ice Climber
instruction booklet (Nintendo of America, 1985), "Rules and Suggestions" 1:

    "Use the control pad when the game menu is displayed to enter the number
     of the mountain you want to climb. (MOUNTAIN XX)"
    "The mountains are numbered from 1 ~ 32. You can attempt to climb any
     mountain, but the larger the number the more difficult the mountain is
     to climb."

and the controller page: "SELECT button - Move the hammer mark ( < ) to the
game you wish to select from the game menu by pressing this button." / "START
button - Press this button to begin."

So one press of Right is one mountain, and the game writes the level index it
accepted into `$0059` - Data Crystal's "Ice Climber (NES)/RAM map": "0x0059
Level Number - Starts at 0 for level 1. Updates at the start of every new
level or when changing the level on the start screen" - which is what every
value's `ramCheck` asserts.

The press shape is measured, not chosen. Holding Right auto-repeats: one
300-frame hold of Right moved `$0059` to 20, one step every ~16 frames
(measured 2026-09-26, runs/f1417/iceclimber/probe/press/hold300). A press of
10 frames with a 10-frame release gives exactly one step each, so 31 presses
reach MOUNTAIN 32 and a 32nd press wraps back to MOUNTAIN 01 - both measured
(runs/f1417/iceclimber/probe/press, p31x10 and p34). 10 frames of hold stays
under the ~16-frame repeat and 20 frames per step keeps the longest entry
script at 700 frames, well inside the 120 s session budget of ADR-0239
section 6.

Why the entry script carries a walking phase and the body does not. The
D-pad is the mountain selector on the title screen, so any d-pad press that
lands there moves `$0059` and the session's own ramCheck stops naming the
mountain it selected. The game returns to the title only after GAME OVER -
the third lost man - so the entry's walk phase is the last d-pad input of a
session by construction, and it is one balanced block long: the body that
follows holds no direction at all. `D-pad` never appears in
`mountain-01-run.txt`, and `mountain-entry.py --check` fails if it ever does.

Run it from the repository root:

    python3 scripts/stages/iceclimber/mountain-entry.py

It rewrites every file it owns and touches nothing else.
"""

from pathlib import Path

HERE = Path(__file__).resolve().parent

#The gap between the boot and the title screen. The title is up at frame 3
#(runs/f1417/iceclimber/probe/st-title) and this is the margin the Excitebike
#set uses for the same ROM family.
LEAD_IN = "60f -"
PRESS = "10f R"
RELEASE = "10f -"
START = "16f T"
START_RELEASE = "16f -"

HEADER = [
    "# mint from power-on to the start of MOUNTAIN {n:02d}",
    "# (ADR-0239 s1 rung 1: the game's own selector, by input - no RAM cheat).",
    "# One 10-frame press of Right is one mountain; the game's own $0059 holds",
    "# the index it accepted (n - 1). The walk block after START is the last",
    "# d-pad input of the session - mountain-01-run.txt holds no direction at all,",
    "# so the title screen never moves the number the ramCheck reads.",
    "# See mountain-entry.py for the sources.",
]

#One balanced walking block (576 frames, 9.6 s), run once, right after START.
#Both directions, the hammer and the jump, each hold long enough for a run
#cycle to turn over (ADR-0179 section 3 wants two turns of a hold). It is one
#block and not more because it has to end before the session's third lost
#man: GAME OVER is what puts the title screen up, and the title screen is the
#only place a d-pad press moves `$0059`.
WALK = [
    "120f R", "12f RB", "12f -",
    "120f R", "12f RA", "12f -",
    "120f L", "12f LB", "12f -",
    "120f L", "12f LA", "12f -",
]

#The body may never name these: see the module docstring.
D_PAD = ("U", "D", "L", "R")


def entry_script(mountain: int) -> str:
    """The input script that starts the game on `mountain` (1..32)."""
    lines = [line.format(n=mountain) for line in HEADER]
    lines.append(LEAD_IN)
    for _ in range(mountain - 1):
        lines.append(PRESS)
        lines.append(RELEASE)
    lines.append(START)
    lines.append(START_RELEASE)
    lines.extend(WALK)
    return "\n".join(lines) + "\n"


def body_is_dpad_free(path: Path) -> str:
    """Empty when `path` holds no d-pad button, else the offending line."""
    for number, raw in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        line = raw.split("#")[0].strip()
        if not line:
            continue
        buttons = line.split()[-1] if len(line.split()) > 1 else ""
        for button in D_PAD:
            if button in buttons:
                return f"{path.name}:{number}: {raw.strip()}"
    return ""


def main():
    written = []
    for mountain in range(1, 33):
        path = HERE / f"mint-mountain-{mountain:02d}.txt"
        text = entry_script(mountain)
        if not path.exists() or path.read_text(encoding="utf-8") != text:
            path.write_text(text, encoding="utf-8")
            written.append(path.name)
    print(f"{len(written)} entry script(s) written: {', '.join(written) or 'none'}")

    body = HERE / "mountain-01-run.txt"
    if body.is_file():
        offending = body_is_dpad_free(body)
        if offending:
            raise SystemExit(f"{body.name} holds a d-pad button: {offending}")
        print(f"{body.name}: no d-pad button, as the entry scripts require")


if __name__ == "__main__":
    main()
