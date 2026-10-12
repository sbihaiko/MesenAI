#!/usr/bin/env python3
"""The graph the squad loop dispatches from must bound its own example (issue #1840).

The spawn-field guard (`scripts/test_squad_graph_spawn_fields.py`) reds on the
graph that taught the rejected field: its example carried `turns` while the field
list beside it, in the same text, did not. What that graph also did was reach a
router, which nothing watched: `run-20261011-160224-2d865b` and
`run-20261011-215112-ed4e92` both ended
`no_dispatch, spawn_rejected:unknown_field:turns` having dispatched nothing, so a
whole run's spend bought nothing, and the conflict the second one was dispatched
to resolve stayed unresolved until a later launch happened to produce a clean
request. The guard red on that graph all along; nothing ran it against the
revision the loop dispatches.

That revision is not out of reach. The launcher
(`~/.claude/skills/squad-goal/scripts/launch.py`) reads
`docs/squad/workflows/<workflow>.graph.json` out of the working tree of the
checkout it is started in - `$SG_CHECKOUT` in the loop's own environment, which
every child and every suite the loop starts inherits - and the engine copies that
file into the run directory, so `$SG_CHECKOUT/runs/<run>/graph.json` records the
graph a run was shown. Two sources, one rule, and the rule is the guard's: it is
executed as its own process here, so nothing is reimplemented.

1. The dispatch source: `docs/squad/workflows/*.graph.json` in the loop's
   checkout, the file the next launch there hands the engine. This is the half
   that can be fixed before another run is lost.
2. The newest dispatch that checkout recorded: `runs/run-*/graph.json`, the
   revision the last launch handed the router. Run directories are
   `run-<date>-<time>-<hex>`, so the newest name is the newest dispatch.

Why the newest and not every recorded dispatch: the question a launch asks is
about the graph it is using now, and dispatches are immutable history. Of the 604
dispatches the loop's checkout had recorded on 2026-10-11, 451 carried the
pre-fix teach; a check red on those could never pass again, so it would be a
check nobody can act on. An older dispatch is therefore not read, and that is the
whole of what the newest source claims.

Which checkout is read: `--dispatch`, else `$SG_CHECKOUT`, else the tree this
check lives in. The loop's checkout is a different tree from the one a child
runs in, and reading this tree would answer a question nobody asked - whether the
graph *here* is bounded, where the graph that reaches a router is the one over
there. Where no checkout is named (the GitHub runner, which is nobody's dispatch
checkout) the tree this check lives in is read instead, and the run says which
tree that was.

Where a runtime plugin is reachable the guard reads its REQUEST_FIELDS as the
contract (ADR-0276 §1) and a graph teaching a field that plugin rejects is red;
where none is, it checks each example against the field list its own text
declares (ADR-0276 §2), which is exactly the #1800/#1840 shape. This check names
no contract of its own and never hands the guard the committed copy under
`scripts/vendor/agent_squad/`, which is a recording and not a contract
(ADR-0276 §3). What a green run here therefore proves: the graph the loop's
checkout dispatches from bounds its own example, and agrees with the runtime
plugin where one is reachable - never that the revision over there is the one
`origin/main` carries, which is the stale-checkout half of #1840 and stays the
loop's own business.

This is a `scripts/test_*.py`, so `make python-tests` and the `python-tests` job
run it with no wiring to add, next to the guard it drives. It is deliberately not
a `make doc-checks` member: that target is hermetic and runs on every pull
request, and this one reads the loop's checkout and the installed plugin, which
no runner has (ADR-0204's split, the same one the guard's plugin half lives
under). On the runner nothing is named, so it reads its own tree - which is
`origin/main` there, and green - and says so.

Usage:
  python3 scripts/test_squad_graph_dispatch.py
  python3 scripts/test_squad_graph_dispatch.py --dispatch /path/to/checkout
Exit 0 when every source is bounded; 1 with the guard's own output per source.
"""
from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
GUARD = Path("scripts") / "test_squad_graph_spawn_fields.py"
WORKFLOWS = Path("docs") / "squad" / "workflows"
RUNS = Path("runs")
ENV_CHECKOUT = "SG_CHECKOUT"
# The name the dispatched copy is given in the throwaway tree: the guard globs
# `*.graph.json` under `<root>/docs/squad/workflows`, and a run directory keeps
# only `graph.json`, which records no workflow name.
DISPATCHED = "dynamic.graph.json"

