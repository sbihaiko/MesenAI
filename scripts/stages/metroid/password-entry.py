#!/usr/bin/env python3
"""Generate Metroid's password-entry scripts from the passwords themselves.

Metroid (USA) has no stage selector, no level byte in the cheat database and
no usable RAM selector at all - the vendored `CheatDb.Nes.json` ships exactly
two RAM codes for this ROM (`6878:FF`, `69B2:01`) and both are WRAM, which
ADR-0184 section 1 refuses (`AAAA < 0x0800`).  What the game does ship is a
**password screen**, so ADR-0239 section 1's first rung - the game's own
selector, by input - is the one this sweep uses, and every session is a clean
pass that carries no cheat.

The screen is a keyboard and each password is 24 characters typed onto it, so
each profile value needs its own input script.  This file is that generator:
it carries the screen's layout (measured off this ROM, see below), turns a
password into the button presses that type it, and writes one
`mint-<value>.txt` per value beside itself.  Nothing here is per-password
data except the password strings, which live in the `PASSWORDS` table with
their published source.

    python3 scripts/stages/metroid/password-entry.py            # (re)write
    python3 scripts/stages/metroid/password-entry.py --verify   # fail on drift
    python3 scripts/stages/metroid/password-entry.py --list     # press counts

What was measured off the ROM itself, 2026-09-26 (probe runs under
`runs/f1417/metroid/probe/`, each one a `headless_record` session whose
`save-state=` was read with `scripts/mss_ram.py`):

  engine mode   `$1D` is DataCrystal's "Engine mode (0 = game, 1 =
                title/password)": it is 1 on the title, the START/CONTINUE and
                the password screens and 0 once the game starts, which is what
                every probe here is read against.  DataCrystal's "Title mode"
                byte `$1F` is NOT used as a screen identifier: traced every
                emulated second over the first twenty seconds from power-on it
                counts 03, 04, 05, 06, 07, 08, 0D, so it moves with the title
                screen's animation.
  reaching it   Start at the title opens the START/CONTINUE screen; **Select**
                moves the crosshair (`$0325` 0 -> 1, DataCrystal's "Title/
                Password Cursor" block), and Start confirms, which is the
                "PASS WORD PLEASE" screen.  A, B, and the D-pad do nothing on
                the START/CONTINUE screen: measured, one run per button,
                `$0325` unmoved.  Select is the only key that moves it.
  the keyboard  five rows of thirteen columns: the 64 characters that fit in
                six bits each (24 characters x 6 = 144 = the 128-bit payload
                plus the shift byte plus the checksum, the encoding the
                Metroid Password Format Guide documents).  Read off the
                screen, in reading order and identical to the alphabet in
                `metroidgen.py`: `0123456789` + `A-Z` + `a-z` + `?` + `-`.
                The last row holds twelve, and the 65th cell (row 4, column
                12) is the alphabet's 65th character - the space, which the
                screen draws no glyph for: pressing A there writes `$FF` to
                `$699A`, the value 255 the guide gives the space.  No password
                in this profile types it.
  the cursor    `$0320` counts the characters entered, and the keyboard
                cursor is `$0321` (row) / `$0322` (column).  **The cursor is
                one index over 65 cells and the row and column are read back
                out of it** as `index // 13` and `index % 13`: Right and Left
                step it by +1 and -1, Up and Down by -13 and +13, all modulo
                65.  That is why Left from the first cell lands on row 4,
                column 12 (index 64) and Up from it lands on row 4, column 0
                (index 52) - the two axes are *not* independent, and a model
                that wraps row and column separately picks the wrong cell for
                any move that crosses a row boundary.  Measured, every one of
                them: $0321/$0322 after one Up from (0,0) is (4,0) and after
                one Left is (4,12); thirteen Rights from (4,0) and one Down
                from it both land on (0,0); four Downs from (0,0) return to
                it.
  the typing    A types the character under the cursor and **does not move
                the cursor**.  `$699A` (DataCrystal's "Password characters",
                in WRAM) holds each character typed as its alphabet index, and
                it is the check that the script typed what it meant to:
                pressing A on cell (0,0) reads 0 there, which is the character
                `0`; two Rights then A reads `02`, the character `2`; and A on
                cell (4,12) reads `FF`, the space.  Stronger still, **seven of
                the eleven recorded sessions' final states still hold their own
                password at `$699A`, character for character** - the four that
                do not ended on the password the ROM shows after a death, where
                it recomputes that buffer from the game state.
  the presses   a two-frame press registers: five presses of `2f R` with two
                frames released between them moved the column by exactly 5.
                The scripts below use four frames for margin, which is
                deterministic in the same way - the emulator is frame-stepped
                and the ROM's own auto-repeat is not what reads these.

A shortest path is taken on that grid for every character, so the scripts are
mechanical and reproducible: regenerate them from this file and they are
byte-identical.

THE SUBMIT IS THE BODY'S FIRST START, NOT THE ENTRY'S LAST CHARACTER.  Measured
2026-09-26: an entry script followed by an idle body leaves the session on the
password screen for its whole 120 seconds - engine mode `$1D` = 1 at every
sample and `$0074` = 0 - for all six passwords it was tried on, and the traced
recordings of the real sweep show `$0074` being written in the second that
follows the body's opening Start.  So the entry types and stops, and
`defaults.body` submits: the pair is the selector, which is why
`mint-<value>.txt` is never run without the body this profile names.  It is
also why that body's opening Start cannot be edited away - the ROM will take a
password that is never submitted and say nothing about it, and a session that
sits on the keyboard still reports drawn keys of its own (see navigation.json's
comment on the value this profile dropped).

THE PASSWORD IS ALSO THE AREA CHECK, and this file decodes it.  The page
`docs/validation/slices/f1416-coverage-sweep-2026-09-26.md` and ADR-0239 section 4 ask
every value for a RAM check that proves the session reached the place it
claims, and for this game the byte to check is `$0074` - DataCrystal's
"Current level ($10 = Brinstar, $11 = Norfair, $12 = Kraid, $13 = Tourian, $14
= Ridley)".  Which of those the ROM writes is decided by the password itself:
the Metroid Password Format Guide's byte 8 (bits 64-71) **is** the level byte,
with bits 0-2 the level (0 Brinstar, 1 Norfair, 2 Kraid, 3 Tourian as
Norfair|Kraid, 4 Ridley) and bit 4 the `$10` DataCrystal's values all carry.
That is why a value whose byte 8 has bit 4 clear lands on `$01`-`$04` and not
on `$11`-`$14`, and why the guide can call bit 3 "the reset bit ... there are
16 possible values, but only 5 valid ones": byte 8 is the level byte, and
everything outside `$10`-`$14` is a combination the ROM's own table does not
use.

Measured, not assumed (probe `runs/f1417/metroid/probe/state/`, one traced
recording per value, `$0074` sampled every emulated second): the byte is
written once, when the ROM accepts the password, and never changes again - not
on a door, not on a death, not on the title screen - and it read
`(byte 8 & 7) | (byte 8 & 0x10)` in all eleven values that entered the game.
`$0074` is therefore not hand-typed in this file: `check_for()` derives it.

`$0074` cannot carry a level-0 value, though: Brinstar is 0 and the title
screen holds 0 too, so `$00` there proves nothing - which is exactly the byte a
session that never left the password screen reads.  The two Brinstar values
therefore check DataCrystal's `$1D` ("Engine mode (0 = game, 1 =
title/password)") instead, and `check_for()` says so per value.

The decode is what caught this profile's first bad value.  `4F---- ------
------ ------` was republished as a Brinstar code by one table; its 24
characters do not reconcile the format's checksum, the ROM's password routine
refuses it, and the recorded session sat on the password screen for its whole
120 seconds (engine mode `$1D` = 1 throughout, `$0074` = 0).  It is not a value
of this sweep, and `password_bytes()` refuses any string whose checksum does
not reconcile, so the same mistake cannot come back as a session that spends a
minute of capture budget being refused.
"""

