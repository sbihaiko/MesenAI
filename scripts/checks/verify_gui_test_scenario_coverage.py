#!/usr/bin/env python3
"""Fail when a GUI test script loses a case of the manual scenario it is the machine-readable form of (#1242).

A script beside a scenario (`docs/validation/process/play-pad-only.gui-test.json` and
`docs/validation/process/play-pad-only-test-script.md`) walks the scenario's cases, but its ids are finer than the
cases: one case is several steps. So a step refers to the case it walks - in its `id`, its `precondition` or its
`expect` - and this check reads the pairing in both directions:

  - a case of the scenario no step refers to is lost, and a lost case is exactly what a narrowed script must not do
    (a `play-pad-only` step whose capability the adapter does not advertise became `manual` for #1242, never dropped);
  - a step naming a case the scenario does not define is a typo that hides a lost case.

Being named is not the same as being walked, and a table says which one it is. A table whose cases are the steps'
own ids - `PAD-01`, `TRAP-02`, `JOURNEY-01` - is walked one step per case, so a row named only in another step's
prose is a case the case tree never gets: `feel` lost its `PAD-03` that way (#1778), its text surviving in the
`SET-05` step's `expect` while no step was that case. A table no step id of which is a case - the subdivided ones,
whose steps carry names like `lib.open-screen` - keeps the looser rule above, since its steps are finer than its
cases by design.

The scenario's id is the leading token of the first cell of a row of a table headed `ID`, because a case may carry a
tag in that cell (`P4-04 **(pass 3)**`) which an anchored pattern would miss.

A script with no scenario beside it is not a failure - the scenario is authored separately - but a run in which no
script has one is: a check that checks nothing is a failure (the same rule as verify_gui_test_render.py).

Usage:
  python3 scripts/checks/verify_gui_test_scenario_coverage.py
  python3 scripts/checks/verify_gui_test_scenario_coverage.py --repo <root>

Exit 0 when every authored scenario is fully referenced; exit 1 with one error line per gap.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

CASE_REF = re.compile(r"\b[A-Z][A-Z0-9]*-\d\d\b")
CASE_TOKEN = re.compile(r"[A-Z][A-Z0-9]*-\d+")


def scenario_case_tables(markdown_text: str) -> list[list[str]]:
    """The scenario's case ids grouped by table, in file order: the leading id token of each row of each `ID` table.

    The grouping is what tells a table walked one step per case from a subdivided one (the module docstring), so it
    is kept rather than flattened away.
    """
    tables: list[list[str]] = []
    current: list[str] | None = None
    in_table = is_case_table = False
    for line in markdown_text.splitlines():
        if not line.lstrip().startswith("|"):
            in_table = is_case_table = False
            continue
        cells = [c.strip() for c in line.strip().strip("|").split("|")]
        if not in_table:
            in_table, is_case_table = True, bool(cells) and cells[0].upper() == "ID"
            current = [] if is_case_table else None
            if current is not None:
                tables.append(current)
            continue
        if all(set(c) <= set("-: ") for c in cells):  # the header separator
            continue
        if is_case_table and cells and current is not None:
            words = cells[0].split()
            token = words[0] if words else ""
            if CASE_TOKEN.fullmatch(token) and token not in current:
                current.append(token)
    return tables


def scenario_case_ids(markdown_text: str) -> list[str]:
    """The case ids of a manual scenario, in file order: the leading id token of the first cell of an `ID` table's rows."""
    out: list[str] = []
    for table in scenario_case_tables(markdown_text):
        out += [case for case in table if case not in out]
    return out


def referenced_case_ids(script: dict) -> set[str]:
    """Every case id the script's steps name, in `id`, `precondition` or `expect`."""
    out: set[str] = set()
    for batch in script.get("batches", []):
        for part in ("setup", "steps", "teardown"):
            for step in batch.get(part) or []:
                out |= set(CASE_REF.findall(" ".join(str(step.get(k, "")) for k in ("id", "precondition", "expect"))))
    return out


def step_id_set(script: dict) -> set[str]:
    """Every step's own id: the ids a table walked one step per case must appear in."""
    return {step["id"] for batch in script.get("batches", [])
            for part in ("setup", "steps", "teardown") for step in batch.get(part) or []}


def check(repo: Path) -> list[str]:
    errors: list[str] = []
    paired = 0
    for js in sorted((repo / "docs").rglob("*.gui-test.json")):
        script = json.loads(js.read_text(encoding="utf-8"))
        scenario = js.with_name((script.get("name") or js.name.split(".gui-test.json")[0]) + "-test-script.md")
        if not scenario.is_file():
            continue
        paired += 1
        text = scenario.read_text(encoding="utf-8")
        tables = scenario_case_tables(text)
        if not any(tables):
            errors.append(f"{scenario.relative_to(repo)}: no case id found: nothing to cover, and a check that "
                          f"checks nothing is a failure")
            continue
        seen = referenced_case_ids(script)
        walked = step_id_set(script)
        for table in tables:
            cases = [c for c in table if c in walked]
            one_step_per_case = bool(cases) and len(cases) != len(table)
            for case in table:
                if case not in seen:
                    errors.append(f"{js.relative_to(repo)}: case {case} of {scenario.name} is in no step's id, "
                                  f"precondition or expect")
                elif one_step_per_case and case not in walked:
                    errors.append(f"{js.relative_to(repo)}: case {case} of {scenario.name} is in no step's id, "
                                  f"while {cases[0]} of the same table is: a table walked one step per case needs "
                                  f"a step for every case")
        for case in sorted(seen - set(scenario_case_ids(text))):
            errors.append(f"{js.relative_to(repo)}: a step names case {case}, which {scenario.name} does not define")
    if paired == 0:
        errors.append("no *.gui-test.json under docs/ has a <name>-test-script.md scenario beside it: nothing to verify")
    return errors


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--repo", type=Path, default=Path(__file__).resolve().parents[2])
    errors = check(ap.parse_args().repo)
    for e in errors:
        print(f"verify_gui_test_scenario_coverage: {e}", file=sys.stderr)
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
