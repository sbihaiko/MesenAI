#!/usr/bin/env python3
"""ADR index integrity — every accepted ADR reaches the session-start index.

`scripts/adr_index.py` is wired as a `SessionStart` hook: it prints one line
per accepted ADR and that output is the *only* register a session sees by
default. An accepted ADR missing from it is a binding decision nobody is told
about, and nothing failed when that happened: on 2026-09-19 the register held
121 accepted ADRs and the index listed 117.

The cause was the parser, not the data. `STATUS` anchored on the first word
after `- Status:`, and four ADRs write the token behind markup or behind a
label — `- Status: **Q4 accepted 2026-09-19**` (0209),
`- Status: **accepted 2026-09-19 and implemented the same turn as F12.3**`
(0212, 0213, 0214). Each was read as `unknown` and dropped in silence, which
is the failure mode this check exists to make loud.

What it asserts, by importing the real parser rather than restating it:

- every ADR whose own `- Status:` line *declares* `accepted` is read as
  `accepted` by the parser and printed by `main()`, and nothing reaches the
  index that did not declare it. Comparing the parser against itself in both
  directions is not enough: the failure this check was written for had the
  parser agree with itself perfectly while disagreeing with 121 files.
- no ADR's `- Status:` line is unparseable (the token must be one of the
  three CLAUDE.md allows: `proposed`, `accepted`, `superseded`).

It deliberately does *not* complain about a line naming two tokens. Three
accepted ADRs say `accepted (partially superseded by ADR-0047)` or `after §2
was superseded` (0041, 0154, and 0209's `Q4 accepted ... and shipped`), the
parser takes the first token, and it is right to. A check that fails on
correct data is worse than no check.

Exit 0 on success, 1 with the offending ids on failure.
"""
import io
import contextlib
import pathlib
import re
import sys

REPO = pathlib.Path(__file__).resolve().parent.parent.parent
sys.path.insert(0, str(REPO / "scripts"))

import adr_index  # noqa: E402  (the parser under test, not a restatement)

TOKENS = ("proposed", "accepted", "superseded")


def accepted_from_output():
    """The ids `main()` prints under the accepted heading, read back from its
    own stdout rather than from a second call to `parse()` - the index is the
    artifact, and a check that re-derives it can pass while the hook is
    broken."""
    buffer = io.StringIO()
    with contextlib.redirect_stdout(buffer):
        adr_index.main()
    ids, in_accepted = [], False
    for line in buffer.getvalue().splitlines():
        if line.startswith("# "):
            in_accepted = line.startswith("# Accepted ")
        elif in_accepted and line.startswith("- "):
            ids.append(line[2:6])
    return ids


def main():
    files = sorted(adr_index.ADR_DIR.glob("[0-9][0-9][0-9][0-9]-*.md"))
    if not files:
        print(f"FAIL: no ADRs under {adr_index.ADR_DIR}")
        return 1

    problems = []
    for path in files:
        line = next((l for l in path.read_text(encoding="utf-8", errors="replace")
                     .splitlines() if l.startswith("- Status:")), "")
        if not line:
            problems.append(f"{path.name[:4]}: no `- Status:` line")
            continue
        if not re.search(r"\b(proposed|accepted|superseded)\b", line, re.IGNORECASE):
            problems.append(f"{path.name[:4]}: no status token in `{line[:60]}`")

    #The declaration is the first token on the Status line, which is the
    #register's own convention: 0209 writes `**Q4 accepted ...**` and is an
    #accepted ADR, 0041 writes `accepted (partially superseded by ...)` and is
    #an accepted ADR.
    declared = {}
    for path in files:
        line = next((l for l in path.read_text(encoding="utf-8", errors="replace")
                     .splitlines() if l.startswith("- Status:")), "")
        token = re.search(r"\b(proposed|accepted|superseded)\b", line, re.IGNORECASE)
        if token:
            declared[path.name[:4]] = token.group(1).lower()

    parsed = {a["id"]: a["status"] for a in (adr_index.parse(p) for p in files)}
    printed = set(accepted_from_output())

    for adr_id, status in sorted(declared.items()):
        if parsed.get(adr_id) != status:
            problems.append(f"{adr_id}: declares `{status}`, the parser reads "
                            f"`{parsed.get(adr_id)}` - the index would be wrong "
                            "about it")
    for adr_id, status in sorted(parsed.items()):
        if status == "accepted" and adr_id not in printed:
            problems.append(f"{adr_id}: accepted but absent from the index")
        if status != "accepted" and adr_id in printed:
            problems.append(f"{adr_id}: in the index but reads `{status}`")

    if problems:
        for p in problems:
            print(f"FAIL: {p}")
        return 1
    print(f"PASS: every one of the {len(printed)} accepted ADRs reaches the "
          f"session-start index, and every Status line carries a status token")
    return 0


if __name__ == "__main__":
    sys.exit(main())
