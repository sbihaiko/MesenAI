# ADR-0276: The spawn-field guard's contract is a runtime plugin where one exists, and the pinned vendored copy in CI — which CI states as its own limit

- Status: accepted
- Date: 2026-10-11
- Related: issue #1800, PR #1804; the run that died on the drift was `run-20261011-160224-2d865b`

## Context

A squad graph teaches its router which field names a spawn request may carry and
shows one example request. Both are hand-written, so both drift from the plugin
the router runs inside. `dynamic.graph.json` drifted exactly there: its field list
was updated for agent-squad 0.2.0 (`stalls`) while its example kept `"turns": 100`,
a 0.1.3-era field. `squad/envelope.py` rejects a request whole on its first unknown
field, and the router had been told to copy the example's field names — so the run
dispatched nothing and its entire spend bought nothing
(`run-20261011-160224-2d865b`: `spawn_rejected unknown_field:turns`, verdict
`failed`, `no_dispatch`).

So the graph is checked against the plugin's real contract. The obvious rule — read
the contract from the installed plugin — cannot hold in GitHub CI: the plugin is
not published, and the repository that holds it answers 404 for the tokens this
repository carries. CI's only reachable contract is a copy committed here.

That copy is a test that can lie: a frozen field list in CI stays green while the
plugin renames a field, which is the failure this guard exists to catch. The
review of PR #1804 raised it as blocking, and the alternative it implies — giving
CI a secret that reads the plugin's repository, or publishing an artifact of the
fork for CI to fetch — is a credential and an access grant. That is the owner's to
give, not a deputy's, and the decision below is worked around it rather than
through it.

## Decision

**Where a runtime plugin exists, the contract is that plugin — never the copy.**
The guard reads it from the newest plugin installed under `~/.claude/plugins/cache`,
or from the one named by `--plugin` / `AGENT_SQUAD_PLUGIN`. Whenever the contract
did come from a runtime plugin, the committed recording is compared against it, so
the two cannot disagree in silence: a field rename on the plugin side fails the
suite on that machine, at its next run, and the copy is re-vendored rather than
left standing in for the plugin.

**In GitHub CI, the contract is the vendored, sha256-pinned copy, and CI says so.**
`python-tests` in `.github/workflows/checks.yml` sets `AGENT_SQUAD_PLUGIN` to
`scripts/vendor/agent_squad/envelope.py` — the fork's `squad/envelope.py` verbatim,
with the sha256 of its body in the header, verified before it is imported. The job
comment names why the copy is there (no plugin installed, the plugin's repository
unreadable) and states the limit of what the job proves.

**What CI proves, and what it does not.** CI proves the graph's field list and its
example agree with the pinned copy, and that the copy is the one whose hash the
header records. CI does **not** prove the copy still agrees with the plugin's
runtime; nothing running here can. That comparison happens on a machine that has
the plugin installed — which is where the skew actually bites, because the loop
runs the plugin from the local fork. A green GitHub CI is therefore not a claim
about the plugin, and no text here may present it as one.

**A missing contract fails; it never skips.** When no contract can be loaded the
guard fails, and a fenced block tagged as JSON that does not parse fails too: a
skip is a PASS that asserts nothing, and skipping is how `turns` sat in the graph
unobserved until a run died on it.

**No plugin access is granted by this decision.** Whether CI should be able to read
the plugin's repository (a secret with read on it, or a published artifact of the
fork) is an access change and stays the owner's. If it is ever granted, the
`AGENT_SQUAD_PLUGIN` line is deleted and the guard falls back to `cached_plugins()`
— which is the behaviour the guard already has, so nothing else moves.

## Consequences

- A plugin-side field rename is caught on the squad's own machine, at the next
  `make python-tests`, and **not** by GitHub CI. This is the cost of the decision
  and is the reason CI's comment states it: a reviewer must be able to see that a
  green check is not a claim about the plugin.
- Any change to the plugin's spawn fields requires re-vendoring the copy. The
  header hash makes a half-updated copy fail rather than pass, and the runtime
  comparison makes a stale copy fail on the machine that has the plugin.
- The vendored file is a recording, not a source: it is never reached for on its
  own, and no code may import it as though it were the plugin.
- If the plugin is ever published, or access is granted, CI's env var goes away and
  this ADR's CI section is superseded by that grant — the rest of the decision
  (runtime first, fail never skip, a copy that cannot disagree in silence) stands.
