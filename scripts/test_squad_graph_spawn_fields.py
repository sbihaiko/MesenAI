#!/usr/bin/env python3
"""Guard: an example spawn in a squad graph may only use fields the plugin accepts.

Every `docs/squad/workflows/*.graph.json` teaches its router which field names a
spawn request may carry and then shows one example request. Both are hand-written,
so they drift from the plugin the router runs inside. That is what happened in
dynamic.graph.json: its field list was updated for agent-squad 0.2.0 (`stalls`)
while the example kept `"turns": 100`, a 0.1.3-era field. `squad/envelope.py`
rejects a request whole on its first unknown field (`unknown_field:turns`), so the
router that copied the example - the prose tells it to, "Use exactly these field
names" - dispatched nothing and the run's whole spend bought nothing
(run-20261011-160224-2d865b: spawn_rejected `unknown_field:turns`, verdict failed
`no_dispatch`; issue #1800).

The accepted field list is read from the installed plugin at runtime, never copied
into this file: a copy would go stale in step with the graph and would be blind to
exactly the version skew this test exists to catch. When no plugin is installed --
the CI image has none -- the check skips instead of failing.

Usage:
  python3 scripts/test_squad_graph_spawn_fields.py
  python3 scripts/test_squad_graph_spawn_fields.py --root /tmp/copy-of-another-revision

`--root` points the check at another revision's checkout of the graph (for example
`git show origin/main:docs/squad/workflows/dynamic.graph.json` written into a temp
tree), so the pre-fix failure can be shown without touching this worktree.
"""
from __future__ import annotations

import argparse
import importlib
import json
import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
WORKFLOWS = Path("docs") / "squad" / "workflows"

# ```json ... ``` or ``` ... ```: the graphs show their example inside a fence.
FENCE = re.compile(r"```(?:json)?[ \t]*\n(.*?)```", re.DOTALL)

FAILURES = []


def fail(msg):
    FAILURES.append(msg)
    print(f"FAIL: {msg}")


def ok(msg):
    print(f"PASS: {msg}")


def version_key(envelope):
    """Sort key for a cached plugin: 0.2.0 sorts above 0.1.3."""
    return [int(p) for p in re.findall(r"\d+", envelope.parents[1].name)] or [0]


def installed_plugin():
    """The newest cached agent-squad plugin carrying squad/envelope.py, or None."""
    cache = Path.home() / ".claude" / "plugins" / "cache"
    if not cache.is_dir():
        return None
    found = [p for p in cache.glob("*/agent-squad/*/squad/envelope.py") if p.is_file()]
    if not found:
        return None
    envelope = max(found, key=version_key)
    return envelope, envelope.parents[1].name


def request_fields():
    """REQUEST_FIELDS as the installed plugin defines them, or None when absent."""
    found = installed_plugin()
    if found is None:
        return None, None
    envelope, version = found
    sys.path.insert(0, str(envelope.parents[1]))   # the plugin root, which holds `squad/`
    module = importlib.import_module("squad.envelope")
    return set(module.REQUEST_FIELDS), version


def spawn_examples(text):
    """Every example request in `text`: a JSON object in a fence with a `prompt`."""
    out = []
    for match in FENCE.finditer(text):
        try:
            parsed = json.loads(match.group(1))
        except json.JSONDecodeError:
            continue
        out.extend(walk(parsed))
    return out


def walk(value):
    if isinstance(value, dict):
        found = [value] if "prompt" in value else []
        for item in value.values():
            found.extend(walk(item))
        return found
    if isinstance(value, list):
        found = []
        for item in value:
            found.extend(walk(item))
        return found
    return []


def graph_files(root):
    return sorted((root / WORKFLOWS).glob("*.graph.json"))


def check_graph(path, root, fields):
    rel = path.relative_to(root)
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        fail(f"{rel} is not valid JSON: {exc}")
        return
    texts = [(None, data.get("description", ""))]
    for node in data.get("nodes", []):
        for key, value in node.items():
            if isinstance(value, str):
                texts.append((node.get("name", "?"), value))
    before = len(FAILURES)
    for node_name, text in texts:
        for spawn in spawn_examples(text):
            extra = sorted(set(spawn) - fields)
            if extra:
                where = f"node {node_name!r} " if node_name else ""
                fail(
                    f"{rel}: {where}example spawn carries fields agent-squad does not "
                    f"accept: {extra} (spawn keys: {sorted(spawn)})"
                )
    if len(FAILURES) == before:
        ok(f"{rel}: every example spawn names only accepted fields")


def check(root, fields, version):
    paths = graph_files(root)
    if not paths:
        fail(f"no *.graph.json under {root / WORKFLOWS}")
        return
    for path in paths:
        check_graph(path, root, fields)
    ok(f"{len(paths)} graph file(s) checked against agent-squad {version} REQUEST_FIELDS")


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument(
        "--root",
        default=str(REPO_ROOT),
        help="checkout holding docs/squad/workflows (default: this repository)",
    )
    args = parser.parse_args()
    root = Path(args.root).resolve()

    fields, version = request_fields()
    if fields is None:
        print("SKIP: no agent-squad plugin under ~/.claude/plugins/cache; nothing to compare against")
        return
    print(f"agent-squad {version} accepts: {sorted(fields)}")
    check(root, fields, version)

    if FAILURES:
        print(f"\n{len(FAILURES)} failure(s)")
        sys.exit(1)
    print("\nall checks passed")


if __name__ == "__main__":
    main()
