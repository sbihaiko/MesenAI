#!/usr/bin/env python3
"""Python port of the Core's cheat decoders (ADR-0248 section 3, slice R.3).

`Core/Shared/CheatManager.cpp` turns a cheat code into an address, a value and
an optional compare (`InternalCheatCode`). The cheat-submitted workflow has no
Core to call, so this module mirrors those converters for the consoles the
product ships (NES, Game Boy, Master System): every regex, bit table and quirk
is copied, including the ones that look wrong (a Pro Action Rocky code reports
`NesCustom` as its type, as the Core does).

Its parity with the Core is a test, not a claim:
scripts/test_cheat_decoder_parity.py builds scripts/cheat_decode_dump.cpp
against the unmodified CheatManager.cpp and compares both decoders over the
9 829 bundled codes of UI/Dependencies/Internal/CheatDb.Nes.json plus a
deterministic sample for every cheat type here. A mismatch fails that test,
never a submission.

SNES and PC Engine types exist in the Core's `CheatType` but are not product
consoles on `main` (docs/roadmap/AGENTS.md), so they are not ported.

Stdlib only.
"""
from __future__ import annotations

import re
from collections import namedtuple

# CheatType (Core/Shared/CheatManager.h) - numeric values matter: the parity
# harness passes them to the Core as they are.
NES_GAME_GENIE = 0
NES_PRO_ACTION_ROCKY = 1
NES_CUSTOM = 2
GB_GAME_GENIE = 3
GB_GAME_SHARK = 4
SMS_PRO_ACTION_REPLAY = 9
SMS_GAME_GENIE = 10

TYPE_NAMES = {
    NES_GAME_GENIE: "NES Game Genie",
    NES_PRO_ACTION_ROCKY: "NES Pro Action Rocky",
    NES_CUSTOM: "NES custom XXXX:YY[:ZZ]",
    GB_GAME_GENIE: "Game Boy Game Genie",
    GB_GAME_SHARK: "Game Boy GameShark",
    SMS_PRO_ACTION_REPLAY: "Master System Pro Action Replay",
    SMS_GAME_GENIE: "Master System Game Genie",
}

# CpuType (Core/Shared/CpuType.h).
CPU_GAMEBOY = 7
CPU_NES = 8
CPU_SMS = 10

# The cheat types each product console decodes (ADR-0248 section 3).
CONSOLE_TYPES = {
    "nes": (NES_GAME_GENIE, NES_PRO_ACTION_ROCKY, NES_CUSTOM),
    "gb": (GB_GAME_GENIE, GB_GAME_SHARK),
    "gbc": (GB_GAME_GENIE, GB_GAME_SHARK),
    "sms": (SMS_PRO_ACTION_REPLAY, SMS_GAME_GENIE),
}

# The UI's InteropCheatCode copies at most 15 bytes into the Core's char[16].
INTEROP_CODE_BYTES = 15

Decoded = namedtuple("Decoded", "type cpu address value compare is_ram is_abs mem_type")

# ASCII-only case folding, as std::regex_constants::icase on bytes: without
# re.ASCII, Python would also fold U+017F and U+212A onto S and K.
_I = re.IGNORECASE | re.ASCII
_NES_GG_RE = re.compile(r"^([APZLGITYEOXUKSVN]{6})|([APZLGITYEOXUKSVN]{8})$", _I)
_HEX8_RE = re.compile(r"^[a-f0-9]{8}$", _I)
_NES_CUSTOM_RE = re.compile(r"^[a-f0-9]{4}:[a-f0-9]{2}(:[a-f0-9]{2}){0,1}$", _I)
_GG_DASHED_RE = re.compile(r"^[a-f0-9]{3}-[a-f0-9]{3}(-[a-f0-9]{3}){0,1}$", _I)

_GG_LETTERS = "APZLGITYEOXUKSVN"


def _from_hex(text):
    """HexUtilities::FromHex: accumulate nibbles in a 32-bit int, skipping
    any character that is not a hex digit."""
    value = 0
    for c in text:
        value = (value << 4) & 0xFFFFFFFF
        if c in "0123456789abcdefABCDEF":
            value |= int(c, 16)
    return value


