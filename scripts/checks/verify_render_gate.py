#!/usr/bin/env python3
"""Fail the ADR-0249 render gate when it rendered nothing (ADR-0263, #968).

Every render class in UI.HeadlessTests gates on
`Assert.SkipWhen(!NativeCore.IsAvailable, ...)`. Without a loaded MesenCore
those cases skip, `dotnet test` exits 0, and the `player-renders` artifact is
empty: a green check that rendered nothing. `.github/workflows/render-gate.yml`
builds the core and calls this script around the test step so that cannot
happen:

  core LIB              loads LIB and calls its `TestDll` export - the same
                        probe NativeCore.cs makes - and fails when either
                        does not work, before any test runs.
  verify TRX RENDERS    reads the test step's TRX and the renders folder and
                        fails on: no TRX, a TRX summary that is not
                        `Completed` or counts a case that did not pass (a host
                        abort leaves no row for the cases after it), any
                        render case not `Passed`
                        (skipped = `NotExecuted`, naming the core when that is
                        the skip reason), no passing case, no PNG, a `W-P*`
                        PNG without its `<name>.wireframe.md` (PlayerRender.Save
                        writes one per W-P render, #951) or a report without
                        its PNG, an expected render (EXPECTED_WIREFRAME_RENDERS:
                        W-S1 and every W-P wireframe) missing its PNG, and a
                        PNG older than the run (committed, not fresh).
  workflow [PATH]       checks that the committed workflow still holds the
                        ADR-0263 contract (path filter, cancelled superseded
                        runs, one ubuntu job, the two calls above, an upload
                        that fails when empty). scripts/test_verify_render_gate.py
                        runs it in the python-tests job, on every PR.

Exit 0 when everything holds, 1 with one `FAIL:` line per broken rule.
"""

from __future__ import annotations

import ctypes
import re
import sys
import xml.etree.ElementTree as ET
from datetime import datetime
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
WORKFLOW_PATH = REPO_ROOT / ".github" / "workflows" / "render-gate.yml"
TRX_NS = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"

# The pull_request paths ADR-0263 requires: the UI, the headless tests, the
# render specs (wireframe PNGs, the token source, the committed renders the
# fresh ones drift against) and the gate's own dependencies.
REQUIRED_PATHS = [
    "UI/**",
    "UI.HeadlessTests/**",
    "docs/media/gui-redesign/**",
    "scripts/render_gui_wireframes.py",
    "UI.Tests/Theme/PlayerRenders/**",
    "InteropDLL/**",
    "makefile",
    ".github/workflows/render-gate.yml",
    "scripts/checks/verify_render_gate.py",
]

# Every W-P render the *RenderTests cases write with a wireframe report
# (PlayerRender.Save, #951), as the Linux run of 2026-10-07 wrote them, plus
# W-S1, the shell frame (a PNG only: PlayerRender.Save reports W-P names).
# Pinned so a deleted case, one renamed out of `*RenderTests`, or a run that
# stopped before writing it fails the gate instead of leaving it green on what
# did run. Adding a W-P render case means adding its name here; every Player
# wireframe in docs/media/gui-redesign/ has one (#1010).
EXPECTED_WIREFRAME_RENDERS = (
    "W-S1",
    "W-P1", "W-P2", "W-P3", "W-P4", "W-P5", "W-P6", "W-P7", "W-P8", "W-P8b",
    "W-P8c", "W-P9", "W-P10", "W-P11", "W-P12", "W-P13", "W-P13-confirm",
    "W-P14", "W-P15", "W-P15-pill", "W-P16", "W-P19",
)