import argparse
import json
import re
import sys
from collections import deque
from pathlib import Path

HERE = Path(__file__).resolve().parent

# --- the screen, as measured (see the module docstring) ----------------------
ALPHABET = ("0123456789"
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ"
            "abcdefghijklmnopqrstuvwxyz"
            "?-")
assert len(ALPHABET) == 64, len(ALPHABET)
ROWS, COLS = 5, 13
RING = ROWS * COLS          # 65 cells the cursor moves over, wrapping
SPACE_KEY = RING - 1        # the 65th cell: the space, drawn as no glyph (255)

# Four frames held, four frames released: one step of the grid cursor, or one
# character typed.  The measured minimum is two (see the docstring).
STEP_FRAMES = 4

# Power-on to the password screen.  Every press is separated from the screen it
# acts on by more than the frame the ROM spends reading a cursor move, and
# every step here was measured on this ROM (docstring).
BOOT = [
    ("420f -", "power-on to the title screen ('PUSH START BUTTON')"),
    ("10f T", "Start opens the START/CONTINUE screen"),
    ("90f -", ""),
    ("10f S", "Select moves the crosshair from START to CONTINUE"),
    ("60f -", ""),
    ("10f T", "Start confirms; 'PASS WORD PLEASE' follows"),
    ("120f -", "the keyboard, with the cursor on (0, 0) = '0'"),
]