def _full(regex, code):
    # std::regex_match: the whole string must match the pattern.
    return regex.fullmatch(code) is not None


def _nes_game_genie(code):
    if not _full(_NES_GG_RE, code):
        return None

    def decode_value(raw, bits):
        result = 0
        for bit in bits:
            result = (result << 1) | ((raw >> bit) & 0x01)
        return result

    address_bits = (14, 13, 12, 19, 22, 21, 20, 7, 10, 9, 8, 15, 18, 17, 16)
    value_bits = [3, 6, 5, 4, 23, 2, 1, 0]
    raw = 0
    for i, c in enumerate(code):
        raw |= _GG_LETTERS.find(c.upper()) << (i * 4)
    raw &= 0xFFFFFFFF

    compare = -1
    if len(code) == 8:
        # Bit 5 of the value is stored in a different location for 8-character codes.
        value_bits[4] = 31
        compare = decode_value(raw, (27, 30, 29, 28, 23, 26, 25, 24))

    address = decode_value(raw, address_bits) + 0x8000
    value = decode_value(raw, value_bits)
    return Decoded(NES_GAME_GENIE, CPU_NES, address, value, compare, False, False, "-")


_PAR_SHIFTS = (
    3, 13, 14, 1, 6, 9, 5, 0, 12, 7, 2, 8, 10, 11, 4,  # address
    19, 21, 23, 22, 20, 17, 16, 18,  # compare
    29, 31, 24, 26, 25, 30, 27, 28,  # value
)


def _nes_pro_action_rocky(code):
    if not _full(_HEX8_RE, code):
        return None
    key = 0x7E5EE93A
    xor_value = 0x5C184B91
    par = _from_hex(code) >> 1  # bit 0 is not used
    result = 0
    for i in range(30, -1, -1):
        if (((key ^ par) >> 30) & 0x01) != 0:
            result |= 1 << _PAR_SHIFTS[i]
            key ^= xor_value
        par = (par << 1) & 0xFFFFFFFF
        key = (key << 1) & 0xFFFFFFFF
    # The Core reports this decode as NesCustom, not NesProActionRocky.
    return Decoded(NES_CUSTOM, CPU_NES, (result & 0x7FFF) + 0x8000, (result >> 24) & 0xFF,
                   (result >> 16) & 0xFF, False, False, "-")


def _nes_custom(code):
    if not _full(_NES_CUSTOM_RE, code):
        return None
    parts = code.split(":")
    compare = _from_hex(parts[2]) if len(parts) == 3 else -1
    return Decoded(NES_CUSTOM, CPU_NES, _from_hex(parts[0]), _from_hex(parts[1]) & 0xFF, compare, False, False, "-")


def _dashed_game_genie(code, cheat_type, cpu):
    """GB and SMS Game Genie share one converter body in the Core."""
    if not _full(_GG_DASHED_RE, code):
        return None
    value = _from_hex(code[0:2]) & 0xFF
    compare = -1
    if len(code) > 7:
        raw = _from_hex(code[8] + code[10]) & 0xFF
        compare = (((raw >> 2) | ((raw & 0x03) << 6)) ^ 0xBA) & 0xFF
    address = (_from_hex(code[6] + code[2] + code[4] + code[5]) ^ 0xF000) & 0xFFFF
    return Decoded(cheat_type, cpu, address, value, compare, False, False, "-")


def _gb_game_shark(code):
    if not _full(_HEX8_RE, code):
        return None
    code_type = _from_hex(code) >> 24
    value = _from_hex(code[2:4]) & 0xFF
    address = _from_hex(code[6:8] + code[4:6]) & 0xFFFF
    if code_type >= 0x80:
        bank = code_type & 0x0F
        if 0xA000 <= address < 0xC000:
            return Decoded(GB_GAME_SHARK, CPU_GAMEBOY, bank * 0x2000 + address - 0xA000, value, -1, True, True, "GbCartRam")
        if 0xD000 <= address < 0xE000:
            return Decoded(GB_GAME_SHARK, CPU_GAMEBOY, bank * 0x1000 + address - 0xD000, value, -1, True, True, "GbWorkRam")
        if 0xC000 <= address < 0xD000:
            return Decoded(GB_GAME_SHARK, CPU_GAMEBOY, address - 0xC000, value, -1, True, True, "GbWorkRam")
        return None
    if code_type == 0x01:
        return Decoded(GB_GAME_SHARK, CPU_GAMEBOY, address, value, -1, True, False, "-")
    return None


