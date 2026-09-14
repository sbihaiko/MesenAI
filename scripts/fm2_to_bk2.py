#!/usr/bin/env python3
"""fm2_to_bk2 — convert an FCEUX `.fm2` NES input movie into a BizHawk-shaped `.bk2`.

Our Core has no `.fm2` reader at all. `MovieManager::Play` only ever considers a
file whose first two bytes are `PK`, and it picks `BizHawkMovie` when the zip
holds an entry named `Input Log.txt` — so the only way a TASVideos NES movie can
be played here is to re-shape it as a bk2. That is all this tool does: it is a
transcoder, not an emulator, and it refuses every input it cannot transcode
exactly rather than emitting a movie that plays for a while and then desyncs.

    scripts/fm2_to_bk2.py <movie.fm2> --rom <rom.nes> -o <out.bk2>
    scripts/fm2_to_bk2.py <movie.fm2> --rom <rom.nes> --verify

`--verify` converts into a temporary file, reads the result back and checks that
every frame's pressed-button set survives the round trip, without writing the
output.

## The two formats, and the one permutation between them

fm2 (https://fceux.com/web/help/fm2.html) is plain ASCII: `key value` header
lines until the first line starting with `|`, then one line per frame shaped
`|commands|port0|port1|port2|`. `commands` is a decimal bitfield (1 soft reset,
2 hard reset, 4/8 FDS, 16 VS coin). A gamepad port is eight characters in the
mnemonic order `RLDUTSBA` — Right, Left, Down, Up, sTart, Select, B, A — where
anything but `.` or a space is pressed. A port the header declares `0` is an
*empty* field, not eight dots.

bk2 (https://tasvideos.org/Bizhawk/BK2Format) is a zip holding `Header.txt` and
`Input Log.txt`. The input log's first line is a `LogKey:` line (documentation
only), then one `|`-delimited line per frame.

What matters for playback here is neither spec's letters but our Core's poll
order, which is positional:

  * `BaseControlManager` registers the system device first
    (`SystemActionManager::GetKeyNames()` -> `"RP"`, Reset and Power), then the
    ports; `NesController::GetKeyNames()` -> `"UDLRSsBA"` (Up Down Left Right
    Start select B A, lowercase `s` for select).
  * `BizHawkMovie::Play` applies **no** column reordering for NES — it splits
    each `|` line and hands the columns straight to the poll order. Only SNES
    and GBA get a `Convert*Input` pass.
  * `BaseControlDevice::SetTextState` treats any character that is not `.` and
    not `:` as pressed, so the letters are documentation and *position* is the
    whole contract.

So a port field is a positional permutation from `RLDUTSBA` to `UDLRSsBA`. It is
derived below from the two named orders rather than written out, and asserted at
import: a silently wrong permutation produces a movie that plays and then
desyncs, which is the expensive failure this tool exists to avoid.

## The power-on row is dropped

The two formats do not agree on which emulated frame the *first* input row
belongs to, and the disagreement is exactly one row. Our Core polls once before
the first frame runs at all — `Emulator::LoadRom` calls
`BaseControlManager::UpdateInputState()` (Emulator.cpp) after the movie's
`PowerCycle`, so poll counter 0 is spent before the PPU has drawn a scanline —
while FCEUX applies its first log row to the first frame it emulates. Emitting
the fm2's rows one-for-one therefore delivers every input one frame late.

Measured, not reasoned: replaying the Zelda "all items" publication through
`scripts/headless_record` one-for-one, Link walks into the first cave but never
touches the sword (a pickup that is decided by a single frame of overlap), and
the run is dead inside a minute. Dropping the fm2's first row — and only that —
puts the sword in the A slot and keeps the run in sync at 300 s (Level 4, 200
rupees) and beyond. Shifting the other way, or by two rows, is worse in both
directions: +1 loses the sword the same way, -2 never leaves the name-entry
screen.

So `FM2_POWER_ON_ROWS` rows are dropped from the front of the input log, and a
dropped row that carries a button or a command is a **refusal** rather than a
silent loss. FCEUX writes a blank first row for every power-on recording, so
the refusal should never fire on a real movie; if it ever does, the movie is
telling us the assumption is wrong.

## No trailing pipe

`StringUtilities::Split` (Utilities/StringUtilities.h) always pushes the
remainder after the last delimiter, so `BizHawkMovie`'s
`Split(line.substr(1), '|')` turns a BizHawk-style `|..|..|..|` into **four**
columns, the last one empty — the behaviour reported at
https://forums.nesdev.org/viewtopic.php?t=13844&start=270. In a release build
the spurious column is harmless (`MesenMovie::SetInput` resets `_deviceIndex` to
0 at the start of every poll row, so the extra column is simply never read), but
a debug build trips `assert(_deviceIndex == 0)` there, because the index never
wraps. We therefore emit **no** trailing pipe: `|RP|UDLRSsBA|UDLRSsBA` splits
into exactly the three columns the poll order consumes.

## What is refused, and why refusing is the point

A movie that starts from a savestate, an FDS movie, a binary-log fm2, a
fourscore movie, a ROM whose checksum does not match, a malformed frame line —
each is refused by name with a non-zero exit. None of them can be converted
faithfully, and `BizHawkMovie::ApplySettings()` is a stub returning `true`, so
the bk2's `SyncSettings.json` is ignored entirely: nothing in the output can
carry a sync setting into the emulator. That is also why `palFlag 1` is reported
loudly instead of quietly handled — the caller has to set Mesen's region by
hand — and why `Comments.txt` records `NewPPU`, `RAMInitOption`, `RAMInitSeed`
and `palFlag` verbatim. Those four are exactly what a desync investigation needs
and dropping them silently would be the defect.

Python 3, standard library only.

Exit codes: 0 = converted (or verified), 1 = refused or a fatal error,
2 = usage error.
"""

