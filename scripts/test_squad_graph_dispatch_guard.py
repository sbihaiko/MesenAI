#!/usr/bin/env python3
"""The check must read the graph the loop dispatches from, not the one in the tree it runs in.

`scripts/test_squad_graph_dispatch.py` exists because of issue #1840.
The spawn-field guard (`scripts/test_squad_graph_spawn_fields.py`) reds on the
graph that taught the rejected field - its example carried `turns` and the field
list beside it did not - but nothing ran it against the revision the loop
actually dispatched, so the run died
`no_dispatch, spawn_rejected:unknown_field:turns` twice in one day with its whole
spend bought nothing (`run-20261011-160224-2d865b`,
`run-20261011-215112-ed4e92`).

The revision a run was shown is not out of reach. The launcher reads
`docs/squad/workflows/<workflow>.graph.json` out of the working tree of the
checkout it is started in (`$SG_CHECKOUT`, which every child the loop starts
inherits) and the engine copies that file into the run directory, so
`$SG_CHECKOUT/runs/<run>/graph.json` records it. That checkout is a different
tree from the worktree a child runs in, and reading the child's own tree would
answer a question nobody asked.

These cases pin the check from the outside: it runs as its own process against a
throwaway tree, so what is asserted is its exit code and its output, never its
internals. What each one is about:

- The dispatch source of the loop's checkout is checked, not this tree's: a clean
  tree that names a checkout whose graph carries the teach is red.
- The loop's checkout is taken from `--dispatch`, else `$SG_CHECKOUT`.
- The newest dispatch of that checkout is checked even when its tree no longer
  carries that revision - the half nothing ran before, and the reason the check
  exists at all.
- Only the newest: an older, immutable dispatch that taught the rejected field
  must not hold the check red forever, or it could never pass again (451 of the
  604 dispatches the loop's checkout had recorded on 2026-10-11 predate the fix).
- A checkout with no dispatch recorded says so instead of passing in silence.
- The check runs the guard itself, contract and all: the same graph is red here
  when a runtime plugin rejects a field its own text declares, which no rule
  reimplemented in this check could know.

Usage: python3 scripts/test_squad_graph_dispatch_guard.py
Exit 0 when every case behaves; exit 1 with one line per case that does not.
"""
from __future__ import annotations

import json
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
CHECK = Path("scripts") / "test_squad_graph_dispatch.py"
GUARD = Path("scripts") / "test_squad_graph_spawn_fields.py"
VENDORED = Path("scripts") / "vendor" / "agent_squad" / "envelope.py"
GRAPH = Path("docs") / "squad" / "workflows" / "dynamic.graph.json"

# The fields the committed copy records, so a fixture plugin built from them
# agrees with the copy and a case fails for the one reason it is about.
COPY_FIELDS = {
    "prompt", "skill", "model", "effort", "tools", "stalls", "spend",
    "deadline", "workspace", "inputs", "after", "task",
}

# The teach `run-20261011-215112-ed4e92` was shown, in the shape the guard reads:
# a field the same text does not declare for a request.
TAUGHT = '"turns": 100, "workspace": "worktrees/child"'

FAILURES = []
CASES = 0


def check(name, condition, detail=""):
    global CASES
    CASES += 1
    if condition:
        print(f"ok {name}")
    else:
        FAILURES.append(f"{name}: {detail}")
        print(f"FAIL {name}: {detail}")


def checkout(tmp, name="loop-checkout", *, teach=False):
    """A checkout of this repository holding the check, the guard and the graph.

    Two of them per case where it matters: the one a child runs in (`self`) and
    the one the loop launches from (`other`) - the check must read the second.
    """
    root = Path(tmp) / name
    for rel in (CHECK, GUARD, GRAPH, VENDORED):
        dest = root / rel
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(REPO_ROOT / rel, dest)
    if teach:
        path = root / GRAPH
        doc = json.loads(path.read_text(encoding="utf-8"))
        for node in doc["nodes"]:
            if isinstance(node.get("instructions"), str):
                node["instructions"] = node["instructions"].replace(
                    '"workspace": "worktrees/child"', TAUGHT
                )
        path.write_text(json.dumps(doc), encoding="utf-8")
    return root