def _sms_pro_action_replay(code):
    if not _full(_HEX8_RE, code):
        return None
    raw = _from_hex(code)
    return Decoded(SMS_PRO_ACTION_REPLAY, CPU_SMS, (raw >> 8) & 0xFFFF, raw & 0xFF, -1, True, False, "-")


_CONVERTERS = {
    NES_GAME_GENIE: _nes_game_genie,
    NES_PRO_ACTION_ROCKY: _nes_pro_action_rocky,
    NES_CUSTOM: _nes_custom,
    GB_GAME_GENIE: lambda c: _dashed_game_genie(c, GB_GAME_GENIE, CPU_GAMEBOY),
    GB_GAME_SHARK: _gb_game_shark,
    SMS_PRO_ACTION_REPLAY: _sms_pro_action_replay,
    SMS_GAME_GENIE: lambda c: _dashed_game_genie(c, SMS_GAME_GENIE, CPU_SMS),
}


def decode(cheat_type, code):
    """CheatManager::GetConvertedCheat for one code: a Decoded, or None when
    the Core would refuse it. The code is cut at the interop's 15 bytes first,
    exactly as the UI hands it to the Core."""
    converter = _CONVERTERS.get(cheat_type)
    if converter is None:
        raise ValueError(f"cheat type {cheat_type} is not ported")
    raw = (code or "").encode("utf-8")[:INTEROP_CODE_BYTES]
    return converter(raw.decode("utf-8", errors="ignore"))


def bundled_type(entry_code):
    """The type the UI gives a bundled NES entry (UI/Logic/CheatTypeDetector.cs):
    `NesCustom` when the entry contains ':', `NesGameGenie` otherwise. One type
    for the whole entry, every part decoded with it."""
    return NES_CUSTOM if ":" in entry_code else NES_GAME_GENIE


def bundled_parts(entry_code):
    """The parts the UI sends the Core for one bundled entry: split on ';'
    dropping empties, then each trimmed and dropped when empty."""
    return [p.strip() for p in entry_code.split(";") if p.strip()]


def decode_for_console(console, part):
    """Decode one submitted code part with the console's cheat types, or None.

    The form asks for no type, so the type is read off the code the way the
    bundled list is read (CheatTypeDetector): ':' is NES custom, '-' a GB/SMS
    Game Genie. An eight-letter NES code is tried as Game Genie first and as
    Pro Action Rocky only when it is not one - a string of only A and E is
    both, and the bundled list reads it as Game Genie."""
    if console == "nes":
        if ":" in part:
            return decode(NES_CUSTOM, part)
        return decode(NES_GAME_GENIE, part) or decode(NES_PRO_ACTION_ROCKY, part)
    if console in ("gb", "gbc"):
        return decode(GB_GAME_GENIE if "-" in part else GB_GAME_SHARK, part)
    if console == "sms":
        return decode(SMS_GAME_GENIE if "-" in part else SMS_PRO_ACTION_REPLAY, part)
    return None


def canonical(decoded):
    """One decoded part as text, for duplicate detection: what the Core acts
    on (CPU, address, value, compare, RAM/absolute and memory type), not how
    the code was spelled - two spellings of one patch are one code."""
    return (f"{decoded.cpu}:{decoded.address:X}:{decoded.value:02X}:{decoded.compare}:"
            f"{int(decoded.is_ram)}{int(decoded.is_abs)}:{decoded.mem_type}")


def normalise(decoded_parts):
    """The normalised code of one effect: its distinct parts, sorted, joined
    with '+'. Order and repetition of the parts do not matter."""
    return "+".join(sorted({canonical(d) for d in decoded_parts}))
