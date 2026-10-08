#!/usr/bin/env python3
"""Write the `mint-<name>.txt` scripts that start Bubble Bobble (NES) at the
round its own password screen selects (ADR-0239 section 1, rung 1: the game's
own selector, by input - no cheat rides with any of these sessions).

    python3 scripts/stages/bubblebobble/password-entry.py [--check] [--out DIR]

`--check` re-derives every script in memory and diffs it against what is on
disk, so a hand edit or a stale mint is reported instead of recorded. It is
the same contract `scripts/stages/iceclimber/mountain-entry.py` uses.

Why a generator: entering one password is up to 45 keypresses and each one is
30 frames, so the file is ~150 lines of a single button, and twelve of them
differ only in a digit count. The comments below are the record of why the
shape is what it is.

THE SCREEN MECHANICS, read off this ROM's own password routine (6502 at
$FE32-$FFA6, mapper 1 bank 7 mapped to $C000-$FFFF) and confirmed with
recorded sessions whose final states read the typed password back out of the
game's own letter array (`runs/f1418/bubblebobble/probe/`, 2026-09-26):

  * Main menu, five entries: 1P START / 2P START / 1P CONTINUE / 2P CONTINUE /
    PASSWORD. SELECT moves the cursor down one entry and wraps; START opens
    the entry. Four SELECTs from power-on reach PASSWORD, and the game then
    holds $0001 = 05 while the password screen is up.
  * The password is five letters in five slots, and the game keeps them in
    RAM $0502-$0506 as indices 0-9 ('A'-'J'). Its own VRAM queue mirrors each
    one as the tile `index + 0x0A` at nametable row 23, columns 11, 13, 15,
    17 and 19, which is how a session reads its typed password back.
  * The button that advances a letter is DOWN (`$FE61`: INC $0502,X, refused
    at 9) and the one that goes back is UP (`$FE53`: DEC $0502,X, refused at
    0). The letters therefore run A->J and clamp at both ends - they do not
    wrap. This is the one thing the manual's prose leaves implicit and no
    walkthrough states; measured 2026-09-26 (probes r2_D11 = eleven DOWNs on
    slot 0 ends at J; r2_DU = DOWN then UP is back at A).
  * RIGHT and LEFT move the bubble cursor between slots (`$FE8D`/`$FE71`:
    INC/DEC $050A, refused at slots 4 and 0). START submits (`$FE32`
    dispatches it to the validator at $FE04).
  * A accepted password plays sound $0D (the bell the manual describes) and
    writes the round it unlocks; a refused one plays $23, restores the five
    letters from its backup and puts the cursor back on slot 0 - the manual's
    "the bubble cursor moves back to the first letter of the password".
  * After the bell the game is back on the main menu with the round armed and
    the cursor back on the first entry (1P START). The manual's own next step
    is "select 1P or 2P CONTINUE and press START", and it matters: START on
    1P START begins a new game at round 1 whatever the password said. So every
    mint here ends with two SELECTs (1P CONTINUE), a START that opens the
    ROUND screen, and a second START that commits the round and begins it.

The alphabet is A-J, the manual's "five letters long and uses the first ten
letters of the alphabet (ABCDEFGHIJ)", so 'H' is expressible and the question
about the three sources that use it is the checksum, not the keyboard.

WHAT EACH PASSWORD DECODES TO, read off the ROM's own validator and used here
to label the values. The five letters are converted to permuted indices
through the ten-byte table at $FDA1 (`0B 0A 12 0F 13 0C 10 0E 0D 11`, which
maps A->1, B->0, I->2, F->3, J->4, C->5, G->6, E->7, D->8, H->9), and then
($FEF3-$FF5E, the accepted branch):

    round = (p1 << 4) | ((p1 ^ p2) << 1) | ((p2 ^ p3) & 1)

- `ORA`, not `+`: the three fields overlap bit 4, so HBGBD is 146 and not
  162. The same routine writes the round to both $049C and $0401, stores
  $0505 & 3 and $0505 >> 2 as the two "level within the round" halves, and
  rings sound $0D.

Every round here was re-derived with that formula and every one of them was
then MEASURED: a session that types the password and reads its own final
state back has $049C equal to the number this file labels it with. The
decoder is also what corrected three labels that came from the walkthrough's
"Identical to Arcade round 65/79/90" remarks - those are the ARCADE rounds -
to the NES ones the page's own level column gives: FFIIG is 49, GBIFG is
108 ("A8"), GAIFJ is 111 ("B1"), and the level-select password EECFG (and its
Regular twin EECJJ) is 112 ("B2").

  Why those codes read as letters: the HUD draws the round as two tiles,
  0x00 + (round / 10) and 0x00 + (round % 10) - there is no third digit - and
  in this ROM's font 0x00-0x09 are '0'-'9' while 0x0A-0x23 are 'A'-'Z'. Up to
  99 the first tile is a digit; from 100 on it is 10 or more and the game
  draws a letter. Measured: round 108 shows "A8", 111 "B1", 112 "B2". That is
  how the walkthrough's own level column writes those rows (its levels run
  "A0".."B2"), and it is where the "C6 GHCCB", "E6 HBGBD" and "F5 HJFAB"
  prefixes come from.

THE THREE 'H' CODES ARE ACCEPTED, AND ARE STILL NOT IN THIS SET. Measured
2026-09-26, one session per code (runs/f1418/bubblebobble/probe/sc_h126,
sc_h171, sc_h174): GHCCB, HBGBD and HJFAB all pass the ROM's own checksum,
ring the bell, arm the round, and start a playable round - $049C reads 126,
146 and 155, exactly the values the walkthrough's C6/E6/F5 prefixes encode.
What they do NOT do is play the round they name. The game draws 60 for
GHCCB, 80 for HBGBD and 60 for HJFAB: past the NES version's own last round
the round-to-level lookup lands somewhere else. So a session on one of them
would pass a $049C check while its art belongs to another round, and two of
the three land on the same round - `new` would read 0 for one of them, which
ADR-0239 s4 reads as `did-not-warp`. The letter is not the problem (the
keyboard offers H); the round is. They are left out on that evidence, and
the evidence is recorded rather than the conclusion alone.

WHY NO PASSWORD HERE IS SYNTHESIZED. The arithmetic in the paragraph above is
the round of a password the validator ACCEPTS, not an acceptance test, and the
difference is measured: ten probes of one shape, `AAAA?` for ? = A..J
(runs/f1418/bubblebobble/probe/accept5/), leave exactly one accepted - AAAAB,
which arms round 16 - while AAAAA computes the same 16 and is refused, still on
the password screen with the cursor back on the first slot. The last two letters
are constrained by something this file does not derive, and the constraint is
not one string per round either (round 1 accepts BBAAB and the Super BBAJI;
round 112 accepts EECJJ and EECFG). Every password below is therefore a
published one, and the set types nothing it cannot point a source at.

Passwords are published values, cited per value in `navigation.json`. None is
invented here, and none is computed: the ROM's own validator was read to
label the values, but the published table is what this set records, because
the published table is what a reader can check.
"""
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent

