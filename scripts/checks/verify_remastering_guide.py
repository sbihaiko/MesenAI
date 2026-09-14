#!/usr/bin/env python3
"""Verifies docs/remastering-a-game.md is still true.

The guide exists because the recording-to-pack pipeline was reachable only
through the ADR trail, and its own subject is a tool nobody could find. A guide
whose commands have quietly rotted is that same failure wearing a hat, so this
checks the part that rots: the commands.

What it asserts:

  * the doc exists and is not trivial;
  * every relative link in it resolves on disk;
  * every `scripts/<file>` the doc names exists (except the harness binary
    `scripts/headless_record`, which `make capture-tool` builds and git ignores);
  * every `--flag` printed on a command line inside a fenced code block is a
    flag the script that line invokes actually accepts — read from the script's
    own `add_argument` calls, or from `scripts/headless_record.cpp`'s usage
    string for the harness;
  * every `key=` word in a `scripts/headless_record` invocation appears in that
    same usage string;
  * the guide still covers the four record drivers and the acceptance test,
    so a rewrite cannot silently drop a whole stage of the workflow.

Stdlib only, no ROM, no emulator, no network. Run: python3 scripts/checks/verify_remastering_guide.py
"""

import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
DOC = REPO_ROOT / "docs" / "remastering-a-game.md"
HARNESS = "scripts/headless_record"

MIN_LINES = 100

#Built by `make capture-tool`; never versioned, so "missing" is its normal state.
BUILD_ARTIFACTS = {HARNESS}

failures: list[str] = []


def fail(message: str) -> None:
    failures.append(message)


def check(condition: bool, message: str) -> bool:
    if condition:
        print(f"PASS: {message}")
    else:
        fail(message)
    return condition


def read_doc() -> str:
    if not DOC.is_file():
        print(f"FAIL: {DOC} does not exist", file=sys.stderr)
        raise SystemExit(1)
    return DOC.read_text(encoding="utf-8")


def fenced_blocks(text: str) -> list[tuple[int, str]]:
    """Every ```-fenced block, as (first line number, body)."""
    blocks = []
    lines = text.splitlines()
    start = None
    for index, line in enumerate(lines):
        if not line.lstrip().startswith("```"):
            continue
        if start is None:
            start = index + 1
        else:
            blocks.append((start + 1, "\n".join(lines[start:index])))
            start = None
    return blocks


def joined_commands(body: str) -> list[str]:
    """Shell continuation lines folded into one logical command each."""
    commands: list[str] = []
    pending = ""
    for line in body.splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith("#"):
            continue
        if stripped.endswith("\\"):
            pending += stripped[:-1] + " "
            continue
        commands.append(pending + stripped)
        pending = ""
    if pending:
        commands.append(pending)
    return commands


def script_flags(relative: str) -> set[str] | None:
    """Flags a Python script accepts, from its own add_argument calls."""
    path = REPO_ROOT / relative
    if not path.is_file():
        return None
    source = path.read_text(encoding="utf-8")
    return set(re.findall(r"""add_argument\(\s*["'](-{1,2}[A-Za-z0-9_-]+)["']""", source))


def harness_tokens() -> set[str]:
    """Bare flags and key= flags the capture harness documents in its usage."""
    source = (REPO_ROOT / "scripts" / "headless_record.cpp").read_text(encoding="utf-8")
    start = source.find('"usage: %s')
    end = source.find("argv[0]);", start)
    if start < 0 or end < 0:
        return set()
    usage = source[start:end]
    tokens = set(re.findall(r"\[([a-z][a-z0-9-]+)\]", usage))
    tokens |= {f"{name}=" for name in re.findall(r"\[([a-z][a-z0-9-]+)=", usage)}
    return tokens