# --- the password format, as far as the area check needs it ------------------
# The Metroid Password Format Guide (John David Ratliff, emuWorks,
# games.technoplaza.net/mpg/password.txt): 24 characters of a 64-character
# alphabet are 144 bits - 16 bytes of game data, the shift byte, and the
# checksum - and the data is rotated by the shift byte.  Section 4.5 numbers
# the bits with bit 0 at the least significant bit of byte 0; the pages these
# passwords come from never number them at all, so the numbering is only used
# here for the shift and the checksum, which are the parts the guide fixes
# exactly and the ROM verifies.


def password_bytes(password: str) -> bytes:
    """The 18 bytes a password encodes, or a refusal.

    The refusal is the point: the checksum is the ROM's own test, so a string
    that fails it is a string the game will not accept, however confidently a
    table republishes it.  Measured on this ROM 2026-09-26 - see the module
    docstring for the value it removed.
    """
    # Every space written here is a group separator, not a character: the space
    # IS one of the keyboard's 65 keys but it is the one that does not fit in
    # six bits (the guide gives it 255), so a 24-character password cannot
    # contain one. A string that does loses a character here and is refused by
    # the length check rather than decoded into something else.
    chars = password.replace(" ", "")
    if len(chars) != 24:
        raise SystemExit(
            f'"{password}" is {len(chars)} characters once the group '
            "separators are dropped, and a Metroid password is 24 (four groups "
            "of six). A 25- or 23-character string is a transcription error, "
            "not a password.")
    bits = "".join(f"{index_of(ch):06b}" for ch in chars)
    raw = bytes(int(bits[i:i + 8], 2) for i in range(0, 144, 8))
    data, shift, checksum = raw[:16], raw[16], raw[17]
    # The guide words the decode as "rotate left and swap the least and most
    # significant bits", which is ambiguous; the direction below is the one
    # under which every published password in this table reconciles its own
    # checksum (the others do not).
    for _ in range(shift):
        value = int.from_bytes(data, "big")
        data = (((value << 1) | (value >> 127)) & ((1 << 128) - 1)) \
            .to_bytes(16, "big")
    if (sum(data) + shift) & 0xFF != checksum:
        raise SystemExit(
            f'"{password}" does not reconcile the password format\'s checksum '
            f"({(sum(data) + shift) & 0xFF:02X} computed, {checksum:02X} in the "
            "string), so this ROM's password routine refuses it - it would "
            "record a session that never leaves the password screen. Drop the "
            "value or re-read it from a source that shows the screen.")
    return data


LEVEL_NAMES = {0: "Brinstar", 1: "Norfair", 2: "Kraid's Lair", 3: "Tourian",
               4: "Ridley's Lair"}


def level_of(password: str) -> int:
    """The level the password starts Samus in, out of the format's byte 8.

    Bits 0-2 of that byte are the guide's "Start in Norfair / Kraid's Lair /
    Ridley's Lair" bits, and Tourian is Norfair|Kraid - the guide's own rule -
    so a level-0 value is Brinstar and 0-4 names the five areas in the order
    DataCrystal's `$10`-`$14` uses.
    """
    return password_bytes(password)[8] & 0x07


