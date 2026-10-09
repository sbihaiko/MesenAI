#!/usr/bin/env python3
"""ADR-0205 section 3 lint: re-checks what the Record-and-share action guarantees.

The clean artifact is produced by construction (section 2): the share action
power-cycles with RamPowerOnState = AllZeros and ignores battery data, so the
`.mmo` it writes has neither `SaveState.mss` nor any `Battery*` member. This
lint is defense in depth over that, and it exists because a submission's bytes
are not the action's output -- anyone can attach anything.

What it checks -- section 3's lint, then section 8's structural gate (R.2):
  * the archive is within the 8 MB cap (compressed), checked on the size alone
    before the archive is opened, so a deflate bomb is never inflated;
  * it is a Mesen movie: a zip whose members include `GameSettings.txt`
    (MovieManager::Play detects the format by content, so the extension is
    cosmetic and a `.mmo` re-uploaded as `.zip` is the same artifact);
  * no `SaveState.mss` (a save state carries a full framebuffer and, on a CHR
    RAM title, the game's tiles) and no `Battery*` member (battery data);
  * the ROM identity is present: a well-formed SHA-1 and the ROM *file name*,
    never a path. An embedded `PatchData.dat` and any cheat are permitted
    (sections 3 and 4);
  * section 8: `GameSettings.txt` parses as `MesenMovie::Play` reads it (a
    `MesenVersion` of 2 or later and a `MovieFormatVersion` of 2 or later --
    the two refusals the Core makes before playing) and names a console the
    pipeline supports (`emu.consoleType` Nes, Gameboy or Sms); `Input.txt` is
    present, non-empty and frame-aligned (every line one frame: `|`-led, the
    same number of `|` fields on every line, the file ending on a newline).
    The gate decides "is this a Mesen movie of this ROM", never "is this a
    good run" -- votes rank (section 8).

`GameSettings.txt` and `MovieInfo.txt` are inflated with a bounded read;
`Input.txt` is streamed line by line, bounded at MAX_INPUT_BYTES of inflated
text, and never held whole. Other members are listed, never read. The facts
also carry the archive's sha256 (section 7's dedupe key and the client's pin).

Usage:
  python3 scripts/replay_lint.py <file.mmo|file.zip> [--json]

Exit 0 when accepted, 1 when refused (one line per reason on stderr), 2 on a
usage error. Stdlib only.
"""
from __future__ import annotations

import hashlib
import io
import json
import re
import sys
import unicodedata
import zipfile
from collections import namedtuple
from pathlib import Path

# Section 3: the one place the cap lives. The CI download and the client
# download both enforce it before inflating (the client mirrors it in R.2).
MAX_ARCHIVE_BYTES = 8 * 1024 * 1024
# The two text members the lint reads. Both are small by construction
# (Author <= 250 B, Description <= 10 KB, settings a few KB); the bound keeps
# a hostile member from being inflated whole.
MAX_TEXT_MEMBER_BYTES = 1024 * 1024
# Section 8: Input.txt is one text line per frame (`|` + each device's state).
# A 4.6-hour run at 1,000,000 frames with two NES pads is about 20 MB of text;
# the bound refuses a deflate bomb that the 8 MB compressed cap still admits.
MAX_INPUT_BYTES = 64 * 1024 * 1024
INPUT_MEMBER = "Input.txt"
# The consoles the pipeline supports (the product consoles; GBC runs on the
# Game Boy core). Mirrored by UI/Logic/CommunityReplayCatalog.ConsoleKey.
CONSOLES = {"Nes": "nes", "Gameboy": "gb", "Sms": "sms"}

SETTINGS_MEMBER = "GameSettings.txt"
INFO_MEMBER = "MovieInfo.txt"
SAVE_STATE_MEMBER = "SaveState.mss"
SHA1_RE = re.compile(r"^[0-9A-Fa-f]{40}$")

Finding = namedtuple("Finding", "code message")


