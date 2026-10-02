#!/usr/bin/env python3
"""Framework-free checks for scripts/replay_lint.py -- the ADR-0205 section 3
lint that re-checks what the Record-and-share action guarantees.

The archives built here mirror what `MovieRecorder::Stop` writes (member names
`Input.txt`, `GameSettings.txt`, optional `MovieInfo.txt`, `PatchData.dat`,
`SaveState.mss`, `Battery<ext>`), so the lint is exercised against the real
member vocabulary without a ROM or the emulator. The end-to-end proof against a
recording the action itself produced is scripts/checks/verify_replay_recorded.sh.

Checks:
  AC-1 an archive the action would produce (no SaveState.mss, no Battery*)
       is accepted and its facts (ROM SHA-1, ROM file name, author,
       description, cheats) are read off it.
  AC-2 a `.mmo` recorded any other way is refused with the section 3 reason
       named: SaveState.mss (stock power-on settings / Record from the current
       state) and Battery* (StartWithSaveData, i.e. the command-line recorder).
  AC-3 the 8 MB cap is enforced on the compressed size before the archive is
       opened at all (a deflate bomb is never inflated).
  AC-4 the required ROM identity: SHA-1 well-formed, ROM file *name* only.
  AC-5 an embedded PatchData.dat is permitted (ADR-0144), and a renamed
       `.zip` is the same artifact as the `.mmo` (the extension is cosmetic).
  AC-6 not a Mesen movie at all (not a zip, no GameSettings.txt) is refused.

Usage: python3 scripts/test_replay_lint.py
"""
from __future__ import annotations

import io
import sys
import unicodedata
import zipfile
from pathlib import Path

SCRIPTS = Path(__file__).resolve().parent
sys.path.insert(0, str(SCRIPTS))
import replay_lint  # noqa: E402

FAILURES = []

SHA1 = "0123456789ABCDEF0123456789ABCDEF01234567"


def fail(msg):
    FAILURES.append(msg)
    print(f"FAIL: {msg}")


def ok(msg):
    print(f"ok: {msg}")


def game_settings(sha1=SHA1, game_file="Contra (USA).nes", cheats=()):
    lines = [
        "MesenVersion 2.1.0",
        "MovieFormatVersion 3",
        f"GameFile {game_file}",
        f"SHA1 {sha1}",
        "emu.consoleType Nes",
        "nes.RamPowerOnState AllZeros",
    ]
    lines += [f"Cheat {kind} {code}" for kind, code in cheats]
    return ("\n".join(lines) + "\n").encode()


def movie_info(author="alice", description="first line\nsecond line"):
    return f"Author {author}\nDescription\n{description}".encode()


def build(members, compression=zipfile.ZIP_DEFLATED):
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w", compression) as z:
        for name, data in members.items():
            z.writestr(name, data)
    return buf.getvalue()


def clean_members(**over):
    members = {
        "Input.txt": b"|........\n" * 600,
        "GameSettings.txt": game_settings(),
        "MovieInfo.txt": movie_info(),
    }
    members.update(over)
    return {k: v for k, v in members.items() if v is not None}


def codes(result):
    return [f.code for f in result.findings]


def check_accepts_action_output():
    result = replay_lint.lint_bytes(build(clean_members()))
    if not result.ok:
        fail(f"AC-1 the action's own shape was refused: {codes(result)}")
        return
    facts = result.facts
    want = {
        "sha1": SHA1,
        "game_file": "Contra (USA).nes",
        "author": "alice",
        "description": "first line\nsecond line",
    }
    for key, value in want.items():
        if facts.get(key) != value:
            fail(f"AC-1 fact {key}: expected {value!r}, got {facts.get(key)!r}")
            return
    cheated = replay_lint.lint_bytes(build(clean_members(
        **{"GameSettings.txt": game_settings(cheats=[("NesCustom", "0032:05")])})))
    if not cheated.ok or cheated.facts.get("cheats") != [("NesCustom", "0032:05")]:
        fail(f"AC-1 a cheated replay must be accepted (section 4): {codes(cheated)} {cheated.facts.get('cheats')}")
        return
    nameless = replay_lint.lint_bytes(build(clean_members(**{"MovieInfo.txt": None})))
    if not nameless.ok or nameless.facts.get("author") != "":
        fail(f"AC-1 an unnamed recorder (no MovieInfo.txt) is accepted with an empty author: {codes(nameless)}")
        return
    ok("AC-1 the action's output is accepted; sha1/game file/author/description/cheats are read off it")