def check_for(password: str) -> dict:
    """The RAM check a session for this password has to pass, off its own final
    state: DataCrystal's address and the byte the ROM writes there.

    `$0074` is the published "Current level" byte and the password decides it
    (see the module docstring), so it is derived here and never typed.  A
    level-0 value is the exception, and it is a real one: Brinstar's byte is
    `$00`, the title screen's is `$00`, and a check that cannot fail is not
    evidence - so Brinstar checks the engine mode (`$1D`, 0 = game) instead,
    which is the byte that separates a session in the game from one still
    sitting on the password screen.
    """
    if level_of(password) == 0:
        return {"address": "001D", "expect": "00",
                "why": "engine mode: in the game, not on the password screen "
                       "(Brinstar's own $0074 is $00 either way)"}
    byte = password_bytes(password)[8]
    return {"address": "0074", "expect": f"{(byte & 0x07) | (byte & 0x10):02X}",
            "why": f"the level byte byte 8 carries: ${byte:02X}"}


# --- the passwords ----------------------------------------------------------
# `password` is the string with the screen's spacing restored: the game shows
# 24 characters as four groups of six, and the group separator is not typed.
# Only the 64 characters of ALPHABET may appear - the generator refuses any
# other, which is what an underscore or a space in a republished table means -
# and the checksum has to reconcile, which is the ROM's own test.
#
# The area and the RAM check are derived from the string, never typed beside
# it: `level_of()` and `check_for()`.
PASSWORDS = [
    {
        "name": "brinstar-suitless",
        "password": "000000 000020 000000 000020",
        "effect": "Suitless Samus in Brinstar with no upgrades, Energy Tanks or "
                  "Missiles.",
        "source": "Wikitroid, 'List of Metroid passwords' "
                  "(https://metroid.fandom.com/wiki/List_of_Metroid_passwords): "
                  "\"Samus will spawn suitless in Brinstar with no upgrades, "
                  "Energy Tanks, or Missiles.\" Also on StrategyWiki, "
                  "'Metroid/Passwords' (https://strategywiki.org/wiki/"
                  "Metroid/Passwords), where it is listed twice.",
    },
    {
        "name": "brinstar-kraid-ready",
        "password": "0Gz--- -m3e0G 00VvjS 3m00n?",
        "effect": "Brinstar with six Energy Tanks and 155 Missiles, described "
                  "as ready for Kraid's Lair. Republished with two different "
                  "area claims (Brinstar and Kraid's Lair), so this value's "
                  "area is the one the ROM itself reports and not the one the "
                  "table claims - see runs/f1417/metroid/RESULT.md.",
        "source": "Aggregated from the Metroid password tables at "
                  "retromaggedon.com ('Metroid (Nintendo Entertainment System) "
                  "Passwords') and IGN's NES cheats page ('Metroid Cheats'), "
                  "both of which carry this string.",
    },
    {
        "name": "norfair",
        "password": "JUSTIN BAILEY ------ ------",
        "effect": "Suitless Samus in Norfair with five empty Energy Tanks, 255 "
                  "Missiles, the Wave Beam, Long Beam, Bombs, High Jump and "
                  "Screw Attack.",
        "source": "Wikitroid, 'List of Metroid passwords' "
                  "(https://metroid.fandom.com/wiki/List_of_Metroid_passwords) "
                  "and StrategyWiki, 'Metroid/Passwords' "
                  "(https://strategywiki.org/wiki/Metroid/Passwords), both of "
                  "which list it verbatim. It is the game's best-known "
                  "password and both tables agree on the area.",
    },
    {
        "name": "norfair-novaria",
        "password": "ODDISH TAUROS MEWTWO VULPIX",
        "effect": "Suited Samus in Norfair without the Varia Suit, so the suit's "
                  "own palette differs from the Brinstar values above.",
        "source": "Wikitroid, 'List of Metroid passwords' "
                  "(https://metroid.fandom.com/wiki/List_of_Metroid_passwords): "
                  "\"Samus spawns in Norfair. However, she does not have Varia "
                  "Suit.\"",
    },
    {
        "name": "kraid",
        "password": "999999 999999 KKKKKK KKKKKK",
        "effect": "Suitless Samus in Kraid's Lair with 36 Missiles, one Energy "
                  "Tank, the Wave Beam without the Long Beam, the Screw Attack "
                  "without High Jump, and Bombs without the Morph Ball. The "
                  "table notes it also changes the ending.",
        "source": "Wikitroid, 'List of Metroid passwords' "
                  "(https://metroid.fandom.com/wiki/List_of_Metroid_passwords): "
                  "\"Samus will spawn suitless (pink leotard and brown hair) in "
                  "Kraid's Lair\". StrategyWiki, 'Metroid/Passwords', calls the "
                  "same string 'Hideout I'.",
    },
    {
        "name": "kraid-minimal",
        "password": "000000 000000 080h00 0000gu",
        "effect": "Missiles but no missile tanks in Kraid's Lair.",
        "source": "Aggregated from the Metroid password tables at "
                  "retromaggedon.com ('Metroid (Nintendo Entertainment System) "
                  "Passwords') and the Top Secret Passwords Nintendo player's "
                  "guide, both of which carry this string.",
    },
    {
        "name": "ridley",
        "password": "llv-?l z--mU4 --y000 00m03x",
        "effect": "Suitless Samus in Ridley's Lair with 255 Missiles, six Energy "
                  "Tanks and all items; every door but the boss door is open, "
                  "Kraid's statue is shot, and Kraid and Mother Brain are "
                  "already defeated.",
        "source": "Wikitroid, 'List of Metroid passwords' "
                  "(https://metroid.fandom.com/wiki/List_of_Metroid_passwords): "
                  "\"Samus will spawn suitless in Ridley's Lair with 255 "
                  "Missiles, six Energy Tanks, and all items.\"",
    },
    {
        "name": "ridley-entrance",
        "password": "00U--- -u0000 0AFw9Y 1800sb",
        "effect": "At the entrance to Ridley's Lair with four Energy Tanks, 68 "
                  "Missiles, the Wave Beam, High Jump Boots and Screw Attack.",
        "source": "Aggregated from the Metroid password tables at "
                  "retromaggedon.com ('Metroid (Nintendo Entertainment System) "
                  "Passwords') and IGN's NES cheats page ('Metroid Cheats'), "
                  "both of which carry this string.",
    },
    {
        "name": "tourian",
        "password": "M7---- --zOA0 2T-tfm a000d5",
        "effect": "Suited Samus in Tourian with 239 Missiles, every Energy Tank "
                  "the run can carry and all upgrades.",
        "source": "Wikitroid, 'List of Metroid passwords' "
                  "(https://metroid.fandom.com/wiki/List_of_Metroid_passwords): "
                  "\"Samus will spawn suited in Tourian with 239 Missiles, all "
                  "available Energy Tanks, and all upgrades.\" StrategyWiki, "
                  "'Metroid/Passwords', notes this is the one table entry that "
                  "uses the capital letter O rather than a zero.",
    },
    {
        "name": "tourian-best-end",
        "password": "X----- --N?WO dV-Gm9 W01GMI",
        "effect": "Tourian with 250 Missiles and every Energy Tank, all upgrades "
                  "except the Wave Beam; the table describes it as the easy "
                  "route to the best ending.",
        "source": "Wikitroid, 'List of Metroid passwords' "
                  "(https://metroid.fandom.com/wiki/List_of_Metroid_passwords): "
                  "\"Samus will spawn in Tourian with 250 Missiles ... This "
                  "code is for those who want to view the best ending\". "
                  "StrategyWiki, 'Metroid/Passwords', lists the same string for "
                  "its 'Hideout II' row.",
    },
    {
        "name": "tourian-suitless",
        "password": "E7GAGE RIDLEY MOTHER FUCKEJ",
        "effect": "Suitless Samus in Tourian with 82 Missiles, three Energy "
                  "Tanks, the Varia Suit and no Morph Ball. The table records "
                  "it as the playable counterpart of the 'ENGAGE RIDLEY MOTHER "
                  "FUCKER' string, whose start location is invalid - that one "
                  "is deliberately not a value of this sweep.",
        "source": "Wikitroid, 'List of Metroid passwords' "
                  "(https://metroid.fandom.com/wiki/List_of_Metroid_passwords): "
                  "\"A playable version of the Engage Ridley password. The "
                  "player will be in Tourian, suitless\". The page's companion "
                  "article 'ENGAGE RIDLEY MOTHER FUCKER' documents the invalid "
                  "start location that makes the original unusable.",
    },
]