import argparse
import base64
import binascii
import hashlib
import sys
import tempfile
import zipfile
from pathlib import Path

MOVIE_VERSION = "BizHawk v2.0.0"
PLATFORM = "NES"

# Rows dropped from the front of the fm2's input log — see "The power-on row is
# dropped" in the module docstring. One, measured against a real replay.
FM2_POWER_ON_ROWS = 1

# The fm2 mnemonic order, one entry per character of a gamepad field.
FM2_ORDER = ("Right", "Left", "Down", "Up", "Start", "Select", "B", "A")
# Our Core's NES poll order: NesController::GetKeyNames() == "UDLRSsBA".
CORE_ORDER = ("Up", "Down", "Left", "Right", "Start", "Select", "B", "A")
CORE_LETTERS = "UDLRSsBA"
# The console column: SystemActionManager::GetKeyNames() == "RP".
CONSOLE_ORDER = ("Reset", "Power")
CONSOLE_LETTERS = "RP"

# Position i of a converted field is position PERMUTATION[i] of the fm2 field.
PERMUTATION = tuple(FM2_ORDER.index(name) for name in CORE_ORDER)
assert PERMUTATION == (3, 2, 1, 0, 4, 5, 6, 7), PERMUTATION
assert len(CORE_LETTERS) == len(CORE_ORDER) == len(FM2_ORDER) == 8
assert len(CONSOLE_LETTERS) == len(CONSOLE_ORDER) == 2

# fm2 `commands` bits. Only the two reset bits have a column in our poll order.
CMD_SOFT_RESET = 1
CMD_HARD_RESET = 2
CMD_FDS_INSERT = 4
CMD_FDS_SELECT = 8
CMD_VS_COIN = 16

# The iNES header the fm2's romChecksum is *not* taken over.
INES_MAGIC = b"NES\x1a"
INES_HEADER_LEN = 16

# Header keys whose value decides whether the movie is convertible at all, or
# whose value a desync investigation will ask for. Everything else in the fm2
# header is metadata.
SYNC_KEYS = ("palFlag", "NewPPU", "RAMInitOption", "RAMInitSeed")


class Fm2Error(Exception):
    pass


# --- the ROM ----------------------------------------------------------------


