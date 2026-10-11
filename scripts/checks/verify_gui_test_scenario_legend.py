#!/usr/bin/env python3
"""Fail when a published GUI test scenario's legend names records its areas do not hold (#1764).

A scenario's body carries one legend line, and a reader follows it to the case records. While the records were
direct sub-issues of the scenario the line could name them by their own range, and #1286's did: it said
`the case records (#1288-#1387)` - the 100 issues the 100-sub-issue cap had left on the scenario. The area
migration (ADR-0275 section 1) re-parented those 100 into the 16 areas' Cases containers and created the rest, so
the records became #1288-#1787. The line was typed at migration time and nothing bound it to the map, so it stayed
naming a range that no longer contains the scenario's records, and the 194 the migration created were invisible to
anyone who followed it.

The map beside the script (`<name>.gui-test.issues.json`) is where the record numbers live, and its `"layout":
"areas"` says how to read them. So the legend is rendered from the map, not typed: this check derives the span and
the area count from the map's areas and fails when the committed legend - the line the publish step writes into the
scenario body, kept in the map as `"legend"` - disagrees with any of them. A migration that changes the areas and
not the legend is red here, which is what stops the next one from leaving it stale.

The format token is checked the same way, against the ADR's own `Status` line: #1286's legend said
`Format: ADR-0275 (proposed)` after the ADR was accepted, the same staleness one clause later.

Two shapes make a legend unverifiable rather than wrong, and both are failures: a map laid out as areas that carries
no areas (its line can say `0 areas` and be internally consistent while naming nothing), and one that carries no
cases (the range its line names has no record to be checked against). A flat scenario - no `"layout": "areas"` at
all - has no areas to name and is not a failure, but a run in which no map carries any is: a check that checks
nothing is a failure (the same rule as verify_gui_test_scenario_coverage).

Usage:
  python3 scripts/checks/verify_gui_test_scenario_legend.py
  python3 scripts/checks/verify_gui_test_scenario_legend.py --repo <root>

Exit 0 when every area map's legend names what its areas hold; exit 1 with one error line per mismatch.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

AREA_MAPS = "docs/**/*.gui-test.issues.json"
SPAN = re.compile(r"#(\d+)–#(\d+)")
AREA_COUNT = re.compile(r"the (\d+) areas below")
FORMAT = re.compile(r"Format: (ADR-\d+) \(([a-z]+)\)")
STATUS = re.compile(r"^- Status:\s*([a-z]+)", re.MULTILINE)


def records_by_area(doc: dict) -> dict[str, list[int]]:
    """The map's record numbers grouped by area key, each list ascending.

    `"layout": "areas"` is what makes a map readable this way: each case entry names the area and the container it
    was published into (ADR-0275 section 3), so the areas are the map's own, not a second list to keep in step.
    """
    by_area: dict[str, list[int]] = {key: [] for key in doc.get("areas", {})}
    for entry in doc.get("cases", {}).values():
        area = entry.get("area")
        if area not in by_area:
            by_area.setdefault(str(area), [])
        by_area[area].append(entry["number"])
    return {key: sorted(numbers) for key, numbers in by_area.items()}


def record_span(doc: dict) -> tuple[int, int] | None:
    numbers = sorted(n for numbers in records_by_area(doc).values() for n in numbers)
    return (numbers[0], numbers[-1]) if numbers else None


def adr_status(repo: Path, adr_id: str) -> str | None:
    """The status token of the ADR the legend names, read from the register: `accepted`, `proposed`, `superseded`."""
    for path in sorted((repo / "docs" / "adr").glob(f"*/{adr_id[4:]}-*.md")):
        found = STATUS.search(path.read_text(encoding="utf-8"))
        if found:
            return found.group(1)
    return None


def check_map(repo: Path, path: Path, doc: dict) -> list[str]:
    rel = path.relative_to(repo)
    areas_by_key = doc.get("areas") or {}
    if not areas_by_key:
        return [f"{rel}: the map is laid out as areas and carries no areas, so a legend naming `0 areas` would "
                f"verify nothing"]
    legend = doc.get("legend")
    if not legend:
        return [f"{rel}: the map carries no legend, so nothing tells a reader which records the "
                f"scenario's areas hold"]
    errors = []
    span, areas = record_span(doc), len(areas_by_key)
    named = SPAN.search(legend)
    if span is None:
        errors.append(f"{rel}: the map carries no cases, so the record range its legend names "
                      f"({named.group(0) if named else 'no record range'}) has no record to be checked against")
    elif not named or tuple(int(g) for g in named.groups()) != span:
        errors.append(f"{rel}: the legend names {named.group(0) if named else 'no record range'}, and the areas "
                      f"hold #{span[0]}–#{span[1]}")
    counted = AREA_COUNT.search(legend)
    if not counted or int(counted.group(1)) != areas:
        errors.append(f"{rel}: the legend names {counted.group(1) if counted else 'no number of'} areas, and the "
                      f"map has {areas} areas")
    fmt = FORMAT.search(legend)
    if not fmt:
        errors.append(f"{rel}: the legend names no format ADR, so the status it carries cannot be checked")
    else:
        status = adr_status(repo, fmt.group(1))
        if status is None:
            errors.append(f"{rel}: the legend names {fmt.group(1)}, which has no file in docs/adr/")
        elif status != fmt.group(2):
            errors.append(f"{rel}: the legend names {fmt.group(1)} ({fmt.group(2)}), and its Status line reads "
                          f"'{status}'")
    return errors


def check(repo: Path) -> list[str]:
    errors: list[str] = []
    checked = 0
    for path in sorted(repo.glob(AREA_MAPS)):
        try:
            doc = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, ValueError) as exc:
            errors.append(f"{path.relative_to(repo)}: not readable as JSON: {exc}")
            continue
        if doc.get("layout") != "areas":
            continue  # a flat scenario has no areas for a legend to name
        checked += 1
        errors.extend(check_map(repo, path, doc))
    if checked == 0:
        errors.append(f"no {AREA_MAPS} under docs/ publishes a scenario as areas: nothing to verify")
    return errors


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--repo", type=Path, default=Path(__file__).resolve().parents[2])
    errors = check(ap.parse_args().repo)
    for e in errors:
        print(f"verify_gui_test_scenario_legend: {e}", file=sys.stderr)
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
