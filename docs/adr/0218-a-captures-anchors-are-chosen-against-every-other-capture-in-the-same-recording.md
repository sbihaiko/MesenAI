# ADR-0218: A capture's anchors are chosen against every other capture already decided in the same recording, not just the raw grid stream

- Status: accepted (2026-09-20; ships coordinated with ADR-0217, both in the same change, not independently and not waiting on it). Labels verbatim: for the safety net, "Sim, implementar agora"; for the avoidance pass, "Sim, adicionar a opção A do ADR-0218". A and B ship with synthetic-`GridFrame` unit coverage in `scripts/core_unit_tests.cpp`, no re-record required; the code is in `HdPackBuilder::FinalizeScreenAnchors` (`Core/NES/HdPacks/HdPackBuilder.cpp`).
- Date: 2026-09-19
- Related: ADR-0050 (bootstrap captures a static screen as a `<background>` gated on three `tileAtPosition` anchors, `kAnchorMinSpread` = 64px), ADR-0159 (anchors are chosen once at save time, from cells the screen's variants do not change), ADR-0217 (opened `proposed`; the read-side companion — same recorder, same `ScreenStitcher.cpp` functions), issue #349, `Core/NES/HdPacks/HdPackBuilder.cpp` (`CaptureScreen`, `FinalizeScreenAnchors`), `Core/NES/HdPacks/HdNesPack.cpp` (`GetLayerIndex`), `Core/NES/HdPacks/ScreenStitcher.cpp` (`SelectScreenAnchors`, `GreedyAnchors`, `IsScreenVariant`)

## Context

`HdPackBuilder::CaptureScreen()` fires whenever the game holds a frame still: it writes `backgrounds/screenNNN.png` immediately and appends a `PendingScreen` (candidate anchor cells, no conditions yet) to `_pendingScreens` (capped at `MaxScreensPerPack` = 300).

`HdPackBuilder::FinalizeScreenAnchors()` runs once at the end of the recording and loops over `_pendingScreens` **independently**: each iteration calls `MesenSheets::SelectScreenAnchors(_gridFrames, captured,
pending.Cells)` on its own, with no memory of what the previous iterations picked. `_gridFrames` is the one retained grid-frame stream for the whole session (capped by `kMaxSheetFrames` = 4096), so every other capture's pixels are technically in scope for each call — but `SelectScreenAnchors` only ever compares the current screen against that raw stream, filtered through `IsScreenVariant`'s 90 % cell-agreement test (`kAnchorVariantAgree`); a frame classified a *variant* is excluded from discrimination, only *rivals* make `GreedyAnchors` narrow the pick.

Two pending screens that are ≥ 90 % cell-identical to each other — successive frames of a typewriter credits screen, near-duplicate title cards, a level-select cursor one position over — file each other as variants, not rivals; neither call learns the *other* is a screen someone chose to capture and is about to receive its own `<background>` entry. Both searches can converge on the exact same `(row, col, tile, palette)` triple, independently and correctly by their own local logic. `HdNesPack::GetLayerIndex()` then walks `BackgroundsByPriority[priority]` in insertion order and returns the first entry whose conditions all evaluate true; two backgrounds with an identical triple are indistinguishable at read time, so the second is permanently unreachable. It ships, lints clean, and builds clean — nothing downstream ever sees the collision.

### Measured (issue #349, 2026-09-19 sweep, 28 packs, 241 captures)

104 of 241 captures share a byte-identical condition triple (name stripped) with an earlier capture in the same pack. The co-gated PNGs are not redundant — verified pixel-distinct by sha256 (Donkey Kong: 46 captures on one gate, all distinct; Ice Climber 22; Pac-Man 13 + 9; Bomberman 6 + 4; Punch-Out!! 5; Tennis 4; Mario Bros., Double Dragon, The Flintstones 2 each). This is silent data loss — 45 of Donkey Kong's 46 recorded backgrounds exist on disk and none can ever be displayed. A greedy minimum-separating-set search over the full 960-cell frame shows the probe *count* is not the blocker: 1 probe already separates 117 of 232 captures in multi-capture packs, 2 more; `kAnchorCount` = 3 is enough for the overwhelming majority.

### Relationship to ADR-0217

ADR-0217 measures the same recorder from the read side: given two captures' gates already match, which wins (`GetLayerIndex`'s first-match rule is arbitrary and silent today). Its Option C — "every other capture is a rival, never a variant" — comes closest to a write-time fix, but it reasons per-call about raw-frame similarity, so by its own numbers it still leaves 9 frame pairs that are byte-identical at every cell, and it interacts with `kAnchorMinSpread`, which alone makes 53 captures inseparable at any probe count. This ADR is independent of whichever precedence rule ADR-0217 settles on: **no read-time resolution rule can recover a frame whose gate is bit-for-bit identical to another already-committed gate** — once two backgrounds carry the same triple, one is gone regardless of which one `GetLayerIndex` prefers. The question here sits earlier: should `FinalizeScreenAnchors` check a new pending screen's chosen triple against triples *already assigned* to other pending screens processed earlier in the same loop — independent of, and in addition to, whatever policy a single `SelectScreenAnchors` call uses internally?

