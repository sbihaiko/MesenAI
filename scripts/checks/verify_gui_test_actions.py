#!/usr/bin/env python3
"""Fail when a GUI test script's `requires.actions` drifts from the actions its steps use.

Two rules, per `docs/**/*.gui-test.json`:
  - the set of action names used by setup and steps equals `requires.actions`;
  - no `under-test` step runs `nav.goal` (ADR-0271 section 3: navigation is setup,
    never the behavior under test).

Usage:
  python3 scripts/checks/verify_gui_test_actions.py
  python3 scripts/checks/verify_gui_test_actions.py --repo <root>

Exit 0 when every script is consistent; exit 1 with one error line per drift.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path


def check_script(doc: dict, name: str) -> list[str]:
    errors: list[str] = []
    used: set[str] = set()
    for batch in doc.get("batches", []):
        for step in batch.get("setup", []) + batch.get("steps", []):
            action = step.get("action")
            if not action:
                continue
            used.add(action["name"])
            if step.get("role") == "under-test" and action["name"] == "nav.goal":
                errors.append(f"{name}: step {step['id']} is under-test but runs nav.goal")
    required = set(doc.get("requires", {}).get("actions", []))
    if used != required:
        errors.append(f"{name}: actions used differ from requires.actions "
                      f"(unrequired: {sorted(used - required)}, unused: {sorted(required - used)})")
    return errors


def check(repo: Path) -> list[str]:
    scripts = sorted((repo / "docs").rglob("*.gui-test.json"))
    if not scripts:
        return ["no *.gui-test.json under docs/: nothing to verify"]
    errors: list[str] = []
    for js in scripts:
        errors += check_script(json.loads(js.read_text(encoding="utf-8")), str(js.relative_to(repo)))
    return errors


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--repo", type=Path, default=Path(__file__).resolve().parents[2])
    errors = check(ap.parse_args().repo)
    for e in errors:
        print(f"verify_gui_test_actions: {e}", file=sys.stderr)
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
