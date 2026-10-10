#!/usr/bin/env python3
"""Run the hook e2e cases in UI.HeadlessTests and fail unless both passed with 0 skipped.

The headless suite skips silently when NativeCore is unavailable (ADR-0272 §5 risk
note), so a green exit code proves nothing: the verdict is read off the summary line.
Needs MESEN_CORE_LIB (the built MesenCore library); without it the run is a failure,
never a pass.

#1242: two cases, and both are required. The Home case drives the walk over the
socket; the fresh-home case drives the pad-only script's own Home and Library steps
(`home.first-focus`, `home.open-library`, and the library batch's `lib.home-ring`,
`lib.reach-library`, `lib.open-screen`, `lib.back-to-home`, `lib.back-ring-on-opener`).
A filter that ran only one of them, or a case that skipped, is a failure.

    MESEN_CORE_LIB=<path> python3 scripts/gui_test/run_headless_e2e.py [rid]
"""
import os
import re
import signal
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CASE = "GuiTestHook_e2e_a_runner_drives_the_Home_over_the_socket"
CASES = (
    "GuiTestHookTests." + CASE,
    "GuiTestHookTests.GuiTestHook_e2e_A_on_the_fresh_home_opens_the_library_over_the_socket",
)
#Both cases answer the same filter; the verdict below is what requires both to have run.
FILTER = "GuiTestHook_e2e"
TIMEOUT_SECONDS = 900  # a hung dotnet must fail the run, never block it
SUMMARY = re.compile(r"(Passed|Failed)!\s*-\s*Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+),\s*Total:\s*(\d+)")


def verdict(output):
    """Return a list of failure reasons; empty means every case passed with 0 skipped."""
    match = SUMMARY.search(output)
    if not match:
        return ["no test summary line in the dotnet output"]
    failed, passed, skipped, total = (int(g) for g in match.groups()[1:])
    if (failed, passed, skipped, total) == (0, len(CASES), 0, len(CASES)):
        return []
    return [f"expected exactly {len(CASES)} passed and 0 skipped, got passed={passed} skipped={skipped} failed={failed} total={total}"]


def run(rid, core_lib):
    env = {**os.environ, "MESEN_CORE_LIB": core_lib}
    # Own session: a timeout kills the whole group, so a grandchild (testhost) cannot hold the pipes open.
    proc = subprocess.Popen(
        ["dotnet", "test", "UI.HeadlessTests", f"-p:RuntimeIdentifier={rid}", "--filter", f"FullyQualifiedName~{FILTER}"],
        cwd=ROOT, env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, start_new_session=True,
    )
    try:
        stdout, stderr = proc.communicate(timeout=TIMEOUT_SECONDS)
    except subprocess.TimeoutExpired:
        try:
            os.killpg(proc.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
        stdout, stderr = proc.communicate()
        return [f"dotnet test did not finish within {TIMEOUT_SECONDS}s"], (stdout or "") + (stderr or "")
    output = stdout + stderr
    reasons = verdict(output)
    if proc.returncode != 0:
        reasons.append(f"dotnet test exited {proc.returncode}")
    return reasons, output


def main(argv):
    core_lib = os.environ.get("MESEN_CORE_LIB", "")
    if not core_lib or not Path(core_lib).is_file():
        print("MESEN_CORE_LIB must name the built MesenCore library; a skipped headless case is not a pass", file=sys.stderr)
        return 2
    rid = argv[1] if len(argv) > 1 else "osx-arm64"
    reasons, output = run(rid, core_lib)
    print(output[-2000:])
    for reason in reasons:
        print("FAIL:", reason, file=sys.stderr)
    return 1 if reasons else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
