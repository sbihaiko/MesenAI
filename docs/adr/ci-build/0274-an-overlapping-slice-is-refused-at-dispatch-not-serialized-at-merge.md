# ADR-0274: An overlapping slice is refused at dispatch, not serialized at merge

- Status: accepted (2026-10-10, by the user: "Porta no dispatch (recomendado)"; implementation = #1400, not yet built)
- Date: 2026-10-10
- Related: ADR-0273 (the two-level review verdict this completes the other half of; its Context and Consequences put GitHub's native merge queue out of scope)

## Context

The squad runs a panel: several children at once, each holding a disjoint file
slice. Two children that edit the same file produce a conflict for whoever
merges second, and the cost is paid twice — once in the merge, once in the fix
round that follows it.

It is not hypothetical. PR #1285 spent three fix rounds on conflicts inside two
hours, against sibling PR #1391 editing the same files (source: the PR's own
fix-round history and the autopilot loop's log lines; ADR-0273 records the same
PR as about two hours across five heads). The overlap had already been detected:
`mkreq.sh`'s `slice_guard` named `UI/Logic/TestHook/TestHookProtocol.cs` and
`UI/Logic/TestHook/TestHookPlayerState.cs` as also named by an open PR. It fired
as a note appended to the child's own request, and a note inside the request is
advice the child may ignore — which it did.

ADR-0273 put GitHub's native merge queue out of its own scope (a change to the
merge path and the `main gate` ruleset). By reasoning about how it works, that
merge queue does not address this overlap: it
serializes *merges*, while the conflict is authored by parallel *edits*. No
ordering of merges turns a file edited on both sides into a clean merge; it only
decides who loses. What remained of its job is already done elsewhere — `autopilot.sh`
already refuses to spend a review on a head it knows is about to move (a
`mergeable_state` of `dirty` queues a fix round instead of a reviewer; see the
`mergeable_state` checks in `~/.claude/skills/squad-goal/scripts/autopilot.sh`).
A queue on top of that adds starvation and ordering rules; the expectation, an
assumption not measured here, is that it brings little gain at ten concurrent PRs.

## Decision

The overlap check becomes a **dispatch-time gate**. Two children of the same
panel may not name the same file: `mkreq.sh` refuses the launch on a hit, and the
panel is re-split before a run is spent. The check stays where the knowledge
already is — the file paths a request names, measured against the paths the
other pending requests of that same panel name.

The gate is calibrated narrow, deliberately:

- **Panel members against each other.** Only same-panel, same-launch siblings are
  compared, and a hit is always a refusal: there is no allowlist and no
  exception for a shared test helper or fixture among members of one panel. Such
  a file is written once by one member, and the others declare `## Blocked by`
  on it (Consequences). Legitimate sharing is between the panel and open PRs
  *outside* it, which is exactly why the gate does not compare against those
  (next item): a hard block against *every* open PR would deadlock a wave on a
  file it is entitled to touch.
- **Never against an older open PR that is about to merge.** That overlap is
  already handled where it belongs, by the dirty-head rule in the autopilot loop.
- **Refuse the launch, not the child.** The repair is a re-split or an explicit
  ordering edge, both of which belong to the dispatcher and are cheap there. A
  child told to avoid a file it needs has no honest way to comply.

GitHub's merge queue is **not enabled**. The question ADR-0273 left open (its
Context non-goals and Consequences) is closed by this decision rather than left
open.

## Consequences

- A panel launch can now fail. A collision that is real and unavoidable is
  resolved by re-splitting the slices or by adding a `## Blocked by` edge between
  the two siblings, so the shared file is written once and read after — never by
  disabling the gate.
- The detection moves earlier, so its cost drops: the same overlap that used to
  cost a review, a merge conflict and a fix round now costs a dispatch retry.
- No merge machinery is added, so nothing new can starve, reorder or batch. The
  loop stays serial per PR and trunk-based per merge.
- The gate can only see paths a request *names*. A child that decides to touch a
  file its request did not name is still caught by the dirty-head rule, one layer
  later and more expensively.