# Frames. Measured 2026-09-26 on the user's dump, one probe per number:
# a press held 10 frames with a 20-frame release is read on every screen here,
# and the screen ignores a press that arrives inside the frame it is reading
# the previous one.
BOOT = 700          # power-on to the title/menu handover
PRESS = 10          # frames a button is held
GAP = 20            # frames released between two presses
ENTER_MENU = 30     # after START leaves the boot screen
ENTER_PASSWORD = 40  # after START opens the PASSWORD entry
SUBMIT = 30         # before START submits the five letters
BELL = 150          # submit -> bell -> back on the main menu
START_ROUND = 10    # each of the two STARTs the CONTINUE entry needs
ROUND_SCREEN = 40   # the ROUND screen needs a frame to draw before its START
TAIL = 240          # the round loads, then the body takes over

SELECTS_TO_PASSWORD = 4
# The bell drops the cursor back on the menu's first entry (1P START), not on
# PASSWORD. START there begins a *new* game at round 1 whatever the password
# said - measured 2026-09-26: the password AGJJJ (published round 30) with the
# single START left a nametable differing from the BBAAB round-1 session in one
# tile. The manual's own step is the missing one: "After choosing the correct
# password, select 1P or 2P CONTINUE and press START." 1P CONTINUE is the third
# entry, so two SELECTs.
SELECTS_TO_CONTINUE = 2
# And that entry does not start a round on its own: it opens a ROUND screen
# (6502 at $CA53: START there jumps to $CAA4, which copies the round into $86
# and redraws) where A raises the round and B lowers it - the Secret codes
# page's "you can then increase or decrease the continue level to the desired
# level" - and a *second* START commits it ($CA80: $0401 = $86) and lets the
# menu's own start routine run. Measured 2026-09-26, one probe per variant at
# the same password: one START, a 60-frame START, and A instead of START all
# end on the menu with two sprites alive ($0402 = 2, $002E = 0); two STARTs
# ends in play ($0402 = 0, $002E = 3, sixteen sprites, the round's number in
# the HUD).

