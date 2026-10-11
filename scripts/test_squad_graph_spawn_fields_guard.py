#!/usr/bin/env python3
"""The spawn-field guard must fail, never pass, when the contract cannot be loaded.

`scripts/test_squad_graph_spawn_fields.py` is only worth its runtime if a graph
that reintroduces a field agent-squad rejects turns it red. It did not: the guard
looked for an installed plugin under `~/.claude/plugins/cache` and, finding none
(the CI image has none), printed `SKIP` and exited 0 -- the check reported PASS
on every pull request while asserting nothing, which is the state review found
(PR #1804, finding at scripts/test_squad_graph_spawn_fields.py:159).

These cases pin the fix from the outside: the guard is run as its own process
against a throwaway tree, so what is asserted is its exit code and its output,
never its internals. The contract sources are, in order, `--plugin`, the
installed plugin cache, and the read-only pin committed at
`scripts/vendor/agent_squad/envelope.py`; a case per source that cannot be
loaded, plus the two that prove the pin is what checks a machine with no plugin.

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
    """A throwaway repo holding the guard and the graph, and the pin only if asked.

    A copy rather than a mutation: the guard resolves the graph and the pin
    relative to its own file, so this is the only way to run it against a tree
    where the pin is absent, and the working tree is never touched.
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


def run(root, home, *args):
    """The guard as its own process, with HOME pointed away from any plugin cache."""
    proc = subprocess.run(
        [sys.executable, str(root / GUARD), "--root", str(root), *args],
        capture_output=True,
        text=True,
        env={"PATH": "/usr/bin:/bin", "HOME": str(home)},
    )
    return proc.returncode, proc.stdout + proc.stderr


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


def case_no_contract_anywhere_is_a_hard_failure(tmp):
    """No --plugin, no installed plugin, no pin: the silent pass review found."""
    root, home = tree(tmp, vendored=False)
    code, out = run(root, home)
    check("no contract anywhere exits non-zero", code != 0, f"exit {code}\n{out}")
    check(
        "no contract anywhere says the contract cannot be loaded",
        "cannot be loaded" in out,
        out,
    )
    check("no contract anywhere does not skip", "SKIP" not in out, out)


def case_tampered_pin_is_a_hard_failure(tmp):
    """The pin is read-only: a field quietly added to it must not satisfy the guard."""
    root, home = tree(tmp, vendored=True)
    pin = root / VENDORED
    text = pin.read_text(encoding="utf-8")
    pin.write_text(text.replace('"task"}', '"task", "turns"}'), encoding="utf-8")
    code, out = run(root, home)
    check("tampered pin exits non-zero", code != 0, f"exit {code}\n{out}")
    check("tampered pin names the sha256 drift", "sha256" in out, out)


def case_pin_checks_a_machine_with_no_plugin(tmp):
    """What CI is: no installed plugin, the committed pin, and a real verdict."""
    root, home = tree(tmp, vendored=True)
    code, out = run(root, home)
    check("pin-only run exits 0", code == 0, f"exit {code}\n{out}")
    check("pin-only run names the pin as its contract", "vendor/agent_squad/envelope.py" in out, out)
    check("pin-only run checks the graph", "every example spawn names only accepted fields" in out, out)

    graph = root / GRAPH
    doc = json.loads(graph.read_text(encoding="utf-8"))
    node = next(n for n in doc["nodes"] if "instructions" in n)
    node["instructions"] = node["instructions"].replace(
        '"workspace": "worktrees/child"', '"turns": 100, "workspace": "worktrees/child"'
    )
    graph.write_text(json.dumps(doc), encoding="utf-8")
    code, out = run(root, home)
    check("pin-only run catches a reintroduced field", code != 0, f"exit {code}\n{out}")
    check("pin-only run names the field", "['turns']" in out, out)


def main():
    if not (REPO_ROOT / GUARD).is_file():
        print(f"FAIL: {GUARD} is missing")
        sys.exit(1)
    cases = [
        case_missing_plugin_is_a_hard_failure,
        case_no_contract_anywhere_is_a_hard_failure,
        case_tampered_pin_is_a_hard_failure,
        case_pin_checks_a_machine_with_no_plugin,
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