def index_of(ch: str) -> int:
    """The alphabet index of one character, refused when it is not one."""
    try:
        return ALPHABET.index(ch)
    except ValueError:
        raise SystemExit(
            f'"{ch}" is not a character of Metroid\'s password keyboard. The 64 '
            f"characters that fit in six bits are 0-9, A-Z, a-z, '?' and '-': a "
            "space, an underscore or any other punctuation in a republished "
            "password table means the table was reformatted and the string has "
            "to be re-read from a source that shows the screen.")


def cell(index: int):
    """The (row, column) the screen shows for a cursor index (see the notes)."""
    return divmod(index, COLS)


def shortest_path(start: int, goal: int) -> str:
    """The presses that walk the keyboard cursor from `start` to `goal`.

    Both are cursor **indices** on the 65-cell ring (see the module docstring):
    Right/Left are +1/-1 and Up/Down are -13/+13, modulo 65, and cell 64 is a
    place the cursor can sit but no character lives there.  A breadth-first
    search over the ring is exact, and the first move found in the fixed order
    U, D, L, R makes the result deterministic.
    """
    moves = (("U", -13), ("D", 13), ("L", -1), ("R", 1))
    seen = {start: None}
    queue = deque([start])
    while queue:
        cur = queue.popleft()
        if cur == goal:
            out = []
            while seen[cur] is not None:
                prev, button = seen[cur]
                out.append(button)
                cur = prev
            return "".join(reversed(out))
        for button, step in moves:
            nxt = (cur + step) % RING
            if nxt in seen:
                continue
            seen[nxt] = (cur, button)
            queue.append(nxt)
    raise AssertionError(f"no path from {start} to {goal}")


