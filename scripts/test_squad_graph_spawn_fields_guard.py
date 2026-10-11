#!/usr/bin/env python3
"""The spawn-field guard must fail, never pass, when it cannot bound an example.

`scripts/test_squad_graph_spawn_fields.py` is only worth its runtime if a graph
that reintroduces a field agent-squad rejects turns it red. It reported PASS
twice where it should have refused: first a `SKIP` when no plugin was installed
(the CI image has none), then - the finding the review of `b04dae52e` kept -
CI still handing it the committed read-only copy through `AGENT_SQUAD_PLUGIN`,
which made a copy of the artifact under test the contract, so a field rename on
the plugin side left the job green while the router's requests were rejected
whole (`unknown_field:*`; PR #1804, findings at
scripts/test_squad_graph_spawn_fields.py:159, :173 and :189).

These cases pin the fixes from the outside: the guard is run as its own process
against a throwaway tree, so what is asserted is its exit code and its output,
never its internals.

The two halves ADR-0276 splits the check into, one case each, because each half
has a contract the other side cannot have:

- The contract is a runtime plugin: `--plugin`, `AGENT_SQUAD_PLUGIN`, or the
  newest plugin installed under the plugin cache.
- A committed copy is **never** a contract, named or not: it is a recording of
  a runtime plugin, and a recording cannot disagree with itself. It is still
  read - against a plugin that answered and against its own sha256 header - so
  it cannot age in silence on a machine that has both.
- With no plugin reachable at all, the guard still refuses to be vacuous: each
  graph's example is checked against the field list that graph declares for
  itself, a graph declaring none fails, and the run says in the open that the
  plugin half is not what it checked. The copy bounds nothing on that side, and
  a field the copy lacks passes there while failing against a plugin - the two
  cases that pin the split, one per half.
- No workflow hands the guard a contract from outside this repository: the
  `AGENT_SQUAD_PLUGIN` line CI used to carry is the #1804 finding, and a
  workflow that names one again fails here.

A fence that should hold an example request but does not parse is a failure, not
an example skipped over.

Usage: python3 scripts/test_squad_graph_spawn_fields_guard.py
Exit 0 when every case behaves; exit 1 with one line per case that does not.
"""
from __future__ import annotations

import json
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
GUARD = Path("scripts") / "test_squad_graph_spawn_fields.py"
VENDORED = Path("scripts") / "vendor" / "agent_squad" / "envelope.py"
GRAPH = Path("docs") / "squad" / "workflows" / "dynamic.graph.json"

# The copy's own field list: a fixture plugin carrying it lets a case fail for
# the one reason it is about instead of for a plugin/copy disagreement.
COPY_FIELDS = {
    "prompt", "skill", "model", "effort", "tools", "stalls", "spend",
    "deadline", "workspace", "inputs", "after", "task",
}

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


def tree(tmp, *, vendored):
    """A throwaway repo holding the guard and the graph, and the copy only if asked.

    A copy rather than a mutation: the guard resolves the graph and the copy
    relative to its own file, so this is the only way to run it against a tree
    where the copy is absent, and the working tree is never touched.
    """
    root = Path(tmp) / "repo"
    for rel in (GUARD, GRAPH):
        dest = root / rel
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(REPO_ROOT / rel, dest)
    if vendored:
        dest = root / VENDORED
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(REPO_ROOT / VENDORED, dest)
    home = Path(tmp) / "home"          # an empty HOME: no plugin cache, and none found
    home.mkdir()
    return root, home


def plugin(tmp, fields, name="plugin"):
    """A plugin root holding squad/envelope.py, the shape the guard imports."""
    root = Path(tmp) / name
    (root / "squad").mkdir(parents=True)
    (root / "squad" / "envelope.py").write_text(
        f"REQUEST_FIELDS = set({sorted(fields)!r})\n", encoding="utf-8"
    )
    return root


def run(root, home, *args, env=None):
    """The guard as its own process, with HOME pointed away from any plugin cache."""
    proc = subprocess.run(
        [sys.executable, str(root / GUARD), "--root", str(root), *args],
        capture_output=True,
        text=True,
        env={"PATH": "/usr/bin:/bin", "HOME": str(home), **(env or {})},
    )
    return proc.returncode, proc.stdout + proc.stderr


def example_fence(root):
    """(node, instructions) of the node carrying the example request."""
    doc = json.loads((root / GRAPH).read_text(encoding="utf-8"))
    node = next(n for n in doc["nodes"] if "instructions" in n)
    return doc, node


