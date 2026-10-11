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

Two rules, and the weaker one is the one that always runs:

1. Every example is bounded by the field list **its own graph declares** for a
   request (the backticked `{...}` list beside the example). This needs nothing
   but the graph, so it holds everywhere, CI included, and it is exactly the
   #1800 shape: list moved, example left behind. Weaker than rule 2 - the two
   lists sit in one file - and it is not a substitute for it.
2. Where a **runtime plugin** is reachable, that plugin's REQUEST_FIELDS is the
   contract: no example may carry a field it does not accept, no graph may
   *declare* one either, and the committed copy at
   `scripts/vendor/agent_squad/envelope.py` must agree with it. This is the rule
   that turns a plugin-side field rename red on the machine that runs the squad.

The contract is read from a runtime plugin and from nothing else: `--plugin`, then
`$AGENT_SQUAD_PLUGIN`, then the newest plugin installed under
`~/.claude/plugins/cache`. The committed copy is a **recording** of a runtime
plugin - the fork's `squad/envelope.py` verbatim, header sha256 verified before it
is imported - and a recording is never a contract, named or not: a copy of the
artifact under test cannot disagree with itself, and CI naming it is what kept a
plugin-side rename green (review of PR #1804). Wherever a plugin did answer, the
copy is read - against that plugin and against its own header - so it cannot age
in silence there; a copy that is stale or edited fails and is re-vendored from
the fork rather than left standing in for the plugin.

Where **no** plugin is reachable - the GitHub runner, which has none installed and
cannot read the plugin's private repository - the guard runs rule 1 and prints what
that leaves unchecked. It does not skip, and it does not name the copy: rule 1 is a
real check that reds on drift, and the plugin half is named as the part only a
machine holding the plugin can make. A fence that is meant to hold JSON and does
not parse fails too: an example the guard cannot read is an example it does not
check. A graph whose example has no declared field list beside it fails for the
same reason - nothing bounds it (review of PR #1804, issue #1800).

Usage:
  python3 scripts/test_squad_graph_spawn_fields.py
  python3 scripts/test_squad_graph_spawn_fields.py --root /tmp/copy-of-another-revision
  python3 scripts/test_squad_graph_spawn_fields.py --plugin ~/.claude/plugins/cache/agent-squad/agent-squad/0.2.0

`--root` points the check at another revision's checkout of the graph (for example
`git show origin/main:docs/squad/workflows/dynamic.graph.json` written into a temp
tree), so the pre-fix failure can be shown without touching this worktree.
`--plugin` and `AGENT_SQUAD_PLUGIN` read the contract from a given runtime plugin
(or its `squad/envelope.py`) rather than from the installed cache; a path that
holds no loadable contract is a failure, never a fallback, and a path that holds
a recording is refused as a contract.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib
import importlib.util
import json
import os
import re
import sys
import types
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
WORKFLOWS = Path("docs") / "squad" / "workflows"
RECORDED = Path("scripts") / "vendor" / "agent_squad" / "envelope.py"
ENV = "AGENT_SQUAD_PLUGIN"
END_MARK = "# ---- end vendored header ----\n"

# `(json)?` separates a fence tagged as JSON from a plain one, and from the prose
# and shell blocks the same instructions carry: only the first is promised to be
# JSON, so only the first is a failure when it does not parse.
FENCE = re.compile(r"```(json)?[ \t]*\n(.*?)```", re.DOTALL)

# The field list a graph declares for a request, written as a backticked brace
# list of bare names - `{prompt, skill, model}`. A backticked JSON object
# (`{"kind": "agent"}`) is not one: its members are `"key": value` pairs.
DECLARATION = re.compile(r"`\{([A-Za-z_][A-Za-z0-9_]*(?:\s*,\s*[A-Za-z_][A-Za-z0-9_]*)*)\}`")

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


def fields_from_recording(path):
    """(REQUEST_FIELDS, version) from a committed copy, header sha256 checked first.

    The copy's body is the fork's file verbatim, so the `from squad import skills`
    it carries is satisfied by stubs: REQUEST_FIELDS is a module-level constant
    and the guard calls nothing in the module. The sha256 check is what makes the
    copy trustworthy: a copy edited to agree with a graph fails here instead.
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
        return set(module.REQUEST_FIELDS), pinned.get("source-version", "unnamed")
    except AttributeError as exc:
        raise ContractError(f"{path}: defines no REQUEST_FIELDS ({exc})") from exc
    except (ImportError, SyntaxError) as exc:
        raise ContractError(f"{path}: cannot be imported ({exc.__class__.__name__}: {exc})") from exc


def is_recording(path):
    """Whether `path` is a committed copy rather than a plugin's own file."""
    try:
        return END_MARK in path.read_text(encoding="utf-8")
    except OSError:
        return False


def read_contract(named):
    """(REQUEST_FIELDS, version, source) from a runtime plugin at the path `named`.

    A recording is refused before anything is imported: it is a copy of the
    artifact under test, so its agreeing with the graph would prove only that the
    two hand-written texts agree with each other (review of PR #1804).
    """
    envelope = envelope_in(named)
    if envelope is None:
        raise ContractError(f"{named}: no runtime plugin there (no squad/envelope.py, no envelope.py)")
    if is_recording(envelope):
        raise ContractError(
            f"{envelope}: a recording, not a runtime contract - it is the committed read-only copy "
            f"of a plugin's squad/envelope.py, and a copy of the artifact under test cannot disagree "
            f"with itself. Name the plugin the router runs inside; the copy is checked against it, "
            f"never in its place"
        )
    version = version_of(envelope)
    return fields_from_plugin(envelope), version, f"{envelope} (runtime agent-squad {version})"


def check_recording(fields, source):
    """Fail when the committed copy disagrees with the runtime plugin that answered.

    This is what keeps the copy from standing in for the plugin: a runtime plugin
    whose fields moved fails the suite here, on the machine that has both, and the
    copy is re-vendored from it instead of quietly ageing.
    """
    if not (REPO_ROOT / RECORDED).is_file():
        fail(f"{RECORDED}: the committed read-only copy is missing; it is the recording of the "
             f"runtime contract, and a tree without it cannot say whether the plugin has moved")
        return
    try:
        recorded, version = fields_from_recording(REPO_ROOT / RECORDED)
    except ContractError as exc:
        fail(f"the committed copy cannot be read: {exc}")
        return
    if recorded != fields:
        fail(
            f"{RECORDED} records agent-squad {version} accepting {sorted(recorded)}, but {source} "
            f"accepts {sorted(fields)}: re-vendor the copy from that plugin "
            f"(the header names the fork commit it came from) before trusting this check"
        )


def load_contract(plugin):
    """(REQUEST_FIELDS, version, source) from a runtime plugin, or None when none is reachable.

    `--plugin` wins, then `AGENT_SQUAD_PLUGIN`, then the newest plugin installed
    under the cache. A source that is present but unusable is an error and not a
    reason to try the next one: falling through would hide the broken plugin the
    guard exists to report, and reporting it is the whole point. With none of them
    the guard returns None and checks rule 1 alone, saying so; it never reaches
    for the committed copy, which is a recording and not a contract.
    """
    named = plugin or os.environ.get(ENV) or ""
    if named:
        fields, version, source = read_contract(named)
        check_recording(fields, source)
        return fields, version, source
    cached = cached_plugins()
    if not cached:
        return None
    envelope, version = cached[0]
    fields, source = fields_from_plugin(envelope), f"{envelope} (runtime agent-squad {version})"
    check_recording(fields, source)
    return fields, version, source


def declared_fields(text):
    """The field names `text` declares for a request: the backticked brace lists."""
    found = set()
    for match in DECLARATION.finditer(text):
        found.update(name.strip() for name in match.group(1).split(","))
    return found


def spawn_examples(text, where):
    """Every example request in `text`; a fence that should be JSON but is not is a failure."""
    out = []
    for match in FENCE.finditer(text):
        tagged, body = match.group(1), match.group(2)
        try:
            parsed = json.loads(body)
        except json.JSONDecodeError as exc:
            # A ```json fence is promised to be JSON, and so is an untagged one
            # that opens an object or an array: a plain fence holding prose or
            # shell is neither. Skipping a malformed one is how an example that
            # carries a rejected field reported PASS without being read.
            if tagged or body.lstrip()[:1] in ("{", "["):
                fail(f"{where}: a JSON fence does not parse, so its example went unchecked ({exc})")
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


def check_graph(path, root, contract):
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
    fields = contract[0] if contract else None
    before = len(FAILURES)
    for node_name, text in texts:
        where = f"{rel}: node {node_name!r}" if node_name else str(rel)
        declared = declared_fields(text)
        for spawn in spawn_examples(text, where):
            keys = set(spawn)
            if not declared:
                fail(
                    f"{where}: the example spawn (keys: {sorted(keys)}) sits in a text that declares no "
                    f"spawn field list, so nothing bounds which fields it may name"
                )
                continue
            undeclared = sorted(keys - declared)
            if undeclared:
                fail(
                    f"{where} example spawn carries fields the graph does not declare for a request: "
                    f"{undeclared} (declared: {sorted(declared)})"
                )
            if fields is not None:
                extra = sorted(keys - fields)
                if extra:
                    fail(
                        f"{where} example spawn carries fields agent-squad does not accept: {extra} "
                        f"(spawn keys: {sorted(keys)})"
                    )
        if fields is not None:
            rejected = sorted(declared - fields)
            if rejected:
                fail(
                    f"{where} declares fields agent-squad does not accept: {rejected} "
                    f"(declared: {sorted(declared)})"
                )
    if len(FAILURES) == before:
        ok(f"{rel}: every example spawn names only accepted fields")


def check(root, contract):
    paths = graph_files(root)
    if not paths:
        fail(f"no *.graph.json under {root / WORKFLOWS}")
        return
    for path in paths:
        check_graph(path, root, contract)
    if contract:
        ok(f"{len(paths)} graph file(s) checked against agent-squad {contract[1]} REQUEST_FIELDS")
    else:
        ok(f"{len(paths)} graph file(s) checked against the field list each one declares")


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument(
        "--root",
        default=str(REPO_ROOT),
        help="checkout holding docs/squad/workflows (default: this repository)",
    )
    parser.add_argument(
        "--plugin",
        help="runtime plugin root (or its squad/envelope.py) to read the contract from, "
        f"instead of ${ENV} or the installed plugin cache; a committed copy is not a contract",
    )
    args = parser.parse_args()
    root = Path(args.root).resolve()

    try:
        contract = load_contract(args.plugin)
    except ContractError as exc:
        fail(f"agent-squad's spawn-field contract cannot be loaded: {exc}")
        print(f"\n{len(FAILURES)} failure(s)")
        sys.exit(1)
    if contract:
        print(f"contract: {contract[2]}")
        print(f"agent-squad {contract[1]} accepts: {sorted(contract[0])}")
    else:
        print(
            f"contract: no runtime plugin reachable here (no --plugin, no ${ENV}, and no plugin under "
            f"{Path.home() / '.claude' / 'plugins' / 'cache'})"
        )
        print(
            "NOT CHECKED here: whether the field list a graph declares still matches agent-squad's "
            "REQUEST_FIELDS. Each graph is checked against the list it declares for itself, which is "
            "what catches a graph whose list moved and whose example did not; agreement with the "
            "plugin is checked on a machine that has it installed, at its next run of this guard"
        )
    check(root, contract)

    if FAILURES:
        print(f"\n{len(FAILURES)} failure(s)")
        sys.exit(1)
    print("\nall checks passed")


if __name__ == "__main__":
    main()
