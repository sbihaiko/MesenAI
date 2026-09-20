# ADR-0218: A capture's anchors are chosen against every other capture already decided in the same recording, not just the raw grid stream

- Status: **accepted 2026-09-20.** Answers to *What a human has to pick*:
  (1) ships coordinated with ADR-0217, not independently and not waiting on
  it — both land in the same change; (2) A and B both ship, A is the
  avoidance pass, B is the fallback safety net for whatever A (and
  ADR-0217's C) still cannot separate; (3) yes, B's drop gets a build-time
  warning (mirrors the existing empty-`pending.Candidates` warning) so an
  artist notices a capture that never made it in, not just a silent gap in
  the log; (4) capture order, as Option A already specifies — the rarity
  ordering is unmeasured extra complexity, deferred; (5) A and B ship with
  synthetic-`GridFrame` unit coverage now, no re-record required. Selected
  through the decision prompt, recorded verbatim as the labels the user
  picked: for the safety net, *"Sim, implementar agora"*; for the avoidance
  pass, *"Sim, adicionar a opção A do ADR-0218"*.
- Date: 2026-09-19
- Related: ADR-0050 (bootstrap captures a static screen as a `<background>`
  gated on three `tileAtPosition` anchors, `kAnchorMinSpread` = 64px),
  ADR-0159 (anchors are chosen once at save time, from cells the screen's
  variants do not change), ADR-0217 (`proposed`; same recorder, same
  `ScreenStitcher.cpp` functions — see *Relationship to ADR-0217* below),
  issue #349, `Core/NES/HdPacks/HdPackBuilder.cpp`
  (`CaptureScreen`, `FinalizeScreenAnchors`), `Core/NES/HdPacks/HdNesPack.cpp`
  (`GetLayerIndex`), `Core/NES/HdPacks/ScreenStitcher.cpp`
  (`SelectScreenAnchors`, `GreedyAnchors`, `IsScreenVariant`)
- Supersedes / amends: nothing yet. Every option changes
  `HdPackBuilder::FinalizeScreenAnchors`; option C also touches
  `MesenSheets::GreedyAnchors`'s signature.

## Context

### What the recorder does today

`HdPackBuilder::CaptureScreen()` fires whenever the game holds a frame
still: it writes `backgrounds/screenNNN.png` to disk immediately and
appends a `PendingScreen` (candidate anchor cells, no conditions yet) to
`_pendingScreens` (capped at `MaxScreensPerPack` = 300).

`HdPackBuilder::FinalizeScreenAnchors()` runs once, at the end of the
recording, and loops over `_pendingScreens` **independently**: each
iteration calls `MesenSheets::SelectScreenAnchors(_gridFrames, captured,
pending.Cells)` on its own, with no memory of what the previous iterations
picked. `_gridFrames` is the one retained grid-frame stream for the whole
session (capped by `kMaxSheetFrames` = 4096), so the raw pixel history of
every other capture is technically in scope for each call — but
`SelectScreenAnchors` only ever compares the current screen against that
raw stream, filtered through `IsScreenVariant`'s 90 % cell-agreement test
(`kAnchorVariantAgree`). A frame classified a *variant* is excluded from
discrimination; only frames classified *rivals* make `GreedyAnchors`
narrow the pick.

Two different pending screens that are ≥ 90 % cell-identical to each
other — successive frames of a typewriter credits screen, near-duplicate
title cards, a level-select cursor one position over — file each other as
variants, not rivals. Neither call ever learns that the *other* pending
screen exists as a screen someone chose to capture and is about to receive
its own `<background>` entry. Both searches can converge on the exact same
`(row, col, tile, palette)` triple, independently and correctly by their
own local logic.

`HdNesPack::GetLayerIndex()` then walks `BackgroundsByPriority[priority]`
in insertion order and returns the first entry whose conditions all
evaluate true. Two backgrounds with an identical triple are indistinguishable
at read time: the second one is permanently unreachable. It ships, lints
clean, and builds clean — nothing downstream ever sees the collision.

### Measured (issue #349, 2026-09-19 sweep, 28 packs, 241 captures)

104 of 241 captures share a byte-identical condition triple (name stripped)
with an earlier capture in the same pack. The co-gated PNGs are not
redundant — verified pixel-distinct by sha256 (Donkey Kong: 46 captures on
one gate, all 46 PNGs distinct; Ice Climber 22; Pac-Man 13 + 9; Bomberman
6 + 4; Punch-Out!! 5; Tennis 4; Mario Bros., Double Dragon, The Flintstones
2 each). This is real, silent data loss, not benign dedup — 45 of Donkey
Kong's 46 recorded backgrounds exist on disk and none of them can ever be
displayed.

A greedy minimum-separating-set search over the full 960-cell frame shows
the probe *count* is not the blocker: 1 probe already separates 117 of 232
captures in multi-capture packs, 2 more; `kAnchorCount` = 3 is enough for
the overwhelming majority. The recorder simply never tries, because
nothing in `FinalizeScreenAnchors` asks one pending screen's search to
avoid another's answer.

### Relationship to ADR-0217

ADR-0217 (`proposed`) measures the same recorder from the read side: given
that two captures' gates already match, which one should win
(`GetLayerIndex`'s first-match rule is arbitrary and silent today). Its
Option C — "every other capture is a rival, never a variant" — comes
closest to a write-time fix, but it works by changing `IsScreenVariant`'s
*raw-frame* classification (the 90 %-agreement test against `_gridFrames`)
for a *single* `SelectScreenAnchors` call. By ADR-0217's own numbers that
still leaves 9 frame pairs that are byte-identical at every cell (no
threshold recovers those) and interacts with `kAnchorMinSpread`, which
alone makes 53 captures inseparable at any probe count.

This ADR is independent of whichever precedence rule ADR-0217 settles on:
**no read-time resolution rule can recover a frame whose gate is bit-for-bit
identical to another already-committed gate** — once two backgrounds carry
the same triple, one of them is gone regardless of which one `GetLayerIndex`
prefers. The question here is narrower and sits earlier in the pipeline:
should `FinalizeScreenAnchors` check a new pending screen's chosen triple
against triples *already assigned* to other pending screens processed
earlier in the same loop — independent of, and in addition to, whatever
variant/rival policy a single `SelectScreenAnchors` call uses internally?
Adopting an ADR-0217 option (e.g. its Option C) narrows how often this
happens; it does not by itself guarantee zero exact collisions, because it
still reasons per-call about raw-frame similarity, not about the finalized
condition set the loop is actually producing.

### A relevant timing fact

`CaptureScreen()` writes `backgrounds/screenNNN.png` to disk immediately,
at capture time. `FinalizeScreenAnchors()` only assigns
`HdPackTileAtPositionCondition`s and appends to
`_hdData.BackgroundsByPriority` — it does not write `hires.txt` (or any
other manifest). `WriteAll()`/the enclosing save function calls
`FinalizeScreenAnchors()` before it builds sheets and before the pack
manifest is serialized. So at the point `FinalizeScreenAnchors` is running,
every capture's PNG already exists on disk, but **no condition has been
written to any file yet** — `_hdData.Conditions` and
`BackgroundsByPriority` are still plain in-memory vectors. Revising an
earlier pending screen's anchor choice inside this same loop touches
nothing already flushed to disk.

### Non-goals

- Changing `GetLayerIndex`'s precedence rule, or anything about which
  capture wins once two gates do collide — that is ADR-0217's question.
- Changing `IsScreenVariant`'s threshold or the raw-frame variant/rival
  split used *inside* a single `SelectScreenAnchors` call for cells a
  variant may legitimately change (ADR-0159's territory).
- Relaxing or measuring `kAnchorMinSpread` — noted as a shared dependency
  with ADR-0217, not re-litigated here.
- Retro-fitting packs already on disk. Every option here is a recorder
  change and only takes effect on the next recording.

## Decision

**A and B ship together.** C is kept below as the record of what was
weighed, not adopted: its fixed-point re-search recovers more but the
implementation cost (a mutable, growing forced-rival set plus a
convergence pass over `GreedyAnchors`'s call sites) is not justified before
A and B are shipped and measured — B's own drop-and-log count is the
number that says whether chasing C's extra recovery is worth it.

### A — Forced extra rivals from previously-decided captures

Process `_pendingScreens` in capture order (already the vector's natural
order). Before calling `SelectScreenAnchors` for screen *N*, collect the
`GridFrame*` at `pending.GridFrameIndex` for every screen 1..N-1 already
finalized, and pass them into `GreedyAnchors` as forced rivals — bypassing
`IsScreenVariant` entirely for this set, since a screen someone chose to
capture separately is by definition a screen worth telling apart from.

- **Cost:** one triple-compare per already-decided screen per candidate,
  per pick — bounded by `MaxScreensPerPack`² × `kAnchorCount` = 300² × 3 =
  270 000 compares worst case, at finalize time, off the hot path.
- **Memory:** none beyond what `_pendingScreens` and `_gridFrames` already
  hold — `GridFrameIndex` is already recorded per pending screen.
- **Order-dependent:** screen 1 never discriminates against screen 300;
  screen 300 discriminates against all 299 before it. A later capture in
  the recording is more constrained than an earlier one. Whether that
  matches which screens most need it (a typewriter's *last* frame vs. its
  first) is unmeasured.
- **Does not rewrite earlier picks:** if screen 1 and screen 50 end up
  with the same triple anyway (screen 1 didn't know screen 50 was coming),
  the collision is caught for every *later* pair but not for pairs where
  the earlier screen was decided before the later one existed — which,
  because of the order, is only true if two screens' *own* variant sets
  keep the collision cell out of both pools regardless.

### B — Post-hoc exact-collision scan, drop the later duplicate

Run `FinalizeScreenAnchors` exactly as it runs today — each pending screen
searches only against `_gridFrames`. After the whole loop, before
`BuildSheets()`, scan `_hdData.BackgroundsByPriority` for any two entries
at the same priority whose condition triples are identical once each
condition's name is stripped. For every collision, drop the later entry:
remove it from `BackgroundsByPriority` and log which pending screen's PNG
now has no `<background>` line (mirrors what the pack already does when
`pending.Candidates` comes back empty).

- **Cost:** O(pendingScreens² × kAnchorCount) triple-compares, same order
  as A, but a separate, simpler pass with no change to `GreedyAnchors`.
- **What it does not do:** exactly like ADR-0217's Option A, it does not
  recover the dropped capture's frame — it converts a silent wrong-render
  into a visible, logged loss (PNG stays on disk, no `<background>` cites
  it). No frame that draws today changes; 104 of 241 captures currently
  never draw regardless, this just stops shipping the dead `<condition>`
  lines and makes the loss auditable.
- **Simplest to implement and to test:** a pure post-processing step over
  already-finalized data, no interaction with `GreedyAnchors`'s internals.

### C — Retroactively re-run the earlier capture too

Same scan as B, but instead of dropping the later duplicate, re-invoke
`SelectScreenAnchors` for *both* colliding screens with each other's
`GridFrame` added to the forced-rival set (as in A), and replace both
entries' conditions with the new picks. Since (per *A relevant timing
fact* above) no condition has been serialized yet when this runs, revising
an earlier pending screen's `HdBackgroundInfo::Conditions` in place is
safe — nothing already on disk needs to change.

- **Cost:** worst case, every pair collides and every pair triggers two
  re-searches — O(pendingScreens² × frames) if collisions cluster, since
  each re-search is a full `GreedyAnchors` pass again. Still bounded
  (300 screens, ≤ 4096 frames), still finalize-time only, but the most
  expensive of the three.
- **What it buys over A:** order stops mattering — a collision between
  screen 1 and screen 50 gets caught and both get a chance to move off the
  shared triple, not just the second one constrained against the first.
- **What it does not solve:** a re-search can still fail to separate two
  screens (the 9 byte-identical-frame cases ADR-0217 found are byte-
  identical at every cell, not just at three probes — no re-search recovers
  those). Needs a fallback matching B's drop-and-log for whatever a
  re-search still can't separate.
- **Implementation cost:** touches `GreedyAnchors`'s call sites to accept
  a mutable, growing forced-rival set and needs a second convergence pass
  (a retroactive fix can itself collide with a third screen), so it is not
  simply "call the function again" — it is closer to a small fixed-point
  loop over `_pendingScreens`.

## What a human has to pick

1. **Does this ADR ship independently of ADR-0217, or wait on it?** A and C
   change how `GreedyAnchors` is fed rivals — the same lever ADR-0217's
   Option C reaches for, for a different rival set (other captures vs.
   raw frames). Landing both without coordinating risks either duplicated
   work or a rival set that double-counts the same frame under two
   different exclusion rules.
2. **A vs. B vs. C:** B is the cheapest and safest (pure post-hoc audit,
   no `GreedyAnchors` signature change) but never recovers a frame — it
   only stops the pack lying about what will draw. A is a single forward
   pass with an order bias that is unmeasured. C recovers the most frames
   but is the most invasive and still needs B's drop-and-log as a
   fallback for the unseparable remainder.
3. **Does B's "drop" belong in the pack, or only in the log?** Dropping
   the `<background>` line for a losing duplicate still leaves its PNG on
   disk with nothing to trigger it — worth a build-time warning
   (`mep_lint`?) so the artist notices a capture that never made it in,
   the way an empty `pending.Candidates` already produces one today.
4. **Is the order in A (capture order) the right one,** or should a
   pending screen's *rarity* (how few frames match it at all) decide which
   screen "gets" a contested triple first?
5. **Whichever option ships, does it get unit coverage before or after a
   re-record of the 30-ROM library?** A and B are pure finalize-time logic
   over `_pendingScreens`/`_hdData` and can be covered with synthetic
   `GridFrame`s in `scripts/core_unit_tests.cpp` without recording
   anything; C's fixed-point convergence needs the same synthetic harness
   but more cases (a three-way collision chain).

## Consequences

- Whatever is picked here composes with ADR-0217: this ADR stops two
  *different* captures from being minted with the same gate; ADR-0217
  decides what happens when a gate still ends up shared (by policy choice,
  or by one of the 9 byte-identical-frame cases neither ADR can fix).
- A and C add a `_pendingScreens`-order or fixed-point dependency to
  `FinalizeScreenAnchors` that today has none — each pending screen is
  currently order-independent of every other. That is worth flagging in
  the function's own comments once one of these lands, since the next
  reader will otherwise assume (correctly, today) that reordering
  `_pendingScreens` cannot change the output.
- B and C both need `scripts/spike_anchor_stability.py` (or a sibling
  script) extended with a same-session cross-capture collision rate, the
  way ADR-0217 already asks for a "captures whose gate another capture
  satisfies" figure — the two measurements are related but not identical:
  ADR-0217's is measured against the finalized, on-disk pack; this ADR's
  would be measured *during* `FinalizeScreenAnchors`, before any dropping
  or re-search, to show how much each option actually closes.
- Nothing here helps a pack already on disk. Every option here is a
  recorder change and takes effect only on the next recording.