def dispatch(root, run, *, taught=False):
    """Record a dispatch this checkout made: the graph the engine copied into runs/<run>/."""
    path = root / "runs" / run / "graph.json"
    path.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(root / GRAPH, path)
    if taught:
        doc = json.loads(path.read_text(encoding="utf-8"))
        for node in doc["nodes"]:
            if isinstance(node.get("instructions"), str):
                node["instructions"] = node["instructions"].replace(
                    '"workspace": "worktrees/child"', TAUGHT
                )
        path.write_text(json.dumps(doc), encoding="utf-8")
    return run


def plugin(tmp, fields, name="plugin"):
    """A runtime plugin holding squad/envelope.py, the shape the guard imports."""
    root = Path(tmp) / name
    (root / "squad").mkdir(parents=True)
    (root / "squad" / "envelope.py").write_text(
        f"REQUEST_FIELDS = set({sorted(fields)!r})\n", encoding="utf-8"
    )
    return root


def run(self_tree, *args, env=None):
    """The check as its own process, from a child's tree, with an empty HOME.

    `$SG_CHECKOUT` is cleared unless a case sets it: a case about the checkout the
    loop dispatches from must name it itself, not inherit it from the machine.
    """
    home = Path(tempfile.mkdtemp(prefix="spawn-dispatch-home-"))
    proc = subprocess.run(
        [sys.executable, str(self_tree / CHECK), *args],
        capture_output=True,
        text=True,
        cwd=str(self_tree),
        env={"PATH": "/usr/bin:/bin", "HOME": str(home), "SG_CHECKOUT": "", **(env or {})},
    )
    return proc.returncode, proc.stdout + proc.stderr


def case_the_loop_checkouts_source_is_checked_not_the_childs_own(tmp):
    """A child's tree is clean; the checkout the loop dispatches from is not."""
    self_tree = checkout(tmp, "child-worktree")
    loop = checkout(tmp, "loop-checkout", teach=True)
    code, out = run(self_tree, "--dispatch", str(loop))
    check("a loop checkout whose source teaches a field exits non-zero", code != 0, f"exit {code}\n{out}")
    check("a loop checkout whose source teaches a field is named", "loop-checkout" in out, out)
    check("a loop checkout whose source teaches a field names the field", "['turns']" in out, out)


def case_the_loop_checkout_is_taken_from_the_environment(tmp):
    """$SG_CHECKOUT is the loop's own name for it, and every child inherits it."""
    self_tree = checkout(tmp, "child-worktree")
    loop = checkout(tmp, "loop-checkout", teach=True)
    code, out = run(self_tree, env={"SG_CHECKOUT": str(loop)})
    check("$SG_CHECKOUT naming a teaching checkout exits non-zero", code != 0, f"exit {code}\n{out}")
    check("$SG_CHECKOUT naming a teaching checkout is said out loud", "SG_CHECKOUT" in out, out)


def case_a_clean_loop_checkout_passes_and_says_which_tree_it_read(tmp):
    """The other half: no teach there, and the run names the tree it read."""
    self_tree = checkout(tmp, "child-worktree")
    loop = checkout(tmp, "loop-checkout")
    code, out = run(self_tree, "--dispatch", str(loop))
    check("a clean loop checkout exits 0", code == 0, f"exit {code}\n{out}")
    check("a clean loop checkout names the tree read", "loop-checkout" in out, out)
    check("a clean loop checkout says its source is bounded", "every example spawn names only accepted fields" in out, out)


def case_the_newest_dispatch_is_checked_when_the_tree_no_longer_carries_it(tmp):
    """The half nothing ran before: a clean checkout that dispatched a teaching graph.

    Its graph has the fix; the dispatch it recorded does not. The suite was green
    through exactly this state before this check existed, which is how the loop's
    checkout kept teaching `turns` to run after run.
    """
    self_tree = checkout(tmp, "child-worktree")
    loop = checkout(tmp, "loop-checkout")
    run_id = dispatch(loop, "run-20261011-215112-ed4e92", taught=True)
    code, out = run(self_tree, "--dispatch", str(loop))
    check("a dispatch teaching an undeclared field exits non-zero", code != 0, f"exit {code}\n{out}")
    check("a dispatch teaching an undeclared field names the run", run_id in out, out)
    check("a dispatch teaching an undeclared field names the field", "['turns']" in out, out)