### A relevant timing fact

`CaptureScreen()` writes `backgrounds/screenNNN.png` at capture time; `FinalizeScreenAnchors()` only assigns `HdPackTileAtPositionCondition`s and appends to `_hdData.BackgroundsByPriority` — it does not write `hires.txt`. `WriteAll()`/the enclosing save function calls `FinalizeScreenAnchors()` before it builds sheets and before the manifest is serialized, so every capture's PNG already exists on disk, but **no condition has been written to any file yet** — `_hdData.Conditions` and `BackgroundsByPriority` are still plain in-memory vectors. Revising an earlier pending screen's anchor choice inside this loop touches nothing flushed to disk.

### Non-goals

- `GetLayerIndex`'s precedence rule, or which capture wins once two gates collide — ADR-0217's question.
- `IsScreenVariant`'s threshold or the raw-frame variant/rival split *inside* a single `SelectScreenAnchors` call for cells a variant may legitimately change (ADR-0159's territory).
- Relaxing or measuring `kAnchorMinSpread` — a shared dependency with ADR-0217, not re-litigated here.
- Retro-fitting packs already on disk. Every option here is a recorder change and takes effect only on the next recording.

## Decision

**A and B ship together.** C is kept below as the record of what was weighed, not adopted: its fixed-point re-search recovers more but the implementation cost (a mutable, growing forced-rival set plus a convergence pass over `GreedyAnchors`'s call sites) is not justified before A and B are shipped and measured — B's own drop-and-log count is the number that says whether chasing C's extra recovery is worth it.

### A — Forced extra rivals from previously-decided captures

Process `_pendingScreens` in capture order (already the vector's natural order). Before calling `SelectScreenAnchors` for screen *N*, collect the `GridFrame*` at `pending.GridFrameIndex` for every screen 1..N-1 already finalized and pass them into `GreedyAnchors` as forced rivals — bypassing `IsScreenVariant` entirely for this set, since a screen someone chose to capture separately is by definition worth telling apart from.

- **Cost:** one triple-compare per already-decided screen per candidate, per pick — bounded by `MaxScreensPerPack`² × `kAnchorCount` = 300² × 3 = 270 000 compares worst case, at finalize time, off the hot path.
- **Memory:** none beyond what `_pendingScreens` and `_gridFrames` already hold — `GridFrameIndex` is already recorded per pending screen.
- **Order-dependent:** screen 1 never discriminates against screen 300; screen 300 discriminates against all 299 before it. A later capture is more constrained than an earlier one; whether that matches which screens most need it (a typewriter's *last* frame vs. its first) is unmeasured.
- **Does not rewrite earlier picks:** if screen 1 and screen 50 end up with the same triple anyway (screen 1 didn't know screen 50 was coming), the collision is caught for every *later* pair but not for pairs where the earlier screen was decided before the later one existed.

### B — Post-hoc exact-collision scan, drop the later duplicate

Run `FinalizeScreenAnchors` exactly as today — each pending screen searches only against `_gridFrames`. After the whole loop, before `BuildSheets()`, scan `_hdData.BackgroundsByPriority` for any two entries at the same priority whose condition triples are identical once each condition's name is stripped. For every collision, drop the later entry: remove it from `BackgroundsByPriority` and log which pending screen's PNG now has no `<background>` line (mirrors what the pack already does when `pending.Candidates` comes back empty). B's "drop" gets a build-time warning (mirrors the existing empty-`pending.Candidates` warning) so an artist notices a capture that never made it in.

- **Cost:** O(pendingScreens² × kAnchorCount) triple-compares, same order as A, but a separate, simpler pass with no change to `GreedyAnchors`.
- **What it does not do:** like ADR-0217's Option A, it does not recover the dropped capture's frame — it converts a silent wrong-render into a visible, logged loss (PNG stays on disk, no `<background>` cites it). No frame that draws today changes; 104 of 241 captures currently never draw regardless, this just stops shipping the dead `<condition>` lines.
- **Simplest to implement and to test:** a pure post-processing step over already-finalized data, no interaction with `GreedyAnchors`'s internals.

### C — Retroactively re-run the earlier capture too (weighed, not adopted)

Same scan as B, but re-invoke `SelectScreenAnchors` for *both* colliding screens with each other's `GridFrame` added to the forced-rival set (as in A) and replace both entries' conditions. Since no condition has been serialized yet, revising an earlier pending screen's `HdBackgroundInfo::Conditions` in place is safe. Cost: worst case O(pendingScreens² × frames) if collisions cluster, since each re-search is a full `GreedyAnchors` pass again — still bounded (300 screens, ≤ 4096 frames), still finalize-time only, but the most expensive of the three. It buys order-independence, but a re-search can still fail to separate two screens (the 9 byte-identical-frame cases) and it needs a fallback matching B's drop-and-log. It touches `MesenSheets::GreedyAnchors`'s signature to accept a mutable, growing forced-rival set and needs a second convergence pass, so it is not simply "call the function again" — it is closer to a small fixed-point loop over `_pendingScreens`.

## Record

Answers to *What a human has to pick*, decided 2026-09-20: (1) ships coordinated with ADR-0217, not independently and not waiting on it; (2) A and B both ship, A the avoidance pass, B the fallback safety net for whatever A (and ADR-0217's C) still cannot separate; (3) B's drop gets a build-time warning; (4) capture order, as Option A specifies — the rarity ordering is unmeasured extra complexity, deferred; (5) A and B ship with synthetic-`GridFrame` unit coverage now, no re-record required.

## Consequences

- This ADR stops two *different* captures from being minted with the same gate; ADR-0217 decides what happens when a gate still ends up shared (by policy choice, or by one of the 9 byte-identical-frame cases neither ADR can fix).
- A and C add a `_pendingScreens`-order or fixed-point dependency to `FinalizeScreenAnchors` that today has none — each pending screen is currently order-independent of every other; A and B are pure finalize-time logic over `_pendingScreens`/`_hdData`. That order dependence is worth flagging in the function's own comments once one of these lands, since the next reader will otherwise assume (correctly, today) that reordering `_pendingScreens` cannot change the output.
- B and C both need `scripts/spike_anchor_stability.py` (or a sibling) extended with a same-session cross-capture collision rate, the way ADR-0217 asks for a "captures whose gate another capture satisfies" figure — ADR-0217's is measured against the finalized, on-disk pack; this ADR's would be measured *during* `FinalizeScreenAnchors`, before any dropping or re-search.
- B's "drop" belongs in the pack (the `<background>` line is removed) and is surfaced by the build-time warning — the `mep_lint`-adjacent slot an empty `pending.Candidates` already occupies.
- Nothing here helps a pack already on disk; every option is a recorder change.