def case_missing_plugin_is_a_hard_failure(tmp):
    """`--plugin` naming nothing: the guard must refuse to run, not skip."""
    root, home = tree(tmp, vendored=True)
    code, out = run(root, home, "--plugin", str(Path(tmp) / "no-such-plugin"))
    check("missing --plugin exits non-zero", code != 0, f"exit {code}\n{out}")
    check(
        "missing --plugin says the contract cannot be loaded",
        "cannot be loaded" in out,
        out,
    )


def case_the_committed_copy_is_never_a_contract(tmp):
    """What CI used to do: hand the guard the copy of the artifact it tests."""
    root, home = tree(tmp, vendored=True)
    copy = str(root / VENDORED)
    for args, env in ((("--plugin", copy), None), ((), {"AGENT_SQUAD_PLUGIN": copy})):
        code, out = run(root, home, *args, env=env)
        named = args[0] if args else "AGENT_SQUAD_PLUGIN"
        check(f"a copy named by {named} is not a contract", code != 0, f"exit {code}\n{out}")
        check(f"a copy named by {named} is refused as a recording", "recording" in out, out)
        check(f"a copy named by {named} checks no graph", "every example spawn" not in out, out)


def case_no_plugin_checks_each_graph_against_its_own_declaration(tmp):
    """No plugin anywhere: the guard checks what it can and says what it did not."""
    root, home = tree(tmp, vendored=True)
    code, out = run(root, home)
    check("no plugin exits 0 when every example matches its graph", code == 0, f"exit {code}\n{out}")
    check(
        "no plugin checks each graph against its own declared fields",
        "every example spawn names only accepted fields" in out,
        out,
    )
    check(
        "no plugin says the plugin half is not what it checked",
        "NOT CHECKED" in out and "REQUEST_FIELDS" in out,
        out,
    )


def case_an_example_outside_its_own_declaration_fails_without_a_plugin(tmp):
    """The #1800 shape: the graph's list was updated and its example was not."""
    root, home = tree(tmp, vendored=True)
    doc, node = example_fence(root)
    node["instructions"] = node["instructions"].replace(
        '"workspace": "worktrees/child"', '"turns": 100, "workspace": "worktrees/child"'
    )
    (root / GRAPH).write_text(json.dumps(doc), encoding="utf-8")
    code, out = run(root, home)
    check("an example its own graph does not declare exits non-zero", code != 0, f"exit {code}\n{out}")
    check("an example its own graph does not declare names the field", "['turns']" in out, out)


def case_a_graph_declaring_no_field_list_fails(tmp):
    """Nothing bounds the example: that is the skip this guard must never take."""
    root, home = tree(tmp, vendored=True)
    doc, node = example_fence(root)
    node["instructions"] = node["instructions"].replace(
        "each `{prompt, skill, model, effort, tools, stalls, spend, deadline, workspace}`", "each request"
    )
    (root / GRAPH).write_text(json.dumps(doc), encoding="utf-8")
    code, out = run(root, home)
    check("a graph declaring no field list exits non-zero", code != 0, f"exit {code}\n{out}")
    check(
        "a graph declaring no field list says so",
        "declares no" in out,
        out,
    )


def case_tampered_copy_is_a_hard_failure(tmp):
    """The copy is read-only: a field quietly added to it must not satisfy the guard."""
    root, home = tree(tmp, vendored=True)
    copy = root / VENDORED
    copy.write_text(
        copy.read_text(encoding="utf-8").replace('"task"}', '"task", "turns"}'), encoding="utf-8"
    )
    code, out = run(root, home, "--plugin", str(plugin(tmp, COPY_FIELDS)))
    check("tampered copy exits non-zero", code != 0, f"exit {code}\n{out}")
    check("tampered copy names the sha256 drift", "sha256" in out, out)


def case_runtime_plugin_that_disagrees_with_the_copy_fails(tmp):
    """A plugin whose fields moved: the copy is re-vendored, never left to disagree."""
    root, home = tree(tmp, vendored=True)
    code, out = run(root, home, "--plugin", str(plugin(tmp, {"prompt", "turns"})))
    check("a plugin the copy disagrees with exits non-zero", code != 0, f"exit {code}\n{out}")
    check(
        "the disagreement names the copy and the plugin",
        "records agent-squad" in out,
        out,
    )