def rom_hashes(path: Path):
    """(full-file sha1, header-stripped md5, header bytes stripped).

    The two hashes answer different questions and mixing them up is the single
    easiest way to get this tool wrong. FCEUX's `romChecksum` is the MD5 of the
    ROM *without* its 16-byte iNES header — the PRG and CHR data only — while a
    bk2 `SHA1` names the file. For roms/Zelda.nes those are
    d3f453931146e95b04a31647de80fdab (post-header MD5, which is what the Zelda
    fm2 carries) and f4095791987351be68674a9355b266bc / 3701381a82... for the
    whole file. A checker that hashed the whole file would reject every correct
    ROM it was ever given."""
    data = path.read_bytes()
    stripped = 0
    body = data
    if data[:4] == INES_MAGIC:
        if len(data) < INES_HEADER_LEN:
            raise Fm2Error(f"{path}: claims an iNES header but is only {len(data)} bytes long")
        body = data[INES_HEADER_LEN:]
        stripped = INES_HEADER_LEN
    return hashlib.sha1(data).hexdigest(), hashlib.md5(body).hexdigest(), stripped


def decode_rom_checksum(value: str) -> str:
    """The MD5 an fm2 `romChecksum` names, as lowercase hex.

    FCEUX writes `base64:<24 chars>`; some tools write the hex directly."""
    raw = value.strip()
    if raw.lower().startswith("base64:"):
        try:
            blob = base64.b64decode(raw[7:], validate=True)
        except (binascii.Error, ValueError) as e:
            raise Fm2Error(f"romChecksum {raw!r} is not valid base64 ({e})")
        if len(blob) != 16:
            raise Fm2Error(f"romChecksum {raw!r} decodes to {len(blob)} bytes, not the 16 of an MD5")
        return blob.hex()
    cleaned = raw.lower().replace("0x", "")
    if len(cleaned) == 32 and all(c in "0123456789abcdef" for c in cleaned):
        return cleaned
    raise Fm2Error(f"romChecksum {raw!r} is neither `base64:...` nor 32 hex digits")


# --- the fm2 header ---------------------------------------------------------


def parse_header(fh):
    """(header dict, comments list, the first input line already read).

    fm2 header lines are `key value`; a key may repeat (`comment`, `subtitle`),
    so those are collected in order and the dict keeps the last. Reading stops
    at the first line starting with `|`, which is already a frame and is handed
    back rather than pushed back onto the stream."""
    header = {}
    comments = []
    for line in fh:
        if line.startswith("|"):
            return header, comments, line
        stripped = line.strip()
        if not stripped:
            continue
        key, _, value = stripped.partition(" ")
        value = value.strip()
        if key in ("comment", "subtitle"):
            comments.append(f"{key} {value}")
        header[key] = value
    return header, comments, None


def author_from(comments):
    """The `comment author <name>` line's name, or None.

    An fm2 has no author field; TASVideos' convention is a comment line."""
    for entry in comments:
        if entry.startswith("comment author "):
            name = entry[len("comment author "):].strip()
            if name:
                return name
    return None


def game_name_from(rom_filename: str) -> str:
    """The basename of the fm2's `romFilename`, which is almost always a Windows
    path (`C:\\fceux 2.2.3\\roms\\...`), so both separators are cut."""
    base = rom_filename.replace("\\", "/").rsplit("/", 1)[-1].strip()
    for ext in (".nes", ".fds", ".unf", ".unif", ".nez"):
        if base.lower().endswith(ext):
            return base[: -len(ext)]
    return base


