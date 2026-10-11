#!/usr/bin/env python3
"""The spawn-field guard must fail, never pass, when the contract cannot be loaded.

`scripts/test_squad_graph_spawn_fields.py` is only worth its runtime if a graph
that reintroduces a field agent-squad rejects turns it red. It reported PASS
twice where it should have refused: first a `SKIP` when no plugin was installed
(the CI image has none), then a fallback to the committed read-only copy at
`scripts/vendor/agent_squad/envelope.py`, which made that copy the contract --
so a field rename on the plugin side left CI green while the router's requests
were rejected whole (`unknown_field:*`; PR #1804, findings at
scripts/test_squad_graph_spawn_fields.py:159, :173 and :189).

These cases pin the fixes from the outside: the guard is run as its own process
against a throwaway tree, so what is asserted is its exit code and its output,
never its internals. The contract is a runtime plugin, named by `--plugin` or
`AGENT_SQUAD_PLUGIN`, or the newest plugin installed under the plugin cache;
the committed copy is read only when one of those names it, and a runtime plugin
that disagrees with the copy fails. A fence that should hold an example request
but does not parse is a failure, not an example skipped over.

Usage: python3 scripts/test_squad_graph_spawn_fields_guard.py
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
GUARD = Path("scripts") / "test_squad_graph_spawn_fields.py"
VENDORED = Path("scripts") / "vendor" / "agent_squad" / "envelope.py"
GRAPH = Path("docs") / "squad" / "workflows" / "dynamic.graph.json"

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


def case_no_plugin_anywhere_is_a_hard_failure(tmp):
    """No --plugin, no $AGENT_SQUAD_PLUGIN, no installed plugin: refuse, never skip."""
    root, home = tree(tmp, vendored=True)
    code, out = run(root, home)
    check("no plugin anywhere exits non-zero", code != 0, f"exit {code}\n{out}")
    check("no plugin anywhere does not skip", "SKIP" not in out, out)


def case_the_committed_copy_is_not_reached_for_on_its_own(tmp):
    """The copy is in the tree and the guard still refuses it: it is a recording."""
    root, home = tree(tmp, vendored=True)
    code, out = run(root, home)
    check("a copy alone is not a contract", code != 0, f"exit {code}\n{out}")
    check(
        "a copy alone says the contract cannot be loaded",
        "cannot be loaded" in out,
        out,
    )


def case_named_copy_checks_the_graph(tmp):
    """What CI does: no plugin installed, the copy named, and a real verdict."""
    root, home = tree(tmp, vendored=True)
    named = str(root / VENDORED)
    code, out = run(root, home, env={"AGENT_SQUAD_PLUGIN": named})
    check("named-copy run exits 0", code == 0, f"exit {code}\n{out}")
    check("named-copy run checks the graph", "every example spawn names only accepted fields" in out, out)

    doc, node = example_fence(root)
    node["instructions"] = node["instructions"].replace(
        '"workspace": "worktrees/child"', '"turns": 100, "workspace": "worktrees/child"'
    )
    (root / GRAPH).write_text(json.dumps(doc), encoding="utf-8")
    code, out = run(root, home, env={"AGENT_SQUAD_PLUGIN": named})
    check("named-copy run catches a reintroduced field", code != 0, f"exit {code}\n{out}")
    check("named-copy run names the field", "['turns']" in out, out)


def case_tampered_copy_is_a_hard_failure(tmp):
    """The copy is read-only: a field quietly added to it must not satisfy the guard."""
    root, home = tree(tmp, vendored=True)
    copy = root / VENDORED
    copy.write_text(
        copy.read_text(encoding="utf-8").replace('"task"}', '"task", "turns"}'), encoding="utf-8"
    )
    code, out = run(root, home, env={"AGENT_SQUAD_PLUGIN": str(copy)})
    check("tampered copy exits non-zero", code != 0, f"exit {code}\n{out}")
    check("tampered copy names the sha256 drift", "sha256" in out, out)


def case_runtime_plugin_that_disagrees_with_the_copy_fails(tmp):
    """A plugin whose fields moved: the copy is re-vendored, never left to disagree."""
    root, home = tree(tmp, vendored=True)
    plugin = Path(tmp) / "plugin" / "squad"
    plugin.mkdir(parents=True)
    (plugin / "envelope.py").write_text('REQUEST_FIELDS = {"prompt", "turns"}\n', encoding="utf-8")
    code, out = run(root, home, "--plugin", str(plugin.parent))
    check("a plugin the copy disagrees with exits non-zero", code != 0, f"exit {code}\n{out}")
    check(
        "the disagreement names the copy and the plugin",
        "records agent-squad" in out,
        out,
    )


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
    code, out = run(root, home, env={"AGENT_SQUAD_PLUGIN": str(root / VENDORED)})
    check("a malformed example fence exits non-zero", code != 0, f"exit {code}\n{out}")
    check("a malformed example fence does not report PASS", "every example spawn" not in out, out)


def main():
    if not (REPO_ROOT / GUARD).is_file():
        print(f"FAIL: {GUARD} is missing")
        sys.exit(1)
    cases = [
        case_missing_plugin_is_a_hard_failure,
        case_no_plugin_anywhere_is_a_hard_failure,
        case_the_committed_copy_is_not_reached_for_on_its_own,
        case_named_copy_checks_the_graph,
        case_tampered_copy_is_a_hard_failure,
        case_runtime_plugin_that_disagrees_with_the_copy_fails,
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
