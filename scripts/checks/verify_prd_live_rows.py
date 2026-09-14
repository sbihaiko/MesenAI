#!/usr/bin/env python3
"""A shipped slice must not sit in a live slice table (PRD Phase 11, slice C.2).

`docs/roadmap/AGENTS.md` states the contract: when a slice ships, its row is
deleted from the roadmap table and one line goes to the Part's shipped record.
Nothing enforced it, so three shipped rows (F9.24, F9.26, F9.27) sat in the
live Phase 9 table for days before a human noticed.

This check parses the slice tables of the PRD's *pending* sections — Part A
"### 4. Roadmap — pending work, by slice" and Part B "### 8. Slices" — and
fails when a row's Decision cell (the last cell) declares the slice shipped.

The rule, deliberately narrow so it reads a declaration and not a mention:

  * split the Decision cell on `;`, `,` and ` — ` (em dash, spaced);
  * strip markdown emphasis and backticks from each fragment's edges;
  * the row is an offender when any fragment *starts with* the word `shipped`.

So `accepted 2026-09-14, shipped` and `shipped (offline half); harness
`cdl=` pending` are offenders, while a cell that merely mentions the word
mid-sentence — `… fused poses. Flag shipped. **Amended 2026-09-14** …` — is
not. Deciding a slice is done is a human act; this only refuses to let the
roadmap keep a row that already says so.

Usage: python3 scripts/checks/verify_prd_live_rows.py [path/to/PRD.md]
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
PRD = REPO / "docs" / "roadmap" / "PRD-mesence-enhancement-ecosystem.md"

# The headings that open a section of *pending* work. Everything else in the
# PRD (the shipped record, the ADR maps, the risk tables) may say "shipped".
PENDING_HEADINGS = (
    re.compile(r"^###\s*4\.\s*Roadmap"),
    re.compile(r"^###\s*8\.\s*Slices"),
)
# A slice table is keyed off its first header cell, so the risk / prior-art /
# standards tables in the same sections are left alone.
SLICE_HEADERS = ("slice", "spike")

SEPARATORS = re.compile(r";|,|\s—\s")
EMPHASIS = "*_`~ \t"
DECLARES_SHIPPED = re.compile(r"^shipped\b", re.IGNORECASE)


def is_divider(line: str) -> bool:
    return bool(re.match(r"^\|[\s:\-|]+\|$", line.strip()))


def cells(line: str) -> list:
    inner = line.strip()
    if inner.startswith("|"):
        inner = inner[1:]
    if inner.endswith("|"):
        inner = inner[:-1]
    return [c.strip() for c in inner.split("|")]


def declares_shipped(decision: str) -> bool:
    """True when a fragment of the Decision cell opens with the word `shipped`."""
    for fragment in SEPARATORS.split(decision):
        if DECLARES_SHIPPED.match(fragment.strip().lstrip(EMPHASIS)):
            return True
    return False


def offenders(text: str) -> list:
    """Return [(slice_id, decision_cell)] for every shipped row in a live table."""
    found = []
    in_pending = False
    header = None
    for raw in text.split("\n"):
        line = raw.rstrip()
        if line.startswith("#"):
            # A `####` phase heading lives *inside* a section, so it must not
            # close it; only `##`/`###` open or close a pending section.
            if len(line) - len(line.lstrip("#")) <= 3:
                in_pending = any(p.match(line) for p in PENDING_HEADINGS)
            header = None
            continue
        if not in_pending:
            continue
        if not line.lstrip().startswith("|"):
            header = None
            continue
        if is_divider(line):
            continue
        row = cells(line)
        if header is None:
            first = row[0].strip().strip(EMPHASIS).lower()
            header = row if first in SLICE_HEADERS else False
            continue
        if header is False or len(row) < 2:
            continue
        slice_id = row[0].strip().strip(EMPHASIS) or "(unnamed row)"
        decision = row[-1]
        if declares_shipped(decision):
            found.append((slice_id, decision))
    return found


def main(argv) -> int:
    path = Path(argv[1]) if len(argv) > 1 else PRD
    if not path.is_file():
        print(f"FAIL verify_prd_live_rows: {path} not found")
        return 1
    bad = offenders(path.read_text(encoding="utf-8"))
    if bad:
        print(f"FAIL verify_prd_live_rows: {len(bad)} shipped row(s) still in a live slice table")
        for slice_id, decision in bad:
            print(f"  {slice_id}: {decision[:160]}")
        print("  A shipped slice loses its row and gains one line in the Part's")
        print("  shipped record — see docs/roadmap/AGENTS.md, Local Contracts.")
        return 1
    print(f"PASS verify_prd_live_rows: no shipped row in a live slice table ({path.name})")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
