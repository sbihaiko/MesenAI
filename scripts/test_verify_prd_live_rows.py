#!/usr/bin/env python3
"""Framework-free checks for scripts/checks/verify_prd_live_rows.py (PRD C.2).

The fixtures are the ones the rule was designed against:

  * the offenders that sat in a live table — the two historical Phase 9
    rows, `accepted 2026-09-14, shipped` (F9.26) and
    `` shipped (offline half); harness `cdl=` pending `` (F9.27), plus the
    F14.17 cell that declares the slice `delivered` instead of `shipped`;
  * the legitimate cells: F9.25, which says "Flag shipped." mid-sentence and
    still owes work; C.1, which says nothing about shipping; and a cell that
    mentions "delivered" mid-sentence.

Usage: python3 scripts/test_verify_prd_live_rows.py
"""
from __future__ import annotations

import sys
from pathlib import Path

CHECKS = Path(__file__).resolve().parent / "checks"
sys.path.insert(0, str(CHECKS))
from verify_prd_live_rows import declares_done, offenders  # noqa: E402

FAILURES = []


def fail(msg):
    FAILURES.append(msg)
    print(f"FAIL: {msg}")


def ok(msg):
    print(f"PASS: {msg}")


OFFENDER_A = "accepted 2026-09-14, shipped"
OFFENDER_B = "shipped (offline half); harness `cdl=` pending"
# F14.17's real cell on 2026-09-26, shortened: the slice shipped, and the row
# said so with the other word.
OFFENDER_C = (
    "ADR-0239; **delivered 2026-09-26** "
    "(`docs/validation/x.md`: 119 sessions, 120 emulated seconds each)"
)
LEGIT_F925 = (
    "**accepted 2026-09-13** — ADR-0184, measured on Contra stage 1: 99 lives "
    "buys survival and no ground. Flag shipped. **Amended 2026-09-14** "
    "(ADR-0184): a third pass feeds all four surfaces. Still owed: the union run"
)
LEGIT_C1 = (
    "accepted 2026-09-14; Binaries stay on demand; amends the "
    "`.github/AGENTS.md` CI contract (ADR-0131) — say so in that file"
)
LEGIT_MENTION = (
    "ADR-0238 §5; the run is delivered only after a second pass, so the "
    "clause stays open"
)


def check_cells():
    for cell in (OFFENDER_A, OFFENDER_B, OFFENDER_C):
        if not declares_done(cell):
            fail(f"should be flagged as shipped/delivered: {cell!r}")
            return
    ok("the two historical offenders and a `delivered` cell are all flagged")

    for cell in (LEGIT_F925, LEGIT_C1, LEGIT_MENTION):
        if declares_done(cell):
            fail(f"should NOT be flagged: {cell!r}")
            return
    ok("a mid-sentence 'Flag shipped.', a mid-sentence 'delivered' and a "
       "plain decision are not flagged")

    # Emphasis and backticks around the declaration must not hide it.
    if not declares_done("accepted 2026-09-14; **shipped** as F9.30"):
        fail("markdown emphasis should not hide a shipped declaration")
        return
    ok("markdown emphasis around `shipped` is stripped before the test")


TABLE = """## Part A

### 4. Roadmap — pending work, by slice

#### Phase 9 — artist kit

| Slice | Deliverable | Decision |
|---|---|---|
| F9.25 | The coverage pass | {legit} |
| F9.26 | The TAS driver | {a} |
| F9.27 | The code/data map | {b} |

#### Phase 14 — the navigation sweep

| Slice | Deliverable | Decision |
|---|---|---|
| F14.17 | Wave two coverage | {c} |

| Risk | Mitigation |
|---|---|
| Something | shipped, but this is not a slice table |

### 3. What has shipped (record, one line each)

| Slice | Deliverable | Decision |
|---|---|---|
| F9.19 | The pose sidecar | shipped 2026-09-11 |
"""


def check_table():
    text = TABLE.format(legit=LEGIT_F925, a=OFFENDER_A, b=OFFENDER_B, c=OFFENDER_C)
    got = [slice_id for slice_id, _ in offenders(text)]
    if got != ["F9.26", "F9.27", "F14.17"]:
        fail(f"expected exactly F9.26, F9.27 and F14.17 to be reported, got {got!r}")
        return
    ok("the shipped and the delivered rows of the live slice tables are reported")
    ok("the risk table and the shipped-record section are left alone")


def check_real_prd():
    prd = Path(__file__).resolve().parents[1] / "docs" / "roadmap"
    prd = prd / "PRD-mesence-enhancement-ecosystem.md"
    if not prd.is_file():
        fail(f"{prd} not found")
        return
    got = offenders(prd.read_text(encoding="utf-8"))
    if got:
        fail(f"the live PRD has shipped rows in a live slice table: {got!r}")
        return
    ok("the repo's own PRD passes the check")


def main():
    check_cells()
    check_table()
    check_real_prd()

    if FAILURES:
        print(f"\n{len(FAILURES)} failure(s)")
        sys.exit(1)
    print("\nall checks passed")


if __name__ == "__main__":
    main()
