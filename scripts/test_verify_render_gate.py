#!/usr/bin/env python3
"""Unit tests for scripts/checks/verify_render_gate.py (ADR-0263, #968).

The render gate used to pass vacuously: every ADR-0249 render class gates on
`Assert.SkipWhen(!NativeCore.IsAvailable, ...)`, and the PR job that ran them
built no core, so a green run meant "nothing rendered". This file is the
evidence that the new job cannot repeat that:

  * a run whose render cases skipped fails, and says the core did not load
    when that is the skip reason;
  * a run with no TRX, no passing case, no PNG, a W-P PNG without its
    `.wireframe.md`, or a PNG older than the run fails;
  * a run of real, fresh renders with their reports passes;
  * the committed `.github/workflows/render-gate.yml` holds the ADR-0263
    contract: one ubuntu job, pull_request path-filtered to the UI, the
    headless tests, the render specs and the gate's own dependencies,
    superseded runs cancelled, the core built and loaded, the gate verified
    and the artifact uploaded with `if-no-files-found: error`.

Usage:
  python3 scripts/test_verify_render_gate.py
"""

from __future__ import annotations

import importlib.util
import os
import sys
import tempfile
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
CHECK_PATH = REPO_ROOT / "scripts" / "checks" / "verify_render_gate.py"

RUN_START = "2026-10-07T01:00:00.1234567+00:00"
RUN_START_EPOCH = 1791334800  # RUN_START, whole seconds


def load_check():
    spec = importlib.util.spec_from_file_location("verify_render_gate", CHECK_PATH)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def trx(*results: tuple[str, str, str]) -> str:
    rows = []
    for name, outcome, message in results:
        info = f"<Output><ErrorInfo><Message>{message}</Message></ErrorInfo></Output>" if message else ""
        rows.append(f'<UnitTestResult testName="{name}" outcome="{outcome}">{info}</UnitTestResult>')
    return (
        '<?xml version="1.0" encoding="utf-8"?>'
        '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
        f'<Times creation="{RUN_START}" start="{RUN_START}" finish="{RUN_START}" />'
        f"<Results>{''.join(rows)}</Results></TestRun>"
    )


PASSED = trx(
    ("Mesen.HeadlessTests.PlayerThemeRenderTests.PlayWorkspace", "Passed", ""),
    ("Mesen.HeadlessTests.ShareThemeRenderTests.ShareWorkspace", "Passed", ""),
)
CORE_SKIPPED = trx(
    ("Mesen.HeadlessTests.PlayerThemeRenderTests.PlayWorkspace", "NotExecuted",
     "MesenCore is not built in this checkout (looked for MESEN_CORE_LIB ...)"),
    ("Mesen.HeadlessTests.ShareThemeRenderTests.ShareWorkspace", "Passed", ""),
)
OTHER_SKIPPED = trx(
    ("Mesen.HeadlessTests.PlayerThemeRenderTests.PlayWorkspace", "Passed", ""),
    ("Mesen.HeadlessTests.ShareThemeRenderTests.ShareWorkspace", "NotExecuted", "Windows only"),
)
NOTHING_RAN = trx()
FAILED = trx(("Mesen.HeadlessTests.PlayerThemeRenderTests.PlayWorkspace", "Failed", "tint drifted"))


def run_case(check, trx_text: str | None, files: dict[str, int]) -> list[str]:
    with tempfile.TemporaryDirectory() as tmp:
        root = Path(tmp)
        trx_path = root / "render.trx"
        if trx_text is not None:
            trx_path.write_text(trx_text, encoding="utf-8")
        renders = root / "player-renders"
        renders.mkdir()
        for name, offset in files.items():
            path = renders / name
            path.write_bytes(b"x")
            stamp = RUN_START_EPOCH + offset
            os.utime(path, (stamp, stamp))
        return check.verify_run(trx_path, renders)


FRESH_PAIR = {"W-P1.png": 30, "W-P1.wireframe.md": 30, "remaster-recent.png": 31}