# Wireframes drawn into docs/media/gui-redesign/ whose render case has not
# landed yet. **Nothing in this module reads this constant**: verify_run and
# verify_core work off EXPECTED_WIREFRAME_RENDERS alone. It is the pinned
# exemption list that scripts/test_verify_render_gate.py reads, so that test
# expects a wireframe drawn without a render case instead of failing on it —
# and the same test asserts this set and EXPECTED_WIREFRAME_RENDERS stay
# disjoint, so the exemption cannot survive into the run the ticket lands.
# ADR-0264 Decision 12 draws W-P19 and W-P19b with
# scripts/render_gui_wireframes.py *before* the sheet they picture exists; PRD
# row L.1 (#1032) builds it and moves W-P19 up into EXPECTED_WIREFRAME_RENDERS,
# and L.2 (#1033) does the same for W-P19b. Pinned like that set is: a
# wireframe drawn without an entry in one of the two still fails.
WIREFRAMES_AWAITING_RENDER_CASE = ("W-P19b",)

# A PNG may predate the TRX's start by this much (filesystem/clock rounding).
FRESHNESS_SLACK_SECONDS = 2.0


def _run_start(root: ET.Element) -> float | None:
    times = root.find(f"{TRX_NS}Times")
    stamp = times.get("start") if times is not None else None
    if not stamp:
        return None
    # TRX writes 7 fractional digits; datetime.fromisoformat (3.10) takes 6.
    stamp = re.sub(r"(\.\d{6})\d+", r"\1", stamp)
    return datetime.fromisoformat(stamp).timestamp()


def _summary_failures(root: ET.Element) -> list[str]:
    summary = root.find(f"{TRX_NS}ResultSummary")
    if summary is None:
        return ["the TRX has no ResultSummary: the test run did not finish"]
    failures = []
    outcome = summary.get("outcome", "?")
    if outcome != "Completed":
        failures.append(f"the TRX summary outcome is {outcome}, not Completed")
    counters = summary.find(f"{TRX_NS}Counters")
    total = int(counters.get("total", "0")) if counters is not None else 0
    passed = int(counters.get("passed", "0")) if counters is not None else 0
    if total != passed:
        failures.append(f"the TRX counters show {passed} of {total} case(s) passed")
    return failures


def verify_run(trx_path: Path, renders: Path, expected=EXPECTED_WIREFRAME_RENDERS) -> list[str]:
    if not trx_path.is_file():
        return [f"no TRX at {trx_path}: the render tests never reported (did the test step run?)"]
    root = ET.parse(trx_path).getroot()
    failures = _summary_failures(root)
    passed = 0
    for result in root.iter(f"{TRX_NS}UnitTestResult"):
        name = result.get("testName", "?")
        outcome = result.get("outcome", "?")
        message = result.findtext(f"{TRX_NS}Output/{TRX_NS}ErrorInfo/{TRX_NS}Message", default="").strip()
        if outcome == "Passed":
            passed += 1
        elif outcome == "NotExecuted":
            if "MesenCore" in message:
                failures.append(f"{name} skipped: MesenCore did not load ({message})")
            else:
                failures.append(f"{name} skipped: {message or 'no reason given'}")
        else:
            failures.append(f"{name} {outcome.lower()}: {message}")
    if passed == 0:
        failures.append("no render case passed: the filter matched nothing or every case skipped")

    pngs = {p.stem: p for p in renders.glob("*.png")} if renders.is_dir() else {}
    reports = {p.name[: -len(".wireframe.md")] for p in renders.glob("*.wireframe.md")} if renders.is_dir() else set()
    if not pngs:
        failures.append(f"no PNG in {renders}: the artifact would be empty")
    for stem in sorted(expected):
        if stem not in pngs:
            failures.append(f"{stem}.png was not rendered: its case is gone, renamed out of *RenderTests, or never ran")
    for stem in sorted(pngs):
        if stem.startswith("W-P") and stem not in reports:
            failures.append(f"{stem}.png has no {stem}.wireframe.md next to it")
    for stem in sorted(reports - set(pngs)):
        failures.append(f"{stem}.wireframe.md has no {stem}.png next to it")
    start = _run_start(root)
    if start is not None:
        for stem, path in sorted(pngs.items()):
            if path.stat().st_mtime < start - FRESHNESS_SLACK_SECONDS:
                failures.append(f"{path.name} is older than the run: not a fresh render")
    return failures