# name, password, the round the ROM's own validator decodes it to, and the
# label the walkthrough's level column gives that row (which is the game's own
# two-glyph display). Every password here is from the walkthrough's password
# list for the level that label names; the two Super forms are the walkthrough's
# own "Super password" for their level, and `levelselect` is the level-select
# password the Secret codes page names.
#
# The list spans the artifact families the round table offers: the first round,
# four more in the first ten, a scatter through the middle, the last round the
# walkthrough lists a regular password for (99), the three rounds past 100 that
# its own level column labels with letters (A8, B1, B2), and two Super rounds.
# Letters: A-J only. The three 'H' codes are left out on the evidence in the
# module docstring, not on the alphabet.
VALUES = [
    ("round01", "BBAAB", 1, "1"),
    ("round02", "BAAAB", 2, "2"),
    ("round03", "BABBI", 3, "3"),
    ("round05", "BIFFB", 5, "5"),
    ("round10", "BCCCB", 10, "10"),
    ("round16", "AAAAB", 16, "16"),
    ("round20", "AFFFB", 20, "20"),
    ("round30", "AGJJJ", 30, "30"),
    ("round48", "FFFFB", 48, "48"),
    ("round49", "FFIIG", 49, "49"),
    ("round99", "GEJJJ", 99, "99"),
    ("round108", "GBIFG", 108, "A8"),
    ("round111", "GAIFJ", 111, "B1"),
    ("levelselect", "EECFG", 112, "B2"),
    ("super01", "BBAJI", 1, "1 Super"),
    ("super48", "FFAJJ", 48, "48 Super"),
]

HEAD = [
    f"{BOOT}f -",           # power-on, the attract demo, the title
    f"{PRESS}f T",          # title -> main menu
    f"{ENTER_MENU}f -",
]
for _ in range(SELECTS_TO_PASSWORD):
    HEAD += [f"{PRESS}f S", f"{GAP}f -"]
HEAD += [f"{PRESS}f T", f"{ENTER_PASSWORD}f -"]   # open PASSWORD


def letters_for(password: str) -> list:
    """The keypresses that type `password`, each with the slot it acts on.

    `(key, slot)`, slot counted from 1 the way the manual counts it ("the bubble
    cursor moves back to the first letter of the password"). The slot travels
    with the key because it is what the RIGHT's own comment names, and sizing
    that comment off the flattened keypress list instead put every one of them a
    slot high: the list is one entry per press, not one per letter.
    """
    out = []
    for i, ch in enumerate(password):
        index = ord(ch) - ord("A")
        if not 0 <= index <= 9:
            raise ValueError(f"{password!r}: {ch!r} is outside the A-J the "
                             "password screen offers")
        # DOWN advances the letter under the cursor, clamped at J ($FE61).
        out += [("D", i + 1)] * index
        if i < len(password) - 1:
            # RIGHT moves the bubble to the next slot ($FE8D).
            out.append(("R", i + 1))
    return out