def entry_lines(password: str) -> list:
    """The input script lines that type one password from the title screen."""
    chars = password.replace(" ", "")
    if len(chars) != 24:
        raise SystemExit(
            f'"{password}" is {len(chars)} characters once the group '
            "separators are dropped, and a Metroid password is 24 (four groups "
            "of six). A 25- or 23-character string is a transcription error, "
            "not a password.")
    # A step's explanation is a comment line of its own, never a trailing one:
    # headless_record's parser reads the button field to the end of the line and
    # refuses anything it does not know, so "420f -  # why" is the error
    # '"-" is not a known button' and the whole session dies on line 4.
    out = ["# --- boot to the password screen"]
    for line, why in BOOT:
        if why:
            out.append(f"# {why}")
        out.append(line)
    out.append("# --- type 24 characters: move the cursor, then A. A types the")
    out.append("# character under the cursor and leaves the cursor where it is.")
    cursor = 0
    for n, ch in enumerate(chars, 1):
        target = index_of(ch)
        path = shortest_path(cursor, target)
        r, c = cell(target)
        out.append(f"# character {n}/24: '{ch}' is index {target}, row {r}, "
                   f"column {c}"
                   + (f" - {len(path)} step(s): {path}" if path else
                      " - the cursor is already there"))
        for button in path:
            out.append(f"{STEP_FRAMES}f {button}")
            out.append(f"{STEP_FRAMES}f -")
        out.append(f"{STEP_FRAMES}f A")
        out.append(f"{STEP_FRAMES}f -")
        cursor = target
    # The entry ends here, and it does NOT submit: the ROM types what is typed
    # and waits.  The submit is a Start, and it is the first press of the
    # profile's body (`defaults.body`) - measured 2026-09-26, see the docstring.
    out.append("# --- 24 characters typed. The ROM does not check the password by")
    out.append("# itself: the submit is the Start the profile's body opens with.")
    out.append("120f -")
    assert_steps_parse(out)
    return out


# headless_record's own grammar: `HeadlessInputScript` reads "<count>f <buttons>"
# or "<count>s <buttons>", and the button field runs to the end of the line.
STEP_OK = re.compile(r"^\d+[fs] [UDLRABST-]+$")


def assert_steps_parse(lines: list) -> None:
    """Every step line has to be exactly what the recorder's parser accepts.

    Written after the first sweep of this profile died on line 4 of all twelve
    sessions - the boot steps carried a trailing `# why` and the parser read
    the comment as the button field ("'-' is not a known button"). A step's
    explanation is a comment line of its own; this is the check that says so
    mechanically instead of by discipline.
    """
    for line in lines:
        if line.startswith("#") or not line.strip():
            continue
        if not STEP_OK.match(line):
            raise SystemExit(
                f'generated step "{line}" is not "<count>(f|s) <buttons>". A '
                "trailing comment is the failure this catches: the recorder "
                "reads the button field to the end of the line.")


