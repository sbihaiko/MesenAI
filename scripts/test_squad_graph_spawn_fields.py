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

The accepted field list is read from the plugin at runtime, never copied by hand
into this file: a hand copy would go stale in step with the graph and be blind to
exactly the version skew this test exists to catch. The sources are the plugin
installed under `~/.claude/plugins/cache`, `--plugin` for any other checkout, and
the read-only pin at `scripts/vendor/agent_squad/envelope.py` -- the fork's
`squad/envelope.py` verbatim, header sha256 verified before it is imported. The
pin is what lets the CI image, which has no plugin, check the graph instead of
skipping it.

When no contract can be loaded the guard FAILS. It never skips: a skip is a PASS
that asserts nothing, and skipping is how this check let `turns` sit in the graph
unobserved until a run died on it (review of PR #1804, issue #1800).

Usage:
  python3 scripts/test_squad_graph_spawn_fields.py
  python3 scripts/test_squad_graph_spawn_fields.py --root /tmp/copy-of-another-revision
  python3 scripts/test_squad_graph_spawn_fields.py --plugin ~/VSCodeProjects/agent-squad

`--root` points the check at another revision's checkout of the graph (for example
`git show origin/main:docs/squad/workflows/dynamic.graph.json` written into a temp
tree), so the pre-fix failure can be shown without touching this worktree.
`--plugin` reads the contract from a given plugin root (or its `squad/envelope.py`)
rather than from the cache or the pin; a path that holds no loadable contract is a
failure, not a fallback.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib
import importlib.util
import json
import re
import sys
import types
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
WORKFLOWS = Path("docs") / "squad" / "workflows"
PINNED = Path("scripts") / "vendor" / "agent_squad" / "envelope.py"
END_MARK = "# ---- end vendored header ----\n"

# ```json ... ``` or ``` ... ```: the graphs show their example inside a fence.
FENCE = re.compile(r"```(?:json)?[ \t]*\n(.*?)```", re.DOTALL)

FAILURES = []


def fail(msg):
    FAILURES.append(msg)
    print(f"FAIL: {msg}")


def ok(msg):
    print(f"PASS: {msg}")


class ContractError(Exception):
    """The spawn-field contract cannot be read. The guard fails; it never skips."""


def version_of(envelope):
    """The version an envelope path names: the cache directory, or `local`."""
    name = envelope.parents[1].name if envelope.parent.name == "squad" else envelope.parent.name
    return name if re.fullmatch(r"\d+(\.\d+)*", name) else "local"


def version_key(envelope):
    """Sort key for a cached plugin: 0.2.0 sorts above 0.1.3."""
    return [int(p) for p in re.findall(r"\d+", envelope.parents[1].name)] or [0]


def cached_plugins():
    """Every cached agent-squad plugin carrying squad/envelope.py, newest first."""
    cache = Path.home() / ".claude" / "plugins" / "cache"
    if not cache.is_dir():
        return []
    found = [p for p in cache.glob("*/agent-squad/*/squad/envelope.py") if p.is_file()]
    return [(p, version_of(p)) for p in sorted(found, key=version_key, reverse=True)]


def envelope_in(root):
    """The plugin's envelope.py under `root`, or None when that root holds none."""
    root = Path(root).expanduser()
    if root.is_file():
        return root
    found = [root / rel for rel in ("squad/envelope.py", "envelope.py") if (root / rel).is_file()]
    return found[0] if found else None


def fields_from_plugin(envelope):
    """REQUEST_FIELDS as an installed plugin defines them."""
    root = envelope.parents[1] if envelope.parent.name == "squad" else envelope.parent
    sys.path.insert(0, str(root))        # the plugin root, which holds `squad/`
    try:
        module = importlib.import_module("squad.envelope")
    except Exception as exc:
        raise ContractError(f"{envelope}: cannot be imported ({exc.__class__.__name__}: {exc})") from exc
    finally:
        sys.path.remove(str(root))
    try:
        return set(module.REQUEST_FIELDS)
    except AttributeError as exc:
        raise ContractError(f"{envelope}: defines no REQUEST_FIELDS ({exc})") from exc


