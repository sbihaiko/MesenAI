#!/usr/bin/env python3
"""Verifies the artist-facing docs still tell the truth.

`docs/remastering-a-game.md` exists because the recording-to-pack pipeline was
reachable only through the ADR trail, and its own subject is a tool nobody
could find. A guide whose commands have quietly rotted is that same failure
wearing a hat, so this checks the part that rots: the commands. The two docs
beside it are read by the same person on the same day -- `ai-kit-review.md`
when they let a model propose names, `hd-pack-authoring.md` when they hand the
pack over -- and rot identically, so they are held to the same gate.

What it asserts, for each doc:

  * the doc exists and is not trivial;
  * every relative link in it resolves on disk;
  * every `scripts/<file>` the doc names exists (except the harness binary
    `scripts/headless_record`, which `make capture-tool` builds and git ignores);
  * every `--flag` printed on a command line inside a fenced code block is a
    flag the script that line invokes actually accepts -- read from the script's
    own `add_argument` calls, or, for a script that hand-parses argv, from the
    usage block it states about itself, plus `scripts/headless_record.cpp`'s
    usage string for the harness;
  * every `key=` word in a `scripts/headless_record` invocation appears in that
    same usage string.

For `docs/remastering-a-game.md` only, it additionally asserts that the guide
still covers the four record drivers and the acceptance test, so a rewrite
cannot silently drop a whole stage of the workflow.

Stdlib only, no ROM, no emulator, no network. Run: python3 scripts/checks/verify_artist_docs.py
"""

import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
HARNESS = "scripts/headless_record"

MIN_LINES = 100

#Built by `make capture-tool` / `make spike-sound-driver`; gitignored, so
#"missing" is their normal state and neither may be reported as a broken path.
BUILD_ARTIFACTS = {HARNESS, "scripts/spike_sound_driver"}

#Every doc an artist is expected to read, and the terms that must survive a
#rewrite of the one whose job is to carry them (the others are free prose).
GUIDES = (
    (
        "remastering-a-game.md",
        (
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
        ),
    ),
    ("ai-kit-review.md", ()),
    ("hd-pack-authoring.md", ()),
)

failures: list[str] = []


def fail(message: str) -> None:
    failures.append(message)


def check(condition: bool, message: str) -> bool:
    if condition:
        print(f"PASS: {message}")
    else:
        fail(message)
    return condition


def read_doc(name: str) -> str | None:
    path = REPO_ROOT / "docs" / name
    if not path.is_file():
        fail(f"docs/{name} does not exist")
        return None
    return path.read_text(encoding="utf-8")


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


def usage_block(source: str) -> str:
    """The script's own stated usage: the `usage:` line plus what hangs off it.

    A hand-parsed CLI has no `add_argument` to read, so its stated usage is the
    only contract it publishes. It is written either as a docstring line
    (`Usage: ...`) or as the shell comment block a `# Usage:` starts, and its
    flags are sometimes spread over the indented lines below the label -- which
    is exactly how `mep_render_audio.py` states `--sf2`, `--layer` and
    `--force`. Reading only the label's own line missed all three and reported
    a correct doc as broken.
    """
    lines = source.splitlines()
    for index, line in enumerate(lines):
        if not re.search(r"\busage:", line, re.IGNORECASE):
            continue
        block = [line]
        for following in lines[index + 1 : index + 13]:
            stripped = following.strip().lstrip("#").strip()
            #Continuation of the block: still commented in, or indented under it.
            if not stripped:
                break
            if following.startswith((" ", "\t")) or following.lstrip().startswith("#"):
                block.append(following)
                continue
            break
        return "\n".join(block)
    return ""


def script_flags(relative: str) -> set[str] | None:
    """Flags a script accepts, from its own `add_argument` calls and usage text.

    `None` means "no such script" and is skipped; an empty set means the script
    declares no flags at all, which is the truth for several of them and must
    stay falsifiable. Both sources are unioned rather than short-circuited,
    because a script may use argparse for most of its surface and hand-parse
    the rest.

    This is the whole-script union, which is the right answer only for a script
    with a single command line. A script with subparsers needs
    `script_subcommands` as well: `--truth` belongs to `score` and `--accepted`
    to `promote`, and argparse rejects either on the other, so validating
    against the union would pass a doc that cannot run.
    """
    path = REPO_ROOT / relative
    if not path.is_file():
        return None
    source = path.read_text(encoding="utf-8", errors="replace")
    flags = set(re.findall(r"""add_argument\(\s*["'](-{1,2}[A-Za-z0-9_-]+)["']""", source))
    flags |= set(re.findall(r"--[A-Za-z][A-Za-z0-9-]*", usage_block(source)))
    return flags


