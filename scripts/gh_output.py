#!/usr/bin/env python3
"""Multi-line GitHub Actions step outputs with a random delimiter (bug #673).

A `name<<DELIM` block in $GITHUB_OUTPUT ends at the first line equal to
DELIM. With a fixed DELIM, a value carrying submitter text (the classify
prompt embeds the PACK BRIEF: archive member names, README/CREDITS excerpts)
could close the block itself and append outputs of its own. Here the
delimiter is fresh random per write and is never one that occurs in the
value.

Usage (workflow):  python3 scripts/gh_output.py NAME < value-file >> "$GITHUB_OUTPUT"
Python callers:    gh_output.append(os.environ["GITHUB_OUTPUT"], NAME, value)
"""
from __future__ import annotations

import secrets
import sys


def _token():
    return "ghadelim_" + secrets.token_hex(16)


def multiline(name, value):
    """The `name<<DELIM` ... `DELIM` block for one output value."""
    delim = _token()
    while delim in value:
        delim = _token()
    return f"{name}<<{delim}\n{value}\n{delim}\n"


def append(path, name, value):
    with open(path, "a", encoding="utf-8") as f:
        f.write(multiline(name, value))


def main(argv):
    if len(argv) != 2:
        print(__doc__, file=sys.stderr)
        return 2
    # One trailing newline is the file's line end, not part of the value
    # (what `echo DELIM; cat file; echo DELIM` produced before).
    value = sys.stdin.read()
    sys.stdout.write(multiline(argv[1], value[:-1] if value.endswith("\n") else value))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