def refuse_unconvertible(header, fm2_path: Path, rom_path: Path, rom_md5: str):
    """Every reason this fm2 cannot be transcoded exactly, raised by name.

    Each of these would otherwise produce a bk2 that loads and desyncs, which is
    far more expensive to diagnose than a refusal at conversion time."""
    if "savestate" in header:
        raise Fm2Error(
            f"{fm2_path.name}: header key `savestate` — this movie does not start from power-on, "
            "and our Core's BizHawkMovie has no CoreState handling at all "
            "(BizHawkMovie::Play always calls PowerCycle). Nothing in a bk2 can carry the state.")
    if header.get("FDS", "0") not in ("0", ""):
        raise Fm2Error(
            f"{fm2_path.name}: header `FDS {header['FDS']}` — an FDS movie needs disk-change "
            "commands (fm2 command bits 4 and 8) that our NES poll order has no column for.")
    if header.get("binary", "0") not in ("0", ""):
        raise Fm2Error(
            f"{fm2_path.name}: header `binary {header['binary']}` — the input log is a binary "
            "blob, not the text frame lines this converter reads.")
    if header.get("fourscore", "0") not in ("0", ""):
        raise Fm2Error(
            f"{fm2_path.name}: header `fourscore {header['fourscore']}` — a Four Score movie lays "
            "its four pads out differently and that layout has not been verified against our "
            "Core's poll order, so converting it would be a guess.")
    if "romChecksum" not in header:
        raise Fm2Error(f"{fm2_path.name}: no `romChecksum` header line, so the ROM cannot be checked")
    want = decode_rom_checksum(header["romChecksum"])
    if want != rom_md5:
        raise Fm2Error(
            f"romChecksum mismatch: {fm2_path.name} names MD5 {want}, but {rom_path} hashes to "
            f"{rom_md5} with its 16-byte iNES header stripped (which is the ROM image FCEUX "
            "hashes). This is a different ROM revision or dump — converting it would desync.")


def declared_ports(header):
    """The `portN` declarations, as a list of booleans (True = a gamepad).

    A port declared `0` contributes an *empty* field to every frame line, not
    eight dots, so the count of fields on a line depends on this."""
    ports = []
    index = 0
    while f"port{index}" in header:
        ports.append(header[f"port{index}"].strip() not in ("0", ""))
        index += 1
    if not ports:
        # fm2 v1 files predate the portN keys; two gamepads is the only layout
        # they could have had.
        ports = [True, True]
    return ports


# --- frames -----------------------------------------------------------------


def split_frame(line: str, lineno: int, nports: int):
    """(commands, [port fields]) from one fm2 frame line.

    A frame line is `|commands|port0|...|portN|`: the leading and trailing pipes
    are delimiters, not fields, so the payload is what lies between them."""
    body = line.strip()
    if not body.startswith("|"):
        raise Fm2Error(f"line {lineno}: {body[:40]!r} does not start with `|`")
    if not body.endswith("|"):
        raise Fm2Error(f"line {lineno}: {body[:40]!r} does not end with `|`")
    fields = body[1:-1].split("|")
    if len(fields) != nports + 1:
        raise Fm2Error(
            f"line {lineno}: {len(fields)} field(s) between the pipes, expected {nports + 1} "
            f"(one command field plus {nports} declared port(s)): {body[:60]!r}")
    try:
        commands = int(fields[0].strip() or "0")
    except ValueError:
        raise Fm2Error(f"line {lineno}: command field {fields[0]!r} is not a decimal number")
    return commands, fields[1:]


def fm2_pressed(field: str):
    """The set of button names an fm2 gamepad field holds down.

    Anything that is not `.` and not a space is pressed (fm2 spec §"Input log")
    — FCEUX itself writes the mnemonic letter, but `X`, `1` or `#` all count."""
    return frozenset(FM2_ORDER[i] for i, c in enumerate(field) if c not in (".", " "))


def core_pressed(field: str):
    """The same, read back off a converted field in our Core's poll order.

    Mirrors BaseControlDevice::SetTextState: not `.` and not `:` is pressed."""
    return frozenset(CORE_ORDER[i] for i, c in enumerate(field) if c not in (".", ":"))


def convert_port(field: str, lineno: int, port: int, is_gamepad: bool) -> str:
    """One fm2 port field, re-ordered into our Core's NES poll order.

    A port declared `0` must be empty and stays empty: emitting eight dots for
    it would add a column the poll order does not have."""
    if not is_gamepad:
        if field.strip():
            raise Fm2Error(
                f"line {lineno}: port{port} is declared `0` in the header but its field is "
                f"{field!r}, not empty")
        return ""
    if len(field) != 8:
        raise Fm2Error(
            f"line {lineno}: port{port} field {field!r} is {len(field)} character(s) wide, but a "
            "gamepad field is exactly 8 (RLDUTSBA)")
    return "".join(
        CORE_LETTERS[i] if field[PERMUTATION[i]] not in (".", " ") else "."
        for i in range(8))


