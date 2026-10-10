#!/usr/bin/env python3
"""Fail when a committed GUI test script claims a surface the `mesen-gui` adapter or the application does not have.

Issue #1242 criterion 1: a script must validate against the adapter's capabilities with no rejection, and
every `manual` step must name the capability it is missing. The narrowed `play-pad-only` script was produced
outside this repository, and nothing here re-derived it; this check is the repository's own half, so the
script cannot go quietly stale after the next adapter change (issue #1182 adds AutomationIds) or the next
hook surface. It reads the surface from its two sources of truth, never from a copy:

  - `scripts/gui_test/mesen_gui_adapter.py` - the target's advertised `capabilities()`, its `FIXTURE_KINDS`
    and its `PROFILES` (the `settings` fixture's `profile`);
  - `UI/**.axaml` / `UI/**.cs` - the application's `AutomationProperties.AutomationId`s, which are the only
    control ids a check may name (ADR-0272 item 3).

Four rules, per `docs/**/*.gui-test.json`:

  - an automated step, or `requires`, naming an action, check or variant value the adapter does not
    advertise: the run command refuses the script before launch (ADR-0272 item 6);
  - an automated step checking or waiting on a control id no `UI/**` file declares: `failed (unknown id)`
    at runtime, never `false` (ADR-0272 item 6);
  - a fixture kind, or a `settings.profile`, the adapter does not support: `launch()` fails, it never skips;
  - a `manual` step whose expect text claims it needs an action, check, variant, control or fixture the
    adapter or the application really has: the claim is what makes the manual list readable, and a claim
    that has gone stale is exactly how a narrowed script drifts out of date.

The last rule is what the free-text reasons would otherwise hide: it needs no rewrite of the texts, and it
turns each `needs <kind> <name>` into a fact the repository can re-check.

Usage:
  python3 scripts/checks/verify_gui_test_adapter_surface.py
  python3 scripts/checks/verify_gui_test_adapter_surface.py --repo <root>

Exit 0 when every script names only real surface; exit 1 with one error line per drift.
"""
from __future__ import annotations

import argparse
import importlib.util
import json
import re
import sys
from pathlib import Path

ADAPTER = Path("scripts") / "gui_test" / "mesen_gui_adapter.py"
VARIANT_AXIS = re.compile(r"^([A-Za-z]+\.[A-Za-z]+)=(\S+)$")
AUTOMATION_ID = re.compile(r'AutomationId\s*[=(]\s*"([^"]+)"')
CHECK_ID = re.compile(r"\b(?:ui\.focused|ui\.visible)\s*(?:==|!=)\s*(\S+)")
WAIT_ID = re.compile(r"\bui\.(?:focused|screen)\s*==\s*(\S+)")
CLAIM = re.compile(r"needs (action|check|variant|control|fixture)\s+([^\s,;)]+)")