def check_refuses_other_recordings():
    stock = replay_lint.lint_bytes(build(clean_members(**{"SaveState.mss": b"\x00" * 4096})))
    if stock.ok or "save-state" not in codes(stock):
        fail(f"AC-2 a SaveState.mss archive must be refused as save-state: ok={stock.ok} {codes(stock)}")
        return
    msg = next(f.message for f in stock.findings if f.code == "save-state")
    for needle in ("SaveState.mss", "ADR-0205", "3", "Record and share"):
        if needle not in msg:
            fail(f"AC-2 the save-state reason must name {needle!r}: {msg!r}")
            return
    battery = replay_lint.lint_bytes(build(clean_members(**{"Battery.sav": b"\x01\x02"})))
    if battery.ok or "battery" not in codes(battery):
        fail(f"AC-2 a Battery* archive must be refused as battery: {codes(battery)}")
        return
    bmsg = next(f.message for f in battery.findings if f.code == "battery")
    if "Battery.sav" not in bmsg or "ADR-0205" not in bmsg:
        fail(f"AC-2 the battery reason must name the member and the ADR: {bmsg!r}")
        return
    both = replay_lint.lint_bytes(build(clean_members(**{"SaveState.mss": b"x", "Battery.sav": b"y"})))
    if {"save-state", "battery"} - set(codes(both)):
        fail(f"AC-2 both reasons are reported together: {codes(both)}")
        return
    ok("AC-2 SaveState.mss and Battery* are refused, each with the section 3 reason named")


def check_size_cap_before_open():
    cap = replay_lint.MAX_ARCHIVE_BYTES
    if cap != 8 * 1024 * 1024:
        fail(f"AC-3 the cap must be 8 MB, got {cap}")
        return
    huge = b"PK" + b"\x00" * cap  # one byte over; not even a valid zip
    result = replay_lint.lint_bytes(huge)
    if result.ok or codes(result) != ["too-large"]:
        fail(f"AC-3 an oversized input is refused as too-large and nothing else (never opened): {codes(result)}")
        return
    at_cap = replay_lint.lint_size(cap)
    over = replay_lint.lint_size(cap + 1)
    if at_cap is not None or over is None or over.code != "too-large":
        fail("AC-3 lint_size is the pre-download gate: at the cap passes, one over refuses")
        return
    ok("AC-3 the 8 MB cap refuses on size alone, before the archive is opened")


def check_rom_identity():
    bad_hash = replay_lint.lint_bytes(build(clean_members(**{"GameSettings.txt": game_settings(sha1="XYZ")})))
    if bad_hash.ok or "rom-hash" not in codes(bad_hash):
        fail(f"AC-4 a malformed SHA-1 is refused: {codes(bad_hash)}")
        return
    no_hash = replay_lint.lint_bytes(build(clean_members(**{"GameSettings.txt": b"GameFile a.nes\n"})))
    if no_hash.ok or "rom-hash" not in codes(no_hash):
        fail(f"AC-4 a missing SHA-1 is refused: {codes(no_hash)}")
        return
    pathy = replay_lint.lint_bytes(build(clean_members(**{"GameSettings.txt": game_settings(game_file="/Users/me/roms/Contra.nes")})))
    if pathy.ok or "rom-path" not in codes(pathy):
        fail(f"AC-4 a ROM path (not a bare file name) is refused: {codes(pathy)}")
        return
    winpath = replay_lint.lint_bytes(build(clean_members(**{"GameSettings.txt": game_settings(game_file="C:\\roms\\Contra.nes")})))
    if winpath.ok or "rom-path" not in codes(winpath):
        fail(f"AC-4 a Windows ROM path is refused: {codes(winpath)}")
        return
    ok("AC-4 the ROM SHA-1 is required and well-formed; only a bare ROM file name is accepted")


def check_patch_and_extension():
    patched = replay_lint.lint_bytes(build(clean_members(**{"PatchData.dat": b"IPS..."})))
    if not patched.ok:
        fail(f"AC-5 PatchData.dat is permitted (ADR-0144): {codes(patched)}")
        return
    import tempfile
    with tempfile.TemporaryDirectory() as tmp:
        renamed = Path(tmp) / "renamed.zip"
        renamed.write_bytes(build(clean_members()))
        result = replay_lint.lint_file(renamed)
    if not result.ok:
        fail(f"AC-5 a .zip rename of a .mmo is the same artifact: {codes(result)}")
        return
    ok("AC-5 PatchData.dat is permitted and the extension is cosmetic")