def refuse_a_loaded_power_on_row(commands: int, fields, ports, lineno: int):
    """The one thing dropping the power-on row could cost us.

    A dropped row that holds nothing costs nothing; a dropped row that holds a
    button or a reset would be input the emulator never sees, which is the
    silent-desync failure this tool exists to avoid. So it is a refusal."""
    pressed = sorted(
        name
        for field, is_gamepad in zip(fields, ports) if is_gamepad
        for name in fm2_pressed(field))
    if pressed:
        raise Fm2Error(
            f"line {lineno}: the fm2's first frame holds {', '.join(pressed)}, but that row is "
            "dropped (see \"The power-on row is dropped\" in this tool's docstring — our Core "
            "spends one poll before the first frame runs). Converting it would silently lose "
            "that input.")
    if commands:
        raise Fm2Error(
            f"line {lineno}: the fm2's first frame carries command bits {commands}, but that row "
            "is dropped (see \"The power-on row is dropped\" in this tool's docstring). "
            "Converting it would silently lose that command.")


def convert_commands(commands: int) -> str:
    """The leading console column, in SystemActionManager's `RP` order."""
    letters = []
    letters.append("R" if commands & CMD_SOFT_RESET else ".")
    letters.append("P" if commands & CMD_HARD_RESET else ".")
    return "".join(letters)


def convert_line(commands: int, fields, lineno: int, ports) -> str:
    """One `Input Log.txt` line. No trailing pipe — see the module docstring."""
    out = [convert_commands(commands)]
    for port, (field, is_gamepad) in enumerate(zip(fields, ports)):
        # A port declared `0` is still checked — it must be empty — even though
        # it contributes no column to the poll order.
        converted = convert_port(field, lineno, port, is_gamepad)
        if is_gamepad:
            out.append(converted)
    return "|" + "|".join(out)


def log_key(ports) -> str:
    """The documentation line BizHawk puts first in an input log. Our Core skips
    it (it does not start with `|`), so it is for humans and for BizHawk."""
    groups = ["#" + "|".join(CONSOLE_ORDER)]
    for port, is_gamepad in enumerate(ports):
        if is_gamepad:
            groups.append("|".join(f"P{port + 1} {name}" for name in CORE_ORDER))
    return "LogKey:" + "|".join(groups)


# --- output -----------------------------------------------------------------


def build_header_txt(header, comments, rom_sha1: str) -> str:
    author = author_from(comments)
    lines = [
        f"MovieVersion {MOVIE_VERSION}",
        f"Platform {PLATFORM}",
        f"GameName {game_name_from(header.get('romFilename', ''))}",
        f"SHA1 {rom_sha1.upper()}",
        f"rerecordCount {header.get('rerecordCount', '0')}",
        # BizHawkMovie::ApplySettings() is a stub returning true, so this is
        # documentation for a human, not something the emulator enforces.
        "StartsFromSavestate False",
        # Named honestly: the movie was recorded in FCEUX, not in a BizHawk
        # core. Our Core reads only `Platform` and `MovieVersion` from here.
        f"Core FCEUX {header.get('emuVersion', '?')}",
        "emuVersion MesenCE scripts/fm2_to_bk2.py",
    ]
    if author:
        lines.insert(4, f"Author {author}")
    return "\n".join(lines) + "\n"