def plain_text(text):
    """Attacker-controlled text made safe to echo on one report line: line
    breaks and other whitespace controls become a space, every other control
    (C0, C1, DEL) and format character (bidi, zero-width) is dropped, and the
    whitespace is collapsed. A zip member name or a GameSettings.txt value is
    submitter-chosen, and the findings are posted into an issue comment."""
    out = []
    for c in str(text):
        cat = unicodedata.category(c)
        if cat in ("Zl", "Zp") or c in "\t\n\r\x0b\x0c\x85":
            out.append(" ")
        elif cat not in ("Cc", "Cf", "Cs", "Co", "Cn"):
            out.append(c)
    return " ".join("".join(out).split())


def render_refusals(result):
    """The human-readable report: exactly one `refused [code]: message` line per
    finding, whatever the findings echo."""
    return "\n".join(f"refused [{f.code}]: {plain_text(f.message)}" for f in result.findings)


class LintResult:
    def __init__(self):
        self.findings = []
        self.facts = {}

    @property
    def ok(self):
        return not self.findings

    def add(self, code, message):
        self.findings.append(Finding(code, message))


def too_large_message(size):
    return (
        f"the archive is {size} bytes, over the {MAX_ARCHIVE_BYTES}-byte (8 MB) cap "
        "(ADR-0205 section 3). A multi-hour input log is orders of magnitude smaller; "
        "an archive this large is not a Record and share output."
    )


def lint_size(size):
    """The pre-download / pre-open size gate: None when `size` is within the
    cap, else the Finding. Callers with only a Content-Length use this."""
    if size > MAX_ARCHIVE_BYTES:
        return Finding("too-large", too_large_message(size))
    return None


def _read_text(zf, name):
    with zf.open(name) as member:
        return member.read(MAX_TEXT_MEMBER_BYTES).decode("utf-8", errors="replace")


def parse_game_settings(text):
    """The keys the lint and the title rule need. `GameSettings.txt` is
    `<Key> <value>` lines (MovieRecorder::GetGameSettings), then the
    serialized settings, then one `Cheat <Type> <Code>` line per active cheat."""
    facts = {"sha1": "", "no_intro_sha1": "", "game_file": "", "cheats": [], "mesen_version": "", "movie_format": "", "console_type": ""}
    for raw in text.splitlines():
        line = raw.rstrip("\r")
        key, _, value = line.partition(" ")
        # The last occurrence wins, as in MesenMovie::ParseSettings: the lint
        # must read the identity the Core would play back, not a decoy line.
        if key == "SHA1":
            facts["sha1"] = value.strip()
        elif key == "NoIntroSHA1":
            facts["no_intro_sha1"] = value.strip()
        elif key == "GameFile":
            facts["game_file"] = value.strip()
        elif key == "MesenVersion":
            facts["mesen_version"] = value.strip()
        elif key == "MovieFormatVersion":
            facts["movie_format"] = value.strip()
        elif key == "emu.consoleType":
            facts["console_type"] = value.strip()
        elif key == "Cheat":
            kind, _, code = value.partition(" ")
            if kind and code:
                facts["cheats"].append((kind, code.strip()))
    return facts


def random_power_on_keys(text):
    """Serialized settings lines that make the power-on state random, whatever
    the console prefix or key casing (ShareRecordingSettings.h drives the same
    fields false for the recording)."""
    found = []
    for raw in text.splitlines():
        key, _, value = raw.rstrip("\r").partition(" ")
        low, val = key.lower(), value.strip().lower()
        if low.endswith(".rampoweronstate") and val == "random":
            found.append(key)
        elif low.endswith((".randomizemapperpoweronstate", ".enablerandompoweronstate",
                           ".randomizecpuppualignment")) and val == "true":
            found.append(key)
    return found


