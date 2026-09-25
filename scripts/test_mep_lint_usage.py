#!/usr/bin/env python3
"""mep_lint's command line without a pack (#389): `--help`/`-h` print the
usage and exit 0; no argument, or flags that name no pack, print the usage
and exit 2 — never a traceback.

Usage: python3 scripts/test_mep_lint_usage.py
"""
from __future__ import annotations

import contextlib
import io
import sys
from pathlib import Path

SCRIPTS = Path(__file__).resolve().parent
sys.path.insert(0, str(SCRIPTS))
import mep_lint  # noqa: E402

FAILURES: list[str] = []


def _run(args: list[str]) -> tuple[object, str]:
    out = io.StringIO()
    with contextlib.redirect_stdout(out), contextlib.redirect_stderr(out):
        try:
            rc = mep_lint.main(["mep_lint.py", *args])
        except Exception as exc:  # the bug: a traceback instead of usage
            return f"raised {type(exc).__name__}: {exc}", out.getvalue()
    return rc, out.getvalue()


def check(args: list[str], want_rc: int) -> None:
    rc, text = _run(args)
    usage_line = mep_lint.__doc__.strip().splitlines()[0]
    if rc != want_rc or usage_line not in text:
        FAILURES.append(f"{args}: want exit {want_rc} with usage, got {rc!r}")
        print(f"FAIL: {args}: exit {rc!r}")
    else:
        print(f"PASS: {args} -> usage, exit {want_rc}")


def main() -> int:
    check(["--help"], 0)
    check(["-h"], 0)
    check([], 2)
    check(["--quiet"], 2)
    check(["--quiet", "--content-id"], 2)
    if FAILURES:
        print(f"\n{len(FAILURES)} failure(s)")
        return 1
    print("\nall usage checks passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