def check_links(text: str) -> None:
    links = re.findall(r"\]\(([^)]+)\)", text)
    broken = []
    for link in links:
        target = link.split("#", 1)[0]
        if not target or "://" in target or target.startswith("mailto:"):
            continue
        if not (DOC.parent / target).exists():
            broken.append(link)
    check(not broken, f"every relative link resolves ({len(links)} checked)" + (f" - broken: {broken}" if broken else ""))


def check_script_paths(text: str) -> None:
    named = set(re.findall(r"scripts/[A-Za-z0-9_./-]+\.(?:py|sh|md)", text))
    named |= {m for m in re.findall(r"`(scripts/[A-Za-z0-9_./-]+)`", text) if "." not in Path(m).name}
    missing = sorted(
        name
        for name in named
        if name not in BUILD_ARTIFACTS and not (REPO_ROOT / name).exists()
    )
    check(not missing, f"every scripts/ path named exists ({len(named)} checked)" + (f" - missing: {missing}" if missing else ""))


def check_commands(text: str) -> None:
    harness = harness_tokens()
    if not harness:
        fail("could not read the usage string out of scripts/headless_record.cpp")
        return

    scripts_checked: set[str] = set()
    bad_flags: list[str] = []
    bad_keys: list[str] = []
    command_count = 0

    for _lineno, body in fenced_blocks(text):
        for command in joined_commands(body):
            invoked = re.findall(r"scripts/[A-Za-z0-9_.-]+\.(?:py|sh)|" + re.escape(HARNESS), command)
            if not invoked:
                continue
            command_count += 1
            for relative in invoked:
                flags = set(re.findall(r"(?<![\w-])(--[A-Za-z][A-Za-z0-9-]*)", command))
                if relative == HARNESS:
                    bad_flags += [f"{relative} {f}" for f in sorted(flags) if f not in harness]
                    keys = set(re.findall(r"(?<![\w-])([a-z][a-z0-9-]*=)", command))
                    bad_keys += [f"{relative} {k}" for k in sorted(keys) if k not in harness]
                    continue
                accepted = script_flags(relative)
                if accepted is None:
                    continue
                scripts_checked.add(relative)
                bad_flags += [f"{relative} {f}" for f in sorted(flags) if f not in accepted]

    check(
        not bad_flags,
        f"every --flag printed is accepted by the script it is printed for"
        f" ({len(scripts_checked)} generators + the harness, {command_count} command(s))"
        + (f" - rejected: {bad_flags}" if bad_flags else ""),
    )
    check(not bad_keys, "every key= flag printed for the harness is one it parses" + (f" - unknown: {bad_keys}" if bad_keys else ""))


def check_coverage_of_subject(text: str) -> None:
    for term, label in (
        ("input=", "the scripted route driver"),
        ("movie=", "the TAS movie driver"),
        ("cheat=", "the RAM-only cheat driver"),
        ("state=", "the save-state driver"),
        ("artist_kit.py", "the sprite kit generator"),
        ("artist_bg_kit.py", "the background kit generator"),
        ("artist_map.py", "the stage panorama generator"),
        ("artist_chr_kit.py", "the CHR pattern-page generator"),
        ("artist_kit_assemble.py", "the kit assembler"),
        ("mep_build.py", "the pack builder"),
        ("mep_lint.py", "the linter"),
        ("--verify", "the round-trip acceptance test"),
        ("artist_cover.py", "the coverage measurement"),
    ):
        check(term in text, f"the guide still covers {label} ({term})")


def main() -> int:
    text = read_doc()
    line_count = len(text.splitlines())
    check(line_count >= MIN_LINES, f"the guide is not trivial ({line_count} lines, expected >= {MIN_LINES})")

    check_links(text)
    check_script_paths(text)
    check_commands(text)
    check_coverage_of_subject(text)

    if failures:
        print(f"\n{len(failures)} check(s) failed:", file=sys.stderr)
        for message in failures:
            print(f"  - {message}", file=sys.stderr)
        return 1

    print("\nAll remastering-guide checks passed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