def script(name: str, password: str, round_no: int, label: str) -> str:
    lines = [
        f"# Bubble Bobble (NES) from power-on to the start of round {round_no}"
        f" the password {password} unlocks - the level the walkthrough's own"
        f" table lists as {label}.",
        "# Generated by scripts/stages/bubblebobble/password-entry.py - do not"
        " edit by hand.",
        "#",
        "# The game's own selector (ADR-0239 section 1 rung 1): no RAM cheat is",
        "# involved, so every session is a clean pass and all four ADR-0183",
        "# surfaces may read it. Typing, read off the ROM's password routine at",
        "# $FE32-$FFA6 and confirmed by recorded sessions: SELECT four times",
        "# moves the main-menu cursor to PASSWORD, START opens it, DOWN advances",
        "# the letter under the bubble cursor (A-J, clamped), UP takes it back,",
        "# RIGHT and LEFT move the bubble between the five slots, and START",
        "# submits. A correct password rings the bell (sound $0D) and returns to",
        "# the main menu with the round armed; the last START here begins the 1P",
        "# CONTINUE at it.",
        "",
    ]
    lines += HEAD[:]
    for key, slot in letters_for(password):
        if key == "R":
            lines.append(f"# slot {slot} -> slot {slot + 1}")
        lines += [f"{PRESS}f {key}", f"{GAP}f -"]
    lines += [
        f"# submit {password}; the bell rings and the main menu returns with the",
        f"# round armed and the cursor back on 1P START",
        f"{SUBMIT}f T",
        f"{BELL}f -",
        f"# two SELECTs: 1P START -> 2P START -> 1P CONTINUE",
    ]
    for _ in range(SELECTS_TO_CONTINUE):
        lines += [f"{PRESS}f S", f"{GAP}f -"]
    lines += [
        f"# START opens the ROUND screen; its own value is the round the",
        f"# password armed",
        f"{START_ROUND}f T",
        f"{ROUND_SCREEN}f -",
        f"# the second START commits the round and begins the 1P CONTINUE on it",
        f"{START_ROUND}f T",
        f"{TAIL}f -",
    ]
    return "\n".join(lines) + "\n"


def plain_round1() -> str:
    """The default entry: no password, the menu's own 1P START."""
    return "\n".join([
        "# Bubble Bobble (NES) from power-on to the start of round 1, taken from",
        "# the main menu's own 1P START entry - no password, no cheat.",
        "# Generated by scripts/stages/bubblebobble/password-entry.py - do not"
        " edit by hand.",
        "#",
        "# This is the set's default entry and the route `stage1-run` mints from",
        "# it (scripts/library_job.py start_plan: the longest mint-*.txt whose",
        "# name prefixes the route). It is calibrated: the same script with the",
        "# four SELECTs added is every other mint in this folder.",
        "",
        *HEAD[:2],
        f"{ENTER_MENU}f -",
        f"# the cursor is on 1P START from power-on, so one START begins it",
        f"{PRESS}f T",
        f"{TAIL}f -",
        "",
    ])


def expected() -> dict:
    out = {f"mint-{name}.txt": script(name, password, round_no, label)
           for name, password, round_no, label in VALUES}
    out["mint-stage1.txt"] = plain_round1()
    return out


def main() -> int:
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    out_dir = Path(args[0]) if args else HERE
    want = expected()
    if "--check" in sys.argv:
        bad = []
        for name, text in sorted(want.items()):
            path = out_dir / name
            if not path.exists():
                bad.append(f"{name}: missing")
            elif path.read_text() != text:
                bad.append(f"{name}: differs from the generator")
        stray = sorted(p.name for p in out_dir.glob("mint-*.txt")
                       if p.name not in want)
        for name in stray:
            bad.append(f"{name}: on disk but not in the generator")
        if bad:
            print("--check: the mints on disk are not the generator's:",
                  file=sys.stderr)
            for line in bad:
                print(f"  {line}", file=sys.stderr)
            return 1
        print(f"--check: {len(want)} mints match the generator "
              f"({len(VALUES)} passwords + mint-stage1.txt)")
        return 0
    for name, text in sorted(want.items()):
        (out_dir / name).write_text(text)
    presses = sum(len(letters_for(p)) for _, p, _, _ in VALUES)
    print(f"wrote {len(want)} mints to {out_dir} "
          f"({len(VALUES)} passwords, {presses} keypresses)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