FAILURES = []


def fail(msg):
    FAILURES.append(msg)
    print(f"FAIL: {msg}")


def check_source(tree, where):
    """Run the guard against the graph under `tree` and report what it found.

    The guard comes from the tree this check lives in and is given `tree` as its
    root: a checkout that predates the guard can still be examined by it, which is
    the whole point - the loop's checkout was on a revision that never had it.
    """
    proc = subprocess.run(
        [sys.executable, str(REPO_ROOT / GUARD), "--root", str(tree)], capture_output=True, text=True
    )
    print(f"--- {where}")
    for line in (proc.stdout + proc.stderr).strip().splitlines():
        print(f"    {line}")
    if proc.returncode:
        fail(
            f"{where}: the guard rejects the graph the loop dispatches from - an example carrying "
            f"a field the same text does not declare for a request reaches the router as a request "
            f"agent-squad refuses whole (`unknown_field:<k>`), and the run then ends `no_dispatch` "
            f"with nothing spawned (issue #1840). What to fix: that graph, in that checkout - it is "
            f"the file a launch there hands the engine, and `origin/main` is where the field list "
            f"and its example agree"
        )
    return proc.returncode


def newest_dispatch(checkout):
    """The newest dispatch `checkout` recorded: (run id, graph path), or (None, None)."""
    found = sorted((checkout / RUNS).glob("run-*/graph.json"))
    return (found[-1].parent.name, found[-1]) if found else (None, None)


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument(
        "--dispatch",
        help=f"checkout the squad loop launches from (default: ${ENV_CHECKOUT}, else this tree)",
    )
    args = parser.parse_args()

    named = args.dispatch or os.environ.get(ENV_CHECKOUT, "")
    if named:
        checkout = Path(named).expanduser().resolve()
        print(f"loop checkout: {checkout}" + ("" if args.dispatch else f" (${ENV_CHECKOUT})"))
    else:
        checkout = REPO_ROOT
        print(
            f"loop checkout: none named (${ENV_CHECKOUT} is unset), so the tree this check lives in "
            f"is read: {checkout}"
        )

    if not (REPO_ROOT / GUARD).is_file():
        fail(
            f"{REPO_ROOT / GUARD}: the spawn-field guard is missing from the tree this check lives "
            f"in, and this check runs it rather than reimplementing it - without it nothing can say "
            f"whether the graph the loop dispatches from is bounded"
        )
        print(f"\n{len(FAILURES)} failure(s)")
        sys.exit(1)

    print(f"dispatch source: {checkout / WORKFLOWS} (the file a launch there hands the engine)")
    check_source(checkout, f"{WORKFLOWS} in {checkout}")

    run_id, graph = newest_dispatch(checkout)
    if run_id is None:
        print(
            f"no dispatch recorded in {checkout}: no runs/run-*/graph.json, so the revision a run "
            f"there was shown is not on this machine to check, and only the source a launch there "
            f"reads was"
        )
    else:
        recorded = len(list((checkout / RUNS).glob("run-*/graph.json")))
        print(
            f"newest dispatch: {RUNS / run_id / 'graph.json'} in {checkout} (of {recorded} recorded "
            f"there; the newest is the revision a launch there is handing the router now)"
        )
        with tempfile.TemporaryDirectory(prefix="squad-dispatch-") as tmp:
            tree = Path(tmp)
            dest = tree / WORKFLOWS / DISPATCHED
            dest.parent.mkdir(parents=True)
            shutil.copy2(graph, dest)
            check_source(tree, f"{checkout / RUNS / run_id / 'graph.json'} (as dispatched)")

    if FAILURES:
        print(f"\n{len(FAILURES)} failure(s)")
        sys.exit(1)
    print("\nall checks passed")


if __name__ == "__main__":
    main()
