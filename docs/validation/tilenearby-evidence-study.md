# `tileNearby`: is the co-occurrence evidence good enough to auto-attach, and what does a wrong one cost?

Measured 2026-09-14. Decision taken from these numbers: **ADR-0190** (yes,
auto-attach, gated on both-ways frame support). This document is the evidence,
not the decision — it is here so the constants in `HdPackBuilder.h`
(`kTileNearbyMinFrames`, `kTileNearbyMinProbability`) can be argued with.

## The question

`BuildObjectSheets` has computed `tileNearby` candidates since F5.4e and emitted
them only as `# inferred` comments, guarded by:

> Never auto-attached - a wrong inference must not make a tile fail to render.

ADR-0189 attached `spriteNearby` and deferred `tileNearby` on the strength of
that comment. Two things had to be measured before the deferral could be
revisited: how good the evidence actually is, and what a wrong condition
actually costs now that ADR-0189 §3 guarantees a bare twin.

## Method

Five ROMs from the local library, one recording each, 120 emulated seconds
(7212 accumulated frames) through `scripts/headless_record ... bootstrap log
input=<entry script>` with the ROM staged into a scratch folder so the bootstrap
fires. Contra used `runs/golden-20260912/contra/entry.txt`; the others used the
same script, which is not tuned for them — Excitebike in particular barely
leaves its intro, which is why its numbers are small. **This is a recording-
quality caveat, not a defect in the statistic.**

`MESEN_TILENEARBY_EVIDENCE=<path>` makes the builder dump the entire
co-occurrence table as CSV *before* any threshold is applied
(`HdPackBuilder::DumpCoOccurrenceEvidence`), one row per directed edge:
`a,b,dir,count,frames,framesA,framesB,aIsObject,bIsObject`. It writes nothing
when the variable is unset and never changes a pack, so the study is repeatable
on a new game without a rebuild.

Two instrumentation additions were needed and were kept, because the old table
could not answer the question:

- **`frames`** — the number of accumulated frames the adjacency held in,
  incremented at most once per frame. The pre-existing `count` is a *cell*
  tally: one frame of a static screen raises it by up to 30.
- **`framesA` / `framesB`** — the frames each shape was on screen in at all, so
  support reads as a conditional probability instead of a raw tally.

## Finding 1 — the old `>= 3` gate is a formality

`ECount + SCount >= 3` was read as `spriteNearby`'s "≥ 3 frames". It is not.
Candidates surviving the "both shapes inside an inferred object" restriction,
and how many of those the old gate then removed:

| game | raw directed edges | both shapes in an object | survive `count >= 3` |
|---|---|---|---|
| Castlevania | 1790 | 770 | 770 |
| Contra | 1178 | 466 | 466 |
| Mega Man 2 | 1787 | 342 | 323 |
| The Legend of Zelda | 916 | 220 | 220 |
| Excitebike | 1002 | 21 | 21 |

It rejects 19 candidates across five games, all on one of them. Object
membership is the filter that was doing the work; the "≥ 3" was decorative.

## Finding 2 — the key threw the direction away

This is a defect, not a measurement. The table was keyed
`{min(a, b), max(a, b)}` with an `ECount`/`SCount` pair inside. That key cannot
express *which* of the two shapes was the left/top one, but the condition it
feeds must say "the target sits 8 px east **of me**" — so the sign came from
hash ordering, and about half of the advisory comments emitted since F5.4e
pointed the wrong way. The same key also merged "B is east of A" with "B is
south of A" into one bucket and resolved them by majority vote.

ADR-0190 makes the key `{A, B, South}` with `A` always the left/top cell. Every
number in this document is measured on the directed key.

## Finding 3 — the support is bimodal, and that is the usable threshold

Distribution of `min(frames/framesA, frames/framesB)` — the adjacency's
conditional probability read in both directions — over the in-object candidates,
in buckets of 0.05 from 0.00 to 1.00:

| game | 0.00 | .05 | .10 | .15 | .20 | .25 | .30 | .35 | .40 | .45 | .50 | .55 | .60 | .65 | .70 | .75 | .80 | .85 | .90 | .95 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Castlevania | 17 | 107 | 21 | 65 | 56 | 21 | 32 | 13 | 22 | 14 | 10 | 7 | 12 | 33 | 35 | 16 | 18 | 11 | 66 | **194** |
| Contra | 70 | 23 | 18 | 36 | 5 | 5 | 19 | 9 | 32 | 0 | 8 | 10 | 7 | 0 | 0 | 28 | 5 | 3 | 3 | **185** |
| Mega Man 2 | 74 | 18 | 4 | 12 | 12 | 33 | 8 | 7 | 0 | 2 | 14 | 7 | 10 | 3 | 10 | 0 | 4 | 1 | 2 | **121** |
| Zelda | 48 | 43 | 17 | 0 | 14 | 1 | 0 | 6 | 0 | 0 | 0 | 0 | 4 | 0 | 0 | 0 | 0 | 13 | 35 | **39** |
| Excitebike | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 5 | 0 | **16** |