def check_not_a_movie():
    junk = replay_lint.lint_bytes(b"this is not a zip at all")
    if junk.ok or "not-a-movie" not in codes(junk):
        fail(f"AC-6 a non-zip is refused as not-a-movie: {codes(junk)}")
        return
    other = replay_lint.lint_bytes(build({"readme.txt": b"hi"}))
    if other.ok or "not-a-movie" not in codes(other):
        fail(f"AC-6 a zip without GameSettings.txt is refused as not-a-movie: {codes(other)}")
        return
    bk2 = replay_lint.lint_bytes(build({"Input Log.txt": b"x"}))
    if bk2.ok or "not-a-movie" not in codes(bk2):
        fail(f"AC-6 a .bk2 (playback-only, section 1) is refused: {codes(bk2)}")
        return
    ok("AC-6 anything that is not a Mesen .mmo is refused")


def check_last_identity_key_wins():
    # AC-9: MesenMovie::ParseSettings keeps the last SHA1/GameFile line.
    decoy = "SHA1 " + "F" * 40 + "\nGameFile decoy.nes\n"
    real = game_settings().decode()
    result = replay_lint.lint_bytes(build(clean_members(**{"GameSettings.txt": (decoy + real).encode()})))
    if result.facts.get("sha1") != SHA1 or result.facts.get("game_file") != "Contra (USA).nes":
        fail(f"AC-9 the last SHA1/GameFile must win: {result.facts}")
        return
    ok("AC-9 duplicate identity keys: the last one wins, as in the Core")


def check_malformed_members_are_refused():
    # AC-7: a hostile archive must be a verdict, never a traceback (a crash
    # leaves a stale replay:valid label on the issue).
    good = bytearray(build(clean_members(), compression=zipfile.ZIP_STORED))
    marker = b"MesenVersion"
    at = good.find(marker)
    good[at] ^= 0x01  # stored payload no longer matches its CRC-32
    try:
        bad_crc = replay_lint.lint_bytes(bytes(good))
    except Exception as exc:  # noqa: BLE001
        fail(f"AC-7 a corrupt member crashed the lint: {type(exc).__name__}: {exc}")
        return
    if bad_crc.ok or "not-a-movie" not in codes(bad_crc):
        fail(f"AC-7 a corrupt member is refused as not-a-movie: {codes(bad_crc)}")
        return
    ok("AC-7 a malformed member is a not-a-movie verdict, not a crash")


def check_power_on_state_is_deterministic():
    # AC-8: a config push mid-recording can leave `Random` in the serialized
    # settings of a file that has no SaveState.mss; it can never replay.
    for extra in ("nes.ramPowerOnState Random", "nes.randomizeMapperPowerOnState true",
                  "snes.enableRandomPowerOnState true", "NES.RAMPOWERONSTATE random"):
        text = game_settings().decode() + extra + "\n"
        result = replay_lint.lint_bytes(build(clean_members(**{"GameSettings.txt": text.encode()})))
        if result.ok or "power-on" not in codes(result):
            fail(f"AC-8 {extra!r} must be refused as power-on: {codes(result)}")
            return
    for extra in ("nes.ramPowerOnState AllZeros", "nes.randomizeMapperPowerOnState false"):
        text = game_settings().decode() + extra + "\n"
        result = replay_lint.lint_bytes(build(clean_members(**{"GameSettings.txt": text.encode()})))
        if not result.ok:
            fail(f"AC-8 {extra!r} is deterministic and must be accepted: {codes(result)}")
            return
    ok("AC-8 a Random power-on state in GameSettings.txt is refused; deterministic ones pass")


def check_hostile_text_cannot_forge_report_lines():
    # AC-9: a member name or GameFile is attacker-controlled; echoed raw, a
    # newline forges a line of the report that submission.py posts in a comment.
    forged = "x\nrefused [forged]: fake\u202e\u200b\x1b[31m\x85\r"
    result = replay_lint.lint_bytes(build(clean_members(**{"Battery" + forged: b"\x00"})))
    if "battery" not in codes(result):
        fail(f"AC-9 setup: a Battery member must be refused: {codes(result)}")
        return
    bad_game = game_settings(game_file="a/b\x1b[2J\u202e\u200bc").decode()
    result2 = replay_lint.lint_bytes(build(clean_members(**{"GameSettings.txt": bad_game.encode()})))
    for r in (result, result2):
        for f in r.findings:
            if len(f.message.splitlines()) != 1 or any(unicodedata.category(c) in ("Cc", "Cf", "Zl", "Zp") for c in f.message):
                fail(f"AC-9 a finding message must not carry control/format characters or extra lines: {f.message!r}")
                return
    rendered = replay_lint.render_refusals(result)
    if len(rendered.splitlines()) != len(result.findings):
        fail(f"AC-9 the report must have exactly one line per finding: {rendered!r}")
        return
    got = replay_lint.plain_text("a\nb\u202ec\u200bd\x85e\tf\x07g")
    if got != "a bcd e fg":
        fail(f"AC-9 plain_text must space line breaks, drop other control/format chars: {got!r}")
        return
    ok("AC-9 duplicate identity keys: the last one wins, as in the Core")