def case_no_workflow_hands_the_guard_a_contract(tmp):
    """The #1804 finding itself: no workflow may supply a contract from outside.

    `AGENT_SQUAD_PLUGIN` in the `python-tests` job's env made the committed copy
    - an artifact under test - the contract, so a plugin-side rename could not
    red CI. CI's contract is the field list each graph declares for itself now
    (ADR-0276), which needs nothing from outside this repository. Comment lines
    are skipped so a job comment may still discuss the variable by name.
    """
    workflows = sorted((REPO_ROOT / ".github" / "workflows").glob("*.yml"))
    check(
        "there are workflows to scan",
        bool(workflows),
        "no *.yml under .github/workflows: nothing here can say CI is given no contract",
    )
    named = []
    for path in workflows:
        for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            if line.lstrip().startswith("#"):
                continue
            if re.search(r"AGENT_SQUAD_PLUGIN\s*[:=]", line):
                named.append(f"{path.name}:{number}")
    check("no workflow supplies the guard a contract", not named, ", ".join(named))


def case_the_recording_bounds_nothing_where_no_plugin_answered(tmp):
    """CI's half of the split: no plugin, so the graph's own list is the contract.

    The mutation the plugin case below shares - `stalls` renamed to a field the
    committed copy does not carry - run where no plugin is reachable, which is
    CI's shape. It passes there: what CI checks is every graph against the list
    it declares for itself, and the copy bounds nothing (ADR-0276). Named as a
    plugin, the same graph is red, which is the other half of the pair.
    """
    root, home = tree(tmp, vendored=True)
    doc, node = example_fence(root)
    node["instructions"] = node["instructions"].replace("stalls", "stall_count")
    (root / GRAPH).write_text(json.dumps(doc), encoding="utf-8")
    code, out = run(root, home)
    check("no plugin, a field the copy lacks exits 0", code == 0, f"exit {code}\n{out}")
    check(
        "no plugin, the recording is not the contract",
        "committed copy of agent-squad" not in out,
        out,
    )
    code, out = run(root, home, "--plugin", str(plugin(tmp, COPY_FIELDS)))
    check("the same graph against a plugin exits non-zero", code != 0, f"exit {code}\n{out}")


def case_a_declaration_the_plugin_rejects_fails(tmp):
    """A graph declaring a field the plugin rejects is red even where examples agree."""
    root, home = tree(tmp, vendored=True)
    doc, node = example_fence(root)
    node["instructions"] = node["instructions"].replace(
        "spend, deadline, workspace}`", "spend, deadline, workspace, turns}`"
    ).replace('"workspace": "worktrees/child"', '"turns": 100, "workspace": "worktrees/child"')
    (root / GRAPH).write_text(json.dumps(doc), encoding="utf-8")
    code, out = run(root, home, "--plugin", str(plugin(tmp, COPY_FIELDS)))
    check("a declaration the plugin rejects exits non-zero", code != 0, f"exit {code}\n{out}")
    check("a declaration the plugin rejects names the field", "['turns']" in out, out)


def case_malformed_example_fence_is_a_hard_failure(tmp):
    """An example that cannot be read is unchecked; that has to be a failure."""
    root, home = tree(tmp, vendored=True)
    doc, node = example_fence(root)
    # The fence keeps a field agent-squad rejects, and its JSON no longer parses:
    # the trailing comma is the whole defect a reader's eye skips over.
    node["instructions"] = node["instructions"].replace(
        '"spend": 6.5, "workspace"', '"turns": 100, "spend": 6.5, "workspace"'
    ).replace('"worktrees/child"}\n```', '"worktrees/child",}\n```')
    (root / GRAPH).write_text(json.dumps(doc), encoding="utf-8")
    code, out = run(root, home)
    check("a malformed example fence exits non-zero", code != 0, f"exit {code}\n{out}")
    check("a malformed example fence does not report PASS", "every example spawn" not in out, out)


def main():
    if not (REPO_ROOT / GUARD).is_file():
        print(f"FAIL: {GUARD} is missing")
        sys.exit(1)
    cases = [
        case_missing_plugin_is_a_hard_failure,
        case_the_committed_copy_is_never_a_contract,
        case_no_plugin_checks_each_graph_against_its_own_declaration,
        case_an_example_outside_its_own_declaration_fails_without_a_plugin,
        case_a_graph_declaring_no_field_list_fails,
        case_tampered_copy_is_a_hard_failure,
        case_runtime_plugin_that_disagrees_with_the_copy_fails,
        case_a_declaration_the_plugin_rejects_fails,
        case_no_workflow_hands_the_guard_a_contract,
        case_the_recording_bounds_nothing_where_no_plugin_answered,
        case_malformed_example_fence_is_a_hard_failure,
    ]
    for case in cases:
        with tempfile.TemporaryDirectory(prefix="spawn-guard-") as tmp:
            case(tmp)
    print(f"\n{CASES - len(FAILURES)}/{CASES} passed")
    if FAILURES:
        print(f"{len(FAILURES)} failure(s)")
        sys.exit(1)


if __name__ == "__main__":
    main()