def case_only_the_newest_dispatch_is_checked(tmp):
    """History must not hold the check red: an old teach is not a claim about the source now."""
    self_tree = checkout(tmp, "child-worktree")
    loop = checkout(tmp, "loop-checkout")
    dispatch(loop, "run-20261011-160224-2d865b", taught=True)
    newest = dispatch(loop, "run-20261011-215730-306c6f")
    code, out = run(self_tree, "--dispatch", str(loop))
    check("an older dispatch that taught a rejected field exits 0", code == 0, f"exit {code}\n{out}")
    check("an older dispatch that taught a rejected field is not the one read", newest in out, out)


def case_no_dispatch_recorded_is_said_out_loud(tmp):
    """A checkout that never dispatched (the runner, a fresh clone) says so."""
    self_tree = checkout(tmp, "child-worktree")
    loop = checkout(tmp, "loop-checkout")
    (loop / "runs" / "f918-accept").mkdir(parents=True)
    code, out = run(self_tree, "--dispatch", str(loop))
    check("a checkout with no dispatch exits 0 when its source is bounded", code == 0, f"exit {code}\n{out}")
    check("a checkout with no dispatch says none was recorded", "no dispatch recorded" in out, out)


def case_no_checkout_named_reads_the_tree_the_check_lives_in(tmp):
    """The runner's shape: nothing named, so the only graph to hand is this tree's."""
    self_tree = checkout(tmp, "child-worktree")
    code, out = run(self_tree)
    check("no checkout named exits 0 on a bounded tree", code == 0, f"exit {code}\n{out}")
    check("no checkout named says the tree it read is this one", "child-worktree" in out, out)

    taught = checkout(tmp, "second-worktree", teach=True)
    code, out = run(taught)
    check("no checkout named exits non-zero on a teaching tree", code != 0, f"exit {code}\n{out}")


def case_the_check_runs_the_guard_and_not_a_rule_of_its_own(tmp):
    """A runtime plugin that rejects a declared field reds, which only the guard knows."""
    self_tree = checkout(tmp, "child-worktree")
    loop = checkout(tmp, "loop-checkout")
    path = loop / GRAPH
    doc = json.loads(path.read_text(encoding="utf-8"))
    for node in doc["nodes"]:
        if isinstance(node.get("instructions"), str):
            node["instructions"] = node["instructions"].replace("stalls", "stall_count")
    path.write_text(json.dumps(doc), encoding="utf-8")
    code, out = run(
        self_tree, "--dispatch", str(loop), env={"AGENT_SQUAD_PLUGIN": str(plugin(tmp, COPY_FIELDS))}
    )
    check("a field a runtime plugin rejects exits non-zero", code != 0, f"exit {code}\n{out}")
    check("a field a runtime plugin rejects is named by the guard's answer", "stall_count" in out, out)


def case_a_loop_checkout_with_no_graph_fails(tmp):
    """Nothing to bound is a failure, never a pass."""
    self_tree = checkout(tmp, "child-worktree")
    loop = checkout(tmp, "loop-checkout")
    (loop / GRAPH).unlink()
    code, out = run(self_tree, "--dispatch", str(loop))
    check("a loop checkout with no graph exits non-zero", code != 0, f"exit {code}\n{out}")


def main():
    for rel in (GUARD, CHECK):
        if not (REPO_ROOT / rel).is_file():
            print(
                f"FAIL: {rel} is missing. This check reads the graph the loop's checkout "
                f"(`$SG_CHECKOUT`) dispatches from - the source a launch there hands the engine and "
                f"the copy that launch handed the router - and runs the guard against both; with it "
                f"absent nothing in this repository reads either (issue #1840)."
            )
            sys.exit(1)
    cases = [
        case_the_loop_checkouts_source_is_checked_not_the_childs_own,
        case_the_loop_checkout_is_taken_from_the_environment,
        case_a_clean_loop_checkout_passes_and_says_which_tree_it_read,
        case_the_newest_dispatch_is_checked_when_the_tree_no_longer_carries_it,
        case_only_the_newest_dispatch_is_checked,
        case_no_dispatch_recorded_is_said_out_loud,
        case_no_checkout_named_reads_the_tree_the_check_lives_in,
        case_the_check_runs_the_guard_and_not_a_rule_of_its_own,
        case_a_loop_checkout_with_no_graph_fails,
    ]
    for case in cases:
        with tempfile.TemporaryDirectory(prefix="spawn-dispatch-") as tmp:
            case(tmp)
    print(f"\n{CASES - len(FAILURES)}/{CASES} passed")
    if FAILURES:
        print(f"{len(FAILURES)} failure(s)")
        sys.exit(1)


if __name__ == "__main__":
    main()
