#!/usr/bin/env python3
"""ADR-0205 section 3 lint: re-checks what the Record-and-share action guarantees.

The clean artifact is produced by construction (section 2): the share action
power-cycles with RamPowerOnState = AllZeros and ignores battery data, so the
`.mmo` it writes has neither `SaveState.mss` nor any `Battery*` member. This
lint is defence in depth over that, and it exists because a submission's bytes
are not the action's output -- anyone can attach anything.

What it checks (and only this -- section 8's structural gate is slice R.2):
  * the archive is within the 8 MB cap (compressed), checked on the size alone
    before the archive is opened, so a deflate bomb is never inflated;
  * it is a Mesen movie: a zip whose members include `GameSettings.txt`
    (MovieManager::Play detects the format by content, so the extension is
    cosmetic and a `.mmo` re-uploaded as `.zip` is the same artifact);
  * no `SaveState.mss` (a save state carries a full framebuffer and, on a CHR
    RAM title, the game's tiles) and no `Battery*` member (battery data);
  * the ROM identity is present: a well-formed SHA-1 and the ROM *file name*,
    never a path. An embedded `PatchData.dat` and any cheat are permitted
    (sections 3 and 4).

Only `GameSettings.txt` and `MovieInfo.txt` are ever inflated, with a bounded
read; `Input.txt` and the other members are listed, never read.

Usage:
  python3 scripts/replay_lint.py <file.mmo|file.zip> [--json]

Exit 0 when accepted, 1 when refused (one line per reason on stderr), 2 on a
usage error. Stdlib only.
"""
from __future__ import annotations

import io
import json
import re
import sys
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

SETTINGS_MEMBER = "GameSettings.txt"
INFO_MEMBER = "MovieInfo.txt"
SAVE_STATE_MEMBER = "SaveState.mss"
SHA1_RE = re.compile(r"^[0-9A-Fa-f]{40}$")

Finding = namedtuple("Finding", "code message")


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
    facts = {"sha1": "", "game_file": "", "cheats": []}
    for raw in text.splitlines():
        line = raw.rstrip("\r")
        key, _, value = line.partition(" ")
        if key == "SHA1" and not facts["sha1"]:
            facts["sha1"] = value.strip()
        elif key == "GameFile" and not facts["game_file"]:
            facts["game_file"] = value.strip()
        elif key == "Cheat":
            kind, _, code = value.partition(" ")
            if kind and code:
                facts["cheats"].append((kind, code.strip()))
    return facts


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
                f"the archive carries battery data ({', '.join(batteries)}) (ADR-0205 section 3). It appears "
                "when a replay is recorded with StartWithSaveData (the command-line recorder); the Record and "
                "share action ignores save data on disk (section 2).",
            )

        facts = parse_game_settings(_read_text(zf, SETTINGS_MEMBER))
        info = {"author": "", "description": ""}
        if INFO_MEMBER in names:
            info = parse_movie_info(_read_text(zf, INFO_MEMBER))
        facts.update(info)
        result.facts = facts

        if not SHA1_RE.match(facts["sha1"]):
            result.add(
                "rom-hash",
                "GameSettings.txt has no well-formed ROM SHA-1 (ADR-0205 section 3): the hash is the pairing key "
                "and is required.",
            )
        game_file = facts["game_file"]
        if not game_file or "/" in game_file or "\\" in game_file:
            result.add(
                "rom-path",
                "GameSettings.txt must carry the ROM file name only, never a path (ADR-0205 section 3); "
                f"found {game_file!r}.",
            )
    return result


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
        print(f"accepted: {args[0]} (rom sha1 {result.facts['sha1']}, game file {result.facts['game_file']})")
    for finding in result.findings:
        print(f"refused [{finding.code}]: {finding.message}", file=sys.stderr)
    return 0 if result.ok else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