def build_comments_txt(fm2_path: Path, header, comments, rom_path: Path,
                       rom_sha1: str, rom_md5: str, extra_commands) -> str:
    """The provenance record.

    Everything here is a value the conversion did *not* carry into the movie,
    because no bk2 field and no Core setting can receive it — our
    `BizHawkMovie::ApplySettings()` ignores `SyncSettings.json` outright. A
    desync investigation starts from this list, so losing it silently is the
    defect this file exists to prevent."""
    lines = [
        "Converted by scripts/fm2_to_bk2.py from an FCEUX fm2 input movie.",
        f"source: {fm2_path.name}",
        f"guid: {header.get('guid', '(none)')}",
        f"fm2 version: {header.get('version', '?')}   fm2 emuVersion: {header.get('emuVersion', '?')}",
        f"romFilename: {header.get('romFilename', '(none)')}",
        f"romChecksum: {header.get('romChecksum', '(none)')} -> MD5 {rom_md5}"
        " (the ROM with its 16-byte iNES header stripped)",
        f"rom: {rom_path.name}  SHA1 {rom_sha1}",
        f"row offset: the fm2's first {FM2_POWER_ON_ROWS} frame line(s) are dropped, so fm2 frame"
        f" N is input log row N-{FM2_POWER_ON_ROWS}. Our Core spends one poll before the first"
        " frame runs (Emulator::LoadRom calls UpdateInputState after the movie's PowerCycle),"
        " so a one-for-one log plays every input one frame late.",
        "",
        "Sync-relevant fm2 header values NOT carried into this bk2 — our Core's",
        "BizHawkMovie::ApplySettings() is a stub, so a bk2 cannot carry them and",
        "they must be set on the Mesen side by hand:",
    ]
    for key in SYNC_KEYS:
        lines.append(f"  {key} {header.get(key, '(absent)')}")
    if header.get("palFlag", "0") not in ("0", ""):
        lines.append("  ^ palFlag is set: this movie is PAL. Set Mesen's region to PAL before playing.")
    if extra_commands:
        lines.append("")
        lines.append("fm2 command bits with no column in our NES poll order, dropped by frame:")
        for bit, frames in sorted(extra_commands.items()):
            shown = ", ".join(str(f) for f in frames[:20])
            more = f", ... ({len(frames)} total)" if len(frames) > 20 else ""
            lines.append(f"  bit {bit}: {shown}{more}")
    if comments:
        lines.append("")
        lines.append("fm2 comment/subtitle lines:")
        lines.extend(f"  {c}" for c in comments)
    return "\n".join(lines) + "\n"


def convert(fm2_path: Path, rom_path: Path, out_path: Path):
    """Write `out_path` and return the statistics.

    Streaming in both directions: the fm2 is read a line at a time and each
    converted line is written straight into the open zip entry, so a 2.5 MB /
    114913-line movie never exists in memory as one string."""
    rom_sha1, rom_md5, _stripped = rom_hashes(rom_path)
    with fm2_path.open("r", encoding="utf-8", errors="replace") as fh:
        header, comments, first = parse_header(fh)
        refuse_unconvertible(header, fm2_path, rom_path, rom_md5)
        ports = declared_ports(header)
        stats = {
            "frames": 0,
            "droppedRows": 0,
            "framesWithInput": 0,
            "framesWithCommand": 0,
            "resetFrames": [],
            "extraCommands": {},
            "header": header,
            "comments": comments,
            "ports": ports,
            "romSha1": rom_sha1,
            "romMd5": rom_md5,
            "pal": header.get("palFlag", "0") not in ("0", ""),
        }
        out_path.parent.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(out_path, "w", zipfile.ZIP_DEFLATED) as zf:
            zf.writestr("Header.txt", build_header_txt(header, comments, rom_sha1))
            with zf.open("Input Log.txt", "w") as log:
                log.write((log_key(ports) + "\n").encode("utf-8"))
                lineno = 0
                line = first
                while line is not None:
                    lineno += 1
                    commands, fields = split_frame(line, lineno, len(ports))
                    if lineno <= FM2_POWER_ON_ROWS:
                        # Parsed and checked like any other row, then dropped:
                        # see "The power-on row is dropped" in the docstring.
                        for port, (field, is_gamepad) in enumerate(zip(fields, ports)):
                            convert_port(field, lineno, port, is_gamepad)
                        refuse_a_loaded_power_on_row(commands, fields, ports, lineno)
                        stats["droppedRows"] += 1
                        line = next(fh, None)
                        continue
                    # The index of the row this frame becomes in the input log,
                    # which is what a desync investigation counts in.
                    row = lineno - 1 - FM2_POWER_ON_ROWS
                    log.write((convert_line(commands, fields, lineno, ports) + "\n").encode("utf-8"))
                    stats["frames"] += 1
                    if any(fm2_pressed(f) for f, g in zip(fields, ports) if g):
                        stats["framesWithInput"] += 1
                    if commands & (CMD_SOFT_RESET | CMD_HARD_RESET):
                        stats["framesWithCommand"] += 1
                        stats["resetFrames"].append(row)
                    for bit in (CMD_FDS_INSERT, CMD_FDS_SELECT, CMD_VS_COIN):
                        if commands & bit:
                            stats["extraCommands"].setdefault(bit, []).append(row)
                    line = next(fh, None)
            zf.writestr("Comments.txt", build_comments_txt(
                fm2_path, header, comments, rom_path, rom_sha1, rom_md5, stats["extraCommands"]))
    if not stats["frames"]:
        # A zip with an empty input log would load and stop on frame 0, which
        # reads as "the movie ended" rather than as a bad conversion.
        out_path.unlink(missing_ok=True)
        if stats["droppedRows"]:
            raise Fm2Error(
                f"{fm2_path.name}: {stats['droppedRows']} frame line(s) and nothing else — the "
                "power-on row is dropped, so there is no input left to play.")
        raise Fm2Error(f"{fm2_path.name}: no frame lines at all — is this an fm2?")
    return stats