def render(entry: dict) -> str:
    check = check_for(entry["password"])
    return "\n".join([
        "# generated by scripts/stages/metroid/password-entry.py - do not edit",
        f"# value:    {entry['name']}",
        f"# password: {entry['password']}",
        f"# area:     {LEVEL_NAMES[level_of(entry['password'])]} - byte 8 of the "
        f"decoded password is ${password_bytes(entry['password'])[8]:02X}, "
        f"level nibble {level_of(entry['password'])}",
        f"# check:    ${check['address']} = {check['expect']} "
        f"({check['why']})",
        f"# source:   {' '.join(entry['source'].split())}",
        f"# entry length: {press_count(entry)} grid steps + 24 A presses",
        *entry_lines(entry["password"]),
    ]) + "\n"


def press_count(entry: dict) -> int:
    chars = entry["password"].replace(" ", "")
    cursor, steps = 0, 0
    for ch in chars:
        target = index_of(ch)
        steps += len(shortest_path(cursor, target))
        cursor = target
    return steps


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--verify", action="store_true",
                    help="regenerate in memory and fail when a file on disk "
                         "differs from what this file would write")
    ap.add_argument("--list", action="store_true",
                    help="print each value's press count and entry length")
    args = ap.parse_args()

    for entry in PASSWORDS:
        entry["file"] = HERE / f"mint-{entry['name']}.txt"

    if args.list:
        for e in PASSWORDS:
            frames = 0
            for line in render(e).splitlines():
                if line.startswith("#"):
                    continue
                n, unit = line.split()[0][:-1], line.split()[0][-1]
                frames += int(n) if unit == "f" else round(int(n) * 60.0988)
            check = check_for(e["password"])
            print(f"{e['name']:<22} {press_count(e):>3} grid steps  "
                  f"{frames:>5} frames ({frames / 60.0988:5.1f} s)  "
                  f"{LEVEL_NAMES[level_of(e['password'])]:<12} "
                  f"${check['address']}={check['expect']}")
        return 0

    drift = []
    for e in PASSWORDS:
        want = render(e)
        have = e["file"].read_text() if e["file"].exists() else None
        if args.verify:
            if have != want:
                drift.append(e["file"])
        else:
            e["file"].write_text(want)
            print(f"wrote {e['file'].relative_to(HERE.parent.parent)}"
                  f"  ({press_count(e)} grid steps, "
                  f"{LEVEL_NAMES[level_of(e['password'])]})")

    if args.verify:
        for problem in profile_drift():
            print(problem, file=sys.stderr)
            drift.append(HERE / "navigation.json")
        if drift:
            for p in sorted(set(drift)):
                print(f"stale: {p}", file=sys.stderr)
            print("re-run password-entry.py without --verify", file=sys.stderr)
            return 1
        print(f"{len(PASSWORDS)} entry scripts and navigation.json match the "
              "generator")
    return 0


def profile_drift() -> list:
    """`navigation.json` and this table have to agree, or the sweep types one
    password under another's name - which the ramCheck catches only as a failed
    session, after the capture budget is spent. So the profile's values are
    read back here and compared field by field.
    """
    path = HERE / "navigation.json"
    if not path.exists():
        return [f"{path} does not exist"]
    profile = json.loads(path.read_text())
    values = {v["name"]: v for v in profile["navigation"]["values"]}
    out = []
    for e in PASSWORDS:
        v = values.get(e["name"])
        if v is None:
            out.append(f"navigation.json has no value \"{e['name']}\"")
            continue
        for key, want in (("_password", e["password"]),
                          ("entry", f"mint-{e['name']}.txt")):
            if v.get(key) != want:
                out.append(f"{e['name']}.{key} is {v.get(key)!r}, "
                           f"the generator says {want!r}")
        want_check = check_for(e["password"])
        check = (v.get("ramCheck") or {})
        if (str(check.get("address", "")).upper(),
                str(check.get("expect", "")).upper()) \
                != (want_check["address"], want_check["expect"]):
            out.append(
                f"{e['name']}.ramCheck is {check!r}, the generator says "
                f"${want_check['address']} = {want_check['expect']} "
                f"({want_check['why']}) - the check is derived from the "
                "password's own level byte, not typed beside it")
    for name in values:
        if name not in {e["name"] for e in PASSWORDS}:
            out.append(f"navigation.json value \"{name}\" has no entry in the "
                       "generator")
    return out


if __name__ == "__main__":
    sys.exit(main())
