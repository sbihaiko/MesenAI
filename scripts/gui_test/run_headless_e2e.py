#!/usr/bin/env python3
"""Run the hook e2e case in UI.HeadlessTests and fail unless it is exactly 1 passed, 0 skipped.

The headless suite skips silently when NativeCore is unavailable (ADR-0272 §5 risk
note), so a green exit code proves nothing: the verdict is read off the summary line.
Needs MESEN_CORE_LIB (the built MesenCore library); without it the run is a failure,
never a pass.

    MESEN_CORE_LIB=<path> python3 scripts/gui_test/run_headless_e2e.py [rid]
"""
import os
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CASE = "GuiTestHook_e2e_a_runner_drives_the_Home_over_the_socket"
TIMEOUT_SECONDS = 900  # a hung dotnet must fail the run, never block it
SUMMARY = re.compile(r"(Passed|Failed)!\s*-\s*Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+),\s*Total:\s*(\d+)")


def verdict(output):
    """Return a list of failure reasons; empty means exactly 1 passed with 0 skipped."""
    match = SUMMARY.search(output)
    if not match:
        return ["no test summary line in the dotnet output"]
    failed, passed, skipped, total = (int(g) for g in match.groups()[1:])
    if (failed, passed, skipped, total) == (0, 1, 0, 1):
        return []
    return [f"expected exactly 1 passed and 0 skipped, got passed={passed} skipped={skipped} failed={failed} total={total}"]


def run(rid, core_lib):
    env = {**os.environ, "MESEN_CORE_LIB": core_lib}
    try:
        done = subprocess.run(
            ["dotnet", "test", "UI.HeadlessTests", f"-p:RuntimeIdentifier={rid}", "--filter", f"FullyQualifiedName~{CASE}"],
            cwd=ROOT, env=env, capture_output=True, text=True, timeout=TIMEOUT_SECONDS,
        )
    except subprocess.TimeoutExpired as ex:
        partial = ex.stdout.decode(errors="replace") if isinstance(ex.stdout, bytes) else (ex.stdout or "")
        return [f"dotnet test did not finish within {TIMEOUT_SECONDS}s"], partial
    output = done.stdout + done.stderr
    reasons = verdict(output)
    if done.returncode != 0:
        reasons.append(f"dotnet test exited {done.returncode}")
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
