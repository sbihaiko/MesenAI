# ADR-0276: The spawn-field guard's contract is a runtime plugin where one is installed, and CI checks the graph against the field list it declares itself

- Status: accepted (2026-10-11, by the panel ruling on PR #1804 that decided this round; the go-ahead, verbatim: "Change what CI checks, so CI stops lying by construction." and "(the decision was taken 2026-10-11 answering the owner question on PR #1804; the alternative - granting CI read access to the plugin - is an owner-only access change, tracked as issue #1812)". Implementation: already in the tree as of this ADR — `scripts/test_squad_graph_spawn_fields.py` runs the two halves, `.github/workflows/checks.yml` supplies no contract, and `scripts/test_squad_graph_spawn_fields_guard.py` carries one case per half)
- Date: 2026-10-11
- Related: issue #1800, PR #1804, issue #1812; the run that died on the drift was `run-20261011-160224-2d865b`
- Supersedes / amends: amends the earlier shape of this decision, recorded on PR #1804, in which CI named the committed copy as its contract — that is the finding the review of #1804 kept

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
not published, and the repository that holds it answers 404 for every credential
this repository carries.

The first attempt at that gap made CI's contract a copy of the plugin committed
here (`scripts/vendor/agent_squad/envelope.py`, named through `AGENT_SQUAD_PLUGIN`).
That is a test that cannot fail on the thing it exists to catch: a copy of the
artifact under test agrees with whatever that copy says, so a field rename on the
plugin side stays green in CI. The review of PR #1804 raised it as blocking, five
heads in a row, and it is right.

Non-goals. Whether CI should be able to read the plugin's repository (a secret with
read on it, or a published artifact of the fork) is an access change, and it is the
owner's to make, not a deputy's: it is tracked as issue #1812 and this ADR does not
settle it. Nothing here weakens the guard on a machine that has the plugin.

## Decision

**1. Where a runtime plugin is reachable, that plugin's `REQUEST_FIELDS` is the
contract.** The guard reads it from the plugin named by `--plugin`, then
`$AGENT_SQUAD_PLUGIN`, then the newest plugin installed under
`~/.claude/plugins/cache/agent-squad/*/squad/envelope.py`. No example spawn may
carry a field it does not accept, and no graph may *declare* one either. A source
that is named but unusable is a failure, never a reason to try the next one:
falling through would hide the broken plugin the guard exists to report.

**2. In GitHub CI the guard is given no contract from outside this repository, and
checks the graph against itself.** `python-tests` sets no `AGENT_SQUAD_PLUGIN`; no
workflow supplies one. With nothing named and no plugin installed, the guard asserts
what the file can prove on its own: every key of every example spawn is one of the
field names the node's own instructions list beside it, and a node whose example has
no declared list beside it fails rather than passing unbounded. That is the #1800
shape — a list that moved inside one file while its example stayed behind — and it is
the whole of what this job claims. Its job comment says so in place, so a reader of
the check sees what a green run here does and does not mean.

**3. The committed copy is a recording, never a contract.** It is the fork's
`squad/envelope.py` verbatim, with the sha256 of its body in its header, verified
before it is read. It is compared against the runtime plugin whenever one answered —
a rename on the plugin side fails the suite on that machine until the copy is
re-vendored — and a copy whose body was edited fails its own header check. No
workflow hands it in as the contract, and naming it as one is refused before anything
is imported.

**4. A missing contract fails; it never skips.** A named source that holds no
loadable contract fails. A fenced block tagged as JSON — or opening as an object or
array while untagged — that does not parse fails too: an example the guard cannot
read is an example it does not check. Skipping is how `turns` sat in the graph
unobserved until a run died on it.

**5. No other claim may be read into a green CI run.** The job comment, the guard's
docstring and this ADR all state the limit: what CI proves is the graph's agreement
with the field list the graph declares, never the plugin's runtime field list.

## Consequences

- A plugin-side field rename is caught on a machine that has the plugin installed,
  at its next `make python-tests`, and **not** by GitHub CI. This is the cost of the
  decision, and it is why item 5 exists: a reviewer must be able to see from the
  check's own text that a green run is not a claim about the plugin.
- A graph edited so its declared list and its example drift apart — the failure that
  actually bit — is caught in CI, on the same run, by item 2.
- Any change to the plugin's spawn fields requires re-vendoring the copy. The header
  sha256 makes a half-updated copy fail rather than pass, and the runtime comparison
  makes a stale copy fail on the machine that has both.
- The vendored file is a recording, not a source: it is never reached for on its own,
  and no code may import it as though it were the plugin.
- If issue #1812 is ever granted — CI reads the plugin's repository, or an artifact
  of the fork is published for it to fetch — item 2 is superseded by that grant and
  the guard falls back to the installed-plugin path it already has. The rest of the
  decision (runtime first, the recording compared and never substituted, fail never
  skip) stands, and item 5 shrinks to the new grant's own limit.
