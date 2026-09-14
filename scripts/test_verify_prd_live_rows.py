#!/usr/bin/env python3
"""Framework-free checks for scripts/checks/verify_prd_live_rows.py (PRD C.2).

The three fixtures are the ones the rule was designed against:

  * the two historical offenders that sat in the live Phase 9 table —
    `accepted 2026-09-14, shipped` (F9.26) and
    `` shipped (offline half); harness `cdl=` pending `` (F9.27);
  * the legitimate F9.25 cell, which says "Flag shipped." mid-sentence and
    still owes work, plus the C.1 cell, which says nothing about shipping.

Usage: python3 scripts/test_verify_prd_live_rows.py
"""
from __future__ import annotations

import sys
from pathlib import Path

CHECKS = Path(__file__).resolve().parent / "checks"
sys.path.insert(0, str(CHECKS))
from verify_prd_live_rows import declares_shipped, offenders  # noqa: E402

FAILURES = []


def fail(msg):
    FAILURES.append(msg)
    print(f"FAIL: {msg}")


def ok(msg):
    print(f"PASS: {msg}")


OFFENDER_A = "accepted 2026-09-14, shipped"
OFFENDER_B = "shipped (offline half); harness `cdl=` pending"
LEGIT_F925 = (
    "**accepted 2026-09-13** — ADR-0184, measured on Contra stage 1: 99 lives "
    "buys survival and no ground. Flag shipped. **Amended 2026-09-14** "
    "(ADR-0184): a third pass feeds all four surfaces. Still owed: the union run"
)
LEGIT_C1 = (
    "accepted 2026-09-14; Binaries stay on demand; amends the "
    "`.github/AGENTS.md` CI contract (ADR-0131) — say so in that file"
)


def check_cells():
    for cell in (OFFENDER_A, OFFENDER_B):
        if not declares_shipped(cell):
            fail(f"should be flagged as shipped: {cell!r}")
            return
    ok("both historical offenders are flagged")

    for cell in (LEGIT_F925, LEGIT_C1):
        if declares_shipped(cell):
            fail(f"should NOT be flagged: {cell!r}")
            return
    ok("a mid-sentence 'Flag shipped.' and a plain decision are not flagged")

    # Emphasis and backticks around the declaration must not hide it.
    if not declares_shipped("accepted 2026-09-14; **shipped** as F9.30"):
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

| Risk | Mitigation |
|---|---|
| Something | shipped, but this is not a slice table |

### 3. What has shipped (record, one line each)

| Slice | Deliverable | Decision |
|---|---|---|
| F9.19 | The pose sidecar | shipped 2026-09-11 |
"""


def check_table():
    text = TABLE.format(legit=LEGIT_F925, a=OFFENDER_A, b=OFFENDER_B)
    got = [slice_id for slice_id, _ in offenders(text)]
    if got != ["F9.26", "F9.27"]:
        fail(f"expected exactly F9.26 and F9.27 to be reported, got {got!r}")
        return
    ok("only the two shipped rows of the live slice table are reported")
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
