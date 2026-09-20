#!/usr/bin/env python3
"""Temporarily redact feature-naming lines from this account's persistent
MEMORY.md before a cold-read evaluator sweep (F12.2/ADR-0214), and restore
them afterwards.

Why this exists: Claude Code loads a per-account MEMORY.md into every
session it starts, including dispatched subagents, independent of which
repo or worktree the session runs in. The 2026-09-19 Sonnet sweep found this
file already documented the exact feature under test (an "F12.2 fechado"
bullet naming the shipped action), which every dispatched evaluator inherits
automatically -- there is no dispatch-side fix for this at all, prompt-based
or otherwise, because the leak happens before the evaluator's briefing is
even read. Redacting the file's own conditions before the sweep, and putting
them back after, is the only mechanism that removes it rather than asking
each evaluator to self-report around it.

This only ever comments out whole lines (prefixing them with
`<!-- f122-redacted -->` so the redaction is visible and mechanically
reversible) inside a copy the caller names explicitly; it never deletes
content and it always makes a timestamped backup before touching anything.

Usage:
    # Before the sweep -- redact any line matching a pattern:
    python3 scripts/f122_redact_memory.py redact \\
        ~/.claude/projects/<project>/memory/MEMORY.md \\
        --pattern 'F12\\.2' --pattern 'ADR-0216' --pattern 'sheet cell'

    # After the sweep -- undo it (uses the backup written by `redact`):
    python3 scripts/f122_redact_memory.py restore \\
        ~/.claude/projects/<project>/memory/MEMORY.md
"""
import argparse
import pathlib
import re
import shutil
import sys
import time

MARKER = "<!-- f122-redacted for a blind evaluator sweep: "


def backup_path(target: pathlib.Path) -> pathlib.Path:
    return target.with_suffix(target.suffix + ".f122-backup")


def cmd_redact(args):
    target = pathlib.Path(args.memory_file).expanduser()
    if not target.is_file():
        print(f"error: {target} does not exist", file=sys.stderr)
        return 1
    backup = backup_path(target)
    if backup.exists():
        print(f"error: {backup} already exists -- a redaction is already "
              "in progress; run `restore` first", file=sys.stderr)
        return 1
    shutil.copy2(target, backup)

    patterns = [re.compile(p, re.IGNORECASE) for p in args.pattern]
    lines = target.read_text(encoding="utf-8").splitlines(keepends=True)
    redacted = 0
    out = []
    stamp = time.strftime("%Y-%m-%dT%H:%M:%S")
    for line in lines:
        if any(p.search(line) for p in patterns) and not line.lstrip().startswith(MARKER):
            out.append(f"{MARKER}{stamp}) {line}")
            redacted += 1
        else:
            out.append(line)
    target.write_text("".join(out), encoding="utf-8")
    print(f"redacted {redacted} line(s) in {target}")
    print(f"backup at {backup} -- run `restore` to undo")
    return 0


def cmd_restore(args):
    target = pathlib.Path(args.memory_file).expanduser()
    backup = backup_path(target)
    if not backup.is_file():
        print(f"error: no backup at {backup} -- nothing to restore",
              file=sys.stderr)
        return 1
    shutil.copy2(backup, target)
    backup.unlink()
    print(f"restored {target} from {backup}, backup removed")
    return 0


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = parser.add_subparsers(dest="command", required=True)

    p_redact = sub.add_parser("redact", help="comment out lines matching any pattern")
    p_redact.add_argument("memory_file")
    p_redact.add_argument("--pattern", action="append", required=True,
                           help="regex; repeatable. A line matching any is redacted")
    p_redact.set_defaults(func=cmd_redact)

    p_restore = sub.add_parser("restore", help="undo the last redact, from its backup")
    p_restore.add_argument("memory_file")
    p_restore.set_defaults(func=cmd_restore)

    args = parser.parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main())