def verify_core(library: Path) -> list[str]:
    if not library.is_file():
        return [f"MesenCore not found at {library}: the core build did not produce it"]
    try:
        core = ctypes.CDLL(str(library))
        test_dll = core.TestDll
    except (OSError, AttributeError) as ex:
        return [f"MesenCore at {library} did not load: {ex}"]
    test_dll.restype = ctypes.c_bool
    if not test_dll():
        return [f"MesenCore at {library} loaded but TestDll() returned false"]
    return []


def _steps_text(job: dict) -> str:
    return "\n".join(str(step.get("run", "")) + str(step.get("uses", "")) for step in job.get("steps") or [])


def verify_workflow(text: str) -> list[str]:
    import yaml  # PyYAML is a python-tests dependency; only this mode needs it

    doc = yaml.safe_load(text) or {}
    failures = []
    # YAML 1.1 reads a bare `on:` key as the boolean True.
    triggers = doc.get("on", doc.get(True)) or {}
    if not isinstance(triggers, dict) or set(triggers) != {"pull_request"}:
        failures.append(f"triggers must be pull_request only (no push, no dispatch), got {sorted(map(str, triggers))}")
    pull_request = triggers.get("pull_request") if isinstance(triggers, dict) else None
    paths = (pull_request or {}).get("paths") or []
    if not paths:
        failures.append("pull_request has no paths filter: the job would run on every PR")
    for required in REQUIRED_PATHS:
        if required not in paths:
            failures.append(f"pull_request paths do not include {required!r}")
    for broad in ("**", "*", "**/*"):
        if broad in paths:
            failures.append(f"pull_request paths include {broad!r}, which matches every PR")

    concurrency = doc.get("concurrency") or {}
    if "github.ref" not in str(concurrency.get("group", "")) or concurrency.get("cancel-in-progress") is not True:
        failures.append("concurrency must group on github.ref with cancel-in-progress: true")

    jobs = doc.get("jobs") or {}
    if len(jobs) != 1:
        failures.append(f"exactly one job expected, got {len(jobs)}")
        return failures
    job = next(iter(jobs.values()))
    if not str(job.get("runs-on", "")).startswith("ubuntu-"):
        failures.append(f"the job must run on ubuntu, got {job.get('runs-on')!r}")
    steps = job.get("steps") or []
    text_of_steps = _steps_text(job)
    if "make" not in text_of_steps or "core" not in text_of_steps:
        failures.append("no step builds the core (make ... core)")
    if "verify_render_gate.py core" not in text_of_steps:
        failures.append("no step runs 'verify_render_gate.py core' before the tests")
    if "dotnet test UI.HeadlessTests/UI.HeadlessTests.csproj" not in text_of_steps or "trx" not in text_of_steps:
        failures.append("no step runs the headless render tests with a TRX logger")
    if "verify_render_gate.py verify" not in text_of_steps:
        failures.append("no step runs 'verify_render_gate.py verify' on the TRX and renders")
    uploads = [s for s in steps if "upload-artifact" in str(s.get("uses", ""))]
    if not uploads:
        failures.append("no upload-artifact step for the renders")
    for upload in uploads:
        if (upload.get("with") or {}).get("if-no-files-found") != "error":
            failures.append("the renders upload must set if-no-files-found: error")
        if "always()" not in str(upload.get("if", "")):
            failures.append("the renders upload must run if: always() so a red gate still attaches its PNGs")
    return failures


def main(argv: list[str]) -> int:
    if len(argv) >= 2 and argv[0] == "core":
        failures = verify_core(Path(argv[1]))
    elif len(argv) >= 3 and argv[0] == "verify":
        failures = verify_run(Path(argv[1]), Path(argv[2]))
    elif argv and argv[0] == "workflow":
        path = Path(argv[1]) if len(argv) >= 2 else WORKFLOW_PATH
        failures = verify_workflow(path.read_text(encoding="utf-8"))
    else:
        print(__doc__)
        return 2
    for line in failures:
        print(f"FAIL: {line}")
    if failures:
        return 1
    print(f"PASS: render gate {argv[0]}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