On four of five games the top bucket dominates and the drop to the next is an
order of magnitude (Contra 185 → 3). **Zelda is the weak case** — 39 against 35
— which is why the threshold is set at 0.80 and not at the gap; setting it at
the gap would be tuning to four samples.

Reading the probability *both* ways is the part that matters. One way round, a
rare shape that only ever appears beside a very common one scores a perfect
`frames/framesRare`; it is `frames/framesCommon` that exposes it. That is the
sky/floor case, and a one-way gate would admit all of it.

## Finding 4 — frame support is not the discriminator

Frames the emitted set held over, out of 7212:

| game | min | p25 | median | p75 | max |
|---|---|---|---|---|---|
| Castlevania | 238 | 1645 | 4676 | 6764 | 7128 |
| Contra | 709 | 5436 | 5645 | 5658 | 5666 |
| Mega Man 2 | 572 | 2576 | 3098 | 3107 | 4321 |
| Zelda | 321 | 1580 | 6202 | 6777 | 7098 |
| Excitebike | 6176 | 6176 | 6176 | 6176 | 6176 |

Every emitted edge is supported by hundreds to thousands of frames. Raising the
frame floor from 3 to 60 changes the emitted count on Contra by **zero**: the
probability gate already subsumes it. The floor is kept as a cheap guard against
a pair seen on one transition screen, not because it is doing work today.

## Finding 5 — a shape next to a copy of itself scores perfectly and means nothing

A run of one floor tile, or a field of sky, is adjacent to itself in every frame
it appears in: 100% both ways. It passes every gate above and carries no
positional information at all. Excluding `A == B` removes 10 (Contra), 47
(Castlevania), 11 (Mega Man 2), 10 (Zelda), 5 (Excitebike) — 5–16% of the
survivors. ADR-0190 §2 excludes them.

## Finding 6 — a wrong `tileNearby` is free, demonstrated

This is the crux, and it is answered from the code and then confirmed on a real
pack.

**From the code.** `HdNesPack::GetMatchingTile` walks `TileByKey[key]` in file
order and returns the first entry whose `MatchesCondition` passes. ADR-0189 §3
requires the conditioned line to be followed by a byte-identical bare twin, so a
failing condition costs one predicate evaluation and then renders the bare line.
Verified on the emitted Contra pack: 2350 runs of consecutive same-cell `<tile>`
lines, 342 of them containing a conditioned line, **0 violating "conditioned
lines first, bare twin last"** and **0 where the conditioned line and its twin
are not byte-identical**.

**On a real pack.** The recorded Contra pack (387 conditions, 517 conditioned
lines) installed as a loose HD pack and played for 3606 frames, against the same
pack with all 186 `tileNearby` targets rewritten to a pattern the game never
draws — every condition fails:

| variant | frame checksum | background tile match rate | loader |
|---|---|---|---|
| as written | `0x80267D82` | 100% | 0 errors |
| every `tileNearby` target broken | `0x80267D82` | 100% | 0 errors |
| two definitions renamed (negative control) | `0x80267D82` | 100% | `Condition not found: obj_nearby0` / `obj_nearby1`, `Loaded with 2 errors` |

Identical frames. The failure mode is "no improvement", never a hole. The
negative control is what makes the "0 errors" in the first two rows mean
something.

## Finding 7 — and it does not cost the tile cache either

`HdPackLoader` sets `ForceDisableCache` for `spriteNearby` unconditionally, but
for `tileNearby` only when `TileX % 8 > 0 || TileY % 8 > 0`. Every offset
emitted is `(8,0)` or `(0,8)`. A/B on Contra, 7212 frames × 3 reps, same pack
with and without the 318 `tileNearby`-gated lines:

| variant | wall clock |
|---|---|
| with the `tileNearby` lines | 33.3 / 30.7 / 32.4 s |
| without them | 31.2 / 29.9 / 35.6 s |

No measurable difference; the run-to-run spread is larger than any effect. For
contrast, removing **all** 517 conditioned lines (i.e. also the `spriteNearby`
ones, which do force the cache off) gives 30.2 / 29.9 / 29.2 s — so the cost
ADR-0189 measured is real and it is not this one.

## Conclusion

Auto-attach, with `A != B`, `frames >= 3` and `min(frames/framesA,
frames/framesB) >= 0.80`. Emitted volume: 242 / 186 / 117 / 77 / 16 on the five
games — the same order as ADR-0189's `spriteNearby` spanning tree and as the 34
a human hand-wrote into Contra80s.

## What this does not establish

- **That the conditions are artistically useful.** Everything above shows they
  are well-supported, correctly oriented and free. Whether an artist wants to
  repaint a tile differently when its neighbour is present is a question for an
  artist, and no measurement here answers it.
- **That the thresholds generalise past five NTSC NES games**, four of which ran
  a Contra-tuned entry script. Re-run the dump before trusting them on a new
  console.
- **Anything about GB/GBC/SMS.** The co-occurrence table is fed by the NES
  background grid only.
- **A visual check by a human.** Every comparison above is a checksum or a
  count; nobody looked at a screenshot of a conditioned pack next to an
  unconditioned one.