# --- the round trip ---------------------------------------------------------


def round_trip(fm2_path: Path, bk2_path: Path, ports, dropped=FM2_POWER_ON_ROWS):
    """Read the bk2 back and check every frame against the fm2 it came from.

    The comparison is by *button name*, derived on each side from that side's
    own order, so it fails if the permutation is wrong in either direction —
    comparing the strings would only prove the converter agrees with itself.

    It also re-derives the row offset: fm2 frame N must be input log row
    N - `dropped`, so a converter that stopped dropping the power-on row, or
    dropped two, shows up here as a wall of mismatches rather than as a desync
    forty seconds into a replay."""
    with zipfile.ZipFile(bk2_path) as zf:
        names = zf.namelist()
        with zf.open("Input Log.txt") as fh:
            log = fh.read().decode("utf-8").splitlines()
    if not log or not log[0].startswith("LogKey:"):
        raise Fm2Error(f"{bk2_path}: the input log does not open with a LogKey: line")
    rows = [line for line in log if line.startswith("|")]
    mismatches = []
    with fm2_path.open("r", encoding="utf-8", errors="replace") as fh:
        _header, _comments, first = parse_header(fh)
        lineno = 0
        line = first
        while line is not None:
            row = lineno - dropped
            if row < 0:
                # The dropped power-on row: it must have held nothing, or the
                # conversion lost input.
                commands, fields = split_frame(line, lineno + 1, len(ports))
                if commands or any(fm2_pressed(f) for f, g in zip(fields, ports) if g):
                    mismatches.append(f"frame {lineno}: dropped power-on row is not empty")
                lineno += 1
                line = next(fh, None)
                continue
            if row >= len(rows):
                mismatches.append(f"frame {lineno}: missing from the input log")
                break
            commands, fields = split_frame(line, lineno + 1, len(ports))
            cols = rows[row][1:].split("|")
            want_ports = [fm2_pressed(f) for f, g in zip(fields, ports) if g]
            got_ports = [core_pressed(c) for c in cols[1:]]
            if want_ports != got_ports:
                mismatches.append(f"frame {lineno}: {want_ports} != {got_ports}")
            want_cmd = frozenset(
                n for n, bit in zip(CONSOLE_ORDER, (CMD_SOFT_RESET, CMD_HARD_RESET))
                if commands & bit)
            got_cmd = frozenset(
                CONSOLE_ORDER[i] for i, c in enumerate(cols[0]) if c not in (".", ":"))
            if want_cmd != got_cmd:
                mismatches.append(f"frame {lineno}: commands {sorted(want_cmd)} != {sorted(got_cmd)}")
            if len(mismatches) > 10:
                break
            lineno += 1
            line = next(fh, None)
    return {
        "entries": names,
        "frames": len(rows),
        "droppedRows": dropped,
        "mismatches": mismatches,
        # The failure the nesdev report warns about: a trailing `|` makes
        # StringUtilities::Split hand the poll order one column too many.
        "trailingPipe": any(r.endswith("|") for r in rows),
    }


