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
    that has gone stale is exactly how a narrowed script drifts out of date;
  - a precondition written as a check that is not one comparison: the supervised runner reads everything
    before ` == ` as the check name, so `ui.screen == play.home and ui.focused == play.home.open-rom`
    asks for a check called `ui.screen == play.home and ui.focused == play.home.open-rom`, and the batch
    dies with `UnknownId` (the #1242 run did);
  - a `manual` step whose precondition is a check at all: the runner evaluates every step's precondition
    and then does not run a manual one, and a manual start state is reached by hand - the adapter cannot
    confirm it (the same run died on `ui.focused == play.library.search`). A manual precondition says
    what the person driving the pad has in front of them.

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
#The control ids a check names inside a `wait.check` string: `ui.focused`, `ui.visible`
#and `ui.screen`, in either direction. One pattern for both places an id can appear - a
#step's own `check` (`check.args`) and the check text a `wait` carries - so no id form is
#the one the rule forgets to read.
ID_IN_CHECK = re.compile(r"\bui\.(?:focused|visible|screen)\s*(?:==|!=)\s*(\S+)")
CLAIM = re.compile(r"needs (action|check|variant|control|fixture)\s+([^\s,;)]+)")
#A precondition the runner can evaluate: one comparison and nothing else. Prose (a cold start,
#a fixture, "every Play surface") is fine and is not read; a compound `A and B` is not.
PRECONDITION = re.compile(r"ui\.(?:screen|focused|visible|dialogs)\s*==\s*\S+\Z")


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
    """Every control id an automated step names in its check or its wait.

    A step's own `check` carries its id in `args`; a `wait` carries it inside the check
    text, read with the same pattern. A malformed step (an `args` that is not a dict, a
    value that is not a string) names no id this check can read and is skipped: the
    format validator is what refuses it, and this check's contract is one error line per
    drift, never a traceback.
    """
    out: list[str] = []
    check = step.get("check") or {}
    args = check.get("args") if isinstance(check, dict) else None
    if isinstance(args, dict):
        for key in ("is", "within"):
            value = args.get(key)
            if isinstance(value, str) and value:
                out.append(value)
    wait = step.get("wait") or {}
    if isinstance(wait, dict) and isinstance(wait.get("check"), str):
        out += ID_IN_CHECK.findall(wait["check"])
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
                pre = (step.get("precondition") or "").strip()
                if "==" in pre and not PRECONDITION.match(pre):
                    bad.append(f"{name}: step {sid} has precondition {pre!r}, which is not one "
                               f"`ui.<key> == <value>` comparison - the supervised runner reads "
                               f"everything before ' == ' as the check name, so the batch fails with "
                               f"`UnknownId`, as the #1242 run did")
                if step.get("mode") != "automated":
                    if "==" in pre:
                        bad.append(f"{name}: step {sid} is manual but its precondition {pre!r} is a "
                                   f"check - the runner evaluates it and then does not run the step, "
                                   f"so it asks the adapter for a state only a hand reaches "
                                   f"(#1242: the library batch died with `UnknownId`)")
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