def check_malformed_members_are_refused():
    # AC-7: a hostile archive must be a verdict, never a traceback (a crash
    # leaves a stale replay:valid label on the issue).
    good = bytearray(build(clean_members(), compression=zipfile.ZIP_STORED))
    marker = b"MesenVersion"
    at = good.find(marker)
    good[at] ^= 0x01  # stored payload no longer matches its CRC-32
    try:
        bad_crc = replay_lint.lint_bytes(bytes(good))
    except Exception as exc:  # noqa: BLE001
        fail(f"AC-7 a corrupt member crashed the lint: {type(exc).__name__}: {exc}")
        return
    if bad_crc.ok or "not-a-movie" not in codes(bad_crc):
        fail(f"AC-7 a corrupt member is refused as not-a-movie: {codes(bad_crc)}")
        return
    ok("AC-7 a malformed member is a not-a-movie verdict, not a crash")


def check_power_on_state_is_deterministic():
    # AC-8: a config push mid-recording can leave `Random` in the serialized
    # settings of a file that has no SaveState.mss; it can never replay.
    for extra in ("nes.ramPowerOnState Random", "nes.randomizeMapperPowerOnState true",
                  "snes.enableRandomPowerOnState true", "NES.RAMPOWERONSTATE random"):
        text = game_settings().decode() + extra + "\n"
        result = replay_lint.lint_bytes(build(clean_members(**{"GameSettings.txt": text.encode()})))
        if result.ok or "power-on" not in codes(result):
            fail(f"AC-8 {extra!r} must be refused as power-on: {codes(result)}")
            return
    for extra in ("nes.ramPowerOnState AllZeros", "nes.randomizeMapperPowerOnState false"):
        text = game_settings().decode() + extra + "\n"
        result = replay_lint.lint_bytes(build(clean_members(**{"GameSettings.txt": text.encode()})))
        if not result.ok:
            fail(f"AC-8 {extra!r} is deterministic and must be accepted: {codes(result)}")
            return
    ok("AC-8 a Random power-on state in GameSettings.txt is refused; deterministic ones pass")


def check_hostile_text_cannot_forge_report_lines():
    # AC-9: a member name or GameFile is attacker-controlled; echoed raw, a
    # newline forges a line of the report that submission.py posts in a comment.
    forged = "x\nrefused [forged]: fake\u202e\u200b\x1b[31m\x85\r"
    result = replay_lint.lint_bytes(build(clean_members(**{"Battery" + forged: b"\x00"})))
    if "battery" not in codes(result):
        fail(f"AC-9 setup: a Battery member must be refused: {codes(result)}")
        return
    bad_game = game_settings(game_file="a/b\x1b[2J\u202e\u200bc").decode()
    result2 = replay_lint.lint_bytes(build(clean_members(**{"GameSettings.txt": bad_game.encode()})))
    for r in (result, result2):
        for f in r.findings:
            if len(f.message.splitlines()) != 1 or any(unicodedata.category(c) in ("Cc", "Cf", "Zl", "Zp") for c in f.message):
                fail(f"AC-9 a finding message must not carry control/format characters or extra lines: {f.message!r}")
                return
    rendered = replay_lint.render_refusals(result)
    if len(rendered.splitlines()) != len(result.findings):
        fail(f"AC-9 the report must have exactly one line per finding: {rendered!r}")
        return
    if replay_lint.plain_text("a\nb\u202ec\u200bd\x85e\tf") != "a b c d e f".replace(" c d", "cd"):
        cleaned = replay_lint.plain_text('a\nb\u202ec\u200bd')
        fail(f"AC-9 plain_text must drop control/format chars and collapse whitespace: {cleaned!r}")
        return
    ok("AC-9 attacker-controlled names cannot forge report lines or smuggle control characters")


def main():
    check_accepts_action_output()
    check_refuses_other_recordings()
    check_size_cap_before_open()
    check_rom_identity()
    check_patch_and_extension()
    check_not_a_movie()
    check_last_identity_key_wins()
    check_malformed_members_are_refused()
    check_power_on_state_is_deterministic()
    check_hostile_text_cannot_forge_report_lines()
    if FAILURES:
        print(f"\n{len(FAILURES)} failure(s)")
        sys.exit(1)
    print("\nall checks passed")


if __name__ == "__main__":
    main()