def parse_movie_info(text):
    """`MovieInfo.txt` is `Author <name>` then `Description` on its own line,
    then the free text (MovieRecorder::Stop)."""
    author = ""
    description = ""
    lines = text.replace("\r", "").split("\n")
    rest = []
    seen_description = False
    for line in lines:
        if seen_description:
            rest.append(line)
        elif line.startswith("Author"):
            author = line[len("Author"):].strip()
        elif line.strip() == "Description":
            seen_description = True
    description = "\n".join(rest).strip("\n")
    return {"author": author, "description": description}


def lint_bytes(data):
    result = LintResult()
    over = lint_size(len(data))
    if over is not None:
        result.findings.append(over)
        return result
    sha256 = hashlib.sha256(data).hexdigest()

    try:
        zf = zipfile.ZipFile(io.BytesIO(data))
    except (zipfile.BadZipFile, ValueError):
        result.add("not-a-movie", "not a zip archive: a Mesen movie is a zip holding GameSettings.txt (ADR-0205 section 1).")
        return result

    with zf:
        names = zf.namelist()
        if SETTINGS_MEMBER not in names:
            what = " (a BizHawk .bk2 is playback-only in this Core)" if "Input Log.txt" in names else ""
            result.add(
                "not-a-movie",
                f"no {SETTINGS_MEMBER} member{what}: only a Mesen .mmo is a publishable replay (ADR-0205 section 1).",
            )
            return result

        if SAVE_STATE_MEMBER in names:
            result.add(
                "save-state",
                f"the archive carries {SAVE_STATE_MEMBER} (ADR-0205 section 3). A save state embeds a full "
                "framebuffer and, on a CHR RAM title, the game's unpacked tiles, so it cannot be published. "
                "It appears when a replay is recorded with stock power-on settings or from the current state; "
                "record it with the Record and share action, which starts from power-on with a deterministic "
                "RAM state (section 2).",
            )
        batteries = sorted(n for n in names if n.startswith("Battery"))
        if batteries:
            result.add(
                "battery",
                f"the archive carries battery data ({', '.join(plain_text(b) for b in batteries)}) (ADR-0205 section 3). It appears "
                "when a replay is recorded with StartWithSaveData (the command-line recorder); the Record and "
                "share action ignores save data on disk (section 2).",
            )

        try:
            settings_text = _read_text(zf, SETTINGS_MEMBER)
            info_text = _read_text(zf, INFO_MEMBER) if INFO_MEMBER in names else None
        except Exception as exc:  # encrypted, corrupt CRC, unsupported method...
            result.add(
                "not-a-movie",
                f"a member of the archive cannot be read ({type(exc).__name__}): a Mesen .mmo is a plain, "
                "intact zip (ADR-0205 section 1).",
            )
            return result
        facts = parse_game_settings(settings_text)
        info = {"author": "", "description": ""}
        if info_text is not None:
            info = parse_movie_info(info_text)
        facts.update(info)
        facts["sha256"] = sha256
        facts["size"] = len(data)
        result.facts = facts

        if not SHA1_RE.match(facts["sha1"]):
            result.add(
                "rom-hash",
                "GameSettings.txt has no well-formed ROM SHA-1 (ADR-0205 section 3): the hash is the pairing key "
                "and is required.",
            )
        random_keys = random_power_on_keys(settings_text)
        if random_keys:
            result.add(
                "power-on",
                f"GameSettings.txt records a random power-on state ({', '.join(plain_text(k) for k in random_keys)}); without a save "
                "state such a replay cannot play back (ADR-0205 section 2). A settings change made while "
                "recording causes this; record again with the Record and share action and leave the "
                "settings alone until it stops.",
            )
        game_file = facts["game_file"]
        if not game_file or "/" in game_file or "\\" in game_file:
            result.add(
                "rom-path",
                "GameSettings.txt must carry the ROM file name only, never a path (ADR-0205 section 3); "
                f"found {plain_text(game_file)!r}.",
            )
        _structural_gate(result, zf, names, facts)
    return result