def fields_from_pin(path):
    """(REQUEST_FIELDS, version) from the read-only pin, header sha256 checked first.

    The pin's body is the fork's file verbatim, so the `from squad import skills`
    it carries is satisfied by stubs: REQUEST_FIELDS is a module-level constant
    and the guard calls nothing in the module. The sha256 check is what makes the
    pin trustworthy: a pin edited to agree with a graph fails here instead.
    """
    try:
        text = path.read_text(encoding="utf-8")
    except OSError as exc:
        raise ContractError(f"{path}: unreadable ({exc})") from exc
    if END_MARK not in text:
        raise ContractError(f"{path}: the vendored header end marker is missing")
    header, body = text.split(END_MARK, 1)
    pinned = dict(l[2:].split(": ", 1) for l in header.splitlines() if l.startswith("# ") and ": " in l)
    digest = hashlib.sha256(body.encode("utf-8")).hexdigest()
    if pinned.get("sha256") != digest:
        raise ContractError(f"{path}: sha256 {digest} differs from header {pinned.get('sha256')}")
    package = types.ModuleType("squad")
    package.__path__ = []
    skills = types.ModuleType("squad.skills")
    skills.VENDOR = None
    package.skills = skills
    sys.modules["squad"], sys.modules["squad.skills"] = package, skills
    module = importlib.util.module_from_spec(importlib.util.spec_from_file_location("squad.envelope", path))
    try:
        module.__spec__.loader.exec_module(module)
        return set(module.REQUEST_FIELDS), pinned.get("source-version", "pinned")
    except AttributeError as exc:
        raise ContractError(f"{path}: defines no REQUEST_FIELDS ({exc})") from exc
    except (ImportError, SyntaxError) as exc:
        raise ContractError(f"{path}: cannot be imported ({exc.__class__.__name__}: {exc})") from exc


def load_contract(plugin):
    """(REQUEST_FIELDS, version, source) from --plugin, the cache, or the pin.

    `--plugin` wins, then the newest plugin installed under the cache, then the
    committed pin. A source that is present but unreadable is an error and not a
    reason to try the next one: falling through would hide the broken plugin the
    guard exists to report, and reporting it is the whole point.
    """
    if plugin:
        envelope = envelope_in(plugin)
        if envelope is None:
            raise ContractError(f"--plugin {plugin}: no squad/envelope.py there")
        return fields_from_plugin(envelope), version_of(envelope), f"{envelope} (--plugin)"
    cached = cached_plugins()
    if cached:
        envelope, version = cached[0]
        return fields_from_plugin(envelope), version, f"{envelope} (installed agent-squad {version})"
    pin = REPO_ROOT / PINNED
    if pin.is_file():
        fields, version = fields_from_pin(pin)
        return fields, version, f"{PINNED} (read-only pin of agent-squad {version})"
    raise ContractError(
        f"no --plugin, no plugin under {Path.home() / '.claude' / 'plugins' / 'cache'}, "
        f"and no committed pin at {PINNED}"
    )


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
    parser.add_argument(
        "--plugin",
        help="agent-squad plugin root (or its squad/envelope.py) to read the contract "
        "from, instead of the installed cache or the committed pin",
    )
    args = parser.parse_args()
    root = Path(args.root).resolve()

    try:
        fields, version, source = load_contract(args.plugin)
    except ContractError as exc:
        fail(f"agent-squad's spawn-field contract cannot be loaded: {exc}")
        print(f"\n{len(FAILURES)} failure(s)")
        sys.exit(1)
    print(f"contract: {source}")
    print(f"agent-squad {version} accepts: {sorted(fields)}")
    check(root, fields, version)

    if FAILURES:
        print(f"\n{len(FAILURES)} failure(s)")
        sys.exit(1)
    print("\nall checks passed")


if __name__ == "__main__":
    main()