def load_adapter(repo: Path, errors: list[str]):
    path = repo / ADAPTER
    if not path.is_file():
        errors.append(f"{ADAPTER}: the mesen-gui adapter is missing")
        return None
    spec = importlib.util.spec_from_file_location("mesen_gui_adapter_surface", path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def automation_ids(repo: Path, errors: list[str]) -> set[str]:
    ids: set[str] = set()
    for p in sorted((repo / "UI").rglob("*")):
        if p.suffix not in (".axaml", ".cs") or not p.is_file():
            continue
        ids.update(AUTOMATION_ID.findall(p.read_text(encoding="utf-8", errors="replace")))
    if not ids:
        errors.append("UI/**: no AutomationId is declared anywhere, so no control id can be verified "
                      "- a check that checks nothing is a failure")
    return ids


def step_ids(step: dict) -> list[str]:
    """Every control id an automated step names in its check or its wait."""
    out: list[str] = []
    check = step.get("check") or {}
    for key in ("is", "within"):
        if key in (check.get("args") or {}):
            out.append(check["args"][key])
    wait = step.get("wait") or {}
    if wait.get("check"):
        out += WAIT_ID.findall(wait["check"])
    return out


def claim_errors(name: str, sid: str, expect: str, caps: dict, kinds: set[str], ids: set[str]) -> list[str]:
    """A manual step's `needs <kind> <name>` must name something that really is missing."""
    out: list[str] = []
    for kind, claimed in CLAIM.findall(expect):
        why = None
        if kind == "action" and claimed in caps.get("actions", []):
            why = "the adapter advertises it"
        elif kind == "check" and claimed in caps.get("checks", []):
            why = "the adapter advertises it"
        elif kind == "variant":
            m = VARIANT_AXIS.match(claimed)
            if m and m.group(2) in caps.get("variants", {}).get(m.group(1), []):
                why = "the adapter advertises it"
        elif kind == "control" and claimed in ids:
            why = "the application declares that AutomationId"
        elif kind == "fixture" and claimed in kinds:
            why = "the adapter supports it"
        if why:
            out.append(f"{name}: step {sid} is manual but claims it needs {kind} {claimed}, and {why} - "
                       f"stale manual claim")
    return out


def check_script(doc: dict, name: str, caps: dict, kinds: set[str], profiles: list[str],
                 ids: set[str]) -> list[str]:
    bad: list[str] = []
    advertised_variants = caps.get("variants", {})
    for axis, values in (doc.get("variants") or {}).items():
        for value in values:
            if value not in advertised_variants.get(axis, []):
                bad.append(f"{name}: adapter does not advertise variant {axis}={value}")
    for axis, values in (doc.get("requires") or {}).items():
        advertised = caps.get(axis, [])
        for value in values:
            if value not in advertised:
                bad.append(f"{name}: adapter does not advertise {axis[:-1]} {value}")
    for fixture, value in (doc.get("fixtures") or {}).items():
        if fixture not in kinds:
            bad.append(f"{name}: fixture kind {fixture!r} is not supported by mesen-gui")
        if fixture == "settings":
            profile = (value or {}).get("profile", "fresh")
            if profile not in profiles:
                bad.append(f"{name}: settings profile {profile!r} is not supported by mesen-gui")
    for batch in doc.get("batches", []):
        for part in ("setup", "steps", "teardown"):
            for step in batch.get(part, []):
                sid = step.get("id", "?")
                if step.get("mode") != "automated":
                    bad += claim_errors(name, sid, step.get("expect") or "", caps, kinds, ids)
                    continue
                action = step.get("action") or {}
                if action.get("name") and action["name"] not in caps.get("actions", []):
                    bad.append(f"{name}: step {sid} is automated but the adapter does not advertise "
                               f"action {action['name']}")
                check = step.get("check") or {}
                if check.get("name") and check["name"] not in caps.get("checks", []):
                    bad.append(f"{name}: step {sid} is automated but the adapter does not advertise "
                               f"check {check['name']}")
                for ident in step_ids(step):
                    if ident not in ids:
                        bad.append(f"{name}: step {sid} names control id {ident!r}, which no UI/** file "
                                   f"declares as an AutomationId")
    return bad


def check(repo: Path) -> list[str]:
    errors: list[str] = []
    scripts = sorted((repo / "docs").rglob("*.gui-test.json"))
    if not scripts:
        errors.append("no *.gui-test.json under docs/: nothing to verify, and a check that checks nothing "
                      "is a failure")
        return errors
    adapter = load_adapter(repo, errors)
    ids = automation_ids(repo, errors)
    if adapter is None:
        return errors
    caps = adapter.MesenGuiAdapter().capabilities()
    for js in scripts:
        rel = str(js.relative_to(repo))
        errors += check_script(json.loads(js.read_text(encoding="utf-8")), rel, caps,
                               set(adapter.FIXTURE_KINDS), list(adapter.PROFILES), ids)
    return errors


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--repo", type=Path, default=Path(__file__).resolve().parents[2])
    errors = check(ap.parse_args().repo)
    for e in errors:
        print(f"verify_gui_test_adapter_surface: {e}", file=sys.stderr)
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
