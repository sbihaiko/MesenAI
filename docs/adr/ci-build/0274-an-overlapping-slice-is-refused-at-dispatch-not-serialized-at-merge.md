# ADR-0274: An overlapping slice is refused at dispatch, not serialized at merge

- Status: accepted (amended 2026-10-11 by GitHub issue #1520; the owner's go-ahead, verbatim: "The change is an amendment in place (same decision, widened scope), not a new ADR.")
- Date: 2026-10-10
- Related: ADR-0273 (the two-level review verdict this completes the other half of)
- Supersedes / amends: amends ADR-0273 (closes its merge-queue non-goal; see Context)

## Context

The squad runs a panel: several children at once, each holding a disjoint file
slice. Two children that edit the same file produce a conflict for whoever
merges second, and the cost is paid twice — once in the merge, once in the fix
round that follows it.

It is not hypothetical. PR #1285 spent three fix rounds on conflicts inside two
hours (`review conflict` 18:22, `conflict` 19:07, `conflict` 19:22), against
sibling PR #1391 editing the same files. The overlap had already been detected:
`mkreq.sh`'s `slice_guard` named `UI/Logic/TestHook/TestHookProtocol.cs` and
`UI/Logic/TestHook/TestHookPlayerState.cs` as also named by an open PR. It fired
as a note appended to the child's own request, and a note inside the request is
advice the child may ignore — which it did.

This decision first read the overlap as a same-panel problem, and the guard
compared a request only against the other pending requests of its own panel. That
scope was too narrow, and the fork's own tree shows it: `gui/1497`, `gui/1498`
and `gui/1502` each landed a branch on `contrib/squad-goal/scripts/hub.py` —
three slices of three different panels, overlapping the same file — and the fork
merge stalled on it. Nothing about that collision was panel-scoped; the file was
simply in flight, and every panel-scoped guard was blind to it.

ADR-0273 sketched a merge queue as the remedy and put it out of its own scope.
Measured against the loop's logs, a merge queue does not address this: it
serializes *merges*, while the conflict is authored by parallel *edits*. No
ordering of merges turns a file edited on both sides into a clean merge; it only
decides who loses. What remained of its job is already done elsewhere — the
autopilot loop merges one PR per pass, and `autopilot.sh` already refuses to
spend a review on a head it knows is about to move (a `mergeable_state` of
`dirty` queues a fix round instead of a reviewer). A queue on top of that adds
starvation and ordering rules with no measurable gain at ten concurrent PRs.

## Decision

The overlap check becomes a **dispatch-time gate over any slice in flight**, not
only over siblings of one panel: `mkreq.sh`'s `slice_guard` refuses the launch
when the request body names a file that is already in flight, and the panel is
re-split before a run is spent. The gate stays where the knowledge already is —
the file paths a request names, measured against the paths every slice in flight
names.

A file is in flight when any of three sources holds it:

- **An open PR that changes the file.** The guard reads the changed files of
  every open PR. An overlap there is refused, not merely reported: the dirty-head
  rule in the autopilot loop sees it only once a head is already moving, and that
  costs a review and a merge round.
- **A queued request of any panel that names it.** Every pending request in the
  squad's request cache is compared, whichever panel wrote it. The same-panel
  case — the original scope of this decision (#1400) — is the narrow half of this
  rule; an overlapping pair of one panel still says so in the refusal, but it is
  no longer the boundary.
- **An unmerged `gui/<N>` branch in the fork checkout.** A fork-only child
  commits on its own branch off the fork base and opens no PR, so its slice is
  visible nowhere else. A branch holding commits ahead of the fork base has its
  changed files read from the fork tree; a branch already merged into the base is
  not in flight and never refuses.

The refusal *is* the launch: `mkreq.sh` exits non-zero, writes no request file,
and prints a message naming the file and its holder — the PR, the other request,
or the branch. The repair is a re-split or an explicit `## Blocked by` edge
between the two slices, both of which belong to the dispatcher and are cheap
there. A child told to avoid a file it needs has no honest way to comply.

A deliberate overlap stays possible: `SG_SLICE_FORCE=1` dispatches anyway and
announces it on stderr, so the exception has to be asked for rather than assumed.

The merge queue is **not built**. ADR-0273's out-of-scope note is closed by this
decision rather than left open.

## Consequences

- A launch can now fail on a file an open PR or an unmerged fork branch already
  holds, not only on a sibling's. A collision that is real and unavoidable is
  resolved by re-splitting the slices or by adding a `## Blocked by` edge between
  the two holders, so the shared file is written once and read after — never by
  disabling the gate.
- The detection moves earlier, so its cost drops: the same overlap that used to
  cost a review, a merge conflict and a fix round now costs a dispatch retry.
  The three `contrib/squad-goal/scripts/hub.py` branches of `gui/1497`,
  `gui/1498` and `gui/1502` measure what the panel-scoped gate let through.
- No merge machinery is added, so nothing new can starve, reorder or batch. The
  loop stays serial per PR and trunk-based per merge.
- The gate can only see paths a request *names*. A child that decides to touch a
  file its request did not name is still caught by the dirty-head rule, one layer
  later and more expensively.
- The gate now reads two sources outside the request cache — the open PRs and the
  fork checkout — so a launch can be refused on state the child cannot see from
  its own request. The refusal names the holder for exactly that reason.