def script_subcommands(relative: str) -> dict[str, set[str]]:
    """Flags per argparse subcommand: `{subcommand name: flags}`, `{}` if none.

    `artist_ai_review.py` registers `packet`, `check`, `truth`, `score` and
    `promote` as subparsers, each with its own arguments. Slicing the source at
    every `add_parser("name")` gives the flags that belong to each, so a doc
    line running `score` can be checked against `score` rather than against
    every flag in the file.
    """
    path = REPO_ROOT / relative
    if not path.is_file():
        return {}
    source = path.read_text(encoding="utf-8", errors="replace")
    #split() interleaves: [prologue, name1, body1, name2, body2, ...]
    parts = re.split(r"add_parser\(\s*[\"']([A-Za-z0-9_-]+)[\"']", source)
    if len(parts) < 3:
        return {}
    groups: dict[str, set[str]] = {}
    for index in range(1, len(parts) - 1, 2):
        body = parts[index + 1]
        #A subparser's body ends at the next `add_parser` because split() already
        #cut there, so anything `add_argument`-ed in it belongs to this name.
        groups[parts[index]] = set(
            re.findall(r"""add_argument\(\s*["'](--[A-Za-z0-9_-]+)["']""", body)
        )
    return groups


def accepted_flags(relative: str, command: str, union: set[str]) -> set[str]:
    """Flags legitimately available to `command`, subcommand-aware.

    When exactly one subcommand name appears in the command line, its group is
    the contract. When none does (a doc invoking a subcommand script without
    naming one) or the text is ambiguous, the union is returned rather than a
    guess: the check must never fail a correct doc.
    """
    groups = script_subcommands(relative)
    if not groups:
        return union
    tokens = set(re.findall(r"(?<![\w-])([A-Za-z][A-Za-z0-9_-]*)", command))
    named = [name for name in groups if name in tokens]
    if len(named) == 1:
        return groups[named[0]]
    return union


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


def check_links(doc: Path, text: str) -> None:
    links = re.findall(r"\]\(([^)]+)\)", text)
    broken = []
    for link in links:
        target = link.split("#", 1)[0]
        if not target or "://" in target or target.startswith("mailto:"):
            continue
        if not (doc.parent / target).exists():
            broken.append(link)
    check(not broken, f"{doc.name}: every relative link resolves ({len(links)} checked)" + (f" - broken: {broken}" if broken else ""))


def fenced_script_names(text: str) -> set[str]:
    """Command words in fenced blocks, extension or not.

    The whole-text scan above only sees a path when it ends in `.py`/`.sh`/
    `.md`; an extensionless command (`scripts/spike_sound_driver`) was
    recognized only inside inline backticks, so a fenced command typo'd into a
    name that does not exist stayed green. A token is taken only when it is a
    bare command word: no glob (`scripts/artist_kit*.py` names a family, not a
    file) and no trailing slash (that is a directory being referred to).
    """
    names: set[str] = set()
    for _lineno, body in fenced_blocks(text):
        for command in joined_commands(body):
            for token in re.findall(r"(?<![\w./-])(scripts/[A-Za-z0-9_./-]+)", command):
                if token.endswith("/") or "*" in token:
                    continue
                names.add(token)
    return names


def check_script_paths(text: str, label: str) -> None:
    named = set(re.findall(r"scripts/[A-Za-z0-9_./-]+\.(?:py|sh|md)", text))
    named |= {m for m in re.findall(r"`(scripts/[A-Za-z0-9_./-]+)`", text) if "." not in Path(m).name}
    named |= fenced_script_names(text)
    missing = sorted(
        name
        for name in named
        if name not in BUILD_ARTIFACTS and not (REPO_ROOT / name).exists()
    )
    check(not missing, f"{label}: every scripts/ path named exists ({len(named)} checked)" + (f" - missing: {missing}" if missing else ""))


def check_commands(text: str, label: str) -> None:
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
                available = accepted_flags(relative, command, accepted)
                bad_flags += [f"{relative} {f}" for f in sorted(flags) if f not in available]

    check(
        not bad_flags,
        f"{label}: every --flag printed is accepted by the script it is printed for"
        f" ({len(scripts_checked)} generators + the harness, {command_count} command(s))"
        + (f" - rejected: {bad_flags}" if bad_flags else ""),
    )
    check(not bad_keys, f"{label}: every key= flag printed for the harness is one it parses" + (f" - unknown: {bad_keys}" if bad_keys else ""))


def check_coverage_of_subject(text: str, terms) -> None:
    for term, description in terms:
        check(term in text, f"the guide still covers {description} ({term})")


def main() -> int:
    for name, terms in GUIDES:
        text = read_doc(name)
        if text is None:
            continue
        doc = REPO_ROOT / "docs" / name
        line_count = len(text.splitlines())
        check(line_count >= MIN_LINES, f"{name}: not trivial ({line_count} lines, expected >= {MIN_LINES})")

        check_links(doc, text)
        check_script_paths(text, name)
        check_commands(text, name)
        check_coverage_of_subject(text, terms)

    if failures:
        print(f"\n{len(failures)} check(s) failed:", file=sys.stderr)
        for message in failures:
            print(f"  - {message}", file=sys.stderr)
        return 1

    print(f"\nAll artist-doc checks passed ({len(GUIDES)} docs)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