def _version_at_least_2(text):
    """MesenMovie::Play refuses a MesenVersion of 0.x/1.x (or shorter than two
    characters) and a MovieFormatVersion below 2."""
    return len(text) >= 2 and not text.startswith(("0.", "1."))


def input_frames(zf, names):
    """(frames, problem): the number of frame lines of Input.txt, streamed with
    a bound, or the reason it is not one frame per line. `problem` is None
    when the member is frame-aligned."""
    if INPUT_MEMBER not in names:
        return 0, f"the archive has no {INPUT_MEMBER}, so there is nothing to replay"
    frames = 0
    fields = None
    read = 0
    last = b"\n"
    with zf.open(INPUT_MEMBER) as member:
        for line in member:
            read += len(line)
            if read > MAX_INPUT_BYTES:
                return frames, f"{INPUT_MEMBER} inflates past {MAX_INPUT_BYTES} bytes, more than any real run"
            last = line
            text = line.rstrip(b"\r\n")
            if not text.startswith(b"|"):
                return frames, f"line {frames + 1} of {INPUT_MEMBER} is not a frame (a frame line starts with `|`)"
            count = text.count(b"|")
            if fields is None:
                fields = count
            elif count != fields:
                return frames, (f"line {frames + 1} of {INPUT_MEMBER} has {count} input fields where every earlier "
                                f"frame has {fields}: the frames are not aligned")
            frames += 1
    if frames == 0:
        return 0, f"{INPUT_MEMBER} is empty: the movie holds no frame"
    if not last.endswith(b"\n"):
        return frames, f"{INPUT_MEMBER} ends in the middle of a frame (no final newline)"
    return frames, None


def _structural_gate(result, zf, names, facts):
    """Section 8: is this a Mesen movie of this ROM (never: is it a good run)."""
    if not _version_at_least_2(facts["mesen_version"]) or not facts["movie_format"].isdigit() \
            or int(facts["movie_format"]) < 2:
        result.add(
            "settings",
            "GameSettings.txt does not parse as a playable Mesen movie: it needs a MesenVersion of 2 or later "
            "and a MovieFormatVersion of 2 or later, the two checks MesenMovie::Play makes before playing "
            f"(found {plain_text(facts['mesen_version'])!r} and {plain_text(facts['movie_format'])!r}; "
            "ADR-0205 section 8).",
        )
    console = CONSOLES.get(facts["console_type"])
    facts["console"] = console or ""
    if console is None:
        result.add(
            "console",
            f"GameSettings.txt names the console {plain_text(facts['console_type'])!r}; the pipeline lists replays "
            "for NES, Game Boy / Game Boy Color and Master System only (ADR-0205 section 8).",
        )
    try:
        frames, problem = input_frames(zf, names)
    except Exception as exc:  # corrupt CRC, unsupported method...
        frames, problem = 0, f"{INPUT_MEMBER} cannot be read ({type(exc).__name__})"
    facts["frames"] = frames
    if problem is not None:
        result.add("input", f"{problem} (ADR-0205 section 8).")


def lint_file(path):
    path = Path(path)
    over = lint_size(path.stat().st_size)
    if over is not None:  # refuse on size alone, without reading the file
        result = LintResult()
        result.findings.append(over)
        return result
    return lint_bytes(path.read_bytes())


def main(argv):
    args = [a for a in argv[1:] if a != "--json"]
    if len(args) != 1:
        print(__doc__)
        return 2
    result = lint_file(args[0])
    if "--json" in argv:
        print(json.dumps({
            "ok": result.ok,
            "findings": [{"code": f.code, "message": f.message} for f in result.findings],
            "facts": result.facts,
        }, indent=2))
    elif result.ok:
        print(f"accepted: {plain_text(args[0])} (rom sha1 {result.facts['sha1']}, "
              f"game file {plain_text(result.facts['game_file'])})")
    if result.findings:
        print(render_refusals(result), file=sys.stderr)
    return 0 if result.ok else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