# --- driver -----------------------------------------------------------------


def report(stats, quiet=False):
    if quiet:
        return
    header = stats["header"]
    pct = 100.0 * stats["framesWithInput"] / stats["frames"] if stats["frames"] else 0.0
    print(f"input log rows: {stats['frames']} "
          f"(fm2 frame N becomes row N-{stats['droppedRows']}; the power-on row is dropped)")
    print(f"frames with any button held: {stats['framesWithInput']} ({pct:.1f}%)")
    print(f"frames carrying a reset/power command: {stats['framesWithCommand']}"
          + (f" (at {', '.join(str(f) for f in stats['resetFrames'][:20])})"
             if stats["resetFrames"] else ""))
    ports = ", ".join(f"port{i}={'gamepad' if g else 'none'}" for i, g in enumerate(stats["ports"]))
    print(f"ports: {ports}")
    print("sync-relevant fm2 header values (NOT carried into the bk2 — set them by hand):")
    for key in SYNC_KEYS:
        print(f"  {key} {header.get(key, '(absent)')}")
    print(f"  rerecordCount {header.get('rerecordCount', '(absent)')}")
    print(f"  guid {header.get('guid', '(absent)')}")
    for bit, frames in sorted(stats["extraCommands"].items()):
        print(f"warning: fm2 command bit {bit} on {len(frames)} frame(s) has no column in our NES "
              f"poll order and was dropped (recorded in Comments.txt)", file=sys.stderr)
    if stats["pal"]:
        print("", file=sys.stderr)
        print("*** palFlag 1: this movie is PAL. Our Core's BizHawkMovie::ApplySettings() is a "
              "stub, so the bk2 cannot set the region — set Mesen's NES region to PAL by hand "
              "before playing, or it will desync immediately. ***", file=sys.stderr)


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(
        prog="fm2_to_bk2.py",
        description="Convert an FCEUX .fm2 NES input movie into a BizHawk-shaped .bk2.",
        formatter_class=argparse.RawDescriptionHelpFormatter, epilog=__doc__)
    ap.add_argument("fm2", help="the source .fm2 movie")
    ap.add_argument("--rom", required=True, help="the .nes ROM the movie targets")
    ap.add_argument("-o", "--out", metavar="OUT.BK2", help="the .bk2 to write")
    ap.add_argument("--verify", action="store_true",
                    help="convert into a temporary file and check the round trip, writing nothing")
    ap.add_argument("--quiet", action="store_true")
    args = ap.parse_args(argv)

    if not args.verify and not args.out:
        print("error: pass -o <out.bk2>, or --verify to convert into a temporary file", file=sys.stderr)
        return 2

    fm2_path = Path(args.fm2)
    rom_path = Path(args.rom)

    try:
        if args.verify:
            with tempfile.TemporaryDirectory(prefix="fm2_to_bk2.") as td:
                tmp = Path(td) / "verify.bk2"
                stats = convert(fm2_path, rom_path, tmp)
                result = round_trip(fm2_path, tmp, stats["ports"])
                report(stats, args.quiet)
                if not args.quiet:
                    print(f"verify: zip entries {result['entries']}")
                    print(f"verify: {result['frames']} input log row(s), "
                          f"{len(result['mismatches'])} mismatch(es), "
                          f"trailing pipe {result['trailingPipe']}")
                if result["frames"] != stats["frames"] or result["mismatches"] or result["trailingPipe"]:
                    print("verify: FAILED", file=sys.stderr)
                    for m in result["mismatches"][:10]:
                        print(f"  {m}", file=sys.stderr)
                    return 1
                if not args.quiet:
                    print("verify: OK — every frame's pressed-button set survives the round trip")
            return 0
        out_path = Path(args.out)
        stats = convert(fm2_path, rom_path, out_path)
        report(stats, args.quiet)
        if not args.quiet:
            print(f"wrote {out_path} ({out_path.stat().st_size} bytes)")
    except (Fm2Error, OSError, ValueError) as e:
        print(f"error: {e}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