# (label, trx, files, a substring each failure list must contain; None = must pass)
RUN_CASES = [
    ("real fresh renders with their reports", PASSED, FRESH_PAIR, None),
    ("a render case skipped because the core did not load", CORE_SKIPPED, FRESH_PAIR, "MesenCore did not load"),
    ("a render case skipped for another reason", OTHER_SKIPPED, FRESH_PAIR, "skipped"),
    ("a render case failed", FAILED, FRESH_PAIR, "failed"),
    ("no render case ran at all", NOTHING_RAN, FRESH_PAIR, "no render case passed"),
    ("the test step wrote no TRX", None, FRESH_PAIR, "no TRX"),
    ("no PNG was written", PASSED, {}, "no PNG"),
    ("a W-P PNG without its wireframe report", PASSED, {"W-P1.png": 30}, "W-P1.wireframe.md"),
    ("a report without its PNG", PASSED, {"W-P1.png": 30, "W-P1.wireframe.md": 30, "W-P2.wireframe.md": 30}, "W-P2.png"),
    ("a PNG older than the run (committed, not fresh)", PASSED, {"W-P1.png": -600, "W-P1.wireframe.md": 30}, "older than the run"),
]


def workflow_failures(check, mutate=None) -> list[str]:
    text = (REPO_ROOT / ".github" / "workflows" / "render-gate.yml").read_text(encoding="utf-8")
    if mutate is not None:
        text = mutate(text)
    return check.verify_workflow(text)


# Each mutation of the committed workflow must be reported.
WORKFLOW_MUTATIONS = [
    ("path filter dropped", lambda t: t.replace("    paths:\n", "    paths-ignore:\n", 1), "paths"),
    ("UI.HeadlessTests/** no longer filtered", lambda t: t.replace("      - 'UI.HeadlessTests/**'\n", "", 1), "UI.HeadlessTests/**"),
    ("render specs no longer filtered", lambda t: t.replace("      - 'docs/media/gui-redesign/**'\n", "", 1), "docs/media/gui-redesign/**"),
    ("superseded runs kept", lambda t: t.replace("cancel-in-progress: true", "cancel-in-progress: false", 1), "cancel-in-progress"),
    ("macOS runner", lambda t: t.replace("runs-on: ubuntu-22.04", "runs-on: macos-15", 1), "ubuntu"),
    ("core load step removed", lambda t: t.replace("verify_render_gate.py core", "verify_render_gate.py nothing", 1), "core"),
    ("skip gate removed", lambda t: t.replace("verify_render_gate.py verify", "verify_render_gate.py nothing", 1), "verify"),
    ("empty artifact admitted", lambda t: t.replace("if-no-files-found: error", "if-no-files-found: ignore", 1), "if-no-files-found"),
    ("push trigger added", lambda t: t.replace("on:\n", "on:\n  push:\n    branches: ['main']\n", 1), "push"),
]


def main() -> int:
    check = load_check()
    failures = 0
    for label, trx_text, files, expected in RUN_CASES:
        got = run_case(check, trx_text, files)
        if expected is None and got:
            print(f"FAIL [{label}]: expected a pass, got {got}")
            failures += 1
        elif expected is not None and not any(expected in line for line in got):
            print(f"FAIL [{label}]: expected a failure mentioning {expected!r}, got {got}")
            failures += 1
        else:
            print(f"ok   [{label}]")

    got = workflow_failures(check)
    if got:
        print(f"FAIL [committed render-gate.yml]: {got}")
        failures += 1
    else:
        print("ok   [committed render-gate.yml holds the ADR-0263 contract]")
    for label, mutate, expected in WORKFLOW_MUTATIONS:
        got = workflow_failures(check, mutate)
        if not any(expected in line for line in got):
            print(f"FAIL [workflow: {label}]: expected a failure mentioning {expected!r}, got {got}")
            failures += 1
        else:
            print(f"ok   [workflow: {label}]")

    total = len(RUN_CASES) + 1 + len(WORKFLOW_MUTATIONS)
    if failures:
        print(f"FAIL: {failures} of {total} case(s) did not hold")
        return 1
    print(f"PASS: {total} case(s) - a skipped render, a missing core or an empty artifact fails the gate")
    return 0


if __name__ == "__main__":
    sys.exit(main())
